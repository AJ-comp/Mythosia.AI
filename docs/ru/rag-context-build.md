# Построение контекста

> 📍 **Пайплайн вопрос-ответ:** [Переписывание запросов](rag-query-rewriting.md) → [Эмбеддинг](rag-embedding.md) → [Фильтрация](rag-filtering.md) → [Поиск](rag-hybrid-search.md) → [Переранжирование](rag-reranking.md) → **`Построение контекста`**

## Что такое построение контекста?

Построение контекста — **последний этап** RAG-пайплайна. После извлечения и ранжирования наиболее релевантных чанков этот этап **собирает их в промпт**, который LLM использует для генерации ответа.

Качественно структурированный промпт снижает галлюцинации и помогает модели опираться на предоставленный контекст.

## Context Builder по умолчанию

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

## Шаблоны промптов

Используйте `{context}` и `{question}` в качестве заполнителей:

```csharp
.WithRag(rag => rag
    .WithPromptTemplate("""
        Вы — ассистент клиентской поддержки.
        Используйте ТОЛЬКО следующие документы для ответа.
        Если ответ не найден в документах, скажите
        «У меня нет такой информации.»

        Документы:
        {context}

        Вопрос клиента: {question}
        """)
    .AddDocument("support-kb.txt")
)
```

### Когда использовать шаблоны

- **Ограничить поведение** — «Если ответа нет в контексте, скажите "Не знаю"»
- **Задать тон** — «Отвечайте профессионально и кратко»
- **Назначить роль** — «Вы — медицинский консультант»
- **Контролировать язык** — «Всегда отвечайте на русском языке»

## Собственный Context Builder

Реализуйте `IContextBuilder` для полного контроля:

```csharp
public class MyContextBuilder : IContextBuilder
{
    public string BuildContext(string query, IReadOnlyList<VectorSearchResult> searchResults)
    {
        var sb = new StringBuilder();
        sb.AppendLine("### Релевантная информация ###");

        foreach (var result in searchResults)
        {
            var source = result.Record.Metadata.TryGetValue("source", out var s) ? s : "неизвестно";
            sb.AppendLine($"📄 Источник: {source} (релевантность: {result.Score:P0})");
            sb.AppendLine(result.Record.Content);
            sb.AppendLine("---");
        }

        sb.AppendLine($"Ответьте на основе информации выше: {query}");
        return sb.ToString();
    }
}
```

## Внутренний механизм

```
Результаты поиска + Запрос → ContextBuilder.BuildContext() → Промпт → LLM
```

Порядок выбора:

1. **Пользовательский `IContextBuilder`** — через `.WithContextBuilder()`
2. **`TemplateContextBuilder`** — через `.WithPromptTemplate()`
3. **`DefaultContextBuilder`** — по умолчанию

## Следующие шаги

- [Настройка пайплайна](rag-pipeline.md) — тонкая настройка поведения RAG
- [Переранжирование](rag-reranking.md) — улучшить качество чанков перед построением
- [Основы RAG](rag.md) — обзор полного процесса
