# トークン使用量

トークン使用量は、モデルへのリクエストで入力、出力、キャッシュ、推論にどれだけトークンが使われたかを表します。Mythosia.AI では、ストリーミングイベントの `TokenUsage` として受け取れます。

特に、1 回の LLM 呼び出しだけで終わらない処理で重要になります。通常の回答は 1 ラウンドで終わることが多いですが、agent や function calling では、モデル呼び出し、関数実行、関数結果を含めた次のモデル呼び出し、という流れになります。そのため、見るべき使用量は 2 種類あります。

- `RoundUsage` は、直前に完了した LLM ラウンド 1 回分の使用量です。
- `Completion.Usage` は、ストリーミング実行全体の累積使用量です。

## ラウンドとは何か

「ラウンド」とは、モデルへリクエストを送り、応答を受け取るまでの 1 往復のことです。普通のチャットメッセージであれば、やり取りはちょうど 1 ラウンドで終わります。

Function calling や agent を使うと、ラウンドが自動的に増えます。具体的な例として、ユーザーが *「今の東京の天気を教えて」* と尋ねた場合を見てみましょう。

**ラウンド 1 — ツールの決定**

アプリがユーザーのメッセージをモデルに送ります。モデルは現在の天気を知る方法がないため、直接答えるのではなく、関数呼び出しリクエストを返します。*「`GetWeather("Tokyo")` を呼び出してください」* — ここでモデルの返答が終わります。

**ラウンドの間**

アプリが `GetWeather("Tokyo")` を実行し、結果 `"15°C、くもり"` を受け取ります。

**ラウンド 2 — 最終回答**

アプリが関数の結果を新しいメッセージとしてモデルに送り返します。必要な情報が揃ったモデルは、ユーザーへの最終回答を生成します。*「現在、東京は 15°C でくもりです。」*

ユーザーのメッセージ 1 件に対して、LLM ラウンドが 2 回発生しました。別のツールをもう 1 回呼び出す必要があれば、3 ラウンドになります。

`RoundUsage` は各ラウンドが終わるたびに発火し、そのラウンドだけのトークン数を保持します。`Completion.Usage` はすべてが完了したときに 1 回だけ発火し、全ラウンドの合計を保持します。

## なぜ必要か

チャット UI のコンテキストメーターには、最後に受け取った `RoundUsage.Usage.TotalTokens` が向いています。これは「この会話状態を次の LLM 呼び出しの入力にしたら、どれくらいの大きさになるか」に近い値です。

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
        UpdateContextTokenMeter(chunk.Usage.TotalTokens);

        if (chunk.IsFinalRound)
            MarkTokenMeterAsFinal();

        continue;
    }

    if (chunk.Type == StreamingContentType.Text)
        AppendToChat(chunk.Content);
}
```

最後のモデルラウンドは、関数結果まで反映された最新の会話状態を見ています。そのため、チャット UI では最後の `RoundUsage.TotalTokens` が、応答直後のコンテキストサイズを最もよく表します。

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
        Console.WriteLine($"Round {chunk.RoundIndex}: {latestRound.TotalTokens} tokens");
        continue;
    }

    if (chunk.Type == StreamingContentType.Completion)
        cumulative = chunk.Usage;
}

Console.WriteLine($"UI meter: {latestRound?.TotalTokens}");
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

## Provider ごとの注意点

provider によって、usage データが付くストリーム chunk は異なります。Mythosia.AI はそれを `RoundUsage` と最終 `Completion.Usage` に正規化して渡します。

Gemini は特に注意が必要です。usage が text や status chunk に付くことがあり、function call chunk の後に遅れて届く場合もあります。ライブラリは次のラウンドへ進む前にストリームを最後まで読み、usage を取りこぼさないようにします。

利用側のコードでは、provider 固有の metadata を直接読むより、正規化された `RoundUsage` と `Completion.Usage` を使うことをおすすめします。
