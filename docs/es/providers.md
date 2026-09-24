# Funcionalidades por Proveedor

> Los ejemplos con `CreateRequest` requieren Mythosia.AI 8.0.0 / Abstractions 4.0.0. La versión 7.1 que introdujo Run y las opciones comunes no incluye el builder. Los paquetes anteriores pueden usar las sobrecargas del servicio.

<a id="image-options-migration"></a>
Los modos y metadatos dependen del proveedor, modelo y API. Revise la [opción común de velocidad](request-building.md#inference-speed) y las capabilities; solicitar Fast no confirma que se haya aplicado.

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

`FunctionDefinition.AllowAsync = true` o `FunctionBuilder.WithAsync()` permite habilitar llamadas asíncronas para GPT-6 Astra / Sol / Luna mediante Responses. El valor predeterminado es `false`; los modelos no compatibles esperan el resultado del mismo manejador. Consulta ejemplos y el ciclo de vida de la solicitud en la [guía de llamadas a funciones](function-calling.md).

<a id="gpt-6-sol-luna"></a>

### GPT-6 Sol / Luna

Elige GPT-6 Sol para programación compleja, herramientas y tareas de agentes, o Luna para procesar grandes volúmenes de texto o imágenes con menor coste. Ambos usan las API existentes de respuesta completa, streaming y Run; cambiar de modelo no cambia el flujo de la aplicación.

> Requiere Mythosia.AI 8.1.0 / Abstractions 4.1.0. Se conservan las versiones mínimas de Astra y el modelo predeterminado del servicio.

Usa `AIModels.OpenAI.Gpt6Sol` (`gpt-6-sol`) o `AIModels.OpenAI.Gpt6Luna` (`gpt-6-luna`). Ambos admiten texto e imágenes de entrada y generan texto, con contexto de 1.050.000 tokens, entrada máxima de 922.000 y salida máxima de 128.000. La suma de entrada, razonamiento y salida debe respetar el contexto. `MaxTokens` configura el presupuesto de salida solicitado, no el tamaño del contexto.

```csharp
using Mythosia.AI.Models;
using Mythosia.AI.Services.OpenAI;

var service = new OpenAIService(apiKey, httpClient);
service.ChangeModel(AIModels.OpenAI.Gpt6Sol);
await using var run = await service.CreateRequest("Review this design.")
    .WithReasoning(ReasoningLevel.High)
    .StartRunAsync(onText: text => Console.Write(text));
var result = await run.Result;

service.ChangeModel(AIModels.OpenAI.Gpt6Luna);
string answer = await service.CreateRequest("Summarize this paragraph.")
    .WithReasoning(ReasoningLevel.None)
    .WithTemperature(0.2f)
    .GetCompletionAsync();
```

`Auto` equivale a `Medium`. Sol/Luna admiten `None`, `Low`, `Medium`, `High`, `XHigh` y `Max`; no admiten `Minimal`. Usa `WithReasoning(ReasoningLevel.None)` por solicitud o `Gpt6Reasoning.None` en `WithGpt6Parameters`. Solo Sol/Luna con `None` envían `Temperature` / `TopP`; se omiten con razonamiento activo. Astra siempre requiere razonamiento y omite el muestreo. `AIRequestProfile.DisableReasoning` selecciona `None` para Sol/Luna y `Low` en modo Standard para Astra, omitiendo los resúmenes del razonamiento.

`Gpt6ReasoningMode.Standard` y `.Pro` conservan el mismo ID de modelo. Los tres GPT-6 admiten herramientas mediante Responses, herramientas asíncronas opcionales, instrucciones durante un Run WebSocket y cambios de razonamiento que conservan la caché en modo Standard de un solo agente. Comprueba `run.CanSteer`; aceptar la instrucción no retira la salida anterior. `WithSpeed(InferenceSpeed.Fast)` solicita procesamiento Fast de pago sin cambiar el esfuerzo de razonamiento. Comprueba el modo aplicado en `result.Processing`; los permisos de cuenta y las reducciones de nivel del servidor son independientes del soporte local.

[GPT-6 Sol](https://developers.openai.com/api/docs/models/gpt-6-sol) · [GPT-6 Luna](https://developers.openai.com/api/docs/models/gpt-6-luna) · [Reasoning](https://developers.openai.com/api/docs/guides/reasoning) · [Fast](https://developers.openai.com/api/docs/guides/fast-mode)

### Nivel de Esfuerzo de Reasoning

GPT-6 Astra / Sol / Luna y GPT-5.1–5.6 permiten ajustar el esfuerzo de razonamiento para equilibrar velocidad y profundidad:

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

<a id="claude-opus-55"></a>

### Claude Opus 5.5: mostrar el progreso de tareas largas con herramientas

Use Opus 5.5 para revisar código o investigar documentos con varias rondas de herramientas. Se mantienen las API de completion y Run, pero el progreso se oculta por defecto y conservar el razonamiento exige cuidar el historial. Requiere Mythosia.AI 8.1.0 / Abstractions 4.1.0.

`ClaudeOpus5_5` selecciona `claude-opus-5-5`: entrada de texto/imágenes, salida de texto, contexto de 1M y salida máxima de 128K tokens. A fecha 2026-09-24, los precios estándar de entrada/salida son $4/$20 por millón de tokens; los modos especiales y las herramientas tienen tarifas propias. [Información oficial](https://platform.claude.com/docs/en/models/opus-5-5/overview).

Sin modificar el servicio, `Auto` usa esfuerzo `Medium` y omite el razonamiento legible. El razonamiento adaptativo siempre está activo. Elija `Low`, `Medium`, `High`, `XHigh` o `Max`; se rechazan los valores comunes `ReasoningLevel.None` y `Minimal`. El modelo predeterminado del servicio no cambia.

```csharp
using Mythosia.AI.Models;
using Mythosia.AI.Models.Streaming;
using Mythosia.AI.Services.Anthropic;

var claude = new AnthropicService(apiKey, httpClient);
claude.ChangeModel(AIModels.Anthropic.ClaudeOpus5_5);
claude.WithAdaptiveThinkingParameters(
    ClaudeReasoningEffort.Medium, ClaudeThinkingDisplay.Updates);

await using var run = await claude.StartRunAsync(
    "Review the migration plan using the registered tools.",
    options: StreamOptions.FullOptions);

await foreach (var item in run.StreamAsync())
{
    if (item.Type == StreamingContentType.Reasoning)
        Console.WriteLine(item.Content);
    else if (item.Type == StreamingContentType.Text)
        Console.Write(item.Content);
}
string answer = (await run.Result).Text;
```

El ejemplo solicita `Updates` y observa `StreamingContentType.Reasoning`. Use `Summarized` para resúmenes de razonamiento u `Omitted` para ocultarlos. El argumento de visualización de `WithAdaptiveThinkingParameters(effort)` sigue siendo `Summarized` por defecto, distinto del servicio sin configurar. Tras una completion normal, lea `LastThinkingContent`. No se garantizan intervalos regulares de progreso.

Un `ThinkingBudget` antiguo positivo se convierte en esfuerzo high/xhigh/max, no en un presupuesto exacto de tokens. Cero o valores negativos no desactivan el razonamiento. Un perfil que lo desactiva usa esfuerzo bajo y oculta su texto. `MaxTokens` incluye razonamiento oculto y respuesta; vuelva a evaluar límites y coste al migrar.

Mythosia conserva los bloques thinking firmados, incluso vacíos, entre turnos y resultados de herramientas. Continúe con el mismo servicio y chat; no reescriba mensajes anteriores, sistema o herramientas si espera conservar el razonamiento. `WithTurnInstruction`, `WithConversationInstruction` y `CachePreservation.Required` usan los controles existentes. `WithThinkingBinding` elige `Error` o `DropBlock`; `LastInputTransformations` informa de descartes. Drop descarta el razonamiento. La [guía del historial](fable-5-1.md) explica estos controles comunes; los valores por defecto y compatibilidad son los de Opus 5.5.

Deje `ForceFunctionName` sin asignar; se admiten la selección normal de herramientas y `FunctionsDisabled`. Se rechaza el prefill del asistente y se omiten parámetros de sampling. Opus 5.5 no lee thinking de Fable/Mythos; en Claude API, Fable 5.1 y Mythos 5.1 sí leen el de Opus 5.5. Cambiar de modelo puede perder razonamiento anterior. Esta incorporación no expone computer toolset nativo, task budgets, cambios de herramientas en la conversación, compactación nativa ni fallback automático del servidor. [Requisitos de migración](https://platform.claude.com/docs/en/models/opus-5-5/migration-guide) · [Funciones nativas](https://platform.claude.com/docs/en/models/opus-5-5/whats-new-opus-5-5).

En Opus 5.5, editar directamente el contenido de una respuesta assistant guardada produce `InvalidOperationException` antes de HTTP; `DropBlock` tampoco permite reescribir una respuesta firmada. Envíe la corrección como una nueva entrada del usuario o inicie otra conversación. Las modificaciones de contenido user/system anterior siguen la política de vinculación del prefijo del proveedor.

Opus 5.5 fast mode está disponible con [WithSpeed](request-building.md#inference-speed) en Claude API directa con los permisos necesarios. Conserva el esfuerzo seleccionado y solicita tarifa premium.

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

<a id="google-image-options"></a>

### Resoluciones y proporciones por modelo de imagen de Google

| Modelo | `Resolutions` | `AspectRatios` |
| --- | --- | --- |
| `gemini-3.1-flash-image` | `Auto`, `FiveTwelve` (512), `OneK` (1K), `TwoK` (2K), `FourK` (4K) | 14 + `Auto` |
| `gemini-3.1-flash-lite-image` | `Auto`, `OneK` (1K) | 14 + `Auto` |
| `gemini-3-pro-image` | `Auto`, `OneK` (1K), `TwoK` (2K), `FourK` (4K) | 10 + `Auto` |

Las 10 proporciones estándar son `1:1`, `2:3`, `3:2`, `3:4`, `4:3`, `4:5`, `5:4`, `9:16`, `16:9`, `21:9`. El conjunto de 14 añade `1:4`, `4:1`, `1:8`, `8:1`. Todos los modelos admiten también `ImageAspectRatio.Auto`.

Usa `ImageSize.Auto` o `ImageSize.Preset(resolution, aspectRatio)`. `Auto` omite el selector correspondiente. `GetImageCapabilities(model)` y `GenerateImagesAsync` / `EditImagesAsync` usan las mismas opciones por modelo. Los valores explícitos no admitidos lanzan `NotSupportedException` antes de HTTP; no se redimensiona ni se envía una solicitud alternativa. Los ID de modelos personalizados desconocidos conservan `Unknown` y se transmiten sin cambios tras validar las opciones generales del proveedor.

Para Flash-Lite, la [página del modelo](https://ai.google.dev/gemini-api/docs/models/gemini-3.1-flash-lite-image) y el texto de la guía indican 1K, pero la [tabla](https://ai.google.dev/gemini-api/docs/generate-content/image-generation#aspect_ratios_and_image_size) también incluye una columna de 512. Hasta verificar esta discrepancia, la biblioteca solo permite 1K por precaución; esto no afirma que se haya observado un rechazo de 512 por el servidor.

---

## xAI (XAIService)

<a id="grok-47"></a>

### Grok 4.7

Para pasar de un borrador rápido a una revisión exigente de código o documentos, selecciona Grok 4.7 y ajusta el esfuerzo por solicitud. Se mantienen las API de respuesta, streaming, Run, herramientas locales, salida estructurada e imágenes de entrada. `grok-4.7` recibe texto e imágenes, devuelve texto y tiene una ventana de contexto de 500.000 tokens. Grok 4.5 sigue siendo el modelo predeterminado. Requiere Mythosia.AI 8.1.0 / Abstractions 4.1.0.

```csharp
using Mythosia.AI.Extensions;
using Mythosia.AI.Models;
using Mythosia.AI.Services.xAI;

var grok = new XAIService(apiKey, httpClient);
grok.ChangeModel(AIModels.xAI.Grok4_7);
grok.WithGrokReasoning(GrokReasoning.Low);

var request = grok.CreateRequest("Review this deployment plan and its rollback risks.")
    .WithReasoning(ReasoningLevel.XHigh)
    .WithSpeed(InferenceSpeed.Fast);

await using var run = await request.StartRunAsync();
await foreach (var content in run.StreamAsync())
    Console.Write(content.Content);
var result = await run.Result;
Console.WriteLine(result.Text);
foreach (var processing in result.Processing)
    Console.WriteLine(processing.AppliedSpeed);
```

Admite `Low`, `Medium`, `High` y `XHigh`. El ajuste nativo `GrokReasoning.Auto` omite `reasoning_effort` y usa el valor predeterminado `High` del proveedor; el común `ReasoningLevel.Auto` también omite el campo y usa el valor predeterminado `High` del proveedor para esa solicitud. `None`, `Minimal` y `Max` se rechazan antes del envío. `WithReasoning(...)` se aplica a la solicitud lógica, incluidas las rondas de herramientas y las correcciones de salida estructurada; `WithGrokReasoning(...)` establece la base del servicio. El perfil interno `DisableReasoning` usa `Low`. Los resúmenes opcionales no son el razonamiento interno completo.

`WithSpeed(InferenceSpeed.Standard)` envía `service_tier: "default"`; `Fast` envía `"priority"` en los endpoints xAI compatibles y puede costar más. `ProviderDefault` no sobrescribe nada. Consulta el nivel declarado en `result.Processing`, pues el servidor puede reducirlo al procesamiento ordinario. Es procesamiento prioritario de `grok-4.7`, no la variante «Grok 4.7 Fast» exclusiva de Cursor/Grok Build, que no tiene ID de modelo de API pública.

`GetCapabilities()` describe localmente la solicitud seleccionada, sin comprobar los permisos de la cuenta. Esta integración usa Chat Completions. No conecta el razonamiento cifrado exclusivo de Responses, búsqueda web/X alojada, herramientas asíncronas nativas, cambios que conservan la caché ni `SteerAsync`. Las funciones del cliente usan el bucle local existente; `run.CanSteer` es false.

[Grok 4.7](https://docs.x.ai/developers/grok-4-7) · [reasoning_effort](https://docs.x.ai/developers/model-capabilities/text/reasoning) · [Priority Processing](https://docs.x.ai/developers/advanced-api-usage/priority-processing)

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

Google acepta `ImageSize.Auto` o `Preset` con resoluciones y proporciones según el modelo; consulta [Opciones de imagen por modelo de Google](#google-image-options). Formatos: `ImageOutputFormat.Auto` o `Jpeg`; rechaza `Png`/`WebP`. Google y xAI rechazan `Pixels`; OpenAI acepta `Auto`/`Pixels` y rechaza `Preset`. Consulta la [migración](#image-options-migration).

[Grok Imagine Image 2.0](https://docs.x.ai/developers/models/grok-imagine-image-2.0) · [Image API](https://docs.x.ai/developers/rest-api-reference/inference/images)

---

## DeepSeek (DeepSeekService)

Usa DeepSeek Flash para pasar de una respuesta rápida a una revisión profunda o interpretar gráficos y capturas. `AIModels.DeepSeek.Flash` (`deepseek-flash`) selecciona V4.1 Flash, publicado el 10 de septiembre de 2026 con visión nativa. Se conservan las API de completions, streaming, Run, funciones y RAG desde `Mythosia.AI` 8.0.0 / `Mythosia.AI.Abstractions` 4.0.0.

> Los paquetes publicados `Mythosia.AI` 8.0.0 / `Mythosia.AI.Abstractions` 4.0.0 ya incluyen el soporte básico de Flash. `AIModels.DeepSeek.V4Pro`, `UseResponsesApi`, Files API, `DeepSeekImageFileContent`: Requiere Mythosia.AI 8.1.0 / Abstractions 4.1.0. [v8.1.0](../../src/core/Mythosia.AI/RELEASE_NOTES.md#v810).

Para tareas solo de texto, selecciona `AIModels.DeepSeek.V4Pro` (`deepseek-v4-pro`, V4-Pro-0813). Flash sigue siendo el modelo predeterminado y admite imágenes; ambos ofrecen razonamiento Low/High/Max y el mismo límite de salida. Configura `UseResponsesApi = true` antes de crear la solicitud para usar Responses con las API existentes de completado, streaming, Run y funciones locales. El valor predeterminado sigue en `false` para conservar Chat Completions en aplicaciones existentes; se captura para toda la solicitud y sus rondas. Responses reenvía el historial completo de conversación y razonamiento nativo sin depender de IDs de respuesta almacenados.

```csharp
using Mythosia.AI.Extensions;
using Mythosia.AI.Models;
using Mythosia.AI.Services.DeepSeek;

var pro = new DeepSeekService(apiKey, AIModels.DeepSeek.V4Pro, httpClient)
{
    UseResponsesApi = true
};
pro.WithDeepSeekReasoning(DeepSeekReasoning.High);
string answer = await pro.CreateRequest("Review this deployment plan.").GetCompletionAsync();
```

Sube una imagen una vez para reutilizarla en varias preguntas o conversaciones. `UploadFileAsync` acepta una ruta o un stream del llamador con nombre de archivo; purpose es `user_data`. JPEG, PNG, GIF y WebP tienen un límite de 64 MiB. `DeepSeekImageFileContent` referencia la imagen en Flash con ambos transportes; no es entrada de PDF/documentos y V4 Pro la rechaza. Sin vencimiento se conserva permanentemente; `expiresAfterSeconds` admite 3600–2592000 segundos. Mantén el archivo hasta que terminen las conversaciones que lo usan.

```csharp
using Mythosia.AI.Models.Messages;

var vision = new DeepSeekService(apiKey, AIModels.DeepSeek.Flash, httpClient)
{
    UseResponsesApi = true
};
var file = await vision.UploadFileAsync("chart.png", expiresAfterSeconds: 3600);
var question = new Message(ActorRole.User, new List<MessageContent>
{
    new TextContent("Explain the trend in this chart."),
    new DeepSeekImageFileContent(file.Id)
});
string uploadedDescription = await vision.GetCompletionAsync(question);
var metadata = await vision.GetFileAsync(file.Id);
```

`GetFileAsync` consulta metadatos, `ListFilesAsync(new DeepSeekFileListOptions { After = lastId, Limit = 20, Order = DeepSeekFileOrder.Ascending })` obtiene una página y `DeleteFileAsync` elimina el archivo. Mientras `HasMore` sea true, usa `LastId` como siguiente `After`; también se admite `Descending`. No hay un endpoint documentado de descarga del contenido. Chat UI ofrece Flash y V4 Pro y reutiliza el catálogo actual para la reescritura; el antiguo valor guardado `DeepSeekChat` migra a Flash y los IDs arbitrarios se conservan.

[Responses](https://api-docs.deepseek.com/guides/responses_api/) · [Files](https://api-docs.deepseek.com/guides/files_api/) · [Models and limits](https://api-docs.deepseek.com/quick_start/pricing/)

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

Registra funciones locales con `WithFunction(...)` para consultar datos o actuar mediante tu código. Las herramientas funcionan con y sin razonamiento. Chat Completions rechaza la selección forzada/obligatoria durante el razonamiento; usa selección automática en ese transporte. Con `UseResponsesApi = true`, `ForceFunctionName` puede elegir una función incluso con razonamiento; el adaptador envía `type` y `name` directamente en `tool_choice` de Responses. Esto no activa herramientas asíncronas nativas. El adaptador conserva `reasoning_content` e IDs de llamadas para rondas posteriores. Run y streaming exponen `StreamingContentType.Reasoning` con `StreamOptions.WithReasoning()`; observar no activa el razonamiento. El uso incluye caché y razonamiento cuando el proveedor los informa. La recuperación automática del contexto usa el bucle común de streaming. Si las herramientas requieren el historial nativo de razonamiento, se bloquea la compactación para conservarlo y se comunica el error de desbordamiento.

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

`ImageContent` acepta bytes JPEG, PNG, GIF o WebP, o una URL HTTP(S) pública obtenida por el proveedor. El ejemplo usa un mensaje de usuario. La API actual también acepta imágenes en mensajes de herramientas, pero los handlers registrados siguen devolviendo texto mediante el contrato común. Un mensaje de imagen manual `ActorRole.Function` debe incluir el ID correspondiente en `MessageMetadataKeys.FunctionId` (`tool_call_id` enviado). Consulta los límites de tamaño y totales en la guía de visión actual. La generación de imágenes sigue sin estar disponible.

Ambos modelos ofrecen contexto de 1M y hasta 384K (`393216`) tokens de salida; el presupuesto predeterminado sigue en 8.000. El razonamiento omite temperature/penalty y usa `top_p` de al menos 0,95; sin razonamiento se omite `top_p`. Responses usa las API existentes de salida tipada para JSON schema nativo. No se admiten ejecución en segundo plano, `store`/`previous_response_id` del servidor, búsqueda alojada, `CachePreservation.Required`, herramientas asíncronas nativas, `SteerAsync` ni generación de imágenes. RAG local y rondas normales siguen disponibles.

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
