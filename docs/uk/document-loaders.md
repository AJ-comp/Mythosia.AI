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
| `HwpDocumentLoader` | `.hwp` | `Mythosia.Documents.Hwp` |
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

## HWP (.hwp)

Розбір файлів корейського текстового процесора Hangul (HWP). Постачається окремим пакетом:

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

HWP-завантажувач перетворює текст, таблиці та структуру заголовків у `DoclingDocument`, який потім виводиться у форматі Markdown. Таблиці відтворюються як Markdown-таблиці (`| ... |`), тому при використанні `MarkdownTextSplitter` структура таблиць повністю зберігається під час розбиття на чанки.

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

## Інтеграція завантажувачів документів та розділювачів тексту

Завантажувачі документів Word, Excel, PowerPoint та HWP внутрішньо перетворюють файли через `DoclingDocument` у **формат Markdown**. Таблиці стають Markdown-таблицями (`| Заголовок |` + `|---|` + `| Дані |`), а заголовки та блоки коду також виводяться у синтаксисі Markdown.

Тому `MarkdownTextSplitter` — найефективніший вибір для документів Office/HWP:

```csharp
var service = new AnthropicService(apiKey, http)
    .WithRag(rag => rag
        .AddDocuments(new WordDocumentLoader(), "manual.docx", new MarkdownTextSplitter(1000, 100))
        .AddDocuments(new ExcelDocumentLoader(), "data.xlsx", new MarkdownTextSplitter(1000, 100))
    );
```

`MarkdownTextSplitter` розділяє таблиці порядково та автоматично додає заголовки до кожного чанка, тому табличні дані в результатах пошуку залишаються цілісними. Детальніше див. [Розділювачі тексту](text-splitters.md).
