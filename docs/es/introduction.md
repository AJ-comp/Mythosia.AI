# Introducción

Mythosia.AI es una biblioteca .NET modular que proporciona una interfaz unificada para trabajar con múltiples proveedores de IA, pipelines RAG, cargadores de documentos y bases de datos vectoriales.

## ¿Por qué Mythosia.AI?

La mayoría de los SDK de proveedores de IA exponen APIs diferentes, lo que dificulta cambiar de proveedor o combinar funcionalidades. Mythosia.AI los envuelve todos detrás de una única interfaz `IAIService`, de modo que el código de tu aplicación permanece igual sin importar qué modelo o proveedor utilices.

## Estructura de Paquetes

Solo instala lo que necesitas:

| Paso | Paquete | Propósito |
|:----:|---------|---------|
| **1** | `Mythosia.AI` | Comienza aquí — completions, streaming, llamada de funciones, salida estructurada |
| **2** | `Mythosia.AI.Rag` | Agrega cuando necesites RAG — splitters, embeddings, hybrid search, reranking |
| **3** | `Mythosia.VectorDb.*` | Agrega cuando necesites un vector store en producción — Postgres, Qdrant o Pinecone |

## Proveedores Soportados

Todos los proveedores están incluidos en el paquete `Mythosia.AI` (excepto Alibaba):

| Proveedor | Modelos |
|----------|--------|
| **OpenAI** | GPT-5.x, GPT-4.1, GPT-4o, serie o3 |
| **Anthropic** | Claude Opus / Sonnet / Haiku 4.x |
| **Google** | Gemini 2.5 / serie 3 |
| **xAI** | serie Grok 4, Grok Build, Grok 3 Mini |
| **DeepSeek** | Chat, Reasoner |
| **Perplexity** | Sonar, Sonar Pro, Sonar Reasoning Pro |
| **Alibaba / Qwen** | Qwen Max / Plus / Turbo / Qwen3 (`Mythosia.AI.Providers.Alibaba`) |

## Visión General de la Arquitectura

```
Mythosia.AI.Rag                 ← pipeline RAG, orquestación
    └── Mythosia.AI             ← servicios de IA principales (todos los proveedores)
        └── Mythosia.AI.Abstractions   ← interfaz IAIService

Mythosia.VectorDb.*             ← vector stores (elige uno o más)
    └── Mythosia.VectorDb.Abstractions

Mythosia.Documents.*            ← cargadores de documentos (Word, Excel, PDF, ...)
    └── Mythosia.Documents.Abstractions
```
