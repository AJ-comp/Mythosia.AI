# Visión General de la Base de Datos Vectorial

Mythosia.AI proporciona una abstracción unificada `IVectorStore` que funciona con múltiples backends de bases de datos vectoriales. Escribes tu aplicación contra la interfaz una sola vez y puedes cambiar de backend sin modificar ninguna lógica de recuperación.

## Interfaz Principal: `IVectorStore`

```csharp
// Upsert
Task UpsertAsync(VectorRecord record, CancellationToken cancellationToken = default);
Task UpsertBatchAsync(IEnumerable<VectorRecord> records, CancellationToken cancellationToken = default);

// Búsqueda
Task<IReadOnlyList<VectorSearchResult>> SearchAsync(
    float[] queryVector, int topK = 5, VectorFilter? filter = null,
    CancellationToken cancellationToken = default);

Task<IReadOnlyList<VectorSearchResult>> HybridSearchAsync(
    float[] denseVector, string query, int topK = 5, VectorFilter? filter = null,
    CancellationToken cancellationToken = default);

// Obtener por ID
Task<VectorRecord?> GetAsync(string id, VectorFilter? filter = null,
    CancellationToken cancellationToken = default);
Task<IReadOnlyList<VectorRecord>> GetBatchAsync(IEnumerable<string> ids,
    VectorFilter? filter = null, CancellationToken cancellationToken = default);

// Eliminar
Task DeleteAsync(string id, VectorFilter? filter = null,
    CancellationToken cancellationToken = default);
Task DeleteByFilterAsync(VectorFilter filter, CancellationToken cancellationToken = default);
Task ReplaceByFilterAsync(VectorFilter filter, IReadOnlyList<VectorRecord> records,
    CancellationToken cancellationToken = default);

// Utilidades
Task<long> CountAsync(VectorFilter? filter = null, CancellationToken cancellationToken = default);
Task VerifyConnectionAsync(CancellationToken cancellationToken = default);
```

## Modelos de Datos

### VectorRecord

Cada entrada almacenada es un `VectorRecord`:

```csharp
public class VectorRecord
{
    public string Id { get; set; }                           // Identificador único
    public float[] Vector { get; set; }                      // Vector de embedding
    public string Content { get; set; }                      // Contenido textual original
    public Dictionary<string, string> Metadata { get; set; } // Metadatos clave-valor personalizados
}
```

Usa el diccionario `Metadata` para cualquier campo personalizado — archivo fuente, idioma, fecha, categoría, etc.:

```csharp
var record = new VectorRecord
{
    Id = Guid.NewGuid().ToString(),
    Vector = await embeddingService.GetEmbeddingAsync("Algún texto"),
    Content = "Algún texto",
    Metadata = new Dictionary<string, string>
    {
        ["source"] = "manual.pdf",
        ["language"] = "es",
        ["date"] = "2024-01-15",
        ["category"] = "policy"
    }
};
```

### VectorSearchResult

Los resultados de búsqueda combinan un registro con su puntuación de similitud:

```csharp
public class VectorSearchResult
{
    public VectorRecord Record { get; set; }
    public double Score { get; set; }  // 0.0–1.0 (mayor = más similar)
}
```

## Backends Disponibles

| Backend | Paquete | Caso de Uso |
|---------|---------|-------------|
| **In-Memory** | `Mythosia.VectorDb.InMemory` | Desarrollo, pruebas, demos |
| **Qdrant** | `Mythosia.VectorDb.Qdrant` | Producción, hybrid search nativo |
| **Pinecone** | `Mythosia.VectorDb.Pinecone` | Servicio gestionado serverless |
| **PostgreSQL** | `Mythosia.VectorDb.Postgres` | Despliegues Postgres existentes, ACID |

Todos los backends implementan la misma interfaz `IVectorStore`. Consulta [Configuración de Backends](vectordb-backends.md) para la configuración por backend.

## Inyección de Dependencias

Registra cualquier backend como `IVectorStore`:

```csharp
// In-Memory
services.AddSingleton<IVectorStore>(new InMemoryVectorStore());

// Qdrant
services.AddSingleton<IVectorStore>(new QdrantStore(new QdrantOptions
{
    CollectionName = "mi-coleccion",
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

## Ejecución de Filtros por Backend

Las condiciones de `VectorFilter` se delegan al backend siempre que es posible:

| Operador | InMemory | Qdrant | Pinecone | Postgres |
|----------|----------|--------|----------|----------|
| Eq / Ne | Cliente | **Servidor** | **Servidor** | **SQL** |
| In / NotIn | Cliente | **Servidor** | **Servidor** | **SQL** |
| Gt / Gte / Lt / Lte | Cliente | Cliente | **Servidor** | **SQL** |
| Like | Cliente | Cliente | Cliente | **SQL** |
| Exists / NotExists | Cliente | Cliente | Cliente | **SQL** |

Postgres tiene pushdown SQL completo para todos los operadores. Qdrant y Pinecone delegan al servidor los operadores de igualdad, pertenencia a conjunto y comparación.

> **Nota:** Qdrant descarta silenciosamente los operadores de filtro no soportados (`Like`, `Exists`, `NotExists`) — no se aplican del lado del cliente. Si necesitas estos operadores con Qdrant, aplica filtrado adicional sobre los resultados devueltos.
