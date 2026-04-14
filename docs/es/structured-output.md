# Salida Estructurada

## ¿Por qué Salida Estructurada?

Los LLM devuelven texto libre por defecto. Si tu aplicación necesita **procesar la respuesta programáticamente** — almacenarla en una base de datos, pasarla a otra API o renderizarla en una UI tipada — tienes que analizar ese texto por tu cuenta. Esto lleva a verificaciones frágiles de regex o `string.Contains` que se rompen cuando el modelo cambia la formulación.

La salida estructurada resuelve esto instruyendo al modelo para que devuelva JSON que coincida con el esquema de un tipo C#. Mythosia.AI maneja la generación del esquema, la inyección del prompt y la deserialización automáticamente — incluyendo **reparación automática de JSON** para pequeños errores de formato.

## Básico

Pasa un parámetro de tipo a `GetCompletionAsync`:

```csharp
public record WeatherResponse(string City, string Condition, int TemperatureC);

var result = await service.GetCompletionAsync<WeatherResponse>(
    "¿Cómo está el tiempo en Madrid?");

Console.WriteLine(result.City);         // Madrid
Console.WriteLine(result.Condition);    // Soleado
Console.WriteLine(result.TemperatureC); // 22
```

## Colecciones

Los tipos de colección funcionan directamente — no se necesita DTO wrapper:

```csharp
public record Entity(string Name, string Type);

var entities = await service.GetCompletionAsync<List<Entity>>(
    "Extrae todas las personas y organizaciones de este texto: ...");

foreach (var e in entities)
    Console.WriteLine($"{e.Type}: {e.Name}");
```

## Streaming + Salida Estructurada

Transmite texto en tiempo real y también obtén el objeto deserializado final:

```csharp
var run = service.BeginStream("Genera un resumen del producto").As<ProductDto>();

// Salida en tiempo real
await foreach (var chunk in run.Stream())
    Console.Write(chunk);

// Resultado final analizado
ProductDto product = await run.Result;
```

## Política de Salida Estructurada

Controla cuán estrictamente se le pide al modelo que produzca salida estructurada:

```csharp
using Mythosia.AI.Models;

// Predeterminado: pide al modelo que devuelva JSON que coincida con el esquema
service.StructuredOutputPolicy = StructuredOutputPolicy.Strict;

// Leniente: permite más libertad al modelo, confía en la reparación automática
service.StructuredOutputPolicy = StructuredOutputPolicy.Lenient;
```
