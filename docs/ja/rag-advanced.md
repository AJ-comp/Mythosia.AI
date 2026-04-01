# 高度なRAG

## ハイブリッド検索

密なベクター検索とBM25キーワード検索を組み合わせます。特定の用語や名前を含むクエリでより高いリコールを提供します:

```csharp
.WithRag(rag => rag
    .UseHybridRetrieval(vectorWeight: 0.6f)  // 60%ベクター、40% BM25
    .AddDocument("knowledge-base.txt")
)
```

`vectorWeight`の範囲は0.0（純粋なBM25）から1.0（純粋なベクター）です。ほとんどの場合0.5〜0.7程度が適切です。

## クエリ書き換え

マルチターンの代名詞参照を解決し、より良い検索のためにクエリを拡張します。`LlmQueryRewriter`は埋め込み前にAIサービス自体を使ってクエリを書き換えます:

```csharp
.WithRag(rag => rag
    .WithQueryRewriter()             // 同じAIサービスを使用
    .WithQueryRewriteMaxTokens(250)  // 書き換えのトークン予算
    .AddDocument("docs.txt")
)
```

次のような会話が与えられた場合:
> ユーザー: "返金ポリシーについて教えてください。"
> ユーザー: "**それ**の例外は何ですか？"

書き換え機は検索前に「それ」→「返金ポリシーの例外」に展開します。

また**検索ゲート**も実装します: "ありがとう！"のように検索が不要なクエリの場合、ベクター検索をスキップします。

## 再ランキング

再ランカーは初期検索候補にスコアを付け、コンテキスト構築前に関連性で並び替えます。

### LLM再ランカー

AIサービスを使って結果にスコアを付けます。効果的ですが遅延が増加します:

```csharp
.WithRag(rag => rag
    .UseLlmReranker(aiService)
    .AddDocument("corpus.txt")
)
```

### Cohere再ランカー

Cohere Rerank APIを呼び出します — 高速で正確です:

```csharp
.WithRag(rag => rag
    .UseCohereReranker(cohereApiKey)
    .AddDocument("corpus.txt")
)
```

### vLLM再ランカー

ローカルでホストされたvLLM再ランキングエンドポイントを使用します:

```csharp
.WithRag(rag => rag
    .UseVllmReranker("http://localhost:8000")
    .AddDocument("corpus.txt")
)
```

## 検索パラメーター

最終選択前に検索される候補数とフィルタリング方法を制御します:

```csharp
.WithRag(rag => rag
    .WithTopK(5)                   // 返される最終チャンク数
    .WithRetrievalMultiplier(3)    // topK × 3の候補を検索（再ランキング用）
    .WithMinScore(0.6)             // 最小類似度スコア
    .AddDocument("corpus.txt")
)
```

`WithRetrievalMultiplier`は再ランカーを使用する際に便利です — より多くの候補を検索することで再ランカーがより多くを活用できます。

## 最終選択モード

再ランカーを使用する際の最終ランキングスコアの計算方法を選択します:

```csharp
using Mythosia.AI.Rag;

// デフォルト: 再ランカースコアのみを信頼
.WithFinalSelectionMode(RagFinalSelectionMode.RerankerOnly)

// 検索スコアと再ランカースコアをブレンド
.WithFinalSelectionMode(RagFinalSelectionMode.WeightedBlend)
.WithRetrievalWeightBlend(0.65)  // 65%検索、35%再ランカー
```

`WeightedBlend`は再ランカーの判断を組み込みながら元の検索シグナルを保持します。

## 進捗追跡

クエリごとの非同期コールバックで実行中のRAGステージを追跡します:

```csharp
var options = new RagQueryOptions
{
    ProgressAsync = async stage =>
    {
        Console.WriteLine($"[RAG] {stage}");
        // ステージ: QueryRewrite, Embedding, Filtering, Retrieval, Reranking, ContextBuild
    }
};

var response = await ragService.GetCompletionAsync("質問", options);
```

## カスタムプロンプトテンプレート

`{context}`と`{question}`プレースホルダーを使って検索されたコンテキストがプロンプトに注入される方法を制御します:

```csharp
.WithRag(rag => rag
    .WithPromptTemplate("""
        以下の情報のみを使用して質問に答えてください。
        コンテキストに答えがない場合は「わかりません」と言ってください。

        コンテキスト:
        {context}

        質問: {question}
        """)
    .AddDocument("faq.txt")
)
```

## RagStoreの共有

インデックスを一度構築して複数のサービスインスタンスで再利用します:

```csharp
// 一度だけ構築
RagStore store = await RagBuilder.Create()
    .UseOpenAIEmbedding(apiKey, http)
    .UseQdrantStore(qdrantUrl, qdrantKey)
    .AddDirectory("docs/", ".txt", ".md", ".pdf")
    .BuildAsync();

// 複数のサービスで再利用
var claudeRag = new ClaudeService(apiKey, http).WithRag(store);
var gptRag    = new ChatGptService(apiKey, http).WithRag(store);
```

## RagStoreへの直接クエリ

AIサービスと独立してストアをクエリして検索結果を確認します:

```csharp
RagProcessedQuery result = await store.QueryAsync("返品ポリシーは何ですか？");

Console.WriteLine($"書き換えられたクエリ: {result.RewrittenQuery}");

foreach (var ref_ in result.References)
{
    Console.WriteLine($"[{ref_.Score:F2}] {ref_.Record.Content[..100]}");
}
```

`result.RequestMessageContent`にはLLMに送信される完全に組み立てられたプロンプトが含まれます。

## マルチターンRAG

書き換え機が参照を解決できるように会話履歴をストアクエリに渡します:

```csharp
var history = new List<ConversationTurn>
{
    new ConversationTurn("返金ポリシーは何ですか？", "30日以内に返品できます。"),
    new ConversationTurn("デジタル製品は？", "デジタル製品は返金不可です。")
};

var result = await store.QueryAsync(
    query: "それに例外はありますか？",
    conversationHistory: history
);
```

## エージェンティックRAG

標準RAGではユーザーメッセージごとに1回検索します。エージェンティックRAGではエージェントが**いつ**検索するか、**何を**検索するか、最初の結果が不十分な場合に**再検索**するかをReActループ内で自律的に決定します。

`WithAgenticRag`で`RagStore`をツールとして登録し、`RunAgentAsync`に委譲します:

```csharp
// インデックスを一度だけビルド
var ragStore = await RagStore.BuildAsync(cfg => cfg
    .AddDocument("manual.pdf")
    .AddDocument("policy.docx")
    .UseOpenAIEmbedding(apiKey));

// RAGをToolとして登録してエージェントを実行
var service = new ClaudeService(apiKey, http);
service.WithAgenticRag(ragStore);

var answer = await service.RunAgentAsync("返金ポリシーを要約してください。");
```

エージェントはドキュメントのコンテキストが必要なときに自動的に`search_documents`を呼び出し、取得した内容をもとに最終的な回答を生成します。

### 他のToolとの組み合わせ

エージェンティックRAGは追加のToolと組み合わせると真価を発揮します。エージェントが各サブタスクに適したToolを自ら選択します:

```csharp
var service = new ClaudeService(apiKey, http);

service.WithAgenticRag(ragStore)
       .WithFunctionAsync("get_order_status", "注文IDで注文ステータスを照会します。",
           ("order_id", "照会する注文ID。", required: true),
           async id => await orderApi.GetStatusAsync(id));

// エージェントがポリシーはドキュメントから検索し、注文状況はAPIから取得
var answer = await service.RunAgentAsync(
    "注文 #12345 — 現在のポリシーで返金対象ですか？");
```

### Toolの説明をカスタマイズ

Toolの説明はエージェントがRAGを呼び出す基準になります。ドメインに合わせて記述するとTool選択の精度が上がります:

```csharp
service.WithAgenticRag(ragStore,
    toolDescription:
        "社内HRポリシー、製品マニュアル、コンプライアンス文書を検索します。" +
        "会社のポリシーや製品に関する情報が必要なときに呼び出してください。");
```

### 標準RAGとの違い

| | 標準RAG | エージェンティックRAG |
|---|---|---|
| 検索タイミング | メッセージごと | エージェントが決定 |
| クエリ生成 | QueryRewriter | エージェント自体 |
| 検索回数 | ターンごとに1回 | 必要に応じて1回以上 |
| Toolの組み合わせ | 非対応 | 登録済みの全Tool |
| 設定方法 | `.WithRag()` | `.WithAgenticRag()` + `RunAgentAsync` |

> **注意:** エージェンティックRAGでは`QueryRewriter`が意図的にバイパスされます。エージェントが自ら独立した検索クエリを生成するため、別途の書き換えステップは不要であり、エージェントの意図を歪める可能性があります。
