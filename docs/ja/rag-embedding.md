# 埋め込み（Embedding）

> 📍 **質問応答パイプライン:** [クエリ書き換え](rag-query-rewriting.md) → [フィルタリング](rag-filtering.md) → **`埋め込み（必要な場合）`** → [検索](rag-hybrid-search.md) → [再ランキング](rag-reranking.md) → [コンテキスト構築](rag-context-build.md)

質問の `Embedding` ステージは検索器に応じて実行され、キーワード検索では通知されません。独自検索器は `request.ProgressAsync` で実際の処理を通知できます。文書の埋め込みは変わりません。

## 埋め込みとは？

埋め込み（Embedding）は、テキストを**数値ベクター**（数値の配列）に変換するプロセスです。変換されたベクターは高次元の空間に配置され、**意味が似たテキスト同士は近い位置に集まります**。

地図上に都市を配置するイメージです。地理的に近い都市は地図上でも近くに表示されます。それと同じように、「サブスクリプションの解約方法は？」と「メンバーシップを終了したい」という文は、まったく違う単語を使っていても、意味が似ているため近いベクターを生成します。

RAGパイプラインでは、埋め込みは2つの場面で使われます：

1. **ドキュメントのインデックス作成時** — 各チャンクを埋め込みし、ベクターストアに保存
2. **クエリ時** — ユーザーの質問を埋め込みし、保存されたチャンクとの類似度を比較

このページでは、**クエリ時の埋め込み**（ステップ2）について詳しく説明します。

## 組み込みの埋め込みプロバイダー

文書の言語、運用環境、検索要件に合う埋め込みプロバイダーを選択してください。

### Perplexity

標準埋め込みは段落を独立して扱い、`IEmbeddingProvider` を実装するため既存のビルダーに接続できます。文脈埋め込みは隣接チャンクの順序と文書ごとのまとまりを維持します。無関係な文書を一つに平坦化しないよう、別の API を使います。

[Perplexity Agent API、検索と埋め込み](perplexity.md).

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

<a id="openai-dimensions"></a>

`text-embedding-ada-002`は**1536次元で固定**されています。プロバイダーは単一・バッチリクエストで、このモデルがサポートしない`dimensions`フィールドを省略します。別の次元数を設定すると、API呼び出し前に`ArgumentOutOfRangeException`が発生します。`text-embedding-3-small`と`text-embedding-3-large`では、引き続き設定した`dimensions`を送信します。

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

<a id="ollama-dimensions"></a>

文書と質問のベクトルは、同じモデルと次元数で作成する必要があります。`OllamaEmbeddingProvider` は設定した `dimensions` を `/api/embed` に送り、返された各ベクトルの長さを検証します。プロバイダーのデフォルトは引き続き `qwen3-embedding:4b` と **要求次元数 1024** で、モデル本来の出力は 2560 次元です。Ollama サーバーと選択したモデルが要求次元数をサポートする必要があります。未対応の要求や設定を無視した応答は失敗となり、`Dimensions` の自動変更やローカルでのベクトルの切り詰めは行いません。

モデルや次元数を変更した場合は、質問と同じ設定で文書を再埋め込みし、ベクトルストアの次元数も合わせてください。既存のベクトルは自動変換されません。

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

`EmbeddingBatchSize` は正の値である必要があります。パイプラインは文書のインデックス呼び出し開始時、埋め込みや保存レコードの置換前に値を検証し、その呼び出し用に固定します。空バッチの反復や、応答待ち中の設定変更によるチャンクの飛ばしを防ぐためです。以降の呼び出しには変更後の設定を使用できます。

<a id="embedding-validation"></a>

## ベクトルとチャンクの対応を守る

HTTP 応答が成功でも、ベクトルの欠落や順序の誤りがあると、別のチャンクの意味がテキストに対応してしまいます。カスタム `IEmbeddingProvider` は入力順に、入力ごとに null でない `float[]` を一つ返す必要があります。`Dimensions` は正で、各ベクトルの長さはその値と等しく、全要素が有限値である必要があります（`NaN`、無限大は不可）。

文書のインデックス作成では、パイプラインは次元、応答数、ベクトルの不備を保存や `onDocumentEmbedded` の前に `InvalidOperationException` で拒否します。次のバッチを要求する前に各ベクトルをコピーするため、後続バッチでバッファーを再利用しても前のチャンクは変化しません。呼び出し側が読み取る間は返却データを安定させてください。検証やコピー中の同時変更はサポートしません。検証失敗時にはその文書の既存レコードを保持します。

`OpenAIEmbeddingProvider` は全応答項目に有効で一意の `index` を要求し、入力順に並べ直します。`VllmEmbeddingProvider` もインデックスがあれば同じ規則を使いますが、互換性のため全項目で `index` を省略した応答も応答順で受け入れます。一部だけの省略、重複、範囲外のインデックスは拒否します。カスタムプロバイダーやインデックスなし応答の順序はプロバイダーの責任であり、形式の検証だけではベクトルの意味までは確認できません。

<a id="query-embedding-validation"></a>

## 検索前に質問ベクトルを保護する

進捗通知や検索の待機中に、プロバイダーのバッファー再利用で質問ベクトルが変わってはいけません。`IRetrievalStrategy` アダプターを含む標準の密ベクトル検索では、正の `Dimensions`、その長さと一致する非 null ベクトル、有限値を要求します。不正な結果は検索前に `InvalidOperationException` で拒否します。正常なベクトルは返却直後、後続の進捗通知や検索前にコピーします。読み取り中のデータはプロバイダーが安定させる必要があり、カスタム `IRagRetriever` は独自の質問準備・検証を担当します。

`OllamaEmbeddingProvider` の単一・バッチ直接呼び出しも、応答構造、正確なベクトル数、次元、有限値を検証します。不正な JSON やベクトルは不完全な結果ではなく `InvalidOperationException` を発生させます。渡した `HttpClient` の所有権は呼び出し側に残り、個々の HTTP 要求・応答の破棄でクライアントは破棄されません。

## ベクター次元数

`Dimensions`プロパティは各埋め込みベクターのサイズを制御します。重要な理由：

- **ベクターストアと一致させる必要があります** — 埋め込みが1536次元なら、ベクターストアのカラムも1536にする必要があります
- **次元数が多い = より詳細** — ただしストレージが増え、検索も遅くなります
- **次元数が少ない = 高速** — ただし微妙な意味の違いを見逃す可能性があります

一般的な次元数：

| プロバイダー | モデル | デフォルト次元数 |
| --- | --- | --- |
| OpenAI | text-embedding-3-small | 1536 |
| OpenAI | text-embedding-ada-002 | 1536 |
| Perplexity | pplx-embed-v1-0.6b | 1024 |
| Perplexity | pplx-embed-v1-4b | 2560 |
| OpenAI | text-embedding-3-large | 3072 |
| Ollama | qwen3-embedding:4b | 要求 1024（本来の出力: 2560） |
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
