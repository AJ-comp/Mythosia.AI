# Manter independentes as configurações de cada solicitação

> Claude Sonnet 5.5: Requer Mythosia.AI 8.2.0 / Abstractions 4.2.0. [Configuração e migração](providers.md#claude-sonnet-55)

> GPT-6.1 Sol: Requer Mythosia.AI 8.2.0 / Abstractions 4.2.0. [Seleção e migração](providers.md#gpt-61-sol)

> Grok 4.7: Requer Mythosia.AI 8.1.0 / Abstractions 4.1.0. [seleção do modelo, raciocínio e velocidade](providers.md#grok-47)

Um resumo pode precisar de temperatura baixa e um rascunho criativo de um valor mais alto. Preparar o rascunho não deve alterar um resumo já preparado. Use `CreateRequest` para configurar cada chamada ou criar variações de uma solicitação base.

Para receber resposta, uso e fontes juntos, `await run.Result` retorna um `AIRunResult` com o estado final. A string fica em `result.Text`, sem ler o fluxo. É uma mudança de Mythosia.AI 8.0.0; `GetCompletionAsync` e `StructuredStreamRun<T>.Result` mantêm seus tipos de retorno. [Resultado Run e migração](execution-api-transition.md#run-result).

Para uma resposta final com botão Parar, passe `cancellationToken` a `GetCompletionAsync`. Use Run para eventos de progresso ou instruções adicionais suportadas. Consulte [cancelamento](completions.md#completion-cancellation).

> Os exemplos com `CreateRequest` exigem Mythosia.AI 8.0.0 / Abstractions 4.0.0. A versão 7.1 que introduziu Run e as opções comuns não inclui o builder. Pacotes anteriores podem usar as sobrecargas do serviço.

## Before: o serviço é compartilhado

O `WithTemperature` existente do serviço altera o serviço e retorna a mesma instância. As duas variáveis abaixo a compartilham; o último valor se aplica às duas. Esses métodos continuam disponíveis para configurar os padrões do serviço.

```csharp
var summary = service.WithTemperature(0.2f);
var creative = service.WithTemperature(0.8f);

bool same = ReferenceEquals(summary, creative); // true
string answer = await summary.GetCompletionAsync("Explique este documento."); // 0.8
```

## After: variações independentes

`CreateRequest` captura os valores padrão. Cada `With...` do builder retorna um novo builder sem alterar o original. A execução usa os valores capturados sem sobrescrever temporariamente as configurações do serviço.

```csharp
using Mythosia.AI.Builders;

AIRequestBuilder basis = service.CreateRequest("Explique este documento.");
AIRequestBuilder summary = basis.WithTemperature(0.2f);
AIRequestBuilder creative = basis.WithTemperature(0.8f);

bool same = ReferenceEquals(summary, creative); // false
string answer = await summary.GetCompletionAsync();
// Usa 0.2; creative e os padrões do serviço permanecem iguais.
```

Use o builder retornado. Descartar o resultado de `basis.WithTemperature(0.2f);` deixa `basis` inalterado.

O builder valida sem ajustar silenciosamente: temperatura 0–2, TopP 0–1 e penalidades −2–2; rejeita NaN e infinito. Limites de tokens, rodadas, concorrência e timeout especificado devem ser positivos. Valores inválidos lançam `ArgumentException` / `ArgumentOutOfRangeException`. O helper antigo de temperatura continua limitando valores ao intervalo.

## Responsabilidade dos objetos

`AIService` mantém a conexão com o provedor, os padrões e a conversa. O tipo público `Mythosia.AI.Builders.AIRequestBuilder` oferece a API fluent. O tipo interno `AIRequest` leva a entrada e as configurações definidas à execução. Não é preciso chamar `Build()`: `GetCompletionAsync()` retorna `Task<string>` e `StartRunAsync()` retorna `Task<AIRun>`, e não um `AIRequest` como resposta.

```text
AIService.CreateRequest(input)
    → AIRequestBuilder.With...()
    → GetCompletionAsync() / StartRunAsync()
    → AIRequest
    → provider
    → string / AIRun
```

## Iniciar um Run com a mesma configuração

Use `GetCompletionAsync()` para receber a resposta completa ou `StartRunAsync()` para exibir progresso ou enviar instruções durante uma execução compatível. O prompt vai para `CreateRequest`, não novamente para o método de execução. `run.StreamAsync()` observa esse Run; as condições de suporte a `run.SteerAsync(...)` permanecem iguais.

```csharp
await using var run = await service
    .CreateRequest("Explique este documento.")
    .WithMaxTokens(2048)
    .WithMaxRounds(10)
    .StartRunAsync(
        onText: text => Console.Write(text),
        cancellationToken: cancellationToken);

string answer = (await run.Result).Text;
```

Ferramentas locais podem retornar objetos por `Task<T>` / `ValueTask<T>` e receber um `CancellationToken` injetado. `run.Cancel()` ou o token inicial alcança ferramentas cooperativas; parar apenas o leitor não. Exceções são falhas. O cancelamento ignora chamadas pendentes, e a limpeza aguarda ferramentas iniciadas que ignoram o token. Veja [resultados, erros e cancelamento](function-calling.md#tool-execution-contract).

[Run](execution-api-transition.md) · [Streaming](streaming.md)

## Reutilizar perfis e contexto

`WithProfile` copia um `AIRequestProfile` e `WithContext` copia um `AIRequestContext`. Alterar os objetos originais depois não afeta a solicitação preparada. O builder configura amostragem, instruções do sistema, modo sem estado, política de funções e raciocínio e buscas compatíveis. As validações do provedor continuam valendo.

`WithFunctions(params FunctionDefinition[])` adiciona definições copiadas. `WithFunctions(toolInstance)` e `WithStaticFunctions<T>()` de `Mythosia.AI.Extensions` aceitam funções existentes com atributos. Registre antes de `CreateRequest` para o serviço, depois para a solicitação. `CreateRequest` captura e consome as opções antigas pendentes para a próxima chamada; reutilize o builder para mantê-las.

```csharp
var request = service
    .CreateRequest("Reformule esta pergunta para pesquisa.")
    .WithProfile(RequestProfiles.QueryRewrite)
    .WithContext(new AIRequestContext
    {
        SystemMessageSuffix = "\nMantenha o significado original."
    });

string rewritten = await request.GetCompletionAsync();
```

Claude resolve em conjunto o modelo, a finalidade da solicitação, o raciocínio e a vinculação de thinking. Em perfis auxiliares como `RequestProfiles.Summarization` ou `RequestProfiles.QueryRewrite`, a vinculação herdada só é omitida quando `DisableReasoning = true`, a finalidade não é `Default` e a solicitação efetiva não mantém estado. Assim, a política herdada não reativa o raciocínio nem rejeita uma solicitação sem prefixo de conversa a preservar. Modelos que permitem desativar thinking o desativam; Opus 5.5, Fable 5.1 e Mythos 5.1, com raciocínio obrigatório, usam `Low` e omitem thinking legível. Sonnet 5.5 usa `between_tools` com esforço alto.

Completion, streaming, saída estruturada e Run seguem a mesma preparação: capturam as configurações, aplicam uma vez o processamento real do perfil e validam as opções comuns e nativas resultantes. Isso ocorre antes do resumo automático, da inclusão da entrada no histórico ou da abertura do transporte. As personalizações de perfil do provedor participam das configurações realmente validadas. O Claude também rejeita nessa etapa um `ThinkingBudget` manual utilizado que atinja ou ultrapasse o limite de saída do modelo; perfis válidos e configurações comuns de raciocínio mantêm sua prioridade.

As chamadas da aplicação iniciam solicitações lógicas independentes, incluindo chamadas comuns de `SystemMessageProvider` ou callbacks de ferramentas e chamadas que reutilizam o mesmo `AIRequestProfile` ou `Message`. Reutilizar um objeto não compartilha a execução. A delegação do framework, as rodadas de ferramentas, as novas tentativas e as correções de formato continuam a solicitação original, cujo perfil é aplicado uma vez. Uma solicitação filha comum captura suas opções e os padrões do serviço; os builders mantêm as configurações capturadas. A execução principal é restaurada após sucesso, falha ou cancelamento. Os métodos sobrescritos dos provedores que encaminham uma chamada do framework seguem as [regras de adaptadores abaixo](#provider-request-adapters).

Os provedores integrados mantêm uma cópia própria do conteúdo de entrada integrado. Reutilizar um `Message` aplica o contexto e as instruções da nova chamada sem reescrever o histórico aceito. O proprietário continua responsável pelo conteúdo personalizado e pelos objetos de metadados não compatíveis. Isso não torna seguras as chamadas simultâneas à mesma conversa.

Após o retorno de `StartRunAsync`, o Run mantém sua própria cópia das configurações efetivas. Restaurar o perfil do chamador não altera o Run ativo, e os hooks de execução do perfil continuam sendo chamados apenas uma vez.

Solicitações auxiliares sem estado usam sua própria conversa e não herdam o esquema de saída, ferramentas hospedadas ou opções de uso único da solicitação principal. O isolamento nunca ignora a validação nativa, inclusive em Runs do OpenAI e Perplexity. As configurações, mensagens, `CurrentSummary` e observações da solicitação principal são preservadas. Solicitações com estado mantêm as verificações de vinculação e conversa. As APIs públicas existentes permanecem inalteradas.

Os resumos de conversa gerados internamente também excluem o callback `SystemMessageProvider` e o contexto da solicitação principal, impedindo que um `RequestMessageOverride` herdado substitua o prompt de resumo. As solicitações iniciadas pela aplicação continuam resolvendo seu contexto dinâmico normalmente, inclusive pedidos explícitos de resumo de texto.

Solicitações sem estado também ignoram o resumo automático da conversa pai, inclusive na sobrecarga existente `GetCompletionAsync(string, profile)`, assim como na sobrecarga `Message` e no construtor de solicitações. `CurrentSummary` e as mensagens da conversa pai permanecem inalterados. Solicitações com estado mantêm o resumo automático habitual.

[AIRequestProfile](request-profiles.md) · [AIRequestContext](request-contexts.md) · [WithReasoning / WithWebSearch / WithFileSearch](reasoning-and-search.md)

<a id="provider-request-adapters"></a>

## Encaminhar solicitações em um provedor personalizado

Quando o framework invoca um adaptador virtual do provedor, a primeira chamada dele ao ponto de entrada correspondente da classe base continua a solicitação preparada, mesmo que o método sobrescrito substitua o `Message` de entrada. As opções capturadas pelo builder e os perfis aplicados são preservados nesse encaminhamento. O adaptador padrão de streaming com callback também continua a mesma solicitação preparada.

Um adaptador pode encaminhar outro `AIRequestProfile`: valores iguais não são reaplicados; alterações substituem a camada anterior sobre as configurações capturadas. A nova validação ocorre antes do resumo automático e do envio, inclusive ao mudar para execução sem estado. Solicitações auxiliares OpenAI sem estado ignoram o histórico alheio sem apagar a proteção da conversa principal.

Substituir um perfil encaminhado preserva adições, remoções e edições posteriores de ferramentas, mudanças de política e atribuições explícitas locais da solicitação, mesmo ao atribuir o mesmo valor escalar. Alterar outro campo do perfil não restaura uma ferramenta removida pelo adaptador. Os padrões do serviço não são capturados novamente.

Para objetos opacos de configuração personalizados, os adaptadores devem substituir o valor com `SetExecutionSetting`, em vez de alterar campos internos. A biblioteca não inspeciona objetos arbitrários da aplicação nem chama seus serializadores para acompanhar mudanças de perfil.

A compactação Claude preserva dependências chamada/resultado em `RequestMessageOverride` e `AdditionalMessages`, inclusive ferramentas do servidor, e o prefixo thinking vinculado do Mythos 5.1. Registros antigos de ferramentas paralelas são agrupados uma única vez no histórico e nas mensagens adicionais, preservando a titularidade de cada registro.

Uma chamada auxiliar independente a esse mesmo ponto de entrada da classe base antes do encaminhamento é ambígua: o framework não consegue determinar se ela é a continuação. Envolva essa chamada auxiliar e seu `await` no método protegido `BeginIndependentRequestScope()`; no streaming, mantenha o escopo aberto durante toda a enumeração. A chamada auxiliar começa com os padrões do serviço, e a liberação do escopo restaura as configurações, os recursos, o contexto e a delegação pendente da solicitação externa. Chamadas aninhadas comuns de callbacks de contexto ou de ferramentas já são independentes e não precisam desse escopo.

Por exemplo, uma subclasse de um provedor concreto pode reformular o texto antes de encaminhá-lo:

```csharp
public override async Task<string> GetCompletionAsync(
    Message message, AIRequestProfile? profile = null,
    AIRequestContext? context = null, CancellationToken cancellationToken = default)
{
    string rewritten;
    using (BeginIndependentRequestScope())
    {
        rewritten = await base.GetCompletionAsync(
            new Message(ActorRole.User, message.Content),
            RequestProfiles.QueryRewrite,
            cancellationToken: cancellationToken);
    }

    var replacement = new Message(message.Role, rewritten);
    return await base.GetCompletionAsync(replacement, profile, context, cancellationToken);
}
```

Esse escopo separa o estado de execução das solicitações; ele não isola o histórico da conversa nem permite o uso simultâneo do serviço. O exemplo usa o perfil sem estado `QueryRewrite` para manter a chamada auxiliar fora da conversa principal.

A compactação do Claude verifica o histórico canônico preservado no formato de transmissão, incluindo o thinking assinado introduzido por `AIRequestContext.AdditionalMessages`. A vinculação de thinking padrão protege esse prefixo contra a compactação por resumo automática ou explícita. Definir explicitamente `ClaudeThinkingPrefixMismatchBehavior.DropBlock` permite a compactação quando compatível; as demais restrições da conversa continuam válidas.

## O que é copiado e o que continua compartilhado

Os padrões comuns e do provedor são capturados em `CreateRequest`. Alterações posteriores não afetam a solicitação preparada. Conteúdos integrados de mensagens, coleções de opções compatíveis, perfis, contextos e políticas são copiados. Handlers, callbacks de contexto dinâmico e conteúdos personalizados preservam suas referências. Não altere conteúdos personalizados; delegates podem ler estado externo. O contexto dinâmico é avaliado na execução.

Após a captura, você pode descartar o `JsonDocument` original ou alterar os valores `JsonNode` originais sem mudar o JSON armazenado nos metadados da solicitação ou nos argumentos das chamadas de função; cada execução recebe sua própria cópia. Uma cadeia `Items` cíclica ou com mais de 64 níveis no esquema de uma ferramenta gera `ArgumentException` durante a captura (`CreateRequest` ou `WithFunctions`), para que um esquema inválido falhe antes da execução sem esgotar a pilha do processo.

A cópia também preserva as dimensões e os índices iniciais dos arrays, além das regras de comparação de chaves dos contêineres padrão `Dictionary<,>`, `SortedDictionary<,>` e `SortedList<,>`. Uma busca de chave que ignora maiúsculas e minúsculas continua funcionando assim na solicitação. O valor vazio `default(JsonElement)` (`Undefined`) é preservado. Objetos de metadados personalizados desconhecidos mantêm suas referências; seu proprietário deve evitar alterações ou coordenar o acesso.

Valores padrão `ReadOnlyCollection<T>` e `ReadOnlyDictionary<TKey, TValue>` mantêm seu tipo dentro de arrays e dicionários tipados. As coleções subjacentes suportadas são copiadas preservando as visões somente leitura, as referências compartilhadas e os ciclos. `Hashtable` e `SortedList` não genérico também mantêm suas regras de comparação de chaves.

Um builder não é outra conversa. Ele usa a conversa ativa do serviço na execução; criá-lo não congela o histórico. Chamadas com estado continuam atualizando o histórico compartilhado. `WithStatelessMode()` evita ler e acumular esse histórico. O limite de um Run ativo por serviço permanece. Configurações independentes não garantem execução paralela no mesmo serviço; use serviços separados para conversas simultâneas independentes.

## Chamadas existentes e extensões

`GetCompletionAsync` e as entradas existentes continuam disponíveis. `BeginMessage()` / `MessageChain` preservam a construção mutável de mensagens e executam pelo novo caminho de solicitações. Use `CreateRequest` para reutilizar variações. A API pertence a `AIService` e suas implementações; não há novos membros obrigatórios em `IAIService`. Quem usa somente a abstração ou um wrapper RAG mantém as APIs de perfil, contexto e execução existentes.

[Criar opções de modelo com definições compartilhadas](model-capabilities.md).

<a id="inference-speed"></a>

## Escolher a velocidade de processamento conforme a tarefa

Uma resposta aguardada na tela pode justificar processamento pago de baixa latência; um relatório em segundo plano pode usar o normal. `WithSpeed` escolhe o modo mantendo modelo e esforço de raciocínio. Requer Mythosia.AI 8.1.0 / Abstractions 4.1.0.

`ProviderDefault` não sobrescreve configurações e preserva as do serviço/provedor; o projeto já pode ter Fast como padrão. `Standard` solicita processamento normal explicitamente. `Fast` solicita o modo premium de baixa latência e pode gerar custos adicionais. Guarde o builder retornado: os três ramos são independentes e a base não muda.

```csharp
using Mythosia.AI.Models;

var basis = service.CreateRequest("Explain this report.");
var providerDefault = basis.WithSpeed(InferenceSpeed.ProviderDefault);
var standard = basis.WithSpeed(InferenceSpeed.Standard);
var fast = basis.WithSpeed(InferenceSpeed.Fast);
```

Verifique `GetSpeedSupport(InferenceSpeed.Fast)` antes de oferecer a opção. `StandardSpeed` e `FastSpeed` também distinguem Supported, Unsupported e Unknown. Supported local não confirma permissão, capacidade ou latência. Pedidos explícitos Standard/Fast sem suporte ou desconhecidos falham sem alterar silenciosamente modelo ou esforço. `ProviderDefault` mantém o caminho existente.

```csharp
using Mythosia.AI.Models;
using Mythosia.AI.Models.Capabilities;

var request = service.CreateRequest("Explain this report.")
    .WithSpeed(InferenceSpeed.Fast);
if (request.GetCapabilities().GetSpeedSupport(InferenceSpeed.Fast)
    != CapabilitySupport.Supported)
    throw new NotSupportedException("Fast processing is not supported here.");

await using var run = await request.StartRunAsync();
var result = await run.Result;
Console.WriteLine(result.Text);
foreach (AIProcessingInfo processing in result.Processing)
{
    Console.WriteLine($"{processing.RequestIndex}: {processing.RequestedSpeed} -> " +
        $"{processing.AppliedSpeed?.ToString() ?? "unknown"}; " +
        $"raw={processing.RawAppliedMode}; response={processing.ResponseId}; " +
        $"downgraded={processing.IsDowngraded}");
}
```

`AIRunResult.Processing` preserva `AIProcessingInfo` imutáveis mesmo sem ler o fluxo. `RequestIndex` começa em 1 e identifica tentativas de inferência do provedor, incluindo continuações do servidor, não a contagem de rodadas de ferramentas nem de requisições HTTP; chamadas posteriores, retentativas e reparos de formato podem acrescentar registros. `AppliedSpeed` é null quando nenhum modo reconhecido é informado, inclusive em tentativas com falha. `RawAppliedMode` e `ResponseId` preservam valores recebidos. `IsDowngraded` só é true quando Fast foi solicitado e Standard foi explicitamente informado; false não confirma Fast.

Após uma completion comum, leia imediatamente `AIService.LastProcessing`; o próximo pedido lógico substitui essa visão. Registros capturados continuam imutáveis. A extensão do serviço configura o próximo pedido lógico e suas rodadas de ferramentas, sem criar um padrão permanente. Resumos auxiliares, reescritas internas e perfis internos não herdam a velocidade nem misturam suas observações às do pedido principal.

```csharp
using Mythosia.AI.Extensions;
using Mythosia.AI.Models;

string answer = await service
    .WithSpeed(InferenceSpeed.Standard)
    .GetCompletionAsync("Explain this report.");
var processing = service.LastProcessing;
```

Esses valores descrevem o modo do provedor, não tokens por segundo medidos. OpenAI, xAI e Google podem reduzir o nível no servidor; Mythosia não repete automaticamente em outra velocidade. Anthropic fast mode exige acesso à Claude API direta; mudar a velocidade pode invalidar o cache de prompt. Gemini Developer API priority exige Tier 2/3. Verifique modelo, API, acesso e preços separadamente. A opção não configura imagens, embeddings nem APIs Batch nativas. [OpenAI](https://developers.openai.com/api/docs/guides/fast-mode) · [Anthropic](https://platform.claude.com/docs/en/build-with-claude/fast-mode) · [xAI](https://docs.x.ai/developers/advanced-api-usage/priority-processing) · [Gemini](https://ai.google.dev/gemini-api/docs/generate-content/priority-inference)

Com uma referência `IAIService`, use `GetLastProcessing()` de `Mythosia.AI.Extensions`. Ele lê a interface opcional `IAIProcessingInfoService` e retorna uma lista vazia sem diagnóstico disponível. `IAIService` não ganha membros obrigatórios. No RAG, `RagEnabledService.WithSpeed(...)` configura a próxima resposta após a busca; `LastProcessing` descreve essa resposta. Reescrita interna fica separada e o Run expõe os mesmos registros `Processing`.

A lista Fast implementada aparece abaixo. Verifique Standard separadamente com `GetSpeedSupport(InferenceSpeed.Standard)`. Modelos não listados, endpoints de terceiros e provedores compatíveis com OpenAI não herdam automaticamente modos pagos.

| API | Fast |
| --- | --- |
| Anthropic — `api.anthropic.com` | `claude-opus-4-8`, `claude-opus-5`, `claude-opus-5-5` |
| OpenAI — `api.openai.com` | `gpt-6.1-sol`, `gpt-6-astra`, `gpt-6-sol`, `gpt-6-luna`, `gpt-5.6`, `gpt-5.6-sol`, `gpt-5.6-terra`, `gpt-5.6-luna`, `gpt-5.3-codex` |
| xAI — `api.x.ai` | `grok-4.7`, `grok-4.6`, `grok-4.5`, `grok-4.5-latest`, `grok-build-latest`, `grok-4.3`, `grok-4.3-latest`, `grok-latest`, `grok-4.20-0309-reasoning`, `grok-4.20-0309-non-reasoning`, `grok-build-0.1` |
| xAI — `us.api.x.ai` | `grok-4.7`, `grok-4.6` |
| Google — Gemini Developer API | `gemini-2.5-pro`, `gemini-2.5-flash`, `gemini-2.5-flash-lite`, `gemini-3-flash-preview`, `gemini-3.1-pro-preview`, `gemini-3.1-flash-lite`, `gemini-3.5-flash`, `gemini-3.5-flash-lite`, `gemini-3.6-flash`, `gemini-3.7-flash`, `gemini-3.8-flash` |

| API | `Standard` | `Fast` | `RawAppliedMode` |
| --- | --- | --- | --- |
| Anthropic — Opus 4.8 / 5 / 5.5 | `speed: "standard"` + `fast-mode-2026-02-01` | `speed: "fast"` + `fast-mode-2026-02-01` | `usage.speed` |
| Anthropic — outros modelos Claude conhecidos, incluindo Sonnet 5 | `speed` e beta fast-mode omitidos | `Unsupported` | `usage.speed` |
| OpenAI | `service_tier: "default"` | `service_tier: "fast"` | `service_tier` (`fast` / `priority` → Fast) |
| xAI | `service_tier: "default"` | `service_tier: "priority"` | `service_tier` |
| Google | `serviceTier: "standard"` | `serviceTier: "priority"` | `x-gemini-service-tier` / `usageMetadata.serviceTier` |

Nesses outros modelos Claude, Standard usa a requisição normal existente. Sem metadados de processamento informados, `AppliedSpeed` permanece null; a biblioteca não deduz Standard a partir do pedido.
