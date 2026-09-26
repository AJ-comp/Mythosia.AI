# Explore the Playground

Use the Playground to compare model options and configure document retrieval before writing application code. This walkthrough shows the current interface in a local workspace, from finding a model to exploring the RAG pipeline settings.

<video controls playsinline preload="metadata" poster="assets/playground-demo.png" aria-label="Mythosia.AI Playground interface walkthrough" style="display: block; width: 100%; height: auto; border-radius: 12px;">
  <source src="assets/playground-demo.mp4" type="video/mp4">
  <track kind="captions" src="assets/playground-demo.vtt" srclang="en" label="English">
  Your browser does not support embedded video. <a href="assets/playground-demo.mp4">Download the walkthrough</a>.
</video>

[Download the video (MP4)](assets/playground-demo.mp4) · [Read the captions](assets/playground-demo.vtt)

The recording has no narration. English step labels appear below the application, with a separate caption track available in the player.

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
