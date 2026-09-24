# Text Splitter

Kết quả tìm kiếm cần đủ ngữ cảnh để trả lời, nhưng gộp cả tài liệu thành một đơn vị lại khó chọn đúng đoạn. Chia đoạn cân bằng kích thước và ngữ cảnh. Các splitter này dùng quy tắc cục bộ, không cần mô hình AI. Hãy chọn theo cấu trúc tài liệu rồi đánh giá bằng câu hỏi thực tế.

## Các splitter có sẵn

### CharacterTextSplitter

Dùng cho văn bản thuần khi chỉ cần giới hạn kích thước đơn giản. Ưu tiên dấu phân cách đã cấu hình, nhưng có thể cắt giữa câu. `RagBuilder` mặc định dùng `CharacterTextSplitter(300, 30)`; đuôi `.md` không tự chọn splitter Markdown.

```csharp
.WithTextSplitter(new CharacterTextSplitter(500, 50))
```

### RecursiveTextSplitter (mặc định được khuyến nghị)

Dùng khi muốn giữ các đoạn văn và từ liền nhau nếu có thể. Thứ tự mặc định là dòng trống → xuống dòng → `. ` → dấu cách → ký tự. Đây là quy tắc văn bản, không phải mô hình đánh giá ý nghĩa; dấu chấm kèm dấu cách chỉ xấp xỉ ranh giới câu.

Các mục trùng trong `Separators` chỉ được áp dụng một lần theo thứ tự xuất hiện đầu tiên. Lặp một dấu phân cách không thêm lượt chia. Danh sách dài được xử lý mà không lồng sâu các lời gọi đệ quy.

```csharp
.WithTextSplitter(new RecursiveTextSplitter(500, 50))
```

### TokenTextSplitter

Chỉ dùng để đếm gần đúng các từ cách nhau bằng khoảng trắng. Dù tên là Token, `MaxTokensPerChunk` và `TokenOverlap` đếm đơn vị do `TokenSeparators` tách (mặc định dấu cách, tab và xuống dòng), không phải token của mô hình. Đầu ra chuẩn hóa dấu phân cách thành dấu cách. Văn bản không có khoảng trắng có thể vẫn là một đơn vị dài; splitter này không bảo đảm giới hạn token của mô hình.

```csharp
.WithTextSplitter(new TokenTextSplitter(256, 32))
```

### MarkdownTextSplitter

Dùng cho tài liệu Markdown hoặc Markdown từ loader Office/HWP khi cần giữ tiêu đề, hàng bảng và khối mã. Nhận diện tiêu đề ATX (`#`–`######`), khối mã có hàng rào và bảng. Đây là splitter theo quy tắc, không phải bộ phân tích cây cú pháp Markdown đầy đủ. Hàm tạo chỉ nhận `chunkSize`; Markdown không có tham số hay tùy chọn overlap.

```csharp
.WithTextSplitter(new MarkdownTextSplitter(500))
```

#### Chất lượng chia bảng

Bảng GFM được nhận diện sẽ tách giữa các hàng, lặp lại tiêu đề và hàng phân cách trong mỗi đoạn bảng. Dấu gạch đứng ngoài cùng là tùy chọn (`Name | Value` được hỗ trợ). Nhờ vậy tên cột được giữ lại; chất lượng tìm kiếm vẫn phụ thuộc tài liệu, embedding và câu hỏi.

Nội dung in đậm trong ô thuộc về hàng đó: chẳng hạn, `**Không hoàn tiền**` ở hàng công ty A không được trở thành điều kiện cho B. Ô bảng và khối mã không được chuyển thành nhãn văn bản lặp lại. Dòng `**nhãn**` đứng riêng chỉ được nhận diện ở đầu đoạn hoặc khối văn bản, sau dòng trống hay ranh giới cấu trúc. Dòng in đậm do xuống dòng giữa một đoạn đang tiếp diễn không tạo đoạn mới hay nhãn lặp lại. Nhãn hợp lệ có thể lặp trong các mảnh của khối đó; bảng, hàng rào mã, tiêu đề hoặc nhãn hợp lệ tiếp theo kết thúc phạm vi.

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

#### Bảo vệ khối mã

Khối được bao bằng dấu backtick hoặc dấu ngã được giữ nguyên. Hàng rào đóng phải dùng cùng ký tự và dài ít nhất bằng hàng rào mở; hàng rào ngắn hơn bên trong không kết thúc khối. Việc giữ nguyên khối có thể vượt `ChunkSize`.

Thụt lề của hàng rào mở được giữ cùng mã, nên việc chia không làm thay đổi thụt lề của mã sau khi hiển thị. Thông tin hàng rào mở được phân tích một lần cho mỗi khối, tránh quét lại hàng rào dài ở từng dòng nội dung.

#### Đường dẫn tiêu đề

`IncludeHeadingBreadcrumb` mặc định là `true`: mỗi đoạn lặp lại đường dẫn tiêu đề để giữ ngữ cảnh khi được tìm thấy. Đặt `false` chỉ tắt việc lặp, vẫn giữ tiêu đề gốc. Phần chỉ có tiêu đề cũng được giữ lại.

`MinSplitHeadingLevel` nhận 1–6 để chọn cấp tiêu đề bắt đầu phần mới; mặc định là 1. Khi tiêu đề cấp trên thay đổi, phần con trước đó kết thúc để đường dẫn tiêu đề cũ không bị áp dụng cho nội dung mới.

```csharp
.WithTextSplitter(new MarkdownTextSplitter(500)
{
    IncludeHeadingBreadcrumb = false
})
```

## Chọn tham số

`CharacterTextSplitter`, `RecursiveTextSplitter` và `MarkdownTextSplitter` đo đơn vị mã UTF-16 (`string.Length`), không phải token hay ký tự hiển thị. Cặp surrogate như emoji không bị cắt đôi. Với kích thước 1, một cặp cần 2 đơn vị có thể vượt giới hạn. Không bảo đảm giữ liền ký tự kết hợp hoặc toàn bộ cụm grapheme.

Kích thước phải dương, overlap không âm. Cấu hình sai gây `ArgumentOutOfRangeException` trước khi xử lý; thuộc tính có thể thay đổi được kiểm tra lại khi chia. Overlap lớn hơn hoặc bằng kích thước sẽ bị tắt để tương thích. Với Character/Recursive, overlap là mục tiêu điều chỉnh theo dấu phân cách, ranh giới Unicode và chỗ trống của đoạn kế tiếp; `0` nghĩa là không chồng lấp. Không tạo đoạn đuôi chỉ chứa phần chồng lấp cuối cùng.

Với Markdown, `ChunkSize` là ngân sách nội dung **không tính đường dẫn tiêu đề lặp lại**. Một khối mã nguyên vẹn hoặc tiêu đề bảng kèm một hàng đầy đủ có thể vượt ngân sách. Văn bản thường tuân thủ kích thước, ngoại trừ cặp surrogate nêu trên.

Việc lặp tiêu đề và đầu bảng không được biến tài liệu nhỏ thành lượng văn bản embedding không giới hạn. Vì vậy Markdown có ngân sách đầu ra riêng cho mỗi tài liệu là `max(65536, 32 × document.Content.Length)` đơn vị UTF-16, cộng độ dài mọi chunk cuối cùng, gồm cả đường dẫn tiêu đề, đầu bảng và nhãn lặp lại. Ngân sách được kiểm tra trước khi tạo phần lặp quá lớn; nếu sẽ vượt giới hạn, hệ thống ném `InvalidOperationException`, không cắt nội dung hoặc trả kết quả một phần. `ChunkSize` và các ngoại lệ cho khối không thể chia vẫn phải nằm trong giới hạn tổng này. Trong luồng lập chỉ mục RAG mặc định, lỗi chia xảy ra trước embedding hoặc thay bản ghi nên chỉ mục hiện có của tài liệu được giữ nguyên. Đây là giới hạn văn bản đầu ra, không phải token mô hình hay bộ nhớ tiến trình. Ngân sách áp dụng cho từng lần gọi `Split` và tăng theo độ dài đầu vào; đây không phải kích thước tài liệu tối đa cố định.

Có thể bắt đầu với `RecursiveTextSplitter(500, 50)` cho văn xuôi hoặc `MarkdownTextSplitter(500)` cho Markdown rồi đo bằng câu hỏi đại diện. Đoạn lớn giữ nhiều ngữ cảnh hơn; overlap lặp nội dung và tăng công việc embedding. Không lựa chọn nào tự bảo đảm tìm kiếm tốt hơn.

Để tuân thủ giới hạn token nghiêm ngặt, đếm từng đoạn cuối cùng, gồm cả tiêu đề lặp và tiêu đề bảng, bằng tokenizer của mô hình đích. Số ký tự/từ và tỷ lệ theo ngôn ngữ không phải ngân sách token an toàn. Hãy triển khai `ITextSplitter` với tokenizer đó nếu cần giới hạn cứng.

Các sửa lỗi này thay đổi ranh giới đoạn của tài liệu bị ảnh hưởng. Lập chỉ mục lại với cùng ID tài liệu để thay đoạn cũ, rồi cập nhật cache embedding và mốc đánh giá liên quan. Các đoạn đã lưu không tự động được viết lại.

## Splitter theo từng tài liệu

Có thể áp dụng splitter khác nhau cho từng tài liệu trong `RagBuilder`:

```csharp
.WithRag(rag => rag
    .AddDocuments(new PlainTextDocumentLoader(), "readme.md", new MarkdownTextSplitter(600))
    .AddDocuments(new PlainTextDocumentLoader(), "data.txt",  new RecursiveTextSplitter(300, 30))
    .WithTextSplitter(new RecursiveTextSplitter(500, 50))  // mặc định cho phần còn lại
)
```

## Splitter tùy chỉnh

Nếu bạn muốn viết một module chia tùy chỉnh và tích hợp vào, hãy triển khai `ITextSplitter`:

Không nên báo lập chỉ mục thành công trong khi một đoạn ghi đè đoạn khác. Hãy cấp cho mỗi đoạn một ID không rỗng, duy nhất trong collection và sao chép metadata của tài liệu để giữ các bộ lọc công ty hoặc quyền truy cập. Ví dụ này ghép ID tài liệu với số thứ tự đoạn. Pipeline từ chối ID thiếu và ID trùng trong cùng tài liệu; không tự tạo ID thay thế. Xem [kiểm tra khi lập chỉ mục](rag-pipeline.md#indexing-validation).

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

// Đăng ký:
.WithTextSplitter(new SentenceSplitter())
```
