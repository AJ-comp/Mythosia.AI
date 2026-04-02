# ストリーミング

## 基本ストリーミング

`StreamAsync`を使用してトークンが生成されるたびに受信します:

```csharp
await foreach (var token in service.StreamAsync("物語を聞かせてください"))
{
    Console.Write(token);
}
```

## コンテンツタイプを含むストリーミング

`StreamAsync`はテキストとタイプ情報を含む`StreamingContent`オブジェクトを返すことができます:

```csharp
await foreach (var content in service.StreamAsync("量子コンピューティングを説明してください"))
{
    Console.Write(content.Content);
}
```

## 推論ストリーミング

推論機能を持つすべてのプロバイダー（OpenAI、Claude、Gemini、Grok、DeepSeek）は同じパターンを共有します。推論を有効にした`StreamOptions`を渡します:

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

`StreamingContentType.Reasoning`はモデルの内部推論プロセスを含み、`StreamingContentType.Text`は最終回答を含みます。

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
await foreach (var content in service.StreamAsync("量子コンピューティングを説明してください"))
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
| `OutputTokens` | 出力/補完のトークン数 |
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

await foreach (var chunk in service.StreamAsync("会話を続けましょう..."))
    Console.Write(chunk.Content);
```
