# Re-ranking e Ajuste de Recuperação

> 📍 **Pipeline de Pergunta e Resposta:** [Reescrita de Consulta](rag-query-rewriting.md) → Filtragem → Embedding (quando necessário) → [Recuperação](rag-hybrid-search.md) → **`Re-ranking`** → Construção de Contexto

A etapa de consulta `Embedding` depende do recuperador; busca lexical não a reporta. Um recuperador personalizado pode reportar etapas por `request.ProgressAsync`. Embeddings de documentos permanecem iguais.

## Por que Re-ranking?

A busca vetorial retorna candidatos ordenados por similaridade de embedding, mas a similaridade de embedding é uma **aproximação**. Um chunk com pontuação 0.82 pode ser mais relevante do que um com pontuação 0.85.

Um **re-ranker** pega a lista inicial de candidatos e pontua cada chunk em relação à consulta original com um modelo mais poderoso, produzindo uma ordenação de relevância muito mais precisa.

## Opções de Re-ranker

### LLM Reranker

Usa seu serviço de IA para pontuar resultados. Eficaz mas adiciona latência:

```csharp
.WithRag(rag => rag
    .WithReranker(new LlmReranker(aiService))
    .AddDocument("corpus.txt")
)
```

Para evitar que a pergunta e os documentos de cada avaliação se misturem com avaliações anteriores ou com a conversa do serviço, `LlmReranker` usa uma solicitação sem estado em cada avaliação. Ele não lê nem adiciona conteúdo ao histórico da conversa. As configurações padrão do serviço e seu código de chamada permanecem iguais. As avaliações dos re-rankers que compartilham o mesmo serviço de IA são processadas em sequência.

### Cohere Reranker

Chama a API Cohere Rerank — rápido e preciso:

```csharp
.WithRag(rag => rag
    .WithReranker(new CohereReranker(cohereApiKey))
    .AddDocument("corpus.txt")
)
```

### vLLM Reranker

Usa um endpoint de reranking vLLM hospedado localmente:

```csharp
.WithRag(rag => rag
    .WithReranker(new VllmReranker(baseUrl: "http://localhost:8000"))
    .AddDocument("corpus.txt")
)
```

## Parâmetros de Recuperação

```csharp
.WithRag(rag => rag
    .WithTopK(5)                   // Número final de chunks retornados
    .WithRetrievalMultiplier(3)    // Recupera topK × 3 candidatos (para re-ranking)
    .WithScoreThreshold(0.6)       // Pontuação mínima de similaridade
    .AddDocument("corpus.txt")
)
```

- **`TopK`** — quantos chunks chegam ao contexto do LLM
- **`RetrievalMultiplier`** — lança uma rede mais ampla para o re-ranker ter mais para trabalhar. Um multiplicador de 3 significa que 15 candidatos são buscados, e os 5 melhores sobrevivem ao re-ranking.
- **`WithScoreThreshold`** — descarta tudo abaixo deste limite de similaridade

## Modo de Seleção Final

```csharp
// Padrão: confiar apenas nas pontuações do re-ranker
.WithFinalSelectionPolicy(RagFinalSelectionMode.RerankerOnly)

// Misturar pontuação de recuperação e pontuação do re-ranker
.WithFinalSelectionPolicy(RagFinalSelectionMode.WeightedBlend, retrievalWeight: 0.65)
```

Os modos vetorial e lexical puros preservam pontuações nativas. O híbrido configurável usa RRF ponderado normalizado mesmo com um ramo; peso vetorial 0 dispensa embedding de consulta. Pontuações não são probabilidades. `WeightedBlend` mistura pontuações sem calibração; prefira `RerankerOnly` para busca lexical sem calibração prévia.
