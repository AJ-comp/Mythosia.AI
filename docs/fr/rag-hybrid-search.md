# Recherche hybride

## Pourquoi la recherche hybride ?

La recherche vectorielle pure excelle à capturer le sens sémantique — « annuler mon abonnement » correspond à « résilier mon adhésion » même s'ils ne partagent aucun mot. Mais elle peut rater les **termes exacts** comme les noms de produits, les codes d'erreur ou les identifiants de politique que les utilisateurs saisissent mot pour mot.

La recherche BM25 par mots-clés gère parfaitement ces cas mais échoue sur la compréhension sémantique. **La recherche hybride combine les deux**, offrant le meilleur des deux mondes : compréhension sémantique et correspondance précise par mots-clés.

## Configuration

Combinez la recherche vectorielle dense avec la recherche BM25 par un seul appel de méthode :

```csharp
.WithRag(rag => rag
    .UseHybridSearch(vectorWeight: 0.6f)  // 60% vecteur, 40% BM25
    .AddDocument("base-de-connaissances.txt")
)
```

`vectorWeight` va de 0,0 (BM25 pur) à 1,0 (vecteur pur). Une valeur autour de **0,5–0,7** fonctionne bien dans la plupart des cas.

## Quand utiliser quoi

| Scénario | Poids recommandé |
| --- | --- |
| Questions générales en langage naturel | 0,7–0,8 (plus de vecteur) |
| Documentation technique avec termes précis | 0,4–0,5 (équilibré) |
| Recherche de code ou de code d'erreur | 0,2–0,3 (plus de BM25) |

## Exemple

```csharp
var service = new OpenAIService(apiKey, http)
    .WithRag(rag => rag
        .UseHybridSearch(vectorWeight: 0.5f)
        .AddDocument("catalogue-produits.txt")
        .AddDocument("codes-erreur.txt")
    );

// "ERR-4012" est retrouvé par BM25 ; le contexte sémantique par le vecteur
var answer = await service.GetCompletionAsync("Comment corriger ERR-4012 ?");
```
