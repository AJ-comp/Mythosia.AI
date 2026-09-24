# Introdução

> Grok 4.7: Requer Mythosia.AI 8.1.0 / Abstractions 4.1.0. [seleção do modelo, raciocínio e velocidade](providers.md#grok-47)

> GPT-6 Sol/Luna ainda não foram publicados. Veja [seleção do modelo e requisitos](providers.md#gpt-6-sol-luna).

Mythosia.AI é uma biblioteca .NET modular que oferece uma interface unificada para trabalhar com múltiplos provedores de IA, pipelines RAG, carregadores de documentos e bancos de dados vetoriais.

## Por que Mythosia.AI?

A maioria dos SDKs de provedores de IA expõe APIs diferentes, dificultando a troca de provedores ou a combinação de funcionalidades. O Mythosia.AI encapsula todos eles por trás de uma única interface `IAIService`, para que o código da sua aplicação permaneça o mesmo independentemente do modelo ou provedor utilizado.

## Estrutura de Pacotes

Instale apenas o que você precisa:

| Passo | Pacote | Finalidade |
|:----:|---------|---------|
| **1** | `Mythosia.AI` | Comece aqui — completions, streaming, chamada de funções, saída estruturada |
| **2** | `Mythosia.AI.Rag` | Adicione quando precisar de RAG — splitters, embeddings, hybrid search, reranking |
| **3** | `Mythosia.VectorDb.*` | Adicione quando precisar de um vector store em produção — Postgres, Qdrant ou Pinecone |

## Provedores Suportados

Todos os provedores estão incluídos no pacote `Mythosia.AI` (exceto Alibaba):

| Provedor | Modelos |
|----------|--------|
| **OpenAI** | GPT-6 Astra / Sol / Luna, GPT-5.1–5.6, GPT-4.1, GPT-4o |
| **Anthropic** | Claude Fable 5.1 / 5, Mythos 5.1 / 5 (limited), Opus / Sonnet 5 and 4.x, Haiku 4.5 |
| **Google** | Gemini 3.8 / 3.7 / 3.6 Flash, Gemini 3.5 / 3.1 / 3, Gemini 2.5 |
| **xAI** | Grok 4.7 / 4.6 / 4.5 / 4.3 / 4.20, Grok Build |
| **DeepSeek** | Flash (V4.1 Flash), V4 Pro |
| **Perplexity** | Presets da Agent API e `perplexity/sonar` |
| **Alibaba / Qwen** | Qwen Max / Plus / Turbo / Qwen3 (`Mythosia.AI.Providers.Alibaba`) |

## Visão Geral da Arquitetura

```
Mythosia.AI                     ← serviços de IA principais (todos os provedores)
    └── Mythosia.AI.Abstractions   ← interface IAIService

Mythosia.AI.Rag                 ← pipeline RAG, orquestração
    ├── Mythosia.AI.Abstractions
    ├── Mythosia.AI.Rag.Abstractions
    │   └── Mythosia.VectorDb.Abstractions
    ├── Mythosia.Documents.Office / Mythosia.Documents.Pdf
    │   └── Mythosia.Documents.Abstractions
    └── Mythosia.VectorDb.InMemory
        ├── Mythosia.VectorDb.Abstractions
        └── Mythosia.AI.Rag.Abstractions

Mythosia.VectorDb.*             ← vector stores (escolha um ou mais)
    └── Mythosia.VectorDb.Abstractions

Mythosia.Documents.*            ← carregadores de documentos (Word, Excel, PDF, ...)
    └── Mythosia.Documents.Abstractions
```
