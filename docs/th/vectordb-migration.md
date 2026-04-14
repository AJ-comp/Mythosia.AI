# Migration Vector Store

Mythosia.AI มีเครื่องมือ migration สำหรับอัปเกรด schema ของ vector store ระหว่างเวอร์ชัน ใช้หลักเมื่ออัปเดตจาก schema แบบเก่า (dense-only) มาเป็น hybrid schema ปัจจุบัน (dense + sparse vectors)

## เมื่อไหรต้องทำ Migration

ถ้าคุณสร้าง Qdrant collection ด้วยเวอร์ชันไลบรารีที่เก่ากว่า (ก่อนที่ hybrid search จะถูกเพิ่มเข้ามา) collection นั้นจะอยู่ใน schema แบบ **dense-only** การรัน hybrid search บน collection ดังกล่าวจะล้มเหลวหรือให้ผลลัพธ์ที่ไม่ถูกต้อง

Migration จะอัปเกรด collection ของคุณไปยัง **hybrid schema** ปัจจุบัน (schema version 2) ซึ่งเก็บทั้ง dense และ sparse vectors ต่อ record

## เครื่องมือ CLI

ติดตั้งเครื่องมือ CLI สำหรับ migration:

```bash
dotnet tool install -g Mythosia.VectorDb.Tools
```

### คำสั่ง

**`migrate`** — อัปเกรด collection แบบ in-place:

```bash
mythosia-vectordb migrate qdrant \
  --endpoint localhost:6334 \
  --source my-collection \
  [--api-key your-key] \
  [--replace]
```

- ไม่มี `--replace`: สร้าง collection ใหม่ชื่อ `my-collection_migrated`
- มี `--replace`: เขียนทับ collection ต้นทางเมื่อสำเร็จ (ทำลายข้อมูลเดิม)

**`copy`** — คัดลอก collection พร้อมอัปเกรด schema:

```bash
mythosia-vectordb copy qdrant \
  --endpoint localhost:6334 \
  --source my-collection \
  --target my-collection-v2 \
  [--api-key your-key]
```

สร้าง collection ปลายทางใหม่ด้วย schema ปัจจุบันและคัดลอก record ทั้งหมดจากต้นทาง

## Migration ด้วยโค้ด

ใช้ `QdrantVectorStoreMigrator` โดยตรงในโค้ด:

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

### วางแผนก่อน Migrate

ตรวจสอบว่า migration จะทำอะไรก่อนรัน:

```csharp
var plan = await migrator.PlanAsync(new VectorStoreMigrationRequest
{
    Source = "my-collection"
});

Console.WriteLine($"Schema ปัจจุบัน: {plan.SchemaKind} v{plan.SchemaVersion}");
Console.WriteLine($"Schema เป้าหมาย: {plan.TargetSchemaKind} v{plan.TargetSchemaVersion}");
Console.WriteLine($"จำเป็นต้อง migrate: {plan.MigrationRequired}");
```

### รัน Migration พร้อมติดตามความคืบหน้า

```csharp
var progress = new Progress<VectorStoreMigrationProgress>(p =>
{
    Console.WriteLine($"[{p.Stage}] {p.ProcessedRecords}/{p.TotalRecords} — {p.Message}");
});

var result = await migrator.MigrateAsync(
    new VectorStoreMigrationRequest
    {
        Source           = "my-collection",
        ReplaceOnSuccess = false   // true = เขียนทับต้นทางเมื่อเสร็จ
    },
    progress: progress
);

Console.WriteLine($"Migrate แล้ว: {result.MigratedRecords} records");
```

### คัดลอกไปยัง Collection ใหม่

คัดลอก collection พร้อมอัปเกรด schema โดยไม่แตะต้นทาง:

```csharp
var result = await migrator.CopyAsync(
    source:   "my-collection",
    target:   "my-collection-v2",
    progress: progress,
    cancellationToken: default
);
```

## การจัดการเวอร์ชัน Schema

Mythosia.AI ติดตามเวอร์ชัน schema ภายในผ่าน record พิเศษใน Qdrant (ID `__mythosia_schema__`) คุณไม่ต้องจัดการสิ่งนี้เอง

| เวอร์ชัน Schema | ประเภท | คำอธิบาย |
|---------------|------|-------------|
| 1 | `dense` | Dense vectors เท่านั้น (legacy) |
| 2 | `hybrid` | Dense + sparse vectors (ปัจจุบัน) |

ถ้าอ่าน collection ที่ไม่มี schema marker จะถูกถือว่าเป็น version 1 (legacy) และจะถูกทำเครื่องหมายว่าต้องทำ migration

## Backend ที่รองรับ

| Provider | Migrate | Copy |
|----------|---------|------|
| Qdrant | ✓ | ✓ |
| Pinecone | — | — |
| PostgreSQL | — | — |
| InMemory | — | — |
