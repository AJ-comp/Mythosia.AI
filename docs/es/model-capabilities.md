# Mostrar las opciones que admite el modelo elegido

> Grok 4.7: Requiere Mythosia.AI 8.1.0 / Abstractions 4.1.0. [selección del modelo, razonamiento y velocidad](providers.md#grok-47)

> GPT-6 Sol/Luna aún no están publicados. Consulta [selección del modelo y requisitos](providers.md#gpt-6-sol-luna).

Para [Claude Opus 5.5](providers.md#claude-opus-55), las capabilities exponen `Low` a `Max`, incluido `XHigh`; `None`, `Minimal` y `ThinkingToggle` no se admiten. `MaxOutputTokens` es 128000. Ocultar el texto no desactiva el razonamiento. Requiere Mythosia.AI 8.1.0 / Abstractions 4.1.0.

Una interfaz de chat debe ofrecer razonamiento, búsqueda, herramientas e imágenes según la conexión elegida. Mantener listas de modelos en cada aplicación duplica reglas de la biblioteca y diverge al cambiar proveedor, protocolo o despliegue. Las instantáneas de capacidades comparten las definiciones entre interfaz y validación de ejecución.

Esta API pertenece a Mythosia.AI 8.0.0. Las instantáneas son descripciones locales inmutables del soporte conocido, no sondeos de cuenta o servidor. Los tipos están en `Mythosia.AI.Models.Capabilities`.

Para peticiones donde importa la espera, elija la [velocidad de procesamiento](request-building.md#inference-speed). `WithSpeed` conserva modelo y esfuerzo; `Processing` informa el modo aplicado. Fast es una opción de pago para combinaciones compatibles.

## Before / After

Before: la aplicación mantiene sus listas. Las listas siguientes son código de la aplicación, no API de la biblioteca.

```csharp
bool showReasoning = mySupportedReasoningModels.Contains(service.Model);
bool showSearch = mySupportedSearchModels.Contains(service.Model);
```

After: inspeccionar la petición configurada y elegir opciones admitidas. Solo la llamada final de completion envía la petición al modelo; consultar capacidades no llama a la API.

```csharp
using Mythosia.AI.Models;
using Mythosia.AI.Models.Capabilities;

var request = service.CreateRequest("Explica los documentos.");
AIModelCapabilities capabilities = request.GetCapabilities();

if (capabilities.GetReasoningSupport(ReasoningLevel.High)
    == CapabilitySupport.Supported)
{
    request = request.WithReasoning(ReasoningLevel.High);
}

string answer = await request.GetCompletionAsync();
```

`CapabilitySupport` distingue `Supported`, `Unsupported` y `Unknown`. Un despliegue propio o selección del servidor puede carecer de datos: `Unknown` no significa no admitido. El ejemplo activa razonamiento adicional solo con soporte conocido. Para casos desconocidos, la aplicación decide conservar valores predeterminados o permitir un intento.

`request.GetCapabilities()` lee modelo, opciones del proveedor y perfil capturados por el builder. `service.GetCapabilities()` inspecciona valores predeterminados sin consumir opciones de la próxima llamada. Ninguno envía HTTP, invoca callbacks de contexto ni validadores de ejecución, cambia el historial o inicia trabajo. Las listas son instantáneas de solo lectura. La consulta del servicio también examina las funciones pendientes para la próxima llamada y las conserva para la petición real. La consulta no serializa valores predeterminados de funciones ni parámetros de herramientas alojadas, y tampoco prepara el perfil de ejecución ni reserva presupuestos de tokens.

Las capacidades describen lo que la conexión puede admitir, no lo que está activado. Importan proveedor, protocolo y modo además del nombre. La identidad refleja anulaciones del proveedor y traducción de ID Qwen/Ollama; sin un modelo único puede ser `null`. El ejemplo Chat UI actualiza los controles a partir de la conexión activa y sus ajustes actuales, incluidas las herramientas registradas, en lugar de depender solo del catálogo de modelos. El soporte del muestreo puede cambiar según el modo de razonamiento o la disponibilidad de herramientas; vuelva a consultar tras esos cambios. El soporte desconocido se distingue del no admitido.

| API | Significado |
| --- | --- |
| `Reasoning`, `ReasoningLevels`, `GetReasoningSupport(...)` | Soporte y niveles del `WithReasoning` común. |
| `NativeReasoning`, `NativeReasoningLevels`, `ThinkingBudgetPresets`, `ThinkingToggle` | Controles nativos del proveedor y presupuestos sugeridos. |
| `Streaming`, `FunctionCalling`, `AsyncFunctionCalling`, `Steering` | Streaming, herramientas, herramientas asíncronas nativas e instrucciones durante la ejecución. |
| `WebSearch`, `FileSearch`, `ReasoningCachePreservation`, `ImageInput`, `StructuredOutput` | Búsqueda alojada, cambios de razonamiento conservando caché, imágenes de entrada y salida estructurada. |
| `Temperature`, `TopP`, `FrequencyPenalty`, `PresencePenalty`, `MaxOutputTokens` | Muestreo admitido y límite conocido de tokens de salida, nullable. |
| `StandardSpeed`, `FastSpeed`, `GetSpeedSupport(...)` | Mythosia.AI 8.1.0 / Abstractions 4.1.0: modos Supported/Unsupported/Unknown; los permisos se comprueban aparte. |
| `Provider`, `Model` | Proveedor y modelo enviado; las identidades pueden ser desconocidas. |

`ReasoningLevels` describe `WithReasoning` común; `NativeReasoningLevels`, los controles nativos. `ThinkingBudgetPresets` ofrece opciones para UI, no todos los presupuestos o un rango exhaustivo. `AsyncFunctionCalling` indica ejecución asíncrona nativa de herramientas, no simplemente handlers locales que devuelven `Task` o ejecución paralela. `StructuredOutput` incluye la API común de salida tipada con alternativa de prompt y reparación; no garantiza decodificación restringida nativa. Ambas listas de niveles usan `ReasoningLevel`; los presupuestos sugeridos son enteros.

Una instantánea no garantiza acceso de cuenta ni disponibilidad del servidor y no valida combinaciones incorrectas. Las validaciones y errores de ejecución siguen. Compruebe `run.CanSteer` en la sesión real: el soporte del modelo no garantiza que el Run siga activo.

## Consultar la generación de imágenes por separado

El modelo de imagen se elige independientemente del chat. `service.GetImageCapabilities(imageModel)` consulta uno específico; sin argumento, el predeterminado de imágenes del proveedor. El builder de chat no elige este modelo. `Generation`, `Editing` y `Mask` ayudan a presentar acciones de imagen.

```csharp
using Mythosia.AI.Models.Capabilities;

ImageModelCapabilities capabilities = service.GetImageCapabilities();
bool showEditing = capabilities.Editing == CapabilitySupport.Supported;
bool showMask = capabilities.Mask == CapabilitySupport.Supported;

foreach (var quality in capabilities.Qualities)
    Console.WriteLine(quality);

int? maximumImages = capabilities.MaxImages;
```

`Qualities`, `Backgrounds`, `OutputFormats`, `SizeKinds`, `Resolutions` y `AspectRatios` son listas tipadas de solo lectura. `MaxImages` y `MaxInputImages` son límites conocidos nullables. Una opción listada no garantiza todas las combinaciones: siguen las validaciones de tamaño, formato, calidad, máscara y modelo. Los modelos propios o desconocidos siguen siendo desconocidos, no se marcan como no admitidos.

En Google, `Resolutions` y `AspectRatios` dependen del modelo de imagen seleccionado y también rigen la validación de generación y edición. Consulta la [tabla por modelo](providers.md#google-image-options), incluida la política conservadora de 1K para Flash-Lite. Los valores explícitos no admitidos fallan antes de HTTP; los modelos personalizados desconocidos conservan `Unknown` y la validación general del proveedor.

Un `AIService` personalizado con definiciones fiables puede sobrescribir el hook protected `ResolveRequestCapabilities()`. El valor predeterminado es `AIModelCapabilities.Unknown`. No estar en el catálogo no vuelve incompatible un despliegue. `IAIService` no añade miembros obligatorios; las consultas pertenecen a `AIService` y a su builder.

Si el perfil de un proveedor personalizado modifica indicadores de modo nativo, sobrescriba `ApplyCapabilityRequestProfile(AIRequestProfile)` y aplique con `SetExecutionSetting(...)` solo los indicadores que necesita el resolver. El hook predeterminado no hace nada. El builder ya ha capturado los ajustes comunes del perfil; la consulta nunca llama a `ApplyRequestProfile` ni a `ApplyProviderSpecificRequestProfile`. Este hook no debe validar, invocar callbacks, serializar, reservar presupuesto ni modificar el estado del servicio o del llamador. Sus ajustes temporales se restauran al terminar la consulta, incluso si la sobrescritura lanza una excepción.

[Configuración de peticiones](request-building.md) · [Opciones de proveedor e imagen](providers.md) · [Control de Run](execution-api-transition.md)
