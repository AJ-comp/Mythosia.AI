# プロバイダー固有設定アーキテクチャ

> GPT-6 Sol/Luna は未リリースの追加機能です。[モデルの選択と必要バージョン](https://github.com/AJ-comp/Mythosia.AI/blob/main/docs/ja/providers.md#gpt-6-sol-luna)を参照してください。

回答・使用量・出典をまとめて取得するには、`await run.Result` が返す `AIRunResult` を使用します。文字列は `result.Text` で取得でき、ストリームを読む必要はありません。Mythosia.AI 8.0.0 / Mythosia.AI.Abstractions 4.0.0 の API 変更です。`GetCompletionAsync` と `StructuredStreamRun<T>.Result` の戻り値型は維持します。 [Run の結果と移行](../../../../../docs/ja/execution-api-transition.md#run-result).


設定をリクエストごとに分離し、共通設定から分岐するには[リクエストビルダー](../../../../../docs/ja/request-building.md)を使います。`CreateRequest(...)`の後に`With...`をつなぎます。サービスのプロパティとfluentメソッドは従来の動作を維持します。

> [Claude Fable 5.1](../../../../../docs/ja/fable-5-1.md) は `Mythosia.AI` 8.0.0 / `Mythosia.AI.Abstractions` 4.0.0 から進捗更新、ターン限定指示、thinking binding 診断を利用できます。Mythos 5.1 は招待制です。両モデルともツール選択の強制を拒否します。

> GPT-6 Astra、`AllowAsync`、`StartRunAsync`、共通の推論・検索 API は `Mythosia.AI` 7.1.0 から利用でき、共通型は `Mythosia.AI.Abstractions` 3.1.0 に含まれます。

## 原則

アプリケーションは[共通の推論・検索 API](https://github.com/AJ-comp/Mythosia.AI/blob/main/docs/ja/reasoning-and-search.md)で、作業に必要な推論量とホスト型検索を指定できます。`AIRequestFeatures` は論理的なリクエストごとにコピーされ、プロバイダーのアダプターが検証・変換します。プロバイダー固有の既定値はサービスに残り、キャッシュを維持する変更の状態は追跡中の会話に保存されます。`AICitation` はストリームの購読とは独立して出典を保持します。カスタムサービスは任意の `IAIRequestFeatureService` で対応し、`IAIService` に必須メンバーは追加されません。

| 設定タイプ | 配置場所 | 例 |
|------------|----------|-----|
| **共通設定** | `ChatBlock` | Temperature, TopP, MaxTokens, FrequencyPenalty 等 |
| **プロバイダー固有** | 各サービスクラス | ThinkingLevel/ThinkingBudget (Gemini), ReasoningEffort (GPT) 等 |
| **関数ごとの実行許可** | `FunctionDefinition` | `AllowAsync`（既定値 `false`） |

`AllowAsync` は呼び出し側が選ぶ許可であり、モデルと API の対応状況はサービスが内部で判断します。`FunctionBuilder.WithAsync()` と `[AiFunction("lookup", "データを取得", AllowAsync = true)]` でも同じ許可を有効にできます。GPT-6 Astra / Sol / Luna では Responses で使用し、未対応のモデルでは API オプションを省略して同じハンドラーの結果を待ちます。設定した許可の値は変更しません。

## 現在の実装: サービスレベル

プロバイダー固有設定は各サービスクラスのプロパティとして管理します。

```csharp
using Mythosia.AI.Models;
using Mythosia.AI.Models.Enums;

geminiService.ChangeModel(AIModels.Google.Gemini3_8Flash);

// 共通設定 → ChatBlock
geminiService.ActivateChat.MaxTokens = 4096;

// プロバイダー固有設定 → サービス
geminiService.ThinkingLevel = GeminiThinkingLevel.Low;
```

軽い初回検討には `Low`、難しいレビューには `High` を使えます。推論を増やすと遅延やトークン使用量が増える場合があります。両モデルは `Low`、`Medium`、`High` に対応し、`Minimal` と `None` は非対応です。`GeminiThinkingLevel.Auto` は上書きを省略し、3.8 のプロバイダー既定値は `Medium` です。`ThinkingLevel` はサービスの基本設定、`WithReasoning(...)` は論理リクエスト単位の上書きです。両モデルでは `temperature`、`topP`、`topK` を送信しません。プロバイダーの上限は入力 1,048,576、出力 65,536 トークンです。

### Grok 4.6

素早い下書きには低いレベルを使い、応答速度より回答の質を重視する難しい検証には推論を増やせます。追加の `XHigh` レベルを使う場合は Grok 4.6 を明示的に選択します。

```csharp
using Mythosia.AI.Extensions;
using Mythosia.AI.Models;
using Mythosia.AI.Services.xAI;

var grok = new XAIService(apiKey, httpClient);
grok.ChangeModel(AIModels.xAI.Grok4_6);
grok.WithGrokReasoning(GrokReasoning.Low);

string review = await grok
    .CreateRequest("ローリング更新とブルーグリーンデプロイを、障害時の復旧手順も含めて比較してください。")
    .WithReasoning(ReasoningLevel.XHigh)
    .GetCompletionAsync();
```

Grok 4.6 は `Low`、`Medium`、`High`、`XHigh` (`GrokReasoning.XHigh`) に対応します。`Auto` は `reasoning_effort` を省略し、プロバイダーの既定値 `High` を使います。`None` で推論を無効にはできません。推論を増やすと待ち時間やトークン使用量が増える場合があります。互換性のため `XAIService` の既定モデルは Grok 4.5 のままです。4.5 は `Low` から `High`、4.3 は `None` から `High` に対応し、これらの旧モデルでの `XHigh` は送信前に拒否されます。

`WithGrokReasoning(...)` と従来の `WithGrokParameters(...)` はサービスの基本設定を変更します。Grok 4.6 の共通 `WithReasoning(...)` はツールラウンドと構造化出力の修正を含む一つの論理リクエストにだけ適用され、その後は基本設定に戻ります。常に推論するこのモデルでは、内部の `DisableReasoning` プロファイルは `Low` を使います。共通設定による xAI のキャッシュ保持更新とホスト型 Web・ファイル検索は未統合です。


### Grok Imagine Image 2.0

商品説明からビジュアル案を作ったり、別々の写真の被写体と背景を組み合わせたりするときに使います。`XAIService` は OpenAI・Google と同じ `IImageGenerationService` で生成と編集を提供します。`Mythosia.AI` 8.0.0 / `Mythosia.AI.Abstractions` 4.0.0 から利用できます。

独立した既定の画像モデルは `AIModels.xAI.GrokImagineImage2_0` (`grok-imagine-image-2.0`) です。画像リクエストはチャットモデルを変更せず、チャット履歴にも追加されません。

```csharp
using Mythosia.AI.Models;
using Mythosia.AI.Models.Images;
using Mythosia.AI.Services;
using Mythosia.AI.Services.xAI;

IImageGenerationService images = new XAIService(apiKey, httpClient);
var generated = await images.GenerateImagesAsync(new ImageGenerationRequest
{
    Model = AIModels.xAI.GrokImagineImage2_0,
    Prompt = "日の出のガラス製パビリオン、横に広い構図",
    Size = ImageSize.Preset(ImageResolution.OneK, ImageAspectRatio.SixteenByNine),
    OutputFormat = ImageOutputFormat.Auto
});

var image = generated.Images[0];
var extension = image.MediaType switch
{
    "image/jpeg" => ".jpg",
    "image/png" => ".png",
    "image/webp" => ".webp",
    _ => throw new NotSupportedException(image.MediaType)
};
await File.WriteAllBytesAsync("pavilion" + extension, image.Data);
```

各 `GeneratedImage.Data` にデコード済みの画像バイト列が入ります。拡張子は `MediaType` に合わせて選んでください。アダプターはインライン base64 応答を要求し、提供元の画像 URL はダウンロードしません。`Count` は出力1～10枚、編集は JPEG・PNG・WebP の参照画像1～5枚に対応します。

xAIは新しい共通既定値`ImageOutputFormat.Auto`のみサポートします。出力コーデックを選択できないため、明示的な`Jpeg`、`Png`、`WebP`は送信前に拒否されます。拡張子は`GeneratedImage.MediaType`で決めてください。ライブラリは画像変換を行いません。品質は`ImageQuality.Auto`、`Low`、`Medium`、背景は`ImageBackground.Auto`のみ。圧縮指定と独立した`Mask`は非対応です。

Google は `ImageSize.Auto` またはモデル別の解像度・比率の `Preset` を使用します。[Google モデル別の画像オプション](https://github.com/AJ-comp/Mythosia.AI/blob/main/docs/ja/providers.md#google-image-options).出力は`ImageOutputFormat.Auto`または`Jpeg`で、`Png`/`WebP`は拒否されます。GoogleとxAIは`Pixels`を拒否し、OpenAIは`Auto`/`Pixels`を受け付けて`Preset`を拒否します。[移行例](https://github.com/AJ-comp/Mythosia.AI/blob/main/docs/ja/providers.md#image-options-migration)を参照してください。

Google の `Resolutions` と `AspectRatios` は選択した画像モデルに応じて変わり、生成・編集の検証にも適用されます。Flash-Lite の保守的な 1K 方針を含む[モデル別の表](https://github.com/AJ-comp/Mythosia.AI/blob/main/docs/ja/providers.md#google-image-options)を参照してください。非対応の明示的な値は HTTP 前に拒否され、不明な独自モデルは `Unknown` とプロバイダー共通のオプション検証を維持します。

### DeepSeek Flash

素早い回答の後に詳しく検証したり、グラフやスクリーンショットを説明したりする場合に DeepSeek Flash を使えます。`AIModels.DeepSeek.Flash` (`deepseek-flash`) は、2026年9月10日公開の視覚理解対応 V4.1 Flash を選択します。既存の補完・ストリーミング・Run・関数呼び出し・RAG API を使用し、`Mythosia.AI` 8.0.0 / `Mythosia.AI.Abstractions` 4.0.0 から利用できます。

> 公開済みの `Mythosia.AI` 8.0.0 / `Mythosia.AI.Abstractions` 4.0.0 は Flash の基本機能に対応しています。`AIModels.DeepSeek.V4Pro`、`UseResponsesApi`、Files API、`DeepSeekImageFileContent` はソースに追加された未リリース機能であり、対応するコアと抽象化のソースビルドが必要です。これらは上記の公開済みパッケージには含まれません。[未リリースの変更履歴](https://github.com/AJ-comp/Mythosia.AI/blob/main/src/core/Mythosia.AI/RELEASE_NOTES.md#unreleased)。

テキスト処理には `AIModels.DeepSeek.V4Pro` (`deepseek-v4-pro`, V4-Pro-0813) を選択できます。既定の Flash は画像にも対応し、両モデルで Low/High/Max 推論と同じ出力上限を使えます。既存の補完・ストリーミング・Run・ローカル関数 API で Responses を使う場合は、リクエスト作成前に `UseResponsesApi = true` を設定します。既存アプリの Chat Completions を維持するため既定値は `false` で、設定は後続のツールラウンドまで固定されます。Responses は保存済み応答 ID に依存せず、会話と元の推論履歴をすべて再送します。

```csharp
using Mythosia.AI.Extensions;
using Mythosia.AI.Models;
using Mythosia.AI.Services.DeepSeek;

var pro = new DeepSeekService(apiKey, AIModels.DeepSeek.V4Pro, httpClient)
{
    UseResponsesApi = true
};
pro.WithDeepSeekReasoning(DeepSeekReasoning.High);
string answer = await pro.CreateRequest("Review this deployment plan.").GetCompletionAsync();
```

複数の質問や会話で同じ画像を使う場合は一度アップロードします。`UploadFileAsync` はパス、または呼び出し元所有のストリームとファイル名を受け取り、purpose は `user_data` です。JPEG・PNG・GIF・WebP は最大 64 MiB。`DeepSeekImageFileContent` は両方の通信方式の Flash で画像を参照します。PDF・文書入力ではなく、V4 Pro では拒否されます。期限省略時は永久保存され、`expiresAfterSeconds` は 3600〜2592000 秒です。参照する会話がすべて終了するまでファイルを保持してください。

```csharp
using Mythosia.AI.Models.Messages;

var vision = new DeepSeekService(apiKey, AIModels.DeepSeek.Flash, httpClient)
{
    UseResponsesApi = true
};
var file = await vision.UploadFileAsync("chart.png", expiresAfterSeconds: 3600);
var question = new Message(ActorRole.User, new List<MessageContent>
{
    new TextContent("Explain the trend in this chart."),
    new DeepSeekImageFileContent(file.Id)
});
string uploadedDescription = await vision.GetCompletionAsync(question);
var metadata = await vision.GetFileAsync(file.Id);
```

`GetFileAsync` はメタデータ取得、`ListFilesAsync(new DeepSeekFileListOptions { After = lastId, Limit = 20, Order = DeepSeekFileOrder.Ascending })` はページ取得、`DeleteFileAsync` は削除です。`HasMore` が true の間は `LastId` を次の `After` に渡します。`Descending` も使えます。公式文書にはファイル内容のダウンロード用エンドポイントが記載されていません。Chat UI の Flash・V4 Pro と書き換えモデルは現在のカタログに従います。保存済みの旧 UI 名 `DeepSeekChat` のみ Flash に移行し、任意のモデル ID は保持します。

[Responses](https://api-docs.deepseek.com/guides/responses_api/) · [Files](https://api-docs.deepseek.com/guides/files_api/) · [Models and limits](https://api-docs.deepseek.com/quick_start/pricing/)

```csharp
using Mythosia.AI.Extensions;
using Mythosia.AI.Models;
using Mythosia.AI.Models.Streaming;
using Mythosia.AI.Services.DeepSeek;

var deepseek = new DeepSeekService(apiKey, httpClient);
deepseek.ChangeModel(AIModels.DeepSeek.Flash);
deepseek.WithDeepSeekReasoning(DeepSeekReasoning.Low);

string draft = await deepseek.GetCompletionAsync("ローリングデプロイとブルーグリーンデプロイを、ロールバックのリスクも含めて比較してください。");

await using var run = await deepseek
    .CreateRequest("その比較で使った前提を検証してください。")
    .WithReasoning(ReasoningLevel.High)
    .StartRunAsync(
        options: new StreamOptions().WithReasoning());
await foreach (var item in run.StreamAsync())
{
    if (item.Type == StreamingContentType.Reasoning)
        Console.Write(item.Content);
}
string review = (await run.Result).Text;
```

ライブラリの `ThinkingEnabled` は既定で `false` です。`WithDeepSeekReasoning(...)` は推論を有効にし、継続的な `ReasoningEffort` (`Auto`, `Low`, `High`, `Max`) を設定します。ネイティブの `Auto` は effort を省略し、提供元の既定値 `High` を使います。共通 `WithReasoning(...)` はツールラウンドを含む一つの論理リクエストだけを変更します。`None` は無効化、`Minimal`/`Low` は `Low`、`Medium`/`High`/`XHigh` は `High`、`Max` は `Max` に対応します。共通の `Auto` は設定済みの基本動作を保ちます。推論量を増やすと待ち時間やトークン使用量が増える場合があります。 `ReasoningEffort` プロパティの変更だけでは推論は有効になりません。

`WithFunction(...)` でローカル関数を登録し、アプリのコードからデータ取得や処理を行えます。ツールは推論の有無にかかわらず利用できます。Chat Completions では推論中の強制・必須選択が拒否されるため、自動選択を使ってください。`UseResponsesApi = true` では推論中でも `ForceFunctionName` で関数を指定でき、Responses の `tool_choice` の直下に `type` と `name` を送ります。ネイティブ非同期ツールが有効になるわけではありません。後続ラウンドに備え、ネイティブの `reasoning_content` と呼び出し ID を保持します。Run と既存ストリーミングでは `StreamOptions.WithReasoning()` により `StreamingContentType.Reasoning` を観察できます。この観察設定自体は推論を有効にしません。使用量には提供元が報告したキャッシュ・推論トークンも含まれます。 自動コンテキスト復旧は共通ストリーミングループを使います。ツールに過去のネイティブ推論履歴が必要な場合は、その履歴を保つため自動圧縮を禁止し、超過エラーを返します。

両モデルの上限はコンテキスト 1M、出力 384K (`393216`) トークンで、既定予算は 8,000 です。推論中は temperature・penalty を省略し、`top_p` は 0.95 以上、非推論では `top_p` を省略します。Responses のネイティブ JSON schema は既存の型付き出力 API から利用できます。バックグラウンド実行、サーバー側 `store`/`previous_response_id`、ホスト型検索、`CachePreservation.Required`、ネイティブ非同期ツール、`SteerAsync`、画像生成は未対応です。ローカル RAG と通常のツールラウンドは利用できます。

### Perplexity Agent API

最新情報に基づく回答と、読者が確認できる出典が必要なときに Perplexity を使います。`PerplexityService` は Agent API を呼び出し、独立した検索と埋め込みは、自分で選んだ回答モデルに検索の仕組みを組み合わせるために使います。

```csharp
using Mythosia.AI.Models.Perplexity;
using Mythosia.AI.Services.Perplexity;

var service = new PerplexityService(apiKey, httpClient)
    .WithPerplexityOptions(new PerplexityAgentOptions
    {
        Preset = PerplexityPreset.Low,
        MaxSteps = 8
    });
string answer = await service.GetCompletionAsync("最新のバッテリーリサイクル手法を比較し、出典を示してください。");
```

`WithPerplexityOptions(...)` はサービスに保持され、論理リクエストごとにコピーされます。共通の `WithReasoning(...)` と `WithWebSearch(...)` は、クライアントツールのラウンドや型付き出力の修復を含む次の論理リクエストに適用されます。内部の RAG クエリ書き換えには最終回答の検索設定が渡りません。

`UsePreset(...)` でプリセットを選べます。プリセット/プロファイルは独自のモデルを選び、`ModelOverride` で明示的に変更します。`DisableWebSearch` はアダプターの既定ツールだけを除き、プリセット内蔵の検索停止を保証しません。モデルに応じて `Minimal`、`Low`、`Medium`、`High`、`XHigh`、`Max` を使用できます。`None` と直接 Sonar への明示的な推論指定は拒否されます。内部の `DisableReasoning` は低い対応レベルか省略を使い、完全な無効化を保証しません。

`PerplexityHostedTools.WebSearch`、`FetchUrl`、`Sandbox`、`FinanceSearch`、`PeopleSearch`、`Mcp`、`Connector` でツール設定を作れます。MCP は承認待ちなしで実行されるため、必要に応じて `allowedTools` を制限します。Connector は提供元のプレビュー機能で、接続済みの統合を参照します。

`StartBackgroundAsync` は履歴を追加せず入力を取得し、有効なローカル関数や `Store = false` を拒否します。`GetResponseAsync` は一度取得し、`WaitForCompletionAsync` は終了状態までポーリングします。`Id` と `LastSequenceNumber` を保存し、`ResumeBackgroundRun(id).StreamAsync(startingAfter: cursor)` で再接続します。遠隔ジョブの停止は `CancelAsync` です。取得・読み取りトークンのキャンセルはそのクライアント操作だけを止めます。`LastResponse` のテキスト、状態、使用量、引用、`OutputJson` を参照し、回答利用前に終了状態を確認してください。

ツール、推論、画像、スキーマの互換性はモデルによります。共通の `WithFileSearch` は Perplexity のベクトルストア用アダプターではありません。サンドボックスの生成ファイル、アップロードされた添付ファイル、外部 MCP データは別のリソースであり、共通のファイル検索ストアにはなりません。

[Perplexity Agent API、検索と埋め込み](../../../../../docs/ja/perplexity.md).


### メリット
- ChatBlockがプロバイダーに対して完全に無関心（クリーンな分離）
- OOP原則に適合（サービスが自身の固有設定を管理）
- サービスインスタンス1つに固有設定1つ → シンプルな構造

### デメリット
- 1つのサービス内の複数ChatBlockに同一の固有設定が適用される

## ChatBlockレベルへの移行が必要な場合

今後 **ChatBlockごとに固有設定を独立して維持する要件** が発生した場合、ChatBlock内にLazy初期化のコンフィグクラスを追加する方式でマイグレーションします。

```csharp
// 例（現在は未実装）
public class ChatBlock
{
    private GeminiConfig _gemini;
    public GeminiConfig Gemini => _gemini ??= new GeminiConfig();
}

// 使用
chatBlock.Gemini.ThinkingBudget = 1024;
```

### この方式が必要なシナリオ
- 1つのサービスインスタンスでChatBlock AとBが異なるThinkingBudgetを使用する必要がある場合
- 実際にはこのケースは非常に稀なため、現在はサービスレベルを維持

## 決定ログ

- **2026-02-12**: 最初 Option B（ChatBlockレベル）で実装後、サービスレベルにロールバック。固有設定はサービスに置くのが自然と判断。

## 実行の制御が必要になる場面

時間のかかる処理では、進行状況を表示したり、ユーザーが途中で条件を変更できるようにしたりする必要があります。`StartRunAsync` が返す `AIRun` でその処理を制御し、実行中の追加指示に対応するかどうかはプロバイダーが決定します。モデル設定はサービスで開始前に構成し、追加指示の前に `run.CanSteer` を確認します。利用場面、例、キャンセル、互換性は [Run の利用ガイド](../../../../../docs/ja/execution-api-transition.md)を参照してください。

## 独自プロバイダーの実装

公開サービスプロパティは実行中も既定値を表します。独自の`AIService`派生クラスは送信データの構築に`RequestTemperature`、`RequestTopP`、`RequestMaxTokens`、`RequestSystemMessage`、`RequestModel`、`RequestFunctions`などのprotectedアクセサーを使ってください。`Temperature`を直接読むとビルダーの変更を反映できません。独自の既定値は`CaptureRequestSettings`でbase実装を呼んでから保存し、変更可能なコレクションをコピーします。値は`RequestSetting<T>`で読みます。別のオプションオブジェクトをキャプチャする場合は`CloneProviderRequestOptions`でコピーします。base実行を経由しない既存overrideは`BeginRequestSettingsScope()`に入り、従来の機能スコープも保持します。これは実装の拡張契約であり、`IAIService`に必須メンバーを追加しません。

独自のツール実行部は既存のprotected virtual `ProcessFunctionCallAsync(FunctionCall)`を引き続きオーバーライドできます。protected `FunctionCancellationToken`をI/Oや`HandlerWithCancellation`に渡すと実行のキャンセルが伝わります。従来の`Handler`デリゲートを直接呼ぶ場合は`CancellationToken.None`を使用します。標準の実行部はすでにキャンセル対応の経路を選びます。[ツール契約](../../../../../docs/ja/function-calling.md#tool-execution-contract)を参照してください。

完成した回答と停止ボタンだけなら`GetCompletionAsync`に`cancellationToken`を渡します。進捗イベントや対応モデルへの追加指示にはRunを使います。[完了要求のキャンセル](../../../../../docs/ja/completions.md#completion-cancellation)を参照してください。

このキャンセル契約は Mythosia.AI 8.0.0 / Mythosia.AI.Abstractions 4.0.0 に含まれます。トークン省略や従来のprofile/context位置引数はソース上で有効ですが、利用側は再ビルドが必要です。独自の`IAIService`実装では両完了メソッドの末尾に`CancellationToken cancellationToken = default`を追加して伝播します。`AIService`派生プロバイダーは既存の`GetCompletionAsync(Message)` overrideを維持し、protectedの`RequestCancellationToken`を通信に渡します。ビルダーとRun自体にはこのインターフェース変更は不要でした。 文字列・profile/contextの完了、画像ヘルパー、`RunAgentAsync`など変更されたpublic virtualオーバーロードを再定義する派生クラスも、新しい`CancellationToken`を末尾に追加して伝播します。従来のシグネチャを維持するのは単一の`Message`を受け取るprovider overrideです。変更されたメソッドをデリゲートに直接渡すコードは、トークンを渡すか省略する明示的なラムダへの変更が必要な場合があります。

[共通の対応定義でモデルの機能選択を構成する](../../../../../docs/ja/model-capabilities.md).

独自プロバイダーのプロファイルがネイティブモードのフラグを変更する場合は、`ApplyCapabilityRequestProfile(AIRequestProfile)` をオーバーライドし、リゾルバーに必要なフラグだけを `SetExecutionSetting(...)` で適用します。既定のフックは何もしません。共通プロファイル設定はビルダーが取得済みであり、照会は `ApplyRequestProfile` や `ApplyProviderSpecificRequestProfile` を呼び出しません。このフックで検証、コールバック、シリアライズ、予算予約、サービスや呼び出し元の状態変更を行わないでください。一時設定は照会終了時に復元され、オーバーライドが例外を送出した場合も同様です。
