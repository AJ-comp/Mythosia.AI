# Введение

> Grok 4.7 — ещё не опубликованное дополнение; см. [выбор модели, рассуждение и скорость обработки](providers.md#grok-47).

> GPT-6 Sol/Luna ещё не опубликованы в пакетах. См. [выбор модели и требования](providers.md#gpt-6-sol-luna).

Mythosia.AI — модульная .NET-библиотека, объединяющая различные AI-провайдеры, RAG-пайплайны, загрузчики документов и векторные базы данных под единым интерфейсом.

## Зачем Mythosia.AI

SDK разных AI-провайдеров сильно отличаются друг от друга, и при смене провайдера или комбинировании функций приходится переписывать значительную часть кода. Mythosia.AI сводит всё к единому интерфейсу `IAIService`, поэтому код приложения не зависит от конкретной модели или провайдера.

## Структура пакетов

Устанавливайте только то, что вам нужно:

| Этап | Пакет | Назначение |
|:----:|-------|------------|
| **1** | `Mythosia.AI` | Отправная точка — генерация текста, стриминг, вызов функций, структурированный вывод |
| **2** | `Mythosia.AI.Rag` | Если нужен RAG — разделители, эмбеддинги, гибридный поиск, переранжирование |
| **3** | `Mythosia.VectorDb.*` | Если нужно продуктовое хранилище — Postgres, Qdrant, Pinecone |

## Поддерживаемые провайдеры

Все провайдеры входят в основной пакет `Mythosia.AI` (кроме Alibaba):

| Провайдер | Модели |
|-----------|--------|
| **OpenAI** | GPT-6 Astra / Sol / Luna, GPT-5.1–5.6, GPT-4.1, GPT-4o |
| **Anthropic** | Claude Fable 5.1 / 5, Mythos 5.1 / 5 (limited), Opus / Sonnet 5 and 4.x, Haiku 4.5 |
| **Google** | Gemini 3.8 / 3.7 / 3.6 Flash, Gemini 3.5 / 3.1 / 3, Gemini 2.5 |
| **xAI** | Grok 4.7 / 4.6 / 4.5 / 4.3 / 4.20, Grok Build |
| **DeepSeek** | Flash (V4.1 Flash), V4 Pro |
| **Perplexity** | Пресеты Agent API и `perplexity/sonar` |
| **Alibaba / Qwen** | Qwen Max / Plus / Turbo / Qwen3 (`Mythosia.AI.Providers.Alibaba`) |

## Обзор архитектуры

```
Mythosia.AI                     ← Основные AI-сервисы (все провайдеры)
    └── Mythosia.AI.Abstractions   ← Интерфейс IAIService

Mythosia.AI.Rag                 ← RAG-пайплайн, оркестрация
    ├── Mythosia.AI.Abstractions
    ├── Mythosia.AI.Rag.Abstractions
    │   └── Mythosia.VectorDb.Abstractions
    ├── Mythosia.Documents.Office / Mythosia.Documents.Pdf
    │   └── Mythosia.Documents.Abstractions
    └── Mythosia.VectorDb.InMemory
        ├── Mythosia.VectorDb.Abstractions
        └── Mythosia.AI.Rag.Abstractions

Mythosia.VectorDb.*             ← Векторные хранилища (выберите одно или несколько)
    └── Mythosia.VectorDb.Abstractions

Mythosia.Documents.*            ← Загрузчики документов (Word, Excel, PDF, ...)
    └── Mythosia.Documents.Abstractions
```
