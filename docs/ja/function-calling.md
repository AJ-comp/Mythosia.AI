# 関数呼び出し

## 関数呼び出しが必要な理由

LLMはテキストの生成しかできません — 天気を確認したり、データベースをクエリしたり、APIを呼び出したりすることは自分ではできません。関数呼び出し**なし**では、モデルの意図を手動でパースする必要があります:

```csharp
// ❌ 関数呼び出しなし — 手動の意図パース
var reply = await service.GetCompletionAsync("東京の天気はどう？");
// reply = "天気情報を確認するには天気サービスを照会する必要があります。"

// ユーザーが天気を求めていることを把握し、"東京"を抽出し、APIを自分で呼ぶ必要がある
if (reply.Contains("天気"))
{
    var city = ExtractCity(reply); // 脆弱な正規表現やキーワードマッチング
    var weather = await weatherApi.GetAsync(city);
    // 天気データを注入して再度リクエスト...
}
```

この方法は脆弱で、スケールせず、すべてのユーザー意図を事前に予測する必要があります。関数呼び出しを**使えば**、モデルが**いつ**コードを呼ぶか、**どの引数**を渡すかを自ら決定します:

```csharp
// ✅ 関数呼び出し使用 — モデルが意図把握＋引数抽出を処理
var service = new OpenAIService(apiKey, http)
    .WithFunction(
        "get_weather",
        "指定された場所の現在の天気を取得します",
        ("location", "都市と国", required: true),
        (string location) => weatherApi.Get(location)
    );

var response = await service.GetCompletionAsync("東京の天気はどう？");
// モデルがget_weather("東京, 日本")を呼び出し、結果を受け取り自然に回答します。
```

開発者はコードが**何を**できるかを定義し、モデルは**いつ**そして**どのように**使うかを判断します。

## クイック例

```csharp
var service = new OpenAIService(apiKey, http)
    .WithFunction(
        "get_weather",
        "特定の場所の現在の天気を取得します",
        ("location", "都市と国", required: true),
        (string location) => $"{location}の天気は晴れ、22°Cです"
    );

var response = await service.GetCompletionAsync("東京の天気はどうですか？");
// モデルがget_weather("Tokyo, Japan")を呼び出して結果を反映します。
```

## アトリビュートを使った関数定義

複雑な関数には`[AiFunction]`と`[AiParameter]`アトリビュートを使用します:

```csharp
using Mythosia.AI.Attributes;
using Mythosia.AI.Extensions;

public sealed class ProductFunctions
{
    [AiFunction("search_products", "商品カタログを検索します")]
    public string SearchProducts(
        [AiParameter("検索クエリ", required: true)] string query,
        [AiParameter("最大結果数")] int limit = 5)
    {
        // ... 実装
        return JsonSerializer.Serialize(results);
    }
}
```

その後登録します:

```csharp
service.WithFunctions(new ProductFunctions());
```

## 関数呼び出しポリシー

モデルが関数を呼び出せるタイミングを制御します:

```csharp
using Mythosia.AI.Models.Functions;

// モデルが判断（デフォルト）
service.FunctionCallMode = FunctionCallMode.Auto;

// 常に関数を呼び出すよう強制
service.ForceFunctionName = "search_products";

// 関数呼び出しを無効化
service.FunctionCallMode = FunctionCallMode.None;
```

## クラスからの一括登録

`[AiFunction]`アトリビュートが付いたメソッドを一括で登録します:

```csharp
var tools = new MyTools();
service.WithFunctions(tools);  // [AiFunction]インスタンスメソッドをスキャン

// 静的メソッドの場合
service.WithStaticFunctions<MyTools>();  // [AiFunction]静的メソッドをスキャン
```

## 非同期関数ハンドラー

すべての`WithFunction`オーバーロードに、`Func<..., Task<string>>`を受け取る`WithFunctionAsync`対応メソッドがあります:

```csharp
service.WithFunctionAsync<string>(
    "fetch_data",
    "外部APIからデータを取得します",
    ("url", "取得するURL", required: true),
    async (string url) =>
    {
        var result = await httpClient.GetStringAsync(url);
        return result;
    }
);
```

同期版と同様に0〜3パラメータをサポートします。

## 関数の一時無効化

登録を削除せずに単一リクエストで関数呼び出しを無効化します:

```csharp
// 拡張メソッド — 関数無効化状態で結果を返す
string answer = await service.AskWithoutFunctionsAsync("直接回答してください");

// またはプロパティをトグル
service.WithoutFunctions();  // FunctionsDisabled = true を設定
```

## FunctionBuilderの使用

関数定義をプログラム的に構築します:

```csharp
using Mythosia.AI.Builders;
using Mythosia.AI.Extensions;

var fn = FunctionBuilder
    .Create("get_stock_price")
    .WithDescription("現在の株価を返します")
    .AddParameter("ticker", "string", "株式ティッカーシンボル", required: true)
    .WithHandler(args => FetchStockPrice(args["ticker"].ToString() ?? string.Empty))
    .Build();

service.WithFunction(fn);
```

## モデルによる非同期ツール呼び出し

天気の取得に時間がかかるときでも、その結果に依存しない一般的な旅行の持ち物は先に説明できます。モデルによる非同期ツール呼び出しは、このように待ち時間に独立した作業を進めるために使います。結果に依存する判断は、結果が届いてから行う必要があります。

GPT-6 Astra と非同期ツール呼び出しは `Mythosia.AI` 7.1.0 から利用でき、共通型は `Mythosia.AI.Abstractions` 3.1.0 に含まれます。

`FunctionDefinition.AllowAsync` の既定値は `false` です。関数の実行中にモデルがほかの作業を続けてもよい場合に限り、`true` を設定するか `FunctionBuilder.WithAsync()` を呼び出します。`WithAsync(false)` で無効にできます。同じ関数定義とハンドラーを複数のプロバイダーで再利用できます。

属性による登録でも `[AiFunction("lookup", "データを取得", AllowAsync = true)]` で同じ許可を指定できます。

```csharp
using System.Threading.Tasks;
using Mythosia.AI.Builders;
using Mythosia.AI.Extensions;
using Mythosia.AI.Models;

service.ChangeModel(AIModels.OpenAI.Gpt6Astra);
var weatherTool = FunctionBuilder.Create("get_demo_weather")
    .WithDescription("ソウルのサンプル天気を返します")
    .WithAsync()
    .WithHandler(async _ =>
    {
        await Task.Delay(5000);
        return "{\"city\":\"Seoul\",\"temperature_c\":24,\"source\":\"demo\"}";
    })
    .Build();
service.WithFunction(weatherTool);
var answer = await service.GetCompletionAsync(
    "ソウルのサンプル天気を確認して。待っている間に旅行の持ち物を三つ説明して。");
```

Mythosia は GPT-6 Astra の Responses API で `async: true` を送信します。未対応のモデルや API ではこのフィールドを省略し、同じハンドラーの結果を待ちます。設定した `AllowAsync` の値は変更しません。実際の呼び出しもプロバイダーが非同期として返す必要があります（`FunctionCall.IsAsync`）。許可を有効にしても非同期実行が保証されるわけではありません。

`WithFunctionAsync` は .NET の非同期ハンドラーを登録し、`FunctionExecutionMode.Parallel` はローカルでのハンドラー実行方法を制御します。どちらもこの許可を自動的には有効にしません。`AllowAsync` は、関数の結果が届く前にモデルが作業を続けるための設定です。 `FunctionExecutionMode` は引き続き通常の呼び出しに適用されます。許可された非同期ジョブは `Sequential` モードでも重複して実行でき、専用のジョブプール全体に `MaxConcurrency` の上限が適用されます。

実行中のジョブは`GetCompletionAsync`、入力付きの既存`StreamAsync`、または`StartRunAsync`が返す`AIRun`の内部で管理し、完了した結果を元の呼び出しIDに対応付けて送信します。正常な最終返却やRunの完了は、保留中の結果を処理した後になります。Runは出力の観測とは独立して進みますが、ツールジョブがRunを離れて存続する公開のバックグラウンドセッションではありません。

非同期ツールを使う場合、`GetCompletionAsync`はリクエスト終了後に中間の独立した説明と最終テキストを順に連結して返します。既存の`StreamAsync`と`run.StreamAsync()`は各ラウンドのテキストを到着順に通知します。`run.Result`もRunの全テキストを連結した結果です。

ハンドラーにはキャンセルトークンが渡されません。そのため、キャンセル、タイムアウト、エラー、入力付きの旧ストリームの早期終了時は、開始済みのハンドラーの完了を待って後処理します。`run.StreamAsync()`の読み取りをやめるだけでは実行は止まりません。Runの実行をキャンセルするには`run.Cancel()`、開始時のトークン、またはRunの破棄を使います。対象は登録済みの関数ハンドラーです。プロトコルは[公式APIガイド](https://developers.openai.com/api/docs/guides/async-tool-calling)を参照してください。

ストリーミングでは、関数呼び出しが完全であり、有効な応答境界に到達したことを確認してからハンドラーを開始します。その後は、非同期ジョブの実行中にも次のモデルラウンドへ進めます。未完成の呼び出しイベントでは実行しません。ジョブが残ったままモデルが新しい呼び出しなしで応答した場合は、結果を待ってから再開します。未完了の呼び出しが履歴から抜け落ちないよう、保留中はコンテキスト超過時の自動要約と再試行を無効にします。
