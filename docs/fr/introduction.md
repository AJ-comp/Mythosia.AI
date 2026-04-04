# Introduction

Mythosia.AI est une bibliothèque .NET modulaire pour l'IA, offrant une interface unifiée pour travailler avec plusieurs fournisseurs d'IA, des pipelines RAG, des chargeurs de documents et des bases de données vectorielles.

## Pourquoi Mythosia.AI ?

Les SDK de la plupart des fournisseurs d'IA exposent des API différentes, ce qui complique le changement de fournisseur ou la combinaison de fonctionnalités. Mythosia.AI les regroupe derrière une seule interface `IAIService` — le code de votre application reste identique, quel que soit le modèle ou le fournisseur utilisé.

## Structure des packages

N'installez que ce dont vous avez besoin :

| Étape | Package | Rôle |
|:----:|---------|---------|
| **1** | `Mythosia.AI` | Le point de départ — complétion, streaming, appel de fonctions, sortie structurée |
| **2** | `Mythosia.AI.Rag` | Pour le RAG — découpage, embeddings, recherche hybride, re-ranking |
| **3** | `Mythosia.VectorDb.*` | Pour un stockage vectoriel en production — Postgres, Qdrant ou Pinecone |

## Fournisseurs pris en charge

Tous les fournisseurs sont inclus dans le package principal `Mythosia.AI` (sauf Alibaba) :

| Fournisseur | Modèles |
|----------|--------|
| **OpenAI** | GPT-5.x, GPT-4.1, GPT-4o, série o3 |
| **Anthropic** | Claude Opus / Sonnet / Haiku 4.x |
| **Google** | Gemini 2.5 / série 3 |
| **xAI** | Grok 3, série Grok 4 |
| **DeepSeek** | Chat, Reasoner |
| **Perplexity** | Sonar, Sonar Pro, Sonar Reasoning |
| **Alibaba / Qwen** | Qwen Max / Plus / Turbo / Qwen3 (`Mythosia.AI.Providers.Alibaba`) |

## Vue d'ensemble de l'architecture

```
Mythosia.AI.Rag                 ← Pipeline RAG, orchestration
    └── Mythosia.AI             ← Services IA principaux (tous les fournisseurs)
        └── Mythosia.AI.Abstractions   ← Interface IAIService

Mythosia.VectorDb.*             ← Stockages vectoriels (un ou plusieurs)
    └── Mythosia.VectorDb.Abstractions

Mythosia.Documents.*            ← Chargeurs de documents (Word, Excel, PDF, ...)
    └── Mythosia.Documents.Abstractions
```
