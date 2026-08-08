# AIRequestContext

## 概要

`AIRequestContext`は、**モデルが見る内容を単一リクエストに対してのみ変更**します — 追加の指示の注入、参考文書の追加、またはユーザーメッセージの完全な置き換え — サービスのシステムメッセージや会話履歴を永続的に変更せずに。

## 従来の方法の課題

関連ドキュメントを検索してプロンプトに含める必要があるRAGパイプラインを考えてみましょう。`AIRequestContext`**なし**ではシステムメッセージを直接変更する必要があります:

```csharp
// ❌ AIRequestContextなし — システムメッセージを汚染
var originalSystem = service.SystemMessage;

service.SystemMessage = originalSystem +
    $"\n\n以下のコンテキストを使って回答してください:\n{retrievedDocs}";

var answer = await service.GetCompletionAsync(userQuestion);

// 復元 — しかしこのコンテキストは会話履歴にも残ってしまう
service.SystemMessage = originalSystem;
```

この方法の問題点:

- 検索されたコンテキストが**会話履歴に漏れます** — 以降のリクエストでも見えてしまいます
- システムメッセージを復元しても履歴の汚染は元に戻せません
- マルチユーザーウェブアプリで共有状態を変更すると競合状態が発生します

`AIRequestContext`を**使えば**、注入は正確に1つのリクエストにのみ適用されます:

```csharp
// ✅ AIRequestContext使用 — クリーンで、スコープが限定され、副作用なし
var answer = await service.GetCompletionAsync(userQuestion,
    context: new AIRequestContext
    {
        SystemMessageSuffix = $"\n\n以下のコンテキストを使って回答してください:\n{retrievedDocs}"
    });
```

システムメッセージはこの1回の呼び出しでのみ変更されます。次のリクエストは元のシステムメッセージを見ます。クリーンアップは不要です。

## 利用可能なプロパティ

### SystemMessagePrefix

このリクエストのみ、システムメッセージの先頭にテキストを追加します:

```csharp
var context = new AIRequestContext
{
    SystemMessagePrefix = "今日の日付は2026-03-31です。\n"
};

var response = await service.GetCompletionAsync("今日は何曜日ですか？", context: context);
```

**使用するタイミング:** リクエストごとに変わる動的メタデータ（日付、ユーザーのタイムゾーン、セッション情報）を注入する場合。

### SystemMessageSuffix

このリクエストのみ、システムメッセージの末尾にテキストを追加します:

```csharp
var context = new AIRequestContext
{
    SystemMessageSuffix = "\n常に日本語で答えてください。"
};

var response = await service.GetCompletionAsync("Hello!", context: context);
```

**使用するタイミング:** リクエストごとの行動指示、RAGコンテキスト、または言語設定を追加する場合。

### AdditionalMessages

このリクエストのみ、会話に追加メッセージを挿入します — 参考文書やfew-shotの例の注入に便利です:

```csharp
var context = new AIRequestContext
{
    AdditionalMessages = new List<Message>
    {
        MessageBuilder.Create().AddText("参考文書: 返金ポリシーは30日以内の返品を許可しています。").Build()
    }
};

var response = await service.GetCompletionAsync("返金対象ですか？", context: context);
```

**使用するタイミング:** 会話履歴に残すべきでない参考資料、few-shotの例、または補助コンテキストを提供する場合。

### RequestMessageOverride

このリクエストのユーザーメッセージを完全に置き換えます。元のプロンプトは無視されます:

```csharp
var context = new AIRequestContext
{
    RequestMessageOverride = MessageBuilder
        .User($"以下のコンテキストに基づいて質問に答えてください。\n\nコンテキスト: {docs}\n\n質問: {userQuery}")
        .Build()
};

await service.GetCompletionAsync(userQuery, context: context);
```

**使用するタイミング:** ミドルウェアレイヤー（RAG、クエリ書き換え）がモデルに送る前にプロンプトを完全に再構成する必要があるが、元のユーザー入力は会話履歴に保持したい場合。

> **💡 参考:** `.WithRag()`を使用すると、RAGパイプラインがこの属性を自動的に活用します。内部動作の仕組みは[パイプラインのカスタマイズ — 内部動作の仕組み](rag-pipeline.md#内部動作の仕組み)を参照してください。

## 導入前後の比較

### シナリオ: 日付注入と検索されたコンテキストを含むRAG

**AIRequestContextなし:**

```csharp
// ❌ 煩雑で、状態を変更し、エラーが起きやすい
var origSys = service.SystemMessage;
service.SystemMessage = origSys
    + $"\n今日: {DateTime.Now:yyyy-MM-dd}"
    + $"\n\nコンテキスト:\n{retrievedChunks}";

var fewShotIndex = service.ActivateChat.Messages.Count;
service.ActivateChat.Messages.Add(MessageBuilder.Create().AddText(fewShotExample).Build());

var answer = await service.GetCompletionAsync(userQuery);

service.SystemMessage = origSys;
service.ActivateChat.Messages.RemoveAt(fewShotIndex); // few-shotの例を削除
```

**AIRequestContext使用:**

```csharp
// ✅ クリーンで、状態変更なし、副作用なし
var answer = await service.GetCompletionAsync(userQuery,
    context: new AIRequestContext
    {
        SystemMessagePrefix = $"今日: {DateTime.Now:yyyy-MM-dd}\n",
        SystemMessageSuffix = $"\n\nコンテキスト:\n{retrievedChunks}",
        AdditionalMessages = new List<Message>
        {
            MessageBuilder.Create().AddText(fewShotExample).Build()
        }
    });
```

## AIRequestProfileとの組み合わせ

単一リクエストに対する最大限の制御のために両方を一緒に渡すことができます:

```csharp
var response = await service.GetCompletionAsync(
    prompt,
    profile: new AIRequestProfile { Temperature = 0.1f, Stateless = true },
    context: new AIRequestContext
    {
        SystemMessageSuffix = $"\nコンテキスト:\n{docs}",
        AdditionalMessages = new List<Message>
        {
            MessageBuilder.Create().AddText("例: ...").Build()
        }
    }
);
```

生成パラメーターのオーバーライドの詳細は[AIRequestProfile](request-profiles.md)を参照してください。

## `SystemMessageProvider` による自動注入

### この機能が解決する問題

典型的なチャットアプリには、同じベースライン（今日の日付、アクティブフォルダー、セッション情報など）を必要とする LLM エントリーポイントが複数あります。`SystemMessageProvider` **なし**では、すべての呼び出し箇所でそのコンテキストを都度構築して渡すことを覚えていなければなりません:

```csharp
// ❌ SystemMessageProvider なし — すべてのエントリーポイントで注入を覚えていないといけない
var today = $"Today is {DateTime.UtcNow:yyyy-MM-dd}.";

// 1. メインチャット応答
var answer = await service.GetCompletionAsync(userMessage,
    context: new AIRequestContext { SystemMessageSuffix = today });

// 2. タイトル生成器（後から追加）
var title = await service.GetCompletionAsync("Summarize as a title: " + conversation,
    context: new AIRequestContext { SystemMessageSuffix = today });

// 3. サマライザー（さらに後から追加）
var summary = await service.GetCompletionAsync("Summarize: " + conversation,
    context: new AIRequestContext { SystemMessageSuffix = today });

// 4. Agent 呼び出し — 忘れやすい！ コンパイラは警告してくれない
var agentResult = await service.RunAgentAsync(goal);  // ← 日付抜け、サイレントバグ
```

このアプローチの問題:

- 同じコンテキスト構築スニペットがすべての呼び出し箇所で**重複**します
- 新しいエントリーポイント（上の `RunAgentAsync`）を**見落としやすく**、コンパイル時のチェックがありません
- LLM 呼び出しを追加するすべての新機能がこの慣例を覚えていなければなりません
- テストでも各呼び出し箇所でコンテキストのセットアップを複製する必要があります

`SystemMessageProvider` を使えば、ベースラインを**一度だけ**登録し、すべての外向き呼び出しが自動で受け取ります:

```csharp
// ✅ SystemMessageProvider あり — 一度登録すればどこでも適用される
service.WithSystemMessageProvider(() => new AIRequestContext
{
    SystemMessageSuffix = $"Today is {DateTime.UtcNow:yyyy-MM-dd}."
});

// これらすべてが自動的にベースラインを受け取ります — 呼び出し毎のボイラープレート不要
var answer      = await service.GetCompletionAsync(userMessage);
var title       = await service.GetCompletionAsync("Summarize as a title: " + conversation);
var summary     = await service.GetCompletionAsync("Summarize: " + conversation);
var agentResult = await service.RunAgentAsync(goal);  // ← これもベースラインを受け取る

// ストリーミングのエントリーポイントも同様 — 同じベースライン、呼び出し毎のボイラープレート不要
await foreach (var chunk in service.StreamAsync(userMessage)) { /* ... */ }
await foreach (var token in service.RunAgentStreamAsync(goal)) { /* ... */ }
```

### 動作の仕組み

`WithSystemMessageProvider` fluent ヘルパーでコールバックを一度登録します。すべての外向き呼び出し（`GetCompletionAsync`、`StreamAsync`、`RunAgentAsync`、`RunAgentStreamAsync`）が自動的にそれを呼び出してベースラインコンテキストを構築します:

```csharp
// 通常はサービス構築 / DI セットアップ時に登録
service.WithSystemMessageProvider(() => new AIRequestContext
{
    SystemMessageSuffix =
        $"Today is {DateTime.UtcNow:yyyy-MM-dd}.\n" +
        $"Current folder: {_uiContext.CurrentFolder}"
});

var answer = await service.GetCompletionAsync(userQuery);
await foreach (var chunk in service.StreamAsync(msg, options)) { /* ... */ }
var agentResult = await service.RunAgentAsync(goal);
```

### IO を伴う provider のための async オーバーロード

ベースラインコンテキストが DB、キャッシュ、HTTP 呼び出しから来る場合は async オーバーロードを使用してください。provider が `.Result` / `.GetAwaiter().GetResult()` でブロックする必要がありません。オーバーロード解決はラムダの arity で自動 — 引数なしが sync、`CancellationToken` 1 つが async:

```csharp
service.WithSystemMessageProvider(async ct =>
{
    var prefs = await _db.UserPreferences.FirstOrDefaultAsync(ct);
    return new AIRequestContext
    {
        SystemMessageSuffix = $"User language: {prefs?.Language ?? "en"}"
    };
});
```

非ストリーミングパス（`GetCompletionAsync`、`RunAgentAsync`）は設計上キャンセルをサポートしません — シグネチャが `CancellationToken` を受け取らず、provider には常に `CancellationToken.None` が渡されます。Provider でキャンセルが必要な場合（例: 長時間の DB クエリ）は、呼び出し元のトークンを provider コールバックまで伝播するストリーミングパス（`StreamAsync`、`RunAgentStreamAsync`）を使用してください。

### 明示的な per-call コンテキストとのマージ

登録された provider があり、かつ呼び出しで明示的な `AIRequestContext` も渡された場合、2 つはフィールド単位でマージされます:

| フィールド | マージルール |
|---|---|
| `SystemMessagePrefix` | 明示的値が非 null ならそれを優先、そうでなければ provider |
| `SystemMessageSuffix` | 明示的値が非 null ならそれを優先、そうでなければ provider |
| `RequestMessageOverride` | 明示的値が非 null ならそれを優先、そうでなければ provider |
| `AdditionalMessages` | 連結（provider が先、次に明示的） |

根拠: 一般的なケースは「provider がベースラインを提供し、特定の呼び出しはスカラーフィールド 1 つを置き換えたい、またはメッセージを追加したい」です — フィールド単位のオーバーライドは予期しない連結なしに意味を予測可能に保ちます。

### 呼び出しごとの invocation

Provider は **リクエストごとに 1 回** 呼び出されるため、戻り値はその瞬間の状態（タイムスタンプ、セッションなど）を反映できます。`null` を返すのは no-op であり、その呼び出しに対して `SystemMessageProvider` を未設定のままにするのと同じです。

### まとめ：いつこのツールを選ぶか — 3 条件の共通部分

上記の利用例とマージ規則から一歩引いて見ると、`SystemMessageProvider` は次の **3 つの条件が同時に成立する** ときの専用ツールです：

1. **すべての LLM 呼び出しに共通で** 敷かれるベースラインである — エントリポイントごとに手動注入を覚えていたくない
2. **呼び出し時点で値を動的に計算** する必要がある — 現在時刻、アクティブフォルダ、ログイン中のユーザーなど、起動時に固定できない値
3. **永続状態（`SystemMessage`、会話履歴）を汚染してはならない** — その値が次の呼び出し以降に漏れてはいけない

3 つの条件のうち 1 つでも欠けると、より単純なツールが正解になります：

| 状況 | 正解 | 理由 |
|---|---|---|
| ベースラインがセッション中ずっと **固定（変化しない）** | `service.SystemMessage = "..."` | 一度設定すれば十分、provider は不要 |
| **ただ 1 回の呼び出し** にだけ特別な処理が必要 | 呼び出し時に `AIRequestContext` を明示的に渡す | 共通ベースラインではなく、一度きりの注入 |
| 共通 + 動的 + 汚染禁止 **（3 条件すべて）** | **`SystemMessageProvider`** | この 3 つの共通部分のための専用ツール |

#### `AIRequestContext` の「一回性」原則と矛盾しない理由

`AIRequestContext` の本質は「一度だけ使う」ではなく **「永続状態を汚染しない」** です。`SystemMessageProvider` は、リクエストごとにコールバックを **再実行** して **そのリクエスト専用の新しい `AIRequestContext` を生成** するファクトリです。生成されたコンテキストは依然として per-request スコープであり、値は会話履歴に漏れず、次の呼び出しではコールバックが再度実行されて **その時点の** 値が反映されます。つまり provider は `AIRequestContext` の設計原則に違反せず、**それを自動化しているだけ** です。

具体的に、下記のように登録しても `service.SystemMessage` と `service.ActivateChat.Messages` はまったく変更されません：

```csharp
service.WithSystemMessageProvider(() => new AIRequestContext
{
    SystemMessageSuffix = $"Today is {DateTime.UtcNow:yyyy-MM-dd}"
});
```

- 日付が変わっても、次の呼び出しの provider 再実行で **新しい日付** が自動で反映されます（静的ではない）
- 一週間後に会話履歴を開いても、過去のリクエストに「Today is ...」が埋め込まれていることはありません
- マルチユーザー環境で共有サービスを使っても、呼び出しごとに独立したコンテキストが生成されます

> Mythosia.AI v6.3.0+ で利用可能。
