# 基本的なテキスト生成

設定をリクエストごとに分離し、共通設定から分岐するには[リクエストビルダー](request-building.md)を使います。`CreateRequest(...)`の後に`With...`をつなぎます。サービスのプロパティとfluentメソッドは従来の動作を維持します。

<a id="completion-cancellation"></a>

## 不要になった回答をキャンセルする

画面を閉じた、停止ボタンを押した、アプリの待機時間を超えた場合、回答はもう必要ないかもしれません。`CancellationToken`を渡すとクライアント側の通信と処理を中断し、不要なツール呼び出しや次のモデル呼び出しを防げます。完成した回答は引き続き`GetCompletionAsync`で受け取れます。キャンセルだけならRunは不要です。

### Before: 呼び出し元からキャンセルを渡さない

```csharp
string answer = await service.CreateRequest("この文書を要約してください。")
    .GetCompletionAsync();
```

### After: ユーザー操作または30秒後にキャンセル

```csharp
using System;
using System.Threading;

using var cancellation = new CancellationTokenSource(TimeSpan.FromSeconds(30));
try
{
    string answer = await service.CreateRequest("この文書を要約してください。")
        .GetCompletionAsync(cancellationToken: cancellation.Token);
    Console.WriteLine(answer);
}
catch (OperationCanceledException) when (cancellation.IsCancellationRequested)
{
    Console.WriteLine("キャンセルしました。");
}
```

実行中はトークンソースを保持し、停止ボタンや画面を閉じるイベントから`cancellation.Cancel()`を呼びます。この例は30秒後のキャンセルも予約します。呼び出し元のキャンセルは後処理後に`OperationCanceledException`として届きます。`CancellationTokenSource`で設定した期限もこれに含まれ、既存の`FunctionCallingPolicy.TimeoutSeconds`は従来のタイムアウトエラー動作を維持します。

サービスの文字列・`Message`オーバーロード、型付き完了、要求ビルダー、`MessageChain.SendAsync` / `SendOnceAsync`でトークンを受け取れます。省略する既存の呼び出しも使えます。以下は別の入口です。

```csharp
using System.Collections.Generic;
using System.Threading;
using Mythosia.AI.Extensions;

using var cancellation = new CancellationTokenSource();
CancellationToken token = cancellation.Token;

string answer = await service.GetCompletionAsync(
    "この文書を要約してください。", cancellationToken: token);

Dictionary<string, string> data = await service.GetCompletionAsync<Dictionary<string, string>>(
    "題名と著者をJSONで返してください。", cancellationToken: token);

string messageAnswer = await service.BeginMessage().AddText("この文書を要約してください。")
    .SendAsync(cancellationToken: token);

string oneOffAnswer = await service.BeginMessage().AddText("この文を翻訳してください。")
    .SendOnceAsync(cancellationToken: token);
```

トークンは要求の準備、HTTP送信・読み取り、協調するローカルツール、後続のモデル呼び出しに渡されます。キャンセルを検知すると待機中のツールと次のラウンドを省略します。後処理は記録されたツール呼び出しと結果の対応を維持するため、トークンを無視する実行中のツールで遅れることがあります。完了した操作や会話履歴は取り消しません。[ツールの契約](function-calling.md#tool-execution-contract)を参照してください。

サーバーの生成や課金の停止は保証しません。OpenAIは通常のResponsesで接続終了によるキャンセルを説明し、Googleはクライアントのみの中断で利用分は課金されると明記しています。[OpenAI Responses](https://developers.openai.com/api/docs/guides/background#limits) · [Google GenerateContentConfig.abortSignal](https://googleapis.github.io/js-genai/release_docs/interfaces/types.GenerateContentConfig.html#abortsignal)。バックグラウンドジョブ自体には明示的な`CancelAsync()`を使います。`WaitForCompletionAsync(cancellationToken: ...)`のキャンセルは待機だけを止めます。通常の完了要求をバックグラウンド実行には変換しません。[Perplexity](perplexity.md)を参照してください。

<a id="completion-cancellation-migration"></a>

この追加はMythosia.AI 8.0.0に含まれます。トークン省略や従来のprofile/context位置引数はソース上で有効ですが、利用側は再ビルドが必要です。独自の`IAIService`実装では両完了メソッドの末尾に`CancellationToken cancellationToken = default`を追加して伝播します。`AIService`派生プロバイダーは既存の`GetCompletionAsync(Message)` overrideを維持し、protectedの`RequestCancellationToken`を通信に渡します。ビルダーとRun自体にはこのインターフェース変更は不要でした。 文字列・profile/contextの完了、画像ヘルパー、`RunAgentAsync`など変更されたpublic virtualオーバーロードを再定義する派生クラスも、新しい`CancellationToken`を末尾に追加して伝播します。従来のシグネチャを維持するのは単一の`Message`を受け取るprovider overrideです。変更されたメソッドをデリゲートに直接渡すコードは、トークンを渡すか省略する明示的なラムダへの変更が必要な場合があります。

## 単発の質問

最もシンプルな使い方です — メッセージを送ってレスポンスを受け取るだけです:

```csharp
var response = await service.GetCompletionAsync("フランスの首都はどこですか？");
Console.WriteLine(response); // パリ
```

完成した回答だけを受け取る場合、`GetCompletionAsync`は引き続き適しています。表示を逐次更新したり、実行中に停止・追加指示を行ったりする場合は[Runの利用ガイド](execution-api-transition.md)を参照してください。

## システムプロンプト

モデルにペルソナや指示を与えるシステムプロンプトを設定します:

```csharp
service.SystemMessage = "あなたは簡潔なアシスタントです。一文で答えてください。";

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
service.ActivateChat.ClearMessages();
```

## メッセージの手動構築

`MessageBuilder`を使ってメッセージを明示的に構築します:

```csharp
using Mythosia.AI.Builders;

var message = MessageBuilder.Create().AddText("このテキストを要約してください: ...")
    .Build();

var response = await service.GetCompletionAsync(message);
```

## マルチモーダル（画像入力）

ビジョンをサポートするプロバイダーはテキストと一緒に画像コンテンツを受け取ることができます:

```csharp
var imageBytes = await File.ReadAllBytesAsync("diagram.png");

var message = MessageBuilder.Create().AddText("この図は何を示していますか？")
    .AddImage(imageBytes, "image/png")
    .Build();

var response = await service.GetCompletionAsync(message);
```

グラフ・スクリーンショットの分析、ローカルツール、素早い回答後の詳しい検証には [DeepSeek Flash](providers.md#deepseek-deepseekservice) (`AIModels.DeepSeek.Flash`, V4.1 Flash) を使えます。推論は既定で無効です。`WithDeepSeekReasoning(...)` またはリクエストごとの `WithReasoning(...)` で有効にします。

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
    model: AIModels.OpenAI.Gpt4_1
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

Perplexity: [Agent プリセットで回答する / 出典、画像、構造化された回答](perplexity.md).
