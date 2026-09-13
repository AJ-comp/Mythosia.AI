# Perplexity: respuestas con fuentes, búsqueda y embeddings

Use Perplexity cuando la respuesta necesite información reciente y fuentes que el lector pueda comprobar. `PerplexityService` llama a la Agent API; la búsqueda y los embeddings independientes permiten crear la recuperación de documentos con el modelo de respuesta que prefiera.

## Elegir primero la tarea

Generar una respuesta actual, obtener páginas web y calcular vectores para un índice propio son trabajos distintos. Elija el componente que debe realizarlos sin llamar a un modelo de respuesta en cada búsqueda.

| Necesidad | Componente |
| --- | --- |
| Una respuesta investigada con fuentes | `PerplexityService` |
| Páginas ordenadas para otra interfaz o modelo | `PerplexitySearchClient` |
| Vectores de pasajes independientes para RAG | `PerplexityEmbeddingProvider` |
| Vectores que tengan en cuenta fragmentos vecinos del mismo documento | `PerplexityContextualizedEmbeddingProvider` |

Instale `Mythosia.AI` y, para los ejemplos de embeddings, `Mythosia.AI.Rag`. Proporcione una clave API y un `HttpClient` administrado por la aplicación. Los ejemplos usan sus variables `apiKey`, `httpClient` y `cancellationToken`.

## Responder con un ajuste Agent

Un ajuste combina modelo, instrucciones, herramientas, esfuerzo y presupuestos mantenidos por el proveedor. Use `Fast` para consultas rápidas, `Low` para investigación cotidiana, `Medium` para comparaciones de varios pasos y `High` / `XHigh` para profundizar. `WideResearch` sirve para investigaciones amplias; utilice ejecución en segundo plano si se prevé una tarea larga. Son ajustes, no identificadores de modelo.

```csharp
using Mythosia.AI.Models.Perplexity;
using Mythosia.AI.Services.Perplexity;

var service = new PerplexityService(apiKey, httpClient)
    .WithPerplexityOptions(new PerplexityAgentOptions
    {
        Preset = PerplexityPreset.Low
    });

await using var run = await service.StartRunAsync(
    "Compara los métodos recientes de reciclaje de baterías y cita las fuentes.",
    onText: text => Console.Write(text),
    cancellationToken: cancellationToken);
string answer = (await run.Result).Text;
foreach (var source in run.Citations)
    Console.WriteLine(source.Url);
```

Use `GetCompletionAsync` para obtener solo la respuesta, `StreamAsync` del servicio para código de streaming existente o `StartRunAsync` para observar y cancelar. `(await run.Result).Text` concatena el texto emitido. `run.Citations` y `LastCitations` conservan fuentes aunque no lea eventos de cita. Los eventos de razonamiento incluyen únicamente contenido expuesto por el proveedor y dependen del modelo.

`AIRunResult.RequestedModel` es el modelo único enviado explícitamente en la petición y capturado al iniciar, incluida una anulación de modelo del proveedor. Es `null` si un preset, perfil o enrutamiento del servidor selecciona el modelo sin enviar un campo de modelo único explícito (por ejemplo, una lista Perplexity `Models`). Es independiente del modelo real de respuesta en `Model`.

## Controlar la investigación y las herramientas

`WithPerplexityOptions(...)` fija opciones persistentes que cada solicitud lógica copia. Las opciones comunes `WithReasoning(...)` y `WithWebSearch(...)` afectan a la próxima solicitud lógica, sus rondas de funciones y reparaciones de salida tipada. La reescritura interna de consultas RAG no hereda la búsqueda de la respuesta final.

`UsePreset(...)` selecciona directamente un ajuste. El ajuste/perfil elige su modelo y `ModelOverride` lo sustituye. `DisableWebSearch` solo quita la herramienta predeterminada del adaptador; no garantiza apagar la búsqueda del ajuste. Los esfuerzos, según modelo, son `Minimal`, `Low`, `Medium`, `High`, `XHigh`, `Max`. Se rechazan `None` y el esfuerzo explícito para Sonar directo. El `DisableReasoning` interno utiliza un esfuerzo bajo compatible u omite la opción, sin garantizar desactivar el razonamiento.

| Opción | Uso |
| --- | --- |
| `Preset` / `ModelOverride` | Elegir una configuración de investigación o sustituir su modelo con un ID proveedor/modelo. |
| `MaxSteps` | Limitar el bucle alojado; cero usa el valor del proveedor. Es distinto de `WithMaxRounds`, que limita las continuaciones de funciones locales. |
| `ReasoningEffort` | Ajustar el razonamiento. `Auto` omite la opción; los niveles admitidos dependen del modelo real. |
| `DisableWebSearch` / `Tools` | Configurar la herramienta web predeterminada del adaptador y las herramientas alojadas explícitas. |
| `Models` | Indicar de uno a cinco modelos alternativos por prioridad. La lista sustituye al modelo único; todos deben admitir las funciones solicitadas. |
| `Profile` | Usar una configuración guardada y fijar opcionalmente su versión. No se combina con `Preset`. |
| `ServiceTier` | Solicitar procesamiento predeterminado, flex o prioritario. El proveedor puede ignorar un nivel incompatible. |
| `Skills` | Proporcionar habilidades integradas, en línea o personalizadas ya cargadas. Los recursos personalizados pertenecen a la cuenta Perplexity. |
| `LanguagePreference` / `PromptCacheKey` | Establecer el idioma o una sugerencia de enrutamiento de caché. No garantiza un acierto de caché. |
| `PreviousResponseId` / `Store` | Continuar una respuesta terminada o controlar su visibilidad de consulta. Use `StatelessMode` y solo el turno nuevo al continuar. `Store = false` no desactiva la persistencia del proveedor. |

`PerplexityHostedTool` acepta un `Type` compatible y `Parameters` JSON documentados: `web_search`, `fetch_url`, `finance_search`, `people_search`, `sandbox` o `mcp`. Los servidores MCP y conectores administrados se ejecutan mediante el proveedor; credenciales, permisos y recursos de cuenta deben corresponder a esa conexión. Registre funciones de aplicación con `Functions` / el constructor de funciones. Los pasos alojados y los manejadores locales tienen responsables de ejecución diferentes.

Las factorías `PerplexityHostedTools.WebSearch`, `FetchUrl`, `Sandbox`, `FinanceSearch`, `PeopleSearch`, `Mcp` y `Connector` crean herramientas. MCP ejecuta sin pausa de aprobación; limite `allowedTools` cuando corresponda. Connector es una función preliminar y referencia una integración ya conectada.

```csharp
service.WithPerplexityOptions(new PerplexityAgentOptions
{
    Preset = PerplexityPreset.Low,
    MaxSteps = 8,
    Tools = new List<PerplexityHostedTool>
    {
        PerplexityHostedTools.WebSearch(),
        PerplexityHostedTools.FetchUrl()
    }
});
string comparison = await service.GetCompletionAsync(
    "Lee la documentación del proyecto y compara las capacidades pertinentes.");
```

El modelo determina la compatibilidad de herramientas, razonamiento, imágenes y esquemas. `WithFileSearch` no es un adaptador de almacén vectorial Perplexity. Los archivos generados en sandbox, los adjuntos y los datos MCP son recursos distintos, sin convertirse automáticamente en un almacén común de búsqueda de archivos.

## Fuentes, imágenes y respuestas estructuradas

Use completion o streaming tipado si necesita campos JSON. El adaptador envía un esquema nativo y conserva el proceso de reparación. Mantiene elementos de respuesta e identificadores de herramientas para continuar; evite borrar o reordenar el historial del protocolo. Las imágenes se introducen con `Message` e `ImageContent`, mediante bytes JPEG/PNG/WebP/GIF o URL HTTPS, según el modelo. Son entradas, no solicitudes de generación de imágenes.

Las trazas originales se conservan en los metadatos del historial, pero las solicitudes posteriores solo reenvían las entradas permitidas `message`, `function_call` y `function_call_output`; use `PreviousResponseId` para continuar el estado alojado completo del proveedor.

Las citas pueden identificar páginas u otras fuentes del proveedor. Las posiciones pertenecen a cada respuesta y parte de contenido, no al resultado Run concatenado. Conserve URL y título para mostrarlos y verificarlos; una fuente devuelta no valida por sí sola todas las afirmaciones generadas.

## Mantener una tarea larga en ejecución

Use ejecución en segundo plano del proveedor para que una investigación sobreviva a desconexiones temporales o pueda recuperarse luego por ID. Un `AIRun` local controla la ejecución cliente actual; la respuesta en segundo plano tiene un ciclo propio en el servidor. Dejar de leer el flujo solo termina la observación. Cancele explícitamente el trabajo remoto para detenerlo.

```csharp
var researcher = new PerplexityService(apiKey, httpClient)
    .UsePreset(PerplexityPreset.High);
var job = await researcher.StartBackgroundAsync(
    "Compara los métodos recientes de reciclaje de baterías y cita las fuentes.", cancellationToken: cancellationToken);
Console.WriteLine(job.Id);
var completed = await job.WaitForCompletionAsync(
    cancellationToken: cancellationToken);
if (completed.Status != "completed")
    throw new InvalidOperationException(completed.Error ?? completed.Status);
Console.WriteLine(completed.Text);
```

`StartBackgroundAsync` captura la entrada sin añadir historial y rechaza funciones locales activas o `Store = false`. `GetResponseAsync` consulta una vez; `WaitForCompletionAsync` consulta hasta un estado terminal. Guarde `Id` y `LastSequenceNumber`; reconecte con `ResumeBackgroundRun(id).StreamAsync(startingAfter: cursor)`. `CancelAsync` cancela el trabajo remoto; cancelar un token de lectura/consulta solo detiene esa operación cliente. `LastResponse` incluye texto, estado, uso, citas y `OutputJson`. Compruebe el estado terminal antes de usar la respuesta.

Use `ListFilesAsync` y `DownloadFileAsync(fileId)` para archivos del sandbox. El servicio también ofrece `GetAgentResponseAsync`, `GetResponseFilesAsync` y `GetResponseFileContentAsync`. Recuperan resultados de respuesta; no crean ni buscan almacenes vectoriales.

Para los skills de Office integrados, use la ruta en segundo plano de esta guía: `StartBackgroundAsync`, luego `WaitForCompletionAsync` / `GetResponseAsync` y los métodos de archivos. Las trazas de herramientas internas de esas respuestas pueden no distinguirse de las llamadas normales a funciones locales.

Enviar, recuperar, cancelar o reconectar un flujo en segundo plano no habilita `SteerAsync` durante la respuesta ni herramientas cliente asíncronas nativas. La reconexión observa una respuesta existente sin volver a enviar la tarea. Conserve el ID y el cursor del proveedor.

## Buscar sin generar una respuesta

`PerplexitySearchClient` obtiene páginas para su clasificación, interfaz u otro LLM. No llama a un modelo de respuesta ni cambia el historial de `PerplexityService`.

```csharp
var search = new PerplexitySearchClient(apiKey, httpClient);
var pages = await search.SearchAsync(
    "métodos de reciclaje de baterías",
    new PerplexitySearchOptions
    {
        MaxResults = 5,
        DomainFilter = new[] { "nature.com", "science.org" },
        ContentSize = PerplexitySearchContentSize.Medium
    }, cancellationToken);
foreach (var page in pages.Results)
    Console.WriteLine($"{page.Rank}: {page.Title} — {page.Url}");
```

`SearchAsync` acepta una o varias consultas. Incluye búsqueda Web/People, país, dominios, idiomas, rangos de publicación/actualización y antigüedad. Elija `ContentSize` o `MaxTokens` / `MaxTokensPerPage` explícitos, no ambos. Los resultados contienen posición, título, URL, extracto y fechas del proveedor. La posición indica el orden devuelto, no una puntuación de relevancia.

`ContentSize` solo se admite en búsquedas Web. Omítalo con People; el cliente rechaza esa combinación antes del envío.

## Usar vectores en su propio índice

Los embeddings estándar tratan cada pasaje por separado e implementan `IEmbeddingProvider`, por lo que encajan en el constructor RAG. Los contextuales conservan el orden de fragmentos y sus grupos documentales. Su API separada evita aplanar documentos sin relación.

| Constante del modelo | ID del proveedor | Dimensiones predeterminadas |
| --- | --- | --- |
| `Standard0_6B` | `pplx-embed-v1-0.6b` | 1024 |
| `Standard4B` | `pplx-embed-v1-4b` | 2560 |
| `Context0_6B` | `pplx-embed-context-v1-0.6b` | 1024 |
| `Context4B` | `pplx-embed-context-v1-4b` | 2560 |

```csharp
using Mythosia.AI.Rag;
using Mythosia.AI.Rag.Embeddings;

var rag = service.WithRag(builder => builder
    .AddText("Se aceptan devoluciones durante 30 días.", id: "returns")
    .UsePerplexityEmbedding(apiKey, httpClient));
string policy = await rag.GetCompletionAsync("¿Hasta cuándo puedo devolver una compra?");
```

```csharp
var contextual = new PerplexityContextualizedEmbeddingProvider(
    apiKey, httpClient, PerplexityEmbeddingModels.Context0_6B);
var documents = new[]
{
    new[] { "Se aceptan devoluciones durante 30 días.", "Conserve el recibo para solicitar un reembolso." },
    new[] { "El envío estándar tarda tres días.", "El envío urgente está disponible entre semana." }
};
var documentVectors = await contextual.GetDocumentEmbeddingsAsync(
    documents, cancellationToken);
float[] queryVector = await contextual.GetQueryEmbeddingAsync(
    "¿Hasta cuándo puedo devolver una compra?", cancellationToken);
```

Use el mismo modelo, dimensiones y codificación para documentos y consultas. `GetQueryEmbeddingAsync` envía una consulta como documento individual al mismo modelo contextual. Los resultados conservan el orden documental y de fragmentos, sin conexión automática al constructor RAG de entrada plana.

Las API float decodifican los vectores base64 signed-int8 y los normalizan para similitud. Las API binarias explícitas devuelven bits compactados y usan distancia de Hamming; nunca convierten implícitamente esos bits en coordenadas float. Las dimensiones completas son 1024 para 0.6B y 2560 para 4B; la reducción sigue los límites del proveedor. También se aplican límites de lotes, longitud, tokens totales y tasa de cuenta.

Métodos binarios: `GetBinaryEmbeddingAsync` / `GetBinaryEmbeddingsAsync`, y contextuales `GetBinaryDocumentEmbeddingsAsync` / `GetBinaryQueryEmbeddingAsync`. `PerplexityBinaryEmbedding` expone `Dimensions`, una copia mediante `ToArray()` y `HammingDistance`; menor distancia significa mayor similitud. Las dimensiones binarias son múltiplos de ocho. Máximo: 512 textos estándar, o 512 documentos y 16.000 fragmentos contextuales. El proveedor comprueba 32K tokens por texto/documento y 120K totales.

## Migrar el código Sonar existente

Esta versión retira deliberadamente el adaptador antiguo antes del cierre anunciado para el 27 de septiembre de 2026. `PerplexityService` llama a `/v1/agent`; `AIModels.Perplexity.Sonar` ahora significa `perplexity/sonar`. Se eliminan los auxiliares de búsqueda y tipos de respuesta propios de Sonar. Use completion/Run/citas comunes, ajustes Agent y `PerplexitySearchClient` para búsqueda independiente.

Mapa recomendado: Sonar → `Fast`, Sonar Pro → `Low`, Sonar Reasoning Pro → `Medium`, Sonar Deep Research → `High`. No se garantizan textos, costes ni comportamientos idénticos. Los ajustes dinámicos pueden cambiar; fije un modelo explícito o un perfil con versión cuando sea necesario.

No se admiten steering nativo, herramientas cliente asíncronas nativas ni `CachePreservation.Required`. Router/Gateway queda fuera de esta integración. La disponibilidad depende del proveedor, modelo y cuenta; esta guía no afirma que todas las combinaciones hayan superado pruebas reales de pago.

Los perfiles, skills personalizados y conectores requieren recursos previamente registrados en la cuenta. Sus formatos de solicitud tienen pruebas unitarias; no se han verificado llamadas reales exitosas con esos recursos.

[Agent API](https://docs.perplexity.ai/docs/agent-api/quickstart) · [Search API](https://docs.perplexity.ai/docs/search/quickstart) · [Embeddings](https://docs.perplexity.ai/docs/embeddings/quickstart) · [Migration](https://docs.perplexity.ai/docs/agent-api/migrate-from-sonar/how-to) · [2026-09-27](https://community.perplexity.ai/t/sonar-is-moving-to-the-agent-api/5802)
