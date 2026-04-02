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

### トークン＋メッセージ数の同時トリガー（OR条件）

トークン制限またはメッセージ数の**いずれかを超過**した時点で要約をトリガーします:

```csharp
service.ConversationPolicy = SummaryConversationPolicy.ByBoth(
    triggerTokens: 4000,
    triggerCount: 30,
    keepRecentTokens: 1300,  // オプション、デフォルト triggerTokens / 3
    keepRecentCount: 7       // オプション、デフォルト triggerCount / 4
);
```

設定すると`GetCompletionAsync`で自動的に要約が発生します。他の変更は不要です。

### 動作の仕組み

1. 各補完呼び出しの前に、ポリシーが会話が設定された閾値を超えているか確認します。
2. トリガーされると、古いメッセージをステートレスLLM呼び出しで簡潔に要約します。
3. 要約はシステムメッセージのプレフィックスとして注入され、モデルは以前のコンテキストとして認識します。
4. 最近のメッセージ（`KeepRecentCount`または`KeepRecentTokens`で制御）はそのまま保持されます。

トークンベースのトリガーを使用する場合、ポリシーはローカル推定の代わりに**APIから報告された実際の入力トークン数**（最後のストリーミングレスポンスから取得）を自動的に使用し、正確なトリガー判断を保証します。

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
