# Observar tareas largas con Claude Fable 5.1

[Claude Opus 5.5](providers.md#claude-opus-55) está disponible con Mythosia.AI 8.1.0 / Abstractions 4.1.0: razonamiento siempre activo, esfuerzo medium por defecto y visualización omitida. Solicite el progreso legible explícitamente; sus valores por defecto y reglas de vinculación difieren de Fable 5.1.

> Los controles de Fable 5.1 requieren `Mythosia.AI` 8.0.0 y `Mythosia.AI.Abstractions` 4.0.0 o posteriores. Las API existentes de Run, razonamiento/búsqueda y GPT-6 Astra mantienen sus versiones mínimas 7.1.0 / 3.1.0.

## ¿Cuándo sirven estos controles?

Investigar documentos puede exigir varias búsquedas y llamadas a herramientas antes de producir una respuesta. La aplicación puede necesitar mostrar el progreso, exigir una comprobación solo durante el turno actual o continuar tras modificar contenido anterior. Fable 5.1 incorpora controles para estos casos, pero al reutilizar pensamiento conservado, el historial también forma parte del contrato de la solicitud.

Usa la [API Run](execution-api-transition.md) para observar y cancelar la tarea, las [opciones comunes de razonamiento y búsqueda](reasoning-and-search.md) para elegir esfuerzo y fuentes, y los ajustes de Claude siguientes para gestionar el progreso y el historial. Las capacidades nativas del modelo no implican que Mythosia exponga todas las API del proveedor.

## Elegir explícitamente modelo y esfuerzo

```csharp
using Mythosia.AI.Models;
using Mythosia.AI.Services.Anthropic;

var service = new AnthropicService(apiKey, httpClient);
service.ChangeModel(AIModels.Anthropic.ClaudeFable5_1);
service.WithAdaptiveThinkingParameters(ClaudeReasoningEffort.High);

string answer = await service.GetCompletionAsync("Review this migration plan.");
```

`ClaudeFable5_1` selecciona `claude-fable-5-1`. `ClaudeMythos5_1` selecciona `claude-mythos-5-1` y requiere acceso a Project Glasswing. Se conservan las constantes de Fable 5 y Mythos 5. Ambos modelos 5.1 aceptan texto e imágenes y generan texto, con una ventana de contexto de 1M de tokens y una salida máxima de 128K tokens. [Descripción del modelo](https://platform.claude.com/docs/en/models/fable-5-1/overview).

El valor nativo predeterminado es `high`, pero `ClaudeReasoningEffort.Auto` de Mythosia conserva la conversión existente de `ThinkingBudget`: los presupuestos habilitados corresponden a `High`, a `XHigh` desde 32.768 y a `Max` desde 100.000. Una solicitud de desactivar razonamiento usa esfuerzo adaptativo bajo y omite el pensamiento legible. Elige `High` explícitamente si necesitas ese comportamiento; `Auto` no significa que la biblioteca siempre omita effort para delegar en el valor del modelo.

## Mostrar progreso entre llamadas a herramientas

`ClaudeThinkingDisplay.Updates` solicita progreso legible manteniendo oculto el razonamiento. `Summarized` también incluye razonamiento resumido; `Omitted` suprime los bloques thinking legibles. Las actualizaciones dependen de que el modelo las genere, por lo que no garantizan una notificación a intervalos fijos. [Actualizaciones de progreso](https://platform.claude.com/docs/en/models/fable-5-1/whats-new-fable-5-1#progress-updates-between-tool-calls-beta).

```csharp
using Mythosia.AI.Models.Streaming;

service.WithAdaptiveThinkingParameters(
    ClaudeReasoningEffort.High, ClaudeThinkingDisplay.Updates);

await using var run = await service.StartRunAsync(
    "Use the registered tools to investigate the report.",
    options: StreamOptions.FullOptions);

await foreach (var item in run.StreamAsync())
{
    if (item.Type == StreamingContentType.Reasoning)
        Console.WriteLine($"Progress: {item.Content}");
    else if (item.Type == StreamingContentType.Text)
        Console.Write(item.Content);
}
string result = (await run.Result).Text;
```

Las actualizaciones utilizan el evento existente `StreamingContentType.Reasoning`. Activa su observación con `StreamOptions.FullOptions` o `StreamOptions.Default.WithReasoning()`. Para una llamada sin streaming, lee `service.LastThinkingContent` después de terminar. El texto de progreso es independiente de la respuesta final y no expone la cadena de pensamiento original.

## Cambiar las instrucciones del turno sin reescribir el historial

Los bloques thinking de Fable 5.1 están vinculados al system prompt, las herramientas y los mensajes anteriores con los que se generaron. Reescribir esas entradas manteniendo thinking posterior puede invalidarlo. Una instrucción limitada a un turno sirve, por ejemplo, para exigir revisar la política de soporte antes de responder ahora: se añade al final y permanece en el historial, pero deja de aplicarse cuando aparece otro mensaje de usuario. Así no es necesario reescribir repetidamente el system prompt superior. Cambiar effort y añadir instrucciones por turno son controles separados.

```csharp
await service
    .WithTurnInstruction("Check the registered support-policy tool before answering this turn.")
    .GetCompletionAsync("Can I return an opened product?");

await service
    .WithConversationInstruction("Use Korean for the remaining conversation.")
    .GetCompletionAsync("Explain the next step.");
```

Ambos métodos capturan instrucciones para la siguiente solicitud lógica. Mythosia añade un mensaje system después de la entrada del usuario o de los resultados de herramientas y conserva los mensajes anteriores. `WithTurnInstruction` utiliza `clear_at: "next_user_message"`; dentro de la misma solicitud, la biblioteca vuelve a añadir la instrucción tras cada turno de resultados para mantenerla activa hasta que termine la solicitud. `WithConversationInstruction` persiste en turnos posteriores. Configúralos antes de empezar: no equivalen a `run.SteerAsync` ni inyectan instrucciones en una respuesta que ya se está ejecutando.

Para ajustar effort entre solicitudes conservando un prefijo de caché reutilizable, usa `.WithReasoning(ReasoningLevel.High, cache: CachePreservation.Required)` de `Mythosia.AI.Extensions`. La biblioteca envía una actualización de effort por mensaje y conserva su historial. Consulta las combinaciones admitidas en la [guía común](reasoning-and-search.md). En 5.1, los prefijos/sufijos system por solicitud de `AIRequestContext` se convierten en instrucciones de turno añadidas al final, sin reescribir un system prompt anterior.

Fable 5.1 puede leer thinking de modelos Claude anteriores, pero los modelos anteriores no pueden leer el suyo. Mythos 5.1 tiene las mismas capacidades 5.1, pero no aplica la comprobación de vinculación de prefijos de Fable. Observa las ediciones, los cambios de modelo y los bloques descartados; no supongas que se ha conservado el mismo razonamiento. [Guía de migración](https://platform.claude.com/docs/en/models/fable-5-1/migration-guide).

## Diagnosticar una modificación intencionada del historial

`ThinkingPrefixMismatchBehavior = null` deja la comprobación a la política de la cuenta del proveedor. `WithThinkingBinding(ClaudeThinkingPrefixMismatchBehavior.Error)` solicita validación explícita en el servidor. Las ediciones del usuario en el historial, `SystemMessage` o las herramientas se envían a Anthropic; con `Error`, un prefijo que no coincide produce una respuesta 400 del proveedor. Reintentar la misma solicitud inválida no la corrige.

Si la aplicación modifica contenido anterior de forma intencionada y acepta perder el razonamiento afectado, elige `DropBlock`. Mythosia envía este control a Anthropic; no elimina thinking silenciosamente antes de enviar la solicitud.

```csharp
service.WithThinkingBinding(ClaudeThinkingPrefixMismatchBehavior.DropBlock);
service.SystemMessage = "You are reviewing the revised policy.";
await service.GetCompletionAsync("Reassess the recommendation.");

foreach (ClaudeInputTransformation change in service.LastInputTransformations)
    Console.WriteLine($"{change.Type}: {change.Reason} ({change.Model})");
```

`LastInputTransformations` expone los campos `Type`, `Path` y `Reason` comunicados por el proveedor, además de `ResponseId` y `Model` para identificar su origen. `prefix_binding_mismatch` señala un prefijo modificado; `model_binding_mismatch`, pensamiento que el modelo destino no puede leer. Descartar no repara el razonamiento. Mantén el historial intacto cuando debas conservarlo, o inicia una conversación nueva para restablecerlo.

Mythosia conserva el historial transmitido para evitar cambios accidentales causados por su procesamiento interno de RAG/context. La compactación local automática se bloquea en conversaciones normales de Fable 5.1 con el comportamiento predeterminado o `Error`. `DropBlock` la permite, pero puede descartar razonamiento y no garantiza aciertos de caché. La opción independiente `CachePreservation.Required` mantiene controles más estrictos del historial. Opciones comunes como `WithWebSearch()` se consumen tras cada solicitud. Omitirlas en el siguiente turno cambia el array nativo tools y puede causar una discrepancia de prefijo. Vuelve a aplicar la misma configuración de herramientas/búsqueda para conservarlo; usa `DropBlock` o una conversación nueva para cambios intencionados. Las opciones no se trasladan automáticamente.

La instantánea del historial transmitido pertenece al servicio y a su `ChatBlock`. Copiar solo el `ChatBlock` a un servicio nuevo no transfiere las instantáneas anteriores de RAG/context ni de system por turno. Continúa con el mismo servicio y conversación para conservar el razonamiento; si solo moviste el historial bruto, inicia una conversación nueva sin asumir que se conservó el estado.

## Usar la selección normal de herramientas

Fable 5.1 y Mythos 5.1 rechazan la selección forzada de herramientas. No asignes `ForceFunctionName`; describe en la solicitud cuándo debe usarse la herramienta registrada. `FunctionsDisabled` sigue disponible para turnos que no deben llamar herramientas. Si necesitas una respuesta tipada, utiliza la API existente de salida estructurada en lugar de forzar una función solo para obtener JSON.

## Distinguir los cambios que corresponden al servidor

| Opción nativa | Beta de Anthropic necesaria |
| --- | --- |
| Effort por mensaje | `mid-conversation-output-config-2026-07-01` |
| Mensaje system limitado a un turno | `mid-conversation-system-clear-at-2026-08-21` |
| `thinking.display: "updates"` | `thinking-display-updates-2026-08-18` |
| Controles de vinculación de thinking e `input_transformations` | `thinking-binding-controls-2026-08-01` |

Mythosia añade la cabecera correspondiente al activar cada ajuste admitido. Activar una beta no activa todas las demás. Esta integración no introduce compactación del servidor, bloques nativos de adición/eliminación de herramientas ni fallback automático de modelos.

Ambos modelos requieren las condiciones aplicables de conservación de datos durante 30 días del proveedor; ZDR necesita autorización explícita de Anthropic. El pensamiento adaptativo permanece activo, no se permiten `budget_tokens` manuales ni desactivarlo y no se envían parámetros de muestreo personalizados. El acceso a la cuenta y la conservación son requisitos del servidor. [Requisitos de migración](https://platform.claude.com/docs/en/models/fable-5-1/migration-guide).

Anthropic aplica las marcas de agua de texto, la procedencia de medios compatibles y los precios de lectura de caché. No hace falta una nueva opción de solicitud Mythosia. La integración no añade una API para crear procedencia de medios, un interruptor de marcas de agua ni controles de facturación. Consulta las [novedades de Fable 5.1](https://platform.claude.com/docs/en/models/fable-5-1/whats-new-fable-5-1).
