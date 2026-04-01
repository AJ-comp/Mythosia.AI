# リクエストプロファイルとコンテキスト

サービスのグローバル状態を変更せずに、単一リクエストの設定を上書きできます。

## AIRequestProfile

リクエストごとのパラメーター上書きのコレクションです。`GetCompletionAsync`または`StreamAsync`に渡します:

```csharp
var profile = new AIRequestProfile
{
    Temperature = 0.1f,
    MaxTokens = 256,
    Stateless = true,        // このリクエストを履歴に追加しない
    DisableFunctions = true, // このリクエストで関数呼び出しをスキップ
    DisableReasoning = true  // このリクエストで推論をスキップ
};

var response = await service.GetCompletionAsync("要約してください。", profile);
```

### 事前定義プロファイル

一般的なユースケース向けの2つの組み込みプロファイル:

```csharp
// 低温度、小さなトークン予算、ステートレス — クエリ書き換え用
var response = await service.GetCompletionAsync(query, RequestProfiles.QueryRewrite);

// やや高い温度、適度なトークン — 要約用
var response = await service.GetCompletionAsync(text, RequestProfiles.Summarization);
```

## AIRequestContext

サービスのシステムメッセージや履歴に触れずに、単一リクエストに追加コンテンツを注入します:

```csharp
var context = new AIRequestContext
{
    SystemMessagePrefix = "今日の日付は2026-03-31です。\n",
    SystemMessageSuffix = "\n常に日本語で答えてください。",
    AdditionalMessages = new List<Message>
    {
        MessageBuilder.User("参考文書: ...").Build()
    }
};

var response = await service.GetCompletionAsync("質問に答えてください。", context);
```

### RequestMessageOverride

この呼び出しのみリクエストメッセージを完全に置き換えます:

```csharp
var context = new AIRequestContext
{
    RequestMessageOverride = MessageBuilder
        .User("検索されたコンテキストに基づいて再構成されたプロンプト...")
        .Build()
};

await service.GetCompletionAsync(originalPrompt, context);
```

## プロファイルとコンテキストの組み合わせ

両方を一緒に渡すことができます:

```csharp
var response = await service.GetCompletionAsync(
    prompt,
    profile: RequestProfiles.QueryRewrite,
    context: new AIRequestContext { SystemMessageSuffix = "\n簡潔に答えてください。" }
);
```
