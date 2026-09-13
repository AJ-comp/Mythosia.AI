# Agent (Bucle ReAct)

Para obtener respuesta, uso y fuentes juntos, `await run.Result` devuelve una instantánea `AIRunResult`. La cadena está en `result.Text`, sin leer el flujo. Es un cambio de Mythosia.AI 8.0.0; `GetCompletionAsync` y `StructuredStreamRun<T>.Result` mantienen sus tipos de retorno. [Resultado Run y migración](execution-api-transition.md#run-result).


Para ajustes independientes y variantes reutilizables, use el [builder de solicitudes](request-building.md). Llame a `CreateRequest(...)` antes de `With...`. Las propiedades y métodos fluent del servicio conservan su comportamiento.

> Los ejemplos con `CreateRequest` requieren Mythosia.AI 8.0.0 / Abstractions 4.0.0. La versión 7.1 que introdujo Run y las opciones comunes no incluye el builder. Los paquetes anteriores pueden usar las sobrecargas del servicio.

Buscar una política y comprobar un pedido puede requerir varias llamadas a herramientas. La [guía de Run](execution-api-transition.md) muestra cómo seguir ese trabajo, cancelarlo y añadir instrucciones cuando el modelo lo admite.

## ¿Por qué un Bucle de Agent?

Algunas preguntas requieren varias fuentes: el modelo elige una herramienta, examina su resultado y puede solicitar otras herramientas. El bucle común entre modelo y herramientas repite esos pasos hasta obtener la respuesta; un límite de rondas acota la ejecución:

- "Investiga las 3 principales empresas de IA y compara sus precios de acciones" — requiere múltiples búsquedas
- "Encuentra la política relevante, verifica el estado del pedido y dime si tengo derecho al reembolso" — requiere encadenar herramientas lógicamente
- El modelo puede necesitar **reintentar o refinar** una búsqueda si el primer resultado es insuficiente

`GetCompletionAsync` y `StartRunAsync` ya ejecutan el bucle compartido entre modelo y herramientas. Los métodos de agente anteriores añaden un límite de rondas por llamada y una traducción de errores específica, no un planificador ni un motor de ejecución independiente.

## Iniciar una tarea con herramientas mediante Run

```csharp
// Registra las funciones en el servicio antes de iniciar la tarea.
await using var run = await service
    .CreateRequest("Busca la política, comprueba el pedido y explica el resultado.")
    .WithMaxRounds(10)
    .StartRunAsync(
        onText: text => Console.Write(text),
        cancellationToken: cancellationToken);
string answer = (await run.Result).Text;
```

Las herramientas locales pueden devolver objetos mediante `Task<T>` / `ValueTask<T>` y recibir un `CancellationToken` inyectado. `run.Cancel()` o el token inicial llega a las herramientas cooperativas; detener solo el lector no. Las excepciones son errores. Al cancelar se omiten las llamadas pendientes y la limpieza espera las herramientas iniciadas que ignoran el token. Consulta [resultados, errores y cancelación](function-calling.md#tool-execution-contract).

## API anterior de agente: ejemplos de compatibilidad

Los ejemplos siguientes documentan `RunAgentAsync` y `RunAgentStreamAsync`, que siguen siendo invocables con advertencias `[Obsolete]`. Las nuevas llamadas pueden utilizar `StartRunAsync`; la [guía de Run](execution-api-transition.md) detalla las diferencias en límites de rondas y tratamiento de errores.

Registra funciones y llama a `RunAgentAsync` con un objetivo:

```csharp
var service = new OpenAIService(apiKey, http)
    .WithFunction(
        "search_web",
        "Busca información en la web",
        ("query", "Consulta de búsqueda", required: true),
        query => WebSearch(query)
    )
    .WithFunction(
        "get_stock_price",
        "Obtiene el precio actual de una acción",
        ("ticker", "Símbolo del ticker", required: true),
        ticker => FetchPrice(ticker)
    );

string result = await service.RunAgentAsync(
    goal: "¿Cuál es el precio actual de las acciones de las 3 principales empresas de IA?",
    maxSteps: 10
);

Console.WriteLine(result);
```

## maxSteps

`maxSteps` limita el número de rondas LLM→llamada de función. Si el agent no termina dentro del límite, se lanza `AgentMaxStepsExceededException`:

```csharp
try
{
    string result = await service.RunAgentAsync("Investiga y resume...", maxSteps: 5);
}
catch (AgentMaxStepsExceededException ex)
{
    Console.WriteLine($"Detenido: {ex.PartialResponse}");
}
```

## FunctionCallingPolicy

Controla el comportamiento del bucle de agent por ronda:

```csharp
service.DefaultPolicy = new FunctionCallingPolicy
{
    TimeoutSeconds = 30
};

// RunAgentAsync usa DefaultPolicy y el argumento explícito maxSteps.
service.DefaultPolicy.TimeoutSeconds = 60;
var policyResult = await service.RunAgentAsync(
    "Investiga y resume...", maxSteps: 15);
```

Políticas predefinidas:

```csharp
service.DefaultPolicy = FunctionCallingPolicy.Fast;    // Bajo timeout, menos rondas — tareas rápidas
var fastResult = await service.RunAgentAsync(
    "Investiga y resume...", maxSteps: service.DefaultPolicy.MaxRounds);
service.DefaultPolicy = FunctionCallingPolicy.Complex; // Mayor timeout, más rondas — investigación profunda
var complexResult = await service.RunAgentAsync(
    "Investiga y resume...", maxSteps: service.DefaultPolicy.MaxRounds);
```

## Contexto de solicitud por llamada

`RunAgentAsync` y `RunAgentStreamAsync` aceptan un `AIRequestContext` opcional para inyectar un prefix/suffix dinámico en el system message, documentos de referencia, o reemplazar el mensaje del objetivo — **limitado a una única ejecución del agent**, sin modificar el system message del servicio ni el historial de conversación.

```csharp
string result = await service.RunAgentAsync(
    goal: "Encuentra la política de reembolso y verifica si el pedido #1234 califica.",
    maxSteps: 10,
    context: new AIRequestContext
    {
        SystemMessagePrefix = $"La fecha de hoy es {DateTime.UtcNow:yyyy-MM-dd}.\n",
        SystemMessageSuffix = "\nSiempre cita la sección de la política que utilizaste."
    });
```

La variante de streaming acepta el mismo parámetro:

```csharp
await foreach (var content in service.RunAgentStreamAsync(
    goal: "Investiga los precios de las acciones de las 3 principales empresas de IA.",
    maxSteps: 10,
    options: StreamOptions.WithFunctions,
    context: new AIRequestContext
    {
        SystemMessagePrefix = $"Zona horaria del usuario: {userTz}\n"
    }))
{
    // manejar contenido
}
```

`AIRequestContext` se propaga mediante `AsyncLocal`, pero esto no permite modificar de forma segura el historial y las políticas del servicio durante operaciones simultáneas. Usa instancias separadas para tareas concurrentes independientes.

Consulta [AIRequestContext](request-contexts.md) para la lista completa de propiedades disponibles (`SystemMessagePrefix`, `SystemMessageSuffix`, `AdditionalMessages`, `RequestMessageOverride`).

> Disponible desde Mythosia.AI v6.3.0.

## Cómo Funciona

Cada paso:

1. El LLM recibe el objetivo + historial de conversación + definiciones de funciones
2. Si el LLM llama una función → ejecútala, agrega el resultado al historial
3. Si el LLM devuelve una respuesta de texto → el bucle termina, retorna esa respuesta
4. Si la cuenta de pasos llega a `maxSteps` → lanza `AgentMaxStepsExceededException`
