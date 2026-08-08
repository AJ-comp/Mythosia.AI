# 嵌入

> 📍 **問答檢索管線：** [查詢改寫](rag-query-rewriting.md) → **`嵌入`** → [過濾](rag-filtering.md) → [檢索](rag-hybrid-search.md) → [重排序](rag-reranking.md) → [上下文構建](rag-context-build.md)

## 什麼是嵌入？

嵌入是將文字轉換為**數值向量**（數字陣列）的過程，向量能夠捕捉文字的語意。在這個向量空間中，**語意相似的文字會彼此靠近**。

想像在地圖上標註城市：地理位置相近的城市在地圖上也會靠在一起。同樣地，「怎麼取消訂閱？」和「我想結束會員資格」雖然用詞完全不同，但因為語意相近，會生成相似的向量。

在 RAG 管線中，嵌入在兩個環節使用：

1. **文件索引時** — 每個文字區塊被向量化並存入向量儲存
2. **查詢時** — 使用者的問題被向量化，用於相似度搜尋

本頁重點介紹查詢時的嵌入（步驟 2）。

## 內建嵌入提供者

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

## 向量維度

| 提供者 | 模型 | 預設維度 |
| --- | --- | --- |
| OpenAI | text-embedding-3-small | 1536 |
| OpenAI | text-embedding-3-large | 3072 |
| Ollama | qwen3-embedding:4b | 1024 (32–2560) |
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
