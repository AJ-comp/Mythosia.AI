# Escolher o esforço de raciocínio e responder com fontes

> Grok 4.7 é uma adição ainda não publicada; veja [seleção do modelo, raciocínio e velocidade](providers.md#grok-47).

> GPT-6 Sol/Luna ainda não foram publicados. Veja [seleção do modelo e requisitos](providers.md#gpt-6-sol-luna).

[Claude Opus 5.5](providers.md#claude-opus-55) é uma adição não publicada: raciocínio sempre ativo, esforço medium por padrão e exibição omitida. Solicite progresso legível explicitamente; padrões e regras de vínculo diferem do Fable 5.1.

Para configurações independentes e variações reutilizáveis, use o [builder de solicitações](request-building.md). Chame `CreateRequest(...)` antes de `With...`. Propriedades e métodos fluent do serviço mantêm o comportamento existente.

> Estas APIs exigem `Mythosia.AI` 7.1.0 ou posterior, que inclui `Mythosia.AI.Abstractions` 3.1.0 ou posterior. Os exemplos de RAG exigem `Mythosia.AI.Rag` 7.6.0 ou posterior.

> Os exemplos com `CreateRequest` exigem a versão atual em desenvolvimento. A versão 7.1 que introduziu Run e as opções comuns não inclui o builder. Pacotes anteriores podem usar as sobrecargas do serviço.

[Claude Fable 5.1](fable-5-1.md) adiciona atualizações de progresso, instruções por turno e diagnósticos de vinculação do raciocínio a partir de `Mythosia.AI` 8.0.0 / `Mythosia.AI.Abstractions` 4.0.0. Mythos 5.1 exige convite. Ambos rejeitam a seleção forçada de ferramentas.

Para pedidos sensíveis ao tempo de espera, escolha a [velocidade de processamento](request-building.md#inference-speed). `WithSpeed` mantém modelo e esforço; `Processing` informa o modo aplicado. Fast é pago nas combinações suportadas.

## Por que usar estas opções?

Etapas diferentes precisam de tipos diferentes de ajuda. Um primeiro rascunho pode exigir uma resposta rápida; revisar suas premissas pode justificar mais raciocínio. Uma pergunta sobre os acontecimentos de hoje precisa de informações atuais, enquanto uma pergunta sobre seu produto precisa da documentação que o descreve. Aumentar o raciocínio, por si só, não fornece nenhuma dessas fontes ao modelo.

Use a API Fluent comum para expressar o que a próxima tarefa precisa. O provedor escolhido traduz as opções compatíveis para sua API nativa. Sua aplicação pode manter `GetCompletionAsync` para receber uma resposta completa ou usar `StartRunAsync` para mostrar o progresso e controlar a mesma tarefa.

| A tarefa precisa de | Configuração |
| --- | --- |
| Um rascunho rápido seguido de uma revisão mais cuidadosa | `WithReasoning(...)` |
| Uma mudança de raciocínio que preserve um prefixo de conversa elegível para cache | `WithReasoning(..., cache: CachePreservation.Required)` |
| Informações atuais da Web | `WithWebSearch()` |
| Respostas fundamentadas em documentos já indexados pelo provedor | `WithFileSearch(store)` |

Os exemplos pressupõem um serviço inicializado com um modelo compatível. Importe `Mythosia.AI.Extensions` e `Mythosia.AI.Models`; os eventos de streaming também usam `Mythosia.AI.Models.Streaming`.

## Passar de um rascunho rápido para uma revisão cuidadosa

Você pode dedicar menos raciocínio a um esboço e depois pedir que a mesma conversa examine os detalhes difíceis:

```csharp
string outline = await service
    .CreateRequest("Esboce o plano de migração.")
    .WithReasoning(ReasoningLevel.Low)
    .GetCompletionAsync();

string review = await service
    .CreateRequest("Revise os cenários de falha e as etapas de recuperação desse plano.")
    .WithReasoning(ReasoningLevel.High)
    .GetCompletionAsync();
```

Gemini 3.7/3.8 Flash aceitam `Low`, `Medium` e `High` com `WithReasoning`; `Minimal`, `None` e `CachePreservation.Required` não são suportados. Completions, streaming, Run, ferramentas e busca nativa usam os fluxos existentes, com as restrições de combinação do Google. Veja o [exemplo de configuração do Google](providers.md#google-googleaiservice).

`ReasoningLevel` expressa o nível solicitado, não um orçamento fixo de tokens nem uma garantia de qualidade. Cada modelo aceita seu próprio subconjunto. `Auto` mantém o comportamento configurado ou padrão do provedor; isso não significa que níveis incompatíveis sejam substituídos automaticamente. As propriedades de orçamento específicas do provedor continuam disponíveis para modelos que oferecem orçamentos de tokens em vez de níveis nomeados.

Em uma conversa longa, alterar o esforço no nível superior pode invalidar um prefixo de prompt reutilizável. Em um modelo compatível, exija o mecanismo do provedor que muda o esforço preservando esse prefixo:

```csharp
string review = await service
    .CreateRequest("Verifique novamente as premissas da resposta anterior.")
    .WithReasoning(ReasoningLevel.High, cache: CachePreservation.Required)
    .GetCompletionAsync();
```

`Required` define como a mudança é enviada. Ele **não** garante um acerto de cache, tokens gratuitos ou menor latência: continuam valendo as condições de elegibilidade, retenção e preço do cache do provedor. Modelos incompatíveis lançam `NotSupportedException` antes do envio. Use a mesma conversa acompanhada, o mesmo modelo e endpoint; não trunque nem reordene uma conversa que contenha essas atualizações. Inicie outra conversa se essas condições mudarem. A compactação automática fica bloqueada enquanto a preservação do prefixo for necessária.

O esforço aceito com preservação de cache passa a ser o esforço ativo da conversa até outra mudança explícita. O uso comum de `WithReasoning(level)` vale para sua solicitação lógica e não substitui silenciosamente essa configuração persistente. A mudança ocorre **entre respostas do modelo**. Ela não altera o esforço de uma resposta já em andamento e é independente de `run.SteerAsync`, que envia uma instrução adicional a um Run ativo compatível.

## Responder a perguntas que precisam de informações atuais

Ative a busca nativa na Web quando a resposta precisar usar informações além dos dados de treinamento do modelo:

```csharp
string answer = await service
    .CreateRequest("Pesquise o anúncio da versão mais recente e cite a fonte.")
    .WithWebSearch()
    .GetCompletionAsync();

foreach (AICitation source in service.GetLastCitations())
    Console.WriteLine($"{source.Title}: {source.Url}");
```

O provedor executa essa ferramenta hospedada. Não é necessário registrar ou executar um manipulador de função local. Ativar a busca a disponibiliza ao modelo; ele pode decidir que um prompt específico não precisa dela. As referências às fontes ficam disponíveis quando o provedor as retorna.

OpenAI e Anthropic também aceitam `WithWebSearch(new WebSearchOptions { AllowedDomains = new[] { "example.com" } })`. O Google não disponibiliza essa lista de domínios permitidos pela ferramenta integrada; por isso, uma solicitação restrita é rejeitada em vez de pesquisar toda a Web.

## Responder com documentos já indexados pelo provedor

Se sua aplicação já mantém um índice de documentos hospedado pelo provedor, use esse repositório para fundamentar respostas sem implementar sua própria rodada de recuperação:

```csharp
var documents = new FileSearchStore("OpenAI", "vs_your_existing_store");

string answer = await service
    .CreateRequest("Pesquise nossos documentos de políticas. Qual é o prazo de cancelamento?")
    .WithFileSearch(documents)
    .GetCompletionAsync();

foreach (AICitation source in service.GetLastCitations())
    Console.WriteLine($"{source.Title}: {source.FileId ?? source.Url}");
```

Para o Google, use `new FileSearchStore("Google", "fileSearchStores/your-existing-store")` com um serviço Google. Os repositórios pertencem a seu provedor, conta e implantação; um ID de repositório OpenAI não pode ser enviado ao Google. Crie o repositório e envie ou indexe seus documentos pela API ou pelo console do provedor antes de usá-lo aqui. Esta API só pesquisa repositórios existentes e não envia arquivos locais.

`CreateRequest(...).With...` guarda opções em um builder independente. Reutilizá-lo aplica as opções a cada execução e suas rodadas de ferramentas. Os antigos `service.WithReasoning`, `service.WithWebSearch` e `service.WithFileSearch` continuam retornando o tipo concreto do serviço e consumindo opções na próxima solicitação lógica. Eles permanecem disponíveis para `IAIRequestFeatureService` e wrappers RAG. Nenhuma API garante execuções simultâneas no mesmo serviço.

## Mostrar o progresso e manter as fontes

Use as mesmas opções antes de `StartRunAsync`. O callback de texto pode atualizar a tela enquanto o Run mantém as fontes da resposta concluída:

```csharp
await using var run = await service
    .CreateRequest("Pesquise os anúncios recentes e compare as mudanças.")
    .WithReasoning(ReasoningLevel.High)
    .WithWebSearch()
    .StartRunAsync(
        onText: text => Console.Write(text),
        cancellationToken: cancellationToken);

string answer = (await run.Result).Text;
foreach (AICitation source in run.Citations)
    Console.WriteLine($"{source.Title}: {source.Url ?? source.FileId}");
```

`run.Citations` continua disponível sem consumir o fluxo, ao observar apenas texto ou depois que o buffer de observação fica cheio. Ele contém as referências do provedor coletadas durante o Run, inclusive nas respostas intermediárias. `service.LastCitations`, ou `GetLastCitations()` por meio de `IAIService`, descreve a solicitação lógica mais recente; mantenha o Run ou copie seu snapshot de citações ao exibir várias respostas.

Para receber os eventos de fontes assim que chegam, use um único leitor de eventos:

```csharp
await using var run = await service
    .CreateRequest("Pesquise e explique as mudanças mais recentes.")
    .WithWebSearch()
    .StartRunAsync(cancellationToken: cancellationToken);

await foreach (var item in run.StreamAsync())
{
    if (item.Type == StreamingContentType.Text)
        Console.Write(item.Content);
    else if (item.Type == StreamingContentType.Citation && item.Citation is AICitation source)
        Console.WriteLine($"\nFonte: {source.Title} {source.Url ?? source.FileId}");
}
string answer = (await run.Result).Text;
```

Os campos de citação podem ser `null` quando o provedor não fornece um valor. `ResponseId`, `OutputIndex` e `ContentIndex` identificam a resposta de origem e sua parte de conteúdo. `StartIndex` e `EndIndex` mantêm os deslocamentos locais e a convenção de índices do provedor; eles **não** são posições no `(await run.Result).Text` concatenado. Não posicione citações usando esses valores indiscriminadamente para indexar a resposta completa.

## Verificar o suporte do provedor e o escopo da solicitação

| Provedor integrado | Níveis de raciocínio nomeados | Mudança preservando o cache | Busca na Web | Busca de arquivos |
| --- | --- | --- | --- | --- |
| OpenAI | Modelos de raciocínio compatíveis; níveis variam por modelo | GPT-6 Astra / Sol / Luna Standard, modo de agente único | Modelos Responses compatíveis | Modelos Responses compatíveis e repositórios vetoriais existentes |
| Anthropic | Modelos com controle nativo de esforço | Opus 5 / 5.5 / Fable 5.1 / Mythos 5.1 compatíveis com a versão beta do provedor | Modelos Claude compatíveis | Sem adaptador nativo de repositório; use RAG |
| Google | Níveis do Gemini 3; Gemini 2.5 mantém os orçamentos específicos do provedor | Não suportado | Modelos de texto Gemini compatíveis | Modelos de texto Gemini compatíveis e repositórios de busca de arquivos existentes |
| xAI | Grok 4.7 / 4.6: `Auto`, `Low`, `Medium`, `High`, `XHigh` | Não suportado | Sem adaptador comum | Sem adaptador comum |
| DeepSeek | Flash / V4 Pro: `Auto`, `None`, `Minimal`/`Low`, `Medium`/`High`/`XHigh`, `Max`; equivalências nativas Low/High/Max | Não suportado | Sem adaptador comum | Sem adaptador comum |
| Perplexity | `Auto` ou `Minimal`/`Low`/`Medium`/`High`/`XHigh`/`Max` conforme o modelo; Sonar não aceita esforço explícito | Não suportado | Agent `web_search` | Sem adaptador comum |
| Outros serviços | As configurações específicas do provedor continuam disponíveis; estas opções comuns exigem um adaptador | Não suportado por estes adaptadores | Sem adaptador comum | Sem adaptador comum |

O adaptador verifica antes do envio as restrições conhecidas de modelo, nível, transporte e combinação; o provedor valida as regras específicas que não podem ser verificadas localmente. Em particular, **a busca na Web e a busca de arquivos do Google não podem ser combinadas na mesma solicitação**. A biblioteca não remove recursos silenciosamente, reduz níveis de esforço, ignora restrições de domínio nem muda para um serviço de busca externo. Ferramentas nativas podem coexistir com funções de cliente registradas quando essa combinação é compatível; as rodadas de ferramentas do Run continuam seguindo a política de funções e `WithMaxRounds`.

`CreateRequest(...).With...` guarda opções em um builder independente. Reutilizá-lo aplica as opções a cada execução e suas rodadas de ferramentas. Os antigos `service.WithReasoning`, `service.WithWebSearch` e `service.WithFileSearch` continuam retornando o tipo concreto do serviço e consumindo opções na próxima solicitação lógica. Eles permanecem disponíveis para `IAIRequestFeatureService` e wrappers RAG. Nenhuma API garante execuções simultâneas no mesmo serviço.

Implementações personalizadas de `IAIService` continuam compatíveis. Elas aderem a estes recursos por meio de `IAIRequestFeatureService`; chamar os auxiliares em uma implementação sem essa capacidade gera uma falha explícita. As APIs existentes de completion, streaming e configuração específica do provedor permanecem disponíveis. Consulte [Controle de Run](execution-api-transition.md) para cancelamento, observação e steering.

Protocolos dos provedores: [Mudanças de raciocínio da OpenAI](https://developers.openai.com/api/docs/guides/reasoning#change-reasoning-mid-conversation), [Ferramentas da OpenAI](https://developers.openai.com/api/docs/guides/tools), [Mudanças de esforço da Anthropic](https://platform.claude.com/docs/en/build-with-claude/effort#change-effort-mid-conversation), [Busca na Web da Anthropic](https://platform.claude.com/docs/en/agents-and-tools/tool-use/web-search-tool), [Fundamentação com Google Search](https://ai.google.dev/gemini-api/docs/google-search), [Busca de arquivos do Google](https://ai.google.dev/gemini-api/docs/file-search).

No Perplexity, o modelo selecionado determina os níveis de esforço e o servidor pode rejeitar combinações incompatíveis. `None` não é aceito. A busca Web padrão e as ferramentas dos presets/perfis são configurações persistentes do provedor; as opções comuns não as desativam.

Perplexity: [Perplexity Agent API, pesquisa e embeddings](perplexity.md).
