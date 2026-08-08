# Personnalisation du pipeline RAG

## Pourquoi personnaliser le pipeline ?

Le pipeline RAG par défaut fonctionne bien tel quel, mais les projets réels ont souvent besoin de plus de contrôle :

- **Débogage** — quelle étape est lente ? Le réécriveur modifie-t-il la requête de façon inattendue ?
- **Prompt engineering** — le template de prompt par défaut peut ne pas convenir au ton ou aux contraintes de votre domaine
- **Architecture** — plusieurs services partageant un même index économisent de la mémoire et assurent la cohérence des embeddings
- **Inspection** — parfois il faut voir ce que la récupération retourne *avant* de l'envoyer au LLM

Ce chapitre couvre les outils qui vous donnent ce contrôle.

## Suivi de la progression

Suivez quelle étape RAG s'exécute via un callback asynchrone par requête :

```csharp
var options = new RagQueryOptions
{
    ProgressAsync = async stage =>
    {
        Console.WriteLine($"[RAG] {stage}");
        // Étapes : QueryRewrite, Embedding, Filtering, Retrieval, Reranking, ContextBuild
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
   Réécriture de requête → Embedding → Récupération → Assemblage du contexte
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
⑤ AIService.GetLatestMessages() remplace le dernier message
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
// À l'intérieur de RagEnabledService.GetCompletionAsync
var processed = await RewriteAndProcessAsync(query, options, cancellationToken);
return await _innerService.GetCompletionAsync(
    new Message(ActorRole.User, query),         // ← question originale (sauvegardée dans l'historique)
    context: BuildRequestContext(processed));    // ← prompt assemblé (seul le modèle le voit)

// BuildRequestContext — crée l'AIRequestContext
private static AIRequestContext BuildRequestContext(RagProcessedQuery processed)
{
    return new AIRequestContext
    {
        RequestMessageOverride = new Message(
            ActorRole.User,
            processed.RequestMessageContent)  // ← résultat du TemplateContextBuilder
    };
}
```

`AIService` stocke ce context dans `AsyncLocal`, puis `GetLatestMessages()` remplace le dernier message par le `RequestMessageOverride`. Une fois la requête terminée, l'état est automatiquement restauré — la requête suivante n'est en rien affectée.
