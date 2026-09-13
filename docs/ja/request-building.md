# リクエストごとに設定を独立させる

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

[AIRequestProfile](request-profiles.md) · [AIRequestContext](request-contexts.md) · [WithReasoning / WithWebSearch / WithFileSearch](reasoning-and-search.md)

## コピーされる設定と共有される状態

共通設定とプロバイダーの既定値は`CreateRequest`時点で取り込みます。その後の既定値変更は準備済みリクエストに影響しません。組み込みのメッセージ内容、対応するオプションコレクション、プロファイル、コンテキスト、ポリシーはコピーされます。関数ハンドラー、動的コンテキストのコールバック、独自のメッセージ内容は参照を保持します。独自の内容は変更せず、デリゲートが外部状態を参照できる点に注意してください。動的コンテキストは実行時に評価されます。

キャプチャ後は元の `JsonDocument` を破棄したり `JsonNode` を変更したりしても、リクエストのメタデータや関数呼び出し引数に保持された JSON 値は変わりません。実行ごとに別のコピーを使用します。ツールスキーマの `Items` が循環する場合やネストが 64 段階を超える場合は、キャプチャ時（`CreateRequest` または `WithFunctions`）に `ArgumentException` が発生します。不正なスキーマによるスタック枯渇を、実行前の通常のエラーとして防ぐためです。

コピーしても配列の次元と開始インデックス、および標準の `Dictionary<,>`・`SortedDictionary<,>`・`SortedList<,>` のキー比較規則は維持されます。そのため、大文字と小文字を区別しないキー検索がリクエスト内で変わることはありません。空の `default(JsonElement)` 値（`Undefined`）もそのまま保持します。不明な独自メタデータオブジェクトは参照を維持するため、所有者が変更を避けるかアクセスを調整してください。

標準の`ReadOnlyCollection<T>`と`ReadOnlyDictionary<TKey, TValue>`も、型付き配列や辞書の中で元の型を維持します。対応する内部コレクションのコピーでは、読み取り専用ビュー、共有参照、循環参照を保持します。`Hashtable`と非ジェネリックの`SortedList`のキー比較規則も維持します。

ビルダーは別の会話ではありません。実行時にサービスで有効な会話の履歴を使い、作成時に履歴を固定しません。状態を保持する呼び出しは共有履歴を更新します。履歴を参照・蓄積しない呼び出しには`WithStatelessMode()`を使います。サービスごとの同時Run数の制限は維持されます。設定の独立性は同じサービスでの並列実行を保証しません。独立した会話を同時に実行する場合は別のサービスを使ってください。

## 既存の呼び出しと拡張

`GetCompletionAsync`と既存のサービスAPIは引き続き利用できます。`BeginMessage()` / `MessageChain`は従来の変更可能なメッセージ構築を維持し、実行には新しいリクエスト経路を使います。設定の分岐と再利用には`CreateRequest`を使います。ビルダーAPIは`AIService`とそのプロバイダー実装にあり、`IAIService`に必須メンバーを追加しません。抽象インターフェイスやRAGラッパーの利用者は既存のプロファイル・コンテキストと実行APIを使えます。

[共通の対応定義でモデルの機能選択を構成する](model-capabilities.md).
