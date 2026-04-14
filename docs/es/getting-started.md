# Primeros Pasos

## Instalación

Instala el paquete principal:

```bash
dotnet add package Mythosia.AI
```

Si planeas usar streaming con operadores LINQ (p. ej., `ToListAsync`), agrega también:

```bash
dotnet add package System.Linq.Async
```

## Tu Primera Completion

Elige un proveedor y crea una instancia de servicio con tu clave API y un `HttpClient`:

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

Luego llama a `GetCompletionAsync`:

```csharp
var response = await service.GetCompletionAsync("¡Hola!");
Console.WriteLine(response);
```

## Elegir un Modelo

Cada servicio usa un modelo predeterminado razonable, pero puedes especificar uno explícitamente:

```csharp
var service = new OpenAIService("your-api-key", http)
{
    Model = AIModels.OpenAI.Gpt4_1
};
```

Consulta la [Referencia de API](../api/Mythosia.AI.Models.AIModels.yml) para ver todas las constantes de modelos disponibles.

## Próximos Pasos

- [Completions Básicas](completions.md) — prompts de sistema, historial de conversación, multimodal
- [Streaming](streaming.md) — salida token a token y streaming de reasoning
- [Llamada de Funciones](function-calling.md) — permite que el modelo llame tu código
- [Salida Estructurada](structured-output.md) — deserializa respuestas en tipos C#
