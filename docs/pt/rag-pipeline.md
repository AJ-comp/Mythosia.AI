# Personalização do Pipeline RAG

<a id="indexing-validation"></a>

## Proteger documentos existentes quando a indexação falha

Um divisor personalizado ou uma resposta de embeddings incorreta não deve substituir silenciosamente um documento pesquisável por conteúdo incompleto ou associado de forma errada. O pipeline valida cada documento antes de iniciar a persistência, inclusive ao usar `onDocumentEmbedded`.

Antes dos embeddings, do armazenamento ou do callback de persistência, um `RagDocument.Id` nulo, vazio ou contendo apenas espaços provoca `ArgumentException`. Uma saída inválida do divisor provoca `InvalidOperationException`: lista ou fragmento nulo, `Content` ou `Metadata` nulos, ID de fragmento vazio ou contendo apenas espaços, ou IDs repetidos no mesmo documento. A comparação usa `StringComparer.Ordinal`, diferenciando maiúsculas e minúsculas. Os valores dos fragmentos e seus metadados são copiados antes da primeira chamada de embeddings.

IDs personalizados válidos são mantidos exatamente como recebidos. Não há geração automática, remoção de espaços ou correção, e colisões de IDs de fragmentos entre documentos diferentes não são detectadas globalmente. Use IDs únicos na coleção de destino, como no [exemplo de divisor personalizado](text-splitters.md). A chave reservada `document_id` é normalizada apenas na cópia usada para armazenamento; os metadados originais não mudam.

IDs inválidos, falhas de divisão e lotes de embeddings inválidos preservam os registros anteriores desse documento e não invocam o callback de persistência. Todos os lotes do documento devem passar pela [validação de embeddings](rag-embedding.md#embedding-validation) antes do armazenamento. Isso não desfaz documentos já concluídos anteriormente na operação; após o início do armazenamento, o rollback depende do armazenamento ou callback.

Essas verificações e correções da ordem das respostas não recuperam automaticamente conteúdo já sobrescrito nem vetores armazenados associados aos fragmentos errados; reindexe os documentos afetados a partir das fontes originais.

<a id="custom-persistence"></a>

## Substituir todo o documento no callback de persistência

Quando um documento fica menor, fazer upsert apenas dos novos chunks deixa a parte final antiga disponível na busca. `onDocumentEmbedded` substitui toda a persistência padrão: use o `document_id` normalizado dos registros para substituir o documento completo. O callback recebe um documento validado e não vazio de cada vez:

```csharp
using Mythosia.AI.Rag;
using Mythosia.VectorDb;

var store = await RagStore.BuildAsync(config => config
    .AddDocuments("./docs/")
    .UseOpenAIEmbedding(apiKey)
    .UseStore(vectorStore),
    onDocumentEmbedded: async records =>
    {
        var documentId = records[0].Metadata["document_id"];
        await vectorStore.ReplaceByFilterAsync(
            new VectorFilter().Where("document_id", documentId), records, cancellationToken);
    },
    cancellationToken: cancellationToken);
```

Uma divisão bem-sucedida com zero chunks não chama esse callback nem acessa o armazenamento padrão. Exclua explicitamente o ID conhecido no seu próprio armazenamento; use `DeleteDocumentAsync` apenas para o armazenamento da pipeline. Atomicidade e rollback dependem do armazenamento ou callback.

<a id="url-documents"></a>

## Ler documentos URL com segurança

Um servidor pode comprimir um documento de texto para transferência. `AddUrl` descomprime `gzip`, `deflate` e Brotli (`br`) antes de ler o texto e verifica se o fluxo comprimido está completo. Uma transferência HTTP bem-sucedida não basta: dados comprimidos truncados, erros de descompressão ou falhas nas somas de verificação incluídas no formato interrompem o carregamento antes do embedding ou da persistência, preservando os registros anteriores do documento. Valores de `Content-Encoding` não suportados ou com várias camadas também são rejeitados antes do embedding ou da persistência.

Para deixar de aguardar um documento URL lento, passe `cancellationToken` a `RagStore.BuildAsync`. O token chega à requisição HTTP, à leitura do corpo e à descompressão. O cancelamento é cooperativo e não desfaz documentos já salvos.

<a id="custom-retriever"></a>

## Conectar um recuperador sem embeddings obrigatórios

Códigos favorecem a pesquisa por palavras-chave; perguntas com outra formulação favorecem a semântica. O recuperador escolhido prepara apenas a representação necessária, sem exigir embedding antes da pesquisa lexical.

- Antes: cada estratégia recebia um embedding de consulta.
- Depois: o recuperador prepara apenas a representação necessária.

Implemente `IRagRetriever` para índices externos ou outras representações. `RagRetrievalRequest` contém `Query` (consulta semântica completa), `TextQuery` nullable (substituição lexical), `TopK`, `Filter` e `ProgressAsync`. Os recuperadores integrados usam `Query` quando `TextQuery` é null; uma string vazia omite o ramo textual. O recuperador personalizado deve preparar a consulta e respeitar filtro, limite e cancelamento.

```csharp
using Mythosia.AI.Rag;
using Mythosia.VectorDb;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

public sealed class CatalogRetriever : IRagRetriever
{
    private readonly ITextSearchStore _catalog;
    public CatalogRetriever(ITextSearchStore catalog) => _catalog = catalog;

    public Task<IReadOnlyList<VectorSearchResult>> RetrieveAsync(
        RagRetrievalRequest request,
        CancellationToken cancellationToken = default)
        => _catalog.TextSearchAsync(
            request.TextQuery ?? request.Query,
            request.TopK,
            request.Filter,
            cancellationToken);
}
```

```csharp
// catalog: an existing ITextSearchStore
var service = new OpenAIService(apiKey, http)
    .WithRag(rag => rag.UseRetriever(new CatalogRetriever(catalog)));
```

Registre com `UseRetriever(...)` ou `RagPipeline.SetRetriever(...)`. `IRetrievalStrategy` e `SetRetrievalStrategy(...)` continuam disponíveis por um adaptador que gera embeddings de consulta. Resultados devem conter texto e metadados para reclassificação e contexto.

```csharp
store.UpdateRetriever(new CatalogRetriever(catalog));
store.UseKeywordSearch();
store.UpdateRetrievalStrategy(new HybridSearchOptions { VectorWeight = 0.7f });
store.UpdateRetriever(null); // UseVectorSearch
```

`UseKeywordSearch()` dispensa embeddings de consulta. A ingestão continua dividindo documentos e gerando embeddings para o armazenamento vetorial existente; não é indexação apenas textual. A inicialização adiada ainda pode chamar embeddings de documentos na primeira pergunta.

A etapa de consulta `Embedding` depende do recuperador; busca lexical não a reporta. Um recuperador personalizado pode reportar etapas por `request.ProgressAsync`. Embeddings de documentos permanecem iguais.

## Por que Personalizar o Pipeline?

O pipeline RAG padrão funciona bem fora da caixa, mas projetos reais frequentemente precisam de mais controle:

- **Depuração** — qual estágio é lento? O rewriter está alterando a consulta de formas inesperadas?
- **Engenharia de prompt** — o template de prompt padrão pode não se adequar ao tom ou restrições do seu domínio
- **Arquitetura** — múltiplos serviços compartilhando um índice economiza memória e mantém os embeddings consistentes
- **Inspeção** — às vezes você precisa ver o que a recuperação retorna *antes* de enviar ao LLM

O pipeline personalizado pode ser combinado com um run para mostrar o progresso e permitir cancelamento durante a resposta. O [guia de Run](execution-api-transition.md) explica quando a busca acontece e o que as instruções adicionais modificam.

## Rastreamento de Progresso

Rastreie qual estágio RAG está executando via callback assíncrono por consulta:

```csharp
var options = new RagQueryOptions
{
    ProgressAsync = async stage =>
    {
        Console.WriteLine($"[RAG] {stage}");
        // Estágios: QueryRewrite, Filtering, Embedding, Retrieval, Reranking, ContextBuild
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

`AIService` armazena o contexto em `AsyncLocal`. `GetLatestMessages()` aplica `RequestMessageOverride` à entrada inicial da requisição lógica atual e preserva as chamadas de ferramentas do assistente e seus resultados posteriores. Assim, os documentos recuperados e os resultados das ferramentas são enviados juntos nas próximas requisições ao modelo. Ao concluir, o contexto anterior é restaurado.
