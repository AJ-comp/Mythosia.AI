# Filtrado

> 📍 **Pipeline de Pregunta y Respuesta:** [Reescritura de Consulta](rag-query-rewriting.md) → [Embedding](rag-embedding.md) → **`Filtrado`** → [Recuperación](rag-hybrid-search.md) → [Re-ranking](rag-reranking.md) → [Construcción de Contexto](rag-context-build.md)

## ¿Qué es el Filtrado?

El filtrado restringe **qué chunks se consideran** antes de que se ejecute la búsqueda por similitud. En lugar de buscar en todo el vector store, puedes limitar la búsqueda a subconjuntos específicos basados en metadatos o umbrales de puntuación.

## Filtrado por Metadatos

### Filtro por Consulta

Pasa un `VectorFilter` al consultar para acotar la búsqueda:

```csharp
var filter = new VectorFilter()
    .Where("category", "politica-devolucion");

var result = await pipeline.QueryAsync("¿Cómo obtengo un reembolso?", filter: filter);
```

### API de Filtro Fluente

```csharp
var filter = new VectorFilter()
    .Where("department", "engineering")
    .WhereNot("status", "archived")
    .WhereIn("region", "es-norte", "es-sur")
    .WhereGreaterThan("year", "2023")
    .WhereLike("title", "%kubernetes%");
```

| Método | Equivalente SQL | Descripción |
| --- | --- | --- |
| `Where` | `=` | Coincidencia exacta |
| `WhereNot` | `!=` | Diferente |
| `WhereIn` | `IN (...)` | Valor en un conjunto |
| `WhereNotIn` | `NOT IN (...)` | Valor no en un conjunto |
| `WhereGreaterThan` | `>` | Mayor que |
| `WhereGreaterThanOrEqual` | `>=` | Mayor o igual |
| `WhereLessThan` | `<` | Menor que |
| `WhereLessThanOrEqual` | `<=` | Menor o igual |
| `WhereLike` | `LIKE` | Coincidencia de patrón |
| `WhereExists` | `IS NOT NULL` | Clave de metadatos existe |
| `WhereNotExists` | `IS NULL` | Clave de metadatos no existe |

### Agrupación Lógica

```csharp
var filter = new VectorFilter()
    .Where("tenant", "acme")
    .Or(f => f
        .Where("category", "facturacion")
        .Where("category", "devolucion")
    );
// Coincide: tenant = "acme" AND (category = "facturacion" OR category = "devolucion")
```

## Filtro de Store a Nivel de Pipeline

Para condiciones que **siempre aplican** (como aislamiento de tenant), establece un `StoreFilter` en `RagQueryOptions`:

```csharp
var options = new RagQueryOptions
{
    StoreFilter = new VectorFilter().Where("tenant_id", currentTenantId)
};

var response = await ragService.GetCompletionAsync("pregunta", ragOptions: options);
```

## Filtrado por Puntuación

El umbral `MinScore` descarta chunks cuya puntuación de similitud cae por debajo de cierto nivel:

```csharp
var options = new RagQueryOptions
{
    FinalFilter = new RagFilter
    {
        TopK = 5,
        MinScore = 0.7
    }
};
```

## Próximos Pasos

- [Recuperación (Hybrid Search)](rag-hybrid-search.md)
- [Referencia VectorFilter](vector-filter.md)
- [Re-ranking](rag-reranking.md)
