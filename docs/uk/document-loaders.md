# Завантажувачі документів

Завантажувачі документів перетворюють файли на структуровані об'єкти `DoclingDocument`, які потім передаються до RAG-пайплайну.

<a id="file-source-identity"></a>

## Зберігати сталу ідентичність файлу

Той самий файл за відносним та абсолютним шляхом має оновлювати один документ, а однойменні файли в різних папках мають залишатися різними. `WordDocumentLoader`, `ExcelDocumentLoader`, `PowerPointDocumentLoader` і `PdfDocumentLoader` тепер записують у `DoclingDocument.Source` нормалізований абсолютний шлях, як вбудовані TXT-завантажувачі. RAG формує з нього автоматичний ID; явно задані ID залишаються під контролем викликача. Стандартні посилання на джерела можуть показувати абсолютні шляхи.

Раніше збережені відносні ID автоматично не переносяться й не видаляються. Визначте попередній ID, явно видаліть лише цей документ із відповідного сховища та проіндексуйте його знову. Інший варіант — проіндексувати повний набір джерел у нову порожню колекцію, перевірити її та переключити застосунок. Індексація тільки за новим абсолютним ID у наявній колекції залишає старі записи. Не видаляйте сторонні документи. Див. [ідентичність і міграцію](rag.md#document-identity).

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
var loader = new PdfDocumentLoader(options: new PdfParserOptions
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
    IncludeSheetNames = true,  // Додавати назву аркуша перед кожною секцією
    NormalizeWhitespace = true
});

var docs = await loader.LoadAsync("spreadsheet.xlsx");
```

## PowerPoint (.pptx)

```csharp
var loader = new PowerPointDocumentLoader(options: new OfficeParserOptions
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
var loader = new PdfDocumentLoader(options: new PdfParserOptions { IncludePageNumbers = true });
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

## Огляд конвеєра обробки

Документи проходять три етапи, перш ніж стати чанками, доступними для RAG-пошуку. Кожен етап обробляється окремим пакетом.

```text
┌─────────────────────────────────────────────────────────────┐
│  1. Парсинг (Documents.Hwp / Documents.Office / Documents.Pdf)
│     .hwp, .pdf, .docx тощо → DoclingDocument (структурована модель)
└──────────────────────────┬──────────────────────────────────┘
                           ↓
┌──────────────────────────┴──────────────────────────────────┐
│  2. Серіалізація (Documents.Abstractions)
│     DoclingDocument → рядок Markdown
│     MarkdownSerializer перетворює заголовки, таблиці,
│     блоки коду в синтаксис Markdown.
│     Рендеринг таблиць замінний через ITableSerializer.
└──────────────────────────┬──────────────────────────────────┘
                           ↓
┌──────────────────────────┴──────────────────────────────────┐
│  3. Чанкінг (AI.Rag)
│     Рядок Markdown → список пошукових чанків
│     MarkdownTextSplitter розділяє за заголовками на секції,
│     потім каскадно: абзац → рядок → межа слова.
└─────────────────────────────────────────────────────────────┘
```

**Етап 1 (Парсинг)** — Кожен завантажувач документів (`HwpDocumentLoader`, `PdfDocumentLoader` тощо) читає вихідний файл і перетворює його на `DoclingDocument` — структуровану модель з текстом, заголовками, таблицями та блоками коду у вигляді дерева.

**Етап 2 (Серіалізація)** — При виклику `DoclingDocument.ToMarkdown()` внутрішній `MarkdownSerializer` обходить дерево і створює рядок Markdown. Рендеринг таблиць можна замінити через `ITableSerializer`. Документи HWP за замовчуванням використовують `SemanticTableSerializer`, який рендерить таблиці-форми з жирними груповими мітками.

**Етап 3 (Чанкінг)** — `MarkdownTextSplitter` RAG-конвеєра отримує рядок Markdown і розбиває його на чанки, зручні для пошуку. Він організовує секції за заголовками (`#`, `##` тощо) і автоматично додає хлібні крихти (шляхи батьківських заголовків) до кожного чанка.

Оскільки ці три етапи розділені, додавання нового завантажувача документів або зміна стратегії рендерингу таблиць не впливає на інші етапи.

## Інтеграція завантажувачів документів та розділювачів тексту

Використовуйте `MarkdownTextSplitter`, щоб зберегти заголовки, блоки коду й рядки таблиць. Аргументу overlap немає. Повторювані заголовки не входять у бюджет; цілий блок коду або заголовок таблиці з рядком може його перевищити. Суворий ліміт перевіряйте токенізатором моделі на кінцевих чанках. Див. [Розділювачі тексту](text-splitters.md).

```csharp
var service = new AnthropicService(apiKey, http)
    .WithRag(rag => rag
        .AddDocuments(new WordDocumentLoader(), "manual.docx", new MarkdownTextSplitter(1000))
        .AddDocuments(new ExcelDocumentLoader(), "data.xlsx", new MarkdownTextSplitter(1000))
    );
```

Розпізнані GFM-таблиці діляться між рядками; заголовок і рядок-роздільник повторюються в кожному табличному чанку. Зовнішні вертикальні риски необов’язкові (`Name | Value` підтримується). Так зберігаються назви стовпців; якість пошуку також залежить від документів, ембедингів і запитань.
