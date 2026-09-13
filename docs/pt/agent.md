# Agent (Loop ReAct)

Para receber resposta, uso e fontes juntos, `await run.Result` retorna um `AIRunResult` com o estado final. A string fica em `result.Text`, sem ler o fluxo. É uma mudança de Mythosia.AI 8.0.0; `GetCompletionAsync` e `StructuredStreamRun<T>.Result` mantêm seus tipos de retorno. [Resultado Run e migração](execution-api-transition.md#run-result).


Para configurações independentes e variações reutilizáveis, use o [builder de solicitações](request-building.md). Chame `CreateRequest(...)` antes de `With...`. Propriedades e métodos fluent do serviço mantêm o comportamento existente.

> Os exemplos com `CreateRequest` exigem Mythosia.AI 8.0.0 / Abstractions 4.0.0. A versão 7.1 que introduziu Run e as opções comuns não inclui o builder. Pacotes anteriores podem usar as sobrecargas do serviço.

Buscar uma política e conferir um pedido pode exigir várias chamadas de ferramentas. O [guia de Run](execution-api-transition.md) mostra como acompanhar esse trabalho, cancelá-lo e acrescentar instruções quando o modelo oferece suporte.

## Por que um Loop de Agent?

Algumas perguntas exigem várias fontes: o modelo escolhe uma ferramenta, examina seu resultado e pode solicitar outras ferramentas. O ciclo comum entre modelo e ferramentas repete essas etapas até produzir a resposta; um limite de rodadas restringe a execução:

- "Pesquise as 3 principais empresas de IA e compare os preços das ações" — requer múltiplas buscas
- "Encontre a política relevante, verifique o status do pedido e diga se tenho direito ao reembolso" — requer encadeamento lógico de ferramentas
- O modelo pode precisar **tentar novamente ou refinar** uma busca se o primeiro resultado for insuficiente

`GetCompletionAsync` e `StartRunAsync` já executam o ciclo compartilhado entre modelo e ferramentas. Os antigos métodos de agente acrescentam um limite de rodadas por chamada e tradução de erros específica, sem um planejador ou mecanismo de execução independente.

## Iniciar uma tarefa com ferramentas usando Run

```csharp
// Registre as funções no serviço antes de iniciar a tarefa.
await using var run = await service
    .CreateRequest("Encontre a política, confira o pedido e explique o resultado.")
    .WithMaxRounds(10)
    .StartRunAsync(
        onText: text => Console.Write(text),
        cancellationToken: cancellationToken);
string answer = (await run.Result).Text;
```

Ferramentas locais podem retornar objetos por `Task<T>` / `ValueTask<T>` e receber um `CancellationToken` injetado. `run.Cancel()` ou o token inicial alcança ferramentas cooperativas; parar apenas o leitor não. Exceções são falhas. O cancelamento ignora chamadas pendentes, e a limpeza aguarda ferramentas iniciadas que ignoram o token. Veja [resultados, erros e cancelamento](function-calling.md#tool-execution-contract).

## API anterior de agente: exemplos de compatibilidade

Os exemplos a seguir documentam `RunAgentAsync` e `RunAgentStreamAsync`, que continuam disponíveis com avisos `[Obsolete]`. Novas chamadas podem usar `StartRunAsync`; o [guia de Run](execution-api-transition.md) detalha diferenças nos limites de rodadas e no tratamento de erros.

Registre funções e chame `RunAgentAsync` com um objetivo:

```csharp
var service = new OpenAIService(apiKey, http)
    .WithFunction(
        "search_web",
        "Pesquisa informações na web",
        ("query", "Consulta de pesquisa", required: true),
        query => WebSearch(query)
    )
    .WithFunction(
        "get_stock_price",
        "Obtém o preço atual de uma ação",
        ("ticker", "Símbolo do ticker", required: true),
        ticker => FetchPrice(ticker)
    );

string result = await service.RunAgentAsync(
    goal: "Qual é o preço atual das ações das 3 principais empresas de IA?",
    maxSteps: 10
);

Console.WriteLine(result);
```

## maxSteps

`maxSteps` limita o número de rounds LLM→chamada de função. Se o agent não terminar dentro do limite, `AgentMaxStepsExceededException` é lançado:

```csharp
try
{
    string result = await service.RunAgentAsync("Pesquise e resuma...", maxSteps: 5);
}
catch (AgentMaxStepsExceededException ex)
{
    Console.WriteLine($"Interrompido: {ex.PartialResponse}");
}
```

## FunctionCallingPolicy

Controle o comportamento do loop de agent por round:

```csharp
service.DefaultPolicy = new FunctionCallingPolicy
{
    TimeoutSeconds = 30
};

// RunAgentAsync usa DefaultPolicy e o argumento explícito maxSteps.
service.DefaultPolicy.TimeoutSeconds = 60;
var policyResult = await service.RunAgentAsync(
    "Pesquise e resuma...", maxSteps: 15);
```

Políticas predefinidas:

```csharp
service.DefaultPolicy = FunctionCallingPolicy.Fast;    // Baixo timeout, menos rounds — tarefas rápidas
var fastResult = await service.RunAgentAsync(
    "Pesquise e resuma...", maxSteps: service.DefaultPolicy.MaxRounds);
service.DefaultPolicy = FunctionCallingPolicy.Complex; // Maior timeout, mais rounds — pesquisa aprofundada
var complexResult = await service.RunAgentAsync(
    "Pesquise e resuma...", maxSteps: service.DefaultPolicy.MaxRounds);
```

## Contexto de solicitação por chamada

`RunAgentAsync` e `RunAgentStreamAsync` aceitam um `AIRequestContext` opcional para injetar prefix/suffix dinâmicos no system message, documentos de referência, ou substituir a mensagem de objetivo — **limitado a uma única execução do agent**, sem modificar o system message do serviço ou o histórico de conversa.

```csharp
string result = await service.RunAgentAsync(
    goal: "Encontre a política de reembolso e verifique se o pedido #1234 se qualifica.",
    maxSteps: 10,
    context: new AIRequestContext
    {
        SystemMessagePrefix = $"A data de hoje é {DateTime.UtcNow:yyyy-MM-dd}.\n",
        SystemMessageSuffix = "\nSempre cite a seção da política utilizada."
    });
```

A variante streaming aceita o mesmo parâmetro:

```csharp
await foreach (var content in service.RunAgentStreamAsync(
    goal: "Pesquise os preços das ações das 3 principais empresas de IA.",
    maxSteps: 10,
    options: StreamOptions.WithFunctions,
    context: new AIRequestContext
    {
        SystemMessagePrefix = $"Fuso horário do usuário: {userTz}\n"
    }))
{
    // lidar com conteúdo
}
```

`AIRequestContext` é propagado por `AsyncLocal`, mas isso não torna seguras as alterações simultâneas no histórico e nas políticas do serviço. Use instâncias separadas para tarefas concorrentes independentes.

Consulte [AIRequestContext](request-contexts.md) para a lista completa de propriedades disponíveis (`SystemMessagePrefix`, `SystemMessageSuffix`, `AdditionalMessages`, `RequestMessageOverride`).

> Disponível a partir do Mythosia.AI v6.3.0.

## Como Funciona

Cada etapa:

1. O LLM recebe o objetivo + histórico de conversa + definições de funções
2. Se o LLM chama uma função → execute-a, adicione o resultado ao histórico
3. Se o LLM retorna uma resposta de texto → o loop termina, retorna essa resposta
4. Se a contagem de etapas atinge `maxSteps` → lança `AgentMaxStepsExceededException`
