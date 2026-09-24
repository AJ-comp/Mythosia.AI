# RAGパイプラインのカスタマイズ

<a id="indexing-validation"></a>

## インデックス作成の失敗から既存文書を守る

カスタム分割器や埋め込み応答に問題があっても、検索可能な文書を不完全な内容や対応の誤った内容で黙って置き換えてはいけません。パイプラインは `onDocumentEmbedded` を使う場合も、文書ごとに保存の開始前に検証します。

埋め込み、保存、保存コールバックの前に、`RagDocument.Id` が null、空文字列、空白のみなら `ArgumentException` が発生します。分割結果のリストまたはチャンクが null、`Content` または `Metadata` が null、チャンク ID が空白、同じ文書内で ID が重複する場合は `InvalidOperationException` になります。重複判定には大文字と小文字を区別する `StringComparer.Ordinal` を使います。チャンクの値とメタデータは最初の埋め込み呼び出し前にコピーします。

有効なカスタム ID はそのまま保持します。自動生成、トリミング、補正は行わず、異なる文書間のカスタムチャンク ID の衝突も全体では検出しません。[カスタム分割器の例](text-splitters.md)のように、対象コレクション内で一意の ID を使ってください。予約キー `document_id` の正規化は保存用コピーにだけ適用し、元のメタデータは変更しません。

無効な ID、分割の失敗、不正な埋め込みバッチでは、その文書の既存レコードを保持し、保存コールバックを呼び出しません。文書の全バッチが[埋め込み検証](rag-embedding.md#embedding-validation)を通過してから保存します。同じ処理ですでに完了した別文書は元に戻しません。保存開始後のロールバックは保存先やコールバックの実装によります。

今回の検証と応答順序の修正では、すでに上書きされた本文や誤ったチャンクに対応付けて保存された既存ベクトルは自動復旧しないため、影響を受けた文書は元のデータから再インデックスしてください。

<a id="custom-persistence"></a>

## 保存コールバックでも文書全体を置き換える

文書が短くなったときに新しいチャンクだけを upsert すると、古い末尾が検索に残ります。`onDocumentEmbedded` は標準保存を完全に置き換えるため、渡されたレコードの正規化済み `document_id` で文書全体を置き換えてください。コールバックは検証済みで空でない文書を一つずつ受け取ります。

```csharp
using Mythosia.AI.Rag;
using Mythosia.VectorDb;

var store = await RagStore.BuildAsync(config => config
    .AddDocuments("./docs/")
    .UseOpenAIEmbedding(apiKey)
    .UseStore(vectorStore),
    onDocumentEmbedded: async records =>
    {
        var documentId = records[0].Metadata["document_id"];
        await vectorStore.ReplaceByFilterAsync(
            new VectorFilter().Where("document_id", documentId), records, cancellationToken);
    },
    cancellationToken: cancellationToken);
```

分割に成功してもチャンクが 0 件なら、このコールバックも標準ストアも呼び出しません。既知の文書 ID で独自ストアから明示的に削除してください。`DeleteDocumentAsync` はパイプラインのストアを対象にする場合に使います。置換の原子性とロールバックはストアやコールバックに依存します。

<a id="url-documents"></a>

## URL 文書を安全に読み取る

サーバーはテキスト文書を圧縮して送信する場合があります。`AddUrl` は `gzip`、`deflate`、Brotli（`br`）を展開してからテキストを読み取り、圧縮ストリームが最後まで完成していることも検証します。HTTP 転送が成功していても、圧縮データの切り詰め、展開エラー、形式に含まれるチェックサムの検証失敗があれば、埋め込み・保存前に読み込みを中止し、その文書の既存レコードを維持します。未対応の `Content-Encoding` や多重圧縮も、埋め込み・保存前に拒否します。

遅い URL 文書の待機を中止するには、`RagStore.BuildAsync` に `cancellationToken` を渡してください。トークンは HTTP 要求、応答本文の読み取り、展開処理まで伝播します。キャンセルは協調的で、先に保存済みの別文書は元に戻しません。

<a id="custom-retriever"></a>

## 埋め込みを必須にしない検索器を接続する

商品コードにはキーワード検索、文書と異なる表現の質問には意味検索が適しています。選んだ検索器が必要な処理だけを行い、キーワード検索の前に質問を埋め込む必要がなくなります。

- 変更前：すべての検索戦略に質問の埋め込みを渡します。
- 変更後：選択した検索器が必要な質問表現だけを作成します。

外部索引や別の質問表現を使うには `IRagRetriever` を実装します。`RagRetrievalRequest` は `Query`（意味検索用の質問全体）、nullableな `TextQuery`（字句検索の上書き）、`TopK`、`Filter`、`ProgressAsync` を渡します。組み込み検索器は `TextQuery` がnullなら `Query`、空文字ならテキスト検索を省略します。独自検索器は前処理、フィルター、件数制限、キャンセルを適用してください。

```csharp
using Mythosia.AI.Rag;
using Mythosia.VectorDb;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

public sealed class CatalogRetriever : IRagRetriever
{
    private readonly ITextSearchStore _catalog;
    public CatalogRetriever(ITextSearchStore catalog) => _catalog = catalog;

    public Task<IReadOnlyList<VectorSearchResult>> RetrieveAsync(
        RagRetrievalRequest request,
        CancellationToken cancellationToken = default)
        => _catalog.TextSearchAsync(
            request.TextQuery ?? request.Query,
            request.TopK,
            request.Filter,
            cancellationToken);
}
```

```csharp
// catalog: an existing ITextSearchStore
var service = new OpenAIService(apiKey, http)
    .WithRag(rag => rag.UseRetriever(new CatalogRetriever(catalog)));
```

`UseRetriever(...)` または `RagPipeline.SetRetriever(...)` で登録します。既存の `IRetrievalStrategy` と `SetRetrievalStrategy(...)` は維持され、互換アダプターは引き続き質問を埋め込みます。結果には再ランキングとコンテキスト構築に必要な本文とメタデータを含めてください。

```csharp
store.UpdateRetriever(new CatalogRetriever(catalog));
store.UseKeywordSearch();
store.UpdateRetrievalStrategy(new HybridSearchOptions { VectorWeight = 0.7f });
store.UpdateRetriever(null); // UseVectorSearch
```

`UseKeywordSearch()` は質問の埋め込みを省略します。文書登録では引き続き既存のベクターストア向けに分割と埋め込みを行います。テキストだけの索引作成APIではありません。遅延初期化で初回の質問時に文書を登録すると、文書の埋め込みは発生します。

質問の `Embedding` ステージは検索器に応じて実行され、キーワード検索では通知されません。独自検索器は `request.ProgressAsync` で実際の処理を通知できます。文書の埋め込みは変わりません。

## RAGパイプラインとは？

RAGパイプラインは、ユーザーの質問が入ってからAIが回答を生成するまでに通る**一連の処理ステージ**のことです。工場の組み立てラインのように、各ステージが順番に実行され、質問をより正確な回答へと仕上げていきます。

## パイプラインの全体フロー

質問が入ると、以下のステージを順番に通ります：

```
ユーザーの質問
    ↓
① クエリ書き換え (QueryRewrite)   — 会話の文脈を反映して検索クエリを整えます
    ↓
② フィルタリング (Filtering)      — ネームスペースやメタデータで検索範囲を絞ります
    ↓
③ 必要な場合のみ埋め込み (Embedding)            — クエリを数値ベクターに変換します
    ↓
④ 検索 (Retrieval)               — ベクターストアから類似チャンクを取得します
    ↓
⑤ 再ランキング (Reranking)       — 検索結果の関連性をより精密に再評価します
    ↓
⑥ コンテキスト構築 (ContextBuild) — 最終チャンクをプロンプトに組み立てます
    ↓
AI回答生成
```

各ステージは独立して動作するため、必要に応じて特定のステージだけを差し替えたりスキップしたりできます。たとえば、クエリ書き換えはマルチターン会話でなければ省略され、再ランカーを設定していなければ再ランキングも自動的にスキップされます。

## パイプラインをカスタマイズする理由

デフォルトのRAGパイプラインは特別な設定なしでもうまく動作しますが、実際のプロジェクトでは以下のような理由でより細かい制御が必要になります：

- **デバッグ** — どのステージで時間がかかっているのか、クエリ書き換えが意図しない形で質問を変えていないか確認したいとき
- **プロンプトエンジニアリング** — デフォルトのプロンプトテンプレートが自社サービスのトーンや要件に合わないとき
- **アーキテクチャ** — 複数のAIサービスが1つのインデックスを共有してコストと一貫性を管理したいとき
- **検査** — AIに送る前に、実際にどのドキュメントが検索されたかを事前に確認したいとき

以下で、これらの制御を可能にするツールを一つずつ見ていきましょう。

検索段階の進捗に加えて回答生成中の表示や停止も扱う場合は、`RagEnabledService.StartRunAsync`から返るRunを使います。追加指示によるRAG再検索は自動では行いません。[Runの利用ガイド](execution-api-transition.md)を参照してください。

## 進捗追跡

各ステージが実行されるたびにコールバックを受け取り、パイプラインがどのステージを通過しているかをリアルタイムで確認できます：

```csharp
var options = new RagQueryOptions
{
    ProgressAsync = async stage =>
    {
        Console.WriteLine($"[RAG] {stage}");
        // ステージ: QueryRewrite, Filtering, Embedding, Retrieval, Reranking, ContextBuild
    }
};

var response = await ragService.GetCompletionAsync("質問", options);
```

各ステージ間の所要時間を計測すれば、どこがボトルネックなのか簡単に把握できます。たとえば、Retrievalステージが特に遅ければ、ベクターストアのインデックス設定を見直すきっかけになりますね。

## カスタムプロンプトテンプレート

検索されたドキュメント内容がAIに渡される方法を直接制御できます。`{context}`には検索されたチャンクが、`{question}`にはユーザーの質問が入ります：

```csharp
.WithRag(rag => rag
    .WithPromptTemplate("""
        以下の情報のみを使用して質問に答えてください。
        コンテキストに答えがない場合は「わかりません」と言ってください。

        コンテキスト:
        {context}

        質問: {question}
        """)
    .AddDocument("faq.txt")
)
```

プロンプトテンプレートをうまく書けば、AIがドキュメント内容の外の話を作り上げてしまう現象（ハルシネーション）を大幅に減らすことができます。

## RagStoreの共有

ドキュメントインデックスを一度だけ作成し、複数のAIサービスで共有できます。同じドキュメントをもとに複数モデルの回答品質を比較したり、A/Bテストを行うときに便利です：

```csharp
// インデックスを一度だけビルド
RagStore store = await RagStore.BuildAsync(rag => rag
    .UseOpenAIEmbedding(apiKey)
    .AddDocuments("docs/"));

// 異なるAIサービスで同じインデックスを再利用
var claudeRag = new AnthropicService(apiKey, http).WithRag(store);
var gptRag    = new OpenAIService(apiKey, http).WithRag(store);
```

両サービスが同じ埋め込みとベクターインデックスを共有するため、ストレージや埋め込み計算の重複がありません。

## RagStoreへの直接クエリ

AIサービスを介さずにベクターストアへ直接質問を投げることもできます。AIに送る前に「実際にどのドキュメントが検索されるか」を確認したいときに役立ちます：

```csharp
RagProcessedQuery result = await store.QueryAsync("返品ポリシーは何ですか？");

Console.WriteLine($"書き換えられたクエリ: {result.RewrittenQuery}");

foreach (var ref_ in result.References)
{
    Console.WriteLine($"[{ref_.Score:F2}] {ref_.Record.Content[..100]}");
}
```

`result.RequestMessageContent`にはAIに渡される完成済みプロンプトがそのまま入っています。LLMトークンを消費せずに検索品質をチェックできるので、開発中のデバッグに非常に便利です。

## 内部動作の仕組み

`.WithRag()`を呼び出すと、実際には`RagEnabledService`というラッパーが作成されます。このラッパーは元のAIServiceを包み、RAGパイプラインとLLM呼び出しを自動的に接続します。その核心には[AIRequestContext](request-contexts.md)があります。

### 全体フロー

```
ragService.GetCompletionAsync("返品ポリシーは何ですか？")
    ↓
① RagEnabledServiceがRAGパイプラインを実行
   クエリ書き換え → フィルタリング → 埋め込み（必要な場合） → 検索 → コンテキスト組み立て
    ↓
② TemplateContextBuilderが{context}と{question}を置換
   → "以下の情報で答えてください。\n[1] 返品は30日以内...\n質問: 返品ポリシーは何ですか？"
    ↓
③ RagEnabledServiceがAIRequestContextを生成
   RequestMessageOverride = 組み立てられたプロンプト
    ↓
④ _innerService.GetCompletionAsync(元のメッセージ, context: context)を呼び出し
   → AIServiceがAsyncLocalにcontextを保存
   → 元の質問を会話履歴に追加
    ↓
⑤ AIService.GetLatestMessages()が現在のリクエストの最初の入力を差し替え
   会話履歴: "返品ポリシーは何ですか？"（元のまま保持）
   モデルが見るもの: 組み立てられたプロンプト（RequestMessageOverride）
```

### なぜこの設計なのか？

この設計の核心は**会話履歴とモデル入力の分離**です：

- **会話履歴には元の質問が残ります** — 後続の会話で「それ」が何を指すか文脈を維持します
- **モデルには組み立てられたプロンプトが渡されます** — 検索されたドキュメント＋質問を含む完成したプロンプト
- **AIServiceの状態は変更されません** — `AsyncLocal<T>`によりリクエスト単位で隔離されます

これが`request-contexts.md`で説明している`RequestMessageOverride`の実際のユースケースです。RAGパイプラインがこのメカニズムを自動的に活用するため、ユーザーは`.WithRag()`を呼び出すだけで済みます。

### コードで見る

`RagEnabledService`内部でこの接続が行われる核心コードです：

```csharp
var processed = await RewriteAndProcessAsync(query, options, cancellationToken);
var original = new Message(ActorRole.User, query);
return await _innerService.GetCompletionAsync(
    original,
    context: BuildRequestContext(processed, original),
    cancellationToken: cancellationToken);

private static AIRequestContext BuildRequestContext(RagProcessedQuery processed, Message original)
{
    var requestMessage = original.Clone();
    requestMessage.Content = processed.RequestMessageContent;
    if (original.HasMultimodalContent)
    {
        requestMessage.Contents = new List<MessageContent>
        {
            new TextContent(processed.RequestMessageContent)
        };
        requestMessage.Contents.AddRange(
            original.Contents.Where(content => !(content is TextContent)));
    }
    return new AIRequestContext { RequestMessageOverride = requestMessage };
}
```

`AIService`はcontextを`AsyncLocal`に保存します。`GetLatestMessages()`は現在の論理リクエストの最初の入力にだけ`RequestMessageOverride`を適用し、後続のassistantのツール呼び出しとツール結果を保持します。そのため、後続のモデルリクエストにも検索文書とツール結果を一緒に渡せます。完了後は以前のcontextに戻します。
