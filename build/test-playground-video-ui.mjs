// Run the actual caption player in jsdom with a deterministic media clock.
// Browser layout, fullscreen presentation, and native caption menus still need
// browser QA; these tests cover synchronization and progressive enhancement.
import assert from 'node:assert/strict';
import fs from 'node:fs';
import { createRequire } from 'node:module';
import path from 'node:path';
import { fileURLToPath } from 'node:url';

const root = path.resolve(path.dirname(fileURLToPath(import.meta.url)), '..');
const require = createRequire(path.join(root, 'build/ui-tests/package.json'));
const { JSDOM } = require('jsdom');
const source = fs.readFileSync(path.join(root, 'templates/custom/public/playground-video.js'), 'utf8');
const cue = (startTime, endTime, text) => ({ startTime, endTime, text });
const firstText = 'Browse available models and select the model you need.';
const secondText = 'Choose a provider and connect with your API key.';

function fixture(options = {}) {
  const dom = new JSDOM('<!doctype html><video class="playground-video"><track default></video>', {
    runScripts: 'outside-only', url: 'https://docs.test/playground-demo.html'
  });
  const { window } = dom;
  const { document } = window;
  const video = document.querySelector('video');
  const fire = (target, name) => target.dispatchEvent(new window.Event(name));
  const tracks = new window.EventTarget();
  const makeTrack = (language, mode, cues) => Object.assign(new window.EventTarget(), {
    language, mode, cues, kind: 'subtitles', activeCues: []
  });
  const ko = makeTrack('ko', 'showing', options.cues || [cue(0, 5, firstText), cue(5, 10, secondText)]);
  const en = makeTrack('en', 'disabled', [cue(0, 5, 'English caption for the first scene.'), cue(5, 10, 'English caption for the second scene.')]);
  Object.assign(tracks, { 0: ko, 1: en, length: 2 });
  Object.defineProperty(video, 'textTracks', { value: options.noTracks ? null : tracks });
  if (options.noCues) ko.cues = null;
  let paused = true;
  let ended = false;
  let hidden = false;
  Object.defineProperties(video, {
    paused: { get: () => paused }, ended: { get: () => ended }
  });
  Object.defineProperties(document, {
    hidden: { get: () => hidden },
    visibilityState: { get: () => hidden ? 'hidden' : 'visible' }
  });
  const motion = Object.assign(new window.EventTarget(), { matches: !!options.reducedMotion });
  // Older browsers support the legacy MediaQueryList listener API.
  motion.addListener = listener => motion.addEventListener('change', listener);
  motion.removeListener = listener => motion.removeEventListener('change', listener);
  window.matchMedia = () => motion;
  if (options.noIntl) window.Intl = undefined;
  if (options.noSegmenter) window.Intl.Segmenter = undefined;
  let nextFrame = 0;
  const frames = new Map();
  window.requestAnimationFrame = callback => {
    const id = ++nextFrame;
    frames.set(id, callback);
    return id;
  };
  window.cancelAnimationFrame = id => frames.delete(id);
  const run = () => {
    window.eval(source);
    fire(document, 'DOMContentLoaded');
  };
  video.currentTime = options.time ?? 0;
  run();
  return {
    dom, window, document, video, tracks, ko, en, motion, makeTrack, fire, run,
    caption: () => document.querySelector('.playground-video-caption'),
    frames: () => frames.size,
    frame(time) {
      video.currentTime = time;
      const callbacks = [...frames.values()];
      frames.clear();
      for (const callback of callbacks) callback(time * 1000);
    },
    play() { paused = false; ended = false; fire(video, 'play'); fire(video, 'playing'); },
    pause() { paused = true; fire(video, 'pause'); },
    end() { ended = true; paused = true; fire(video, 'ended'); },
    seek(time) { video.currentTime = time; fire(video, 'seeking'); fire(video, 'seeked'); },
    visibility(value) { hidden = value; fire(document, 'visibilitychange'); },
    reducedMotion(value) { motion.matches = value; fire(motion, 'change'); },
    close() { window.close(); }
  };
}

// The visual copy is hidden from assistive technology; AT gets one complete
// sentence while individual graphemes are revealed without changing line width.
function visual(f) { return f.caption().querySelector('[aria-hidden="true"]'); }
function completeText(f) {
  return Array.from(f.caption().children).filter(node => node.getAttribute('aria-hidden') !== 'true')
    .map(node => node.textContent).join('');
}
function visibleText(f) {
  const element = visual(f);
  if (!element) return f.caption().textContent;
  function read(node) {
    if (node.nodeType === f.window.Node.TEXT_NODE) return node.textContent;
    if (node.hidden || node.style.visibility === 'hidden') return '';
    return Array.from(node.childNodes).map(read).join('');
  }
  return read(element);
}

let passed = 0;
function test(name, body, options = {}) {
  const f = fixture(options);
  try {
    body(f);
    passed++;
    console.log('PASS: ' + name);
  } finally { f.close(); }
}

test('Typing follows the media clock and leaves most of each cue for reading', f => {
  assert.equal(completeText(f), firstText);
  assert.equal(visual(f).textContent, firstText);
  assert.equal(visibleText(f), firstText, 'initial paused preview keeps the complete caption readable');
  assert.equal(f.frames(), 0, 'paused media must not schedule animation');
  f.play();
  assert.ok(visibleText(f).length < firstText.length);
  assert.equal(f.frames(), 1);
  f.frame(0.35);
  const partial = visibleText(f);
  assert.ok(partial.length > 0 && partial.length < firstText.length);
  assert.ok(firstText.startsWith(partial));
  assert.equal(visual(f).textContent, firstText, 'unrevealed text reserves the final layout');
  assert.equal(completeText(f), firstText, 'assistive text does not reveal letter by letter');
  f.frame(1.5);
  assert.equal(visibleText(f), firstText);
  assert.equal(f.frames(), 0, 'completed cues must not keep a frame loop alive');
  assert.equal(f.ko.mode, 'showing', 'browser keeps ownership of subtitle selection');
});

test('Pause freezes typing; resume does not create duplicate frame loops', f => {
  f.play(); f.frame(0.2);
  const before = visibleText(f);
  f.pause();
  assert.equal(f.frames(), 0);
  f.frame(0.2);
  assert.equal(visibleText(f), before);
  f.play(); f.fire(f.video, 'playing'); f.fire(f.ko, 'cuechange');
  assert.equal(f.frames(), 1);
  f.frame(0.6);
  assert.ok(visibleText(f).length > before.length);
});

test('Seeking selects the destination cue even when activeCues is stale', f => {
  f.play(); f.frame(0.2);
  f.ko.activeCues = [f.ko.cues[0]];
  f.seek(7);
  assert.equal(visibleText(f), secondText);
  assert.equal(completeText(f), secondText);
  assert.equal(f.frames(), 0);
  f.seek(0.15);
  assert.ok(firstText.startsWith(visibleText(f)));
  assert.ok(visibleText(f).length < firstText.length);
  assert.equal(f.frames(), 1);
  f.seek(10);
  assert.equal(visibleText(f), '');
  assert.equal(f.frames(), 0);
});

test('Cue change begins typing the next sentence from its own timestamp', f => {
  f.play(); f.frame(2);
  f.video.currentTime = 5.2;
  f.ko.activeCues = [f.ko.cues[1]];
  f.fire(f.ko, 'cuechange');
  assert.ok(secondText.startsWith(visibleText(f)));
  assert.ok(visibleText(f).length > 0 && visibleText(f).length < secondText.length);
  assert.equal(completeText(f), secondText);
  assert.equal(f.frames(), 1);
});

test('Language change and subtitles off discard the previous animation', f => {
  f.play(); f.frame(0.3);
  f.ko.mode = 'disabled'; f.en.mode = 'showing'; f.fire(f.tracks, 'change');
  assert.equal(f.caption().lang, 'en');
  assert.equal(completeText(f), f.en.cues[0].text);
  assert.ok(f.en.cues[0].text.startsWith(visibleText(f)));
  assert.equal(f.frames(), 1);
  f.en.mode = 'disabled'; f.fire(f.tracks, 'change');
  assert.ok(f.caption().hidden);
  assert.equal(f.frames(), 0);
  assert.equal(f.en.mode, 'disabled');
  assert.ok(!f.video.classList.contains('playground-video--inline-captions'));
});

test('Hidden documents stop work and restore the current media position', f => {
  f.play(); f.frame(0.2); f.visibility(true);
  assert.equal(f.frames(), 0);
  f.video.currentTime = 2;
  f.visibility(false);
  assert.equal(visibleText(f), firstText);
  assert.equal(f.frames(), 0);
});

test('Ended playback cancels animation work', f => {
  f.play(); f.frame(0.1); f.end();
  assert.equal(f.frames(), 0);
});

test('Reduced motion displays the complete sentence without animation', f => {
  f.play();
  assert.equal(visibleText(f), firstText);
  assert.equal(f.frames(), 0);
}, { reducedMotion: true });

test('Changing reduced-motion preference during playback stops typing immediately', f => {
  f.play(); f.frame(0.2); f.reducedMotion(true);
  assert.equal(visibleText(f), firstText);
  assert.equal(f.frames(), 0);
});

for (const [name, options] of [
  ['Missing Intl', { noIntl: true }], ['Missing Intl.Segmenter', { noSegmenter: true }]
]) {
  test(name + ' falls back to complete captions safely', f => {
    f.play();
    assert.equal(visibleText(f), firstText);
    assert.equal(f.frames(), 0);
  }, options);
}

test('Thai combining marks and emoji are revealed as whole graphemes', f => {
  const text = f.ko.cues[0].text;
  const units = [...new Intl.Segmenter('th', { granularity: 'grapheme' }).segment(text)]
    .map(part => part.segment);
  const prefixes = new Set(units.map((_, index) => units.slice(0, index + 1).join('')));
  prefixes.add('');
  let sawPartial = false;
  f.play();
  for (let time = 0; time < 0.8; time += 0.015) {
    f.frame(time);
    const shown = visibleText(f);
    assert.ok(prefixes.has(shown), 'must not split a combining sequence or emoji: ' + shown);
    if (shown && shown !== text) sawPartial = true;
  }
  assert.ok(sawPartial);
  assert.equal(visibleText(f), text);
}, { cues: [cue(0, 4, 'กำลังดู👩🏽‍💻 e\u0301 🇰🇷')] });

test('Short cues finish typing early enough to remain readable', f => {
  f.play(); f.frame(0.15);
  assert.equal(visibleText(f), firstText);
  assert.equal(f.frames(), 0);
}, { cues: [cue(0, 0.5, firstText)] });

test('Fullscreen and picture-in-picture retain native subtitle rendering', f => {
  f.play(); f.frame(0.2);
  Object.defineProperty(f.document, 'fullscreenElement', { value: f.video, configurable: true });
  f.fire(f.document, 'fullscreenchange');
  assert.ok(f.caption().hidden);
  assert.ok(!f.video.classList.contains('playground-video--inline-captions'));
  assert.equal(f.frames(), 0);
  assert.equal(f.ko.mode, 'showing');
  Object.defineProperty(f.document, 'fullscreenElement', { value: null, configurable: true });
  f.fire(f.document, 'fullscreenchange');
  assert.equal(f.caption().hidden, false);
  assert.equal(f.frames(), 1);
  Object.defineProperty(f.document, 'pictureInPictureElement', { value: f.video, configurable: true });
  f.fire(f.video, 'enterpictureinpicture');
  assert.ok(f.caption().hidden);
  assert.equal(f.frames(), 0);
  Object.defineProperty(f.document, 'pictureInPictureElement', { value: null, configurable: true });
  f.fire(f.video, 'leavepictureinpicture');
  assert.equal(f.caption().hidden, false);
});

test('WebKit native presentations also stop inline animation', f => {
  f.play(); f.frame(0.2); f.fire(f.video, 'webkitbeginfullscreen');
  assert.ok(f.caption().hidden);
  assert.equal(f.frames(), 0);
  f.fire(f.video, 'webkitendfullscreen');
  assert.equal(f.caption().hidden, false);
  f.video.webkitPresentationMode = 'picture-in-picture';
  f.fire(f.video, 'webkitpresentationmodechanged');
  assert.ok(f.caption().hidden);
  assert.equal(f.frames(), 0);
});

test('Unloaded captions preserve native fallback and enhance once loaded', f => {
  assert.ok(f.caption().hidden);
  assert.ok(!f.video.classList.contains('playground-video--inline-captions'));
  f.fire(f.video.querySelector('track'), 'error');
  assert.ok(f.caption().hidden);
  f.ko.cues = [cue(0, 5, firstText)];
  f.fire(f.video.querySelector('track'), 'load');
  assert.equal(f.caption().hidden, false);
  assert.equal(completeText(f), firstText);
}, { noCues: true });

test('Missing TextTrack support leaves the native player untouched', f => {
  assert.equal(f.caption(), null);
  assert.equal(f.frames(), 0);
  assert.ok(!f.video.classList.contains('playground-video--inline-captions'));
}, { noTracks: true });

test('Caption text is never interpreted as HTML', f => {
  f.seek(2);
  assert.equal(visibleText(f), '<img src=x onerror="alert(1)"> & text');
  assert.equal(f.caption().querySelector('img'), null);
}, { cues: [cue(0, 5, '<img src=x onerror="alert(1)"> & text')] });

test('Repeated initialization adds neither duplicate captions nor animation loops', f => {
  f.run(); f.play(); f.run();
  assert.equal(f.document.querySelectorAll('.playground-video-caption').length, 1);
  assert.equal(f.frames(), 1);
});

test('Tracks added after initialization update and remove correctly', f => {
  f.ko.mode = 'disabled';
  const track = f.makeTrack('ja', 'showing', [cue(0, 5, '追加された字幕です。')]);
  f.tracks[2] = track; f.tracks.length = 3; f.fire(f.tracks, 'addtrack');
  assert.equal(f.caption().lang, 'ja');
  assert.equal(completeText(f), track.cues[0].text);
  track.cues[0].text = '更新された字幕です。'; f.fire(track, 'cuechange');
  assert.equal(completeText(f), track.cues[0].text);
  f.tracks.length = 2; f.fire(f.tracks, 'removetrack');
  assert.ok(f.caption().hidden);
  assert.equal(f.frames(), 0);
});

console.log(`All ${passed} Playground caption behavior checks passed.`);
