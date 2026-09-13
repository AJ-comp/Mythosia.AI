# Migrer vers Mythosia.AI 8

Cette version permet d’isoler les réglages des requêtes, d’arrêter un travail en cours et de conserver la réponse avec sa consommation et ses sources. Elle regroupe six évolutions d’architecture, les mises à jour des fournisseurs et modèles, et les correctifs de trois examens adversariaux dans une version majeure.

Mettez à jour ensemble les seuls paquets utilisés et recompilez leurs consommateurs. Mythosia.AI apporte la dépendance Abstractions correspondante. Le tableau associe la base publiée aux versions compatibles de cette version.

| Paquet | Base publiée | Version cible |
| --- | --- | --- |
| `Mythosia.AI` | 7.1.0 | [8.0.0](../../src/core/Mythosia.AI/RELEASE_NOTES.md#v800) |
| `Mythosia.AI.Abstractions` | 3.1.0 | [4.0.0](../../src/core/Mythosia.AI.Abstractions/RELEASE_NOTES.md#v400) |
| `Mythosia.AI.Providers.Alibaba` | 2.0.1 | [3.0.0](../../src/core/Mythosia.AI.Providers.Alibaba/RELEASE_NOTES.md#v300) |
| `Mythosia.AI.Rag` | 7.6.0 | [8.0.0](../../src/rag/Mythosia.AI.Rag/RELEASE_NOTES.md#v800) |
| `Mythosia.AI.Mcp` | 0.0.1-preview | [0.1.0-preview](../../src/integrations/Mythosia.AI.Mcp/RELEASE_NOTES.md#v010-preview) |
| `Mythosia.AI.Serving.Vllm` | 1.0.0-preview | [1.0.0](../../src/serving/Mythosia.AI.Serving.Vllm/RELEASE_NOTES.md#v100) |

`Mythosia.AI.Serving.Vllm` passe de `1.0.0-preview` à la version stable `1.0.0`. Il conserve les API existantes de modèles, d’état, de version serveur et de métriques dans un paquet indépendant, sans dépendance au paquet AI principal.

## Partir du besoin

| Besoin | Changement et migration |
| --- | --- |
| Détecter les erreurs d’options d’image avant l’envoi | Remplacez les chaînes par `ImageQuality`, `ImageBackground`, `ImageOutputFormat` et `ImageSize.Pixels(...)` / `ImageSize.Preset(...)`. Le support dépend du fournisseur. |
| Préparer plusieurs requêtes indépendantes | Commencez par `CreateRequest(...)` et conservez le nouveau builder rendu par chaque `With...`. Les setters du service modifient toujours les valeurs partagées par défaut. |
| Renvoyer des données depuis un outil asynchrone | Les méthodes enregistrées par attribut acceptent des objets via `Task<T>` / `ValueTask<T>` et un `CancellationToken` injecté. Les exceptions sont des échecs ; les gestionnaires de chaînes restent pris en charge. |
| Cesser d’attendre après annulation | Passez `cancellationToken` aux entrées de complétion, Run et RAG compatibles. Il arrête le travail local et les outils coopératifs, sans garantir l’arrêt distant ni annuler les actions externes déjà exécutées. |
| Conserver réponse, consommation et sources | `AIRun.Result` devient `Task<AIRunResult>`. Lisez `(await run.Result).Text` pour la chaîne. La collecte fonctionne sans lire le flux. |
| Afficher les commandes adaptées au modèle | Utilisez `request.GetCapabilities()` ou les requêtes du service/des images. `Supported`, `Unsupported` et `Unknown` décrivent les connaissances locales de la bibliothèque, pas l’accès réel au compte. |

## Adapter les appelants et fournisseurs personnalisés

Les types d’options d’image, `AIRun.Result` et les signatures d’annulation modifiées rompent des contrats. Les implémentations personnalisées de `IAIService` et les overrides des surcharges publiques modifiées doivent ajouter et transmettre le token. L’override fournisseur `GetCompletionAsync(Message)` garde sa signature et transmet `RequestCancellationToken`. Un `AIRun` personnalisé renvoie `AIRunResult`. GetCompletionAsync conserve son résultat chaîne ; la complétion typée et `StructuredStreamRun<T>.Result` conservent leurs types. Les StreamAsync de service/RAG acceptant une entrée restent publics en v8. RunAgentAsync et RunAgentStreamAsync gardent leur comportement de compatibilité et leurs avertissements obsolete. Utilisez Run pour les nouveaux flux de progression, annulation et pilotage pris en charge.

## Une requête, un résultat et une progression facultative

```csharp
using Mythosia.AI.Builders;
using Mythosia.AI.Models.Runs;
using Mythosia.AI.Models.Capabilities;

AIRequestBuilder request = service.CreateRequest("Summarize this document.");
var capabilities = request.GetCapabilities();
if (capabilities.Temperature == CapabilitySupport.Supported)
    request = request.WithTemperature(0.2f);

await using var run = await request
    .StartRunAsync(
        onText: text => Console.Write(text),
        cancellationToken: cancellationToken);

AIRunResult result = await run.Result;
Console.WriteLine(result.Text);
```

OpenAI utilise des pixels ; Google et xAI utilisent `ImageSize.Preset(...)`. Ne changez Auto que si le fournisseur accepte un format explicite ; enregistrez selon le `GeneratedImage.MediaType` retourné.

```csharp
using Mythosia.AI.Models.Images;

var imageRequest = new ImageGenerationRequest
{
    Prompt = "A simple architectural study",
    Quality = ImageQuality.Auto,
    Background = ImageBackground.Auto,
    OutputFormat = ImageOutputFormat.Auto,
    Size = ImageSize.Pixels(1536, 1024)
};
var generated = await images.GenerateImagesAsync(imageRequest, cancellationToken);
```

Un outil enregistré peut renvoyer un objet applicatif comme ci-dessous. Le gestionnaire bas niveau `HandlerWithCancellation` renvoie toujours `Task<string>` ; aucun nouveau wrapper objet n’est requis.

```csharp
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Mythosia.AI.Attributes;

public sealed record FileResult(string Path, string Text);

public sealed class FileTools
{
    [AiFunction("read_file", "Read a text file")]
    public async Task<FileResult> ReadFileAsync(
        string path, CancellationToken cancellationToken)
    {
        string text = await File.ReadAllTextAsync(path, cancellationToken);
        return new FileResult(path, text);
    }
}
```

## Fournisseurs et validation

La version comprend aussi les intégrations préparées de Fable 5.1, Gemini 3.7/3.8 Flash, Grok 4.6, DeepSeek Flash et Perplexity Agent, ainsi que la génération et l’édition d’images communes à OpenAI, Google et xAI. Des constantes supprimées et le changement d’endpoint Perplexity peuvent nécessiter des adaptations ; consultez le guide des fournisseurs et les notes des paquets.

Configurez la recherche avec `PerplexityAgentOptions`. Les tests Profile, Custom Skill et Connector sont préparés mais exigent des ressources enregistrées. MCP reste preview. Après le début de la libération, les appels échouent avec `ObjectDisposedException` ; après l’arrêt de la boucle de lecture, les nouveaux appels échouent avec `McpException`, sans attente indéfinie.

Trois examens adversariaux ont renforcé les copies, résultats d’outils, annulation/nettoyage, calcul des tokens, validation des réponses et cycle de vie MCP. Le troisième a ajouté 43 cas de régression ; les 2 703 tests ont réussi. La documentation couvre 13 langues. Aucun appel réel aux API fournisseurs n’a été effectué durant cet examen ; les tests unitaires ne démontrent pas toutes les intégrations dépendantes du compte.

## Guides détaillés

- [CreateRequest / AIRequestBuilder](request-building.md)
- [AIRun / AIRunResult](execution-api-transition.md)
- [ImageQuality / ImageSize](providers.md#image-options-migration)
- [CancellationToken](completions.md#completion-cancellation)
- [Task<T> / ValueTask<T> / HandlerWithCancellation](function-calling.md#tool-execution-contract)
- [GetCapabilities](model-capabilities.md)
- [PerplexityAgentOptions](perplexity.md)
