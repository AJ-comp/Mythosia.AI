# 概要

> Grok 4.7: Mythosia.AI 8.1.0 / Abstractions 4.1.0 が必要です。 [モデル選択・推論・処理速度](providers.md#grok-47)

> GPT-6 Sol/Luna: Mythosia.AI 8.1.0 / Abstractions 4.1.0 が必要です。 [モデルの選択と必要バージョン](providers.md#gpt-6-sol-luna)

Mythosia.AIは、複数のAIプロバイダー、RAGパイプライン、ドキュメントローダー、ベクターデータベースを単一のインターフェースで統合したモジュール式.NET AIライブラリです。

## Mythosia.AIを使う理由

ほとんどのAIプロバイダーSDKはそれぞれ異なるAPIを提供しているため、プロバイダーの切り替えや機能の組み合わせが困難です。Mythosia.AIはそれらを一つの`IAIService`インターフェースに統合しているため、どのモデルやプロバイダーを使ってもアプリケーションコードはそのままです。

## パッケージ構造

必要なパッケージだけインストールすれば始められます:

| ステップ | パッケージ | 用途 |
|:------:|---------|------|
| **1** | `Mythosia.AI` | 開始点 — テキスト生成、ストリーミング、関数呼び出し、構造化出力 |
| **2** | `Mythosia.AI.Rag` | RAGが必要な場合 — スプリッター、埋め込み、ハイブリッド検索、再ランキング |
| **3** | `Mythosia.VectorDb.*` | 本番ベクターストアが必要な場合 — Postgres、Qdrant、Pinecone |

## 対応プロバイダー

すべてのプロバイダーはコア`Mythosia.AI`パッケージに含まれます（Alibabaを除く）:

| プロバイダー | モデル |
|------------|--------|
| **OpenAI** | GPT-6 Astra / Sol / Luna, GPT-5.1–5.6, GPT-4.1, GPT-4o |
| **Anthropic** | Claude Fable 5.1 / 5, Mythos 5.1 / 5 (limited), Opus / Sonnet 5 and 4.x, Haiku 4.5 |
| **Google** | Gemini 3.8 / 3.7 / 3.6 Flash, Gemini 3.5 / 3.1 / 3, Gemini 2.5 |
| **xAI** | Grok 4.7 / 4.6 / 4.5 / 4.3 / 4.20, Grok Build |
| **DeepSeek** | Flash (V4.1 Flash), V4 Pro |
| **Perplexity** | Agent API プリセットと `perplexity/sonar` |
| **Alibaba / Qwen** | Qwen Max / Plus / Turbo / Qwen3 (`Mythosia.AI.Providers.Alibaba`) |

## アーキテクチャ概要

```
Mythosia.AI                     ← コアAIサービス（全プロバイダー）
    └── Mythosia.AI.Abstractions   ← IAIServiceインターフェース

Mythosia.AI.Rag                 ← RAGパイプライン、オーケストレーション
    ├── Mythosia.AI.Abstractions
    ├── Mythosia.AI.Rag.Abstractions
    │   └── Mythosia.VectorDb.Abstractions
    ├── Mythosia.Documents.Office / Mythosia.Documents.Pdf
    │   └── Mythosia.Documents.Abstractions
    └── Mythosia.VectorDb.InMemory
        ├── Mythosia.VectorDb.Abstractions
        └── Mythosia.AI.Rag.Abstractions

Mythosia.VectorDb.*             ← ベクターストア（1つ以上選択）
    └── Mythosia.VectorDb.Abstractions

Mythosia.Documents.*            ← ドキュメントローダー（Word、Excel、PDF、...）
    └── Mythosia.Documents.Abstractions
```
