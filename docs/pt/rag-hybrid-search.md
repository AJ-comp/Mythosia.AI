# Hybrid Search

Códigos favorecem a pesquisa por palavras-chave; perguntas com outra formulação favorecem a semântica. O recuperador escolhido prepara apenas a representação necessária, sem exigir embedding antes da pesquisa lexical.

## Modos integrados

```csharp
// Pesquisa semântica (padrão)
.UseVectorSearch()

// Pesquisa lexical sem embedding de consulta
.UseKeywordSearch()

// Pesquisa híbrida ponderada
.UseHybridSearch(new HybridSearchOptions
{
    VectorWeight = 0.7f,
    CandidateMultiplier = 4,
    RrfK = 60
})
```

`HybridSearchOptions`: `using Mythosia.VectorDb;`

`UseKeywordSearch()` dispensa embeddings de consulta. A ingestão continua dividindo documentos e gerando embeddings para o armazenamento vetorial existente; não é indexação apenas textual. A inicialização adiada ainda pode chamar embeddings de documentos na primeira pergunta.

## Combinar resultados lexicais e semânticos

`VectorWeight` define o peso vetorial (0–1); o lexical é `1 - VectorWeight`. `CandidateMultiplier` controla candidatos por ramo e `RrfK` a suavização de posições na Reciprocal Rank Fusion ponderada. São separados do multiplicador do reranker RAG. Avalie com documentos e perguntas representativos.

Os modos vetorial e lexical puros preservam pontuações nativas. O híbrido configurável usa RRF ponderado normalizado mesmo com um ramo; peso vetorial 0 dispensa embedding de consulta. Pontuações não são probabilidades. `WeightedBlend` mistura pontuações sem calibração; prefira `RerankerOnly` para busca lexical sem calibração prévia.

```csharp
using Mythosia.AI.Rag;
using Mythosia.VectorDb;

var service = new OpenAIService(apiKey, http)
    .WithRag(rag => rag
        .AddDocument("manual.txt")
        .UseHybridSearch(new HybridSearchOptions
        {
            VectorWeight = 0.7f,
            CandidateMultiplier = 4,
            RrfK = 60
        }));

string answer = await service.GetCompletionAsync("What is the refund policy?");
```

## Suporte e compatibilidade

InMemory, PostgreSQL e Qdrant suportam os novos caminhos lexical e RRF ponderado configurável. A pontuação textual difere: BM25 no InMemory, busca textual ou trigramas configurados no PostgreSQL, índice esparso no Qdrant. Pontuações entre mecanismos não são equivalentes.

Pinecone mantém o caminho híbrido nativo via `UseHybridSearch()` com valores padrão em um índice `dotproduct` compatível. Este adaptador não suporta modo lexical nem RRF ponderado configurável com ambos os ramos. Outros armazenamentos precisam das interfaces opcionais correspondentes. Modos ou opções incompatíveis falham explicitamente, sem mudar silenciosamente para busca vetorial nem ignorar pesos.

Os adaptadores existentes de InMemory, PostgreSQL e Qdrant não instalam modelos neurais nem migram índices. A distinção `C#`/`C++` depende dos analisadores. A opção PIXIE abaixo também exige avaliação de identificadores exatos.

Veja [modos e suporte](rag.md#retrieval-modes) e [recuperadores personalizados](rag-pipeline.md#custom-retriever).

<a id="pixie-search"></a>

## Comparar busca neural local com PIXIE

Quando pergunta e documento usam expressões diferentes, a busca esparsa aprendida pode acrescentar vocabulário relacionado. O pacote opcional `Mythosia.AI.Rag.Search.Pixie` codifica documentos e consultas localmente com PIXIE e combina os resultados com seus embeddings densos existentes. PIXIE não exige servidor Python nem chave de API; os provedores escolhidos para embeddings densos ou respostas ainda podem usar APIs remotas.

```csharp
using Mythosia.AI.Rag;
using Mythosia.AI.Rag.Search.Pixie;
using Mythosia.VectorDb;

using var encoder = new PixieSparseEncoder(new PixieOptions());
var searchStore = new PixieInMemoryStore(encoder);
RagStore rag = await RagStore.BuildAsync(builder => builder
    .UseEmbedding(embeddings)
    .UseStore(searchStore)
    .AddDocument("manual.txt")
    .UseHybridSearch(new HybridSearchOptions { VectorWeight = 0.7f }));

RagProcessedQuery result = await rag.QueryAsync("refund policy");
```

Com este armazenamento, `UseKeywordSearch()` escolhe busca neural esparsa: dispensa o provedor de embedding denso da consulta, mas executa PIXIE na pergunta. A ingestão RAG ainda cria embeddings densos de documentos. `UseHybridSearch(...)` combina as posições do produto escalar esparso e do cosseno denso pelo RRF ponderado configurado.

Esta prévia fornece `PixieInMemoryStore`, um índice em memória. Não conecta PIXIE a PostgreSQL, Qdrant ou Pinecone. Recrie o índice após reiniciar ou alterar modelo/configurações. Mantenha o codificador ativo durante todas as operações e libere-o ao terminar. A busca existente continua padrão: compare os mesmos documentos e perguntas com relevância avaliada antes de mudar. PIXIE não garante distinção exata entre `C#` e `C++` nem condições de exclusão.

[Guia PIXIE e comparação (inglês)](../rag-pixie-search.md).
