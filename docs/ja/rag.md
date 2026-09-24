# RAG（検索拡張生成）

完成した回答と停止ボタンだけなら`GetCompletionAsync`に`cancellationToken`を渡します。進捗イベントや対応モデルへの追加指示にはRunを使います。[完了要求のキャンセル](completions.md#completion-cancellation)を参照してください。

検索で補強した回答にも`RagEnabledService.GetCompletionAsync`へ`cancellationToken`を渡します。同じトークンが検索、`LlmQueryRewriter`、`LlmReranker`、内部完了呼び出しへ伝わり、検索中のキャンセルは後続のモデル呼び出しを防ぎます。`RagPipeline.QueryAndGenerateAsync`もトークンを渡します。各要素はキャンセルに協調する必要があり、完了した検索やツール操作は巻き戻しません。


`IAIService` 参照では `Mythosia.AI.Extensions` の `GetLastProcessing()` を使います。任意の `IAIProcessingInfoService` を読み、診断非対応なら空のリストを返します。`IAIService` に必須メンバーは追加しません。RAG では `RagEnabledService.WithSpeed(...)` が検索後の次の回答に適用され、`LastProcessing` はその回答を記録します。内部クエリ書き換えは分離され、Run 結果にも同じ `Processing` があります。 [WithSpeed](request-building.md#inference-speed)

## RAGとは？

RAG（Retrieval-Augmented Generation）は、AIモデルが回答を生成する際に、**自分が持っているドキュメントから関連情報を先に探し出し**、その情報をもとに回答させる技術です。

図書館でレポートを書く場面を想像してみてください。すべてを記憶だけで書くより、関連する本を先に探して読み、その内容を参考にして書く方がずっと正確ですよね？ RAGはまさにこの方法です。

## RAGが必要な理由

LLM（大規模言語モデル）は学習データをもとに回答するため、以下のような限界があります：

- **最新情報を知りません** — 学習時点以降の情報にはアクセスできません
- **社内ドキュメントを知りません** — 会社のポリシーや製品マニュアルなどの非公開データには触れられません
- **ハルシネーション** — 知らない内容でもそれらしく作り上げてしまうことがあります

RAGはこれらの限界を解決します。質問が来たらまず自分のドキュメントから関連情報を検索し、その結果をプロンプトに含めることで、AIが**根拠のある回答**を生成できるようにします。

文書の索引をすでにプロバイダーが管理している場合は、[ホスト型ファイル検索との使い分け](reasoning-and-search.md)を確認してください。RAG が管理する検索参照と、プロバイダーが返す出典は別に保持されます。

## RAGの動作フロー

RAGは大きく2つのステージに分かれます。

### ステージ1：ドキュメント準備（初回のみ実行）

```
ドキュメント → テキスト分割（チャンキング） → 埋め込み（ベクター変換） → ベクターストアに保存
```

1. **[テキスト分割](text-splitters.md)** — 長いドキュメントを検索に適した小さな断片（チャンク）に分けます
2. **埋め込み** — 各チャンクを数値ベクターに変換します。意味が似たテキストは似たベクターになります
3. **保存** — 変換されたベクターを[ベクターストア](vectordb-overview.md)に保存します

### ステージ2：質問応答（質問のたびに実行）

以下は既定のベクター検索の流れです。キーワードモードは質問の埋め込みを省略し、独自検索器は必要な変換を選びます。

```
ユーザーの質問 → 質問を埋め込み → ベクターストアで類似チャンクを検索 → プロンプトに注入 → AI回答生成
```

1. **質問の埋め込み** — ユーザーの質問も同じ方法でベクターに変換します
2. **類似度検索** — ベクターストアから質問に最も似たチャンクを見つけます
3. **プロンプト構築** — 見つかったチャンクをプロンプトに入れてAIに渡します
4. **回答生成** — AIが受け取ったドキュメント内容を参考にして回答を生成します

検索結果に基づく回答を逐次表示し、生成を停止できるようにする場合は、`RagEnabledService.StartRunAsync`を使えます。検索はRunの前に行われ、追加指示で自動的に再検索されることはありません。[Runの利用ガイド](execution-api-transition.md)に例と適用範囲があります。

## インストール

```bash
dotnet add package Mythosia.AI.Rag
```

## クイックスタート

Mythosia.AIでは、この一連のプロセスを`.WithRag()`の一行で設定できます：

```csharp
using Mythosia.AI.Rag;

var service = new AnthropicService(apiKey, http)
    .WithRag(rag => rag
        .AddDocument("manual.txt")
        .AddDocument("policy.txt")
    );

var response = await service.GetCompletionAsync("返金ポリシーは何ですか？");
```

上記のコードだけで、ドキュメント分割 → 埋め込み → 保存 → 検索 → プロンプト注入が自動的に処理されます。

ローカルのニューラル疎検索を既存検索と比較するには、任意の `Mythosia.AI.Rag.Search.Pixie` プレビューを使用します。既存の密埋め込みプロバイダーを維持し、PIXIE索引をメモリに保存します。永続ストアの移行や既定検索の自動置換は行いません。 [PIXIEの接続と比較ガイド（英語）](rag-hybrid-search.md#pixie-search).

<a id="rag-message-attachments"></a>

## 添付画像と自分の文書を使って回答する

製品写真をマニュアルに基づいて説明するには、質問と画像を含む `Message` を `RagEnabledService.GetCompletionAsync(Message)` または `StartRunAsync(Message)` に渡します。どちらも、テキスト以外の添付内容を内部 AI サービスへのリクエストに保持します。検索にはメッセージのテキストを使い、添付ファイル自体を自動的に索引化したり埋め込んだりはしません。選択したプロバイダーとモデルがその添付形式をサポートしている必要があります。検索した文脈は送信リクエストにのみ追加し、元の `Message` や会話履歴のユーザーテキストを検索した文脈で上書きしません。

マニュアルと最新の在庫を両方参照するには、RAGと登録済みツールを組み合わせます。`GetCompletionAsync`のツール呼び出し中も、検索した文脈は最初の入力に保持され、その後の各ツール結果はそのままモデルに送信されます。会話履歴には元のユーザー入力を保持します。

<a id="retrieval-modes"></a>

## 文書の検索方法を選ぶ

商品コードにはキーワード検索、文書と異なる表現の質問には意味検索が適しています。選んだ検索器が必要な処理だけを行い、キーワード検索の前に質問を埋め込む必要がなくなります。

```csharp
RagStore store = await RagStore.BuildAsync(rag => rag
    .AddDocument("manual.txt")
    .UseKeywordSearch());

RagProcessedQuery result = await store.QueryAsync("refund policy");
```

`UseKeywordSearch()` は質問の埋め込みを省略します。文書登録では引き続き既存のベクターストア向けに分割と埋め込みを行います。テキストだけの索引作成APIではありません。遅延初期化で初回の質問時に文書を登録すると、文書の埋め込みは発生します。

[検索方法とストア対応](rag-hybrid-search.md)、[独自検索器](rag-pipeline.md#custom-retriever)を参照してください。

## ドキュメントの追加

ローカルファイル、URL、直接入力したテキストなど、さまざまな方法でドキュメントを追加できます：

```csharp
.WithRag(rag => rag
    .AddDocument("readme.txt")                    // ローカルファイル
    .AddUrl("https://example.com/doc.txt")        // URL
    .AddText("インラインコンテンツもここに追加できます。")   // 生の文字列
)
```

`AddUrl` は対応する HTTP 圧縮を検証・展開してからテキストを読み取り、不完全・未対応の圧縮や多重圧縮を拒否します。[URL の展開とキャンセル](rag-pipeline.md#url-documents)を参照してください。

<a id="document-identity"></a>

### 同じ名前のファイルを区別する

2 社がそれぞれ `docs/faq.txt` を提供する場合があります。両方の文書をインデックスに残し、同じファイルを再登録するときは同じ文書として識別する必要があります。

```csharp
var store = await RagStore.BuildAsync(rag => rag
    .AddDocuments("company-a/docs")
    .AddDocuments("company-b/docs"));
```

RAG の既定の保存処理では、ベクトルストアへレコードを送る前に文書 ID を作成し、その `document_id` に一致するレコードを置き換えます。本ライブラリの PostgreSQL（pgvector）ストアはその ID を使い、元ファイルのパスを独自に調べることはありません。以前はディレクトリ登録時に `company-a/docs/faq.txt` と `company-b/docs/faq.txt` のどちらにも `faq.txt` が送られたため、2 つ目の文書が最初の文書を置き換えていました。今回の修正では ID 作成時にフルパスを保持します。PostgreSQL のスキーマは変更しません。 ストレージの使用例にある `full_path` フィルターは呼び出し側が指定したメタデータを使うもので、文書やレコードの一意な ID を自動生成するものではありません。

組み込みの `PlainTextDocumentLoader` と `DirectoryDocumentLoader` は、`Path.GetFullPath` で正規化したファイルの絶対パスを `Source` と自動文書 ID に使用します。そのため、異なるディレクトリのファイルは異なる ID になります。相対パス・絶対パス・`./` を含むパスも、大文字と小文字を含めて同じ絶対パスに解決されれば同じ ID を使います。相対パスを使う場合は作業ディレクトリを一定にしてください。ファイルの移動、シンボリックリンク、ハードリンク、大文字と小文字が異なるパスでの ID の維持は保証されません。

`AddText(..., id: ...)`、明示的な `RagDocument.Id`、カスタムローダーの `Source` の規則は変わりません。呼び出し API の変更は不要です。これらの組み込みローダーでは `Source` が絶対パスになるため、既定の出典表示にも絶対パスが現れる場合があります。表示には `filename`、または既定のディレクトリローダーの `relative_path` メタデータを利用できます。設定を受け取るディレクトリ登録のオーバーロードは `relative_path` を自動追加しません。

**既存インデックスの移行:** 以前の相対パス ID は自動削除・変換されません。新しいコレクションに全書類を再インデックスし、検証後にアプリケーションを切り替える方法を推奨します。既存コレクションを使い続ける場合は、所有関係を確認した旧文書 ID だけを削除して元ファイルを再インデックスしてください。同じファイル名をまとめて削除すると、別のディレクトリの文書にも影響する可能性があります。

更新・削除の対象を正しい文書に限定するため、`document_id` はパイプラインの予約キーです。入力メタデータが別の値を指定していても、保存する各レコードには実際の `RagDocument.Id` を設定します。入力文書とスプリッターのメタデータ辞書自体は変更せず、カスタム保存コールバックにも正規化済みレコードを渡します。アプリケーション独自の ID には別のキーを使ってください。

すでに誤った `document_id` で保存されたレコードは自動修復されません。信頼できる原文から新しいコレクションを作るか、影響を受けたレコードの所属を確認して対象だけを整理し、再インデックスしてください。正しい ID で再登録するだけでは、別の ID に保存された古いレコードを確実に見つけられません。

同じファイルを相対パスと絶対パスで登録しても一つの文書を更新し、別フォルダーの同名ファイルは区別する必要があります。`WordDocumentLoader`、`ExcelDocumentLoader`、`PowerPointDocumentLoader`、`PdfDocumentLoader` は、標準 TXT ローダーと同様に `DoclingDocument.Source` を正規化した絶対ファイルパスに設定します。RAG の自動文書 ID はこの値から生成され、明示的な ID は呼び出し側が管理します。既定の出典表示に絶対パスが現れる場合があります。

[登録パスが異なっても同じファイルを更新する](document-loaders.md#file-source-identity).

<a id="empty-document-updates"></a>

### 空の文書への更新で古い検索内容も削除する

廃止した返金案内を空にして同じ文書を再インデックスした場合、以前の案内が回答に使われ続けてはいけません。標準の RAG 保存処理では、分割が正常に完了してチャンクが 0 件なら、その `document_id` の既存レコードを空の集合に置き換えます。埋め込みは要求せず、他の文書 ID のデータは維持します。空文字列や空白のみの文書で分割結果が 0 件になる場合も、カスタム splitter が正常に 0 件を返す場合も同じです。

設定済みの `RagPipeline` インスタンス `pipeline` で、保存済みの文書 ID を再利用します:

```csharp
await pipeline.IndexDocumentAsync(
    new RagDocument { Id = "refund-policy", Content = "" },
    cancellationToken);
```

後から同じ ID で内容のある文書を再びインデックスできます。ローダーが文書を一つも返さない場合や、次のファイル一覧から文書が消えた場合は、削除指示ではありません。置き換える文書 ID が渡されていないため、自動削除しません。

読み込み・解析・分割で例外が発生した場合や、保存の呼び出し前にキャンセルを検知した場合は、その文書の既存レコードを維持します。ローダーとパーサーは失敗を例外で通知してください。正常に返された 0 件という結果だけでは、意図的に空にした文書と失敗を区別できません。保存開始後の失敗やキャンセルのロールバックは保存先の実装に依存し、PostgreSQL の置換処理はトランザクションを使います。一括処理は文書単位のため、先に完了した文書までは元に戻しません。

**カスタム保存:** `onDocumentEmbedded` を渡した場合、引き続きそのコールバックが保存を担当します。チャンクが 0 件ならコールバックを呼ばず、標準の保存先にもアクセスしません。アプリケーションが既知の文書 ID を使って独自の保存先から明示的に削除するか、パイプラインの保存先に対して `DeleteDocumentAsync` を使ってください。

## カスタム埋め込みプロバイダー

デフォルトでは組み込みのローカル埋め込みプロバイダーを使用します。埋め込み専用のモデルを別途指定したい場合は次のように設定します：

```csharp
using Mythosia.AI.Rag.Embeddings;

var embedder = new OpenAIEmbeddingProvider(apiKey, http, "text-embedding-3-small");

var service = new AnthropicService(apiKey, http)
    .WithRag(rag => rag
        .UseEmbedding(embedder)
        .AddDocument("knowledge-base.txt")
    );
```

## カスタムベクターストア

デフォルトではインメモリストアを使用するため、アプリを再起動するとデータが消えます。本番環境ではデータを永続的に保管できるベクターストアを接続しましょう：

```bash
dotnet add package Mythosia.VectorDb.Postgres
```

```csharp
using Mythosia.VectorDb.Postgres;

var store = new PostgresStore(new PostgresOptions
{
    ConnectionString = connectionString,
    Dimension = 1536
});

var service = new OpenAIService(apiKey, http)
    .WithRag(rag => rag
        .UseStore(store)
        .AddDocument("large-corpus.txt")
    );
```

## クエリオプション

検索時に何個のチャンクを取得するか、最低類似度をどの程度にするかなどを調整できます：

```csharp
var options = new RagQueryOptions
{
    FinalFilter = new RagFilter
    {
        TopK = 5,           // 取得するチャンク数（デフォルト5個）
        MinScore = 0.7      // このスコア以上のチャンクのみ取得
    }
};

var response = await service.GetCompletionAsync("質問", options: options);
```

## 次のステップ

基本のRAGを理解したら、次の機能で検索品質をさらに高めましょう：

- [ハイブリッド検索](rag-hybrid-search.md) — 意味検索とキーワード検索を同時に
- [クエリ書き換え](rag-query-rewriting.md) — 会話の文脈を反映した検索クエリの最適化
- [再ランキング](rag-reranking.md) — 検索結果の精度をもう一段高める
- [パイプラインのカスタマイズ](rag-pipeline.md) — RAG動作プロセスをきめ細かく制御
- [エージェンティックRAG](rag-agentic.md) — AIが自ら判断して検索するインテリジェントRAG
- [ベクターストア](../vectordb-overview.md) — 永続ストアの設定
- [テキストスプリッター](text-splitters.md) — ドキュメントの分割方法を変更

Perplexity: [文書インデックスでベクトルを使う / 回答を生成せずに検索する](perplexity.md).
