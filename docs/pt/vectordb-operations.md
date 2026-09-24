# Operações do Vector Store

## Upsert

Insere ou atualiza um único registro. Se já existir um registro com o mesmo `Id`, ele é substituído.

```csharp
var record = new VectorRecord
{
    Id      = Guid.NewGuid().ToString(),
    Vector  = await embeddingService.GetEmbeddingAsync("Reembolsos são aceitos em até 30 dias."),
    Content = "Reembolsos são aceitos em até 30 dias.",
    Metadata = new Dictionary<string, string>
    {
        ["source"]   = "faq.pdf",
        ["language"] = "pt",
        ["section"]  = "returns"
    }
};

await store.UpsertAsync(record);
```

## Upsert em Lote

Insere ou atualiza múltiplos registros em uma única chamada. Mais eficiente do que chamar `UpsertAsync` em loop — os backends usam APIs em lote internamente quando disponível.

```csharp
var records = chunks.Select(chunk => new VectorRecord
{
    Id      = Guid.NewGuid().ToString(),
    Vector  = chunk.Embedding,
    Content = chunk.Text,
    Metadata = new Dictionary<string, string>
    {
        ["source"] = "manual.pdf",
        ["page"]   = chunk.Page.ToString()
    }
});

await store.UpsertBatchAsync(records);
```

## Busca

Retorna os K registros mais similares a um vetor de consulta. Opcionalmente filtra por metadados antes da pontuação.

```csharp
float[] queryVector = await embeddingService.GetEmbeddingAsync("Qual é a política de reembolso?");

var results = await store.SearchAsync(queryVector, topK: 5);

foreach (var r in results)
{
    Console.WriteLine($"[{r.Score:F3}] {r.Record.Content}");
    Console.WriteLine($"  Fonte: {r.Record.Metadata["source"]}");
}
```

### Busca com Filtro

Combine similaridade vetorial com filtragem por metadados:

```csharp
var filter = new VectorFilter()
    .Where("language", "pt")
    .Where("section", "returns")
    .WithMinScore(0.7);

var results = await store.SearchAsync(queryVector, topK: 5, filter: filter);
```

Consulte [VectorFilter](vector-filter.md) para a API completa de filtragem.

## Hybrid Search

Combina similaridade vetorial densa com busca por palavras-chave (BM25). Melhor recall para consultas com termos específicos, nomes ou códigos.

```csharp
float[] queryVector = await embeddingService.GetEmbeddingAsync("pedido #12345 status");

var results = await store.HybridSearchAsync(
    denseVector: queryVector,
    query: "pedido #12345 status",   // Texto bruto usado para BM25
    topK: 5
);
```

Como o hybrid search funciona por backend:

| Backend | Mecanismo |
|---------|-----------|
| **InMemory** | RRF combina similaridade cosseno + pontuações BM25 Lucene |
| **Qdrant** | No servidor: vetores densos + esparsos fundidos com RRF ou DBSF |
| **Pinecone** | Vetores sparse + dense mesclados no servidor |
| **Postgres** | Similaridade vetorial + pontuações `tsvector`/`trigram` mescladas em SQL |

### Busca textual e híbrida configurável (não publicada)

A sobrecarga acima usa o comportamento híbrido existente de cada backend. No código-fonte atual, InMemory, PostgreSQL e Qdrant também implementam `ITextSearchStore` e `IConfigurableHybridSearchStore`. Essas APIs opcionais estão **não publicadas (Unreleased)**; Pinecone não as implementa.

```csharp
using Mythosia.VectorDb;

var filter = new VectorFilter().Where("tenant", "acme");
var textResults = await ((ITextSearchStore)store).TextSearchAsync(
    "pedido #12345 status", topK: 5, filter: filter);

var hybridResults = await ((IConfigurableHybridSearchStore)store).HybridSearchAsync(
    queryVector, "pedido #12345 status",
    new HybridSearchOptions { VectorWeight = 0.7f, CandidateMultiplier = 4, RrfK = 60 },
    topK: 5, filter: filter);
```

`TextSearchAsync` não precisa de um vetor denso de consulta. A sobrecarga configurável usa pontuações RRF ponderadas normalizadas em `[0, 1]`; peso `0` ignora a busca vetorial e `1` ignora a textual. Os filtros de metadados são aplicados antes da seleção top-K, e `MinScore` após a fusão. As pontuações nativas de texto e vetor têm escalas diferentes; ajuste os limiares por modo. `HybridFusionStrategy` do Qdrant controla apenas a sobrecarga existente acima.

## Buscar por ID

Recupera um registro específico pelo seu ID:

```csharp
VectorRecord? record = await store.GetAsync("record-id-123");

if (record is null)
    Console.WriteLine("Não encontrado");
```

Aplique um filtro para escopo de busca (ex: em namespaces multi-tenant):

```csharp
var filter = new VectorFilter().Where("tenant", "acme");
var record = await store.GetAsync("record-id-123", filter: filter);
```

## Busca em Lote por ID

Recupera múltiplos registros por ID em uma única chamada:

```csharp
var ids = new[] { "id-1", "id-2", "id-3" };
var records = await store.GetBatchAsync(ids);
```

## Excluir por ID

Remove um único registro:

```csharp
await store.DeleteAsync("record-id-123");
```

## Excluir por Filtro

Remove todos os registros que correspondem a um filtro. Use com cautela — esta é uma exclusão em massa.

```csharp
// Excluir todos os registros de um documento específico
var filter = new VectorFilter().Where("source", "manual-antigo.pdf");
await store.DeleteByFilterAsync(filter);
```

## Substituir por Filtro

Exclui todos os registros que correspondem a um filtro e insere um novo conjunto. Útil para re-indexar um documento sem deixar chunks desatualizados. A atomicidade da substituição completa depende do backend.

```csharp
var filter = new VectorFilter().Where("source", "manual-v1.pdf");

var newRecords = newChunks.Select(c => new VectorRecord
{
    Id      = Guid.NewGuid().ToString(),
    Vector  = c.Embedding,
    Content = c.Text,
    Metadata = new Dictionary<string, string> { ["source"] = "manual-v2.pdf" }
}).ToList();

await store.ReplaceByFilterAsync(filter, newRecords);
```

> Postgres usa uma transação de banco de dados. InMemory executa a exclusão e a inserção em lote em sequência, então outra consulta pode observar o intervalo vazio. Uma falha ou um cancelamento pode deixar uma substituição parcial; as gravações concluídas não são desfeitas. Sincronizar cada operação mantém os registros e o índice BM25 consistentes, mas não torna a substituição completa transacional.

## Contar

Conta os registros armazenados, opcionalmente com escopo por filtro:

```csharp
long total      = await store.CountAsync();
long portuguese = await store.CountAsync(new VectorFilter().Where("language", "pt"));

Console.WriteLine($"Total: {total}, Português: {portuguese}");
```

## Verificar Conexão

Verifica se o backend está acessível. Útil em health checks ou validação na inicialização:

```csharp
try
{
    await store.VerifyConnectionAsync();
    Console.WriteLine("Conexão com vector store OK");
}
catch (Exception ex)
{
    Console.WriteLine($"Falha na conexão: {ex.Message}");
}
```

## Usando com RAG

Passe um `IVectorStore` para o `RagBuilder` para usar qualquer backend como store de recuperação RAG:

```csharp
var store = new QdrantStore(new QdrantOptions
{
    CollectionName = "base-de-conhecimento",
    Dimension      = 1536
});

var ragService = new AnthropicService(apiKey, http)
    .WithRag(rag => rag
        .UseStore(store)
        .UseOpenAIEmbedding(embeddingKey)
        .AddDocuments("docs/")
    );

var answer = await ragService.GetCompletionAsync("Qual é a política de devolução?");
```

Ou construa um `RagStore` de forma independente e compartilhe-o entre múltiplos serviços de IA:

```csharp
RagStore ragStore = await RagStore.BuildAsync(rag => rag
    .UseStore(store)
    .UseOpenAIEmbedding(apiKey)
    .AddDocument("base-de-conhecimento.pdf"));

var claudeRag = new AnthropicService(claudeKey, http).WithRag(ragStore);
var gptRag    = new OpenAIService(openAiKey, http).WithRag(ragStore);
```
