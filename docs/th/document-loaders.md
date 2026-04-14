# Document Loaders

Document loader แปลงไฟล์เป็นออบเจกต์ `DoclingDocument` ที่มีโครงสร้าง จากนั้นส่งต่อไปยัง RAG pipeline ได้

## การติดตั้ง

Loader สำหรับ Office และ PDF รวมอยู่ใน `Mythosia.AI.Rag` หากต้องการใช้แบบ standalone:

```bash
dotnet add package Mythosia.Documents.Office
dotnet add package Mythosia.Documents.Pdf
```

## รูปแบบที่รองรับ

| Loader | รูปแบบ | Package |
|--------|--------|---------|
| `PdfDocumentLoader` | `.pdf` | `Mythosia.Documents.Pdf` |
| `WordDocumentLoader` | `.docx` | `Mythosia.Documents.Office` |
| `ExcelDocumentLoader` | `.xlsx` | `Mythosia.Documents.Office` |
| `PowerPointDocumentLoader` | `.pptx` | `Mythosia.Documents.Office` |
| `HwpDocumentLoader` | `.hwp` | `Mythosia.Documents.Hwp` |
| `PlainTextDocumentLoader` | `.txt`, `.md` เป็นต้น | `Mythosia.AI.Rag` |

## PDF

```csharp
var loader = new PdfDocumentLoader(new PdfParserOptions
{
    Password = "secret",           // สำหรับ PDF ที่เข้ารหัส
    IncludeMetadata = true,        // ดึงชื่อเรื่องและผู้แต่ง
    IncludePageNumbers = true,     // เพิ่มหมายเลขหน้า
    NormalizeWhitespace = true     // ยุบช่องว่างเกิน
});

var docs = await loader.LoadAsync("report.pdf");
```

## Word (.docx)

```csharp
var loader = new WordDocumentLoader(new OfficeParserOptions
{
    IncludeMetadata = true,
    NormalizeWhitespace = true
});

var docs = await loader.LoadAsync("document.docx");
```

## Excel (.xlsx)

```csharp
var loader = new ExcelDocumentLoader(new OfficeParserOptions
{
    IncludeSheetNames = true,  // เพิ่มชื่อ sheet นำหน้าแต่ละ section
    NormalizeWhitespace = true
});

var docs = await loader.LoadAsync("spreadsheet.xlsx");
```

## PowerPoint (.pptx)

```csharp
var loader = new PowerPointDocumentLoader(new OfficeParserOptions
{
    IncludeSlideNumbers = true,  // เพิ่มหมายเลข slide นำหน้าแต่ละ section
    NormalizeWhitespace = true
});

var docs = await loader.LoadAsync("presentation.pptx");
```

## HWP (.hwp)

แปลงไฟล์ Hangul Word Processor (HWP) ของเกาหลี ต้องติดตั้ง package แยก:

```bash
dotnet add package Mythosia.Documents.Hwp
```

```csharp
var loader = new HwpDocumentLoader(options: new HwpParserOptions
{
    IncludeMetadata = true,
    NormalizeWhitespace = true,
    IncludeSectionHeaders = false
});

var docs = await loader.LoadAsync("report.hwp");
```

HWP loader แปลงข้อความ ตาราง และโครงสร้าง heading เป็น `DoclingDocument` แล้วแสดงผลเป็น Markdown ตารางถูก render เป็น Markdown table (`| ... |`) ดังนั้นการใช้ `MarkdownTextSplitter` จะรักษาโครงสร้างตารางตลอดกระบวนการ chunking

## การใช้ใน RAG

Loader ถูกรวมโดยอัตโนมัติเมื่อใช้ `.AddDocument()` ใน `RagBuilder` หากต้องการโหลดเองและเพิ่มผลลัพธ์:

```csharp
var loader = new PdfDocumentLoader(new PdfParserOptions { IncludePageNumbers = true });
var docs = await loader.LoadAsync("report.pdf");

var service = new AnthropicService(apiKey, http)
    .WithRag(rag => rag
        .AddDocument("report.pdf")  // ตรวจรูปแบบอัตโนมัติ
        .AddDocument("notes.docx")
    );
```

## โครงสร้าง DoclingDocument

แต่ละไฟล์ที่โหลดจะกลายเป็น `DoclingDocument` ที่มีโครงสร้างต้นไม้:

```csharp
var docs = await loader.LoadAsync("report.pdf");
var doc = docs[0];

Console.WriteLine(doc.Title);   // ชื่อเอกสาร
Console.WriteLine(doc.Source);  // path ของไฟล์

foreach (var item in doc.Document)
{
    switch (item)
    {
        case SectionHeaderItem h: Console.WriteLine($"## {h.Text}"); break;
        case TextItem t:          Console.WriteLine(t.Text); break;
        case TableItem table:     /* ประมวลผลเซลล์ตาราง */ break;
        case CodeItem code:       Console.WriteLine(code.Text); break;
    }
}
```

**ประเภท element:** `TextItem`, `SectionHeaderItem`, `TitleItem`, `ListItem`, `TableItem`, `CodeItem`, `FormulaItem`, `PictureItem`, `GroupItem`, `RefItem`

## ภาพรวม Processing Pipeline

เอกสารผ่านสามขั้นตอนก่อนกลายเป็น chunk ที่ค้นหาได้ใน RAG แต่ละขั้นตอนจัดการโดย package ที่ต่างกัน

```text
┌─────────────────────────────────────────────────────────────┐
│  1. Parsing (Documents.Hwp / Documents.Office / Documents.Pdf)
│     .hwp, .pdf, .docx ฯลฯ → DoclingDocument (structured model)
└──────────────────────────┬──────────────────────────────────┘
                           ↓
┌──────────────────────────┴──────────────────────────────────┐
│  2. Serialization (Documents.Abstractions)
│     DoclingDocument → Markdown string
│     MarkdownSerializer แปลง heading, table, code block
│     เป็น Markdown syntax
│     การ render ตารางสามารถสลับได้ผ่าน ITableSerializer
└──────────────────────────┬──────────────────────────────────┘
                           ↓
┌──────────────────────────┴──────────────────────────────────┐
│  3. Chunking (AI.Rag)
│     Markdown string → รายการ chunk ที่ค้นหาได้
│     MarkdownTextSplitter แบ่งตาม header เป็น section
│     จากนั้น cascade: ย่อหน้า → บรรทัด → คำ
└─────────────────────────────────────────────────────────────┘
```

## การรวม Document Loader กับ Text Splitter

`MarkdownTextSplitter` เหมาะสมที่สุดสำหรับเอกสาร Office/HWP:

```csharp
var service = new AnthropicService(apiKey, http)
    .WithRag(rag => rag
        .AddDocuments(new WordDocumentLoader(), "manual.docx", new MarkdownTextSplitter(1000, 100))
        .AddDocuments(new ExcelDocumentLoader(), "data.xlsx", new MarkdownTextSplitter(1000, 100))
    );
```

`MarkdownTextSplitter` แบ่งตารางที่ขอบเขตแถวและใส่ header ในแต่ละ chunk อัตโนมัติ ทำให้ข้อมูลตารางยังคงครบถ้วนในผลการค้นหา ดู [Text Splitters](text-splitters.md) สำหรับรายละเอียด
