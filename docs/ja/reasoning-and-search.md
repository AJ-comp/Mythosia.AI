# 推論の深さを選び、出典のある回答を得る

> Grok 4.7: Mythosia.AI 8.1.0 / Abstractions 4.1.0 が必要です。 [モデル選択・推論・処理速度](providers.md#grok-47)

> GPT-6 Sol/Luna: Mythosia.AI 8.1.0 / Abstractions 4.1.0 が必要です。 [モデルの選択と必要バージョン](providers.md#gpt-6-sol-luna)

[Claude Opus 5.5](providers.md#claude-opus-55) は Mythosia.AI 8.1.0 / Abstractions 4.1.0 で利用できます。推論は常時有効、既定の effort は medium、表示は省略です。読める進行状況は明示的に指定します。既定値とモデル binding は Fable 5.1 と異なります。

設定をリクエストごとに分離し、共通設定から分岐するには[リクエストビルダー](request-building.md)を使います。`CreateRequest(...)`の後に`With...`をつなぎます。サービスのプロパティとfluentメソッドは従来の動作を維持します。

> これらの API は `Mythosia.AI` 7.1.0 以降で利用でき、`Mythosia.AI.Abstractions` 3.1.0 以降を含みます。RAG の例には `Mythosia.AI.Rag` 7.6.0 以降が必要です。

> `CreateRequest`の例にはMythosia.AI 8.0.0 / Abstractions 4.0.0が必要です。Runと共通リクエスト機能を導入した旧7.1リリースにはビルダーがありません。旧パッケージでは既存のサービスオーバーロードを使えます。

[Claude Fable 5.1](fable-5-1.md) は `Mythosia.AI` 8.0.0 / `Mythosia.AI.Abstractions` 4.0.0 から進捗更新、ターン限定指示、thinking binding 診断を利用できます。Mythos 5.1 は招待制です。両モデルともツール選択の強制を拒否します。

待ち時間が重要なリクエストでは[処理速度](request-building.md#inference-speed)を選べます。`WithSpeed` はモデルと推論レベルを保持し、`Processing` は実際に適用されたモードを示します。Fast は対応する組み合わせで使う有料設定です。

## これらの設定が必要な理由

作業の段階によって、必要な支援は異なります。最初の下書きは素早く作り、その前提を検証するときは推論に時間をかけたいことがあります。今日の出来事には最新情報が必要で、製品についての質問にはその製品の文書が必要です。推論レベルを上げるだけでは、どちらの情報源もモデルに渡りません。

共通の Fluent API で、次のタスクに必要な条件を指定します。選択したプロバイダーが、対応している設定をネイティブ API の形式に変換します。完成した回答を受け取るなら `GetCompletionAsync`、進捗を表示しながら同じタスクを制御するなら `StartRunAsync` を使えます。

| タスクで必要なこと | 設定 |
| --- | --- |
| 素早く下書きを作り、その後に詳しく検証する | `WithReasoning(...)` |
| 対応する会話のキャッシュプレフィックスを保持して推論レベルを変える | `WithReasoning(..., cache: CachePreservation.Required)` |
| Web から最新情報を得る | `WithWebSearch()` |
| プロバイダーが索引を作成済みの文書に基づいて回答する | `WithFileSearch(store)` |

例では、対応モデルで初期化済みのサービスを使用します。`Mythosia.AI.Extensions` と `Mythosia.AI.Models` をインポートしてください。ストリームイベントには `Mythosia.AI.Models.Streaming` も使用します。

## 素早い下書きから詳しい検証へ進む

概要の作成では推論を抑え、その後に同じ会話で難しい点を検討できます。

```csharp
string outline = await service
    .CreateRequest("移行計画の概要を作成してください。")
    .WithReasoning(ReasoningLevel.Low)
    .GetCompletionAsync();

string review = await service
    .CreateRequest("その計画の障害シナリオと復旧手順を検証してください。")
    .WithReasoning(ReasoningLevel.High)
    .GetCompletionAsync();
```

Gemini 3.7/3.8 Flash は `WithReasoning` の `Low`、`Medium`、`High` に対応します。`Minimal`、`None`、`CachePreservation.Required` は非対応です。補完、ストリーミング、Run、ツール呼び出し、組み込み検索には既存の処理経路と Google の組み合わせ制限を適用します。[Google の設定例](providers.md#google-googleaiservice)を参照してください。

`ReasoningLevel` は要求する推論レベルを表します。固定のトークン予算でも、回答品質の保証でもありません。使用できる値はモデルによって異なります。`Auto` はプロバイダーの設定または既定の動作を維持し、非対応のレベルを自動で別の値に置き換える意味ではありません。名前付きレベルではなくトークン予算を公開するモデルでは、従来のプロバイダー固有の予算プロパティを引き続き使えます。

長い会話では、リクエストの最上位にある推論設定を変えると、再利用できるプロンプトのプレフィックスが無効になることがあります。対応モデルでは、プレフィックスを保持しながらレベルを変えるプロバイダーの仕組みを要求できます。

```csharp
string review = await service
    .CreateRequest("前の回答で使った前提をもう一度検証してください。")
    .WithReasoning(ReasoningLevel.High, cache: CachePreservation.Required)
    .GetCompletionAsync();
```

`Required` は変更の送信方法についての契約です。キャッシュヒット、無料のトークン、短い待ち時間を**保証するものではありません**。プロバイダーのキャッシュ対象条件、保存期間、料金は引き続き適用されます。非対応モデルでは送信前に `NotSupportedException` が発生します。同じ追跡済みの会話、モデル、エンドポイントを使用し、これらの更新を含む履歴を切り詰めたり並べ替えたりしないでください。条件を変える場合は新しい会話を開始します。プレフィックスの保持が必要な間は、自動圧縮を行いません。

受け入れられたキャッシュ保持付きの推論レベルは、次に明示的に変更するまで会話で有効です。通常の `WithReasoning(level)` はその論理リクエストにだけ適用され、持続する設定を暗黙に置き換えません。この変更は**モデルの応答と応答の間**で行われます。すでに生成中の応答の推論レベルは変更せず、対応する実行中の Run に追加指示を送る `run.SteerAsync` とは別の仕組みです。

## 最新情報が必要な質問に答える

モデルの学習データ以外の情報を使う必要がある場合は、ネイティブの Web 検索を有効にします。

```csharp
string answer = await service
    .CreateRequest("最新のリリース発表を検索し、出典を示してください。")
    .WithWebSearch()
    .GetCompletionAsync();

foreach (AICitation source in service.GetLastCitations())
    Console.WriteLine($"{source.Title}: {source.Url}");
```

このホスト型ツールはプロバイダー側で実行されます。ローカルの関数ハンドラーを登録したり実行したりする必要はありません。検索を有効にするとモデルが利用できるようになりますが、その質問には検索が不要だとモデルが判断することもあります。出典はプロバイダーが返した場合に取得できます。

OpenAI と Anthropic では `WithWebSearch(new WebSearchOptions { AllowedDomains = new[] { "example.com" } })` も使えます。Google の統合ツールではこの許可リストを指定できないため、ドメイン制限を要求した場合は、Web 全体を検索する代わりにリクエストを拒否します。

## プロバイダーが索引を作成済みの文書から回答する

アプリケーションがすでにプロバイダー上の文書索引を利用している場合は、そのストアを使うことで、独自の検索処理を実装せずに文書を根拠にした回答を得られます。

```csharp
var documents = new FileSearchStore("OpenAI", "vs_your_existing_store");

string answer = await service
    .CreateRequest("当社の規約文書を検索してください。解約可能な期間はいつまでですか？")
    .WithFileSearch(documents)
    .GetCompletionAsync();

foreach (AICitation source in service.GetLastCitations())
    Console.WriteLine($"{source.Title}: {source.FileId ?? source.Url}");
```

Google では、Google サービスと `new FileSearchStore("Google", "fileSearchStores/your-existing-store")` を組み合わせます。ストアはプロバイダー、アカウント、デプロイ環境に属します。OpenAI のストア ID を Google に渡すことはできません。先にプロバイダーの API またはコンソールでストアを作成し、文書をアップロードして索引を作成してください。この API は既存のストアだけを検索し、ローカルファイルはアップロードしません。

`CreateRequest(...).With...`は独立したビルダーにオプションを保持します。同じビルダーを再利用すると、各実行とそのツールラウンドに同じ設定が適用されます。従来の`service.WithReasoning`、`service.WithWebSearch`、`service.WithFileSearch`は具体的なサービス型を返し、次の論理リクエストで設定を消費します。`IAIRequestFeatureService`やRAGラッパーの既存コードはこれらを使えます。どちらも同じサービスでの並列実行を保証しません。

## 進捗を表示し、出典を保持する

`StartRunAsync` の前にも同じ設定を使用できます。テキストコールバックで画面を更新しながら、完成した回答に必要な出典を Run に保持できます。

```csharp
await using var run = await service
    .CreateRequest("最近の発表を検索し、変更点を比較してください。")
    .WithReasoning(ReasoningLevel.High)
    .WithWebSearch()
    .StartRunAsync(
        onText: text => Console.Write(text),
        cancellationToken: cancellationToken);

string answer = (await run.Result).Text;
foreach (AICitation source in run.Citations)
    Console.WriteLine($"{source.Title}: {source.Url ?? source.FileId}");
```

`run.Citations` はストリームを読まなくても、テキストだけを観測していても、出力観測用バッファが満杯になっても利用できます。途中の応答を含め、Run の実行中にプロバイダーから集めた出典が入ります。`service.LastCitations`（`IAIService` 経由では `GetLastCitations()`）は直近の論理リクエストを表します。複数の回答を表示する場合は Run 自体を保持するか、出典のスナップショットをコピーしてください。

出典イベントを到着時に受け取る場合は、イベントを読む処理を一つだけ用意します。

```csharp
await using var run = await service
    .CreateRequest("最新の変更点を検索して説明してください。")
    .WithWebSearch()
    .StartRunAsync(cancellationToken: cancellationToken);

await foreach (var item in run.StreamAsync())
{
    if (item.Type == StreamingContentType.Text)
        Console.Write(item.Content);
    else if (item.Type == StreamingContentType.Citation && item.Citation is AICitation source)
        Console.WriteLine($"\n出典: {source.Title} {source.Url ?? source.FileId}");
}
string answer = (await run.Result).Text;
```

プロバイダーが値を返さない出典フィールドは null になります。`ResponseId`、`OutputIndex`、`ContentIndex` は元の応答やコンテンツ部分を識別します。`StartIndex` と `EndIndex` はプロバイダーの部分内オフセットと添字の規則を保持し、連結後の `(await run.Result).Text` に対する位置では**ありません**。これらの値をそのまま完成した回答の添字として使って、出典を配置しないでください。

## プロバイダーの対応範囲とリクエストの適用範囲を確認する

| 統合プロバイダー | 名前付き推論レベル | キャッシュを保持する変更 | Web 検索 | ファイル検索 |
| --- | --- | --- | --- | --- |
| OpenAI | 対応する推論モデル。レベルはモデルごとに異なる | GPT-6 Astra / Sol / Luna Standard、単一エージェントモード | 対応する Responses モデル | 対応する Responses モデル、既存のベクトルストア |
| Anthropic | ネイティブの effort 制御に対応するモデル | 対応する Opus 5 / 5.5 / Fable 5.1 / Mythos 5.1 とプロバイダーのベータ機能 | 対応する Claude モデル | ネイティブストアのアダプターなし。RAG を使用 |
| Google | Gemini 3 のレベル。Gemini 2.5 ではプロバイダー固有の予算を維持 | 非対応 | 対応する Gemini テキストモデル | 対応する Gemini テキストモデル、既存のファイル検索ストア |
| xAI | Grok 4.7 / 4.6: `Auto`、`Low`、`Medium`、`High`、`XHigh` | 非対応 | 共通アダプターなし | 共通アダプターなし |
| DeepSeek | Flash / V4 Pro: `Auto`, `None`, `Minimal`/`Low`, `Medium`/`High`/`XHigh`, `Max`; ネイティブ Low/High/Max へ対応付け | 未対応 | 共通アダプターなし | 共通アダプターなし |
| Perplexity | `Auto` またはモデルが対応する `Minimal`/`Low`/`Medium`/`High`/`XHigh`/`Max`。Sonar の明示的な effort は未対応 | 未対応 | Agent `web_search` | 共通アダプターなし |
| その他のサービス | 従来のプロバイダー固有設定は引き続き利用可能。共通設定にはアダプターが必要 | 今回のアダプター群では非対応 | 共通アダプターなし | 共通アダプターなし |

アダプターは既知のモデル、レベル、通信方式、組み合わせの制約を送信前に検証し、ローカルで判定できないモデル固有の規則はプロバイダーが検証します。特に、**Google の Web 検索とファイル検索は同じリクエストで併用できません**。ライブラリは機能を黙って削除したり、推論レベルを下げたり、ドメイン制限を無視したり、外部検索サービスに切り替えたりしません。対応している場合は、ネイティブツールと登録済みのクライアント関数を併用できます。Run のツールラウンドには引き続き関数ポリシーと `WithMaxRounds` が適用されます。

`CreateRequest(...).With...`は独立したビルダーにオプションを保持します。同じビルダーを再利用すると、各実行とそのツールラウンドに同じ設定が適用されます。従来の`service.WithReasoning`、`service.WithWebSearch`、`service.WithFileSearch`は具体的なサービス型を返し、次の論理リクエストで設定を消費します。`IAIRequestFeatureService`やRAGラッパーの既存コードはこれらを使えます。どちらも同じサービスでの並列実行を保証しません。

独自の `IAIService` 実装との互換性は維持されます。この機能を提供する実装は任意の `IAIRequestFeatureService` を実装します。その機能を持たない実装でヘルパーを呼ぶと、明示的に例外が発生します。従来の完了、ストリーミング、プロバイダー固有設定 API は引き続き使用できます。キャンセル、出力の観測、追加指示については [Run の制御](execution-api-transition.md)を参照してください。

プロバイダーのプロトコル: [OpenAI の推論変更](https://developers.openai.com/api/docs/guides/reasoning#change-reasoning-mid-conversation)、[OpenAI のツール](https://developers.openai.com/api/docs/guides/tools)、[Anthropic の effort 変更](https://platform.claude.com/docs/en/build-with-claude/effort#change-effort-mid-conversation)、[Anthropic の Web 検索](https://platform.claude.com/docs/en/agents-and-tools/tool-use/web-search-tool)、[Google Search による根拠付け](https://ai.google.dev/gemini-api/docs/google-search)、[Google File Search](https://ai.google.dev/gemini-api/docs/file-search)。

Perplexity の effort 対応は実際に選択されたモデルによって決まり、互換性のない組み合わせはサーバーで拒否される場合があります。`None` は未対応です。既定の Web 検索と preset/profile のツールは永続的なプロバイダー設定であり、共通リクエストオプションでは無効になりません。

Perplexity: [Perplexity Agent API、検索と埋め込み](perplexity.md).
