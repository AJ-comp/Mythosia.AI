# AIRequestContext

## ¿Qué Es?

`AIRequestContext` permite modificar **lo que el modelo ve** para una única solicitud — inyectar instrucciones adicionales, añadir documentos de referencia o reemplazar completamente el mensaje del usuario — sin cambiar permanentemente el mensaje de sistema ni el historial de conversación del servicio.

## El Problema que Resuelve

Considera un pipeline RAG que recupera documentos relevantes y necesita incluirlos en el prompt. **Sin** `AIRequestContext`, tendrías que modificar el mensaje de sistema directamente — contaminando el historial y causando condiciones de carrera en aplicaciones multiusuario.

**Con** `AIRequestContext`, la inyección queda acotada a exactamente una solicitud:

```csharp
// ✅ Con AIRequestContext — limpio, acotado, sin efectos secundarios
var answer = await service.GetCompletionAsync(userQuestion,
    context: new AIRequestContext
    {
        SystemMessageSuffix = $"\n\nUsa el siguiente contexto para responder:\n{retrievedDocs}"
    });
```

## Propiedades Disponibles

### SystemMessagePrefix

Antepone texto al mensaje de sistema solo para esta solicitud:

```csharp
var context = new AIRequestContext
{
    SystemMessagePrefix = "La fecha de hoy es 2026-03-31.\n"
};
```

**Cuándo usar:** Inyectar metadatos dinámicos (fecha, zona horaria del usuario, información de sesión).

### SystemMessageSuffix

Agrega texto al final del mensaje de sistema solo para esta solicitud:

```csharp
var context = new AIRequestContext
{
    SystemMessageSuffix = "\nResponde siempre en español."
};
```

**Cuándo usar:** Agregar instrucciones de comportamiento por solicitud, contexto RAG o preferencias de idioma.

### AdditionalMessages

Inserta mensajes adicionales en la conversación solo para esta solicitud:

```csharp
var context = new AIRequestContext
{
    AdditionalMessages = new List<Message>
    {
        MessageBuilder.Create().AddText("Doc de referencia: La política de devolución permite retornos en 30 días.").Build()
    }
};
```

### RequestMessageOverride

Reemplaza completamente el mensaje del usuario para esta solicitud:

```csharp
var context = new AIRequestContext
{
    RequestMessageOverride = MessageBuilder
        .User($"Basándote en el siguiente contexto, responde la pregunta.\n\nContexto: {docs}\n\nPregunta: {userQuery}")
        .Build()
};
```

## Combinar con AIRequestProfile

Ambos pueden pasarse juntos para control máximo sobre una única solicitud:

```csharp
var response = await service.GetCompletionAsync(
    prompt,
    profile: new AIRequestProfile { Temperature = 0.1f, Stateless = true },
    context: new AIRequestContext
    {
        SystemMessageSuffix = $"\nContexto:\n{docs}",
        AdditionalMessages = new List<Message>
        {
            MessageBuilder.Create().AddText("Ejemplo: ...").Build()
        }
    }
);
```

Consulta [AIRequestProfile](request-profiles.md) para detalles sobre cómo sobrescribir parámetros de generación.

## Inyección automática con `SystemMessageProvider`

### El problema que resuelve

Una app de chat típica tiene varios puntos de entrada al LLM que necesitan la misma baseline — fecha de hoy, carpeta activa, info de sesión. **Sin** `SystemMessageProvider`, cada punto de llamada tiene que acordarse de construir y pasar ese contexto:

```csharp
// ❌ Sin SystemMessageProvider — cada punto de entrada debe recordar inyectar
var today = $"Today is {DateTime.UtcNow:yyyy-MM-dd}.";

// 1. Respuesta principal del chat
var answer = await service.GetCompletionAsync(userMessage,
    context: new AIRequestContext { SystemMessageSuffix = today });

// 2. Generador de títulos (añadido después)
var title = await service.GetCompletionAsync("Summarize as a title: " + conversation,
    context: new AIRequestContext { SystemMessageSuffix = today });

// 3. Resumidor (añadido aún más tarde)
var summary = await service.GetCompletionAsync("Summarize: " + conversation,
    context: new AIRequestContext { SystemMessageSuffix = today });

// 4. Llamada al agent — ¡fácil de olvidar! El compilador no te avisa
var agentResult = await service.RunAgentAsync(goal);  // ← falta fecha, bug silencioso
```

Problemas de este enfoque:

- El mismo snippet de construcción de contexto se **duplica** en cada punto de llamada
- Los nuevos puntos de entrada (el `RunAgentAsync` arriba) son **fáciles de omitir** — no hay verificación en tiempo de compilación
- Cada nueva característica que añade otra llamada al LLM tiene que recordar la convención
- Los tests tienen que replicar el setup de contexto en cada punto de llamada

Con `SystemMessageProvider`, registras la baseline **una vez** y cada llamada saliente la recoge automáticamente:

```csharp
// ✅ Con SystemMessageProvider — registrar una vez, aplicado en todas partes
service.WithSystemMessageProvider(() => new AIRequestContext
{
    SystemMessageSuffix = $"Today is {DateTime.UtcNow:yyyy-MM-dd}."
});

// Todas estas reciben automáticamente la baseline — sin boilerplate por llamada
var answer      = await service.GetCompletionAsync(userMessage);
var title       = await service.GetCompletionAsync("Summarize as a title: " + conversation);
var summary     = await service.GetCompletionAsync("Summarize: " + conversation);
var agentResult = await service.RunAgentAsync(goal);  // ← también recibe la baseline

// Los puntos de entrada streaming también — misma baseline, sin boilerplate por llamada
await foreach (var chunk in service.StreamAsync(userMessage)) { /* ... */ }
await foreach (var token in service.RunAgentStreamAsync(goal)) { /* ... */ }
```

### Cómo funciona

Registra el callback una vez mediante el helper fluent `WithSystemMessageProvider`. Cada llamada saliente (`GetCompletionAsync`, `StreamAsync`, `RunAgentAsync`, `RunAgentStreamAsync`) lo invoca automáticamente para construir un contexto base:

```csharp
// Típicamente en la construcción del servicio / configuración DI
service.WithSystemMessageProvider(() => new AIRequestContext
{
    SystemMessageSuffix =
        $"Today is {DateTime.UtcNow:yyyy-MM-dd}.\n" +
        $"Current folder: {_uiContext.CurrentFolder}"
});

var answer = await service.GetCompletionAsync(userQuery);
await foreach (var chunk in service.StreamAsync(msg, options)) { /* ... */ }
var agentResult = await service.RunAgentAsync(goal);
```

### Sobrecarga async para providers basados en IO

Cuando el contexto base proviene de una base de datos, caché o llamada HTTP, usa la sobrecarga async para que el provider no tenga que bloquearse con `.Result` / `.GetAwaiter().GetResult()`. La resolución de sobrecarga elige la correcta por la arity de la lambda — sin argumento para sync, un `CancellationToken` para async:

```csharp
service.WithSystemMessageProvider(async ct =>
{
    var prefs = await _db.UserPreferences.FirstOrDefaultAsync(ct);
    return new AIRequestContext
    {
        SystemMessageSuffix = $"User language: {prefs?.Language ?? "en"}"
    };
});
```

Las rutas sin streaming (`GetCompletionAsync`, `RunAgentAsync`) no admiten cancelación por diseño — sus firmas no aceptan un `CancellationToken` y siempre se pasa `CancellationToken.None` al provider. Si tu provider necesita cancelación (p. ej. una consulta DB larga), usa las rutas de streaming (`StreamAsync`, `RunAgentStreamAsync`), que propagan el token del llamador hasta el callback del provider.

### Fusión con un contexto per-call explícito

Cuando una llamada tiene un provider registrado **y** también pasa un `AIRequestContext` explícito, los dos se fusionan campo por campo:

| Campo | Regla de fusión |
|---|---|
| `SystemMessagePrefix` | explícito gana si non-null, si no provider |
| `SystemMessageSuffix` | explícito gana si non-null, si no provider |
| `RequestMessageOverride` | explícito gana si non-null, si no provider |
| `AdditionalMessages` | concatenados (primero provider, luego explícito) |

Motivación: el caso común es "el provider aporta una base, una llamada específica quiere reemplazar un campo escalar o añadir mensajes extra" — el override a nivel de campo mantiene la semántica predecible sin concatenación sorprendente.

### Invocación por llamada

El provider se invoca **una vez por petición**, de modo que los valores de retorno pueden reflejar el estado en ese mismo instante (timestamp, sesión, etc.). Devolver `null` es un no-op — idéntico a dejar `SystemMessageProvider` sin configurar para esa llamada.

### En resumen: cuándo elegir esta herramienta — la intersección de tres condiciones

Dando un paso atrás respecto a los ejemplos y las reglas de fusión anteriores, `SystemMessageProvider` es la herramienta dedicada cuando **tres condiciones se cumplen simultáneamente**:

1. **Debe haber una base común en cada llamada al LLM** — no se quiere recordar la inyección en cada punto de entrada
2. **El valor debe evaluarse dinámicamente en el momento de la llamada** — hora actual, carpeta activa, usuario conectado y otros valores que no se pueden fijar al arranque
3. **El estado permanente (`SystemMessage`, historial de conversación) no debe contaminarse** — el valor no debe filtrarse a llamadas posteriores

Si falta alguna de las tres condiciones, una herramienta más simple es la respuesta correcta:

| Situación | Herramienta correcta | Razón |
|---|---|---|
| La base es **fija (no cambia)** durante toda la sesión | `service.SystemMessage = "..."` | Una asignación única es suficiente, no se necesita provider |
| **Solo una llamada específica** necesita tratamiento especial | Pasar `AIRequestContext` explícitamente en el punto de llamada | No es una base compartida, es una inyección puntual |
| Compartida + dinámica + sin contaminación **(las tres)** | **`SystemMessageProvider`** | La herramienta dedicada para esta intersección triple |

#### Por qué esto no entra en conflicto con el principio de "uso único" de `AIRequestContext`

La esencia de `AIRequestContext` no es "usado solo una vez" sino **"nunca contamina el estado permanente"**. `SystemMessageProvider` es una factoría que **re-ejecuta el callback en cada petición**, produciendo **un nuevo `AIRequestContext` acotado a esa petición**. El contexto resultante sigue siendo per-request scoped, el valor nunca se filtra al historial de conversación, y en la siguiente llamada el callback vuelve a ejecutarse reflejando el valor **de ese momento**. Así que el provider no viola el principio de diseño de `AIRequestContext` — simplemente **lo automatiza**.

En concreto, registrar el provider de abajo **no** modifica `service.SystemMessage` ni `service.ActivateChat.Messages`:

```csharp
service.WithSystemMessageProvider(() => new AIRequestContext
{
    SystemMessageSuffix = $"Today is {DateTime.UtcNow:yyyy-MM-dd}"
});
```

- Una vez pasada la medianoche, la re-ejecución del provider en la siguiente llamada refleja automáticamente la **nueva fecha** (no es estático)
- Una semana después, al abrir el historial de conversación no se encuentra "Today is ..." incrustado en peticiones pasadas
- Incluso cuando se usa un servicio compartido en un entorno multi-usuario, cada llamada produce su propio contexto independiente

> Disponible en Mythosia.AI v6.3.0+.
