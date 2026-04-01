# VectorFilter

`VectorFilter`はメタデータでベクターストアクエリをフィルタリングするフルーエントAPIです。`IVectorStore.SearchAsync`、`HybridSearchAsync`、RAGクエリに適用されます。

## 基本的な等値比較

```csharp
var filter = new VectorFilter()
    .Where("source", "manual.pdf")
    .Where("language", "ja");
```

## 比較演算子

```csharp
var filter = new VectorFilter()
    .WhereGreaterThan("date", "2024-01-01")
    .WhereLessThanOrEqual("priority", "3")
    .WhereNot("status", "archived");
```

| メソッド | SQL同等 |
|---------|---------|
| `.Where(key, value)` | `key = value` |
| `.WhereNot(key, value)` | `key != value` |
| `.WhereGreaterThan(key, value)` | `key > value` |
| `.WhereGreaterThanOrEqual(key, value)` | `key >= value` |
| `.WhereLessThan(key, value)` | `key < value` |
| `.WhereLessThanOrEqual(key, value)` | `key <= value` |
| `.WhereLike(key, pattern)` | `key LIKE pattern` |

## セットメンバーシップ

```csharp
var filter = new VectorFilter()
    .WhereIn("category", "legal", "compliance", "policy")
    .WhereNotIn("type", "draft", "archived");
```

## キーの存在確認

```csharp
var filter = new VectorFilter()
    .WhereExists("reviewed_by")      // キーが存在する必要がある
    .WhereNotExists("deprecated");   // キーが存在してはならない
```

## 論理グループ化（AND / OR）

同じレベルの条件はデフォルトでANDで結合されます。`.Or()`を使ってORグループを作成します:

```csharp
var filter = new VectorFilter()
    .Where("source", "manual.pdf")
    .Or(f => f
        .Where("type", "urgent")
        .Where("priority", "high")
    );
// source = "manual.pdf" AND (type = "urgent" OR priority = "high")
```

ネストされたAND:

```csharp
var filter = new VectorFilter()
    .Or(f => f
        .And(a => a.Where("lang", "ja").Where("region", "jp"))
        .And(a => a.Where("lang", "en").Where("region", "us"))
    );
// (lang = "ja" AND region = "jp") OR (lang = "en" AND region = "us")
```

## スコア閾値

```csharp
var filter = new VectorFilter()
    .Where("source", "faq.pdf")
    .WithMinScore(0.75);
```

## ベクターストアでの使用

```csharp
var filter = new VectorFilter()
    .Where("document_type", "contract")
    .WhereGreaterThan("year", "2023");

var results = await vectorStore.SearchAsync(
    queryVector: embedding,
    topK: 5,
    filter: filter
);
```

## RAGでの使用

`RagQueryOptions`の`StoreFilter`として渡します:

```csharp
var options = new RagQueryOptions
{
    StoreFilter = new VectorFilter()
        .Where("source", "product-manual.pdf")
        .WithMinScore(0.7)
};

var response = await ragService.GetCompletionAsync("デバイスをリセットするにはどうすればよいですか？", options);
```
