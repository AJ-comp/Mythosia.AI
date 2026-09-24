# 文本分割器

搜索结果需要足够的上下文来回答问题，但将整篇文档作为一个单元又难以定位所需片段。分块用于平衡大小与上下文。以下分割器按本地规则运行，无需 AI 模型。请按文档结构选择，再用实际问题评估检索效果。

## 可用分割器

### CharacterTextSplitter

适用于只需简单大小限制的纯文本。优先采用配置的分隔符，但也可能在句子中间切分。`RagBuilder` 默认使用 `CharacterTextSplitter(300, 30)`；`.md` 扩展名不会自动选择 Markdown 分割器。

```csharp
.WithTextSplitter(new CharacterTextSplitter(500, 50))
```

### RecursiveTextSplitter（推荐默认选项）

适用于希望尽量保留完整段落和单词的正文。默认分隔顺序为空行 → 换行 → `. ` → 空格 → 单个字符。这是文本规则，不是模型判断语义；句点加空格也只是近似的句子边界。

`Separators` 中的重复项按首次出现的顺序只应用一次。重复配置同一个分隔符不会增加分割轮次。长分隔符列表也不会通过深度嵌套的递归调用处理。

```csharp
.WithTextSplitter(new RecursiveTextSplitter(500, 50))
```

### TokenTextSplitter

仅适用于按空白分隔单词进行粗略计数。虽然名称包含 Token，`MaxTokensPerChunk` 与 `TokenOverlap` 实际统计 `TokenSeparators`（默认空格、制表符、换行）分隔的单元，而非模型 token。输出会将分隔符统一为空格。没有空白的长文本可能仍是一个单元，因此不能保证模型的 token 上限。

```csharp
.WithTextSplitter(new TokenTextSplitter(256, 32))
```

### MarkdownTextSplitter

适用于需保留标题上下文、表格行和代码块的 Markdown 文档，以及 Office/HWP 加载器输出的 Markdown。识别 ATX 标题（`#`–`######`）、代码围栏和表格。这是基于规则的分割器，并非完整的 Markdown 语法树解析器。构造函数仅接受 `chunkSize`；Markdown 没有 overlap 参数或选项。

```csharp
.WithTextSplitter(new MarkdownTextSplitter(500))
```

#### 表格分割质量

识别的 GFM 表格按行之间的边界切分，每个表格块重复表头与分隔行。外侧竖线可省略（支持 `Name | Value`）。这保留了列名，但检索质量仍取决于文档、嵌入和问题。

加粗的表格单元格属于所在行。例如，A 公司行中的 `**不退款**` 不能变成 B 公司的条件。表格单元格和代码块不会被提升为重复的正文标签。独立的 `**标签**` 行只有位于空行或结构边界之后，即段落或文本块起点时才会被识别。现有段落中仅因换行独占一行的加粗内容不会被变成新段落或重复标签。识别出的标签可在该文本块的分片中重复；遇到表格、代码围栏、标题或下一个识别出的标签时，其作用范围结束。

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

#### 代码块保护

反引号或波浪号围栏内的代码保持完整。结束围栏须使用相同字符，长度不少于起始围栏；块内较短的围栏不会将其结束。为了保留完整代码块，大小可能超过 `ChunkSize`。

起始围栏的缩进与代码一同保留，因此分割不会改变渲染后代码的缩进含义。每个块只解析一次起始围栏信息，避免在正文的每一行重复扫描很长的围栏。

#### 标题面包屑

`IncludeHeadingBreadcrumb` 默认为 `true`：每个块重复所属标题路径，使检索片段保留上下文。设为 `false` 只停止重复，原始标题仍会保留。仅有标题的章节也不会被丢弃。

`MinSplitHeadingLevel` 接受 1–6，用于选择哪些标题级别开始新章节，默认值为 1。 上级标题变化时，会结束此前的下级章节，避免将旧标题路径附加到新内容。

```csharp
.WithTextSplitter(new MarkdownTextSplitter(500)
{
    IncludeHeadingBreadcrumb = false
})
```

## 参数选择

`CharacterTextSplitter`、`RecursiveTextSplitter`、`MarkdownTextSplitter` 按 UTF-16 代码单元（`string.Length`）计数，不是模型 token 或可见字形。表情等代理对不会从中间切开。大小为 1 时，一个需要 2 单元的代理对可例外超限。不保证组合字符及完整字素簇始终在一起。

大小必须为正，overlap 不可为负；无效设置在处理前抛出 `ArgumentOutOfRangeException`。可修改属性在分割时也会重新验证。overlap 大于或等于大小时，为兼容而禁用重叠。Character/Recursive 的 overlap 是按分隔符、Unicode 边界和下一块剩余空间调整的目标值；`0` 表示不重叠。不会生成只含末尾重复内容的额外块。

Markdown 的 `ChunkSize` 是**不含重复标题路径的正文预算**。完整代码块，或表头加一整行，可能超过预算。普通正文遵守大小限制，代理对例外除外。

为防止重复标题和表头把小文档放大成过量的嵌入输入，Markdown 另设文档整体输出预算。上限为 `max(65536, 32 × document.Content.Length)` 个 UTF-16 单位，按所有最终块的长度求和，包含重复的标题路径、表头和标签。创建过量重复输出之前会检查预算，若将超限则抛出 `InvalidOperationException`，不会截断内容或只返回部分结果。`ChunkSize` 及不可拆分块的例外仍受这一整体上限约束。在默认 RAG 索引流程中，此分割失败发生在嵌入或替换存储记录之前，因此该文档的现有索引保持不变。这是输出字符串上限，并非模型 token 或进程内存上限。 预算按每次 `Split` 调用应用，随原文长度增长，并非固定的文档输入大小上限。

可从正文 `RecursiveTextSplitter(500, 50)` 或 Markdown 的 `MarkdownTextSplitter(500)` 开始，再用代表性问题评估。较大块保留更多周边内容；重叠会重复文本并增加嵌入工作量。两者均不自动保证检索更好。

若必须严格遵守嵌入或 LLM 的 token 上限，应使用目标模型的分词器统计每个最终块，包括重复标题和表头。字符、单词数量及语言换算比例不是安全的 token 预算。需要硬性上限时，可使用该分词器实现 `ITextSplitter`。

此次修复会改变相关文档的分块边界。请按相同文档 ID 重新索引以替换旧块，并更新相关嵌入缓存与评估基准。已有存储块不会自动重写。

## 按文档指定分割器

在 `RagBuilder` 中可以为不同文档应用不同的分割器：

```csharp
.WithRag(rag => rag
    .AddDocuments(new PlainTextDocumentLoader(), "readme.md", new MarkdownTextSplitter(600))
    .AddDocuments(new PlainTextDocumentLoader(), "data.txt",  new RecursiveTextSplitter(300, 30))
    .WithTextSplitter(new RecursiveTextSplitter(500, 50))  // 其余文档的默认分割器
)
```

## 自定义分割器

如果想编写自定义的分割模块并接入使用，请实现 `ITextSplitter` 接口：

索引不应报告成功，却让后一个分块覆盖前一个分块。请为每个分块提供在集合中唯一且非空白的 ID，并复制文档元数据，以保留公司或访问权限过滤条件。下面的示例组合文档 ID 与分块序号。管线会拒绝缺失的 ID 和同一文档内重复的 ID，不会自动生成替代 ID。请参阅[索引验证](rag-pipeline.md#indexing-validation)。

```csharp
public class SentenceSplitter : ITextSplitter
{
    public IReadOnlyList<RagChunk> Split(RagDocument document)
    {
        var sentences = document.Content.Split(". ");
        return sentences.Select((s, i) => new RagChunk
        {
            Id = $"{document.Id}_chunk_{i}",
            Content = s,
            Index = i,
            DocumentId = document.Id,
            Metadata = new Dictionary<string, string>(document.Metadata)
        }).ToList();
    }
}

// 注册：
.WithTextSplitter(new SentenceSplitter())
```
