# Completions Básicas

Para ajustes independientes y variantes reutilizables, use el [builder de solicitudes](request-building.md). Llame a `CreateRequest(...)` antes de `With...`. Las propiedades y métodos fluent del servicio conservan su comportamiento.

Si la aplicación solo necesita la respuesta terminada, `GetCompletionAsync` sigue siendo adecuado. Para mostrar el progreso, cancelar o añadir instrucciones durante el trabajo en modelos compatibles, consulta la [guía de Run](execution-api-transition.md).

<a id="completion-cancellation"></a>

## Cancelar una respuesta que ya no se necesita

Si el usuario cierra la pantalla, pulsa Detener o vence el tiempo de espera de la aplicación, la respuesta puede dejar de ser útil. Pase un `CancellationToken` para detener la comunicación y el trabajo del cliente, y evitar herramientas y llamadas posteriores al modelo. `GetCompletionAsync` sigue siendo apropiado para la respuesta final; cancelar no requiere un Run.

### Before: sin señal de cancelación del llamador

```csharp
string answer = await service.CreateRequest("Resume este documento.")
    .GetCompletionAsync();
```

### After: cancelar por acción del usuario o tras 30 segundos

```csharp
using System;
using System.Threading;

using var cancellation = new CancellationTokenSource(TimeSpan.FromSeconds(30));
try
{
    string answer = await service.CreateRequest("Resume este documento.")
        .GetCompletionAsync(cancellationToken: cancellation.Token);
    Console.WriteLine(answer);
}
catch (OperationCanceledException) when (cancellation.IsCancellationRequested)
{
    Console.WriteLine("Cancelado.");
}
```

Conserve la fuente durante la llamada y conecte Detener o el cierre a `cancellation.Cancel()`. El ejemplo también programa la cancelación tras 30 segundos. El llamador recibe `OperationCanceledException` después de la limpieza. Un plazo con `CancellationTokenSource` también es cancelación del llamador; `FunctionCallingPolicy.TimeoutSeconds` mantiene su comportamiento de error por tiempo agotado.

Las sobrecargas del servicio para texto y `Message`, respuestas tipadas, el builder y `MessageChain.SendAsync` / `SendOnceAsync` aceptan el token. Las llamadas que lo omiten siguen funcionando. Entradas alternativas:

```csharp
using System.Collections.Generic;
using System.Threading;
using Mythosia.AI.Extensions;

using var cancellation = new CancellationTokenSource();
CancellationToken token = cancellation.Token;

string answer = await service.GetCompletionAsync(
    "Resume este documento.", cancellationToken: token);

Dictionary<string, string> data = await service.GetCompletionAsync<Dictionary<string, string>>(
    "Devuelve el título y autor como JSON.", cancellationToken: token);

string messageAnswer = await service.BeginMessage().AddText("Resume este documento.")
    .SendAsync(cancellationToken: token);

string oneOffAnswer = await service.BeginMessage().AddText("Traduce esta frase.")
    .SendOnceAsync(cancellationToken: token);
```

El token llega a la preparación, envío y lectura HTTP, herramientas locales cooperativas y rondas posteriores. Al detectar la cancelación se omiten herramientas pendientes y rondas futuras. La limpieza mantiene emparejadas las llamadas registradas y sus resultados; una herramienta iniciada que ignore el token puede retrasarla. No se deshacen acciones completadas ni se borra el historial. Consulte el [contrato de herramientas](function-calling.md#tool-execution-contract).

No se garantiza detener la inferencia o facturación del proveedor. OpenAI documenta cerrar la conexión para Responses ordinarias; Google especifica cancelación solo del cliente y facturación del uso aplicable. [OpenAI Responses](https://developers.openai.com/api/docs/guides/background#limits) · [Google GenerateContentConfig.abortSignal](https://googleapis.github.io/js-genai/release_docs/interfaces/types.GenerateContentConfig.html#abortsignal). Un trabajo en segundo plano requiere su `CancelAsync()` explícito; cancelar `WaitForCompletionAsync(cancellationToken: ...)` solo deja de esperar. La llamada normal no se convierte en ejecución en segundo plano. Consulte [Perplexity](perplexity.md).

<a id="completion-cancellation-migration"></a>

Esta adición pertenece a Mythosia.AI 8.0.0. Las llamadas sin token y los argumentos posicionales profile/context siguen siendo válidos en el código fuente, pero debe recompilarse. Las implementaciones propias de `IAIService` deben añadir `CancellationToken cancellationToken = default` al final de ambas firmas y propagarlo. Los proveedores derivados de `AIService` conservan el override `GetCompletionAsync(Message)` y transmiten el `RequestCancellationToken` protegido al transporte. El builder y Run por sí solos no requerían este cambio de interfaz. Las subclases que redefinen sobrecargas public virtual modificadas para respuestas string/profile/context, auxiliares de imagen o `RunAgentAsync` también deben añadir y transmitir el nuevo `CancellationToken`; solo el override del proveedor que recibe un único `Message` conserva su firma. Los delegados vinculados directamente a firmas modificadas pueden necesitar una lambda explícita que transmita u omita el token.

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

Para analizar gráficos y capturas, llamar funciones locales o revisar una respuesta rápida, usa [DeepSeek Flash](providers.md#deepseek-deepseekservice) (`AIModels.DeepSeek.Flash`, V4.1 Flash). El razonamiento sigue desactivado por defecto; actívalo con `WithDeepSeekReasoning(...)` o `WithReasoning(...)` por solicitud.

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

Perplexity: [Responder con un ajuste Agent / Fuentes, imágenes y respuestas estructuradas](perplexity.md).
