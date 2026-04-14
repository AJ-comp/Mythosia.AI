# Embedding

> 📍 **Pipeline de Pergunta e Resposta:** [Reescrita de Consulta](rag-query-rewriting.md) → **`Embedding`** → [Filtragem](rag-filtering.md) → [Recuperação](rag-hybrid-search.md) → [Re-ranking](rag-reranking.md) → [Construção de Contexto](rag-context-build.md)

## O que é Embedding?

Embedding é o processo de converter texto em vetores numéricos que capturam significado. Esses vetores ficam em um espaço de alta dimensionalidade onde **textos com significados semelhantes ficam próximos uns dos outros**.

No pipeline RAG, o embedding acontece em dois pontos:

1. **Indexação de documentos** — cada chunk é embutido e armazenado no vector store
2. **Tempo de consulta** — a pergunta do usuário é embutida para ser comparada com os chunks armazenados

## Provedores de Embedding Integrados

### OpenAI Embedding

A opção mais popular baseada em nuvem:

```csharp
using Mythosia.AI.Rag.Embeddings;

var embedder = new OpenAIEmbeddingProvider(
    apiKey: "sk-...",
    httpClient: new HttpClient(),
    model: "text-embedding-3-small",
    dimensions: 1536
);
```

Ou com o builder fluente:

```csharp
.WithRag(rag => rag
    .UseOpenAIEmbedding(apiKey, model: "text-embedding-3-small", dimensions: 1536)
    .AddDocument("docs.txt")
)
```

### Ollama (Local)

Execute embeddings localmente sem enviar dados para a nuvem:

```csharp
var embedder = new OllamaEmbeddingProvider(
    httpClient: new HttpClient(),
    model: "qwen3-embedding:4b",
    dimensions: 1024,
    baseUrl: "http://localhost:11434"
);
```

### vLLM (Auto-hospedado)

```csharp
var embedder = new VllmEmbeddingProvider(
    httpClient: new HttpClient(),
    model: "Qwen/Qwen3-Embedding-0.6B",
    dimensions: 1024,
    baseUrl: "http://localhost:8002"
);
```

### Local (Sem API)

Um provedor leve baseado em hashing de features. **Não recomendado para produção.**

```csharp
.WithRag(rag => rag
    .UseLocalEmbedding(dimensions: 1024)
    .AddDocument("docs.txt")
)
```

## Dimensões

A propriedade `Dimensions` controla o tamanho de cada vetor de embedding. O vector store deve ter a mesma dimensão configurada.

| Provedor | Modelo | Dimensões Padrão |
| --- | --- | --- |
| OpenAI | text-embedding-3-small | 1536 |
| OpenAI | text-embedding-3-large | 3072 |
| Ollama | qwen3-embedding:4b | 1024 |
| Local | (hashing de features) | 1024 |

## Provedor de Embedding Personalizado

Implemente `IEmbeddingProvider`:

```csharp
public class MyEmbeddingProvider : IEmbeddingProvider
{
    public int Dimensions => 768;

    public async Task<float[]> GetEmbeddingAsync(
        string text, CancellationToken cancellationToken = default)
    {
        // Chame sua API de embedding aqui
    }

    public async Task<IReadOnlyList<float[]>> GetEmbeddingsAsync(
        IEnumerable<string> texts, CancellationToken cancellationToken = default)
    {
        // Chamada de embedding em lote
    }
}
```

Registre-o com o builder:

```csharp
.WithRag(rag => rag
    .UseEmbedding(new MyEmbeddingProvider())
    .AddDocument("docs.txt")
)
```
