# 文字分割器

文字分割器在嵌入前將文件切分為多個文字片段。片段大小和重疊量對檢索品質有顯著影響。

## 可用分割器

### CharacterTextSplitter

按字元數分割。簡單快速，但可能在句子中間截斷：

```csharp
.WithTextSplitter(new CharacterTextSplitter(500, 50))
```

### RecursiveTextSplitter（建議預設選項）

按語義邊界優先順序依次嘗試分割：段落 → 句子 → 單詞 → 字元。生成的片段更連貫：

```csharp
.WithTextSplitter(new RecursiveTextSplitter(500, 50))
```

### TokenTextSplitter

按 Token 數而非字元數分割。對 LLM 上下文視窗的預算控制更精確：

```csharp
.WithTextSplitter(new TokenTextSplitter(256, 32))
```

### MarkdownTextSplitter

保留 Markdown 結構 — 優先按標題、列表和程式碼區塊分割：

```csharp
.WithTextSplitter(new MarkdownTextSplitter(500, 50))
```

## 參數選擇

| 參數 | 效果 |
|------|------|
| `chunkSize`（較大） | 每個片段包含更多上下文，片段更少，嵌入成本更低 |
| `chunkSize`（較小） | 檢索精度更高，片段更多，嵌入次數更多 |
| `chunkOverlap` | 防止片段邊界處的資訊遺失 |

常見起點：`chunkSize: 500, chunkOverlap: 50`。

## 按文件指定分割器

```csharp
.WithRag(rag => rag
    .AddDocuments(new PlainTextDocumentLoader(), "readme.md", new MarkdownTextSplitter(600, 60))
    .AddDocuments(new PlainTextDocumentLoader(), "data.txt",  new RecursiveTextSplitter(300, 30))
    .WithTextSplitter(new RecursiveTextSplitter(500, 50))
)
```

## 自訂分割器

實作 `ITextSplitter` 介面：

```csharp
public class SentenceSplitter : ITextSplitter
{
    public IReadOnlyList<RagChunk> Split(RagDocument document)
    {
        var sentences = document.Content.Split(". ");
        return sentences.Select((s, i) => new RagChunk
        {
            Content = s,
            Index = i,
            DocumentId = document.Id
        }).ToList();
    }
}

.WithTextSplitter(new SentenceSplitter())
```
