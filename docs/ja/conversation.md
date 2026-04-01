# 会話管理

## 会話履歴の仕組み

`GetCompletionAsync`または`StreamAsync`を呼び出すたびにサービスの内部メッセージリストに追加されます。そのため、モデルは以前のすべてのターンのコンテキストを持ちます。

```csharp
await service.GetCompletionAsync("私の好きな色は青です。");
var reply = await service.GetCompletionAsync("私の好きな色は何ですか？");
// → "あなたの好きな色は青です。"
```

新しく始めるには:

```csharp
service.ClearMessages();
```

## 要約ポリシー

長い会話はトークンを消費し、最終的にモデルのコンテキスト制限を超えます。`SummaryConversationPolicy`は閾値に達すると古いメッセージを自動的に要約します。

### メッセージ数でトリガー

```csharp
service.ConversationPolicy = SummaryConversationPolicy.ByMessage(
    triggerCount: 20,   // 履歴が20件を超えたら要約
    keepRecentCount: 5  // 最近の5件はそのまま保持
);
```

### トークン数でトリガー

```csharp
service.ConversationPolicy = SummaryConversationPolicy.ByToken(
    triggerTokens: 3000,    // トークン使用量が3000を超えたら要約
    keepRecentTokens: 1000  // 最近1000トークン分のメッセージを保持
);
```

設定すると`GetCompletionAsync`で自動的に要約が発生します。他の変更は不要です。

### ストリーミング

`StreamAsync`中は要約が自動的にトリガーされません。先に明示的に呼び出します:

```csharp
await service.ApplySummaryPolicyIfNeededAsync();

await foreach (var chunk in service.StreamAsync("会話を続けましょう..."))
    Console.Write(chunk.Content);
```

## 要約の保存と復元

セッション間で要約を永続化し、再起動後もモデルがコンテキストを保持するようにします:

```csharp
// 保存
string saved = service.ConversationPolicy.CurrentSummary;
// → データベース、ファイルなどに保存

// 新しいセッションで復元
service.ConversationPolicy.LoadSummary(saved);
```
