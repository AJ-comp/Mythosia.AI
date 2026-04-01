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

## ストリーミング前の会話要約

自動要約ポリシーはストリーミング中にはトリガーされません。`StreamAsync`の前に明示的に呼び出します:

```csharp
await service.ApplySummaryPolicyIfNeededAsync();

await foreach (var chunk in service.StreamAsync("会話を続けましょう..."))
    Console.Write(chunk.Content);
```
