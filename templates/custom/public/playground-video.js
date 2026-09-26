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
    video.insertAdjacentElement('afterend', caption);
    video.dataset.inlineCaptionsReady = 'true';

    var attachedTracks = new Set();
    var webkitFullscreen = false;

    function usesNativePlayer() {
      var fullscreen = document.fullscreenElement || document.webkitFullscreenElement;
      return !!((fullscreen && (fullscreen === video || fullscreen.contains(video)))
        || webkitFullscreen || video.webkitDisplayingFullscreen
        || video.webkitPresentationMode === 'fullscreen'
        || video.webkitPresentationMode === 'picture-in-picture'
        || document.pictureInPictureElement === video);
    }

    function currentText(track) {
      var time = video.currentTime;
      var active = track.activeCues;
      var texts = [];

      function collect(cues) {
        for (var i = 0; cues && i < cues.length; i++) {
          var cue = cues[i];
          // A seek can update the clock before activeCues receives its next event.
          if (cue.startTime <= time && time < cue.endTime && typeof cue.text === 'string') {
            texts.push(cue.text.replace(/\s+/g, ' ').trim());
          }
        }
      }

      collect(active);
      if (!texts.length) collect(track.cues);
      return texts.join(' ');
    }

    function render() {
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
      var text = inline ? currentText(selected) : '';
      if (caption.textContent !== text) caption.textContent = text;
      caption.hidden = !inline;
      if (inline) caption.lang = selected.language;
      video.classList.toggle('playground-video--inline-captions', inline);
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
    ['loadedmetadata', 'timeupdate', 'seeking', 'seeked', 'emptied',
      'enterpictureinpicture', 'leavepictureinpicture', 'webkitpresentationmodechanged']
      .forEach(function (event) { video.addEventListener(event, render); });
    document.addEventListener('fullscreenchange', render);
    document.addEventListener('webkitfullscreenchange', render);
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
