# トークン使用量

トークン使用量は、モデルへのリクエストで入力、出力、キャッシュ、推論にどれだけトークンが使われたかを表します。Mythosia.AI では、ストリーミングイベントの `TokenUsage` として受け取れます。

特に、1 回の LLM 呼び出しだけで終わらない処理で重要になります。通常の回答は 1 ラウンドで終わることが多いですが、agent や function calling では、モデル呼び出し、関数実行、関数結果を含めた次のモデル呼び出し、という流れになります。そのため、見るべき使用量は 2 種類あります。

- `RoundUsage` は、直前に完了した LLM ラウンド 1 回分の使用量です。
- `Completion.Usage` は、ストリーミング実行全体の累積使用量です。

> [!NOTE]
> このページは **LLM ラウンド** の概念を既に理解している前提です。簡単に言うと、ラウンド 1 回 = アプリとモデルの間の 1 往復のことです。function calling では 1 つのユーザーメッセージに対して複数のラウンドが発生することがあります。ステップごとの詳しい説明は [基本概念 — ラウンドとは何か](core-concepts.md#ラウンドとは何か) を参照してください。

## なぜ必要か

チャット UI のコンテキストメーターには、最後に受け取った `RoundUsage.Usage.InputTokens` が向いています。これは「この会話状態を次の LLM 呼び出しの入力にしたら、どれくらいの大きさになるか」に近い値です。

ログ、診断、コスト分析には `Completion.Usage.TotalTokens` を使います。function calling や agent のように複数ラウンドが発生しても、実行全体の累積値として扱えます。

性能調整では、キャッシュや推論関連のフィールドが役に立ちます。入力キャッシュが効いているか、reasoning モデルが内部推論にどれくらい使ったかを確認できます。

## イベントモデル

| イベント | 意味 | 主な用途 |
|---|---|---|
| `StreamingContentType.RoundUsage` | 完了した LLM ラウンドの使用量 | UI のコンテキストメーター、ラウンド単位のデバッグ |
| `StreamingContentType.Completion` | 最終ストリームイベントと累積使用量 | ログ、診断、コスト集計 |

`RoundUsage.Usage` は累積値ではありません。たとえば 1 ラウンド目が 10,100 トークン、2 ラウンド目が 14,000 トークンなら、最終的な `Completion.Usage.TotalTokens` は 24,100 になり得ますが、最後の `RoundUsage.Usage.TotalTokens` は 14,000 のままです。

| プロパティ | 意味 |
|---|---|
| `RoundIndex` | 1 から始まる LLM ラウンド番号 |
| `IsFinalRound` | このラウンドがストリーム内の最後の LLM ラウンドなら `true` |

トークン使用量は、provider が usage データを返したときに emit されます。usage イベントを受け取るために `IncludeMetadata = true` にする必要はありません。

## 最終的な累積使用量

ストリーミングリクエスト全体の使用量を見たい場合は、`Completion.Usage` を読みます。

```csharp
await foreach (var chunk in service.StreamAsync("量子コンピューティングを説明してください", StreamOptions.FullOptions))
{
    if (chunk.Type == StreamingContentType.Text)
        Console.Write(chunk.Content);

    if (chunk.Type == StreamingContentType.Completion && chunk.Usage is not null)
    {
        Console.WriteLine($"Input:  {chunk.Usage.InputTokens}");
        Console.WriteLine($"Output: {chunk.Usage.OutputTokens}");
        Console.WriteLine($"Total:  {chunk.Usage.TotalTokens}");
    }
}
```

1 回の LLM ラウンドだけなら、この値はラウンド使用量にかなり近くなります。agent 実行では、すべての LLM ラウンドを合算した値です。

## UI のトークンメーター

コンテキストサイズのメーターには、最新の `RoundUsage` を使います。

```csharp
await foreach (var chunk in service.StreamAsync(message, StreamOptions.FullOptions))
{
    if (chunk.Type == StreamingContentType.RoundUsage && chunk.Usage is not null)
    {
        UpdateContextTokenMeter(chunk.Usage.InputTokens);

        if (chunk.IsFinalRound)
            MarkTokenMeterAsFinal();

        continue;
    }

    if (chunk.Type == StreamingContentType.Text)
        AppendToChat(chunk.Content);
}
```

最後のモデルラウンドは、関数結果まで反映された最新の会話状態を見ています。そのため、チャット UI では最後の `RoundUsage.Usage.InputTokens` が、応答直後のコンテキストサイズを最もよく表します。

<a id="how-context-size-changes"></a>

## コンテキストサイズの変化

コンテキストサイズは累積合計ではなく、直近のモデル呼び出しに入った入力サイズとして考えます。後続のラウンドには、前のラウンドから残った会話要素がすでに含まれます。そのため、ラウンドごとの入力を足し合わせると、同じプロンプト、ツール定義、履歴を二重に数えてしまいます。

例:

| ステップ | このモデル呼び出しの前に追加されるもの | おおよその入力トークン | UI コンテキストメーター |
|---|---|---:|---:|
| ラウンド 1 | システムプロンプト、ツール、履歴、ユーザーメッセージ | 20,000 | 20,000 |
| ラウンド間 | tool call の出力が 100 トークン、ツール結果が 5,000 トークン | LLM 呼び出しなし | 変化なし |
| ラウンド 2 | ラウンド 1 の入力 + tool call メッセージ + ツール結果 | 25,100 + オーバーヘッド | 25,100 + オーバーヘッド |
| ラウンド 2 の出力 | モデルが 3,000 トークンを生成し、さらにラウンドが必要 | LLM 呼び出しなし | 変化なし |
| ラウンド 3 | ラウンド 2 の入力 + ラウンド 2 の出力、必要なら新しいツール結果 | 28,100 + オーバーヘッド | 28,100 + オーバーヘッド |
| ラウンド 3 の出力 | モデルが 2,000 トークンの最終回答を生成 | LLM 呼び出しなし | 変化なし |
| 次のユーザーメッセージ | 前回の最終回答と新しいユーザーメッセージが次の入力に含まれる | 約 30,100 + 新しいメッセージ + オーバーヘッド | 新しいラウンドの `InputTokens` に置き換わる |

したがってラウンド 3 が最終ラウンドなら、コンテキストメーターはおおよそ **28,100 + オーバーヘッド** を表示するのが正しく、30,100 でも全ラウンドの合計でもありません。2,000 トークンの最終回答は会話履歴になるため、次のモデル呼び出しで入力に含まれます。

## Function Calling と Agent

function calling では、モデルが複数回実行されることがあります。UI では毎回 `RoundUsage` を受け取り、最後の値を保持します。実行全体の累積値は最後の `Completion.Usage` で確認します。

```csharp
TokenUsage? latestRound = null;
TokenUsage? cumulative = null;

await foreach (var chunk in service.StreamAsync(message, StreamOptions.WithFunctions))
{
    if (chunk.Type == StreamingContentType.FunctionCall)
    {
        Console.WriteLine($"Calling function: {chunk.Content}");
        continue;
    }

    if (chunk.Type == StreamingContentType.FunctionResult)
    {
        Console.WriteLine($"Function result: {chunk.Content}");
        continue;
    }

    if (chunk.Type == StreamingContentType.RoundUsage && chunk.Usage is not null)
    {
        latestRound = chunk.Usage;
        Console.WriteLine($"Round {chunk.RoundIndex}: input={latestRound.InputTokens}, total={latestRound.TotalTokens} tokens");
        continue;
    }

    if (chunk.Type == StreamingContentType.Completion)
        cumulative = chunk.Usage;
}

Console.WriteLine($"UI meter: {latestRound?.InputTokens}");
Console.WriteLine($"Run total: {cumulative?.TotalTokens}");
```

## キャッシュと推論フィールド

provider が返す場合、`TokenUsage` にはキャッシュや推論に関する値も入ります。

| プロパティ | 意味 |
|---|---|
| `InputTokens` | プロンプト/入力のトークン |
| `OutputTokens` | モデルが生成した出力トークン |
| `TotalTokens` | そのイベント範囲での入力 + 出力 |
| `CachedInputTokens` | キャッシュから再利用された入力トークン |
| `CacheCreationTokens` | キャッシュに新しく書き込まれたトークン |
| `ReasoningTokens` | 非表示の内部推論に使われたトークン |
| `VisibleOutputTokens` | 推論トークンを除いた実際の出力トークン |

## なぜ正規化イベントを使うべきか

provider によって、usage データが付くストリーム chunk は異なります。特に Gemini は注意が必要で、usage が text や status chunk に付くことがあり、function call chunk の後に遅れて届く場合もあるため、Mythosia.AI は次のラウンドへ進む前にストリームを最後まで読み、その usage を取りこぼさないようにします。ライブラリはこうした provider ごとの差をすべて吸収し、`RoundUsage` と最終 `Completion.Usage` イベントに正規化して渡すので、利用側のコードでは provider 固有の metadata を直接読むのではなく、正規化された `RoundUsage` と `Completion.Usage` を使ってください。
