# ベクターストア操作

## Upsert

単一レコードを挿入または更新します。同じ`Id`を持つレコードがすでに存在する場合は置き換えられます。

```csharp
var record = new VectorRecord
{
    Id      = Guid.NewGuid().ToString(),
    Vector  = await embeddingService.GetEmbeddingAsync("返金は30日以内に受け付けます。"),
    Content = "返金は30日以内に受け付けます。",
    Metadata = new Dictionary<string, string>
    {
        ["source"]   = "faq.pdf",
        ["language"] = "ja",
        ["section"]  = "returns"
    }
};

await store.UpsertAsync(record);
```

## バッチUpsert

単一呼び出しで複数のレコードをupsertします。ループで`UpsertAsync`を呼び出すより効率的です — バックエンドは内部でバッチAPIを使用します。

```csharp
var records = chunks.Select(chunk => new VectorRecord
{
    Id      = Guid.NewGuid().ToString(),
    Vector  = chunk.Embedding,
    Content = chunk.Text,
    Metadata = new Dictionary<string, string>
    {
        ["source"] = "manual.pdf",
        ["page"]   = chunk.Page.ToString()
    }
});

await store.UpsertBatchAsync(records);
```

## 検索

クエリベクターに最も類似したtop-Kレコードを返します。スコアリング前にメタデータでフィルタリングできます。

```csharp
float[] queryVector = await embeddingService.GetEmbeddingAsync("返金ポリシーは何ですか？");

var results = await store.SearchAsync(queryVector, topK: 5);

foreach (var r in results)
{
    Console.WriteLine($"[{r.Score:F3}] {r.Record.Content}");
    Console.WriteLine($"  出典: {r.Record.Metadata["source"]}");
}
```

### フィルター検索

ベクター類似度とメタデータフィルタリングを組み合わせます:

```csharp
var filter = new VectorFilter()
    .Where("language", "ja")
    .Where("section", "returns")
    .WithMinScore(0.7);

var results = await store.SearchAsync(queryVector, topK: 5, filter: filter);
```

## ハイブリッド検索

密なベクター類似度とキーワード（BM25）検索を組み合わせます。特定の用語、名前、コードを含むクエリでより高いリコールを提供します。

```csharp
float[] queryVector = await embeddingService.GetEmbeddingAsync("注文 #12345 状態");

var results = await store.HybridSearchAsync(
    denseVector: queryVector,
    query: "注文 #12345 状態",
    topK: 5
);
```

## IDで取得

特定のレコードをIDで検索します:

```csharp
VectorRecord? record = await store.GetAsync("record-id-123");
```

## バッチ取得

単一呼び出しで複数のレコードをIDで取得します:

```csharp
var ids = new[] { "id-1", "id-2", "id-3" };
var records = await store.GetBatchAsync(ids);
```

## IDで削除

単一レコードを削除します:

```csharp
await store.DeleteAsync("record-id-123");
```

## フィルターで削除

フィルターに一致するすべてのレコードを削除します。注意して使用してください — 一括削除です。

```csharp
var filter = new VectorFilter().Where("source", "old-manual.pdf");
await store.DeleteByFilterAsync(filter);
```

## フィルターで置換

フィルターに一致するすべてのレコードをアトミックに削除して新しいセットを挿入します。古いチャンクを残さずにドキュメントを再インデックスするのに便利です。

```csharp
var filter = new VectorFilter().Where("source", "manual-v1.pdf");

var newRecords = newChunks.Select(c => new VectorRecord
{
    Id      = Guid.NewGuid().ToString(),
    Vector  = c.Embedding,
    Content = c.Text,
    Metadata = new Dictionary<string, string> { ["source"] = "manual-v2.pdf" }
}).ToList();

await store.ReplaceByFilterAsync(filter, newRecords);
```

> Postgresではトランザクション内で実行され、完全にアトミックです。

## カウント

保存されたレコード数を計算します:

```csharp
long total    = await store.CountAsync();
long japanese = await store.CountAsync(new VectorFilter().Where("language", "ja"));
Console.WriteLine($"合計: {total}, 日本語: {japanese}");
```

## 接続確認

バックエンドに到達可能かどうかを確認します:

```csharp
try
{
    await store.VerifyConnectionAsync();
    Console.WriteLine("ベクターストア接続正常");
}
catch (Exception ex)
{
    Console.WriteLine($"接続失敗: {ex.Message}");
}
```

## RAGでの使用

```csharp
var store = new QdrantStore(new QdrantOptions
{
    CollectionName = "knowledge-base",
    Dimension      = 1536
});

var ragService = new AnthropicService(apiKey, http)
    .WithRag(rag => rag
        .UseStore(store)
        .UseOpenAIEmbedding(embeddingKey, http)
        .AddDirectory("docs/", ".txt", ".md")
    );

var answer = await ragService.GetCompletionAsync("返品ポリシーは何ですか？");
```
