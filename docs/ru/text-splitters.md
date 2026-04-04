# Разделители текста

Разделители делят документы на чанки перед эмбеддингом. Размер чанка и перекрытие существенно влияют на качество поиска.

## Доступные разделители

### CharacterTextSplitter

Разбивает по количеству символов. Быстрый и простой, но может разрезать текст посреди предложения:

```csharp
.WithTextSplitter(new CharacterTextSplitter(500, 50))
```

### RecursiveTextSplitter (рекомендуется по умолчанию)

Старается разбивать по смысловым границам в порядке приоритета: абзацы → предложения → слова → символы. Даёт более связные чанки:

```csharp
.WithTextSplitter(new RecursiveTextSplitter(500, 50))
```

### TokenTextSplitter

Разбивает по количеству токенов, а не символов. Точнее учитывает лимиты контекстного окна LLM:

```csharp
.WithTextSplitter(new TokenTextSplitter(256, 32))
```

Полезен, когда модель эмбеддинга имеет строгие ограничения по количеству токенов.

### MarkdownTextSplitter

Сохраняет структуру Markdown — разбивает по заголовкам, спискам и блокам кода, а затем дробит оставшийся текст посимвольно:

```csharp
.WithTextSplitter(new MarkdownTextSplitter(500, 50))
```

Лучший выбор для документации, README и любого структурированного Markdown-контента.

## Подбор параметров

| Параметр | Влияние |
|----------|---------|
| `chunkSize` (больше) | Больше контекста на чанк, меньше чанков, дешевле эмбеддинг |
| `chunkSize` (меньше) | Более точный поиск, больше чанков, больше эмбеддингов |
| `chunkOverlap` | Предотвращает потерю информации на стыках чанков |

Хорошая отправная точка: `chunkSize: 500, chunkOverlap: 50`.

## Разделитель для каждого документа

В `RagBuilder` можно назначить отдельный разделитель для каждого документа:

```csharp
.WithRag(rag => rag
    .AddDocuments(new PlainTextDocumentLoader(), "readme.md", new MarkdownTextSplitter(600, 60))
    .AddDocuments(new PlainTextDocumentLoader(), "data.txt",  new RecursiveTextSplitter(300, 30))
    .WithTextSplitter(new RecursiveTextSplitter(500, 50))  // по умолчанию для остальных
)
```

## Собственный разделитель

Реализуйте интерфейс `ITextSplitter` для полностью пользовательской логики разбиения:

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

// Регистрация:
.WithTextSplitter(new SentenceSplitter())
```
