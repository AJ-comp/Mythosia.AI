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
    new AIRequestContext
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

var response = await service.GetCompletionAsync("今日は何曜日ですか？", context);
```

**使用するタイミング:** リクエストごとに変わる動的メタデータ（日付、ユーザーのタイムゾーン、セッション情報）を注入する場合。

### SystemMessageSuffix

このリクエストのみ、システムメッセージの末尾にテキストを追加します:

```csharp
var context = new AIRequestContext
{
    SystemMessageSuffix = "\n常に日本語で答えてください。"
};

var response = await service.GetCompletionAsync("Hello!", context);
```

**使用するタイミング:** リクエストごとの行動指示、RAGコンテキスト、または言語設定を追加する場合。

### AdditionalMessages

このリクエストのみ、会話に追加メッセージを挿入します — 参考文書やfew-shotの例の注入に便利です:

```csharp
var context = new AIRequestContext
{
    AdditionalMessages = new List<Message>
    {
        MessageBuilder.User("参考文書: 返金ポリシーは30日以内の返品を許可しています。").Build()
    }
};

var response = await service.GetCompletionAsync("返金対象ですか？", context);
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

await service.GetCompletionAsync(userQuery, context);
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

service.Messages.Add(MessageBuilder.User(fewShotExample).Build());

var answer = await service.GetCompletionAsync(userQuery);

service.SystemMessage = origSys;
service.Messages.RemoveAt(service.Messages.Count - 2); // few-shotの例を削除
```

**AIRequestContext使用:**

```csharp
// ✅ クリーンで、状態変更なし、副作用なし
var answer = await service.GetCompletionAsync(userQuery,
    new AIRequestContext
    {
        SystemMessagePrefix = $"今日: {DateTime.Now:yyyy-MM-dd}\n",
        SystemMessageSuffix = $"\n\nコンテキスト:\n{retrievedChunks}",
        AdditionalMessages = new List<Message>
        {
            MessageBuilder.User(fewShotExample).Build()
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
            MessageBuilder.User("例: ...").Build()
        }
    }
);
```

生成パラメーターのオーバーライドの詳細は[AIRequestProfile](request-profiles.md)を参照してください。
