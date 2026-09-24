# Introducción

> Grok 4.7: Requiere Mythosia.AI 8.1.0 / Abstractions 4.1.0. [selección del modelo, razonamiento y velocidad](providers.md#grok-47)

> GPT-6 Sol/Luna aún no están publicados. Consulta [selección del modelo y requisitos](providers.md#gpt-6-sol-luna).

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
| **OpenAI** | GPT-6 Astra / Sol / Luna, GPT-5.1–5.6, GPT-4.1, GPT-4o |
| **Anthropic** | Claude Fable 5.1 / 5, Mythos 5.1 / 5 (limited), Opus / Sonnet 5 and 4.x, Haiku 4.5 |
| **Google** | Gemini 3.8 / 3.7 / 3.6 Flash, Gemini 3.5 / 3.1 / 3, Gemini 2.5 |
| **xAI** | Grok 4.7 / 4.6 / 4.5 / 4.3 / 4.20, Grok Build |
| **DeepSeek** | Flash (V4.1 Flash), V4 Pro |
| **Perplexity** | Ajustes de Agent API y `perplexity/sonar` |
| **Alibaba / Qwen** | Qwen Max / Plus / Turbo / Qwen3 (`Mythosia.AI.Providers.Alibaba`) |

## Visión General de la Arquitectura

```
Mythosia.AI                     ← servicios de IA principales (todos los proveedores)
    └── Mythosia.AI.Abstractions   ← interfaz IAIService

Mythosia.AI.Rag                 ← pipeline RAG, orquestación
    ├── Mythosia.AI.Abstractions
    ├── Mythosia.AI.Rag.Abstractions
    │   └── Mythosia.VectorDb.Abstractions
    ├── Mythosia.Documents.Office / Mythosia.Documents.Pdf
    │   └── Mythosia.Documents.Abstractions
    └── Mythosia.VectorDb.InMemory
        ├── Mythosia.VectorDb.Abstractions
        └── Mythosia.AI.Rag.Abstractions

Mythosia.VectorDb.*             ← vector stores (elige uno o más)
    └── Mythosia.VectorDb.Abstractions

Mythosia.Documents.*            ← cargadores de documentos (Word, Excel, PDF, ...)
    └── Mythosia.Documents.Abstractions
```
