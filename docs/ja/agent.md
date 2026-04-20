# エージェント（ReActループ）

## エージェントループが必要な理由

通常の関数呼び出しでは、モデルはリクエストごとに**1回**の関数を呼び出し、実行した後会話が続きます。しかし実際の多くのタスクは、モデルが自律的に計画し実行する**複数のステップ**を必要とします:

- 「上位3社のAI企業を調査して株価を比較して」 — 複数のウェブ検索と株価照会が必要
- 「関連ポリシーを見つけ、注文状況を確認し、返金対象か教えて」 — 異なるツールを論理的な順序で連鎖させる必要がある
- 最初の結果が不十分な場合、モデルが検索を**リトライまたは改善**する必要がある場合も

このオーケストレーションループを自分で書くのは面倒でエラーが起きやすいです。**エージェントループ**（ReActパターン：推論 → 行動 → 観察 → 繰り返し）がこれを自動的に処理します — モデルが最終回答に到達するまで各ステップで次の行動を自ら決定します。

## 基本的な使い方

関数を登録してから、目標と共に`RunAgentAsync`を呼び出します:

```csharp
var service = new OpenAIService(apiKey, http)
    .WithFunction(
        "search_web",
        "ウェブで情報を検索します",
        ("query", "検索クエリ", required: true),
        query => WebSearch(query)
    )
    .WithFunction(
        "get_stock_price",
        "現在の株価を取得します",
        ("ticker", "株式ティッカーシンボル", required: true),
        ticker => FetchPrice(ticker)
    );

string result = await service.RunAgentAsync(
    goal: "上位3社のAI企業の現在の株価は何ですか？",
    maxSteps: 10
);

Console.WriteLine(result);
```

モデルは必要に応じて関数を呼び出し、結果を観察し、最終的なテキストレスポンスを生成するまで次のステップを決定します。

## maxSteps

`maxSteps`はLLM→関数呼び出しラウンドの上限です。制限内に完了しない場合、`AgentMaxStepsExceededException`がスローされます:

```csharp
try
{
    string result = await service.RunAgentAsync("調査して要約してください...", maxSteps: 5);
}
catch (AgentMaxStepsExceededException ex)
{
    // ex.PartialResponseにモデルがここまで生成した内容が含まれます
    Console.WriteLine($"早期終了: {ex.PartialResponse}");
}
```

## FunctionCallingPolicy

エージェントループのラウンドごとの動作を制御します:

```csharp
service.FunctionCallingPolicy = new FunctionCallingPolicy
{
    MaxRounds = 10,
    TimeoutSeconds = 30
};

// または拡張メソッドで:
service.WithMaxRounds(15).WithTimeout(60);
```

事前定義されたポリシー:

```csharp
service.WithFastPolicy();    // 低タイムアウト、少ないラウンド — 素早いタスク用
service.WithComplexPolicy(); // 高タイムアウト、多いラウンド — 詳細な調査用
```

## 呼び出しごとのリクエストコンテキスト

`RunAgentAsync`と`RunAgentStreamAsync`はオプションの`AIRequestContext`を受け取り、動的なシステムメッセージのprefix/suffix、参照ドキュメント、または目標メッセージの置き換えを**単一のエージェント実行内に限定**して注入できます — サービスのシステムメッセージや会話履歴を変更することはありません。

```csharp
string result = await service.RunAgentAsync(
    goal: "返金ポリシーを見つけて、注文 #1234 が対象か確認して。",
    maxSteps: 10,
    context: new AIRequestContext
    {
        SystemMessagePrefix = $"今日の日付は {DateTime.UtcNow:yyyy-MM-dd} です。\n",
        SystemMessageSuffix = "\n必ず参照したポリシー条項を引用してください。"
    });
```

ストリーミング版も同じパラメータを受け取ります:

```csharp
await foreach (var content in service.RunAgentStreamAsync(
    goal: "上位3社のAI企業の株価を調査して。",
    maxSteps: 10,
    options: StreamOptions.WithFunctions,
    context: new AIRequestContext
    {
        SystemMessagePrefix = $"ユーザーのタイムゾーン: {userTz}\n"
    }))
{
    // コンテンツを処理
}
```

コンテキストは`AsyncLocal`を介して伝播されるため、同じサービスインスタンスで並行して実行される複数のエージェント呼び出しは互いに干渉しません。

利用可能なプロパティの全リストは [AIRequestContext](request-contexts.md) を参照してください (`SystemMessagePrefix`、`SystemMessageSuffix`、`AdditionalMessages`、`RequestMessageOverride`)。

> Mythosia.AI v6.3.0 以降で利用可能です。

## 動作の仕組み

各ステップ:

1. LLMが目標 + 会話履歴 + 関数定義を受け取る
2. LLMが関数を呼び出す → 実行して結果を履歴に追加
3. LLMがテキストレスポンスを返す → ループ終了、レスポンスを返す
4. ステップ数が`maxSteps`に達する → `AgentMaxStepsExceededException`をスロー
