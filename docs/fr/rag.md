# RAG (Retrieval-Augmented Generation)

Pour un résultat final et un bouton Arrêter, passez `cancellationToken` à `GetCompletionAsync`. Utilisez Run pour les événements de progression ou les instructions supplémentaires prises en charge. Voir [l’annulation](completions.md#completion-cancellation).

Pour une réponse enrichie par recherche, passez aussi `cancellationToken` à `RagEnabledService.GetCompletionAsync`. Il atteint la recherche, `LlmQueryRewriter`, `LlmReranker` et la complétion interne ; une annulation pendant la recherche empêche l’appel suivant au modèle. `RagPipeline.QueryAndGenerateAsync` transmet également son jeton. Chaque composant doit coopérer ; les recherches ou actions d’outils terminées ne sont pas annulées rétroactivement.

Le RAG permet au modèle de répondre à des questions à partir de vos propres documents, en récupérant les passages pertinents au moment de la requête.

Pour permettre à l’utilisateur de suivre ou d’arrêter la rédaction d’une réponse fondée sur ses documents, combinez la recherche RAG avec un run. Le [guide Run](execution-api-transition.md) explique le déroulement et les limites des instructions supplémentaires.


Avec une référence `IAIService`, utilisez `GetLastProcessing()` de `Mythosia.AI.Extensions`. Il lit l’interface facultative `IAIProcessingInfoService` et retourne une liste vide sans diagnostics disponibles. `IAIService` ne reçoit aucun membre obligatoire. En RAG, `RagEnabledService.WithSpeed(...)` configure la prochaine réponse après recherche, décrite par `LastProcessing`. La reformulation interne reste séparée ; les résultats Run exposent les mêmes `Processing`. [WithSpeed](request-building.md#inference-speed)

## Installation

```bash
dotnet add package Mythosia.AI.Rag
```

## Démarrage rapide

Utilisez `.WithRag()` sur n'importe quel `IAIService` pour activer le RAG avec une API fluente :

```csharp
using Mythosia.AI.Rag;

var service = new AnthropicService(apiKey, http)
    .WithRag(rag => rag
        .AddDocument("manuel.txt")
        .AddDocument("politique.txt")
    );

var response = await service.GetCompletionAsync("Quelle est la politique de remboursement ?");
```

Les documents sont automatiquement découpés, transformés en embeddings et stockés. Au moment de la requête, les passages les plus pertinents sont récupérés et injectés dans le prompt.

La préversion optionnelle `Mythosia.AI.Rag.Search.Pixie` permet de comparer la recherche neuronale creuse locale avec la recherche existante. Elle conserve votre fournisseur d’embeddings denses et utilise un index PIXIE en mémoire, sans migrer les stockages persistants ni remplacer le mode par défaut. [Guide PIXIE et comparaison (anglais)](rag-hybrid-search.md#pixie-search).

<a id="rag-message-attachments"></a>

## Répondre sur une pièce jointe à partir de vos documents

Pour expliquer une photo de produit à partir de votre manuel, transmettez un `Message` contenant la question et l’image à `RagEnabledService.GetCompletionAsync(Message)` ou `StartRunAsync(Message)`. Les deux préservent les pièces jointes non textuelles dans la requête au service d’IA interne. La recherche utilise le texte du message ; les pièces jointes elles-mêmes ne sont pas automatiquement indexées ni converties en embeddings. Le fournisseur et le modèle choisis doivent prendre en charge leur type. Le contexte trouvé est ajouté uniquement à la requête envoyée : il ne remplace ni le `Message` d’origine ni le texte de l’utilisateur dans l’historique de conversation.

Si une réponse nécessite à la fois un manuel et le stock actuel, associez RAG à vos outils enregistrés. Pendant les appels d’outils de `GetCompletionAsync`, le contexte trouvé reste sur l’entrée initiale et chaque résultat d’outil ultérieur est envoyé au modèle sans modification. L’historique conserve la saisie originale de l’utilisateur.

<a id="retrieval-modes"></a>

## Choisir comment rechercher les documents

Un code produit bénéficie de la recherche par mots-clés, une question formulée autrement de la recherche sémantique. Le moteur choisi prépare seulement la représentation nécessaire ; la recherche lexicale n’impose plus d’embedding préalable.

```csharp
RagStore store = await RagStore.BuildAsync(rag => rag
    .AddDocument("manual.txt")
    .UseKeywordSearch());

RagProcessedQuery result = await store.QueryAsync("refund policy");
```

`UseKeywordSearch()` ignore les embeddings de requête. L’import découpe et vectorise toujours les documents pour le stockage existant ; ce n’est pas une indexation purement textuelle. L’initialisation différée peut encore appeler les embeddings de documents lors de la première question.

Voir [modes et stockages compatibles](rag-hybrid-search.md) et [moteurs personnalisés](rag-pipeline.md#custom-retriever).

## Ajouter des documents

Plusieurs types de sources sont pris en charge :

```csharp
.WithRag(rag => rag
    .AddDocument("readme.txt")                    // fichier local
    .AddUrl("https://example.com/doc.txt")        // URL
    .AddText("Du contenu inline peut aussi aller ici.")  // chaîne brute
)
```

`AddUrl` valide et décompresse les formats HTTP pris en charge avant de lire le texte et rejette les encodages incomplets, non pris en charge ou multicouches. Voir [décompression des URL et annulation](rag-pipeline.md#url-documents).

<a id="document-identity"></a>

### Distinguer les fichiers de même nom

Deux entreprises peuvent chacune fournir un `docs/faq.txt`. Les deux documents doivent rester dans l'index, tandis qu'un nouvel enregistrement du même fichier doit réutiliser son identité :

```csharp
var store = await RagStore.BuildAsync(rag => rag
    .AddDocuments("company-a/docs")
    .AddDocuments("company-b/docs"));
```

Dans le flux de stockage RAG par défaut, l'ID du document est créé avant l'envoi des enregistrements au stockage vectoriel, puis les enregistrements portant le même `document_id` sont remplacés. Notre stockage PostgreSQL (pgvector) utilise cet ID ; il n'examine pas lui-même le chemin du fichier d'origine. Auparavant, l'enregistrement d'un répertoire envoyait `faq.txt` aussi bien pour `company-a/docs/faq.txt` que pour `company-b/docs/faq.txt`. Le second document remplaçait donc le premier. La correction conserve le chemin complet lors de la création de l'ID ; le schéma PostgreSQL reste inchangé. Un filtre `full_path` dans un exemple de stockage utilise des métadonnées fournies par l'appelant ; il ne génère pas automatiquement des IDs uniques de documents ou d'enregistrements.

Les chargeurs intégrés `PlainTextDocumentLoader` et `DirectoryDocumentLoader` utilisent le chemin absolu du fichier, normalisé par `Path.GetFullPath`, comme `Source` et ID automatique du document. Les fichiers de répertoires différents ont donc des IDs différents. Les chemins relatifs, absolus et contenant `./` réutilisent l'ID lorsqu'ils aboutissent au même chemin absolu, casse comprise. Gardez un répertoire de travail constant pour les chemins relatifs. Le déplacement des fichiers, les liens symboliques ou physiques et les différences de casse ne garantissent pas la conservation de l'ID.

`AddText(..., id: ...)`, un `RagDocument.Id` défini explicitement et les règles de `Source` des chargeurs personnalisés restent inchangés. Aucun changement de l'API d'appel n'est nécessaire. Comme le `Source` de ces chargeurs intégrés est désormais absolu, les citations par défaut peuvent aussi afficher un chemin absolu. Pour l'affichage, utilisez `filename` ou les métadonnées `relative_path` du chargeur de répertoires par défaut. La surcharge de répertoire avec configuration n'ajoute pas automatiquement `relative_path`.

**Index existants :** Les anciens IDs relatifs ne sont ni supprimés ni migrés automatiquement. Privilégiez une réindexation complète dans une nouvelle collection, vérifiez-la, puis basculez l'application. Si vous réutilisez une collection, supprimez uniquement les anciens IDs dont vous avez confirmé l'appartenance, puis réindexez leurs fichiers sources. Ne supprimez pas globalement les documents selon leur nom de fichier : d'autres répertoires peuvent contenir des documents de même nom.

Pour limiter les mises à jour et suppressions au bon document, `document_id` est une clé réservée du pipeline. Avant la persistance, chaque enregistrement reçoit le véritable `RagDocument.Id`, même si les métadonnées d’entrée indiquent une autre valeur. Les dictionnaires de métadonnées du document d’entrée et du découpeur ne sont pas modifiés ; les callbacks de persistance personnalisés reçoivent eux aussi les enregistrements normalisés. Utilisez une autre clé pour un identifiant propre à l’application.

Cela ne répare pas les enregistrements déjà stockés avec un `document_id` incorrect. Reconstruisez une nouvelle collection depuis des sources fiables, ou identifiez et nettoyez uniquement les enregistrements concernés avant de les réindexer. Réindexer sous le bon ID ne permet pas à lui seul de retrouver de façon fiable les anciens enregistrements stockés sous un autre ID.

Un même fichier enregistré par un chemin relatif ou absolu doit mettre à jour un seul document, tandis que les fichiers homonymes de dossiers différents doivent rester distincts. `WordDocumentLoader`, `ExcelDocumentLoader`, `PowerPointDocumentLoader` et `PdfDocumentLoader` définissent désormais `DoclingDocument.Source` avec le chemin absolu normalisé, comme les chargeurs TXT intégrés. RAG en dérive les ID automatiques ; les ID explicites restent sous votre contrôle. Les citations par défaut peuvent donc afficher un chemin absolu.

[Conserver une identité stable pour chaque fichier](document-loaders.md#file-source-identity).

<a id="empty-document-updates"></a>

### Vider un document sans conserver ses anciens résultats

Si vous videz une politique de remboursement obsolète puis réindexez le même document, son ancien texte ne doit plus apparaître dans les réponses. Avec le stockage RAG par défaut, un découpage réussi produisant zéro fragment remplace les enregistrements de ce `document_id` par un ensemble vide. Aucun embedding n’est demandé et les autres ID restent inchangés. Cela concerne les documents vides ou composés d’espaces lorsque leur découpeur produit zéro fragment, ainsi que les découpeurs personnalisés qui renvoient correctement zéro fragment.

Pour une `RagPipeline` déjà configurée nommée `pipeline`, réutilisez l’ID du document stocké :

```csharp
await pipeline.IndexDocumentAsync(
    new RagDocument { Id = "refund-policy", Content = "" },
    cancellationToken);
```

Vous pouvez ensuite réindexer du contenu non vide avec le même ID. Un chargeur qui ne renvoie aucun document, ou un document absent d’une liste de fichiers ultérieure, n’est pas une instruction de suppression : aucun ID à remplacer n’a été fourni.

Les exceptions de chargement, d’analyse ou de découpage, ainsi qu’une annulation constatée avant l’appel au stockage, préservent les enregistrements de ce document. Les chargeurs et analyseurs doivent signaler leurs échecs par des exceptions ; un résultat réussi de zéro fragment ne se distingue pas d’un effacement volontaire. Une fois le stockage commencé, le rollback en cas d’échec ou d’annulation dépend du stockage ; PostgreSQL utilise une transaction pour le remplacement. Un lot traite chaque document séparément et ne restaure pas les documents déjà terminés.

**Persistance personnalisée :** avec `onDocumentEmbedded`, la persistance reste à la charge du callback. Zéro fragment n’entraîne ni son appel ni un accès au stockage par défaut. L’application doit supprimer explicitement l’ID connu dans son propre stockage, ou utiliser `DeleteDocumentAsync` pour celui de la pipeline.

## Fournisseur d'embeddings personnalisé

Par défaut, le RAG utilise le fournisseur local d'embeddings intégré. Pour utiliser un modèle d'embedding dédié :

```csharp
using Mythosia.AI.Rag.Embeddings;

var embedder = new OpenAIEmbeddingProvider(apiKey, http, "text-embedding-3-small");

var service = new AnthropicService(apiKey, http)
    .WithRag(rag => rag
        .UseEmbedding(embedder)
        .AddDocument("base-de-connaissances.txt")
    );
```

## Stockage vectoriel personnalisé

Par défaut, un stockage en mémoire est utilisé. Pour la production, branchez un stockage vectoriel persistant :

```csharp
dotnet add package Mythosia.VectorDb.Postgres
```

```csharp
using Mythosia.VectorDb.Postgres;

var store = new PostgresStore(new PostgresOptions
{
    ConnectionString = connectionString,
    Dimension = 1536
});

var service = new OpenAIService(apiKey, http)
    .WithRag(rag => rag
        .UseStore(store)
        .AddDocument("grand-corpus.txt")
    );
```

## Options de requête

Affinez le comportement de récupération par requête :

```csharp
var options = new RagQueryOptions
{
    FinalFilter = new RagFilter
    {
        TopK = 5,              // nombre de passages à récupérer
        MinScore = 0.7         // score de similarité minimum
    }
};

var response = await service.GetCompletionAsync("Votre question", options: options);
```

Si votre index est déjà hébergé par le fournisseur du modèle, comparez RAG avec [la recherche native de fichiers et les options communes de raisonnement](reasoning-and-search.md).

## Prochaines étapes

- [Recherche hybride](rag-hybrid-search.md) — recherche sémantique et par mots-clés simultanément
- [Réécriture de requêtes](rag-query-rewriting.md) — optimisation des requêtes avec le contexte conversationnel
- [Re-classement](rag-reranking.md) — améliorer la précision des résultats de recherche
- [Personnalisation du pipeline](rag-pipeline.md) — contrôle fin du processus RAG
- [RAG agentique](rag-agentic.md) — l'IA décide quand et quoi chercher
- [Stockages vectoriels](vectordb-overview.md) — configuration du stockage persistant
- [Découpeurs de texte](text-splitters.md) — personnaliser la segmentation des documents

Perplexity: [Utiliser des vecteurs dans votre index / Rechercher sans produire de réponse](perplexity.md).
