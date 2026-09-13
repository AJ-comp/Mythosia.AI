# Perplexity: respostas com fontes, pesquisa e embeddings

Use o Perplexity quando a resposta precisar de informações recentes e fontes que o leitor possa verificar. `PerplexityService` chama a Agent API; a pesquisa e os embeddings independentes permitem montar a recuperação de documentos com o modelo de resposta que preferir.

## Escolha primeiro o trabalho

Gerar uma resposta atual, obter páginas da web e calcular vetores para um índice próprio são trabalhos diferentes. Escolha o componente responsável sem chamar um modelo de resposta em toda pesquisa.

| Necessidade | Componente |
| --- | --- |
| Resposta pesquisada com fontes | `PerplexityService` |
| Páginas ordenadas para outro modelo ou interface | `PerplexitySearchClient` |
| Vetores de trechos independentes para RAG | `PerplexityEmbeddingProvider` |
| Vetores que consideram trechos vizinhos do mesmo documento | `PerplexityContextualizedEmbeddingProvider` |

Instale `Mythosia.AI` e, para os exemplos de embeddings, `Mythosia.AI.Rag`. Forneça uma chave API e um `HttpClient` gerenciado pela aplicação. Os exemplos usam suas variáveis `apiKey`, `httpClient` e `cancellationToken`.

## Responder com um preset Agent

Um preset combina modelo, instruções, ferramentas, esforço e orçamentos mantidos pelo provedor. Use `Fast` para consultas rápidas, `Low` para pesquisas comuns, `Medium` para comparações em várias etapas e `High` / `XHigh` para aprofundar. `WideResearch` atende pesquisas amplas; use execução em segundo plano para tarefas que devem levar mais tempo. São presets, não IDs de modelo.

```csharp
using Mythosia.AI.Models.Perplexity;
using Mythosia.AI.Services.Perplexity;

var service = new PerplexityService(apiKey, httpClient)
    .WithPerplexityOptions(new PerplexityAgentOptions
    {
        Preset = PerplexityPreset.Low
    });

await using var run = await service.StartRunAsync(
    "Compare os métodos recentes de reciclagem de baterias e cite as fontes.",
    onText: text => Console.Write(text),
    cancellationToken: cancellationToken);
string answer = (await run.Result).Text;
foreach (var source in run.Citations)
    Console.WriteLine(source.Url);
```

Use `GetCompletionAsync` apenas para a resposta, `StreamAsync` do serviço para streaming existente ou `StartRunAsync` para observar e cancelar. `(await run.Result).Text` concatena o texto emitido. `run.Citations` e `LastCitations` mantêm fontes mesmo sem leitura de eventos de citação. Eventos de raciocínio incluem somente o conteúdo exposto pelo provedor e dependem do modelo.

`AIRunResult.RequestedModel` é o modelo único enviado explicitamente na requisição e capturado no início, incluindo uma substituição de modelo do provedor. É `null` quando um preset, perfil ou roteamento do servidor escolhe o modelo sem enviar um campo de modelo único explícito (por exemplo, uma lista Perplexity `Models`). É independente do modelo real da resposta em `Model`.

## Controlar a pesquisa e as ferramentas

`WithPerplexityOptions(...)` define opções persistentes, copiadas por solicitação lógica. As opções comuns `WithReasoning(...)` e `WithWebSearch(...)` valem para a próxima solicitação lógica, incluindo rodadas de funções e reparos da saída tipada. A reescrita interna de consultas RAG não herda a pesquisa da resposta final.

`UsePreset(...)` seleciona um preset diretamente. Preset/profile escolhe seu modelo; `ModelOverride` o substitui. `DisableWebSearch` remove apenas a ferramenta padrão do adaptador, sem garantir desligar a pesquisa do preset. Os esforços, conforme o modelo, são `Minimal`, `Low`, `Medium`, `High`, `XHigh`, `Max`. `None` e esforço explícito para Sonar direto são rejeitados. O `DisableReasoning` interno usa esforço baixo disponível ou omite a opção, sem garantir desligar o raciocínio.

| Opção | Uso |
| --- | --- |
| `Preset` / `ModelOverride` | Escolher uma configuração de pesquisa ou substituir seu modelo com um ID provedor/modelo. |
| `MaxSteps` | Limitar o ciclo hospedado; zero usa o padrão do provedor. É separado de `WithMaxRounds`, que limita continuações de funções locais. |
| `ReasoningEffort` | Ajustar o raciocínio. `Auto` omite a opção; os níveis dependem do modelo efetivamente escolhido. |
| `DisableWebSearch` / `Tools` | Configurar a ferramenta web padrão do adaptador e ferramentas hospedadas explícitas. |
| `Models` | Indicar de um a cinco modelos alternativos por prioridade. A lista substitui o modelo único; todos devem aceitar os recursos solicitados. |
| `Profile` | Usar uma configuração salva e opcionalmente fixar sua versão. Não pode ser combinado com `Preset`. |
| `ServiceTier` | Solicitar processamento padrão, flex ou prioritário. O provedor pode ignorar um nível incompatível. |
| `Skills` | Fornecer habilidades integradas, inline ou personalizadas já enviadas. Recursos personalizados pertencem à conta Perplexity. |
| `LanguagePreference` / `PromptCacheKey` | Definir idioma ou sugestão de roteamento de cache. A sugestão não garante acerto de cache. |
| `PreviousResponseId` / `Store` | Continuar uma resposta concluída ou controlar sua visibilidade para consulta. Use `StatelessMode` e apenas o novo turno na continuação. `Store = false` não desativa a persistência do provedor. |

`PerplexityHostedTool` aceita um `Type` compatível e `Parameters` JSON documentados: `web_search`, `fetch_url`, `finance_search`, `people_search`, `sandbox` ou `mcp`. Servidores MCP e conectores gerenciados executam pelo provedor; credenciais, permissões e recursos da conta precisam corresponder à conexão. Registre funções da aplicação por `Functions` / construtor de funções. Etapas hospedadas e handlers locais têm responsáveis de execução diferentes.

As fábricas `PerplexityHostedTools.WebSearch`, `FetchUrl`, `Sandbox`, `FinanceSearch`, `PeopleSearch`, `Mcp` e `Connector` criam ferramentas. MCP executa sem pausa para aprovação; restrinja `allowedTools` quando necessário. Connector é uma prévia do provedor e referencia uma integração já conectada.

```csharp
service.WithPerplexityOptions(new PerplexityAgentOptions
{
    Preset = PerplexityPreset.Low,
    MaxSteps = 8,
    Tools = new List<PerplexityHostedTool>
    {
        PerplexityHostedTools.WebSearch(),
        PerplexityHostedTools.FetchUrl()
    }
});
string comparison = await service.GetCompletionAsync(
    "Leia a documentação do projeto e compare os recursos pertinentes.");
```

O modelo determina a compatibilidade de ferramentas, raciocínio, imagens e schemas. `WithFileSearch` não é um adaptador de armazenamento vetorial Perplexity. Arquivos do sandbox, anexos enviados e dados MCP são recursos distintos e não viram automaticamente um repositório comum de pesquisa de arquivos.

## Fontes, imagens e respostas estruturadas

Use completion ou streaming tipado para obter campos JSON. O adaptador envia um schema nativo e mantém o fluxo de reparo. Itens de resposta e IDs de ferramentas são preservados para continuações; evite excluir ou reordenar o histórico do protocolo. Imagens usam `Message` e `ImageContent` com bytes JPEG/PNG/WebP/GIF ou URL HTTPS, conforme o modelo. São entradas, não solicitações de geração de imagens.

Os registros originais ficam nos metadados do histórico, mas as requisições seguintes reenviam apenas as entradas permitidas `message`, `function_call` e `function_call_output`; use `PreviousResponseId` para continuar o estado hospedado completo no provedor.

Citações podem identificar páginas ou outras fontes do provedor. Posições pertencem a cada resposta e parte do conteúdo, não ao resultado Run concatenado. Guarde URL e título para apresentação e verificação; uma fonte retornada não valida sozinha todas as afirmações geradas.

## Manter uma tarefa longa em execução

Use a execução em segundo plano do provedor para continuar pesquisas após desconexões temporárias ou consultá-las depois por ID. Um `AIRun` local controla a execução cliente atual; a resposta em segundo plano tem ciclo próprio no servidor. Parar a leitura encerra apenas a observação. Cancele explicitamente o trabalho remoto para interrompê-lo.

```csharp
var researcher = new PerplexityService(apiKey, httpClient)
    .UsePreset(PerplexityPreset.High);
var job = await researcher.StartBackgroundAsync(
    "Compare os métodos recentes de reciclagem de baterias e cite as fontes.", cancellationToken: cancellationToken);
Console.WriteLine(job.Id);
var completed = await job.WaitForCompletionAsync(
    cancellationToken: cancellationToken);
if (completed.Status != "completed")
    throw new InvalidOperationException(completed.Error ?? completed.Status);
Console.WriteLine(completed.Text);
```

`StartBackgroundAsync` captura a entrada sem acrescentar histórico e rejeita funções locais ativas ou `Store = false`. `GetResponseAsync` consulta uma vez; `WaitForCompletionAsync` consulta até um estado terminal. Guarde `Id` e `LastSequenceNumber`; reconecte com `ResumeBackgroundRun(id).StreamAsync(startingAfter: cursor)`. `CancelAsync` cancela o trabalho remoto; cancelar um token de leitura/consulta encerra apenas essa operação cliente. `LastResponse` contém texto, estado, uso, citações e `OutputJson`. Confira o estado terminal antes de usar a resposta.

Use `ListFilesAsync` e `DownloadFileAsync(fileId)` para arquivos do sandbox. O serviço também oferece `GetAgentResponseAsync`, `GetResponseFilesAsync` e `GetResponseFileContentAsync`. Leem artefatos da resposta, sem criar ou pesquisar armazenamento vetorial.

Para os skills internos de Office, use o caminho em segundo plano deste guia: `StartBackgroundAsync`, depois `WaitForCompletionAsync` / `GetResponseAsync` e os métodos de arquivos. Os registros de ferramentas internas dessas respostas podem não se distinguir de chamadas comuns a funções locais.

Enviar, consultar, cancelar ou reconectar um fluxo em segundo plano não ativa `SteerAsync` durante a resposta nem ferramentas cliente assíncronas nativas. A reconexão observa a resposta existente sem enviar novamente a tarefa. Guarde o ID e o cursor do provedor.

## Pesquisar sem gerar uma resposta

`PerplexitySearchClient` obtém páginas para sua classificação, interface ou outro LLM. Não chama um modelo de resposta nem modifica o histórico de `PerplexityService`.

```csharp
var search = new PerplexitySearchClient(apiKey, httpClient);
var pages = await search.SearchAsync(
    "métodos de reciclagem de baterias",
    new PerplexitySearchOptions
    {
        MaxResults = 5,
        DomainFilter = new[] { "nature.com", "science.org" },
        ContentSize = PerplexitySearchContentSize.Medium
    }, cancellationToken);
foreach (var page in pages.Results)
    Console.WriteLine($"{page.Rank}: {page.Title} — {page.Url}");
```

`SearchAsync` aceita uma ou várias consultas. As opções incluem Web/People, país, domínios, idiomas, datas de publicação/atualização e recência. Escolha `ContentSize` ou `MaxTokens` / `MaxTokensPerPage` explícitos, não ambos. Resultados contêm posição, título, URL, trecho e datas do provedor. A posição é a ordem retornada, não uma pontuação de relevância.

`ContentSize` é aceito apenas na pesquisa Web. Omita-o em People; o cliente rejeita essa combinação antes do envio.

## Usar vetores em seu próprio índice

Embeddings padrão tratam trechos de forma independente e implementam `IEmbeddingProvider`, encaixando-se no construtor RAG. Embeddings contextuais mantêm a ordem dos trechos e os grupos de documentos. Sua API separada evita achatar documentos sem relação em uma entrada única.

| Constante do modelo | ID do provedor | Dimensões padrão |
| --- | --- | --- |
| `Standard0_6B` | `pplx-embed-v1-0.6b` | 1024 |
| `Standard4B` | `pplx-embed-v1-4b` | 2560 |
| `Context0_6B` | `pplx-embed-context-v1-0.6b` | 1024 |
| `Context4B` | `pplx-embed-context-v1-4b` | 2560 |

```csharp
using Mythosia.AI.Rag;
using Mythosia.AI.Rag.Embeddings;

var rag = service.WithRag(builder => builder
    .AddText("Devoluções são aceitas em até 30 dias.", id: "returns")
    .UsePerplexityEmbedding(apiKey, httpClient));
string policy = await rag.GetCompletionAsync("Até quando posso devolver uma compra?");
```

```csharp
var contextual = new PerplexityContextualizedEmbeddingProvider(
    apiKey, httpClient, PerplexityEmbeddingModels.Context0_6B);
var documents = new[]
{
    new[] { "Devoluções são aceitas em até 30 dias.", "Guarde o recibo para solicitar reembolso." },
    new[] { "A entrega padrão leva três dias.", "A entrega expressa está disponível em dias úteis." }
};
var documentVectors = await contextual.GetDocumentEmbeddingsAsync(
    documents, cancellationToken);
float[] queryVector = await contextual.GetQueryEmbeddingAsync(
    "Até quando posso devolver uma compra?", cancellationToken);
```

Use o mesmo modelo, dimensões e codificação para documentos e consultas. `GetQueryEmbeddingAsync` envia uma consulta como documento individual ao mesmo modelo contextual. Os resultados mantêm a ordem de documentos e trechos, sem ligação automática ao construtor RAG de entrada plana.

APIs float decodificam vetores base64 signed-int8 e os normalizam para similaridade. APIs binárias explícitas retornam bits compactados e usam distância de Hamming; nunca tratam bits implicitamente como coordenadas float. As dimensões completas são 1024 para 0.6B e 2560 para 4B; dimensões reduzidas seguem os limites do provedor. Limites de lotes, tamanho, tokens totais e taxa da conta continuam aplicáveis.

Métodos binários: `GetBinaryEmbeddingAsync` / `GetBinaryEmbeddingsAsync`, e contextuais `GetBinaryDocumentEmbeddingsAsync` / `GetBinaryQueryEmbeddingAsync`. `PerplexityBinaryEmbedding` fornece `Dimensions`, cópia por `ToArray()` e `HammingDistance`; menor distância significa maior similaridade. Dimensões binárias são múltiplos de oito. Máximo de 512 textos padrão ou 512 documentos e 16.000 trechos contextuais. O provedor verifica 32K tokens por texto/documento e 120K no total.

## Migrar o código Sonar existente

Esta versão remove deliberadamente o adaptador antigo antes do encerramento anunciado para 27 de setembro de 2026. `PerplexityService` chama `/v1/agent`; `AIModels.Perplexity.Sonar` agora significa `perplexity/sonar`. Helpers de pesquisa e respostas específicos do Sonar foram removidos. Use completion/Run/citações comuns, presets Agent e `PerplexitySearchClient` para pesquisa independente.

Mapeamento recomendado: Sonar → `Fast`, Sonar Pro → `Low`, Sonar Reasoning Pro → `Medium`, Sonar Deep Research → `High`. Não há garantia de textos, custos ou comportamentos idênticos. Presets dinâmicos podem mudar; use modelo explícito ou perfil versionado quando precisar fixar essa escolha.

O adaptador não oferece steering nativo, ferramentas cliente assíncronas nativas nem `CachePreservation.Required`. APIs Router/Gateway estão fora desta integração. A disponibilidade depende do provedor, modelo e conta; este guia não afirma que toda combinação passou em testes reais pagos.

Perfis, skills personalizados e conectores exigem recursos previamente cadastrados na conta. Seus formatos de requisição têm testes unitários; chamadas reais bem-sucedidas com esses recursos não foram verificadas.

[Agent API](https://docs.perplexity.ai/docs/agent-api/quickstart) · [Search API](https://docs.perplexity.ai/docs/search/quickstart) · [Embeddings](https://docs.perplexity.ai/docs/embeddings/quickstart) · [Migration](https://docs.perplexity.ai/docs/agent-api/migrate-from-sonar/how-to) · [2026-09-27](https://community.perplexity.ai/t/sonar-is-moving-to-the-agent-api/5802)
