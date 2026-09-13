# Runで実行中のAIタスクを制御する

完成した回答と停止ボタンだけなら`GetCompletionAsync`に`cancellationToken`を渡します。進捗イベントや対応モデルへの追加指示にはRunを使います。[完了要求のキャンセル](completions.md#completion-cancellation)を参照してください。

設定をリクエストごとに分離し、共通設定から分岐するには[リクエストビルダー](request-building.md)を使います。`CreateRequest(...)`の後に`With...`をつなぎます。サービスのプロパティとfluentメソッドは従来の動作を維持します。

> 回答・使用量・出典をまとめて取得するには、`await run.Result` が返す `AIRunResult` を使用します。文字列は `result.Text` で取得でき、ストリームを読む必要はありません。Mythosia.AI 8.0.0 の API 変更です。`GetCompletionAsync` と `StructuredStreamRun<T>.Result` の戻り値型は維持します。 [Run の結果と移行](#run-result).

> `CreateRequest`の例にはMythosia.AI 8.0.0 / Abstractions 4.0.0が必要です。Runと共通リクエスト機能を導入した旧7.1リリースにはビルダーがありません。旧パッケージでは既存のサービスオーバーロードを使えます。

## なぜ実行中のタスクを制御するのか？

レポートの完成には、文書検索、API呼び出し、文章作成を何度か繰り返すことがあります。その途中でユーザーが進捗を確認したり、処理を中止したり、「今年のデータだけを含めて」と条件を追加したくなるかもしれません。アプリケーションには、これらの操作を実行中のタスクに結び付ける方法が必要です。

Runは、そのタスクを操作するためのハンドルを返します。チャット画面で到着したテキストやツールの実行状況を表示し、停止ボタンでキャンセルし、対応モデルには追加指示を送れます。いずれも同じ実行を対象とする操作です。

| アプリケーションで必要なこと | 使用するAPI |
| --- | --- |
| 完成した回答を受け取り、必要ならキャンセル | `GetCompletionAsync(..., cancellationToken: token)` |
| テキストを到着時に表示し、完了時にまとめて取得する | `onText`を指定してRunを開始し、`run.Result`を待ちます。 |
| ツールの動作を表示する、または出力処理を非同期で待つ | `run.StreamAsync()`のイベントを読み取ります。 |
| ユーザーが処理を停止できるようにする | 保持したハンドルで`run.Cancel()`を呼び出します。 |
| タスクの完了前に条件を追加する | `run.CanSteer`を確認し、対応モデルで`run.SteerAsync(...)`を使います。 |

`StartRunAsync`は1つのモデルタスクを開始して`AIRun`を返します。出力を読み取らなくても処理は進みます。同じハンドルでストリーミング、蓄積した結果の取得、キャンセル、対応モデルへの実行途中の追加指示を扱います。型付き・RAG版を含む`GetCompletionAsync`は、最終結果を受け取るための公開の便利APIとして残ります。

<a id="run-result"></a>

## 回答・使用量・出典をまとめて取得する

画面に完成した回答だけを表示する場合も、トークン使用量や出典を保存したいことがあります。以前の `run.Result` は文字列のみを返し、使用量はストリームから収集し、出典は Run から別途取得する必要がありました。`AIRunResult` はストリームを読まなくてもこれらをまとめます。

Before — 以前の Run 契約

```csharp
string answer = await run.Result;
```

After — Mythosia.AI 8.0.0

```csharp
using Mythosia.AI.Models.Runs;

await using var run = await service
    .CreateRequest("文書を分析してください。")
    .StartRunAsync();

AIRunResult result = await run.Result;
string answer = result.Text;
int? totalTokens = result.Usage?.TotalTokens;
var citations = result.Citations;
string? requestedModel = result.RequestedModel;
string? actualModel = result.Model;
var finishReason = result.FinishReason;
```

テキストを到着時に表示する場合も同じ結果を取得できます。コールバック、`run.StreamAsync()`、`SteerAsync`、キャンセルの役割は変わりません。

```csharp
using Mythosia.AI.Models.Runs;

await using var run = await service
    .CreateRequest("文書を分析してください。")
    .StartRunAsync(onText: text => Console.Write(text));

AIRunResult result = await run.Result;
Console.WriteLine($"\nTokens: {result.Usage?.TotalTokens}");
```

完了結果はスナップショットです。`Usage` と引用オブジェクトはコピーされ、取得した値を変更しても保存済み結果は変わりません。破棄後も取得できます。ストリームのフィルター、読み取り中止、観測バッファ超過で結果データは失われません。

`Usage` はライブラリの各モデルラウンドが報告した使用量を一度ずつ合計し、ラウンドイベントと最終合計を二重加算しません。報告がなければ `null` です。未提供の情報は推定しません。別途行われる補助要約リクエストを含まないため、請求全体やアカウント使用量ではありません。 一部のラウンドだけが使用量を報告する場合はその分だけを合計し、全ラウンドの使用量を保証しません。

プロバイダーが明示した `TotalTokens` は入力・出力の内訳が不完全でも保持され、ラウンド集計でも報告された合計を加算します。

トークン数は `Int32` です。ラウンドの合計が `Int32.MaxValue` を超えると、値を折り返す代わりにストリームまたは Run が `OverflowException` で失敗します。後処理は完了し、最終集計に失敗しても `run.Result` は待機したままになりません。

`run.StreamAsync()` が返すシーケンスは一度だけ列挙できます。同じシーケンスの再列挙や並行列挙は `InvalidOperationException` になり、元の読み取りと実行は互いに独立しています。

`Provider` はアダプター名、`RequestedModel` は提供元別のモデル上書きを含め、実際の要求に送る単一の明示的モデルを開始時に取得した値です。プリセット、プロファイル、サーバー側ルーティングで選択し、単一のモデルフィールドを送らない場合は `null` です（例: Perplexity の `Models` リスト）。実際の応答モデルである `Model` とは独立しています。 `Model` は提供元が最終ラウンドの応答で報告した実際のモデル ID で、なければ `null`。要求モデルで代用しません。`RoundCount` はライブラリの LLM ラウンド数で、個々のツールや提供元内部のエージェント段階の数ではありません。ラウンド数を報告しない独自提供元では `0` です。

`FinishReason` は `AIFinishReason`（`Unknown`、`Stop`、`MaxTokens`、`ToolCalls`、`ContentFilter`、`Other`）、`RawFinishReason` は提供元の終了値です。未提供なら `Unknown`/`null`。これらは成功時の情報です。ラウンド上限などの既存エラーは引き続き `Result` を失敗させ、利用者のキャンセルは `OperationCanceledException` になります。キャンセルを成功結果に変換しません。

`Text` は以前と同じく、ツール間の中間出力や追加指示以前の出力を含む全テキストを順に連結します。実行と後処理を待って完了します。`Citations` は完了時の出典で、実行中は `run.Citations` を読めます。引用位置は提供元の元コンテンツ部分内の位置で、連結テキスト全体の位置ではありません。

<a id="run-result-migration"></a>

**メジャー版への移行:** `AIRun.Result` は `Task<string>` から `Task<AIRunResult>` に変わります。文字列には `(await run.Result).Text` を使います。独自 `AIRun` はオーバーライドを変更し `AIRunResult` を構築し、利用側も再ビルドします。結果型は `Mythosia.AI.Models.Runs`、`TokenUsage` と `AIFinishReason` は `Mythosia.AI.Models.Streaming` にあります。`GetCompletionAsync` は `Task<string>`、`StructuredStreamRun<T>.Result` は `Task<T>` のままです。この例はMythosia.AI 8.0.0用で、最初の 7.1/3.1 Run パッケージ用ではありません。

## コールバックでテキストを表示する

チャット画面やコンソールで最初のテキストから表示すれば、長い回答が書かれる途中でもユーザーは内容を読み進められます。

```csharp
await using var run = await service
    .CreateRequest("文書を読んでレポートを作成してください。")
    .WithMaxRounds(10)
    .StartRunAsync(
        onText: text => Console.Write(text),
        cancellationToken: cancellationToken);

string answer = (await run.Result).Text;
```

ローカルツールは`Task<T>` / `ValueTask<T>`でオブジェクトを返し、注入された`CancellationToken`を受け取れます。`run.Cancel()`や開始トークンのキャンセルは協調するツールにも届きますが、読み取りの停止だけでは届きません。例外は失敗として記録します。キャンセル時は未開始の呼び出しをスキップし、トークンを無視する開始済みツールは後処理で待ちます。[結果・エラー・キャンセル](function-calling.md#tool-execution-contract)を参照してください。

`onText`は開始前に登録される省略可能な`Action<string>`です。テキストを順に受信するもので、ツールは実行しません。結果だけが必要なら省略できます。コールバックが例外を送出するとRunはキャンセルされ、`Result`は例外で失敗します。`onText`に`async`ラムダを渡すと`async void`になり、その処理やエラーをRunから待機できません。非同期の出力処理にはイベントストリームを使ってください。コールバックはUIスレッドへ自動的には切り替わりません。

`(await run.Result).Text`は、そのRunのテキストイベントを連結したものです。ツール呼び出し間の中間テキストや追加指示より前のテキストも含みます。2回目のモデルリクエストや、改めて書き直した回答ではありません。従来の完了時の返却動作が適している場合は、`GetCompletionAsync`を引き続き利用できます。

## テキスト・ツール・使用量のイベントを読む

文書検索や業務APIの呼び出し中は、テキストだけでは待ち時間の理由が伝わらないことがあります。種類付きのイベントからツールの動作を回答と併せて表示し、プロバイダーが使用量を返す場合は記録できます。

```csharp
await using var run = await service.StartRunAsync(
    "文書を検索して結果を説明してください。",
    cancellationToken: cancellationToken);

await foreach (var item in run.StreamAsync())
{
    switch (item.Type)
    {
        case StreamingContentType.Text:
            Console.Write(item.Content);
            break;
        case StreamingContentType.FunctionCall:
            Console.WriteLine("\n[ツールを呼び出しています]");
            break;
        case StreamingContentType.FunctionResult:
            Console.WriteLine("\n[ツールの結果を受信しました]");
            break;
        case StreamingContentType.Completion when item.Usage != null:
            Console.WriteLine($"\n[合計トークン数: {item.Usage.TotalTokens}]");
            break;
    }
}

string answer = (await run.Result).Text;
```

`run.StreamAsync()`は観測用の省略可能なキャンセルトークンを受け取り、プロンプトは受け取りません。`StartRunAsync`で開始済みの処理を観測します。登録した関数ハンドラーはライブラリ内部で実行されるため、表示イベントを受けて同じツールを再実行しないでください。テキストの表示設定によって登録済みツールが無効になることもありません。

開始時のコールバックと`run.StreamAsync()`は同じRunを同時に観測できます。イベントストリームの読み取り元は1つです。例えば`onText`でテキストを表示し、ストリームではツールイベントだけを処理すれば二重表示を防げます。コールバックの有無にかかわらず、未読イベントを最大1,024件保持します。範囲内であれば後から読み始めても先頭から取得できます。上限を超えるとストリームの観測は明示的に失敗しますが、コールバック、処理、`Result`は続行します。無制限の再生ログとして使わないでください。`Result`の完了を待つためにイベントを読み切る必要はありません。

Web や文書を検索する Run では、回答の出典も表示できます。[推論と検索のガイド](reasoning-and-search.md)に `WithWebSearch`、`WithFileSearch`、`run.Citations` と出典イベントの例があります。

## 非同期で出力を処理する

出力処理を非同期で行う場合は、`onText`を非同期にする代わりに、読み取りループ内で処理を待ちます。

```csharp
using var writer = new StreamWriter("report.txt");
await using var run = await service.StartRunAsync(
    "レポートを作成してください。", cancellationToken: cancellationToken);

await foreach (var item in run.StreamAsync())
{
    if (item.Type == StreamingContentType.Text && item.Content is string text)
        await writer.WriteAsync(text);
}
string answer = (await run.Result).Text;
```

## キャンセルとリソースの解放

- `await foreach`を途中で抜けるか、`run.StreamAsync(token)`にだけ渡したトークンをキャンセルすると、観測だけが終了し、処理は続きます。
- `run.Cancel()`、`StartRunAsync`に渡したトークン、実行中のRunの破棄は、実行をキャンセルします。
- `await using`により、`DisposeAsync()`は生成処理とプロバイダーの後処理を待ちます。キャンセル非対応のツールは完了まで時間がかかることがあり、破棄しても完了済みの操作は元に戻りません。
- 1つのサービスで同時に実行できる`StartRunAsync`タスクは1つです。重複した開始は拒否されます。独立した並行タスクには別のサービスを使い、Runの実行中に旧APIの呼び出しを混ぜたりサービス設定を変更したりしないでください。

Runはバックグラウンド実行の前に入力と適用待ちのリクエスト単位のポリシーを取り込みます。組み込みのテキスト・画像・音声コンテンツとメディアのバイト配列はコピーします。独自の`MessageContent`派生クラスは同じインスタンスを保持するため、Runが終わるまで変更しないでください。

取り込まれた`FunctionCallingPolicy.TimeoutSeconds`は、Runの準備とすべてのモデル・ツールラウンドを合わせた1つの期限を設定します。期限切れは`AIServiceException`として報告され、ユーザーによるキャンセルは結果をキャンセル状態にします。後処理では、キャンセル非対応のハンドラーの完了を引き続き待ちます。

## 実行中に別の指示を送る

プロジェクト計画の作成を始めた後で、ユーザーが「2週間で完了する計画にする必要がある」と気付くことがあります。追加指示（steering）を使えば、モデルが作業中でも新たな条件を送れます。長いタスクの途中で判明した修正や範囲変更に役立ちます。完了後の新しい質問は、通常どおり次のリクエストとして開始してください。

```csharp
await using var run = await service.StartRunAsync(
    "プロジェクト計画の草案を作成してください。",
    onText: text => Console.Write(text),
    cancellationToken: cancellationToken);

// Runの実行中に、UIの追加指示ハンドラーから呼び出します。
async Task SendUpdateAsync(string instruction)
{
    if (!run.CanSteer)
        throw new NotSupportedException("このRunは追加指示に対応していません。");

    await run.SteerAsync(instruction, cancellationToken);
}

string answer = (await run.Result).Text;
```

実行途中の追加指示は、Responses WebSocket接続を使用するGPT-6 Astraで利用できます。他のプロバイダーや非対応モデルでも通常のRunを利用できますが、`CanSteer`は`false`となり、追加指示は非対応として報告されます。通常の次のターンを黙って作成することはありません。`CanSteer`は、後で呼び出す時点でもRunが実行中であることまでは保証しません。

AstraのRunは専用ソケットを開きます。渡した`HttpClient`とメッセージハンドラーは引き続きHTTP呼び出しで使われ、このソケットには介在しません。独自のトランスポートが必要な場合は`OpenAIService.ConnectRunWebSocketAsync`をオーバーライドできます。

`SteerAsync`の成功は、サーバーが入力をキューに受け入れたことを意味し、モデルへの適用完了を意味しません。継続処理も同じRunで観測するか、その結果を待ちます。すでに配信したテキストや完了した操作は取り消されず、追加指示だけを理由に開始済みのツールがキャンセルされることもありません。ライブラリが同じ接続で継続処理とツール結果の対応付けを行います。OpenAIの[実行途中の追加指示ガイド](https://developers.openai.com/api/docs/guides/steering)と[WebSocketモード](https://developers.openai.com/api/docs/guides/websocket-mode)も参照してください。キュー内の入力は接続に属し、切断後も残るとは想定できません。受け入れ済みの指示を無条件で再送しないでください。

## ツールを使うタスクと旧エージェントメソッド

「返金ポリシーとこの注文の状況を確認して」といった質問には複数の情報源が必要です。文書検索と注文照会のツールを登録すれば、モデルが必要な呼び出しを選べます。ラウンド上限は、処理を終えるかエラーを報告するまでにツールを要求できる回数を制限します。

通常の関数呼び出しはすでに複数のモデル・ツールラウンドに対応しています。`StartRunAsync`も同じ登録済み関数と実行ポリシーを使います。別のエージェントモード、プランナー、`WithAgentic`スイッチは不要です。

`RunAgentAsync`と`RunAgentStreamAsync`は引き続き呼び出せますが、`[Obsolete]`警告が付きます。移行中も既存のシグネチャ、既定の`maxSteps = 10`、旧来のステップ上限エラーの動作を維持します。新しく書く呼び出しでは次を使ってください。

```csharp
await using var run = await service
    .CreateRequest("ポリシーを探し、注文を確認して結果を説明してください。")
    .WithMaxRounds(10)
    .StartRunAsync(
        onText: text => Console.Write(text),
        cancellationToken: cancellationToken);
string answer = (await run.Result).Text;
```

通常の`FunctionCallingPolicy.MaxRounds`の既定値は20です。旧エージェントの上限を維持するなら10を指定してください。`WithMaxRounds`は1回のリクエスト向けのポリシー上書きを設定し、`DefaultPolicy`は変更しません。開始前に設定します。一方、旧エージェントメソッドは現在の既定ポリシーを複製し、呼び出しごとの`maxSteps`を適用します。新しいRunは共通の実行エラー契約を使い、旧来の`AgentMaxStepsExceededException`/`PartialResponse`への変換は保証しません。その契約に依存している場合は、例外処理を移行するまで旧呼び出しを維持してください。

## RAG・MCP・パッケージの境界

- `RagEnabledService.StartRunAsync`は文字列または`Message`入力、`onText`、クエリ単位の`RagQueryOptions`、`streamOptions`、キャンセルに対応します。基になるRunの前に検索を行い、画像・音声・メタデータを保持し、会話履歴には元の入力を残して、拡張したテキストをリクエストコンテキストで送ります。この拡張は元のユーザー質問に固定されるため、後のツール結果や追加指示が元のRAGプロンプトに置き換わることはありません。返されたRunへの追加指示はモデルを更新しますが、RAG検索を自動的にはやり直しません。
- `WithAgenticRag`は引き続き検索ツールを登録します。`StartRunAsync`で実行すれば、モデルは必要に応じて後から検索できます。`WithMcpServerAsync`によるMCP登録も変わりません。共有MCP接続は、それを使うRunとは別に破棄してください。
- `IAIRunService`は`Mythosia.AI.Abstractions`の任意の機能インターフェイスであり、`IAIService`に必須メンバーは増えません。独自サービスでRAGからRunを開始するには`IAIRunService`を実装します。非対応サービスはRAGのインデックス作成が始まる前に拒否されます。
- RAGは引き続きAbstractionsに依存し、別パッケージのプロバイダーでは公開の完了メソッドのオーバーライドと利用可能なプロバイダー拡張ポイントを保持します。この変更でベクトルストア、文書ローダー、サーバー管理APIが非推奨になることはありません。

## Mythosia.AI 8への移行

| API | 現在の扱い |
| --- | --- |
| `GetCompletionAsync` / `GetCompletionAsync<T>` | インターフェイス・プロバイダー・RAG版を含め、公開APIとしてサポートします。 |
| `StartRunAsync` / `AIRun` | 共通の実行・制御APIです。 |
| `RunAgentAsync` / `RunAgentStreamAsync` | 非推奨警告が付きますが、互換性のため既存動作を保持します。 |
| 入力を受け取る`service.StreamAsync`とRAGの`StreamAsync` | 入力を受け取るサービス・RAGのStreamAsyncはv8でも公開です。新しい実行制御にはStartRunAsyncを使い、run.StreamAsync()は開始済みrunの出力だけを観測します。 |
| `run.StreamAsync()` | 既存タスクの出力を観測します。リクエスト入力は受け取りません。 |
| `BeginStream(...).As<T>()` / `StructuredStreamRun<T>` | 型付きストリーミングAPIを維持します。出力専用の`Stream()`は旧サービスのリクエストメソッドではありません。 |

[Mythosia.AI 8への移行](v8-migration.md).

Perplexity: [長い処理を継続する](perplexity.md).

[共通の対応定義でモデルの機能選択を構成する](model-capabilities.md).
