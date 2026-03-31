# Structured Output

Structured output lets you deserialize the model's response directly into a C# type. Mythosia.AI includes automatic JSON repair, so minor formatting errors from the model are handled transparently.

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
