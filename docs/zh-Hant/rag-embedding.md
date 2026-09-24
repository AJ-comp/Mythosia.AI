# 嵌入

> 📍 **問答檢索管線：** [查詢改寫](rag-query-rewriting.md) → [過濾](rag-filtering.md) → **`嵌入（按需）`** → [檢索](rag-hybrid-search.md) → [重排序](rag-reranking.md) → [上下文構建](rag-context-build.md)

問題的 `Embedding` 階段依檢索器需要執行，關鍵字搜尋不會回報此階段。自訂檢索器可透過 `request.ProgressAsync` 回報實際階段。文件嵌入不變。

## 什麼是嵌入？

嵌入是將文字轉換為**數值向量**（數字陣列）的過程，向量能夠捕捉文字的語意。在這個向量空間中，**語意相似的文字會彼此靠近**。

想像在地圖上標註城市：地理位置相近的城市在地圖上也會靠在一起。同樣地，「怎麼取消訂閱？」和「我想結束會員資格」雖然用詞完全不同，但因為語意相近，會生成相似的向量。

在 RAG 管線中，嵌入在兩個環節使用：

1. **文件索引時** — 每個文字區塊被向量化並存入向量儲存
2. **查詢時** — 使用者的問題被向量化，用於相似度搜尋

本頁重點介紹查詢時的嵌入（步驟 2）。

## 內建嵌入提供者

依文件語言、部署環境及檢索需求選擇嵌入提供者。

### Perplexity

標準嵌入獨立處理各段落並實作 `IEmbeddingProvider`，因此可接入既有建構器。情境嵌入保留相鄰區塊順序與文件分組，並使用獨立 API，避免將無關文件攤平成單一輸入。

[Perplexity Agent API、搜尋與嵌入](perplexity.md).

### OpenAI

```csharp
var embedder = new OpenAIEmbeddingProvider(
    apiKey: "sk-...",
    httpClient: new HttpClient(),
    model: "text-embedding-3-small",
    dimensions: 1536
);
```

Builder 簡寫：

```csharp
.WithRag(rag => rag
    .UseOpenAIEmbedding(apiKey, model: "text-embedding-3-small", dimensions: 1536)
    .AddDocument("docs.txt")
)
```

<a id="openai-dimensions"></a>

`text-embedding-ada-002` 固定使用 **1536 維**。提供者在單筆與批次請求中省略該模型不支援的 `dimensions` 欄位；若設定其他維度，會在呼叫 API 前擲回 `ArgumentOutOfRangeException`。`text-embedding-3-small` 和 `text-embedding-3-large` 仍會在請求中傳送設定的 `dimensions`。

### Ollama（本機）

透過 [Ollama](https://ollama.com/) 在本機執行嵌入：

```csharp
var embedder = new OllamaEmbeddingProvider(
    httpClient: new HttpClient(),
    model: "qwen3-embedding:4b",
    dimensions: 1024,
    baseUrl: "http://localhost:11434"
);
```

<a id="ollama-dimensions"></a>

文件和查詢向量必須使用相同的模型與維度。`OllamaEmbeddingProvider` 將設定的 `dimensions` 傳送至 `/api/embed`，並驗證回傳的每個向量是否具有該長度。提供者仍預設使用 `qwen3-embedding:4b` 並**要求 1024 維**；模型的原生輸出為 2560 維。Ollama 伺服器和所選模型必須支援要求的維度。不支援的要求或忽略設定的回應會失敗，不會默默更改 `Dimensions` 或在本機調整向量長度。

更改模型或維度後，請使用與查詢相同的設定重新產生文件嵌入，並相應設定向量儲存區。既有向量不會自動轉換。

### vLLM（自託管）

適合運行自有 [vLLM](https://docs.vllm.ai/) 伺服器的團隊：

```csharp
var embedder = new VllmEmbeddingProvider(
    httpClient: new HttpClient(),
    model: "Qwen/Qwen3-Embedding-0.6B",
    dimensions: 1024,
    baseUrl: "http://localhost:8002"
);
```

### Local（無需 API）

基於特徵雜湊的輕量提供者，無需 API 金鑰或外部服務。但嵌入品質遠低於神經網路模型，**不建議用於正式環境**。

```csharp
.WithRag(rag => rag
    .UseLocalEmbedding(dimensions: 1024)
    .AddDocument("docs.txt")
)
```

> **提示：** 建議改用 `OpenAIEmbeddingProvider` 的 `text-embedding-3-small` 模型。費用極低，幾乎免費，效果遠優於本機方案。

## 批次處理

索引時按批次處理文字區塊：

```csharp
var options = pipeline.Options.Clone();
options.EmbeddingBatchSize = 100; // 預設：每次 API 呼叫 100 個區塊
pipeline.Options = options;
```

`EmbeddingBatchSize` 必須為正數。管線在每次文件索引呼叫開始時，在嵌入或取代儲存紀錄之前驗證並固定本次呼叫使用的值，以防止空批次迴圈，以及等待回應期間修改設定導致跳過區塊。後續索引呼叫可以使用新設定。

<a id="embedding-validation"></a>

## 讓每個向量始終對應正確的區塊

HTTP 回應成功仍可能缺少向量或順序錯誤，導致文字與另一個區塊的含義配對。自訂 `IEmbeddingProvider` 必須按輸入順序，為每個輸入傳回一個非 null 的 `float[]`，並提供正數 `Dimensions`。每個向量的長度必須等於該維度，所有元素必須是有限值（不能有 `NaN` 或無限大）。

文件索引期間，管線會在儲存或呼叫 `onDocumentEmbedded` 之前，以 `InvalidOperationException` 拒絕無效維度、回應數量或向量。每個向量都會在要求下一批次前複製，因此提供者在後續批次重複使用緩衝區不會改變先前的區塊。呼叫端讀取期間應保持傳回資料穩定；不支援在驗證或複製期間同時修改。驗證失敗時，該文件的現有記錄保持不變。

`OpenAIEmbeddingProvider` 要求每個回應項目都有有效且唯一的 `index`，並恢復輸入順序。`VllmEmbeddingProvider` 在包含索引時採用相同規則；為維持相容性，也接受所有項目均省略 `index` 的回應，並使用回應順序。部分缺漏、重複或超出範圍的索引均被拒絕。自訂提供者或無索引回應的順序仍由提供者負責；結構檢查無法驗證向量的實際含義。

<a id="query-embedding-validation"></a>

## 搜尋前保護問題向量

等待進度通知或搜尋時，提供者重複使用緩衝區不應讓問題向量變成另一個問題。內建密集向量擷取（包括 `IRetrievalStrategy` 配接器）要求正數 `Dimensions`、長度完全相符的非 null 向量及有限值。無效結果會在搜尋前擲出 `InvalidOperationException`。有效向量會在傳回後立即複製，早於後續進度通知和搜尋。提供者應在讀取期間保持資料穩定；自訂 `IRagRetriever` 負責自己的問題準備與驗證。

直接呼叫 `OllamaEmbeddingProvider` 的單筆或批次方法時，也會驗證回應結構、精確的向量數量、維度及有限值。錯誤的 JSON 或向量會擲出 `InvalidOperationException`，而非默默傳回不完整結果。傳入的 `HttpClient` 仍由呼叫端擁有；釋放個別 HTTP 要求和回應不會釋放該用戶端。

## 向量維度

| 提供者 | 模型 | 預設維度 |
| --- | --- | --- |
| OpenAI | text-embedding-3-small | 1536 |
| OpenAI | text-embedding-ada-002 | 1536 |
| Perplexity | pplx-embed-v1-0.6b | 1024 |
| Perplexity | pplx-embed-v1-4b | 2560 |
| OpenAI | text-embedding-3-large | 3072 |
| Ollama | qwen3-embedding:4b | 要求 1024（原生：2560） |
| vLLM | Qwen/Qwen3-Embedding-0.6B | 1024 (32–1024) |
| vLLM | Qwen/Qwen3-Embedding-4B | 2560 (32–2560) |
| Local | （特徵雜湊） | 1024 |

## 自訂提供者

實作 `IEmbeddingProvider` 介面：

```csharp
public class MyEmbeddingProvider : IEmbeddingProvider
{
    public int Dimensions => 768;

    public async Task<float[]> GetEmbeddingAsync(
        string text, CancellationToken cancellationToken = default)
    {
        // 呼叫您的 API
    }

    public async Task<IReadOnlyList<float[]>> GetEmbeddingsAsync(
        IEnumerable<string> texts, CancellationToken cancellationToken = default)
    {
        // 批次呼叫
    }
}
```

## 內部機制

```
使用者問題 (string) → EmbeddingProvider.GetEmbeddingAsync() → 查詢向量 (float[])
```

該向量傳遞到下一步（[過濾](rag-filtering.md)），然後進入[檢索](rag-hybrid-search.md)。

## 後續步驟

- [過濾](rag-filtering.md) — 縮小搜尋範圍
- [混合檢索](rag-hybrid-search.md) — 結合向量搜尋與關鍵字搜尋
- [管線自訂](rag-pipeline.md) — 跨服務共享嵌入提供者
