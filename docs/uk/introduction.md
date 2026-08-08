# Вступ

Mythosia.AI — модульна .NET-бібліотека, що об'єднує різних AI-провайдерів, RAG-пайплайни, завантажувачі документів і векторні бази даних під єдиним інтерфейсом.

## Навіщо Mythosia.AI

SDK різних AI-провайдерів суттєво відрізняються, і при зміні провайдера чи комбінуванні функцій доводиться суттєво переписувати код. Mythosia.AI зводить усе до єдиного інтерфейсу `IAIService`, тому код застосунку не залежить від конкретної моделі чи провайдера.

## Структура пакетів

Встановлюйте лише те, що потрібно:

| Крок | Пакет | Призначення |
|:----:|-------|-------------|
| **1** | `Mythosia.AI` | Відправна точка — генерація тексту, стримінг, виклик функцій, структурований вивід |
| **2** | `Mythosia.AI.Rag` | Якщо потрібен RAG — розділювачі, ембедінги, гібридний пошук, переранжування |
| **3** | `Mythosia.VectorDb.*` | Якщо потрібне продуктове сховище — Postgres, Qdrant, Pinecone |

## Підтримувані провайдери

Усі провайдери входять до основного пакета `Mythosia.AI` (окрім Alibaba):

| Провайдер | Моделі |
|-----------|--------|
| **OpenAI** | GPT-5.x, GPT-4.1, GPT-4o, серія o3 |
| **Anthropic** | Claude Fable 5, Mythos 5 (limited), Opus / Sonnet 5 and 4.x, Haiku 4.5 |
| **Google** | Gemini 2.5 / 3 серії |
| **xAI** | Grok 4 серії, Grok Build |
| **DeepSeek** | Chat, Reasoner |
| **Perplexity** | Sonar, Sonar Pro, Sonar Reasoning Pro |
| **Alibaba / Qwen** | Qwen Max / Plus / Turbo / Qwen3 (`Mythosia.AI.Providers.Alibaba`) |

## Огляд архітектури

```
Mythosia.AI                     ← Основні AI-сервіси (усі провайдери)
    └── Mythosia.AI.Abstractions   ← Інтерфейс IAIService

Mythosia.AI.Rag                 ← RAG-пайплайн, оркестрація
    ├── Mythosia.AI.Abstractions
    ├── Mythosia.AI.Rag.Abstractions
    │   └── Mythosia.VectorDb.Abstractions
    ├── Mythosia.Documents.Office / Mythosia.Documents.Pdf
    │   └── Mythosia.Documents.Abstractions
    └── Mythosia.VectorDb.InMemory
        ├── Mythosia.VectorDb.Abstractions
        └── Mythosia.AI.Rag.Abstractions

Mythosia.VectorDb.*             ← Векторні сховища (оберіть одне або кілька)
    └── Mythosia.VectorDb.Abstractions

Mythosia.Documents.*            ← Завантажувачі документів (Word, Excel, PDF, ...)
    └── Mythosia.Documents.Abstractions
```
