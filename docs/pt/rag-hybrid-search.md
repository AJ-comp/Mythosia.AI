# Hybrid Search

> 📍 **Pipeline de Pergunta e Resposta:** [Reescrita de Consulta](rag-query-rewriting.md) → Embedding → Filtragem → **`Recuperação`** → [Re-ranking](rag-reranking.md) → Construção de Contexto

## Por que Hybrid Search?

A busca vetorial pura é excelente para capturar significado semântico — "cancelar minha assinatura" corresponde a "encerrar minha adesão" mesmo sem palavras em comum. No entanto, pode perder **termos exatos** como nomes de produtos, códigos de erro ou identificadores de políticas.

A busca por palavras-chave BM25 lida perfeitamente com esses casos, mas falha na compreensão semântica. **O Hybrid Search combina ambos**, dando a você o melhor dos dois mundos.

## Configuração

Combine busca vetorial densa com busca por palavras-chave BM25 com uma única chamada de método:

```csharp
.WithRag(rag => rag
    .UseHybridSearch(vectorWeight: 0.6f)  // 60% vetor, 40% BM25
    .AddDocument("base-conhecimento.txt")
)
```

`vectorWeight` varia de 0.0 (BM25 puro) a 1.0 (vetor puro). Um valor em torno de **0.5–0.7** funciona bem na maioria dos casos.

## Quando Usar Qual

| Cenário | Peso Recomendado |
| --- | --- |
| Perguntas e respostas gerais em linguagem natural | 0.7–0.8 (mais vetor) |
| Documentação técnica com termos específicos | 0.4–0.5 (equilibrado) |
| Pesquisa de código ou código de erro | 0.2–0.3 (mais BM25) |

## Exemplo

```csharp
var service = new OpenAIService(apiKey, http)
    .WithRag(rag => rag
        .UseHybridSearch(vectorWeight: 0.5f)
        .AddDocument("catalogo-produtos.txt")
        .AddDocument("codigos-erro.txt")
    );

// "ERR-4012" é encontrado pelo BM25; contexto semântico é encontrado pelo vetor
var answer = await service.GetCompletionAsync("Como corrijo o ERR-4012?");
```
