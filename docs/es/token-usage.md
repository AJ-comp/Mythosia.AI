# Uso de tokens

El uso de tokens indica cuánto ha consumido una petición del modelo en entrada, salida, caché y razonamiento. En Mythosia.AI se expone mediante `TokenUsage` dentro de los eventos de streaming.

Esto se vuelve especialmente importante cuando una respuesta no termina en una sola llamada al LLM. Una respuesta normal suele tener un único round. Un agente o un flujo con function calling puede llamar al modelo, ejecutar una herramienta y volver a llamar al modelo con el resultado. Por eso conviene separar dos valores.

- `RoundUsage` muestra el uso de un único round del LLM.
- `Completion.Usage` muestra el uso acumulado de todo el stream.

> [!NOTE]
> Esta página asume que ya sabes qué es un **round de LLM**. En resumen: un round = un intercambio petición–respuesta entre tu app y el modelo. Los flujos de function calling pueden producir varios rounds por cada mensaje de usuario. Para una explicación paso a paso, consulta [Conceptos básicos — ¿Qué es un round?](core-concepts.md#qué-es-un-round).

## Por qué importa

Para un medidor de contexto en una UI de chat, normalmente quieres el último `RoundUsage.Usage.TotalTokens`. Es el valor más cercano a "cuánto ocuparía la próxima entrada del modelo si la conversación continuara ahora".

Para logs, diagnóstico y análisis de costes, usa `Completion.Usage.TotalTokens`. Ese valor sigue siendo acumulado para todo el run, aunque haya varios rounds por function calling o agentes.

Para ajustar rendimiento, los campos de caché y razonamiento ayudan a ver si el proveedor reutilizó entrada en caché o gastó tokens adicionales en razonamiento interno.

## Modelo de eventos

| Evento | Significado | Uso recomendado |
|---|---|---|
| `StreamingContentType.RoundUsage` | Uso del round del LLM que acaba de terminar | Medidor de contexto, depuración por round |
| `StreamingContentType.Completion` | Evento final con uso acumulado | Logs, diagnóstico, informes de coste |

`RoundUsage.Usage` no es acumulado. Si el round 1 usa 10 100 tokens y el round 2 usa 14 000, `Completion.Usage.TotalTokens` puede ser 24 100, mientras que el último `RoundUsage.Usage.TotalTokens` sigue siendo 14 000.

| Propiedad | Significado |
|---|---|
| `RoundIndex` | Número del round del LLM, empezando en 1 |
| `IsFinalRound` | `true` si este es el último round del stream |

El uso de tokens se emite cuando el proveedor devuelve datos de usage. No hace falta activar `IncludeMetadata = true` para recibir estos eventos.

## Uso acumulado final

Usa `Completion.Usage` cuando quieras el total de toda la petición en streaming.

```csharp
await foreach (var chunk in service.StreamAsync("Explica la computación cuántica", StreamOptions.FullOptions))
{
    if (chunk.Type == StreamingContentType.Text)
        Console.Write(chunk.Content);

    if (chunk.Type == StreamingContentType.Completion && chunk.Usage is not null)
    {
        Console.WriteLine($"Input:  {chunk.Usage.InputTokens}");
        Console.WriteLine($"Output: {chunk.Usage.OutputTokens}");
        Console.WriteLine($"Total:  {chunk.Usage.TotalTokens}");
    }
}
```

En un único round del LLM, este valor suele estar muy cerca del `RoundUsage`. En un agente, suma todos los rounds del LLM.

## Medidor de tokens en la UI

Para un medidor de tamaño de contexto, usa el último `RoundUsage`.

```csharp
await foreach (var chunk in service.StreamAsync(message, StreamOptions.FullOptions))
{
    if (chunk.Type == StreamingContentType.RoundUsage && chunk.Usage is not null)
    {
        UpdateContextTokenMeter(chunk.Usage.TotalTokens);

        if (chunk.IsFinalRound)
            MarkTokenMeterAsFinal();

        continue;
    }

    if (chunk.Type == StreamingContentType.Text)
        AppendToChat(chunk.Content);
}
```

El último round del modelo ve el estado más reciente de la conversación, incluidos los resultados de herramientas que se hayan añadido durante el run. Por eso el último `RoundUsage.TotalTokens` es el mejor valor para una UI de chat.

## Function Calling y agentes

En flujos con function calling, el modelo puede ejecutarse varias veces. Lee cada `RoundUsage`, conserva el último para la UI y usa `Completion.Usage` al final para el total.

```csharp
TokenUsage? latestRound = null;
TokenUsage? cumulative = null;

await foreach (var chunk in service.StreamAsync(message, StreamOptions.WithFunctions))
{
    if (chunk.Type == StreamingContentType.FunctionCall)
    {
        Console.WriteLine($"Calling function: {chunk.Content}");
        continue;
    }

    if (chunk.Type == StreamingContentType.FunctionResult)
    {
        Console.WriteLine($"Function result: {chunk.Content}");
        continue;
    }

    if (chunk.Type == StreamingContentType.RoundUsage && chunk.Usage is not null)
    {
        latestRound = chunk.Usage;
        Console.WriteLine($"Round {chunk.RoundIndex}: {latestRound.TotalTokens} tokens");
        continue;
    }

    if (chunk.Type == StreamingContentType.Completion)
        cumulative = chunk.Usage;
}

Console.WriteLine($"UI meter: {latestRound?.TotalTokens}");
Console.WriteLine($"Run total: {cumulative?.TotalTokens}");
```

## Caché y razonamiento

Cuando el proveedor los ofrece, `TokenUsage` también incluye campos de caché y razonamiento.

| Propiedad | Significado |
|---|---|
| `InputTokens` | Tokens del prompt o entrada |
| `OutputTokens` | Tokens generados por el modelo |
| `TotalTokens` | Entrada + salida dentro del alcance del evento |
| `CachedInputTokens` | Tokens de entrada servidos desde caché |
| `CacheCreationTokens` | Tokens escritos en caché |
| `ReasoningTokens` | Tokens usados en razonamiento oculto |
| `VisibleOutputTokens` | Tokens de salida sin contar razonamiento |

## Por qué usar los eventos normalizados

Cada proveedor adjunta los datos de usage a chunks distintos. El caso con más matices es Gemini: el usage puede venir en chunks de texto o de estado, e incluso llegar después de un chunk de function call — por eso la librería sigue leyendo el stream lo suficiente para capturar ese usage antes de pasar al siguiente round. Mythosia.AI absorbe estas diferencias entre proveedores y las normaliza en eventos `RoundUsage` y `Completion.Usage`, así que en el código consumidor, en lugar de parsear metadata específica de cada proveedor, usa los eventos normalizados `RoundUsage` y `Completion.Usage`.
