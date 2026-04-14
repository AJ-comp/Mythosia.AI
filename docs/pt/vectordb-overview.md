# Visão Geral do Banco de Dados Vetorial

Mythosia.AI oferece uma abstração unificada `IVectorStore` que funciona com múltiplos backends de banco de dados vetorial. Você escreve sua aplicação contra a interface uma única vez e troca de backend sem alterar nenhuma lógica de recuperação.

## Interface Principal: `IVectorStore`

```csharp
// Upsert
Task UpsertAsync(VectorRecord record, CancellationToken cancellationToken = default);
Task UpsertBatchAsync(IEnumerable<VectorRecord> records, CancellationToken cancellationToken = default);

// Busca
Task<IReadOnlyList<VectorSearchResult>> SearchAsync(
    float[] queryVector, int topK = 5, VectorFilter? filter = null,
    CancellationToken cancellationToken = default);

Task<IReadOnlyList<VectorSearchResult>> HybridSearchAsync(
    float[] denseVector, string query, int topK = 5, VectorFilter? filter = null,
    CancellationToken cancellationToken = default);

// Buscar por ID
Task<VectorRecord?> GetAsync(string id, VectorFilter? filter = null,
    CancellationToken cancellationToken = default);
Task<IReadOnlyList<VectorRecord>> GetBatchAsync(IEnumerable<string> ids,
    VectorFilter? filter = null, CancellationToken cancellationToken = default);

// Excluir
Task DeleteAsync(string id, VectorFilter? filter = null,
    CancellationToken cancellationToken = default);
Task DeleteByFilterAsync(VectorFilter filter, CancellationToken cancellationToken = default);
Task ReplaceByFilterAsync(VectorFilter filter, IReadOnlyList<VectorRecord> records,
    CancellationToken cancellationToken = default);

// Utilitários
Task<long> CountAsync(VectorFilter? filter = null, CancellationToken cancellationToken = default);
Task VerifyConnectionAsync(CancellationToken cancellationToken = default);
```

## Modelos de Dados

### VectorRecord

Cada entrada armazenada é um `VectorRecord`:

```csharp
public class VectorRecord
{
    public string Id { get; set; }                           // Identificador único
    public float[] Vector { get; set; }                      // Vetor de embedding
    public string Content { get; set; }                      // Conteúdo textual original
    public Dictionary<string, string> Metadata { get; set; } // Metadados chave-valor personalizados
}
```

Use o dicionário `Metadata` para qualquer campo personalizado — arquivo de origem, idioma, data, categoria, etc.:

```csharp
var record = new VectorRecord
{
    Id = Guid.NewGuid().ToString(),
    Vector = await embeddingService.GetEmbeddingAsync("Algum texto"),
    Content = "Algum texto",
    Metadata = new Dictionary<string, string>
    {
        ["source"] = "manual.pdf",
        ["language"] = "pt",
        ["date"] = "2024-01-15",
        ["category"] = "policy"
    }
};
```

### VectorSearchResult

Os resultados de busca combinam um registro com sua pontuação de similaridade:

```csharp
public class VectorSearchResult
{
    public VectorRecord Record { get; set; }
    public double Score { get; set; }  // 0.0–1.0 (maior = mais similar)
}
```

## Backends Disponíveis

| Backend | Pacote | Caso de Uso |
|---------|--------|-------------|
| **In-Memory** | `Mythosia.VectorDb.InMemory` | Desenvolvimento, testes, demos |
| **Qdrant** | `Mythosia.VectorDb.Qdrant` | Produção, hybrid search nativo |
| **Pinecone** | `Mythosia.VectorDb.Pinecone` | Serviço gerenciado serverless |
| **PostgreSQL** | `Mythosia.VectorDb.Postgres` | Deployments Postgres existentes, ACID |

Todos os backends implementam a mesma interface `IVectorStore`. Veja [Configuração de Backends](vectordb-backends.md) para configuração por backend.

## Injeção de Dependência

Registre qualquer backend como `IVectorStore`:

```csharp
// In-Memory
services.AddSingleton<IVectorStore>(new InMemoryVectorStore());

// Qdrant
services.AddSingleton<IVectorStore>(new QdrantStore(new QdrantOptions
{
    CollectionName = "minha-colecao",
    Dimension = 1536
}));

// PostgreSQL
services.AddSingleton<IVectorStore>(new PostgresStore(new PostgresOptions
{
    ConnectionString = "Host=localhost;Database=vectors;",
    Dimension = 1536,
    EnsureSchema = true
}));
```

## Execução de Filtros por Backend

As condições do `VectorFilter` são empurradas para o backend sempre que possível:

| Operador | InMemory | Qdrant | Pinecone | Postgres |
|----------|----------|--------|----------|----------|
| Eq / Ne | Cliente | **Servidor** | **Servidor** | **SQL** |
| In / NotIn | Cliente | **Servidor** | **Servidor** | **SQL** |
| Gt / Gte / Lt / Lte | Cliente | Cliente | **Servidor** | **SQL** |
| Like | Cliente | Cliente | Cliente | **SQL** |
| Exists / NotExists | Cliente | Cliente | Cliente | **SQL** |

O Postgres tem pushdown SQL completo para todos os operadores. Qdrant e Pinecone empurram ao servidor os operadores de igualdade, pertencimento a conjunto e comparação.

> **Nota:** O Qdrant descarta silenciosamente operadores de filtro não suportados (`Like`, `Exists`, `NotExists`) — eles não são aplicados no lado do cliente. Se precisar desses operadores com Qdrant, aplique filtragem adicional nos resultados retornados.
