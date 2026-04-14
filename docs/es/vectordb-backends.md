# Configuración de Backends

## In-Memory

El backend más sencillo — sin dependencias externas. Los datos se almacenan en RAM y se pierden al terminar el proceso. Ideal para desarrollo, pruebas y demos.

```bash
dotnet add package Mythosia.VectorDb.InMemory
```

```csharp
using Mythosia.VectorDb.InMemory;

var store = new InMemoryVectorStore();
```

**Hybrid search integrado**: RRF (Reciprocal Rank Fusion) combina similaridad coseno y puntuaciones BM25 de palabras clave.

### Diagnóstico

```csharp
// Listar todos los registros almacenados
var all = await store.ListAllRecordsAsync();
Console.WriteLine($"Total: {store.GetTotalRecordCount()}");

// Inspeccionar puntuaciones de similitud en bruto
var scored = await store.ScoredListAsync(queryVector);
foreach (var r in scored)
    Console.WriteLine($"[{r.Score:F3}] {r.Record.Content[..60]}");
```

---

## Qdrant

Base de datos vectorial para producción con hybrid search nativo. Se ejecuta como servicio independiente vía Docker o Qdrant Cloud.

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
    Port             = 6334,           // Puerto gRPC
    CollectionName   = "mis-docs",
    Dimension        = 1536,           // Debe coincidir con el modelo de embedding
    AutoCreateCollection = true        // Crea la colección en el primer upsert
});
```

### Todas las Opciones

```csharp
new QdrantOptions
{
    Host                   = "localhost",
    Port                   = 6334,
    UseTls                 = false,
    ApiKey                 = null,             // Requerido para Qdrant Cloud

    CollectionName         = "mi-coleccion",   // Requerido
    Dimension              = 1536,             // Requerido

    DistanceStrategy       = QdrantDistanceStrategy.Cosine,
    HybridFusionStrategy   = QdrantHybridFusionStrategy.Rrf,
    AutoCreateCollection   = true,

    // Índices de payload adicionales para filtrado más rápido en servidor
    AdditionalPayloadIndexes = new List<QdrantIndexOption>
    {
        new QdrantIndexOption { Field = "meta.language", SchemaType = PayloadSchemaType.Keyword },
        new QdrantIndexOption { Field = "meta.date",     SchemaType = PayloadSchemaType.Integer }
    }
}
```

### Estrategias de Distancia

| Valor | Descripción |
|-------|-------------|
| `Cosine` | Similitud coseno — mejor para embeddings normalizados (predeterminado) |
| `Euclidean` | Distancia L2 — menor distancia = más similar |
| `DotProduct` | Producto punto — usar con vectores unitarios normalizados |

### Estrategias de Fusión Híbrida

| Valor | Descripción |
|-------|-------------|
| `Rrf` | Reciprocal Rank Fusion — fusión robusta basada en ranking (predeterminado) |
| `Dbsf` | Distribution-Based Score Fusion — fusiona por distribución de puntuaciones |

### Qdrant Cloud

```csharp
new QdrantOptions
{
    Host           = "tu-cluster.cloud.qdrant.io",
    Port           = 6334,
    UseTls         = true,
    ApiKey         = "tu-clave-qdrant-cloud",
    CollectionName = "produccion",
    Dimension      = 1536
}
```

### Usando un QdrantClient Externo

Si ya tienes un `QdrantClient` configurado (p.ej., desde un contenedor DI), pásalo directamente:

```csharp
var store = new QdrantStore(options, existingQdrantClient);
```

El store **no** hará dispose del cliente proporcionado externamente.

> Todos los vector stores implementan `IDisposable`. Cuando creas un store con el constructor estándar, llama a `Dispose()` (o usa `using`) para liberar recursos internos.

---

## Pinecone

Base de datos vectorial serverless totalmente gestionada. Sin infraestructura que administrar.

```bash
dotnet add package Mythosia.VectorDb.Pinecone
```

```csharp
using Mythosia.VectorDb.Pinecone;

var store = new PineconeStore(new PineconeOptions
{
    IndexHost = "https://mi-index-xxxx.svc.us-east1-gcp.pinecone.io",
    ApiKey    = "tu-api-key"
});
```

### Creación Automática de Índice

Si aún no tienes un índice, deja que el SDK lo cree:

```csharp
new PineconeOptions
{
    ApiKey          = "tu-api-key",
    AutoCreateIndex = true,
    IndexName       = "mi-index",
    Dimension       = 1536,
    Cloud           = "aws",          // "aws", "gcp" o "azure"
    Region          = "us-east-1"
}
```

> Cuando `AutoCreateIndex` está habilitado, el índice se crea con la métrica `dotproduct` — necesaria para hybrid search (sparse + dense).

### Todas las Opciones

```csharp
new PineconeOptions
{
    IndexHost              = "https://...",   // Requerido (o usa AutoCreateIndex)
    ApiKey                 = "...",           // Requerido
    Namespace              = "produccion",    // Opcional: se aplica a todas las operaciones

    UpsertBatchSize        = 100,             // Registros por solicitud de upsert en lote
    RequestTimeoutSeconds  = 100,

    AutoCreateIndex        = false,
    IndexName              = null,
    Dimension              = 0,
    Cloud                  = null,
    Region                 = null,
    ControlPlaneHost       = "https://api.pinecone.io"
}
```

### Usando un HttpClient Externo

Si ya tienes un `HttpClient` configurado (p.ej., de `IHttpClientFactory`):

```csharp
var store = new PineconeStore(options, existingHttpClient);
```

El store **no** hará dispose del cliente proporcionado externamente.

---

## PostgreSQL (pgvector)

Usa la extensión [`pgvector`](https://github.com/pgvector/pgvector) para añadir búsqueda por similitud vectorial a una base de datos PostgreSQL estándar.

```bash
dotnet add package Mythosia.VectorDb.Postgres
```

### Prerrequisitos

```sql
-- Ejecutar una vez en tu servidor PostgreSQL
CREATE EXTENSION IF NOT EXISTS vector;
CREATE EXTENSION IF NOT EXISTS pg_trgm;  -- Solo si usas búsqueda por Trigrama
```

O deja que el SDK lo gestione automáticamente con `EnsureSchema = true`.

```csharp
using Mythosia.VectorDb.Postgres;

var store = new PostgresStore(new PostgresOptions
{
    ConnectionString = "Host=localhost;Port=5432;Database=mydb;Username=user;Password=pass;",
    Dimension        = 1536,
    EnsureSchema     = true    // Crea extensión, tabla e índices automáticamente
});
```

### Tipos de Índice

| Tipo | Clase | Cuándo Usar |
|------|-------|-------------|
| HNSW | `HnswIndexOptions` | Predeterminado. Búsqueda aproximada rápida. Mejor para la mayoría de casos. |
| IVFFlat | `IvfFlatIndexOptions` | Menor memoria. Bueno para datasets estáticos grandes. |
| None | `NoIndexOptions` | Escaneo secuencial. Usar solo para datasets pequeños. |

```csharp
// HNSW (predeterminado)
new PostgresOptions
{
    // ...
    Index = new HnswIndexOptions
    {
        M              = 16,   // Conexiones máximas de vecinos por nodo
        EfConstruction = 64,   // Alcance de búsqueda durante la construcción del índice
        EfSearch       = 40    // Alcance de búsqueda en tiempo de ejecución
    }
}

// IVFFlat
new PostgresOptions
{
    // ...
    Index = new IvfFlatIndexOptions
    {
        Lists  = 100,  // Número de listas invertidas
        Probes = 10    // Cuántas listas sondear en tiempo de consulta
    }
}

// Sin índice (escaneo secuencial)
new PostgresOptions { Index = new NoIndexOptions() }
```

### Modos de Búsqueda de Texto

Usado para el lado de palabras clave de la búsqueda híbrida:

| Modo | Mejor Para |
|------|------------|
| `TsVector` | Búsqueda full-text estándar — inglés, mayoría de idiomas occidentales |
| `Trigram` | Idiomas CJK (coreano, chino, japonés), coincidencia difusa |

```csharp
new PostgresOptions
{
    TextSearchMode   = TextSearchMode.Trigram,
    TextSearchConfig = "simple"     // Configuración de búsqueda de texto de PostgreSQL
}
```

### Estrategias de Distancia

| Valor | Operador Postgres | Notas |
|-------|------------------|-------|
| `Cosine` | `<=>` | 1 − similitud coseno (predeterminado) |
| `Euclidean` | `<->` | Distancia L2 |
| `InnerProduct` | `<#>` | Producto interno negativo — usar con vectores unitarios normalizados |

### Perfil de Búsqueda en Tiempo de Ejecución

Ajusta fino el recall vs. latencia en tiempo de consulta:

```csharp
var opts = new HnswSearchRuntimeOptions
{
    Profile = SearchProfile.HighRecall,  // Fast | Balanced | HighRecall
    EfSearch = 80                        // Sobreescribe ef_search de HNSW directamente
};

var results = await store.SearchAsync(queryVector, topK: 5, filter: null, runtimeOptions: opts);
```

### Todas las Opciones

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
