# Text Splitter

Text splitter chia tài liệu thành các đoạn trước khi embedding. Kích thước và độ chồng lấp của đoạn ảnh hưởng đáng kể đến chất lượng truy xuất.

## Các splitter có sẵn

### CharacterTextSplitter

Chia theo số ký tự. Đơn giản và nhanh, nhưng có thể cắt giữa câu:

```csharp
.WithTextSplitter(new CharacterTextSplitter(500, 50))
```

### RecursiveTextSplitter (mặc định được khuyến nghị)

Cố gắng chia tại các ranh giới có nghĩa theo thứ tự: đoạn văn → câu → từ → ký tự. Tạo ra các đoạn nhất quán hơn:

```csharp
.WithTextSplitter(new RecursiveTextSplitter(500, 50))
```

### TokenTextSplitter

Chia theo số token thay vì ký tự. Chính xác hơn cho việc quản lý ngân sách context window của LLM:

```csharp
.WithTextSplitter(new TokenTextSplitter(256, 32))
```

Dùng khi model embedding có giới hạn token nghiêm ngặt.

### MarkdownTextSplitter

Splitter nhận biết cấu trúc, hiểu phân cấp heading Markdown (H1–H6), code fence và bảng, chia nội dung thành các đơn vị có nghĩa:

```csharp
.WithTextSplitter(new MarkdownTextSplitter(500, 50))
```

Phù hợp nhất cho file tài liệu, README và output từ document loader như Office và HWP.

> [!TIP]
> Document loader cho Word, Excel, PowerPoint và HWP chuyển đổi tài liệu sang Markdown. Dùng `MarkdownTextSplitter` với các tài liệu này đảm bảo cấu trúc bảng và code block được giữ nguyên trong quá trình chunking.

#### Chất lượng chia bảng

`MarkdownTextSplitter` chia Markdown table tại **ranh giới hàng**. Không bao giờ cắt ngang hàng, và mỗi đoạn kết quả tự động bao gồm **hàng header và dòng phân cách**:

```
Bảng gốc:
| Tên    | Phòng ban | Lương   |
|--------|-----------|---------|
| Alice  | Dev       | $90,000 |
| Bob    | PM        | $85,000 |
| Carol  | Design    | $80,000 |

→ Đoạn 1:
| Tên    | Phòng ban | Lương   |
|--------|-----------|---------|
| Alice  | Dev       | $90,000 |
| Bob    | PM        | $85,000 |

→ Đoạn 2:
| Tên    | Phòng ban | Lương   |
|--------|-----------|---------|
| Carol  | Design    | $80,000 |
```

Mỗi đoạn là một bảng hoàn chỉnh, độc lập — đảm bảo chất lượng embedding và truy xuất.

#### Bảo vệ code block

Các khối code fence (`` ``` ``) được coi là **đơn vị nguyên tử**. Code block không bao giờ bị chia giữa chừng, dù vượt quá kích thước đoạn, giữ nguyên ngữ nghĩa của code.

#### Breadcrumb heading

Mỗi đoạn tự động được tiền tố bằng đường dẫn heading dẫn đến nội dung của nó, làm giàu context cho vector search:

```
# Hướng dẫn sử dụng
## Hướng dẫn cài đặt
### Windows

(nội dung thực tế của section này)
```

Tính năng này được kiểm soát bởi thuộc tính `IncludeHeadingBreadcrumb` (mặc định: `true`).

## Chọn tham số

| Tham số | Tác động |
|-----------|--------|
| `chunkSize` (lớn hơn) | Nhiều context hơn mỗi đoạn, ít đoạn hơn, embedding rẻ hơn |
| `chunkSize` (nhỏ hơn) | Truy xuất chính xác hơn, nhiều đoạn hơn, nhiều embedding hơn |
| `chunkOverlap` | Ngăn mất thông tin tại ranh giới đoạn |

Điểm khởi đầu phổ biến: `chunkSize: 500, chunkOverlap: 50`.

## Kích thước đoạn so với số token (Đa ngôn ngữ)

`chunkSize` được tính theo **ký tự**, nhưng giới hạn của model embedding tính theo **token**. Cùng số ký tự có thể tạo ra số lượng token rất khác nhau tùy ngôn ngữ:

| Ngôn ngữ | 1.000 ký tự ≈ token | chunkSize khuyến nghị |
|----------|----------------------|-----------------------|
| Tiếng Anh | ~250 token | 500–2.000 |
| Tiếng Hàn / Nhật / Trung | ~800–1.500 token | 300–1.000 |

> [!WARNING]
> Văn bản CJK (Hàn, Nhật, Trung) có tỷ lệ token/ký tự cao hơn nhiều so với tiếng Anh. Nếu các đoạn vượt quá giới hạn token của model embedding (ví dụ 2.048 token), sẽ xảy ra lỗi. Hãy giảm `chunkSize` đáng kể khi làm việc với tài liệu CJK.

Ví dụ với model embedding có giới hạn 2.048 token:

```csharp
// Tài liệu tiếng Anh: 2000 ký tự ≈ 500 token → trong giới hạn
.WithTextSplitter(new MarkdownTextSplitter(2000, 200))

// Tài liệu tiếng Hàn: 1000 ký tự ≈ 1000 token → phạm vi an toàn
.WithTextSplitter(new MarkdownTextSplitter(1000, 200))
```

## Splitter theo từng tài liệu

Có thể áp dụng splitter khác nhau cho từng tài liệu trong `RagBuilder`:

```csharp
.WithRag(rag => rag
    .AddDocuments(new PlainTextDocumentLoader(), "readme.md", new MarkdownTextSplitter(600, 60))
    .AddDocuments(new PlainTextDocumentLoader(), "data.txt",  new RecursiveTextSplitter(300, 30))
    .WithTextSplitter(new RecursiveTextSplitter(500, 50))  // mặc định cho phần còn lại
)
```

## Splitter tùy chỉnh

Nếu bạn muốn viết một module chia tùy chỉnh và tích hợp vào, hãy triển khai `ITextSplitter`:

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

// Đăng ký:
.WithTextSplitter(new SentenceSplitter())
```
