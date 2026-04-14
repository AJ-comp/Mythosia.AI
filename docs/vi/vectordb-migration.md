# Migration Vector Store

Mythosia.AI cung cấp công cụ migration để nâng cấp schema vector store giữa các phiên bản. Chủ yếu dùng khi cập nhật từ schema collection cũ (chỉ có dense vector) sang schema hybrid hiện tại (dense + sparse vectors).

## Khi nào cần migration

Nếu bạn tạo Qdrant collection với phiên bản thư viện cũ hơn (trước khi hybrid search được giới thiệu), collection đó sẽ ở dạng schema **dense-only**. Chạy hybrid search trên đó sẽ thất bại hoặc cho kết quả không chính xác.

Migration nâng cấp collection của bạn lên **hybrid schema** hiện tại (schema version 2), lưu trữ cả dense và sparse vectors cho mỗi record.

## Công cụ CLI

Cài đặt công cụ CLI migration:

```bash
dotnet tool install -g Mythosia.VectorDb.Tools
```

### Lệnh

**`migrate`** — Nâng cấp collection tại chỗ:

```bash
mythosia-vectordb migrate qdrant \
  --endpoint localhost:6334 \
  --source my-collection \
  [--api-key your-key] \
  [--replace]
```

- Không có `--replace`: tạo collection mới tên `my-collection_migrated`
- Có `--replace`: ghi đè collection nguồn khi thành công (phá hủy dữ liệu gốc)

**`copy`** — Sao chép collection kèm nâng cấp schema:

```bash
mythosia-vectordb copy qdrant \
  --endpoint localhost:6334 \
  --source my-collection \
  --target my-collection-v2 \
  [--api-key your-key]
```

Tạo collection đích mới với schema hiện tại và sao chép toàn bộ record từ nguồn.

## Migration bằng code

Dùng `QdrantVectorStoreMigrator` trực tiếp trong code:

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

### Lập kế hoạch trước khi migration

Kiểm tra migration sẽ làm gì trước khi chạy:

```csharp
var plan = await migrator.PlanAsync(new VectorStoreMigrationRequest
{
    Source = "my-collection"
});

Console.WriteLine($"Schema hiện tại: {plan.SchemaKind} v{plan.SchemaVersion}");
Console.WriteLine($"Schema mục tiêu: {plan.TargetSchemaKind} v{plan.TargetSchemaVersion}");
Console.WriteLine($"Cần migration: {plan.MigrationRequired}");
```

### Chạy migration theo dõi tiến độ

```csharp
var progress = new Progress<VectorStoreMigrationProgress>(p =>
{
    Console.WriteLine($"[{p.Stage}] {p.ProcessedRecords}/{p.TotalRecords} — {p.Message}");
});

var result = await migrator.MigrateAsync(
    new VectorStoreMigrationRequest
    {
        Source           = "my-collection",
        ReplaceOnSuccess = false   // true = ghi đè nguồn khi hoàn thành
    },
    progress: progress
);

Console.WriteLine($"Đã migrate: {result.MigratedRecords} records");
```

### Sao chép sang collection mới

Sao chép collection và nâng cấp schema mà không động vào nguồn:

```csharp
var result = await migrator.CopyAsync(
    source:   "my-collection",
    target:   "my-collection-v2",
    progress: progress,
    cancellationToken: default
);
```

## Quản lý phiên bản schema

Mythosia.AI theo dõi phiên bản schema nội bộ qua một record đặc biệt trong Qdrant (ID `__mythosia_schema__`). Bạn không cần quản lý thủ công.

| Phiên bản Schema | Loại | Mô tả |
|---------------|------|-------------|
| 1 | `dense` | Chỉ dense vectors (legacy) |
| 2 | `hybrid` | Dense + sparse vectors (hiện tại) |

Nếu đọc một collection không có schema marker, nó sẽ được coi là version 1 (legacy) và được đánh dấu cần migration.

## Backend được hỗ trợ

| Provider | Migrate | Copy |
|----------|---------|------|
| Qdrant | ✓ | ✓ |
| Pinecone | — | — |
| PostgreSQL | — | — |
| InMemory | — | — |
