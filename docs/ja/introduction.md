# 概要

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
| **OpenAI** | GPT-5.x、GPT-4.1、GPT-4o、o3シリーズ |
| **Anthropic** | Claude Opus / Sonnet / Haiku 4.x |
| **Google** | Gemini 2.5 / 3シリーズ |
| **xAI** | Grok 3、Grok 4シリーズ |
| **DeepSeek** | Chat、Reasoner |
| **Perplexity** | Sonar、Sonar Pro、Sonar Reasoning |
| **Alibaba / Qwen** | Qwen Max / Plus / Turbo / Qwen3 (`Mythosia.AI.Providers.Alibaba`) |

## アーキテクチャ概要

```
Mythosia.AI.Rag                 ← RAGパイプライン、オーケストレーション
    └── Mythosia.AI             ← コアAIサービス（全プロバイダー）
        └── Mythosia.AI.Abstractions   ← IAIServiceインターフェース

Mythosia.VectorDb.*             ← ベクターストア（1つ以上選択）
    └── Mythosia.VectorDb.Abstractions

Mythosia.Documents.*            ← ドキュメントローダー（Word、Excel、PDF、...）
    └── Mythosia.Documents.Abstractions
```
