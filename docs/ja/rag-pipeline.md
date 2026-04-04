# RAGパイプラインのカスタマイズ

## RAGパイプラインとは？

RAGパイプラインは、ユーザーの質問が入ってからAIが回答を生成するまでに通る**一連の処理ステージ**のことです。工場の組み立てラインのように、各ステージが順番に実行され、質問をより正確な回答へと仕上げていきます。

## パイプラインの全体フロー

質問が入ると、以下のステージを順番に通ります：

```
ユーザーの質問
    ↓
① クエリ書き換え (QueryRewrite)   — 会話の文脈を反映して検索クエリを整えます
    ↓
② 埋め込み (Embedding)            — クエリを数値ベクターに変換します
    ↓
③ フィルタリング (Filtering)      — ネームスペースやメタデータで検索範囲を絞ります
    ↓
④ 検索 (Retrieval)               — ベクターストアから類似チャンクを取得します
    ↓
⑤ 再ランキング (Reranking)       — 検索結果の関連性をより精密に再評価します
    ↓
⑥ コンテキスト構築 (ContextBuild) — 最終チャンクをプロンプトに組み立てます
    ↓
AI回答生成
```

各ステージは独立して動作するため、必要に応じて特定のステージだけを差し替えたりスキップしたりできます。たとえば、クエリ書き換えはマルチターン会話でなければ省略され、再ランカーを設定していなければ再ランキングも自動的にスキップされます。

## パイプラインをカスタマイズする理由

デフォルトのRAGパイプラインは特別な設定なしでもうまく動作しますが、実際のプロジェクトでは以下のような理由でより細かい制御が必要になります：

- **デバッグ** — どのステージで時間がかかっているのか、クエリ書き換えが意図しない形で質問を変えていないか確認したいとき
- **プロンプトエンジニアリング** — デフォルトのプロンプトテンプレートが自社サービスのトーンや要件に合わないとき
- **アーキテクチャ** — 複数のAIサービスが1つのインデックスを共有してコストと一貫性を管理したいとき
- **検査** — AIに送る前に、実際にどのドキュメントが検索されたかを事前に確認したいとき

以下で、これらの制御を可能にするツールを一つずつ見ていきましょう。

## 進捗追跡

各ステージが実行されるたびにコールバックを受け取り、パイプラインがどのステージを通過しているかをリアルタイムで確認できます：

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

各ステージ間の所要時間を計測すれば、どこがボトルネックなのか簡単に把握できます。たとえば、Retrievalステージが特に遅ければ、ベクターストアのインデックス設定を見直すきっかけになりますね。

## カスタムプロンプトテンプレート

検索されたドキュメント内容がAIに渡される方法を直接制御できます。`{context}`には検索されたチャンクが、`{question}`にはユーザーの質問が入ります：

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

プロンプトテンプレートをうまく書けば、AIがドキュメント内容の外の話を作り上げてしまう現象（ハルシネーション）を大幅に減らすことができます。

## RagStoreの共有

ドキュメントインデックスを一度だけ作成し、複数のAIサービスで共有できます。同じドキュメントをもとに複数モデルの回答品質を比較したり、A/Bテストを行うときに便利です：

```csharp
// インデックスを一度だけビルド
RagStore store = await RagBuilder.Create()
    .UseOpenAIEmbedding(apiKey, http)
    .UseQdrantStore(qdrantUrl, qdrantKey)
    .AddDocuments("docs/")
    .BuildAsync();

// 異なるAIサービスで同じインデックスを再利用
var claudeRag = new AnthropicService(apiKey, http).WithRag(store);
var gptRag    = new OpenAIService(apiKey, http).WithRag(store);
```

両サービスが同じ埋め込みとベクターインデックスを共有するため、ストレージや埋め込み計算の重複がありません。

## RagStoreへの直接クエリ

AIサービスを介さずにベクターストアへ直接質問を投げることもできます。AIに送る前に「実際にどのドキュメントが検索されるか」を確認したいときに役立ちます：

```csharp
RagProcessedQuery result = await store.QueryAsync("返品ポリシーは何ですか？");

Console.WriteLine($"書き換えられたクエリ: {result.RewrittenQuery}");

foreach (var ref_ in result.References)
{
    Console.WriteLine($"[{ref_.Score:F2}] {ref_.Record.Content[..100]}");
}
```

`result.RequestMessageContent`にはAIに渡される完成済みプロンプトがそのまま入っています。LLMトークンを消費せずに検索品質をチェックできるので、開発中のデバッグに非常に便利です。

## 内部動作の仕組み

`.WithRag()`を呼び出すと、実際には`RagEnabledService`というラッパーが作成されます。このラッパーは元のAIServiceを包み、RAGパイプラインとLLM呼び出しを自動的に接続します。その核心には[AIRequestContext](request-contexts.md)があります。

### 全体フロー

```
ragService.GetCompletionAsync("返品ポリシーは何ですか？")
    ↓
① RagEnabledServiceがRAGパイプラインを実行
   クエリ書き換え → 埋め込み → 検索 → コンテキスト組み立て
    ↓
② TemplateContextBuilderが{context}と{question}を置換
   → "以下の情報で答えてください。\n[1] 返品は30日以内...\n質問: 返品ポリシーは何ですか？"
    ↓
③ RagEnabledServiceがAIRequestContextを生成
   RequestMessageOverride = 組み立てられたプロンプト
    ↓
④ _innerService.GetCompletionAsync(元のメッセージ, context)を呼び出し
   → AIServiceがAsyncLocalにcontextを保存
   → 元の質問を会話履歴に追加
    ↓
⑤ AIService.GetLatestMessages()が最後のメッセージを差し替え
   会話履歴: "返品ポリシーは何ですか？"（元のまま保持）
   モデルが見るもの: 組み立てられたプロンプト（RequestMessageOverride）
```

### なぜこの設計なのか？

この設計の核心は**会話履歴とモデル入力の分離**です：

- **会話履歴には元の質問が残ります** — 後続の会話で「それ」が何を指すか文脈を維持します
- **モデルには組み立てられたプロンプトが渡されます** — 検索されたドキュメント＋質問を含む完成したプロンプト
- **AIServiceの状態は変更されません** — `AsyncLocal<T>`によりリクエスト単位で隔離されます

これが`request-contexts.md`で説明している`RequestMessageOverride`の実際のユースケースです。RAGパイプラインがこのメカニズムを自動的に活用するため、ユーザーは`.WithRag()`を呼び出すだけで済みます。

### コードで見る

`RagEnabledService`内部でこの接続が行われる核心コードです：

```csharp
// RagEnabledService.GetCompletionAsync内部
var processed = await RewriteAndProcessAsync(query, options, cancellationToken);
return await _innerService.GetCompletionAsync(
    new Message(ActorRole.User, query),         // ← 元の質問（会話履歴に保存される）
    context: BuildRequestContext(processed));    // ← 組み立てられたプロンプト（モデルだけが見る）

// BuildRequestContext — AIRequestContextを生成する部分
private static AIRequestContext BuildRequestContext(RagProcessedQuery processed)
{
    return new AIRequestContext
    {
        RequestMessageOverride = new Message(
            ActorRole.User,
            processed.RequestMessageContent)  // ← TemplateContextBuilderの出力
    };
}
```

`AIService`はこのcontextを`AsyncLocal`に保存し、`GetLatestMessages()`で最後のメッセージを`RequestMessageOverride`に差し替えます。リクエストが完了すると自動的に復元されるため、後続のリクエストに影響を与えません。
