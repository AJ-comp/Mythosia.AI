# RAG (Retrieval-Augmented Generation)

Para uma resposta final com botão Parar, passe `cancellationToken` a `GetCompletionAsync`. Use Run para eventos de progresso ou instruções adicionais suportadas. Consulte [cancelamento](completions.md#completion-cancellation).

Para respostas enriquecidas com pesquisa, passe também `cancellationToken` a `RagEnabledService.GetCompletionAsync`. O mesmo token chega à pesquisa, `LlmQueryRewriter`, `LlmReranker` e resposta interna; cancelar durante a pesquisa evita a chamada posterior ao modelo. `RagPipeline.QueryAndGenerateAsync` também transmite o token. Os componentes devem cooperar; pesquisas e ações de ferramentas já concluídas não são revertidas.

O RAG permite que o modelo responda perguntas com base nos seus próprios documentos, recuperando chunks relevantes no momento da consulta.

Para permitir que o usuário acompanhe ou interrompa a escrita de uma resposta baseada nos seus documentos, combine a busca RAG com um run. O [guia de Run](execution-api-transition.md) explica o fluxo e os limites das instruções adicionais.


Com uma referência `IAIService`, use `GetLastProcessing()` de `Mythosia.AI.Extensions`. Ele lê a interface opcional `IAIProcessingInfoService` e retorna uma lista vazia sem diagnóstico disponível. `IAIService` não ganha membros obrigatórios. No RAG, `RagEnabledService.WithSpeed(...)` configura a próxima resposta após a busca; `LastProcessing` descreve essa resposta. Reescrita interna fica separada e o Run expõe os mesmos registros `Processing`. [WithSpeed](request-building.md#inference-speed)

## Instalação

```bash
dotnet add package Mythosia.AI.Rag
```

## Início Rápido

Use `.WithRag()` em qualquer `IAIService` para habilitar o RAG com uma API fluente:

```csharp
using Mythosia.AI.Rag;

var service = new AnthropicService(apiKey, http)
    .WithRag(rag => rag
        .AddDocument("manual.txt")
        .AddDocument("politica.txt")
    );

var response = await service.GetCompletionAsync("Qual é a política de reembolso?");
```

Os documentos são divididos, embutidos e armazenados automaticamente. No momento da consulta, os chunks mais relevantes são recuperados e injetados no prompt.

A prévia opcional `Mythosia.AI.Rag.Search.Pixie` permite comparar busca neural esparsa local com a busca existente. Mantém o provedor de embeddings densos e um índice PIXIE em memória, sem migrar armazenamentos persistentes nem substituir a busca padrão. [Guia PIXIE e comparação (inglês)](rag-hybrid-search.md#pixie-search).

<a id="rag-message-attachments"></a>

## Responder sobre um anexo usando seus documentos

Para explicar uma foto de produto com base no manual, passe uma `Message` contendo a pergunta e a imagem para `RagEnabledService.GetCompletionAsync(Message)` ou `StartRunAsync(Message)`. Ambos preservam anexos não textuais na solicitação ao serviço de IA interno. A busca usa o texto da mensagem; os anexos em si não são indexados nem convertidos em embeddings automaticamente. O provedor e o modelo escolhidos devem aceitar esse tipo de anexo. O contexto recuperado é adicionado apenas à solicitação enviada: ele não sobrescreve a `Message` original nem substitui o texto do usuário no histórico da conversa.

Quando uma resposta precisa tanto de um manual quanto do estoque atual, combine RAG com suas ferramentas registradas. Durante as chamadas de ferramentas de `GetCompletionAsync`, o contexto recuperado permanece na entrada inicial e cada resultado posterior é enviado ao modelo sem alterações. O histórico preserva a entrada original do usuário.

<a id="retrieval-modes"></a>

## Escolher como pesquisar documentos

Códigos favorecem a pesquisa por palavras-chave; perguntas com outra formulação favorecem a semântica. O recuperador escolhido prepara apenas a representação necessária, sem exigir embedding antes da pesquisa lexical.

```csharp
RagStore store = await RagStore.BuildAsync(rag => rag
    .AddDocument("manual.txt")
    .UseKeywordSearch());

RagProcessedQuery result = await store.QueryAsync("refund policy");
```

`UseKeywordSearch()` dispensa embeddings de consulta. A ingestão continua dividindo documentos e gerando embeddings para o armazenamento vetorial existente; não é indexação apenas textual. A inicialização adiada ainda pode chamar embeddings de documentos na primeira pergunta.

Veja [modos e suporte](rag-hybrid-search.md) e [recuperadores personalizados](rag-pipeline.md#custom-retriever).

## Adicionando Documentos

Vários tipos de fontes são suportados:

```csharp
.WithRag(rag => rag
    .AddDocument("readme.txt")                    // arquivo local
    .AddUrl("https://example.com/doc.txt")        // URL
    .AddText("Conteúdo inline pode ir aqui também.")   // string bruta
)
```

`AddUrl` valida e descomprime os formatos HTTP suportados antes de ler o texto e rejeita codificações incompletas, não suportadas ou com várias camadas. Consulte [descompressão de URL e cancelamento](rag-pipeline.md#url-documents).

<a id="document-identity"></a>

### Distinguir arquivos com o mesmo nome

Duas empresas podem fornecer, cada uma, um `docs/faq.txt`. Ambos os documentos devem permanecer no índice, enquanto registrar novamente o mesmo arquivo deve reutilizar sua identidade:

```csharp
var store = await RagStore.BuildAsync(rag => rag
    .AddDocuments("company-a/docs")
    .AddDocuments("company-b/docs"));
```

No fluxo de armazenamento padrão do RAG, o ID do documento é criado antes de enviar os registros ao armazenamento vetorial; em seguida, os registros com o mesmo `document_id` são substituídos. Nosso armazenamento PostgreSQL (pgvector) usa esse ID e não inspeciona por conta própria o caminho original do arquivo. Antes, o registro de diretórios enviava `faq.txt` tanto para `company-a/docs/faq.txt` quanto para `company-b/docs/faq.txt`, fazendo o segundo documento substituir o primeiro. A correção preserva o caminho completo ao criar o ID; o esquema do PostgreSQL permanece igual. Um filtro `full_path` em um exemplo de armazenamento usa metadados fornecidos por quem faz a chamada; ele não gera automaticamente IDs exclusivos de documentos ou registros.

Os carregadores integrados `PlainTextDocumentLoader` e `DirectoryDocumentLoader` usam o caminho absoluto do arquivo, normalizado por `Path.GetFullPath`, como `Source` e ID automático do documento. Assim, arquivos em diretórios diferentes recebem IDs diferentes. Caminhos relativos, absolutos e com `./` reutilizam o ID quando resultam no mesmo caminho absoluto, incluindo maiúsculas e minúsculas. Mantenha o diretório de trabalho constante ao usar caminhos relativos. Mover arquivos, usar links simbólicos ou físicos, ou alterar a capitalização não garante preservar o ID.

`AddText(..., id: ...)`, um `RagDocument.Id` definido explicitamente e as regras de `Source` dos carregadores personalizados permanecem iguais. Não é preciso alterar a API de chamada. Como o `Source` desses carregadores integrados agora é absoluto, as citações padrão também podem exibir caminhos absolutos. Para exibição, use `filename` ou os metadados `relative_path` do carregador de diretórios padrão. A sobrecarga de diretório com configuração não adiciona `relative_path` automaticamente.

**Índices existentes:** Os IDs antigos baseados em caminhos relativos não são excluídos nem migrados automaticamente. Prefira reindexar todos os documentos em uma nova coleção, verificá-la e depois mudar a aplicação. Ao reutilizar uma coleção, exclua apenas os IDs antigos cuja origem tenha confirmado e reindexe seus arquivos de origem. Não exclua documentos amplamente pelo nome do arquivo: outros diretórios podem conter documentos com o mesmo nome.

Para limitar atualizações e exclusões ao documento correto, `document_id` é uma chave reservada do pipeline. Antes da persistência, cada registro recebe o `RagDocument.Id` real, mesmo que os metadados de entrada indiquem outro valor. Os dicionários de metadados do documento de entrada e do splitter não são alterados; callbacks de persistência personalizados também recebem os registros normalizados. Use outra chave para um identificador próprio da aplicação.

Isso não corrige registros já salvos com um `document_id` incorreto. Reconstrua uma nova coleção a partir de fontes confiáveis, ou identifique e limpe apenas os registros afetados antes de reindexar. Reindexar somente com o ID correto não encontra de modo confiável registros antigos salvos sob outro ID.

Registrar o mesmo arquivo por caminhos relativos e absolutos deve atualizar um único documento; arquivos de mesmo nome em pastas diferentes devem permanecer separados. `WordDocumentLoader`, `ExcelDocumentLoader`, `PowerPointDocumentLoader` e `PdfDocumentLoader` agora definem `DoclingDocument.Source` como o caminho absoluto normalizado, assim como os loaders TXT integrados. O RAG deriva daí os IDs automáticos; IDs explícitos continuam sob controle do chamador. As citações padrão podem exibir caminhos absolutos.

[Manter uma identidade estável para cada arquivo](document-loaders.md#file-source-identity).

<a id="empty-document-updates"></a>

### Esvaziar um documento sem manter resultados antigos

Se você esvaziar uma política de reembolso desativada e reindexar o mesmo documento, o texto anterior deve deixar de aparecer nas respostas. No armazenamento RAG padrão, uma divisão bem-sucedida que produz zero fragmentos substitui os registros daquele `document_id` por um conjunto vazio. Nenhum embedding é solicitado, e outros IDs permanecem intactos. Isso inclui documentos vazios ou contendo apenas espaços quando o divisor retorna zero fragmentos, além de divisores personalizados que retornam zero fragmentos com sucesso.

Em uma `RagPipeline` já configurada chamada `pipeline`, reutilize o ID do documento armazenado:

```csharp
await pipeline.IndexDocumentAsync(
    new RagDocument { Id = "refund-policy", Content = "" },
    cancellationToken);
```

Você pode indexar conteúdo não vazio com o mesmo ID posteriormente. Um carregador que não retorna documentos, ou um documento ausente de uma lista posterior de arquivos, não constitui uma instrução de exclusão: nenhum ID foi fornecido para substituição.

Exceções de carregamento, análise ou divisão, e cancelamento observado antes da chamada ao armazenamento, preservam os registros daquele documento. Carregadores e analisadores devem comunicar falhas por exceções; um resultado bem-sucedido com zero fragmentos não pode ser distinguido de um esvaziamento intencional. Após o início da gravação, a reversão em caso de falha ou cancelamento depende do armazenamento; o PostgreSQL usa uma transação na substituição. Um lote processa cada documento separadamente e não reverte os documentos já concluídos.

**Persistência personalizada:** ao fornecer `onDocumentEmbedded`, a persistência continua sob responsabilidade do callback. Zero fragmentos não invocam o callback nem acessam o armazenamento padrão. A aplicação deve excluir explicitamente o ID conhecido no próprio armazenamento, ou usar `DeleteDocumentAsync` para o armazenamento da pipeline.

## Provedor de Embedding Personalizado

Por padrão, o RAG usa o provedor local de embeddings integrado. Para usar um modelo de embedding dedicado:

```csharp
using Mythosia.AI.Rag.Embeddings;

var embedder = new OpenAIEmbeddingProvider(apiKey, http, "text-embedding-3-small");

var service = new AnthropicService(apiKey, http)
    .WithRag(rag => rag
        .UseEmbedding(embedder)
        .AddDocument("base-conhecimento.txt")
    );
```

## Vector Store Personalizado

Por padrão, um store em memória é usado. Para produção, use um vector store persistente:

```bash
dotnet add package Mythosia.VectorDb.Postgres
```

```csharp
using Mythosia.VectorDb.Postgres;

var store = new PostgresStore(new PostgresOptions
{
    ConnectionString = connectionString,
    Dimension = 1536
});

var service = new OpenAIService(apiKey, http)
    .WithRag(rag => rag
        .UseStore(store)
        .AddDocument("corpus-grande.txt")
    );
```

## Opções de Consulta

Ajuste o comportamento de recuperação por consulta:

```csharp
var options = new RagQueryOptions
{
    FinalFilter = new RagFilter
    {
        TopK = 5,
        MinScore = 0.7
    }
};

var response = await service.GetCompletionAsync("Sua pergunta", options: options);
```

Se seu índice já estiver hospedado pelo provedor do modelo, compare RAG com a [busca nativa de arquivos e as opções comuns de raciocínio](reasoning-and-search.md).

## Próximos Passos

- [Hybrid Search](rag-hybrid-search.md) — combine busca semântica e por palavras-chave
- [Reescrita de Consulta](rag-query-rewriting.md) — otimize consultas com contexto de conversa
- [Re-ranking](rag-reranking.md) — refine ainda mais a precisão dos resultados de busca
- [Personalização de Pipeline](rag-pipeline.md) — controle refinado sobre o processo RAG
- [Agentic RAG](rag-agentic.md) — IA decide quando e o que pesquisar
- [Vector Stores](vectordb-overview.md) — configuração de armazenamento persistente
- [Text Splitters](text-splitters.md) — personalize como os documentos são divididos

Perplexity: [Usar vetores em seu próprio índice / Pesquisar sem gerar uma resposta](perplexity.md).
