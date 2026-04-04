# 向量存储迁移

Mythosia.AI 提供迁移工具，用于在版本之间升级向量存储 Schema。主要用于从旧版集合 Schema（仅稠密向量）升级到当前的混合 Schema（稠密 + 稀疏向量）。

## 何时需要迁移

如果你使用早期版本的库（在引入混合检索之前）创建了 Qdrant 集合，该集合将处于**仅稠密向量** Schema。对其运行混合检索将失败或产生不正确的结果。

迁移会将你的集合升级到当前的**混合 Schema**（Schema 版本 2），每条记录同时存储稠密和稀疏向量。

## CLI 工具

安装迁移 CLI 工具：

```bash
dotnet tool install -g Mythosia.VectorDb.Tools
```

### 命令

**`migrate`** — 原地升级集合：

```bash
mythosia-vectordb migrate qdrant \
  --endpoint localhost:6334 \
  --source my-collection \
  [--api-key your-key] \
  [--replace]
```

- 不带 `--replace`：创建名为 `my-collection_migrated` 的新集合
- 带 `--replace`：成功后覆盖源集合（破坏性操作）

**`copy`** — 复制集合并升级 Schema：

```bash
mythosia-vectordb copy qdrant \
  --endpoint localhost:6334 \
  --source my-collection \
  --target my-collection-v2 \
  [--api-key your-key]
```

创建包含当前 Schema 的新目标集合，并从源集合复制所有记录。

## 编程式迁移

在代码中直接使用 `QdrantVectorStoreMigrator`：

```csharp
using Mythosia.VectorDb.Qdrant;

var migrator = new QdrantVectorStoreMigrator(new QdrantOptions
{
    Host           = "localhost",
    Port           = 6334,
    CollectionName = "my-collection",
    Dimension      = 1536
});
```

### 迁移前规划

在执行前检查迁移将做什么：

```csharp
var plan = await migrator.PlanAsync(new VectorStoreMigrationRequest
{
    Source = "my-collection"
});

Console.WriteLine($"当前 Schema：{plan.SchemaKind} v{plan.SchemaVersion}");
Console.WriteLine($"目标 Schema：{plan.TargetSchemaKind} v{plan.TargetSchemaVersion}");
Console.WriteLine($"需要迁移：{plan.MigrationRequired}");
```

### 带进度的迁移

```csharp
var progress = new Progress<VectorStoreMigrationProgress>(p =>
{
    Console.WriteLine($"[{p.Stage}] {p.ProcessedRecords}/{p.TotalRecords} — {p.Message}");
});

var result = await migrator.MigrateAsync(
    new VectorStoreMigrationRequest
    {
        Source           = "my-collection",
        ReplaceOnSuccess = false   // true = 完成后覆盖源集合
    },
    progress: progress
);

Console.WriteLine($"已迁移：{result.MigratedRecords} 条记录");
```

### 复制到新集合

复制集合并升级 Schema，不影响源集合：

```csharp
var result = await migrator.CopyAsync(
    source:   "my-collection",
    target:   "my-collection-v2",
    progress: progress,
    cancellationToken: default
);
```

## Schema 版本

Mythosia.AI 使用 Qdrant 中的特殊标记记录（ID `__mythosia_schema__`）内部追踪 Schema 版本。无需手动管理。

| Schema 版本 | 类型 | 说明 |
|-------------|------|------|
| 1 | `dense` | 仅稠密向量（旧版） |
| 2 | `hybrid` | 稠密 + 稀疏向量（当前） |

如果读取的集合没有 Schema 标记，将被视为版本 1（旧版）并标记为待迁移。

## 支持的提供商

| 提供商 | 迁移 | 复制 |
|--------|------|------|
| Qdrant | ✓ | ✓ |
| Pinecone | — | — |
| PostgreSQL | — | — |
| 内存 | — | — |
