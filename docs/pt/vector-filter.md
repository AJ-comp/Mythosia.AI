# VectorFilter

`VectorFilter` é uma API fluente para filtrar consultas ao vector store por metadados. Aplica-se a `IVectorStore.SearchAsync`, `HybridSearchAsync` e consultas RAG.

## Igualdade Básica

```csharp
var filter = new VectorFilter()
    .Where("source", "manual.pdf")
    .Where("language", "pt");
```

## Operadores de Comparação

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

## Pertencimento a Conjunto

```csharp
var filter = new VectorFilter()
    .WhereIn("category", "legal", "compliance", "policy")
    .WhereNotIn("type", "draft", "archived");
```

## Existência de Chave

```csharp
var filter = new VectorFilter()
    .WhereExists("reviewed_by")      // Chave deve estar presente
    .WhereNotExists("deprecated");   // Chave deve estar ausente
```

## Agrupamento Lógico (AND / OR)

As condições no mesmo nível são combinadas com AND por padrão. Use `.Or()` para criar grupos OR:

```csharp
var filter = new VectorFilter()
    .Where("source", "manual.pdf")
    .Or(f => f
        .Where("type", "urgent")
        .Where("priority", "high")
    );
// source = "manual.pdf" AND (type = "urgent" OR priority = "high")
```

AND aninhado:

```csharp
var filter = new VectorFilter()
    .Or(f => f
        .And(a => a.Where("lang", "pt").Where("region", "br"))
        .And(a => a.Where("lang", "es").Where("region", "ar"))
    );
// (lang = "pt" AND region = "br") OR (lang = "es" AND region = "ar")
```

## Limiar de Pontuação

```csharp
var filter = new VectorFilter()
    .Where("source", "faq.pdf")
    .WithMinScore(0.75);
```

## Usando com Vector Store

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

## Usando com RAG

Passe como `StoreFilter` em `RagQueryOptions`:

```csharp
var options = new RagQueryOptions
{
    StoreFilter = new VectorFilter()
        .Where("source", "manual-produto.pdf")
        .WithMinScore(0.7)
};

var response = await ragService.GetCompletionAsync("Como resetar o dispositivo?", options);
```

## Mesclando Filtros

Use `AppendConditionsFrom` para combinar dois filtros (ex: mesclando um filtro de nível de pipeline com um filtro por consulta):

```csharp
var baseFilter = new VectorFilter().Where("tenant", "acme");
var queryFilter = new VectorFilter().Where("language", "pt");

baseFilter.AppendConditionsFrom(queryFilter);
// baseFilter agora tem ambas as condições
```
