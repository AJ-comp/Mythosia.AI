# 開始使用

## 安裝

安裝核心套件：

```bash
dotnet add package Mythosia.AI
```

若需使用串流 LINQ 運算子（如 `ToListAsync`），還需安裝：

```bash
dotnet add package System.Linq.Async
```

## 第一次生成文字

選擇一個供應商，使用 API Key 和 `HttpClient` 建立服務實體：

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

呼叫 `GetCompletionAsync`：

```csharp
var response = await service.GetCompletionAsync("你好！");
Console.WriteLine(response);
```

## 選擇模型

每個服務都有合理的預設模型，也可以明確指定：

```csharp
var service = new OpenAIService("your-api-key", http)
{
    Model = AIModels.OpenAI.Gpt4_1
};
```

所有可用模型常數請參閱 [API 參考](../../api/Mythosia.AI.Models.AIModels.yml)。

## 後續步驟

- [文字生成](completions.md) — 系統提示詞、對話歷史、多模態
- [串流輸出](streaming.md) — 逐 Token 輸出與推理過程串流傳輸
- [函式呼叫](function-calling.md) — 讓模型呼叫你的程式碼
- [結構化輸出](structured-output.md) — 將回應反序列化為 C# 型別
