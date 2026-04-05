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

能夠理解並保留 Markdown 結構的分割器。它識別標題層級（H1–H6）、程式碼圈欄和表格等結構，以語義有意義的單位進行分割：

```csharp
.WithTextSplitter(new MarkdownTextSplitter(500, 50))
```

最適合文件檔案、README 以及 Office/HWP 等結構化文件載入器的輸出。

> [!TIP]
> Word、Excel、PowerPoint 和 HWP 等文件載入器會在內部將文件轉換為 Markdown。對這些文件使用 `MarkdownTextSplitter`，可以確保表格和程式碼區塊的結構在分塊過程中得到完整保留。

#### 表格分割品質

`MarkdownTextSplitter` 按**行**分割 Markdown 表格。行中間絕不會被截斷，每個分割後的片段都會自動包含**表頭行和分隔線**：

```
原始表格：
| 姓名   | 部門   | 薪資     |
|--------|--------|----------|
| 張三   | 開發部 | 30,000   |
| 李四   | 產品部 | 28,000   |
| 王五   | 設計部 | 25,000   |

→ 片段 1：
| 姓名   | 部門   | 薪資     |
|--------|--------|----------|
| 張三   | 開發部 | 30,000   |
| 李四   | 產品部 | 28,000   |

→ 片段 2：
| 姓名   | 部門   | 薪資     |
|--------|--------|----------|
| 王五   | 設計部 | 25,000   |
```

每個片段都是一個獨立有效的表格，保證了嵌入和檢索品質。

#### 程式碼區塊保護

程式碼圈欄（`` ``` ``）包裹的區塊被視為**原子單位**。程式碼區塊即使超出片段大小也絕不會被中間分割，確保程式碼語義完整。

#### 標題面包屑

每個片段會自動在前面加上所屬的標題路徑，豐富向量搜尋時的上下文：

```
# 產品手冊
## 安裝指南
### Windows

（該部分的實際內容）
```

該功能由 `IncludeHeadingBreadcrumb` 屬性控制（預設值：`true`）。

## 參數選擇

| 參數 | 效果 |
|------|------|
| `chunkSize`（較大） | 每個片段包含更多上下文，片段更少，嵌入成本更低 |
| `chunkSize`（較小） | 檢索精度更高，片段更多，嵌入次數更多 |
| `chunkOverlap` | 防止片段邊界處的資訊遺失 |

常見起點：`chunkSize: 500, chunkOverlap: 50`。

## 片段大小與令牌數（多語言參考）

`chunkSize` 以**字元**為單位，但嵌入模型的限制以**令牌（token）**為單位。同樣的字元數在不同語言中可能產生差異很大的令牌數：

| 語言 | 1,000 字元 ≈ 令牌數 | 推薦 chunkSize |
|------|---------------------|----------------|
| 英語 | ~250 令牌 | 500–2,000 |
| 中文 / 韓語 / 日語 | ~800–1,500 令牌 | 300–1,000 |

> [!WARNING]
> 中文、韓語、日語等 CJK 文字的每字元令牌比率遠高於英語。如果片段超出嵌入模型的令牌限制（例如 2,048 令牌），將會發生錯誤。處理 CJK 文件時，請充分減小 `chunkSize`。

例如，使用令牌限制為 2,048 的嵌入模型時：

```csharp
// 英語文件：2000 字元 ≈ 500 令牌 → 寬裕充足
.WithTextSplitter(new MarkdownTextSplitter(2000, 200))

// 中文文件：1000 字元 ≈ 1000 令牌 → 安全範圍
.WithTextSplitter(new MarkdownTextSplitter(1000, 200))
```

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
