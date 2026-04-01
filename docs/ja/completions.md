# 基本的な補完

## シングルターン

最もシンプルな使い方 — メッセージを送信してレスポンスを受け取ります:

```csharp
var response = await service.GetCompletionAsync("フランスの首都はどこですか？");
Console.WriteLine(response); // パリ
```

## システムプロンプト

モデルにペルソナや指示を与えるシステムプロンプトを設定します:

```csharp
service.SystemPrompt = "あなたは簡潔なアシスタントです。一文で答えてください。";

var response = await service.GetCompletionAsync("再帰を説明してください。");
```

## マルチターン会話

メッセージは自動的に蓄積されます。`GetCompletionAsync`を呼び出すたびに会話履歴に追加されます:

```csharp
await service.GetCompletionAsync("私の名前はアリスです。");
var response = await service.GetCompletionAsync("私の名前は何ですか？");
// → "あなたの名前はアリスです。"
```

会話履歴をクリアするには:

```csharp
service.ClearMessages();
```

## メッセージの手動構築

`MessageBuilder`を使ってメッセージを明示的に構築します:

```csharp
using Mythosia.AI.Builders;

var message = MessageBuilder.User("このテキストを要約してください: ...")
    .Build();

var response = await service.GetCompletionAsync(message);
```

## マルチモーダル（画像入力）

ビジョンをサポートするプロバイダーはテキストと一緒に画像コンテンツを受け取ることができます:

```csharp
var imageBytes = await File.ReadAllBytesAsync("diagram.png");

var message = MessageBuilder.User("この図は何を示していますか？")
    .WithImage(imageBytes, "image/png")
    .Build();

var response = await service.GetCompletionAsync(message);
```

## 出力長と温度の制御

```csharp
service.MaxTokens = 512;
service.Temperature = 0.2f;  // 低いほど決定論的
```
