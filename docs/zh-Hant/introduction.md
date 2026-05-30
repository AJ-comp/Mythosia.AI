# 簡介

Mythosia.AI 是一個模組化的 .NET AI 函式庫，提供統一介面來對接多個 AI 供應商、RAG 管線、文件載入器以及向量資料庫。

## 為什麼選擇 Mythosia.AI？

大多數 AI 供應商的 SDK 各自暴露不同的 API，導致切換供應商或組合多種功能十分困難。Mythosia.AI 將它們統一封裝在 `IAIService` 介面之後，無論底層使用哪個模型或供應商，應用程式碼都無需改動。

## 套件結構

按需安裝即可：

| 步驟 | 套件名稱 | 用途 |
|:----:|----------|------|
| **1** | `Mythosia.AI` | 從這裡開始 — 文字生成、串流輸出、函式呼叫、結構化輸出 |
| **2** | `Mythosia.AI.Rag` | 需要 RAG 時安裝 — 分割器、嵌入、混合檢索、重排序 |
| **3** | `Mythosia.VectorDb.*` | 需要正式環境向量儲存時安裝 — Postgres、Qdrant 或 Pinecone |

## 支援的供應商

除阿里巴巴外，所有供應商均包含在核心套件 `Mythosia.AI` 中：

| 供應商 | 模型 |
|--------|------|
| **OpenAI** | GPT-5.x、GPT-4.1、GPT-4o、o3 系列 |
| **Anthropic** | Claude Opus / Sonnet / Haiku 4.x |
| **Google** | Gemini 2.5 / 3 系列 |
| **xAI** | Grok 4 系列、Grok Build、Grok 3 Mini |
| **DeepSeek** | Chat、Reasoner |
| **Perplexity** | Sonar、Sonar Pro、Sonar Reasoning Pro |
| **阿里巴巴 / 通義千問** | Qwen Max / Plus / Turbo / Qwen3（`Mythosia.AI.Providers.Alibaba`） |

## 架構概覽

```
Mythosia.AI.Rag                 ← RAG 管線與編排
    └── Mythosia.AI             ← 核心 AI 服務（全部供應商）
        └── Mythosia.AI.Abstractions   ← IAIService 介面

Mythosia.VectorDb.*             ← 向量儲存（按需選擇）
    └── Mythosia.VectorDb.Abstractions

Mythosia.Documents.*            ← 文件載入器（Word、Excel、PDF 等）
    └── Mythosia.Documents.Abstractions
```
