# Découpeurs de texte

Un résultat de recherche doit garder assez de contexte pour répondre, sans intégrer tout le document comme une seule unité. Le découpage règle ce compromis. Ces découpeurs utilisent des règles locales, sans modèle d’IA. Choisissez selon la structure du document, puis évaluez la recherche avec vos questions.

## Découpeurs disponibles

### CharacterTextSplitter

Pour du texte brut lorsqu’une limite de taille simple suffit. Le séparateur configuré est privilégié, mais une phrase peut être coupée. `RagBuilder` utilise par défaut `CharacterTextSplitter(300, 30)` ; l’extension `.md` ne sélectionne pas automatiquement le découpeur Markdown.

```csharp
.WithTextSplitter(new CharacterTextSplitter(500, 50))
```

### RecursiveTextSplitter (recommandé par défaut)

Pour du texte dont les paragraphes et les mots doivent rester ensemble si possible. L’ordre par défaut est ligne vide → saut de ligne → `. ` → espace → caractères individuels. Ce sont des règles textuelles, sans jugement sémantique d’un modèle ; le point suivi d’un espace n’est qu’une approximation de fin de phrase.

Les doublons dans `Separators` sont appliqués une seule fois, dans l’ordre de leur première occurrence. Répéter un séparateur n’ajoute pas de passage de découpage. Les longues listes sont traitées sans empiler des appels récursifs.

```csharp
.WithTextSplitter(new RecursiveTextSplitter(500, 50))
```

### TokenTextSplitter

À utiliser pour compter approximativement des mots séparés par des espaces. Malgré leur nom, `MaxTokensPerChunk` et `TokenOverlap` comptent les unités de `TokenSeparators` (espaces, tabulations et sauts de ligne par défaut), pas les tokens du modèle. La sortie normalise ces séparateurs en espaces. Un texte sans espaces peut rester une longue unité ; ce découpeur ne garantit pas la limite de tokens du modèle.

```csharp
.WithTextSplitter(new TokenTextSplitter(256, 32))
```

### MarkdownTextSplitter

Pour les documents Markdown ou le Markdown produit par les chargeurs Office/HWP, quand il faut conserver titres, lignes de tableau et blocs de code. Reconnaît les titres ATX (`#`–`######`), les blocs délimités et les tableaux. Ce découpeur à règles n’est pas un analyseur complet d’arbre syntaxique Markdown. Le constructeur accepte uniquement `chunkSize` ; aucun paramètre de chevauchement Markdown n’existe.

```csharp
.WithTextSplitter(new MarkdownTextSplitter(500))
```

#### Qualité du découpage des tableaux

Les tableaux GFM reconnus sont divisés entre les lignes ; l’en-tête et la ligne de séparation sont répétés dans chaque fragment de tableau. Les barres extérieures sont facultatives (`Name | Value` est accepté). Les noms des colonnes sont conservés ; la qualité de recherche dépend toujours des documents, embeddings et questions.

Une cellule en gras appartient à sa ligne : par exemple, `**Aucun remboursement**` pour l’entreprise A ne doit pas devenir une condition pour B. Les cellules et les blocs de code ne deviennent jamais des libellés de texte répétés. Une ligne autonome `**libellé**` n’est reconnue qu’au début d’un paragraphe ou d’un bloc de texte, après une ligne vide ou une frontière structurelle. Une ligne en gras au milieu d’un paragraphe avec retours à la ligne ne crée ni nouveau paragraphe ni libellé répété. Un libellé reconnu peut se répéter dans les fragments de son bloc ; un tableau, une clôture de code, un titre ou le prochain libellé reconnu termine sa portée.

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

#### Protection des blocs de code

Les blocs délimités par des accents graves ou des tildes restent entiers. Le délimiteur fermant utilise le même caractère et doit être au moins aussi long que l’ouvrant ; un délimiteur plus court dans le bloc ne le ferme pas. Le bloc entier peut dépasser `ChunkSize`.

L’indentation de la clôture d’ouverture est conservée avec le code, afin que le découpage ne change pas l’indentation du code rendu. Les informations de cette clôture sont analysées une fois par bloc, sans reparcourir une longue clôture pour chaque ligne de contenu.

#### Fil d’Ariane des titres

`IncludeHeadingBreadcrumb` vaut `true` par défaut : chaque fragment répète son chemin de titres pour garder le contexte du passage trouvé. `false` désactive cette répétition tout en conservant les titres d’origine. Les sections contenant seulement un titre sont aussi conservées.

`MinSplitHeadingLevel` accepte 1–6 pour choisir les niveaux de titre qui ouvrent une section ; la valeur par défaut est 1. Lorsqu’un titre parent change, la sous-section précédente se termine afin que son ancien chemin de titres ne soit pas appliqué au nouveau contenu.

```csharp
.WithTextSplitter(new MarkdownTextSplitter(500)
{
    IncludeHeadingBreadcrumb = false
})
```

## Choisir les paramètres

`CharacterTextSplitter`, `RecursiveTextSplitter` et `MarkdownTextSplitter` mesurent des unités de code UTF-16 (`string.Length`), pas des tokens ni des glyphes visibles. Une paire de substituts, comme pour un emoji, n’est jamais coupée. Avec une taille de 1, une paire de 2 unités peut dépasser la limite. La conservation des caractères combinés et des graphèmes complets n’est pas garantie.

Les tailles doivent être positives, les chevauchements non négatifs. Les réglages invalides lèvent `ArgumentOutOfRangeException` avant traitement ; les propriétés modifiables sont revérifiées au découpage. Un chevauchement supérieur ou égal à la taille le désactive pour compatibilité. Pour Character/Recursive, c’est une cible ajustée aux séparateurs, aux limites Unicode et à la place dans le fragment suivant ; `0` signifie aucun chevauchement. Aucun fragment supplémentaire composé uniquement du dernier chevauchement n’est émis.

Pour Markdown, `ChunkSize` est le budget du contenu **sans le chemin de titres répété**. Un bloc de code entier, ou un en-tête de tableau avec une ligne entière, peut le dépasser. Le texte ordinaire respecte la taille, sauf l’exception des paires de substituts.

La répétition des titres et en-têtes de tableau ne doit pas transformer un petit document en texte d’embedding sans limite. Markdown impose donc un budget global par document de `max(65536, 32 × document.Content.Length)` unités UTF-16, en additionnant tous les fragments finaux, y compris les chemins de titres, en-têtes et libellés répétés. Il vérifie ce budget avant de produire des répétitions excessives et lève `InvalidOperationException` en cas de dépassement prévu, sans tronquer le contenu ni renvoyer de résultat partiel. `ChunkSize` et ses exceptions pour les blocs indivisibles restent soumis à cette limite globale. Dans l’indexation RAG par défaut, l’échec précède l’embedding et le remplacement des enregistrements : l’index existant du document reste intact. Il s’agit d’une limite de texte produit, pas de tokens du modèle ni de mémoire du processus. Le budget s’applique à chaque appel à `Split` et augmente avec la longueur d’entrée ; ce n’est pas une taille maximale fixe du document.

Commencez par exemple avec `RecursiveTextSplitter(500, 50)` pour la prose ou `MarkdownTextSplitter(500)` pour Markdown, puis mesurez avec des questions représentatives. Une grande taille garde plus de contexte ; le chevauchement répète du texte et augmente le travail d’embedding. Aucun ne garantit de meilleurs résultats.

Pour une limite stricte, comptez chaque fragment final, titres et en-têtes répétés compris, avec le tokenizer du modèle cible. Les nombres de caractères/mots et les ratios par langue ne sont pas des budgets fiables. Implémentez `ITextSplitter` avec ce tokenizer si nécessaire.

Ces corrections changent les limites des fragments concernés. Réindexez les mêmes identifiants de document pour remplacer les anciens fragments, puis actualisez les caches d’embeddings et références d’évaluation concernés. Les fragments stockés ne sont pas réécrits automatiquement.

## Découpeur par document

Différents découpeurs peuvent être appliqués par document dans le `RagBuilder` :

```csharp
.WithRag(rag => rag
    .AddDocuments(new PlainTextDocumentLoader(), "readme.md", new MarkdownTextSplitter(600))
    .AddDocuments(new PlainTextDocumentLoader(), "donnees.txt", new RecursiveTextSplitter(300, 30))
    .WithTextSplitter(new RecursiveTextSplitter(500, 50))  // par défaut pour le reste
)
```

## Découpeur personnalisé

Si vous souhaitez écrire un module de découpage personnalisé et le brancher, implémentez `ITextSplitter` :

L’indexation ne doit pas annoncer un succès alors qu’un fragment en écrase un autre. Donnez à chaque fragment un ID non vide, unique dans la collection, et copiez les métadonnées du document pour conserver les filtres d’entreprise ou d’accès. Cet exemple combine l’ID du document et l’indice du fragment. Le pipeline refuse les ID manquants et les doublons au sein d’un document ; il ne génère pas d’ID de remplacement. Consultez la [validation de l’indexation](rag-pipeline.md#indexing-validation).

```csharp
public class PhraseSplitter : ITextSplitter
{
    public IReadOnlyList<RagChunk> Split(RagDocument document)
    {
        var sentences = document.Content.Split(". ");
        return sentences.Select((s, i) => new RagChunk
        {
            Id = $"{document.Id}_chunk_{i}",
            Content = s,
            Index = i,
            DocumentId = document.Id,
            Metadata = new Dictionary<string, string>(document.Metadata)
        }).ToList();
    }
}

// Enregistrer :
.WithTextSplitter(new PhraseSplitter())
```
