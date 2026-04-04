# RAG（検索拡張生成）

## RAGとは？

RAG（Retrieval-Augmented Generation）は、AIモデルが回答を生成する際に、**自分が持っているドキュメントから関連情報を先に探し出し**、その情報をもとに回答させる技術です。

図書館でレポートを書く場面を想像してみてください。すべてを記憶だけで書くより、関連する本を先に探して読み、その内容を参考にして書く方がずっと正確ですよね？ RAGはまさにこの方法です。

## RAGが必要な理由

LLM（大規模言語モデル）は学習データをもとに回答するため、以下のような限界があります：

- **最新情報を知りません** — 学習時点以降の情報にはアクセスできません
- **社内ドキュメントを知りません** — 会社のポリシーや製品マニュアルなどの非公開データには触れられません
- **ハルシネーション** — 知らない内容でもそれらしく作り上げてしまうことがあります

RAGはこれらの限界を解決します。質問が来たらまず自分のドキュメントから関連情報を検索し、その結果をプロンプトに含めることで、AIが**根拠のある回答**を生成できるようにします。

## RAGの動作フロー

RAGは大きく2つのステージに分かれます。

### ステージ1：ドキュメント準備（初回のみ実行）

```
ドキュメント → テキスト分割（チャンキング） → 埋め込み（ベクター変換） → ベクターストアに保存
```

1. **テキスト分割** — 長いドキュメントを検索に適した小さな断片（チャンク）に分けます
2. **埋め込み** — 各チャンクを数値ベクターに変換します。意味が似たテキストは似たベクターになります
3. **保存** — 変換されたベクターをベクターストアに保存します

### ステージ2：質問応答（質問のたびに実行）

```
ユーザーの質問 → 質問を埋め込み → ベクターストアで類似チャンクを検索 → プロンプトに注入 → AI回答生成
```

1. **質問の埋め込み** — ユーザーの質問も同じ方法でベクターに変換します
2. **類似度検索** — ベクターストアから質問に最も似たチャンクを見つけます
3. **プロンプト構築** — 見つかったチャンクをプロンプトに入れてAIに渡します
4. **回答生成** — AIが受け取ったドキュメント内容を参考にして回答を生成します

## インストール

```bash
dotnet add package Mythosia.AI.Rag
```

## クイックスタート

Mythosia.AIでは、この一連のプロセスを`.WithRag()`の一行で設定できます：

```csharp
using Mythosia.AI.Rag;

var service = new AnthropicService(apiKey, http)
    .WithRag(rag => rag
        .AddDocument("manual.txt")
        .AddDocument("policy.txt")
    );

var response = await service.GetCompletionAsync("返金ポリシーは何ですか？");
```

上記のコードだけで、ドキュメント分割 → 埋め込み → 保存 → 検索 → プロンプト注入が自動的に処理されます。

## ドキュメントの追加

ローカルファイル、URL、直接入力したテキストなど、さまざまな方法でドキュメントを追加できます：

```csharp
.WithRag(rag => rag
    .AddDocument("readme.txt")                    // ローカルファイル
    .AddDocument("https://example.com/doc.txt")   // URL
    .AddText("インラインコンテンツもここに追加できます。")   // 生の文字列
)
```

## カスタム埋め込みプロバイダー

デフォルトではAIサービスのプロバイダーを埋め込みにも使用します。埋め込み専用のモデルを別途指定したい場合は次のように設定します：

```csharp
using Mythosia.AI.Rag.Embeddings;

var embedder = new OpenAIEmbeddingProvider(apiKey, http, "text-embedding-3-small");

var service = new AnthropicService(apiKey, http)
    .WithRag(rag => rag
        .UseEmbeddingProvider(embedder)
        .AddDocument("knowledge-base.txt")
    );
```

## カスタムベクターストア

デフォルトではインメモリストアを使用するため、アプリを再起動するとデータが消えます。本番環境ではデータを永続的に保管できるベクターストアを接続しましょう：

```bash
dotnet add package Mythosia.VectorDb.Postgres
```

```csharp
using Mythosia.VectorDb.Postgres;

var store = new PostgresStore(connectionString, embedDimension: 1536);

var service = new OpenAIService(apiKey, http)
    .WithRag(rag => rag
        .UseVectorStore(store)
        .AddDocument("large-corpus.txt")
    );
```

## クエリオプション

検索時に何個のチャンクを取得するか、最低類似度をどの程度にするかなどを調整できます：

```csharp
var options = new RagQueryOptions
{
    TopK = 5,               // 取得するチャンク数（デフォルト5個）
    ScoreThreshold = 0.7f   // このスコア以上のチャンクのみ取得
};

var response = await service.GetCompletionAsync("質問", ragOptions: options);
```

## 次のステップ

基本のRAGを理解したら、次の機能で検索品質をさらに高めましょう：

- [ハイブリッド検索](rag-hybrid-search.md) — 意味検索とキーワード検索を同時に
- [クエリ書き換え](rag-query-rewriting.md) — 会話の文脈を反映した検索クエリの最適化
- [再ランキング](rag-reranking.md) — 検索結果の精度をもう一段高める
- [パイプラインのカスタマイズ](rag-pipeline.md) — RAG動作プロセスをきめ細かく制御
- [エージェンティックRAG](rag-agentic.md) — AIが自ら判断して検索するインテリジェントRAG
- [ベクターストア](../vectordb-overview.md) — 永続ストアの設定
- [テキストスプリッター](text-splitters.md) — ドキュメントの分割方法を変更
