# ハイブリッド検索

商品コードにはキーワード検索、文書と異なる表現の質問には意味検索が適しています。選んだ検索器が必要な処理だけを行い、キーワード検索の前に質問を埋め込む必要がなくなります。

## 組み込みの検索方法

```csharp
// 意味検索（既定）
.UseVectorSearch()

// 質問を埋め込まないキーワード検索
.UseKeywordSearch()

// 重み付きハイブリッド検索
.UseHybridSearch(new HybridSearchOptions
{
    VectorWeight = 0.7f,
    CandidateMultiplier = 4,
    RrfK = 60
})
```

`HybridSearchOptions`: `using Mythosia.VectorDb;`

`UseKeywordSearch()` は質問の埋め込みを省略します。文書登録では引き続き既存のベクターストア向けに分割と埋め込みを行います。テキストだけの索引作成APIではありません。遅延初期化で初回の質問時に文書を登録すると、文書の埋め込みは発生します。

## キーワードと意味検索の結果を組み合わせる

`VectorWeight` はベクターの比重（0–1）、キーワードの比重は `1 - VectorWeight` です。`CandidateMultiplier` は各検索の候補数、`RrfK` は重み付きReciprocal Rank Fusionの順位平滑化を制御します。RAG再ランカーの候補倍率とは別です。実際の文書と質問で評価してください。

ベクター・キーワード単独モードは固有のスコアを維持します。設定可能なハイブリッドは片方だけでも正規化した重み付きRRFを使い、ベクター比重0では質問を埋め込みません。スコアは確率ではありません。`WeightedBlend` は検索と再ランカーのスコアを補正せず合成するため、入力を較正していないキーワード検索には既定の `RerankerOnly` を推奨します。

```csharp
using Mythosia.AI.Rag;
using Mythosia.VectorDb;

var service = new OpenAIService(apiKey, http)
    .WithRag(rag => rag
        .AddDocument("manual.txt")
        .UseHybridSearch(new HybridSearchOptions
        {
            VectorWeight = 0.7f,
            CandidateMultiplier = 4,
            RrfK = 60
        }));

string answer = await service.GetCompletionAsync("What is the refund policy?");
```

## ストアの対応範囲と互換性

InMemory、PostgreSQL、Qdrantは新しいキーワード検索と設定可能な重み付きRRFに対応します。テキストスコアにはInMemoryのBM25、PostgreSQLの設定した全文検索またはtrigram、Qdrantの疎索引を使います。エンジン間でスコアを同一視できません。

Pineconeは対応する `dotproduct` 索引で既定設定の `UseHybridSearch()` による従来のネイティブ検索を維持します。このアダプターはキーワードモードと両検索を混合する設定可能な重み付きRRFには対応しません。他のストアも該当する追加インターフェイスが必要です。非対応のモードや設定は、ベクター検索への切り替えや重みの無視ではなく、明示的なエラーになります。

標準のInMemory、PostgreSQL、Qdrantアダプターはニューラルモデルの導入や索引移行を行いません。`C#` と `C++` の区別は各解析器に依存します。以下のPIXIEオプションでも識別子の厳密な一致を評価する必要があります。

[検索方法とストア対応](rag.md#retrieval-modes)、[独自検索器](rag-pipeline.md#custom-retriever)を参照してください。

<a id="pixie-search"></a>

## PIXIEでローカルのニューラル検索を比較する

質問と文書の表現が異なる場合、学習済み疎検索は単語の一致だけでは見つからない関連語を補えます。任意の `Mythosia.AI.Rag.Search.Pixie` パッケージは文書と質問の両方をローカルのPIXIEでエンコードし、既存の密ベクトル検索と組み合わせます。PIXIEの実行にPythonサーバーやAPIキーは不要です。選択した密埋め込み・回答生成プロバイダーは外部APIを利用する場合があります。

```csharp
using Mythosia.AI.Rag;
using Mythosia.AI.Rag.Search.Pixie;
using Mythosia.VectorDb;

using var encoder = new PixieSparseEncoder(new PixieOptions());
var searchStore = new PixieInMemoryStore(encoder);
RagStore rag = await RagStore.BuildAsync(builder => builder
    .UseEmbedding(embeddings)
    .UseStore(searchStore)
    .AddDocument("manual.txt")
    .UseHybridSearch(new HybridSearchOptions { VectorWeight = 0.7f }));

RagProcessedQuery result = await rag.QueryAsync("refund policy");
```

このストアでは `UseKeywordSearch()` がニューラル疎検索を選択します。密ベクトル用の質問埋め込みは省略しますが、PIXIEの質問推論は実行します。RAGの文書登録では引き続き密埋め込みを生成します。`UseHybridSearch(...)` は疎ベクトルの内積と密ベクトルのコサイン類似度の順位を、設定した重み付きRRFで統合します。

このプレビューの `PixieInMemoryStore` はメモリ内索引です。PostgreSQL、Qdrant、PineconeへのPIXIE接続は追加しません。再起動やモデル・設定変更後は再索引してください。ストアの全処理が終わるまでエンコーダーを維持し、その後に破棄します。既存検索が既定のままなので、同じ文書と正解付き質問で比較してから切り替えてください。`C#` と `C++` の厳密な区別や除外条件は保証されません。

[PIXIEの接続と比較ガイド（英語）](../rag-pixie-search.md).
