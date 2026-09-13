# RAG (Retrieval-Augmented Generation)

Para uma resposta final com botão Parar, passe `cancellationToken` a `GetCompletionAsync`. Use Run para eventos de progresso ou instruções adicionais suportadas. Consulte [cancelamento](completions.md#completion-cancellation).

Para respostas enriquecidas com pesquisa, passe também `cancellationToken` a `RagEnabledService.GetCompletionAsync`. O mesmo token chega à pesquisa, `LlmQueryRewriter`, `LlmReranker` e resposta interna; cancelar durante a pesquisa evita a chamada posterior ao modelo. `RagPipeline.QueryAndGenerateAsync` também transmite o token. Os componentes devem cooperar; pesquisas e ações de ferramentas já concluídas não são revertidas.

O RAG permite que o modelo responda perguntas com base nos seus próprios documentos, recuperando chunks relevantes no momento da consulta.

Para permitir que o usuário acompanhe ou interrompa a escrita de uma resposta baseada nos seus documentos, combine a busca RAG com um run. O [guia de Run](execution-api-transition.md) explica o fluxo e os limites das instruções adicionais.

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
    .AddUrl("https://example.com/doc.txt")        // URL
    .AddText("Conteúdo inline pode ir aqui também.")   // string bruta
)
```

## Provedor de Embedding Personalizado

Por padrão, o RAG usa o provedor local de embeddings integrado. Para usar um modelo de embedding dedicado:

```csharp
using Mythosia.AI.Rag.Embeddings;

var embedder = new OpenAIEmbeddingProvider(apiKey, http, "text-embedding-3-small");

var service = new AnthropicService(apiKey, http)
    .WithRag(rag => rag
        .UseEmbedding(embedder)
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

var store = new PostgresStore(new PostgresOptions
{
    ConnectionString = connectionString,
    Dimension = 1536
});

var service = new OpenAIService(apiKey, http)
    .WithRag(rag => rag
        .UseStore(store)
        .AddDocument("corpus-grande.txt")
    );
```

## Opções de Consulta

Ajuste o comportamento de recuperação por consulta:

```csharp
var options = new RagQueryOptions
{
    FinalFilter = new RagFilter
    {
        TopK = 5,
        MinScore = 0.7
    }
};

var response = await service.GetCompletionAsync("Sua pergunta", options: options);
```

Se seu índice já estiver hospedado pelo provedor do modelo, compare RAG com a [busca nativa de arquivos e as opções comuns de raciocínio](reasoning-and-search.md).

## Próximos Passos

- [Hybrid Search](rag-hybrid-search.md) — combine busca semântica e por palavras-chave
- [Reescrita de Consulta](rag-query-rewriting.md) — otimize consultas com contexto de conversa
- [Re-ranking](rag-reranking.md) — refine ainda mais a precisão dos resultados de busca
- [Personalização de Pipeline](rag-pipeline.md) — controle refinado sobre o processo RAG
- [Agentic RAG](rag-agentic.md) — IA decide quando e o que pesquisar
- [Vector Stores](vectordb-overview.md) — configuração de armazenamento persistente
- [Text Splitters](text-splitters.md) — personalize como os documentos são divididos

Perplexity: [Usar vetores em seu próprio índice / Pesquisar sem gerar uma resposta](perplexity.md).
