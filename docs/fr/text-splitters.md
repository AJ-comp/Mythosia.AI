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

Un découpeur qui comprend et préserve la structure Markdown. Il reconnaît la hiérarchie des titres (H1–H6), les blocs de code et les tableaux pour découper le contenu en unités sémantiques :

```csharp
.WithTextSplitter(new MarkdownTextSplitter(500, 50))
```

Idéal pour les fichiers de documentation, les README et les sorties de chargeurs de documents structurés comme Office et HWP.

> [!TIP]
> Les chargeurs Word, Excel, PowerPoint et HWP convertissent les documents en Markdown en interne. Utiliser `MarkdownTextSplitter` avec ces documents garantit que les structures de tableaux et de blocs de code sont préservées durant le découpage.

#### Qualité du découpage des tableaux

`MarkdownTextSplitter` découpe les tableaux Markdown au **niveau des lignes**. Il ne coupe jamais une ligne en deux, et chaque morceau résultant inclut automatiquement **la ligne d’en-tête et le séparateur** :

```
Tableau original :
| Nom    | Dépt.   | Salaire  |
|--------|---------|----------|
| Alice  | Dév     | 45 000 € |
| Bob    | PM      | 42 000 € |
| Carol  | Design  | 40 000 € |

→ Morceau 1 :
| Nom    | Dépt.   | Salaire  |
|--------|---------|----------|
| Alice  | Dév     | 45 000 € |
| Bob    | PM      | 42 000 € |

→ Morceau 2 :
| Nom    | Dépt.   | Salaire  |
|--------|---------|----------|
| Carol  | Design  | 40 000 € |
```

Chaque morceau est un tableau autonome et valide, garantissant la qualité des embeddings et de la recherche.

#### Protection des blocs de code

Les blocs délimités par des barrières de code (`` ``` ``) sont traités comme des **unités atomiques**. Un bloc de code n’est jamais scindé, même s’il dépasse la taille du morceau, préservant la sémantique du code.

#### Fil d’Ariane des titres

Chaque morceau est automatiquement préfixé par le chemin de titres menant à son contenu, enrichissant le contexte pour la recherche vectorielle :

```
# Manuel produit
## Guide d’installation
### Windows

(contenu réel de cette section)
```

Cette fonctionnalité est contrôlée par la propriété `IncludeHeadingBreadcrumb` (par défaut : `true`).

## Choisir les paramètres

| Paramètre | Effet |
|-----------|--------|
| `chunkSize` (plus grand) | Plus de contexte par morceau, moins de morceaux, embeddings moins coûteux |
| `chunkSize` (plus petit) | Récupération plus précise, plus de morceaux, plus d'embeddings |
| `chunkOverlap` | Évite la perte d'information aux frontières des morceaux |

Un bon point de départ : `chunkSize: 500, chunkOverlap: 50`.

## Taille des morceaux et nombre de tokens (multilingue)

`chunkSize` est mesuré en **caractères**, mais les limites des modèles d’embedding sont en **tokens**. Le même nombre de caractères peut générer des nombres de tokens très différents selon la langue :

| Langue | 1 000 caractères ≈ tokens | chunkSize recommandé |
|--------|------------------------|-----------------------|
| Anglais | ~250 tokens | 500–2 000 |
| Coréen / Japonais / Chinois | ~800–1 500 tokens | 300–1 000 |

> [!WARNING]
> Le texte CJK (coréen, japonais, chinois) a un ratio tokens/caractère bien plus élevé que l’anglais. Si les morceaux dépassent la limite de tokens du modèle d’embedding (ex. : 2 048 tokens), une erreur se produira. Réduisez généreusement `chunkSize` pour les documents CJK.

Par exemple, avec un modèle d’embedding limité à 2 048 tokens :

```csharp
// Documents anglais : 2000 caractères ≈ 500 tokens → dans la limite
.WithTextSplitter(new MarkdownTextSplitter(2000, 200))

// Documents coréens : 1000 caractères ≈ 1000 tokens → plage sûre
.WithTextSplitter(new MarkdownTextSplitter(1000, 200))
```

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

Si vous souhaitez écrire un module de découpage personnalisé et le brancher, implémentez `ITextSplitter` :

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
