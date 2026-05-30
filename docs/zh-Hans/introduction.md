# 简介

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
| **OpenAI** | GPT-5.x、GPT-4.1、GPT-4o、o3 系列 |
| **Anthropic** | Claude Opus / Sonnet / Haiku 4.x |
| **Google** | Gemini 2.5 / 3 系列 |
| **xAI** | Grok 4 系列、Grok Build、Grok 3 Mini |
| **DeepSeek** | Chat、Reasoner |
| **Perplexity** | Sonar、Sonar Pro、Sonar Reasoning Pro |
| **阿里巴巴 / 通义千问** | Qwen Max / Plus / Turbo / Qwen3（`Mythosia.AI.Providers.Alibaba`） |

## 架构概览

```
Mythosia.AI.Rag                 ← RAG 管道与编排
    └── Mythosia.AI             ← 核心 AI 服务（全部提供商）
        └── Mythosia.AI.Abstractions   ← IAIService 接口

Mythosia.VectorDb.*             ← 向量存储（按需选择）
    └── Mythosia.VectorDb.Abstractions

Mythosia.Documents.*            ← 文档加载器（Word、Excel、PDF 等）
    └── Mythosia.Documents.Abstractions
```
