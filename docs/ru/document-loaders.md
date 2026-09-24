# Загрузчики документов

Загрузчики документов превращают файлы в структурированные объекты `DoclingDocument`, которые затем передаются в RAG-пайплайн.

<a id="file-source-identity"></a>

## Сохранять стабильную идентичность файла

Один файл, зарегистрированный по относительному и абсолютному пути, должен обновлять один документ, а одноимённые файлы в разных папках должны оставаться разными. `WordDocumentLoader`, `ExcelDocumentLoader`, `PowerPointDocumentLoader` и `PdfDocumentLoader` теперь записывают в `DoclingDocument.Source` нормализованный абсолютный путь, как встроенные TXT-загрузчики. RAG строит по нему автоматический ID; явно заданные ID остаются под контролем вызывающего кода. В стандартных ссылках на источники могут отображаться абсолютные пути.

Ранее сохранённые относительные ID автоматически не переносятся и не удаляются. Найдите прежний ID, явно удалите только этот документ из соответствующего хранилища и проиндексируйте его заново. Другой вариант — проиндексировать полный набор исходников в новую пустую коллекцию, проверить её и переключить приложение. Индексация только с новым абсолютным ID в прежней коллекции оставит старые записи. Не удаляйте посторонние документы. См. [идентичность и миграцию](rag.md#document-identity).

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
| `HwpDocumentLoader` | `.hwp` | `Mythosia.Documents.Hwp` |
| `PlainTextDocumentLoader` | `.txt`, `.md` и др. | `Mythosia.AI.Rag` |

## PDF

```csharp
var loader = new PdfDocumentLoader(options: new PdfParserOptions
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
    IncludeSheetNames = true,  // Добавлять имя листа перед каждой секцией
    NormalizeWhitespace = true
});

var docs = await loader.LoadAsync("spreadsheet.xlsx");
```

## PowerPoint (.pptx)

```csharp
var loader = new PowerPointDocumentLoader(options: new OfficeParserOptions
{
    IncludeSlideNumbers = true,  // Добавлять номер слайда перед каждой секцией
    NormalizeWhitespace = true
});

var docs = await loader.LoadAsync("presentation.pptx");
```

## HWP (.hwp)

Разбор файлов корейского текстового процессора Hangul (HWP). Поставляется отдельным пакетом:

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

HWP-загрузчик преобразует текст, таблицы и структуру заголовков в `DoclingDocument`, который затем выводится в формате Markdown. Таблицы представляются в виде Markdown-таблиц (`| ... |`), поэтому при использовании `MarkdownTextSplitter` структура таблиц полностью сохраняется при разбиении на чанки.

## Использование в RAG

Загрузчики автоматически интегрируются при вызове `.AddDocument()` в `RagBuilder`. Для ручной загрузки:

```csharp
var loader = new PdfDocumentLoader(options: new PdfParserOptions { IncludePageNumbers = true });
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

## Обзор конвейера обработки

Документы проходят три этапа, прежде чем стать чанками, доступными для RAG-поиска. Каждый этап обрабатывается отдельным пакетом.

```text
┌─────────────────────────────────────────────────────────────┐
│  1. Парсинг (Documents.Hwp / Documents.Office / Documents.Pdf)
│     .hwp, .pdf, .docx и др. → DoclingDocument (структурированная модель)
└──────────────────────────┬──────────────────────────────────┘
                           ↓
┌──────────────────────────┴──────────────────────────────────┐
│  2. Сериализация (Documents.Abstractions)
│     DoclingDocument → строка Markdown
│     MarkdownSerializer преобразует заголовки, таблицы,
│     блоки кода в синтаксис Markdown.
│     Рендеринг таблиц заменяем через ITableSerializer.
└──────────────────────────┬──────────────────────────────────┘
                           ↓
┌──────────────────────────┴──────────────────────────────────┐
│  3. Чанкинг (AI.Rag)
│     Строка Markdown → список поисковых чанков
│     MarkdownTextSplitter разделяет по заголовкам на секции,
│     затем каскадно: абзац → строка → граница слова.
└─────────────────────────────────────────────────────────────┘
```

**Этап 1 (Парсинг)** — Каждый загрузчик документов (`HwpDocumentLoader`, `PdfDocumentLoader` и др.) читает исходный файл и преобразует его в `DoclingDocument` — структурированную модель с текстом, заголовками, таблицами и блоками кода в виде дерева.

**Этап 2 (Сериализация)** — При вызове `DoclingDocument.ToMarkdown()` внутренний `MarkdownSerializer` обходит дерево и создаёт строку Markdown. Рендеринг таблиц можно заменить через `ITableSerializer`. Документы HWP по умолчанию используют `SemanticTableSerializer`, который рендерит таблицы-формы с жирными групповыми метками.

**Этап 3 (Чанкинг)** — `MarkdownTextSplitter` RAG-конвейера получает строку Markdown и разбивает её на чанки, удобные для поиска. Он организует секции по заголовкам (`#`, `##` и т.д.) и автоматически включает хлебные крошки (пути родительских заголовков) в каждый чанк.

Поскольку эти три этапа разделены, добавление нового загрузчика документов или изменение стратегии рендеринга таблиц не влияет на остальные этапы.

## Интеграция загрузчиков документов и разделителей текста

Используйте `MarkdownTextSplitter` для сохранения заголовков, блоков кода и строк таблиц. Аргумента overlap нет. Повторяемые заголовки не входят в бюджет содержимого; целый блок кода или заголовок таблицы с одной строкой может превысить его. Строгий лимит проверяйте токенизатором модели на итоговых чанках. См. [Разделители текста](text-splitters.md).

```csharp
var service = new AnthropicService(apiKey, http)
    .WithRag(rag => rag
        .AddDocuments(new WordDocumentLoader(), "manual.docx", new MarkdownTextSplitter(1000))
        .AddDocuments(new ExcelDocumentLoader(), "data.xlsx", new MarkdownTextSplitter(1000))
    );
```

Распознанные GFM-таблицы делятся между строками; заголовок и строка-разделитель повторяются в каждом табличном чанке. Внешние вертикальные черты необязательны (`Name | Value` поддерживается). Так сохраняются названия столбцов; качество поиска зависит также от документов, эмбеддингов и вопросов.
