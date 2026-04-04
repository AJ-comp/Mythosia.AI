# バックエンド設定

## InMemory

最もシンプルなバックエンド — 外部依存関係なし。データはRAMに保持され、プロセス終了時に失われます。開発、テスト、デモに適しています。

```bash
dotnet add package Mythosia.VectorDb.InMemory
```

```csharp
using Mythosia.VectorDb.InMemory;

var store = new InMemoryVectorStore();
```

**内蔵ハイブリッド検索**: RRF（Reciprocal Rank Fusion）でコサイン類似度とBM25キーワードスコアを結合します。

### 診断メソッド

```csharp
// 保存されたすべてのレコードを列挙
var all = await store.ListAllRecordsAsync();
Console.WriteLine($"合計: {store.GetTotalRecordCount()}");

// 生の類似度スコアを確認
var scored = await store.ScoredListAsync(queryVector);
foreach (var r in scored)
    Console.WriteLine($"[{r.Score:F3}] {r.Record.Content[..60]}");
```

---

## Qdrant

ネイティブハイブリッド検索を備えた本番グレードのベクターデータベースです。DockerまたはQdrant Cloudで実行します。

```bash
dotnet add package Mythosia.VectorDb.Qdrant
```

```bash
# ローカルでQdrantを起動
docker run -p 6333:6333 -p 6334:6334 qdrant/qdrant
```

```csharp
using Mythosia.VectorDb.Qdrant;

var store = new QdrantStore(new QdrantOptions
{
    Host             = "localhost",
    Port             = 6334,             // gRPCポート
    CollectionName   = "my-docs",
    Dimension        = 1536,             // 埋め込みモデルと一致する必要がある
    AutoCreateCollection = true          // 最初のupsert時にコレクションを作成
});
```

### 全オプション

```csharp
new QdrantOptions
{
    Host                   = "localhost",
    Port                   = 6334,
    UseTls                 = false,
    ApiKey                 = null,              // Qdrant Cloudに必要

    CollectionName         = "my-collection",   // 必須
    Dimension              = 1536,              // 必須

    DistanceStrategy       = QdrantDistanceStrategy.Cosine,
    HybridFusionStrategy   = QdrantHybridFusionStrategy.Rrf,
    AutoCreateCollection   = true,

    // サーバー側フィルタリングの高速化のための追加ペイロードインデックス
    AdditionalPayloadIndexes = new List<QdrantIndexOption>
    {
        new QdrantIndexOption { Field = "meta.language", SchemaType = PayloadSchemaType.Keyword },
        new QdrantIndexOption { Field = "meta.date",     SchemaType = PayloadSchemaType.Integer }
    }
}
```

### 距離戦略

| 値 | 説明 |
|----|------|
| `Cosine` | コサイン類似度 — 正規化された埋め込みに最適（デフォルト） |
| `Euclidean` | L2距離 — 距離が低いほど類似 |
| `DotProduct` | 内積 — ユニット正規化ベクターと組み合わせて使用 |

### ハイブリッド融合戦略

| 値 | 説明 |
|----|------|
| `Rrf` | Reciprocal Rank Fusion — ランクベースの堅牢な結合（デフォルト） |
| `Dbsf` | 分布ベーススコア融合 — スコア分布で結合 |

### Qdrant Cloud

```csharp
new QdrantOptions
{
    Host           = "your-cluster.cloud.qdrant.io",
    Port           = 6334,
    UseTls         = true,
    ApiKey         = "your-qdrant-cloud-key",
    CollectionName = "production",
    Dimension      = 1536
}
```

### 外部 QdrantClient の使用

既に構成済みの `QdrantClient`（例: DI コンテナから）がある場合、直接渡せます:

```csharp
var store = new QdrantStore(options, existingQdrantClient);
```

外部から提供されたクライアントはストアが Dispose **しません**。

> すべてのベクターストアは `IDisposable` を実装しています。標準コンストラクタでストアを作成した場合は、`Dispose()` または `using` で内部リソースを解放してください。

---

## Pinecone

完全マネージドのサーバーレスベクターデータベースです。インフラ管理が不要です。

```bash
```

```csharp
using Mythosia.VectorDb.Pinecone;

var store = new PineconeStore(new PineconeOptions
{
    IndexHost = "https://my-index-xxxx.svc.us-east1-gcp.pinecone.io",
    ApiKey    = "your-api-key"
});
```

### インデックスの自動作成

まだインデックスがない場合、SDKに作成させることができます:

```csharp
new PineconeOptions
{
    ApiKey          = "your-api-key",
    AutoCreateIndex = true,
    IndexName       = "my-index",
    Dimension       = 1536,
    Cloud           = "aws",          // "aws", "gcp", "azure"
    Region          = "us-east-1"
}
```

> `AutoCreateIndex = true`の場合、ハイブリッド検索に必要な`dotproduct`メトリックでインデックスを作成します。

### 外部 HttpClient の使用

既に構成済みの `HttpClient`（例: `IHttpClientFactory` から）がある場合:

```csharp
var store = new PineconeStore(options, existingHttpClient);
```

外部から提供されたクライアントはストアが Dispose **しません**。

---

## PostgreSQL (pgvector)

標準PostgreSQLデータベースにベクター類似度検索を追加する[`pgvector`](https://github.com/pgvector/pgvector)拡張を使用します。

```bash
dotnet add package Mythosia.VectorDb.Postgres
```

### 前提条件

```sql
-- PostgreSQLサーバーで一度実行
CREATE EXTENSION IF NOT EXISTS vector;
CREATE EXTENSION IF NOT EXISTS pg_trgm;  -- Trigramテキスト検索を使用する場合のみ
```

または`EnsureSchema = true`でSDKに自動処理させることができます。

```csharp
using Mythosia.VectorDb.Postgres;

var store = new PostgresStore(new PostgresOptions
{
    ConnectionString = "Host=localhost;Port=5432;Database=mydb;Username=user;Password=pass;",
    Dimension        = 1536,
    EnsureSchema     = true    // 拡張、テーブル、インデックスを自動作成
});
```

### インデックスタイプ

| タイプ | クラス | 使用時期 |
|--------|--------|---------|
| HNSW | `HnswIndexOptions` | デフォルト。高速な近似検索。ほとんどのユースケースに適しています。 |
| IVFFlat | `IvfFlatIndexOptions` | メモリが少ない。大規模な静的データセットに適しています。 |
| None | `NoIndexOptions` | 順次スキャン。小規模データセットのみに使用。 |

```csharp
// HNSW（デフォルト）
new PostgresOptions
{
    Index = new HnswIndexOptions
    {
        M              = 16,   // ノードごとの最大近傍接続数
        EfConstruction = 64,   // インデックス構築時の検索範囲
        EfSearch       = 40    // ランタイム検索範囲
    }
}

// IVFFlat
new PostgresOptions
{
    Index = new IvfFlatIndexOptions
    {
        Lists  = 100,  // 反転リスト数
        Probes = 10    // クエリ時に探索するリスト数
    }
}
```

### テキスト検索モード

ハイブリッド検索のキーワード側に使用されます:

| モード | 最適言語 |
|--------|---------|
| `TsVector` | 標準全文検索 — 英語、ほとんどの西洋言語 |
| `Trigram` | CJK言語（日本語、韓国語、中国語）、ファジーマッチング |

```csharp
new PostgresOptions
{
    TextSearchMode   = TextSearchMode.Trigram,
    TextSearchConfig = "simple"
}
```

### 距離戦略

| 値 | Postgres演算子 | 備考 |
|----|--------------|------|
| `Cosine` | `<=>` | 1 − コサイン類似度（デフォルト） |
| `Euclidean` | `<->` | L2距離 |
| `InnerProduct` | `<#>` | 負の内積 — ユニット正規化ベクターで使用 |

### ランタイム検索プロファイル

クエリ時にリコール対レイテンシのバランスを調整します:

```csharp
var opts = new HnswSearchRuntimeOptions
{
    Profile = SearchProfile.HighRecall,  // Fast | Balanced | HighRecall
    EfSearch = 80                        // HNSWのef_searchを直接上書き
};

var results = await store.SearchAsync(queryVector, topK: 5, filter: null, runtimeOptions: opts);
```
