# Agent (Bucle ReAct)

## ¿Por qué un Bucle de Agent?

Con la llamada de funciones normal, el modelo hace **una** llamada de función por solicitud. Pero muchas tareas del mundo real requieren **múltiples pasos** que el modelo debe planificar y ejecutar de forma autónoma:

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
service.FunctionCallingPolicy = new FunctionCallingPolicy
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

## Cómo Funciona

Cada paso:

1. El LLM recibe el objetivo + historial de conversación + definiciones de funciones
2. Si el LLM llama una función → ejecútala, agrega el resultado al historial
3. Si el LLM devuelve una respuesta de texto → el bucle termina, retorna esa respuesta
4. Si la cuenta de pasos llega a `maxSteps` → lanza `AgentMaxStepsExceededException`
