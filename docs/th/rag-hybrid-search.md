# Hybrid Search

รหัสสินค้าเหมาะกับการค้นหาคำ ส่วนคำถามที่ใช้ถ้อยคำต่างจากเอกสารเหมาะกับการค้นหาความหมาย ตัวค้นหาที่เลือกเตรียมเฉพาะข้อมูลที่จำเป็น จึงไม่ต้องสร้าง embedding ก่อนค้นหาคำทุกครั้ง

## โหมดที่มีให้

```csharp
// ค้นหาความหมาย (ค่าเริ่มต้น)
.UseVectorSearch()

// ค้นหาคำโดยไม่มี embedding คำถาม
.UseKeywordSearch()

// ค้นหา hybrid แบบถ่วงน้ำหนัก
.UseHybridSearch(new HybridSearchOptions
{
    VectorWeight = 0.7f,
    CandidateMultiplier = 4,
    RrfK = 60
})
```

`HybridSearchOptions`: `using Mythosia.VectorDb;`

`UseKeywordSearch()` ข้าม embedding ของคำถาม แต่การนำเข้าเอกสารยังแบ่งส่วนและสร้าง embedding สำหรับที่เก็บเวกเตอร์เดิม นี่ไม่ใช่ API ดัชนีเฉพาะข้อความ การเริ่มต้นแบบล่าช้ายังอาจเรียก embedding เอกสารเมื่อถามครั้งแรก

## รวมผลการค้นหาคำและความหมาย

`VectorWeight` กำหนดน้ำหนักเวกเตอร์ (0–1) และข้อความคือ `1 - VectorWeight` ส่วน `CandidateMultiplier` กำหนดจำนวนตัวเลือกแต่ละแขนง และ `RrfK` กำหนดการปรับอันดับของ Reciprocal Rank Fusion แบบถ่วงน้ำหนัก ซึ่งแยกจากตัวคูณ RAG reranker ควรประเมินด้วยเอกสารและคำถามจริง

โหมดเวกเตอร์และคำล้วนคงคะแนนเดิม ส่วน hybrid ที่ตั้งค่าได้ใช้ RRF ถ่วงน้ำหนักแบบปรับมาตรฐานแม้มีแขนงเดียว น้ำหนักเวกเตอร์ 0 ข้าม embedding คำถาม คะแนนไม่ใช่ความน่าจะเป็น `WeightedBlend` ผสมคะแนนค้นหากับ reranker โดยไม่ปรับเทียบ จึงแนะนำ `RerankerOnly` สำหรับค้นหาคำที่ยังไม่ปรับเทียบคะแนน

```csharp
using Mythosia.AI.Rag;
using Mythosia.VectorDb;

var service = new OpenAIService(apiKey, http)
    .WithRag(rag => rag
        .AddDocument("manual.txt")
        .UseHybridSearch(new HybridSearchOptions
        {
            VectorWeight = 0.7f,
            CandidateMultiplier = 4,
            RrfK = 60
        }));

string answer = await service.GetCompletionAsync("What is the refund policy?");
```

## การรองรับและความเข้ากันได้

InMemory, PostgreSQL และ Qdrant รองรับการค้นหาคำใหม่และ RRF แบบถ่วงน้ำหนักที่กำหนดค่าได้ คะแนนข้อความต่างกัน: BM25 ใน InMemory, full-text หรือ trigram ตามการตั้งค่าใน PostgreSQL และดัชนี sparse ใน Qdrant คะแนนข้ามระบบไม่เท่ากัน

Pinecone คง hybrid เดิมผ่าน `UseHybridSearch()` ค่าเริ่มต้นบนดัชนี `dotproduct` ที่เข้ากันได้ อะแดปเตอร์ไม่รองรับโหมดค้นหาคำหรือ RRF แบบถ่วงน้ำหนักที่กำหนดค่ารวมทั้งสองแขนง ที่เก็บอื่นต้องมี optional interface ที่เกี่ยวข้อง โหมดหรือตัวเลือกที่ไม่รองรับจะแจ้งข้อผิดพลาด ไม่สลับไปเวกเตอร์หรือเพิกเฉยต่อน้ำหนัก

อะแดปเตอร์ InMemory, PostgreSQL และ Qdrant เดิมไม่ติดตั้งโมเดลนิวรัลหรือย้ายดัชนี การแยก `C#` กับ `C++` ขึ้นกับตัววิเคราะห์แต่ละตัว ตัวเลือก PIXIE ด้านล่างก็ต้องทดสอบการจับคู่รหัสอย่างแม่นยำเช่นกัน

ดู[โหมดและที่เก็บที่รองรับ](rag.md#retrieval-modes) และ[ตัวค้นหาที่กำหนดเอง](rag-pipeline.md#custom-retriever)

<a id="pixie-search"></a>

## เปรียบเทียบการค้นหานิวรัลในเครื่องด้วย PIXIE

เมื่อคำถามกับเอกสารใช้ถ้อยคำต่างกัน การค้นหาแบบ sparse ที่เรียนรู้แล้วอาจเพิ่มคำที่เกี่ยวข้องได้ แพ็กเกจเสริม `Mythosia.AI.Rag.Search.Pixie` เข้ารหัสทั้งเอกสารและคำถามด้วย PIXIE ในเครื่อง และรวมผลกับ dense embedding เดิม PIXIE ไม่ต้องใช้เซิร์ฟเวอร์ Python หรือ API key แต่ผู้ให้บริการ dense embedding หรือคำตอบที่เลือกอาจยังเรียก API ภายนอก

```csharp
using Mythosia.AI.Rag;
using Mythosia.AI.Rag.Search.Pixie;
using Mythosia.VectorDb;

using var encoder = new PixieSparseEncoder(new PixieOptions());
var searchStore = new PixieInMemoryStore(encoder);
RagStore rag = await RagStore.BuildAsync(builder => builder
    .UseEmbedding(embeddings)
    .UseStore(searchStore)
    .AddDocument("manual.txt")
    .UseHybridSearch(new HybridSearchOptions { VectorWeight = 0.7f }));

RagProcessedQuery result = await rag.QueryAsync("refund policy");
```

เมื่อใช้สโตร์นี้ `UseKeywordSearch()` จะเลือกการค้นหานิวรัลแบบ sparse โดยข้ามผู้ให้บริการ dense embedding ของคำถาม แต่ยังรัน PIXIE กับคำถาม การนำเข้าเอกสาร RAG ยังสร้าง dense embedding ส่วน `UseHybridSearch(...)` รวมอันดับจาก sparse dot product และ dense cosine ด้วย weighted RRF ตามที่ตั้งค่า

รุ่นพรีวิวนี้มี `PixieInMemoryStore` ซึ่งเก็บดัชนีในหน่วยความจำ ไม่ได้เชื่อม PIXIE เข้ากับ PostgreSQL, Qdrant หรือ Pinecone ต้องสร้างดัชนีใหม่หลังรีสตาร์ตหรือเปลี่ยนโมเดล/การตั้งค่า คงตัวเข้ารหัสไว้จนการทำงานของสโตร์ทั้งหมดเสร็จแล้วจึงคืนทรัพยากร การค้นหาเดิมยังเป็นค่าเริ่มต้น ควรเปรียบเทียบด้วยเอกสารและคำถามที่มีคำตอบอ้างอิงชุดเดียวกันก่อนเปลี่ยน PIXIE ไม่รับประกันการแยก `C#` กับ `C++` อย่างแม่นยำหรือการทำตามเงื่อนไขยกเว้น

[คู่มือเชื่อมต่อและเปรียบเทียบ PIXIE (ภาษาอังกฤษ)](../rag-pixie-search.md).
