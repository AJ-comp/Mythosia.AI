# ベクターストアマイグレーション

Mythosia.AIはバージョン間でベクターストアスキーマをアップグレードするマイグレーションツールを含んでいます。主に古いコレクションスキーマ（密なベクターのみ）から現在のハイブリッドスキーマ（密 + 疎ベクター）にアップグレードする際に使用します。

## マイグレーションが必要な場合

ハイブリッド検索が導入される前のライブラリバージョンでQdrantコレクションを作成した場合、そのコレクションは**密のみ**スキーマ状態です。この状態でハイブリッド検索を実行すると失敗するか不正な結果になります。

マイグレーションはコレクションを現在の**ハイブリッドスキーマ**（スキーマバージョン2）にアップグレードします。このスキーマはレコードごとに密ベクターと疎ベクターの両方を保存します。

## CLIツール

マイグレーションCLIツールをインストールします:

```bash
dotnet tool install -g Mythosia.VectorDb.Tools
```

### コマンド

**`migrate`** — コレクションをその場でアップグレードします:

```bash
mythosia-vectordb migrate qdrant \
  --endpoint localhost:6334 \
  --source my-collection \
  [--api-key your-key] \
  [--replace]
```

- `--replace`なし: `my-collection_migrated`という新しいコレクションを作成
- `--replace`あり: 完了時にソースコレクションを上書き（破壊的）

**`copy`** — スキーマをアップグレードしながらコレクションをコピーします:

```bash
mythosia-vectordb copy qdrant \
  --endpoint localhost:6334 \
  --source my-collection \
  --target my-collection-v2 \
  [--api-key your-key]
```

## プログラム的マイグレーション

コードから直接`QdrantVectorStoreMigrator`を使用します:

```csharp
using Mythosia.VectorDb.Qdrant;

var migrator = new QdrantVectorStoreMigrator(new QdrantOptions
{
    Host           = "localhost",
    Port           = 6334,
    CollectionName = "my-collection",
    Dimension      = 1536
});
```

### マイグレーション前の計画確認

実行前にマイグレーションが何をするかを確認します:

```csharp
var plan = await migrator.PlanAsync(new VectorStoreMigrationRequest
{
    Source = "my-collection"
});

Console.WriteLine($"現在のスキーマ: {plan.SchemaKind} v{plan.SchemaVersion}");
Console.WriteLine($"対象スキーマ: {plan.TargetSchemaKind} v{plan.TargetSchemaVersion}");
Console.WriteLine($"マイグレーション必要: {plan.MigrationRequired}");
```

### 進捗付きマイグレーションの実行

```csharp
var progress = new Progress<VectorStoreMigrationProgress>(p =>
{
    Console.WriteLine($"[{p.Stage}] {p.ProcessedRecords}/{p.TotalRecords} — {p.Message}");
});

var result = await migrator.MigrateAsync(
    new VectorStoreMigrationRequest
    {
        Source           = "my-collection",
        ReplaceOnSuccess = false
    },
    progress: progress
);

Console.WriteLine($"マイグレーション: {result.MigratedRecords}件");
```

## スキーマバージョン管理

| スキーマバージョン | 種類 | 説明 |
|-----------------|------|------|
| 1 | `dense` | 密ベクターのみ（レガシー） |
| 2 | `hybrid` | 密 + 疎ベクター（現在） |

## 対応プロバイダー

| プロバイダー | Migrate | Copy |
|------------|---------|------|
| Qdrant | ✓ | ✓ |
| Pinecone | — | — |
| PostgreSQL | — | — |
| InMemory | — | — |
