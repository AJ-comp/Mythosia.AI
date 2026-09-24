# Fonctionnalités par fournisseur

> Les exemples `CreateRequest` nécessitent Mythosia.AI 8.0.0 / Abstractions 4.0.0. Le builder n’existe pas dans l’ancienne version 7.1 qui a introduit Run et les options communes. Les anciens packages peuvent conserver les surcharges du service.

<a id="image-options-migration"></a>
Les modes et métadonnées retournées varient selon fournisseur, modèle et API. Consultez l’[option de vitesse commune](request-building.md#inference-speed) et les capabilities ; demander Fast ne prouve pas son application.

## Migration des options d’image typées

Choisissez la qualité et le format avec des enums et la complétion, et distinguez dimensions exactes et niveau de résolution. Cela évite les fautes de frappe et la conversion silencieuse des pixels demandés en une autre taille.

Changement incompatible pour Mythosia.AI 8.0.0 : `Quality`, `Background` et `OutputFormat` deviennent des enums, `Size` devient `ImageSize`, et la propriété séparée `AspectRatio` disparaît. La valeur par défaut de `OutputFormat` est désormais `ImageOutputFormat.Auto`. Les méthodes de génération et de retouche restent les mêmes.

**Before**

```text
var request = new ImageGenerationRequest
{
    Prompt = "A glass pavilion at sunrise",
    Quality = "max",
    Background = "transparent",
    OutputFormat = "png",
    Size = "1536x1024"
};

// Google / xAI
Size = "2K";
AspectRatio = "3:2";
```

**After**

```csharp
using Mythosia.AI.Models;
using Mythosia.AI.Models.Images;

var request = new ImageGenerationRequest
{
    Model = AIModels.OpenAI.GptImage2_5Sunburst,
    Prompt = "A glass pavilion at sunrise",
    Quality = ImageQuality.Max,
    Background = ImageBackground.Transparent,
    OutputFormat = ImageOutputFormat.Png,
    Size = ImageSize.Pixels(1536, 1024)
};

// Google / xAI
var presetRequest = new ImageGenerationRequest
{
    Prompt = "A glass pavilion at sunrise",
    Size = ImageSize.Preset(ImageResolution.TwoK, ImageAspectRatio.ThreeByTwo),
    OutputFormat = ImageOutputFormat.Auto
};
```

`Pixels(width, height)` demande des dimensions exactes. `Preset(resolution, aspectRatio)` indique un niveau de résolution et un ratio ; le fournisseur choisit les pixels réels. Utilisez `ImageSize.Auto` sans contrainte de taille. Ne migrez des pixels vers un preset que si des dimensions approximatives conviennent.

| Provider | `Size` | `OutputFormat` |
| --- | --- | --- |
| OpenAI | `ImageSize.Auto`, `Pixels(...)` | `Auto` → PNG, `Png`, `Jpeg`, `WebP` |
| Google | `ImageSize.Auto`, `Preset(...)` | `Auto`, `Jpeg` |
| xAI | `ImageSize.Auto`, `Preset(...)` | `Auto` |

Les valeurs enum non définies et les combinaisons non prises en charge sont refusées avant HTTP. Tous les modèles ne prennent pas en charge tous les membres. Google accepte uniquement `ImageQuality.Auto`, xAI `Auto`, `Low` et `Medium`.

Vous pouvez réutiliser les buffers d’entrée dès que `EditImagesAsync` renvoie son `Task`. La requête démarrée conserve ses propres données d’image, y compris les octets du masque OpenAI ; modifier ensuite les tableaux `ImageInput.Data` d’origine ne change pas le contenu envoyé.

Pour éviter d’enregistrer une sortie interrompue comme une image terminée, la génération et la retouche Google exigent que tous les candidats renvoyés se terminent avec `finishReason: STOP`. Si l’un d’eux est bloqué, incomplet ou sans cet état final, l’appel entier lève `AIServiceException`. Des données base64 intégrées ou des métadonnées MIME d’image manquantes ou invalides font également échouer l’appel entier ; le format PNG n’est pas supposé. Ces contrôles ne vérifient pas que les octets du fichier correspondent au format d’image déclaré.

## OpenAI (OpenAIService)

> GPT-6 Astra et les appels d’outils asynchrones sont pris en charge à partir de `Mythosia.AI` 7.1.0, avec les types partagés dans `Mythosia.AI.Abstractions` 3.1.0.

Une consultation lente ne doit pas forcément suspendre toute la réponse. Pendant le chargement de la météo, par exemple, le modèle peut déjà formuler des conseils de voyage généraux qui ne dépendent pas du résultat.

`FunctionDefinition.AllowAsync = true` ou `FunctionBuilder.WithAsync()` permet d’autoriser les appels asynchrones pour GPT-6 Astra / Sol / Luna via Responses. La valeur par défaut est `false` ; les modèles non compatibles attendent le résultat du même gestionnaire. Voir les exemples et la durée de vie des requêtes dans le [guide des appels de fonctions](function-calling.md).

<a id="gpt-6-sol-luna"></a>

### GPT-6 Sol / Luna

Choisissez GPT-6 Sol pour le code complexe, les outils et les tâches d’agent, ou Luna pour traiter de gros volumes de texte ou d’images à moindre coût. Les deux utilisent les API existantes de réponse complète, de streaming et de Run : changer de modèle conserve le fonctionnement de l’application.

> Nécessite Mythosia.AI 8.1.0 / Abstractions 4.1.0. Les versions minimales d’Astra et le modèle par défaut du service restent inchangés.

Utilisez `AIModels.OpenAI.Gpt6Sol` (`gpt-6-sol`) ou `AIModels.OpenAI.Gpt6Luna` (`gpt-6-luna`). Les deux acceptent du texte et des images et produisent du texte : contexte de 1 050 000 tokens, entrée maximale de 922 000 et sortie maximale de 128 000. L’entrée, le raisonnement et la sortie doivent ensemble respecter le contexte. `MaxTokens` définit le budget de sortie demandé, pas la taille du contexte.

```csharp
using Mythosia.AI.Models;
using Mythosia.AI.Services.OpenAI;

var service = new OpenAIService(apiKey, httpClient);
service.ChangeModel(AIModels.OpenAI.Gpt6Sol);
await using var run = await service.CreateRequest("Review this design.")
    .WithReasoning(ReasoningLevel.High)
    .StartRunAsync(onText: text => Console.Write(text));
var result = await run.Result;

service.ChangeModel(AIModels.OpenAI.Gpt6Luna);
string answer = await service.CreateRequest("Summarize this paragraph.")
    .WithReasoning(ReasoningLevel.None)
    .WithTemperature(0.2f)
    .GetCompletionAsync();
```

`Auto` correspond à `Medium`. Sol/Luna acceptent `None`, `Low`, `Medium`, `High`, `XHigh` et `Max`, mais pas `Minimal`. Utilisez `WithReasoning(ReasoningLevel.None)` par requête ou `Gpt6Reasoning.None` dans `WithGpt6Parameters`. Seuls Sol/Luna avec `None` envoient `Temperature` / `TopP` ; ces champs sont omis avec le raisonnement actif. Astra exige toujours le raisonnement et omet l’échantillonnage. `AIRequestProfile.DisableReasoning` choisit `None` pour Sol/Luna et `Low` en mode Standard pour Astra, sans résumé du raisonnement.

`Gpt6ReasoningMode.Standard` et `.Pro` utilisent le même identifiant de modèle. Les trois GPT-6 prennent en charge les outils via Responses, les outils asynchrones facultatifs, les instructions pendant un Run WebSocket et les changements de raisonnement préservant le cache en mode Standard à un seul agent. Vérifiez `run.CanSteer` ; accepter une instruction n’annule pas la sortie précédente. `WithSpeed(InferenceSpeed.Fast)` demande le traitement Fast payant sans modifier le raisonnement. Vérifiez le mode appliqué dans `result.Processing` ; droits du compte et déclassement côté serveur sont distincts des capacités locales.

[GPT-6 Sol](https://developers.openai.com/api/docs/models/gpt-6-sol) · [GPT-6 Luna](https://developers.openai.com/api/docs/models/gpt-6-luna) · [Reasoning](https://developers.openai.com/api/docs/guides/reasoning) · [Fast](https://developers.openai.com/api/docs/guides/fast-mode)

### Niveau d'effort de raisonnement

GPT-6 Astra / Sol / Luna et GPT-5.1–5.6 permettent de régler l’effort de raisonnement pour équilibrer rapidité et profondeur :

```csharp
using Mythosia.AI.Models;

// GPT-6 Astra
service.ChangeModel(AIModels.OpenAI.Gpt6Astra);
service.WithGpt6Parameters(
    reasoningEffort: Gpt6Reasoning.Medium, // Auto, Low, Medium, High, XHigh, Max
    verbosity: Verbosity.Medium,
    reasoningSummary: ReasoningSummary.Auto,
    reasoningMode: Gpt6ReasoningMode.Standard);

// GPT-5.6 : Sol est le modèle phare ; Terra et Luna sont des options plus économiques.
service.ChangeModel(AIModels.OpenAI.Gpt5_6Sol);
service.WithGpt5_6Parameters(
    reasoningEffort: Gpt5_6Reasoning.Medium, // None, Low, Medium, High, XHigh, Max
    verbosity: Verbosity.Medium);            // Low, Medium, High

// Série GPT-5.4
service.ChangeModel(AIModels.OpenAI.Gpt5_4);
service.Gpt5_4ReasoningEffort = Gpt5_4Reasoning.High; // None, Low, Medium, High, XHigh

// Série GPT-5.2
service.ChangeModel(AIModels.OpenAI.Gpt5_2);
service.Gpt5_2ReasoningEffort = Gpt5_2Reasoning.Medium;

```

GPT-6 Astra utilise l’API Responses par défaut ; les appels de fonctions l’exigent. `Auto` correspond à la valeur par défaut de la bibliothèque, `Medium` ; `None` et `Minimal` ne sont pas disponibles. `AIRequestProfile.DisableReasoning = true` utilise `Low` en mode `Standard` et omet le résumé du raisonnement. Sélectionnez `Gpt6ReasoningMode.Pro` pour activer le mode Pro avec le même identifiant de modèle `gpt-6-astra`.

Pour les tâches communes aux fournisseurs, utilisez [le raisonnement et la recherche native](reasoning-and-search.md) ; les réglages propres aux fournisseurs présentés ci-dessous restent disponibles.

### Synthèse vocale

```csharp
byte[] audio = await service.GetSpeechAsync(
    inputText: "Bonjour le monde !",
    voice: "alloy",   // alloy, echo, fable, onyx, nova, shimmer
    model: "tts-1"
);

await File.WriteAllBytesAsync("sortie.mp3", audio);
```

### Transcription audio

```csharp
byte[] audioData = await File.ReadAllBytesAsync("enregistrement.mp3");

string transcript = await service.TranscribeAudioAsync(
    audioData: audioData,
    fileName: "enregistrement.mp3",
    language: "fr"  // optionnel, ISO-639-1
);
```

`TranscribeAudioAsync` utilise `gpt-transcribe` ; sa signature publique reste inchangée.

### Génération d'images

#### GPT Image 2.5

Choisissez Flare pour produire rapidement des propositions visuelles, ou Sunburst lorsqu’une révision exige le respect précis des consignes d’édition. Les deux modèles génèrent et modifient des images via `IImageGenerationService`, sans changer le modèle de conversation.

| Modèle | Quand le choisir |
| --- | --- |
| `AIModels.OpenAI.GptImage2_5Flare` | Génération quotidienne d’images rapide et de haute qualité. |
| `AIModels.OpenAI.GptImage2_5Sunburst` | Génération et retouche où la précision de l’édition est prioritaire. |

Définissez explicitement `ImageGenerationRequest.Model`, ou la propriété héritée de `ImageEditRequest`. Le modèle OpenAI par défaut reste `AIModels.OpenAI.GptImage2`. Les alias sont `gpt-image-2.5-flare` et `gpt-image-2.5-sunburst` ; pour figer les versions du 8 septembre 2026, utilisez `GptImage2_5Flare_260908` ou `GptImage2_5Sunburst_260908` (identifiants terminés par `-2026-09-08`).

Créez une proposition avec Flare :

```csharp
using Mythosia.AI.Models;
using Mythosia.AI.Models.Images;
using Mythosia.AI.Services;
using Mythosia.AI.Services.OpenAI;

IImageGenerationService images = new OpenAIService(apiKey, httpClient);
var generated = await images.GenerateImagesAsync(new ImageGenerationRequest
{
    Model = AIModels.OpenAI.GptImage2_5Flare,
    Prompt = "Un pavillon de verre au lever du soleil, illustration de concept architectural",
    Size = ImageSize.Pixels(1024, 1024),
    Quality = ImageQuality.Low,
    OutputFormat = ImageOutputFormat.Png
});

await File.WriteAllBytesAsync("pavilion.png", generated.Images[0].Data);
```

Puis retouchez l’image générée avec Sunburst :

```csharp
var edited = await images.EditImagesAsync(new ImageEditRequest
{
    Model = AIModels.OpenAI.GptImage2_5Sunburst,
    Prompt = "Conservez le dessin du pavillon, supprimez les alentours et rendez le fond transparent.",
    InputImages = new[]
    {
        new ImageInput(generated.Images[0].Data, "image/png", "pavilion.png")
    },
    Size = ImageSize.Pixels(1024, 1024),
    Quality = ImageQuality.XHigh,
    Background = ImageBackground.Transparent,
    OutputFormat = ImageOutputFormat.Png
});

await File.WriteAllBytesAsync("pavilion-cutout.png", edited.Images[0].Data);
```

Sur ces deux modèles et leurs versions figées, `Quality` accepte `Auto`, `Low`, `Medium`, `High`, `XHigh` et `Max`. Utilisez une qualité basse pour les brouillons, puis comparez les niveaux supérieurs pour les livrables. `OutputFormat` accepte `Auto` / `Png`, `Jpeg` ou `WebP` ; `OutputCompression` va de 0 à 100 pour JPEG/WebP uniquement. Un fond `Transparent` exige PNG/WebP. `Count` va de 1 à 10.

`Size` utilise `ImageSize.Auto` ou `ImageSize.Pixels(width, height)` : dimensions multiples de 16, ratio 1:3–3:1, maximum 3840 pixels par côté et surface 655360–8294400 pixels. Au-delà de 2560×1440, les tailles sont expérimentales. OpenAI refuse `Preset`.

L’édition accepte 1 à 16 références JPEG/PNG/WebP non vides, chacune de moins de 50 MiB. Le masque facultatif doit être PNG/WebP, de moins de 50 MiB, au format et aux dimensions de la première référence, avec un canal alpha. La bibliothèque vérifie le MIME et la longueur en octets ; le fournisseur vérifie les dimensions et l’alpha.

Ces exemples utilisent les voies existantes de l’Image API : retour d’octets et édition multipart. Cette intégration n’expose pas les outils Responses `image_generation`, le streaming d’images partielles ni `input_fidelity`. Lisez `GeneratedImage.Data` et `MediaType` dans le résultat.

Consultez le [guide officiel des images](https://developers.openai.com/api/docs/guides/image-generation) et les pages [Sunburst](https://developers.openai.com/api/docs/models/gpt-image-2.5-sunburst) et [Flare](https://developers.openai.com/api/docs/models/gpt-image-2.5-flare).

---

## Anthropic (AnthropicService)

[Claude Fable 5.1](fable-5-1.md) ajoute le suivi de progression, les instructions limitées à un tour et le diagnostic des liens du raisonnement à partir de `Mythosia.AI` 8.0.0 / `Mythosia.AI.Abstractions` 4.0.0. Mythos 5.1 nécessite une invitation. Les deux refusent la sélection forcée d’outils.

<a id="claude-opus-55"></a>

### Claude Opus 5.5 : rendre visibles les longues tâches avec outils

Opus 5.5 convient aux revues de code et aux recherches documentaires nécessitant plusieurs échanges avec des outils. Les API completion et Run restent les mêmes, mais la progression est masquée par défaut et le raisonnement conservé impose de respecter l’historique. Nécessite Mythosia.AI 8.1.0 / Abstractions 4.1.0.

`ClaudeOpus5_5` sélectionne `claude-opus-5-5` : texte/images en entrée, texte en sortie, contexte de 1M et sortie maximale de 128K tokens. Au 2026-09-24, les tarifs standard entrée/sortie sont de $4/$20 par million de tokens ; les modes spéciaux et outils ont leur propre tarification. [Informations officielles](https://platform.claude.com/docs/en/models/opus-5-5/overview).

Sans modifier les réglages du service, `Auto` utilise l’effort `Medium` et omet le raisonnement lisible. Le raisonnement adaptatif reste toujours actif. Choisissez `Low`, `Medium`, `High`, `XHigh` ou `Max` ; les valeurs communes `ReasoningLevel.None` et `Minimal` sont refusées. Le modèle par défaut du service ne change pas.

```csharp
using Mythosia.AI.Models;
using Mythosia.AI.Models.Streaming;
using Mythosia.AI.Services.Anthropic;

var claude = new AnthropicService(apiKey, httpClient);
claude.ChangeModel(AIModels.Anthropic.ClaudeOpus5_5);
claude.WithAdaptiveThinkingParameters(
    ClaudeReasoningEffort.Medium, ClaudeThinkingDisplay.Updates);

await using var run = await claude.StartRunAsync(
    "Review the migration plan using the registered tools.",
    options: StreamOptions.FullOptions);

await foreach (var item in run.StreamAsync())
{
    if (item.Type == StreamingContentType.Reasoning)
        Console.WriteLine(item.Content);
    else if (item.Type == StreamingContentType.Text)
        Console.Write(item.Content);
}
string answer = (await run.Result).Text;
```

L’exemple demande `Updates` et observe `StreamingContentType.Reasoning`. Utilisez `Summarized` pour les résumés de raisonnement ou `Omitted` pour les masquer. L’argument d’affichage de `WithAdaptiveThinkingParameters(effort)` conserve `Summarized` par défaut, contrairement au service sans réglage. Après une completion ordinaire, lisez `LastThinkingContent`. Aucune périodicité de progression n’est garantie.

Un ancien `ThinkingBudget` positif est converti en effort high/xhigh/max, sans budget exact de tokens. Zéro ou une valeur négative ne désactive pas le raisonnement. Un profil qui le désactive utilise un effort faible et masque son affichage. `MaxTokens` comprend le raisonnement masqué et la réponse ; réévaluez donc limites de sortie et coûts lors de la migration.

Mythosia conserve les blocs thinking signés, même vides, entre tours de conversation et résultats d’outils. Gardez le même service et le même chat ; ne réécrivez pas les anciens messages, le système ou les outils si le raisonnement doit être conservé. `WithTurnInstruction`, `WithConversationInstruction` et `CachePreservation.Required` utilisent les contrôles existants. `WithThinkingBinding` choisit `Error` ou `DropBlock` ; `LastInputTransformations` rapporte les suppressions. Drop abandonne le raisonnement. Le [guide de l’historique](fable-5-1.md) explique ces contrôles communs ; appliquez les valeurs par défaut et la compatibilité propres à Opus 5.5.

Ne définissez pas `ForceFunctionName` ; la sélection normale des outils et `FunctionsDisabled` restent disponibles. Les prefills assistant sont refusés et les paramètres de sampling omis. Opus 5.5 ne lit pas le thinking de Fable/Mythos ; sur la Claude API, Fable 5.1 et Mythos 5.1 lisent celui d’Opus 5.5. Changer de modèle peut donc perdre le raisonnement précédent. Cet ajout n’expose pas le computer toolset natif, les task budgets, les modifications d’outils dans la conversation, la compression native ou le fallback serveur automatique. [Conditions de migration](https://platform.claude.com/docs/en/models/opus-5-5/migration-guide) · [Fonctions natives](https://platform.claude.com/docs/en/models/opus-5-5/whats-new-opus-5-5).

Avec Opus 5.5, modifier directement le contenu d’une réponse assistant stockée provoque `InvalidOperationException` avant la requête HTTP ; `DropBlock` n’autorise pas non plus à réécrire une réponse signée. Envoyez la correction comme nouvelle entrée utilisateur ou démarrez une autre conversation. Les changements d’anciens contenus user/system suivent la politique de liaison du préfixe du fournisseur.

Opus 5.5 fast mode est disponible via [WithSpeed](request-building.md#inference-speed) sur la Claude API directe avec les droits requis. L’effort choisi est conservé et la tarification premium s’applique.

### Comptage de tokens (API native)

`GetInputTokenCountAsync` est disponible chez tous les fournisseurs (voir [Générer du texte](completions.md#comptage-de-tokens)). L'implémentation d'Anthropic appelle l'endpoint officiel `messages/count_tokens`, retournant des comptes **exacts** plutôt qu'une estimation locale :

```csharp
uint tokens = await service.GetInputTokenCountAsync("Votre prompt ici");
uint total = await service.GetInputTokenCountAsync();
```

---

## Google (GoogleAIService)

Pour relire de longs documents ou effectuer plusieurs tours d’outils, choisissez Gemini 3.7 Flash ou 3.8 Flash via l’adaptateur Google existant. Ils sont disponibles à partir de `Mythosia.AI` 8.0.0 / `Mythosia.AI.Abstractions` 4.0.0 ; le modèle par défaut reste Gemini 3.6 Flash.

### Niveau de réflexion

```csharp
using Mythosia.AI.Extensions;
using Mythosia.AI.Models;
using Mythosia.AI.Models.Enums;
using Mythosia.AI.Services.Google;

var gemini = new GoogleAIService(apiKey, httpClient);
gemini.ChangeModel(AIModels.Google.Gemini3_8Flash);
// Gemini 3.7: AIModels.Google.Gemini3_7Flash
gemini.ThinkingLevel = GeminiThinkingLevel.Low;

string review = await gemini
    .CreateRequest("Compare les déploiements progressifs et blue-green, y compris les risques de retour arrière.")
    .WithReasoning(ReasoningLevel.High)
    .GetCompletionAsync();
```

Utilisez `Low` pour un premier examen léger et `High` pour une analyse exigeante ; davantage de raisonnement peut augmenter la latence et les tokens. Les deux modèles acceptent `Low`, `Medium` et `High`, mais pas `Minimal` ni `None`. `GeminiThinkingLevel.Auto` omet la surcharge ; le défaut du fournisseur pour 3.8 est `Medium`. `ThinkingLevel` définit la base du service et `WithReasoning(...)` la remplace pour une requête logique. L’adaptateur omet `temperature`, `topP` et `topK` pour ces modèles. Les limites du fournisseur sont de 1 048 576 tokens en entrée et 65 536 en sortie. [Gemini 3.7 Flash](https://ai.google.dev/gemini-api/docs/models/gemini-3.7-flash), [Gemini 3.8 Flash](https://ai.google.dev/gemini-api/docs/models/gemini-3.8-flash).

<a id="google-image-options"></a>

### Résolutions et proportions par modèle d’image Google

| Modèle | `Resolutions` | `AspectRatios` |
| --- | --- | --- |
| `gemini-3.1-flash-image` | `Auto`, `FiveTwelve` (512), `OneK` (1K), `TwoK` (2K), `FourK` (4K) | 14 + `Auto` |
| `gemini-3.1-flash-lite-image` | `Auto`, `OneK` (1K) | 14 + `Auto` |
| `gemini-3-pro-image` | `Auto`, `OneK` (1K), `TwoK` (2K), `FourK` (4K) | 10 + `Auto` |

Les 10 proportions standard sont `1:1`, `2:3`, `3:2`, `3:4`, `4:3`, `4:5`, `5:4`, `9:16`, `16:9`, `21:9`. L’ensemble de 14 ajoute `1:4`, `4:1`, `1:8`, `8:1`. Tous les modèles acceptent aussi `ImageAspectRatio.Auto`.

Utilisez `ImageSize.Auto` ou `ImageSize.Preset(resolution, aspectRatio)`. `Auto` omet le sélecteur correspondant. `GetImageCapabilities(model)` et `GenerateImagesAsync` / `EditImagesAsync` utilisent les mêmes options par modèle. Les valeurs explicites non prises en charge déclenchent `NotSupportedException` avant HTTP ; aucune modification de taille ni requête de remplacement n’est effectuée. Les identifiants personnalisés inconnus conservent `Unknown` et sont transmis tels quels après validation des options communes au fournisseur.

Pour Flash-Lite, la [page du modèle](https://ai.google.dev/gemini-api/docs/models/gemini-3.1-flash-lite-image) et le texte du guide indiquent 1K, mais le [tableau](https://ai.google.dev/gemini-api/docs/generate-content/image-generation#aspect_ratios_and_image_size) contient aussi une colonne 512. En attendant de vérifier cette divergence, la bibliothèque autorise uniquement 1K par prudence ; cela ne signifie pas qu’un refus de 512 par le serveur a été observé.

---

## xAI (XAIService)

<a id="grok-47"></a>

### Grok 4.7

Pour passer d’une ébauche rapide à une vérification exigeante de code ou de documents, choisissez Grok 4.7 et adaptez l’effort à chaque requête. Les API existantes de réponse, streaming, Run, outils locaux, sortie structurée et entrée image restent utilisables. `grok-4.7` reçoit du texte et des images, renvoie du texte et dispose d’un contexte de 500 000 tokens. Grok 4.5 reste le modèle par défaut. Nécessite Mythosia.AI 8.1.0 / Abstractions 4.1.0.

```csharp
using Mythosia.AI.Extensions;
using Mythosia.AI.Models;
using Mythosia.AI.Services.xAI;

var grok = new XAIService(apiKey, httpClient);
grok.ChangeModel(AIModels.xAI.Grok4_7);
grok.WithGrokReasoning(GrokReasoning.Low);

var request = grok.CreateRequest("Review this deployment plan and its rollback risks.")
    .WithReasoning(ReasoningLevel.XHigh)
    .WithSpeed(InferenceSpeed.Fast);

await using var run = await request.StartRunAsync();
await foreach (var content in run.StreamAsync())
    Console.Write(content.Content);
var result = await run.Result;
Console.WriteLine(result.Text);
foreach (var processing in result.Processing)
    Console.WriteLine(processing.AppliedSpeed);
```

`Low`, `Medium`, `High` et `XHigh` sont pris en charge. Le réglage natif `GrokReasoning.Auto` omet `reasoning_effort` et conserve la valeur fournisseur `High` ; le réglage commun `ReasoningLevel.Auto` omet également ce champ et utilise la valeur fournisseur `High` pour cette requête. `None`, `Minimal` et `Max` sont refusés avant l’envoi. `WithReasoning(...)` s’applique à la requête logique, y compris ses tours d’outils et corrections de sortie structurée ; `WithGrokReasoning(...)` règle la base du service. Le profil interne `DisableReasoning` utilise `Low`. Les résumés de raisonnement facultatifs ne représentent pas le raisonnement interne complet.

`WithSpeed(InferenceSpeed.Standard)` envoie `service_tier: "default"` ; `Fast` envoie `"priority"` sur les points d’accès xAI compatibles et peut coûter davantage. `ProviderDefault` ne remplace aucun réglage. Consultez `result.Processing` pour le niveau déclaré, car le serveur peut revenir au traitement ordinaire. Il s’agit du traitement prioritaire de `grok-4.7`, pas de la variante « Grok 4.7 Fast » réservée à Cursor/Grok Build, qui n’a pas d’identifiant de modèle API public.

`GetCapabilities()` décrit localement la requête sélectionnée sans vérifier les droits du compte. Cette intégration utilise Chat Completions. Le raisonnement chiffré propre à Responses, la recherche Web/X hébergée, les outils asynchrones natifs, les mises à jour préservant le cache et `SteerAsync` ne sont pas intégrés ici. Les fonctions clientes utilisent la boucle locale existante ; `run.CanSteer` vaut false.

[Grok 4.7](https://docs.x.ai/developers/grok-4-7) · [reasoning_effort](https://docs.x.ai/developers/model-capabilities/text/reasoning) · [Priority Processing](https://docs.x.ai/developers/advanced-api-usage/priority-processing)

### Choisir l’effort selon la tâche

Utilisez un effort faible pour une première ébauche rapide, puis davantage de raisonnement pour les vérifications difficiles où la qualité prime sur le temps de réponse. Sélectionnez explicitement Grok 4.6 pour utiliser son niveau supplémentaire `XHigh`.

```csharp
using Mythosia.AI.Extensions;
using Mythosia.AI.Models;
using Mythosia.AI.Services.xAI;

var grok = new XAIService(apiKey, httpClient);
grok.ChangeModel(AIModels.xAI.Grok4_6);
grok.WithGrokReasoning(GrokReasoning.Low);

string review = await grok
    .CreateRequest("Compare les déploiements progressif et bleu-vert, y compris la récupération après panne.")
    .WithReasoning(ReasoningLevel.XHigh)
    .GetCompletionAsync();
```

Grok 4.6 accepte `Low`, `Medium`, `High` et `XHigh` (`GrokReasoning.XHigh`). `Auto` omet `reasoning_effort` et conserve la valeur par défaut `High` du fournisseur ; `None` ne peut pas désactiver le raisonnement. Un effort accru peut augmenter la latence et la consommation de tokens. Pour préserver la compatibilité, `XAIService` utilise toujours Grok 4.5 par défaut. La version 4.5 accepte de `Low` à `High`, et la 4.3 de `None` à `High` ; l’adaptateur refuse `XHigh` sur ces anciens modèles avant l’envoi.

`WithGrokReasoning(...)` et la méthode existante `WithGrokParameters(...)` définissent le réglage de base du service. Sur Grok 4.6, la méthode commune `WithReasoning(...)` ne le remplace que pour une requête logique, y compris ses tours d’outils et corrections de sortie structurée, puis le restaure. Les profils internes `DisableReasoning` utilisent `Low` sur ce modèle qui raisonne toujours. Les mises à jour préservant le cache et la recherche web/de fichiers hébergée ne sont pas intégrées pour xAI via ces options communes.

Grok 4.6 peut renvoyer des résumés de raisonnement fournis par le service sous forme de `StreamingContentType.Reasoning` lorsque l’observation active `new StreamOptions().WithReasoning()`. Ces résumés sont facultatifs et ne constituent pas le raisonnement interne complet. Les mêmes options fonctionnent avec Run ; modifier l’affichage du flux ne change pas l’effort demandé.

[Grok 4.6](https://docs.x.ai/developers/grok-4-6) · [reasoning_effort](https://docs.x.ai/developers/model-capabilities/text/reasoning)

### Grok Imagine Image 2.0

Utilisez la génération pour transformer une description de produit en proposition visuelle, ou l’édition pour combiner le sujet et le décor de photos de référence. `XAIService` propose ces deux opérations via le même `IImageGenerationService` qu’OpenAI et Google, dès `Mythosia.AI` 8.0.0 / `Mythosia.AI.Abstractions` 4.0.0.

Le modèle d’image par défaut, indépendant, est `AIModels.xAI.GrokImagineImage2_0` (`grok-imagine-image-2.0`). Les requêtes d’image ne changent pas le modèle de chat et ne sont pas ajoutées à sa conversation.

```csharp
using Mythosia.AI.Models;
using Mythosia.AI.Models.Images;
using Mythosia.AI.Services;
using Mythosia.AI.Services.xAI;

IImageGenerationService images = new XAIService(apiKey, httpClient);
var generated = await images.GenerateImagesAsync(new ImageGenerationRequest
{
    Model = AIModels.xAI.GrokImagineImage2_0,
    Prompt = "Un pavillon de verre au lever du soleil, composition panoramique",
    Size = ImageSize.Preset(ImageResolution.OneK, ImageAspectRatio.SixteenByNine),
    OutputFormat = ImageOutputFormat.Auto
});

var image = generated.Images[0];
var extension = image.MediaType switch
{
    "image/jpeg" => ".jpg",
    "image/png" => ".png",
    "image/webp" => ".webp",
    _ => throw new NotSupportedException(image.MediaType)
};
await File.WriteAllBytesAsync("pavilion" + extension, image.Data);
```

Pour une édition, fournissez les octets des images dans l’ordre mentionné par le prompt :

```csharp
var edited = await images.EditImagesAsync(new ImageEditRequest
{
    Prompt = "Placez le sujet de l’image 1 dans le décor de l’image 2.",
    InputImages = new[]
    {
        new ImageInput(await File.ReadAllBytesAsync("subject.png"), "image/png", "subject.png"),
        new ImageInput(await File.ReadAllBytesAsync("scene.jpg"), "image/jpeg", "scene.jpg")
    },
    Size = ImageSize.Preset(ImageResolution.OneK),
    OutputFormat = ImageOutputFormat.Auto
});

var imageEdited = edited.Images[0];
var extensionEdited = imageEdited.MediaType switch
{
    "image/jpeg" => ".jpg",
    "image/png" => ".png",
    "image/webp" => ".webp",
    _ => throw new NotSupportedException(imageEdited.MediaType)
};
await File.WriteAllBytesAsync("combined" + extensionEdited, imageEdited.Data);
```

Chaque `GeneratedImage.Data` contient les octets décodés ; choisissez l’extension du fichier selon `MediaType`. L’adaptateur demande une réponse base64 intégrée et ne télécharge pas les URL d’images hébergées par le fournisseur. `Count` accepte 1 à 10 sorties ; l’édition accepte 1 à 5 références JPEG, PNG ou WebP.

Pour xAI, utilisez `ImageSize.Auto` ou `ImageSize.Preset(ImageResolution.OneK, ImageAspectRatio.SixteenByNine)`. Résolutions : `Auto`, `OneK`, `TwoK`, avec un ratio pris en charge par le modèle. `Pixels(...)` est refusé car des dimensions exactes ne peuvent pas être demandées.

xAI accepte uniquement le nouveau défaut commun `ImageOutputFormat.Auto`. Sans sélecteur de codec, `Jpeg`, `Png` et `WebP` explicites sont refusés avant envoi. Choisissez l’extension selon `GeneratedImage.MediaType` ; la bibliothèque ne transcode pas. Qualité : `ImageQuality.Auto`, `Low`, `Medium` ; fond : uniquement `ImageBackground.Auto`. Compression explicite et `Mask` séparé non pris en charge.

Google accepte `ImageSize.Auto` ou `Preset` avec des résolutions et proportions propres au modèle ; voir [Options d’image par modèle Google](#google-image-options). Formats : `ImageOutputFormat.Auto` ou `Jpeg` ; `Png`/`WebP` sont refusés. Google et xAI refusent `Pixels` ; OpenAI accepte `Auto`/`Pixels` et refuse `Preset`. Voir la [migration](#image-options-migration).

[Grok Imagine Image 2.0](https://docs.x.ai/developers/models/grok-imagine-image-2.0) · [Image API](https://docs.x.ai/developers/rest-api-reference/inference/images)

---

## DeepSeek (DeepSeekService)

Utilisez DeepSeek Flash pour passer d’une réponse rapide à une analyse approfondie, ou interpréter un graphique ou une capture d’écran. `AIModels.DeepSeek.Flash` (`deepseek-flash`) sélectionne V4.1 Flash, publié le 10 septembre 2026 avec vision native. Les API existantes de complétion, streaming, Run, fonctions et RAG restent disponibles dès `Mythosia.AI` 8.0.0 / `Mythosia.AI.Abstractions` 4.0.0.

> Les paquets publiés `Mythosia.AI` 8.0.0 / `Mythosia.AI.Abstractions` 4.0.0 incluent déjà la prise en charge de base de Flash. `AIModels.DeepSeek.V4Pro`, `UseResponsesApi`, Files API, `DeepSeekImageFileContent`: Nécessite Mythosia.AI 8.1.0 / Abstractions 4.1.0. [v8.1.0](../../src/core/Mythosia.AI/RELEASE_NOTES.md#v810).

Pour le texte uniquement, choisissez `AIModels.DeepSeek.V4Pro` (`deepseek-v4-pro`, V4-Pro-0813). Flash reste le modèle par défaut et accepte les images ; les deux offrent Low/High/Max et le même plafond de sortie. Définissez `UseResponsesApi = true` avant de créer la requête pour utiliser Responses avec les API existantes de complétion, streaming, Run et fonctions locales. La valeur par défaut reste `false` pour conserver Chat Completions dans les applications existantes. Le choix est figé pour la requête et ses tours d’outils. Responses renvoie tout l’historique de conversation et de raisonnement natif sans dépendre d’identifiants de réponses stockés.

```csharp
using Mythosia.AI.Extensions;
using Mythosia.AI.Models;
using Mythosia.AI.Services.DeepSeek;

var pro = new DeepSeekService(apiKey, AIModels.DeepSeek.V4Pro, httpClient)
{
    UseResponsesApi = true
};
pro.WithDeepSeekReasoning(DeepSeekReasoning.High);
string answer = await pro.CreateRequest("Review this deployment plan.").GetCompletionAsync();
```

Téléversez une image une seule fois pour la réutiliser dans plusieurs questions ou conversations. `UploadFileAsync` accepte un chemin ou un flux appartenant à l’appelant avec son nom de fichier ; purpose vaut `user_data`. JPEG, PNG, GIF et WebP sont limités à 64 MiB. `DeepSeekImageFileContent` référence cette image avec Flash sur les deux transports ; ce n’est pas une entrée PDF/document et V4 Pro la refuse. Sans expiration, le fichier reste permanent ; `expiresAfterSeconds` accepte 3600–2592000 secondes. Conservez-le jusqu’à la fin des conversations qui le référencent.

```csharp
using Mythosia.AI.Models.Messages;

var vision = new DeepSeekService(apiKey, AIModels.DeepSeek.Flash, httpClient)
{
    UseResponsesApi = true
};
var file = await vision.UploadFileAsync("chart.png", expiresAfterSeconds: 3600);
var question = new Message(ActorRole.User, new List<MessageContent>
{
    new TextContent("Explain the trend in this chart."),
    new DeepSeekImageFileContent(file.Id)
});
string uploadedDescription = await vision.GetCompletionAsync(question);
var metadata = await vision.GetFileAsync(file.Id);
```

`GetFileAsync` lit les métadonnées, `ListFilesAsync(new DeepSeekFileListOptions { After = lastId, Limit = 20, Order = DeepSeekFileOrder.Ascending })` lit une page et `DeleteFileAsync` supprime le fichier. Tant que `HasMore` vaut true, utilisez `LastId` comme prochain `After` ; `Descending` est aussi accepté. Aucun endpoint de téléchargement du contenu n’est documenté. Chat UI propose Flash et V4 Pro et utilise le catalogue courant pour la réécriture. L’ancien nom enregistré `DeepSeekChat` migre vers Flash ; les identifiants arbitraires sont conservés.

[Responses](https://api-docs.deepseek.com/guides/responses_api/) · [Files](https://api-docs.deepseek.com/guides/files_api/) · [Models and limits](https://api-docs.deepseek.com/quick_start/pricing/)

```csharp
using Mythosia.AI.Extensions;
using Mythosia.AI.Models;
using Mythosia.AI.Models.Streaming;
using Mythosia.AI.Services.DeepSeek;

var deepseek = new DeepSeekService(apiKey, httpClient);
deepseek.ChangeModel(AIModels.DeepSeek.Flash);
deepseek.WithDeepSeekReasoning(DeepSeekReasoning.Low);

string draft = await deepseek.GetCompletionAsync("Comparez les déploiements progressif et blue-green, y compris les risques de retour arrière.");

await using var run = await deepseek
    .CreateRequest("Vérifiez les hypothèses de cette comparaison.")
    .WithReasoning(ReasoningLevel.High)
    .StartRunAsync(
        options: new StreamOptions().WithReasoning());
await foreach (var item in run.StreamAsync())
{
    if (item.Type == StreamingContentType.Reasoning)
        Console.Write(item.Content);
}
string review = (await run.Result).Text;
```

`ThinkingEnabled` reste `false` par défaut. `WithDeepSeekReasoning(...)` active le raisonnement et définit le `ReasoningEffort` persistant (`Auto`, `Low`, `High`, `Max`) ; son `Auto` omet l’effort et laisse le défaut fournisseur `High`. Le `WithReasoning(...)` commun ne modifie qu’une requête logique et ses tours d’outils : `None` désactive, `Minimal`/`Low` correspond à `Low`, `Medium`/`High`/`XHigh` à `High`, et `Max` à `Max`. L’`Auto` commun conserve la configuration existante. Un effort accru peut augmenter la latence et les jetons. Modifier uniquement `ReasoningEffort` n’active pas le raisonnement.

Enregistrez des fonctions locales avec `WithFunction(...)` pour récupérer des données ou agir via votre code. Les outils fonctionnent avec ou sans raisonnement. Chat Completions refuse la sélection forcée/obligatoire pendant le raisonnement ; utilisez alors le choix automatique. Avec `UseResponsesApi = true`, `ForceFunctionName` peut désigner une fonction même pendant le raisonnement ; l’adaptateur place `type` et `name` directement dans `tool_choice` de Responses. Cela n’active pas les outils asynchrones natifs. L’adaptateur conserve `reasoning_content` et les identifiants pour les tours suivants. Run et le streaming existant exposent `StreamingContentType.Reasoning` avec `StreamOptions.WithReasoning()` ; ce réglage d’observation n’active pas le raisonnement. L’usage inclut cache et raisonnement lorsque le fournisseur les rapporte. La récupération automatique du contexte utilise la boucle commune de streaming. Si les outils nécessitent l’historique natif du raisonnement, la compression automatique est bloquée pour le conserver et l’erreur de dépassement reste visible.

Envoyez un graphique ou une capture avec les types de messages existants :

```csharp
using Mythosia.AI.Models.Messages;

var message = new Message(ActorRole.User, new List<MessageContent>
{
    new TextContent("Expliquez la tendance de ce graphique et signalez les libellés illisibles."),
    new ImageContent(await File.ReadAllBytesAsync("chart.png"), "image/png")
});
string description = await deepseek.GetCompletionAsync(message);
```

`ImageContent` accepte des octets JPEG, PNG, GIF ou WebP, ou une URL HTTP(S) publique récupérée par le fournisseur. L’exemple utilise un message utilisateur. L’API actuelle accepte aussi les images dans les messages d’outils, mais les fonctions enregistrées renvoient du texte via le contrat commun. Un message image `ActorRole.Function` fourni manuellement doit inclure l’ID correspondant dans `MessageMetadataKeys.FunctionId` (`tool_call_id` transmis). Consultez le guide vision actuel pour les limites de taille et cumulées. La génération d’images reste indisponible.

Les deux modèles offrent 1M de contexte et jusqu’à 384K (`393216`) jetons de sortie ; le budget par défaut reste 8 000. Le raisonnement omet temperature/penalty et utilise `top_p` au moins égal à 0,95 ; hors raisonnement, `top_p` est omis. Responses utilise les API de sortie typée existantes pour JSON schema natif. Exécution en arrière-plan, `store`/`previous_response_id` côté serveur, recherche hébergée, `CachePreservation.Required`, outils asynchrones natifs, `SteerAsync` et génération d’images ne sont pas pris en charge. Le RAG local et les tours ordinaires restent disponibles.

`V4Flash`, `Chat` et `Reasoner` restent des constantes obsolete avec avertissement et identifiants inchangés. Le fournisseur redirige temporairement l’alias retiré `deepseek-v4-flash` vers V4.1 Flash ; la bibliothèque ne réécrit pas la constante. Choisissez `Flash` dans le nouveau code. `UseReasonerModel()` sélectionne Flash avec raisonnement `High`.

[DeepSeek V4.1 Flash](https://api-docs.deepseek.com/updates/) · [Thinking](https://api-docs.deepseek.com/guides/thinking_mode/) · [Vision](https://api-docs.deepseek.com/guides/vision/) · [Tools](https://api-docs.deepseek.com/guides/tool_calls/) · [Limits](https://api-docs.deepseek.com/quick_start/pricing/)

---

## Perplexity (PerplexityService)

Utilisez Perplexity quand une réponse doit tenir compte d'informations récentes et fournir des sources vérifiables. `PerplexityService` appelle l'Agent API ; la recherche et les embeddings indépendants permettent de construire la récupération de documents autour du modèle de réponse de votre choix.

```csharp
using Mythosia.AI.Models;
using Mythosia.AI.Services.Perplexity;

var service = new PerplexityService(apiKey, httpClient);
service.ChangeModel(AIModels.Perplexity.Sonar);

await using var run = await service.StartRunAsync("Compare les méthodes récentes de recyclage des batteries et cite les sources.");
Console.WriteLine((await run.Result).Text);
foreach (var source in run.Citations)
    Console.WriteLine(source.Url);
```

Le [guide Perplexity](perplexity.md) explique les préréglages de recherche, les fonctions locales, les outils hébergés et les tâches longues en arrière-plan. Les API habituelles de complétion, de streaming, Run et de citations restent utilisables.

Cette version passe le service à `/v1/agent`. `AIModels.Perplexity.Sonar` sélectionne désormais `perplexity/sonar`. Le fournisseur a annoncé l'arrêt des anciens endpoints Sonar le 27 septembre 2026 ; les intégrations existantes doivent migrer. [Perplexity](https://community.perplexity.ai/t/sonar-is-moving-to-the-agent-api/5802)

---

## Alibaba / Qwen (QwenService)

Installez le package séparé :

```bash
dotnet add package Mythosia.AI.Providers.Alibaba
```

```csharp
using Mythosia.AI.Providers.Alibaba;

var service = new QwenService(apiKey, http)
{
    Model = AlibabaModels.QwenMax
};
```

Modèles disponibles : `QwenMax`, `QwenPlus`, `QwenTurbo`, `Qwen3` et variantes.

Choisissez un endpoint compatible lors de la création du service avec `EndpointPlatform` :

```csharp
var vllmService = new QwenService(
    "http://localhost:8000",
    EndpointPlatform.Vllm,
    http);
```

[Construire les options du modèle avec des définitions partagées](model-capabilities.md).
