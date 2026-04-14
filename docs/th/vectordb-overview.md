# ภาพรวมฐานข้อมูล Vector

Mythosia.AI มี abstraction `IVectorStore` ที่ใช้งานได้กับ vector database backend หลายตัว คุณเขียน code ต่อ interface ครั้งเดียวและสลับ backend ได้โดยไม่ต้องเปลี่ยน logic การดึงข้อมูล

## Interface หลัก: `IVectorStore`

```csharp
// Upsert
Task UpsertAsync(VectorRecord record, CancellationToken cancellationToken = default);
Task UpsertBatchAsync(IEnumerable<VectorRecord> records, CancellationToken cancellationToken = default);

// ค้นหา
Task<IReadOnlyList<VectorSearchResult>> SearchAsync(
    float[] queryVector, int topK = 5, VectorFilter? filter = null,
    CancellationToken cancellationToken = default);

Task<IReadOnlyList<VectorSearchResult>> HybridSearchAsync(
    float[] denseVector, string query, int topK = 5, VectorFilter? filter = null,
    CancellationToken cancellationToken = default);

// ดึงตาม ID
Task<VectorRecord?> GetAsync(string id, VectorFilter? filter = null,
    CancellationToken cancellationToken = default);
Task<IReadOnlyList<VectorRecord>> GetBatchAsync(IEnumerable<string> ids,
    VectorFilter? filter = null, CancellationToken cancellationToken = default);

// ลบ
Task DeleteAsync(string id, VectorFilter? filter = null,
    CancellationToken cancellationToken = default);
Task DeleteByFilterAsync(VectorFilter filter, CancellationToken cancellationToken = default);
Task ReplaceByFilterAsync(VectorFilter filter, IReadOnlyList<VectorRecord> records,
    CancellationToken cancellationToken = default);

// Utility
Task<long> CountAsync(VectorFilter? filter = null, CancellationToken cancellationToken = default);
Task VerifyConnectionAsync(CancellationToken cancellationToken = default);
```

## Data Model

### VectorRecord

ทุก entry ที่เก็บไว้เป็น `VectorRecord`:

```csharp
public class VectorRecord
{
    public string Id { get; set; }                           // ตัวระบุเฉพาะ
    public float[] Vector { get; set; }                      // Embedding vector
    public string Content { get; set; }                      // เนื้อหาข้อความต้นฉบับ
    public Dictionary<string, string> Metadata { get; set; } // Metadata key-value แบบกำหนดเอง
}
```

ใช้ dictionary `Metadata` สำหรับข้อมูลเพิ่มเติมใด ๆ — ไฟล์ต้นทาง ภาษา วันที่ หมวดหมู่ เป็นต้น:

```csharp
var record = new VectorRecord
{
    Id = Guid.NewGuid().ToString(),
    Vector = await embeddingService.GetEmbeddingAsync("ข้อความบางส่วน"),
    Content = "ข้อความบางส่วน",
    Metadata = new Dictionary<string, string>
    {
        ["source"] = "manual.pdf",
        ["language"] = "th",
        ["date"] = "2024-01-15",
        ["category"] = "policy"
    }
};
```

### VectorSearchResult

ผลการค้นหาจับคู่ record กับคะแนน similarity:

```csharp
public class VectorSearchResult
{
    public VectorRecord Record { get; set; }
    public double Score { get; set; }  // 0.0–1.0 (สูงกว่า = คล้ายกว่า)
}
```

## Backend ที่รองรับ

| Backend | Package | กรณีใช้งาน |
|---------|---------|----------|
| **In-Memory** | `Mythosia.VectorDb.InMemory` | พัฒนา ทดสอบ demo |
| **Qdrant** | `Mythosia.VectorDb.Qdrant` | Production, native hybrid search |
| **Pinecone** | `Mythosia.VectorDb.Pinecone` | Serverless managed service |
| **PostgreSQL** | `Mythosia.VectorDb.Postgres` | Postgres ที่มีอยู่แล้ว ACID |

ทุก backend implement `IVectorStore` interface เดียวกัน ดู [การตั้งค่า Backend](vectordb-backends.md) สำหรับการตั้งค่าแต่ละตัว

## Dependency Injection

Register backend ใด ๆ เป็น `IVectorStore`:

```csharp
// In-Memory
services.AddSingleton<IVectorStore>(new InMemoryVectorStore());

// Qdrant
services.AddSingleton<IVectorStore>(new QdrantStore(new QdrantOptions
{
    CollectionName = "my-collection",
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

## การ Filter ตาม Backend

เงื่อนไข `VectorFilter` ถูก push down ไปยัง backend เมื่อทำได้:

| Operator | InMemory | Qdrant | Pinecone | Postgres |
|----------|----------|--------|----------|----------|
| Eq / Ne | Client | **Server** | **Server** | **SQL** |
| In / NotIn | Client | **Server** | **Server** | **SQL** |
| Gt / Gte / Lt / Lte | Client | Client | **Server** | **SQL** |
| Like | Client | Client | Client | **SQL** |
| Exists / NotExists | Client | Client | Client | **SQL** |

Postgres มี SQL pushdown สำหรับทุก operator Qdrant และ Pinecone push down สำหรับ equality, set membership และ comparison

> **หมายเหตุ:** Qdrant จะละเว้น filter operator ที่ไม่รองรับ (`Like`, `Exists`, `NotExists`) โดยไม่แจ้งเตือน — operator เหล่านั้นจะไม่ถูกใช้ฝั่ง client ถ้าต้องการ operator เหล่านี้กับ Qdrant ให้กรองเพิ่มเติมบนผลลัพธ์ที่ได้รับ
