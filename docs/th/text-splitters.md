# Text Splitters

Text splitter แบ่งเอกสารเป็น chunk ก่อน embedding ขนาด chunk และ overlap ส่งผลอย่างมากต่อคุณภาพการดึงข้อมูล

## Splitter ที่มีให้ใช้

### CharacterTextSplitter

แบ่งตามจำนวนตัวอักษร เรียบง่ายและเร็ว แต่อาจตัดกลางประโยค:

```csharp
.WithTextSplitter(new CharacterTextSplitter(500, 50))
```

### RecursiveTextSplitter (แนะนำเป็นค่าเริ่มต้น)

พยายามแบ่งที่ขอบเขตที่มีความหมายตามลำดับ: ย่อหน้า → ประโยค → คำ → ตัวอักษร ให้ chunk ที่สอดคล้องกันมากขึ้น:

```csharp
.WithTextSplitter(new RecursiveTextSplitter(500, 50))
```

### TokenTextSplitter

แบ่งตามจำนวน token แทนตัวอักษร แม่นยำกว่าสำหรับการจัดการ context window ของ LLM:

```csharp
.WithTextSplitter(new TokenTextSplitter(256, 32))
```

ใช้เมื่อ embedding model มีข้อจำกัด token ที่เข้มงวด

### MarkdownTextSplitter

Splitter ที่เข้าใจโครงสร้าง Markdown รู้จัก heading (H1–H6), code fence และตาราง แบ่งเนื้อหาเป็นหน่วยที่มีความหมาย:

```csharp
.WithTextSplitter(new MarkdownTextSplitter(500, 50))
```

เหมาะที่สุดสำหรับไฟล์เอกสาร README และ output จาก document loader อย่าง Office และ HWP

> [!TIP]
> Document loader สำหรับ Word, Excel, PowerPoint และ HWP แปลงเอกสารเป็น Markdown ภายใน การใช้ `MarkdownTextSplitter` กับเอกสารเหล่านี้ช่วยให้โครงสร้างตารางและ code block ยังคงอยู่ตลอดกระบวนการ chunking

#### คุณภาพการแบ่งตาราง

`MarkdownTextSplitter` แบ่ง Markdown table ที่ **ขอบเขตแถว** ไม่ตัดกลางแถวเด็ดขาด และแต่ละ chunk ที่ได้จะมี **แถว header และบรรทัด separator** โดยอัตโนมัติ:

```
ตารางต้นฉบับ:
| ชื่อ   | แผนก  | เงินเดือน |
|--------|-------|-----------|
| Alice  | Dev   | $90,000   |
| Bob    | PM    | $85,000   |
| Carol  | Design| $80,000   |

→ Chunk 1:
| ชื่อ   | แผนก  | เงินเดือน |
|--------|-------|-----------|
| Alice  | Dev   | $90,000   |
| Bob    | PM    | $85,000   |

→ Chunk 2:
| ชื่อ   | แผนก  | เงินเดือน |
|--------|-------|-----------|
| Carol  | Design| $80,000   |
```

แต่ละ chunk คือตารางที่สมบูรณ์ในตัวเอง — รับประกันคุณภาพ embedding และการค้นหา

#### การป้องกัน Code Block

Code fence (`` ``` ``) ถือเป็น **หน่วยที่แบ่งไม่ได้** Code block จะไม่ถูกแบ่งกลางทางแม้จะเกินขนาด chunk เพื่อรักษา semantics ของโค้ด

#### Heading Breadcrumb

แต่ละ chunk จะมี path ของ heading นำหน้าโดยอัตโนมัติ เพื่อเพิ่ม context สำหรับ vector search:

```
# คู่มือผลิตภัณฑ์
## คู่มือการติดตั้ง
### Windows

(เนื้อหาจริงของ section นี้)
```

ฟีเจอร์นี้ควบคุมด้วย property `IncludeHeadingBreadcrumb` (ค่าเริ่มต้น: `true`)

## การเลือกพารามิเตอร์

| พารามิเตอร์ | ผลลัพธ์ |
|-----------|--------|
| `chunkSize` (ใหญ่ขึ้น) | context มากต่อ chunk น้อย chunk ค่า embedding ถูกกว่า |
| `chunkSize` (เล็กลง) | ค้นหาแม่นยำขึ้น chunk มากขึ้น embedding มากขึ้น |
| `chunkOverlap` | ป้องกันการสูญหายของข้อมูลที่ขอบเขต chunk |

จุดเริ่มต้นที่แนะนำ: `chunkSize: 500, chunkOverlap: 50`

## ขนาด Chunk เทียบกับ Token (หลายภาษา)

`chunkSize` วัดเป็น **ตัวอักษร** แต่ขีดจำกัดของ embedding model วัดเป็น **token** ตัวอักษรจำนวนเท่ากันให้ token ต่างกันมากตามภาษา:

| ภาษา | 1,000 ตัวอักษร ≈ token | chunkSize ที่แนะนำ |
|----------|----------------------|-----------------------|
| อังกฤษ | ~250 token | 500–2,000 |
| เกาหลี / ญี่ปุ่น / จีน | ~800–1,500 token | 300–1,000 |

> [!WARNING]
> ข้อความ CJK (เกาหลี ญี่ปุ่น จีน) มีอัตราส่วน token ต่อตัวอักษรสูงกว่าภาษาอังกฤษมาก ถ้า chunk เกินขีดจำกัด token ของ embedding model (เช่น 2,048 token) จะเกิด error ลด `chunkSize` ลงมากพอเมื่อทำงานกับเอกสาร CJK

ตัวอย่างกับ embedding model ที่มีขีดจำกัด 2,048 token:

```csharp
// เอกสารภาษาอังกฤษ: 2000 ตัวอักษร ≈ 500 token → ยังอยู่ในขีดจำกัด
.WithTextSplitter(new MarkdownTextSplitter(2000, 200))

// เอกสารภาษาเกาหลี: 1000 ตัวอักษร ≈ 1000 token → ปลอดภัย
.WithTextSplitter(new MarkdownTextSplitter(1000, 200))
```

## Splitter ต่อเอกสาร

ใช้ splitter ต่างกันในแต่ละเอกสารใน `RagBuilder`:

```csharp
.WithRag(rag => rag
    .AddDocuments(new PlainTextDocumentLoader(), "readme.md", new MarkdownTextSplitter(600, 60))
    .AddDocuments(new PlainTextDocumentLoader(), "data.txt",  new RecursiveTextSplitter(300, 30))
    .WithTextSplitter(new RecursiveTextSplitter(500, 50))  // ค่าเริ่มต้นสำหรับที่เหลือ
)
```

## Splitter แบบกำหนดเอง

หากต้องการเขียนโมดูลการแบ่งแบบกำหนดเองแล้วนำมาเชื่อมต่อ ให้ implement `ITextSplitter`:

```csharp
public class SentenceSplitter : ITextSplitter
{
    public IReadOnlyList<RagChunk> Split(RagDocument document)
    {
        var sentences = document.Content.Split(". ");
        return sentences.Select((s, i) => new RagChunk
        {
            Content = s,
            Index = i,
            DocumentId = document.Id
        }).ToList();
    }
}

// Register:
.WithTextSplitter(new SentenceSplitter())
```
