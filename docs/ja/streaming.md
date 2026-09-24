# ストリーミング

> Grok 4.7 は未リリースの追加機能です。[モデル選択・推論・処理速度](providers.md#grok-47)を参照してください。

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
