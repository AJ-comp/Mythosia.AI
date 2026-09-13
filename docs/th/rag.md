# RAG (Retrieval-Augmented Generation)

หากต้องการเพียงคำตอบสุดท้ายและปุ่มหยุด ให้ส่ง `cancellationToken` ไปยัง `GetCompletionAsync` ใช้ Run สำหรับเหตุการณ์ความคืบหน้าหรือคำสั่งเพิ่มเติมที่รองรับ ดู[การยกเลิกคำตอบ](completions.md#completion-cancellation)

สำหรับคำตอบที่เสริมด้วยข้อมูลค้นคืน ให้ส่ง `cancellationToken` ไปยัง `RagEnabledService.GetCompletionAsync` ด้วย โทเค็นเดียวกันส่งผ่านการค้นคืน `LlmQueryRewriter` `LlmReranker` และการเรียกภายใน การยกเลิกระหว่างค้นคืนจะป้องกันการเรียกโมเดลถัดไป `RagPipeline.QueryAndGenerateAsync` ก็ส่งโทเค็นด้วย แต่ละองค์ประกอบต้องรองรับการยกเลิก และไม่ย้อนคืนการค้นคืนหรือการทำงานเครื่องมือที่เสร็จแล้ว

หลังค้นหาเอกสาร การเขียนคำตอบอาจใช้เวลา [Run](execution-api-transition.md) ช่วยแสดงความคืบหน้าและยกเลิกการทำงานได้ โดยยังคงการค้นหาและเสริมบริบทของ RAG

RAG ช่วยให้ model ตอบคำถามจากเอกสารของคุณเองโดยดึงส่วนที่เกี่ยวข้องมาในเวลา query

## การติดตั้ง

```bash
dotnet add package Mythosia.AI.Rag
```

## เริ่มต้นใช้งาน

ใช้ `.WithRag()` บน `IAIService` ใด ๆ เพื่อเปิด RAG ด้วย fluent API:

```csharp
using Mythosia.AI.Rag;

var service = new AnthropicService(apiKey, http)
    .WithRag(rag => rag
        .AddDocument("manual.txt")
        .AddDocument("policy.txt")
    );

var response = await service.GetCompletionAsync("นโยบายการคืนสินค้าคืออะไร?");
```

เอกสารจะถูกแบ่ง embed และเก็บอัตโนมัติ เมื่อ query ส่วนที่เกี่ยวข้องที่สุดจะถูกดึงและใส่ใน prompt

หากผู้ให้บริการจัดการดัชนีเอกสารอยู่แล้ว ให้เปรียบเทียบ[การค้นหาไฟล์ที่โฮสต์ไว้กับ RAG](reasoning-and-search.md) แหล่งอ้างอิงจากการค้นคืน RAG กับแหล่งอ้างอิงที่ผู้ให้บริการส่งกลับจะถูกเก็บแยกกัน

## เพิ่มเอกสาร

รองรับหลายประเภทแหล่งข้อมูล:

```csharp
.WithRag(rag => rag
    .AddDocument("readme.txt")                    // ไฟล์ local
    .AddUrl("https://example.com/doc.txt")        // URL
    .AddText("เนื้อหาที่ใส่โดยตรงก็ได้")           // string ตรง ๆ
)
```

## Embedding Provider แบบกำหนดเอง

ค่าเริ่มต้น RAG ใช้ local embedding provider ที่มีมาให้ หากต้องการใช้ embedding model เฉพาะ:

```csharp
using Mythosia.AI.Rag.Embeddings;

var embedder = new OpenAIEmbeddingProvider(apiKey, http, "text-embedding-3-small");

var service = new AnthropicService(apiKey, http)
    .WithRag(rag => rag
        .UseEmbedding(embedder)
        .AddDocument("knowledge-base.txt")
    );
```

## Vector Store แบบกำหนดเอง

ค่าเริ่มต้นใช้ store แบบ in-memory สำหรับ production ให้ใช้ vector store แบบถาวร:

```csharp
dotnet add package Mythosia.VectorDb.Postgres
```

```csharp
using Mythosia.VectorDb.Postgres;

var store = new PostgresStore(new PostgresOptions
{
    ConnectionString = connectionString,
    Dimension = 1536
});

var service = new OpenAIService(apiKey, http)
    .WithRag(rag => rag
        .UseStore(store)
        .AddDocument("large-corpus.txt")
    );
```

## ตัวเลือก Query

ปรับแต่งพฤติกรรมการดึงข้อมูลต่อ query:

```csharp
var options = new RagQueryOptions
{
    FinalFilter = new RagFilter
    {
        TopK = 5,            // จำนวนชิ้นส่วนที่ต้องการ
        MinScore = 0.7       // คะแนนความคล้ายขั้นต่ำ
    }
};

var response = await service.GetCompletionAsync("คำถามของคุณ", options: options);
```

## ขั้นตอนต่อไป

- [Hybrid Search](rag-hybrid-search.md) — รวม semantic และ keyword search
- [การเขียนคำถามใหม่](rag-query-rewriting.md) — ปรับ query ด้วย context การสนทนา
- [Reranking](rag-reranking.md) — ปรับปรุงความแม่นยำผลการค้นหา
- [การปรับแต่ง Pipeline](rag-pipeline.md) — ควบคุมรายละเอียดของ RAG
- [Agentic RAG](rag-agentic.md) — AI ตัดสินใจเองว่าเมื่อไหร่และค้นอะไร
- [Vector Store](vectordb-overview.md) — ตั้งค่า storage แบบถาวร
- [Text Splitter](text-splitters.md) — กำหนดวิธีแบ่งเอกสาร

Perplexity: [ใช้เวกเตอร์กับดัชนีของคุณ / ค้นหาโดยไม่สร้างคำตอบ](perplexity.md).
