# エージェント（ReActループ）

エージェントループを使用すると、モデルがループを自分で書かなくても、関数を繰り返し呼び出して結果を反映し、最終回答に到達するまで自律的に目標を追求できます。

## 基本的な使い方

関数を登録してから、目標と共に`RunAgentAsync`を呼び出します:

```csharp
var service = new ChatGptService(apiKey, http)
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

## 動作の仕組み

各ステップ:
1. LLMが目標 + 会話履歴 + 関数定義を受け取る
2. LLMが関数を呼び出す → 実行して結果を履歴に追加
3. LLMがテキストレスポンスを返す → ループ終了、レスポンスを返す
4. ステップ数が`maxSteps`に達する → `AgentMaxStepsExceededException`をスロー
