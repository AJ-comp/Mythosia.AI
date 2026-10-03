# プロバイダー固有設定アーキテクチャ

> GPT-6.1 Sol: Mythosia.AI 8.2.0 / Abstractions 4.2.0 が必要です。[モデル選択と移行](https://github.com/AJ-comp/Mythosia.AI/blob/main/docs/ja/providers.md#gpt-61-sol)

> GPT-6 Sol/Luna は Mythosia.AI 8.1.0 / Abstractions 4.1.0 から利用できます。

回答・使用量・出典をまとめて取得するには、`await run.Result` が返す `AIRunResult` を使用します。文字列は `result.Text` で取得でき、ストリームを読む必要はありません。Mythosia.AI 8.0.0 / Mythosia.AI.Abstractions 4.0.0 の API 変更です。`GetCompletionAsync` と `StructuredStreamRun<T>.Result` の戻り値型は維持します。 [Run の結果と移行](../../../../../docs/ja/execution-api-transition.md#run-result).


設定をリクエストごとに分離し、共通設定から分岐するには[リクエストビルダー](../../../../../docs/ja/request-building.md)を使います。`CreateRequest(...)`の後に`With...`をつなぎます。サービスのプロパティとfluentメソッドは従来の動作を維持します。

> [Claude Fable 5.1](../../../../../docs/ja/fable-5-1.md) は `Mythosia.AI` 8.0.0 / `Mythosia.AI.Abstractions` 4.0.0 から進捗更新、ターン限定指示、thinking binding 診断を利用できます。Mythos 5.1 は招待制です。両モデルともツール選択の強制を拒否します。

> GPT-6 Astra、`AllowAsync`、`StartRunAsync`、共通の推論・検索 API は `Mythosia.AI` 7.1.0 から利用でき、共通型は `Mythosia.AI.Abstractions` 3.1.0 に含まれます。

<a id="claude-sonnet-55"></a>

## Claude Sonnet 5.5

`AIModels.Anthropic.ClaudeSonnet5_5` (`claude-sonnet-5-5`) はテキスト・画像入力とテキスト出力に対応し、コンテキストは 1M、最大出力は 128K トークンです。Mythosia.AI 8.2.0 / Abstractions 4.2.0 が必要です。既存の既定モデルとモデル識別子は変わりません。

未設定時は adaptive 推論、`High` effort、読み取り可能な推論の省略が既定です。Adaptive は `Low`、`Medium`、`High`、`XHigh`、`Max` を受け付け、`Minimal` は拒否します。`MaxTokens` は推論と回答を含みます。サンプリング引数は送信しません。

```csharp
using Mythosia.AI.Models;
using Mythosia.AI.Models.Streaming;
using Mythosia.AI.Services.Anthropic;

var claude = new AnthropicService(apiKey, httpClient);
claude.ChangeModel(AIModels.Anthropic.ClaudeSonnet5_5);
claude.WithAdaptiveThinkingParameters(
    ClaudeReasoningEffort.High, ClaudeThinkingDisplay.Updates);

await using var run = await claude.CreateRequest("Review the plan using the registered tools.")
    .WithReasoning(ReasoningLevel.High)
    .StartRunAsync(options: StreamOptions.FullOptions);
await foreach (var item in run.StreamAsync())
{
    if (item.Type == StreamingContentType.Reasoning)
        Console.WriteLine(item.Content);
    else if (item.Type == StreamingContentType.Text)
        Console.Write(item.Content);
}
string answer = (await run.Result).Text;
```

Adaptive では `ClaudeThinkingDisplay.Updates` でツールの進捗、`Summarized` で推論要約を要求します。`StreamingContentType.Reasoning`、通常の完了では `LastThinkingContent` を確認します。Adaptive ヘルパーの display 引数の既定値は `Summarized` で、未設定時とは異なります。`between_tools` は進捗を自動で返します。一定間隔の通知は保証されません。

`ReasoningLevel.None`、無効化した従来の `ThinkingBudget`、`AIRequestProfile.DisableReasoning` は high effort の `between_tools` を選択します。事前推論は無効になりますが、ツールの進捗は thinking ブロックで返る場合があります。`WithBetweenToolsThinking(...)` は `Auto`（high）、`Low`、`Medium`、`High` に対応し、`XHigh` と `Max` は拒否します。thinking オブジェクトには `type` だけを送信し、display、budget、binding は送りません。このモードはメッセージごとの effort 変更と `CachePreservation.Required` に対応しません。共通の `WithReasoning(Low...Max)` は adaptive に戻し、`Auto` は選択済みのプロバイダーモードを維持します。

```csharp
claude.StartNewConversation(AIModels.Anthropic.ClaudeSonnet5_5);
claude.WithBetweenToolsThinking(ClaudeReasoningEffort.Low);
string quick = await claude.CreateRequest("Use the registered tools to check the status.")
    .GetCompletionAsync();

// A separate conversation using request-scoped high-effort between_tools.
claude.StartNewConversation(AIModels.Anthropic.ClaudeSonnet5_5);
string next = await claude.CreateRequest("Give me the latest status.")
    .WithReasoning(ReasoningLevel.None)
    .GetCompletionAsync();
```

Claude はモデル、リクエストの目的、推論と thinking binding の設定をまとめて決定します。`RequestProfiles.Summarization` や `RequestProfiles.QueryRewrite` などの補助プロファイルでは、`DisableReasoning = true`、目的が `Default` 以外、かつ実際のリクエストがステートレスの場合に限り、継承した binding を省略します。保持する会話プレフィックスがないリクエストで、継承したポリシーが推論を再度有効にしたりリクエストを拒否したりするのを防ぎます。推論を無効にできるモデルでは無効化し、常時推論する Opus 5.5、Fable 5.1、Mythos 5.1 では `Low` を使い、読み取り可能な thinking を省略します。Sonnet 5.5 では high effort の `between_tools` を使います。

完了、ストリーミング、構造化出力、Run は同じ順序でリクエストを準備します。設定を取得し、実際のプロファイル処理を一度適用してから、最終的な共通オプションとプロバイダー固有オプションを検証します。自動要約、新しい入力の履歴追加、通信開始より前に行うため、カスタムプロバイダーのプロファイルオーバーライドも検証対象に反映されます。Claude で実際に使用する手動 `ThinkingBudget` がモデルの出力上限以上なら、この段階で拒否します。有効なプロファイルと共通推論設定の優先順位は変わりません。

アプリケーションからの呼び出しは独立した論理リクエストを開始します。`SystemMessageProvider` やツールのコールバック内からの通常の呼び出し、同じ `AIRequestProfile`・`Message` を再利用する呼び出しも含みます。オブジェクトの再利用は実行の共有を意味しません。フレームワーク内部の委譲、ツール処理、再試行、形式修正は元のリクエストを継続し、プロファイルは一度だけ適用します。通常の子リクエストは独自のオプションとサービス既定値を取得し、ビルダーは取得済み設定を使います。成功・失敗・キャンセル後に親の実行状態を復元します。フレームワークからの呼び出しを転送するプロバイダーのオーバーライドには、[プロバイダーアダプターの規則](../../../../../docs/ja/request-building.md#provider-request-adapters)が適用されます。 フレームワークが呼び出した仮想プロバイダーアダプターでは、対応する基底メソッドへの最初の呼び出しが、入力の `Message` を置き換えた場合も準備済みリクエストを継続します。転送前に無関係な補助処理で同じ基底メソッドを呼び出す場合は、その呼び出しと `await`（ストリーミングでは列挙全体）を `BeginIndependentRequestScope()` のスコープで囲みます。

組み込みプロバイダーは組み込み入力コンテンツの独立したコピーを保持します。同じ `Message` を再利用しても、その呼び出しのコンテキストとターン指示を適用し、受理済みの履歴は書き換えません。カスタムコンテンツと未対応のメタデータオブジェクトは所有者が管理してください。同じ会話への並行呼び出しを安全にするものではありません。

`StartRunAsync` が戻った後も、Run は最終設定の独自のコピーを保持します。呼び出し元のプロファイルを復元しても実行中の Run は変わらず、実行用プロファイルフックも一度だけ呼び出されます。

ステートレスな補助リクエストは独立した会話を使用し、親の出力スキーマ、ホスト型ツール、一回限りのオプションを継承しません。この分離によってネイティブオプションの検証を省略することはなく、OpenAI・Perplexity の Run にも適用します。親の設定、メッセージ、`CurrentSummary`、リクエストの観測情報は保持します。ステートフルなリクエストでは binding と会話の検証を維持します。既存の公開 API は変わりません。

ライブラリが自動生成する会話要約では、親の `SystemMessageProvider` コールバックとリクエストコンテキストも除外します。継承した `RequestMessageOverride` が内部の要約プロンプトを置き換えることを防ぐためです。アプリケーションが明示的に文章の要約を依頼する場合を含め、通常のリクエストでは動的コンテキストを従来どおり適用します。

ステートレスなリクエストは親の会話の自動要約も実行しません。従来の `GetCompletionAsync(string, profile)` オーバーロードでも、`Message` オーバーロードやリクエストビルダーと同じ動作になります。親の `CurrentSummary` とメッセージは変わりません。状態を保持するリクエストでは通常の自動要約を維持します。

`(ClaudeReasoningEffort)1234` などの未定義の `ClaudeReasoningEffort` 値は、実際の推論設定がそのネイティブ effort を使用する場合にローカルで拒否されます。拒否されたリクエストは親の会話を自動要約せず、未送信の入力を履歴に残しません。有効な明示的な共通推論設定やプロファイルによる上書きは、従来どおりネイティブの基本設定より優先されます。

`ClaudeThinkingMode`: `Auto` / `Adaptive` / `BetweenTools`; `AnthropicService.ThinkingMode`.

履歴は追記のみとしてください。空のブロックと `progress_updates` を含む署名付き thinking をターン間とツール結果間で保持します。保存した assistant 応答の書き換えは `ClaudeThinkingPrefixMismatchBehavior.DropBlock` でもローカルで拒否します。過去の user/system/tool プレフィックス編集は自動でローカル拒否せず、Anthropic の binding ポリシーに従って送信します。Adaptive の `WithThinkingBinding(ClaudeThinkingPrefixMismatchBehavior.Error)` は厳密なプロバイダー検証を要求し、不正なプレフィックスでは HTTP 400 になり得ます。`DropBlock` は該当する推論の破棄を許可し、null はプロバイダーの既定値を使います。`LastInputTransformations` で報告された破棄を確認してください。`between_tools` は binding 制御に非対応です。新しい指示には `WithTurnInstruction` / `WithConversationInstruction` を使用します。Adaptive の `CachePreservation.Required` も過去の編集の安全性を保証しません。

既存の完了、ストリーミング、構造化出力、画像、ローカル関数、Web 検索、通常の Run API を使用します。`ForceFunctionName` は未設定にします。強制ツール選択（`any` / `tool`）と assistant prefill は HTTP 前に拒否し、自動選択と `FunctionsDisabled` は利用できます。`Fast`、ネイティブ非同期ツール、Run steering は非対応です。Computer toolset、advisor ツール、ネイティブ圧縮、会話中のツール変更、自動サーバー fallback は統合していません。モデル・アカウントの変更で bound thinking が失われる場合があり、リクエスト成功だけでは推論保持を保証しません。

Mythosia.AI 8.2.0 の既知の制限: Sonnet 5.5 と Opus 5.5 では、未実行の `server_tool_use` で終わる有効な `pause_turn` 応答を assistant prefill と誤判定し、2 回目の HTTP リクエスト前に拒否します。完了したサーバーツールの結果で終わる継続は既存の検証を通過しています。[ネイティブ継続の制限](https://github.com/AJ-comp/Mythosia.AI/blob/main/docs/ja/providers.md#claude-native-continuation-limitation)を参照してください。

共通の構造化出力 API はスキーマ指示、逆シリアル化、修復リトライを使用し、ネイティブの `output_config.format` スキーマ制約は送信しません。

[公式モデル情報](https://platform.claude.com/docs/en/models/sonnet-5-5/overview) · [移行](https://platform.claude.com/docs/en/models/sonnet-5-5/migration-guide) · [プロバイダーの変更](https://platform.claude.com/docs/en/models/sonnet-5-5/whats-new-sonnet-5-5).


## Claude のトークン計算

引数なしの `GetInputTokenCountAsync()` はツールに対応した完了メッセージのシリアル化を再利用し、現在ツールが無効でも assistant の `tool_use` と user の `tool_result` ブロックを保持します。インポートした従来の並列ツール記録が同じ `OriginalContent` バッチを共有する場合、そのバッチを一度だけシリアル化し、署名付きコンテンツと過去の指示を保持します。現在有効なツール定義と `tool_choice` を含め、生成専用フィールドは省略し、トークン計算固有の thinking 制限を維持します。`GetInputTokenCountAsync(string prompt)` は独立したプロンプトのトークンを計算する従来の動作を維持し、保存した会話履歴やツール定義は含めません。

通常の完了、thinking を保持する継続リクエスト、トークン計算は同じツール履歴変換を使用します。関数を無効化または削除した後の通常リクエストでも、過去の呼び出しと結果はネイティブのツールブロックとして保持します。インポートした `FunctionSource` メタデータは、呼び出しと結果の両方で同じ意味の定義済み enum、整数、文字列、JSON 表現を受け付けます。同じ並列 assistant バッチの重複記録は、ツールの順序、署名、プロバイダーフィールドを変更せず一度だけ出力します。メタデータ表現だけの変更で、受理済みプレフィックスが重複することはありません。

Claude はシリアライズ、トークン計算、要約圧縮の保護で同じ元の assistant コンテンツを確認します。ネイティブコンテンツ、型付きバッチ、従来の `Message.Metadata[OriginalContent]` を含むため、インポート形式にかかわらず署名付き thinking のプレフィックスを同じように保護します。これはローカル履歴の保持であり、実サーバーによる署名の受理を保証しません。

## 原則

アプリケーションは[共通の推論・検索 API](https://github.com/AJ-comp/Mythosia.AI/blob/main/docs/ja/reasoning-and-search.md)で、作業に必要な推論量とホスト型検索を指定できます。`AIRequestFeatures` は論理的なリクエストごとにコピーされ、プロバイダーのアダプターが検証・変換します。プロバイダー固有の既定値はサービスに残り、キャッシュを維持する変更の状態は追跡中の会話に保存されます。`AICitation` はストリームの購読とは独立して出典を保持します。カスタムサービスは任意の `IAIRequestFeatureService` で対応し、`IAIService` に必須メンバーは追加されません。

| 設定タイプ | 配置場所 | 例 |
|------------|----------|-----|
| **共通設定** | `ChatBlock` | Temperature, TopP, MaxTokens, FrequencyPenalty 等 |
| **プロバイダー固有** | 各サービスクラス | ThinkingLevel/ThinkingBudget (Gemini), ReasoningEffort (GPT) 等 |
| **関数ごとの実行許可** | `FunctionDefinition` | `AllowAsync`（既定値 `false`） |

`AllowAsync` は呼び出し側が選ぶ許可であり、モデルと API の対応状況はサービスが内部で判断します。`FunctionBuilder.WithAsync()` と `[AiFunction("lookup", "データを取得", AllowAsync = true)]` でも同じ許可を有効にできます。GPT-6.1 Sol / GPT-6 Astra / Sol / Luna では Responses で使用し、未対応のモデルでは API オプションを省略して同じハンドラーの結果を待ちます。設定した許可の値は変更しません。

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

Perplexity はプロファイル適用後の同じ最終リクエスト計画で検証とシリアライズを行います。抑制された親のツールや推論設定で補助 Sonar リクエストを拒否せず、実際に適用するオプションは引き続き検証します。プリセットとフォールバック一覧のモデル選択も維持します。

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

`ApplyRequestProfile` と `ApplyProviderSpecificRequestProfile` は論理リクエストごとに一度実行し、最終設定を検証します。アプリケーションからの通常の呼び出しは、コンテキストやツールのコールバック内でも独立したリクエストになります。フレームワークが呼び出した仮想プロバイダーアダプターでは、対応する基底メソッドへの最初の呼び出しが、入力を置き換えた場合も準備済みリクエストとその設定を継続します。転送前に同じ基底メソッドで無関係な補助処理を行う場合は `BeginIndependentRequestScope()` を使います。[プロバイダーアダプターの規則](../../../../../docs/ja/request-building.md#provider-request-adapters)を参照してください。カスタムプロバイダーは `BeginRequestFeaturesScope` の後で追加の protected フック `ResolveRequestMessage(message)` を呼び、返された入力コピーを保持できます。既存のオーバーライドとの互換性は維持します。機能照会は副作用のない別のフックを使用します。

独自プロバイダーのプロファイルがネイティブモードのフラグを変更する場合は、`ApplyCapabilityRequestProfile(AIRequestProfile)` をオーバーライドし、リゾルバーに必要なフラグだけを `SetExecutionSetting(...)` で適用します。既定のフックは何もしません。共通プロファイル設定はビルダーが取得済みであり、照会は `ApplyRequestProfile` や `ApplyProviderSpecificRequestProfile` を呼び出しません。このフックで検証、コールバック、シリアライズ、予算予約、サービスや呼び出し元の状態変更を行わないでください。一時設定は照会終了時に復元され、オーバーライドが例外を送出した場合も同様です。
