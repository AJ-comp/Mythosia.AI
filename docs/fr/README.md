<div align="center">

🌐 [English](../../README.md) · [한국어](../ko/README.md) · [日本語](../ja/README.md) · [Français](README.md) · [Deutsch](../de/README.md) · [Русский](../ru/README.md) · [Українська](../uk/README.md) · [简体中文](../zh-Hans/README.md) · [繁體中文](../zh-Hant/README.md) · [Tiếng Việt](../vi/README.md) · [ภาษาไทย](../th/README.md) · [Português](../pt/README.md) · [Español](../es/README.md)

<br>

[![OPEN SOURCE](https://img.shields.io/badge/OPEN%20SOURCE%20·%20.NET%20·%20NUGET-111827?style=flat-square&labelColor=111827&color=111827)](https://github.com/AJ-comp/Mythosia.AI)

<img width="694" height="181" alt="title_60" src="https://github.com/user-attachments/assets/57fd8c63-5b9b-46f6-be30-274354808c0d" />

### Bibliothèque .NET AI modulaire pour créer des applications intelligentes

**Changez de fournisseur, ajoutez le RAG, chargez des documents — le tout avec une API unifiée.**

<br>

[![NuGet](https://img.shields.io/nuget/v/Mythosia.AI.svg?style=for-the-badge&logo=nuget&label=NuGet&color=512BD4)](https://www.nuget.org/packages/Mythosia.AI)
[![Downloads](https://img.shields.io/nuget/dt/Mythosia.AI.svg?style=for-the-badge&logo=nuget&color=512BD4)](https://www.nuget.org/packages/Mythosia.AI)
[![Docs](https://img.shields.io/badge/Docs-GitHub%20Pages-0ea5e9?style=for-the-badge&logo=readthedocs&logoColor=white)](https://aj-comp.github.io/Mythosia.AI/)
[![.NET](https://img.shields.io/badge/.NET-Standard%202.1-6d28d9?style=for-the-badge&logo=dotnet&logoColor=white)](https://dotnet.microsoft.com)

<br>

**[📖 Démarrage](https://aj-comp.github.io/Mythosia.AI/)** &nbsp;·&nbsp; **[Référence API](https://aj-comp.github.io/Mythosia.AI/api/)** &nbsp;·&nbsp; **[GitHub ↗](https://github.com/AJ-comp/Mythosia.AI)**

<br>

</div>

## Démo / Banc d'essai (Chat UI)

Essayez les modèles et la recherche documentaire dans le Playground avant d’écrire votre code d’intégration.

Cette démonstration enregistrée dans l’interface actuelle du Playground présente les modèles, le changement de langue et les paramètres des documents et du pipeline RAG. La vidéo comporte des sous-titres anglais.

https://github.com/user-attachments/assets/4cf90210-b000-41be-8317-a467e93e7504

### Lancer l'exemple

Exécutez **`Mythosia.AI.Samples.ChatUi`** en local :

```bash
# depuis la racine du dépôt
dotnet run --project apps/Mythosia.AI.Samples.ChatUi
```

<details>
<summary>Commandes et langues du Playground</summary>

Recherchez un modèle par nom ou fournisseur et réglez les paramètres à gauche, discutez au centre et consultez les informations de traitement dans Inspector à droite avant de l’intégrer à votre application. Stop permet de cesser d’attendre la réponse ; les choix explicites de vitesse ne sont activés que pour les modèles et points de connexion compatibles, et Fast peut entraîner un surcoût. Sur petit écran, Models et Inspector s’ouvrent en panneaux coulissants ; consultez le [guide Chat UI](https://github.com/AJ-comp/Mythosia.AI/blob/main/apps/Mythosia.AI.Samples.ChatUi/README.md) pour le démarrage local, les documents et les réglages du pipeline.

Le panneau Pipeline permet de configurer clés, dimensions et délai pour Voyage Context 4, Gemini Embedding 2 et les embeddings contextuels Perplexity. Documents affiche les nombres de fragments et de vecteurs et permet d'annuler l'indexation. Les réglages enregistrés, la reconnexion à la base et les exemples de code utilisent cette configuration. Réindexez après un changement de modèle ou de dimensions.

Le sélecteur de langue en haut de la page permet de choisir parmi 13 langues sans perdre les saisies ni les réglages. Les sept fournisseurs sont visibles sous forme de groupes repliés ; dépliez un groupe ou recherchez un modèle.

</details>

## Pourquoi Mythosia.AI ?

- **Changer de fournisseur d’IA avec une seule API** pour le chat, le streaming, les appels d’outils et les réponses structurées.
- **Construire des réponses à partir de vos documents** avec des chargeurs, des embeddings, la recherche et le reclassement.
- **Garder les paramètres des requêtes indépendants** et piloter le travail en cours avec une API Run commune.
- **Choisir les packages nécessaires**, du cœur de la bibliothèque aux intégrations RAG et aux bases vectorielles optionnelles.

## Quels packages installer ?

```
dotnet add package Mythosia.AI                    # commencez ici (c'est tout ce qu'il vous faut)
dotnet add package Mythosia.AI.Rag                # optionnel : quand vous avez besoin du RAG
dotnet add package Mythosia.VectorDb.Postgres     # optionnel : quand vous avez besoin d'un vector store de production
```

| Étape | Package | Quand |
| :--: | --- | --- |
| **1** | **`Mythosia.AI`** | **Commencez ici** — complétion, streaming, appels de fonctions, sortie structurée (OpenAI / Claude / Gemini / Grok / DeepSeek / Perplexity) |
| **2** | **`Mythosia.AI.Rag`** | Quand vous avez besoin du RAG — découpage de texte, embeddings, recherche hybride, reranking, vector store InMemory et chargeurs de documents (Word / Excel / PowerPoint / PDF) |
| **3** | **`Mythosia.VectorDb.Postgres`** / **`Qdrant`** / **`Pinecone`** | Quand vous avez besoin d'un vector store de production à la place d'InMemory — choisissez-en un |

Préparez des paramètres indépendants avec `CreateRequest(...).WithTemperature(...).GetCompletionAsync()`. Le [guide des requêtes](request-building.md) présente Before/After, Run, profils et limites de la conversation partagée.

Pour une requête sensible au temps d’attente, choisissez la [vitesse de traitement](request-building.md#inference-speed). `WithSpeed` conserve modèle et effort, tandis que `Processing` rapporte le mode réellement appliqué. Fast est payant sur les combinaisons compatibles.

## Démarrage rapide

### Complétion IA de base

```csharp
using Mythosia.AI.Services.OpenAI;

var service = new OpenAIService(apiKey, httpClient);
var response = await service.GetCompletionAsync("Hello!");
```

### Streaming

```csharp
await using var run = await service.StartRunAsync(
    "Tell me a story",
    onText: text => Console.Write(text));
string answer = (await run.Result).Text;
```

### Streaming avec raisonnement

OpenAI, Claude, Gemini, Grok et DeepSeek Flash exposent le raisonnement du fournisseur selon le même schéma de streaming. Activez le raisonnement dans le service ou la requête, puis observez-le avec `StreamOptions.WithReasoning()` :

```csharp
await using var run = await service.StartRunAsync(
    message, options: new StreamOptions().WithReasoning());
await foreach (var content in run.StreamAsync())
{
    if (content.Type == StreamingContentType.Reasoning)
        Console.Write($"[Think] {content.Content}");
    else if (content.Type == StreamingContentType.Text)
        Console.Write(content.Content);
}
```

### Appel de fonctions

```csharp
using Mythosia.AI.Extensions;
using Mythosia.AI.Services.OpenAI;

var service = new OpenAIService(apiKey, httpClient)
    .WithFunction(
        "get_weather",
        "Gets the current weather for a location",
        ("location", "The city and country", required: true),
        (string location) => $"The weather in {location} is sunny, 22C"
    );

var response = await service.GetCompletionAsync("What's the weather in Seoul?");
```

Les appels renvoyés dans une même réponse du modèle s’exécutent séquentiellement par défaut. Si les fonctions enregistrées sont indépendantes, vous pouvez activer une exécution parallèle avec une limite de concurrence :

```csharp
using Mythosia.AI.Models.Functions;

service.DefaultPolicy = new FunctionCallingPolicy
{
    ExecutionMode = FunctionExecutionMode.Parallel,
    MaxConcurrency = 3
};
```

Les résultats d’un lot normal sont renvoyés au modèle dans l’ordre d’origine des appels. Une annulation ignore les appels qui n’ont pas démarré et fournit les résultats d’annulation correspondants. Les outils déjà démarrés reçoivent le jeton lorsque cela est pris en charge ; leur exécution est attendue pour préserver les paires appel/résultat dans l’historique. `FunctionCallingPolicy.TimeoutSeconds` couvre toute la boucle des tours de streaming, y compris les en-têtes de réponse et le corps SSE, sans être réinitialisé entre les tours d’outils. Son expiration déclenche `AIServiceException` ; une annulation par l’appelant reste une `OperationCanceledException` associée à son jeton.

Une consultation lente ne doit pas forcément suspendre toute la réponse. Pendant le chargement de la météo, par exemple, le modèle peut déjà formuler des conseils de voyage généraux qui ne dépendent pas du résultat.

`FunctionDefinition.AllowAsync = true` ou `FunctionBuilder.WithAsync()` permet d’autoriser les appels asynchrones pour GPT-6 Astra / Sol / Luna via Responses. La valeur par défaut est `false` ; les modèles non compatibles attendent le résultat du même gestionnaire. Voir les exemples et la durée de vie des requêtes dans le [guide des appels de fonctions](function-calling.md).

Les modèles non compatibles ne reçoivent pas cette option d’API. Ce mécanisme est distinct des gestionnaires C# `async` et de leur exécution parallèle. Voir les [appels d’outils asynchrones](function-calling.md#async-tool-calling) pour les exemples et la durée de vie des requêtes.

### Génération et modification d’images

Créez des propositions visuelles à partir de texte ou modifiez des images existantes avec une capacité optionnelle commune à OpenAI, Google et xAI. Le modèle d’image est indépendant du modèle de chat sélectionné :

```csharp
using Mythosia.AI.Models.Images;
using Mythosia.AI.Services;
using Mythosia.AI.Services.OpenAI;

IImageGenerationService images = new OpenAIService(apiKey, httpClient);
var generated = await images.GenerateImagesAsync(new ImageGenerationRequest
{
    Prompt = "Un pavillon de verre au lever du soleil",
    Size = ImageSize.Pixels(1024, 1024),
    OutputFormat = ImageOutputFormat.Png
});

await File.WriteAllBytesAsync("pavilion.png", generated.Images[0].Data);
```

Consultez le [guide des fournisseurs](providers.md#image-generation) pour la génération et la modification, ou les [options d’image typées et la migration](providers.md#image-options-migration) pour ce changement majeur d’API. xAI utilise `ImageOutputFormat.Auto` ; choisissez l’extension du fichier d’après `GeneratedImage.MediaType`.

Pour choisir des tailles valides lors de la génération ou de la modification d’images, consultez les [options d’image Google par modèle](providers.md#google-image-options). Flash accepte 512/1K/2K/4K, Flash-Lite actuellement 1K et Pro 1K/2K/4K. Flash/Lite proposent 14 rapports d’aspect et Pro les 10 standards ; tous acceptent `Auto`. Les tailles ou rapports explicitement demandés mais non pris en charge sont rejetés avant l’envoi HTTP. Vérifiez `GetImageCapabilities(model)` avant de proposer les options. Le [tableau des modèles](providers.md#google-image-options) explique aussi les divergences de la documentation Flash-Lite.

### Sortie structurée (basique)

```csharp
// Désérialisez les réponses du LLM directement en POCO C# avec auto-récupération
var result = await service.GetCompletionAsync<WeatherResponse>(
    "What's the weather in Seoul?");
```

### Sortie structurée (liste)

```csharp
// Les types collection fonctionnent directement — pas besoin de DTO wrapper
var items = await service.GetCompletionAsync<List<ItemDto>>(
    "Extract all entities from this document...");
```

### Sortie structurée (streaming)

```csharp
// Streamez les fragments de texte en temps réel + obtenez l'objet désérialisé final
var run = service.BeginStream(prompt).As<MyDto>();

await foreach (var chunk in run.Stream())
    Console.Write(chunk);          // UI en temps réel

MyDto dto = await run.Result;      // parsé et auto-réparé
```

### Politique de résumé de conversation

```csharp
// Résumez automatiquement les anciens messages quand la conversation s'allonge
service.ConversationPolicy = SummaryConversationPolicy.ByMessage(
    triggerCount: 20,
    keepRecentCount: 5
);

// Déclencheur basé sur les tokens
service.ConversationPolicy = SummaryConversationPolicy.ByToken(
    triggerTokens: 3000,
    keepRecentTokens: 1000
);

// Utilisez normalement — la synthèse se fait automatiquement
await service.GetCompletionAsync("Continue our conversation...");

// Pour le streaming, appliquez explicitement la politique avant StreamAsync()
await service.ApplySummaryPolicyIfNeededAsync();
await foreach (var chunk in service.StreamAsync("Continue..."))
    Console.Write(chunk.Content);

// Sauvegardez/restaurez le résumé entre les sessions
string saved = service.ConversationPolicy.CurrentSummary;
policy.LoadSummary(saved);
```

### RAG (Génération Augmentée par Récupération)

Choisissez la recherche lexicale, sémantique ou hybride sans imposer un embedding à chaque requête. `UseKeywordSearch()` ignore l’embedding de la requête ; `UseRetriever(...)` relie un index externe ; `UseHybridSearch(HybridSearchOptions)` transmet les poids et paramètres des candidats explicitement définis. L’ingestion des documents produit toujours des vecteurs. Voir les [modes de recherche et les stockages compatibles](rag-hybrid-search.md).

```bash
dotnet add package Mythosia.AI.Rag
```

```csharp
using Mythosia.AI.Rag;
using Mythosia.AI.Services.Anthropic;

var service = new AnthropicService(apiKey, httpClient)
    .WithRag(rag => rag
        .AddDocument("manual.txt")
        .AddDocument("policy.txt")
    );

var response = await service.GetCompletionAsync("What is the refund policy?");
```

Pour une recherche pilotée par un agent, enregistrez le stockage avec `WithAgenticRag(...)` et démarrez le travail avec `service.WithMaxRounds(10).StartRunAsync(...)`. Attendez `run.Result` ou observez `run.StreamAsync()` sur la même exécution. Les exemples complets figurent dans le [README Mythosia.AI.Rag](../../src/rag/Mythosia.AI.Rag/README.md).

#### Conserver le contexte du document et le rôle de la requête

Un fragment peut dépendre des passages voisins, tandis qu’une question de recherche et un document indexé ont des rôles différents. RAG 8.2.0 ajoute les embeddings contextuels Voyage et Gemini Embedding 2 pour le texte extrait de fichiers TXT, Markdown et PDF.

```csharp
using Mythosia.AI.Rag;

var store = await RagStore.BuildAsync(rag => rag
    .UseVoyageEmbedding(voyageApiKey, httpClient)
    .AddDocument("policy.pdf"));
var result = await store.QueryAsync("What is the refund period?");
```

[Configuration et contrats des fournisseurs](rag-embedding.md#retrieval-aware-embeddings): `UseVoyageEmbedding(...)`, `UseGeminiEmbedding(...)`, `IRetrievalEmbeddingProvider` / `EmbeddingDocument` (RAG 8.2.0, RAG Abstractions 6.4.0).

## Fournisseurs supportés

> Grok 4.7: Nécessite Mythosia.AI 8.1.0 / Abstractions 4.1.0. [le choix du modèle, le raisonnement et la vitesse](providers.md#grok-47)

> GPT-6 Sol/Luna nécessitent Mythosia.AI 8.1.0 et Abstractions 4.1.0 ; voir [le choix du modèle et les prérequis](providers.md#gpt-6-sol-luna).

> Claude Opus 5.5: Nécessite Mythosia.AI 8.1.0 / Abstractions 4.1.0. [configuration et migration](providers.md#claude-opus-55)

| Fournisseur | Package | Modèles |
| --- | --- | --- |
| **OpenAI** | `Mythosia.AI` | GPT-6 Astra / Sol / Luna, GPT-5.6 Sol / Terra / Luna, GPT-5.5 / 5.5 Pro / 5.4 / 5.4 Mini / 5.4 Nano / 5.4 Pro / 5.3 Codex / 5.2 / 5.2 Pro / 5.1, GPT-4.1 / 4.1 Mini, GPT-4o / 4o Mini |
| **Anthropic** | `Mythosia.AI` | Claude Fable 5.1 / 5, Mythos 5.1 / 5 (accès limité), [Opus 5.5](providers.md#claude-opus-55) / 5 / 4.8 / 4.7 / 4.6 / 4.5, Sonnet 5 / 4.6 / 4.5, Haiku 4.5 |
| **Google** | `Mythosia.AI` | Gemini 3.8 Flash, Gemini 3.7 Flash, Gemini 3.6 Flash, Gemini 3.5 Flash/Flash-Lite, Gemini 3.1 Pro Preview/Flash-Lite, Gemini 3 Flash Preview, Gemini 2.5 Pro/Flash/Flash-Lite, Gemini 3.1 Flash Image, Gemini 3.1 Flash-Lite Image, Gemini 3 Pro Image |
| **xAI** | `Mythosia.AI` | Grok 4.7, Grok 4.6, Grok 4.5 (par défaut), Grok 4.3, Grok 4.20 (avec / sans raisonnement), Grok Build |
| **DeepSeek** | `Mythosia.AI` | Flash (V4.1 Flash), V4 Pro |
| **Perplexity** | `Mythosia.AI` | Préréglages Agent API et `perplexity/sonar` |
| **Alibaba / Qwen** | `Mythosia.AI.Providers.Alibaba` | Variantes Qwen Max / Plus / Turbo / Qwen3 / Qwen3.5 |

Utilisez Perplexity quand une réponse doit tenir compte d'informations récentes et fournir des sources vérifiables. `PerplexityService` appelle l'Agent API ; la recherche et les embeddings indépendants permettent de construire la récupération de documents autour du modèle de réponse de votre choix. [Perplexity Agent API, recherche et embeddings](perplexity.md).

Pour relire de longs documents ou effectuer plusieurs tours d’outils, choisissez Gemini 3.7 Flash ou 3.8 Flash via l’adaptateur Google existant. Ils sont disponibles à partir de `Mythosia.AI` 8.0.0 / `Mythosia.AI.Abstractions` 4.0.0 ; le modèle par défaut reste Gemini 3.6 Flash.

Pour passer d’une ébauche rapide à une analyse exigeante, sélectionnez explicitement Grok 4.6 et un effort de `Low` à `XHigh`. Disponible dès `Mythosia.AI` 8.0.0 / `Mythosia.AI.Abstractions` 4.0.0 ; Grok 4.5 reste le modèle par défaut de `XAIService`. Voir la [configuration Grok](providers.md#xai-xaiservice).

Pour créer des ébauches ou combiner des références, utilisez [Grok Imagine Image 2.0](providers.md#grok-imagine-image-20) via `IImageGenerationService`. Gardez `OutputFormat = ImageOutputFormat.Auto` et choisissez l’extension selon `MediaType` ; xAI ne permet pas de choisir le codec. Voir la [migration des options d’image](providers.md#image-options-migration). Le modèle de chat reste inchangé.

Choisissez Flare pour des propositions visuelles rapides, Sunburst pour des retouches précises. La [génération et l’édition GPT Image 2.5](providers.md#gpt-image-25) utilisent l’API existante avec un modèle explicite par requête ; le modèle OpenAI par défaut reste GPT Image 2.

Pour analyser graphiques et captures, appeler vos fonctions ou approfondir une première réponse, utilisez [DeepSeek Flash](providers.md#deepseek-deepseekservice) (`AIModels.DeepSeek.Flash`, V4.1 Flash). Le raisonnement reste désactivé par défaut ; activez-le avec `WithDeepSeekReasoning(...)` ou `WithReasoning(...)` par requête.

Pour le texte uniquement, choisissez `AIModels.DeepSeek.V4Pro` (`deepseek-v4-pro`, V4-Pro-0813). Flash reste le modèle par défaut et accepte les images ; les deux offrent Low/High/Max et le même plafond de sortie. Définissez `UseResponsesApi = true` avant de créer la requête pour utiliser Responses avec les API existantes de complétion, streaming, Run et fonctions locales. La valeur par défaut reste `false` pour conserver Chat Completions dans les applications existantes. Le choix est figé pour la requête et ses tours d’outils. Responses renvoie tout l’historique de conversation et de raisonnement natif sans dépendre d’identifiants de réponses stockés.

Réutilisez une image téléversée dans plusieurs questions à Flash avec `DeepSeekImageFileContent`, via Chat Completions ou Responses ; V4 Pro, réservé au texte, refuse les images. Voir [téléversement, réutilisation et limites des images](providers.md#deepseek-deepseekservice). Nécessite Mythosia.AI 8.1.0 / Abstractions 4.1.0.

> Claude Fable 5 et Claude Mythos 5 exigent une conservation des données pendant 30 jours et ne sont pas éligibles aux accords sans conservation. Leur raisonnement adaptatif reste toujours actif ; si l’appelant demande sa désactivation, Mythosia utilise un effort faible et omet le résumé du raisonnement. Mythos 5 est réservé aux clients approuvés du Project Glasswing.

## Guides et migration

Pour TXT et Markdown, choisissez un [découpeur à règles](text-splitters.md) adapté à la structure. Taille, chevauchement et limites Unicode sont vérifiés ; Markdown conserve titres, code et lignes de tableau. Compter caractères ou mots ne garantit pas le nombre de tokens du modèle. Le sens des conditions de tableau et de l’indentation est conservé ; une répétition excessive de contexte Markdown s’arrête avec une exception explicite.

Pour éviter qu’une indexation apparemment réussie écrase des fragments ou leur associe les mauvais vecteurs, la [validation de l’indexation](rag-pipeline.md#indexing-validation) refuse les ID et les lots d’embeddings invalides avant la persistance. Les découpeurs personnalisés doivent fournir des ID uniques et reprendre les métadonnées du document.

Des [identités de fichier stables](document-loaders.md#file-source-identity), des [vecteurs de question validés](rag-embedding.md#query-embedding-validation) et une [persistance par document avec annulation URL](rag-pipeline.md#custom-persistence) évitent doublons, recherches invalides et fragments périmés.

La préversion optionnelle `Mythosia.AI.Rag.Search.Pixie` permet de comparer la recherche neuronale creuse locale avec la recherche existante. Elle conserve votre fournisseur d’embeddings denses et utilise un index PIXIE en mémoire, sans migrer les stockages persistants ni remplacer le mode par défaut. [Guide PIXIE et comparaison (anglais)](../rag-pixie-search.md).

L’[infrastructure d’évaluation de la recherche](https://github.com/AJ-comp/Mythosia.AI/blob/main/tests/Mythosia.AI.Rag.Evaluation/README.md) prend en charge les jeux de données réutilisables, les adaptateurs de recherche, les rapports persistants et les contrôles de régression. Étendez le même évaluateur à de nouvelles méthodes de recherche et à vos collections de documents.

Isolez les réglages, arrêtez le travail en cours et recevez les réponses avec consommation et sources. Le [guide de migration v8](v8-migration.md) présente six changements d’architecture, des exemples et le périmètre de validation.

> Versions des packages documentées ici: [Mythosia.AI 8.1.0](../../src/core/Mythosia.AI/RELEASE_NOTES.md#v810), [Abstractions 4.1.0](../../src/core/Mythosia.AI.Abstractions/RELEASE_NOTES.md#v410), [Alibaba 3.0.1](../../src/core/Mythosia.AI.Providers.Alibaba/RELEASE_NOTES.md#v301), [RAG 8.2.0](../../src/rag/Mythosia.AI.Rag/RELEASE_NOTES.md#v820), [PostgreSQL 10.8.1](../../src/vectordb/Mythosia.VectorDb.Postgres/RELEASE_NOTES.md#v1081), [MCP 0.1.1-preview](../../src/integrations/Mythosia.AI.Mcp/RELEASE_NOTES.md#v011-preview), [Serving.Abstractions 1.0.0](../../src/serving/Mythosia.AI.Serving.Abstractions/RELEASE_NOTES.md#v100), [Serving.Ollama 1.0.0](../../src/serving/Mythosia.AI.Serving.Ollama/RELEASE_NOTES.md#v100), [Serving.LlamaCpp 1.0.0](../../src/serving/Mythosia.AI.Serving.LlamaCpp/RELEASE_NOTES.md#v100), [Serving.Vllm 1.1.0](../../src/serving/Mythosia.AI.Serving.Vllm/RELEASE_NOTES.md#v110). Consultez le [tableau du correctif précédent](https://github.com/AJ-comp/Mythosia.AI/blob/main/RELEASE_NOTES.md#v811) et la [publication coordonnée précédente](https://github.com/AJ-comp/Mythosia.AI/blob/main/RELEASE_NOTES.md#v810) pour les versions des autres packages de recherche, de documents et de vecteurs.

> [Correctif RAG 8.1.1 / PostgreSQL 10.8.1](https://github.com/AJ-comp/Mythosia.AI/blob/main/RELEASE_NOTES.md#v811) : les wrappers RAG existants prennent en compte les changements du réécrivain à l’exécution, et la recherche hybride mixte PostgreSQL respecte les paramètres vectoriels configurés. Le package principal `Mythosia.AI` reste en 8.1.0.

---

## Architecture

<a href="../assets/architecture.svg">
  <picture>
    <source media="(prefers-color-scheme: dark)" srcset="../assets/architecture-dark.svg">
    <img src="../assets/architecture.svg" alt="Architecture Mythosia.AI : IA centrale, orchestration RAG, chargeurs de documents, magasins vectoriels, contrats communs, intégration MCP et gestion indépendante d’Ollama, llama.cpp et vLLM." width="1600">
  </picture>
</a>

### Détails des dépendances des packages

Les flèches indiquent les références directes. Les paquets partagés figurent dans plusieurs vues ; les clients Serving partagent leurs contrats de gestion et restent indépendants de l’IA centrale.

#### IA principale et extensions

```mermaid
%%{init: {"theme":"base","look":"classic","htmlLabels":false,"themeVariables":{"fontFamily":"Arial, sans-serif","fontSize":"18px","primaryTextColor":"#172c46","lineColor":"#64748b","clusterBkg":"#f8fafc","clusterBorder":"#cbd5e1"},"flowchart":{"htmlLabels":false,"curve":"linear","nodeSpacing":20,"rankSpacing":36,"padding":16,"wrappingWidth":300},"fontFamily":"Arial, sans-serif","fontSize":18}}%%
flowchart LR
    subgraph Extensions["Extensions de fournisseurs et outils"]
        Alibaba["Mythosia.AI.<br/>Providers.Alibaba"]:::extension
        Mcp["Mythosia.AI.Mcp"]:::extension
    end
    AI["Mythosia.AI"]:::core
    AIAbs["Mythosia.AI.<br/>Abstractions"]:::contract
    subgraph Independent["Gestion indépendante du serveur"]
        ServingAbs["Mythosia.AI.Serving.<br/>Abstractions"]:::contract
        OllamaServing["Mythosia.AI.<br/>Serving.Ollama"]:::extension
        LlamaCppServing["Mythosia.AI.<br/>Serving.LlamaCpp"]:::extension
        VllmServing["Mythosia.AI.<br/>Serving.Vllm"]:::extension
        OllamaServing --> ServingAbs
        LlamaCppServing --> ServingAbs
        VllmServing --> ServingAbs
    end
    Alibaba --> AI
    Mcp --> AI
    AI --> AIAbs
    classDef core fill:#eff6ff,stroke:#93b4de,color:#172c46,stroke-width:1.5px
    classDef rag fill:#eef8f5,stroke:#83b4a4,color:#164638,stroke-width:1.5px
    classDef extension fill:#f5f0fc,stroke:#b9a4d4,color:#403054,stroke-width:1.5px
    classDef documents fill:#fff8e9,stroke:#d4b879,color:#61491d,stroke-width:1.5px
    classDef store fill:#edf7fb,stroke:#8dbaca,color:#194758,stroke-width:1.5px
    classDef contract fill:#f8fafc,stroke:#a7b2c2,color:#334155,stroke-width:1.5px
```

#### RAG et chargement de documents

```mermaid
%%{init: {"theme":"base","look":"classic","htmlLabels":false,"themeVariables":{"fontFamily":"Arial, sans-serif","fontSize":"18px","primaryTextColor":"#172c46","lineColor":"#64748b","clusterBkg":"#f8fafc","clusterBorder":"#cbd5e1"},"flowchart":{"htmlLabels":false,"curve":"linear","nodeSpacing":20,"rankSpacing":36,"padding":16,"wrappingWidth":300},"fontFamily":"Arial, sans-serif","fontSize":18}}%%
flowchart LR
    Rag["Mythosia.AI.Rag"]:::rag
    subgraph Contracts["Contrats IA et RAG"]
        AIAbs["Mythosia.AI.<br/>Abstractions"]:::contract
        RagAbs["Mythosia.AI.Rag.<br/>Abstractions"]:::contract
    end
    InMem["Mythosia.VectorDb.<br/>InMemory"]:::store
    subgraph Documents["Chargement de documents"]
        Office["Mythosia.Documents.<br/>Office"]:::documents
        Pdf["Mythosia.Documents.<br/>Pdf"]:::documents
        LoaderAbs["Mythosia.Documents.<br/>Abstractions"]:::contract
        Office --> LoaderAbs
        Pdf --> LoaderAbs
    end
    Rag --> AIAbs
    Rag --> RagAbs
    Rag --> InMem
    Rag --> Office
    Rag --> Pdf
    classDef core fill:#eff6ff,stroke:#93b4de,color:#172c46,stroke-width:1.5px
    classDef rag fill:#eef8f5,stroke:#83b4a4,color:#164638,stroke-width:1.5px
    classDef extension fill:#f5f0fc,stroke:#b9a4d4,color:#403054,stroke-width:1.5px
    classDef documents fill:#fff8e9,stroke:#d4b879,color:#61491d,stroke-width:1.5px
    classDef store fill:#edf7fb,stroke:#8dbaca,color:#194758,stroke-width:1.5px
    classDef contract fill:#f8fafc,stroke:#a7b2c2,color:#334155,stroke-width:1.5px
```

#### Bases vectorielles et recherche

```mermaid
%%{init: {"theme":"base","look":"classic","htmlLabels":false,"themeVariables":{"fontFamily":"Arial, sans-serif","fontSize":"18px","primaryTextColor":"#172c46","lineColor":"#64748b","clusterBkg":"#f8fafc","clusterBorder":"#cbd5e1"},"flowchart":{"htmlLabels":false,"curve":"linear","nodeSpacing":20,"rankSpacing":36,"padding":16,"wrappingWidth":300},"fontFamily":"Arial, sans-serif","fontSize":18}}%%
flowchart LR
    subgraph Stores["Bases vectorielles"]
        InMem["Mythosia.VectorDb.<br/>InMemory"]:::store
        Pg["Mythosia.VectorDb.<br/>Postgres"]:::store
        Qd["Mythosia.VectorDb.<br/>Qdrant"]:::store
        Pine["Mythosia.VectorDb.<br/>Pinecone"]:::store
    end
    subgraph Search["Recherche neuronale optionnelle"]
        Pixie["Mythosia.AI.Rag.<br/>Search.Pixie"]:::rag
    end
    RagAbs["Mythosia.AI.Rag.<br/>Abstractions"]:::contract
    VdbAbs["Mythosia.VectorDb.<br/>Abstractions"]:::contract
    InMem --> RagAbs
    InMem --> VdbAbs
    RagAbs --> VdbAbs
    Pg --> VdbAbs
    Qd --> VdbAbs
    Pine --> VdbAbs
    Pixie --> VdbAbs
    classDef core fill:#eff6ff,stroke:#93b4de,color:#172c46,stroke-width:1.5px
    classDef rag fill:#eef8f5,stroke:#83b4a4,color:#164638,stroke-width:1.5px
    classDef extension fill:#f5f0fc,stroke:#b9a4d4,color:#403054,stroke-width:1.5px
    classDef documents fill:#fff8e9,stroke:#d4b879,color:#61491d,stroke-width:1.5px
    classDef store fill:#edf7fb,stroke:#8dbaca,color:#194758,stroke-width:1.5px
    classDef contract fill:#f8fafc,stroke:#a7b2c2,color:#334155,stroke-width:1.5px
```

## Packages

### Cœur

| Package | NuGet | Description |
| --- | --- | --- |
| [Mythosia.AI](../../src/core/Mythosia.AI/README.md) | [![NuGet](https://img.shields.io/nuget/v/Mythosia.AI.svg)](https://www.nuget.org/packages/Mythosia.AI) | Bibliothèque principale — fournisseurs intégrés, streaming, appels de fonctions et support multimodal |
| [Mythosia.AI.Abstractions](../../src/core/Mythosia.AI.Abstractions/README.md) | [![NuGet](https://img.shields.io/nuget/v/Mythosia.AI.Abstractions.svg)](https://www.nuget.org/packages/Mythosia.AI.Abstractions) | Interface `IAIService` et modèles partagés — package de contrat léger pour les bibliothèques |
| [Mythosia.AI.Providers.Alibaba](../../src/core/Mythosia.AI.Providers.Alibaba/README.md) | [![NuGet](https://img.shields.io/nuget/v/Mythosia.AI.Providers.Alibaba.svg)](https://www.nuget.org/packages/Mythosia.AI.Providers.Alibaba) | Package fournisseur Alibaba / Qwen basé sur `Mythosia.AI` |

### RAG

| Package | NuGet | Description |
| --- | --- | --- |
| [Mythosia.AI.Rag](../../src/rag/Mythosia.AI.Rag/README.md) | [![NuGet](https://img.shields.io/nuget/v/Mythosia.AI.Rag.svg)](https://www.nuget.org/packages/Mythosia.AI.Rag) | Extension RAG fluide pour IAIService avec l'API `.WithRag()` |
| [Mythosia.AI.Rag.Abstractions](../../src/rag/Mythosia.AI.Rag.Abstractions/README.md) | [![NuGet](https://img.shields.io/nuget/v/Mythosia.AI.Rag.Abstractions.svg)](https://www.nuget.org/packages/Mythosia.AI.Rag.Abstractions) | Interfaces et modèles pour les composants du pipeline RAG |

### Chargeurs de documents

| Package | NuGet | Description |
| --- | --- | --- |
| [Mythosia.Documents.Abstractions](../../src/loaders/Mythosia.Documents.Abstractions/README.md) | [![NuGet](https://img.shields.io/nuget/v/Mythosia.Documents.Abstractions.svg)](https://www.nuget.org/packages/Mythosia.Documents.Abstractions) | Interfaces et modèles des chargeurs de documents (`IDocumentLoader`, `DoclingDocument`) |
| [Mythosia.Documents.Office](../../src/loaders/Mythosia.Documents.Office/README.md) | [![NuGet](https://img.shields.io/nuget/v/Mythosia.Documents.Office.svg)](https://www.nuget.org/packages/Mythosia.Documents.Office) | Parseurs OpenXml pour Word / Excel / PowerPoint |
| [Mythosia.Documents.Pdf](../../src/loaders/Mythosia.Documents.Pdf/README.md) | [![NuGet](https://img.shields.io/nuget/v/Mythosia.Documents.Pdf.svg)](https://www.nuget.org/packages/Mythosia.Documents.Pdf) | Parseur PDF via PdfPig |

### Bases vectorielles

> **Choisissez-en un ou plusieurs** — tous implémentent `IVectorStore` du package Abstractions.

| Package | NuGet | Description |
| --- | --- | --- |
| [Mythosia.VectorDb.Abstractions](../../src/vectordb/Mythosia.VectorDb.Abstractions/README.md) | [![NuGet](https://img.shields.io/nuget/v/Mythosia.VectorDb.Abstractions.svg)](https://www.nuget.org/packages/Mythosia.VectorDb.Abstractions) | Contrats `IVectorStore` · `VectorRecord` · `VectorFilter` |
| [Mythosia.VectorDb.InMemory](../../src/vectordb/Mythosia.VectorDb.InMemory/README.md) | [![NuGet](https://img.shields.io/nuget/v/Mythosia.VectorDb.InMemory.svg)](https://www.nuget.org/packages/Mythosia.VectorDb.InMemory) | Store en mémoire — zéro infrastructure, idéal pour le prototypage |
| [Mythosia.VectorDb.Pinecone](../../src/vectordb/Mythosia.VectorDb.Pinecone/README.md) | [![NuGet](https://img.shields.io/nuget/v/Mythosia.VectorDb.Pinecone.svg)](https://www.nuget.org/packages/Mythosia.VectorDb.Pinecone) | API HTTP Pinecone — isolation par index/namespace/scope pour la base de vecteurs gérée |
| [Mythosia.VectorDb.Postgres](../../src/vectordb/Mythosia.VectorDb.Postgres/README.md) | [![NuGet](https://img.shields.io/nuget/v/Mythosia.VectorDb.Postgres.svg)](https://www.nuget.org/packages/Mythosia.VectorDb.Postgres) | PostgreSQL + pgvector — index HNSW / IVFFlat, prêt pour la production |
| [Mythosia.VectorDb.Qdrant](../../src/vectordb/Mythosia.VectorDb.Qdrant/README.md) | [![NuGet](https://img.shields.io/nuget/v/Mythosia.VectorDb.Qdrant.svg)](https://www.nuget.org/packages/Mythosia.VectorDb.Qdrant) | Client gRPC Qdrant — Cosine / Euclidean / Dot, provisionnement automatique |

### Serving — Plan de contrôle

Créez des sélecteurs de modèles et des écrans d'état avec une API de gestion commune aux instances Ollama, llama.cpp et vLLM en cours d'exécution. `IModelServer` consulte l'état, les modèles et les capacités ; la découverte ne charge ni ne télécharge de modèle. Ces clients se connectent à des serveurs existants, sans héberger de moteur ni envoyer de requêtes de chat.

Les interfaces facultatives `IModelLifecycle`, `IModelDownloader` et `IModelMetricsProvider` exposent des opérations explicites lorsqu'elles sont disponibles. Vérifiez les capacités du serveur connecté : `Unknown` signifie que les éléments disponibles sont insuffisants, et non `Unsupported` ; `Supported` ne garantit pas non plus le succès pour chaque modèle. Les états d'installation et de chargement inconnus restent inconnus.

Les vérifications sur serveurs réels ont réussi avec Ollama **0.34.4** (`qwen2.5:0.5b`), llama.cpp **b11146** en modes Router et mono-modèle (Qwen2.5 0.5B, Q4_K_M), et vLLM **0.30.0** (petit modèle Qwen). Ces résultats concernent uniquement les configurations testées. Consultez le [guide de gestion des serveurs](serving.md) pour les opérations vérifiées et les limites propres à chaque moteur.

| Package | NuGet | Description |
| --- | --- | --- |
| [Mythosia.AI.Serving.Abstractions](../../src/serving/Mythosia.AI.Serving.Abstractions/README.md) | [![NuGet](https://img.shields.io/nuget/v/Mythosia.AI.Serving.Abstractions.svg)](https://www.nuget.org/packages/Mythosia.AI.Serving.Abstractions) | Contrats de gestion communs et instantanés immuables des serveurs, modèles et capacités. |
| [Mythosia.AI.Serving.Ollama](../../src/serving/Mythosia.AI.Serving.Ollama/README.md) | [![NuGet](https://img.shields.io/nuget/v/Mythosia.AI.Serving.Ollama.svg)](https://www.nuget.org/packages/Mythosia.AI.Serving.Ollama) | Inventaire et état Ollama, préchargement/déchargement explicites et téléchargements en flux. |
| [Mythosia.AI.Serving.LlamaCpp](../../src/serving/Mythosia.AI.Serving.LlamaCpp/README.md) | [![NuGet](https://img.shields.io/nuget/v/Mythosia.AI.Serving.LlamaCpp.svg)](https://www.nuget.org/packages/Mythosia.AI.Serving.LlamaCpp) | Inspection llama.cpp, commandes routeur vérifiées et métriques sans chargement automatique. |
| [Mythosia.AI.Serving.Vllm](../../src/serving/Mythosia.AI.Serving.Vllm/README.md) | [![NuGet](https://img.shields.io/nuget/v/Mythosia.AI.Serving.Vllm.svg)](https://www.nuget.org/packages/Mythosia.AI.Serving.Vllm) | Fiches de modèles, état, version et métriques étiquetées vLLM ; API concrète conservée. |

## Structure du dépôt

```text
src/
  core/
    Mythosia.AI/                        # Bibliothèque principale du service AI
    Mythosia.AI.Abstractions/           # Interface IAIService et modèles partagés
    Mythosia.AI.Providers.Alibaba/      # Package fournisseur Alibaba / Qwen
  loaders/
    Mythosia.Documents.Abstractions/    # Contrats des chargeurs de documents (IDocumentLoader, DoclingDocument)
    Mythosia.Documents.Office/          # Chargeurs de documents Office (Word/Excel/PowerPoint)
    Mythosia.Documents.Pdf/             # Chargeur de documents PDF
  rag/
    Mythosia.AI.Rag/                    # API Fluent RAG et pipeline
    Mythosia.AI.Rag.Abstractions/       # Interfaces et modèles RAG (RagDocument)
  serving/
    Mythosia.AI.Serving.Abstractions/  # Contrats communs de gestion des serveurs de modèles
    Mythosia.AI.Serving.Ollama/        # Gestion Ollama et téléchargements explicites
    Mythosia.AI.Serving.LlamaCpp/      # Gestion llama.cpp en mode unique ou routeur
    Mythosia.AI.Serving.Vllm/          # Gestion et métriques vLLM
  vectordb/
    Mythosia.VectorDb.Abstractions/     # Contrats des vector stores
    Mythosia.VectorDb.InMemory/         # Vector store en mémoire
    Mythosia.VectorDb.Pinecone/         # Vector store Pinecone
    Mythosia.VectorDb.Postgres/         # Store PostgreSQL + pgvector
    Mythosia.VectorDb.Qdrant/           # Vector store Qdrant
apps/                                   # Applications (exemples et outils)
tests/                                  # Projets de tests unitaires / d'intégration
```

## Installation

```bash
dotnet add package Mythosia.AI
```

Pour les opérations LINQ avancées sur les flux :

```bash
dotnet add package System.Linq.Async
```

## Documentation

Pour passer d’une ébauche rapide à une analyse approfondie, ou produire des réponses fondées sur des informations récentes et des documents hébergés, consultez le [raisonnement et la recherche avec sources](reasoning-and-search.md).

- **[📖 Site de documentation complet](https://aj-comp.github.io/Mythosia.AI/)** — documentation générée par DocFX couvrant toutes les fonctionnalités, le pipeline RAG, les bases vectorielles et la référence API
- [Guide d'utilisation de base](getting-started.md)
- [README Mythosia.AI](../../src/core/Mythosia.AI/README.md)  Référence API complète : appels de fonctions, streaming et configuration des modèles
- [README Mythosia.AI.Rag](../../src/rag/Mythosia.AI.Rag/README.md)  Utilisation du pipeline RAG et implémentations personnalisées
- [Guide des chargeurs](document-loaders.md)
- [Notes de version](../../src/core/Mythosia.AI/RELEASE_NOTES.md)

## Valider la vitesse de traitement auprès des fournisseurs réels

Depuis la racine du dépôt, exécutez :

```powershell
./build/test-inference-speed-live.ps1
```

Cette suite payante utilise la configuration Key Vault existante et des prompts synthétiques. Elle teste Anthropic Opus 5.5, OpenAI GPT-6 Astra, Gemini 3.8 Flash et Grok 4.6 avec ProviderDefault/Standard/Fast, via les chemins de complétion et Run : 24 cas. Les erreurs d’accès du compte, l’absence de mode effectivement appliqué et les rétrogradations du serveur ne valident pas Fast ; tous les cas doivent réussir sans être ignorés. Les rapports sont écrits dans `artifacts/test-results/inference-speed-live`. Utilisez `-NoBuild` uniquement après avoir compilé les tests Release actuels. Cette commande décrit comment lancer la suite, sans affirmer que le compte actuel l’a réussie.

## Licence

Ce projet est distribué sous la [licence MIT](https://github.com/AJ-comp/Mythosia.AI/blob/main/LICENSE).

## À l'origine

Ce projet faisait à l'origine partie de [Mythosia](https://github.com/AJ-comp/Mythosia).

[Construire les options du modèle avec des définitions partagées](model-capabilities.md).
