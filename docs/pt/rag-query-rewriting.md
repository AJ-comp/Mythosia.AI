# Reescrita de Consulta

> 📍 **Pipeline de Pergunta e Resposta:** **`Reescrita de Consulta`** → Filtragem → Embedding (quando necessário) → [Recuperação](rag-hybrid-search.md) → [Re-ranking](rag-reranking.md) → Construção de Contexto

A etapa de consulta `Embedding` depende do recuperador; busca lexical não a reporta. Um recuperador personalizado pode reportar etapas por `request.ProgressAsync`. Embeddings de documentos permanecem iguais.

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

<a id="runtime-query-rewriter"></a>

## Alterar a reescrita durante as consultas

Para desativar temporariamente a reescrita ou trocar sua implementação sem reconstruir o índice, use `store.SetQueryRewriter(null)` ou `store.SetQueryRewriter(rewriter)`. Uma chamada direta à sobrecarga de `RagStore.QueryAsync` que aceita `conversationHistory` mantém o reescritor selecionado no início da consulta. Mesmo que ele seja desativado ou substituído enquanto aguarda uma notificação de progresso ou a reescrita, essa consulta continua usando a mesma instância; as seguintes usam a nova configuração. Isso se aplica às consultas diretas ao armazenamento e não atualiza um reescritor já mantido por um wrapper `RagEnabledService`.

## Como Funciona o Gate de Busca

Nem toda mensagem do usuário precisa de uma busca de documento. O rewriter classifica a consulta e retorna uma reescrita vazia para mensagens como:

- "Obrigado!"
- "Entendido, isso foi útil."
- "Pode resumir o que acabou de dizer?"

Quando o gate é ativado, todo o pipeline de recuperação é ignorado — sem embedding, sem busca vetorial, sem re-ranking — e o LLM responde diretamente do contexto de conversa.
