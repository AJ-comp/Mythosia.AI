# エージェント（ReActループ）

## エージェントループが必要な理由

通常の関数呼び出しでも、モデルの1つの応答から**複数の関数を順序付きバッチとして**実行し、複数のツールラウンドを継続できます。Agent APIはこの仕組みを、明示的な**ステップ上限**を持つ目標指向のReActループとしてまとめ、最終回答が生成されるまで各バッチの結果をモデルに返します:

- 「上位3社のAI企業を調査して株価を比較して」 — 複数のウェブ検索と株価照会が必要
- 「関連ポリシーを見つけ、注文状況を確認し、返金対象か教えて」 — 異なるツールを論理的な順序で連鎖させる必要がある
- 最初の結果が不十分な場合、モデルが検索を**リトライまたは改善**する必要がある場合も

`GetCompletionAsync`と`StartRunAsync`も共通のモデル・ツール反復処理を実行します。既存のエージェント関数が追加するのは呼び出しごとのラウンド制限と専用の例外変換であり、独立したプランナーや実行エンジンではありません。

## Runでツール処理の進捗を表示する

複数のツールを使うタスクで進捗を表示したり、ユーザーが途中で止めたりできるようにするには、`StartRunAsync`を使います。追加指示への対応と結果の取得は[Runの利用ガイド](execution-api-transition.md)を参照してください。

```csharp
// タスクの開始前にserviceへ関数を登録します。
await using var run = await service.WithMaxRounds(10).StartRunAsync(
    "ポリシーを探し、注文を確認して結果を説明してください。",
    onText: text => Console.Write(text),
    cancellationToken: cancellationToken);
string answer = await run.Result;
```

## 既存APIの互換性例

以下の`RunAgentAsync`と`RunAgentStreamAsync`は`[Obsolete]`警告付きの互換APIです。新しいコードでは上記のRunを使用します。既存の上限10回を維持するには`WithMaxRounds(10)`を指定し、専用の例外処理がある場合は移行ガイドを確認してください。

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
service.DefaultPolicy = new FunctionCallingPolicy
{
    TimeoutSeconds = 30
};

// 既存のRunAgentAsyncはDefaultPolicyと明示的なmaxStepsを使用します。
service.DefaultPolicy.TimeoutSeconds = 60;
var policyResult = await service.RunAgentAsync(
    "調査して要約してください...", maxSteps: 15);
```

事前定義されたポリシー:

```csharp
service.DefaultPolicy = FunctionCallingPolicy.Fast;    // 低タイムアウト、少ないラウンド — 素早いタスク用
var fastResult = await service.RunAgentAsync(
    "調査して要約してください...", maxSteps: service.DefaultPolicy.MaxRounds);
service.DefaultPolicy = FunctionCallingPolicy.Complex; // 高タイムアウト、多いラウンド — 詳細な調査用
var complexResult = await service.RunAgentAsync(
    "調査して要約してください...", maxSteps: service.DefaultPolicy.MaxRounds);
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

`AIRequestContext`は`AsyncLocal`で伝播しますが、サービスの会話履歴や実行ポリシーを並行操作してよいという意味ではありません。独立した同時タスクには別のサービスインスタンスを使用してください。

利用可能なプロパティの全リストは [AIRequestContext](request-contexts.md) を参照してください (`SystemMessagePrefix`、`SystemMessageSuffix`、`AdditionalMessages`、`RequestMessageOverride`)。

> Mythosia.AI v6.3.0 以降で利用可能です。

## 動作の仕組み

各ステップ:

1. LLMが目標 + 会話履歴 + 関数定義を受け取る
2. LLMが関数を呼び出す → 実行して結果を履歴に追加
3. LLMがテキストレスポンスを返す → ループ終了、レスポンスを返す
4. ステップ数が`maxSteps`に達する → `AgentMaxStepsExceededException`をスロー
