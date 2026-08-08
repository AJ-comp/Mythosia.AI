# Reescrita de Consulta

> 📍 **Pipeline de Pergunta e Resposta:** **`Reescrita de Consulta`** → Embedding → Filtragem → [Recuperação](rag-hybrid-search.md) → [Re-ranking](rag-reranking.md) → Construção de Contexto

## Por que Reescrita de Consulta?

Em uma conversa com múltiplos turnos, os usuários usam pronomes e referências curtas naturalmente:

> Usuário: "Fale-me sobre a política de reembolso."
> Usuário: "E as exceções **a ela**?"

Se "E as exceções a ela?" for enviado ao vector store como está, o embedding não saberá a que "ela" se refere. A busca retorna resultados irrelevantes.

A **reescrita de consulta** resolve essas referências antes da recuperação, expandindo "ela" → "exceções à política de reembolso". Também implementa um **gate de busca** — se a consulta não precisa de recuperação (ex.: "Obrigado!"), pula a busca vetorial inteiramente, economizando latência e custo.

## Configuração

Um `LlmQueryRewriter` usa o próprio serviço de IA para reescrever a consulta antes do embedding:

```csharp
.WithRag(rag => rag
    .WithQueryRewriter(250)
    .AddDocument("docs.txt")
)
```

## RAG com Múltiplos Turnos

Ao consultar o `RagStore` diretamente, passe o histórico de conversa para o rewriter resolver referências:

```csharp
var history = new List<ConversationTurn>
{
    new ConversationTurn("Qual é a política de reembolso?", "Você pode devolver itens em até 30 dias."),
    new ConversationTurn("E os produtos digitais?", "Produtos digitais não têm reembolso.")
};

var result = await store.QueryAsync(
    query: "Há alguma exceção a isso?",
    conversationHistory: history
);
```

## Como Funciona o Gate de Busca

Nem toda mensagem do usuário precisa de uma busca de documento. O rewriter classifica a consulta e retorna uma reescrita vazia para mensagens como:

- "Obrigado!"
- "Entendido, isso foi útil."
- "Pode resumir o que acabou de dizer?"

Quando o gate é ativado, todo o pipeline de recuperação é ignorado — sem embedding, sem busca vetorial, sem re-ranking — e o LLM responde diretamente do contexto de conversa.
