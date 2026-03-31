# Mythosia.AI

A modular .NET AI library ecosystem for building AI applications with multiple model providers, RAG, document loaders, and vector database integrations.

## Overview

Mythosia.AI provides:

- Unified AI abstractions and implementations
- Multiple provider integrations
- RAG components
- Document loader packages
- Vector database packages

## Main Areas

### Core
Core AI interfaces, models, builders, services, and provider implementations.

### RAG
Retrieval-augmented generation components including loaders, splitters, embeddings, reranking, and diagnostics.

### Document Loaders
Packages for loading and extracting content from office and PDF documents.

### Vector Database
Vector store abstractions and implementations for multiple backends.

## Documentation

- [API Reference](api/index.md)
- [GitHub Repository](https://github.com/AJ-comp/Mythosia.AI)

## Packages

### Core
- `Mythosia.AI`
- `Mythosia.AI.Abstractions`
- `Mythosia.AI.Providers.Alibaba`

### RAG
- `Mythosia.AI.Rag`
- `Mythosia.AI.Rag.Abstractions`

### Document Loaders
- `Mythosia.Documents.Abstractions`
- `Mythosia.Documents.Office`
- `Mythosia.Documents.Pdf`

### Vector Database
- `Mythosia.VectorDb.Abstractions`
- `Mythosia.VectorDb.InMemory`
- `Mythosia.VectorDb.Pinecone`
- `Mythosia.VectorDb.Postgres`
- `Mythosia.VectorDb.Qdrant`
- `Mythosia.VectorDb.Tools`