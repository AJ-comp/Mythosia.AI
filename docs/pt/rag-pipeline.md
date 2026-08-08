# Personalização do Pipeline RAG

## Por que Personalizar o Pipeline?

O pipeline RAG padrão funciona bem fora da caixa, mas projetos reais frequentemente precisam de mais controle:

- **Depuração** — qual estágio é lento? O rewriter está alterando a consulta de formas inesperadas?
- **Engenharia de prompt** — o template de prompt padrão pode não se adequar ao tom ou restrições do seu domínio
- **Arquitetura** — múltiplos serviços compartilhando um índice economiza memória e mantém os embeddings consistentes
- **Inspeção** — às vezes você precisa ver o que a recuperação retorna *antes* de enviar ao LLM

## Rastreamento de Progresso

Rastreie qual estágio RAG está executando via callback assíncrono por consulta:

```csharp
var options = new RagQueryOptions
{
    ProgressAsync = async stage =>
    {
        Console.WriteLine($"[RAG] {stage}");
        // Estágios: QueryRewrite, Embedding, Filtering, Retrieval, Reranking, ContextBuild
    }
};

var response = await ragService.GetCompletionAsync("Sua pergunta", options);
```

## Template de Prompt Personalizado

```csharp
.WithRag(rag => rag
    .WithPromptTemplate("""
        Use apenas as informações a seguir para responder à pergunta.
        Se a resposta não estiver no contexto, diga "Não sei."

        Contexto:
        {context}

        Pergunta: {question}
        """)
    .AddDocument("faq.txt")
)
```

## Compartilhando um RagStore

Construa o índice uma vez e reutilize-o em múltiplas instâncias de serviço:

```csharp
// Construir uma vez
RagStore store = await RagStore.BuildAsync(rag => rag
    .UseOpenAIEmbedding(apiKey)
    .AddDocuments("docs/"));

// Reutilizar em vários serviços
var claudeRag = new AnthropicService(apiKey, http).WithRag(store);
var gptRag    = new OpenAIService(apiKey, http).WithRag(store);
```

## Consulta Direta ao RagStore

Consulte o store independentemente de qualquer serviço de IA para inspecionar o que seria recuperado:

```csharp
RagProcessedQuery result = await store.QueryAsync("Qual é a política de devolução?");

Console.WriteLine($"Consulta reescrita: {result.RewrittenQuery}");

foreach (var ref_ in result.References)
{
    Console.WriteLine($"[{ref_.Score:F2}] {ref_.Record.Content[..100]}");
}
```

`result.RequestMessageContent` contém o prompt completamente montado que seria enviado ao LLM. Extremamente útil para depurar a qualidade da recuperação sem gastar tokens LLM.

## Como Funciona Internamente

Quando você chama `.WithRag()`, um wrapper `RagEnabledService` é criado em torno do seu AIService. O mecanismo chave é [AIRequestContext](request-contexts.md):

- O histórico de conversa mantém a pergunta original
- O modelo recebe o prompt montado (com documentos recuperados + pergunta)
- O estado do AIService nunca é mutado — `AsyncLocal<T>` fornece isolamento por requisição
