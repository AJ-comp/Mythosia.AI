# 関数呼び出し

完成した回答と停止ボタンだけなら`GetCompletionAsync`に`cancellationToken`を渡します。進捗イベントや対応モデルへの追加指示にはRunを使います。[完了要求のキャンセル](completions.md#completion-cancellation)を参照してください。

設定をリクエストごとに分離し、共通設定から分岐するには[リクエストビルダー](request-building.md)を使います。`CreateRequest(...)`の後に`With...`をつなぎます。サービスのプロパティとfluentメソッドは従来の動作を維持します。

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

[Claude Fable 5.1](fable-5-1.md) は `Mythosia.AI` 8.0.0 / `Mythosia.AI.Abstractions` 4.0.0 から進捗更新、ターン限定指示、thinking binding 診断を利用できます。Mythos 5.1 は招待制です。両モデルともツール選択の強制を拒否します。

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

<a id="tool-execution-contract"></a>

## 非同期ツールでオブジェクトを返し、実行をキャンセルする

ファイルやデータベースを読むツールでは、非同期I/Oの後にオブジェクトを返すことがよくあります。停止ボタンの操作も実際の処理に届く必要があります。同期関数のオブジェクト返却は以前から対応済みです。今回の変更では非同期関数も同じように扱い、例外を失敗として記録します。

Before: 非同期関数では結果を自分でJSONに変換していました。`Task<FileResult>`を返すと値が失われ、`"Success"`だけが渡されていました。

```csharp
using System.IO;
using System.Text.Json;
using System.Threading.Tasks;
using Mythosia.AI.Attributes;

public sealed class FileToolsBefore
{
    [AiFunction("read_file", "テキストファイルを読む")]
    public async Task<string> ReadFileAsync(string path)
    {
        string text = await File.ReadAllTextAsync(path);
        return JsonSerializer.Serialize(new { Path = path, Text = text });
    }
}
```

After: オブジェクトをそのまま返し、注入されたキャンセルトークンをI/O処理に渡します。アプリ側で新しい結果ラッパーやアダプターを実装する必要はありません。

```csharp
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Mythosia.AI.Attributes;

public sealed record FileResult(string Path, string Text);

public sealed class FileTools
{
    [AiFunction("read_file", "テキストファイルを読む")]
    public async Task<FileResult> ReadFileAsync(
        string path, CancellationToken cancellationToken)
    {
        string text = await File.ReadAllTextAsync(path, cancellationToken);
        return new FileResult(path, text);
    }
}
```

`[AiFunction]`による登録は通常のオブジェクト、`Task<T>`、`ValueTask<T>`に対応し、文字列以外の値をJSONに変換します。`string`、`Task<string>`、`ValueTask<string>`は余分な引用符なしでそのまま返します。戻り値のない`Task`と`ValueTask`も完了を待ちます。既存の同期オブジェクト返却も維持します。 nullの戻り値は`"Done"`、戻り値のない`Task` / `ValueTask`の完了は`"Success"`として渡します。

戻り値の型を`Task`または`object`と宣言していても、実際の値が`Task<T>`なら完了した結果を同じ規則で変換します。`object`として返した`ValueTask<T>`や戻り値のない`ValueTask`も完了を待ち、`ValueTask`は一度だけ消費します。

`CancellationToken`引数はライブラリが渡し、モデル向けの引数スキーマには含めません。サービスまたはリクエストビルダーの`WithFunctions(...)`、`WithStaticFunctions<T>()`で登録できます。

`async void`のツールメソッドは登録時に拒否します。完了を待ち、例外を確認し、キャンセル後の後処理を終えられるよう、`Task`または`ValueTask`を返してください。

```csharp
using Mythosia.AI.Extensions;

await using var run = await service
    .CreateRequest("report.txtを読んで要約してください。")
    .WithFunctions(new FileTools())
    .StartRunAsync(cancellationToken: cancellationToken);

string answer = (await run.Result).Text;
```

`run.Cancel()`、`StartRunAsync`に渡したトークンのキャンセル、実行中のrunの破棄は、対応するローカルツールにも伝わります。ツールがトークンを使う必要があり、無視するコードを強制停止はできません。未開始の呼び出しは実行せずキャンセル結果を記録し、開始済みの関数は待機して呼び出しと結果の対応を保ちます。実行をキャンセルするとrunはキャンセル状態で終了し、次のモデルラウンドを開始しません。

Runの開始失敗やMCP接続の破棄中にキャンセルコールバックが例外を投げても、セッションやトランスポートの後処理を続けます。元のエラーと後処理のエラーを保持し、必要に応じて`AggregateException`でまとめて伝えます。 `McpConnection.DisposeAsync()`を同時に非同期で待機する呼び出しは、同じ後処理の完了を待ちます。先にトランスポートを閉じて接続終了が必要な読み取りを解除してから、読み取りループの終了を待ちます。

接続終了中に遅れて届いたツール呼び出しが待ち続けないよう、接続の破棄開始後は新しい `InitializeAsync`、`RefreshToolsAsync`、`CallToolAsync` を `ObjectDisposedException` で拒否します。応答のリクエスト ID が一致していても本文の形式が不正な場合は、その応答をスキップして呼び出しの待機登録を保持します。後続の正常な応答、呼び出し元のキャンセル、接続の後処理によって呼び出しを終了できます。 サーバーがストリームを閉じた場合や読み取りエラーで受信が終了した場合も、新しい操作は届かない応答を待つのではなく `McpException` で失敗します。続けるには新しい接続を作成してください。

実際の失敗では例外を投げます。実行部は`FunctionCallResult.IsError = true`として記録し、正常な`"Error: ..."`文字列として扱いません。意図的に返した文字列は正常な結果のままです。キャンセルされたツール結果には`IsCancelled = true`と`IsError = true`を設定します。

プログラムで登録する場合は、2引数の`WithHandler`オーバーロードを使います:

```csharp
using System.IO;
using Mythosia.AI.Builders;

var readText = FunctionBuilder.Create("read_text")
    .WithDescription("テキストファイルを読む")
    .AddParameter("path", "string", "ファイルパス", required: true)
    .WithHandler(async (args, token) =>
        await File.ReadAllTextAsync(args["path"].ToString()!, token))
    .Build();
```

既存の1引数の文字列ハンドラーも使えます。直接定義する場合は`HandlerWithCancellation`に`Func<Dictionary<string, object>, CancellationToken, Task<string>>`を設定します。`Handler`と`HandlerWithCancellation`は同じハンドラーを置き換える入口であり、2回の実行を登録するものではありません。この低水準APIは文字列を返し、オブジェクトの自動JSON変換はメソッド登録が担当します。

これはローカル.NET関数の戻り値とキャンセル処理であり、プロバイダーの`AllowAsync`機能は不要です。`run.StreamAsync(token)`の読み取りだけを止めてもrunは続きます。[Runガイド](execution-api-transition.md)と[プロトコル](https://developers.openai.com/api/docs/guides/async-tool-calling)も参照してください。

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

非同期ツールを使う場合、`GetCompletionAsync`はリクエスト終了後に中間の独立した説明と最終テキストを順に連結して返します。既存の`StreamAsync`と`run.StreamAsync()`は各ラウンドのテキストを到着順に通知します。`(await run.Result).Text`もRunの全テキストを連結した結果です。

ローカルツールは`Task<T>` / `ValueTask<T>`でオブジェクトを返し、注入された`CancellationToken`を受け取れます。`run.Cancel()`や開始トークンのキャンセルは協調するツールにも届きますが、読み取りの停止だけでは届きません。例外は失敗として記録します。キャンセル時は未開始の呼び出しをスキップし、トークンを無視する開始済みツールは後処理で待ちます。[結果・エラー・キャンセル](function-calling.md#tool-execution-contract)を参照してください。

ストリーミングでは、関数呼び出しが完全であり、有効な応答境界に到達したことを確認してからハンドラーを開始します。その後は、非同期ジョブの実行中にも次のモデルラウンドへ進めます。未完成の呼び出しイベントでは実行しません。ジョブが残ったままモデルが新しい呼び出しなしで応答した場合は、結果を待ってから再開します。未完了の呼び出しが履歴から抜け落ちないよう、保留中はコンテキスト超過時の自動要約と再試行を無効にします。

Perplexity: [調査範囲とツールを制御する](perplexity.md).
