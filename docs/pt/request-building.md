# Manter independentes as configurações de cada solicitação

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

[AIRequestProfile](request-profiles.md) · [AIRequestContext](request-contexts.md) · [WithReasoning / WithWebSearch / WithFileSearch](reasoning-and-search.md)

## O que é copiado e o que continua compartilhado

Os padrões comuns e do provedor são capturados em `CreateRequest`. Alterações posteriores não afetam a solicitação preparada. Conteúdos integrados de mensagens, coleções de opções compatíveis, perfis, contextos e políticas são copiados. Handlers, callbacks de contexto dinâmico e conteúdos personalizados preservam suas referências. Não altere conteúdos personalizados; delegates podem ler estado externo. O contexto dinâmico é avaliado na execução.

Após a captura, você pode descartar o `JsonDocument` original ou alterar os valores `JsonNode` originais sem mudar o JSON armazenado nos metadados da solicitação ou nos argumentos das chamadas de função; cada execução recebe sua própria cópia. Uma cadeia `Items` cíclica ou com mais de 64 níveis no esquema de uma ferramenta gera `ArgumentException` durante a captura (`CreateRequest` ou `WithFunctions`), para que um esquema inválido falhe antes da execução sem esgotar a pilha do processo.

A cópia também preserva as dimensões e os índices iniciais dos arrays, além das regras de comparação de chaves dos contêineres padrão `Dictionary<,>`, `SortedDictionary<,>` e `SortedList<,>`. Uma busca de chave que ignora maiúsculas e minúsculas continua funcionando assim na solicitação. O valor vazio `default(JsonElement)` (`Undefined`) é preservado. Objetos de metadados personalizados desconhecidos mantêm suas referências; seu proprietário deve evitar alterações ou coordenar o acesso.

Valores padrão `ReadOnlyCollection<T>` e `ReadOnlyDictionary<TKey, TValue>` mantêm seu tipo dentro de arrays e dicionários tipados. As coleções subjacentes suportadas são copiadas preservando as visões somente leitura, as referências compartilhadas e os ciclos. `Hashtable` e `SortedList` não genérico também mantêm suas regras de comparação de chaves.

Um builder não é outra conversa. Ele usa a conversa ativa do serviço na execução; criá-lo não congela o histórico. Chamadas com estado continuam atualizando o histórico compartilhado. `WithStatelessMode()` evita ler e acumular esse histórico. O limite de um Run ativo por serviço permanece. Configurações independentes não garantem execução paralela no mesmo serviço; use serviços separados para conversas simultâneas independentes.

## Chamadas existentes e extensões

`GetCompletionAsync` e as entradas existentes continuam disponíveis. `BeginMessage()` / `MessageChain` preservam a construção mutável de mensagens e executam pelo novo caminho de solicitações. Use `CreateRequest` para reutilizar variações. A API pertence a `AIService` e suas implementações; não há novos membros obrigatórios em `IAIService`. Quem usa somente a abstração ou um wrapper RAG mantém as APIs de perfil, contexto e execução existentes.

[Criar opções de modelo com definições compartilhadas](model-capabilities.md).
