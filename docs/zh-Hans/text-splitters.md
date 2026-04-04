# 文本分割器

文本分割器在嵌入前将文档切分为多个文本片段。片段大小和重叠量对检索质量有显著影响。

## 可用分割器

### CharacterTextSplitter

按字符数分割。简单快速，但可能在句子中间截断：

```csharp
.WithTextSplitter(new CharacterTextSplitter(500, 50))
```

### RecursiveTextSplitter（推荐默认选项）

按语义边界优先级依次尝试分割：段落 → 句子 → 单词 → 字符。生成的片段更连贯：

```csharp
.WithTextSplitter(new RecursiveTextSplitter(500, 50))
```

### TokenTextSplitter

按 Token 数而非字符数分割。对 LLM 上下文窗口的预算控制更精确：

```csharp
.WithTextSplitter(new TokenTextSplitter(256, 32))
```

当嵌入模型有严格 Token 限制时使用此分割器。

### MarkdownTextSplitter

保留 Markdown 结构 — 优先按标题、列表和代码块分割，再回退到字符分割：

```csharp
.WithTextSplitter(new MarkdownTextSplitter(500, 50))
```

最适合文档文件、README 和其他结构化 Markdown 内容。

## 参数选择

| 参数 | 效果 |
|------|------|
| `chunkSize`（较大） | 每个片段包含更多上下文，片段更少，嵌入成本更低 |
| `chunkSize`（较小） | 检索精度更高，片段更多，嵌入次数更多 |
| `chunkOverlap` | 防止片段边界处的信息丢失 |

常见起点：`chunkSize: 500, chunkOverlap: 50`。

## 按文档指定分割器

在 `RagBuilder` 中可以为不同文档应用不同的分割器：

```csharp
.WithRag(rag => rag
    .AddDocuments(new PlainTextDocumentLoader(), "readme.md", new MarkdownTextSplitter(600, 60))
    .AddDocuments(new PlainTextDocumentLoader(), "data.txt",  new RecursiveTextSplitter(300, 30))
    .WithTextSplitter(new RecursiveTextSplitter(500, 50))  // 其余文档的默认分割器
)
```

## 自定义分割器

实现 `ITextSplitter` 接口以使用完全自定义的分割逻辑：

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

// 注册：
.WithTextSplitter(new SentenceSplitter())
```
