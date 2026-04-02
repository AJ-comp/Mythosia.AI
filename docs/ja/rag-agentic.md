# エージェンティックRAG

## なぜエージェンティックRAGが必要か？

標準RAGでは、すべてのユーザーメッセージに対して正確に**1回**の検索が実行されます。システムが検索し、コンテキストを構築し、応答を生成します — 無条件に。単純な質問にはうまく機能しますが、以下の場合には限界があります:

- 質問が異なるトピックにわたって**複数の検索**を必要とする場合（例：「ハードウェアとソフトウェア製品の返金ポリシーを比較してください」）
- 最初の検索結果が**不十分**で、補完検索が必要な場合
- **検索がまったく不要な**質問の場合（例：「今までの会話を要約してください」）
- 回答が**ドキュメント検索とリアルタイムデータ**の組み合わせに依存する場合

エージェンティックRAGはこれらすべてを解決します。固定された検索→回答パイプラインの代わりに、**エージェントが自律的に判断**します — いつ検索するか、何を検索するか、再検索が必要か、他のToolを呼ぶかをReActループ内で決定します。

## クイックスタート

`WithAgenticRag`で`RagStore`をツールとして登録し、`RunAgentAsync`に委譲します:

```csharp
// インデックスを一度だけビルド
var ragStore = await RagStore.BuildAsync(cfg => cfg
    .AddDocument("manual.pdf")
    .AddDocument("policy.docx")
    .UseOpenAIEmbedding(apiKey));

// RAGをToolとして登録してエージェントを実行
var service = new AnthropicService(apiKey, http);
service.WithAgenticRag(ragStore);

var answer = await service.RunAgentAsync("返金ポリシーを要約してください。");
```

エージェントはドキュメントのコンテキストが必要なときに自動的に`search_documents`を呼び出し、取得した内容をもとに最終的な回答を生成します。

## 他のToolとの組み合わせ

エージェンティックRAGは追加のToolと組み合わせると真価を発揮します。エージェントが各サブタスクに適したToolを自ら選択します:

```csharp
var service = new AnthropicService(apiKey, http);

service.WithAgenticRag(ragStore)
       .WithFunctionAsync("get_order_status", "注文IDで注文ステータスを照会します。",
           ("order_id", "照会する注文ID。", required: true),
           async id => await orderApi.GetStatusAsync(id));

// エージェントがポリシーはドキュメントから検索し、注文状況はAPIから取得
var answer = await service.RunAgentAsync(
    "注文 #12345 — 現在のポリシーで返金対象ですか？");
```

この例では、エージェントが自律的に:

1. ドキュメントから返金ポリシーを検索
2. 注文APIを呼び出して注文 #12345のステータスを取得
3. 両方の情報を組み合わせて最終回答を生成

## Toolの説明をカスタマイズ

Toolの説明はエージェントがRAGを呼び出す基準になります。ドメインに合わせて記述するとTool選択の精度が上がります:

```csharp
service.WithAgenticRag(ragStore,
    toolDescription:
        "社内HRポリシー、製品マニュアル、コンプライアンス文書を検索します。" +
        "会社のポリシーや製品に関する情報が必要なときに呼び出してください。");
```

「ドキュメントを検索」のような曖昧な説明は、エージェントがRAGを呼び出しすぎたり、十分に呼び出さなかったりする原因になります。ドキュメントに**どのような種類の情報**が含まれているかを具体的に記述してください。

## 標準RAGとの違い

| | 標準RAG | エージェンティックRAG |
| --- | --- | --- |
| 検索タイミング | メッセージごと | エージェントが決定 |
| クエリ生成 | QueryRewriter | エージェント自体 |
| 検索回数 | ターンごとに1回 | 必要に応じて1回以上 |
| Toolの組み合わせ | 非対応 | 登録済みの全Tool |
| 設定方法 | `.WithRag()` | `.WithAgenticRag()` + `RunAgentAsync` |

> **注意:** エージェンティックRAGでは`QueryRewriter`が意図的にバイパスされます。エージェントが自ら独立した検索クエリを生成するため、別途の書き換えステップは不要であり、エージェントの意図を歪める可能性があります。

## どちらを選ぶべきか

- **標準RAG** — すべての質問がドキュメントベースで、単一トピックで、最小レイテンシーを求める場合
- **エージェンティックRAG** — 質問が複数トピックにまたがる場合、ドキュメント＋リアルタイムデータの組み合わせが必要な場合、反復検索が必要な場合
