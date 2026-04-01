# ベクターデータベース概要

Mythosia.AIは複数のベクターデータベースバックエンドで動作する統一された`IVectorStore`抽象化を提供します。インターフェースに対して一度記述するだけで、検索ロジックを変更せずにバックエンドを交換できます。

## コアインターフェース: `IVectorStore`

```csharp
// Upsert
Task UpsertAsync(VectorRecord record, CancellationToken cancellationToken = default);
Task UpsertBatchAsync(IEnumerable<VectorRecord> records, CancellationToken cancellationToken = default);

// 検索
Task<IReadOnlyList<VectorSearchResult>> SearchAsync(
    float[] queryVector, int topK = 5, VectorFilter? filter = null,
    CancellationToken cancellationToken = default);

Task<IReadOnlyList<VectorSearchResult>> HybridSearchAsync(
    float[] denseVector, string query, int topK = 5, VectorFilter? filter = null,
    CancellationToken cancellationToken = default);

// IDで取得
Task<VectorRecord?> GetAsync(string id, VectorFilter? filter = null,
    CancellationToken cancellationToken = default);
Task<IReadOnlyList<VectorRecord>> GetBatchAsync(IEnumerable<string> ids,
    VectorFilter? filter = null, CancellationToken cancellationToken = default);

// 削除
Task DeleteAsync(string id, VectorFilter? filter = null,
    CancellationToken cancellationToken = default);
Task DeleteByFilterAsync(VectorFilter filter, CancellationToken cancellationToken = default);
Task ReplaceByFilterAsync(VectorFilter filter, IReadOnlyList<VectorRecord> records,
    CancellationToken cancellationToken = default);

// ユーティリティ
Task<long> CountAsync(VectorFilter? filter = null, CancellationToken cancellationToken = default);
Task VerifyConnectionAsync(CancellationToken cancellationToken = default);
```

## データモデル

### VectorRecord

保存されるすべてのエントリは`VectorRecord`です:

```csharp
public class VectorRecord
{
    public string Id { get; set; }                            // 一意識別子
    public float[] Vector { get; set; }                       // 埋め込みベクター
    public string Content { get; set; }                       // 元のテキストコンテンツ
    public Dictionary<string, string> Metadata { get; set; } // カスタムキー・バリューメタデータ
}
```

カスタムフィールド（ソースファイル、言語、日付、カテゴリなど）には`Metadata`ディクショナリを使用します:

```csharp
var record = new VectorRecord
{
    Id = Guid.NewGuid().ToString(),
    Vector = await embeddingService.GetEmbeddingAsync("あるテキスト"),
    Content = "あるテキスト",
    Metadata = new Dictionary<string, string>
    {
        ["source"]   = "manual.pdf",
        ["language"] = "ja",
        ["date"]     = "2024-01-15",
        ["category"] = "policy"
    }
};
```

### VectorSearchResult

検索結果はレコードと類似度スコアをペアにして返します:

```csharp
public class VectorSearchResult
{
    public VectorRecord Record { get; set; }
    public double Score { get; set; }  // 0.0〜1.0（高いほど類似）
}
```

## 利用可能なバックエンド

| バックエンド | パッケージ | ユースケース |
|------------|---------|-----------|
| **InMemory** | `Mythosia.VectorDb.InMemory` | 開発、テスト、デモ |
| **Qdrant** | `Mythosia.VectorDb.Qdrant` | 本番、ネイティブハイブリッド検索 |
| **Pinecone** | `Mythosia.VectorDb.Pinecone` | サーバーレスマネージドサービス |
| **PostgreSQL** | `Mythosia.VectorDb.Postgres` | 既存Postgres環境、ACID保証 |

すべてのバックエンドは同じ`IVectorStore`インターフェースを実装します。バックエンドごとの設定は[バックエンド設定](vectordb-backends.md)を参照してください。

## 依存性注入

任意のバックエンドを`IVectorStore`として登録します:

```csharp
// InMemory
services.AddSingleton<IVectorStore>(new InMemoryVectorStore());

// Qdrant
services.AddSingleton<IVectorStore>(new QdrantStore(new QdrantOptions
{
    CollectionName = "my-collection",
    Dimension = 1536
}));

// PostgreSQL
services.AddSingleton<IVectorStore>(new PostgresStore(new PostgresOptions
{
    ConnectionString = "Host=localhost;Database=vectors;",
    Dimension = 1536,
    EnsureSchema = true
}));
```

## バックエンド別フィルター実行

`VectorFilter`条件は可能な場合バックエンドにプッシュダウンされます:

| 演算子 | InMemory | Qdrant | Pinecone | Postgres |
|--------|----------|--------|----------|---------|
| Eq / Ne | クライアント | **サーバー** | **サーバー** | **SQL** |
| In / NotIn | クライアント | **サーバー** | **サーバー** | **SQL** |
| Gt / Gte / Lt / Lte | クライアント | クライアント | クライアント | **SQL** |
| Like | クライアント | クライアント | クライアント | **SQL** |
| Exists / NotExists | クライアント | クライアント | クライアント | **SQL** |

Postgresはすべての演算子に対して完全なSQLプッシュダウンをサポートします。
