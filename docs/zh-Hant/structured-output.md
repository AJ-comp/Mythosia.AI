# 結構化輸出

## 為什麼需要結構化輸出？

LLM 預設回傳自由格式文字。如果你的應用程式需要**以程式方式處理回應** — 存入資料庫、傳遞給另一個 API 或在型別化 UI 中呈現 — 你必須自己解析文字。

結構化輸出透過指示模型回傳與 C# 型別結構相符的 JSON 來解決這個問題。Mythosia.AI 自動處理 JSON Schema 生成、提示詞注入和反序列化 — 包括對模型可能產生的小格式錯誤的**自動 JSON 修復**。

## 它解決了什麼問題

```csharp
// ❌ 沒有結構化輸出 — 脆弱的手動解析
var text = await service.GetCompletionAsync("台北今天天氣怎麼樣？");
var tempMatch = Regex.Match(text, @"(\d+)°C");
int temp = tempMatch.Success ? int.Parse(tempMatch.Groups[1].Value) : 0;
```

**有了**結構化輸出：

```csharp
// ✅ 有結構化輸出 — 型別安全，自動處理
public record WeatherResponse(string City, string Condition, int TemperatureC);

var result = await service.GetCompletionAsync<WeatherResponse>(
    "台北今天天氣怎麼樣？");

Console.WriteLine(result.City);         // 台北
Console.WriteLine(result.Condition);    // 晴
Console.WriteLine(result.TemperatureC); // 22
```

## 基本用法

向 `GetCompletionAsync` 傳入型別參數：

```csharp
public record WeatherResponse(string City, string Condition, int TemperatureC);

var result = await service.GetCompletionAsync<WeatherResponse>(
    "台北今天天氣怎麼樣？");
```

## 集合型別

集合型別可以直接使用 — 無需包裝 DTO：

```csharp
public record Entity(string Name, string Type);

var entities = await service.GetCompletionAsync<List<Entity>>(
    "擷取這段文字中的所有人名和組織：...");

foreach (var e in entities)
    Console.WriteLine($"{e.Type}: {e.Name}");
```

## 串流輸出 + 結構化輸出

即時串流輸出文字，同時取得最終的反序列化物件：

```csharp
var run = service.BeginStream("生成一份產品摘要").As<ProductDto>();

await foreach (var chunk in run.Stream())
    Console.Write(chunk);

ProductDto product = await run.Result;
```

## 結構化輸出策略

控制模型生成結構化輸出的嚴格程度：

```csharp
using Mythosia.AI.Extensions;
using Mythosia.AI.Models;

service.WithStructuredOutputPolicy(StructuredOutputPolicy.Strict);
service.WithStructuredOutputPolicy(StructuredOutputPolicy.NoRetry);
```
