# Configuração de Backends

## In-Memory

O backend mais simples — sem dependências externas. Os dados ficam na RAM e são perdidos quando o processo encerra. Ideal para desenvolvimento, testes e demos.

```bash
dotnet add package Mythosia.VectorDb.InMemory
```

```csharp
using Mythosia.VectorDb.InMemory;

var store = new InMemoryVectorStore();
```

**Hybrid search embutido**: RRF (Reciprocal Rank Fusion) combina similaridade cosseno e pontuações BM25 de palavras-chave.

### Diagnóstico

```csharp
// Listar todos os registros armazenados
var all = await store.ListAllRecordsAsync();
Console.WriteLine($"Total: {store.GetTotalRecordCount()}");

// Inspecionar pontuações de similaridade brutas
var scored = await store.ScoredListAsync(queryVector);
foreach (var r in scored)
    Console.WriteLine($"[{r.Score:F3}] {r.Record.Content[..60]}");
```

---

## Qdrant

Banco de dados vetorial para produção com hybrid search nativo. Roda como serviço independente via Docker ou Qdrant Cloud.

```bash
dotnet add package Mythosia.VectorDb.Qdrant
```

```bash
# Iniciar Qdrant localmente
docker run -p 6333:6333 -p 6334:6334 qdrant/qdrant
```

```csharp
using Mythosia.VectorDb.Qdrant;

var store = new QdrantStore(new QdrantOptions
{
    Host             = "localhost",
    Port             = 6334,           // Porta gRPC
    CollectionName   = "meus-docs",
    Dimension        = 1536,           // Deve corresponder ao modelo de embedding
    AutoCreateCollection = true        // Cria a coleção no primeiro upsert
});
```

### Todas as Opções

```csharp
new QdrantOptions
{
    Host                   = "localhost",
    Port                   = 6334,
    UseTls                 = false,
    ApiKey                 = null,             // Obrigatório para Qdrant Cloud

    CollectionName         = "minha-colecao",  // Obrigatório
    Dimension              = 1536,             // Obrigatório

    DistanceStrategy       = QdrantDistanceStrategy.Cosine,
    HybridFusionStrategy   = QdrantHybridFusionStrategy.Rrf,
    AutoCreateCollection   = true,

    // Índices de payload adicionais para filtragem mais rápida no servidor
    AdditionalPayloadIndexes = new List<QdrantIndexOption>
    {
        new QdrantIndexOption { Field = "meta.language", SchemaType = PayloadSchemaType.Keyword },
        new QdrantIndexOption { Field = "meta.date",     SchemaType = PayloadSchemaType.Integer }
    }
}
```

### Estratégias de Distância

| Valor | Descrição |
|-------|-----------|
| `Cosine` | Similaridade cosseno — melhor para embeddings normalizados (padrão) |
| `Euclidean` | Distância L2 — menor distância = mais similar |
| `DotProduct` | Produto escalar — usar com vetores unitários normalizados |

### Estratégias de Fusão Híbrida

| Valor | Descrição |
|-------|-----------|
| `Rrf` | Reciprocal Rank Fusion — mesclagem robusta baseada em ranking (padrão) |
| `Dbsf` | Distribution-Based Score Fusion — mescla por distribuição de pontuações |

### Qdrant Cloud

```csharp
new QdrantOptions
{
    Host           = "seu-cluster.cloud.qdrant.io",
    Port           = 6334,
    UseTls         = true,
    ApiKey         = "sua-chave-qdrant-cloud",
    CollectionName = "producao",
    Dimension      = 1536
}
```

### Usando um QdrantClient Externo

Se você já tem um `QdrantClient` configurado (ex: de um container DI), passe-o diretamente:

```csharp
var store = new QdrantStore(options, existingQdrantClient);
```

O store **não** fará dispose do cliente fornecido externamente.

> Todos os vector stores implementam `IDisposable`. Quando você cria um store com o construtor padrão, chame `Dispose()` (ou use `using`) para liberar recursos internos.

---

## Pinecone

Banco de dados vetorial serverless totalmente gerenciado. Sem infraestrutura para administrar.

```bash
dotnet add package Mythosia.VectorDb.Pinecone
```

```csharp
using Mythosia.VectorDb.Pinecone;

var store = new PineconeStore(new PineconeOptions
{
    IndexHost = "https://meu-index-xxxx.svc.us-east1-gcp.pinecone.io",
    ApiKey    = "sua-api-key"
});
```

### Criação Automática de Índice

Se ainda não tiver um índice, deixe o SDK criá-lo:

```csharp
new PineconeOptions
{
    ApiKey          = "sua-api-key",
    AutoCreateIndex = true,
    IndexName       = "meu-index",
    Dimension       = 1536,
    Cloud           = "aws",          // "aws", "gcp" ou "azure"
    Region          = "us-east-1"
}
```

> Quando `AutoCreateIndex` está habilitado, o índice é criado com a métrica `dotproduct` — necessária para hybrid search (sparse + dense).

### Todas as Opções

```csharp
new PineconeOptions
{
    IndexHost              = "https://...",   // Obrigatório (ou use AutoCreateIndex)
    ApiKey                 = "...",           // Obrigatório
    Namespace              = "producao",      // Opcional: aplicado a todas as operações

    UpsertBatchSize        = 100,             // Registros por requisição de upsert em lote
    RequestTimeoutSeconds  = 100,

    AutoCreateIndex        = false,
    IndexName              = null,
    Dimension              = 0,
    Cloud                  = null,
    Region                 = null,
    ControlPlaneHost       = "https://api.pinecone.io"
}
```

### Usando um HttpClient Externo

Se você já tem um `HttpClient` configurado (ex: de `IHttpClientFactory`):

```csharp
var store = new PineconeStore(options, existingHttpClient);
```

O store **não** fará dispose do cliente fornecido externamente.

---

## PostgreSQL (pgvector)

Utiliza a extensão [`pgvector`](https://github.com/pgvector/pgvector) para adicionar busca por similaridade vetorial a um banco PostgreSQL padrão.

```bash
dotnet add package Mythosia.VectorDb.Postgres
```

### Pré-requisitos

```sql
-- Execute uma vez no seu servidor PostgreSQL
CREATE EXTENSION IF NOT EXISTS vector;
CREATE EXTENSION IF NOT EXISTS pg_trgm;  -- Apenas se usar busca por Trigrama
```

Ou deixe o SDK fazer automaticamente com `EnsureSchema = true`.

```csharp
using Mythosia.VectorDb.Postgres;

var store = new PostgresStore(new PostgresOptions
{
    ConnectionString = "Host=localhost;Port=5432;Database=mydb;Username=user;Password=pass;",
    Dimension        = 1536,
    EnsureSchema     = true    // Cria extensão, tabela e índices automaticamente
});
```

### Tipos de Índice

| Tipo | Classe | Quando Usar |
|------|--------|-------------|
| HNSW | `HnswIndexOptions` | Padrão. Busca aproximada rápida. Melhor para a maioria dos casos. |
| IVFFlat | `IvfFlatIndexOptions` | Menor uso de memória. Bom para datasets estáticos grandes. |
| None | `NoIndexOptions` | Varredura sequencial. Use apenas para datasets pequenos. |

```csharp
// HNSW (padrão)
new PostgresOptions
{
    // ...
    Index = new HnswIndexOptions
    {
        M              = 16,   // Conexões máximas de vizinhos por nó
        EfConstruction = 64,   // Escopo de busca durante a construção do índice
        EfSearch       = 40    // Escopo de busca em tempo de execução
    }
}

// IVFFlat
new PostgresOptions
{
    // ...
    Index = new IvfFlatIndexOptions
    {
        Lists  = 100,  // Número de listas invertidas
        Probes = 10    // Quantas listas sondar no momento da consulta
    }
}

// Sem índice (varredura sequencial)
new PostgresOptions { Index = new NoIndexOptions() }
```

### Modos de Busca Textual

Usado para o lado de palavras-chave da busca híbrida:

| Modo | Melhor Para |
|------|-------------|
| `TsVector` | Busca full-text padrão — inglês, maioria das línguas ocidentais |
| `Trigram` | Idiomas CJK (coreano, chinês, japonês), correspondência fuzzy |

```csharp
new PostgresOptions
{
    TextSearchMode   = TextSearchMode.Trigram,
    TextSearchConfig = "simple"     // Configuração de busca textual do PostgreSQL
}
```

### Estratégias de Distância

| Valor | Operador Postgres | Notas |
|-------|------------------|-------|
| `Cosine` | `<=>` | 1 − similaridade cosseno (padrão) |
| `Euclidean` | `<->` | Distância L2 |
| `InnerProduct` | `<#>` | Produto interno negativo — usar com vetores unitários normalizados |

### Perfil de Busca em Tempo de Execução

Ajuste fino de recall vs. latência no momento da consulta:

```csharp
var opts = new HnswSearchRuntimeOptions
{
    Profile = SearchProfile.HighRecall,  // Fast | Balanced | HighRecall
    EfSearch = 80                        // Sobrescreve ef_search do HNSW diretamente
};

var results = await store.SearchAsync(queryVector, topK: 5, filter: null, runtimeOptions: opts);
```

### Todas as Opções

```csharp
new PostgresOptions
{
    ConnectionString  = "...",
    Dimension         = 1536,

    SchemaName        = "public",
    TableName         = "vectors",

    EnsureSchema      = false,
    DistanceStrategy  = DistanceStrategy.Cosine,
    Index             = new HnswIndexOptions(),

    TextSearchConfig  = "simple",
    TextSearchMode    = TextSearchMode.TsVector,

    FailFastOnIndexCreationFailure = true
}
```
