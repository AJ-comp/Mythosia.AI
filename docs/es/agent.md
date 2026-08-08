# Agent (Bucle ReAct)

## ¿Por qué un Bucle de Agent?

La llamada de funciones normal puede ejecutar **varias funciones de una respuesta del modelo como un lote ordenado** y continuar durante más rondas de herramientas. La API de Agent empaqueta ese mecanismo como un bucle ReAct orientado a objetivos con un **límite de pasos** explícito y devuelve al modelo los resultados de cada lote hasta que produce una respuesta final:

- "Investiga las 3 principales empresas de IA y compara sus precios de acciones" — requiere múltiples búsquedas
- "Encuentra la política relevante, verifica el estado del pedido y dime si tengo derecho al reembolso" — requiere encadenar herramientas lógicamente
- El modelo puede necesitar **reintentar o refinar** una búsqueda si el primer resultado es insuficiente

El **bucle de agent** (patrón ReAct: Razonar → Actuar → Observar → Repetir) maneja todo esto automáticamente.

## Uso Básico

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
    MaxRounds = 10,
    TimeoutSeconds = 30
};

// O mediante métodos de extensión:
service.WithMaxRounds(15).WithTimeout(60);
```

Políticas predefinidas:

```csharp
service.WithFastPolicy();    // Bajo timeout, menos rondas — tareas rápidas
service.WithComplexPolicy(); // Mayor timeout, más rondas — investigación profunda
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

El contexto se propaga a través de `AsyncLocal`, por lo que las ejecuciones concurrentes de agent en la misma instancia de servicio no interfieren entre sí.

Consulta [AIRequestContext](request-contexts.md) para la lista completa de propiedades disponibles (`SystemMessagePrefix`, `SystemMessageSuffix`, `AdditionalMessages`, `RequestMessageOverride`).

> Disponible desde Mythosia.AI v6.3.0.

## Cómo Funciona

Cada paso:

1. El LLM recibe el objetivo + historial de conversación + definiciones de funciones
2. Si el LLM llama una función → ejecútala, agrega el resultado al historial
3. Si el LLM devuelve una respuesta de texto → el bucle termina, retorna esa respuesta
4. Si la cuenta de pasos llega a `maxSteps` → lanza `AgentMaxStepsExceededException`
