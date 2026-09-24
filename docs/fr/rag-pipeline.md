# Personnalisation du pipeline RAG

<a id="indexing-validation"></a>

## Protéger les documents existants en cas d’échec d’indexation

Un découpeur personnalisé ou une réponse d’embedding incorrecte ne doit pas remplacer silencieusement un document consultable par un contenu incomplet ou mal associé. Le pipeline vérifie chaque document avant de commencer sa persistance, y compris avec `onDocumentEmbedded`.

Avant l’embedding, le stockage ou le callback de persistance, un `RagDocument.Id` null, vide ou composé uniquement d’espaces provoque une `ArgumentException`. Un résultat de découpage invalide provoque une `InvalidOperationException` : liste ou fragment null, `Content` ou `Metadata` null, ID de fragment vide ou composé d’espaces, ou ID répétés dans un même document. La détection des doublons utilise `StringComparer.Ordinal`, sensible à la casse. Les valeurs des fragments et leurs métadonnées sont copiées avant le premier appel d’embedding.

Les ID personnalisés valides sont conservés tels quels, sans génération automatique, suppression d’espaces ni réparation. Les collisions entre ID de fragments de documents différents ne sont pas détectées globalement. Utilisez des ID uniques dans la collection cible, comme dans l’[exemple de découpeur personnalisé](text-splitters.md). La clé réservée `document_id` est normalisée uniquement dans la copie destinée au stockage ; les métadonnées sources restent intactes.

Les ID invalides, les échecs de découpage et les lots d’embeddings invalides préservent les enregistrements existants de ce document et ne déclenchent pas le callback de persistance. Tous ses lots doivent réussir la [validation des embeddings](rag-embedding.md#embedding-validation) avant le stockage. Cela n’annule pas les documents déjà traités auparavant dans la même opération ; après le début du stockage, le rollback dépend du store ou du callback.

Ces vérifications et corrections de l’ordre des réponses ne restaurent pas automatiquement le contenu déjà écrasé ni les vecteurs stockés associés aux mauvais fragments ; réindexez les documents concernés à partir de leurs sources originales.

<a id="custom-persistence"></a>

## Remplacer tout le document dans le callback de persistance

Lorsqu’un document raccourcit, un simple upsert des nouveaux fragments laisse son ancienne fin dans les résultats. `onDocumentEmbedded` remplace entièrement la persistance par défaut : utilisez le `document_id` normalisé des enregistrements pour remplacer tout le document. Le callback reçoit un document validé et non vide à la fois :

```csharp
using Mythosia.AI.Rag;
using Mythosia.VectorDb;

var store = await RagStore.BuildAsync(config => config
    .AddDocuments("./docs/")
    .UseOpenAIEmbedding(apiKey)
    .UseStore(vectorStore),
    onDocumentEmbedded: async records =>
    {
        var documentId = records[0].Metadata["document_id"];
        await vectorStore.ReplaceByFilterAsync(
            new VectorFilter().Where("document_id", documentId), records, cancellationToken);
    },
    cancellationToken: cancellationToken);
```

Une découpe réussie produisant zéro fragment n’appelle ni ce callback ni le stockage par défaut. Supprimez explicitement cet ID connu dans votre stockage ; réservez `DeleteDocumentAsync` au stockage de la pipeline. L’atomicité du remplacement et le rollback dépendent du stockage ou du callback.

<a id="url-documents"></a>

## Lire les documents URL en toute sécurité

Un serveur peut compresser un document texte pour le transfert. `AddUrl` décompresse `gzip`, `deflate` et Brotli (`br`) avant de lire le texte et vérifie que le flux compressé est complet. Un transfert HTTP réussi ne suffit pas : des données compressées tronquées, une erreur de décompression ou une somme de contrôle invalide lorsque le format en fournit une interrompent le chargement avant l’embedding ou l’enregistrement, en conservant les anciens enregistrements du document. Les valeurs `Content-Encoding` non prises en charge ou multicouches sont également rejetées avant l’embedding ou l’enregistrement.

Pour cesser d’attendre un document URL lent, transmettez `cancellationToken` à `RagStore.BuildAsync`. Le jeton atteint la requête HTTP, la lecture du corps et la décompression. L’annulation est coopérative et n’annule pas les écritures de documents déjà terminées.

<a id="custom-retriever"></a>

## Brancher un moteur sans embeddings obligatoires

Un code produit bénéficie de la recherche par mots-clés, une question formulée autrement de la recherche sémantique. Le moteur choisi prépare seulement la représentation nécessaire ; la recherche lexicale n’impose plus d’embedding préalable.

- Avant : chaque stratégie recevait un embedding de requête.
- Après : le moteur prépare seulement la représentation nécessaire.

Implémentez `IRagRetriever` pour un index externe ou une autre représentation. `RagRetrievalRequest` transporte `Query` (requête sémantique complète), `TextQuery` nullable (remplacement lexical), `TopK`, `Filter` et `ProgressAsync`. Les moteurs intégrés utilisent `Query` si `TextQuery` est null ; une chaîne vide ignore la branche textuelle. Le moteur personnalisé doit préparer la requête et respecter filtre, limite et annulation.

```csharp
using Mythosia.AI.Rag;
using Mythosia.VectorDb;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

public sealed class CatalogRetriever : IRagRetriever
{
    private readonly ITextSearchStore _catalog;
    public CatalogRetriever(ITextSearchStore catalog) => _catalog = catalog;

    public Task<IReadOnlyList<VectorSearchResult>> RetrieveAsync(
        RagRetrievalRequest request,
        CancellationToken cancellationToken = default)
        => _catalog.TextSearchAsync(
            request.TextQuery ?? request.Query,
            request.TopK,
            request.Filter,
            cancellationToken);
}
```

```csharp
// catalog: an existing ITextSearchStore
var service = new OpenAIService(apiKey, http)
    .WithRag(rag => rag.UseRetriever(new CatalogRetriever(catalog)));
```

Enregistrez-le avec `UseRetriever(...)` ou `RagPipeline.SetRetriever(...)`. `IRetrievalStrategy` et `SetRetrievalStrategy(...)` restent disponibles via un adaptateur créant toujours l’embedding de requête. Les résultats doivent contenir texte et métadonnées pour le reclassement et le contexte.

```csharp
store.UpdateRetriever(new CatalogRetriever(catalog));
store.UseKeywordSearch();
store.UpdateRetrievalStrategy(new HybridSearchOptions { VectorWeight = 0.7f });
store.UpdateRetriever(null); // UseVectorSearch
```

`UseKeywordSearch()` ignore les embeddings de requête. L’import découpe et vectorise toujours les documents pour le stockage existant ; ce n’est pas une indexation purement textuelle. L’initialisation différée peut encore appeler les embeddings de documents lors de la première question.

L’étape de requête `Embedding` dépend désormais du moteur ; la recherche lexicale ne la signale pas. Un moteur personnalisé peut signaler ses étapes via `request.ProgressAsync`. Les embeddings des documents ne changent pas.

## Pourquoi personnaliser le pipeline ?

Le pipeline RAG par défaut fonctionne bien tel quel, mais les projets réels ont souvent besoin de plus de contrôle :

- **Débogage** — quelle étape est lente ? Le réécriveur modifie-t-il la requête de façon inattendue ?
- **Prompt engineering** — le template de prompt par défaut peut ne pas convenir au ton ou aux contraintes de votre domaine
- **Architecture** — plusieurs services partageant un même index économisent de la mémoire et assurent la cohérence des embeddings
- **Inspection** — parfois il faut voir ce que la récupération retourne *avant* de l'envoyer au LLM

Ce chapitre couvre les outils qui vous donnent ce contrôle.

Un pipeline personnalisé peut être associé à un run pour afficher la progression et permettre l’annulation pendant la réponse. Le [guide Run](execution-api-transition.md) précise quand la recherche a lieu et ce que changent les instructions supplémentaires.

## Suivi de la progression

Suivez quelle étape RAG s'exécute via un callback asynchrone par requête :

```csharp
var options = new RagQueryOptions
{
    ProgressAsync = async stage =>
    {
        Console.WriteLine($"[RAG] {stage}");
        // Étapes : QueryRewrite, Filtering, Embedding, Retrieval, Reranking, ContextBuild
    }
};

var response = await ragService.GetCompletionAsync("Votre question", options);
```

Indispensable pour le profilage de la latence — vous pouvez mesurer le temps entre les étapes pour trouver les goulots d'étranglement.

## Template de prompt personnalisé

Contrôlez comment le contexte récupéré est injecté dans le prompt avec les espaces réservés `{context}` et `{question}` :

```csharp
.WithRag(rag => rag
    .WithPromptTemplate("""
        Utilisez uniquement les informations suivantes pour répondre à la question.
        Si la réponse n'est pas dans le contexte, dites "Je ne sais pas."

        Contexte :
        {context}

        Question : {question}
        """)
    .AddDocument("faq.txt")
)
```

Un template bien rédigé peut réduire considérablement les hallucinations en demandant au modèle de rester dans le contexte fourni.

## Partager un RagStore

Construisez l'index une seule fois et réutilisez-le entre plusieurs instances de service — utile pour comparer des fournisseurs ou faire des tests A/B :

```csharp
// Construire une fois
RagStore store = await RagStore.BuildAsync(rag => rag
    .UseOpenAIEmbedding(apiKey)
    .AddDocuments("docs/"));

// Réutiliser entre les services
var claudeRag = new AnthropicService(apiKey, http).WithRag(store);
var gptRag    = new OpenAIService(apiKey, http).WithRag(store);
```

Les deux services partagent les mêmes embeddings et le même index vectoriel — aucune duplication de stockage ou de calcul.

## Requête directe au RagStore

Interrogez le store indépendamment de tout service IA pour inspecter ce qui serait récupéré :

```csharp
RagProcessedQuery result = await store.QueryAsync("Quelle est la politique de retour ?");

Console.WriteLine($"Requête réécrite : {result.RewrittenQuery}");

foreach (var ref_ in result.References)
{
    Console.WriteLine($"[{ref_.Score:F2}] {ref_.Record.Content[..100]}");
}
```

`result.RequestMessageContent` contient le prompt entièrement assemblé qui serait envoyé au LLM. Extrêmement utile pour déboguer la qualité de la récupération sans dépenser de tokens LLM.

## Fonctionnement interne

Lorsque vous appelez `.WithRag()`, un `RagEnabledService` est créé en coulisse. Ce wrapper enveloppe votre AIService et relie automatiquement le pipeline RAG à l'appel au LLM. La pièce maîtresse de ce mécanisme est [AIRequestContext](request-contexts.md).

### Le flux complet

```
ragService.GetCompletionAsync("Quelle est la politique de retour ?")
    ↓
① RagEnabledService exécute le pipeline RAG
   Réécriture de requête → Filtrage → Embedding (si nécessaire) → Récupération → Assemblage du contexte
    ↓
② TemplateContextBuilder remplace {context} et {question}
   → "Répondez en vous basant sur les informations suivantes.\n[1] Retours sous 30 jours...\nQuestion : Quelle est la politique de retour ?"
    ↓
③ RagEnabledService crée un AIRequestContext
   RequestMessageOverride = prompt assemblé
    ↓
④ _innerService.GetCompletionAsync(message original, context: context) est appelé
   → AIService stocke le context dans AsyncLocal
   → La question originale est ajoutée à l'historique de conversation
    ↓
⑤ AIService.GetLatestMessages() remplace l’entrée initiale de la requête en cours
   Historique : "Quelle est la politique de retour ?" (original conservé)
   Ce que le modèle voit : prompt assemblé (RequestMessageOverride)
```

### Pourquoi cette architecture ?

L'idée centrale est la **séparation entre l'historique de conversation et l'entrée du modèle** :

- **L'historique conserve la question originale** — les questions de suivi comme « et dans ce cas ? » gardent ainsi leur contexte
- **Le modèle reçoit le prompt assemblé** — contenant les documents récupérés et la question
- **L'état de l'AIService n'est jamais modifié** — `AsyncLocal<T>` assure une isolation par requête

C'est exactement le cas d'usage concret de `RequestMessageOverride`, décrit dans la [documentation AIRequestContext](request-contexts.md). Le pipeline RAG exploite ce mécanisme automatiquement : il vous suffit d'appeler `.WithRag()`.

### Dans le code

Voici le code clé à l'intérieur de `RagEnabledService`, là où pipeline et appel LLM se rejoignent :

```csharp
var processed = await RewriteAndProcessAsync(query, options, cancellationToken);
var original = new Message(ActorRole.User, query);
return await _innerService.GetCompletionAsync(
    original,
    context: BuildRequestContext(processed, original),
    cancellationToken: cancellationToken);

private static AIRequestContext BuildRequestContext(RagProcessedQuery processed, Message original)
{
    var requestMessage = original.Clone();
    requestMessage.Content = processed.RequestMessageContent;
    if (original.HasMultimodalContent)
    {
        requestMessage.Contents = new List<MessageContent>
        {
            new TextContent(processed.RequestMessageContent)
        };
        requestMessage.Contents.AddRange(
            original.Contents.Where(content => !(content is TextContent)));
    }
    return new AIRequestContext { RequestMessageOverride = requestMessage };
}
```

`AIService` conserve le contexte dans `AsyncLocal`. `GetLatestMessages()` applique `RequestMessageOverride` à l’entrée initiale de la requête logique en cours et préserve les appels d’outils de l’assistant ainsi que leurs résultats ultérieurs. Les documents trouvés et les résultats des outils sont ainsi transmis ensemble dans les requêtes suivantes au modèle. À la fin, le contexte précédent est restauré.
