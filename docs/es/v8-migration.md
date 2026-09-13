# Migrar a Mythosia.AI 8

Use esta versión para aislar la configuración de cada solicitud, detener trabajo en curso y guardar la respuesta junto con consumo y fuentes. Reúne seis cambios de arquitectura, actualizaciones de proveedores y modelos, y correcciones de tres revisiones adversariales en una versión mayor.

Actualice juntos solo los paquetes que utiliza y recompile los consumidores. Mythosia.AI incluye la dependencia Abstractions correspondiente. La tabla relaciona la base publicada con las versiones compatibles de esta entrega.

| Paquete | Base publicada | Versión objetivo |
| --- | --- | --- |
| `Mythosia.AI` | 7.1.0 | [8.0.0](../../src/core/Mythosia.AI/RELEASE_NOTES.md#v800) |
| `Mythosia.AI.Abstractions` | 3.1.0 | [4.0.0](../../src/core/Mythosia.AI.Abstractions/RELEASE_NOTES.md#v400) |
| `Mythosia.AI.Providers.Alibaba` | 2.0.1 | [3.0.0](../../src/core/Mythosia.AI.Providers.Alibaba/RELEASE_NOTES.md#v300) |
| `Mythosia.AI.Rag` | 7.6.0 | [8.0.0](../../src/rag/Mythosia.AI.Rag/RELEASE_NOTES.md#v800) |
| `Mythosia.AI.Mcp` | 0.0.1-preview | [0.1.0-preview](../../src/integrations/Mythosia.AI.Mcp/RELEASE_NOTES.md#v010-preview) |
| `Mythosia.AI.Serving.Vllm` | 1.0.0-preview | [1.0.0](../../src/serving/Mythosia.AI.Serving.Vllm/RELEASE_NOTES.md#v100) |

`Mythosia.AI.Serving.Vllm` pasa de `1.0.0-preview` a la versión estable `1.0.0`. Conserva las API existentes de modelos, estado, versión del servidor y métricas como paquete independiente, sin dependencia del paquete AI principal.

## Elegir según la necesidad

| Necesidad | Cambio y migración |
| --- | --- |
| Detectar errores en opciones de imagen antes del envío | Cambie cadenas por `ImageQuality`, `ImageBackground`, `ImageOutputFormat` e `ImageSize.Pixels(...)` / `ImageSize.Preset(...)`. El soporte sigue dependiendo del proveedor. |
| Preparar solicitudes sin cambiar sus ajustes entre sí | Empiece con `CreateRequest(...)` y conserve el nuevo builder de cada `With...`. Los setters del servicio siguen cambiando valores predeterminados compartidos. |
| Devolver datos desde herramientas asíncronas | Los métodos registrados por atributos devuelven objetos mediante `Task<T>` / `ValueTask<T>` y reciben `CancellationToken` inyectado. Las excepciones se registran como fallos; los handlers de cadenas se mantienen. |
| Dejar de esperar al cancelar | Pase `cancellationToken` a completion, Run y entradas RAG compatibles. Detiene trabajo local y herramientas cooperativas; no garantiza cancelación remota ni revierte acciones externas completadas. |
| Guardar respuesta, consumo y fuentes juntos | `AIRun.Result` devuelve `Task<AIRunResult>`. Use `(await run.Result).Text` para la cadena; el resultado se recopila incluso sin leer el stream. |
| Mostrar controles adecuados al modelo | Use `request.GetCapabilities()` o consultas de servicio/imagen. `Supported`, `Unsupported` y `Unknown` describen información local de la biblioteca, no acceso real a la cuenta. |

## Actualizar llamadas y proveedores propios

Los tipos de imagen, `AIRun.Result` y las firmas de cancelación modificadas rompen contratos. Las implementaciones propias de `IAIService` y los overrides de sobrecargas públicas modificadas deben añadir y propagar el token. El override del proveedor `GetCompletionAsync(Message)` mantiene su firma y propaga `RequestCancellationToken`. Los `AIRun` propios deben devolver `AIRunResult`. GetCompletionAsync conserva la cadena; completion tipado y `StructuredStreamRun<T>.Result` conservan sus resultados tipados. Los StreamAsync de servicio/RAG con entrada siguen públicos en v8. RunAgentAsync y RunAgentStreamAsync mantienen compatibilidad y avisos obsolete. Use Run para nuevos flujos de progreso, cancelación e instrucciones admitidas durante la ejecución.

## Una solicitud con resultado y progreso opcional

```csharp
using Mythosia.AI.Builders;
using Mythosia.AI.Models.Runs;
using Mythosia.AI.Models.Capabilities;

AIRequestBuilder request = service.CreateRequest("Summarize this document.");
var capabilities = request.GetCapabilities();
if (capabilities.Temperature == CapabilitySupport.Supported)
    request = request.WithTemperature(0.2f);

await using var run = await request
    .StartRunAsync(
        onText: text => Console.Write(text),
        cancellationToken: cancellationToken);

AIRunResult result = await run.Result;
Console.WriteLine(result.Text);
```

OpenAI utiliza píxeles; Google y xAI, `ImageSize.Preset(...)`. Cambie Auto solo si se admite un formato explícito; guarde según el `GeneratedImage.MediaType` devuelto.

```csharp
using Mythosia.AI.Models.Images;

var imageRequest = new ImageGenerationRequest
{
    Prompt = "A simple architectural study",
    Quality = ImageQuality.Auto,
    Background = ImageBackground.Auto,
    OutputFormat = ImageOutputFormat.Auto,
    Size = ImageSize.Pixels(1536, 1024)
};
var generated = await images.GenerateImagesAsync(imageRequest, cancellationToken);
```

Una herramienta registrada puede devolver el objeto de la aplicación como abajo. El handler de bajo nivel `HandlerWithCancellation` sigue devolviendo `Task<string>`; no requiere un nuevo wrapper de objetos.

```csharp
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Mythosia.AI.Attributes;

public sealed record FileResult(string Path, string Text);

public sealed class FileTools
{
    [AiFunction("read_file", "Read a text file")]
    public async Task<FileResult> ReadFileAsync(
        string path, CancellationToken cancellationToken)
    {
        string text = await File.ReadAllTextAsync(path, cancellationToken);
        return new FileResult(path, text);
    }
}
```

## Proveedores y validación

También se incluyen las integraciones preparadas de Fable 5.1, Gemini 3.7/3.8 Flash, Grok 4.6, DeepSeek Flash y Perplexity Agent, y generación/edición de imágenes común a OpenAI, Google y xAI. Las constantes eliminadas y el nuevo endpoint de Perplexity pueden exigir cambios de llamadas; consulte la guía de proveedores y las notas de cada paquete.

Configure investigación con `PerplexityAgentOptions`. Los tests de Profile, Custom Skill y Connector están preparados, pero requieren recursos registrados. MCP sigue en preview. Al comenzar la liberación, las llamadas fallan con `ObjectDisposedException`; tras detenerse el lector, las nuevas llamadas fallan con `McpException` en lugar de esperar indefinidamente.

Tres revisiones adversariales reforzaron copias, resultados de herramientas, cancelación/limpieza, cálculo de tokens, validación de respuestas y ciclo MCP. La tercera añadió 43 casos de regresión; pasaron los 2.703 tests. Los documentos abarcan 13 idiomas. Esa revisión no realizó llamadas reales a APIs; las pruebas unitarias no acreditan todas las integraciones dependientes de recursos de cuenta.

## Guías detalladas

- [CreateRequest / AIRequestBuilder](request-building.md)
- [AIRun / AIRunResult](execution-api-transition.md)
- [ImageQuality / ImageSize](providers.md#image-options-migration)
- [CancellationToken](completions.md#completion-cancellation)
- [Task<T> / ValueTask<T> / HandlerWithCancellation](function-calling.md#tool-execution-contract)
- [GetCapabilities](model-capabilities.md)
- [PerplexityAgentOptions](perplexity.md)
