# Saída Estruturada

## Por que Saída Estruturada?

LLMs retornam texto livre por padrão. Se sua aplicação precisa **processar a resposta programaticamente** — armazená-la em um banco de dados, passá-la para outra API ou renderizá-la em uma UI tipada — você tem que analisar esse texto por conta própria. Isso leva a verificações frágeis de regex ou `string.Contains` que quebram quando o modelo muda a formulação.

A saída estruturada resolve isso instruindo o modelo a retornar JSON correspondente ao schema de um tipo C#. O Mythosia.AI lida com a geração de schema, injeção de prompt e desserialização automaticamente — incluindo **reparo automático de JSON** para pequenos erros de formatação.

## Básico

Passe um parâmetro de tipo para `GetCompletionAsync`:

```csharp
public record WeatherResponse(string City, string Condition, int TemperatureC);

var result = await service.GetCompletionAsync<WeatherResponse>(
    "Como está o tempo em São Paulo?");

Console.WriteLine(result.City);         // São Paulo
Console.WriteLine(result.Condition);    // Ensolarado
Console.WriteLine(result.TemperatureC); // 28
```

## Coleções

Tipos de coleção funcionam diretamente — sem DTO wrapper necessário:

```csharp
public record Entity(string Name, string Type);

var entities = await service.GetCompletionAsync<List<Entity>>(
    "Extraia todas as pessoas e organizações deste texto: ...");

foreach (var e in entities)
    Console.WriteLine($"{e.Type}: {e.Name}");
```

## Streaming + Saída Estruturada

Transmita texto em tempo real e também obtenha o objeto desserializado final:

```csharp
var run = service.BeginStream("Gere um resumo do produto").As<ProductDto>();

// Saída em tempo real
await foreach (var chunk in run.Stream())
    Console.Write(chunk);

// Resultado final analisado
ProductDto product = await run.Result;
```

## Política de Saída Estruturada

Controle o rigor com que o modelo é solicitado a produzir saída estruturada:

```csharp
using Mythosia.AI.Models;

// Padrão: peça ao modelo para retornar JSON correspondente ao schema
service.StructuredOutputPolicy = StructuredOutputPolicy.Strict;

// Leniente: permita mais liberdade ao modelo, confie no reparo automático
service.StructuredOutputPolicy = StructuredOutputPolicy.Lenient;
```
