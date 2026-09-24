# Choisir l’effort de raisonnement et répondre avec des sources

> Grok 4.7 est un ajout non publié ; consultez [le choix du modèle, le raisonnement et la vitesse](providers.md#grok-47).

> GPT-6 Sol/Luna ne sont pas encore publiés. Voir [choix du modèle et prérequis](providers.md#gpt-6-sol-luna).

[Claude Opus 5.5](providers.md#claude-opus-55) est un ajout non publié : raisonnement toujours actif, effort medium par défaut et affichage omis. Demandez explicitement une progression lisible ; ses valeurs par défaut et règles de liaison diffèrent de Fable 5.1.

Pour des paramètres indépendants et réutilisables, utilisez [le builder de requête](request-building.md). Appelez `CreateRequest(...)` avant `With...`. Les propriétés et méthodes fluent du service conservent leur comportement existant.

> Ces API nécessitent `Mythosia.AI` 7.1.0 ou ultérieur, qui inclut `Mythosia.AI.Abstractions` 3.1.0 ou ultérieur. Les exemples RAG nécessitent `Mythosia.AI.Rag` 7.6.0 ou ultérieur.

> Les exemples `CreateRequest` nécessitent la version de travail actuelle. Le builder n’existe pas dans l’ancienne version 7.1 qui a introduit Run et les options communes. Les anciens packages peuvent conserver les surcharges du service.

[Claude Fable 5.1](fable-5-1.md) ajoute le suivi de progression, les instructions limitées à un tour et le diagnostic des liens du raisonnement à partir de `Mythosia.AI` 8.0.0 / `Mythosia.AI.Abstractions` 4.0.0. Mythos 5.1 nécessite une invitation. Les deux refusent la sélection forcée d’outils.

Pour une requête sensible au temps d’attente, choisissez la [vitesse de traitement](request-building.md#inference-speed). `WithSpeed` conserve modèle et effort, tandis que `Processing` rapporte le mode réellement appliqué. Fast est payant sur les combinaisons compatibles.

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
    .CreateRequest("Esquisse le plan de migration.")
    .WithReasoning(ReasoningLevel.Low)
    .GetCompletionAsync();

string review = await service
    .CreateRequest("Vérifie les scénarios de panne et les étapes de récupération de ce plan.")
    .WithReasoning(ReasoningLevel.High)
    .GetCompletionAsync();
```

Gemini 3.7/3.8 Flash acceptent `Low`, `Medium` et `High` avec `WithReasoning` ; `Minimal`, `None` et `CachePreservation.Required` ne sont pas pris en charge. Les complétions, le streaming, Run, les outils et la recherche native empruntent les voies existantes, avec les restrictions de combinaison de Google. Voir l’[exemple de configuration Google](providers.md#google-googleaiservice).

`ReasoningLevel` exprime le niveau demandé, et non un budget fixe de tokens ou une garantie de qualité. Chaque modèle accepte son propre sous-ensemble. `Auto` conserve le comportement configuré ou par défaut du fournisseur ; il ne remplace pas automatiquement les niveaux non pris en charge. Les propriétés de budget propres au fournisseur restent disponibles pour les modèles qui proposent des budgets de tokens plutôt que des niveaux nommés.

Dans une longue conversation, modifier le paramètre d’effort de premier niveau peut invalider un préfixe de prompt réutilisable. Sur un modèle compatible, exigez le mécanisme du fournisseur permettant de modifier l’effort tout en conservant ce préfixe :

```csharp
string review = await service
    .CreateRequest("Revérifie les hypothèses de la réponse précédente.")
    .WithReasoning(ReasoningLevel.High, cache: CachePreservation.Required)
    .GetCompletionAsync();
```

`Required` définit la façon d’envoyer la modification. Il ne garantit **ni** accès au cache, ni tokens gratuits, ni baisse de latence : les règles d’admissibilité, de conservation et de tarification du cache restent applicables. Les modèles incompatibles lèvent `NotSupportedException` avant l’envoi. Conservez la même conversation suivie, le même modèle et le même endpoint ; ne tronquez pas et ne réordonnez pas une conversation contenant ces mises à jour. Commencez une nouvelle conversation pour changer ces conditions. La compaction automatique est bloquée tant que la conservation du préfixe est requise.

L’effort accepté avec conservation du cache devient l’effort actif de la conversation jusqu’à une nouvelle modification explicite. Un appel ordinaire à `WithReasoning(level)` s’applique à sa requête logique ; il ne remplace pas silencieusement ce réglage persistant. Ce changement intervient **entre les réponses du modèle**. Il ne modifie pas l’effort d’une réponse déjà en cours et est indépendant de `run.SteerAsync`, qui transmet une instruction supplémentaire à un Run actif compatible.

## Répondre aux questions qui exigent des informations récentes

Activez la recherche Web native lorsque la réponse doit s’appuyer sur des informations absentes des données d’entraînement du modèle :

```csharp
string answer = await service
    .CreateRequest("Recherche la dernière annonce de version et cite la source.")
    .WithWebSearch()
    .GetCompletionAsync();

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
    .CreateRequest("Recherche dans nos documents de politique. Quel est le délai de résiliation ?")
    .WithFileSearch(documents)
    .GetCompletionAsync();

foreach (AICitation source in service.GetLastCitations())
    Console.WriteLine($"{source.Title}: {source.FileId ?? source.Url}");
```

Avec un service Google, utilisez `new FileSearchStore("Google", "fileSearchStores/your-existing-store")`. Les magasins appartiennent à leur fournisseur, compte et déploiement ; un identifiant de magasin OpenAI ne peut pas être transmis à Google. Créez le magasin et importez ou indexez ses documents avec l’API ou la console du fournisseur avant de l’utiliser ici. Cette API recherche uniquement dans des magasins existants et n’importe pas de fichiers locaux.

`CreateRequest(...).With...` conserve les options dans un builder indépendant. Le réutiliser applique ces options à chaque exécution et à ses tours d’outils. Les anciens `service.WithReasoning`, `service.WithWebSearch` et `service.WithFileSearch` renvoient toujours le type concret du service et consomment leurs options lors de la prochaine requête logique. Ils restent utilisables avec `IAIRequestFeatureService` et les wrappers RAG. Aucune de ces API ne garantit des exécutions simultanées sur un service.

## Afficher la progression et conserver les sources

Utilisez les mêmes options avant `StartRunAsync`. Le callback de texte peut actualiser l’écran pendant que le Run conserve les sources de la réponse terminée :

```csharp
await using var run = await service
    .CreateRequest("Recherche les annonces récentes et compare les changements.")
    .WithReasoning(ReasoningLevel.High)
    .WithWebSearch()
    .StartRunAsync(
        onText: text => Console.Write(text),
        cancellationToken: cancellationToken);

string answer = (await run.Result).Text;
foreach (AICitation source in run.Citations)
    Console.WriteLine($"{source.Title}: {source.Url ?? source.FileId}");
```

`run.Citations` reste disponible sans lire le flux, avec une observation limitée au texte, ou après saturation du tampon d’observation. Il contient les références du fournisseur collectées pendant le Run, y compris celles des réponses intermédiaires. `service.LastCitations`, ou `GetLastCitations()` via `IAIService`, décrit la requête logique la plus récente ; conservez le Run ou copiez son instantané de citations pour afficher plusieurs réponses.

Pour traiter les événements de sources à leur arrivée, utilisez un seul lecteur d’événements :

```csharp
await using var run = await service
    .CreateRequest("Recherche et explique les dernières modifications.")
    .WithWebSearch()
    .StartRunAsync(cancellationToken: cancellationToken);

await foreach (var item in run.StreamAsync())
{
    if (item.Type == StreamingContentType.Text)
        Console.Write(item.Content);
    else if (item.Type == StreamingContentType.Citation && item.Citation is AICitation source)
        Console.WriteLine($"\nSource : {source.Title} {source.Url ?? source.FileId}");
}
string answer = (await run.Result).Text;
```

Les champs d’une citation peuvent être `null` lorsque le fournisseur ne donne pas de valeur. `ResponseId`, `OutputIndex` et `ContentIndex` identifient la réponse d’origine et sa partie de contenu. `StartIndex` et `EndIndex` conservent les décalages locaux et la convention d’indexation du fournisseur ; ce ne sont **pas** des positions dans le `(await run.Result).Text` concaténé. Ne placez pas les citations en utilisant aveuglément ces valeurs pour indexer la réponse complète.

## Vérifier la prise en charge et la portée des requêtes

| Fournisseur intégré | Niveaux de raisonnement nommés | Modification conservant le cache | Recherche Web | Recherche de fichiers |
| --- | --- | --- | --- | --- |
| OpenAI | Modèles de raisonnement compatibles ; niveaux variables selon le modèle | GPT-6 Astra / Sol / Luna Standard, mode à un seul agent | Modèles Responses compatibles | Modèles Responses compatibles, magasins vectoriels existants |
| Anthropic | Modèles avec contrôle natif de l’effort | Opus 5 / 5.5 / Fable 5.1 / Mythos 5.1 compatibles, avec la bêta du fournisseur | Modèles Claude compatibles | Aucun adaptateur de magasin natif ; utiliser RAG |
| Google | Niveaux Gemini 3 ; Gemini 2.5 conserve les budgets propres au fournisseur | Non pris en charge | Modèles de texte Gemini compatibles | Modèles de texte Gemini compatibles, magasins de recherche de fichiers existants |
| xAI | Grok 4.6 : `Auto`, `Low`, `Medium`, `High`, `XHigh` | Non pris en charge | Aucun adaptateur commun | Aucun adaptateur commun |
| DeepSeek | Flash / V4 Pro : `Auto`, `None`, `Minimal`/`Low`, `Medium`/`High`/`XHigh`, `Max` ; correspondances natives Low/High/Max | Non pris en charge | Aucun adaptateur commun | Aucun adaptateur commun |
| Perplexity | `Auto` ou `Minimal`/`Low`/`Medium`/`High`/`XHigh`/`Max` selon le modèle ; aucun effort explicite pour Sonar | Non pris en charge | Agent `web_search` | Aucun adaptateur commun |
| Autres services | Les réglages propres au fournisseur restent disponibles ; ces options communes nécessitent un adaptateur | Non pris en charge par ces adaptateurs | Aucun adaptateur commun | Aucun adaptateur commun |

L’adaptateur vérifie avant l’envoi les contraintes de modèle, de niveau, de transport et de combinaison connues localement ; le fournisseur valide les règles propres au modèle qui ne sont pas connues localement. En particulier, **la recherche Web et la recherche de fichiers Google ne peuvent pas être combinées dans une même requête**. La bibliothèque ne retire pas silencieusement une fonction, n’abaisse pas un niveau d’effort, n’ignore pas une restriction de domaines et ne bascule pas vers un service de recherche externe. Les outils natifs peuvent coexister avec des fonctions clientes enregistrées lorsque cette combinaison est prise en charge ; les cycles d’outils du Run suivent toujours la politique de fonctions et `WithMaxRounds`.

`CreateRequest(...).With...` conserve les options dans un builder indépendant. Le réutiliser applique ces options à chaque exécution et à ses tours d’outils. Les anciens `service.WithReasoning`, `service.WithWebSearch` et `service.WithFileSearch` renvoient toujours le type concret du service et consomment leurs options lors de la prochaine requête logique. Ils restent utilisables avec `IAIRequestFeatureService` et les wrappers RAG. Aucune de ces API ne garantit des exécutions simultanées sur un service.

Les implémentations personnalisées de `IAIService` restent compatibles. Elles adoptent ces fonctions via `IAIRequestFeatureService` ; les méthodes d’aide échouent explicitement sur une implémentation sans cette capacité. Les API existantes de completion, de streaming et de configuration propre au fournisseur restent disponibles. Consultez [Contrôle d’un Run](execution-api-transition.md) pour l’annulation, l’observation et le steering.

Protocoles des fournisseurs : [Modifications du raisonnement OpenAI](https://developers.openai.com/api/docs/guides/reasoning#change-reasoning-mid-conversation), [Outils OpenAI](https://developers.openai.com/api/docs/guides/tools), [Modifications de l’effort Anthropic](https://platform.claude.com/docs/en/build-with-claude/effort#change-effort-mid-conversation), [Recherche Web Anthropic](https://platform.claude.com/docs/en/agents-and-tools/tool-use/web-search-tool), [Sources avec Google Search](https://ai.google.dev/gemini-api/docs/google-search), [Recherche de fichiers Google](https://ai.google.dev/gemini-api/docs/file-search).

Avec Perplexity, le modèle réellement sélectionné détermine les niveaux d’effort et le serveur peut rejeter une combinaison incompatible. `None` n’est pas pris en charge. La recherche Web par défaut et les outils des presets/profils sont des réglages persistants du fournisseur ; les options communes ne les désactivent pas.

Perplexity: [Perplexity Agent API, recherche et embeddings](perplexity.md).
