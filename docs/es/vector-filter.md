# VectorFilter

`VectorFilter` es una API fluente para filtrar consultas al vector store por metadatos. Se aplica a `IVectorStore.SearchAsync`, `HybridSearchAsync` y consultas RAG.

## Igualdad Básica

```csharp
var filter = new VectorFilter()
    .Where("source", "manual.pdf")
    .Where("language", "es");
```

## Operadores de Comparación

```csharp
var filter = new VectorFilter()
    .WhereGreaterThan("date", "2024-01-01")
    .WhereLessThanOrEqual("priority", "3")
    .WhereNot("status", "archived");
```

| Método | Equivalente SQL |
|--------|----------------|
| `.Where(key, value)` | `key = value` |
| `.WhereNot(key, value)` | `key != value` |
| `.WhereGreaterThan(key, value)` | `key > value` |
| `.WhereGreaterThanOrEqual(key, value)` | `key >= value` |
| `.WhereLessThan(key, value)` | `key < value` |
| `.WhereLessThanOrEqual(key, value)` | `key <= value` |
| `.WhereLike(key, pattern)` | `key LIKE pattern` |

## Pertenencia a Conjunto

```csharp
var filter = new VectorFilter()
    .WhereIn("category", "legal", "compliance", "policy")
    .WhereNotIn("type", "draft", "archived");
```

## Existencia de Clave

```csharp
var filter = new VectorFilter()
    .WhereExists("reviewed_by")      // La clave debe estar presente
    .WhereNotExists("deprecated");   // La clave debe estar ausente
```

## Agrupamiento Lógico (AND / OR)

Las condiciones en el mismo nivel se combinan con AND por defecto. Usa `.Or()` para crear grupos OR:

```csharp
var filter = new VectorFilter()
    .Where("source", "manual.pdf")
    .Or(f => f
        .Where("type", "urgent")
        .Where("priority", "high")
    );
// source = "manual.pdf" AND (type = "urgent" OR priority = "high")
```

AND anidado:

```csharp
var filter = new VectorFilter()
    .Or(f => f
        .And(a => a.Where("lang", "es").Where("region", "mx"))
        .And(a => a.Where("lang", "pt").Where("region", "br"))
    );
// (lang = "es" AND region = "mx") OR (lang = "pt" AND region = "br")
```

## Umbral de Puntuación

```csharp
var filter = new VectorFilter()
    .Where("source", "faq.pdf")
    .WithMinScore(0.75);
```

## Usando con Vector Store

```csharp
var filter = new VectorFilter()
    .Where("document_type", "contract")
    .WhereGreaterThan("year", "2023");

var results = await vectorStore.SearchAsync(
    queryVector: embedding,
    topK: 5,
    filter: filter
);
```

## Usando con RAG

Pasa como `StoreFilter` en `RagQueryOptions`:

```csharp
var options = new RagQueryOptions
{
    StoreFilter = new VectorFilter()
        .Where("source", "manual-producto.pdf")
        .WithMinScore(0.7)
};

var response = await ragService.GetCompletionAsync("¿Cómo reinicio el dispositivo?", options);
```

## Combinando Filtros

Usa `AppendConditionsFrom` para combinar dos filtros (p.ej., mezclando un filtro de nivel de pipeline con un filtro por consulta):

```csharp
var baseFilter = new VectorFilter().Where("tenant", "acme");
var queryFilter = new VectorFilter().Where("language", "es");

baseFilter.AppendConditionsFrom(queryFilter);
// baseFilter ahora tiene ambas condiciones
```
