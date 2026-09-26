# Explore the Playground

Use the Playground to compare model options and configure document retrieval before writing application code. This walkthrough shows the current interface in a local workspace, from finding a model to exploring the RAG pipeline settings.

<video class="playground-video" controls playsinline preload="metadata" poster="assets/playground-demo.png" aria-label="Mythosia.AI Playground interface walkthrough">
  <source src="assets/playground-demo.mp4?v=6" type="video/mp4">
  <track kind="subtitles" src="assets/playground-demo.vtt?v=6" srclang="en" label="English" default>
  <track kind="subtitles" src="assets/playground-demo.ko.vtt?v=6" srclang="ko" label="한국어">
  <track kind="subtitles" src="assets/playground-demo.ja.vtt?v=6" srclang="ja" label="日本語">
  <track kind="subtitles" src="assets/playground-demo.zh-Hans.vtt?v=6" srclang="zh-Hans" label="简体中文">
  <track kind="subtitles" src="assets/playground-demo.zh-Hant.vtt?v=6" srclang="zh-Hant" label="繁體中文">
  <track kind="subtitles" src="assets/playground-demo.de.vtt?v=6" srclang="de" label="Deutsch">
  <track kind="subtitles" src="assets/playground-demo.fr.vtt?v=6" srclang="fr" label="Français">
  <track kind="subtitles" src="assets/playground-demo.es.vtt?v=6" srclang="es" label="Español">
  <track kind="subtitles" src="assets/playground-demo.pt.vtt?v=6" srclang="pt" label="Português">
  <track kind="subtitles" src="assets/playground-demo.ru.vtt?v=6" srclang="ru" label="Русский">
  <track kind="subtitles" src="assets/playground-demo.uk.vtt?v=6" srclang="uk" label="Українська">
  <track kind="subtitles" src="assets/playground-demo.vi.vtt?v=6" srclang="vi" label="Tiếng Việt">
  <track kind="subtitles" src="assets/playground-demo.th.vtt?v=6" srclang="th" label="ไทย">
  Your browser does not support embedded video. <a href="assets/playground-demo.mp4?v=6">Download the walkthrough</a>.
</video>

[Download the video (MP4)](assets/playground-demo.mp4?v=6) · [Read the captions](assets/playground-demo.vtt?v=6)

The recording has no narration. Subtitles explain each action below the application and start in this page's language. Use the player's captions menu to choose another language or turn them off.

## What the walkthrough shows

1. Browse the seven provider groups and search for a model by name or provider.
2. Open the provider-key dialog before connecting a model.
3. Switch between English and Korean; the interface supports 13 languages.
4. Explore document registration and text-splitting options.
5. Inspect embedding providers, vector stores, hybrid retrieval and reranking settings.

This is an interface walkthrough: it does not submit API keys, index documents or generate model responses.

## Try it locally

From the repository root, with the SDK specified in `global.json`:

```sh
dotnet run --project apps/Mythosia.AI.Samples.ChatUi
```

Open the address printed by the application. Browse the models and settings first, then add your provider key when you are ready to make requests.

See the [Playground guide](https://github.com/AJ-comp/Mythosia.AI/blob/main/apps/Mythosia.AI.Samples.ChatUi/README.md) for connection, language and local-development details. For the corresponding library APIs, start with [Getting Started](getting-started.md) or [RAG Basics](rag.md).
