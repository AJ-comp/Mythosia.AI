# Mythosia.AI 8への移行

リクエストごとに設定を分け、ユーザーが進行中の処理を中止し、回答と使用量・出典をまとめて保存するためのリリースです。6つの構造改善、プロバイダーとモデルの更新、3回の敵対的検証で修正した内容を1つのメジャー更新にまとめています。

アプリで使うパッケージだけをまとめて更新し、利用側を再ビルドしてください。Mythosia.AIは対応するAbstractions依存関係を自動的に取得します。以下は公開済みの基準と本リリースで組み合わせる互換バージョンです。

| パッケージ | 公開済み基準 | 公開予定 |
| --- | --- | --- |
| `Mythosia.AI` | 7.1.0 | [8.0.0](../../src/core/Mythosia.AI/RELEASE_NOTES.md#v800) |
| `Mythosia.AI.Abstractions` | 3.1.0 | [4.0.0](../../src/core/Mythosia.AI.Abstractions/RELEASE_NOTES.md#v400) |
| `Mythosia.AI.Providers.Alibaba` | 2.0.1 | [3.0.0](../../src/core/Mythosia.AI.Providers.Alibaba/RELEASE_NOTES.md#v300) |
| `Mythosia.AI.Rag` | 7.6.0 | [8.0.0](../../src/rag/Mythosia.AI.Rag/RELEASE_NOTES.md#v800) |
| `Mythosia.AI.Mcp` | 0.0.1-preview | [0.1.0-preview](../../src/integrations/Mythosia.AI.Mcp/RELEASE_NOTES.md#v010-preview) |
| `Mythosia.AI.Serving.Vllm` | 1.0.0-preview | [1.0.0](../../src/serving/Mythosia.AI.Serving.Vllm/RELEASE_NOTES.md#v100) |

`Mythosia.AI.Serving.Vllm`は本リリースで`1.0.0-preview`から安定版`1.0.0`へ移行します。既存のモデル・状態・サーバーバージョン・メトリクスAPIを維持し、コアAIパッケージに依存しない独立したパッケージです。

## 目的から変更を選ぶ

| 目的 | 変更と移行 |
| --- | --- |
| 送信前に画像設定の誤記を防ぐ | 文字列の代わりに`ImageQuality`、`ImageBackground`、`ImageOutputFormat`、`ImageSize.Pixels(...)` / `ImageSize.Preset(...)`を使います。対応範囲はプロバイダーごとに異なります。 |
| 複数のリクエストを独立して準備する | `CreateRequest(...)`から始め、`With...`が返す新しいビルダーを保持します。サービスのsetterは引き続き共有の既定値を変更します。 |
| 非同期ツールからデータを直接返す | 属性で登録したメソッドは`Task<T>` / `ValueTask<T>`のオブジェクトを返し、注入される`CancellationToken`を受け取れます。例外は失敗として記録され、既存の文字列ハンドラーも使えます。 |
| 中止時に応答待ちを終える | 完了、Run、対応するRAG入口へ`cancellationToken`を渡します。ローカル処理と協調するツールを止めますが、リモート処理の停止や完了した外部操作の取消は保証しません。 |
| 回答・使用量・出典を保存する | `AIRun.Result`は`Task<AIRunResult>`です。文字列には`(await run.Result).Text`を使います。ストリームを読まなくても結果を集めます。 |
| モデルに適した操作を表示する | `request.GetCapabilities()`またはサービス・画像の機能照会を使います。`Supported`、`Unsupported`、`Unknown`はライブラリの情報であり、アカウントへのライブ接続確認ではありません。 |

## 呼び出し側と独自プロバイダーの移行

画像設定の型、`AIRun.Result`、キャンセルトークン追加後のシグネチャは破壊的変更です。独自`IAIService`実装と変更された公開オーバーロードのoverrideはトークンを追加・転送します。プロバイダーの`GetCompletionAsync(Message)` overrideはシグネチャを保ち、`RequestCancellationToken`を転送します。独自`AIRun`は`AIRunResult`を返してください。GetCompletionAsyncの文字列、型付き完了と`StructuredStreamRun<T>.Result`の型付き結果は維持されます。入力付きサービス・RAG StreamAsyncはv8でも公開です。RunAgentAsyncとRunAgentStreamAsyncは互換動作とobsolete警告を維持します。新しい進捗表示・中止・対応モデルへの追加指示にはRunを使います。

## 1つのリクエストから結果と必要に応じた進捗を受け取る

```csharp
using Mythosia.AI.Builders;
using Mythosia.AI.Models.Runs;
using Mythosia.AI.Models.Capabilities;

AIRequestBuilder request = service.CreateRequest("Summarize this document.");
var capabilities = request.GetCapabilities();
if (capabilities.Temperature == CapabilitySupport.Supported)
    request = request.WithTemperature(0.2f);

await using var run = await request
    .StartRunAsync(
        onText: text => Console.Write(text),
        cancellationToken: cancellationToken);

AIRunResult result = await run.Result;
Console.WriteLine(result.Text);
```

OpenAIはピクセル、Google・xAIは`ImageSize.Preset(...)`で画像サイズを選びます。明示形式に対応する場合だけAutoを変更し、保存形式は返された`GeneratedImage.MediaType`に従ってください。

```csharp
using Mythosia.AI.Models.Images;

var imageRequest = new ImageGenerationRequest
{
    Prompt = "A simple architectural study",
    Quality = ImageQuality.Auto,
    Background = ImageBackground.Auto,
    OutputFormat = ImageOutputFormat.Auto,
    Size = ImageSize.Pixels(1536, 1024)
};
var generated = await images.GenerateImagesAsync(imageRequest, cancellationToken);
```

登録したツールでは次のようにアプリのオブジェクトを返します。低レベルの`HandlerWithCancellation`は引き続き`Task<string>`を返し、新しいオブジェクトラッパーは不要です。

```csharp
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Mythosia.AI.Attributes;

public sealed record FileResult(string Path, string Text);

public sealed class FileTools
{
    [AiFunction("read_file", "Read a text file")]
    public async Task<FileResult> ReadFileAsync(
        string path, CancellationToken cancellationToken)
    {
        string text = await File.ReadAllTextAsync(path, cancellationToken);
        return new FileResult(path, text);
    }
}
```

## プロバイダー更新と検証範囲

準備済みのFable 5.1、Gemini 3.7/3.8 Flash、Grok 4.6、DeepSeek Flash、Perplexity Agent連携とOpenAI・Google・xAI共通の画像生成・編集も含まれます。削除されたモデル定数やPerplexityエンドポイントの変更には呼び出し側の修正が必要な場合があります。詳細はプロバイダーガイドとパッケージのリリースノートを参照してください。

Perplexityの調査設定には`PerplexityAgentOptions`を使います。Profile・Custom Skill・Connectorの実証テストは準備済みですが、実行には登録済みリソースが必要です。MCPはpreviewを維持します。破棄開始後の呼び出しは`ObjectDisposedException`、読取ループ終了後の新規呼び出しは`McpException`で失敗し、無期限の待機を防ぎます。

3回の敵対的検証でリクエストのコピー、ツール結果、取消・後処理、トークン計算、応答検証、MCP接続の寿命を改善しました。3回目で43件の回帰ケースを追加し、全2,703件が通過しました。文書は13言語で確認しました。この検証では実際のプロバイダーAPIを呼んでおらず、単体テストはアカウント依存の全連携を実証したものではありません。

## 詳細ガイド

- [CreateRequest / AIRequestBuilder](request-building.md)
- [AIRun / AIRunResult](execution-api-transition.md)
- [ImageQuality / ImageSize](providers.md#image-options-migration)
- [CancellationToken](completions.md#completion-cancellation)
- [Task<T> / ValueTask<T> / HandlerWithCancellation](function-calling.md#tool-execution-contract)
- [GetCapabilities](model-capabilities.md)
- [PerplexityAgentOptions](perplexity.md)
