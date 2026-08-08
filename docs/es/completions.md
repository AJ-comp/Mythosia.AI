# Completions Básicas

## Turno Único

El uso más sencillo — envía un mensaje, recibe una respuesta:

```csharp
var response = await service.GetCompletionAsync("¿Cuál es la capital de España?");
Console.WriteLine(response); // Madrid
```

## Prompt del Sistema

Define un prompt de sistema para darle al modelo una persona o instrucciones:

```csharp
service.SystemMessage = "Eres un asistente conciso. Responde en una sola oración.";

var response = await service.GetCompletionAsync("Explica la recursión.");
```

## Conversación Multi-turno

Los mensajes se acumulan automáticamente. Cada llamada a `GetCompletionAsync` se añade al historial de conversación:

```csharp
await service.GetCompletionAsync("Mi nombre es Carlos.");
var response = await service.GetCompletionAsync("¿Cuál es mi nombre?");
// → "Tu nombre es Carlos."
```

Para limpiar el historial de conversación:

```csharp
service.ActivateChat.ClearMessages();
```

## Construir Mensajes Manualmente

Usa `MessageBuilder` para construir mensajes de forma explícita:

```csharp
using Mythosia.AI.Builders;

var message = MessageBuilder.Create().AddText("Resume este texto: ...")
    .Build();

var response = await service.GetCompletionAsync(message);
```

## Multimodal (Entrada de Imagen)

Los proveedores que admiten visión aceptan contenido de imagen junto con texto:

```csharp
var imageBytes = await File.ReadAllBytesAsync("diagrama.png");

var message = MessageBuilder.Create().AddText("¿Qué muestra este diagrama?")
    .AddImage(imageBytes, "image/png")
    .Build();

var response = await service.GetCompletionAsync(message);
```

## Consulta Rápida (API Estática)

Para consultas puntuales sin construir una instancia de servicio, usa el `QuickAskAsync` estático. El proveedor se detecta automáticamente por el nombre del modelo:

```csharp
string answer = await AIService.QuickAskAsync(
    apiKey: "sk-...",
    prompt: "¿Cuál es la capital de España?",
    model: AIModels.OpenAI.Gpt4oMini
);
```

Variante con imagen:

```csharp
string description = await AIService.QuickAskWithImageAsync(
    apiKey: "sk-...",
    prompt: "Describe esta imagen",
    imagePath: "foto.jpg",
    model: AIModels.OpenAI.Gpt4_1
);
```

## Métodos de Conveniencia para Imágenes

Analiza imágenes sin `MessageBuilder` — el servicio lee el archivo y resuelve el tipo MIME automáticamente:

```csharp
// Desde ruta de archivo
var response = await service.GetCompletionWithImageAsync(
    "¿Qué muestra este diagrama?", "diagrama.png");

// Desde URL
var response = await service.GetCompletionWithImageUrlAsync(
    "Describe esta foto", "https://example.com/foto.jpg");
```

## Reenviar Último Mensaje

Elimina la última respuesta del asistente y reenvía el último mensaje del usuario:

```csharp
string regenerated = await service.RetryLastMessageAsync();
```

Útil cuando la respuesta anterior no fue satisfactoria y quieres que el modelo lo intente de nuevo.

## Conteo de Tokens

Estima el uso de tokens antes de enviar una solicitud. Disponible en **todos los proveedores**:

```csharp
// Tokens para el historial de conversación actual
uint conversationTokens = await service.GetInputTokenCountAsync();

// Tokens para un prompt específico
uint promptTokens = await service.GetInputTokenCountAsync("Tu prompt aquí");
```

## Cadena de Mensajes Fluente

`BeginMessage()` ofrece una API fluente para construir y enviar mensajes en una sola cadena:

```csharp
// Texto + imagen → enviar
string response = await service.BeginMessage()
    .AddText("¿Qué muestra este diagrama?")
    .AddImage("diagrama.png")
    .SendAsync();

// Consulta única (sin historial de conversación)
string answer = await service.BeginMessage()
    .AddText("Traduce al español")
    .SendOnceAsync();

// Streaming
await service.BeginMessage()
    .AddText("Escribe un poema sobre la primavera")
    .StreamAsync(chunk => Console.Write(chunk));
```

## Controlar Longitud de Salida y Temperatura

```csharp
service.MaxTokens = 512;
service.Temperature = 0.2f;  // menor = más determinista
```
