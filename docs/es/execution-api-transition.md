# Controlar tareas de IA en curso con Run

Para una respuesta final con botón Detener, pase `cancellationToken` a `GetCompletionAsync`. Use Run para eventos de progreso o instrucciones adicionales compatibles. Consulte [cancelación](completions.md#completion-cancellation).

Para ajustes independientes y variantes reutilizables, use el [builder de solicitudes](request-building.md). Llame a `CreateRequest(...)` antes de `With...`. Las propiedades y métodos fluent del servicio conservan su comportamiento.

> Para obtener respuesta, uso y fuentes juntos, `await run.Result` devuelve una instantánea `AIRunResult`. La cadena está en `result.Text`, sin leer el flujo. Es un cambio de Mythosia.AI 8.0.0; `GetCompletionAsync` y `StructuredStreamRun<T>.Result` mantienen sus tipos de retorno. [Resultado Run y migración](#run-result).

> Los ejemplos con `CreateRequest` requieren Mythosia.AI 8.0.0 / Abstractions 4.0.0. La versión 7.1 que introdujo Run y las opciones comunes no incluye el builder. Los paquetes anteriores pueden usar las sobrecargas del servicio.

## ¿Por qué controlar una tarea mientras se ejecuta?

Redactar un informe puede requerir varias búsquedas de documentos, llamadas a API y etapas de escritura. Mientras tanto, el usuario puede querer ver el progreso, detener el trabajo o añadir una condición como «Incluir solo los datos de este año». La aplicación necesita vincular esas acciones con la tarea que ya está en marcha.

Run proporciona un objeto que la aplicación puede conservar para esa tarea. Por ejemplo, una pantalla de chat puede mostrar el texto recibido, indicar cuándo se utiliza una herramienta, conectar un botón Detener con la cancelación y enviar una instrucción adicional si el modelo lo admite. Todas estas acciones se refieren a la misma ejecución.

| Necesidad de la aplicación | Qué utilizar |
| --- | --- |
| Recibir la respuesta final con cancelación opcional | `GetCompletionAsync(..., cancellationToken: token)` |
| Mostrar el texto según llega y recuperarlo al finalizar | Iniciar un run con `onText` y después esperar `run.Result`. |
| Mostrar la actividad de herramientas o esperar un procesamiento de salida asíncrono | Leer eventos de `run.StreamAsync()`. |
| Permitir que el usuario detenga el trabajo | Llamar a `run.Cancel()` sobre el objeto conservado. |
| Añadir una condición antes de terminar | Comprobar `run.CanSteer` y usar `run.SteerAsync(...)` con un modelo compatible. |

`StartRunAsync` inicia una tarea del modelo y devuelve un `AIRun`. La tarea continúa aunque no se observe su salida. El mismo objeto sirve para el streaming, el resultado acumulado, la cancelación y las instrucciones durante la respuesta cuando estén disponibles. `GetCompletionAsync`, incluidas las sobrecargas tipadas y RAG, sigue siendo una API pública de conveniencia para recibir el resultado una vez terminado el trabajo.

<a id="run-result"></a>

## Obtener juntos la respuesta, el uso y las fuentes

Aunque una pantalla solo muestre la respuesta terminada, puede necesitar guardar los tokens y las fuentes. Antes, `run.Result` devolvía únicamente una cadena: había que recopilar eventos del flujo para el uso y leer las fuentes por separado. `AIRunResult` reúne esos datos incluso sin leer el flujo.

Before — contrato Run anterior

```csharp
string answer = await run.Result;
```

After — Mythosia.AI 8.0.0

```csharp
using Mythosia.AI.Models.Runs;

await using var run = await service
    .CreateRequest("Analiza los documentos.")
    .StartRunAsync();

AIRunResult result = await run.Result;
string answer = result.Text;
int? totalTokens = result.Usage?.TotalTokens;
var citations = result.Citations;
string? requestedModel = result.RequestedModel;
string? actualModel = result.Model;
var finishReason = result.FinishReason;
```

Mostrar el texto según llega usa el mismo resultado. El callback, `run.StreamAsync()`, `SteerAsync` y los controles de cancelación conservan su función.

```csharp
using Mythosia.AI.Models.Runs;

await using var run = await service
    .CreateRequest("Analiza los documentos.")
    .StartRunAsync(onText: text => Console.Write(text));

AIRunResult result = await run.Result;
Console.WriteLine($"\nTokens: {result.Usage?.TotalTokens}");
```

El resultado terminado es una instantánea. `Usage` y los objetos de cita se copian; modificar lo devuelto no altera el resultado guardado, disponible tras liberar el Run. Los filtros, detener el lector o desbordar el búfer de observación no eliminan datos del resultado.

`Usage` suma una vez el uso informado en cada ronda del modelo. Los eventos de ronda y el total final no se cuentan dos veces. Si no se informa ningún uso, es `null`; no se estiman datos ausentes. Excluye peticiones auxiliares de resumen independientes, por lo que no representa toda la facturación ni el uso de la cuenta. Si solo algunas rondas informan su uso, la suma incluye únicamente esas rondas; no garantiza una cobertura completa.

El valor de `TotalTokens` comunicado explícitamente se conserva aunque el desglose de entrada y salida esté incompleto; la suma entre rondas utiliza esos totales comunicados.

Los contadores de tokens siguen siendo `Int32`. Si la suma entre rondas supera `Int32.MaxValue`, el flujo o Run falla con `OverflowException` en vez de devolver valores desbordados. La limpieza se completa y `run.Result` no queda pendiente aunque falle la suma final.

La secuencia devuelta por `run.StreamAsync()` solo se puede enumerar una vez. Volver a enumerarla, incluso en paralelo, produce `InvalidOperationException`; el lector original y la ejecución siguen siendo independientes.

`Provider` identifica el adaptador; `RequestedModel` es el modelo único enviado explícitamente en la petición y capturado al iniciar, incluida una anulación de modelo del proveedor. Es `null` si un preset, perfil o enrutamiento del servidor selecciona el modelo sin enviar un campo de modelo único explícito (por ejemplo, una lista Perplexity `Models`). Es independiente del modelo real de respuesta en `Model`. `Model` es el ID real informado en la respuesta de la última ronda, o `null`; no se sustituye por el solicitado. `RoundCount` cuenta rondas LLM de la biblioteca, no herramientas individuales ni pasos internos del agente alojado. Un proveedor personalizado sin contador produce `0`.

`FinishReason` usa `AIFinishReason` (`Unknown`, `Stop`, `MaxTokens`, `ToolCalls`, `ContentFilter`, `Other`); `RawFinishReason` conserva el valor terminal del proveedor. Si falta: `Unknown`/`null`. Solo describen resultados correctos. Los errores existentes, como superar las rondas, siguen haciendo fallar `Result`. La cancelación sigue lanzando `OperationCanceledException`, sin sustituirla por un resultado correcto.

`Text` conserva todos los textos emitidos en orden, incluidos los intermedios y los anteriores a una instrucción adicional. El resultado espera la ejecución y la limpieza. `Citations` contiene las fuentes finales; `run.Citations` sigue disponible durante la ejecución. Las posiciones de cita pertenecen a las partes originales del contenido, no al texto concatenado.

<a id="run-result-migration"></a>

**Migración mayor:** `AIRun.Result` pasa de `Task<string>` a `Task<AIRunResult>`. Para la cadena, lea `(await run.Result).Text`. Las implementaciones propias de `AIRun` deben actualizar su override y crear `AIRunResult`; los consumidores deben recompilar. El resultado está en `Mythosia.AI.Models.Runs`; `TokenUsage` y `AIFinishReason`, en `Mythosia.AI.Models.Streaming`. `GetCompletionAsync` mantiene `Task<string>` y `StructuredStreamRun<T>.Result` mantiene `Task<T>`. Estos ejemplos requieren Mythosia.AI 8.0.0, no los primeros paquetes Run 7.1/3.1.

## Mostrar texto mediante un callback

En una pantalla de chat o una consola, mostrar el texto en cuanto llega permite seguir una respuesta larga mientras se escribe.

```csharp
await using var run = await service
    .CreateRequest("Lee los documentos y redacta un informe.")
    .WithMaxRounds(10)
    .StartRunAsync(
        onText: text => Console.Write(text),
        cancellationToken: cancellationToken);

string answer = (await run.Result).Text;
```

Las herramientas locales pueden devolver objetos mediante `Task<T>` / `ValueTask<T>` y recibir un `CancellationToken` inyectado. `run.Cancel()` o el token inicial llega a las herramientas cooperativas; detener solo el lector no. Las excepciones son errores. Al cancelar se omiten las llamadas pendientes y la limpieza espera las herramientas iniciadas que ignoran el token. Consulta [resultados, errores y cancelación](function-calling.md#tool-execution-contract).

`onText` es un callback opcional de tipo `Action<string>` que se registra antes de iniciar el trabajo. Recibe el texto en orden y no ejecuta herramientas. Omítelo si solo necesitas el resultado. Una excepción en el callback cancela el run y hace que `Result` falle. No pases una lambda `async` a `onText`: se convertiría en `async void`, cuyo trabajo y errores el run no puede esperar. Usa el flujo de eventos para la salida asíncrona. Los callbacks no se trasladan automáticamente al hilo de la interfaz.

`(await run.Result).Text` concatena los eventos de texto del run, incluido el texto intermedio entre llamadas a herramientas y el producido antes de una instrucción adicional. No es una segunda petición al modelo ni una respuesta reescrita. Si solo necesitas esperar el resultado y prefieres la semántica de respuesta existente, puedes seguir usando `GetCompletionAsync`.

## Leer eventos de texto, herramientas y consumo

Cuando una tarea busca documentos o llama a una API de negocio, el texto por sí solo puede no explicar la espera. Los eventos tipados permiten mostrar la actividad de las herramientas junto a la respuesta y registrar el consumo cuando el proveedor lo facilita.

```csharp
await using var run = await service.StartRunAsync(
    "Busca en los documentos y explica el resultado.",
    cancellationToken: cancellationToken);

await foreach (var item in run.StreamAsync())
{
    switch (item.Type)
    {
        case StreamingContentType.Text:
            Console.Write(item.Content);
            break;
        case StreamingContentType.FunctionCall:
            Console.WriteLine("\n[Llamando a una herramienta]");
            break;
        case StreamingContentType.FunctionResult:
            Console.WriteLine("\n[Resultado de herramienta recibido]");
            break;
        case StreamingContentType.Completion when item.Usage != null:
            Console.WriteLine($"\n[Tokens totales: {item.Usage.TotalTokens}]");
            break;
    }
}

string answer = (await run.Result).Text;
```

`run.StreamAsync()` acepta un token opcional de cancelación de la observación, sin prompt. Observa la tarea que ya inició `StartRunAsync`. La biblioteca ejecuta los manejadores de funciones registrados; nunca vuelvas a ejecutar una herramienta al recibir su evento de visualización. Las opciones de presentación del texto no desactivan las herramientas registradas del run.

El callback de inicio y `run.StreamAsync()` pueden observar simultáneamente el mismo run; el flujo de eventos admite un lector. Por ejemplo, muestra el texto en `onText` y procesa solo los eventos de herramientas en el flujo para evitar mostrarlo dos veces. Se almacenan hasta 1.024 eventos sin leer, incluso si hay un callback. Un lector que empieza tarde recibe los eventos desde el inicio mientras quepan en el búfer. Si se supera el límite, la observación del flujo falla de forma explícita, pero el callback, la ejecución y `Result` continúan. El flujo no es un registro de reproducción ilimitado. Esperar `Result` nunca exige consumir por completo el flujo de eventos.

## Salida asíncrona

Para procesar la salida de forma asíncrona, espera la operación dentro del lector en lugar de utilizar un callback `onText` asíncrono:

```csharp
using var writer = new StreamWriter("informe.txt");
await using var run = await service.StartRunAsync(
    "Redacta un informe.", cancellationToken: cancellationToken);

await foreach (var item in run.StreamAsync())
{
    if (item.Type == StreamingContentType.Text && item.Content is string text)
        await writer.WriteAsync(text);
}
string answer = (await run.Result).Text;
```

## Cancelar y liberar recursos

- Salir de `await foreach` o cancelar el token pasado únicamente a `run.StreamAsync(token)` detiene la observación; la tarea continúa.
- `run.Cancel()`, el token pasado a `StartRunAsync` y la liberación de un run activo cancelan la ejecución.
- `await using` garantiza que `DisposeAsync()` espere al productor y a la limpieza del proveedor. Las herramientas sin soporte de cancelación pueden tardar en terminar; liberar recursos no deshace acciones completadas.
- Un servicio admite una tarea `StartRunAsync` activa. Se rechaza otro inicio mientras siga activa. Usa servicios distintos para tareas concurrentes independientes y no mezcles llamadas antiguas ni cambies la configuración del servicio mientras un run esté en curso.

El run captura su entrada y la política pendiente para esa petición antes de ejecutarse en segundo plano. Se copian los contenidos integrados de texto, imagen y audio, así como los arrays de bytes de los medios. Las subclases personalizadas de `MessageContent` conservan su identidad y deben permanecer sin cambios hasta que termine el run.

El valor capturado de `FunctionCallingPolicy.TimeoutSeconds` establece un único plazo para la preparación y todas las rondas de modelo y herramientas. Al vencer, se produce una `AIServiceException`; la cancelación del usuario cancela el resultado. La limpieza sigue esperando a los manejadores que no admiten cancelación.

## Enviar otra instrucción durante el trabajo

Supongamos que un usuario empieza un plan de proyecto y después se da cuenta de que debe ajustarse a dos semanas. Las instrucciones durante la ejecución permiten enviar esa nueva condición mientras el modelo sigue trabajando. Son útiles para correcciones y cambios de alcance descubiertos durante tareas largas. Para una pregunta nueva después de terminar, inicia la siguiente petición de la forma habitual.

```csharp
await using var run = await service.StartRunAsync(
    "Prepara un plan de proyecto.",
    onText: text => Console.Write(text),
    cancellationToken: cancellationToken);

// Llamar desde el manejador de instrucciones adicionales de la interfaz mientras el run esté activo.
async Task SendUpdateAsync(string instruction)
{
    if (!run.CanSteer)
        throw new NotSupportedException("Este run no admite instrucciones adicionales.");

    await run.SteerAsync(instruction, cancellationToken);
}

string answer = (await run.Result).Text;
```

Las instrucciones durante la respuesta están disponibles para GPT-6 Astra mediante una conexión WebSocket de Responses. Otros proveedores y modelos no compatibles pueden ejecutar runs normales, pero `CanSteer` es false y el intento de enviar instrucciones informa de la falta de soporte en vez de crear silenciosamente otro turno ordinario. `CanSteer` no garantiza que el run siga activo cuando se realice una llamada posterior.

Los runs de Astra abren un socket dedicado. El `HttpClient` proporcionado y sus manejadores de mensajes siguen atendiendo llamadas HTTP y no interceptan ese socket. Los transportes personalizados pueden sobrescribir `OpenAIService.ConnectRunWebSocketAsync`.

Que `SteerAsync` termine correctamente significa que el servidor aceptó la entrada en su cola, no que el modelo ya la haya aplicado. Sigue observando el mismo run o esperando su resultado durante la continuación. No se deshacen el texto ya entregado ni las acciones completadas; las herramientas iniciadas no se cancelan por el mero envío de una nueva instrucción. La biblioteca gestiona la continuación y la asociación de resultados de herramientas en la misma conexión. Consulta la [guía de instrucciones durante la respuesta](https://developers.openai.com/api/docs/guides/steering) y el [modo WebSocket](https://developers.openai.com/api/docs/guides/websocket-mode) de OpenAI. No supongas que las entradas en cola vinculadas a una conexión sobreviven a una desconexión ni reenvíes sin comprobar una instrucción ya aceptada.

## Tareas con herramientas y métodos antiguos de agente

Preguntas como «Comprueba la política de reembolso y el estado de este pedido» requieren varias fuentes. Registra herramientas para buscar documentos y consultar pedidos, y deja que el modelo elija las llamadas necesarias. Un límite de rondas acota cuánto puede seguir solicitando herramientas antes de terminar o informar de un error.

Las llamadas a funciones normales ya admiten varias rondas entre modelo y herramientas. `StartRunAsync` utiliza las mismas funciones registradas y políticas de ejecución; no hace falta un modo de agente independiente, un planificador ni un interruptor `WithAgentic`.

`RunAgentAsync` y `RunAgentStreamAsync` siguen siendo invocables, pero ahora muestran advertencias `[Obsolete]`. Durante la migración se conservan sus firmas, el valor predeterminado `maxSteps = 10` y el comportamiento de error anterior al superar el límite de pasos. Para las nuevas llamadas, utiliza:

```csharp
await using var run = await service
    .CreateRequest("Busca la política, comprueba el pedido y explica el resultado.")
    .WithMaxRounds(10)
    .StartRunAsync(
        onText: text => Console.Write(text),
        cancellationToken: cancellationToken);
string answer = (await run.Result).Text;
```

El valor predeterminado general de `FunctionCallingPolicy.MaxRounds` es 20, por lo que debes indicar 10 para conservar el límite del agente antiguo. `WithMaxRounds` configura una política para una sola petición y no cambia `DefaultPolicy`. Configúrala antes de iniciar el trabajo. Los métodos antiguos de agente, en cambio, copian la política predeterminada actual y aplican el `maxSteps` de su llamada. Un run nuevo utiliza el contrato común de errores de ejecución y no garantiza la conversión anterior a `AgentMaxStepsExceededException`/`PartialResponse`. Si dependes de ese contrato, conserva la llamada antigua hasta migrar también su tratamiento de excepciones.

## RAG, MCP y límites entre paquetes

- `RagEnabledService.StartRunAsync` admite una cadena o un `Message`, `onText`, `RagQueryOptions` por consulta, `streamOptions` y cancelación. Busca antes de iniciar el run subyacente, conserva imágenes, audio y metadatos, mantiene la entrada original en el historial y envía el texto enriquecido mediante el contexto de la petición. Ese enriquecimiento queda vinculado a la consulta original del usuario, de modo que los resultados de herramientas y las instrucciones posteriores no se sustituyen por el prompt RAG original. Enviar instrucciones al run devuelto actualiza al modelo, pero no repite automáticamente la búsqueda RAG.
- `WithAgenticRag` sigue registrando una herramienta de búsqueda. Al usarla mediante `StartRunAsync`, el modelo puede solicitar nuevas búsquedas cuando las necesite. El registro MCP mediante `WithMcpServerAsync` tampoco cambia. Libera las conexiones MCP compartidas por separado de los runs que las utilizan.
- `IAIRunService` es una capacidad opcional de `Mythosia.AI.Abstractions`; `IAIService` no incorpora nuevos miembros obligatorios. Un servicio personalizado debe implementar `IAIRunService` para iniciar runs RAG. Los servicios no compatibles se rechazan antes de comenzar la indexación RAG.
- RAG conserva su dependencia de Abstractions. Los proveedores distribuidos en paquetes separados mantienen sus sobrescrituras públicas de completion y sus puntos de extensión accesibles. Este cambio no deja obsoleta ninguna API de almacenes vectoriales, carga de documentos o administración de servidores.

Para configurar razonamiento, búsqueda web o de archivos antes de iniciar el Run y mostrar sus fuentes, consulte [Razonamiento y respuestas con fuentes](reasoning-and-search.md).

## Migrar a Mythosia.AI 8

| API | Estado actual |
| --- | --- |
| `GetCompletionAsync` / `GetCompletionAsync<T>` | Pública y compatible, incluidas las variantes de interfaz, proveedor y RAG. |
| `StartRunAsync` / `AIRun` | API común de ejecución y control. |
| `RunAgentAsync` / `RunAgentStreamAsync` | Advertencias Obsolete; comportamiento existente conservado por compatibilidad. |
| `service.StreamAsync` y `StreamAsync` de RAG que reciben una entrada | Los StreamAsync de servicio/RAG con entrada siguen públicos en v8. Use StartRunAsync para nuevo control de ejecución; run.StreamAsync() solo observa un run existente. |
| `run.StreamAsync()` | Observa la salida de una tarea existente sin recibir una nueva petición. |
| `BeginStream(...).As<T>()` / `StructuredStreamRun<T>` | Se conserva la API de streaming tipado; su `Stream()` solo de salida no es un método antiguo de petición del servicio. |

[Migrar a Mythosia.AI 8](v8-migration.md).

Perplexity: [Mantener una tarea larga en ejecución](perplexity.md).

[Crear opciones de modelo con definiciones compartidas](model-capabilities.md).
