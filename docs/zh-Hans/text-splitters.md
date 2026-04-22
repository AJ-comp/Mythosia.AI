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

能够理解并保留 Markdown 结构的分割器。它识别标题层级（H1–H6）、代码围栏和表格等结构，以语义有意义的单位进行分割：

```csharp
.WithTextSplitter(new MarkdownTextSplitter(500, 50))
```

最适合文档文件、README 以及 Office/HWP 等结构化文档加载器的输出。

> [!TIP]
> Word、Excel、PowerPoint 和 HWP 等文档加载器会在内部将文档转换为 Markdown。对这些文档使用 `MarkdownTextSplitter`，可以确保表格和代码块的结构在分块过程中得到完整保留。

#### 表格分割质量

`MarkdownTextSplitter` 按**行**分割 Markdown 表格。行中间绝不会被截断，每个分割后的片段都会自动包含**表头行和分隔线**：

```
原始表格：
| 姓名   | 部门   | 薪资     |
|--------|--------|----------|
| 张三   | 开发部 | 30,000   |
| 李四   | 产品部 | 28,000   |
| 王五   | 设计部 | 25,000   |

→ 片段 1：
| 姓名   | 部门   | 薪资     |
|--------|--------|----------|
| 张三   | 开发部 | 30,000   |
| 李四   | 产品部 | 28,000   |

→ 片段 2：
| 姓名   | 部门   | 薪资     |
|--------|--------|----------|
| 王五   | 设计部 | 25,000   |
```

每个片段都是一个独立有效的表格，保证了嵌入和检索质量。

#### 代码块保护

代码围栏（`` ``` ``）包裹的块被视为**原子单位**。代码块即使超出片段大小也绝不会被中间分割，确保代码语义完整。

#### 标题面包屑

每个片段会自动在前面加上所属的标题路径，丰富向量搜索时的上下文：

```
# 产品手册
## 安装指南
### Windows

（该部分的实际内容）
```

该功能由 `IncludeHeadingBreadcrumb` 属性控制（默认值：`true`）。

## 参数选择

| 参数 | 效果 |
|------|------|
| `chunkSize`（较大） | 每个片段包含更多上下文，片段更少，嵌入成本更低 |
| `chunkSize`（较小） | 检索精度更高，片段更多，嵌入次数更多 |
| `chunkOverlap` | 防止片段边界处的信息丢失 |

常见起点：`chunkSize: 500, chunkOverlap: 50`。

## 片段大小与令牌数（多语言参考）

`chunkSize` 以**字符**为单位，但嵌入模型的限制以**令牌（token）**为单位。同样的字符数在不同语言中可能产生差异很大的令牌数：

| 语言 | 1,000 字符 ≈ 令牌数 | 推荐 chunkSize |
|------|---------------------|----------------|
| 英语 | ~250 令牌 | 500–2,000 |
| 中文 / 韩语 / 日语 | ~800–1,500 令牌 | 300–1,000 |

> [!WARNING]
> 中文、韩语、日语等 CJK 文本的每字符令牌比率远高于英语。如果片段超出嵌入模型的令牌限制（例如 2,048 令牌），将会发生错误。处理 CJK 文档时，请充分减小 `chunkSize`。

例如，使用令牌限制为 2,048 的嵌入模型时：

```csharp
// 英语文档：2000 字符 ≈ 500 令牌 → 富余充足
.WithTextSplitter(new MarkdownTextSplitter(2000, 200))

// 中文文档：1000 字符 ≈ 1000 令牌 → 安全范围
.WithTextSplitter(new MarkdownTextSplitter(1000, 200))
```

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

如果想编写自定义的分割模块并接入使用，请实现 `ITextSplitter` 接口：

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
