# Structured Output

## Why Structured Output?

LLMs return free-form text by default. If your application needs to **process the response programmatically** — store it in a database, pass it to another API, or render it in a typed UI — you have to parse that text yourself. This leads to fragile regex or `string.Contains` checks that break when the model changes phrasing.

Structured output solves this by instructing the model to return JSON matching a C# type's schema. Mythosia.AI handles the schema generation, prompt injection, and deserialization automatically — including **automatic JSON repair** for minor formatting errors the model may produce.

### When to Use

- Extracting entities, classifications, or structured data from unstructured text
- Building typed API responses from AI-generated content
- Feeding AI output into downstream pipelines that expect specific data shapes
- Any scenario where you need **reliable, machine-readable** output from the model

## The Problem It Solves

Suppose you need to extract weather data from the model's response. **Without** structured output:

```csharp
// ❌ Without structured output — fragile manual parsing
var text = await service.GetCompletionAsync("What's the weather in Seoul?");
// text = "The weather in Seoul is sunny with a temperature of 22°C."

// Now you have to parse this yourself...
var city = "Seoul"; // hardcoded? regex?
var tempMatch = Regex.Match(text, @"(\d+)°C");
int temp = tempMatch.Success ? int.Parse(tempMatch.Groups[1].Value) : 0;
// What if the model says "twenty-two degrees" instead of "22°C"? 💥
```

This breaks whenever the model changes phrasing. **With** structured output:

```csharp
// ✅ With structured output — type-safe, automatic
public record WeatherResponse(string City, string Condition, int TemperatureC);

var result = await service.GetCompletionAsync<WeatherResponse>(
    "What's the weather in Seoul?");

Console.WriteLine(result.City);         // Seoul
Console.WriteLine(result.Condition);    // Sunny
Console.WriteLine(result.TemperatureC); // 22
```

The model is instructed to return JSON matching your C# type. Mythosia.AI deserializes it automatically. If the model produces slightly malformed JSON (missing comma, trailing text), the built-in **auto-repair** fixes it before deserialization — no manual error handling needed.

## Basic

Pass a type parameter to `GetCompletionAsync`:

```csharp
public record WeatherResponse(string City, string Condition, int TemperatureC);

var result = await service.GetCompletionAsync<WeatherResponse>(
    "What's the weather in Seoul?");

Console.WriteLine(result.City);        // Seoul
Console.WriteLine(result.Condition);   // Sunny
Console.WriteLine(result.TemperatureC); // 22
```

## Collections

Collection types work directly — no wrapper DTO needed:

```csharp
public record Entity(string Name, string Type);

var entities = await service.GetCompletionAsync<List<Entity>>(
    "Extract all people and organizations from this text: ...");

foreach (var e in entities)
    Console.WriteLine($"{e.Type}: {e.Name}");
```

## Streaming + Structured Output

Stream text in real-time while also getting the final deserialized object:

```csharp
var run = service.BeginStream("Generate a product summary").As<ProductDto>();

// Real-time output
await foreach (var chunk in run.Stream())
    Console.Write(chunk);

// Final parsed result
ProductDto product = await run.Result;
```

## Structured Output Policy

Control how strictly the model is asked to produce structured output:

```csharp
using Mythosia.AI.Models;

// Default: ask the model to return JSON matching the schema
service.StructuredOutputPolicy = StructuredOutputPolicy.Strict;

// Lenient: allow the model more freedom, rely on auto-repair
service.StructuredOutputPolicy = StructuredOutputPolicy.Lenient;
```
