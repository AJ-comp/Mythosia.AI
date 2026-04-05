# Побудова контексту

> 📍 **Пайплайн запитання-відповіді:** [Переписування запитів](rag-query-rewriting.md) → [Ембеддинг](rag-embedding.md) → [Фільтрація](rag-filtering.md) → [Пошук](rag-hybrid-search.md) → [Переранжування](rag-reranking.md) → **`Побудова контексту`**

## Що таке побудова контексту?

Побудова контексту — **останній етап** RAG-пайплайну. Після витягування та ранжування найбільш релевантних чанків цей етап **збирає їх у промпт**, який LLM використовує для генерації відповіді.

Якісно структурований промпт зменшує галюцинації та допомагає моделі спиратися на наданий контекст.

## Context Builder за замовчуванням

```csharp
var contextBuilder = new DefaultContextBuilder
{
    Header = "Answer the question based on the following context:",
    QueryPrefix = "Question:",
    IncludeScores = false,
    IncludeSource = true
};

.WithRag(rag => rag
    .WithContextBuilder(contextBuilder)
    .AddDocument("docs.txt")
)
```

## Шаблони промптів

Використовуйте `{context}` та `{question}` як заповнювачі:

```csharp
.WithRag(rag => rag
    .WithPromptTemplate("""
        Ви — асистент клієнтської підтримки.
        Використовуйте ТІЛЬКИ наступні документи для відповіді.
        Якщо відповідь не знайдена в документах, скажіть
        «У мене немає такої інформації.»

        Документи:
        {context}

        Питання клієнта: {question}
        """)
    .AddDocument("support-kb.txt")
)
```

### Коли використовувати шаблони

- **Обмежити поведінку** — «Якщо відповіді немає в контексті, скажіть "Не знаю"»
- **Задати тон** — «Відповідайте професійно та стисло»
- **Призначити роль** — «Ви — медичний консультант»
- **Контролювати мову** — «Завжди відповідайте українською»

## Власний Context Builder

Реалізуйте `IContextBuilder` для повного контролю:

```csharp
public class MyContextBuilder : IContextBuilder
{
    public string BuildContext(string query, IReadOnlyList<VectorSearchResult> searchResults)
    {
        var sb = new StringBuilder();
        sb.AppendLine("### Релевантна інформація ###");

        foreach (var result in searchResults)
        {
            var source = result.Record.Metadata.TryGetValue("source", out var s) ? s : "невідомо";
            sb.AppendLine($"📄 Джерело: {source} (релевантність: {result.Score:P0})");
            sb.AppendLine(result.Record.Content);
            sb.AppendLine("---");
        }

        sb.AppendLine($"Відповідайте на основі інформації вище: {query}");
        return sb.ToString();
    }
}
```

## Внутрішній механізм

```
Результати пошуку + Запит → ContextBuilder.BuildContext() → Промпт → LLM
```

Порядок вибору:

1. **Користувацький `IContextBuilder`** — через `.WithContextBuilder()`
2. **`TemplateContextBuilder`** — через `.WithPromptTemplate()`
3. **`DefaultContextBuilder`** — за замовчуванням

## Наступні кроки

- [Налаштування пайплайну](rag-pipeline.md) — тонке налаштування поведінки RAG
- [Переранжування](rag-reranking.md) — покращити якість чанків перед побудовою
- [Основи RAG](rag.md) — огляд повного процесу
