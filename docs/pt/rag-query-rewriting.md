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

> É necessário `Mythosia.AI.Rag` 8.1.1 ou posterior para aplicar mudanças em tempo de execução aos wrappers `WithRag(store)` existentes. [Notas do patch](../../src/rag/Mythosia.AI.Rag/RELEASE_NOTES.md#v811).

Para adicionar ou substituir um reescritor sem reconstruir o índice, use `store.SetQueryRewriter(rewriter)`; use `store.SetQueryRewriter(null)` para desativar a reescrita e a extração de termos de pesquisa. Tanto a sobrecarga de `RagStore.QueryAsync` que aceita `conversationHistory` quanto os wrappers já conectados por `service.WithRag(store)` selecionam o reescritor atual do armazenamento a cada solicitação. A solicitação mantém a instância escolhida enquanto aguarda uma notificação de progresso ou a reescrita. Adicionar, substituir ou remover o reescritor afeta as solicitações seguintes, incluindo pesquisa, geração de respostas, streaming e runs por esses wrappers.

```csharp
var rag = service.WithRag(store);
store.SetQueryRewriter(rewriter);
var rewritten = await rag.RetrieveAsync("Quais são as exceções à política de reembolso?");

store.SetQueryRewriter(null);
var original = await rag.RetrieveAsync("Quais são as exceções à política de reembolso?");
```

O `LlmQueryRewriter` padrão ativado por `WithQueryRewriter()` é criado uma única vez durante a inicialização adiada; removê-lo não o recria na próxima solicitação. As sobrecargas do armazenamento sem `conversationHistory` continuam sem executar reescrita, assim como o RAG Agêntico, em que o agente formula a consulta de pesquisa.

## Como Funciona o Gate de Busca

Nem toda mensagem do usuário precisa de uma busca de documento. O rewriter classifica a consulta e retorna uma reescrita vazia para mensagens como:

- "Obrigado!"
- "Entendido, isso foi útil."
- "Pode resumir o que acabou de dizer?"

Quando o gate é ativado, todo o pipeline de recuperação é ignorado — sem embedding, sem busca vetorial, sem re-ranking — e o LLM responde diretamente do contexto de conversa.
