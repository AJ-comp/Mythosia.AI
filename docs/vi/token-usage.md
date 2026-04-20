# Sử dụng token

Sử dụng token cho biết một request tới model đã tiêu tốn bao nhiêu token cho input, output, cache và reasoning. Trong Mythosia.AI, thông tin này được trả về qua `TokenUsage` trên các sự kiện streaming.

Điều này đặc biệt quan trọng khi câu trả lời không chỉ có một lần gọi LLM. Một câu trả lời đơn giản thường chỉ có một round. Agent hoặc luồng function calling có thể gọi model, chạy tool, rồi gọi model lần nữa với kết quả của tool. Vì vậy có hai con số cần phân biệt.

- `RoundUsage` là usage của một round LLM vừa kết thúc.
- `Completion.Usage` là usage cộng dồn của toàn bộ stream.

## Round là gì?

"Round" là một chuyến đi khứ hồi hoàn chỉnh đến model: ứng dụng của bạn gửi một prompt, model trả lời và trao đổi đó kết thúc. Một tin nhắn chat thông thường là đúng một round.

Function calling và agent sẽ tự động tạo ra nhiều round hơn. Dưới đây là ví dụ cụ thể — người dùng hỏi: *«Thời tiết ở Hà Nội hiện tại thế nào?»*

**Round 1 — quyết định công cụ**

App gửi tin nhắn của người dùng cho model. Model không biết thời tiết hiện tại, nên thay vì trả lời trực tiếp, nó trả về một yêu cầu gọi hàm: *«Vui lòng gọi `GetWeather("Hanoi")`».* Lượt của model kết thúc ở đây.

**Giữa các round**

App chạy `GetWeather("Hanoi")` và nhận được kết quả: `«15°C, có mây»`.

**Round 2 — câu trả lời cuối cùng**

App gửi kết quả hàm trở lại cho model dưới dạng tin nhắn mới. Bây giờ model có đủ thông tin cần thiết và viết câu trả lời cuối cùng: *«Hiện tại ở Hà Nội là 15°C và có mây.»*

Một tin nhắn của người dùng đã tạo ra hai round LLM. Nếu model cần gọi thêm một công cụ nữa, sẽ có round thứ ba.

`RoundUsage` được phát ra sau mỗi round riêng lẻ và chỉ chứa số token của round đó. `Completion.Usage` được phát ra một lần khi tất cả xong và chứa tổng của tất cả các round.

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

## Ghi chú theo provider

Mỗi provider gắn usage vào stream chunk theo cách khác nhau. Mythosia.AI chuẩn hóa phần đó thành `RoundUsage` và `Completion.Usage`.

Gemini là trường hợp cần chú ý nhất: usage có thể nằm trên text hoặc status chunk, đôi khi còn đến sau function-call chunk. Thư viện sẽ đọc tiếp stream đủ lâu để lấy usage trước khi chuyển sang round tiếp theo.

Ở phía ứng dụng, nên đọc các sự kiện đã được chuẩn hóa là `RoundUsage` và `Completion.Usage`, thay vì tự parse metadata riêng của từng provider.
