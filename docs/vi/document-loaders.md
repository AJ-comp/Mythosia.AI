# Document Loader

Document loader phân tích file thành các đối tượng `DoclingDocument` có cấu trúc, sau đó có thể truyền vào RAG pipeline.

## Cài đặt

Loader cho Office và PDF được bao gồm trong `Mythosia.AI.Rag`. Để dùng độc lập:

```bash
dotnet add package Mythosia.Documents.Office
dotnet add package Mythosia.Documents.Pdf
```

## Định dạng được hỗ trợ

| Loader | Định dạng | Package |
|--------|--------|---------|
| `PdfDocumentLoader` | `.pdf` | `Mythosia.Documents.Pdf` |
| `WordDocumentLoader` | `.docx` | `Mythosia.Documents.Office` |
| `ExcelDocumentLoader` | `.xlsx` | `Mythosia.Documents.Office` |
| `PowerPointDocumentLoader` | `.pptx` | `Mythosia.Documents.Office` |
| `HwpDocumentLoader` | `.hwp` | `Mythosia.Documents.Hwp` |
| `PlainTextDocumentLoader` | `.txt`, `.md`, v.v. | `Mythosia.AI.Rag` |

## PDF

```csharp
var loader = new PdfDocumentLoader(options: new PdfParserOptions
{
    Password = "secret",           // Cho PDF được mã hóa
    IncludeMetadata = true,        // Trích xuất tiêu đề, tác giả
    IncludePageNumbers = true,     // Thêm đánh dấu số trang
    NormalizeWhitespace = true     // Thu gọn khoảng trắng thừa
});

var docs = await loader.LoadAsync("report.pdf");
```

## Word (.docx)

```csharp
var loader = new WordDocumentLoader(options: new OfficeParserOptions
{
    IncludeMetadata = true,
    NormalizeWhitespace = true
});

var docs = await loader.LoadAsync("document.docx");
```

## Excel (.xlsx)

```csharp
var loader = new ExcelDocumentLoader(options: new OfficeParserOptions
{
    IncludeSheetNames = true,  // Thêm tên sheet trước mỗi section
    NormalizeWhitespace = true
});

var docs = await loader.LoadAsync("spreadsheet.xlsx");
```

## PowerPoint (.pptx)

```csharp
var loader = new PowerPointDocumentLoader(options: new OfficeParserOptions
{
    IncludeSlideNumbers = true,  // Thêm số slide trước mỗi section
    NormalizeWhitespace = true
});

var docs = await loader.LoadAsync("presentation.pptx");
```

## HWP (.hwp)

Phân tích file định dạng Hangul Word Processor (HWP) của Hàn Quốc. Cài package riêng:

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

HWP loader chuyển đổi văn bản, bảng và cấu trúc heading thành `DoclingDocument`, sau đó xuất ra Markdown. Bảng được render dưới dạng Markdown table (`| ... |`), nên dùng `MarkdownTextSplitter` sẽ giữ nguyên cấu trúc bảng trong suốt quá trình chunking.

## Dùng trong RAG

Loader được tích hợp tự động khi dùng `.AddDocument()` trong `RagBuilder`. Để load thủ công và thêm kết quả:

```csharp
var loader = new PdfDocumentLoader(options: new PdfParserOptions { IncludePageNumbers = true });
var docs = await loader.LoadAsync("report.pdf");

var service = new AnthropicService(apiKey, http)
    .WithRag(rag => rag
        .AddDocument("report.pdf")  // tự phát hiện định dạng
        .AddDocument("notes.docx")
    );
```

## Cấu trúc DoclingDocument

Mỗi file được load trở thành một `DoclingDocument` với cây phần tử phân cấp:

```csharp
var docs = await loader.LoadAsync("report.pdf");
var doc = docs[0];

Console.WriteLine(doc.Title);   // Tiêu đề tài liệu
Console.WriteLine(doc.Source);  // Đường dẫn file

foreach (var item in doc.Document)
{
    switch (item)
    {
        case SectionHeaderItem h: Console.WriteLine($"## {h.Text}"); break;
        case TextItem t:          Console.WriteLine(t.Text); break;
        case TableItem table:     /* xử lý ô bảng */ break;
        case CodeItem code:       Console.WriteLine(code.Text); break;
    }
}
```

**Loại phần tử:** `TextItem`, `SectionHeaderItem`, `TitleItem`, `ListItem`, `TableItem`, `CodeItem`, `FormulaItem`, `PictureItem`, `GroupItem`, `RefItem`

## Tổng quan pipeline xử lý

Tài liệu trải qua ba giai đoạn trước khi trở thành các đoạn có thể tìm kiếm trong RAG. Mỗi giai đoạn do một package khác nhau xử lý.

```text
┌─────────────────────────────────────────────────────────────┐
│  1. Phân tích (Documents.Hwp / Documents.Office / Documents.Pdf)
│     .hwp, .pdf, .docx, v.v. → DoclingDocument (model có cấu trúc)
└──────────────────────────┬──────────────────────────────────┘
                           ↓
┌──────────────────────────┴──────────────────────────────────┐
│  2. Serialization (Documents.Abstractions)
│     DoclingDocument → chuỗi Markdown
│     MarkdownSerializer chuyển đổi heading, bảng, code block
│     thành cú pháp Markdown.
│     Render bảng có thể hoán đổi qua ITableSerializer.
└──────────────────────────┬──────────────────────────────────┘
                           ↓
┌──────────────────────────┴──────────────────────────────────┐
│  3. Chunking (AI.Rag)
│     chuỗi Markdown → danh sách chunk có thể tìm kiếm
│     MarkdownTextSplitter chia theo header thành section,
│     sau đó cascade: đoạn → dòng → ranh giới từ.
└─────────────────────────────────────────────────────────────┘
```

## Tích hợp Document Loader & Text Splitter

`MarkdownTextSplitter` là lựa chọn hiệu quả nhất cho tài liệu Office/HWP:

```csharp
var service = new AnthropicService(apiKey, http)
    .WithRag(rag => rag
        .AddDocuments(new WordDocumentLoader(), "manual.docx", new MarkdownTextSplitter(1000, 100))
        .AddDocuments(new ExcelDocumentLoader(), "data.xlsx", new MarkdownTextSplitter(1000, 100))
    );
```

`MarkdownTextSplitter` chia bảng tại ranh giới hàng và tự động thêm header vào mỗi đoạn, đảm bảo dữ liệu bảng còn nguyên vẹn trong kết quả tìm kiếm. Xem [Text Splitter](text-splitters.md) để biết thêm.
