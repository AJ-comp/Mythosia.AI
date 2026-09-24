# การกำหนดค่า Pipeline

<a id="indexing-validation"></a>

## ปกป้องเอกสารเดิมเมื่อการทำดัชนีล้มเหลว

ตัวแบ่งข้อความที่กำหนดเองหรือผลตอบกลับ embedding ที่ผิดพลาดต้องไม่แทนที่เอกสารที่ค้นหาได้ด้วยเนื้อหาที่ไม่ครบหรือจับคู่ผิดอย่างเงียบ ๆ Pipeline จะตรวจสอบแต่ละเอกสารก่อนเริ่มบันทึก รวมถึงเมื่อใช้ `onDocumentEmbedded`

ก่อนเรียก embedding การจัดเก็บ หรือ callback สำหรับบันทึก หาก `RagDocument.Id` เป็น null สตริงว่าง หรือมีแต่ช่องว่าง จะเกิด `ArgumentException` ผลจากตัวแบ่งที่ไม่ถูกต้องจะทำให้เกิด `InvalidOperationException` ได้แก่ รายการหรือชังก์เป็น null, `Content` หรือ `Metadata` เป็น null, ID ชังก์ว่างหรือมีแต่ช่องว่าง หรือ ID ซ้ำภายในเอกสารเดียวกัน การตรวจ ID ซ้ำใช้ `StringComparer.Ordinal` ซึ่งแยกตัวพิมพ์ใหญ่และเล็ก ค่าของชังก์และเมทาดาทาจะถูกคัดลอกก่อนเรียก embedding ครั้งแรก

ID ที่กำหนดเองและถูกต้องจะถูกเก็บไว้ตามเดิม ไม่มีการสร้าง ตัดช่องว่าง หรือแก้ไข ID อัตโนมัติ และไม่มีการตรวจความซ้ำของ ID ชังก์ที่กำหนดเองระหว่างเอกสารต่าง ๆ ทั่วทั้งระบบ ใช้ ID ที่ไม่ซ้ำในคอลเลกชันปลายทางตาม[ตัวอย่างตัวแบ่งที่กำหนดเอง](text-splitters.md) คีย์สงวน `document_id` จะถูกปรับให้ตรงกับเอกสารเฉพาะในสำเนาที่ใช้จัดเก็บ โดยไม่เปลี่ยนเมทาดาทาต้นฉบับ

ID ที่ไม่ถูกต้อง การแบ่งที่ล้มเหลว และ batch embedding ที่ไม่ถูกต้องจะไม่เปลี่ยนระเบียนเดิมของเอกสารนั้น และจะไม่เรียก callback สำหรับบันทึก ทุก batch ของเอกสารต้องผ่าน[การตรวจสอบ embedding](rag-embedding.md#embedding-validation) ก่อนเริ่มจัดเก็บ การดำเนินการนี้ไม่ย้อนคืนเอกสารอื่นที่บันทึกเสร็จก่อนหน้าในงานเดียวกัน ส่วนการย้อนคืนหลังเริ่มจัดเก็บขึ้นอยู่กับที่เก็บข้อมูลหรือ callback

การตรวจสอบและการแก้ลำดับผลตอบกลับนี้ไม่กู้คืนเนื้อหาที่ถูกเขียนทับหรือเวกเตอร์เดิมที่บันทึกโดยจับคู่ชังก์ผิดโดยอัตโนมัติ จึงต้องทำดัชนีเอกสารที่ได้รับผลกระทบใหม่จากต้นฉบับ

<a id="custom-persistence"></a>

## แทนที่ทั้งเอกสารใน callback จัดเก็บ

เมื่อเอกสารสั้นลง การ upsert เฉพาะชิ้นใหม่จะทำให้ส่วนท้ายเก่ายังคงค้นหาได้ `onDocumentEmbedded` แทนการจัดเก็บเริ่มต้นทั้งหมด จึงควรใช้ `document_id` ที่ปรับแล้วในเรคอร์ดเพื่อแทนที่ทั้งเอกสาร callback จะได้รับเอกสารที่ตรวจสอบแล้วและไม่ว่างครั้งละหนึ่งเอกสาร:

```csharp
using Mythosia.AI.Rag;
using Mythosia.VectorDb;

var store = await RagStore.BuildAsync(config => config
    .AddDocuments("./docs/")
    .UseOpenAIEmbedding(apiKey)
    .UseStore(vectorStore),
    onDocumentEmbedded: async records =>
    {
        var documentId = records[0].Metadata["document_id"];
        await vectorStore.ReplaceByFilterAsync(
            new VectorFilter().Where("document_id", documentId), records, cancellationToken);
    },
    cancellationToken: cancellationToken);
```

เมื่อแบ่งสำเร็จแต่ได้ 0 ชิ้น จะไม่เรียก callback และไม่เข้าถึงแหล่งจัดเก็บเริ่มต้น ให้ลบ ID เอกสารที่ทราบในแหล่งจัดเก็บของคุณอย่างชัดเจน ใช้ `DeleteDocumentAsync` เมื่อต้องการจัดการแหล่งจัดเก็บของ pipeline เท่านั้น ความเป็นอะตอมและการย้อนกลับขึ้นอยู่กับแหล่งจัดเก็บหรือ callback

<a id="url-documents"></a>

## อ่านเอกสาร URL อย่างปลอดภัย

เซิร์ฟเวอร์อาจบีบอัดเอกสารข้อความเพื่อส่งผ่านเครือข่าย `AddUrl` จะคลาย `gzip`, `deflate` และ Brotli (`br`) ก่อนอ่านข้อความ และตรวจสอบว่าสตรีมที่บีบอัดสมบูรณ์ การส่ง HTTP สำเร็จเพียงอย่างเดียวไม่เพียงพอ หากข้อมูลที่บีบอัดถูกตัดขาด เกิดข้อผิดพลาดในการคลายการบีบอัด หรือการตรวจสอบ checksum ที่มีในรูปแบบนั้นล้มเหลว จะหยุดโหลดก่อนสร้าง embedding หรือบันทึกข้อมูล โดยคงระเบียนเดิมของเอกสารนั้นไว้ ค่า `Content-Encoding` ที่ไม่รองรับหรือการบีบอัดหลายชั้นจะถูกปฏิเสธก่อนสร้าง embedding หรือบันทึกเช่นกัน

หากต้องการหยุดรอเอกสาร URL ที่ช้า ให้ส่ง `cancellationToken` ไปยัง `RagStore.BuildAsync` โทเคนจะส่งต่อถึงคำขอ HTTP การอ่านเนื้อหาคำตอบ และการคลายการบีบอัด การยกเลิกเป็นแบบร่วมมือและไม่ย้อนกลับเอกสารที่บันทึกเสร็จไปแล้ว

<a id="custom-retriever"></a>

## เชื่อมต่อตัวค้นหาที่ไม่บังคับ embedding

รหัสสินค้าเหมาะกับการค้นหาคำ ส่วนคำถามที่ใช้ถ้อยคำต่างจากเอกสารเหมาะกับการค้นหาความหมาย ตัวค้นหาที่เลือกเตรียมเฉพาะข้อมูลที่จำเป็น จึงไม่ต้องสร้าง embedding ก่อนค้นหาคำทุกครั้ง

- ก่อน: ทุกกลยุทธ์ได้รับ embedding คำถาม
- หลัง: ตัวค้นหาเตรียมเฉพาะรูปแบบที่จำเป็น

ใช้ `IRagRetriever` สำหรับดัชนีภายนอกหรือรูปแบบอื่น `RagRetrievalRequest` มี `Query` (คำถามความหมายเต็ม), `TextQuery` ที่เป็น null ได้ (คำค้นทดแทน), `TopK`, `Filter` และ `ProgressAsync` ตัวค้นหาในระบบใช้ `Query` เมื่อ `TextQuery` เป็น null และข้ามแขนงข้อความเมื่อเป็นสตริงว่าง ตัวค้นหาที่กำหนดเองต้องเตรียมคำค้นและใช้ตัวกรอง จำนวนผล และการยกเลิก

```csharp
using Mythosia.AI.Rag;
using Mythosia.VectorDb;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

public sealed class CatalogRetriever : IRagRetriever
{
    private readonly ITextSearchStore _catalog;
    public CatalogRetriever(ITextSearchStore catalog) => _catalog = catalog;

    public Task<IReadOnlyList<VectorSearchResult>> RetrieveAsync(
        RagRetrievalRequest request,
        CancellationToken cancellationToken = default)
        => _catalog.TextSearchAsync(
            request.TextQuery ?? request.Query,
            request.TopK,
            request.Filter,
            cancellationToken);
}
```

```csharp
// catalog: an existing ITextSearchStore
var service = new OpenAIService(apiKey, http)
    .WithRag(rag => rag.UseRetriever(new CatalogRetriever(catalog)));
```

ลงทะเบียนด้วย `UseRetriever(...)` หรือ `RagPipeline.SetRetriever(...)` ส่วน `IRetrievalStrategy` และ `SetRetrievalStrategy(...)` ยังทำงานผ่านอะแดปเตอร์ที่สร้าง embedding คำถาม ผลลัพธ์ต้องมีเนื้อหาและ metadata สำหรับจัดอันดับใหม่และประกอบบริบท

```csharp
store.UpdateRetriever(new CatalogRetriever(catalog));
store.UseKeywordSearch();
store.UpdateRetrievalStrategy(new HybridSearchOptions { VectorWeight = 0.7f });
store.UpdateRetriever(null); // UseVectorSearch
```

`UseKeywordSearch()` ข้าม embedding ของคำถาม แต่การนำเข้าเอกสารยังแบ่งส่วนและสร้าง embedding สำหรับที่เก็บเวกเตอร์เดิม นี่ไม่ใช่ API ดัชนีเฉพาะข้อความ การเริ่มต้นแบบล่าช้ายังอาจเรียก embedding เอกสารเมื่อถามครั้งแรก

ขั้นตอนคำถาม `Embedding` ขึ้นกับตัวค้นหา การค้นหาคำไม่รายงานขั้นตอนนี้ ตัวค้นหาที่กำหนดเองรายงานผ่าน `request.ProgressAsync` ได้ ส่วน embedding เอกสารไม่เปลี่ยนแปลง

ข้อมูลวินิจฉัยของ pipeline แสดงวิธีดึงบริบท ส่วน [Run](execution-api-transition.md) ควบคุมงานของโมเดลหลังการค้นหา ทั้งเอาต์พุต การยกเลิก และคำสั่งเพิ่มเติมที่รองรับ

## ทำไมต้องปรับแต่ง?

RAG pipeline เริ่มต้นทำงานได้ดีทันที แต่โปรเจกต์จริงมักต้องการควบคุมมากขึ้น:

- **Debug** — ขั้นตอนไหนช้า? rewriter เปลี่ยน query ในแบบที่ไม่คาดไว้หรือเปล่า?
- **Prompt engineering** — template prompt เริ่มต้นอาจไม่เหมาะกับโทนหรือข้อจำกัดของ domain คุณ
- **สถาปัตยกรรม** — หลาย service ใช้ index เดียวกันประหยัดหน่วยความจำและ embedding คงที่
- **การตรวจสอบ** — บางครั้งต้องดูผลการดึงข้อมูล *ก่อน* ส่งให้ LLM

## ติดตามความคืบหน้า

ติดตามว่า RAG stage ไหนกำลังทำงานผ่าน async callback ต่อ query:

```csharp
var options = new RagQueryOptions
{
    ProgressAsync = async stage =>
    {
        Console.WriteLine($"[RAG] {stage}");
        // Stages: QueryRewrite, Filtering, Embedding, Retrieval, Reranking, ContextBuild
    }
};

var response = await ragService.GetCompletionAsync("คำถามของคุณ", options);
```

มีประโยชน์มากสำหรับวัด latency — วัดเวลาระหว่าง stage เพื่อหาคอขวด

## Custom Prompt Template

ควบคุมวิธีที่ context ที่ดึงมาถูกใส่ใน prompt ด้วย placeholder `{context}` และ `{question}`:

```csharp
.WithRag(rag => rag
    .WithPromptTemplate("""
        ใช้เฉพาะข้อมูลต่อไปนี้ในการตอบคำถาม
        ถ้าคำตอบไม่อยู่ใน context ให้บอกว่า "ฉันไม่ทราบ"

        Context:
        {context}

        คำถาม: {question}
        """)
    .AddDocument("faq.txt")
)
```

Template ที่ออกแบบดีช่วยลด hallucination ได้อย่างมากโดยสั่งให้ model ยึดอยู่กับ context ที่ให้

## แชร์ RagStore

สร้าง index ครั้งเดียวและนำไปใช้กับหลาย service instance — มีประโยชน์เมื่อต้องการเปรียบเทียบ provider หรือทำ A/B test:

```csharp
// สร้างครั้งเดียว
RagStore store = await RagStore.BuildAsync(rag => rag
    .UseOpenAIEmbedding(apiKey)
    .AddDocuments("docs/"));

// ใช้ซ้ำกับหลาย service
var claudeRag = new AnthropicService(apiKey, http).WithRag(store);
var gptRag    = new OpenAIService(apiKey, http).WithRag(store);
```

ทั้งสอง service ใช้ embedding และ vector index เดียวกัน ไม่ต้องเก็บข้อมูลหรือคำนวณซ้ำ

## Query RagStore โดยตรง

Query store โดยอิสระจาก AI service เพื่อตรวจสอบว่าจะดึงอะไรมา:

```csharp
RagProcessedQuery result = await store.QueryAsync("นโยบายการคืนสินค้าคืออะไร?");

Console.WriteLine($"Query ที่เขียนใหม่: {result.RewrittenQuery}");

foreach (var ref_ in result.References)
{
    Console.WriteLine($"[{ref_.Score:F2}] {ref_.Record.Content[..100]}");
}
```

`result.RequestMessageContent` มี prompt ที่ประกอบเสร็จแล้วที่จะส่งให้ LLM มีประโยชน์มากสำหรับ debug คุณภาพการดึงข้อมูลโดยไม่เสีย LLM token

## การทำงานภายใน

เมื่อเรียก `.WithRag()` จะสร้าง wrapper `RagEnabledService` รอบ AIService ของคุณ กลไกหลักคือ [AIRequestContext](request-contexts.md)

### Flow ทั้งหมด

```
ragService.GetCompletionAsync("นโยบายการคืนสินค้าคืออะไร?")
    ↓
① RagEnabledService รัน RAG pipeline
   เขียน query ใหม่ → การกรอง → Embedding (เมื่อจำเป็น) → ดึงข้อมูล → ประกอบ context
    ↓
② TemplateContextBuilder แทนที่ {context} และ {question}
   → "ตอบโดยใช้ข้อมูลต่อไปนี้\n[1] คืนสินค้าได้ภายใน 30 วัน...\nคำถาม: นโยบายการคืนสินค้าคืออะไร?"
    ↓
③ RagEnabledService สร้าง AIRequestContext
   RequestMessageOverride = prompt ที่ประกอบแล้ว
    ↓
④ _innerService.GetCompletionAsync(ข้อความต้นฉบับ, context: context) ถูกเรียก
   → AIService เก็บ context ใน AsyncLocal
   → คำถามต้นฉบับเพิ่มเข้าประวัติการสนทนา
    ↓
⑤ AIService.GetLatestMessages() แทนที่อินพุตแรกของคำขอปัจจุบัน
   ประวัติ: "นโยบายการคืนสินค้าคืออะไร?" (เก็บต้นฉบับ)
   model เห็น: prompt ที่ประกอบแล้ว (RequestMessageOverride)
```

### ทำไมต้องออกแบบแบบนี้?

จุดสำคัญคือ **แยกประวัติการสนทนาออกจาก input ของ model**:

- **ประวัติการสนทนาเก็บคำถามต้นฉบับ** — เพื่อให้คำถามต่อ ๆ มาอย่าง "แล้วอันนั้นล่ะ?" มี context ที่ถูกต้อง
- **Model รับ prompt ที่ประกอบแล้ว** — prompt เต็มรูปแบบพร้อมเอกสารที่ดึงมา + คำถาม
- **State ของ AIService ไม่ถูกแตะต้องเลย** — `AsyncLocal<T>` แยกข้อมูลต่อ request

`AIService` เก็บบริบทใน `AsyncLocal` โดย `GetLatestMessages()` ใช้ `RequestMessageOverride` กับอินพุตแรกของคำขอเชิงตรรกะปัจจุบัน และคงการเรียกเครื่องมือของ assistant พร้อมผลลัพธ์ที่ตามมาไว้ จึงส่งเอกสารที่ค้นพบและผลลัพธ์เครื่องมือไปด้วยกันในคำขอถัดไปที่ส่งให้โมเดล เมื่อเสร็จสิ้นจะคืนค่าบริบทก่อนหน้า
