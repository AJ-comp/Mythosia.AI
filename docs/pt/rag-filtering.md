# Filtragem

> 📍 **Pipeline de Pergunta e Resposta:** [Reescrita de Consulta](rag-query-rewriting.md) → [Embedding](rag-embedding.md) → **`Filtragem`** → [Recuperação](rag-hybrid-search.md) → [Re-ranking](rag-reranking.md) → [Construção de Contexto](rag-context-build.md)

## O que é Filtragem?

A filtragem restringe **quais chunks são considerados** antes da busca por similaridade. Em vez de pesquisar em todo o vector store, você pode limitar a busca a subconjuntos específicos com base em metadados ou limites de pontuação.

## Filtragem por Metadados

### Filtro por Consulta

Passe um `VectorFilter` ao consultar para delimitar a busca:

```csharp
var filter = new VectorFilter()
    .Where("category", "politica-reembolso");

var result = await pipeline.QueryAsync("Como obter reembolso?", filter: filter);
```

### API de Filtro Fluente

```csharp
var filter = new VectorFilter()
    .Where("department", "engineering")
    .WhereNot("status", "archived")
    .WhereIn("region", "br-sul", "br-norte")
    .WhereGreaterThan("year", "2023")
    .WhereLike("title", "%kubernetes%");
```

| Método | Equivalente SQL | Descrição |
| --- | --- | --- |
| `Where` | `=` | Correspondência exata |
| `WhereNot` | `!=` | Diferente |
| `WhereIn` | `IN (...)` | Valor em um conjunto |
| `WhereNotIn` | `NOT IN (...)` | Valor não em um conjunto |
| `WhereGreaterThan` | `>` | Maior que |
| `WhereGreaterThanOrEqual` | `>=` | Maior ou igual |
| `WhereLessThan` | `<` | Menor que |
| `WhereLessThanOrEqual` | `<=` | Menor ou igual |
| `WhereLike` | `LIKE` | Correspondência de padrão |
| `WhereExists` | `IS NOT NULL` | Chave de metadados existe |
| `WhereNotExists` | `IS NULL` | Chave de metadados não existe |

### Agrupamento Lógico

```csharp
var filter = new VectorFilter()
    .Where("tenant", "acme")
    .Or(f => f
        .Where("category", "faturamento")
        .Where("category", "reembolso")
    );
// Coincide: tenant = "acme" AND (category = "faturamento" OR category = "reembolso")
```

## Filtro de Store em Nível de Pipeline

Para condições que **sempre se aplicam** (como isolamento de tenant), defina um `StoreFilter` em `RagQueryOptions`:

```csharp
var options = new RagQueryOptions
{
    StoreFilter = new VectorFilter().Where("tenant_id", currentTenantId)
};

var response = await ragService.GetCompletionAsync("pergunta", ragOptions: options);
```

## Filtragem por Pontuação

O limite `MinScore` descarta chunks cuja pontuação de similaridade fica abaixo de um certo nível:

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

## Próximos Passos

- [Recuperação (Hybrid Search)](rag-hybrid-search.md)
- [Referência VectorFilter](vector-filter.md)
- [Re-ranking](rag-reranking.md)
