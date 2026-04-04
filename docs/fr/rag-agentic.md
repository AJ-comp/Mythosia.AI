# RAG agentique

## Pourquoi le RAG agentique ?

Dans le RAG standard, chaque message utilisateur déclenche exactement **une** récupération. Le système cherche, construit le contexte et génère une réponse — quoi qu'il arrive. Ça fonctionne bien pour les questions simples, mais atteint ses limites quand :

- La question nécessite **plusieurs recherches** sur des sujets différents (ex. « Compare la politique de remboursement pour le matériel et les logiciels »)
- Le premier résultat de recherche est **insuffisant** et le système devrait affiner et réessayer
- Certaines questions **n'ont pas besoin de récupération** (ex. « Résume notre conversation jusqu'ici »)
- La réponse dépend de la **combinaison de documents et de données en direct** provenant d'API

Le RAG agentique résout tout cela. Au lieu d'un pipeline fixe récupérer-puis-répondre, **l'agent décide de façon autonome** — quand chercher, quoi chercher, s'il faut chercher à nouveau, et quand appeler d'autres outils — le tout dans une boucle ReAct.

## Démarrage rapide

Enregistrez le `RagStore` comme outil avec `WithAgenticRag`, puis lancez `RunAgentAsync` :

```csharp
// Construire l'index une fois
var ragStore = await RagStore.BuildAsync(cfg => cfg
    .AddDocument("manuel.pdf")
    .AddDocument("politique.docx")
    .UseOpenAIEmbedding(apiKey));

// Enregistrer le RAG comme outil et lancer l'agent
var service = new AnthropicService(apiKey, http);
service.WithAgenticRag(ragStore);

var answer = await service.RunAgentAsync("Résume la politique de remboursement.");
```

L'agent appelle `search_documents` automatiquement dès qu'il a besoin de contexte documentaire, puis synthétise la réponse finale à partir des extraits récupérés.

## Combiner avec d'autres outils

Le RAG agentique brille quand il est combiné avec des outils supplémentaires — l'agent sélectionne le bon outil pour chaque sous-tâche :

```csharp
var service = new AnthropicService(apiKey, http);

service.WithAgenticRag(ragStore)
       .WithFunctionAsync("get_order_status", "Cherche le statut d'une commande par son identifiant.",
           ("order_id", "L'identifiant de commande à chercher.", required: true),
           async id => await orderApi.GetStatusAsync(id));

// L'agent cherche dans les documents la politique ET appelle l'API pour les données de commande en direct
var answer = await service.RunAgentAsync(
    "Commande #12345 — suis-je éligible à un remboursement selon la politique actuelle ?");
```

Dans cet exemple, l'agent procède de façon autonome :

1. Cherche dans les documents la politique de remboursement
2. Appelle l'API de commande pour obtenir le statut de la commande #12345
3. Combine les deux informations pour produire une réponse finale

## Description d'outil personnalisée

La description de l'outil contrôle quand l'agent décide d'invoquer le RAG. Adaptez-la à votre domaine pour une sélection d'outil plus précise :

```csharp
service.WithAgenticRag(ragStore,
    toolDescription:
        "Recherche dans les politiques RH internes, les manuels produits et les documents de conformité. " +
        "Utilisez cet outil chaque fois que des informations de politique interne ou de produit sont nécessaires.");
```

Une description vague comme « Rechercher des documents » peut amener l'agent à appeler le RAG trop souvent ou pas assez. Soyez précis sur **quel type d'information** contiennent les documents.

## Différences avec le RAG standard

| | RAG standard | RAG agentique |
| --- | --- | --- |
| Moment de la recherche | Chaque message | L'agent décide |
| Formulation de la requête | QueryRewriter | L'agent lui-même |
| Nombre de recherches | Une par tour | Une ou plusieurs selon les besoins |
| Combinaison d'outils | Non applicable | Tout outil enregistré |
| Configuration | `.WithRag()` | `.WithAgenticRag()` + `RunAgentAsync` |

> **Note :** `QueryRewriter` est intentionnellement contourné dans le RAG agentique. L'agent formule lui-même sa requête de recherche autonome, donc une étape de réécriture séparée serait redondante et pourrait déformer l'intention de l'agent.

## Quand choisir lequel

- **RAG standard** — chaque question est documentaire, mono-thématique, et vous voulez une latence minimale
- **RAG agentique** — les questions couvrent plusieurs sujets, nécessitent de combiner documents et données en direct, ou demandent une récupération itérative
