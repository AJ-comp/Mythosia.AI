# Réécriture de requête

> 📍 **Pipeline questions-réponses :** **`Réécriture de requête`** → Filtrage → Embedding (si nécessaire) → [Recherche](rag-hybrid-search.md) → [Re-ranking](rag-reranking.md) → Construction du contexte

L’étape de requête `Embedding` dépend désormais du moteur ; la recherche lexicale ne la signale pas. Un moteur personnalisé peut signaler ses étapes via `request.ProgressAsync`. Les embeddings des documents ne changent pas.

## Pourquoi réécrire les requêtes ?

Dans une conversation multi-tours, les utilisateurs utilisent naturellement des pronoms et des références courtes :

> Utilisateur : « Parle-moi de la politique de remboursement. »
> Utilisateur : « Et les exceptions à **ça** ? »

Si « Et les exceptions à ça ? » est envoyé tel quel au stockage vectoriel, l'embedding n'a aucune idée de ce que « ça » désigne. La recherche retourne des résultats non pertinents, et la réponse s'en trouve dégradée.

**La réécriture de requête** résout ces références avant la récupération, en développant « ça » en « les exceptions à la politique de remboursement » pour que l'embedding capture l'intention complète. Elle implémente aussi un **filtre de recherche** — si la requête ne nécessite pas de récupération (ex. « Merci ! »), la recherche vectorielle est entièrement ignorée, économisant latence et coût.

## Configuration

Un `LlmQueryRewriter` utilise le service IA lui-même pour réécrire la requête avant l'embedding :

```csharp
.WithRag(rag => rag
    .WithQueryRewriter(250)          // Utilise le même service IA
    .AddDocument("docs.txt")
)
```

Le réécriveur examine le contexte de la conversation et produit une requête de recherche autonome que le stockage vectoriel peut comprendre sans l'historique.

## RAG multi-tours

Lors d'une requête directe au `RagStore`, passez l'historique de conversation pour que le réécriveur puisse résoudre les références :

```csharp
var history = new List<ConversationTurn>
{
    new ConversationTurn("Quelle est la politique de remboursement ?", "Vous pouvez retourner les articles dans les 30 jours."),
    new ConversationTurn("Et pour les produits numériques ?", "Les produits numériques ne sont pas remboursables.")
};

var result = await store.QueryAsync(
    query: "Y a-t-il des exceptions à ça ?",
    conversationHistory: history
);
```

Le réécriveur voit l'historique complet et reformule « Y a-t-il des exceptions à ça ? » en quelque chose comme « exceptions à la politique de non-remboursement des produits numériques », produisant des résultats de récupération bien meilleurs.

<a id="runtime-query-rewriter"></a>

## Modifier la réécriture pendant le traitement des requêtes

> `Mythosia.AI.Rag` 8.1.1 ou ultérieur est nécessaire pour appliquer les changements à l’exécution aux wrappers `WithRag(store)` existants. [Notes du correctif](../../src/rag/Mythosia.AI.Rag/RELEASE_NOTES.md#v811).

Pour ajouter ou remplacer un réécrivain sans reconstruire l’index, utilisez `store.SetQueryRewriter(rewriter)` ; utilisez `store.SetQueryRewriter(null)` pour désactiver la réécriture et l’extraction des termes de recherche. La surcharge de `RagStore.QueryAsync` acceptant `conversationHistory` et les wrappers déjà connectés par `service.WithRag(store)` sélectionnent tous le réécrivain actuel du stockage pour chaque requête. Une requête conserve l’instance choisie pendant l’attente d’une notification de progression ou de la réécriture. L’ajout, le remplacement ou la suppression s’applique aux requêtes suivantes, y compris la recherche, la génération de réponses, le streaming et les runs via ces wrappers.

```csharp
var rag = service.WithRag(store);
store.SetQueryRewriter(rewriter);
var rewritten = await rag.RetrieveAsync("Quelles sont les exceptions à la politique de remboursement ?");

store.SetQueryRewriter(null);
var original = await rag.RetrieveAsync("Quelles sont les exceptions à la politique de remboursement ?");
```

Le `LlmQueryRewriter` par défaut activé par `WithQueryRewriter()` est créé une seule fois lors de l’initialisation différée ; sa suppression ne le recrée pas à la requête suivante. Les surcharges du stockage sans `conversationHistory` continuent de contourner la réécriture, tout comme Agentic RAG, où l’agent formule la requête de recherche.

## Fonctionnement du filtre de recherche

Tous les messages utilisateur ne nécessitent pas une recherche dans les documents. Le réécriveur classifie la requête et retourne une réécriture vide pour des messages comme :

- « Merci ! »
- « Compris, c'est utile. »
- « Peux-tu résumer ce que tu viens de dire ? »

Quand le filtre se déclenche, tout le pipeline de récupération est ignoré — pas d'embedding, pas de recherche vectorielle, pas de re-ranking — et le LLM répond directement depuis le contexte de conversation.
