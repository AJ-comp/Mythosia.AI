# Gestión de Conversaciones

## Cómo Funciona el Historial de Conversación

Cada llamada a `GetCompletionAsync` o `StreamAsync` se añade a la lista de mensajes interna del servicio. Esto significa que el modelo tiene contexto de todos los turnos anteriores.

```csharp
await service.GetCompletionAsync("Mi color favorito es el azul.");
var reply = await service.GetCompletionAsync("¿Cuál es mi color favorito?");
// → "Tu color favorito es el azul."
```

Para empezar de cero:

```csharp
service.ActivateChat.ClearMessages();
```

## Política de Resumen

### ¿Por qué Resumir Automáticamente?

Cada mensaje en el historial de conversación se envía al modelo en cada solicitud. A medida que las conversaciones crecen, esto crea dos problemas:

1. **Costo** — historiales más largos significan más tokens de entrada facturados por solicitud
2. **Desbordamiento de contexto** — una vez que el historial supera la ventana de contexto del modelo, las solicitudes fallan

**`SummaryConversationPolicy`** resuelve esto condensando automáticamente los mensajes más antiguos en un resumen compacto, manteniendo los mensajes recientes literalmente.

### Disparar por Conteo de Mensajes

```csharp
service.ConversationPolicy = SummaryConversationPolicy.ByMessage(
    triggerCount: 20,   // resume cuando el historial supera 20 mensajes
    keepRecentCount: 5  // mantiene los 5 mensajes más recientes literalmente
);
```

### Disparar por Conteo de Tokens

```csharp
service.ConversationPolicy = SummaryConversationPolicy.ByToken(
    triggerTokens: 3000,    // resume cuando el uso de tokens supera 3000
    keepRecentTokens: 1000  // mantiene mensajes recientes hasta 1000 tokens
);
```

### Disparar por Ambos (Condición O)

```csharp
service.ConversationPolicy = SummaryConversationPolicy.ByBoth(
    triggerTokens: 4000,
    triggerCount: 30,
    keepRecentTokens: 1300,
    keepRecentCount: 7
);
```

Una vez configurado, el resumen ocurre automáticamente en `GetCompletionAsync`.

### Cómo Funciona

1. Antes de cada completion, la política verifica si la conversación supera el límite configurado
2. Si se activa, los mensajes más antiguos se resumen en un texto conciso usando una llamada LLM sin estado
3. El resumen se inyecta como prefijo del mensaje de sistema
4. Los mensajes recientes se conservan literalmente

### Streaming

El resumen no se activa automáticamente durante `StreamAsync`. Llámalo explícitamente antes:

```csharp
await service.ApplySummaryPolicyIfNeededAsync();

await foreach (var chunk in service.StreamAsync("Continúa nuestra conversación..."))
    Console.Write(chunk.Content);
```

## Guardar y Restaurar Resumen

Persiste el resumen entre sesiones para que el modelo retenga contexto tras un reinicio:

```csharp
// Guardar
string saved = service.ConversationPolicy.CurrentSummary;
// → almacena en base de datos, archivo, etc.

// Restaurar en una nueva sesión
service.ConversationPolicy.LoadSummary(saved);
```
