# Hybrid Search

> 📍 **Pipeline de Pregunta y Respuesta:** [Reescritura de Consulta](rag-query-rewriting.md) → Embedding → Filtrado → **`Recuperación`** → [Re-ranking](rag-reranking.md) → Construcción de Contexto

## ¿Por qué Hybrid Search?

La búsqueda vectorial pura es excelente para capturar significado semántico — "cancelar mi suscripción" coincide con "terminar mi membresía" aunque no compartan palabras. Sin embargo, puede perder **términos exactos** como nombres de productos, códigos de error o identificadores de políticas.

La búsqueda por palabras clave BM25 maneja estos casos perfectamente pero falla en la comprensión semántica. **El Hybrid Search combina ambos**, dándote lo mejor de los dos mundos.

## Configuración

Combina búsqueda vectorial densa con búsqueda por palabras clave BM25 con una sola llamada de método:

```csharp
.WithRag(rag => rag
    .UseHybridSearch(vectorWeight: 0.6f)  // 60% vector, 40% BM25
    .AddDocument("base-conocimiento.txt")
)
```

`vectorWeight` va de 0.0 (BM25 puro) a 1.0 (vector puro). Un valor alrededor de **0.5–0.7** funciona bien en la mayoría de los casos.

## Cuándo Usar Cada Uno

| Escenario | Peso Recomendado |
| --- | --- |
| Preguntas y respuestas generales en lenguaje natural | 0.7–0.8 (más vector) |
| Documentación técnica con términos específicos | 0.4–0.5 (equilibrado) |
| Búsqueda de código o código de error | 0.2–0.3 (más BM25) |

## Ejemplo

```csharp
var service = new OpenAIService(apiKey, http)
    .WithRag(rag => rag
        .UseHybridSearch(vectorWeight: 0.5f)
        .AddDocument("catalogo-productos.txt")
        .AddDocument("codigos-error.txt")
    );

// "ERR-4012" es encontrado por BM25; el contexto semántico es encontrado por vector
var answer = await service.GetCompletionAsync("¿Cómo soluciono el ERR-4012?");
```
