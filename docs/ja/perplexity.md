# Perplexity：出典付きの回答、検索と埋め込み

最新情報に基づく回答と、読者が確認できる出典が必要なときに Perplexity を使います。`PerplexityService` は Agent API を呼び出し、独立した検索と埋め込みは、自分で選んだ回答モデルに検索の仕組みを組み合わせるために使います。

## 必要な処理から選ぶ

最新情報への回答、Web ページの取得、自分の文書インデックス用ベクトルの作成は別の処理です。検索のたびに回答モデルを呼ぶのではなく、その仕事を担当する機能を選びます。

| 必要な処理 | 機能 |
| --- | --- |
| 出典を伴う調査回答 | `PerplexityService` |
| 別のモデルや画面で使う Web ページ一覧 | `PerplexitySearchClient` |
| 通常の RAG に使う独立した段落のベクトル | `PerplexityEmbeddingProvider` |
| 同じ文書の隣接チャンクを考慮したベクトル | `PerplexityContextualizedEmbeddingProvider` |

`Mythosia.AI` をインストールします。埋め込みの例には `Mythosia.AI.Rag` も必要です。API キーと、アプリケーションが管理する `HttpClient` を用意してください。例の `apiKey`、`httpClient`、`cancellationToken` はアプリケーションから渡す値です。

## Agent プリセットで回答する

プリセットはモデル、指示、ツール、推論量、予算を組み合わせた設定です。簡単な確認には `Fast`、日常的な調査には `Low`、複数段階の比較には `Medium`、詳しい調査には `High` / `XHigh` を使います。`WideResearch` は広範な調査に使用し、長時間かかる作業にはバックグラウンド実行を推奨します。これらはモデル ID ではありません。

```csharp
using Mythosia.AI.Models.Perplexity;
using Mythosia.AI.Services.Perplexity;

var service = new PerplexityService(apiKey, httpClient)
    .WithPerplexityOptions(new PerplexityAgentOptions
    {
        Preset = PerplexityPreset.Low
    });

await using var run = await service.StartRunAsync(
    "最新のバッテリーリサイクル手法を比較し、出典を示してください。",
    onText: text => Console.Write(text),
    cancellationToken: cancellationToken);
string answer = (await run.Result).Text;
foreach (var source in run.Citations)
    Console.WriteLine(source.Url);
```

回答だけなら `GetCompletionAsync`、既存のストリーミング処理にはサービスの `StreamAsync`、実行中の観察やキャンセルには `StartRunAsync` を使います。`(await run.Result).Text` は出力された回答テキストを連結します。引用イベントを読まなくても `run.Citations` と `LastCitations` に出典が残ります。推論イベントは提供元が公開する内容のみで、モデルによって異なります。

`AIRunResult.RequestedModel` は提供元別のモデル上書きを含め、実際の要求に送る単一の明示的モデルを開始時に取得した値です。プリセット、プロファイル、サーバー側ルーティングで選択し、単一のモデルフィールドを送らない場合は `null` です（例: Perplexity の `Models` リスト）。実際の応答モデルである `Model` とは独立しています。

## 調査範囲とツールを制御する

`WithPerplexityOptions(...)` はサービスに保持され、論理リクエストごとにコピーされます。共通の `WithReasoning(...)` と `WithWebSearch(...)` は、クライアントツールのラウンドや型付き出力の修復を含む次の論理リクエストに適用されます。内部の RAG クエリ書き換えには最終回答の検索設定が渡りません。

`UsePreset(...)` でプリセットを選べます。プリセット/プロファイルは独自のモデルを選び、`ModelOverride` で明示的に変更します。`DisableWebSearch` はアダプターの既定ツールだけを除き、プリセット内蔵の検索停止を保証しません。モデルに応じて `Minimal`、`Low`、`Medium`、`High`、`XHigh`、`Max` を使用できます。`None` と直接 Sonar への明示的な推論指定は拒否されます。内部の `DisableReasoning` は低い対応レベルか省略を使い、完全な無効化を保証しません。

| 設定 | 用途 |
| --- | --- |
| `Preset` / `ModelOverride` | 調査設定を選ぶか、provider/model ID でそのモデルを明示的に変更します。 |
| `MaxSteps` | 提供元のツールループを制限します。0 は提供元の既定値です。クライアント関数の継続回数を制限する `WithMaxRounds` とは別です。 |
| `ReasoningEffort` | 推論に使う量を調節します。`Auto` は指定を省略し、有効な段階は実際のモデルに依存します。 |
| `DisableWebSearch` / `Tools` | アダプターの既定 Web ツールと、明示的なホスト型ツールを設定します。 |
| `Models` | 優先順に 1～5 個の代替モデルを指定します。単一モデルの設定に優先し、要求機能はすべての候補に対応している必要があります。 |
| `Profile` | 保存済みのサーバー設定を使い、必要ならバージョンを固定します。`Preset` とは併用できません。 |
| `ServiceTier` | 既定、flex、priority 処理を要求します。モデルが対応しない区分は提供元が無視する場合があります。 |
| `Skills` | 組み込み、インライン、またはアップロード済みのカスタムスキルを渡します。カスタムリソースは Perplexity アカウントに属します。 |
| `LanguagePreference` / `PromptCacheKey` | 回答言語やキャッシュルーティングのヒントを指定します。キャッシュヒットを保証するものではありません。 |
| `PreviousResponseId` / `Store` | 完了済みの提供元レスポンスを継続したり、取得の可否を設定します。継続時は `StatelessMode` で新しいターンだけを渡します。`Store = false` は提供元の保存自体を無効にしません。 |

`PerplexityHostedTool` に対応する `Type` と文書化された JSON 互換の `Parameters` を指定します：`web_search`、`fetch_url`、`finance_search`、`people_search`、`sandbox`、`mcp`。MCP サーバーや管理コネクターは提供元を通じて動作するため、認証情報、権限、アカウントのリソースが接続先と一致する必要があります。アプリケーション関数は既存の `Functions` / 関数ビルダーに登録します。ホスト型ツールとローカルハンドラーでは実行主体が異なります。

`PerplexityHostedTools.WebSearch`、`FetchUrl`、`Sandbox`、`FinanceSearch`、`PeopleSearch`、`Mcp`、`Connector` でツール設定を作れます。MCP は承認待ちなしで実行されるため、必要に応じて `allowedTools` を制限します。Connector は提供元のプレビュー機能で、接続済みの統合を参照します。

```csharp
service.WithPerplexityOptions(new PerplexityAgentOptions
{
    Preset = PerplexityPreset.Low,
    MaxSteps = 8,
    Tools = new List<PerplexityHostedTool>
    {
        PerplexityHostedTools.WebSearch(),
        PerplexityHostedTools.FetchUrl()
    }
});
string comparison = await service.GetCompletionAsync(
    "プロジェクトの文書を読み、関連する機能を比較してください。");
```

ツール、推論、画像、スキーマの互換性はモデルによります。共通の `WithFileSearch` は Perplexity のベクトルストア用アダプターではありません。サンドボックスの生成ファイル、アップロードされた添付ファイル、外部 MCP データは別のリソースであり、共通のファイル検索ストアにはなりません。

## 出典、画像、構造化された回答

JSON フィールドが必要なら型付き completion または型付きストリーミングを使います。アダプターはネイティブスキーマを送信し、既存の修復処理を維持します。継続に必要なレスポンス項目とツール ID を保存するため、プロトコル履歴を手動で削除したり並べ替えたりしないでください。画像入力には `Message` と `ImageContent` で JPEG/PNG/WebP/GIF のバイト列か HTTPS URL を渡します。対応はモデルに依存し、画像生成の要求ではありません。

元の応答記録は履歴メタデータに保持しますが、後続リクエストには許可された `message`、`function_call`、`function_call_output` 入力項目だけを再送します。プロバイダー側のホスト実行状態全体を継続するには `PreviousResponseId` を使ってください。

引用は Web 検索結果や他の提供元の出典を示します。位置情報は個別のレスポンスとコンテンツ内を指し、連結済みの Run 結果内の位置ではありません。表示や検証には URL とタイトルを保持します。出典があることだけで、すべての生成内容が検証されたとは限りません。

## 長い処理を継続する

一時的な接続切断後も調査を続けたり、後から ID で結果を取得したりするには提供元のバックグラウンド実行を使います。ローカルの `AIRun` は現在のクライアント実行を制御し、バックグラウンドレスポンスにはサーバー側の独立した寿命があります。ストリームの読み取り終了は観察の終了です。遠隔処理を止めるにはジョブを明示的にキャンセルします。

```csharp
var researcher = new PerplexityService(apiKey, httpClient)
    .UsePreset(PerplexityPreset.High);
var job = await researcher.StartBackgroundAsync(
    "最新のバッテリーリサイクル手法を比較し、出典を示してください。", cancellationToken: cancellationToken);
Console.WriteLine(job.Id);
var completed = await job.WaitForCompletionAsync(
    cancellationToken: cancellationToken);
if (completed.Status != "completed")
    throw new InvalidOperationException(completed.Error ?? completed.Status);
Console.WriteLine(completed.Text);
```

`StartBackgroundAsync` は履歴を追加せず入力を取得し、有効なローカル関数や `Store = false` を拒否します。`GetResponseAsync` は一度取得し、`WaitForCompletionAsync` は終了状態までポーリングします。`Id` と `LastSequenceNumber` を保存し、`ResumeBackgroundRun(id).StreamAsync(startingAfter: cursor)` で再接続します。遠隔ジョブの停止は `CancelAsync` です。取得・読み取りトークンのキャンセルはそのクライアント操作だけを止めます。`LastResponse` のテキスト、状態、使用量、引用、`OutputJson` を参照し、回答利用前に終了状態を確認してください。

サンドボックスの出力は `ListFilesAsync` と `DownloadFileAsync(fileId)` で取得します。サービスにも `GetAgentResponseAsync`、`GetResponseFilesAsync`、`GetResponseFileContentAsync` があります。提供元の出力成果物を読む機能で、ベクトルストアの作成や検索ではありません。

組み込み Office スキルには、このガイドのバックグラウンド経路を使ってください。`StartBackgroundAsync` の後に `WaitForCompletionAsync` / `GetResponseAsync` とファイルメソッドを使用します。応答内の内部ツール記録は、通常のローカル関数呼び出しと区別できない場合があります。

バックグラウンドの送信、取得、キャンセル、ストリーム再接続は、実行中の `SteerAsync` やネイティブ非同期クライアントツールを有効にしません。再接続は元の処理を再送せず、既存レスポンスの観察を再開します。提供元のレスポンス ID とカーソルを保持してください。

## 回答を生成せずに検索する

独自のランキング、画面表示、別の LLM への入力には `PerplexitySearchClient` でページを取得します。回答モデルを呼ばず、`PerplexityService` の会話履歴も変えません。

```csharp
var search = new PerplexitySearchClient(apiKey, httpClient);
var pages = await search.SearchAsync(
    "バッテリーのリサイクル方法",
    new PerplexitySearchOptions
    {
        MaxResults = 5,
        DomainFilter = new[] { "nature.com", "science.org" },
        ContentSize = PerplexitySearchContentSize.Medium
    }, cancellationToken);
foreach (var page in pages.Results)
    Console.WriteLine($"{page.Rank}: {page.Title} — {page.Url}");
```

`SearchAsync` は単一または複数のクエリを受け取ります。Web/人物検索、国、ドメイン、言語、公開日・更新日、最新性を設定できます。`ContentSize` と明示的な `MaxTokens` / `MaxTokensPerPage` はいずれかを選びます。結果には順位、タイトル、URL、抜粋、提供元の日付が含まれます。順位は返却順であり関連性スコアではありません。

`ContentSize` は Web 検索のみで使用できます。People 検索では省略してください。この組み合わせは送信前にクライアントが拒否します。

## 文書インデックスでベクトルを使う

標準埋め込みは段落を独立して扱い、`IEmbeddingProvider` を実装するため既存のビルダーに接続できます。文脈埋め込みは隣接チャンクの順序と文書ごとのまとまりを維持します。無関係な文書を一つに平坦化しないよう、別の API を使います。

| モデル定数 | 提供元 ID | 既定の次元数 |
| --- | --- | --- |
| `Standard0_6B` | `pplx-embed-v1-0.6b` | 1024 |
| `Standard4B` | `pplx-embed-v1-4b` | 2560 |
| `Context0_6B` | `pplx-embed-context-v1-0.6b` | 1024 |
| `Context4B` | `pplx-embed-context-v1-4b` | 2560 |

```csharp
using Mythosia.AI.Rag;
using Mythosia.AI.Rag.Embeddings;

var rag = service.WithRag(builder => builder
    .AddText("購入後30日以内なら返品できます。", id: "returns")
    .UsePerplexityEmbedding(apiKey, httpClient));
string policy = await rag.GetCompletionAsync("購入品はいつまで返品できますか？");
```

```csharp
var contextual = new PerplexityContextualizedEmbeddingProvider(
    apiKey, httpClient, PerplexityEmbeddingModels.Context0_6B);
var documents = new[]
{
    new[] { "購入後30日以内なら返品できます。", "返金申請にはレシートを保管してください。" },
    new[] { "通常配送は3日かかります。", "速達配送は平日に利用できます。" }
};
var documentVectors = await contextual.GetDocumentEmbeddingsAsync(
    documents, cancellationToken);
float[] queryVector = await contextual.GetQueryEmbeddingAsync(
    "購入品はいつまで返品できますか？", cancellationToken);
```

文書とクエリには同じモデル、次元、エンコーディングを使います。`GetQueryEmbeddingAsync` は一つのクエリを独立した文書として同じ文脈モデルに送ります。文脈結果は文書とチャンクの順序を維持し、平坦な RAG ビルダーに自動接続されません。

float API は base64 の signed-int8 ベクトルを復号し、類似度計算用に正規化します。明示的な binary API は圧縮ビットを返し、ハミング距離を使います。バイナリを float 座標として暗黙に扱いません。全次元は 0.6B が 1024、4B が 2560 で、次元削減は提供元の制限に従います。バッチ、文書長、総トークン、アカウントのレート制限も適用されます。

バイナリ用は `GetBinaryEmbeddingAsync` / `GetBinaryEmbeddingsAsync` と文脈用 `GetBinaryDocumentEmbeddingsAsync` / `GetBinaryQueryEmbeddingAsync` です。`PerplexityBinaryEmbedding` は `Dimensions`、コピーを返す `ToArray()`、小さいほど類似する `HammingDistance` を提供します。バイナリ次元は8の倍数です。標準バッチは512テキスト、文脈バッチは512文書・16,000チャンクまでです。テキスト/文書当たり32K、総計120Kトークンは提供元が検証します。

## 既存の Sonar コードを移行する

このリリースは、提供元が告知した 2026年9月27日のエンドポイント終了に先立ち旧 Sonar アダプターを削除します。`PerplexityService` は `/v1/agent` を呼び、`AIModels.Perplexity.Sonar` は `perplexity/sonar` を指します。旧 Sonar 専用の検索ヘルパーとレスポンス型は削除されました。共通 completion/Run/引用、Agent プリセット、独立検索用 `PerplexitySearchClient` に移行してください。

推奨対応は Sonar → `Fast`、Sonar Pro → `Low`、Sonar Reasoning Pro → `Medium`、Sonar Deep Research → `High` です。同一の文章、料金、モデル動作を保証するものではありません。動的プリセットは提供元が更新するため、固定が重要なら明示的なモデルかバージョン付きプロファイルを使います。

ネイティブ steering、ネイティブ非同期クライアントツール、`CachePreservation.Required` は未対応です。Router/Gateway API は今回の対象外です。利用可能な組み合わせは提供元、モデル、アカウントに依存し、このガイドは全組み合わせの有料実証成功を意味しません。

Profile、custom skill、connector にはアカウントに事前登録したリソースが必要です。リクエスト形式は単体テスト済みですが、これらのリソースを使った実 API 呼び出しの成功は未検証です。

[Agent API](https://docs.perplexity.ai/docs/agent-api/quickstart) · [Search API](https://docs.perplexity.ai/docs/search/quickstart) · [Embeddings](https://docs.perplexity.ai/docs/embeddings/quickstart) · [Migration](https://docs.perplexity.ai/docs/agent-api/migrate-from-sonar/how-to) · [2026-09-27](https://community.perplexity.ai/t/sonar-is-moving-to-the-agent-api/5802)
