# Text Splitters

Os text splitters dividem documentos em chunks antes do embedding. O tamanho e a sobreposição dos chunks afetam significativamente a qualidade da recuperação.

## Splitters Disponíveis

### CharacterTextSplitter

Divide por contagem de caracteres. Simples e rápido, mas pode cortar no meio de uma frase:

```csharp
.WithTextSplitter(new CharacterTextSplitter(500, 50))
```

### RecursiveTextSplitter (padrão recomendado)

Tenta dividir em limites semanticamente significativos nesta ordem: parágrafos → frases → palavras → caracteres. Produz chunks mais coerentes:

```csharp
.WithTextSplitter(new RecursiveTextSplitter(500, 50))
```

### TokenTextSplitter

Divide por contagem de tokens em vez de contagem de caracteres. Mais preciso para orçamento de janela de contexto LLM:

```csharp
.WithTextSplitter(new TokenTextSplitter(256, 32))
```

### MarkdownTextSplitter

Um splitter com consciência de estrutura que entende hierarquia de títulos Markdown (H1–H6), cercas de código e tabelas:

```csharp
.WithTextSplitter(new MarkdownTextSplitter(500, 50))
```

Melhor para arquivos de documentação, README e saída de carregadores de documentos estruturados como Office.

> [!TIP]
> Os carregadores de documentos para Word, Excel, PowerPoint e HWP convertem internamente os documentos para Markdown. Usar `MarkdownTextSplitter` com esses documentos garante que as estruturas de tabela e bloco de código sejam preservadas durante o chunking.

#### Qualidade de Divisão de Tabelas

`MarkdownTextSplitter` divide tabelas Markdown em **limites de linha**. Nunca corta uma linha ao meio, e cada chunk resultante inclui automaticamente o **cabeçalho e linha separadora**:

```
Tabela original:
| Nome   | Depto  | Salário |
|--------|--------|---------|
| Alice  | Dev    | R$9.000 |
| Bob    | PM     | R$8.500 |
| Carol  | Design | R$8.000 |

→ Chunk 1:
| Nome   | Depto  | Salário |
|--------|--------|---------|
| Alice  | Dev    | R$9.000 |
| Bob    | PM     | R$8.500 |

→ Chunk 2:
| Nome   | Depto  | Salário |
|--------|--------|---------|
| Carol  | Design | R$8.000 |
```

## Escolhendo Parâmetros

| Parâmetro | Efeito |
|-----------|--------|
| `chunkSize` (maior) | Mais contexto por chunk, menos chunks, embedding mais barato |
| `chunkSize` (menor) | Recuperação de maior precisão, mais chunks, mais embeddings |
| `chunkOverlap` | Evita perda de informação nos limites dos chunks |

Um ponto de partida comum: `chunkSize: 500, chunkOverlap: 50`.

## Splitter por Documento

Splitters diferentes podem ser aplicados por documento no `RagBuilder`:

```csharp
.WithRag(rag => rag
    .AddDocuments(new PlainTextDocumentLoader(), "readme.md", new MarkdownTextSplitter(600, 60))
    .AddDocuments(new PlainTextDocumentLoader(), "dados.txt",  new RecursiveTextSplitter(300, 30))
    .WithTextSplitter(new RecursiveTextSplitter(500, 50))  // padrão para o restante
)
```

## Splitter Personalizado

Implemente `ITextSplitter` para lógica de divisão totalmente personalizada:

```csharp
public class SentenceSplitter : ITextSplitter
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

// Registrar:
.WithTextSplitter(new SentenceSplitter())
```
