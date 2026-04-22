# Sử dụng token

Sử dụng token cho biết một request tới model đã tiêu tốn bao nhiêu token cho input, output, cache và reasoning. Trong Mythosia.AI, thông tin này được trả về qua `TokenUsage` trên các sự kiện streaming.

Điều này đặc biệt quan trọng khi câu trả lời không chỉ có một lần gọi LLM. Một câu trả lời đơn giản thường chỉ có một round. Agent hoặc luồng function calling có thể gọi model, chạy tool, rồi gọi model lần nữa với kết quả của tool. Vì vậy có hai con số cần phân biệt.

- `RoundUsage` là usage của một round LLM vừa kết thúc.
- `Completion.Usage` là usage cộng dồn của toàn bộ stream.

> [!NOTE]
> Trang này giả định bạn đã biết **LLM round** là gì. Tóm lại: một round = một lần trao đổi yêu cầu–phản hồi giữa app và model. Luồng function calling có thể tạo ra nhiều round cho một tin nhắn duy nhất của người dùng. Để xem giải thích từng bước, hãy tham khảo [Khái niệm cốt lõi — Round là gì?](core-concepts.md#round-là-gì).

## Vì sao cần quan tâm

Với đồng hồ đo context trong UI chat, thường bạn nên dùng `RoundUsage.Usage.TotalTokens` mới nhất. Giá trị này gần nhất với câu hỏi: "nếu tiếp tục hội thoại ngay bây giờ, input tiếp theo gửi vào model sẽ lớn cỡ nào?"

Với log, chẩn đoán và phân tích chi phí, hãy dùng `Completion.Usage.TotalTokens`. Giá trị này là tổng của cả run, kể cả khi function calling hoặc agent tạo ra nhiều round.

Với tối ưu hiệu năng, các trường cache và reasoning giúp bạn biết provider có tái sử dụng input cache hay không, và model đã dùng thêm bao nhiêu token cho reasoning nội bộ.

## Mô hình sự kiện

| Sự kiện | Ý nghĩa | Nên dùng cho |
|---|---|---|
| `StreamingContentType.RoundUsage` | Usage của round LLM vừa hoàn tất | Đồng hồ context UI, debug theo từng round |
| `StreamingContentType.Completion` | Sự kiện cuối cùng với usage cộng dồn | Log, chẩn đoán, báo cáo chi phí |

`RoundUsage.Usage` không phải giá trị cộng dồn. Nếu round 1 dùng 10.100 token và round 2 dùng 14.000 token, `Completion.Usage.TotalTokens` cuối cùng có thể là 24.100, còn `RoundUsage.Usage.TotalTokens` cuối cùng vẫn là 14.000.

| Thuộc tính | Ý nghĩa |
|---|---|
| `RoundIndex` | Số thứ tự round LLM, bắt đầu từ 1 |
| `IsFinalRound` | `true` nếu đây là round LLM cuối cùng trong stream |

Usage được emit khi provider trả về dữ liệu usage. Bạn không cần bật `IncludeMetadata = true` để nhận các sự kiện này.

## Usage cộng dồn cuối cùng

Dùng `Completion.Usage` khi bạn muốn tổng usage của cả request streaming.

```csharp
await foreach (var chunk in service.StreamAsync("Giải thích điện toán lượng tử", StreamOptions.FullOptions))
{
    if (chunk.Type == StreamingContentType.Text)
        Console.Write(chunk.Content);

    if (chunk.Type == StreamingContentType.Completion && chunk.Usage is not null)
    {
        Console.WriteLine($"Input:  {chunk.Usage.InputTokens}");
        Console.WriteLine($"Output: {chunk.Usage.OutputTokens}");
        Console.WriteLine($"Total:  {chunk.Usage.TotalTokens}");
    }
}
```

Với một round LLM duy nhất, giá trị này thường gần với `RoundUsage`. Với agent, đây là tổng của tất cả các round LLM.

## Đồng hồ token trong UI

Với đồng hồ đo kích thước context, dùng `RoundUsage` mới nhất.

```csharp
await foreach (var chunk in service.StreamAsync(message, StreamOptions.FullOptions))
{
    if (chunk.Type == StreamingContentType.RoundUsage && chunk.Usage is not null)
    {
        UpdateContextTokenMeter(chunk.Usage.TotalTokens);

        if (chunk.IsFinalRound)
            MarkTokenMeterAsFinal();

        continue;
    }

    if (chunk.Type == StreamingContentType.Text)
        AppendToChat(chunk.Content);
}
```

Round cuối cùng của model nhìn thấy trạng thái hội thoại mới nhất, bao gồm cả kết quả tool được thêm trong lúc run. Vì vậy `RoundUsage.TotalTokens` cuối cùng là giá trị hợp lý nhất cho UI chat.

## Function Calling và agent

Trong luồng function calling, model có thể chạy nhiều lần. Hãy đọc từng `RoundUsage`, giữ giá trị cuối cùng cho UI, rồi dùng `Completion.Usage` ở cuối để lấy tổng.

```csharp
TokenUsage? latestRound = null;
TokenUsage? cumulative = null;

await foreach (var chunk in service.StreamAsync(message, StreamOptions.WithFunctions))
{
    if (chunk.Type == StreamingContentType.RoundUsage && chunk.Usage is not null)
    {
        latestRound = chunk.Usage;
        Console.WriteLine($"Round {chunk.RoundIndex}: {latestRound.TotalTokens} tokens");
        continue;
    }

    if (chunk.Type == StreamingContentType.Completion)
        cumulative = chunk.Usage;
}
```

## Cache và reasoning

Khi provider cung cấp, `TokenUsage` cũng có các trường liên quan đến cache và reasoning.

| Thuộc tính | Ý nghĩa |
|---|---|
| `InputTokens` | Token trong prompt/input |
| `OutputTokens` | Token do model sinh ra |
| `TotalTokens` | Input + output trong phạm vi sự kiện |
| `CachedInputTokens` | Input token được phục vụ từ cache |
| `CacheCreationTokens` | Token được ghi vào cache |
| `ReasoningTokens` | Token dùng cho reasoning nội bộ ẩn |
| `VisibleOutputTokens` | Output token không tính reasoning |

## Vì sao nên dùng các sự kiện đã chuẩn hóa

Mỗi provider gắn usage vào stream chunk theo cách khác nhau. Trường hợp cần chú ý nhất là Gemini: usage có thể nằm trên text hoặc status chunk, đôi khi còn đến sau function-call chunk — vì vậy thư viện sẽ đọc tiếp stream đủ lâu để lấy usage trước khi chuyển sang round tiếp theo. Mythosia.AI hấp thụ những khác biệt giữa các provider này và chuẩn hóa chúng thành các sự kiện `RoundUsage` và `Completion.Usage`, nên ở phía ứng dụng, thay vì tự parse metadata riêng của từng provider, hãy dùng các sự kiện đã chuẩn hóa là `RoundUsage` và `Completion.Usage`.
