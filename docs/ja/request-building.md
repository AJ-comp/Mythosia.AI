# リクエストごとに設定を独立させる

> Claude Sonnet 5.5: Mythosia.AI 8.2.0 / Abstractions 4.2.0 が必要です。[設定と移行](providers.md#claude-sonnet-55)

> GPT-6.1 Sol: Mythosia.AI 8.2.0 / Abstractions 4.2.0 が必要です。[モデル選択と移行](providers.md#gpt-61-sol)

> Grok 4.7: Mythosia.AI 8.1.0 / Abstractions 4.1.0 が必要です。 [モデル選択・推論・処理速度](providers.md#grok-47)

文書の要約には低いTemperature、創作の下書きには高い値が必要になることがあります。下書きを準備しただけで、先に準備した要約の設定が変わってはいけません。呼び出しごとに設定を変えたい場合や、共通のリクエストから複数のバリエーションを作る場合は`CreateRequest`を使います。

回答・使用量・出典をまとめて取得するには、`await run.Result` が返す `AIRunResult` を使用します。文字列は `result.Text` で取得でき、ストリームを読む必要はありません。Mythosia.AI 8.0.0 の API 変更です。`GetCompletionAsync` と `StructuredStreamRun<T>.Result` の戻り値型は維持します。 [Run の結果と移行](execution-api-transition.md#run-result).

完成した回答と停止ボタンだけなら`GetCompletionAsync`に`cancellationToken`を渡します。進捗イベントや対応モデルへの追加指示にはRunを使います。[完了要求のキャンセル](completions.md#completion-cancellation)を参照してください。

> `CreateRequest`の例にはMythosia.AI 8.0.0 / Abstractions 4.0.0が必要です。Runと共通リクエスト機能を導入した旧7.1リリースにはビルダーがありません。旧パッケージでは既存のサービスオーバーロードを使えます。

## Before: サービスの設定を共有

従来のサービスの`WithTemperature`は設定を変更し、同じインスタンスを返します。以下の変数は同じサービスを参照するため、後から設定した値が両方に適用されます。これらのメソッドはサービスの既定値を設定する用途で引き続き利用できます。

```csharp
var summary = service.WithTemperature(0.2f);
var creative = service.WithTemperature(0.8f);

bool same = ReferenceEquals(summary, creative); // true
string answer = await summary.GetCompletionAsync("この文書を説明してください。"); // 0.8
```

## After: 独立したリクエストから分岐

`CreateRequest`はサービスの既定値を取り込みます。ビルダーの各`With...`は元の値を変更せず、新しいビルダーを返します。実行時はそのリクエストの設定を直接使い、サービスの既定値を一時的に上書きしません。

```csharp
using Mythosia.AI.Builders;

AIRequestBuilder basis = service.CreateRequest("この文書を説明してください。");
AIRequestBuilder summary = basis.WithTemperature(0.2f);
AIRequestBuilder creative = basis.WithTemperature(0.8f);

bool same = ReferenceEquals(summary, creative); // false
string answer = await summary.GetCompletionAsync();
// 0.2を使用。creativeとサービスの既定値は変わりません。
```

返されたビルダーを使用してください。`basis.WithTemperature(0.2f);`の戻り値を破棄すると、`basis`は変わりません。

ビルダーは値を自動補正せず検証します。`WithTemperature`は0–2、`WithTopP`は0–1、ペナルティは−2–2で、NaNと無限大は拒否します。トークン数、ラウンド数、同時実行数、指定タイムアウトは正数が必要です。不正な値は`ArgumentException` / `ArgumentOutOfRangeException`になります。従来のサービスのTemperatureヘルパーは範囲補正を維持します。

## 各オブジェクトの役割

`AIService`はプロバイダー接続、既定値、既存の会話状態を管理します。公開型`Mythosia.AI.Builders.AIRequestBuilder`がfluent APIを提供し、内部型`AIRequest`が確定した入力と設定を実行部へ渡します。利用者による`Build()`呼び出しは不要です。`AIRequest`が回答として返るわけではなく、`GetCompletionAsync()`は`Task<string>`、`StartRunAsync()`は`Task<AIRun>`を返します。

```text
AIService.CreateRequest(input)
    → AIRequestBuilder.With...()
    → GetCompletionAsync() / StartRunAsync()
    → AIRequest
    → provider
    → string / AIRun
```

## 同じ設定で制御可能な実行を開始

完成した回答には`GetCompletionAsync()`を使います。進捗表示、対応モデルへの実行中の指示には`StartRunAsync()`を使います。入力は`CreateRequest`に渡し、ビルダーの実行メソッドには再度渡しません。`run.StreamAsync()`はその実行を観測し、`run.SteerAsync(...)`のモデル別対応条件は変わりません。

```csharp
await using var run = await service
    .CreateRequest("この文書を説明してください。")
    .WithMaxTokens(2048)
    .WithMaxRounds(10)
    .StartRunAsync(
        onText: text => Console.Write(text),
        cancellationToken: cancellationToken);

string answer = (await run.Result).Text;
```

ローカルツールは`Task<T>` / `ValueTask<T>`でオブジェクトを返し、注入された`CancellationToken`を受け取れます。`run.Cancel()`や開始トークンのキャンセルは協調するツールにも届きますが、読み取りの停止だけでは届きません。例外は失敗として記録します。キャンセル時は未開始の呼び出しをスキップし、トークンを無視する開始済みツールは後処理で待ちます。[結果・エラー・キャンセル](function-calling.md#tool-execution-contract)を参照してください。

[Run](execution-api-transition.md) · [Streaming](streaming.md)

## プロファイルとコンテキストの再利用

`WithProfile`は既存の`AIRequestProfile`を、`WithContext`は`AIRequestContext`をコピーします。後から元のオブジェクトを変更しても、準備済みのリクエストには影響しません。サンプリング、システム指示、ステートレスモード、関数呼び出しポリシー、対応する推論・Web検索・ファイル検索を設定できます。プロバイダーの対応範囲の検証は引き続き適用されます。

`WithFunctions(params FunctionDefinition[])`はコピーした関数定義をリクエストに追加します。`Mythosia.AI.Extensions`の`WithFunctions(toolInstance)`と`WithStaticFunctions<T>()`で既存の属性付き関数も使えます。既定の登録は`CreateRequest`前、個別の登録はその後に行います。サービスに保留中の次回用機能・ポリシーは`CreateRequest`が取り込み消費します。再利用する場合は返されたビルダーを使ってください。

```csharp
var request = service
    .CreateRequest("この質問を検索向けに書き換えてください。")
    .WithProfile(RequestProfiles.QueryRewrite)
    .WithContext(new AIRequestContext
    {
        SystemMessageSuffix = "\n元の意味を保ってください。"
    });

string rewritten = await request.GetCompletionAsync();
```

Claude はモデル、リクエストの目的、推論と thinking binding の設定をまとめて決定します。`RequestProfiles.Summarization` や `RequestProfiles.QueryRewrite` などの補助プロファイルでは、`DisableReasoning = true`、目的が `Default` 以外、かつ実際のリクエストがステートレスの場合に限り、継承した binding を省略します。保持する会話プレフィックスがないリクエストで、継承したポリシーが推論を再度有効にしたりリクエストを拒否したりするのを防ぎます。推論を無効にできるモデルでは無効化し、常時推論する Opus 5.5、Fable 5.1、Mythos 5.1 では `Low` を使い、読み取り可能な thinking を省略します。Sonnet 5.5 では high effort の `between_tools` を使います。

完了、ストリーミング、構造化出力、Run は同じ順序でリクエストを準備します。設定を取得し、実際のプロファイル処理を一度適用してから、最終的な共通オプションとプロバイダー固有オプションを検証します。自動要約、新しい入力の履歴追加、通信開始より前に行うため、カスタムプロバイダーのプロファイルオーバーライドも検証対象に反映されます。Claude で実際に使用する手動 `ThinkingBudget` がモデルの出力上限以上なら、この段階で拒否します。有効なプロファイルと共通推論設定の優先順位は変わりません。

アプリケーションからの呼び出しは独立した論理リクエストを開始します。`SystemMessageProvider` やツールのコールバック内からの通常の呼び出し、同じ `AIRequestProfile`・`Message` を再利用する呼び出しも含みます。オブジェクトの再利用は実行の共有を意味しません。フレームワーク内部の委譲、ツール処理、再試行、形式修正は元のリクエストを継続し、プロファイルは一度だけ適用します。通常の子リクエストは独自のオプションとサービス既定値を取得し、ビルダーは取得済み設定を使います。成功・失敗・キャンセル後に親の実行状態を復元します。フレームワークからの呼び出しを転送するプロバイダーのオーバーライドには、[後述のアダプター規則](#provider-request-adapters)が適用されます。

組み込みプロバイダーは組み込み入力コンテンツの独立したコピーを保持します。同じ `Message` を再利用しても、その呼び出しのコンテキストとターン指示を適用し、受理済みの履歴は書き換えません。カスタムコンテンツと未対応のメタデータオブジェクトは所有者が管理してください。同じ会話への並行呼び出しを安全にするものではありません。

`StartRunAsync` が戻った後も、Run は最終設定の独自のコピーを保持します。呼び出し元のプロファイルを復元しても実行中の Run は変わらず、実行用プロファイルフックも一度だけ呼び出されます。

ステートレスな補助リクエストは独立した会話を使用し、親の出力スキーマ、ホスト型ツール、一回限りのオプションを継承しません。この分離によってネイティブオプションの検証を省略することはなく、OpenAI・Perplexity の Run にも適用します。親の設定、メッセージ、`CurrentSummary`、リクエストの観測情報は保持します。ステートフルなリクエストでは binding と会話の検証を維持します。既存の公開 API は変わりません。

ライブラリが自動生成する会話要約では、親の `SystemMessageProvider` コールバックとリクエストコンテキストも除外します。継承した `RequestMessageOverride` が内部の要約プロンプトを置き換えることを防ぐためです。アプリケーションが明示的に文章の要約を依頼する場合を含め、通常のリクエストでは動的コンテキストを従来どおり適用します。

ステートレスなリクエストは親の会話の自動要約も実行しません。従来の `GetCompletionAsync(string, profile)` オーバーロードでも、`Message` オーバーロードやリクエストビルダーと同じ動作になります。親の `CurrentSummary` とメッセージは変わりません。状態を保持するリクエストでは通常の自動要約を維持します。

[AIRequestProfile](request-profiles.md) · [AIRequestContext](request-contexts.md) · [WithReasoning / WithWebSearch / WithFileSearch](reasoning-and-search.md)

<a id="provider-request-adapters"></a>

## カスタムプロバイダーでリクエストを転送する

フレームワークが仮想プロバイダーアダプターを呼び出した場合、対応する基底メソッドへの最初の呼び出しは、準備済みのリクエストを継続します。オーバーライドで入力の `Message` を置き換えた場合も同様です。ビルダーが取得したオプションと適用済みのプロファイルは、この転送でも維持されます。既定のコールバック型ストリーミングアダプターも、同じ準備済みリクエストを継続します。

アダプターが異なる `AIRequestProfile` を渡す場合、同じ値は再適用せず、変更値は取得済み設定を基に以前のプロファイル層を置き換えます。自動要約や送信の前に再検証するため、ステートレスへの変更で親の会話が先に要約されることはありません。OpenAI のステートレス補助要求は無関係な保持履歴の検査を省略し、親の保護状態は維持します。

転送するプロファイルを置き換えても、リクエスト内で後から追加・削除・編集したツール、ポリシー変更、明示的に再設定した値は維持されます。同じスカラー値を再設定した場合も含みます。別のプロファイル項目の変更で、アダプターが削除したツールが復活することはありません。サービスの既定値も再取得しません。

ライブラリが内部構造を扱えない独自設定オブジェクトは、内部フィールドを変更せず `SetExecutionSetting` で値を置き換えてください。プロファイル変更の追跡のために任意のアプリケーションオブジェクトを調査したり、そのシリアライザーを実行したりはしません。

Claude の圧縮は保持済み `RequestMessageOverride` と `AdditionalMessages` の呼び出しと結果の依存関係を、サーバーツールも含めて維持します。Mythos 5.1 のバインド済み thinking 接頭部も保護します。並列ツールの旧形式記録は通常履歴でも追加メッセージでも一度だけまとめ、各記録の所有関係を維持します。

転送前に無関係な補助処理が同じ基底メソッドを呼び出すと、それが継続なのかをフレームワークは判断できません。その補助呼び出しと `await` を、protected メソッド `BeginIndependentRequestScope()` のスコープで囲んでください。ストリーミングの場合は、列挙全体が終わるまでスコープを維持します。補助リクエストはサービス既定値から開始し、スコープを破棄すると外側の設定、機能、コンテキスト、保留中の委譲が復元されます。コンテキストやツールのコールバックからの通常の入れ子呼び出しはすでに独立しており、このスコープは不要です。

たとえば、具象プロバイダーのサブクラスでテキストを書き換えてから転送できます。

```csharp
public override async Task<string> GetCompletionAsync(
    Message message, AIRequestProfile? profile = null,
    AIRequestContext? context = null, CancellationToken cancellationToken = default)
{
    string rewritten;
    using (BeginIndependentRequestScope())
    {
        rewritten = await base.GetCompletionAsync(
            new Message(ActorRole.User, message.Content),
            RequestProfiles.QueryRewrite,
            cancellationToken: cancellationToken);
    }

    var replacement = new Message(message.Role, rewritten);
    return await base.GetCompletionAsync(replacement, profile, context, cancellationToken);
}
```

このスコープはリクエストの実行状態を分離しますが、会話履歴を分離したり、サービスの並行利用を可能にしたりするものではありません。この例ではステートレスな `QueryRewrite` プロファイルを使い、補助リクエストを親の会話から分離しています。

Claude の圧縮処理は、`AIRequestContext.AdditionalMessages` で追加された署名付き thinking を含め、正規の送信形式で保持された履歴を検査します。既定の thinking binding は、そのプレフィックスを自動要約や明示的な要約による圧縮から保護します。`ClaudeThinkingPrefixMismatchBehavior.DropBlock` を明示すると、対応する場合には圧縮が許可されます。他の会話に関する制約は引き続き適用されます。

## コピーされる設定と共有される状態

共通設定とプロバイダーの既定値は`CreateRequest`時点で取り込みます。その後の既定値変更は準備済みリクエストに影響しません。組み込みのメッセージ内容、対応するオプションコレクション、プロファイル、コンテキスト、ポリシーはコピーされます。関数ハンドラー、動的コンテキストのコールバック、独自のメッセージ内容は参照を保持します。独自の内容は変更せず、デリゲートが外部状態を参照できる点に注意してください。動的コンテキストは実行時に評価されます。

キャプチャ後は元の `JsonDocument` を破棄したり `JsonNode` を変更したりしても、リクエストのメタデータや関数呼び出し引数に保持された JSON 値は変わりません。実行ごとに別のコピーを使用します。ツールスキーマの `Items` が循環する場合やネストが 64 段階を超える場合は、キャプチャ時（`CreateRequest` または `WithFunctions`）に `ArgumentException` が発生します。不正なスキーマによるスタック枯渇を、実行前の通常のエラーとして防ぐためです。

コピーしても配列の次元と開始インデックス、および標準の `Dictionary<,>`・`SortedDictionary<,>`・`SortedList<,>` のキー比較規則は維持されます。そのため、大文字と小文字を区別しないキー検索がリクエスト内で変わることはありません。空の `default(JsonElement)` 値（`Undefined`）もそのまま保持します。不明な独自メタデータオブジェクトは参照を維持するため、所有者が変更を避けるかアクセスを調整してください。

標準の`ReadOnlyCollection<T>`と`ReadOnlyDictionary<TKey, TValue>`も、型付き配列や辞書の中で元の型を維持します。対応する内部コレクションのコピーでは、読み取り専用ビュー、共有参照、循環参照を保持します。`Hashtable`と非ジェネリックの`SortedList`のキー比較規則も維持します。

ビルダーは別の会話ではありません。実行時にサービスで有効な会話の履歴を使い、作成時に履歴を固定しません。状態を保持する呼び出しは共有履歴を更新します。履歴を参照・蓄積しない呼び出しには`WithStatelessMode()`を使います。サービスごとの同時Run数の制限は維持されます。設定の独立性は同じサービスでの並列実行を保証しません。独立した会話を同時に実行する場合は別のサービスを使ってください。

## 既存の呼び出しと拡張

`GetCompletionAsync`と既存のサービスAPIは引き続き利用できます。`BeginMessage()` / `MessageChain`は従来の変更可能なメッセージ構築を維持し、実行には新しいリクエスト経路を使います。設定の分岐と再利用には`CreateRequest`を使います。ビルダーAPIは`AIService`とそのプロバイダー実装にあり、`IAIService`に必須メンバーを追加しません。抽象インターフェイスやRAGラッパーの利用者は既存のプロファイル・コンテキストと実行APIを使えます。

[共通の対応定義でモデルの機能選択を構成する](model-capabilities.md).

<a id="inference-speed"></a>

## 作業に合わせて処理速度を選ぶ

画面で回答を待つ利用者には有料の低遅延処理を、バックグラウンドの報告書には通常処理を選べます。 `WithSpeed` はモデルと推論レベルを保ったまま処理モードを指定します。 Mythosia.AI 8.1.0 / Abstractions 4.1.0 が必要です。

`ProviderDefault` は上書きせず、既存のサービス・プロバイダー設定に従います。プロジェクトの既定値が Fast の場合もあります。`Standard` は通常処理を明示し、`Fast` は追加料金のある低遅延処理を要求します。返されたビルダーを保持してください。以下の三つの分岐は独立し、元のリクエストは変わりません。

```csharp
using Mythosia.AI.Models;

var basis = service.CreateRequest("Explain this report.");
var providerDefault = basis.WithSpeed(InferenceSpeed.ProviderDefault);
var standard = basis.WithSpeed(InferenceSpeed.Standard);
var fast = basis.WithSpeed(InferenceSpeed.Fast);
```

選択肢を表示する前に `GetSpeedSupport(InferenceSpeed.Fast)` を確認します。`StandardSpeed` と `FastSpeed` も Supported・Unsupported・Unknown を返します。ローカルの Supported はアカウント権限、容量、遅延を保証しません。非対応または不明の Standard/Fast 指定は失敗し、モデルや推論レベルを暗黙に変更しません。既存の経路には `ProviderDefault` を使います。

```csharp
using Mythosia.AI.Models;
using Mythosia.AI.Models.Capabilities;

var request = service.CreateRequest("Explain this report.")
    .WithSpeed(InferenceSpeed.Fast);
if (request.GetCapabilities().GetSpeedSupport(InferenceSpeed.Fast)
    != CapabilitySupport.Supported)
    throw new NotSupportedException("Fast processing is not supported here.");

await using var run = await request.StartRunAsync();
var result = await run.Result;
Console.WriteLine(result.Text);
foreach (AIProcessingInfo processing in result.Processing)
{
    Console.WriteLine($"{processing.RequestIndex}: {processing.RequestedSpeed} -> " +
        $"{processing.AppliedSpeed?.ToString() ?? "unknown"}; " +
        $"raw={processing.RawAppliedMode}; response={processing.ResponseId}; " +
        $"downgraded={processing.IsDowngraded}");
}
```

`AIRunResult.Processing` はストリームを読まなくても不変の `AIProcessingInfo` を保持します。`RequestIndex` は 1 始まりのプロバイダー推論試行番号で、サーバー continuation も含みます。ツールのラウンド数や HTTP リクエスト数とは異なります。ツール後続呼び出し、再試行、形式修復で記録が増える場合があります。失敗した試行を含め、認識できるモードが報告されなければ `AppliedSpeed` は null です。`RawAppliedMode` と `ResponseId` は報告された原値を保持します。`IsDowngraded` が true になるのは、Fast 要求に対して Standard が明示された場合だけです。false でも Fast 適用を確定できません。

通常の completion では直後に `AIService.LastProcessing` を読みます。次の論理リクエストでビューは置き換わり、取得済みの記録は不変です。サービス拡張は次の論理リクエストとツール往復に適用され、恒久的な既定値にはなりません。補助要約、内部のクエリ書き換え、内部プロファイルは本体の速度指定を継承せず、観測記録も混在しません。

```csharp
using Mythosia.AI.Extensions;
using Mythosia.AI.Models;

string answer = await service
    .WithSpeed(InferenceSpeed.Standard)
    .GetCompletionAsync("Explain this report.");
var processing = service.LastProcessing;
```

これらは処理モードの報告であり、実測の毎秒トークン数ではありません。OpenAI・xAI・Google はサーバー側で通常処理に下げる場合がありますが、Mythosia は別の速度で自動再試行しません。Anthropic fast mode は権限のある直接 Claude API に限定され、速度変更でプロンプトキャッシュが無効になる場合があります。Gemini Developer API priority は Tier 2/3 の資格が必要です。料金とモデル・API の対応を別途確認してください。画像生成、埋め込み、ネイティブ Batch API は対象外です。 [OpenAI](https://developers.openai.com/api/docs/guides/fast-mode) · [Anthropic](https://platform.claude.com/docs/en/build-with-claude/fast-mode) · [xAI](https://docs.x.ai/developers/advanced-api-usage/priority-processing) · [Gemini](https://ai.google.dev/gemini-api/docs/generate-content/priority-inference)

`IAIService` 参照では `Mythosia.AI.Extensions` の `GetLastProcessing()` を使います。任意の `IAIProcessingInfoService` を読み、診断非対応なら空のリストを返します。`IAIService` に必須メンバーは追加しません。RAG では `RagEnabledService.WithSpeed(...)` が検索後の次の回答に適用され、`LastProcessing` はその回答を記録します。内部クエリ書き換えは分離され、Run 結果にも同じ `Processing` があります。

今回の Fast 対応リストは以下のとおりです。Standard は `GetSpeedSupport(InferenceSpeed.Standard)` で別途確認してください。リスト外モデル、サードパーティー接続先、OpenAI 互換プロバイダーが有料モードを自動的に継承することはありません。

| API | Fast |
| --- | --- |
| Anthropic — `api.anthropic.com` | `claude-opus-4-8`, `claude-opus-5`, `claude-opus-5-5` |
| OpenAI — `api.openai.com` | `gpt-6.1-sol`, `gpt-6-astra`, `gpt-6-sol`, `gpt-6-luna`, `gpt-5.6`, `gpt-5.6-sol`, `gpt-5.6-terra`, `gpt-5.6-luna`, `gpt-5.3-codex` |
| xAI — `api.x.ai` | `grok-4.7`, `grok-4.6`, `grok-4.5`, `grok-4.5-latest`, `grok-build-latest`, `grok-4.3`, `grok-4.3-latest`, `grok-latest`, `grok-4.20-0309-reasoning`, `grok-4.20-0309-non-reasoning`, `grok-build-0.1` |
| xAI — `us.api.x.ai` | `grok-4.7`, `grok-4.6` |
| Google — Gemini Developer API | `gemini-2.5-pro`, `gemini-2.5-flash`, `gemini-2.5-flash-lite`, `gemini-3-flash-preview`, `gemini-3.1-pro-preview`, `gemini-3.1-flash-lite`, `gemini-3.5-flash`, `gemini-3.5-flash-lite`, `gemini-3.6-flash`, `gemini-3.7-flash`, `gemini-3.8-flash` |

| API | `Standard` | `Fast` | `RawAppliedMode` |
| --- | --- | --- | --- |
| Anthropic — Opus 4.8 / 5 / 5.5 | `speed: "standard"` + `fast-mode-2026-02-01` | `speed: "fast"` + `fast-mode-2026-02-01` | `usage.speed` |
| Anthropic — Sonnet 5 を含むその他の既知の Claude モデル | `speed` と fast-mode beta を省略 | `Unsupported` | `usage.speed` |
| OpenAI | `service_tier: "default"` | `service_tier: "fast"` | `service_tier` (`fast` / `priority` → Fast) |
| xAI | `service_tier: "default"` | `service_tier: "priority"` | `service_tier` |
| Google | `serviceTier: "standard"` | `serviceTier: "priority"` | `x-gemini-service-tier` / `usageMetadata.serviceTier` |

これらの Claude モデルの Standard は既存の通常リクエストを使います。サーバーが処理情報を返さなければ `AppliedSpeed` は null のままで、要求値だけから Standard と推定しません。
