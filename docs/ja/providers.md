# プロバイダー固有機能

> `CreateRequest`の例にはMythosia.AI 8.0.0 / Abstractions 4.0.0が必要です。Runと共通リクエスト機能を導入した旧7.1リリースにはビルダーがありません。旧パッケージでは既存のサービスオーバーロードを使えます。

<a id="image-options-migration"></a>
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

`FunctionDefinition.AllowAsync = true` または `FunctionBuilder.WithAsync()` で、GPT-6 Astra の Responses API による非同期ツール呼び出しを選択的に許可できます。既定値は `false` で、未対応のモデルでは同じハンドラーの結果を待ちます。例とリクエストの有効期間は[関数呼び出しガイド](function-calling.md)を参照してください。

プロバイダー間で推論レベルを指定し、最新情報や索引済みの文書を回答の根拠にする方法は、[推論と検索のガイド](reasoning-and-search.md)を参照してください。対応モデル、キャッシュ保持、設定の組み合わせの制限をまとめています。

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

---

## xAI (XAIService)

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

Googleでは`ImageSize.Auto`またはモデルが対応する`ImageResolution.Auto`、`FiveTwelve`、`OneK`、`TwoK`、`FourK`の`Preset`を使用します。出力は`ImageOutputFormat.Auto`または`Jpeg`で、`Png`/`WebP`は拒否されます。GoogleとxAIは`Pixels`を拒否し、OpenAIは`Auto`/`Pixels`を受け付けて`Preset`を拒否します。[移行例](#image-options-migration)を参照してください。

[Grok Imagine Image 2.0](https://docs.x.ai/developers/models/grok-imagine-image-2.0) · [Image API](https://docs.x.ai/developers/rest-api-reference/inference/images)

---

## DeepSeek (DeepSeekService)

素早い回答の後に詳しく検証したり、グラフやスクリーンショットを説明したりする場合に DeepSeek Flash を使えます。`AIModels.DeepSeek.Flash` (`deepseek-flash`) は、2026年9月10日公開の視覚理解対応 V4.1 Flash を選択します。既存の補完・ストリーミング・Run・関数呼び出し・RAG API を使用し、`Mythosia.AI` 8.0.0 / `Mythosia.AI.Abstractions` 4.0.0 から利用できます。

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

`WithFunction(...)` でローカル関数を登録し、アプリのコードからデータ取得や処理を行えます。ツールは推論の有無にかかわらず使えますが、推論中は強制・必須のツール選択を拒否するため自動選択を使ってください。後続ラウンドに備え、ネイティブの `reasoning_content` と呼び出し ID を保持します。Run と既存ストリーミングでは `StreamOptions.WithReasoning()` により `StreamingContentType.Reasoning` を観察できます。この観察設定自体は推論を有効にしません。使用量には提供元が報告したキャッシュ・推論トークンも含まれます。 自動コンテキスト復旧は共通ストリーミングループを使います。ツールに過去のネイティブ推論履歴が必要な場合は、その履歴を保つため自動圧縮を禁止し、超過エラーを返します。

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

`ImageContent` は JPEG・PNG・GIF・WebP のバイト列、または提供元が取得する公開 HTTP(S) URL を受け付けます。例ではユーザーメッセージを使います。現在の API はツールメッセージの画像にも対応しますが、登録関数のハンドラーは共通結果契約を通じてテキストを返します。`ActorRole.Function` の画像メッセージを手動で作る場合は、対応する呼び出し ID を `MessageMetadataKeys.FunctionId` に設定してください（wire の `tool_call_id`）。画像サイズ・合計制限は最新の公式視覚ガイドを参照してください。`file_id`、Files API、画像生成は未統合です。

提供元の上限はコンテキスト1M、出力384K (`393216`)トークンで、ライブラリの既定予算は8,000です。推論中は temperature・penalty を省略し、`top_p` は0.95以上、非推論では `top_p` を省略します。通信は Chat Completions を使用します。Responses、ホスト型検索、`CachePreservation.Required`、ネイティブ非同期ツール、`SteerAsync` は未対応です。ローカル RAG と通常のツールラウンドは使えます。

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
