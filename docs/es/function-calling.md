# Llamada de Funciones

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

Al usar herramientas asíncronas, `GetCompletionAsync` devuelve al terminar la solicitud el texto independiente intermedio y el texto final acumulados en orden. `StreamAsync` entrega el texto de cada ronda conforme llega. `run.StreamAsync()` también transmite el texto conforme llega; `run.Result` concatena todos los eventos de texto del run.

Los manejadores no reciben tokens de cancelación. Tras una cancelación, un tiempo agotado o un error, la limpieza espera a los manejadores ya iniciados. Cerrar antes de tiempo el flujo antiguo del servicio termina su ejecución; terminar `run.StreamAsync()` solo detiene la observación. Para cancelar el run, usa `run.Cancel()` o libéralo. La integración cubre los manejadores registrados; consulta el [protocolo de la API](https://developers.openai.com/api/docs/guides/async-tool-calling) y la [guía de Run](execution-api-transition.md).

En streaming, los manejadores comienzan tras confirmar llamadas de función completas y un límite válido de respuesta del proveedor. Después, la siguiente ronda del modelo puede avanzar mientras se ejecutan los trabajos asíncronos; las llamadas incompletas no inician la ejecución. Si el modelo no devuelve nuevas llamadas y quedan trabajos pendientes, Mythosia espera sus resultados. Los reintentos con resumen automático por exceso de contexto permanecen desactivados mientras haya llamadas pendientes para conservar las llamadas inacabadas en el historial.
