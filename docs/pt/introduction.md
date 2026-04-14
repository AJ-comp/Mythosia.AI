# Introdução

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
| **OpenAI** | GPT-5.x, GPT-4.1, GPT-4o, série o3 |
| **Anthropic** | Claude Opus / Sonnet / Haiku 4.x |
| **Google** | Gemini 2.5 / série 3 |
| **xAI** | Grok 3, série Grok 4 |
| **DeepSeek** | Chat, Reasoner |
| **Perplexity** | Sonar, Sonar Pro, Sonar Reasoning |
| **Alibaba / Qwen** | Qwen Max / Plus / Turbo / Qwen3 (`Mythosia.AI.Providers.Alibaba`) |

## Visão Geral da Arquitetura

```
Mythosia.AI.Rag                 ← pipeline RAG, orquestração
    └── Mythosia.AI             ← serviços de IA principais (todos os provedores)
        └── Mythosia.AI.Abstractions   ← interface IAIService

Mythosia.VectorDb.*             ← vector stores (escolha um ou mais)
    └── Mythosia.VectorDb.Abstractions

Mythosia.Documents.*            ← carregadores de documentos (Word, Excel, PDF, ...)
    └── Mythosia.Documents.Abstractions
```
