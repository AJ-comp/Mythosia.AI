# การตั้งค่า Backend

## In-Memory

Backend ที่ง่ายที่สุด — ไม่ต้องมี dependency ภายนอก ข้อมูลเก็บใน RAM และหายไปเมื่อ process จบ เหมาะสำหรับการพัฒนา ทดสอบ และ demo

```bash
dotnet add package Mythosia.VectorDb.InMemory
```

```csharp
using Mythosia.VectorDb.InMemory;

var store = new InMemoryVectorStore();
```

**Hybrid search ในตัว**: RRF (Reciprocal Rank Fusion) รวม cosine similarity และ BM25 keyword score

### การใช้งานพร้อมกันและการแก้ไขเรคคอร์ด

เมื่ออัปเดตที่เก็บข้อมูลร่วมระหว่างการค้นหา เนื้อหาและดัชนีคำค้นต้องเป็นเวอร์ชันเดียวกัน `InMemoryVectorStore` ประสานการเขียน การลบ และการอ่าน เพื่อให้การค้นหาแบบเวกเตอร์ ข้อความ หรือไฮบริดแต่ละครั้งเห็นสถานะที่สอดคล้องกัน การค้นหาทั้งสองส่วนของไฮบริดใช้สถานะเดียวกันด้วย

ที่เก็บข้อมูลจะคัดลอกเรคคอร์ดขาเข้า รวมทั้งอาร์เรย์เวกเตอร์และเมทาดาทา และคืนสำเนาแยกในการอ่าน การค้นหา และการวินิจฉัย การแก้ไขอ็อบเจ็กต์ขาเข้าหรือเรคคอร์ดที่คืนมาไม่เปลี่ยนข้อมูลที่บันทึกไว้ ให้เรียก `UpsertAsync` อีกครั้งเพื่อบันทึกการเปลี่ยนแปลง อย่าแก้ไขเรคคอร์ด เวกเตอร์ หรือเมทาดาทาขาเข้าขณะที่การเรียกกำลังคัดลอกหรืออ่านข้อมูลเหล่านั้น

`CancellationToken` ที่ส่งเข้ามาสามารถยกเลิกการเรียกขณะรอให้การดำเนินการอื่นปล่อยล็อกของที่เก็บข้อมูลได้ การยกเลิกการรอไม่ได้หยุดการดำเนินการที่กำลังใช้ที่เก็บข้อมูลอยู่ด้วยตัวมันเอง เมื่อเริ่มอัปเดตเรคคอร์ดแล้ว การยกเลิกจะไม่แทรกระหว่างการอัปเดตเนื้อหากับดัชนี

การยกเลิกแบตช์อาจคงเรคคอร์ดที่เขียนแล้วไว้ `ReplaceByFilterAsync` ยังคงลบแล้วแทรกแบบแบตช์ตามลำดับโดยไม่มีทรานแซกชัน การค้นหาอื่นอาจเห็นช่วงที่ข้อมูลว่างระหว่างสองขั้นตอน และข้อผิดพลาดหรือการยกเลิกจะไม่ย้อนกลับการเขียนที่เสร็จแล้ว

### Diagnostics

```csharp
// แสดง record ทั้งหมดที่เก็บไว้
var all = await store.ListAllRecordsAsync();
Console.WriteLine($"ทั้งหมด: {store.GetTotalRecordCount()}");

// ดู similarity score ดิบ
var scored = await store.ScoredListAsync(queryVector);
foreach (var r in scored)
    Console.WriteLine($"[{r.Score:F3}] {r.Record.Content[..60]}");
```

---

## Qdrant

Vector database ระดับ production ที่มี native hybrid search รัน standalone ผ่าน Docker หรือ Qdrant Cloud

```bash
dotnet add package Mythosia.VectorDb.Qdrant
```

```bash
# เริ่ม Qdrant ในเครื่อง
docker run -p 6333:6333 -p 6334:6334 qdrant/qdrant
```

```csharp
using Mythosia.VectorDb.Qdrant;

var store = new QdrantStore(new QdrantOptions
{
    Host             = "localhost",
    Port             = 6334,           // gRPC port
    CollectionName   = "my-docs",
    Dimension        = 1536,           // ต้องตรงกับ embedding model
    AutoCreateCollection = true        // สร้าง collection เมื่อ upsert ครั้งแรก
});
```

### Option ทั้งหมด

```csharp
new QdrantOptions
{
    Host                   = "localhost",
    Port                   = 6334,
    UseTls                 = false,
    ApiKey                 = null,             // ต้องใช้กับ Qdrant Cloud

    CollectionName         = "my-collection",  // จำเป็น
    Dimension              = 1536,             // จำเป็น

    DistanceStrategy       = QdrantDistanceStrategy.Cosine,
    HybridFusionStrategy   = QdrantHybridFusionStrategy.Rrf,
    AutoCreateCollection   = true,

    // เพิ่ม payload index สำหรับ server-side filter ที่เร็วขึ้น
    AdditionalPayloadIndexes = new List<QdrantIndexOption>
    {
        new QdrantIndexOption { Field = "meta.language", SchemaType = PayloadSchemaType.Keyword },
        new QdrantIndexOption { Field = "meta.date",     SchemaType = PayloadSchemaType.Integer }
    }
}
```

### Distance Strategy

| ค่า | คำอธิบาย |
|-------|-------------|
| `Cosine` | Cosine similarity — เหมาะสำหรับ embedding ที่ normalize แล้ว (ค่าเริ่มต้น) |
| `Euclidean` | L2 distance — ระยะห่างน้อยกว่า = คล้ายกว่า |
| `DotProduct` | Dot product — ใช้กับ unit-normalized vector |

### Qdrant Cloud

```csharp
new QdrantOptions
{
    Host           = "your-cluster.cloud.qdrant.io",
    Port           = 6334,
    UseTls         = true,
    ApiKey         = "your-qdrant-cloud-key",
    CollectionName = "production",
    Dimension      = 1536
}
```

---

## Pinecone

Vector database serverless ที่มีคนดูแลให้ครบ ไม่ต้องจัดการ infrastructure

```bash
dotnet add package Mythosia.VectorDb.Pinecone
```

```csharp
using Mythosia.VectorDb.Pinecone;

var store = new PineconeStore(new PineconeOptions
{
    IndexHost = "https://my-index-xxxx.svc.us-east1-gcp.pinecone.io",
    ApiKey    = "your-api-key"
});
```

### สร้าง Index อัตโนมัติ

```csharp
new PineconeOptions
{
    ApiKey          = "your-api-key",
    AutoCreateIndex = true,
    IndexName       = "my-index",
    Dimension       = 1536,
    Cloud           = "aws",          // "aws", "gcp" หรือ "azure"
    Region          = "us-east-1"
}
```

> เมื่อเปิด `AutoCreateIndex` จะสร้าง index ด้วย metric `dotproduct` ซึ่งจำเป็นสำหรับ hybrid (sparse + dense) search

---

## PostgreSQL (pgvector)

ใช้ extension [`pgvector`](https://github.com/pgvector/pgvector) เพื่อเพิ่ม vector similarity search ให้ PostgreSQL มาตรฐาน

```bash
dotnet add package Mythosia.VectorDb.Postgres
```

### ข้อกำหนดเบื้องต้น

```sql
-- รันครั้งเดียวบน PostgreSQL server
CREATE EXTENSION IF NOT EXISTS vector;
CREATE EXTENSION IF NOT EXISTS pg_trgm;  -- เฉพาะถ้าใช้ Trigram text search
```

```csharp
using Mythosia.VectorDb.Postgres;

var store = new PostgresStore(new PostgresOptions
{
    ConnectionString = "Host=localhost;Port=5432;Database=mydb;Username=user;Password=pass;",
    Dimension        = 1536,
    EnsureSchema     = true    // สร้าง extension, table และ index อัตโนมัติ
});
```

### ประเภท Index

| ประเภท | Class | เมื่อไหรใช้ |
|------|-------|-------------|
| HNSW | `HnswIndexOptions` | ค่าเริ่มต้น ค้นหาประมาณเร็ว เหมาะกับกรณีส่วนใหญ่ |
| IVFFlat | `IvfFlatIndexOptions` | หน่วยความจำน้อยกว่า เหมาะกับ dataset ขนาดใหญ่ที่นิ่ง |
| None | `NoIndexOptions` | Sequential scan ใช้เฉพาะ dataset เล็กมาก |

> ต้องใช้ `Mythosia.VectorDb.Postgres` 10.8.1 ขึ้นไป เพื่อใช้การตั้งค่าค้นหาเวกเตอร์เมื่อการค้นหาทั้งสองส่วนของไฮบริดทำงานอยู่ [บันทึกแพตช์](../../src/vectordb/Mythosia.VectorDb.Postgres/RELEASE_NOTES.md#v1081)

กำหนดขอบเขตการค้นหาเวกเตอร์ผู้สมัครของ hybrid search ด้วย `HnswIndexOptions.EfSearch` หรือ `IvfFlatIndexOptions.Probes` ใน `PostgresOptions.Index` ค่าเริ่มต้นเหล่านี้ใช้กับทั้ง vector search ปกติและส่วนเวกเตอร์ของ hybrid search ภายใน transaction เดียวกับคำสั่งค้นหา การกำหนด runtime option แยกตามคำขอรองรับเฉพาะ `SearchAsync` การค้นหาโดยประมาณและตัวกรองยังอาจทำให้ได้ผลลัพธ์น้อยกว่า `topK`

### Text Search Mode

ใช้สำหรับ keyword side ของ hybrid search:

| Mode | เหมาะสำหรับ |
|------|----------|
| `TsVector` | Full-text search มาตรฐาน — ภาษาอังกฤษ ภาษาตะวันตกส่วนใหญ่ |
| `Trigram` | ภาษา CJK (เกาหลี จีน ญี่ปุ่น) fuzzy matching |

```csharp
new PostgresOptions
{
    TextSearchMode   = TextSearchMode.Trigram,
    TextSearchConfig = "simple"
}
```
