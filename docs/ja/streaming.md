# ストリーミング

既定のコールバック式ストリーミングは列挙の早期終了時に生成処理をキャンセルして待機します。Run のキャンセル、タイムアウト、観測コールバックの失敗、`DisposeAsync` はプロバイダーの後処理完了後に `Result` と実行ガードを解放します。非協調的な処理は完了を遅らせ得ます。観測と後処理の例外は両方保持します。`ContextRecoveryMaxRetries` は取得済みの値を使います。`run.StreamAsync()` の観測終了だけでは Run をキャンセルしません。 成功した SSE 応答の本文取得には別の[キャンセル制限](#sse-acquisition-cancellation-limitation)があります。

> Claude Sonnet 5.5: Mythosia.AI 8.2.0 / Abstractions 4.2.0 が必要です。[設定と移行](providers.md#claude-sonnet-55)

Adaptive では `ClaudeThinkingDisplay.Updates` でツールの進捗、`Summarized` で推論要約を要求します。`StreamingContentType.Reasoning`、通常の完了では `LastThinkingContent` を確認します。Adaptive ヘルパーの display 引数の既定値は `Summarized` で、未設定時とは異なります。`between_tools` は進捗を自動で返します。一定間隔の通知は保証されません。

> Grok 4.7: Mythosia.AI 8.1.0 / Abstractions 4.1.0 が必要です。 [モデル選択・推論・処理速度](providers.md#grok-47)

回答・使用量・出典をまとめて取得するには、`await run.Result` が返す `AIRunResult` を使用します。文字列は `result.Text` で取得でき、ストリームを読む必要はありません。Mythosia.AI 8.0.0 の API 変更です。`GetCompletionAsync` と `StructuredStreamRun<T>.Result` の戻り値型は維持します。 [Run の結果と移行](execution-api-transition.md#run-result).


設定をリクエストごとに分離し、共通設定から分岐するには[リクエストビルダー](request-building.md)を使います。`CreateRequest(...)`の後に`With...`をつなぎます。サービスのプロパティとfluentメソッドは従来の動作を維持します。

長い回答を最後まで待たずに読み始められるよう、到着したテキストを順に表示します。停止ボタンやツール状況も同じ処理に結び付ける場合は、`StartRunAsync`で開始して`run.StreamAsync()`を読みます。[Runの利用ガイド](execution-api-transition.md)にコールバックとキャンセルの例があります。

```csharp
await using var run = await service.StartRunAsync(
    "文書を要約してください。",
    cancellationToken: cancellationToken);

await foreach (var item in run.StreamAsync())
{
    if (item.Type == StreamingContentType.Text)
        Console.Write(item.Content);
}

string answer = (await run.Result).Text;
```

**Claude のエラー応答:** ストリーミングや Run で HTTP エラー本文の読み取りが停止していても、キャンセルとリクエストポリシーのタイムアウトが適用されます。Run の後処理が完了すれば、同じサービスで次の Run を開始できます。呼び出し元のキャンセルは `OperationCanceledException`、ポリシーのタイムアウトは `AIServiceException` になります。キャンセルはローカルの通信と協調的な後処理を制御し、プロバイダーの処理や課金の停止を保証しません。

**Claude 応答の後処理:** Claude のストリーミングと Run は、取得済みの HTTP 応答本文の非同期後処理を待機します。非同期の破棄が必要なカスタムストリームも対象です。その後の応答やコンテンツの破棄で例外が発生しても、正常完了、元の読み取りエラー、キャンセルを置き換えることはありません。元の応答やコンテンツの破棄自体は引き続き試みます。

**HTTP タイムアウト:** 共通のストリーミングラウンド経路を使うテキスト・コンテンツ・コールバックストリーミングと Run では、呼び出し元のキャンセルもリクエストポリシーのタイムアウトも発生していない場合、識別可能な `HttpClient.Timeout`（内部に `TimeoutException` を持つ `TaskCanceledException`）が `AIServiceException` になります。`InnerException` に元の通信例外が保持されるため、`run.Result` はタイムアウトの原因を保持した失敗になります。呼び出し元のキャンセル、ポリシーのタイムアウト、その他の通信キャンセルの動作は変わりません。

<a id="sse-acquisition-cancellation-limitation"></a>

## 既知の制限: 成功した SSE 応答の本文取得

HTTP 200 SSE で、カスタムハンドラーが本文をバッファリングする `HttpContent` ラッパーを使う場合、本文ストリームの取得と後処理の開始前に `ReadAsStreamAsync` が停止することがあります。呼び出し元のキャンセルやリクエストポリシーのタイムアウト後も、取得が終わるまで `run.Result` が未完了、応答が未破棄、サービスの実行ガードが保持された状態になり、次の Run は実行中として拒否されます。この問題は未修正で、後処理が遅い場合とは異なります。既定の `SocketsHttpHandler` は検証したシナリオを通過し、同じラッパーでも HTTP エラー本文のキャンセルは成功しました。本文をバッファリングするラッパーを避け、通常のストリーミングコンテンツを使用してください。Claude のネイティブ Web 検索には別の[継続制限](providers.md#claude-native-continuation-limitation)があります。

入力を受け取るサービス・RAGのStreamAsyncはv8でも公開です。新しい実行制御にはStartRunAsyncを使い、run.StreamAsync()は開始済みrunの出力だけを観測します。

## 基本ストリーミング

`StreamAsync`で、生成されたテキストを順次受け取れます。

```csharp
await foreach (var token in service.StreamAsync("物語を聞かせてください"))
{
    Console.Write(token);
}
```

## コンテンツタイプを含むストリーミング

`StreamAsync`はテキストとタイプ情報を含む`StreamingContent`オブジェクトを返すことができます:

```csharp
await foreach (var content in service.StreamAsync("量子コンピューティングを説明してください", StreamOptions.Default))
{
    Console.Write(content.Content);
}
```

## 推論ストリーミング

OpenAI、Claude、Gemini、Grok、DeepSeek Flash は同じストリーミング形式で提供元の推論を返します。サービスまたはリクエストで推論を有効にし、`StreamOptions.WithReasoning()` で観察します：

```csharp
using Mythosia.AI.Models.Streaming;

await foreach (var content in service.StreamAsync("解いてください: 2x + 5 = 13", new StreamOptions().WithReasoning()))
{
    if (content.Type == StreamingContentType.Reasoning)
        Console.Write($"[思考中] {content.Content}");
    else if (content.Type == StreamingContentType.Text)
        Console.Write(content.Content);
}
```

Gemini 3.7/3.8 Flash は既存のストリーミングと Run のイベントを使います。`StreamingContentType.Reasoning` はプロバイダーが返した要約や進捗を含み、内部推論の完全な開示を保証しません。`StreamOptions.WithReasoning()` はこの出力を選び、サービスの `WithReasoning(ReasoningLevel...)` は推論レベルを設定します。

Grok 4.6 もプロバイダーが任意に提供する推論要約を同じイベントで返します。ストリーム設定は表示対象を選び、`WithReasoning(ReasoningLevel...)` は一つのタスクの推論レベルを選びます。要約がなくても推論が無効とは限りません。[Grok の設定](providers.md#xai-xaiservice)を参照してください。

DeepSeek Flash は推論を有効にすると、同じイベントで `reasoning_content` を返します。`StreamOptions.WithReasoning()` は観察、`WithDeepSeekReasoning(...)` やサービスの `WithReasoning(...)` は推論を制御します。[DeepSeek 設定](providers.md#deepseek-deepseekservice)を参照してください。

## 構造化出力と組み合わせたストリーミング

リアルタイムでテキストをストリーミングしながら、完了後にデシリアライズされたオブジェクトを取得します:

```csharp
var run = service.BeginStream(prompt).As<MyDto>();

// トークンが到着するたびにUIにストリーミング
await foreach (var chunk in run.Stream())
    Console.Write(chunk);

// ストリーミング完了後にパースされた結果を取得
MyDto result = await run.Result;
```

## トークン使用量

ストリーミングが完了すると、最後の`Completion`イベントに詳細な使用量メトリクスを含む`TokenUsage`オブジェクトが含まれます:

```csharp
await foreach (var content in service.StreamAsync("量子コンピューティングを説明してください", StreamOptions.Default))
{
    if (content.Type == StreamingContentType.Text)
        Console.Write(content.Content);

    if (content.Type == StreamingContentType.Completion && content.Usage != null)
    {
        Console.WriteLine($"\n入力トークン:  {content.Usage.InputTokens}");
        Console.WriteLine($"出力トークン: {content.Usage.OutputTokens}");
        Console.WriteLine($"合計トークン: {content.Usage.TotalTokens}");
    }
}
```

### TokenUsageプロパティ

| プロパティ | 説明 |
|---|---|
| `InputTokens` | 入力/プロンプトのトークン数 |
| `OutputTokens` | 出力応答のトークン数 |
| `TotalTokens` | 入力 + 出力 |
| `CachedInputTokens` | キャッシュから提供されたトークン（コスト削減） |
| `CacheCreationTokens` | キャッシュに書き込まれたトークン（Anthropic） |
| `ReasoningTokens` | 内部推論に使用されたトークン |
| `CacheHitRatio` | キャッシュヒット率（0.0–1.0） |
| `VisibleOutputTokens` | 推論を除いた出力トークン |

### キャッシュ効率の確認

```csharp
if (content.Usage?.HasCacheActivity == true)
{
    Console.WriteLine($"キャッシュヒット率: {content.Usage.CacheHitRatio:P1}");
    Console.WriteLine($"非キャッシュ入力: {content.Usage.NonCachedInputTokens}");
}
```

## StreamOptionsプリセット

`StreamOptions`はストリームが返す内容を制御するプリセットとFluentビルダーを提供します:

```csharp
// フル機能 — メタデータ、関数呼び出し、推論
await foreach (var c in service.StreamAsync("プロンプト", StreamOptions.FullOptions))
    Console.Write(c.Content);

// 最小オーバーヘッド — テキストのみ、メタデータなし
await foreach (var c in service.StreamAsync("プロンプト", StreamOptions.Minimal))
    Console.Write(c.Content);

// 関数呼び出しシナリオ
await foreach (var c in service.StreamAsync("プロンプト", StreamOptions.WithFunctions))
{ /* Text, FunctionCall, FunctionResult, Completionを処理 */ }
```

カスタム組み合わせ用のFluentビルダー:

```csharp
var options = new StreamOptions()
    .WithReasoning()       // 思考過程を含む
    .WithMetadata()        // Completionにモデル情報を含む
    .WithFunctionCalls();  // ストリーム中の関数呼び出しを有効化
```

表示済みのチャンクは、`run.Result` が成功するまで暫定的な出力として扱ってください。共通の OpenAI 互換ストリーミング経路と DeepSeek のストリーミング経路では、明示的な終了後に新しいテキスト・推論・ツールデータが届く場合や、終了理由が変わる場合に失敗します。`run.Result` は例外をスローし、失敗したラウンドは会話履歴に保存されず、そのツールも実行されません。この失敗処理は、以前のラウンドや外部ですでに実行された操作を元に戻すものではありません。 最初の終了イベントに最後の差分が含まれる場合と、その後に使用量情報のみが届く場合は許可されます。

## ステートレスストリーミング（StreamOnceAsync）

会話履歴に影響を与えずにレスポンスをストリーミングします — `AskOnceAsync`のストリーミング版です:

```csharp
await foreach (var chunk in service.StreamOnceAsync("これをフランス語に翻訳してください"))
    Console.Write(chunk);
```

マルチモーダル入力用の`Message`オーバーロードもサポートしています:

```csharp
var message = MessageBuilder.Create().AddText("これを説明してください").AddImage("photo.jpg").Build();

await foreach (var chunk in service.StreamOnceAsync(message))
    Console.Write(chunk);
```

## ストリーミング前の会話要約

自動要約ポリシーはストリーミング中にはトリガーされません。`StreamAsync`の前に明示的に呼び出します:

```csharp
await service.ApplySummaryPolicyIfNeededAsync();

await foreach (var chunk in service.StreamAsync("会話を続けましょう...", StreamOptions.Default))
    Console.Write(chunk.Content);
```

Perplexity: [長い処理を継続する / 引用は Web 検索結果や他の提供元の出典を示します。位置情報は個別のレスポンスとコンテンツ内を指し、連結済みの Run 結果内の位置ではありません。表示や検証には URL とタイトルを保持します。出典があることだけで、すべての生成内容が検証されたとは限りません。](perplexity.md).
