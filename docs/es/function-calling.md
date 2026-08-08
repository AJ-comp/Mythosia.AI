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
