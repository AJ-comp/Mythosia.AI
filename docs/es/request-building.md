# Mantener independientes los ajustes de cada solicitud

> Grok 4.7: Requiere Mythosia.AI 8.1.0 / Abstractions 4.1.0. [selección del modelo, razonamiento y velocidad](providers.md#grok-47)

Un resumen puede necesitar una temperatura baja y un borrador creativo una más alta. Preparar el borrador no debe cambiar un resumen ya preparado. Use `CreateRequest` para configurar cada llamada o crear variantes a partir de una solicitud base.

Para obtener respuesta, uso y fuentes juntos, `await run.Result` devuelve una instantánea `AIRunResult`. La cadena está en `result.Text`, sin leer el flujo. Es un cambio de Mythosia.AI 8.0.0; `GetCompletionAsync` y `StructuredStreamRun<T>.Result` mantienen sus tipos de retorno. [Resultado Run y migración](execution-api-transition.md#run-result).

Para una respuesta final con botón Detener, pase `cancellationToken` a `GetCompletionAsync`. Use Run para eventos de progreso o instrucciones adicionales compatibles. Consulte [cancelación](completions.md#completion-cancellation).

> Los ejemplos con `CreateRequest` requieren Mythosia.AI 8.0.0 / Abstractions 4.0.0. La versión 7.1 que introdujo Run y las opciones comunes no incluye el builder. Los paquetes anteriores pueden usar las sobrecargas del servicio.

## Before: el servicio se comparte

El `WithTemperature` existente del servicio lo modifica y devuelve la misma instancia. Las dos variables siguientes la comparten: el último valor se aplica a ambas. Estos métodos siguen disponibles para configurar los valores predeterminados del servicio.

```csharp
var summary = service.WithTemperature(0.2f);
var creative = service.WithTemperature(0.8f);

bool same = ReferenceEquals(summary, creative); // true
string answer = await summary.GetCompletionAsync("Explica este documento."); // 0.8
```

## After: variantes independientes

`CreateRequest` captura los valores predeterminados. Cada `With...` del builder devuelve otro builder sin modificar el original. La ejecución utiliza los ajustes capturados sin sobrescribir temporalmente los del servicio.

```csharp
using Mythosia.AI.Builders;

AIRequestBuilder basis = service.CreateRequest("Explica este documento.");
AIRequestBuilder summary = basis.WithTemperature(0.2f);
AIRequestBuilder creative = basis.WithTemperature(0.8f);

bool same = ReferenceEquals(summary, creative); // false
string answer = await summary.GetCompletionAsync();
// Usa 0.2; creative y los valores del servicio no cambian.
```

Use el builder devuelto. Descartar el resultado de `basis.WithTemperature(0.2f);` deja `basis` sin cambios.

El builder valida sin ajustar silenciosamente: temperatura 0–2, TopP 0–1 y penalizaciones −2–2; rechaza NaN e infinito. Los límites de tokens, rondas, concurrencia y tiempo especificado deben ser positivos. Los valores inválidos lanzan `ArgumentException` / `ArgumentOutOfRangeException`. El helper de temperatura antiguo conserva su ajuste al rango.

## Responsabilidad de cada objeto

`AIService` mantiene la conexión al proveedor, los valores predeterminados y la conversación. El tipo público `Mythosia.AI.Builders.AIRequestBuilder` ofrece la API fluent. El tipo interno `AIRequest` lleva la entrada y los ajustes fijados a la ejecución. No necesita llamar a `Build()`: `GetCompletionAsync()` devuelve `Task<string>` y `StartRunAsync()` devuelve `Task<AIRun>`, no un `AIRequest` como respuesta.

```text
AIService.CreateRequest(input)
    → AIRequestBuilder.With...()
    → GetCompletionAsync() / StartRunAsync()
    → AIRequest
    → provider
    → string / AIRun
```

## Iniciar un Run con los mismos ajustes

Use `GetCompletionAsync()` para recibir la respuesta completa o `StartRunAsync()` para mostrar progreso o enviar instrucciones durante una ejecución compatible. El prompt se pasa a `CreateRequest`, no otra vez al método de ejecución. `run.StreamAsync()` observa ese Run; las condiciones de soporte de `run.SteerAsync(...)` no cambian.

```csharp
await using var run = await service
    .CreateRequest("Explica este documento.")
    .WithMaxTokens(2048)
    .WithMaxRounds(10)
    .StartRunAsync(
        onText: text => Console.Write(text),
        cancellationToken: cancellationToken);

string answer = (await run.Result).Text;
```

Las herramientas locales pueden devolver objetos mediante `Task<T>` / `ValueTask<T>` y recibir un `CancellationToken` inyectado. `run.Cancel()` o el token inicial llega a las herramientas cooperativas; detener solo el lector no. Las excepciones son errores. Al cancelar se omiten las llamadas pendientes y la limpieza espera las herramientas iniciadas que ignoran el token. Consulta [resultados, errores y cancelación](function-calling.md#tool-execution-contract).

[Run](execution-api-transition.md) · [Streaming](streaming.md)

## Reutilizar perfiles y contexto

`WithProfile` copia un `AIRequestProfile` y `WithContext` copia un `AIRequestContext`. Cambiar después los objetos originales no altera la solicitud preparada. El builder permite ajustar muestreo, instrucciones del sistema, modo sin estado, políticas de funciones y razonamiento y búsquedas compatibles. Las validaciones del proveedor siguen aplicándose.

`WithFunctions(params FunctionDefinition[])` añade definiciones copiadas. `WithFunctions(toolInstance)` y `WithStaticFunctions<T>()` de `Mythosia.AI.Extensions` admiten funciones existentes con atributos. Registre antes de `CreateRequest` para el servicio, después para la solicitud. `CreateRequest` captura y consume las opciones antiguas pendientes para la siguiente llamada; reutilice el builder para conservarlas.

```csharp
var request = service
    .CreateRequest("Reformula esta pregunta para buscar.")
    .WithProfile(RequestProfiles.QueryRewrite)
    .WithContext(new AIRequestContext
    {
        SystemMessageSuffix = "\nConserva el significado original."
    });

string rewritten = await request.GetCompletionAsync();
```

[AIRequestProfile](request-profiles.md) · [AIRequestContext](request-contexts.md) · [WithReasoning / WithWebSearch / WithFileSearch](reasoning-and-search.md)

## Qué se copia y qué sigue compartido

Los valores comunes y del proveedor se capturan al llamar a `CreateRequest`. Cambiarlos después no modifica la solicitud preparada. Se copian el contenido integrado de mensajes, las colecciones de opciones compatibles, perfiles, contextos y políticas. Los handlers, callbacks de contexto dinámico y contenidos personalizados conservan sus referencias. No modifique contenido personalizado; los delegates pueden leer estado externo. El contexto dinámico se evalúa durante la ejecución.

Tras la captura, puede liberar el `JsonDocument` original o modificar los valores `JsonNode` originales sin cambiar el JSON guardado en los metadatos de la solicitud o los argumentos de llamadas a funciones; cada ejecución recibe su propia copia. Una cadena `Items` cíclica o de más de 64 niveles en el esquema de una herramienta provoca una `ArgumentException` durante la captura (`CreateRequest` o `WithFunctions`), para que un esquema inválido falle antes de la ejecución sin agotar la pila del proceso.

La copia también conserva las dimensiones y los índices iniciales de los arrays, así como las reglas de comparación de claves de los contenedores estándar `Dictionary<,>`, `SortedDictionary<,>` y `SortedList<,>`. Una búsqueda de clave que no distingue mayúsculas y minúsculas sigue funcionando igual en la solicitud. El valor vacío `default(JsonElement)` (`Undefined`) se conserva. Los objetos de metadatos personalizados desconocidos mantienen sus referencias; su propietario debe evitar modificarlos o coordinar el acceso.

Los valores estándar `ReadOnlyCollection<T>` y `ReadOnlyDictionary<TKey, TValue>` mantienen su tipo dentro de arrays y diccionarios tipados. Las colecciones subyacentes admitidas se copian conservando las vistas de solo lectura, las referencias compartidas y los ciclos. `Hashtable` y `SortedList` no genérico también conservan sus reglas de comparación de claves.

Un builder no es otra conversación. Utiliza la conversación activa del servicio al ejecutar; crearlo no congela el historial. Las llamadas con estado siguen actualizando el historial compartido. `WithStatelessMode()` evita leerlo y acumularlo. Se mantiene el límite de un Run activo por servicio. La independencia de ajustes no garantiza ejecución paralela en el mismo servicio; use servicios separados para conversaciones simultáneas independientes.

## Llamadas existentes y extensiones

`GetCompletionAsync` y las entradas existentes siguen disponibles. `BeginMessage()` / `MessageChain` conservan la construcción mutable de mensajes, pero ejecutan por la nueva ruta de solicitudes. Use `CreateRequest` para reutilizar variantes. La API pertenece a `AIService` y sus implementaciones; no se agregan miembros obligatorios a `IAIService`. Los consumidores de la abstracción o de un wrapper RAG mantienen las API existentes de perfiles, contexto y ejecución.

[Crear opciones de modelo con definiciones compartidas](model-capabilities.md).

<a id="inference-speed"></a>

## Elegir la velocidad de procesamiento según la tarea

Una respuesta que el usuario espera en pantalla puede justificar procesamiento de pago y baja latencia; un informe en segundo plano puede usar el ordinario. `WithSpeed` elige el modo conservando modelo y esfuerzo de razonamiento. Requiere Mythosia.AI 8.1.0 / Abstractions 4.1.0.

`ProviderDefault` no sobrescribe nada y conserva ajustes del servicio/proveedor; el proyecto ya podría usar Fast por defecto. `Standard` solicita procesamiento ordinario explícitamente. `Fast` solicita el modo premium de baja latencia y puede generar costes adicionales. Guarde el builder devuelto: las tres ramas son independientes y la base no cambia.

```csharp
using Mythosia.AI.Models;

var basis = service.CreateRequest("Explain this report.");
var providerDefault = basis.WithSpeed(InferenceSpeed.ProviderDefault);
var standard = basis.WithSpeed(InferenceSpeed.Standard);
var fast = basis.WithSpeed(InferenceSpeed.Fast);
```

Compruebe `GetSpeedSupport(InferenceSpeed.Fast)` antes de ofrecer la opción. `StandardSpeed` y `FastSpeed` también distinguen Supported, Unsupported y Unknown. Supported local no verifica permisos, capacidad ni latencia. Las peticiones explícitas Standard/Fast no compatibles o desconocidas fallan sin cambiar silenciosamente modelo ni esfuerzo. `ProviderDefault` mantiene la ruta existente.

```csharp
using Mythosia.AI.Models;
using Mythosia.AI.Models.Capabilities;

var request = service.CreateRequest("Explain this report.")
    .WithSpeed(InferenceSpeed.Fast);
if (request.GetCapabilities().GetSpeedSupport(InferenceSpeed.Fast)
    != CapabilitySupport.Supported)
    throw new NotSupportedException("Fast processing is not supported here.");

await using var run = await request.StartRunAsync();
var result = await run.Result;
Console.WriteLine(result.Text);
foreach (AIProcessingInfo processing in result.Processing)
{
    Console.WriteLine($"{processing.RequestIndex}: {processing.RequestedSpeed} -> " +
        $"{processing.AppliedSpeed?.ToString() ?? "unknown"}; " +
        $"raw={processing.RawAppliedMode}; response={processing.ResponseId}; " +
        $"downgraded={processing.IsDowngraded}");
}
```

`AIRunResult.Processing` conserva `AIProcessingInfo` inmutables incluso sin leer el flujo. `RequestIndex` empieza en 1 e identifica intentos de inferencia del proveedor, incluidas continuaciones del servidor, no el número de rondas de herramientas ni de solicitudes HTTP; llamadas posteriores, reintentos y reparación de formato pueden añadir registros. `AppliedSpeed` es null si no se informa un modo reconocido, incluidos los intentos fallidos. `RawAppliedMode` y `ResponseId` conservan los valores recibidos. `IsDowngraded` solo es true al solicitar Fast y recibir Standard explícitamente; false no confirma Fast.

Tras una completion normal, lea inmediatamente `AIService.LastProcessing`; la siguiente petición lógica sustituye esta vista. Los registros capturados siguen siendo inmutables. La extensión del servicio configura la próxima petición lógica y sus rondas de herramientas, sin crear un valor permanente. Resúmenes auxiliares, reescrituras internas y perfiles internos no heredan esta velocidad ni mezclan sus observaciones con la petición principal.

```csharp
using Mythosia.AI.Extensions;
using Mythosia.AI.Models;

string answer = await service
    .WithSpeed(InferenceSpeed.Standard)
    .GetCompletionAsync("Explain this report.");
var processing = service.LastProcessing;
```

Estos valores describen el modo del proveedor, no tokens por segundo medidos. OpenAI, xAI y Google pueden degradar en el servidor; Mythosia no reintenta automáticamente a otra velocidad. Anthropic fast mode exige acceso a Claude API directa; cambiar velocidad puede invalidar la caché del prompt. Gemini Developer API priority requiere Tier 2/3. Compruebe por separado modelo, API, permisos y precio. Esto no configura imágenes, embeddings ni API Batch nativas. [OpenAI](https://developers.openai.com/api/docs/guides/fast-mode) · [Anthropic](https://platform.claude.com/docs/en/build-with-claude/fast-mode) · [xAI](https://docs.x.ai/developers/advanced-api-usage/priority-processing) · [Gemini](https://ai.google.dev/gemini-api/docs/generate-content/priority-inference)

Con una referencia `IAIService`, use `GetLastProcessing()` de `Mythosia.AI.Extensions`. Lee la interfaz opcional `IAIProcessingInfoService` y devuelve una lista vacía si no hay diagnósticos. `IAIService` no añade miembros obligatorios. En RAG, `RagEnabledService.WithSpeed(...)` configura la próxima respuesta tras la búsqueda; `LastProcessing` describe esa respuesta. La reescritura interna queda separada y Run expone los mismos registros `Processing`.

La lista Fast implementada aparece abajo. Compruebe Standard por separado con `GetSpeedSupport(InferenceSpeed.Standard)`. Modelos no listados, endpoints de terceros y proveedores compatibles con OpenAI no heredan automáticamente modos de pago.

| API | Fast |
| --- | --- |
| Anthropic — `api.anthropic.com` | `claude-opus-4-8`, `claude-opus-5`, `claude-opus-5-5` |
| OpenAI — `api.openai.com` | `gpt-6-astra`, `gpt-6-sol`, `gpt-6-luna`, `gpt-5.6`, `gpt-5.6-sol`, `gpt-5.6-terra`, `gpt-5.6-luna`, `gpt-5.3-codex` |
| xAI — `api.x.ai` | `grok-4.7`, `grok-4.6`, `grok-4.5`, `grok-4.5-latest`, `grok-build-latest`, `grok-4.3`, `grok-4.3-latest`, `grok-latest`, `grok-4.20-0309-reasoning`, `grok-4.20-0309-non-reasoning`, `grok-build-0.1` |
| xAI — `us.api.x.ai` | `grok-4.7`, `grok-4.6` |
| Google — Gemini Developer API | `gemini-2.5-pro`, `gemini-2.5-flash`, `gemini-2.5-flash-lite`, `gemini-3-flash-preview`, `gemini-3.1-pro-preview`, `gemini-3.1-flash-lite`, `gemini-3.5-flash`, `gemini-3.5-flash-lite`, `gemini-3.6-flash`, `gemini-3.7-flash`, `gemini-3.8-flash` |

| API | `Standard` | `Fast` | `RawAppliedMode` |
| --- | --- | --- | --- |
| Anthropic — Opus 4.8 / 5 / 5.5 | `speed: "standard"` + `fast-mode-2026-02-01` | `speed: "fast"` + `fast-mode-2026-02-01` | `usage.speed` |
| Anthropic — otros modelos Claude conocidos, incluido Sonnet 5 | se omiten `speed` y la beta fast-mode | `Unsupported` | `usage.speed` |
| OpenAI | `service_tier: "default"` | `service_tier: "fast"` | `service_tier` (`fast` / `priority` → Fast) |
| xAI | `service_tier: "default"` | `service_tier: "priority"` | `service_tier` |
| Google | `serviceTier: "standard"` | `serviceTier: "priority"` | `x-gemini-service-tier` / `usageMetadata.serviceTier` |

En estos otros modelos Claude, Standard usa la petición ordinaria existente. Si el servidor omite metadatos de procesamiento, `AppliedSpeed` sigue en null; no se deduce Standard de lo solicitado.
