# 選択したモデルに合う機能を表示する

> Grok 4.7 は未リリースの追加機能です。[モデル選択・推論・処理速度](providers.md#grok-47)を参照してください。

> GPT-6 Sol/Luna は未リリースの追加機能です。[モデルの選択と必要バージョン](providers.md#gpt-6-sol-luna)を参照してください。

[Claude Opus 5.5](providers.md#claude-opus-55) の capability は `XHigh` を含む `Low`〜`Max` を公開し、`None` と `Minimal` は未対応です。`ThinkingToggle` は未対応、`MaxOutputTokens` は 128000 です。非表示は推論の無効化を意味しません。これは開発中の追加機能であり、既存の公開パッケージの説明ではありません。

チャット画面の推論・検索・ツール・画像設定は、選択した接続に合わせる必要があります。アプリごとにモデル名のリストを管理するとライブラリの規則と重複し、提供元・プロトコル・デプロイの変更で食い違います。機能スナップショットなら画面と実行時検証が同じモデル定義を使えます。

Mythosia.AI 8.0.0の API です。スナップショットはライブラリが把握する対応情報の不変オブジェクトで、アカウントやサーバーへの実時間照会ではありません。型は `Mythosia.AI.Models.Capabilities` にあります。

待ち時間が重要なリクエストでは[処理速度](request-building.md#inference-speed)を選べます。`WithSpeed` はモデルと推論レベルを保持し、`Processing` は実際に適用されたモードを示します。Fast は対応する組み合わせで使う有料設定です。

## Before / After

Before: アプリが対応モデルのリストを管理します。以下のリストはライブラリ API ではなく、アプリ側で用意するコードです。

```csharp
bool showReasoning = mySupportedReasoningModels.Contains(service.Model);
bool showSearch = mySupportedSearchModels.Contains(service.Model);
```

After: 構成したリクエストを調べて対応オプションを選びます。実際のモデル要求は最後の完了呼び出しで行い、対応情報の照会自体は API を呼びません。

```csharp
using Mythosia.AI.Models;
using Mythosia.AI.Models.Capabilities;

var request = service.CreateRequest("文書の内容を説明してください。");
AIModelCapabilities capabilities = request.GetCapabilities();

if (capabilities.GetReasoningSupport(ReasoningLevel.High)
    == CapabilitySupport.Supported)
{
    request = request.WithReasoning(ReasoningLevel.High);
}

string answer = await request.GetCompletionAsync();
```

`CapabilitySupport` は `Supported`、`Unsupported`、`Unknown` を区別します。独自デプロイやサーバー選択モデルで情報不足なら `Unknown` で、非対応という意味ではありません。例では対応が確認できたときだけ追加の推論を有効にします。不明なら既定値の維持や要求の試行など、アプリの方針を選びます。

`request.GetCapabilities()` はビルダーに取得済みのモデル・提供元オプション・プロファイルを読みます。`service.GetCapabilities()` は次回用オプションを消費せずサービスの既定設定を調べます。HTTP、コンテキストコールバック、実行時検証を呼ばず、履歴を変えず、処理も開始しません。リストも読み取り専用のスナップショットです。 サービスの照会は次回用に待機する機能設定も参照しますが、実際のリクエストで使えるよう保持します。 照会では関数の既定値やホスト型ツールのパラメーターをシリアライズせず、実行用プロファイルの準備やトークン予算の予約も行いません。

対応情報は接続が使える機能を示し、現在有効なオプションではありません。モデル名に加え提供元・API プロトコル・モードにも依存します。モデル識別は提供元別の上書きと Qwen/Ollama の ID 変換を反映します。単一モデルが選択されなければ `null` の場合があります。 サンプル Chat UI はモデル一覧だけに依存せず、現在の接続と登録済みツールを含む設定から選択肢を更新します。推論モードやツールの有無によってサンプリングのサポートが変わることがあるため、設定変更後に再照会してください。不明なサポート状態は非対応と区別して表示します。

| API | 意味 |
| --- | --- |
| `Reasoning`, `ReasoningLevels`, `GetReasoningSupport(...)` | 共通 `WithReasoning` の対応とレベル。 |
| `NativeReasoning`, `NativeReasoningLevels`, `ThinkingBudgetPresets`, `ThinkingToggle` | 提供元固有の推論設定と予算の候補。 |
| `Streaming`, `FunctionCalling`, `AsyncFunctionCalling`, `Steering` | ストリーミング、ツール、提供元固有の非同期ツール、実行中の追加指示。 |
| `WebSearch`, `FileSearch`, `ReasoningCachePreservation`, `ImageInput`, `StructuredOutput` | ホスト型検索、キャッシュを維持する推論変更、画像入力、構造化出力。 |
| `Temperature`, `TopP`, `FrequencyPenalty`, `PresencePenalty`, `MaxOutputTokens` | サンプリング設定の対応と、判明している出力トークン上限（nullable）。 |
| `StandardSpeed`, `FastSpeed`, `GetSpeedSupport(...)` | 未公開：処理モードの Supported/Unsupported/Unknown。アカウント権限は別途確認します。 |
| `Provider`, `Model` | 提供元と送信するモデルの識別。不明なら null の場合があります。 |

`ReasoningLevels` は共通 `WithReasoning`、`NativeReasoningLevels` は提供元固有の設定です。`ThinkingBudgetPresets` は UI 用の予算候補で、全許容値や数値範囲の網羅ではありません。`AsyncFunctionCalling` は提供元の非同期ツール実行で、ローカル関数の `Task` 戻り値や並列実行とは別です。 `StructuredOutput` はプロンプトと修復による代替処理を含む共通の型付き出力 API を指し、ネイティブの制約付きデコーディングを保証しません。両方の推論レベル一覧は `ReasoningLevel`、予算候補は整数です。

アカウント権限、サーバーの準備状態、不正なオプションの組み合わせを保証するものではありません。実行時の検証とエラーは残ります。追加指示前には実際の `run.CanSteer` を確認します。モデルが対応していても実行がまだ続いている保証はありません。

## 画像生成を別に調べる

画像生成モデルはチャットモデルと独立しています。特定モデルには `service.GetImageCapabilities(imageModel)`、引数省略時は提供元の既定画像モデルを使います。チャットのリクエストビルダーでは画像生成モデルを選びません。`Generation`、`Editing`、`Mask` から表示する画像操作を決めます。

```csharp
using Mythosia.AI.Models.Capabilities;

ImageModelCapabilities capabilities = service.GetImageCapabilities();
bool showEditing = capabilities.Editing == CapabilitySupport.Supported;
bool showMask = capabilities.Mask == CapabilitySupport.Supported;

foreach (var quality in capabilities.Qualities)
    Console.WriteLine(quality);

int? maximumImages = capabilities.MaxImages;
```

`Qualities`、`Backgrounds`、`OutputFormats`、`SizeKinds`、`Resolutions`、`AspectRatios` は型付きの読み取り専用リストです。`MaxImages` と `MaxInputImages` は既知の上限で、不明なら null。候補のすべての組み合わせが有効とは限らず、サイズ・形式・品質・マスク・モデルの既存検証は適用されます。独自・不明な画像モデルを非対応と断定しません。

Google の `Resolutions` と `AspectRatios` は選択した画像モデルに応じて変わり、生成・編集の検証にも適用されます。Flash-Lite の保守的な 1K 方針を含む[モデル別の表](providers.md#google-image-options)を参照してください。非対応の明示的な値は HTTP 前に拒否され、不明な独自モデルは `Unknown` とプロバイダー共通のオプション検証を維持します。

独自 `AIService` で信頼できる定義がある場合は protected `ResolveRequestCapabilities()` を再定義します。既定は `AIModelCapabilities.Unknown` です。一覧にないデプロイを非対応に変えてはいけません。`IAIService` に必須メンバーは追加せず、照会は `AIService` とリクエストビルダーにあります。

独自プロバイダーのプロファイルがネイティブモードのフラグを変更する場合は、`ApplyCapabilityRequestProfile(AIRequestProfile)` をオーバーライドし、リゾルバーに必要なフラグだけを `SetExecutionSetting(...)` で適用します。既定のフックは何もしません。共通プロファイル設定はビルダーが取得済みであり、照会は `ApplyRequestProfile` や `ApplyProviderSpecificRequestProfile` を呼び出しません。このフックで検証、コールバック、シリアライズ、予算予約、サービスや呼び出し元の状態変更を行わないでください。一時設定は照会終了時に復元され、オーバーライドが例外を送出した場合も同様です。

[リクエスト設定](request-building.md) · [提供元・画像設定](providers.md) · [Run の制御](execution-api-transition.md)
