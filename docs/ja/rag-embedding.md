# 埋め込み（Embedding）

> 📍 **質問応答パイプライン:** [クエリ書き換え](rag-query-rewriting.md) → [フィルタリング](rag-filtering.md) → **`埋め込み（必要な場合）`** → [検索](rag-hybrid-search.md) → [再ランキング](rag-reranking.md) → [コンテキスト構築](rag-context-build.md)

質問の `Embedding` ステージは検索器に応じて実行され、キーワード検索では通知されません。独自検索器は `request.ProgressAsync` で実際の処理を通知できます。

<a id="retrieval-aware-embeddings"></a>

## 文書の文脈と検索クエリの役割を保つ

チャンクの理解には隣接する段落が必要な場合があり、検索クエリと索引文書では役割が異なります。RAG 8.2.0 は TXT・Markdown・PDF から抽出したテキストに Voyage の文脈埋め込みと Gemini Embedding 2 を提供します。

```csharp
using Mythosia.AI.Rag;

var store = await RagStore.BuildAsync(rag => rag
    .UseVoyageEmbedding(voyageApiKey, httpClient)
    .AddDocument("policy.pdf"));
var result = await store.QueryAsync("What is the refund period?");
```

`IRetrievalEmbeddingProvider : IEmbeddingProvider` は任意の機能で、既存のプロバイダーはそのまま動作します。索引作成では `EmbeddingBatchSize` に関係なく、すべてのチャンクを順番に含む不変の `EmbeddingDocument(documentId, chunks, title)` を渡します。タイトルは `RagDocument.Metadata["title"]` から取得します。ベクトル検索と診断は `GetQueryEmbeddingAsync` を呼び、既存のプロバイダーは `GetEmbeddingsAsync` のバッチと `GetEmbeddingAsync` のクエリを維持します。キーワード専用検索はクエリを埋め込みません。

ストアに使う埋め込み設定を一つ選択します：

```csharp
rag.UseVoyageEmbedding(voyageApiKey, httpClient,
    model: "voyage-context-4", dimensions: 1024,
    timeout: TimeSpan.FromSeconds(60));

rag.UseGeminiEmbedding(geminiApiKey, httpClient,
    model: "gemini-embedding-2", dimensions: 1536,
    timeout: TimeSpan.FromSeconds(60), maxConcurrency: 4);
```

### Voyage

`VoyageContextualizedEmbeddingProvider` の既定値は `voyage-context-4`、1024 次元で、256・512・1024・2048 次元を選べます。文書全体を順序付きの一つのグループとして `input_type=document` で送信し、クエリは単独グループを `input_type=query` で送ります。自動チャンク化は無効です。文書は最大 16,000 チャンクで、トークン制限はサーバーが検査します。汎用メソッドは `input_type` を省略し、最大 1,000 テキストを独立した単一チャンクのグループとして扱います。文書 ID とタイトルは送信しません。 [Voyage API](https://docs.voyageai.com/docs/contextualized-chunk-embeddings).

汎用バッチは入力を読み取る間もキャンセルを確認し、テキストが 1,000 件を超えると直ちに読み取りを止め、HTTP リクエストを送らずに拒否します。文書全体のグループは維持します。

### Gemini

`GeminiEmbeddingProvider` の既定値は `gemini-embedding-2`、1536 次元（128–3072）、`maxConcurrency=4` です。各チャンクは個別の HTTP リクエストから一つのベクトルを取得します。検索入力は `title: {title} | text: {text}`（タイトルなしは `none`）または `task: search result | query: {query}` です。接頭辞は HTTP 入力にだけ適用し、汎用メソッドは原文を送ります。`embedContentConfig.autoTruncate=false` により長すぎる入力は切り捨てず失敗します。 [Gemini API](https://ai.google.dev/gemini-api/docs/embeddings).

両プロバイダーは保存する原文を保ち、ベクトル数・次元・有限値を検証します。`HttpClient` の所有権と設定は呼び出し側に残ります。Voyage は検証済みの応答インデックスから文書とチャンクの順序を復元します。エラーにはキーや応答本文を含めず、キャンセルを伝播し、タイムアウトは `TimeoutException` になります。Voyage の `timeout` はリクエストごと、Gemini は並列実行待ちを含む操作全体に適用され、クライアントの制限も有効です。入力を黙って再分割・切り捨てません。保存前の失敗では既存文書を保ち、保存開始後の原子性はストアやコールバック次第です。モデル・次元・検索形式を変更したら文書を再索引し、ストアも同じベクトル空間に合わせてください。

### 実際のサービスを検証する

ライブテストは合成 TXT・Markdown・PDF テキストを送信し、API 料金が発生します。`MYTHOSIA_RETRIEVAL_EMBEDDING_LIVE=1` と認証情報を設定し、`All`、`Voyage`、`Gemini` を選びます。スキップや判定不能のケースは失敗扱いです。オフラインテストだけではサービスの利用可否は確認できません。

```powershell
$env:MYTHOSIA_RETRIEVAL_EMBEDDING_LIVE = "1"
pwsh -NoProfile -File build/test-retrieval-embedding-live.ps1 -Provider All
```

[実際のサービスを検証する](https://github.com/AJ-comp/Mythosia.AI/blob/main/build/RELEASE.md#retrieval-embedding-live-validation).

## 埋め込みとは？

埋め込み（Embedding）は、テキストを**数値ベクター**（数値の配列）に変換するプロセスです。変換されたベクターは高次元の空間に配置され、**意味が似たテキスト同士は近い位置に集まります**。

地図上に都市を配置するイメージです。地理的に近い都市は地図上でも近くに表示されます。それと同じように、「サブスクリプションの解約方法は？」と「メンバーシップを終了したい」という文は、まったく違う単語を使っていても、意味が似ているため近いベクターを生成します。

RAGパイプラインでは、埋め込みは2つの場面で使われます：

1. **ドキュメントのインデックス作成時** — 各チャンクを埋め込みし、ベクターストアに保存
2. **クエリ時** — ユーザーの質問を埋め込みし、保存されたチャンクとの類似度を比較

## 組み込みの埋め込みプロバイダー

文書の言語、運用環境、検索要件に合う埋め込みプロバイダーを選択してください。

### Perplexity

`PerplexityContextualizedEmbeddingProvider` は `IRetrievalEmbeddingProvider` を実装し、`.UseEmbedding(contextual)` で登録できます。既存の公開グループ版 `GetDocumentEmbeddingsAsync` とバイナリメソッドは維持されます。新しい単一文書メソッドは明示的インターフェイス実装なので既存の呼び出しを保ちます。RAG は文書境界を維持し、クエリにも同じ文脈モデルと次元を使います。

Perplexity の浮動小数点・バイナリバッチは、独立したテキストを最大 512 件、または文脈文書を最大 512 件・合計 16,000 チャンクまで受け付けます。入力を読み取る間にキャンセルを確認し、上限を超えた時点で読み取りを停止して HTTP リクエスト前に拒否します。文書のグループと順序は維持されます。詳細は [Perplexity ガイド](perplexity.md)を参照してください。

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

`EmbeddingBatchSize` は従来の `IEmbeddingProvider` のフラットなバッチを制御します。`IRetrievalEmbeddingProvider` は文書全体を受け取り HTTP バッチを管理するため、この値を下げても Voyage の文書を複数の文脈グループに分割しません。

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
| Voyage | voyage-context-4 | 1024 |
| Gemini | gemini-embedding-2 | 1536 |
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
ユーザーの質問（文字列） → GetQueryEmbeddingAsync() / GetEmbeddingAsync() → クエリベクター（float[]）
```

このクエリベクターは次のステージ（[フィルタリング](rag-filtering.md)）に渡され、メタデータフィルターと組み合わせた後、[検索](rag-hybrid-search.md)で類似度検索が実行されます。

## 次のステップ

- [フィルタリング](rag-filtering.md) — 検索対象のチャンクを絞り込む
- [検索（ハイブリッド検索）](rag-hybrid-search.md) — ベクター検索とキーワード検索を組み合わせる
- [パイプラインカスタマイズ](rag-pipeline.md) — 埋め込みプロバイダーを複数のサービスで共有する
