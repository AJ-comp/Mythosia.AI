# 埋め込み（Embedding）

> 📍 **質問応答パイプライン:** [クエリ書き換え](rag-query-rewriting.md) → **`埋め込み`** → [フィルタリング](rag-filtering.md) → [検索](rag-hybrid-search.md) → [再ランキング](rag-reranking.md) → [コンテキスト構築](rag-context-build.md)

## 埋め込みとは？

埋め込み（Embedding）は、テキストを**数値ベクター**（数値の配列）に変換するプロセスです。変換されたベクターは高次元の空間に配置され、**意味が似たテキスト同士は近い位置に集まります**。

地図上に都市を配置するイメージです。地理的に近い都市は地図上でも近くに表示されます。それと同じように、「サブスクリプションの解約方法は？」と「メンバーシップを終了したい」という文は、まったく違う単語を使っていても、意味が似ているため近いベクターを生成します。

RAGパイプラインでは、埋め込みは2つの場面で使われます：

1. **ドキュメントのインデックス作成時** — 各チャンクを埋め込みし、ベクターストアに保存
2. **クエリ時** — ユーザーの質問を埋め込みし、保存されたチャンクとの類似度を比較

このページでは、**クエリ時の埋め込み**（ステップ2）について詳しく説明します。

## 組み込みの埋め込みプロバイダー

Mythosia.AI.Ragには4種類のプロバイダーが用意されています。用途に応じて選択してください。

### OpenAI Embedding

最も一般的なクラウドベースのオプションです。高品質ですがAPIキーが必要です：

```csharp
using Mythosia.AI.Rag.Embeddings;

var embedder = new OpenAIEmbeddingProvider(
    apiKey: "sk-...",
    httpClient: new HttpClient(),
    model: "text-embedding-3-small",   // デフォルト
    dimensions: 1536                    // デフォルト
);
```

ビルダーのショートハンドも使えます：

```csharp
.WithRag(rag => rag
    .UseOpenAIEmbedding(apiKey, model: "text-embedding-3-small", dimensions: 1536)
    .AddDocument("docs.txt")
)
```

### Ollama（ローカル実行）

データをクラウドに送らず、ローカルで埋め込みを実行します。マシン上で[Ollama](https://ollama.com/)が動作している必要があります：

```csharp
var embedder = new OllamaEmbeddingProvider(
    httpClient: new HttpClient(),
    model: "qwen3-embedding:4b",       // デフォルト
    dimensions: 1024,                    // デフォルト
    baseUrl: "http://localhost:11434"    // デフォルト
);
```

### vLLM（セルフホスト）

[vLLM](https://docs.vllm.ai/)で独自の埋め込みサーバーを運用するチーム向けです：

```csharp
var embedder = new VllmEmbeddingProvider(
    httpClient: new HttpClient(),
    model: "Qwen/Qwen3-Embedding-0.6B", // デフォルト
    dimensions: 1024,                     // デフォルト
    baseUrl: "http://localhost:8002"      // デフォルト
);
```

### Local（API不要）

特徴ハッシュベースの軽量プロバイダーで、APIキーも外部サービスも不要です。ただし、ニューラルモデルと比べて埋め込み品質が大幅に劣るため、**実用には推奨しません**。

```csharp
.WithRag(rag => rag
    .UseLocalEmbedding(dimensions: 1024)
    .AddDocument("docs.txt")
)
```

> **ヒント：** 代わりに`OpenAIEmbeddingProvider`の`text-embedding-3-small`モデルをお使いください。ほぼ無料に近い価格で、はるかに優れた結果が得られます。

## バッチ処理

ドキュメントのインデックス作成時、パイプラインはチャンクをバッチ単位で埋め込みます。何千ものテキストを一度のAPI呼び出しで送るのを避けるためです。バッチサイズは設定可能です：

```csharp
var options = pipeline.Options.Clone();
options.EmbeddingBatchSize = 100; // デフォルト：1回のAPI呼び出しあたり100チャンク
pipeline.Options = options;
```

バッチサイズが大きいほどAPI呼び出し回数は減りますが、1回あたりのメモリ使用量が増えます。APIのレート制限やメモリの問題が発生する場合は、この値を小さくしてみてください。

## ベクター次元数

`Dimensions`プロパティは各埋め込みベクターのサイズを制御します。重要な理由：

- **ベクターストアと一致させる必要があります** — 埋め込みが1536次元なら、ベクターストアのカラムも1536にする必要があります
- **次元数が多い = より詳細** — ただしストレージが増え、検索も遅くなります
- **次元数が少ない = 高速** — ただし微妙な意味の違いを見逃す可能性があります

一般的な次元数：

| プロバイダー | モデル | デフォルト次元数 |
| --- | --- | --- |
| OpenAI | text-embedding-3-small | 1536 |
| OpenAI | text-embedding-3-large | 3072 |
| Ollama | qwen3-embedding:4b | 1024 (32–2560) |
| vLLM | Qwen/Qwen3-Embedding-0.6B | 1024 (32–1024) |
| vLLM | Qwen/Qwen3-Embedding-4B | 2560 (32–2560) |
| Local | （特徴ハッシュ） | 1024 |

## カスタム埋め込みプロバイダー

別の埋め込みサービスを使う場合は、`IEmbeddingProvider`を実装します：

```csharp
public class MyEmbeddingProvider : IEmbeddingProvider
{
    public int Dimensions => 768;

    public async Task<float[]> GetEmbeddingAsync(
        string text, CancellationToken cancellationToken = default)
    {
        // ここで埋め込みAPIを呼び出す
    }

    public async Task<IReadOnlyList<float[]>> GetEmbeddingsAsync(
        IEnumerable<string> texts, CancellationToken cancellationToken = default)
    {
        // バッチ埋め込み呼び出し
    }
}
```

ビルダーで登録します：

```csharp
.WithRag(rag => rag
    .UseEmbedding(new MyEmbeddingProvider())
    .AddDocument("docs.txt")
)
```

## 内部の動作

`QueryAsync`が実行されると、埋め込みステージは以下の1つの処理だけを行います：

```
ユーザーの質問（文字列） → EmbeddingProvider.GetEmbeddingAsync() → クエリベクター（float[]）
```

このクエリベクターは次のステージ（[フィルタリング](rag-filtering.md)）に渡され、メタデータフィルターと組み合わせた後、[検索](rag-hybrid-search.md)で類似度検索が実行されます。

## 次のステップ

- [フィルタリング](rag-filtering.md) — 検索対象のチャンクを絞り込む
- [検索（ハイブリッド検索）](rag-hybrid-search.md) — ベクター検索とキーワード検索を組み合わせる
- [パイプラインカスタマイズ](rag-pipeline.md) — 埋め込みプロバイダーを複数のサービスで共有する
