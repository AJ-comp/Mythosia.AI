# RAG (Retrieval-Augmented Generation)

หากต้องการเพียงคำตอบสุดท้ายและปุ่มหยุด ให้ส่ง `cancellationToken` ไปยัง `GetCompletionAsync` ใช้ Run สำหรับเหตุการณ์ความคืบหน้าหรือคำสั่งเพิ่มเติมที่รองรับ ดู[การยกเลิกคำตอบ](completions.md#completion-cancellation)

สำหรับคำตอบที่เสริมด้วยข้อมูลค้นคืน ให้ส่ง `cancellationToken` ไปยัง `RagEnabledService.GetCompletionAsync` ด้วย โทเค็นเดียวกันส่งผ่านการค้นคืน `LlmQueryRewriter` `LlmReranker` และการเรียกภายใน การยกเลิกระหว่างค้นคืนจะป้องกันการเรียกโมเดลถัดไป `RagPipeline.QueryAndGenerateAsync` ก็ส่งโทเค็นด้วย แต่ละองค์ประกอบต้องรองรับการยกเลิก และไม่ย้อนคืนการค้นคืนหรือการทำงานเครื่องมือที่เสร็จแล้ว

หลังค้นหาเอกสาร การเขียนคำตอบอาจใช้เวลา [Run](execution-api-transition.md) ช่วยแสดงความคืบหน้าและยกเลิกการทำงานได้ โดยยังคงการค้นหาและเสริมบริบทของ RAG

RAG ช่วยให้ model ตอบคำถามจากเอกสารของคุณเองโดยดึงส่วนที่เกี่ยวข้องมาในเวลา query


เมื่ออ้างอิงผ่าน `IAIService` ให้ใช้ `GetLastProcessing()` จาก `Mythosia.AI.Extensions` ซึ่งอ่านอินเทอร์เฟซเสริม `IAIProcessingInfoService` และคืนรายการว่างหากไม่มีข้อมูลวินิจฉัย โดยไม่เพิ่มสมาชิกบังคับให้ `IAIService` สำหรับ RAG นั้น `RagEnabledService.WithSpeed(...)` ตั้งค่าคำตอบถัดไปหลังค้นหา และ `LastProcessing` อธิบายคำตอบนั้น การเขียนคำค้นใหม่ภายในแยกออกจากกัน ส่วนผล Run มีบันทึก `Processing` เดียวกัน [WithSpeed](request-building.md#inference-speed)

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

แพ็กเกจพรีวิวเสริม `Mythosia.AI.Rag.Search.Pixie` ใช้เปรียบเทียบการค้นหานิวรัลแบบ sparse ในเครื่องกับการค้นหาเดิม โดยคงผู้ให้บริการ dense embedding และเก็บดัชนี PIXIE ในหน่วยความจำ ไม่ย้ายสโตร์ถาวรหรือเปลี่ยนการค้นหาเริ่มต้นโดยอัตโนมัติ [คู่มือเชื่อมต่อและเปรียบเทียบ PIXIE (ภาษาอังกฤษ)](rag-hybrid-search.md#pixie-search).

<a id="rag-message-attachments"></a>

## ตอบเกี่ยวกับไฟล์แนบโดยอ้างอิงเอกสารของคุณ

หากต้องการอธิบายภาพสินค้าโดยอ้างอิงคู่มือ ให้ส่ง `Message` ที่มีคำถามและภาพไปยัง `RagEnabledService.GetCompletionAsync(Message)` หรือ `StartRunAsync(Message)` ทั้งสองวิธีจะเก็บเนื้อหาแนบที่ไม่ใช่ข้อความไว้ในคำขอที่ส่งต่อไปยังบริการ AI ภายใน การค้นหาใช้ข้อความของข้อความสนทนา โดยไม่ได้สร้างดัชนีหรือ embedding ของไฟล์แนบโดยอัตโนมัติ ผู้ให้บริการและโมเดลที่เลือกต้องรองรับชนิดของไฟล์แนบนั้น บริบทที่ค้นพบจะถูกเพิ่มเฉพาะในคำขอขาออก ไม่เขียนทับ `Message` ต้นฉบับหรือแทนที่ข้อความของผู้ใช้ในประวัติการสนทนา

หากคำตอบต้องอ้างอิงทั้งคู่มือและจำนวนสินค้าคงเหลือปัจจุบัน ให้ใช้ RAG ร่วมกับเครื่องมือที่ลงทะเบียนไว้ ระหว่างการเรียกเครื่องมือของ `GetCompletionAsync` บริบทที่ค้นพบจะยังอยู่ในอินพุตแรก และผลลัพธ์ของเครื่องมือในลำดับถัดไปจะถูกส่งให้โมเดลโดยไม่เปลี่ยนแปลง ประวัติการสนทนายังคงเก็บอินพุตต้นฉบับของผู้ใช้

<a id="retrieval-modes"></a>

## เลือกวิธีค้นหาเอกสาร

รหัสสินค้าเหมาะกับการค้นหาคำ ส่วนคำถามที่ใช้ถ้อยคำต่างจากเอกสารเหมาะกับการค้นหาความหมาย ตัวค้นหาที่เลือกเตรียมเฉพาะข้อมูลที่จำเป็น จึงไม่ต้องสร้าง embedding ก่อนค้นหาคำทุกครั้ง

```csharp
RagStore store = await RagStore.BuildAsync(rag => rag
    .AddDocument("manual.txt")
    .UseKeywordSearch());

RagProcessedQuery result = await store.QueryAsync("refund policy");
```

`UseKeywordSearch()` ข้าม embedding ของคำถาม แต่การนำเข้าเอกสารยังแบ่งส่วนและสร้าง embedding สำหรับที่เก็บเวกเตอร์เดิม นี่ไม่ใช่ API ดัชนีเฉพาะข้อความ การเริ่มต้นแบบล่าช้ายังอาจเรียก embedding เอกสารเมื่อถามครั้งแรก

ดู[โหมดและที่เก็บที่รองรับ](rag-hybrid-search.md) และ[ตัวค้นหาที่กำหนดเอง](rag-pipeline.md#custom-retriever)

## เพิ่มเอกสาร

รองรับหลายประเภทแหล่งข้อมูล:

```csharp
.WithRag(rag => rag
    .AddDocument("readme.txt")                    // ไฟล์ local
    .AddUrl("https://example.com/doc.txt")        // URL
    .AddText("เนื้อหาที่ใส่โดยตรงก็ได้")           // string ตรง ๆ
)
```

`AddUrl` ตรวจสอบและคลายการบีบอัด HTTP ที่รองรับก่อนอ่านข้อความ และปฏิเสธการบีบอัดที่ไม่สมบูรณ์ ไม่รองรับ หรือมีหลายชั้น ดู[การคลายการบีบอัด URL และการยกเลิก](rag-pipeline.md#url-documents)

<a id="document-identity"></a>

### แยกไฟล์ที่มีชื่อเหมือนกัน

บริษัทสองแห่งอาจมีไฟล์ `docs/faq.txt` ของตนเอง เอกสารทั้งสองต้องอยู่ในดัชนีได้พร้อมกัน และเมื่อลงทะเบียนไฟล์เดิมอีกครั้งก็ควรใช้ตัวระบุเดิม:

```csharp
var store = await RagStore.BuildAsync(rag => rag
    .AddDocuments("company-a/docs")
    .AddDocuments("company-b/docs"));
```

ในขั้นตอนจัดเก็บ RAG แบบเริ่มต้น ระบบจะสร้าง ID เอกสารก่อนส่งระเบียนไปยัง vector store แล้วแทนที่ระเบียนที่มี `document_id` ตรงกัน PostgreSQL store (pgvector) ของไลบรารีเราใช้ ID นี้ โดยไม่ได้ตรวจสอบพาธไฟล์ต้นฉบับเอง ก่อนหน้านี้ การลงทะเบียนไดเรกทอรีส่ง `faq.txt` ให้ทั้ง `company-a/docs/faq.txt` และ `company-b/docs/faq.txt` ทำให้เอกสารที่สองแทนที่เอกสารแรก การแก้ไขนี้เก็บพาธเต็มไว้เมื่อสร้าง ID โดยไม่เปลี่ยนสคีมาของ PostgreSQL ตัวกรอง `full_path` ในตัวอย่างการจัดเก็บใช้ metadata ที่ผู้เรียกกำหนด ไม่ได้สร้าง ID เอกสารหรือระเบียนที่ไม่ซ้ำกันให้อัตโนมัติ

`PlainTextDocumentLoader` และ `DirectoryDocumentLoader` ที่มาพร้อมไลบรารีใช้พาธสัมบูรณ์ของไฟล์ซึ่งปรับรูปแบบด้วย `Path.GetFullPath` เป็น `Source` และ ID เอกสารอัตโนมัติ ไฟล์ในคนละไดเรกทอรีจึงมี ID ต่างกัน พาธสัมพัทธ์ พาธสัมบูรณ์ และพาธที่มี `./` จะใช้ ID เดียวกันเมื่อแปลงได้เป็นพาธสัมบูรณ์เดียวกันรวมถึงตัวพิมพ์ใหญ่และเล็ก ควรใช้ไดเรกทอรีทำงานเดิมเมื่อใช้พาธสัมพัทธ์ การย้ายไฟล์ การใช้ symbolic link หรือ hard link หรือพาธที่มีตัวพิมพ์ต่างกันไม่รับประกันว่าจะคง ID เดิม

`AddText(..., id: ...)`, `RagDocument.Id` ที่กำหนดเอง และกฎ `Source` ของ loader ที่เขียนเองยังคงเดิม ไม่ต้องเปลี่ยน API ที่ใช้เรียก เนื่องจาก `Source` ของ loader สองตัวนี้เป็นพาธสัมบูรณ์ การอ้างอิงแหล่งข้อมูลแบบเริ่มต้นจึงอาจแสดงพาธสัมบูรณ์ด้วย หากต้องการชื่อสำหรับแสดงผล ให้ใช้ `filename` หรือ metadata `relative_path` จาก directory loader แบบเริ่มต้น ส่วน overload ลงทะเบียนไดเรกทอรีที่รับการตั้งค่าจะไม่เพิ่ม `relative_path` ให้อัตโนมัติ

**ดัชนีที่มีอยู่แล้ว:** ระบบจะไม่ลบหรือย้าย ID แบบพาธสัมพัทธ์เดิมโดยอัตโนมัติ แนะนำให้สร้างดัชนีของเอกสารทั้งหมดใน collection ใหม่ ตรวจสอบ แล้วจึงเปลี่ยนแอปพลิเคชันไปใช้ collection นั้น หากใช้ collection เดิม ให้ลบเฉพาะ ID เก่าที่ตรวจสอบแล้วว่าเป็นของเอกสารใด จากนั้นสร้างดัชนีจากไฟล์ต้นฉบับอีกครั้ง อย่าลบเป็นวงกว้างตามชื่อไฟล์ เพราะไดเรกทอรีอื่นอาจมีเอกสารชื่อเดียวกัน

เพื่อให้การอัปเดตและลบจำกัดอยู่ที่เอกสารที่ต้องการ `document_id` เป็นคีย์สงวนของ pipeline ก่อนบันทึก แต่ละระเบียนจะได้รับ `RagDocument.Id` จริง แม้ metadata ขาเข้าจะระบุค่าอื่น ระบบไม่แก้ไขพจนานุกรม metadata ของเอกสารขาเข้าหรือ splitter และ callback บันทึกแบบกำหนดเองก็ได้รับระเบียนที่ปรับให้ตรงกันแล้วด้วย หากต้องการ ID เฉพาะของแอป ให้ใช้คีย์อื่น

การเปลี่ยนแปลงนี้ไม่ซ่อมระเบียนเดิมที่บันทึกด้วย `document_id` ผิดโดยอัตโนมัติ ให้สร้าง collection ใหม่จากต้นฉบับที่เชื่อถือได้ หรือตรวจสอบเจ้าของแล้วล้างเฉพาะระเบียนที่ได้รับผลกระทบก่อนสร้างดัชนีใหม่ การลงทะเบียนด้วย ID ที่ถูกต้องเพียงอย่างเดียวไม่สามารถค้นหาระเบียนเก่าภายใต้ ID อื่นได้อย่างแน่นอน

การลงทะเบียนไฟล์เดียวกันด้วยพาธสัมพัทธ์และพาธสัมบูรณ์ควรอัปเดตเอกสารเดียวกัน ส่วนไฟล์ชื่อเหมือนกันในคนละโฟลเดอร์ต้องแยกกัน `WordDocumentLoader`, `ExcelDocumentLoader`, `PowerPointDocumentLoader` และ `PdfDocumentLoader` กำหนด `DoclingDocument.Source` เป็นพาธไฟล์สัมบูรณ์ที่ปรับรูปแบบแล้ว เช่นเดียวกับตัวโหลด TXT ในตัว RAG สร้าง ID อัตโนมัติจากค่านี้ ส่วน ID ที่ระบุเองยังอยู่ในการควบคุมของผู้เรียก แหล่งอ้างอิงเริ่มต้นอาจแสดงพาธสัมบูรณ์

[รักษา ID ของไฟล์ให้คงที่เมื่อเปลี่ยนรูปแบบพาธ](document-loaders.md#file-source-identity).

<a id="empty-document-updates"></a>

### ล้างเอกสารโดยไม่เก็บผลการค้นหาเก่าไว้

เมื่อล้างเนื้อหานโยบายคืนเงินที่เลิกใช้แล้วและทำดัชนีเอกสารเดิมใหม่ เนื้อหาเก่าต้องไม่ปรากฏในคำตอบอีก ในขั้นตอนจัดเก็บ RAG เริ่มต้น หากแบ่งข้อความสำเร็จแต่ได้ 0 ชิ้น ระบบจะแทนที่ระเบียนที่ตรงกับ `document_id` นั้นด้วยชุดว่าง โดยไม่เรียกสร้าง embedding และไม่เปลี่ยนข้อมูลของ ID อื่น กรณีนี้ครอบคลุมเอกสารว่างหรือมีเฉพาะช่องว่างเมื่อ splitter คืน 0 ชิ้น รวมถึง splitter แบบกำหนดเองที่คืน 0 ชิ้นได้สำเร็จ

สำหรับอินสแตนซ์ `RagPipeline` ชื่อ `pipeline` ที่กำหนดค่าแล้ว ให้ใช้ ID ของเอกสารที่จัดเก็บอยู่เดิม:

```csharp
await pipeline.IndexDocumentAsync(
    new RagDocument { Id = "refund-policy", Content = "" },
    cancellationToken);
```

ภายหลังสามารถทำดัชนีเนื้อหาที่ไม่ว่างด้วย ID เดิมได้ การที่ loader ไม่คืนเอกสารเลย หรือเอกสารหายไปจากรายการไฟล์ครั้งถัดไป ไม่ใช่คำสั่งลบ เพราะไม่ได้ส่ง ID ของเอกสารที่จะให้แทนที่

ข้อยกเว้นระหว่างโหลด แยกวิเคราะห์ หรือแบ่งข้อความ และการยกเลิกที่ตรวจพบก่อนเรียกแหล่งจัดเก็บ จะคงระเบียนเดิมของเอกสารนั้นไว้ Loader และ parser ต้องรายงานความล้มเหลวด้วยข้อยกเว้น เพราะผลลัพธ์สำเร็จที่มี 0 ชิ้นไม่สามารถแยกจากการตั้งใจล้างเนื้อหาได้ เมื่อเริ่มจัดเก็บแล้ว การย้อนกลับเมื่อผิดพลาดหรือยกเลิกขึ้นอยู่กับแหล่งจัดเก็บ โดย PostgreSQL ใช้ธุรกรรมในการแทนที่ การประมวลผลแบบชุดทำทีละเอกสารและไม่ย้อนกลับเอกสารที่เสร็จก่อนแล้ว

**การจัดเก็บแบบกำหนดเอง:** เมื่อส่ง `onDocumentEmbedded` การจัดเก็บยังคงเป็นหน้าที่ของ callback นี้ หากมี 0 ชิ้น จะไม่เรียก callback และไม่เข้าถึงแหล่งจัดเก็บเริ่มต้น แอปพลิเคชันต้องลบ ID ที่ทราบอย่างชัดเจนในแหล่งจัดเก็บของตนเอง หรือใช้ `DeleteDocumentAsync` สำหรับแหล่งจัดเก็บของ pipeline

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
