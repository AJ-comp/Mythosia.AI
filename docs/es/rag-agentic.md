# RAG Agéntico

## ¿Por qué RAG Agéntico?

En el RAG estándar, cada mensaje del usuario dispara exactamente **una** búsqueda. El sistema busca, construye el contexto y genera la respuesta — sin excepciones. Esto funciona bien para preguntas simples, pero se queda corto cuando:

- La pregunta requiere **múltiples búsquedas** sobre temas diferentes (ej: "Compara la política de reembolso para productos físicos versus digitales")
- El primer resultado de búsqueda es **insuficiente** y el sistema debería refinar e intentar de nuevo
- Algunas preguntas **no necesitan recuperación** (ej: "Resume nuestra conversación hasta ahora")
- La respuesta depende de combinar **recuperación de documentos con datos en tiempo real** de APIs

El RAG Agéntico resuelve todo esto. En lugar de un pipeline fijo de recuperar-y-responder, el **agente decide de forma autónoma** — cuándo buscar, qué buscar, si debe buscar de nuevo y cuándo llamar otras herramientas — todo dentro de un loop ReAct.

## Inicio Rápido

Registra el `RagStore` como herramienta con `WithAgenticRag` y delega a `RunAgentAsync`:

```csharp
// Construir el índice una vez
var ragStore = await RagStore.BuildAsync(cfg => cfg
    .AddDocument("manual.pdf")
    .AddDocument("policy.docx")
    .UseOpenAIEmbedding(apiKey));

// Registrar RAG como herramienta y ejecutar el agente
var service = new AnthropicService(apiKey, http);
service.WithAgenticRag(ragStore);

var answer = await service.RunAgentAsync("Resume la política de reembolso.");
```

El agente llama a `search_documents` automáticamente cuando necesita contexto documental y sintetiza la respuesta final a partir de los fragmentos recuperados.

## Combinando con Otras Herramientas

El RAG Agéntico brilla cuando se combina con herramientas adicionales — el agente selecciona la herramienta correcta para cada subtarea:

```csharp
var service = new AnthropicService(apiKey, http);

service.WithAgenticRag(ragStore)
       .WithFunctionAsync("get_order_status", "Consultar el estado de un pedido por su ID.",
           ("order_id", "El ID del pedido a consultar.", required: true),
           async id => await orderApi.GetStatusAsync(id));

// El agente busca la política en documentos Y llama a la API para datos del pedido
var answer = await service.RunAgentAsync(
    "Pedido #12345 — ¿tengo derecho a reembolso según la política actual?");
```

En este ejemplo, el agente de forma autónoma:

1. Busca en los documentos la política de reembolso
2. Llama a la API de pedidos para obtener el estado del pedido #12345
3. Combina ambas piezas de información para producir la respuesta final

## Descripción Personalizada de la Herramienta

La descripción de la herramienta controla cuándo el agente decide invocar RAG. Adáptala a tu dominio para una selección de herramienta más precisa:

```csharp
service.WithAgenticRag(ragStore,
    toolDescription:
        "Buscar políticas internas de RRHH, manuales de productos y documentos de cumplimiento. " +
        "Usa esta herramienta cuando se necesite información específica de la empresa o de productos.");
```

Una descripción vaga como "Buscar documentos" puede hacer que el agente llame a RAG con demasiada o muy poca frecuencia. Sé específico sobre **qué tipo de información** contienen los documentos.

## Diferencias con RAG Estándar

| | RAG Estándar | RAG Agéntico |
| --- | --- | --- |
| Momento de búsqueda | En cada mensaje | El agente decide |
| Formulación de la consulta | QueryRewriter | El propio agente |
| Número de búsquedas | Una por turno | Una o más según sea necesario |
| Combinación de herramientas | No aplica | Cualquier herramienta registrada |
| Configuración | `.WithRag()` | `.WithAgenticRag()` + `RunAgentAsync` |

> **Nota:** El `QueryRewriter` se omite intencionalmente en RAG Agéntico. El agente formula su propia consulta de búsqueda autocontenida, por lo que un paso de reescritura separado sería redundante y podría distorsionar la intención del agente.

## Cuándo Elegir Cada Uno

- **RAG Estándar** — cada pregunta es sobre documentos, de un solo tema, y quieres latencia mínima
- **RAG Agéntico** — las preguntas abarcan múltiples temas, requieren combinar documentos + datos en tiempo real, o necesitan recuperación iterativa
