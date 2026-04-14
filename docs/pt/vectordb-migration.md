# Migração do Vector Store

Mythosia.AI inclui ferramentas de migração para atualizar schemas de vector store entre versões. O principal caso de uso é a atualização de coleções com schema antigo (somente vetores densos) para o schema híbrido atual (vetores densos + esparsos).

## Quando Você Precisa Migrar

Se você criou uma coleção Qdrant com uma versão anterior da biblioteca (antes da introdução do hybrid search), a coleção estará no schema **somente denso**. Executar hybrid search contra ela falhará ou produzirá resultados incorretos.

A migração atualiza sua coleção para o **schema híbrido** atual (schema versão 2), que armazena vetores densos e esparsos por registro.

## Ferramenta CLI

Instale a ferramenta CLI de migração:

```bash
dotnet tool install -g Mythosia.VectorDb.Tools
```

### Comandos

**`migrate`** — Atualiza uma coleção no lugar:

```bash
mythosia-vectordb migrate qdrant \
  --endpoint localhost:6334 \
  --source minha-colecao \
  [--api-key sua-chave] \
  [--replace]
```

- Sem `--replace`: cria uma nova coleção chamada `minha-colecao_migrated`
- Com `--replace`: sobrescreve a coleção de origem no sucesso (destrutivo)

**`copy`** — Copia uma coleção com atualização de schema:

```bash
mythosia-vectordb copy qdrant \
  --endpoint localhost:6334 \
  --source minha-colecao \
  --target minha-colecao-v2 \
  [--api-key sua-chave]
```

Cria uma nova coleção de destino com o schema atual e copia todos os registros da origem.

## Migração Programática

Use `QdrantVectorStoreMigrator` diretamente no código:

```csharp
using Mythosia.VectorDb.Qdrant;

var migrator = new QdrantVectorStoreMigrator(new QdrantOptions
{
    Host           = "localhost",
    Port           = 6334,
    CollectionName = "minha-colecao",
    Dimension      = 1536
});
```

### Planeje Antes de Migrar

Verifique o que a migração fará antes de executá-la:

```csharp
var plan = await migrator.PlanAsync(new VectorStoreMigrationRequest
{
    Source = "minha-colecao"
});

Console.WriteLine($"Schema atual: {plan.SchemaKind} v{plan.SchemaVersion}");
Console.WriteLine($"Schema alvo:  {plan.TargetSchemaKind} v{plan.TargetSchemaVersion}");
Console.WriteLine($"Migração necessária: {plan.MigrationRequired}");
```

### Executar Migração com Progresso

```csharp
var progress = new Progress<VectorStoreMigrationProgress>(p =>
{
    Console.WriteLine($"[{p.Stage}] {p.ProcessedRecords}/{p.TotalRecords} — {p.Message}");
});

var result = await migrator.MigrateAsync(
    new VectorStoreMigrationRequest
    {
        Source           = "minha-colecao",
        ReplaceOnSuccess = false   // true = sobrescreve a origem ao concluir
    },
    progress: progress
);

Console.WriteLine($"Migrados: {result.MigratedRecords} registros");
```

### Copiar para uma Nova Coleção

Copia uma coleção atualizando seu schema, sem tocar na origem:

```csharp
var result = await migrator.CopyAsync(
    source:   "minha-colecao",
    target:   "minha-colecao-v2",
    progress: progress,
    cancellationToken: default
);
```

## Versionamento de Schema

Mythosia.AI rastreia a versão do schema internamente usando um registro marcador especial no Qdrant (ID `__mythosia_schema__`). Você não precisa gerenciar isso manualmente.

| Versão do Schema | Tipo | Descrição |
|-----------------|------|-----------|
| 1 | `dense` | Somente vetores densos (legado) |
| 2 | `hybrid` | Vetores densos + esparsos (atual) |

Se você ler uma coleção que não tem marcador de schema, ela é tratada como versão 1 (legado) e marcada para migração.

## Providers Suportados

| Provider | Migrate | Copy |
|----------|---------|------|
| Qdrant | ✓ | ✓ |
| Pinecone | — | — |
| PostgreSQL | — | — |
| InMemory | — | — |
