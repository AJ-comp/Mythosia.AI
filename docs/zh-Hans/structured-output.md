# 结构化输出

## 为什么需要结构化输出？

LLM 默认返回自由格式文本。如果你的应用需要**以编程方式处理响应** — 存入数据库、传递给另一个 API 或在类型化 UI 中渲染 — 你必须自己解析文本。这会导致脆弱的正则或 `string.Contains` 检查，一旦模型措辞改变就会失效。

结构化输出通过指示模型返回与 C# 类型结构匹配的 JSON 来解决这个问题。Mythosia.AI 自动处理 JSON Schema 生成、提示词注入和反序列化 — 包括对模型可能产生的小格式错误的**自动 JSON 修复**。

### 适用场景

- 从非结构化文本中提取实体、分类或结构化数据
- 基于 AI 生成内容构建类型化 API 响应
- 将 AI 输出传入需要特定数据结构的下游管道
- 任何需要模型输出**可靠、机器可读**结果的场景

## 它解决了什么问题

假设你需要从模型响应中提取天气数据。**没有**结构化输出时：

```csharp
// ❌ 没有结构化输出 — 脆弱的手动解析
var text = await service.GetCompletionAsync("北京今天天气怎么样？");
// text = "北京今天天气晴朗，气温 22°C。"

// 你得自己解析...
var city = "北京"; // 硬编码？正则？
var tempMatch = Regex.Match(text, @"(\d+)°C");
int temp = tempMatch.Success ? int.Parse(tempMatch.Groups[1].Value) : 0;
// 如果模型说"二十二度"而不是"22°C"呢？💥
```

只要模型措辞一变就会出错。**有了**结构化输出：

```csharp
// ✅ 有结构化输出 — 类型安全，自动处理
public record WeatherResponse(string City, string Condition, int TemperatureC);

var result = await service.GetCompletionAsync<WeatherResponse>(
    "北京今天天气怎么样？");

Console.WriteLine(result.City);         // 北京
Console.WriteLine(result.Condition);    // 晴
Console.WriteLine(result.TemperatureC); // 22
```

模型被指示返回与你 C# 类型匹配的 JSON。Mythosia.AI 自动反序列化。即使模型生成的 JSON 略有格式问题（缺少逗号、尾部多余文本），内置的**自动修复**也会在反序列化前修正 — 无需手动错误处理。

## 基本用法

向 `GetCompletionAsync` 传入类型参数：

```csharp
public record WeatherResponse(string City, string Condition, int TemperatureC);

var result = await service.GetCompletionAsync<WeatherResponse>(
    "北京今天天气怎么样？");

Console.WriteLine(result.City);        // 北京
Console.WriteLine(result.Condition);   // 晴
Console.WriteLine(result.TemperatureC); // 22
```

## 集合类型

集合类型可以直接使用 — 无需包装 DTO：

```csharp
public record Entity(string Name, string Type);

var entities = await service.GetCompletionAsync<List<Entity>>(
    "提取这段文字中的所有人名和组织：...");

foreach (var e in entities)
    Console.WriteLine($"{e.Type}: {e.Name}");
```

## 流式输出 + 结构化输出

实时流式输出文本，同时获取最终的反序列化对象：

```csharp
var run = service.BeginStream("生成一份产品摘要").As<ProductDto>();

// 实时输出
await foreach (var chunk in run.Stream())
    Console.Write(chunk);

// 最终解析结果
ProductDto product = await run.Result;
```

## 结构化输出策略

控制模型生成结构化输出的严格程度：

```csharp
using Mythosia.AI.Extensions;
using Mythosia.AI.Models;

// Strict：最多允许三次自动修复
service.WithStructuredOutputPolicy(StructuredOutputPolicy.Strict);

// NoRetry：不重试修复，直接返回首次验证错误
service.WithStructuredOutputPolicy(StructuredOutputPolicy.NoRetry);
```
