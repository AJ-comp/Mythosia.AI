# 推論の深さを選び、出典のある回答を得る

> これらの API は `Mythosia.AI` 7.1.0 以降で利用でき、`Mythosia.AI.Abstractions` 3.1.0 以降を含みます。RAG の例には `Mythosia.AI.Rag` 7.6.0 以降が必要です。

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
    .WithReasoning(ReasoningLevel.Low)
    .GetCompletionAsync("移行計画の概要を作成してください。");

string review = await service
    .WithReasoning(ReasoningLevel.High)
    .GetCompletionAsync("その計画の障害シナリオと復旧手順を検証してください。");
```

`ReasoningLevel` は要求する推論レベルを表します。固定のトークン予算でも、回答品質の保証でもありません。使用できる値はモデルによって異なります。`Auto` はプロバイダーの設定または既定の動作を維持し、非対応のレベルを自動で別の値に置き換える意味ではありません。名前付きレベルではなくトークン予算を公開するモデルでは、従来のプロバイダー固有の予算プロパティを引き続き使えます。

長い会話では、リクエストの最上位にある推論設定を変えると、再利用できるプロンプトのプレフィックスが無効になることがあります。対応モデルでは、プレフィックスを保持しながらレベルを変えるプロバイダーの仕組みを要求できます。

```csharp
string review = await service
    .WithReasoning(ReasoningLevel.High, cache: CachePreservation.Required)
    .GetCompletionAsync("前の回答で使った前提をもう一度検証してください。");
```

`Required` は変更の送信方法についての契約です。キャッシュヒット、無料のトークン、短い待ち時間を**保証するものではありません**。プロバイダーのキャッシュ対象条件、保存期間、料金は引き続き適用されます。非対応モデルでは送信前に `NotSupportedException` が発生します。同じ追跡済みの会話、モデル、エンドポイントを使用し、これらの更新を含む履歴を切り詰めたり並べ替えたりしないでください。条件を変える場合は新しい会話を開始します。プレフィックスの保持が必要な間は、自動圧縮を行いません。

受け入れられたキャッシュ保持付きの推論レベルは、次に明示的に変更するまで会話で有効です。通常の `WithReasoning(level)` はその論理リクエストにだけ適用され、持続する設定を暗黙に置き換えません。この変更は**モデルの応答と応答の間**で行われます。すでに生成中の応答の推論レベルは変更せず、対応する実行中の Run に追加指示を送る `run.SteerAsync` とは別の仕組みです。

## 最新情報が必要な質問に答える

モデルの学習データ以外の情報を使う必要がある場合は、ネイティブの Web 検索を有効にします。

```csharp
string answer = await service
    .WithWebSearch()
    .GetCompletionAsync("最新のリリース発表を検索し、出典を示してください。");

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
    .WithFileSearch(documents)
    .GetCompletionAsync("当社の規約文書を検索してください。解約可能な期間はいつまでですか？");

foreach (AICitation source in service.GetLastCitations())
    Console.WriteLine($"{source.Title}: {source.FileId ?? source.Url}");
```

Google では、Google サービスと `new FileSearchStore("Google", "fileSearchStores/your-existing-store")` を組み合わせます。ストアはプロバイダー、アカウント、デプロイ環境に属します。OpenAI のストア ID を Google に渡すことはできません。先にプロバイダーの API またはコンソールでストアを作成し、文書をアップロードして索引を作成してください。この API は既存のストアだけを検索し、ローカルファイルはアップロードしません。

ホスト型のファイル検索とライブラリの [RAG パイプライン](rag.md) は、異なる構成上の要件に対応します。プロバイダーが索引を管理している場合はホスト型検索を選びます。ローダー、分割、埋め込み、検索、ベクトルストアをアプリケーションで制御したい場合は RAG を選びます。`RagEnabledService` は `WithReasoning`、`WithWebSearch`、`WithFileSearch` を最終回答に転送しますが、内部のクエリ書き換えはこれらを引き継ぎません。RAG の検索参照は `RagProcessedQuery` に残り、プロバイダー由来の `AICitation` とは別に扱います。

## 進捗を表示し、出典を保持する

`StartRunAsync` の前にも同じ設定を使用できます。テキストコールバックで画面を更新しながら、完成した回答に必要な出典を Run に保持できます。

```csharp
await using var run = await service
    .WithReasoning(ReasoningLevel.High)
    .WithWebSearch()
    .StartRunAsync(
        "最近の発表を検索し、変更点を比較してください。",
        onText: text => Console.Write(text),
        cancellationToken: cancellationToken);

string answer = await run.Result;
foreach (AICitation source in run.Citations)
    Console.WriteLine($"{source.Title}: {source.Url ?? source.FileId}");
```

`run.Citations` はストリームを読まなくても、テキストだけを観測していても、出力観測用バッファが満杯になっても利用できます。途中の応答を含め、Run の実行中にプロバイダーから集めた出典が入ります。`service.LastCitations`（`IAIService` 経由では `GetLastCitations()`）は直近の論理リクエストを表します。複数の回答を表示する場合は Run 自体を保持するか、出典のスナップショットをコピーしてください。

出典イベントを到着時に受け取る場合は、イベントを読む処理を一つだけ用意します。

```csharp
await using var run = await service.WithWebSearch().StartRunAsync(
    "最新の変更点を検索して説明してください。", cancellationToken: cancellationToken);

await foreach (var item in run.StreamAsync())
{
    if (item.Type == StreamingContentType.Text)
        Console.Write(item.Content);
    else if (item.Type == StreamingContentType.Citation && item.Citation is AICitation source)
        Console.WriteLine($"\n出典: {source.Title} {source.Url ?? source.FileId}");
}
string answer = await run.Result;
```

プロバイダーが値を返さない出典フィールドは null になります。`ResponseId`、`OutputIndex`、`ContentIndex` は元の応答やコンテンツ部分を識別します。`StartIndex` と `EndIndex` はプロバイダーの部分内オフセットと添字の規則を保持し、連結後の `run.Result` に対する位置では**ありません**。これらの値をそのまま完成した回答の添字として使って、出典を配置しないでください。

## プロバイダーの対応範囲とリクエストの適用範囲を確認する

| 統合プロバイダー | 名前付き推論レベル | キャッシュを保持する変更 | Web 検索 | ファイル検索 |
| --- | --- | --- | --- | --- |
| OpenAI | 対応する推論モデル。レベルはモデルごとに異なる | GPT-6 Astra Standard、単一エージェントモード | 対応する Responses モデル | 対応する Responses モデル、既存のベクトルストア |
| Anthropic | ネイティブの effort 制御に対応するモデル | 対応する Opus 5 / Fable 5.1 / Mythos 5.1 とプロバイダーのベータ機能 | 対応する Claude モデル | ネイティブストアのアダプターなし。RAG を使用 |
| Google | Gemini 3 のレベル。Gemini 2.5 ではプロバイダー固有の予算を維持 | 非対応 | 対応する Gemini テキストモデル | 対応する Gemini テキストモデル、既存のファイル検索ストア |
| その他のサービス | 従来のプロバイダー固有設定は引き続き利用可能。共通設定にはアダプターが必要 | 今回のアダプター群では非対応 | 共通アダプターなし | 共通アダプターなし |

モデル、レベル、通信方式、設定の組み合わせは送信前に検証されます。特に、**Google の Web 検索とファイル検索は同じリクエストで併用できません**。ライブラリは機能を黙って削除したり、推論レベルを下げたり、ドメイン制限を無視したり、外部検索サービスに切り替えたりしません。対応している場合は、ネイティブツールと登録済みのクライアント関数を併用できます。Run のツールラウンドには引き続き関数ポリシーと `WithMaxRounds` が適用されます。

Fluent メソッドはサービスの具象型を保ち、渡された設定をコピーします。null でない各設定は次の論理リクエストに向けて合成され、ツールラウンドと構造化出力の修正呼び出しにも適用された後で消費されます。後の無関係な呼び出しでは検索は有効になりません。必要なときに `WithWebSearch` または `WithFileSearch` をもう一度指定してください。開始済みの Run は取得済みの設定を維持します。他の変更可能なサービス設定と同様、リクエスト実行中に同じサービスの設定を変えたり、別のリクエストを重ねて開始したりしないでください。

独自の `IAIService` 実装との互換性は維持されます。この機能を提供する実装は任意の `IAIRequestFeatureService` を実装します。その機能を持たない実装でヘルパーを呼ぶと、明示的に例外が発生します。従来の完了、ストリーミング、プロバイダー固有設定 API は引き続き使用できます。キャンセル、出力の観測、追加指示については [Run の制御](execution-api-transition.md)を参照してください。

プロバイダーのプロトコル: [OpenAI の推論変更](https://developers.openai.com/api/docs/guides/reasoning#change-reasoning-mid-conversation)、[OpenAI のツール](https://developers.openai.com/api/docs/guides/tools)、[Anthropic の effort 変更](https://platform.claude.com/docs/en/build-with-claude/effort#change-effort-mid-conversation)、[Anthropic の Web 検索](https://platform.claude.com/docs/en/agents-and-tools/tool-use/web-search-tool)、[Google Search による根拠付け](https://ai.google.dev/gemini-api/docs/google-search)、[Google File Search](https://ai.google.dev/gemini-api/docs/file-search)。
