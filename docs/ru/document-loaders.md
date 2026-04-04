# Загрузчики документов

Загрузчики документов превращают файлы в структурированные объекты `DoclingDocument`, которые затем передаются в RAG-пайплайн.

## Установка

Загрузчики Office и PDF входят в `Mythosia.AI.Rag`. Для использования отдельно:

```bash
dotnet add package Mythosia.Documents.Office
dotnet add package Mythosia.Documents.Pdf
```

## Поддерживаемые форматы

| Загрузчик | Формат | Пакет |
|-----------|--------|-------|
| `PdfDocumentLoader` | `.pdf` | `Mythosia.Documents.Pdf` |
| `WordDocumentLoader` | `.docx` | `Mythosia.Documents.Office` |
| `ExcelDocumentLoader` | `.xlsx` | `Mythosia.Documents.Office` |
| `PowerPointDocumentLoader` | `.pptx` | `Mythosia.Documents.Office` |
| `PlainTextDocumentLoader` | `.txt`, `.md` и др. | `Mythosia.AI.Rag` |

## PDF

```csharp
var loader = new PdfDocumentLoader(new PdfParserOptions
{
    Password = "secret",           // Для зашифрованных PDF
    IncludeMetadata = true,        // Извлечение заголовка, автора
    IncludePageNumbers = true,     // Маркеры номеров страниц
    NormalizeWhitespace = true     // Нормализация пробелов
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
    IncludeSheetNames = true,  // Добавлять имя листа перед каждой секцией
    NormalizeWhitespace = true
});

var docs = await loader.LoadAsync("spreadsheet.xlsx");
```

## PowerPoint (.pptx)

```csharp
var loader = new PowerPointDocumentLoader(new OfficeParserOptions
{
    IncludeSlideNumbers = true,  // Добавлять номер слайда перед каждой секцией
    NormalizeWhitespace = true
});

var docs = await loader.LoadAsync("presentation.pptx");
```

## Использование в RAG

Загрузчики автоматически интегрируются при вызове `.AddDocument()` в `RagBuilder`. Для ручной загрузки:

```csharp
var loader = new PdfDocumentLoader(new PdfParserOptions { IncludePageNumbers = true });
var docs = await loader.LoadAsync("report.pdf");

var service = new AnthropicService(apiKey, http)
    .WithRag(rag => rag
        .AddDocument("report.pdf")  // автоматически определяет формат
        .AddDocument("notes.docx")
    );
```

## Структура DoclingDocument

Каждый загруженный файл представлен в виде `DoclingDocument` с иерархическим деревом элементов:

```csharp
var docs = await loader.LoadAsync("report.pdf");
var doc = docs[0];

Console.WriteLine(doc.Title);   // Заголовок документа
Console.WriteLine(doc.Source);  // Путь к файлу

foreach (var item in doc.Document)
{
    switch (item)
    {
        case SectionHeaderItem h: Console.WriteLine($"## {h.Text}"); break;
        case TextItem t:          Console.WriteLine(t.Text); break;
        case TableItem table:     /* обработка ячеек таблицы */ break;
        case CodeItem code:       Console.WriteLine(code.Text); break;
    }
}
```

**Типы элементов:** `TextItem`, `SectionHeaderItem`, `TitleItem`, `ListItem`, `TableItem`, `CodeItem`, `FormulaItem`, `PictureItem`, `GroupItem`, `RefItem`
