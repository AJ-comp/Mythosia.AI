/* Progressive enhancement: native subtitle selection, compact inline rendering. */
(function () {
  'use strict';

  function enhance(video) {
    var tracks = video.textTracks;
    if (!tracks || !tracks.addEventListener || video.dataset.inlineCaptionsReady) return;

    var caption = document.createElement('div');
    caption.className = 'playground-video-caption';
    caption.hidden = true;
    caption.dir = 'auto';
    // Captions remain readable by assistive technology without announcing every change.
    caption.setAttribute('aria-live', 'off');
    var accessibleText = document.createElement('span');
    accessibleText.className = 'playground-video-caption-accessible';
    var animatedText = document.createElement('span');
    animatedText.className = 'playground-video-caption-text';
    animatedText.setAttribute('aria-hidden', 'true');
    caption.append(accessibleText, animatedText);
    video.insertAdjacentElement('afterend', caption);
    video.dataset.inlineCaptionsReady = 'true';

    var attachedTracks = new Set();
    var webkitFullscreen = false;
    var motionPreference = window.matchMedia && window.matchMedia('(prefers-reduced-motion: reduce)');
    // Grapheme boundaries preserve Korean syllables, Thai marks and combined emoji.
    // Older browsers keep complete captions rather than splitting those characters.
    var segmenter = typeof Intl !== 'undefined' && typeof Intl.Segmenter === 'function'
      ? new Intl.Segmenter(undefined, { granularity: 'grapheme' }) : null;
    var characters = [];
    var lastText = null;
    var visibleCount = 0;
    var frame = 0;

    function usesNativePlayer() {
      var fullscreen = document.fullscreenElement || document.webkitFullscreenElement;
      return !!((fullscreen && (fullscreen === video || fullscreen.contains(video)))
        || webkitFullscreen || video.webkitDisplayingFullscreen
        || video.webkitPresentationMode === 'fullscreen'
        || video.webkitPresentationMode === 'picture-in-picture'
        || document.pictureInPictureElement === video);
    }

    function currentCue(track) {
      var time = video.currentTime;
      var active = track.activeCues;
      var texts = [];
      var start = Infinity;
      var end = Infinity;

      function collect(cues) {
        for (var i = 0; cues && i < cues.length; i++) {
          var cue = cues[i];
          // A seek can update the clock before activeCues receives its next event.
          if (cue.startTime <= time && time < cue.endTime && typeof cue.text === 'string') {
            texts.push(cue.text.replace(/\s+/g, ' ').trim());
            start = Math.min(start, cue.startTime);
            end = Math.min(end, cue.endTime);
          }
        }
      }

      collect(active);
      if (!texts.length) collect(track.cues);
      return { text: texts.join(' '), start: start, end: end };
    }

    function showText(cue) {
      if (lastText !== cue.text) {
        lastText = cue.text;
        accessibleText.textContent = cue.text;
        animatedText.replaceChildren();
        var units = segmenter
          ? Array.from(segmenter.segment(cue.text), function (part) { return part.segment; })
          : [cue.text];
        characters = units.map(function (unit) {
          var character = document.createElement('span');
          character.textContent = unit;
          character.style.visibility = 'hidden';
          animatedText.appendChild(character);
          return character;
        });
        visibleCount = 0;
      }

      // Finish early enough to leave most of the cue available for reading.
      var duration = Math.min(1.4, characters.length * 0.028, (cue.end - cue.start) * 0.3);
      var animate = segmenter && !(motionPreference && motionPreference.matches)
        && !(video.paused && video.currentTime === 0) && duration > 0;
      var count = animate
        ? Math.min(characters.length, Math.max(1, Math.ceil((video.currentTime - cue.start) / duration * characters.length)))
        : characters.length;
      for (var i = Math.min(visibleCount, count); i < Math.max(visibleCount, count); i++) {
        characters[i].style.visibility = i < count ? 'visible' : 'hidden';
      }
      visibleCount = count;
      return animate && count < characters.length;
    }

    function render() {
      if (frame) {
        window.cancelAnimationFrame(frame);
        frame = 0;
      }
      var selected = null;
      for (var i = 0; i < tracks.length; i++) {
        var track = tracks[i];
        if (track.mode === 'showing' && (track.kind === 'subtitles' || track.kind === 'captions')) {
          selected = track;
          break;
        }
      }

      // Until cues load, or in native fullscreen/PiP, retain normal browser captions.
      var inline = !!(selected && selected.cues !== null && !usesNativePlayer());
      var cue = inline ? currentCue(selected) : { text: '', start: 0, end: 0 };
      var typing = showText(cue);
      caption.hidden = !inline;
      if (inline) caption.lang = selected.language;
      video.classList.toggle('playground-video--inline-captions', inline);
      // Use the video clock so pause, seek, playback speed and language changes
      // never leave a separate typewriter timer running behind the current cue.
      if (inline && typing && !video.paused && !video.ended && !video.seeking && !document.hidden) {
        frame = window.requestAnimationFrame(function () {
          frame = 0;
          render();
        });
      }
    }

    function attachTracks() {
      for (var i = 0; i < tracks.length; i++) {
        var track = tracks[i];
        if (!attachedTracks.has(track)) {
          track.addEventListener('cuechange', render);
          attachedTracks.add(track);
        }
      }
      render();
    }

    tracks.addEventListener('change', render);
    tracks.addEventListener('addtrack', attachTracks);
    tracks.addEventListener('removetrack', render);
    video.querySelectorAll('track').forEach(function (track) {
      track.addEventListener('load', render);
      track.addEventListener('error', render);
    });
    ['loadedmetadata', 'timeupdate', 'seeking', 'seeked', 'emptied', 'play', 'playing', 'pause', 'ended', 'ratechange',
      'enterpictureinpicture', 'leavepictureinpicture', 'webkitpresentationmodechanged']
      .forEach(function (event) { video.addEventListener(event, render); });
    document.addEventListener('fullscreenchange', render);
    document.addEventListener('webkitfullscreenchange', render);
    document.addEventListener('visibilitychange', render);
    if (motionPreference && motionPreference.addEventListener) {
      motionPreference.addEventListener('change', render);
    }
    video.addEventListener('webkitbeginfullscreen', function () {
      webkitFullscreen = true;
      render();
    });
    video.addEventListener('webkitendfullscreen', function () {
      webkitFullscreen = false;
      render();
    });
    attachTracks();
  }

  function initialize() {
    document.querySelectorAll('video.playground-video').forEach(enhance);
  }

  if (document.readyState === 'loading') {
    document.addEventListener('DOMContentLoaded', initialize, { once: true });
  } else {
    initialize();
  }
})();
