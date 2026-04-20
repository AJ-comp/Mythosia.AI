# Conceptos básicos

Esta página reúne los conceptos fundamentales que aparecen referenciados en el resto de la documentación. Se irán añadiendo más conceptos con el tiempo.

## ¿Qué es un round?

> [!NOTE]
> Un **round** es un viaje completo de ida y vuelta entre tu aplicación y el modelo: tu app envía un prompt, el modelo responde, y ese intercambio constituye un round. Un mensaje de chat simple es 1 round. El function calling y los agentes pueden encadenar varios rounds para un solo mensaje del usuario.

### El caso más sencillo: 1 round

En un mensaje de chat normal toda la conversación ocurre en un único round.

```
app  →  "¿Cuánto es 2 + 2?"    →  modelo
app  ←  "Es 4."                 ←  modelo
```

`RoundUsage` se dispara una vez con los tokens de este round. `Completion.Usage` se dispara al final del stream con el mismo total, ya que solo hay un round.

### Varios rounds: function calling

Los rounds se multiplican cuando el modelo no puede responder por sí solo. Supongamos que un usuario pregunta *«¿Qué tiempo hace ahora en Madrid?»* — el modelo no tiene acceso al tiempo en vivo, así que tiene que llamar a una herramienta.

**Round 1 — el modelo decide llamar a una herramienta**

Tu app envía el mensaje del usuario junto con la lista de herramientas registradas (por ejemplo `GetWeather`) al modelo. El modelo ve esta conversación:

```
system: Eres un asistente meteorológico. Puedes llamar a GetWeather(city).
user:   ¿Qué tiempo hace ahora en Madrid?
```

En lugar de escribir una respuesta final, el modelo devuelve una **solicitud de llamada a herramienta**:

```
tool_call: GetWeather(city="Madrid")
```

El turno del modelo termina y el round 1 también. `RoundUsage` se dispara con los tokens consumidos en el round 1. **Todavía no hay respuesta final para el usuario.**

**Entre rounds — tu app ejecuta la función**

Este paso **no** es una llamada LLM. El runtime de Mythosia.AI invoca tu implementación registrada de `GetWeather` y recibe `«15°C, nublado»`. No se consumen tokens.

**Round 2 — el modelo escribe la respuesta final**

Tu app añade el resultado de la herramienta a la conversación y llama al modelo **por segunda vez**. El modelo ve ahora:

```
system:      Eres un asistente meteorológico. Puedes llamar a GetWeather(city).
user:        ¿Qué tiempo hace ahora en Madrid?
assistant:   [llamó a GetWeather(city="Madrid")]
tool_result: 15°C, nublado
```

Con la información que necesitaba, el modelo escribe texto:

```
En Madrid hay actualmente 15°C y está nublado.
```

El round 2 termina. `RoundUsage` se dispara por segunda vez — esta vez solo con los tokens del round 2 (la entrada suele ser mayor que la del round 1 porque la conversación se ha alargado). Cuando el stream se cierra, `Completion.Usage` se dispara una vez con la **suma del round 1 y el round 2**.

### De un vistazo

| Paso | ¿Llamada LLM? | Qué ocurre | Evento |
|---|---|---|---|
| Round 1 | ✅ | El modelo decide llamar a `GetWeather` | `RoundUsage` (`RoundIndex=1`) |
| Entre rounds | ❌ | La app ejecuta la función, recibe `«15°C, nublado»` | `FunctionCall`, `FunctionResult` |
| Round 2 | ✅ | El modelo ve el resultado y escribe la respuesta final | `RoundUsage` (`RoundIndex=2`, `IsFinalRound=true`) |
| Fin del stream | — | — | `Completion` (Usage = round 1 + round 2) |

### Más herramientas implica más rounds

Si el modelo necesita encadenar varias llamadas a herramientas, los rounds se suman. Para *«Compara el tiempo en Madrid y Barcelona»*:

1. **Round 1** — el modelo llama a `GetWeather("Madrid")`
2. La app lo ejecuta → `«15°C, nublado»`
3. **Round 2** — el modelo ve el resultado y también llama a `GetWeather("Barcelona")`
4. La app lo ejecuta → `«18°C, soleado»`
5. **Round 3** — el modelo combina ambos resultados en la respuesta final

Tres rounds en total, y `Completion.Usage` suma los tres. Un medidor de contexto en la UI debería usar `RoundUsage.TotalTokens` del último round — en este ejemplo, el del round 3.
