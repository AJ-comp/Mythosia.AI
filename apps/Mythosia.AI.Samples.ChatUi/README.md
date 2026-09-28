# Mythosia.AI Playground

Try a model or document pipeline before integrating it into an application. The playground keeps model selection, conversation and session inspection in one local workspace, with separate document and pipeline panels.

## Start locally

From the repository root, with the SDK specified in `global.json`:

```sh
dotnet run --project apps/Mythosia.AI.Samples.ChatUi
```

Open the address printed by the application. To choose a loopback-only HTTP address without a development certificate:

```sh
dotnet run --project apps/Mythosia.AI.Samples.ChatUi --no-launch-profile --urls http://127.0.0.1:50915
```

The app references the current workspace projects. Its header reads loaded package versions from `/api/testbed`; it does not advertise a hardcoded release number.

## Explore a model

All seven providers appear as collapsed groups so a large OpenAI catalogue does not push the other providers out of sight. Expand any group, or search across all providers. **Clear search** removes the filter and restores the group expansion state from before the search. A key is required to connect, not to browse.

1. Search the catalogue by model or provider, or press **Ctrl/Cmd + K**. You can browse models before adding a key.
2. Add the provider key, then select a model. Qwen custom endpoints use their existing connection settings.
3. Adjust request settings and send a message. **Enter** sends; **Shift + Enter** adds a line. IME composition does not submit a message.
4. Use **Stop** to cancel the active HTTP request and stop waiting. Already received text remains visible. This does not guarantee that the provider immediately stops processing or charging for its work.

**Response speed** defaults to the provider's settings. Standard and Fast are offered only when the connected model and endpoint report support. Fast can carry premium pricing; the session inspector shows the processing information actually returned by the provider, when available. Capability details describe library support; they do not turn on web search, steering or other features automatically.

**View Code** exports request-builder examples for the selected provider and request settings. Replace the API-key placeholder in your own environment. Actual keys are not exported.

## Ground an answer in documents

Open **Documents** to register and index files. Open **Pipeline** to configure query rewriting, embeddings, retrieval and reranking. Embedding or model calls can incur provider charges. Changes that affect document vectors require indexing again; the panel explains the active configuration.

In **Pipeline → Embedding**, select Voyage Context 4, Gemini Embedding 2 or Perplexity contextual embeddings alongside the existing providers. Save the matching provider key, then choose the model and vector dimensions:

Gemini embeddings share the saved Google key with chat; replacing it updates both. Voyage has its own key entry in the embedding panel.

| Provider | Model | Default dimensions | Allowed dimensions |
| --- | --- | --- | --- |
| Voyage | `voyage-context-4` | 1024 | 256, 512, 1024, 2048 |
| Gemini | `gemini-embedding-2` | 1536 | 128–3072 |
| Perplexity contextual | `pplx-embed-context-v1-0.6b` / `pplx-embed-context-v1-4b` | 1024 / 2560 | 128–1024 / 128–2560 |

The Playground embedding timeout defaults to **120 seconds**, adjustable from **1 to 600 seconds**. Gemini concurrency defaults to **4 requests**, adjustable from **1 to 16**. These are sample-app settings; the library APIs retain their own defaults. Saved settings are restored in this browser and included when reconnecting a vector database. Timeout, concurrency and API-key changes apply to subsequent operations without reindexing. Database dimensions and existing vectors must match the selected embedding model and dimensions. A reconnect does not convert an existing index.

After changing the provider, model, dimensions or document/query formatting, rebuild the document index with the settings used for queries, even when dimensions match. The app rejects a change of provider, model or dimensions for the currently connected external table, collection or namespace. Choose a new storage target and index all required documents with the new configuration.

Open **Documents**, add files and run **Run Reference**; use **Cancel indexing** to stop an active indexing request. Cancellation is cooperative and does not undo documents already stored or guarantee that a remote provider stops billing. Check the index before retrying; atomic replacement of an individual document follows the selected backend's contract. Inspect the per-document chunk and vector counts, then use **View Code** for an example of the selected configuration. Code examples contain key placeholders rather than saved credentials. See the [embedding guide](https://github.com/AJ-comp/Mythosia.AI/blob/main/docs/rag-embedding.md#playground-embeddings) for provider behavior and reindexing requirements.

The pipeline retains the latest library fixes for query rewriting and PostgreSQL search settings. A skipped search or an empty retrieval result remains visible in the response diagnostics rather than disappearing.

On narrow screens, **Models** and **Inspector** open the same panels as drawers. Dialogs support Escape, keyboard focus containment and focus return. Reduced-motion preferences are respected.

## Local development scope

This is a single-workspace test application. Service and document state are shared within its process, and provider keys are stored in browser local storage as in the existing sample. Use a trusted local browser and loopback binding; this sample is not a multi-user hosted service.

Model Markdown is sanitized with the locally bundled DOMPurify before insertion. If Markdown or sanitization libraries are unavailable, output falls back to escaped text. Third-party notices are in `wwwroot/lib`.

## Choose the interface language

Use the language selector in the header to read controls and guidance in English, Korean, Japanese, Simplified or Traditional Chinese, German, Spanish, French, Portuguese, Russian, Ukrainian, Vietnamese or Thai. The first visit follows the browser's supported language preference; a manual choice is remembered in this browser. Unsupported languages fall back to English, and a failed language-file load keeps the current interface usable.

On the first visit, the app checks the browser's preferred languages in order and chooses the first supported one. Regional preferences map to the available language, such as `ko-KR` to Korean, `pt-BR` to Portuguese and `zh-TW` to Traditional Chinese. If no supported preference is available, the interface opens in English. Automatic detection does not save an override; only choosing a language yourself takes precedence on later visits.

Language changes happen in place: the current conversation, unsent input, selected model and request settings remain intact. Model/provider names, API keys, code, document content and model responses retain their original text. Raw provider errors and diagnostic payloads also retain their original details. Changing the interface language does not request translated answers from the model.

UI resources live in `wwwroot/js/locales/`. Each language has the same English-source keys and named placeholders. Add new UI phrases to every catalogue and run `build/test-i18n-ui.mjs`; this checks complete keys, placeholder preservation, dynamic labels, language-load races and separation from user data. Dynamic UI labels must be included in the explicit localization scopes in `i18n.js`; do not mark model output or arbitrary diagnostic data as translatable.

## Validation

The shared CI and local release checks run UI regression scripts in `build/`, including console navigation, cancellation, stream ordering and sanitized rendering. With Node 24, install their pinned test-only DOM dependencies using `npm ci --prefix build/ui-tests --ignore-scripts`. Run a script with `node --experimental-vm-modules build/test-console-ui.mjs` (or the corresponding UI test filename). Provider and code-export contracts are covered by `ChatUiTestbedTests` in the core test project. These deterministic checks use fake provider responses and do not call paid APIs.

For UI changes, also open the running app in a browser and check desktop and narrow-screen layouts, model search, keyboard navigation, provider settings, document and pipeline dialogs, and error/empty states.
