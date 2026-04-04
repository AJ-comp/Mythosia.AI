# Découpeurs de texte

Les découpeurs de texte divisent les documents en morceaux avant leur transformation en embeddings. La taille des morceaux et leur chevauchement influencent considérablement la qualité de la récupération.

## Découpeurs disponibles

### CharacterTextSplitter

Découpe selon le nombre de caractères. Simple et rapide, mais peut couper en plein milieu d'une phrase :

```csharp
.WithTextSplitter(new CharacterTextSplitter(500, 50))
```

### RecursiveTextSplitter (recommandé par défaut)

Essaie de découper sur des frontières sémantiquement cohérentes dans cet ordre : paragraphes → phrases → mots → caractères. Produit des morceaux plus cohérents :

```csharp
.WithTextSplitter(new RecursiveTextSplitter(500, 50))
```

### TokenTextSplitter

Découpe selon le nombre de tokens plutôt que de caractères. Plus précis pour la gestion du budget de la fenêtre de contexte du LLM :

```csharp
.WithTextSplitter(new TokenTextSplitter(256, 32))
```

À utiliser quand le modèle d'embedding a des limites strictes de tokens.

### MarkdownTextSplitter

Préserve la structure Markdown — découpe sur les titres, listes et blocs de code avant de revenir au découpage par caractères :

```csharp
.WithTextSplitter(new MarkdownTextSplitter(500, 50))
```

Idéal pour les fichiers de documentation, les README et tout contenu Markdown structuré.

## Choisir les paramètres

| Paramètre | Effet |
|-----------|--------|
| `chunkSize` (plus grand) | Plus de contexte par morceau, moins de morceaux, embeddings moins coûteux |
| `chunkSize` (plus petit) | Récupération plus précise, plus de morceaux, plus d'embeddings |
| `chunkOverlap` | Évite la perte d'information aux frontières des morceaux |

Un bon point de départ : `chunkSize: 500, chunkOverlap: 50`.

## Découpeur par document

Différents découpeurs peuvent être appliqués par document dans le `RagBuilder` :

```csharp
.WithRag(rag => rag
    .AddDocuments(new PlainTextDocumentLoader(), "readme.md", new MarkdownTextSplitter(600, 60))
    .AddDocuments(new PlainTextDocumentLoader(), "donnees.txt", new RecursiveTextSplitter(300, 30))
    .WithTextSplitter(new RecursiveTextSplitter(500, 50))  // par défaut pour le reste
)
```

## Découpeur personnalisé

Implémentez `ITextSplitter` pour une logique de découpage entièrement sur mesure :

```csharp
public class PhraseSplitter : ITextSplitter
{
    public IReadOnlyList<RagChunk> Split(RagDocument document)
    {
        var sentences = document.Content.Split(". ");
        return sentences.Select((s, i) => new RagChunk
        {
            Content = s,
            Index = i,
            DocumentId = document.Id
        }).ToList();
    }
}

// Enregistrer :
.WithTextSplitter(new PhraseSplitter())
```
