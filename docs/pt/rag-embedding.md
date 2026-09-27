# Embedding

> 📍 **Pipeline de Pergunta e Resposta:** [Reescrita de Consulta](rag-query-rewriting.md) → [Filtragem](rag-filtering.md) → **`Embedding (quando necessário)`** → [Recuperação](rag-hybrid-search.md) → [Re-ranking](rag-reranking.md) → [Construção de Contexto](rag-context-build.md)

A etapa de consulta `Embedding` depende do recuperador; busca lexical não a reporta. Um recuperador personalizado pode reportar etapas por `request.ProgressAsync`.

<a id="retrieval-aware-embeddings"></a>

## Preservar o contexto do documento e a intenção da consulta

Um fragmento pode depender das passagens vizinhas, e uma pergunta de busca tem um papel diferente do documento indexado. O RAG 8.2.0 adiciona embeddings contextuais Voyage e Gemini Embedding 2 para texto extraído de TXT, Markdown e PDF.

```csharp
using Mythosia.AI.Rag;

var store = await RagStore.BuildAsync(rag => rag
    .UseVoyageEmbedding(voyageApiKey, httpClient)
    .AddDocument("policy.pdf"));
var result = await store.QueryAsync("What is the refund period?");
```

`IRetrievalEmbeddingProvider : IEmbeddingProvider` é opcional; os provedores existentes continuam funcionando. A indexação entrega um `EmbeddingDocument(documentId, chunks, title)` imutável com todos os fragmentos ordenados, independentemente de `EmbeddingBatchSize`. O título vem de `RagDocument.Metadata["title"]`. A busca vetorial e os diagnósticos chamam `GetQueryEmbeddingAsync`; provedores anteriores mantêm lotes `GetEmbeddingsAsync` e consultas `GetEmbeddingAsync`. Busca apenas por palavras-chave não gera embedding da consulta.

Escolha uma configuração de embeddings por armazenamento:

```csharp
rag.UseVoyageEmbedding(voyageApiKey, httpClient,
    model: "voyage-context-4", dimensions: 1024,
    timeout: TimeSpan.FromSeconds(60));

rag.UseGeminiEmbedding(geminiApiKey, httpClient,
    model: "gemini-embedding-2", dimensions: 1536,
    timeout: TimeSpan.FromSeconds(60), maxConcurrency: 4);
```

### Voyage

`VoyageContextualizedEmbeddingProvider` usa por padrão `voyage-context-4` e 1024 dimensões (256, 512, 1024 ou 2048). Envia o documento inteiro em um grupo ordenado com `input_type=document`, e a consulta em um grupo próprio com `input_type=query`. A fragmentação automática fica desativada. Cada documento aceita até 16.000 fragmentos; o serviço impõe os limites de tokens. Os métodos genéricos omitem `input_type` e tratam até 1.000 textos como grupos independentes de um único fragmento. ID e título não são enviados. [Voyage API](https://docs.voyageai.com/docs/contextualized-chunk-embeddings).

Os lotes genéricos verificam o cancelamento durante a leitura das entradas. Ao ultrapassar 1.000 textos, a leitura para e o lote é rejeitado sem enviar uma requisição HTTP. Os grupos de documentos permanecem completos.

### Gemini

`GeminiEmbeddingProvider` usa por padrão `gemini-embedding-2`, 1536 dimensões (128–3072) e `maxConcurrency=4`. Cada fragmento recebe um vetor por uma requisição HTTP própria. A entrada de busca usa `title: {title} | text: {text}` (sem título: `none`) ou `task: search result | query: {query}`. Esses prefixos afetam apenas a entrada HTTP; métodos genéricos enviam o texto original. `embedContentConfig.autoTruncate=false` rejeita entradas longas demais sem encurtá-las. [Gemini API](https://ai.google.dev/gemini-api/docs/embeddings).

Os dois provedores preservam o texto armazenado e validam quantidade, dimensões e valores finitos dos vetores. O `HttpClient` continua pertencendo ao chamador, com os mesmos ajustes. Voyage restaura a ordem a partir de índices de resposta validados. Erros omitem chaves e conteúdo remoto; o cancelamento é propagado e tempos esgotados geram `TimeoutException`. O `timeout` de Voyage vale por requisição; o de Gemini cobre a operação inteira, incluindo esperas de concorrência. O limite do cliente também se aplica. Não há divisão nem truncamento silencioso. Falhas antes da persistência preservam o documento anterior; depois, a atomicidade depende do armazenamento ou callback. Ao mudar modelo, dimensões ou formato de busca, reindexe os documentos e configure o armazenamento para o mesmo espaço vetorial.

### Verificar o serviço real

Os testes ao vivo enviam texto sintético TXT, Markdown e PDF e geram custos de API. Defina `MYTHOSIA_RETRIEVAL_EMBEDDING_LIVE=1`, configure as credenciais e escolha `All`, `Voyage` ou `Gemini`. O executor rejeita casos ignorados ou inconclusivos; testes offline não comprovam a disponibilidade do serviço.

```powershell
$env:MYTHOSIA_RETRIEVAL_EMBEDDING_LIVE = "1"
pwsh -NoProfile -File build/test-retrieval-embedding-live.ps1 -Provider All
```

[Verificar o serviço real](https://github.com/AJ-comp/Mythosia.AI/blob/main/build/RELEASE.md#retrieval-embedding-live-validation).

## O que é Embedding?

Embedding é o processo de converter texto em vetores numéricos que capturam significado. Esses vetores ficam em um espaço de alta dimensionalidade onde **textos com significados semelhantes ficam próximos uns dos outros**.

No pipeline RAG, o embedding acontece em dois pontos:

1. **Indexação de documentos** — cada chunk é embutido e armazenado no vector store
2. **Tempo de consulta** — a pergunta do usuário é embutida para ser comparada com os chunks armazenados

## Provedores de Embedding Integrados

Escolha o provedor de embeddings conforme o idioma dos documentos, o ambiente de hospedagem e as necessidades de recuperação.

### Perplexity

`PerplexityContextualizedEmbeddingProvider` agora implementa `IRetrievalEmbeddingProvider` e pode ser registrado com `.UseEmbedding(contextual)`. A API pública agrupada `GetDocumentEmbeddingsAsync` e os métodos binários são mantidos. O novo método para um documento implementa a interface explicitamente, preservando chamadas existentes. O RAG mantém os limites dos documentos e usa o mesmo modelo contextual e dimensões nas consultas.

Os lotes de ponto flutuante e binários do Perplexity aceitam até 512 textos independentes, ou 512 documentos contextuais com um total de 16.000 trechos. A validação verifica o cancelamento durante a leitura e para ao ultrapassar um limite, rejeitando o lote antes de enviar HTTP. O agrupamento e a ordem são preservados. Veja o [guia do Perplexity](perplexity.md).

[Perplexity Agent API, pesquisa e embeddings](perplexity.md).

### OpenAI Embedding

A opção mais popular baseada em nuvem:

```csharp
using Mythosia.AI.Rag.Embeddings;

var embedder = new OpenAIEmbeddingProvider(
    apiKey: "sk-...",
    httpClient: new HttpClient(),
    model: "text-embedding-3-small",
    dimensions: 1536
);
```

Ou com o builder fluente:

```csharp
.WithRag(rag => rag
    .UseOpenAIEmbedding(apiKey, model: "text-embedding-3-small", dimensions: 1536)
    .AddDocument("docs.txt")
)
```

<a id="openai-dimensions"></a>

`text-embedding-ada-002` tem um tamanho fixo de **1536 dimensões**. O provedor omite o campo `dimensions`, não compatível com esse modelo, nas solicitações individuais e em lote; configurar outro tamanho gera uma `ArgumentOutOfRangeException` antes de qualquer chamada à API. As solicitações para `text-embedding-3-small` e `text-embedding-3-large` continuam incluindo o valor configurado de `dimensions`.

### Ollama (Local)

Execute embeddings localmente sem enviar dados para a nuvem:

```csharp
var embedder = new OllamaEmbeddingProvider(
    httpClient: new HttpClient(),
    model: "qwen3-embedding:4b",
    dimensions: 1024,
    baseUrl: "http://localhost:11434"
);
```

<a id="ollama-dimensions"></a>

Os vetores dos documentos e das consultas devem usar o mesmo modelo e as mesmas dimensões. `OllamaEmbeddingProvider` envia as `dimensions` configuradas para `/api/embed` e verifica o comprimento de cada vetor recebido. O provedor mantém `qwen3-embedding:4b` com **1024 dimensões solicitadas** como padrão; a saída nativa do modelo tem 2560 dimensões. O servidor Ollama e o modelo escolhido devem suportar o tamanho solicitado. Uma solicitação não suportada ou uma resposta que a ignore falha, sem alterar `Dimensions` silenciosamente nem redimensionar vetores localmente.

Se alterar o modelo ou as dimensões, recrie os embeddings dos documentos com as mesmas configurações usadas nas consultas e ajuste o armazenamento vetorial. Os vetores existentes não são convertidos automaticamente.

### vLLM (Auto-hospedado)

```csharp
var embedder = new VllmEmbeddingProvider(
    httpClient: new HttpClient(),
    model: "Qwen/Qwen3-Embedding-0.6B",
    dimensions: 1024,
    baseUrl: "http://localhost:8002"
);
```

### Local (Sem API)

Um provedor leve baseado em hashing de features. **Não recomendado para produção.**

```csharp
.WithRag(rag => rag
    .UseLocalEmbedding(dimensions: 1024)
    .AddDocument("docs.txt")
)
```

## Processamento em lotes

`EmbeddingBatchSize` controla os lotes planos das implementações anteriores de `IEmbeddingProvider`. `IRetrievalEmbeddingProvider` recebe o documento inteiro e gerencia o envio HTTP; reduzir esse valor não divide um documento Voyage em vários grupos de contexto.

```csharp
var options = pipeline.Options.Clone();
options.EmbeddingBatchSize = 100;
pipeline.Options = options;
```

`EmbeddingBatchSize` deve ser positivo. O pipeline valida e captura o valor no início de cada chamada de indexação de documento, antes do embedding ou da substituição dos registros. Isso evita ciclos de lotes vazios e que mudanças durante uma espera assíncrona pulem fragmentos. Chamadas posteriores podem usar o novo valor.

<a id="embedding-validation"></a>

## Manter cada vetor associado ao fragmento correto

Mesmo uma resposta HTTP bem-sucedida pode conter vetores ausentes ou em ordem incorreta, associando o texto ao significado de outro fragmento. Um `IEmbeddingProvider` personalizado deve devolver exatamente um `float[]` não nulo por entrada, na ordem de entrada, e fornecer um valor positivo de `Dimensions`. Cada vetor deve ter esse comprimento e conter apenas valores finitos, sem `NaN` ou infinito.

Durante a indexação, o pipeline rejeita dimensões, quantidades de respostas ou vetores inválidos com `InvalidOperationException` antes do armazenamento ou de `onDocumentEmbedded`. Cada vetor aceito é copiado antes da solicitação do próximo lote, impedindo que a reutilização posterior de um buffer do provedor altere os fragmentos anteriores. Mantenha os dados retornados estáveis durante a leitura; alterações simultâneas durante a validação ou cópia não são suportadas. Se a validação falhar, os registros existentes desse documento são preservados.

`OpenAIEmbeddingProvider` exige um `index` válido e único para cada item de resposta e restaura a ordem de entrada. `VllmEmbeddingProvider` segue a mesma regra quando há índices; por compatibilidade, também aceita respostas em que todos os itens omitem `index`, usando a ordem da resposta. Índices parcialmente ausentes, duplicados ou fora do intervalo são rejeitados. Um provedor personalizado ou sem índices continua responsável pela ordem correta; a validação estrutural não verifica o significado do vetor.

<a id="query-embedding-validation"></a>

## Proteger o vetor da pergunta antes da busca

A reutilização de um buffer não deve alterar a pergunta enquanto notificações ou buscas aguardam. A busca densa integrada, incluindo o adaptador `IRetrievalStrategy`, exige `Dimensions` positivas, um vetor não nulo com esse comprimento exato e valores finitos. Resultados inválidos geram `InvalidOperationException` antes da busca. O vetor aceito é copiado imediatamente após o retorno, antes das notificações ou buscas seguintes. O provedor deve manter os dados estáveis durante a leitura; um `IRagRetriever` personalizado cuida de sua própria preparação e validação.

`OllamaEmbeddingProvider` também valida estrutura, quantidade exata de vetores, dimensões e valores finitos nas chamadas diretas individuais ou em lote. JSON ou vetores inválidos geram `InvalidOperationException` em vez de resultados incompletos. O `HttpClient` fornecido continua pertencendo ao chamador; descartar requisições e respostas HTTP não descarta esse cliente.

## Dimensões

A propriedade `Dimensions` controla o tamanho de cada vetor de embedding. O vector store deve ter a mesma dimensão configurada.

| Provedor | Modelo | Dimensões Padrão |
| --- | --- | --- |
| Voyage | voyage-context-4 | 1024 |
| Gemini | gemini-embedding-2 | 1536 |
| OpenAI | text-embedding-3-small | 1536 |
| OpenAI | text-embedding-ada-002 | 1536 |
| Perplexity | pplx-embed-v1-0.6b | 1024 |
| Perplexity | pplx-embed-v1-4b | 2560 |
| OpenAI | text-embedding-3-large | 3072 |
| Ollama | qwen3-embedding:4b | 1024 solicitadas (nativas: 2560) |
| Local | (hashing de features) | 1024 |

## Provedor de Embedding Personalizado

Implemente `IEmbeddingProvider`:

```csharp
public class MyEmbeddingProvider : IEmbeddingProvider
{
    public int Dimensions => 768;

    public async Task<float[]> GetEmbeddingAsync(
        string text, CancellationToken cancellationToken = default)
    {
        // Chame sua API de embedding aqui
    }

    public async Task<IReadOnlyList<float[]>> GetEmbeddingsAsync(
        IEnumerable<string> texts, CancellationToken cancellationToken = default)
    {
        // Chamada de embedding em lote
    }
}
```

Registre-o com o builder:

```csharp
.WithRag(rag => rag
    .UseEmbedding(new MyEmbeddingProvider())
    .AddDocument("docs.txt")
)
```
