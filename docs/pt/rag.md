# RAG (Retrieval-Augmented Generation)

O RAG permite que o modelo responda perguntas com base nos seus próprios documentos, recuperando chunks relevantes no momento da consulta.

## Instalação

```bash
dotnet add package Mythosia.AI.Rag
```

## Início Rápido

Use `.WithRag()` em qualquer `IAIService` para habilitar o RAG com uma API fluente:

```csharp
using Mythosia.AI.Rag;

var service = new AnthropicService(apiKey, http)
    .WithRag(rag => rag
        .AddDocument("manual.txt")
        .AddDocument("politica.txt")
    );

var response = await service.GetCompletionAsync("Qual é a política de reembolso?");
```

Os documentos são divididos, embutidos e armazenados automaticamente. No momento da consulta, os chunks mais relevantes são recuperados e injetados no prompt.

## Adicionando Documentos

Vários tipos de fontes são suportados:

```csharp
.WithRag(rag => rag
    .AddDocument("readme.txt")                    // arquivo local
    .AddDocument("https://example.com/doc.txt")   // URL
    .AddText("Conteúdo inline pode ir aqui também.")   // string bruta
)
```

## Provedor de Embedding Personalizado

Por padrão, o RAG usa o próprio provedor do serviço para embeddings. Para usar um modelo de embedding dedicado:

```csharp
using Mythosia.AI.Rag.Embeddings;

var embedder = new OpenAIEmbeddingProvider(apiKey, http, "text-embedding-3-small");

var service = new AnthropicService(apiKey, http)
    .WithRag(rag => rag
        .UseEmbeddingProvider(embedder)
        .AddDocument("base-conhecimento.txt")
    );
```

## Vector Store Personalizado

Por padrão, um store em memória é usado. Para produção, use um vector store persistente:

```bash
dotnet add package Mythosia.VectorDb.Postgres
```

```csharp
using Mythosia.VectorDb.Postgres;

var store = new PostgresStore(connectionString, embedDimension: 1536);

var service = new OpenAIService(apiKey, http)
    .WithRag(rag => rag
        .UseVectorStore(store)
        .AddDocument("corpus-grande.txt")
    );
```

## Opções de Consulta

Ajuste o comportamento de recuperação por consulta:

```csharp
var options = new RagQueryOptions
{
    TopK = 5,
    ScoreThreshold = 0.7f
};

var response = await service.GetCompletionAsync("Sua pergunta", ragOptions: options);
```

## Próximos Passos

- [Hybrid Search](rag-hybrid-search.md) — combine busca semântica e por palavras-chave
- [Reescrita de Consulta](rag-query-rewriting.md) — otimize consultas com contexto de conversa
- [Re-ranking](rag-reranking.md) — refine ainda mais a precisão dos resultados de busca
- [Personalização de Pipeline](rag-pipeline.md) — controle refinado sobre o processo RAG
- [Agentic RAG](rag-agentic.md) — IA decide quando e o que pesquisar
- [Vector Stores](vectordb-overview.md) — configuração de armazenamento persistente
- [Text Splitters](text-splitters.md) — personalize como os documentos são divididos
