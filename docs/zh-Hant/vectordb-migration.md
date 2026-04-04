# 向量儲存遷移

Mythosia.AI 提供遷移工具，用於在版本之間升級向量儲存 Schema。主要用於從舊版集合 Schema（僅稠密向量）升級到目前的混合 Schema（稠密 + 稀疏向量）。

## 何時需要遷移

如果你使用早期版本的程式庫（在引入混合檢索之前）建立了 Qdrant 集合，該集合將處於**僅稠密向量** Schema。對其執行混合檢索將失敗或產生不正確的結果。

遷移會將你的集合升級到目前的**混合 Schema**（Schema 版本 2），每筆記錄同時儲存稠密和稀疏向量。

## CLI 工具

安裝遷移 CLI 工具：

```bash
dotnet tool install -g Mythosia.VectorDb.Tools
```

### 命令

**`migrate`** — 原地升級集合：

```bash
mythosia-vectordb migrate qdrant \
  --endpoint localhost:6334 \
  --source my-collection \
  [--api-key your-key] \
  [--replace]
```

- 不帶 `--replace`：建立名為 `my-collection_migrated` 的新集合
- 帶 `--replace`：成功後覆蓋來源集合（破壞性操作）

**`copy`** — 複製集合並升級 Schema：

```bash
mythosia-vectordb copy qdrant \
  --endpoint localhost:6334 \
  --source my-collection \
  --target my-collection-v2 \
  [--api-key your-key]
```

建立包含目前 Schema 的新目標集合，並從來源集合複製所有記錄。

## 程式碼式遷移

在程式碼中直接使用 `QdrantVectorStoreMigrator`：

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

### 遷移前規劃

在執行前檢查遷移將做什麼：

```csharp
var plan = await migrator.PlanAsync(new VectorStoreMigrationRequest
{
    Source = "my-collection"
});

Console.WriteLine($"目前 Schema：{plan.SchemaKind} v{plan.SchemaVersion}");
Console.WriteLine($"目標 Schema：{plan.TargetSchemaKind} v{plan.TargetSchemaVersion}");
Console.WriteLine($"需要遷移：{plan.MigrationRequired}");
```

### 帶進度的遷移

```csharp
var progress = new Progress<VectorStoreMigrationProgress>(p =>
{
    Console.WriteLine($"[{p.Stage}] {p.ProcessedRecords}/{p.TotalRecords} — {p.Message}");
});

var result = await migrator.MigrateAsync(
    new VectorStoreMigrationRequest
    {
        Source           = "my-collection",
        ReplaceOnSuccess = false   // true = 完成後覆蓋來源集合
    },
    progress: progress
);

Console.WriteLine($"已遷移：{result.MigratedRecords} 筆記錄");
```

### 複製到新集合

複製集合並升級 Schema，不影響來源集合：

```csharp
var result = await migrator.CopyAsync(
    source:   "my-collection",
    target:   "my-collection-v2",
    progress: progress,
    cancellationToken: default
);
```

## Schema 版本

Mythosia.AI 使用 Qdrant 中的特殊標記記錄（ID `__mythosia_schema__`）內部追蹤 Schema 版本。無需手動管理。

| Schema 版本 | 類型 | 說明 |
|-------------|------|------|
| 1 | `dense` | 僅稠密向量（舊版） |
| 2 | `hybrid` | 稠密 + 稀疏向量（目前） |

如果讀取的集合沒有 Schema 標記，將被視為版本 1（舊版）並標記為待遷移。

## 支援的供應商

| 供應商 | 遷移 | 複製 |
|--------|------|------|
| Qdrant | ✓ | ✓ |
| Pinecone | — | — |
| PostgreSQL | — | — |
| 記憶體 | — | — |
