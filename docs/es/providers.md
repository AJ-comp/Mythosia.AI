# Funcionalidades por Proveedor

> Los ejemplos con `CreateRequest` requieren Mythosia.AI 8.0.0 / Abstractions 4.0.0. La versión 7.1 que introdujo Run y las opciones comunes no incluye el builder. Los paquetes anteriores pueden usar las sobrecargas del servicio.

<a id="image-options-migration"></a>
## Migración a opciones de imagen tipadas

Elige calidad y formato con enums y autocompletado, y distingue píxeles exactos de niveles de resolución. Así se evitan errores de escritura y conversiones silenciosas a otro tamaño.

Cambio incompatible para Mythosia.AI 8.0.0: `Quality`, `Background` y `OutputFormat` pasan a ser enums; `Size` es `ImageSize`; se elimina la propiedad independiente `AspectRatio`. El valor predeterminado de `OutputFormat` ahora es `ImageOutputFormat.Auto`. Los métodos de generación y edición siguen iguales.

**Before**

```text
var request = new ImageGenerationRequest
{
    Prompt = "A glass pavilion at sunrise",
    Quality = "max",
    Background = "transparent",
    OutputFormat = "png",
    Size = "1536x1024"
};

// Google / xAI
Size = "2K";
AspectRatio = "3:2";
```

**After**

```csharp
using Mythosia.AI.Models;
using Mythosia.AI.Models.Images;

var request = new ImageGenerationRequest
{
    Model = AIModels.OpenAI.GptImage2_5Sunburst,
    Prompt = "A glass pavilion at sunrise",
    Quality = ImageQuality.Max,
    Background = ImageBackground.Transparent,
    OutputFormat = ImageOutputFormat.Png,
    Size = ImageSize.Pixels(1536, 1024)
};

// Google / xAI
var presetRequest = new ImageGenerationRequest
{
    Prompt = "A glass pavilion at sunrise",
    Size = ImageSize.Preset(ImageResolution.TwoK, ImageAspectRatio.ThreeByTwo),
    OutputFormat = ImageOutputFormat.Auto
};
```

`Pixels(width, height)` solicita dimensiones exactas. `Preset(resolution, aspectRatio)` indica nivel y proporción; el proveedor decide los píxeles reales. Usa `ImageSize.Auto` sin restricciones. Migra de píxeles a presets solo si tu aplicación acepta dimensiones aproximadas.

| Provider | `Size` | `OutputFormat` |
| --- | --- | --- |
| OpenAI | `ImageSize.Auto`, `Pixels(...)` | `Auto` → PNG, `Png`, `Jpeg`, `WebP` |
| Google | `ImageSize.Auto`, `Preset(...)` | `Auto`, `Jpeg` |
| xAI | `ImageSize.Auto`, `Preset(...)` | `Auto` |

Los valores enum no definidos y las combinaciones no compatibles se rechazan antes de HTTP. No todos los modelos admiten cada valor. Google solo acepta `ImageQuality.Auto`; xAI acepta `Auto`, `Low` y `Medium`.

Puede reutilizar los búferes de entrada cuando `EditImagesAsync` devuelva su `Task`. La solicitud iniciada conserva sus propios datos de imagen, incluidos los bytes de la máscara de OpenAI; los cambios posteriores en los arrays originales `ImageInput.Data` no alteran la carga.

Para evitar guardar una salida interrumpida como imagen terminada, la generación y edición de Google exigen que todos los candidatos devueltos terminen con `finishReason: STOP`. Si alguno está bloqueado, incompleto o carece de ese estado final, la llamada completa lanza `AIServiceException`. Los datos base64 integrados o los metadatos MIME de imagen ausentes o incorrectos también hacen fallar toda la llamada; no se presupone PNG. Estas comprobaciones no verifican que los bytes del archivo coincidan con el formato de imagen declarado.

## OpenAI (OpenAIService)

> La compatibilidad con GPT-6 Astra y las llamadas asíncronas a herramientas están disponibles desde `Mythosia.AI` 7.1.0, con tipos compartidos en `Mythosia.AI.Abstractions` 3.1.0.

Una consulta lenta no tiene por qué detener toda la respuesta. Mientras se cargan los datos del tiempo, por ejemplo, el modelo puede explicar consejos generales de viaje que no dependen del resultado.

`FunctionDefinition.AllowAsync = true` o `FunctionBuilder.WithAsync()` permite habilitar llamadas asíncronas para GPT-6 Astra mediante Responses. El valor predeterminado es `false`; los modelos no compatibles esperan el resultado del mismo manejador. Consulta ejemplos y el ciclo de vida de la solicitud en la [guía de llamadas a funciones](function-calling.md).

### Nivel de Esfuerzo de Reasoning

GPT-6 Astra y GPT-5.1–5.6 permiten ajustar el esfuerzo de razonamiento para equilibrar velocidad y profundidad:

```csharp
using Mythosia.AI.Models;

// GPT-6 Astra
service.ChangeModel(AIModels.OpenAI.Gpt6Astra);
service.WithGpt6Parameters(
    reasoningEffort: Gpt6Reasoning.Medium, // Auto, Low, Medium, High, XHigh, Max
    verbosity: Verbosity.Medium,
    reasoningSummary: ReasoningSummary.Auto,
    reasoningMode: Gpt6ReasoningMode.Standard);

// GPT-5.6: Sol es el modelo insignia; Terra y Luna son opciones más económicas.
service.ChangeModel(AIModels.OpenAI.Gpt5_6Sol);
service.WithGpt5_6Parameters(
    reasoningEffort: Gpt5_6Reasoning.Medium, // None, Low, Medium, High, XHigh, Max
    verbosity: Verbosity.Medium);            // Low, Medium, High

// Serie GPT-5.4
service.ChangeModel(AIModels.OpenAI.Gpt5_4);
service.Gpt5_4ReasoningEffort = Gpt5_4Reasoning.High; // None, Low, Medium, High, XHigh

```

GPT-6 Astra usa la API Responses de forma predeterminada; las llamadas a funciones la requieren. `Auto` equivale al valor predeterminado de la biblioteca, `Medium`; `None` y `Minimal` no están disponibles. `AIRequestProfile.DisableReasoning = true` usa `Low` en modo `Standard` y omite el resumen del razonamiento. Selecciona `Gpt6ReasoningMode.Pro` para ejecutar el modo Pro con el mismo ID de modelo `gpt-6-astra`.

Para tareas compartidas entre proveedores, use [razonamiento y búsqueda nativa](reasoning-and-search.md); los ajustes específicos que se muestran a continuación siguen disponibles.

### Texto a Voz

```csharp
byte[] audio = await service.GetSpeechAsync(
    inputText: "¡Hola, mundo!",
    voice: "alloy",
    model: "tts-1"
);

await File.WriteAllBytesAsync("output.mp3", audio);
```

### Voz a Texto (Transcripción)

```csharp
byte[] audioData = await File.ReadAllBytesAsync("grabacion.mp3");

string transcript = await service.TranscribeAudioAsync(
    audioData: audioData,
    fileName: "grabacion.mp3",
    language: "es"  // opcional, ISO-639-1
);
```

`TranscribeAudioAsync` usa `gpt-transcribe`; su firma pública no cambia.

### Generación de Imágenes

#### GPT Image 2.5

Elige Flare para crear propuestas visuales rápidamente, o Sunburst cuando una revisión deba seguir instrucciones de edición precisas. Ambos generan y editan imágenes mediante el `IImageGenerationService` existente; elegir un modelo de imagen no cambia el de chat.

| Modelo | Cuándo elegirlo |
| --- | --- |
| `AIModels.OpenAI.GptImage2_5Flare` | Generación cotidiana de imágenes rápida y de alta calidad. |
| `AIModels.OpenAI.GptImage2_5Sunburst` | Generación y edición donde prima la precisión de los cambios. |

Indica `ImageGenerationRequest.Model`, o su propiedad heredada en `ImageEditRequest`. El valor predeterminado de OpenAI sigue siendo `AIModels.OpenAI.GptImage2`. Los alias son `gpt-image-2.5-flare` y `gpt-image-2.5-sunburst`; para fijar las versiones del 8 de septiembre de 2026, usa `GptImage2_5Flare_260908` o `GptImage2_5Sunburst_260908` (sus ID terminan en `-2026-09-08`).

Crea un borrador con Flare:

```csharp
using Mythosia.AI.Models;
using Mythosia.AI.Models.Images;
using Mythosia.AI.Services;
using Mythosia.AI.Services.OpenAI;

IImageGenerationService images = new OpenAIService(apiKey, httpClient);
var generated = await images.GenerateImagesAsync(new ImageGenerationRequest
{
    Model = AIModels.OpenAI.GptImage2_5Flare,
    Prompt = "Un pabellón de cristal al amanecer, ilustración conceptual de arquitectura",
    Size = ImageSize.Pixels(1024, 1024),
    Quality = ImageQuality.Low,
    OutputFormat = ImageOutputFormat.Png
});

await File.WriteAllBytesAsync("pavilion.png", generated.Images[0].Data);
```

Después edita la imagen generada con Sunburst:

```csharp
var edited = await images.EditImagesAsync(new ImageEditRequest
{
    Model = AIModels.OpenAI.GptImage2_5Sunburst,
    Prompt = "Conserva el diseño del pabellón, elimina el entorno y deja el fondo transparente.",
    InputImages = new[]
    {
        new ImageInput(generated.Images[0].Data, "image/png", "pavilion.png")
    },
    Size = ImageSize.Pixels(1024, 1024),
    Quality = ImageQuality.XHigh,
    Background = ImageBackground.Transparent,
    OutputFormat = ImageOutputFormat.Png
});

await File.WriteAllBytesAsync("pavilion-cutout.png", edited.Images[0].Data);
```

En estos modelos y sus versiones fijadas, `Quality` admite `Auto`, `Low`, `Medium`, `High`, `XHigh` y `Max`. Usa calidad baja para borradores y compara niveles superiores para las piezas finales. `OutputFormat` admite `Auto` / `Png`, `Jpeg` o `WebP`; `OutputCompression` va de 0 a 100 solo para JPEG/WebP. El fondo `Transparent` requiere PNG/WebP. `Count` va de 1 a 10.

`Size` usa `ImageSize.Auto` o `ImageSize.Pixels(width, height)`: dimensiones múltiplos de 16, proporción 1:3–3:1, máximo 3840 píxeles por lado y área 655360–8294400 píxeles. Los tamaños superiores a 2560×1440 son experimentales. OpenAI rechaza `Preset`.

La edición acepta 1–16 referencias JPEG/PNG/WebP no vacías, cada una de menos de 50 MiB. La máscara opcional debe ser PNG/WebP, de menos de 50 MiB, con el formato y dimensiones de la primera referencia y un canal alfa. La biblioteca comprueba MIME y longitud en bytes; el proveedor valida dimensiones y alfa.

Estos ejemplos usan las rutas existentes de Image API: retorno de bytes y edición multipart. Esta integración no expone herramientas Responses `image_generation`, streaming de imágenes parciales ni `input_fidelity`. Lee `GeneratedImage.Data` y `MediaType` del resultado.

Consulta la [guía oficial de imágenes](https://developers.openai.com/api/docs/guides/image-generation) y las páginas de [Sunburst](https://developers.openai.com/api/docs/models/gpt-image-2.5-sunburst) y [Flare](https://developers.openai.com/api/docs/models/gpt-image-2.5-flare).

---

## Anthropic (AnthropicService)

[Claude Fable 5.1](fable-5-1.md) añade actualizaciones de progreso, instrucciones por turno y diagnósticos de vinculación del pensamiento desde `Mythosia.AI` 8.0.0 / `Mythosia.AI.Abstractions` 4.0.0. Mythos 5.1 requiere invitación. Ambos rechazan la selección forzada de herramientas.

### Conteo de Tokens (API Nativa)

La implementación de Anthropic llama al endpoint oficial `messages/count_tokens`, devolviendo conteos **exactos** de tokens:

```csharp
uint tokens = await service.GetInputTokenCountAsync("Tu prompt aquí");
uint total = await service.GetInputTokenCountAsync();
```

---

## Google (GoogleAIService)

Para revisar documentos extensos y realizar tareas con varias rondas de herramientas, puedes elegir Gemini 3.7 Flash o 3.8 Flash mediante el adaptador de Google existente. Se admiten desde `Mythosia.AI` 8.0.0 / `Mythosia.AI.Abstractions` 4.0.0; Gemini 3.6 Flash sigue siendo el modelo predeterminado.

### Nivel de Razonamiento

```csharp
using Mythosia.AI.Extensions;
using Mythosia.AI.Models;
using Mythosia.AI.Models.Enums;
using Mythosia.AI.Services.Google;

var gemini = new GoogleAIService(apiKey, httpClient);
gemini.ChangeModel(AIModels.Google.Gemini3_8Flash);
// Gemini 3.7: AIModels.Google.Gemini3_7Flash
gemini.ThinkingLevel = GeminiThinkingLevel.Low;

string review = await gemini
    .CreateRequest("Compara los despliegues progresivos y blue-green, incluidos los riesgos de reversión.")
    .WithReasoning(ReasoningLevel.High)
    .GetCompletionAsync();
```

Usa `Low` para una primera revisión ligera y `High` para un análisis exigente; más razonamiento puede aumentar la latencia y los tokens. Ambos modelos aceptan `Low`, `Medium` y `High`, pero no `Minimal` ni `None`. `GeminiThinkingLevel.Auto` omite la configuración; el valor del proveedor para 3.8 es `Medium`. `ThinkingLevel` establece la base del servicio y `WithReasoning(...)` la sustituye para una solicitud lógica. El adaptador omite `temperature`, `topP` y `topK` en ambos modelos. Los límites del proveedor son 1.048.576 tokens de entrada y 65.536 de salida. [Gemini 3.7 Flash](https://ai.google.dev/gemini-api/docs/models/gemini-3.7-flash), [Gemini 3.8 Flash](https://ai.google.dev/gemini-api/docs/models/gemini-3.8-flash).

---

## xAI (XAIService)

### Elegir el esfuerzo según la tarea

Usa un esfuerzo menor para un primer borrador rápido y más razonamiento para comprobaciones difíciles donde la calidad importe más que el tiempo de respuesta. Selecciona Grok 4.6 explícitamente para usar el nivel adicional `XHigh`.

```csharp
using Mythosia.AI.Extensions;
using Mythosia.AI.Models;
using Mythosia.AI.Services.xAI;

var grok = new XAIService(apiKey, httpClient);
grok.ChangeModel(AIModels.xAI.Grok4_6);
grok.WithGrokReasoning(GrokReasoning.Low);

string review = await grok
    .CreateRequest("Compara los despliegues gradual y azul-verde, incluida la recuperación ante fallos.")
    .WithReasoning(ReasoningLevel.XHigh)
    .GetCompletionAsync();
```

Grok 4.6 admite `Low`, `Medium`, `High` y `XHigh` (`GrokReasoning.XHigh`). `Auto` omite `reasoning_effort` y conserva el valor predeterminado `High` del proveedor; `None` no puede desactivar el razonamiento. Un esfuerzo mayor puede aumentar la latencia y el uso de tokens. Por compatibilidad, `XAIService` sigue usando Grok 4.5 de forma predeterminada. La versión 4.5 admite de `Low` a `High`, y la 4.3 de `None` a `High`; el adaptador rechaza `XHigh` en esos modelos anteriores antes del envío.

`WithGrokReasoning(...)` y el método existente `WithGrokParameters(...)` establecen la configuración base del servicio. En Grok 4.6, el método común `WithReasoning(...)` la sustituye para una sola solicitud lógica, incluidas sus rondas de herramientas y reparaciones de salida estructurada, y después la restaura. Los perfiles internos `DisableReasoning` usan `Low` en este modelo que siempre razona. Las actualizaciones que conservan la caché y la búsqueda web/de archivos alojada no están integradas para xAI mediante estas opciones comunes.

Grok 4.6 puede devolver resúmenes de razonamiento del proveedor como `StreamingContentType.Reasoning` cuando la observación activa `new StreamOptions().WithReasoning()`. Los resúmenes son opcionales y no representan el razonamiento interno completo. Las mismas opciones funcionan con Run; cambiar la visualización del flujo no cambia el esfuerzo solicitado.

[Grok 4.6](https://docs.x.ai/developers/grok-4-6) · [reasoning_effort](https://docs.x.ai/developers/model-capabilities/text/reasoning)

### Grok Imagine Image 2.0

Usa la generación para convertir una descripción de producto en un borrador visual, o la edición para combinar el sujeto y el fondo de fotos de referencia. `XAIService` ofrece ambas operaciones mediante el mismo `IImageGenerationService` que OpenAI y Google, desde `Mythosia.AI` 8.0.0 / `Mythosia.AI.Abstractions` 4.0.0.

El modelo de imagen predeterminado e independiente es `AIModels.xAI.GrokImagineImage2_0` (`grok-imagine-image-2.0`). Las solicitudes de imagen no cambian el modelo de chat ni se añaden a su conversación.

```csharp
using Mythosia.AI.Models;
using Mythosia.AI.Models.Images;
using Mythosia.AI.Services;
using Mythosia.AI.Services.xAI;

IImageGenerationService images = new XAIService(apiKey, httpClient);
var generated = await images.GenerateImagesAsync(new ImageGenerationRequest
{
    Model = AIModels.xAI.GrokImagineImage2_0,
    Prompt = "Un pabellón de cristal al amanecer, composición panorámica",
    Size = ImageSize.Preset(ImageResolution.OneK, ImageAspectRatio.SixteenByNine),
    OutputFormat = ImageOutputFormat.Auto
});

var image = generated.Images[0];
var extension = image.MediaType switch
{
    "image/jpeg" => ".jpg",
    "image/png" => ".png",
    "image/webp" => ".webp",
    _ => throw new NotSupportedException(image.MediaType)
};
await File.WriteAllBytesAsync("pavilion" + extension, image.Data);
```

Para editar, pasa los bytes de las imágenes en el orden al que se refiere el prompt:

```csharp
var edited = await images.EditImagesAsync(new ImageEditRequest
{
    Prompt = "Coloca el sujeto de la imagen 1 en la escena de la imagen 2.",
    InputImages = new[]
    {
        new ImageInput(await File.ReadAllBytesAsync("subject.png"), "image/png", "subject.png"),
        new ImageInput(await File.ReadAllBytesAsync("scene.jpg"), "image/jpeg", "scene.jpg")
    },
    Size = ImageSize.Preset(ImageResolution.OneK),
    OutputFormat = ImageOutputFormat.Auto
});

var imageEdited = edited.Images[0];
var extensionEdited = imageEdited.MediaType switch
{
    "image/jpeg" => ".jpg",
    "image/png" => ".png",
    "image/webp" => ".webp",
    _ => throw new NotSupportedException(imageEdited.MediaType)
};
await File.WriteAllBytesAsync("combined" + extensionEdited, imageEdited.Data);
```

Cada `GeneratedImage.Data` contiene los bytes decodificados; elige la extensión según `MediaType`. El adaptador solicita una respuesta base64 integrada y no descarga URL de imágenes alojadas por el proveedor. `Count` acepta de 1 a 10 resultados; la edición admite de 1 a 5 referencias JPEG, PNG o WebP.

Para xAI, usa `ImageSize.Auto` o `ImageSize.Preset(ImageResolution.OneK, ImageAspectRatio.SixteenByNine)`. Resoluciones: `Auto`, `OneK`, `TwoK`, con proporciones compatibles con el modelo. Se rechaza `Pixels(...)` porque no se pueden solicitar dimensiones exactas.

xAI solo admite el nuevo valor predeterminado común `ImageOutputFormat.Auto`. No permite elegir códec; `Jpeg`, `Png` y `WebP` explícitos se rechazan antes de enviar. Elige la extensión según `GeneratedImage.MediaType`; la biblioteca no transcodifica. Calidad: `ImageQuality.Auto`, `Low`, `Medium`; fondo: solo `ImageBackground.Auto`. No admite compresión explícita ni `Mask` independiente.

Google acepta `ImageSize.Auto` o `Preset` con `ImageResolution.Auto`, `FiveTwelve`, `OneK`, `TwoK`, `FourK`, según el modelo. Formatos: `ImageOutputFormat.Auto` o `Jpeg`; rechaza `Png`/`WebP`. Google y xAI rechazan `Pixels`; OpenAI acepta `Auto`/`Pixels` y rechaza `Preset`. Consulta la [migración](#image-options-migration).

[Grok Imagine Image 2.0](https://docs.x.ai/developers/models/grok-imagine-image-2.0) · [Image API](https://docs.x.ai/developers/rest-api-reference/inference/images)

---

## DeepSeek (DeepSeekService)

Usa DeepSeek Flash para pasar de una respuesta rápida a una revisión profunda o interpretar gráficos y capturas. `AIModels.DeepSeek.Flash` (`deepseek-flash`) selecciona V4.1 Flash, publicado el 10 de septiembre de 2026 con visión nativa. Se conservan las API de completions, streaming, Run, funciones y RAG desde `Mythosia.AI` 8.0.0 / `Mythosia.AI.Abstractions` 4.0.0.

```csharp
using Mythosia.AI.Extensions;
using Mythosia.AI.Models;
using Mythosia.AI.Models.Streaming;
using Mythosia.AI.Services.DeepSeek;

var deepseek = new DeepSeekService(apiKey, httpClient);
deepseek.ChangeModel(AIModels.DeepSeek.Flash);
deepseek.WithDeepSeekReasoning(DeepSeekReasoning.Low);

string draft = await deepseek.GetCompletionAsync("Compara despliegues progresivos y blue-green, incluidos los riesgos de reversión.");

await using var run = await deepseek
    .CreateRequest("Revisa las suposiciones de esa comparación.")
    .WithReasoning(ReasoningLevel.High)
    .StartRunAsync(
        options: new StreamOptions().WithReasoning());
await foreach (var item in run.StreamAsync())
{
    if (item.Type == StreamingContentType.Reasoning)
        Console.Write(item.Content);
}
string review = (await run.Result).Text;
```

`ThinkingEnabled` sigue en `false` por defecto. `WithDeepSeekReasoning(...)` activa el razonamiento y fija `ReasoningEffort` persistente (`Auto`, `Low`, `High`, `Max`); su `Auto` omite el esfuerzo y usa el valor del proveedor `High`. El `WithReasoning(...)` común afecta una solicitud lógica y sus rondas: `None` desactiva, `Minimal`/`Low` se asigna a `Low`, `Medium`/`High`/`XHigh` a `High`, y `Max` a `Max`. El `Auto` común mantiene la configuración. Más esfuerzo puede aumentar latencia y tokens. Cambiar solo `ReasoningEffort` no activa el razonamiento.

Registra funciones locales con `WithFunction(...)` para consultar datos o actuar mediante tu código. Las herramientas funcionan con y sin razonamiento; con razonamiento se rechaza la selección forzada/obligatoria, por lo que debes usar selección automática. El adaptador conserva `reasoning_content` e IDs de llamadas para rondas posteriores. Run y streaming exponen `StreamingContentType.Reasoning` con `StreamOptions.WithReasoning()`; observar no activa el razonamiento. El uso incluye caché y razonamiento cuando el proveedor los informa. La recuperación automática del contexto usa el bucle común de streaming. Si las herramientas requieren el historial nativo de razonamiento, se bloquea la compactación para conservarlo y se comunica el error de desbordamiento.

Envía gráficos o capturas con los tipos de mensaje existentes:

```csharp
using Mythosia.AI.Models.Messages;

var message = new Message(ActorRole.User, new List<MessageContent>
{
    new TextContent("Explica la tendencia del gráfico e identifica etiquetas poco claras."),
    new ImageContent(await File.ReadAllBytesAsync("chart.png"), "image/png")
});
string description = await deepseek.GetCompletionAsync(message);
```

`ImageContent` acepta bytes JPEG, PNG, GIF o WebP, o una URL HTTP(S) pública obtenida por el proveedor. El ejemplo usa un mensaje de usuario. La API actual también acepta imágenes en mensajes de herramientas, pero los handlers registrados siguen devolviendo texto mediante el contrato común. Un mensaje de imagen manual `ActorRole.Function` debe incluir el ID correspondiente en `MessageMetadataKeys.FunctionId` (`tool_call_id` enviado). Consulta los límites de tamaño y totales en la guía de visión actual. No se integran `file_id`, Files API ni generación de imágenes.

El proveedor anuncia 1M de contexto y hasta 384K (`393216`) tokens de salida; el presupuesto predeterminado sigue en 8.000. En razonamiento se omiten temperature/penalty y `top_p` es al menos 0,95; sin razonamiento se omite `top_p`. El adaptador usa Chat Completions. No integra Responses, búsqueda alojada, `CachePreservation.Required`, herramientas asíncronas nativas ni `SteerAsync`. RAG local y rondas normales siguen disponibles.

`V4Flash`, `Chat` y `Reasoner` siguen como constantes obsolete con advertencia e IDs originales. El proveedor redirige temporalmente el alias retirado `deepseek-v4-flash` a V4.1 Flash; la biblioteca no reescribe la constante. Elige `Flash` en código nuevo. `UseReasonerModel()` selecciona Flash con razonamiento `High`.

[DeepSeek V4.1 Flash](https://api-docs.deepseek.com/updates/) · [Thinking](https://api-docs.deepseek.com/guides/thinking_mode/) · [Vision](https://api-docs.deepseek.com/guides/vision/) · [Tools](https://api-docs.deepseek.com/guides/tool_calls/) · [Limits](https://api-docs.deepseek.com/quick_start/pricing/)

---

## Perplexity (PerplexityService)

Use Perplexity cuando la respuesta necesite información reciente y fuentes que el lector pueda comprobar. `PerplexityService` llama a la Agent API; la búsqueda y los embeddings independientes permiten crear la recuperación de documentos con el modelo de respuesta que prefiera.

```csharp
using Mythosia.AI.Models;
using Mythosia.AI.Services.Perplexity;

var service = new PerplexityService(apiKey, httpClient);
service.ChangeModel(AIModels.Perplexity.Sonar);

await using var run = await service.StartRunAsync("Compara los métodos recientes de reciclaje de baterías y cita las fuentes.");
Console.WriteLine((await run.Result).Text);
foreach (var source in run.Citations)
    Console.WriteLine(source.Url);
```

La [guía de Perplexity](perplexity.md) explica los ajustes de investigación, las funciones locales, las herramientas alojadas y las tareas largas en segundo plano. Se conservan las API habituales de completion, streaming, Run y citas.

Esta versión cambia el servicio a `/v1/agent`. `AIModels.Perplexity.Sonar` ahora selecciona `perplexity/sonar`. El proveedor anunció el cierre de los endpoints Sonar anteriores para el 27 de septiembre de 2026; las integraciones existentes deben migrarse. [Perplexity](https://community.perplexity.ai/t/sonar-is-moving-to-the-agent-api/5802)

---

## Alibaba / Qwen (QwenService)

Instala el paquete separado:

```bash
dotnet add package Mythosia.AI.Providers.Alibaba
```

```csharp
using Mythosia.AI.Providers.Alibaba;

var service = new QwenService(apiKey, http)
{
    Model = AlibabaModels.QwenMax
};
```

Modelos disponibles: `QwenMax`, `QwenPlus`, `QwenTurbo`, `Qwen3` y variantes.

[Crear opciones de modelo con definiciones compartidas](model-capabilities.md).
