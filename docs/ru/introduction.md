# Введение

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
| **OpenAI** | GPT-5.x, GPT-4.1, GPT-4o, серия o3 |
| **Anthropic** | Claude Opus / Sonnet / Haiku 4.x |
| **Google** | Gemini 2.5 / 3 серии |
| **xAI** | Grok 4 серии, Grok Build, Grok 3 Mini |
| **DeepSeek** | Chat, Reasoner |
| **Perplexity** | Sonar, Sonar Pro, Sonar Reasoning Pro |
| **Alibaba / Qwen** | Qwen Max / Plus / Turbo / Qwen3 (`Mythosia.AI.Providers.Alibaba`) |

## Обзор архитектуры

```
Mythosia.AI.Rag                 ← RAG-пайплайн, оркестрация
    └── Mythosia.AI             ← Основные AI-сервисы (все провайдеры)
        └── Mythosia.AI.Abstractions   ← Интерфейс IAIService

Mythosia.VectorDb.*             ← Векторные хранилища (выберите одно или несколько)
    └── Mythosia.VectorDb.Abstractions

Mythosia.Documents.*            ← Загрузчики документов (Word, Excel, PDF, ...)
    └── Mythosia.Documents.Abstractions
```
