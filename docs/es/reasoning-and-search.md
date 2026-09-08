# Elegir el esfuerzo de razonamiento y responder con fuentes

> Estas API requieren `Mythosia.AI` 7.1.0 o posterior, que incluye `Mythosia.AI.Abstractions` 3.1.0 o posterior. Los ejemplos de RAG requieren `Mythosia.AI.Rag` 7.6.0 o posterior.

## ¿Para qué sirven estas opciones?

Cada etapa necesita un tipo de ayuda distinto. Un primer borrador puede requerir una respuesta rápida; revisar sus supuestos puede justificar más razonamiento. Una pregunta sobre los acontecimientos de hoy necesita información actual, mientras que una pregunta sobre su producto necesita la documentación que lo describe. Aumentar el razonamiento por sí solo no proporciona ninguna de esas fuentes al modelo.

Use la API Fluent común para expresar qué necesita la siguiente tarea. El proveedor seleccionado traduce las opciones compatibles a su API nativa. Su aplicación puede conservar `GetCompletionAsync` para obtener una respuesta completa o usar `StartRunAsync` para mostrar el progreso y controlar la misma tarea.

| La tarea necesita | Configuración |
| --- | --- |
| Un borrador rápido seguido de una revisión más cuidadosa | `WithReasoning(...)` |
| Cambiar el razonamiento conservando un prefijo de conversación apto para la caché | `WithReasoning(..., cache: CachePreservation.Required)` |
| Información actual de la web | `WithWebSearch()` |
| Respuestas basadas en documentos ya indexados por el proveedor | `WithFileSearch(store)` |

Los ejemplos suponen un servicio inicializado con un modelo compatible. Importe `Mythosia.AI.Extensions` y `Mythosia.AI.Models`; los eventos del flujo también utilizan `Mythosia.AI.Models.Streaming`.

## Pasar de un borrador rápido a una revisión cuidadosa

Puede dedicar menos razonamiento a un esquema y luego pedir que la misma conversación examine los detalles difíciles:

```csharp
string outline = await service
    .WithReasoning(ReasoningLevel.Low)
    .GetCompletionAsync("Esboza el plan de migración.");

string review = await service
    .WithReasoning(ReasoningLevel.High)
    .GetCompletionAsync("Revisa los escenarios de fallo y los pasos de recuperación del plan.");
```

`ReasoningLevel` expresa el nivel solicitado, no un presupuesto fijo de tokens ni una garantía de calidad. Cada modelo acepta su propio subconjunto. `Auto` conserva el comportamiento configurado o predeterminado del proveedor; no significa que los niveles incompatibles se sustituyan automáticamente. Las propiedades de presupuesto específicas del proveedor siguen disponibles para modelos que ofrecen presupuestos de tokens en lugar de niveles con nombre.

En una conversación larga, cambiar el esfuerzo en el nivel superior puede invalidar un prefijo de prompt reutilizable. En un modelo compatible, exija el mecanismo del proveedor que permite cambiar el esfuerzo conservando ese prefijo:

```csharp
string review = await service
    .WithReasoning(ReasoningLevel.High, cache: CachePreservation.Required)
    .GetCompletionAsync("Vuelve a comprobar los supuestos de la respuesta anterior.");
```

`Required` establece cómo se envía el cambio. **No** garantiza un acierto de caché, tokens gratuitos ni menor latencia: siguen vigentes las condiciones de elegibilidad, retención y precio de la caché del proveedor. Los modelos incompatibles lanzan `NotSupportedException` antes de enviar la solicitud. Use la misma conversación registrada, el mismo modelo y endpoint; no trunque ni reordene una conversación que contenga estas actualizaciones. Inicie otra conversación si cambian esas condiciones. La compactación automática se bloquea mientras sea necesario conservar el prefijo.

El esfuerzo aceptado con conservación de caché pasa a ser el esfuerzo activo de la conversación hasta otro cambio explícito. El uso ordinario de `WithReasoning(level)` se aplica a su solicitud lógica y no sustituye silenciosamente ese ajuste persistente. El cambio ocurre **entre respuestas del modelo**. No cambia el esfuerzo de una respuesta que ya se está generando y es independiente de `run.SteerAsync`, que envía una instrucción adicional a un Run activo compatible.

## Responder preguntas que necesitan información actual

Active la búsqueda web nativa cuando la respuesta deba utilizar información ajena a los datos de entrenamiento del modelo:

```csharp
string answer = await service
    .WithWebSearch()
    .GetCompletionAsync("Busca el anuncio de la versión más reciente y cita la fuente.");

foreach (AICitation source in service.GetLastCitations())
    Console.WriteLine($"{source.Title}: {source.Url}");
```

El proveedor ejecuta esta herramienta alojada. No hay que registrar ni ejecutar un controlador de función local. Activar la búsqueda la pone a disposición del modelo; este puede decidir que una entrada concreta no la necesita. Las referencias a fuentes están disponibles cuando el proveedor las devuelve.

OpenAI y Anthropic también aceptan `WithWebSearch(new WebSearchOptions { AllowedDomains = new[] { "example.com" } })`. Google no ofrece esta lista de dominios permitidos mediante la herramienta integrada, por lo que se rechaza una solicitud restringida en lugar de buscar en toda la web.

## Responder a partir de documentos ya indexados por el proveedor

Si su aplicación ya mantiene un índice documental alojado por el proveedor, use ese almacén para fundamentar las respuestas sin implementar una ronda de recuperación propia:

```csharp
var documents = new FileSearchStore("OpenAI", "vs_your_existing_store");

string answer = await service
    .WithFileSearch(documents)
    .GetCompletionAsync("Busca en nuestros documentos de políticas. ¿Cuál es el plazo de cancelación?");

foreach (AICitation source in service.GetLastCitations())
    Console.WriteLine($"{source.Title}: {source.FileId ?? source.Url}");
```

Para Google, use `new FileSearchStore("Google", "fileSearchStores/your-existing-store")` con un servicio de Google. Los almacenes pertenecen a su proveedor, cuenta y despliegue; no se puede pasar un ID de almacén OpenAI a Google. Cree el almacén y cargue o indexe sus documentos mediante la API o consola del proveedor antes de usarlo aquí. Esta API solo busca en almacenes existentes y no carga archivos locales.

La búsqueda de archivos alojada y la [canalización RAG](rag.md) de la biblioteca responden a necesidades de configuración distintas. Elija la búsqueda alojada si el proveedor ya administra su índice. Elija RAG cuando su aplicación necesite controlar los cargadores, la división, los embeddings, la recuperación o el almacenamiento vectorial. `RagEnabledService` también transmite `WithReasoning`, `WithWebSearch` y `WithFileSearch` a la respuesta final; su reescritura interna de consultas no hereda estas opciones. Las referencias de recuperación RAG permanecen en `RagProcessedQuery`, separadas de las fuentes `AICitation` suministradas por el proveedor.

## Mostrar el progreso y conservar las fuentes

Use las mismas opciones antes de `StartRunAsync`. El callback de texto puede actualizar la pantalla mientras el Run conserva las fuentes para la respuesta terminada:

```csharp
await using var run = await service
    .WithReasoning(ReasoningLevel.High)
    .WithWebSearch()
    .StartRunAsync(
        "Busca los anuncios recientes y compara los cambios.",
        onText: text => Console.Write(text),
        cancellationToken: cancellationToken);

string answer = await run.Result;
foreach (AICitation source in run.Citations)
    Console.WriteLine($"{source.Title}: {source.Url ?? source.FileId}");
```

`run.Citations` sigue disponible sin consumir el flujo, al observar solo texto o después de que se llene el búfer de observación. Contiene las referencias del proveedor recopiladas durante el Run, incluidas las respuestas intermedias. `service.LastCitations`, o `GetLastCitations()` a través de `IAIService`, describe la solicitud lógica más reciente; conserve el Run o copie su instantánea de citas cuando muestre varias respuestas.

Para recibir los eventos de fuentes a medida que llegan, use un único lector de eventos:

```csharp
await using var run = await service.WithWebSearch().StartRunAsync(
    "Busca y explica los cambios más recientes.", cancellationToken: cancellationToken);

await foreach (var item in run.StreamAsync())
{
    if (item.Type == StreamingContentType.Text)
        Console.Write(item.Content);
    else if (item.Type == StreamingContentType.Citation && item.Citation is AICitation source)
        Console.WriteLine($"\nFuente: {source.Title} {source.Url ?? source.FileId}");
}
string answer = await run.Result;
```

Los campos de una cita pueden ser `null` si el proveedor no suministra un valor. `ResponseId`, `OutputIndex` y `ContentIndex` identifican la respuesta original y su parte de contenido. `StartIndex` y `EndIndex` conservan los desplazamientos locales y la convención de índices del proveedor; **no** son posiciones en el `run.Result` concatenado. No coloque citas indexando indiscriminadamente la respuesta completa con esos valores.

## Comprobar la compatibilidad del proveedor y el alcance de la solicitud

| Proveedor integrado | Niveles de razonamiento con nombre | Cambio conservando la caché | Búsqueda web | Búsqueda de archivos |
| --- | --- | --- | --- | --- |
| OpenAI | Modelos de razonamiento compatibles; los niveles varían según el modelo | GPT-6 Astra Standard, modo de un solo agente | Modelos Responses compatibles | Modelos Responses compatibles y almacenes vectoriales existentes |
| Anthropic | Modelos con control nativo de esfuerzo | Opus 5 / Fable 5.1 / Mythos 5.1 compatibles con la beta del proveedor | Modelos Claude compatibles | Sin adaptador de almacén nativo; use RAG |
| Google | Niveles de Gemini 3; Gemini 2.5 conserva los presupuestos específicos del proveedor | No compatible | Modelos de texto Gemini compatibles | Modelos de texto Gemini compatibles y almacenes de búsqueda de archivos existentes |
| Otros servicios | Los ajustes específicos del proveedor siguen disponibles; estas opciones comunes requieren un adaptador | No compatible con estos adaptadores | Sin adaptador común | Sin adaptador común |

El modelo, el nivel, el transporte y las combinaciones se verifican antes de enviar la solicitud. En particular, **la búsqueda web y la búsqueda de archivos de Google no pueden combinarse en una misma solicitud**. La biblioteca no elimina funciones silenciosamente, reduce niveles de esfuerzo, ignora restricciones de dominio ni cambia a un servicio de búsqueda externo. Las herramientas nativas pueden coexistir con funciones de cliente registradas cuando sea compatible; las rondas de herramientas del Run siguen la política de funciones y `WithMaxRounds`.

Los métodos Fluent conservan el tipo concreto del servicio y copian sus opciones de entrada. Los componentes no nulos se combinan para la siguiente solicitud lógica, incluidas sus rondas de herramientas y reparaciones de salida estructurada, y después se consumen. La búsqueda no permanece activada para llamadas posteriores independientes; vuelva a añadir `WithWebSearch` o `WithFileSearch` cuando sea necesario. Un Run iniciado conserva los ajustes capturados. Como con cualquier configuración mutable del servicio, no cambie los ajustes ni inicie solicitudes superpuestas en el mismo servicio mientras se ejecuta una solicitud.

Las implementaciones personalizadas de `IAIService` siguen siendo compatibles. Se incorporan a estas funciones mediante `IAIRequestFeatureService`; llamar a estos auxiliares en una implementación sin esa capacidad produce un error explícito. Las API existentes de completion, streaming y configuración específica del proveedor siguen disponibles. Consulte [Control de Run](execution-api-transition.md) para cancelación, observación y steering.

Protocolos de los proveedores: [Cambios de razonamiento de OpenAI](https://developers.openai.com/api/docs/guides/reasoning#change-reasoning-mid-conversation), [Herramientas de OpenAI](https://developers.openai.com/api/docs/guides/tools), [Cambios de esfuerzo de Anthropic](https://platform.claude.com/docs/en/build-with-claude/effort#change-effort-mid-conversation), [Búsqueda web de Anthropic](https://platform.claude.com/docs/en/agents-and-tools/tool-use/web-search-tool), [Fundamentación con Google Search](https://ai.google.dev/gemini-api/docs/google-search), [Búsqueda de archivos de Google](https://ai.google.dev/gemini-api/docs/file-search).
