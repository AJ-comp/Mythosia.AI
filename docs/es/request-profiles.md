# AIRequestProfile

## ¿Qué Es?

`AIRequestProfile` permite sobrescribir parámetros de generación — temperatura, máximo de tokens, modo sin estado, llamada de funciones — **solo para una única solicitud**. La configuración global del servicio no se toca.

## El Problema que Resuelve

Imagina que tienes un chatbot configurado para conversación creativa:

```csharp
var service = new OpenAIService(apiKey, http)
    .WithTemperature(0.8f)
    .WithMaxTokens(2048)
    .WithSystemMessage("Eres un asistente de escritura creativa.");
```

Ahora tu pipeline RAG necesita reescribir la consulta del usuario con baja temperatura y sin historial. **Sin** `AIRequestProfile`, tendrías que hacer esto:

```csharp
// ❌ Sin AIRequestProfile — gestión manual de estado
var savedTemp = service.Temperature;
// ...guarda, modifica, usa, restaura — frágil y no thread-safe
```

**Con** `AIRequestProfile`, es una sola línea:

```csharp
// ✅ Con AIRequestProfile — limpio y seguro
var rewritten = await service.GetCompletionAsync("Reescribe esta consulta: ...",
    new AIRequestProfile { Temperature = 0.1f, MaxTokens = 256, Stateless = true });
```

La configuración global del servicio nunca se toca. Sin necesidad de limpieza. Thread-safe.

## Propiedades Disponibles

```csharp
var profile = new AIRequestProfile
{
    Temperature = 0.1f,       // Sobrescribe temperatura
    MaxTokens = 256,          // Sobrescribe tokens de salida máximos
    Stateless = true,         // No agrega este intercambio al historial
    DisableFunctions = true,  // Omite llamada de funciones para esta solicitud
    DisableReasoning = true   // Omite reasoning para esta solicitud
};

var response = await service.GetCompletionAsync("Tu prompt", profile);
```

## Perfiles Predefinidos

```csharp
// Reescritura de consulta: baja temperatura, presupuesto pequeño de tokens, sin estado
var rewritten = await service.GetCompletionAsync(query, RequestProfiles.QueryRewrite);

// Resumen: temperatura ligeramente mayor, tokens moderados
var summary = await service.GetCompletionAsync(text, RequestProfiles.Summarization);
```

## Combinar con AIRequestContext

Ambos pueden pasarse juntos para control máximo:

```csharp
var response = await service.GetCompletionAsync(
    prompt,
    profile: RequestProfiles.QueryRewrite,
    context: new AIRequestContext { SystemMessageSuffix = "\nSé conciso." }
);
```

Consulta [AIRequestContext](request-contexts.md) para detalles sobre cómo inyectar contenido en las solicitudes.
