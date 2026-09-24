# 生成パラメータ

> Grok 4.7 は未リリースの追加機能です。[モデル選択・推論・処理速度](providers.md#grok-47)を参照してください。

設定をリクエストごとに分離し、共通設定から分岐するには[リクエストビルダー](request-building.md)を使います。`CreateRequest(...)`の後に`With...`をつなぎます。サービスのプロパティとfluentメソッドは従来の動作を維持します。

## サービスの既定値と互換メソッド

すべてのAIサービスインスタンスはこれらのプロパティを提供します:

```csharp
service.Temperature = 0.7f;        // ランダム性 [0, 2]. 低いほど決定論的
service.TopP = 1.0f;               // 核サンプリング閾値
service.MaxTokens = 1024;          // 最大出力トークン数
service.FrequencyPenalty = 0.0f;   // 繰り返しトークンペナルティ
service.PresencePenalty = 0.0f;    // 既出トークンペナルティ
```

GPT-6 Astra は `temperature` と `top_p` をサポートしません。共通プロパティやリクエストプロファイルで設定しても、Mythosia は両方を送信時に省略します。最大出力は 128,000 トークンです。[GPT-6 の設定](providers.md)も参照してください。

GPT-6 Sol/Luna は `ReasoningLevel.None` の場合だけ `temperature` と `top_p` を送信し、それ以外では省略します。Astra は `None` に対応しません。[モデルの選択と設定](providers.md#gpt-6-sol-luna)を参照してください。


下書きと検証で推論の深さを変えたい場合は、[共通の推論設定](reasoning-and-search.md)を使えます。キャッシュを保持する変更と通常のリクエスト単位の指定の違いも説明しています。

## フルーエント拡張メソッド

`this`を返すのでチェーンが可能です:

```csharp
var service = new OpenAIService(apiKey, http)
    .WithSystemMessage("あなたは役立つアシスタントです。")
    .WithTemperature(0.3f)
    .WithMaxTokens(2048)
    .WithStatelessMode(true);
```

| メソッド | 説明 |
|---------|------|
| `.WithSystemMessage(string)` | システムプロンプトを設定 |
| `.WithTemperature(float)` | [0, 2]の範囲に制限 |
| `.WithMaxTokens(uint)` | 最大出力トークン数 |
| `.WithStatelessMode(bool)` | 会話履歴の蓄積を無効化 |

## ステートレスモード

有効にすると各リクエストが独立します — 会話履歴は送信も保存もされません:

```csharp
service.StatelessMode = true;

// 同等:
var service = new OpenAIService(apiKey, http).WithStatelessMode(true);
```

履歴オーバーヘッドが不要な単発クエリに便利です。

## 単発クエリ

会話履歴に影響を与えることなく単一クエリを実行します:

```csharp
// テキストプロンプト
string response = await service.AskOnceAsync("2+2は何ですか？");

// メッセージ（マルチモーダル）
string response = await service.AskOnceAsync(message);

// ファイルパスの画像
string response = await service.AskOnceWithImageAsync("説明してください", "photo.jpg");
```

## モデルの切り替え

会話履歴を保持しながらセッション途中でモデルを変更します:

```csharp
service.ChangeModel(AIModels.OpenAI.Gpt4_1);

// または拡張メソッドで — 履歴をクリアして新しく開始:
service.StartNewConversation(AIModels.Anthropic.ClaudeSonnet4_6);
```

## 複数の会話管理

単一のサービスインスタンスが複数の独立した会話スレッドを持てます:

```csharp
// 新しい会話ブロックを開始
service.AddNewChat();
var chat1 = service.ActivateChat;

// 別のブロックに切り替え
service.SetActivateChat(chat2Id);

// すべてのブロックにアクセス
var allChats = service.ChatRequests;
```

## 会話状態の確認

最後のAI応答や現在のセッションの簡易サマリーを取得します:

```csharp
// 最後のAI応答を取得（なければnull）
string? lastReply = service.GetLastAssistantResponse();

// 現在のサービス状態のテキストサマリー
string info = service.GetConversationSummary();
// → Model: gpt-4o-mini
// → Messages: 12
// → Stateless Mode: False
// → System: You are a helpful assistant.
```

## サービス設定のコピー

会話履歴なしで別のサービスインスタンスのすべての設定を複製します:

```csharp
var newService = new AnthropicService(apiKey, http);
newService.CopyFrom(existingService);
```

Perplexity: [調査範囲とツールを制御する](perplexity.md).
