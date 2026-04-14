# AIRequestContext

## ¿Qué Es?

`AIRequestContext` permite modificar **lo que el modelo ve** para una única solicitud — inyectar instrucciones adicionales, añadir documentos de referencia o reemplazar completamente el mensaje del usuario — sin cambiar permanentemente el mensaje de sistema ni el historial de conversación del servicio.

## El Problema que Resuelve

Considera un pipeline RAG que recupera documentos relevantes y necesita incluirlos en el prompt. **Sin** `AIRequestContext`, tendrías que modificar el mensaje de sistema directamente — contaminando el historial y causando condiciones de carrera en aplicaciones multiusuario.

**Con** `AIRequestContext`, la inyección queda acotada a exactamente una solicitud:

```csharp
// ✅ Con AIRequestContext — limpio, acotado, sin efectos secundarios
var answer = await service.GetCompletionAsync(userQuestion,
    new AIRequestContext
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
        MessageBuilder.User("Doc de referencia: La política de devolución permite retornos en 30 días.").Build()
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
            MessageBuilder.User("Ejemplo: ...").Build()
        }
    }
);
```

Consulta [AIRequestProfile](request-profiles.md) para detalles sobre cómo sobrescribir parámetros de generación.
