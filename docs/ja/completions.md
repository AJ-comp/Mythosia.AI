# 基本的なテキスト生成

## 単発の質問

最もシンプルな使い方です — メッセージを送ってレスポンスを受け取るだけです:

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

## クイック質問（静的API）

サービスインスタンスを作成せずに一行で質問できます。モデル名からプロバイダーが自動検出されます:

```csharp
string answer = await AIService.QuickAskAsync(
    apiKey: "sk-...",
    prompt: "フランスの首都は？",
    model: AIModels.OpenAI.Gpt4oMini  // デフォルト
);
```

画像バリアント:

```csharp
string description = await AIService.QuickAskWithImageAsync(
    apiKey: "sk-...",
    prompt: "この画像を説明してください",
    imagePath: "photo.jpg",
    model: AIModels.OpenAI.Gpt4Vision
);
```

## 画像便利メソッド

`MessageBuilder`なしで画像を分析します — ファイル読み込みとMIMEタイプの判別は自動的に処理されます:

```csharp
// ファイルパスから
var response = await service.GetCompletionWithImageAsync(
    "この図は何を示していますか？", "diagram.png");

// URLから
var response = await service.GetCompletionWithImageUrlAsync(
    "この写真を説明してください", "https://example.com/photo.jpg");
```

## 最後のメッセージを再試行

最後のAI応答を削除し、最後のユーザーメッセージを再送信します:

```csharp
string regenerated = await service.RetryLastMessageAsync();
```

前の応答が不満足な場合、モデルに再試行させることができます。

## トークンカウント

リクエストを送信する前にトークン使用量を推定します。**すべてのプロバイダー**で利用可能です:

```csharp
// 現在の会話履歴のトークン数を計算
uint conversationTokens = await service.GetInputTokenCountAsync();

// 特定プロンプトのトークン数を計算
uint promptTokens = await service.GetInputTokenCountAsync("プロンプト内容");
```

OpenAIおよびほとんどのプロバイダーはローカルのTikTokenベース推定を使用します。AnthropicとGoogleは正確な結果のためにネイティブトークンカウントAPIを呼び出します。

## Fluentメッセージチェーン

`BeginMessage()`は、テキスト・画像・ストリーミング・ポリシー設定を一つのチェーンでビルドして送信するFluent APIを提供します:

```csharp
// テキスト + 画像 → 送信
string response = await service.BeginMessage()
    .AddText("この図は何を示していますか？")
    .AddImage("diagram.png")
    .SendAsync();

// ワンショットクエリ（会話履歴に影響なし）
string answer = await service.BeginMessage()
    .AddText("これを韓国語に翻訳してください")
    .SendOnceAsync();

// ストリーミング
await service.BeginMessage()
    .AddText("春についての詩を書いてください")
    .StreamAsync(chunk => Console.Write(chunk));

// カスタムタイムアウトとポリシー
string result = await service.BeginMessage()
    .AddText("この画像を分析してください")
    .AddImageUrl("https://example.com/photo.jpg")
    .WithHighDetail()
    .WithTimeout(90)
    .SendAsync();
```

`StreamAsync()`は`IAsyncEnumerable`もサポートしています:

```csharp
await foreach (var chunk in service.BeginMessage().AddText("物語を聞かせてください").StreamAsync())
    Console.Write(chunk);
```

## 出力長と温度の制御

```csharp
service.MaxTokens = 512;
service.Temperature = 0.2f;  // 低いほど決定論的
```
