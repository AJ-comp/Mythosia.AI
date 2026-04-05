# フィルタリング

> 📍 **質問応答パイプライン:** [クエリ書き換え](rag-query-rewriting.md) → [埋め込み](rag-embedding.md) → **`フィルタリング`** → [検索](rag-hybrid-search.md) → [再ランキング](rag-reranking.md) → [コンテキスト構築](rag-context-build.md)

## フィルタリングとは？

フィルタリングは、類似度検索を実行する**前に**、どのチャンクを検索対象にするかを絞り込むステージです。ベクターストア全体を検索する代わりに、メタデータやスコアのしきい値に基づいて対象を限定します。

図書館での検索を想像してみてください。フィルタリングなしでは、建物全体のすべての本を探し回ることになります。フィルタリングがあれば、まず適切なセクション（「医学」「法律」など）に直行し、そこの棚だけを探索します。検索が速くなり、結果もより的確になります。

パイプラインでは2種類のフィルタリングが適用されます：

1. **メタデータフィルタリング** — チャンクに付与されたメタデータ（カテゴリ、テナント、日付など）に基づく絞り込み
2. **スコアフィルタリング** — 類似度スコアのしきい値を設定して、低品質な結果を除外

## メタデータフィルタリング

ベクターストアに保存された各チャンクには、インデックス作成時に付与されたメタデータ（キーと値のペア）があります。フィルタリングにより、特定の条件に一致するチャンクだけを検索対象にできます。

### クエリごとのフィルター

`VectorFilter`を渡して検索範囲を指定します：

```csharp
var filter = new VectorFilter()
    .Where("category", "refund-policy");

var result = await pipeline.QueryAsync("返金はどうすればいいですか？", filter: filter);
```

### フルエントフィルターAPI

`VectorFilter`は豊富な演算子をサポートしています：

```csharp
var filter = new VectorFilter()
    .Where("department", "engineering")         // 完全一致
    .WhereNot("status", "archived")             // 不一致
    .WhereIn("region", "us-east", "eu-west")    // セットに含まれる
    .WhereGreaterThan("year", "2023")           // 範囲比較
    .WhereLike("title", "%kubernetes%");        // パターンマッチング
```

使用可能な演算子：

| メソッド | SQLの同等表現 | 説明 |
| --- | --- | --- |
| `Where` | `=` | 完全一致 |
| `WhereNot` | `!=` | 不一致 |
| `WhereIn` | `IN (...)` | セットに含まれる |
| `WhereNotIn` | `NOT IN (...)` | セットに含まれない |
| `WhereGreaterThan` | `>` | より大きい |
| `WhereGreaterThanOrEqual` | `>=` | 以上 |
| `WhereLessThan` | `<` | より小さい |
| `WhereLessThanOrEqual` | `<=` | 以下 |
| `WhereLike` | `LIKE` | パターンマッチ（`%` = 任意の文字列、`_` = 任意の1文字） |
| `WhereExists` | `IS NOT NULL` | メタデータキーが存在する |
| `WhereNotExists` | `IS NULL` | メタデータキーが存在しない |

### 論理グループ化

AND/ORロジックで条件を組み合わせられます：

```csharp
var filter = new VectorFilter()
    .Where("tenant", "acme")
    .Or(f => f
        .Where("category", "billing")
        .Where("category", "refund")
    );
// マッチ条件：tenant = "acme" AND (category = "billing" OR category = "refund")
```

## パイプラインレベルのStoreFilter

テナント隔離のように**常に適用すべき条件**がある場合は、`RagQueryOptions`の`StoreFilter`を設定します。このフィルターはクエリごとのフィルターと自動的にマージされます：

```csharp
var options = new RagQueryOptions
{
    StoreFilter = new VectorFilter().Where("tenant_id", currentTenantId)
};

var response = await ragService.GetCompletionAsync("質問", ragOptions: options);
```

EF CoreのGlobal Query Filterと同じパターンです。StoreFilterは常に適用され、クエリごとのフィルターはその上にさらに条件を追加します。

### フィルターのマージ方法

パイプラインレベルの`StoreFilter`とクエリごとのフィルターが両方存在する場合、AND結合されます：

```
最終フィルター = StoreFilterの条件 AND クエリごとのフィルター条件
```

どちら側も無視されることはありません。StoreFilterの条件（パーミッション/テナント制約）が先に配置され、その後にクエリごとの条件が追加されます。

## スコアフィルタリング

`MinScore`しきい値は、類似度スコアが一定レベル以下のチャンクを除外します。関連性の低いチャンクがコンテキストを汚すのを防ぎます：

```csharp
var options = new RagQueryOptions
{
    FinalFilter = new RagFilter
    {
        TopK = 5,
        MinScore = 0.7   // 0.7未満のものは除外
    }
};
```

[再ランカー](rag-reranking.md)が設定されている場合、パイプラインは自動的に検索ステージのスコアしきい値を緩和します（`RetrievalDerivation.MinScoreDivider`を使用）。再ランカーにより広い候補群を与え、再ランキング後に厳密な`MinScore`を適用します。

## 一般的なユースケース

### マルチテナント隔離

テナントごとに自分のドキュメントだけが見えるようにします：

```csharp
// インデックス作成時 — テナントメタデータを付与
var doc = new RagDocument
{
    Id = "doc-1",
    Content = "...",
    Metadata = { ["tenant_id"] = "tenant-abc" }
};

// クエリ時 — テナントでフィルタリング
var options = new RagQueryOptions
{
    StoreFilter = new VectorFilter().Where("tenant_id", "tenant-abc")
};
```

### カテゴリ別検索

特定のドキュメントカテゴリ内だけを検索します：

```csharp
var filter = new VectorFilter().Where("category", "troubleshooting");
var result = await pipeline.QueryAsync("エラー404", filter: filter);
```

### 時間ベースのフィルタリング

最近のドキュメントだけに結果を限定します：

```csharp
var filter = new VectorFilter()
    .WhereGreaterThanOrEqual("updated_at", "2024-01-01");
```

## 内部の動作

フィルタリングステージは[埋め込み](rag-embedding.md)と[検索](rag-hybrid-search.md)の間に位置します：

```
クエリベクター（埋め込みから取得） + VectorFilter条件
    → StoreFilterとマージ（存在する場合）
    → MinScoreしきい値を適用
    → 検索戦略に渡して検索実行
```

フィルタリングは別のデータベースクエリを実行するわけではありません。ベクターストアの検索メソッドに条件が渡され、類似度検索の中で条件が適用されます。効率的でアトミックな処理です。

## 次のステップ

- [検索（ハイブリッド検索）](rag-hybrid-search.md) — ベクター検索とキーワード検索を組み合わせる
- [VectorFilterリファレンス](vector-filter.md) — フィルターAPIの完全なドキュメント
- [再ランキング](rag-reranking.md) — 検索後の結果精度をさらに高める
