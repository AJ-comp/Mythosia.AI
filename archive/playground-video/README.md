# Playground video production archive

These are the September 26–27, 2026 recording sources and the final English
README video. They show the interface at that time, not a current feature or
model inventory. The application has continued to change.

| Material | Purpose |
|---|---|
| [spotlight/walkthrough.webm](spotlight/walkthrough.webm) | Main original recording, before zoom encoding; 1440×900, 113.72 seconds, including a removable magenta leader. |
| [spotlight/recording.json](spotlight/recording.json), [timeline.json](spotlight/timeline.json) | Actions, chapter starts, zooms, and calibrated 112.4-second output timeline. |
| [initial/](initial/) | Earlier 55.4-second recording, a complete 62.52-second alternate take, and original chapter/production notes. Only the 55.4-second take has matching chapter metadata. |
| [readme/Mythosia-Playground-English.mp4](readme/Mythosia-Playground-English.mp4) | Final 1440×972, 112.4-second video, with English typing captions on a green bottom strip. |
| [readme/captions.ass](readme/captions.ass), [captions/](captions/) | Historical subtitle source and 13 VTT language tracks for the final timeline. |
| [tools/](tools/) | Portable adaptations of the latest recording and encoding scripts. |
| [manifest.json](manifest.json) | Source paths, SHA-256 hashes, transformations, and omitted duplicate files. |

The historical recording plan specifies a fresh browser context with no saved
credentials, uploaded documents, provider calls, microphone, or desktop capture.
The recorded requests list only local GET endpoints. Representative frames,
including the empty API-key dialog, were inspected during archiving. No browser
profile, credentials, dependency cache, or font binary is included.

## Reuse from a local or cloud checkout

The scripts resolve the repository from their own location and write **only to
`artifacts/playground-video/reuse/`**, which is ignored by Git. They never replace
the archived sources or published documentation. Review new output before
copying it into documentation or uploading it.

Requirements:

- Node.js 20 or later.
- FFmpeg with `libx264`, `libass`, and `zoompan`, plus FFprobe, on `PATH`.
  `FFMPEG_PATH` and `FFPROBE_PATH` may instead specify executable paths.
- An installed caption font. `CAPTION_FONT` defaults to `DejaVu Sans` (for
  example, the Linux `fonts-dejavu-core` package). On Windows, an installed font
  such as `Segoe UI` can be selected. Fonts are not redistributed here.
- Only for a new recording: the repository's pinned .NET SDK to run the
  Playground, and Playwright/Chromium installed as below.

To re-encode the archived spotlight footage and English caption strip, from the
repository root:

```sh
node archive/playground-video/tools/encode-spotlight.cjs
node archive/playground-video/tools/encode-readme.cjs
```

The first script produces `artifacts/playground-video/reuse/spotlight/playground-demo.mp4`.
The second produces `artifacts/playground-video/reuse/readme/Mythosia-Playground-English.mp4`.
Font rendering may differ from the historical output. `PLAYGROUND_VIDEO` and
`PLAYGROUND_CAPTIONS` select alternative video/VTT inputs for the second script.
Inputs must be one 1440×900 video stream and single-line captions on its timeline.

For a new recording, install the pinned recorder dependency and browser:

```sh
npm ci --prefix archive/playground-video/tools
cd archive/playground-video/tools
npx playwright install chromium
cd ../../..
```

On a Linux host missing Chromium system libraries, install them using the
Playwright host setup supported by that environment. Start the local Playground
separately with the repository's SDK:

```sh
dotnet run --project apps/Mythosia.AI.Samples.ChatUi -- --urls http://127.0.0.1:50915
```

Then, in another terminal at the repository root:

```sh
node archive/playground-video/tools/record.cjs
node archive/playground-video/tools/encode-spotlight.cjs --recorded
node archive/playground-video/tools/retime-captions.cjs
```

`PLAYGROUND_URL` can select another local loopback port. The recorder uses a
fresh browser context and blocks off-origin requests and non-GET requests; it
does not enter or save API keys. It is a navigation demonstration, not an API
integration test. UI selectors describe the historical walkthrough and may need
updating as the application changes. Failures stop the recording rather than
fabricating a successful walkthrough.

To caption a newly recorded video, set `PLAYGROUND_CAPTIONS` to
`artifacts/playground-video/reuse/captions/playground-demo.vtt` and run
`encode-readme.cjs`. For example:

```powershell
$env:PLAYGROUND_CAPTIONS = 'artifacts/playground-video/reuse/captions/playground-demo.vtt'
$env:CAPTION_FONT = 'Segoe UI'
node archive/playground-video/tools/encode-readme.cjs
```

```sh
PLAYGROUND_CAPTIONS=artifacts/playground-video/reuse/captions/playground-demo.vtt \
  node archive/playground-video/tools/encode-readme.cjs
```

Each runnable tool supports `--help` without recording or encoding.

## Archive validation

The October 16 archive check verified source hashes, video metadata, JavaScript
syntax and help paths, 13-language retiming with byte-for-byte unchanged text,
and a two-second local caption-encoding smoke test. No new browser recording,
provider API calls, or full-length re-encode was run for this migration. Original
local files were left intact.

Duplicate captures, an incomplete raw recording with no duration metadata,
intermediate screenshots/HTML/CI logs, superseded one-off website patch scripts,
`node_modules`, and the machine's proprietary font file were excluded. The
currently published webpage video and poster remain under `docs/assets/`; they
are not duplicated here.
