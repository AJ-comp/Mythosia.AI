# Theo dõi các tác vụ dài với Claude Fable 5.1

> Các tùy chọn Fable 5.1 yêu cầu `Mythosia.AI` 8.0.0 và `Mythosia.AI.Abstractions` 4.0.0 trở lên. API Run, suy luận/tìm kiếm và GPT-6 hiện có vẫn giữ phiên bản tối thiểu 7.1.0 / 3.1.0.

## Khi nào cần những tùy chọn này?

Một tác vụ nghiên cứu tài liệu có thể cần nhiều lần tìm kiếm và gọi công cụ trước khi trả lời. Ứng dụng có thể cần hiển thị tiến độ, yêu cầu kiểm tra chỉ trong lượt hiện tại, hoặc tiếp tục sau khi sửa nội dung hội thoại cũ. Fable 5.1 bổ sung điều khiển cho các trường hợp này, nhưng khi dùng lại thinking đã lưu, lịch sử hội thoại cũng là một phần của hợp đồng yêu cầu.

Dùng [Run API](execution-api-transition.md) để quan sát và hủy tác vụ, [tùy chọn suy luận và tìm kiếm chung](reasoning-and-search.md) để chọn mức effort và nguồn, cùng các thiết lập riêng của Claude bên dưới để xử lý tiến độ và lịch sử. Khả năng nguyên bản của mô hình không có nghĩa Mythosia cung cấp mọi API của nhà cung cấp.

## Chọn rõ mô hình và mức effort

```csharp
using Mythosia.AI.Models;
using Mythosia.AI.Services.Anthropic;

var service = new AnthropicService(apiKey, httpClient);
service.ChangeModel(AIModels.Anthropic.ClaudeFable5_1);
service.WithAdaptiveThinkingParameters(ClaudeReasoningEffort.High);

string answer = await service.GetCompletionAsync("Review this migration plan.");
```

`ClaudeFable5_1` chọn `claude-fable-5-1`. `ClaudeMythos5_1` chọn `claude-mythos-5-1` và yêu cầu quyền truy cập Project Glasswing. Các hằng Fable 5 và Mythos 5 vẫn được giữ lại. Cả hai mô hình 5.1 nhận văn bản và hình ảnh, trả về văn bản, với cửa sổ ngữ cảnh 1M token và đầu ra tối đa 128K token. [Tổng quan mô hình](https://platform.claude.com/docs/en/models/fable-5-1/overview).

Mặc định nguyên bản của mô hình là `high`, còn `ClaudeReasoningEffort.Auto` trong Mythosia giữ ánh xạ `ThinkingBudget` cũ: ngân sách đã bật tương ứng `High`, từ 32.768 là `XHigh`, từ 100.000 là `Max`. Yêu cầu tắt suy luận dùng adaptive effort thấp và bỏ thinking có thể đọc. Nếu cần `High`, hãy chọn rõ; `Auto` không có nghĩa thư viện luôn bỏ effort để dùng mặc định của mô hình.

## Hiển thị tiến độ giữa các lần gọi công cụ

`ClaudeThinkingDisplay.Updates` yêu cầu thông báo tiến độ đọc được trong khi vẫn ẩn suy luận. `Summarized` còn trả về tóm tắt suy luận; `Omitted` bỏ các khối thinking đọc được. Chỉ có cập nhật khi mô hình tạo ra, không bảo đảm thông báo theo chu kỳ cố định. [Cập nhật tiến độ](https://platform.claude.com/docs/en/models/fable-5-1/whats-new-fable-5-1#progress-updates-between-tool-calls-beta).

```csharp
using Mythosia.AI.Models.Streaming;

service.WithAdaptiveThinkingParameters(
    ClaudeReasoningEffort.High, ClaudeThinkingDisplay.Updates);

await using var run = await service.StartRunAsync(
    "Use the registered tools to investigate the report.",
    options: StreamOptions.FullOptions);

await foreach (var item in run.StreamAsync())
{
    if (item.Type == StreamingContentType.Reasoning)
        Console.WriteLine($"Progress: {item.Content}");
    else if (item.Type == StreamingContentType.Text)
        Console.Write(item.Content);
}
string result = (await run.Result).Text;
```

Cập nhật dùng sự kiện `StreamingContentType.Reasoning` hiện có. Bật quan sát bằng `StreamOptions.FullOptions` hoặc `StreamOptions.Default.WithReasoning()`. Với lời gọi không streaming, đọc `service.LastThinkingContent` sau khi hoàn tất. Văn bản tiến độ tách biệt với câu trả lời cuối và không tiết lộ chuỗi suy nghĩ thô.

## Không sửa lịch sử cũ để thay đổi từng lượt

Khối thinking của Fable 5.1 gắn với system prompt, công cụ và các tin nhắn trước lúc tạo ra nó. Sửa các đầu vào đó nhưng vẫn giữ thinking phía sau có thể làm thinking mất hiệu lực. Chỉ dẫn theo lượt hữu ích khi yêu cầu kiểm tra chính sách hỗ trợ trước câu trả lời hiện tại: chỉ dẫn được thêm cuối hội thoại và giữ lại, rồi ngừng áp dụng khi có tin nhắn người dùng tiếp theo. Không cần viết lại system prompt cấp cao mỗi lần. Thay đổi effort và thêm chỉ dẫn theo lượt là hai điều khiển riêng.

```csharp
await service
    .WithTurnInstruction("Check the registered support-policy tool before answering this turn.")
    .GetCompletionAsync("Can I return an opened product?");

await service
    .WithConversationInstruction("Use Korean for the remaining conversation.")
    .GetCompletionAsync("Explain the next step.");
```

Cả hai helper chụp chỉ dẫn cho yêu cầu logic tiếp theo. Mythosia giữ tin nhắn cũ và thêm tin nhắn system sau đầu vào người dùng hoặc kết quả công cụ. `WithTurnInstruction` dùng `clear_at: "next_user_message"`; trong cùng yêu cầu logic, thư viện thêm lại chỉ dẫn sau mỗi lượt kết quả công cụ để duy trì hiệu lực đến hết yêu cầu. `WithConversationInstruction` tiếp tục áp dụng cho các lượt sau. Cấu hình trước khi bắt đầu tác vụ; chúng không phải `run.SteerAsync` và không chèn chỉ dẫn vào phản hồi đang chạy.

Để thay đổi effort giữa các yêu cầu mà vẫn giữ tiền tố cache có thể tái sử dụng, dùng `.WithReasoning(ReasoningLevel.High, cache: CachePreservation.Required)` từ `Mythosia.AI.Extensions`. Thư viện gửi cập nhật effort theo tin nhắn và giữ lịch sử của nó. Xem tổ hợp được hỗ trợ trong [hướng dẫn chung](reasoning-and-search.md). Với 5.1, tiền tố/hậu tố system theo yêu cầu của `AIRequestContext` trở thành chỉ dẫn theo lượt được thêm cuối, thay vì sửa system prompt trước đó.

Fable 5.1 có thể đọc thinking từ Claude cũ, nhưng các mô hình cũ không đọc được thinking của Fable 5.1. Mythos 5.1 có cùng khả năng 5.1 nhưng không bắt buộc kiểm tra prefix binding của Fable. Khi sửa lịch sử, chuyển mô hình hoặc bỏ thinking, cần quan sát thay đổi thay vì giả định suy luận vẫn được giữ nguyên. [Hướng dẫn chuyển đổi](https://platform.claude.com/docs/en/models/fable-5-1/migration-guide).

## Chẩn đoán thay đổi lịch sử có chủ đích

`ThinkingPrefixMismatchBehavior = null` để việc kiểm tra theo chính sách tài khoản của nhà cung cấp. `WithThinkingBinding(ClaudeThinkingPrefixMismatchBehavior.Error)` yêu cầu máy chủ xác thực rõ ràng. Các sửa đổi lịch sử, `SystemMessage` hoặc công cụ do người dùng thực hiện vẫn được gửi đến Anthropic; với `Error`, tiền tố không khớp khiến nhà cung cấp trả về 400. Gửi lại cùng yêu cầu không hợp lệ không giải quyết được lỗi.

Nếu ứng dụng chủ động sửa nội dung trước đó và chấp nhận mất suy luận bị ảnh hưởng, chọn `DropBlock`. Mythosia gửi điều khiển này đến Anthropic, không âm thầm xóa thinking trước khi gửi yêu cầu.

```csharp
service.WithThinkingBinding(ClaudeThinkingPrefixMismatchBehavior.DropBlock);
service.SystemMessage = "You are reviewing the revised policy.";
await service.GetCompletionAsync("Reassess the recommendation.");

foreach (ClaudeInputTransformation change in service.LastInputTransformations)
    Console.WriteLine($"{change.Type}: {change.Reason} ({change.Model})");
```

`LastInputTransformations` cung cấp `Type`, `Path`, `Reason` do nhà cung cấp báo cáo, cùng `ResponseId` và `Model` để xác định nguồn. `prefix_binding_mismatch` chỉ tiền tố đã thay đổi; `model_binding_mismatch` chỉ thinking mà mô hình đích không đọc được. Loại bỏ nghĩa là mất suy luận, không phải sửa nó. Giữ nguyên hội thoại nếu cần bảo toàn, hoặc bắt đầu hội thoại mới khi muốn đặt lại.

Mythosia giữ lịch sử truyền đi để tránh thay đổi ngoài ý muốn do xử lý RAG/context nội bộ. Hội thoại Fable 5.1 thông thường chặn nén cục bộ tự động trong nhánh mặc định/`Error`. `DropBlock` cho phép nén nhưng có thể làm mất suy luận và không bảo đảm cache hit. Tùy chọn riêng `CachePreservation.Required` vẫn giữ quy tắc bảo vệ lịch sử nghiêm ngặt hơn. Các tùy chọn chung như `WithWebSearch()` được tiêu thụ sau mỗi yêu cầu. Bỏ chúng ở lượt kế tiếp sẽ đổi mảng tools nguyên bản và có thể làm tiền tố không khớp. Hãy áp dụng lại cùng thiết lập công cụ/tìm kiếm để giữ lịch sử; dùng `DropBlock` hoặc hội thoại mới khi chủ động thay đổi. Những tùy chọn này không tự chuyển sang yêu cầu tiếp theo.

Ảnh chụp lịch sử truyền đi thuộc về dịch vụ và `ChatBlock` của nó. Chỉ sao chép `ChatBlock` sang dịch vụ mới không chuyển các ảnh chụp RAG/context hoặc system theo lượt trước đó. Muốn giữ suy luận, hãy tiếp tục với cùng dịch vụ và cuộc trò chuyện; nếu chỉ chuyển lịch sử thô, nên bắt đầu hội thoại mới thay vì giả định trạng thái đã được giữ.

## Dùng lựa chọn công cụ thông thường

Fable 5.1 và Mythos 5.1 từ chối ép chọn công cụ. Không đặt `ForceFunctionName`; hãy mô tả trong yêu cầu khi nào cần dùng công cụ đã đăng ký. `FunctionsDisabled` vẫn dùng được khi một lượt không được gọi công cụ. Để nhận phản hồi có kiểu, dùng API structured output hiện có thay vì ép gọi hàm chỉ để lấy JSON.

## Phân biệt các thay đổi do máy chủ xử lý

| Tùy chọn nguyên bản | Anthropic beta bắt buộc |
| --- | --- |
| Effort theo tin nhắn | `mid-conversation-output-config-2026-07-01` |
| Tin nhắn system giới hạn theo lượt | `mid-conversation-system-clear-at-2026-08-21` |
| `thinking.display: "updates"` | `thinking-display-updates-2026-08-18` |
| Điều khiển thinking binding và `input_transformations` | `thinking-binding-controls-2026-08-01` |

Mythosia thêm header tương ứng khi bật thiết lập được hỗ trợ. Bật một beta không đồng thời bật tất cả beta khác. Tích hợp này không thêm compaction phía máy chủ, khối thêm/xóa công cụ nguyên bản hoặc fallback mô hình tự động.

Cả hai mô hình yêu cầu thỏa thuận lưu giữ 30 ngày áp dụng của nhà cung cấp; ZDR cần Anthropic cho phép rõ ràng. Adaptive thinking luôn bật, không có `budget_tokens` thủ công hay chế độ tắt suy luận, và không gửi tham số sampling tùy chỉnh. Quyền truy cập tài khoản và lưu giữ là yêu cầu phía máy chủ. [Điều kiện chuyển đổi](https://platform.claude.com/docs/en/models/fable-5-1/migration-guide).

Anthropic áp dụng watermark văn bản, thông tin nguồn gốc cho media được hỗ trợ và giá đọc cache. Các thay đổi này không cần tùy chọn yêu cầu Mythosia mới. Tích hợp không thêm API tạo nguồn gốc media, công tắc watermark hay điều khiển tính phí. Xem [thay đổi của Fable 5.1](https://platform.claude.com/docs/en/models/fable-5-1/whats-new-fable-5-1).
