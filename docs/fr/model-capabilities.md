# Afficher les options prises en charge par le modèle

Une interface de chat doit proposer raisonnement, recherche, outils et images selon la connexion choisie. Des listes de modèles maintenues dans chaque application dupliquent les règles de la bibliothèque et divergent lorsque fournisseur, protocole ou déploiement change. Les instantanés de capacités partagent les mêmes définitions entre interface et validation d’exécution.

Cette API appartient à Mythosia.AI 8.0.0. Les instantanés sont des descriptions locales immuables du support connu, pas des sondes du compte ou du serveur. Les types sont dans `Mythosia.AI.Models.Capabilities`.

## Before / After

Before : l’application maintient ses listes. Les listes ci-dessous représentent son code, pas des API de la bibliothèque.

```csharp
bool showReasoning = mySupportedReasoningModels.Contains(service.Model);
bool showSearch = mySupportedSearchModels.Contains(service.Model);
```

After : examiner la requête configurée, puis choisir les options supportées. Seul l’appel final de complétion envoie la requête au modèle ; l’inspection ne contacte pas l’API.

```csharp
using Mythosia.AI.Models;
using Mythosia.AI.Models.Capabilities;

var request = service.CreateRequest("Expliquez les documents.");
AIModelCapabilities capabilities = request.GetCapabilities();

if (capabilities.GetReasoningSupport(ReasoningLevel.High)
    == CapabilitySupport.Supported)
{
    request = request.WithReasoning(ReasoningLevel.High);
}

string answer = await request.GetCompletionAsync();
```

`CapabilitySupport` distingue `Supported`, `Unsupported` et `Unknown`. Un déploiement personnalisé ou un modèle choisi par le serveur peut manquer d’informations : `Unknown` ne signifie pas non supporté. L’exemple active le raisonnement supplémentaire seulement si son support est connu. Sinon, l’application choisit de conserver les valeurs par défaut ou d’autoriser un essai.

`request.GetCapabilities()` lit modèle, options fournisseur et profil capturés dans le builder. `service.GetCapabilities()` inspecte les valeurs par défaut sans consommer les options du prochain appel. Aucun HTTP, callback de contexte, validateur d’exécution, changement d’historique ou démarrage n’a lieu. Les listes sont des instantanés en lecture seule. La requête sur le service consulte aussi les fonctions en attente pour le prochain appel, sans les retirer de la future requête. L’inspection ne sérialise ni les valeurs par défaut des fonctions ni les paramètres des outils hébergés ; elle ne prépare pas non plus le profil d’exécution et ne réserve aucun budget de tokens.

Les capacités décrivent ce que la connexion peut supporter, pas les options activées. Fournisseur, protocole API et mode comptent aussi. L’identité du modèle reflète les surcharges et la traduction d’ID Qwen/Ollama ; elle peut être `null` sans sélection d’un modèle unique. L’exemple Chat UI actualise les contrôles à partir de la connexion active et de ses réglages, outils enregistrés compris, plutôt que du seul catalogue de modèles. Le support de l’échantillonnage peut changer selon le mode de raisonnement ou la présence d’outils ; relancez la consultation après ces changements. Un support inconnu reste distinct d’un support absent.

| API | Signification |
| --- | --- |
| `Reasoning`, `ReasoningLevels`, `GetReasoningSupport(...)` | Support et niveaux du `WithReasoning` commun. |
| `NativeReasoning`, `NativeReasoningLevels`, `ThinkingBudgetPresets`, `ThinkingToggle` | Contrôles natifs du fournisseur et suggestions de budget. |
| `Streaming`, `FunctionCalling`, `AsyncFunctionCalling`, `Steering` | Streaming, outils, outils asynchrones natifs et instructions pendant l’exécution. |
| `WebSearch`, `FileSearch`, `ReasoningCachePreservation`, `ImageInput`, `StructuredOutput` | Recherche hébergée, modification du raisonnement avec cache conservé, images en entrée, sortie structurée. |
| `Temperature`, `TopP`, `FrequencyPenalty`, `PresencePenalty`, `MaxOutputTokens` | Paramètres d’échantillonnage supportés et limite connue de jetons de sortie, nullable. |
| `Provider`, `Model` | Fournisseur et modèle envoyé ; ces identités peuvent être inconnues. |

`ReasoningLevels` concerne le `WithReasoning` commun ; `NativeReasoningLevels`, les réglages natifs. `ThinkingBudgetPresets` fournit des choix d’interface, pas tous les budgets valides ni une plage exhaustive. `AsyncFunctionCalling` désigne les outils asynchrones natifs, pas simplement un handler local retournant `Task` ou exécuté en parallèle. `StructuredOutput` couvre l’API de sortie typée commune, y compris le repli par prompt et réparation ; il ne garantit pas le décodage contraint natif. Les deux listes de niveaux utilisent `ReasoningLevel` ; les budgets suggérés sont entiers.

L’instantané ne garantit ni l’accès du compte ni la disponibilité du serveur et ne rend pas valides des combinaisons incorrectes. Les validations et erreurs d’exécution restent applicables. Vérifiez `run.CanSteer` sur la session réelle : le support du modèle ne garantit pas que le Run soit encore actif.

## Inspecter séparément la génération d’images

Le modèle d’image est indépendant du chat. `service.GetImageCapabilities(imageModel)` inspecte un modèle donné ; sans argument, le modèle d’image par défaut du fournisseur. Le builder de chat ne choisit pas ce modèle. `Generation`, `Editing` et `Mask` déterminent les actions à présenter.

```csharp
using Mythosia.AI.Models.Capabilities;

ImageModelCapabilities capabilities = service.GetImageCapabilities();
bool showEditing = capabilities.Editing == CapabilitySupport.Supported;
bool showMask = capabilities.Mask == CapabilitySupport.Supported;

foreach (var quality in capabilities.Qualities)
    Console.WriteLine(quality);

int? maximumImages = capabilities.MaxImages;
```

`Qualities`, `Backgrounds`, `OutputFormats`, `SizeKinds`, `Resolutions` et `AspectRatios` sont des listes typées en lecture seule. `MaxImages` et `MaxInputImages` sont des limites connues nullables. Une valeur listée ne garantit pas toutes les combinaisons : les validations de taille, format, qualité, masque et modèle demeurent. Les modèles inconnus ou personnalisés restent inconnus, sans être déclarés non supportés.

Pour un `AIService` personnalisé disposant de définitions fiables, redéfinissez le hook protected `ResolveRequestCapabilities()`. Sa valeur par défaut est `AIModelCapabilities.Unknown`. L’absence du catalogue ne doit pas rendre un déploiement non supporté. Aucun membre obligatoire n’est ajouté à `IAIService` ; les méthodes appartiennent à `AIService` et à son builder.

Si le profil d’un fournisseur personnalisé modifie des indicateurs de mode natif, redéfinissez `ApplyCapabilityRequestProfile(AIRequestProfile)` et appliquez uniquement les indicateurs nécessaires au résolveur avec `SetExecutionSetting(...)`. Le hook par défaut ne fait rien. Le builder a déjà capturé les réglages communs du profil ; l’inspection n’appelle jamais `ApplyRequestProfile` ni `ApplyProviderSpecificRequestProfile`. Ce hook ne doit effectuer ni validation, rappel, sérialisation ou réservation de budget, ni modification de l’état appartenant au service ou à l’appelant. Ses réglages temporaires sont restaurés après l’inspection, même si la redéfinition lève une exception.

[Réglages de requête](request-building.md) · [Options fournisseur et image](providers.md) · [Contrôle du Run](execution-api-transition.md)
