# プロバイダー固有機能

> `CreateRequest`の例にはMythosia.AI 8.0.0 / Abstractions 4.0.0が必要です。Runと共通リクエスト機能を導入した旧7.1リリースにはビルダーがありません。旧パッケージでは既存のサービスオーバーロードを使えます。

<a id="image-options-migration"></a>
対応する処理モードと返される情報はプロバイダー・モデル・API によって異なります。[共通の速度設定](request-building.md#inference-speed)と capability を確認し、Fast の要求と実際の適用を区別してください。

## 画像オプションの型への移行

品質や形式を列挙型の補完から選び、正確なピクセル数と解像度クラスを区別します。文字列の入力ミスを減らし、指定サイズが別の解像度へ暗黙変換されるのを防ぎます。

Mythosia.AI 8.0.0 での破壊的変更です。`Quality`、`Background`、`OutputFormat`はenum、`Size`は`ImageSize`になり、リクエストの独立した`AspectRatio`プロパティは削除されます。`OutputFormat`の既定値は`ImageOutputFormat.Auto`です。生成・編集メソッドは変わりません。

**Before**

```text
var request = new ImageGenerationRequest
{
    Prompt = "A glass pavilion at sunrise",
    Quality = "max",
    Background = "transparent",
    OutputFormat = "png",
    Size = "1536x1024"
};

// Google / xAI
Size = "2K";
AspectRatio = "3:2";
```

**After**

```csharp
using Mythosia.AI.Models;
using Mythosia.AI.Models.Images;

var request = new ImageGenerationRequest
{
    Model = AIModels.OpenAI.GptImage2_5Sunburst,
    Prompt = "A glass pavilion at sunrise",
    Quality = ImageQuality.Max,
    Background = ImageBackground.Transparent,
    OutputFormat = ImageOutputFormat.Png,
    Size = ImageSize.Pixels(1536, 1024)
};

// Google / xAI
var presetRequest = new ImageGenerationRequest
{
    Prompt = "A glass pavilion at sunrise",
    Size = ImageSize.Preset(ImageResolution.TwoK, ImageAspectRatio.ThreeByTwo),
    OutputFormat = ImageOutputFormat.Auto
};
```

`Pixels(width, height)`は正確な寸法を要求します。`Preset(resolution, aspectRatio)`は解像度クラスと比率を指定し、実際のピクセル数は提供者が決めます。サイズ制約がなければ`ImageSize.Auto`を使用します。概算サイズでよい場合だけピクセル指定からプリセットに移行してください。

| Provider | `Size` | `OutputFormat` |
| --- | --- | --- |
| OpenAI | `ImageSize.Auto`, `Pixels(...)` | `Auto` → PNG, `Png`, `Jpeg`, `WebP` |
| Google | `ImageSize.Auto`, `Preset(...)` | `Auto`, `Jpeg` |
| xAI | `ImageSize.Auto`, `Preset(...)` | `Auto` |

未定義のenum値やモデル非対応の組み合わせはHTTP送信前に拒否されます。すべてのモデルがすべての値をサポートするわけではありません。Googleの品質は`ImageQuality.Auto`のみ、xAIは`Auto`、`Low`、`Medium`です。

`EditImagesAsync`が`Task`を返した後は、入力バッファを再利用できます。開始済みのリクエストはOpenAIのマスクを含む画像バイトを独立して保持するため、元の`ImageInput.Data`配列を後から変更してもアップロード内容は変わりません。

中断された出力を完成した画像として保存しないよう、Googleの生成・編集では、返されたすべての候補が`finishReason: STOP`で終了する必要があります。1つでもブロック、未完了、または終了状態の欠落があれば、呼び出し全体が`AIServiceException`をスローします。 インラインのbase64データや画像MIME情報が欠落・不正な場合も、呼び出し全体が失敗し、PNGと推測しません。この検証では、実際のファイルバイトが宣言された画像形式と一致するかまでは確認しません。

## OpenAI (OpenAIService)

> GPT-6 Astra と非同期ツール呼び出しは `Mythosia.AI` 7.1.0 から利用でき、共通型は `Mythosia.AI.Abstractions` 3.1.0 に含まれます。

天気の取得に時間がかかるときでも、その結果に依存しない一般的な旅行の持ち物は先に説明できます。モデルによる非同期ツール呼び出しは、このように待ち時間に独立した作業を進めるために使います。結果に依存する判断は、結果が届いてから行う必要があります。

`FunctionDefinition.AllowAsync = true` または `FunctionBuilder.WithAsync()` で、GPT-6 Astra / Sol / Luna の Responses API による非同期ツール呼び出しを選択的に許可できます。既定値は `false` で、未対応のモデルでは同じハンドラーの結果を待ちます。例とリクエストの有効期間は[関数呼び出しガイド](function-calling.md)を参照してください。

プロバイダー間で推論レベルを指定し、最新情報や索引済みの文書を回答の根拠にする方法は、[推論と検索のガイド](reasoning-and-search.md)を参照してください。対応モデル、キャッシュ保持、設定の組み合わせの制限をまとめています。

<a id="gpt-6-sol-luna"></a>

### GPT-6 Sol / Luna（未リリース）

複雑なコーディング、ツール利用、エージェント処理には GPT-6 Sol を、テキストや画像入力を大量に処理しコストを抑えたい場合には Luna を選びます。既存の完了応答、ストリーミング、Run API を使えるため、モデルを切り替えても呼び出しの流れは変わりません。

> 未リリースの追加機能で、対応する core と abstractions のビルドが必要です。公開済みの Mythosia.AI 8.0.0 / Abstractions 4.0.0 には `Gpt6Sol`、`Gpt6Luna`、`Gpt6Reasoning.None` は含まれません。既存の Astra 機能の最低バージョンとサービスの既定モデルは変わりません。

`AIModels.OpenAI.Gpt6Sol` (`gpt-6-sol`) または `AIModels.OpenAI.Gpt6Luna` (`gpt-6-luna`) を選択します。両モデルはテキスト・画像入力とテキスト出力に対応し、コンテキストは 1,050,000 トークン、最大入力は 922,000、最大出力は 128,000 トークンです。入力・推論・出力の合計もコンテキストの制限内に収めます。`MaxTokens` はコンテキスト長ではなく出力の予算です。

```csharp
using Mythosia.AI.Models;
using Mythosia.AI.Services.OpenAI;

var service = new OpenAIService(apiKey, httpClient);
service.ChangeModel(AIModels.OpenAI.Gpt6Sol);
await using var run = await service.CreateRequest("Review this design.")
    .WithReasoning(ReasoningLevel.High)
    .StartRunAsync(onText: text => Console.Write(text));
var result = await run.Result;

service.ChangeModel(AIModels.OpenAI.Gpt6Luna);
string answer = await service.CreateRequest("Summarize this paragraph.")
    .WithReasoning(ReasoningLevel.None)
    .WithTemperature(0.2f)
    .GetCompletionAsync();
```

`Auto` は `Medium` になります。Sol/Luna は `None`、`Low`、`Medium`、`High`、`XHigh`、`Max` に対応し、`Minimal` には対応しません。リクエストには `WithReasoning(ReasoningLevel.None)`、`WithGpt6Parameters` には `Gpt6Reasoning.None` を指定できます。Sol/Luna の `None` の場合だけ `Temperature` / `TopP` を送信し、推論が有効な場合は省略します。Astra は常に推論が必要でサンプリングを省略します。`AIRequestProfile.DisableReasoning` は Sol/Luna では `None`、Astra では Standard モードの `Low` を使い、推論要約を省略します。

`Gpt6ReasoningMode.Standard` と `.Pro` は選択したモデル ID をそのまま使います。3 つの GPT-6 モデルは Responses のツール呼び出し、任意の非同期ツール、WebSocket Run の追加指示、Standard・単一エージェントモードでのキャッシュを保つ推論変更に対応します。追加指示前に `run.CanSteer` を確認してください。受付は既存の出力を取り消しません。`WithSpeed(InferenceSpeed.Fast)` は推論レベルとは別に有料の Fast 処理を要求します。適用結果は `result.Processing` で確認します。アカウントの権限やサーバーの通常処理への切り替えはローカルの対応状況とは別です。

[GPT-6 Sol](https://developers.openai.com/api/docs/models/gpt-6-sol) · [GPT-6 Luna](https://developers.openai.com/api/docs/models/gpt-6-luna) · [Reasoning](https://developers.openai.com/api/docs/guides/reasoning) · [Fast](https://developers.openai.com/api/docs/guides/fast-mode)

### 推論レベル

応答速度と分析の深さのバランスを調整します:

```csharp
using Mythosia.AI.Models;

// GPT-6 Astra
service.ChangeModel(AIModels.OpenAI.Gpt6Astra);
service.WithGpt6Parameters(
    reasoningEffort: Gpt6Reasoning.Medium, // Auto, Low, Medium, High, XHigh, Max
    verbosity: Verbosity.Medium,
    reasoningSummary: ReasoningSummary.Auto,
    reasoningMode: Gpt6ReasoningMode.Standard);

// GPT-5.6: Sol は最上位モデルで、Terra と Luna は低コストの選択肢です。
service.ChangeModel(AIModels.OpenAI.Gpt5_6Sol);
service.WithGpt5_6Parameters(
    reasoningEffort: Gpt5_6Reasoning.Medium, // None, Low, Medium, High, XHigh, Max
    verbosity: Verbosity.Medium);            // Low, Medium, High

// GPT-5.4シリーズ
service.ChangeModel(AIModels.OpenAI.Gpt5_4);
service.Gpt5_4ReasoningEffort = Gpt5_4Reasoning.High; // None, Low, Medium, High, XHigh

// GPT-5.2シリーズ
service.ChangeModel(AIModels.OpenAI.Gpt5_2);
service.Gpt5_2ReasoningEffort = Gpt5_2Reasoning.Medium;

```

GPT-6 Astra は既定で Responses API を使用し、関数呼び出しにもこの API が必要です。`Auto` はライブラリの既定値 `Medium` に解決され、`None` と `Minimal` は使用できません。`AIRequestProfile.DisableReasoning = true` は`Standard` モードで推論を `Low` に設定し、推論要約を省略します。`Gpt6ReasoningMode.Pro` を選択すると、同じモデル ID `gpt-6-astra` で Pro モードを使用できます。

### テキスト音声変換 (TTS)

```csharp
byte[] audio = await service.GetSpeechAsync(
    inputText: "こんにちは！",
    voice: "alloy",   // alloy, echo, fable, onyx, nova, shimmer
    model: "tts-1"
);

await File.WriteAllBytesAsync("output.mp3", audio);
```

### 音声テキスト変換 (STT)

```csharp
byte[] audioData = await File.ReadAllBytesAsync("recording.mp3");

string transcript = await service.TranscribeAudioAsync(
    audioData: audioData,
    fileName: "recording.mp3",
    language: "ja"  // オプション、ISO-639-1
);
```

`TranscribeAudioAsync` は `gpt-transcribe` を使用します。公開シグネチャは変わりません。

### 画像生成

#### GPT Image 2.5

ビジュアル案を素早く作るなら Flare、細かな編集指示を正確に反映したいなら Sunburst を選びます。両モデルとも既存の `IImageGenerationService` で画像の生成と編集に対応し、画像モデルを選んでもチャットモデルは変わりません。

| モデル | 選ぶ場面 |
| --- | --- |
| `AIModels.OpenAI.GptImage2_5Flare` | 高速で高品質な日常的な画像生成。 |
| `AIModels.OpenAI.GptImage2_5Sunburst` | 編集の精度を重視する画像生成・修正。 |

`ImageGenerationRequest.Model`、または継承先の `ImageEditRequest.Model` に明示します。OpenAI の既定値は `AIModels.OpenAI.GptImage2` のままです。エイリアスは `gpt-image-2.5-flare` と `gpt-image-2.5-sunburst`。2026年9月8日のスナップショットに固定するには `GptImage2_5Flare_260908` または `GptImage2_5Sunburst_260908` を使います。対応する ID の末尾は `-2026-09-08` です。

Flare で案を生成します。

```csharp
using Mythosia.AI.Models;
using Mythosia.AI.Models.Images;
using Mythosia.AI.Services;
using Mythosia.AI.Services.OpenAI;

IImageGenerationService images = new OpenAIService(apiKey, httpClient);
var generated = await images.GenerateImagesAsync(new ImageGenerationRequest
{
    Model = AIModels.OpenAI.GptImage2_5Flare,
    Prompt = "日の出のガラス製パビリオン、建築のコンセプトアート",
    Size = ImageSize.Pixels(1024, 1024),
    Quality = ImageQuality.Low,
    OutputFormat = ImageOutputFormat.Png
});

await File.WriteAllBytesAsync("pavilion.png", generated.Images[0].Data);
```

続いて Sunburst で生成画像を編集します。

```csharp
var edited = await images.EditImagesAsync(new ImageEditRequest
{
    Model = AIModels.OpenAI.GptImage2_5Sunburst,
    Prompt = "パビリオンのデザインを維持し、周囲の風景を取り除いて背景を透明にしてください。",
    InputImages = new[]
    {
        new ImageInput(generated.Images[0].Data, "image/png", "pavilion.png")
    },
    Size = ImageSize.Pixels(1024, 1024),
    Quality = ImageQuality.XHigh,
    Background = ImageBackground.Transparent,
    OutputFormat = ImageOutputFormat.Png
});

await File.WriteAllBytesAsync("pavilion-cutout.png", edited.Images[0].Data);
```

両モデルと各スナップショットの `Quality` は `Auto`、`Low`、`Medium`、`High`、`XHigh`、`Max` に対応します。下書きには低い品質を使い、完成品では高い設定を比較してください。`OutputFormat` は `Auto` / `Png`、`Jpeg`、`WebP`。`OutputCompression` は JPEG/WebP のみ 0–100 です。背景 `Transparent` には PNG/WebP が必要で、`Count` は 1–10 です。

`Size`は`ImageSize.Auto`または`ImageSize.Pixels(width, height)`です。両辺は16の倍数、比率は1:3～3:1、各辺は3840以下、面積は655360～8294400ピクセルです。2560×1440を超えるサイズは実験的です。OpenAIは`Preset`を拒否します。

編集には空でない JPEG/PNG/WebP を1–16枚、各50 MiB未満で渡せます。任意のマスクは50 MiB未満の PNG/WebP で、最初の参照画像と形式・ピクセル寸法を合わせ、アルファチャンネルを持つ必要があります。ライブラリは MIME とバイト長を検証し、ピクセル寸法とアルファはプロバイダーが検証します。

この例は既存の Image API のバイト返却と multipart 編集経路を使います。Responses の `image_generation` ツール、部分画像ストリーミング、`input_fidelity` はこの連携では公開していません。結果の `GeneratedImage.Data` と `MediaType` を参照してください。

公式の[画像ガイド](https://developers.openai.com/api/docs/guides/image-generation)、[Sunburst](https://developers.openai.com/api/docs/models/gpt-image-2.5-sunburst)、[Flare](https://developers.openai.com/api/docs/models/gpt-image-2.5-flare) を参照してください。

---

## Anthropic (AnthropicService)

[Claude Fable 5.1](fable-5-1.md) は `Mythosia.AI` 8.0.0 / `Mythosia.AI.Abstractions` 4.0.0 から進捗更新、ターン限定指示、thinking binding 診断を利用できます。Mythos 5.1 は招待制です。両モデルともツール選択の強制を拒否します。

<a id="claude-opus-55"></a>

### Claude Opus 5.5: 長いツール処理の進行状況を表示する

複数回のツール呼び出しを伴うコードレビューや文書調査に Opus 5.5 を使えます。既存の completion・Run API を使いますが、既定では進行状況が非表示で、推論を保持する際は履歴変更にも注意が必要です。この対応は開発中であり、公開済みの 8.0.0 / 4.0.0 パッケージには含まれません。

`ClaudeOpus5_5` は `claude-opus-5-5` を選択します。テキスト・画像入力、テキスト出力、1M コンテキスト、最大 128K 出力トークンに対応します。2026-09-24 時点の通常入出力料金は 100 万トークン当たり $4/$20 で、特殊モードやツールは別料金です。 [公式モデル情報](https://platform.claude.com/docs/en/models/opus-5-5/overview).

サービス設定を変更しなければ、`Auto` は `Medium` effort を使い、読める推論を省略します。Adaptive thinking は常時有効です。`Low`、`Medium`、`High`、`XHigh`、`Max` を指定でき、共通の `ReasoningLevel.None` と `Minimal` は拒否します。サービスの既定モデルは変更しません。

```csharp
using Mythosia.AI.Models;
using Mythosia.AI.Models.Streaming;
using Mythosia.AI.Services.Anthropic;

var claude = new AnthropicService(apiKey, httpClient);
claude.ChangeModel(AIModels.Anthropic.ClaudeOpus5_5);
claude.WithAdaptiveThinkingParameters(
    ClaudeReasoningEffort.Medium, ClaudeThinkingDisplay.Updates);

await using var run = await claude.StartRunAsync(
    "Review the migration plan using the registered tools.",
    options: StreamOptions.FullOptions);

await foreach (var item in run.StreamAsync())
{
    if (item.Type == StreamingContentType.Reasoning)
        Console.WriteLine(item.Content);
    else if (item.Type == StreamingContentType.Text)
        Console.Write(item.Content);
}
string answer = (await run.Result).Text;
```

例では `Updates` を指定し、`StreamingContentType.Reasoning` を観察します。推論要約には `Summarized`、非表示には `Omitted` を使います。`WithAdaptiveThinkingParameters(effort)` の表示引数は引き続き `Summarized` が既定で、未設定のサービスとは異なります。通常の completion では呼び出し後に `LastThinkingContent` を読みます。一定間隔での更新は保証しません。

従来の正の `ThinkingBudget` は正確なトークン予算ではなく high/xhigh/max effort に変換します。0 や負の値でも推論は無効にできません。推論を無効化するプロファイルは low effort と表示省略を使います。`MaxTokens` は非表示の推論と回答の両方を含むため、移行時に出力上限と費用を再評価してください。

Mythosia は空のものも含め、署名付き thinking ブロックを通常の会話とツール結果の間で保持します。同じサービスと会話を続け、推論保持を期待する場合は以前のメッセージ・system・tools を書き換えないでください。`WithTurnInstruction`、`WithConversationInstruction`、`CachePreservation.Required` を利用できます。`WithThinkingBinding` の `Error` / `DropBlock` と `LastInputTransformations` で破棄を扱います。Drop は推論の破棄です。[履歴ガイド](fable-5-1.md)の共通操作を参照し、既定値とモデル互換性には Opus 5.5 の規則を適用してください。

`ForceFunctionName` は未設定にします。通常のツール選択と `FunctionsDisabled` は利用可能です。Assistant prefill は拒否し、sampling パラメーターは送信しません。Opus 5.5 は Fable/Mythos の thinking を読めませんが、Claude API の Fable 5.1 と Mythos 5.1 は Opus 5.5 の thinking を読めます。モデル切り替えで推論が失われる場合があります。今回の追加は ネイティブ computer toolset、task budget、会話内ツール変更、サーバー圧縮、自動サーバー fallback を公開しません。 [移行条件](https://platform.claude.com/docs/en/models/opus-5-5/migration-guide) · [ネイティブ機能の範囲](https://platform.claude.com/docs/en/models/opus-5-5/whats-new-opus-5-5).

Opus 5.5 で保存済み assistant 応答の内容を直接編集すると、HTTP リクエスト前に `InvalidOperationException` になります。`DropBlock` でも署名付き応答の書き換えは許可しません。訂正は新しいユーザー入力として送るか、新しい会話を開始してください。以前の user/system 内容の編集には、プロバイダーの接頭部 binding ポリシーが適用されます。

Opus 5.5 fast mode は必要な権限がある直接 Claude API で [WithSpeed](request-building.md#inference-speed) から使えます。指定した推論レベルを保持し、有料モードを要求します。

### トークンカウント（ネイティブAPI）

`GetInputTokenCountAsync`はすべてのプロバイダーで利用可能です（[基本的な補完](completions.md#トークンカウント)参照）。Anthropicの実装は公式の`messages/count_tokens`エンドポイントを呼び出し、ローカル推定の代わりに**正確な**トークン数を返します:

```csharp
uint tokens = await service.GetInputTokenCountAsync("プロンプト内容");
uint total = await service.GetInputTokenCountAsync();
```

---

## Google (GoogleAIService)

長い文書のレビューやツールを繰り返し呼び出す処理には、Gemini 3.7 Flash または 3.8 Flash を選択できます。既存の Google アダプターで `Mythosia.AI` 8.0.0 / `Mythosia.AI.Abstractions` 4.0.0 から利用でき、サービスの既定モデルは Gemini 3.6 Flash のままです。

### 思考レベル

```csharp
using Mythosia.AI.Extensions;
using Mythosia.AI.Models;
using Mythosia.AI.Models.Enums;
using Mythosia.AI.Services.Google;

var gemini = new GoogleAIService(apiKey, httpClient);
gemini.ChangeModel(AIModels.Google.Gemini3_8Flash);
// Gemini 3.7: AIModels.Google.Gemini3_7Flash
gemini.ThinkingLevel = GeminiThinkingLevel.Low;

string review = await gemini
    .CreateRequest("ローリングデプロイとブルーグリーンデプロイを、ロールバックのリスクも含めて比較してください。")
    .WithReasoning(ReasoningLevel.High)
    .GetCompletionAsync();
```

軽い初回検討には `Low`、難しいレビューには `High` を使えます。推論を増やすと遅延やトークン使用量が増える場合があります。両モデルは `Low`、`Medium`、`High` に対応し、`Minimal` と `None` は非対応です。`GeminiThinkingLevel.Auto` は上書きを省略し、3.8 のプロバイダー既定値は `Medium` です。`ThinkingLevel` はサービスの基本設定、`WithReasoning(...)` は論理リクエスト単位の上書きです。両モデルでは `temperature`、`topP`、`topK` を送信しません。プロバイダーの上限は入力 1,048,576、出力 65,536 トークンです。 [Gemini 3.7 Flash](https://ai.google.dev/gemini-api/docs/models/gemini-3.7-flash), [Gemini 3.8 Flash](https://ai.google.dev/gemini-api/docs/models/gemini-3.8-flash).

<a id="google-image-options"></a>

### Google 画像モデル別の解像度と比率

| モデル | `Resolutions` | `AspectRatios` |
| --- | --- | --- |
| `gemini-3.1-flash-image` | `Auto`, `FiveTwelve` (512), `OneK` (1K), `TwoK` (2K), `FourK` (4K) | 14 + `Auto` |
| `gemini-3.1-flash-lite-image` | `Auto`, `OneK` (1K) | 14 + `Auto` |
| `gemini-3-pro-image` | `Auto`, `OneK` (1K), `TwoK` (2K), `FourK` (4K) | 10 + `Auto` |

標準の10比率は `1:1`, `2:3`, `3:2`, `3:4`, `4:3`, `4:5`, `5:4`, `9:16`, `16:9`, `21:9` です。14比率のセットには `1:4`, `4:1`, `1:8`, `8:1` が加わります。すべてのモデルで `ImageAspectRatio.Auto` も使用できます。

`ImageSize.Auto` または `ImageSize.Preset(resolution, aspectRatio)` を使用します。`Auto` は該当する指定を送信しません。`GetImageCapabilities(model)` と `GenerateImagesAsync` / `EditImagesAsync` は同じモデル別の選択肢を使用します。非対応の明示的な値は HTTP 前に `NotSupportedException` で拒否され、リサイズや代替リクエストは行いません。不明な独自モデル ID は `Unknown` を維持し、プロバイダー共通のオプション検証後にそのまま送信します。

Flash-Lite の[モデルページ](https://ai.google.dev/gemini-api/docs/models/gemini-3.1-flash-lite-image)とガイド本文は 1K を指定していますが、[ガイドの表](https://ai.google.dev/gemini-api/docs/generate-content/image-generation#aspect_ratios_and_image_size)には 512 列もあります。この不一致を検証するまでは、ライブラリは保守的に 1K のみ許可します。512 がサーバーで拒否されることを実測したという意味ではありません。

---

## xAI (XAIService)

<a id="grok-47"></a>

### Grok 4.7

素早い下書きの後にコードや文書を詳しく検証する場合は、Grok 4.7 を選び、リクエストごとに推論レベルを調整します。既存の通常応答・ストリーミング・Run・ローカルツール・構造化出力・画像入力 API を使えます。`grok-4.7` はテキストと画像を入力し、テキストを出力します。コンテキストは 500,000 トークンです。対応する未リリースの core と abstractions が必要で、公開済み 8.0.0 / 4.0.0 には含まれません。既定モデルは Grok 4.5 のままです。

```csharp
using Mythosia.AI.Extensions;
using Mythosia.AI.Models;
using Mythosia.AI.Services.xAI;

var grok = new XAIService(apiKey, httpClient);
grok.ChangeModel(AIModels.xAI.Grok4_7);
grok.WithGrokReasoning(GrokReasoning.Low);

var request = grok.CreateRequest("Review this deployment plan and its rollback risks.")
    .WithReasoning(ReasoningLevel.XHigh)
    .WithSpeed(InferenceSpeed.Fast);

await using var run = await request.StartRunAsync();
await foreach (var content in run.StreamAsync())
    Console.Write(content.Content);
var result = await run.Result;
Console.WriteLine(result.Text);
foreach (var processing in result.Processing)
    Console.WriteLine(processing.AppliedSpeed);
```

`Low`、`Medium`、`High`、`XHigh` に対応します。ネイティブの `GrokReasoning.Auto` は `reasoning_effort` を省略し、プロバイダー既定の `High` を使います。共通の `ReasoningLevel.Auto` もそのリクエストでフィールドを省略し、プロバイダー既定の `High` を使います。`None`、`Minimal`、`Max` は送信前に拒否します。`WithReasoning(...)` はツールラウンドと構造化出力修正を含む論理リクエストに適用し、`WithGrokReasoning(...)` はサービス基本設定を指定します。内部の `DisableReasoning` は `Low` を使います。任意の推論要約はプロバイダーによる要約で、内部推論の全体ではありません。

`WithSpeed(InferenceSpeed.Standard)` は `service_tier: "default"`、`Fast` は対応する xAI エンドポイントで `"priority"` を送り、追加料金がかかる場合があります。`ProviderDefault` は上書きしません。サーバーが通常処理に下げる場合があるため、報告された階層は `result.Processing` で確認します。これは `grok-4.7` の優先処理であり、Cursor・Grok Build 専用の別モデル「Grok 4.7 Fast」ではありません。その変種の公開 API モデル ID はありません。

`GetCapabilities()` は選択したリクエストのローカルな対応情報で、アカウント権限は確認しません。この連携は Chat Completions を使います。Responses 専用の暗号化推論、ホスト型 Web・X 検索、ネイティブ非同期ツール、キャッシュ保持更新、`SteerAsync` は今回の対象外です。クライアント関数は既存のローカルツールループを使い、`run.CanSteer` は false です。

[Grok 4.7](https://docs.x.ai/developers/grok-4-7) · [reasoning_effort](https://docs.x.ai/developers/model-capabilities/text/reasoning) · [Priority Processing](https://docs.x.ai/developers/advanced-api-usage/priority-processing)

### タスクに合う推論レベルを選ぶ

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

Grok 4.6 は `new StreamOptions().WithReasoning()` で観測を有効にすると、プロバイダーの推論要約を `StreamingContentType.Reasoning` で返すことがあります。要約は任意に提供され、内部推論の全体ではありません。Run でも同じ観測設定を使えます。表示設定を変えても要求する推論レベルは変わりません。

[Grok 4.6](https://docs.x.ai/developers/grok-4-6) · [reasoning_effort](https://docs.x.ai/developers/model-capabilities/text/reasoning)

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

編集では、プロンプトで参照する順番に既存画像のバイト列を渡します。

```csharp
var edited = await images.EditImagesAsync(new ImageEditRequest
{
    Prompt = "画像1の被写体を画像2の風景に配置してください。",
    InputImages = new[]
    {
        new ImageInput(await File.ReadAllBytesAsync("subject.png"), "image/png", "subject.png"),
        new ImageInput(await File.ReadAllBytesAsync("scene.jpg"), "image/jpeg", "scene.jpg")
    },
    Size = ImageSize.Preset(ImageResolution.OneK),
    OutputFormat = ImageOutputFormat.Auto
});

var imageEdited = edited.Images[0];
var extensionEdited = imageEdited.MediaType switch
{
    "image/jpeg" => ".jpg",
    "image/png" => ".png",
    "image/webp" => ".webp",
    _ => throw new NotSupportedException(imageEdited.MediaType)
};
await File.WriteAllBytesAsync("combined" + extensionEdited, imageEdited.Data);
```

各 `GeneratedImage.Data` にデコード済みの画像バイト列が入ります。拡張子は `MediaType` に合わせて選んでください。アダプターはインライン base64 応答を要求し、提供元の画像 URL はダウンロードしません。`Count` は出力1～10枚、編集は JPEG・PNG・WebP の参照画像1～5枚に対応します。

xAIでは`ImageSize.Auto`または`ImageSize.Preset(ImageResolution.OneK, ImageAspectRatio.SixteenByNine)`を使用します。解像度は`Auto`、`OneK`、`TwoK`で、比率はモデルの対応範囲内で指定します。正確な寸法を要求できないため`Pixels(...)`は拒否されます。

xAIは新しい共通既定値`ImageOutputFormat.Auto`のみサポートします。出力コーデックを選択できないため、明示的な`Jpeg`、`Png`、`WebP`は送信前に拒否されます。拡張子は`GeneratedImage.MediaType`で決めてください。ライブラリは画像変換を行いません。品質は`ImageQuality.Auto`、`Low`、`Medium`、背景は`ImageBackground.Auto`のみ。圧縮指定と独立した`Mask`は非対応です。

Google は `ImageSize.Auto` またはモデル別の解像度・比率の `Preset` を使用します。[Google モデル別の画像オプション](#google-image-options).出力は`ImageOutputFormat.Auto`または`Jpeg`で、`Png`/`WebP`は拒否されます。GoogleとxAIは`Pixels`を拒否し、OpenAIは`Auto`/`Pixels`を受け付けて`Preset`を拒否します。[移行例](#image-options-migration)を参照してください。

[Grok Imagine Image 2.0](https://docs.x.ai/developers/models/grok-imagine-image-2.0) · [Image API](https://docs.x.ai/developers/rest-api-reference/inference/images)

---

## DeepSeek (DeepSeekService)

素早い回答の後に詳しく検証したり、グラフやスクリーンショットを説明したりする場合に DeepSeek Flash を使えます。`AIModels.DeepSeek.Flash` (`deepseek-flash`) は、2026年9月10日公開の視覚理解対応 V4.1 Flash を選択します。既存の補完・ストリーミング・Run・関数呼び出し・RAG API を使用し、`Mythosia.AI` 8.0.0 / `Mythosia.AI.Abstractions` 4.0.0 から利用できます。

> 公開済みの `Mythosia.AI` 8.0.0 / `Mythosia.AI.Abstractions` 4.0.0 は Flash の基本機能に対応しています。`AIModels.DeepSeek.V4Pro`、`UseResponsesApi`、Files API、`DeepSeekImageFileContent` はソースに追加された未リリース機能であり、対応するコアと抽象化のソースビルドが必要です。これらは上記の公開済みパッケージには含まれません。[未リリースの変更履歴](../../src/core/Mythosia.AI/RELEASE_NOTES.md#unreleased)。

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

グラフやスクリーンショットは、既存のメッセージ型に画像バイト列を入れて送ります。

```csharp
using Mythosia.AI.Models.Messages;

var message = new Message(ActorRole.User, new List<MessageContent>
{
    new TextContent("このグラフの傾向と、読み取りにくいラベルを説明してください。"),
    new ImageContent(await File.ReadAllBytesAsync("chart.png"), "image/png")
});
string description = await deepseek.GetCompletionAsync(message);
```

`ImageContent` は JPEG・PNG・GIF・WebP のバイト列、または提供元が取得する公開 HTTP(S) URL を受け付けます。例ではユーザーメッセージを使います。現在の API はツールメッセージの画像にも対応しますが、登録関数のハンドラーは共通結果契約を通じてテキストを返します。`ActorRole.Function` の画像メッセージを手動で作る場合は、対応する呼び出し ID を `MessageMetadataKeys.FunctionId` に設定してください（wire の `tool_call_id`）。画像サイズ・合計制限は最新の公式視覚ガイドを参照してください。 画像生成には対応していません。

両モデルの上限はコンテキスト 1M、出力 384K (`393216`) トークンで、既定予算は 8,000 です。推論中は temperature・penalty を省略し、`top_p` は 0.95 以上、非推論では `top_p` を省略します。Responses のネイティブ JSON schema は既存の型付き出力 API から利用できます。バックグラウンド実行、サーバー側 `store`/`previous_response_id`、ホスト型検索、`CachePreservation.Required`、ネイティブ非同期ツール、`SteerAsync`、画像生成は未対応です。ローカル RAG と通常のツールラウンドは利用できます。

`V4Flash`、`Chat`、`Reasoner` は元の wire ID を保つ警告のみの obsolete 定数です。提供元は引退済み `deepseek-v4-flash` を一時的に V4.1 Flash へ転送しますが、ライブラリは定数を書き換えません。新規コードは `Flash` を選んでください。`UseReasonerModel()` は Flash と推論 `High` を選びます。

[DeepSeek V4.1 Flash](https://api-docs.deepseek.com/updates/) · [Thinking](https://api-docs.deepseek.com/guides/thinking_mode/) · [Vision](https://api-docs.deepseek.com/guides/vision/) · [Tools](https://api-docs.deepseek.com/guides/tool_calls/) · [Limits](https://api-docs.deepseek.com/quick_start/pricing/)

---

## Perplexity (PerplexityService)

最新情報に基づく回答と、読者が確認できる出典が必要なときに Perplexity を使います。`PerplexityService` は Agent API を呼び出し、独立した検索と埋め込みは、自分で選んだ回答モデルに検索の仕組みを組み合わせるために使います。

```csharp
using Mythosia.AI.Models;
using Mythosia.AI.Services.Perplexity;

var service = new PerplexityService(apiKey, httpClient);
service.ChangeModel(AIModels.Perplexity.Sonar);

await using var run = await service.StartRunAsync("最新のバッテリーリサイクル手法を比較し、出典を示してください。");
Console.WriteLine((await run.Result).Text);
foreach (var source in run.Citations)
    Console.WriteLine(source.Url);
```

調査プリセット、ローカル関数やホスト型ツールの接続、長時間のバックグラウンド処理については [Perplexity ガイド](perplexity.md) を参照してください。既存の completion、ストリーミング、Run、引用 API を利用できます。

このリリースではサービスを `/v1/agent` に移行します。`AIModels.Perplexity.Sonar` は `perplexity/sonar` を選択するようになります。提供元は旧 Sonar エンドポイントを 2026年9月27日に終了すると発表しているため、既存の連携は移行が必要です。 [Perplexity](https://community.perplexity.ai/t/sonar-is-moving-to-the-agent-api/5802)

---

## Alibaba / Qwen (QwenService)

別パッケージをインストールします:

```bash
dotnet add package Mythosia.AI.Providers.Alibaba
```

```csharp
using Mythosia.AI.Providers.Alibaba;

var service = new QwenService(apiKey, http)
{
    Model = AlibabaModels.QwenMax
};
```

利用可能なモデル: `QwenMax`、`QwenPlus`、`QwenTurbo`、`Qwen3`およびバリアント。

サービスの作成時に `EndpointPlatform` で互換エンドポイントを選択します:

```csharp
var vllmService = new QwenService(
    "http://localhost:8000",
    EndpointPlatform.Vllm,
    http);
```

[共通の対応定義でモデルの機能選択を構成する](model-capabilities.md).
