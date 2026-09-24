# 文字分割器

搜尋結果需要足夠的上下文來回答問題，但將整份文件視為一個單位又難以定位所需片段。分塊用於平衡大小與上下文。以下分割器依本機規則運作，不需要 AI 模型。請依文件結構選擇，再以實際問題評估檢索效果。

## 可用分割器

### CharacterTextSplitter

適用於只需簡單大小限制的純文字。優先採用設定的分隔符號，但也可能在句子中間切分。`RagBuilder` 預設使用 `CharacterTextSplitter(300, 30)`；`.md` 副檔名不會自動選擇 Markdown 分割器。

```csharp
.WithTextSplitter(new CharacterTextSplitter(500, 50))
```

### RecursiveTextSplitter（建議預設選項）

適用於希望盡量保留完整段落與單字的內文。預設分隔順序為空白行 → 換行 → `. ` → 空格 → 個別字元。這是文字規則，不是模型判斷語意；句點加空格也只是近似的句子邊界。

`Separators` 中的重複項按首次出現的順序只套用一次。重複設定同一個分隔符不會增加分割次數。長分隔符清單也不會透過深度巢狀的遞迴呼叫處理。

```csharp
.WithTextSplitter(new RecursiveTextSplitter(500, 50))
```

### TokenTextSplitter

僅適用於依空白分隔單字進行粗略計數。雖然名稱包含 Token，`MaxTokensPerChunk` 與 `TokenOverlap` 實際統計 `TokenSeparators`（預設空格、定位字元、換行）分隔的單位，而非模型 token。輸出會將分隔符號統一為空格。沒有空白的長文字可能仍是一個單位，因此無法保證模型的 token 上限。

```csharp
.WithTextSplitter(new TokenTextSplitter(256, 32))
```

### MarkdownTextSplitter

適用於需保留標題上下文、表格列與程式碼區塊的 Markdown 文件，以及 Office/HWP 載入器輸出的 Markdown。辨識 ATX 標題（`#`–`######`）、程式碼圍欄與表格。這是規則式分割器，並非完整的 Markdown 語法樹剖析器。建構函式僅接受 `chunkSize`；Markdown 沒有 overlap 參數或選項。

```csharp
.WithTextSplitter(new MarkdownTextSplitter(500))
```

#### 表格分割品質

辨識的 GFM 表格依列之間的邊界切分，每個表格塊重複表頭與分隔列。外側豎線可省略（支援 `Name | Value`）。這保留了欄名，但檢索品質仍取決於文件、嵌入與問題。

粗體的表格儲存格屬於所在列。例如，A 公司列中的 `**不退款**` 不能變成 B 公司的條件。表格儲存格與程式碼區塊不會被提升為重複的正文標籤。獨立的 `**標籤**` 行只有位於空行或結構邊界之後，即段落或文字區塊起點時才會被辨識。既有段落中僅因換行獨占一行的粗體內容不會被變成新段落或重複標籤。辨識出的標籤可在該文字區塊的分片中重複；遇到表格、程式碼圍欄、標題或下一個辨識出的標籤時，其作用範圍結束。

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

#### 程式碼區塊保護

反引號或波浪號圍欄內的程式碼維持完整。結束圍欄須使用相同字元，長度不短於起始圍欄；區塊內較短的圍欄不會將它結束。為保留完整程式碼區塊，大小可能超過 `ChunkSize`。

起始圍欄的縮排與程式碼一同保留，因此分割不會改變呈現後程式碼的縮排含義。每個區塊只解析一次起始圍欄資訊，避免在正文的每一行重複掃描很長的圍欄。

#### 標題麵包屑

`IncludeHeadingBreadcrumb` 預設為 `true`：每個塊重複所屬標題路徑，讓搜尋片段保有上下文。設為 `false` 只停止重複，原始標題仍會保留。僅有標題的章節也不會被丟棄。

`MinSplitHeadingLevel` 接受 1–6，用來選擇哪些標題層級開始新章節，預設值為 1。 上層標題變更時，會結束先前的下層章節，避免將舊標題路徑附加到新內容。

```csharp
.WithTextSplitter(new MarkdownTextSplitter(500)
{
    IncludeHeadingBreadcrumb = false
})
```

## 參數選擇

`CharacterTextSplitter`、`RecursiveTextSplitter`、`MarkdownTextSplitter` 以 UTF-16 程式碼單位（`string.Length`）計數，不是模型 token 或可見字形。表情符號等代理對不會從中間切開。大小為 1 時，一個需要 2 單位的代理對可例外超限。不保證組合字元與完整字素叢集始終在一起。

大小必須為正，overlap 不可為負；無效設定在處理前擲回 `ArgumentOutOfRangeException`。可修改屬性在分割時也會重新驗證。overlap 大於或等於大小時，為相容而停用重疊。Character/Recursive 的 overlap 是依分隔符號、Unicode 邊界與下一塊剩餘空間調整的目標值；`0` 表示不重疊。不會產生只含末尾重複內容的額外塊。

Markdown 的 `ChunkSize` 是**不含重複標題路徑的內文預算**。完整程式碼區塊，或表頭加一整列，可能超過預算。一般內文遵守大小限制，代理對例外除外。

為防止重複標題與表頭把小文件放大成過量的嵌入輸入，Markdown 另設文件整體輸出預算。上限為 `max(65536, 32 × document.Content.Length)` 個 UTF-16 單位，按所有最終區塊的長度加總，包含重複的標題路徑、表頭與標籤。建立過量重複輸出之前會檢查預算，若將超限則擲出 `InvalidOperationException`，不會截斷內容或只回傳部分結果。`ChunkSize` 及不可拆分區塊的例外仍受這一整體上限約束。在預設 RAG 索引流程中，此分割失敗發生在嵌入或取代儲存紀錄之前，因此該文件的既有索引保持不變。這是輸出字串上限，並非模型 token 或處理程序記憶體上限。 預算按每次 `Split` 呼叫套用，隨原文長度增長，並非固定的文件輸入大小上限。

可從內文的 `RecursiveTextSplitter(500, 50)` 或 Markdown 的 `MarkdownTextSplitter(500)` 開始，再用代表性問題評估。較大塊保留更多周邊內容；重疊會重複文字並增加嵌入工作量。兩者均不自動保證檢索更好。

若必須嚴格遵守嵌入或 LLM 的 token 上限，應以目標模型的 tokenizer 統計每個最終塊，包含重複標題與表頭。字元、單字數量及語言換算比例不是安全的 token 預算。需要硬性上限時，可使用該 tokenizer 實作 `ITextSplitter`。

此次修正會改變相關文件的分塊邊界。請以相同文件 ID 重新索引以取代舊塊，並更新相關嵌入快取與評估基準。既有儲存塊不會自動重寫。

## 按文件指定分割器

```csharp
.WithRag(rag => rag
    .AddDocuments(new PlainTextDocumentLoader(), "readme.md", new MarkdownTextSplitter(600))
    .AddDocuments(new PlainTextDocumentLoader(), "data.txt",  new RecursiveTextSplitter(300, 30))
    .WithTextSplitter(new RecursiveTextSplitter(500, 50))
)
```

## 自訂分割器

如果想撰寫自訂的分割模組並整合使用，請實作 `ITextSplitter` 介面：

索引不應回報成功，卻讓後一個區塊覆寫前一個區塊。請為每個區塊提供在集合中唯一且非空白的 ID，並複製文件中繼資料，以保留公司或存取權限篩選條件。下列範例結合文件 ID 與區塊序號。管線會拒絕缺少的 ID 和同一文件內重複的 ID，不會自動產生替代 ID。請參閱[索引驗證](rag-pipeline.md#indexing-validation)。

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

.WithTextSplitter(new SentenceSplitter())
```
