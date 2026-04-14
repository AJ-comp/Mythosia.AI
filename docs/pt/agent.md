# Agent (Loop ReAct)

## Por que um Loop de Agent?

Com a chamada de funções comum, o modelo faz **uma** chamada de função por requisição. Mas muitas tarefas do mundo real exigem **múltiplas etapas** que o modelo precisa planejar e executar autonomamente:

- "Pesquise as 3 principais empresas de IA e compare os preços das ações" — requer múltiplas buscas
- "Encontre a política relevante, verifique o status do pedido e diga se tenho direito ao reembolso" — requer encadeamento lógico de ferramentas
- O modelo pode precisar **tentar novamente ou refinar** uma busca se o primeiro resultado for insuficiente

O **loop de agent** (padrão ReAct: Raciocinar → Agir → Observar → Repetir) cuida disso automaticamente.

## Uso Básico

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
service.FunctionCallingPolicy = new FunctionCallingPolicy
{
    MaxRounds = 10,
    TimeoutSeconds = 30
};

// Ou via métodos de extensão:
service.WithMaxRounds(15).WithTimeout(60);
```

Políticas predefinidas:

```csharp
service.WithFastPolicy();    // Baixo timeout, menos rounds — tarefas rápidas
service.WithComplexPolicy(); // Maior timeout, mais rounds — pesquisa aprofundada
```

## Como Funciona

Cada etapa:

1. O LLM recebe o objetivo + histórico de conversa + definições de funções
2. Se o LLM chama uma função → execute-a, adicione o resultado ao histórico
3. Se o LLM retorna uma resposta de texto → o loop termina, retorna essa resposta
4. Se a contagem de etapas atinge `maxSteps` → lança `AgentMaxStepsExceededException`
