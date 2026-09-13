# Perplexity : réponses sourcées, recherche et embeddings

Utilisez Perplexity quand une réponse doit tenir compte d'informations récentes et fournir des sources vérifiables. `PerplexityService` appelle l'Agent API ; la recherche et les embeddings indépendants permettent de construire la récupération de documents autour du modèle de réponse de votre choix.

## Choisir d'abord le travail à effectuer

Produire une réponse actuelle, obtenir des pages web et calculer les vecteurs d'un index documentaire sont des tâches distinctes. Choisissez le composant responsable du travail, sans appeler un modèle de réponse à chaque recherche.

| Besoin | Composant |
| --- | --- |
| Une réponse documentée avec ses sources | `PerplexityService` |
| Des pages classées pour une interface ou un autre modèle | `PerplexitySearchClient` |
| Des vecteurs de passages indépendants pour le RAG | `PerplexityEmbeddingProvider` |
| Des vecteurs tenant compte des passages voisins d'un document | `PerplexityContextualizedEmbeddingProvider` |

Installez `Mythosia.AI`, et aussi `Mythosia.AI.Rag` pour les exemples d'embeddings. Fournissez une clé API et un `HttpClient` géré par l'application. Les exemples utilisent vos variables `apiKey`, `httpClient` et `cancellationToken`.

## Répondre avec un préréglage Agent

Un préréglage associe un modèle, des instructions, des outils, un effort et des budgets entretenus par le fournisseur. Utilisez `Fast` pour une vérification rapide, `Low` pour la recherche courante, `Medium` pour une comparaison en plusieurs étapes et `High` / `XHigh` pour approfondir. `WideResearch` convient aux recherches étendues ; privilégiez l’exécution en arrière-plan pour les tâches longues. Ce sont des préréglages, pas des identifiants de modèles.

```csharp
using Mythosia.AI.Models.Perplexity;
using Mythosia.AI.Services.Perplexity;

var service = new PerplexityService(apiKey, httpClient)
    .WithPerplexityOptions(new PerplexityAgentOptions
    {
        Preset = PerplexityPreset.Low
    });

await using var run = await service.StartRunAsync(
    "Compare les méthodes récentes de recyclage des batteries et cite les sources.",
    onText: text => Console.Write(text),
    cancellationToken: cancellationToken);
string answer = (await run.Result).Text;
foreach (var source in run.Citations)
    Console.WriteLine(source.Url);
```

Utilisez `GetCompletionAsync` pour le résultat seul, `StreamAsync` du service pour le streaming existant, ou `StartRunAsync` pour observer et annuler une exécution. `(await run.Result).Text` concatène le texte émis. `run.Citations` et `LastCitations` conservent les sources même sans lecture des événements de citation. Les événements de raisonnement ne contiennent que les informations exposées par le fournisseur, selon le modèle.

`AIRunResult.RequestedModel` est le modèle unique explicitement envoyé dans la requête, capturé au départ, y compris une surcharge de modèle du fournisseur. Il vaut `null` si un preset, un profil ou le routage côté serveur choisit le modèle sans champ de modèle unique explicite (par exemple, une liste Perplexity `Models`). Cette valeur est indépendante du modèle réel de réponse dans `Model`.

## Configurer la recherche et les outils

`WithPerplexityOptions(...)` définit des options persistantes, copiées pour chaque requête logique. Les options communes `WithReasoning(...)` et `WithWebSearch(...)` concernent la prochaine requête logique, ses appels de fonctions et ses réparations de sortie typée. La réécriture interne des requêtes RAG n'hérite pas de la recherche destinée à la réponse finale.

`UsePreset(...)` sélectionne directement un préréglage. Un préréglage/profil choisit son modèle ; `ModelOverride` le remplace explicitement. `DisableWebSearch` ne retire que l'outil par défaut de l'adaptateur, sans désactiver la recherche intégrée d'un préréglage. Selon le modèle, les efforts sont `Minimal`, `Low`, `Medium`, `High`, `XHigh`, `Max`. `None` est refusé, comme l'effort explicite avec Sonar direct. `DisableReasoning` interne utilise un effort faible disponible ou omet l'option, sans garantir l'arrêt du raisonnement.

| Option | Utilisation |
| --- | --- |
| `Preset` / `ModelOverride` | Choisir une configuration de recherche ou remplacer explicitement son modèle par un identifiant fournisseur/modèle. |
| `MaxSteps` | Limiter la boucle hébergée ; zéro conserve la valeur du fournisseur. Distinct de `WithMaxRounds`, qui limite les continuations des fonctions locales. |
| `ReasoningEffort` | Ajuster le raisonnement. `Auto` omet la surcharge ; les niveaux acceptés dépendent du modèle réel. |
| `DisableWebSearch` / `Tools` | Configurer l'outil web par défaut de l'adaptateur et les outils hébergés explicites. |
| `Models` | Indiquer un à cinq modèles de repli, par priorité. Cette liste remplace le modèle unique ; chaque candidat doit accepter les fonctions demandées. |
| `Profile` | Utiliser une configuration enregistrée, éventuellement à version fixe. Incompatible avec `Preset`. |
| `ServiceTier` | Demander le traitement par défaut, flex ou prioritaire. Le fournisseur peut ignorer un niveau non pris en charge. |
| `Skills` | Fournir des compétences intégrées, inline ou personnalisées déjà téléversées. Les ressources personnalisées appartiennent au compte Perplexity. |
| `LanguagePreference` / `PromptCacheKey` | Indiquer une langue ou un indice de routage du cache. Aucun succès du cache n'est garanti. |
| `PreviousResponseId` / `Store` | Continuer une réponse terminée ou contrôler sa visibilité à la lecture. Avec une continuation, utilisez `StatelessMode` et seulement le nouveau tour. `Store = false` ne désactive pas la persistance côté fournisseur. |

`PerplexityHostedTool` accepte un `Type` pris en charge et des `Parameters` JSON documentés : `web_search`, `fetch_url`, `finance_search`, `people_search`, `sandbox` ou `mcp`. Les serveurs MCP et connecteurs gérés s'exécutent via le fournisseur ; leurs identifiants, autorisations et ressources de compte doivent correspondre à la connexion. Enregistrez les fonctions applicatives avec `Functions` / le constructeur de fonctions. Les étapes hébergées et les gestionnaires locaux ont des responsables d'exécution distincts.

Les fabriques `PerplexityHostedTools.WebSearch`, `FetchUrl`, `Sandbox`, `FinanceSearch`, `PeopleSearch`, `Mcp` et `Connector` créent les outils. Les appels MCP n'attendent pas d'approbation ; limitez `allowedTools` si nécessaire. Connector est une préversion du fournisseur et référence une intégration déjà connectée.

```csharp
service.WithPerplexityOptions(new PerplexityAgentOptions
{
    Preset = PerplexityPreset.Low,
    MaxSteps = 8,
    Tools = new List<PerplexityHostedTool>
    {
        PerplexityHostedTools.WebSearch(),
        PerplexityHostedTools.FetchUrl()
    }
});
string comparison = await service.GetCompletionAsync(
    "Lis la documentation du projet et compare les capacités pertinentes.");
```

La compatibilité des outils, du raisonnement, des images et des schémas dépend du modèle. `WithFileSearch` n'est pas un adaptateur de magasin vectoriel Perplexity. Les fichiers produits par le sandbox, les pièces jointes et les données MCP restent des ressources distinctes, sans devenir un magasin commun de recherche de fichiers.

## Sources, images et réponses structurées

Utilisez une complétion ou un streaming typé pour obtenir des champs JSON. L'adaptateur envoie un schéma natif et conserve le mécanisme de réparation. Il garde les éléments de réponse et identifiants d'outils pour la continuation ; évitez de supprimer ou réordonner l'historique du protocole. `Message` et `ImageContent` acceptent des octets JPEG/PNG/WebP/GIF ou une URL HTTPS selon le modèle. Il s'agit d'images en entrée, pas de génération d'images.

Les traces brutes restent dans les métadonnées de l’historique, mais les requêtes suivantes ne rejouent que les entrées autorisées `message`, `function_call` et `function_call_output` ; utilisez `PreviousResponseId` pour poursuivre l’état hébergé complet côté fournisseur.

Les citations peuvent désigner des pages web ou d'autres sources du fournisseur. Les positions sont locales à une réponse et à une partie de contenu, pas au résultat Run concaténé. Conservez URL et titre pour l'affichage et la vérification ; une source retournée ne valide pas automatiquement toutes les affirmations.

## Poursuivre une tâche longue

L'exécution en arrière-plan du fournisseur permet à une recherche de continuer après une déconnexion temporaire ou d'être retrouvée par ID. Un `AIRun` local contrôle l'exécution cliente ; une réponse en arrière-plan a son propre cycle serveur. Arrêter la lecture du flux termine seulement l'observation. Annulez explicitement le travail distant pour l'arrêter.

```csharp
var researcher = new PerplexityService(apiKey, httpClient)
    .UsePreset(PerplexityPreset.High);
var job = await researcher.StartBackgroundAsync(
    "Compare les méthodes récentes de recyclage des batteries et cite les sources.", cancellationToken: cancellationToken);
Console.WriteLine(job.Id);
var completed = await job.WaitForCompletionAsync(
    cancellationToken: cancellationToken);
if (completed.Status != "completed")
    throw new InvalidOperationException(completed.Error ?? completed.Status);
Console.WriteLine(completed.Text);
```

`StartBackgroundAsync` capture l'entrée sans ajouter d'historique et refuse les fonctions locales actives ou `Store = false`. `GetResponseAsync` interroge une fois ; `WaitForCompletionAsync` attend un état terminal par interrogation périodique. Conservez `Id` et `LastSequenceNumber`, puis utilisez `ResumeBackgroundRun(id).StreamAsync(startingAfter: cursor)`. `CancelAsync` annule le travail distant ; annuler un token de lecture/requête arrête seulement cette opération cliente. `LastResponse` contient texte, état, usage, citations et `OutputJson`. Vérifiez l'état terminal avant d'utiliser la réponse.

Les fichiers du sandbox se lisent avec `ListFilesAsync` et `DownloadFileAsync(fileId)`. Le service propose aussi `GetAgentResponseAsync`, `GetResponseFilesAsync` et `GetResponseFileContentAsync`. Ils récupèrent des résultats de réponse, sans créer ni rechercher de magasin vectoriel.

Pour les compétences Office intégrées, utilisez le parcours en arrière-plan de ce guide : `StartBackgroundAsync`, puis `WaitForCompletionAsync` / `GetResponseAsync` et les méthodes de fichiers. Les traces d’outils internes de ces réponses peuvent être impossibles à distinguer des appels de fonctions locales ordinaires.

Soumettre, récupérer, annuler ou reconnecter un flux en arrière-plan n'active ni `SteerAsync` en cours de réponse ni les outils clients asynchrones natifs. La reconnexion reprend l'observation d'une réponse existante sans soumettre à nouveau la tâche. Conservez l'ID de réponse et le curseur du fournisseur.

## Rechercher sans produire de réponse

`PerplexitySearchClient` fournit des pages pour votre classement, votre interface ou un autre LLM. Il n'appelle pas de modèle de réponse et ne modifie pas l'historique de `PerplexityService`.

```csharp
var search = new PerplexitySearchClient(apiKey, httpClient);
var pages = await search.SearchAsync(
    "méthodes de recyclage des batteries",
    new PerplexitySearchOptions
    {
        MaxResults = 5,
        DomainFilter = new[] { "nature.com", "science.org" },
        ContentSize = PerplexitySearchContentSize.Medium
    }, cancellationToken);
foreach (var page in pages.Results)
    Console.WriteLine($"{page.Rank}: {page.Title} — {page.Url}");
```

`SearchAsync` accepte une ou plusieurs requêtes. Les options couvrent Web/People, pays, domaines, langues, dates de publication/mise à jour et récence. Choisissez `ContentSize` ou les limites explicites `MaxTokens` / `MaxTokensPerPage`, jamais les deux. Les résultats contiennent rang, titre, URL, extrait et dates du fournisseur. Le rang est l'ordre retourné, pas un score de pertinence.

`ContentSize` est réservé à la recherche Web. Omettez-le pour People : le client rejette cette combinaison avant l’envoi.

## Utiliser des vecteurs dans votre index

Les embeddings standard traitent chaque passage indépendamment et implémentent `IEmbeddingProvider`, donc s'intègrent au constructeur RAG. Les embeddings contextuels préservent l'ordre des passages et les groupes documentaires. Leur API distincte évite d'aplatir des documents sans rapport.

| Constante | ID fournisseur | Dimensions par défaut |
| --- | --- | --- |
| `Standard0_6B` | `pplx-embed-v1-0.6b` | 1024 |
| `Standard4B` | `pplx-embed-v1-4b` | 2560 |
| `Context0_6B` | `pplx-embed-context-v1-0.6b` | 1024 |
| `Context4B` | `pplx-embed-context-v1-4b` | 2560 |

```csharp
using Mythosia.AI.Rag;
using Mythosia.AI.Rag.Embeddings;

var rag = service.WithRag(builder => builder
    .AddText("Les retours sont acceptés pendant 30 jours.", id: "returns")
    .UsePerplexityEmbedding(apiKey, httpClient));
string policy = await rag.GetCompletionAsync("Pendant combien de temps puis-je retourner un achat ?");
```

```csharp
var contextual = new PerplexityContextualizedEmbeddingProvider(
    apiKey, httpClient, PerplexityEmbeddingModels.Context0_6B);
var documents = new[]
{
    new[] { "Les retours sont acceptés pendant 30 jours.", "Conservez votre reçu pour demander un remboursement." },
    new[] { "La livraison standard prend trois jours.", "La livraison express est disponible en semaine." }
};
var documentVectors = await contextual.GetDocumentEmbeddingsAsync(
    documents, cancellationToken);
float[] queryVector = await contextual.GetQueryEmbeddingAsync(
    "Pendant combien de temps puis-je retourner un achat ?", cancellationToken);
```

Utilisez le même modèle, les mêmes dimensions et le même encodage pour les documents et les requêtes. `GetQueryEmbeddingAsync` transmet une requête comme document individuel au même modèle contextuel. Les résultats gardent l'ordre des documents et des passages, sans connexion automatique au constructeur RAG à entrée plate.

Les API float décodent les vecteurs base64 signed-int8 et les normalisent pour la similarité. Les API binaires explicites retournent des bits compactés avec distance de Hamming, jamais des coordonnées float implicites. Les dimensions complètes sont 1024 pour 0.6B et 2560 pour 4B ; les dimensions réduites suivent les limites du fournisseur. Les limites de lots, longueur, tokens totaux et débit du compte s'appliquent.

Méthodes binaires : `GetBinaryEmbeddingAsync` / `GetBinaryEmbeddingsAsync`, et `GetBinaryDocumentEmbeddingsAsync` / `GetBinaryQueryEmbeddingAsync` pour le contexte. `PerplexityBinaryEmbedding` expose `Dimensions`, une copie via `ToArray()` et `HammingDistance` ; une distance faible indique davantage de similarité. Les dimensions binaires sont multiples de huit. Maximum : 512 textes standard, ou 512 documents et 16 000 fragments contextuels. Le fournisseur vérifie 32K tokens par texte/document et 120K au total.

## Migrer le code Sonar existant

Cette version retire volontairement l'ancien adaptateur avant l'arrêt annoncé des endpoints Sonar le 27 septembre 2026. `PerplexityService` appelle `/v1/agent` ; `AIModels.Perplexity.Sonar` signifie désormais `perplexity/sonar`. Les anciennes méthodes de recherche et réponses propres à Sonar sont supprimées. Utilisez les API communes completion/Run/citations, les préréglages Agent et `PerplexitySearchClient` pour la recherche indépendante.

Correspondance recommandée : Sonar → `Fast`, Sonar Pro → `Low`, Sonar Reasoning Pro → `Medium`, Sonar Deep Research → `High`. Cette migration ne garantit ni texte, ni coût, ni comportement identiques. Les préréglages dynamiques évoluent ; utilisez un modèle explicite ou un profil versionné si vous devez fixer ce choix.

L'adaptateur ne prend pas en charge le steering natif, les outils clients asynchrones natifs ni `CachePreservation.Required`. Router/Gateway est hors périmètre. La disponibilité dépend du fournisseur, du modèle et du compte ; ce guide ne prétend pas que chaque combinaison a passé un test réel payant.

Les profils, compétences personnalisées et connecteurs nécessitent des ressources déjà enregistrées dans le compte. Les formats de requête sont couverts par des tests unitaires ; aucun appel réel réussi avec ces ressources n’a été vérifié.

[Agent API](https://docs.perplexity.ai/docs/agent-api/quickstart) · [Search API](https://docs.perplexity.ai/docs/search/quickstart) · [Embeddings](https://docs.perplexity.ai/docs/embeddings/quickstart) · [Migration](https://docs.perplexity.ai/docs/agent-api/migrate-from-sonar/how-to) · [2026-09-27](https://community.perplexity.ai/t/sonar-is-moving-to-the-agent-api/5802)
