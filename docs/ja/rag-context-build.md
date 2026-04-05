# コンテキスト構築

> 📍 **質問応答パイプライン:** [クエリ書き換え](rag-query-rewriting.md) → [埋め込み](rag-embedding.md) → [フィルタリング](rag-filtering.md) → [検索](rag-hybrid-search.md) → [再ランキング](rag-reranking.md) → **`コンテキスト構築`**

## コンテキスト構築とは？

コンテキスト構築はRAGパイプラインの**最終ステージ**です。最も関連性の高いチャンクを取得し順位付けした後、このステージではそれらを**LLMが理解して使えるプロンプト**に組み立てます。

会議の前に上司へブリーフィング資料を用意する場面を想像してください。関連情報をすべて集め（検索）、重要度順に並べ（再ランキング）ました。最後に、読み手が何をすべきかわかるように**明確に整理する**必要があります。

このステージの品質は、LLMの回答品質に直結します。適切に構造化されたプロンプトはハルシネーションを抑え、モデルが提供されたコンテキストに基づいて回答するよう導きます。

## デフォルトのコンテキストビルダー

特別な設定がない場合、パイプラインは`DefaultContextBuilder`を使用し、以下のフォーマットを生成します：

```
Answer the question based on the following context:

[1] (Source: manual.txt)
返品は購入から30日以内に可能です...

[2] (Source: policy.txt)
デジタル製品は返金不可です...

Question: 返金ポリシーは何ですか？
```

デフォルトビルダーにはカスタマイズ可能なプロパティがあります：

```csharp
var contextBuilder = new DefaultContextBuilder
{
    Header = "以下のコンテキストに基づいて質問に答えてください：",
    QueryPrefix = "質問：",
    IncludeScores = false,    // 類似度スコアを表示するか？
    IncludeSource = true      // ソースメタデータを表示するか？
};

.WithRag(rag => rag
    .WithContextBuilder(contextBuilder)
    .AddDocument("docs.txt")
)
```

### スコアの表示

`IncludeScores = true`を設定すると、各チャンクに類似度スコアが表示されます：

```
[1] (Source: manual.txt) [Score: 0.892]
返品は購入から30日以内に可能です...
```

デバッグや、特定のチャンクが選ばれた理由の理解に役立ちます。

## プロンプトテンプレート

最終プロンプトをより細かく制御するには、`{context}`と`{question}`プレースホルダーを使った**プロンプトテンプレート**を設定します：

```csharp
.WithRag(rag => rag
    .WithPromptTemplate("""
        あなたはカスタマーサポートのアシスタントです。
        以下のドキュメントの内容のみを使って質問に回答してください。
        ドキュメントに答えがない場合は「その情報はありません」と回答してください。

        ドキュメント：
        {context}

        お客様のご質問：{question}
        """)
    .AddDocument("support-kb.txt")
)
```

パイプラインが`{context}`を番号付きチャンクリストに、`{question}`をユーザーの質問に置き換えます。内部的には`TemplateContextBuilder`が作成され、チャンクは次のようにフォーマットされます：

```
[1] 最初のチャンクの内容...

[2] 2番目のチャンクの内容...
```

### テンプレートを使うべき場面

テンプレートは以下のような場面で特に効果的です：

- **動作を制限する** — 「コンテキストにない情報は『わかりません』と答えてください」
- **トーンを設定する** — 「丁寧で簡潔な口調で回答してください」
- **役割を指定する** — 「あなたは医療アシスタントです」「あなたは法務アドバイザーです」
- **言語を指定する** — 「常に日本語で回答してください」

### テンプレート設計のコツ

| コツ | 例 |
| --- | --- |
| モデルをコンテキスト内に留める | 「提供されたドキュメントのみを根拠に回答してください」 |
| 情報不足への対応 | 「答えが見つからない場合は『その情報はありません』と回答してください」 |
| 出力フォーマットの指定 | 「箇条書きで回答してください」 |
| 言語の制約 | 「質問と同じ言語で回答してください」 |

## カスタムコンテキストビルダー

完全な制御が必要な場合は、`IContextBuilder`を実装します：

```csharp
public class MyContextBuilder : IContextBuilder
{
    public string BuildContext(string query, IReadOnlyList<VectorSearchResult> searchResults)
    {
        var sb = new StringBuilder();

        sb.AppendLine("### 関連情報 ###");
        sb.AppendLine();

        foreach (var result in searchResults)
        {
            var source = result.Record.Metadata.TryGetValue("source", out var s) ? s : "不明";
            sb.AppendLine($"📄 出典: {source}（関連度: {result.Score:P0}）");
            sb.AppendLine(result.Record.Content);
            sb.AppendLine("---");
        }

        sb.AppendLine();
        sb.AppendLine($"上記の情報を踏まえて回答してください：{query}");

        return sb.ToString();
    }
}
```

ビルダーで登録します：

```csharp
.WithRag(rag => rag
    .WithContextBuilder(new MyContextBuilder())
    .AddDocument("docs.txt")
)
```

## 内部の動作

コンテキスト構築ステージは以下を受け取ります：

1. 元のクエリ文字列
2. 最終的な`VectorSearchResult`のリスト（フィルタリング、検索、オプションの再ランキング後）

これらから1つのプロンプト文字列を生成し、LLMに送信します：

```
検索結果 + クエリ → ContextBuilder.BuildContext() → プロンプト文字列 → LLM
```

どのコンテキストビルダーが使われるかの優先順位：

1. **カスタム`IContextBuilder`** — `.WithContextBuilder()`で設定された場合
2. **`TemplateContextBuilder`** — `.WithPromptTemplate()`でテンプレートが設定された場合
3. **`DefaultContextBuilder`** — デフォルトのフォールバック

## 次のステップ

- [パイプラインカスタマイズ](rag-pipeline.md) — RAG全体の動作を細かく調整する
- [再ランキング](rag-reranking.md) — コンテキスト構築前のチャンク品質を向上させる
- [RAG基礎](rag.md) — RAGの全体フローを復習する
