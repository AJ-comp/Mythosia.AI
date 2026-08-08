# เริ่มต้นอย่างรวดเร็ว

## การติดตั้ง

ติดตั้ง package หลัก:

```bash
dotnet add package Mythosia.AI
```

หากต้องการใช้ streaming กับ LINQ operator (เช่น `ToListAsync`) ให้ติดตั้งเพิ่ม:

```bash
dotnet add package System.Linq.Async
```

## Completion แรกของคุณ

เลือก provider และสร้าง service instance พร้อม API key และ `HttpClient`:

```csharp
using Mythosia.AI;

var http = new HttpClient();

// OpenAI
var service = new OpenAIService("your-openai-api-key", http);

// Anthropic
// var service = new AnthropicService("your-anthropic-api-key", http);

// Google
// var service = new GoogleAIService("your-google-api-key", http);
```

จากนั้นเรียก `GetCompletionAsync`:

```csharp
var response = await service.GetCompletionAsync("สวัสดี!");
Console.WriteLine(response);
```

## การเลือก Model

แต่ละ service มี model เริ่มต้นที่เหมาะสม แต่คุณสามารถระบุได้เองดังนี้:

```csharp
var service = new OpenAIService("your-api-key", http)
{
    Model = AIModels.OpenAI.Gpt4_1
};
```

ดู [API Reference](../../api/Mythosia.AI.Models.AIModels.yml) สำหรับ model constant ทั้งหมด

## ขั้นตอนต่อไป

- [การสร้างข้อความพื้นฐาน](completions.md) — system prompt, ประวัติการสนทนา, multimodal
- [Streaming](streaming.md) — รับ output ทีละ token และ reasoning streaming
- [Function Calling](function-calling.md) — ให้ model เรียกใช้โค้ดของคุณ
- [Structured Output](structured-output.md) — แปลง response เป็น C# type
