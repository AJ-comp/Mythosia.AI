# การดำเนินการพื้นฐานของ Vector Store

## Upsert

แทรกหรืออัปเดต record เดียว ถ้ามี record ที่มี `Id` เดียวกันอยู่แล้วจะถูกแทนที่

```csharp
var record = new VectorRecord
{
    Id      = Guid.NewGuid().ToString(),
    Vector  = await embeddingService.GetEmbeddingAsync("คืนสินค้าได้ภายใน 30 วัน"),
    Content = "คืนสินค้าได้ภายใน 30 วัน",
    Metadata = new Dictionary<string, string>
    {
        ["source"]   = "faq.pdf",
        ["language"] = "th",
        ["section"]  = "returns"
    }
};

await store.UpsertAsync(record);
```

## Batch Upsert

Upsert หลาย record ในครั้งเดียว มีประสิทธิภาพกว่าการเรียก `UpsertAsync` ใน loop — backend ใช้ batch API ภายในเมื่อทำได้

```csharp
var records = chunks.Select(chunk => new VectorRecord
{
    Id      = Guid.NewGuid().ToString(),
    Vector  = chunk.Embedding,
    Content = chunk.Text,
    Metadata = new Dictionary<string, string>
    {
        ["source"] = "manual.pdf",
        ["page"]   = chunk.Page.ToString()
    }
});

await store.UpsertBatchAsync(records);
```

## ค้นหา

คืน top-K record ที่คล้ายกับ query vector มากที่สุด ตัวเลือกกรองตาม metadata ก่อนให้คะแนน

```csharp
float[] queryVector = await embeddingService.GetEmbeddingAsync("นโยบายการคืนสินค้าคืออะไร?");

var results = await store.SearchAsync(queryVector, topK: 5);

foreach (var r in results)
{
    Console.WriteLine($"[{r.Score:F3}] {r.Record.Content}");
    Console.WriteLine($"  แหล่งที่มา: {r.Record.Metadata["source"]}");
}
```

### ค้นหาพร้อม Filter

รวม vector similarity กับการกรอง metadata:

```csharp
var filter = new VectorFilter()
    .Where("language", "th")
    .Where("section", "returns")
    .WithMinScore(0.7);

var results = await store.SearchAsync(queryVector, topK: 5, filter: filter);
```

ดู [VectorFilter](vector-filter.md) สำหรับ filtering API ฉบับเต็ม

## Hybrid Search

รวม dense vector similarity กับ keyword (BM25) search recall ดีขึ้นสำหรับ query ที่มีคำเฉพาะ ชื่อ หรือโค้ด

```csharp
float[] queryVector = await embeddingService.GetEmbeddingAsync("สถานะคำสั่งซื้อ #12345");

var results = await store.HybridSearchAsync(
    denseVector: queryVector,
    query: "สถานะคำสั่งซื้อ #12345",   // ข้อความดิบสำหรับ BM25
    topK: 5
);
```

วิธีที่ hybrid search ทำงานต่อ backend:

| Backend | กลไก |
|---------|-----------|
| **InMemory** | RRF รวม cosine similarity + Lucene BM25 score |
| **Qdrant** | Server-side: dense + sparse vector รวมด้วย RRF หรือ DBSF |
| **Pinecone** | Sparse + dense vector รวม server-side |
| **Postgres** | Vector similarity + คะแนน `tsvector`/`trigram` รวมใน SQL |

## ดึงตาม ID

ดึง record เฉพาะตาม ID:

```csharp
VectorRecord? record = await store.GetAsync("record-id-123");

if (record is null)
    Console.WriteLine("ไม่พบ");
```

## ลบตาม ID

ลบ record เดียว:

```csharp
await store.DeleteAsync("record-id-123");
```

## ลบตาม Filter

ลบ record ทั้งหมดที่ตรงกับ filter ใช้ด้วยความระมัดระวัง — นี่คือการลบแบบกลุ่ม

```csharp
// ลบ record ทั้งหมดจากเอกสารที่ระบุ
var filter = new VectorFilter().Where("source", "old-manual.pdf");
await store.DeleteByFilterAsync(filter);
```

## แทนที่ตาม Filter

ลบ record ที่ตรง filter และแทรกชุดใหม่แบบ atomic มีประโยชน์สำหรับ re-index เอกสารโดยไม่ทิ้ง chunk เก่า

```csharp
var filter = new VectorFilter().Where("source", "manual-v1.pdf");

var newRecords = newChunks.Select(c => new VectorRecord
{
    Id      = Guid.NewGuid().ToString(),
    Vector  = c.Embedding,
    Content = c.Text,
    Metadata = new Dictionary<string, string> { ["source"] = "manual-v2.pdf" }
}).ToList();

await store.ReplaceByFilterAsync(filter, newRecords);
```

> บน Postgres จะรันใน transaction ทำให้ atomic สมบูรณ์

## นับ

นับ record ที่เก็บ ตัวเลือกจำกัดขอบเขตด้วย filter:

```csharp
long total = await store.CountAsync();
long thai  = await store.CountAsync(new VectorFilter().Where("language", "th"));

Console.WriteLine($"ทั้งหมด: {total}, ภาษาไทย: {thai}");
```

## ตรวจสอบการเชื่อมต่อ

ตรวจว่า backend เข้าถึงได้ มีประโยชน์ใน health check หรือตรวจสอบตอน startup:

```csharp
try
{
    await store.VerifyConnectionAsync();
    Console.WriteLine("Vector store เชื่อมต่อสำเร็จ");
}
catch (Exception ex)
{
    Console.WriteLine($"เชื่อมต่อล้มเหลว: {ex.Message}");
}
```

## ใช้กับ RAG

ส่ง `IVectorStore` ให้ `RagBuilder` เพื่อใช้ backend ใด ๆ เป็น RAG retrieval store:

```csharp
var store = new QdrantStore(new QdrantOptions
{
    CollectionName = "knowledge-base",
    Dimension      = 1536
});

var ragService = new AnthropicService(apiKey, http)
    .WithRag(rag => rag
        .UseStore(store)
        .UseOpenAIEmbedding(embeddingKey, http)
        .AddDirectory("docs/", ".txt", ".md")
    );

var answer = await ragService.GetCompletionAsync("นโยบายการคืนสินค้าคืออะไร?");
```
