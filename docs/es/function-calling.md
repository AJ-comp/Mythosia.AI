# Llamada de Funciones

Para una respuesta final con botón Detener, pase `cancellationToken` a `GetCompletionAsync`. Use Run para eventos de progreso o instrucciones adicionales compatibles. Consulte [cancelación](completions.md#completion-cancellation).

Para ajustes independientes y variantes reutilizables, use el [builder de solicitudes](request-building.md). Llame a `CreateRequest(...)` antes de `With...`. Las propiedades y métodos fluent del servicio conservan su comportamiento.

## ¿Por qué Usar Llamada de Funciones?

Los LLM solo pueden generar texto — no pueden consultar el tiempo, consultar una base de datos o llamar a una API por sí solos. **Sin** llamada de funciones, tendrías que analizar la intención del modelo manualmente:

```csharp
// ❌ Sin llamada de funciones — análisis manual de intención
var reply = await service.GetCompletionAsync("¿Cómo está el tiempo en Madrid?");
// reply = "Necesitaría verificar un servicio meteorológico."

// Tienes que descubrir que el usuario quiere el tiempo, extraer "Madrid", llamar a la API...
if (reply.Contains("tiempo"))
{
    var city = ExtractCity(reply); // regex frágil
    var weather = await weatherApi.GetAsync(city);
}
```

**Con** llamada de funciones, el modelo decide **cuándo** llamar tu código y **qué argumentos** pasar:

```csharp
// ✅ Con llamada de funciones — el modelo gestiona intención + extracción
var service = new OpenAIService(apiKey, http)
    .WithFunction(
        "get_weather",
        "Obtiene el tiempo actual para un lugar",
        ("location", "La ciudad y el país", required: true),
        (string location) => weatherApi.Get(location)
    );

var response = await service.GetCompletionAsync("¿Cómo está el tiempo en Madrid?");
```

## Ejemplo Rápido

```csharp
var service = new OpenAIService(apiKey, http)
    .WithFunction(
        "get_weather",
        "Obtiene el tiempo actual para un lugar",
        ("location", "La ciudad y el país", required: true),
        (string location) => $"El tiempo en {location} es soleado, 22°C"
    );

var response = await service.GetCompletionAsync("¿Cómo está el tiempo en Madrid?");
```

## Definir Funciones con Atributos

Para funciones más complejas, usa los atributos `[AiFunction]` y `[AiParameter]`:

```csharp
using Mythosia.AI.Attributes;
using Mythosia.AI.Extensions;

public sealed class ProductFunctions
{
    [AiFunction("search_products", "Busca en el catálogo de productos")]
    public string SearchProducts(
        [AiParameter("Consulta de búsqueda", required: true)] string query,
        [AiParameter("Número máximo de resultados")] int limit = 5)
    {
        // ... tu implementación
        return JsonSerializer.Serialize(results);
    }
}
```

Luego regístrala:

```csharp
service.WithFunctions(new ProductFunctions());
```

## Política de Llamada de Funciones

Controla cuándo el modelo puede llamar funciones:

```csharp
using Mythosia.AI.Models.Functions;

// Deja que el modelo decida (predeterminado)
service.FunctionCallMode = FunctionCallMode.Auto;

// Fuerza al modelo a siempre llamar una función
service.ForceFunctionName = "search_products";

// Desactiva la llamada de funciones
service.FunctionCallMode = FunctionCallMode.None;
```

[Claude Fable 5.1](fable-5-1.md) añade actualizaciones de progreso, instrucciones por turno y diagnósticos de vinculación del pensamiento desde `Mythosia.AI` 8.0.0 / `Mythosia.AI.Abstractions` 4.0.0. Mythos 5.1 requiere invitación. Ambos rechazan la selección forzada de herramientas.

## Registro Masivo desde una Clase

Registra todos los métodos anotados con `[AiFunction]` de un objeto de una vez:

```csharp
var tools = new MyTools();
service.WithFunctions(tools);  // escanea métodos de instancia con [AiFunction]
```

Para métodos estáticos:

```csharp
service.WithStaticFunctions<MyTools>();
```

## Handlers de Función Asincrónicos

Todos los sobrecargas de `WithFunction` tienen contrapartes `WithFunctionAsync` que aceptan `Func<..., Task<string>>`:

```csharp
service.WithFunctionAsync<string>(
    "fetch_data",
    "Obtiene datos de una API externa",
    ("url", "La URL a consultar", required: true),
    async (string url) =>
    {
        var result = await httpClient.GetStringAsync(url);
        return result;
    }
);
```

## Deshabilitar Funciones Temporalmente

Desactiva la llamada de funciones para una sola solicitud sin eliminar los registros:

```csharp
string answer = await service.AskWithoutFunctionsAsync("Responde directamente");

// O alterna la propiedad
service.WithoutFunctions();
```

## Usar FunctionBuilder

Construye definiciones de funciones de forma programática:

```csharp
using Mythosia.AI.Builders;
using Mythosia.AI.Extensions;

var fn = FunctionBuilder
    .Create("get_stock_price")
    .WithDescription("Devuelve el precio actual de una acción")
    .AddParameter("ticker", "string", "Símbolo del ticker", required: true)
    .WithHandler(args => FetchStockPrice(args["ticker"].ToString() ?? string.Empty))
    .Build();

service.WithFunction(fn);
```

<a id="tool-execution-contract"></a>

## Devolver objetos desde herramientas asíncronas y cancelar el trabajo

Una herramienta de archivos o bases de datos suele devolver un objeto tras una operación de E/S asíncrona. El botón Detener también debe llegar a la operación que sigue activa. Las funciones síncronas ya podían devolver objetos; esta actualización hace coherentes las respuestas asíncronas y registra las excepciones como errores.

Before: una herramienta asíncrona debía serializar su resultado. Devolver `Task<FileResult>` perdía el valor y entregaba solamente `"Success"`.

```csharp
using System.IO;
using System.Text.Json;
using System.Threading.Tasks;
using Mythosia.AI.Attributes;

public sealed class FileToolsBefore
{
    [AiFunction("read_file", "Leer un archivo de texto")]
    public async Task<string> ReadFileAsync(string path)
    {
        string text = await File.ReadAllTextAsync(path);
        return JsonSerializer.Serialize(new { Path = path, Text = text });
    }
}
```

After: devuelve el objeto directamente y pasa el token de cancelación inyectado a la operación de E/S. La aplicación no necesita nuevos contenedores de resultados ni adaptadores.

```csharp
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Mythosia.AI.Attributes;

public sealed record FileResult(string Path, string Text);

public sealed class FileTools
{
    [AiFunction("read_file", "Leer un archivo de texto")]
    public async Task<FileResult> ReadFileAsync(
        string path, CancellationToken cancellationToken)
    {
        string text = await File.ReadAllTextAsync(path, cancellationToken);
        return new FileResult(path, text);
    }
}
```

El registro con `[AiFunction]` admite objetos, `Task<T>` y `ValueTask<T>`; los valores que no son cadenas se convierten en JSON. `string`, `Task<string>` y `ValueTask<string>` conservan el texto sin comillas JSON adicionales. También se espera a `Task` y `ValueTask` sin resultado. Las devoluciones síncronas de objetos siguen funcionando. Un retorno null se convierte en `"Done"`; `Task` / `ValueTask` completados sin resultado se convierten en `"Success"`.

La biblioteca también reconoce valores asíncronos en tiempo de ejecución: espera un `Task<T>` devuelto como `Task` u `object`, o un `ValueTask<T>` devuelto como `object`, y serializa su resultado con las mismas reglas. Cada `ValueTask` se consume una sola vez.

La biblioteca proporciona el parámetro `CancellationToken` y lo excluye del esquema de argumentos del modelo. Registra los métodos con `WithFunctions(...)` o `WithStaticFunctions<T>()` en el servicio o en el constructor de solicitudes.

Los métodos de herramienta `async void` se rechazan al registrarlos. Devuelve `Task` o `ValueTask` para poder esperar su finalización, observar errores y completar la limpieza tras cancelar.

```csharp
using Mythosia.AI.Extensions;

await using var run = await service
    .CreateRequest("Lee report.txt y resúmelo.")
    .WithFunctions(new FileTools())
    .StartRunAsync(cancellationToken: cancellationToken);

string answer = (await run.Result).Text;
```

`run.Cancel()`, cancelar el token enviado a `StartRunAsync` o liberar un run activo llega a las herramientas locales que admiten cancelación. La función debe usar el token; no se puede detener por la fuerza código que lo ignora. Las llamadas aún no iniciadas se omiten y reciben resultados de cancelación; se espera a las funciones ya iniciadas para mantener emparejados los registros de llamada y resultado. El run cancelado no inicia otra ronda del modelo.

Si un callback de cancelación lanza una excepción al fallar el inicio de un run o al liberar una conexión MCP, se sigue intentando limpiar la sesión o el transporte. Se conservan el error original y los errores de limpieza, juntos en una `AggregateException` cuando es necesario. Las llamadas asíncronas simultáneas a `McpConnection.DisposeAsync()` esperan la misma limpieza. El transporte se cierra antes de esperar a que termine el bucle de lectura, permitiendo finalizar lecturas que necesitan el cierre de la conexión.

Para evitar que una llamada tardía a una herramienta quede esperando durante el cierre, al comenzar la liberación de la conexión se rechazan las nuevas operaciones `InitializeAsync`, `RefreshToolsAsync` y `CallToolAsync` con `ObjectDisposedException`. Si una respuesta tiene el ID esperado pero un cuerpo mal formado, se omite sin eliminar la solicitud pendiente: una respuesta válida posterior, la cancelación del llamador o la limpieza de la conexión aún pueden finalizar la llamada. Si la lectura ya terminó porque el servidor cerró el flujo o falló una lectura del transporte, las nuevas operaciones fallan con `McpException` en vez de esperar una respuesta que no puede llegar; cree una conexión nueva para continuar.

Los fallos reales deben lanzar una excepción. El ejecutor los registra con `FunctionCallResult.IsError = true`, en lugar de tratarlos como una cadena de éxito `"Error: ..."`. Una cadena devuelta intencionalmente sigue siendo un resultado normal. Los resultados cancelados tienen `IsCancelled = true` e `IsError = true`.

Para registrar mediante código, usa la sobrecarga de `WithHandler` con dos argumentos:

```csharp
using System.IO;
using Mythosia.AI.Builders;

var readText = FunctionBuilder.Create("read_text")
    .WithDescription("Leer un archivo de texto")
    .AddParameter("path", "string", "Ruta del archivo", required: true)
    .WithHandler(async (args, token) =>
        await File.ReadAllTextAsync(args["path"].ToString()!, token))
    .Build();
```

Los manejadores existentes de un argumento que devuelven cadenas siguen admitidos. Una definición directa puede asignar `Func<Dictionary<string, object>, CancellationToken, Task<string>>` a `HandlerWithCancellation`. Asignar `Handler` o `HandlerWithCancellation` sustituye el mismo manejador y no registra dos ejecuciones. Esta API de bajo nivel sigue devolviendo cadenas; la serialización automática de objetos corresponde al registro de métodos.

Este es el tratamiento de resultados y cancelación de funciones .NET locales; no requiere `AllowAsync` nativo del proveedor. Detener solo el lector `run.StreamAsync(token)` detiene la observación, no el run. Consulta la [guía de Run](execution-api-transition.md) y el [protocolo del proveedor](https://developers.openai.com/api/docs/guides/async-tool-calling).

## Llamadas asíncronas a herramientas

Una consulta lenta no tiene por qué detener toda la respuesta. Mientras se cargan los datos del tiempo, por ejemplo, el modelo puede explicar consejos generales de viaje que no dependen del resultado. Las llamadas asíncronas a herramientas permiten ese trabajo independiente; las afirmaciones que necesiten el resultado deben seguir esperando.

La compatibilidad con GPT-6 Astra y las llamadas asíncronas a herramientas están disponibles desde `Mythosia.AI` 7.1.0, con tipos compartidos en `Mythosia.AI.Abstractions` 3.1.0.

`FunctionDefinition.AllowAsync` es `false` por defecto. Actívalo con `true` o `FunctionBuilder.WithAsync()` solo cuando el modelo pueda seguir trabajando mientras se ejecuta esa función. `WithAsync(false)` lo desactiva. La misma definición y el mismo manejador se reutilizan entre proveedores.

El registro mediante atributos permite lo mismo: `[AiFunction("lookup", "Consultar datos", AllowAsync = true)]`.

```csharp
using System.Threading.Tasks;
using Mythosia.AI.Builders;
using Mythosia.AI.Extensions;
using Mythosia.AI.Models;

service.ChangeModel(AIModels.OpenAI.Gpt6Astra);
var weatherTool = FunctionBuilder.Create("get_demo_weather")
    .WithDescription("Devuelve un ejemplo del tiempo en Seúl")
    .WithAsync()
    .WithHandler(async _ =>
    {
        await Task.Delay(5000);
        return "{\"city\":\"Seoul\",\"temperature_c\":24,\"source\":\"demo\"}";
    })
    .Build();
service.WithFunction(weatherTool);
var answer = await service.GetCompletionAsync(
    "Consulta el ejemplo del tiempo en Seúl. Mientras tanto, indica tres cosas esenciales para viajar.");
```

Mythosia envía `async: true` para GPT-6 Astra mediante Responses. Con modelos y API no compatibles, omite ese campo y espera el resultado del mismo manejador sin cambiar `AllowAsync`. El proveedor también debe marcar la llamada real como asíncrona (`FunctionCall.IsAsync`); habilitar el permiso no garantiza la ejecución asíncrona.

`WithFunctionAsync` registra un manejador asíncrono de .NET y `FunctionExecutionMode.Parallel` controla la ejecución local de los manejadores. Ninguno activa automáticamente este permiso. `AllowAsync` permite al modelo continuar antes de recibir el resultado de la función. `FunctionExecutionMode` sigue controlando las llamadas normales. Los trabajos asíncronos habilitados pueden solaparse incluso en modo `Sequential` y comparten un límite independiente de trabajos establecido por `MaxConcurrency`.

Los trabajos pendientes pertenecen a la petición activa: `GetCompletionAsync`, el antiguo `service.StreamAsync` o un `AIRun` iniciado mediante `StartRunAsync`. Cada resultado se asocia posteriormente con su ID de llamada original. La finalización correcta de la petición o de `run.Result` espera el procesamiento de los resultados pendientes. No existe una sesión pública de trabajos en segundo plano independiente de la petición.

Al usar herramientas asíncronas, `GetCompletionAsync` devuelve al terminar la solicitud el texto independiente intermedio y el texto final acumulados en orden. `StreamAsync` entrega el texto de cada ronda conforme llega. `run.StreamAsync()` también transmite el texto conforme llega; `(await run.Result).Text` concatena todos los eventos de texto del run.

Las herramientas locales pueden devolver objetos mediante `Task<T>` / `ValueTask<T>` y recibir un `CancellationToken` inyectado. `run.Cancel()` o el token inicial llega a las herramientas cooperativas; detener solo el lector no. Las excepciones son errores. Al cancelar se omiten las llamadas pendientes y la limpieza espera las herramientas iniciadas que ignoran el token. Consulta [resultados, errores y cancelación](function-calling.md#tool-execution-contract).

En streaming, los manejadores comienzan tras confirmar llamadas de función completas y un límite válido de respuesta del proveedor. Después, la siguiente ronda del modelo puede avanzar mientras se ejecutan los trabajos asíncronos; las llamadas incompletas no inician la ejecución. Si el modelo no devuelve nuevas llamadas y quedan trabajos pendientes, Mythosia espera sus resultados. Los reintentos con resumen automático por exceso de contexto permanecen desactivados mientras haya llamadas pendientes para conservar las llamadas inacabadas en el historial.

Perplexity: [Controlar la investigación y las herramientas](perplexity.md).
