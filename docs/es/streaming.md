# Streaming

## Streaming Básico

Usa `StreamAsync` para recibir tokens a medida que se generan:

```csharp
await foreach (var token in service.StreamAsync("Cuéntame una historia"))
{
    Console.Write(token);
}
```

## Streaming con Tipo de Contenido

`StreamAsync` puede devolver objetos `StreamingContent` que llevan tanto el texto como su tipo:

```csharp
await foreach (var content in service.StreamAsync("Explica la computación cuántica", StreamOptions.Default))
{
    Console.Write(content.Content);
}
```

## Streaming con Reasoning

Todos los proveedores con capacidad de reasoning (OpenAI, Claude, Gemini, Grok, DeepSeek) comparten el mismo patrón. Pasa `StreamOptions` con reasoning habilitado:

```csharp
using Mythosia.AI.Models.Streaming;

await foreach (var content in service.StreamAsync("Resuelve: 2x + 5 = 13", new StreamOptions().WithReasoning()))
{
    if (content.Type == StreamingContentType.Reasoning)
        Console.Write($"[Pensando] {content.Content}");
    else if (content.Type == StreamingContentType.Text)
        Console.Write(content.Content);
}
```

## Streaming con Salida Estructurada

Transmite texto en tiempo real y obtén un objeto deserializado al terminar:

```csharp
var run = service.BeginStream(prompt).As<MyDto>();

// Transmite tokens a la UI conforme llegan
await foreach (var chunk in run.Stream())
    Console.Write(chunk);

// Obtiene el resultado completamente analizado tras el streaming
MyDto result = await run.Result;
```

## Uso de Tokens

Al completar el streaming, el evento final `Completion` lleva un objeto `TokenUsage` con métricas detalladas:

```csharp
await foreach (var content in service.StreamAsync("Explica la computación cuántica", StreamOptions.Default))
{
    if (content.Type == StreamingContentType.Text)
        Console.Write(content.Content);

    if (content.Type == StreamingContentType.Completion && content.Usage != null)
    {
        Console.WriteLine($"\nTokens de entrada:  {content.Usage.InputTokens}");
        Console.WriteLine($"Tokens de salida: {content.Usage.OutputTokens}");
        Console.WriteLine($"Total de tokens:  {content.Usage.TotalTokens}");
    }
}
```

### Propiedades de TokenUsage

| Propiedad | Descripción |
|---|---|
| `InputTokens` | Tokens en el input/prompt |
| `OutputTokens` | Tokens en el output/completion |
| `TotalTokens` | Input + Output |
| `CachedInputTokens` | Tokens servidos desde caché (costo reducido) |
| `CacheCreationTokens` | Tokens escritos en caché (Anthropic) |
| `ReasoningTokens` | Tokens usados para reasoning interno |
| `CacheHitRatio` | Tasa de acierto de caché (0.0–1.0) |
| `VisibleOutputTokens` | Tokens de salida excluyendo reasoning |

## Preajustes de StreamOptions

```csharp
// Completo — metadatos, llamadas de función, reasoning
await foreach (var c in service.StreamAsync("prompt", StreamOptions.FullOptions))
    Console.Write(c.Content);

// Mínimo — solo texto, sin metadatos
await foreach (var c in service.StreamAsync("prompt", StreamOptions.Minimal))
    Console.Write(c.Content);
```

Constructor fluente para combinaciones personalizadas:

```csharp
var options = new StreamOptions()
    .WithReasoning()       // incluye chain-of-thought
    .WithMetadata()        // incluye info del modelo en Completion
    .WithFunctionCalls();  // habilita llamada de funciones durante el stream
```

## Streaming Sin Estado (StreamOnceAsync)

Transmite una respuesta sin afectar el historial de conversación:

```csharp
await foreach (var chunk in service.StreamOnceAsync("Traduce al español"))
    Console.Write(chunk);
```

## Resumen de Conversación Antes del Streaming

La política de resumen automático no se activa durante el streaming. Llama a `ApplySummaryPolicyIfNeededAsync` explícitamente antes de `StreamAsync`:

```csharp
await service.ApplySummaryPolicyIfNeededAsync();

await foreach (var chunk in service.StreamAsync("Continúa nuestra conversación...", StreamOptions.Default))
    Console.Write(chunk.Content);
```
