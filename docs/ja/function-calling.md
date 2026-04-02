# 関数呼び出し

関数呼び出しを使用すると、モデルが情報を取得したりアクションを実行したりする際にC#コードを呼び出すことができます。

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

[AiFunction("search_products", "商品カタログを検索します")]
public static string SearchProducts(
    [AiParameter("検索クエリ", required: true)] string query,
    [AiParameter("最大結果数")] int limit = 5)
{
    // ... 実装
    return JsonSerializer.Serialize(results);
}
```

その後登録します:

```csharp
service.AddFunction(SearchProducts);
```

## 関数呼び出しポリシー

モデルが関数を呼び出せるタイミングを制御します:

```csharp
using Mythosia.AI.Models.Functions;

// モデルが判断（デフォルト）
service.FunctionCallingPolicy = FunctionCallingPolicy.Auto;

// 常に関数を呼び出すよう強制
service.FunctionCallingPolicy = FunctionCallingPolicy.Required;

// 関数呼び出しを無効化
service.FunctionCallingPolicy = FunctionCallingPolicy.None;
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

var fn = FunctionBuilder
    .Create("get_stock_price", "現在の株価を返します")
    .AddParameter("ticker", "株式ティッカーシンボル", required: true)
    .Build();

service.AddFunction(fn, ticker => FetchStockPrice(ticker));
```
