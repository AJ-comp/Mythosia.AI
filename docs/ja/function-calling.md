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
