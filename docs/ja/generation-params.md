# 生成パラメータ

## 共通プロパティ

すべてのAIサービスインスタンスはこれらのプロパティを提供します:

```csharp
service.Temperature = 0.7f;        // ランダム性 [0, 2]. 低いほど決定論的
service.TopP = 1.0f;               // 核サンプリング閾値
service.MaxTokens = 1024;          // 最大出力トークン数
service.FrequencyPenalty = 0.0f;   // 繰り返しトークンペナルティ
service.PresencePenalty = 0.0f;    // 既出トークンペナルティ
service.MaxMessageCount = 20;      // 会話ウィンドウサイズ（非推奨 — v7.0 で削除）
```

> **非推奨:** `MaxMessageCount`（メッセージ数ベースのスライディングウィンドウ）は廃止予定で、v7.0 で削除されます — コンテキスト管理は `ConversationPolicy` によるトークンベースのみになります。削除までの間、このウィンドウは直近のユーザーメッセージを決して破棄しないことが保証されているため、エージェント的なツール実行中に処理対象のクエリが失われることはありません。

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
var chat1 = service.AddNewChat();

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
