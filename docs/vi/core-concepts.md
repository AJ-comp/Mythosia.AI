# Khái niệm cốt lõi

Trang này tập hợp các khái niệm nền tảng được tham chiếu xuyên suốt phần còn lại của tài liệu. Các khái niệm khác sẽ được thêm vào theo thời gian.

## Round là gì?

> [!NOTE]
> **Round** là một chuyến đi khứ hồi hoàn chỉnh giữa ứng dụng của bạn và model — app gửi một prompt, model trả lời, và lần trao đổi đó là một round. Một tin nhắn chat thông thường là 1 round. Function calling và agent có thể nối tiếp nhiều round cho một tin nhắn duy nhất của người dùng.

### Trường hợp đơn giản nhất: 1 round

Với một tin nhắn chat thông thường, toàn bộ cuộc trò chuyện diễn ra trong một round.

```
app  →  "2 cộng 2 bằng mấy?"   →  model
app  ←  "Bằng 4."               ←  model
```

`RoundUsage` phát một lần với số token của round này. `Completion.Usage` phát ở cuối stream với cùng tổng số, vì chỉ có một round.

### Nhiều round: function calling

Round nhân lên khi model không thể tự trả lời. Giả sử người dùng hỏi *«Thời tiết ở Hà Nội hiện tại thế nào?»* — model không có quyền truy cập thời tiết thời gian thực, nên nó phải gọi tool.

**Round 1 — model quyết định gọi tool**

App gửi tin nhắn của người dùng cùng với danh sách các tool đã đăng ký (ví dụ `GetWeather`) cho model. Model nhìn thấy cuộc trò chuyện này:

```
system: Bạn là một weather assistant. Bạn có thể gọi GetWeather(city).
user:   Thời tiết ở Hà Nội hiện tại thế nào?
```

Thay vì viết câu trả lời cuối cùng, model trả về một **yêu cầu gọi tool**:

```
tool_call: GetWeather(city="Hanoi")
```

Lượt của model kết thúc, và round 1 cũng kết thúc. `RoundUsage` phát với số token tiêu thụ trong round 1. **Vẫn chưa có câu trả lời cuối cùng cho người dùng.**

**Giữa các round — app chạy hàm**

Bước này **không phải** là lời gọi LLM. Runtime của Mythosia.AI gọi phần cài đặt `GetWeather` mà bạn đã đăng ký và nhận được `«15°C, có mây»`. Không tiêu thụ token.

**Round 2 — model viết câu trả lời cuối cùng**

App thêm **function_call mà model đưa ra ở Round 1 cùng với kết quả của tool** vào cuộc trò chuyện và gọi model **lần thứ hai**. Bây giờ model nhìn thấy:

```
system:      Bạn là một weather assistant. Bạn có thể gọi GetWeather(city).
user:        Thời tiết ở Hà Nội hiện tại thế nào?
assistant:   [đã gọi GetWeather(city="Hanoi")]
tool_result: 15°C, có mây
```

Với thông tin cần thiết đã có, model viết văn bản:

```
Hiện tại ở Hà Nội là 15°C và có mây.
```

Round 2 kết thúc. `RoundUsage` phát lần thứ hai — lần này chỉ chứa token của round 2 (input thường lớn hơn round 1 vì cuộc trò chuyện đã dài hơn). Khi stream đóng lại, `Completion.Usage` phát một lần với **tổng của round 1 và round 2**.

### Nhìn nhanh

| Bước | Gọi LLM? | Điều gì xảy ra | Sự kiện |
|---|---|---|---|
| Round 1 | ✅ | Model quyết định gọi `GetWeather` | `RoundUsage` (`RoundIndex=1`) |
| Giữa các round | ❌ | App chạy hàm, nhận được `«15°C, có mây»` | `FunctionCall`, `FunctionResult` |
| Round 2 | ✅ | Model nhìn thấy kết quả và viết câu trả lời cuối | `RoundUsage` (`RoundIndex=2`, `IsFinalRound=true`) |
| Stream kết thúc | — | — | `Completion` (Usage = round 1 + round 2) |

### Nhiều tool nghĩa là nhiều round

Nếu model cần gọi liên tiếp nhiều tool, số round sẽ cộng dồn. Với *«So sánh thời tiết ở Hà Nội và Thành phố Hồ Chí Minh»*:

1. **Round 1** — model gọi `GetWeather("Hanoi")`
2. App thực thi → `«15°C, có mây»`
3. **Round 2** — model nhìn thấy kết quả và cũng gọi `GetWeather("Ho Chi Minh City")`
4. App thực thi → `«30°C, nắng»`
5. **Round 3** — model kết hợp cả hai kết quả vào câu trả lời cuối cùng

Tổng cộng ba round, và `Completion.Usage` cộng tất cả ba lại. Thanh đo ngữ cảnh trên UI nên dùng `RoundUsage.Usage.InputTokens` của round cuối cùng — trong ví dụ này là round 3.

Để xem ví dụ bằng số về cách context meter thay đổi qua từng round, hãy xem [Token Usage — Context size thay đổi như thế nào](token-usage.md#how-context-size-changes).
