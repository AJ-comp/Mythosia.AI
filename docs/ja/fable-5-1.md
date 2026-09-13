# Claude Fable 5.1 の長い作業を観察する

> Fable 5.1 の設定には `Mythosia.AI` 8.0.0 と `Mythosia.AI.Abstractions` 4.0.0 以降が必要です。既存の Run・推論/検索・GPT-6 API の最小バージョンは 7.1.0 / 3.1.0 のままです。

## この設定が必要になる場面

文書の調査では、回答までに複数の検索やツール呼び出しが必要になることがあります。アプリは進捗を表示したり、現在のターンだけに確認事項を指定したり、過去の会話を変更してから作業を続けたりします。Fable 5.1 はこれらに対応する設定を備えていますが、思考を再利用するときは会話履歴自体もリクエストの契約に含まれます。

作業の観察とキャンセルには [Run API](execution-api-transition.md)、推論量と情報源の選択には[共通の推論・検索設定](reasoning-and-search.md)を使います。進捗と履歴の扱いには以下の Claude 固有設定を使います。モデルのネイティブ機能がすべて Mythosia API に公開されるわけではありません。

## モデルと推論量を明示する

```csharp
using Mythosia.AI.Models;
using Mythosia.AI.Services.Anthropic;

var service = new AnthropicService(apiKey, httpClient);
service.ChangeModel(AIModels.Anthropic.ClaudeFable5_1);
service.WithAdaptiveThinkingParameters(ClaudeReasoningEffort.High);

string answer = await service.GetCompletionAsync("Review this migration plan.");
```

`ClaudeFable5_1` は `claude-fable-5-1` を選択します。`ClaudeMythos5_1` は `claude-mythos-5-1` を選択し、Project Glasswing のアクセス権が必要です。既存の Fable 5 と Mythos 5 の定数も残ります。両 5.1 モデルはテキスト・画像を入力しテキストを出力します。コンテキストは 1M トークン、最大出力は 128K トークンです。[モデル概要](https://platform.claude.com/docs/en/models/fable-5-1/overview)。

モデル自身の既定 effort は `high` ですが、Mythosia の `ClaudeReasoningEffort.Auto` は従来の `ThinkingBudget` 対応を維持します。有効な予算は基本 `High`、32,768 以上は `XHigh`、100,000 以上は `Max` になります。推論をオフにする要求は、低い adaptive effort と読み取り可能な思考の省略に置き換えます。`High` が必要なら明示してください。`Auto` は常に effort を省略してモデルの既定値に任せる意味ではありません。

## ツール呼び出しの間に進捗を表示する

`ClaudeThinkingDisplay.Updates` は推論を非表示にしたまま読める進捗を要求します。`Summarized` は要約された推論も返し、`Omitted` は読める thinking ブロックを省略します。進捗はモデルが生成したときに届き、一定間隔の通知は保証されません。[進捗更新](https://platform.claude.com/docs/en/models/fable-5-1/whats-new-fable-5-1#progress-updates-between-tool-calls-beta)。

```csharp
using Mythosia.AI.Models.Streaming;

service.WithAdaptiveThinkingParameters(
    ClaudeReasoningEffort.High, ClaudeThinkingDisplay.Updates);

await using var run = await service.StartRunAsync(
    "Use the registered tools to investigate the report.",
    options: StreamOptions.FullOptions);

await foreach (var item in run.StreamAsync())
{
    if (item.Type == StreamingContentType.Reasoning)
        Console.WriteLine($"Progress: {item.Content}");
    else if (item.Type == StreamingContentType.Text)
        Console.Write(item.Content);
}
string result = (await run.Result).Text;
```

進捗は既存の `StreamingContentType.Reasoning` イベントで届きます。`StreamOptions.FullOptions` または `StreamOptions.Default.WithReasoning()` で観察を有効にします。非ストリーミング呼び出しでは完了後に `service.LastThinkingContent` を読みます。進捗は最終回答と別であり、内部の思考過程そのものは公開しません。

## ターンごとの変更で過去の履歴を書き換えない

Fable 5.1 の thinking ブロックは生成時のシステムプロンプト、ツール、先行メッセージに結び付いています。後続の thinking を残したままそれらを書き換えると無効になる場合があります。今回の回答前にサポート方針を確認させる、といった指示にはターン限定の指示を使います。会話の末尾に追加して保持し、後続のユーザーメッセージが来ると適用が終わるため、最上位のシステムプロンプトを繰り返し変更せずに済みます。effort の変更とターンの指示は別の設定です。

```csharp
await service
    .WithTurnInstruction("Check the registered support-policy tool before answering this turn.")
    .GetCompletionAsync("Can I return an opened product?");

await service
    .WithConversationInstruction("Use Korean for the remaining conversation.")
    .GetCompletionAsync("Explain the next step.");
```

両メソッドは次の論理リクエストに指示を取り込みます。Mythosia は先行メッセージを保ち、ユーザー入力またはツール結果の後にシステムメッセージを追加します。`WithTurnInstruction` は `clear_at: "next_user_message"` を使用します。同じ論理リクエストの中ではツール結果のターンごとに指示を追加し直すため、そのリクエストが終わるまで有効です。`WithConversationInstruction` は後のターンにも適用されます。どちらも作業開始前に設定し、実行中の応答に介入する `run.SteerAsync` とは異なります。

リクエスト間で再利用可能なキャッシュ接頭部を保ちながら effort を変える場合は、`Mythosia.AI.Extensions` の `.WithReasoning(ReasoningLevel.High, cache: CachePreservation.Required)` を使います。ライブラリはメッセージ単位の effort 更新を送り履歴に保持します。対応する組み合わせは[共通ガイド](reasoning-and-search.md)に記載しています。5.1 では `AIRequestContext` のリクエスト単位のシステム接頭辞・接尾辞を、過去のシステムプロンプトを書き換える代わりに末尾のターン指示へ変換します。

Fable 5.1 は以前の Claude の thinking を利用できますが、以前のモデルは Fable 5.1 の thinking を利用できません。Mythos 5.1 は同じ 5.1 機能を持ちますが、Fable の接頭部 binding 検査は強制しません。履歴編集、モデル切り替え、thinking の削除があれば、同じ推論が維持されたと仮定せず変更を確認してください。[移行ガイド](https://platform.claude.com/docs/en/models/fable-5-1/migration-guide)。

## 意図的な履歴変更を診断する

`ThinkingPrefixMismatchBehavior = null` では検査をプロバイダーのアカウント方針に委ねます。`WithThinkingBinding(ClaudeThinkingPrefixMismatchBehavior.Error)` はサーバーでの検証を明示的に要求します。ユーザーによる履歴、`SystemMessage`、ツールの変更は Anthropic に送信し、`Error` で接頭部が一致しなければプロバイダーが 400 を返します。同じ不正なリクエストを再試行しても解決しません。

以前の内容を意図的に変更し、関連する推論を失ってもよい場合は `DropBlock` を選びます。Mythosia はこの制御を Anthropic に送り、送信前に thinking を黙って削除することはありません。

```csharp
service.WithThinkingBinding(ClaudeThinkingPrefixMismatchBehavior.DropBlock);
service.SystemMessage = "You are reviewing the revised policy.";
await service.GetCompletionAsync("Reassess the recommendation.");

foreach (ClaudeInputTransformation change in service.LastInputTransformations)
    Console.WriteLine($"{change.Type}: {change.Reason} ({change.Model})");
```

`LastInputTransformations` はプロバイダーが報告した `Type`、`Path`、`Reason` と、出所を示す `ResponseId`、`Model` を公開します。`prefix_binding_mismatch` は接頭部の変更、`model_binding_mismatch` は対象モデルが読めない thinking を示します。削除は推論の修復ではありません。維持が必要なら履歴をそのまま残し、リセットしたい場合は新しい会話を始めてください。

Mythosia は内部の RAG/context 処理で偶発的に変化しないよう送信履歴を保ちます。通常の Fable 5.1 会話では、既定値または `Error` のとき自動のローカル圧縮を阻止します。`DropBlock` では許可しますが、推論が失われる場合がありキャッシュヒットも保証しません。別途指定する `CachePreservation.Required` にはさらに厳しい履歴保護が残ります。 `WithWebSearch()` などの共通設定はリクエスト後に消費されます。次のターンで省略するとネイティブの tools 配列が変わり、接頭部が不一致になる場合があります。履歴を保つには同じツール・検索設定を再指定し、意図的に変える場合は `DropBlock` または新しい会話を使ってください。設定が自動で次のリクエストに引き継がれることはありません。

保存された送信スナップショットはサービスとその `ChatBlock` に属します。`ChatBlock` だけを新しいサービスへコピーしても、以前の RAG/context やターン system のスナップショットは移りません。推論を維持する場合は同じサービスと会話を使い、元の履歴だけを移した場合は維持を仮定せず新しい会話を始めてください。

## 通常のツール選択を使う

Fable 5.1 と Mythos 5.1 はツール選択の強制を拒否します。`ForceFunctionName` は設定せず、登録ツールを使う条件をリクエストに記述してください。ツールを呼び出してはいけないターンには `FunctionsDisabled` を使用できます。型付きの応答が目的なら、JSON を得るためだけに関数を強制せず、既存の構造化出力 API を使います。

## サーバーが担当する変更を理解する

| ネイティブ設定 | 必要な Anthropic beta |
| --- | --- |
| メッセージ単位の effort | `mid-conversation-output-config-2026-07-01` |
| ターン限定システムメッセージ | `mid-conversation-system-clear-at-2026-08-21` |
| `thinking.display: "updates"` | `thinking-display-updates-2026-08-18` |
| Thinking binding 制御と `input_transformations` | `thinking-binding-controls-2026-08-01` |

Mythosia は対応する設定を有効にすると必要なヘッダーを追加します。1 つの beta を有効にしても、他の beta がすべて有効になるわけではありません。この統合はサーバー側の compaction、ネイティブのツール追加・削除ブロック、自動モデル fallback を新設しません。

両モデルにはプロバイダーの適用する 30 日間のデータ保持条件が必要で、ZDR には Anthropic の明示的な承認が必要です。Adaptive thinking は常に有効で、手動の `budget_tokens` と推論無効化は使用できません。独自のサンプリングパラメーターも送信しません。アクセス権とデータ保持はサーバー側の要件です。[移行要件](https://platform.claude.com/docs/en/models/fable-5-1/migration-guide)。

テキストの透かし、対応メディアの来歴情報、キャッシュ読み取り料金は Anthropic が適用します。Mythosia の新しいリクエスト設定は不要です。この統合はメディア来歴の生成 API、透かしのスイッチ、課金制御を追加しません。[Fable 5.1 の変更点](https://platform.claude.com/docs/en/models/fable-5-1/whats-new-fable-5-1)を参照してください。
