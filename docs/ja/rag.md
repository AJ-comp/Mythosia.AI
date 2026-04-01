# RAG（検索拡張生成）

RAGは、クエリ時に関連チャンクを検索することで、モデルが自分のドキュメントに基づいて質問に答えられるようにします。

## インストール

```bash
dotnet add package Mythosia.AI.Rag
```

## クイックスタート

任意の`IAIService`で`.WithRag()`を使用してフルーエントAPIでRAGを有効にします:

```csharp
using Mythosia.AI.Rag;

var service = new ClaudeService(apiKey, http)
    .WithRag(rag => rag
        .AddDocument("manual.txt")
        .AddDocument("policy.txt")
    );

var response = await service.GetCompletionAsync("返金ポリシーは何ですか？");
```

ドキュメントは自動的に分割、埋め込み、保存されます。クエリ時に最も関連性の高いチャンクが検索されてプロンプトに注入されます。

## ドキュメントの追加

複数のソースタイプをサポートします:

```csharp
.WithRag(rag => rag
    .AddDocument("readme.txt")                    // ローカルファイル
    .AddDocument("https://example.com/doc.txt")   // URL
    .AddText("インラインコンテンツもここに追加できます。")   // 生の文字列
)
```

## カスタム埋め込みプロバイダー

デフォルトではRAGはサービス自身のプロバイダーを埋め込みに使用します。専用の埋め込みモデルを使用するには:

```csharp
using Mythosia.AI.Rag.Embeddings;

var embedder = new OpenAIEmbeddingProvider(apiKey, http, "text-embedding-3-small");

var service = new ClaudeService(apiKey, http)
    .WithRag(rag => rag
        .UseEmbeddingProvider(embedder)
        .AddDocument("knowledge-base.txt")
    );
```

## カスタムベクターストア

デフォルトではインメモリストアを使用します。本番環境では永続的なベクターストアを接続します:

```bash
dotnet add package Mythosia.VectorDb.Postgres
```

```csharp
using Mythosia.VectorDb.Postgres;

var store = new PostgresStore(connectionString, embedDimension: 1536);

var service = new ChatGptService(apiKey, http)
    .WithRag(rag => rag
        .UseVectorStore(store)
        .AddDocument("large-corpus.txt")
    );
```

## クエリオプション

クエリごとの検索動作を細かく調整します:

```csharp
var options = new RagQueryOptions
{
    TopK = 5,               // 検索するチャンク数
    ScoreThreshold = 0.7f   // 最小類似度スコア
};

var response = await service.GetCompletionAsync("質問", ragOptions: options);
```

## 次のステップ

- [ベクターストア](../vectordb-overview.md) — 概要とバックエンド設定
- [テキストスプリッター](text-splitters.md) — ドキュメントのチャンク方法をカスタマイズ
- [高度なRAG](rag-advanced.md) — ハイブリッド検索、再ランキング、クエリ書き換え
