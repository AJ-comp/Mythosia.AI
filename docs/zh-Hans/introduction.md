# 简介

> Grok 4.7: 需要 Mythosia.AI 8.1.0 / Abstractions 4.1.0。 [模型选择、推理与处理速度](providers.md#grok-47)

> GPT-6 Sol/Luna: 需要 Mythosia.AI 8.1.0 / Abstractions 4.1.0。 [模型选择与版本要求](providers.md#gpt-6-sol-luna)

Mythosia.AI 是一个模块化的 .NET AI 库，提供统一接口来对接多个 AI 提供商、RAG 管道、文档加载器以及向量数据库。

## 为什么选择 Mythosia.AI？

大多数 AI 提供商的 SDK 各自暴露不同的 API，导致切换提供商或组合多种功能十分困难。Mythosia.AI 将它们统一封装在 `IAIService` 接口之后，无论底层使用哪个模型或提供商，应用代码都无需改动。

## 包结构

按需安装即可：

| 步骤 | 包名 | 用途 |
|:----:|------|------|
| **1** | `Mythosia.AI` | 从这里开始 — 文本生成、流式输出、函数调用、结构化输出 |
| **2** | `Mythosia.AI.Rag` | 需要 RAG 时安装 — 分割器、嵌入、混合检索、重排序 |
| **3** | `Mythosia.VectorDb.*` | 需要生产级向量存储时安装 — Postgres、Qdrant 或 Pinecone |

## 支持的提供商

除阿里巴巴外，所有提供商均包含在核心包 `Mythosia.AI` 中：

| 提供商 | 模型 |
|--------|------|
| **OpenAI** | GPT-6 Astra / Sol / Luna, GPT-5.1–5.6, GPT-4.1, GPT-4o |
| **Anthropic** | Claude Fable 5.1 / 5, Mythos 5.1 / 5 (limited), Opus / Sonnet 5 and 4.x, Haiku 4.5 |
| **Google** | Gemini 3.8 / 3.7 / 3.6 Flash, Gemini 3.5 / 3.1 / 3, Gemini 2.5 |
| **xAI** | Grok 4.7 / 4.6 / 4.5 / 4.3 / 4.20, Grok Build |
| **DeepSeek** | Flash (V4.1 Flash), V4 Pro |
| **Perplexity** | Agent API 预设与 `perplexity/sonar` |
| **阿里巴巴 / 通义千问** | Qwen Max / Plus / Turbo / Qwen3（`Mythosia.AI.Providers.Alibaba`） |

## 架构概览

```
Mythosia.AI                     ← 核心 AI 服务（全部提供商）
    └── Mythosia.AI.Abstractions   ← IAIService 接口

Mythosia.AI.Rag                 ← RAG 管道与编排
    ├── Mythosia.AI.Abstractions
    ├── Mythosia.AI.Rag.Abstractions
    │   └── Mythosia.VectorDb.Abstractions
    ├── Mythosia.Documents.Office / Mythosia.Documents.Pdf
    │   └── Mythosia.Documents.Abstractions
    └── Mythosia.VectorDb.InMemory
        ├── Mythosia.VectorDb.Abstractions
        └── Mythosia.AI.Rag.Abstractions

Mythosia.VectorDb.*             ← 向量存储（按需选择）
    └── Mythosia.VectorDb.Abstractions

Mythosia.Documents.*            ← 文档加载器（Word、Excel、PDF 等）
    └── Mythosia.Documents.Abstractions
```
