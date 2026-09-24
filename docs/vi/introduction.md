# Giới thiệu

> Grok 4.7: Cần Mythosia.AI 8.1.0 / Abstractions 4.1.0. [chọn mô hình, suy luận và tốc độ xử lý](providers.md#grok-47)

> GPT-6 Sol/Luna: Cần Mythosia.AI 8.1.0 / Abstractions 4.1.0. [chọn mô hình và yêu cầu phiên bản](providers.md#gpt-6-sol-luna)

Mythosia.AI là thư viện .NET AI dạng module, cung cấp giao diện thống nhất để làm việc với nhiều AI provider, RAG pipeline, document loader và vector database.

## Tại sao chọn Mythosia.AI?

Hầu hết các SDK của từng AI provider đều có API riêng biệt, khiến việc chuyển đổi provider hoặc kết hợp tính năng trở nên phức tạp. Mythosia.AI gói tất cả lại sau một interface `IAIService` duy nhất — code ứng dụng của bạn không thay đổi dù dùng model hay provider nào.

## Cấu trúc package

Chỉ cài những gì bạn thực sự cần:

| Bước | Package | Mục đích |
|:----:|---------|---------|
| **1** | `Mythosia.AI` | Bắt đầu từ đây — completions, streaming, function calling, structured output |
| **2** | `Mythosia.AI.Rag` | Thêm khi cần RAG — splitter, embedding, hybrid search, reranking |
| **3** | `Mythosia.VectorDb.*` | Thêm khi cần vector store cho production — Postgres, Qdrant, hoặc Pinecone |

## Provider được hỗ trợ

Tất cả provider đều có trong package `Mythosia.AI` (trừ Alibaba):

| Provider | Models |
|----------|--------|
| **OpenAI** | GPT-6 Astra / Sol / Luna, GPT-5.1–5.6, GPT-4.1, GPT-4o |
| **Anthropic** | Claude Fable 5.1 / 5, Mythos 5.1 / 5 (limited), Opus / Sonnet 5 and 4.x, Haiku 4.5 |
| **Google** | Gemini 3.8 / 3.7 / 3.6 Flash, Gemini 3.5 / 3.1 / 3, Gemini 2.5 |
| **xAI** | Grok 4.7 / 4.6 / 4.5 / 4.3 / 4.20, Grok Build |
| **DeepSeek** | Flash (V4.1 Flash), V4 Pro |
| **Perplexity** | Preset Agent API và `perplexity/sonar` |
| **Alibaba / Qwen** | Qwen Max / Plus / Turbo / Qwen3 (`Mythosia.AI.Providers.Alibaba`) |

## Kiến trúc tổng quan

```
Mythosia.AI                     ← Core AI services (tất cả provider)
    └── Mythosia.AI.Abstractions   ← Interface IAIService

Mythosia.AI.Rag                 ← RAG pipeline, orchestration
    ├── Mythosia.AI.Abstractions
    ├── Mythosia.AI.Rag.Abstractions
    │   └── Mythosia.VectorDb.Abstractions
    ├── Mythosia.Documents.Office / Mythosia.Documents.Pdf
    │   └── Mythosia.Documents.Abstractions
    └── Mythosia.VectorDb.InMemory
        ├── Mythosia.VectorDb.Abstractions
        └── Mythosia.AI.Rag.Abstractions

Mythosia.VectorDb.*             ← Vector store (chọn một hoặc nhiều)
    └── Mythosia.VectorDb.Abstractions

Mythosia.Documents.*            ← Document loader (Word, Excel, PDF, ...)
    └── Mythosia.Documents.Abstractions
```
