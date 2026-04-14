# การสร้าง Context

> 📍 **Q&A Pipeline:** [การเขียนคำถามใหม่](rag-query-rewriting.md) → [Embedding](rag-embedding.md) → [การกรอง](rag-filtering.md) → [การดึงข้อมูล](rag-hybrid-search.md) → [Reranking](rag-reranking.md) → **`การสร้าง Context`**

## การสร้าง Context คืออะไร?

การสร้าง Context คือขั้นตอนสุดท้ายของ RAG pipeline หลังจากดึงและจัดอันดับ chunk ที่เกี่ยวข้องที่สุดแล้ว ขั้นตอนนี้จะ **รวบรวมเป็น prompt** ที่ LLM เข้าใจได้และนำไปสร้างคำตอบ

ลองนึกถึงการเขียนเอกสารสรุปให้ใครสักคนก่อนประชุม คุณได้รวบรวมข้อมูลที่เกี่ยวข้อง (การดึงข้อมูล) และเรียงตามความสำคัญ (reranking) แล้ว ตอนนี้ต้อง **จัดระเบียบให้ชัดเจน** และตั้งคำถามเพื่อให้ผู้อ่านรู้ว่าจะทำอะไรกับข้อมูลนั้น

คุณภาพของขั้นตอนนี้ส่งผลโดยตรงต่อคุณภาพคำตอบของ LLM Prompt ที่มีโครงสร้างดีลด hallucination และช่วยให้ model ยึดอยู่กับ context ที่ให้ไว้

## Default Context Builder

เมื่อไม่มีการตั้งค่าเอง pipeline ใช้ `DefaultContextBuilder` ซึ่งสร้างรูปแบบนี้:

```
ตอบคำถามโดยอิงจาก context ต่อไปนี้:

[1] (แหล่งที่มา: manual.txt)
การคืนสินค้าทำได้ภายใน 30 วันนับจากวันซื้อ...

[2] (แหล่งที่มา: policy.txt)
สินค้าดิจิทัลไม่สามารถคืนเงินได้...

คำถาม: นโยบายการคืนสินค้าคืออะไร?
```

Default builder มี property ที่ปรับได้:

```csharp
var contextBuilder = new DefaultContextBuilder
{
    Header = "ตอบคำถามโดยอิงจาก context ต่อไปนี้:",
    QueryPrefix = "คำถาม:",
    IncludeScores = false,    // แสดงคะแนน similarity?
    IncludeSource = true      // แสดง source metadata?
};

.WithRag(rag => rag
    .WithContextBuilder(contextBuilder)
    .AddDocument("docs.txt")
)
```

### รวม Score

เมื่อ `IncludeScores = true` แต่ละ chunk จะแสดง similarity score:

```
[1] (แหล่งที่มา: manual.txt) [Score: 0.892]
การคืนสินค้าทำได้ภายใน 30 วัน...
```

มีประโยชน์สำหรับ debug และทำความเข้าใจว่าทำไม chunk นั้น ๆ ถึงถูกเลือก

## Prompt Template

สำหรับการควบคุม prompt สุดท้ายมากขึ้น ใช้ **prompt template** ที่มี placeholder `{context}` และ `{question}`:

```csharp
.WithRag(rag => rag
    .WithPromptTemplate("""
        คุณคือผู้ช่วยฝ่าย support ตอบคำถามโดยใช้เฉพาะเอกสารต่อไปนี้เท่านั้น
        ถ้าคำตอบไม่อยู่ในเอกสาร ให้บอกว่า "ฉันไม่มีข้อมูลนั้น"

        เอกสาร:
        {context}

        คำถามลูกค้า: {question}
        """)
    .AddDocument("support-kb.txt")
)
```

Pipeline แทนที่ `{context}` ด้วยรายการ chunk ที่มีหมายเลข และ `{question}` ด้วย query ของผู้ใช้

### เมื่อไหรควรใช้ Template

Template มีประโยชน์โดยเฉพาะเมื่อต้องการ:

- **จำกัดพฤติกรรม** — "ถ้าคำตอบไม่อยู่ใน context ให้บอกว่า 'ฉันไม่ทราบ'"
- **กำหนดโทน** — "ตอบในแบบมืออาชีพและกระชับ"
- **กำหนดบทบาท** — "คุณคือผู้ช่วยแพทย์" หรือ "คุณคือที่ปรึกษากฎหมาย"
- **ควบคุมภาษา** — "ตอบเป็นภาษาไทยเสมอ"

### เคล็ดลับออกแบบ Template

| เคล็ดลับ | ตัวอย่าง |
| --- | --- |
| บอก model ให้อยู่ใน context | "อิงจากเอกสารที่ให้มาเท่านั้น" |
| จัดการข้อมูลที่ไม่มี | "ถ้าไม่พบคำตอบ ให้บอกว่า 'ฉันไม่มีข้อมูลนั้น'" |
| ระบุรูปแบบ output | "ตอบเป็นหัวข้อย่อย" |
| ตั้งข้อจำกัดภาษา | "ตอบในภาษาเดียวกับคำถาม" |

## Custom Context Builder

สำหรับการควบคุมเต็มรูปแบบ implement `IContextBuilder`:

```csharp
public class MyContextBuilder : IContextBuilder
{
    public string BuildContext(string query, IReadOnlyList<VectorSearchResult> searchResults)
    {
        var sb = new StringBuilder();

        sb.AppendLine("### ข้อมูลที่เกี่ยวข้อง ###");
        sb.AppendLine();

        foreach (var result in searchResults)
        {
            var source = result.Record.Metadata.TryGetValue("source", out var s) ? s : "ไม่ระบุ";
            sb.AppendLine($"📄 จาก: {source} (ความเกี่ยวข้อง: {result.Score:P0})");
            sb.AppendLine(result.Record.Content);
            sb.AppendLine("---");
        }

        sb.AppendLine();
        sb.AppendLine($"อิงจากข้อมูลข้างต้น ตอบว่า: {query}");

        return sb.ToString();
    }
}
```

Register กับ builder:

```csharp
.WithRag(rag => rag
    .WithContextBuilder(new MyContextBuilder())
    .AddDocument("docs.txt")
)
```

## ขั้นตอนต่อไป

- [การปรับแต่ง Pipeline](rag-pipeline.md) — ปรับแต่งพฤติกรรม RAG โดยรวม
- [Reranking](rag-reranking.md) — ปรับปรุงคุณภาพ chunk ก่อนสร้าง context
- [ภาพรวม RAG](rag.md) — ทบทวน flow RAG ทั้งหมด
