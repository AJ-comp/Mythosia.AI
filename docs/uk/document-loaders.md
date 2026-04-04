# Завантажувачі документів

Завантажувачі документів перетворюють файли на структуровані об'єкти `DoclingDocument`, які потім передаються до RAG-пайплайну.

## Встановлення

Завантажувачі Office та PDF входять до `Mythosia.AI.Rag`. Для окремого використання:

```bash
dotnet add package Mythosia.Documents.Office
dotnet add package Mythosia.Documents.Pdf
```

## Підтримувані формати

| Завантажувач | Формат | Пакет |
|-------------|--------|-------|
| `PdfDocumentLoader` | `.pdf` | `Mythosia.Documents.Pdf` |
| `WordDocumentLoader` | `.docx` | `Mythosia.Documents.Office` |
| `ExcelDocumentLoader` | `.xlsx` | `Mythosia.Documents.Office` |
| `PowerPointDocumentLoader` | `.pptx` | `Mythosia.Documents.Office` |
| `PlainTextDocumentLoader` | `.txt`, `.md` тощо | `Mythosia.AI.Rag` |

## PDF

```csharp
var loader = new PdfDocumentLoader(new PdfParserOptions
{
    Password = "secret",           // Для зашифрованих PDF
    IncludeMetadata = true,        // Витяг заголовка, автора
    IncludePageNumbers = true,     // Маркери номерів сторінок
    NormalizeWhitespace = true     // Нормалізація пробілів
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
    IncludeSheetNames = true,  // Додавати назву аркуша перед кожною секцією
    NormalizeWhitespace = true
});

var docs = await loader.LoadAsync("spreadsheet.xlsx");
```

## PowerPoint (.pptx)

```csharp
var loader = new PowerPointDocumentLoader(new OfficeParserOptions
{
    IncludeSlideNumbers = true,  // Додавати номер слайда перед кожною секцією
    NormalizeWhitespace = true
});

var docs = await loader.LoadAsync("presentation.pptx");
```

## Використання в RAG

Завантажувачі автоматично інтегруються при виклику `.AddDocument()` у `RagBuilder`. Для ручного завантаження:

```csharp
var loader = new PdfDocumentLoader(new PdfParserOptions { IncludePageNumbers = true });
var docs = await loader.LoadAsync("report.pdf");

var service = new AnthropicService(apiKey, http)
    .WithRag(rag => rag
        .AddDocument("report.pdf")  // автоматично визначає формат
        .AddDocument("notes.docx")
    );
```

## Структура DoclingDocument

Кожен завантажений файл представлений як `DoclingDocument` з ієрархічним деревом елементів:

```csharp
var docs = await loader.LoadAsync("report.pdf");
var doc = docs[0];

Console.WriteLine(doc.Title);   // Заголовок документа
Console.WriteLine(doc.Source);  // Шлях до файлу

foreach (var item in doc.Document)
{
    switch (item)
    {
        case SectionHeaderItem h: Console.WriteLine($"## {h.Text}"); break;
        case TextItem t:          Console.WriteLine(t.Text); break;
        case TableItem table:     /* обробка комірок таблиці */ break;
        case CodeItem code:       Console.WriteLine(code.Text); break;
    }
}
```

**Типи елементів:** `TextItem`, `SectionHeaderItem`, `TitleItem`, `ListItem`, `TableItem`, `CodeItem`, `FormulaItem`, `PictureItem`, `GroupItem`, `RefItem`
