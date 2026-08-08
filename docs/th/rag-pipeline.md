# การกำหนดค่า Pipeline

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
        // Stages: QueryRewrite, Embedding, Filtering, Retrieval, Reranking, ContextBuild
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
   เขียน query ใหม่ → Embedding → ดึงข้อมูล → ประกอบ context
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
⑤ AIService.GetLatestMessages() แทนที่ข้อความล่าสุด
   ประวัติ: "นโยบายการคืนสินค้าคืออะไร?" (เก็บต้นฉบับ)
   model เห็น: prompt ที่ประกอบแล้ว (RequestMessageOverride)
```

### ทำไมต้องออกแบบแบบนี้?

จุดสำคัญคือ **แยกประวัติการสนทนาออกจาก input ของ model**:

- **ประวัติการสนทนาเก็บคำถามต้นฉบับ** — เพื่อให้คำถามต่อ ๆ มาอย่าง "แล้วอันนั้นล่ะ?" มี context ที่ถูกต้อง
- **Model รับ prompt ที่ประกอบแล้ว** — prompt เต็มรูปแบบพร้อมเอกสารที่ดึงมา + คำถาม
- **State ของ AIService ไม่ถูกแตะต้องเลย** — `AsyncLocal<T>` แยกข้อมูลต่อ request
