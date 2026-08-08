# Réécriture de requête

> 📍 **Pipeline questions-réponses :** **`Réécriture de requête`** → Embedding → Filtrage → [Recherche](rag-hybrid-search.md) → [Re-ranking](rag-reranking.md) → Construction du contexte

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

## Fonctionnement du filtre de recherche

Tous les messages utilisateur ne nécessitent pas une recherche dans les documents. Le réécriveur classifie la requête et retourne une réécriture vide pour des messages comme :

- « Merci ! »
- « Compris, c'est utile. »
- « Peux-tu résumer ce que tu viens de dire ? »

Quand le filtre se déclenche, tout le pipeline de récupération est ignoré — pas d'embedding, pas de recherche vectorielle, pas de re-ranking — et le LLM répond directement depuis le contexte de conversation.
