# Choisir l’effort de raisonnement et répondre avec des sources

> Ces API nécessitent `Mythosia.AI` 7.1.0 ou ultérieur, qui inclut `Mythosia.AI.Abstractions` 3.1.0 ou ultérieur. Les exemples RAG nécessitent `Mythosia.AI.Rag` 7.6.0 ou ultérieur.

## Pourquoi utiliser ces options ?

Chaque étape demande une aide différente. Un premier brouillon peut nécessiter une réponse rapide ; vérifier ses hypothèses peut justifier davantage de raisonnement. Une question sur les événements du jour exige des informations récentes, tandis qu’une question sur votre produit demande sa documentation. Augmenter le raisonnement ne donne, à lui seul, accès à aucune de ces sources.

Utilisez l’API Fluent commune pour exprimer les besoins de la prochaine tâche. Le fournisseur choisi traduit les options prises en charge dans son API native. Votre application peut conserver `GetCompletionAsync` pour obtenir une réponse complète ou utiliser `StartRunAsync` pour afficher la progression et contrôler la même tâche.

| Besoin de la tâche | Configuration |
| --- | --- |
| Un brouillon rapide, puis une vérification plus approfondie | `WithReasoning(...)` |
| Modifier le raisonnement en conservant un préfixe de conversation admissible au cache | `WithReasoning(..., cache: CachePreservation.Required)` |
| Des informations récentes provenant du Web | `WithWebSearch()` |
| Des réponses fondées sur des documents déjà indexés par le fournisseur | `WithFileSearch(store)` |

Les exemples supposent un service initialisé avec un modèle compatible. Importez `Mythosia.AI.Extensions` et `Mythosia.AI.Models` ; les événements du flux utilisent aussi `Mythosia.AI.Models.Streaming`.

## Passer d’un brouillon rapide à une vérification approfondie

Vous pouvez consacrer moins de raisonnement à un plan, puis demander à la même conversation d’en examiner les détails difficiles :

```csharp
string outline = await service
    .WithReasoning(ReasoningLevel.Low)
    .GetCompletionAsync("Esquisse le plan de migration.");

string review = await service
    .WithReasoning(ReasoningLevel.High)
    .GetCompletionAsync("Vérifie les scénarios de panne et les étapes de récupération de ce plan.");
```

`ReasoningLevel` exprime le niveau demandé, et non un budget fixe de tokens ou une garantie de qualité. Chaque modèle accepte son propre sous-ensemble. `Auto` conserve le comportement configuré ou par défaut du fournisseur ; il ne remplace pas automatiquement les niveaux non pris en charge. Les propriétés de budget propres au fournisseur restent disponibles pour les modèles qui proposent des budgets de tokens plutôt que des niveaux nommés.

Dans une longue conversation, modifier le paramètre d’effort de premier niveau peut invalider un préfixe de prompt réutilisable. Sur un modèle compatible, exigez le mécanisme du fournisseur permettant de modifier l’effort tout en conservant ce préfixe :

```csharp
string review = await service
    .WithReasoning(ReasoningLevel.High, cache: CachePreservation.Required)
    .GetCompletionAsync("Revérifie les hypothèses de la réponse précédente.");
```

`Required` définit la façon d’envoyer la modification. Il ne garantit **ni** accès au cache, ni tokens gratuits, ni baisse de latence : les règles d’admissibilité, de conservation et de tarification du cache restent applicables. Les modèles incompatibles lèvent `NotSupportedException` avant l’envoi. Conservez la même conversation suivie, le même modèle et le même endpoint ; ne tronquez pas et ne réordonnez pas une conversation contenant ces mises à jour. Commencez une nouvelle conversation pour changer ces conditions. La compaction automatique est bloquée tant que la conservation du préfixe est requise.

L’effort accepté avec conservation du cache devient l’effort actif de la conversation jusqu’à une nouvelle modification explicite. Un appel ordinaire à `WithReasoning(level)` s’applique à sa requête logique ; il ne remplace pas silencieusement ce réglage persistant. Ce changement intervient **entre les réponses du modèle**. Il ne modifie pas l’effort d’une réponse déjà en cours et est indépendant de `run.SteerAsync`, qui transmet une instruction supplémentaire à un Run actif compatible.

## Répondre aux questions qui exigent des informations récentes

Activez la recherche Web native lorsque la réponse doit s’appuyer sur des informations absentes des données d’entraînement du modèle :

```csharp
string answer = await service
    .WithWebSearch()
    .GetCompletionAsync("Recherche la dernière annonce de version et cite la source.");

foreach (AICitation source in service.GetLastCitations())
    Console.WriteLine($"{source.Title}: {source.Url}");
```

Le fournisseur exécute cet outil hébergé. Aucun gestionnaire de fonction local n’est à enregistrer ou à exécuter. L’activation rend la recherche accessible au modèle ; celui-ci peut décider qu’un prompt particulier n’en a pas besoin. Les références aux sources sont disponibles lorsque le fournisseur les renvoie.

OpenAI et Anthropic acceptent également `WithWebSearch(new WebSearchOptions { AllowedDomains = new[] { "example.com" } })`. Google n’expose pas cette liste de domaines autorisés dans l’outil intégré : une requête restreinte est donc refusée au lieu de lancer une recherche sur tout le Web.

## Répondre à partir de documents déjà indexés par le fournisseur

Si votre application dispose déjà d’un index documentaire hébergé par le fournisseur, utilisez ce magasin pour étayer les réponses sans implémenter votre propre cycle de récupération :

```csharp
var documents = new FileSearchStore("OpenAI", "vs_your_existing_store");

string answer = await service
    .WithFileSearch(documents)
    .GetCompletionAsync("Recherche dans nos documents de politique. Quel est le délai de résiliation ?");

foreach (AICitation source in service.GetLastCitations())
    Console.WriteLine($"{source.Title}: {source.FileId ?? source.Url}");
```

Avec un service Google, utilisez `new FileSearchStore("Google", "fileSearchStores/your-existing-store")`. Les magasins appartiennent à leur fournisseur, compte et déploiement ; un identifiant de magasin OpenAI ne peut pas être transmis à Google. Créez le magasin et importez ou indexez ses documents avec l’API ou la console du fournisseur avant de l’utiliser ici. Cette API recherche uniquement dans des magasins existants et n’importe pas de fichiers locaux.

La recherche de fichiers hébergée et le [pipeline RAG](rag.md) de la bibliothèque répondent à des besoins de mise en place différents. Choisissez la recherche hébergée si le fournisseur gère déjà votre index. Choisissez RAG si votre application doit contrôler les chargeurs, le découpage, les embeddings, la récupération ou le stockage vectoriel. `RagEnabledService` transmet également `WithReasoning`, `WithWebSearch` et `WithFileSearch` à la réponse finale ; sa réécriture interne des requêtes n’hérite pas de ces options. Les références de récupération RAG restent dans `RagProcessedQuery`, séparées des sources `AICitation` fournies par le fournisseur.

## Afficher la progression et conserver les sources

Utilisez les mêmes options avant `StartRunAsync`. Le callback de texte peut actualiser l’écran pendant que le Run conserve les sources de la réponse terminée :

```csharp
await using var run = await service
    .WithReasoning(ReasoningLevel.High)
    .WithWebSearch()
    .StartRunAsync(
        "Recherche les annonces récentes et compare les changements.",
        onText: text => Console.Write(text),
        cancellationToken: cancellationToken);

string answer = await run.Result;
foreach (AICitation source in run.Citations)
    Console.WriteLine($"{source.Title}: {source.Url ?? source.FileId}");
```

`run.Citations` reste disponible sans lire le flux, avec une observation limitée au texte, ou après saturation du tampon d’observation. Il contient les références du fournisseur collectées pendant le Run, y compris celles des réponses intermédiaires. `service.LastCitations`, ou `GetLastCitations()` via `IAIService`, décrit la requête logique la plus récente ; conservez le Run ou copiez son instantané de citations pour afficher plusieurs réponses.

Pour traiter les événements de sources à leur arrivée, utilisez un seul lecteur d’événements :

```csharp
await using var run = await service.WithWebSearch().StartRunAsync(
    "Recherche et explique les dernières modifications.", cancellationToken: cancellationToken);

await foreach (var item in run.StreamAsync())
{
    if (item.Type == StreamingContentType.Text)
        Console.Write(item.Content);
    else if (item.Type == StreamingContentType.Citation && item.Citation is AICitation source)
        Console.WriteLine($"\nSource : {source.Title} {source.Url ?? source.FileId}");
}
string answer = await run.Result;
```

Les champs d’une citation peuvent être `null` lorsque le fournisseur ne donne pas de valeur. `ResponseId`, `OutputIndex` et `ContentIndex` identifient la réponse d’origine et sa partie de contenu. `StartIndex` et `EndIndex` conservent les décalages locaux et la convention d’indexation du fournisseur ; ce ne sont **pas** des positions dans le `run.Result` concaténé. Ne placez pas les citations en utilisant aveuglément ces valeurs pour indexer la réponse complète.

## Vérifier la prise en charge et la portée des requêtes

| Fournisseur intégré | Niveaux de raisonnement nommés | Modification conservant le cache | Recherche Web | Recherche de fichiers |
| --- | --- | --- | --- | --- |
| OpenAI | Modèles de raisonnement compatibles ; niveaux variables selon le modèle | GPT-6 Astra Standard, mode à un seul agent | Modèles Responses compatibles | Modèles Responses compatibles, magasins vectoriels existants |
| Anthropic | Modèles avec contrôle natif de l’effort | Opus 5 / Fable 5.1 / Mythos 5.1 compatibles, avec la bêta du fournisseur | Modèles Claude compatibles | Aucun adaptateur de magasin natif ; utiliser RAG |
| Google | Niveaux Gemini 3 ; Gemini 2.5 conserve les budgets propres au fournisseur | Non pris en charge | Modèles de texte Gemini compatibles | Modèles de texte Gemini compatibles, magasins de recherche de fichiers existants |
| Autres services | Les réglages propres au fournisseur restent disponibles ; ces options communes nécessitent un adaptateur | Non pris en charge par ces adaptateurs | Aucun adaptateur commun | Aucun adaptateur commun |

Le modèle, le niveau, le transport et les combinaisons sont vérifiés avant l’envoi. En particulier, **la recherche Web et la recherche de fichiers Google ne peuvent pas être combinées dans une même requête**. La bibliothèque ne retire pas silencieusement une fonction, n’abaisse pas un niveau d’effort, n’ignore pas une restriction de domaines et ne bascule pas vers un service de recherche externe. Les outils natifs peuvent coexister avec des fonctions clientes enregistrées lorsque cette combinaison est prise en charge ; les cycles d’outils du Run suivent toujours la politique de fonctions et `WithMaxRounds`.

Les méthodes Fluent conservent le type concret du service et copient leurs options d’entrée. Les composants non nuls sont fusionnés pour la prochaine requête logique, y compris ses cycles d’outils et appels de réparation de sortie structurée, puis consommés. La recherche n’est pas activée pour les appels ultérieurs indépendants ; ajoutez de nouveau `WithWebSearch` ou `WithFileSearch` si nécessaire. Un Run démarré conserve les réglages capturés. Comme pour toute configuration mutable du service, ne modifiez pas les réglages et ne démarrez pas de requêtes simultanées sur le même service pendant une requête en cours.

Les implémentations personnalisées de `IAIService` restent compatibles. Elles adoptent ces fonctions via `IAIRequestFeatureService` ; les méthodes d’aide échouent explicitement sur une implémentation sans cette capacité. Les API existantes de completion, de streaming et de configuration propre au fournisseur restent disponibles. Consultez [Contrôle d’un Run](execution-api-transition.md) pour l’annulation, l’observation et le steering.

Protocoles des fournisseurs : [Modifications du raisonnement OpenAI](https://developers.openai.com/api/docs/guides/reasoning#change-reasoning-mid-conversation), [Outils OpenAI](https://developers.openai.com/api/docs/guides/tools), [Modifications de l’effort Anthropic](https://platform.claude.com/docs/en/build-with-claude/effort#change-effort-mid-conversation), [Recherche Web Anthropic](https://platform.claude.com/docs/en/agents-and-tools/tool-use/web-search-tool), [Sources avec Google Search](https://ai.google.dev/gemini-api/docs/google-search), [Recherche de fichiers Google](https://ai.google.dev/gemini-api/docs/file-search).
