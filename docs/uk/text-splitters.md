# Розділювачі тексту

Розділювачі ділять документи на чанки перед ембеддингом. Розмір чанка та перекриття суттєво впливають на якість пошуку.

## Доступні розділювачі

### CharacterTextSplitter

Розбиває за кількістю символів. Швидкий і простий, але може розрізати текст посеред речення:

```csharp
.WithTextSplitter(new CharacterTextSplitter(500, 50))
```

### RecursiveTextSplitter (рекомендований за замовчуванням)

Намагається розбивати за смисловими межами у порядку пріоритету: абзаци → речення → слова → символи. Дає більш зв'язні чанки:

```csharp
.WithTextSplitter(new RecursiveTextSplitter(500, 50))
```

### TokenTextSplitter

Розбиває за кількістю токенів, а не символів. Точніше враховує ліміти контекстного вікна LLM:

```csharp
.WithTextSplitter(new TokenTextSplitter(256, 32))
```

Корисний, коли модель ембеддингу має суворі обмеження за кількістю токенів.

### MarkdownTextSplitter

Зберігає структуру Markdown — розбиває за заголовками, списками та блоками коду, а потім дробить решту тексту посимвольно:

```csharp
.WithTextSplitter(new MarkdownTextSplitter(500, 50))
```

Найкращий вибір для документації, README та будь-якого структурованого Markdown-контенту.

## Підбір параметрів

| Параметр | Вплив |
|----------|-------|
| `chunkSize` (більше) | Більше контексту на чанк, менше чанків, дешевший ембеддинг |
| `chunkSize` (менше) | Точніший пошук, більше чанків, більше ембеддингів |
| `chunkOverlap` | Запобігає втраті інформації на стиках чанків |

Добра відправна точка: `chunkSize: 500, chunkOverlap: 50`.

## Розділювач для кожного документа

У `RagBuilder` можна призначити окремий розділювач для кожного документа:

```csharp
.WithRag(rag => rag
    .AddDocuments(new PlainTextDocumentLoader(), "readme.md", new MarkdownTextSplitter(600, 60))
    .AddDocuments(new PlainTextDocumentLoader(), "data.txt",  new RecursiveTextSplitter(300, 30))
    .WithTextSplitter(new RecursiveTextSplitter(500, 50))  // за замовчуванням для решти
)
```

## Власний розділювач

Реалізуйте інтерфейс `ITextSplitter` для повністю користувацької логіки розбиття:

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

// Реєстрація:
.WithTextSplitter(new SentenceSplitter())
```
