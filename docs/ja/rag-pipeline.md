# RAGパイプラインのカスタマイズ

## なぜパイプラインをカスタマイズするのか？

デフォルトのRAGパイプラインはそのままでもうまく動作しますが、実際のプロジェクトではより多くの制御が必要になることがよくあります:

- **デバッグ** — どのステージが遅いか？書き換え機がクエリを予期しない方法で変更していないか？
- **プロンプトエンジニアリング** — デフォルトのプロンプトテンプレートがドメインのトーンや制約に合わない場合がある
- **アーキテクチャ** — 複数のサービスが1つのインデックスを共有するとメモリを節約し、埋め込みの一貫性を維持
- **検査** — LLMに送る*前に*検索結果を確認する必要がある場合がある

この章ではそれらの制御を可能にするツールを説明します。

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

レイテンシーのプロファイリングに非常に有用です — ステージ間の時間を測定してボトルネックを特定できます。

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

適切に作成されたテンプレートは、モデルに提供されたコンテキスト内にとどまるよう指示することで、ハルシネーションを劇的に減らすことができます。

## RagStoreの共有

インデックスを一度構築して複数のサービスインスタンスで再利用します — プロバイダーの比較やA/Bテストに便利です:

```csharp
// 一度だけ構築
RagStore store = await RagBuilder.Create()
    .UseOpenAIEmbedding(apiKey, http)
    .UseQdrantStore(qdrantUrl, qdrantKey)
    .AddDirectory("docs/", ".txt", ".md", ".pdf")
    .BuildAsync();

// 複数のサービスで再利用
var claudeRag = new AnthropicService(apiKey, http).WithRag(store);
var gptRag    = new OpenAIService(apiKey, http).WithRag(store);
```

両サービスが同じ埋め込みとベクターインデックスを共有します — ストレージや計算の重複がありません。

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

`result.RequestMessageContent`にはLLMに送信される完全に組み立てられたプロンプトが含まれます。LLMトークンを使わずに検索品質をデバッグするのに非常に有用です。
