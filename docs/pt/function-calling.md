# Chamada de Funções

## Por que Usar Chamada de Funções?

LLMs só conseguem gerar texto — eles não conseguem verificar o tempo, consultar um banco de dados ou chamar uma API por conta própria. **Sem** chamada de funções, você teria que analisar a intenção do modelo manualmente:

```csharp
// ❌ Sem chamada de funções — análise manual de intenção
var reply = await service.GetCompletionAsync("Como está o tempo em São Paulo?");
// reply = "Precisaria verificar um serviço meteorológico."

// Você tem que descobrir que o usuário quer o tempo, extrair "São Paulo", chamar a API...
if (reply.Contains("tempo"))
{
    var city = ExtractCity(reply); // regex frágil
    var weather = await weatherApi.GetAsync(city);
}
```

**Com** chamada de funções, o modelo decide **quando** chamar seu código e **quais argumentos** passar:

```csharp
// ✅ Com chamada de funções — o modelo gerencia intenção + extração
var service = new OpenAIService(apiKey, http)
    .WithFunction(
        "get_weather",
        "Obtém o tempo atual para um local",
        ("location", "A cidade e o país", required: true),
        (string location) => weatherApi.Get(location)
    );

var response = await service.GetCompletionAsync("Como está o tempo em São Paulo?");
```

## Exemplo Rápido

```csharp
var service = new OpenAIService(apiKey, http)
    .WithFunction(
        "get_weather",
        "Obtém o tempo atual para um local",
        ("location", "A cidade e o país", required: true),
        (string location) => $"O tempo em {location} está ensolarado, 25°C"
    );

var response = await service.GetCompletionAsync("Como está o tempo em São Paulo?");
```

## Definindo Funções com Atributos

Para funções mais complexas, use os atributos `[AiFunction]` e `[AiParameter]`:

```csharp
using Mythosia.AI.Attributes;
using Mythosia.AI.Extensions;

public sealed class ProductFunctions
{
    [AiFunction("search_products", "Pesquisa o catálogo de produtos")]
    public string SearchProducts(
        [AiParameter("Consulta de pesquisa", required: true)] string query,
        [AiParameter("Número máximo de resultados")] int limit = 5)
    {
        // ... sua implementação
        return JsonSerializer.Serialize(results);
    }
}
```

Em seguida, registre-a:

```csharp
service.WithFunctions(new ProductFunctions());
```

## Política de Chamada de Funções

Controle quando o modelo pode chamar funções:

```csharp
using Mythosia.AI.Models.Functions;

// Deixe o modelo decidir (padrão)
service.FunctionCallMode = FunctionCallMode.Auto;

// Force o modelo a sempre chamar uma função
service.ForceFunctionName = "search_products";

// Desative a chamada de funções
service.FunctionCallMode = FunctionCallMode.None;
```

## Registro em Massa a partir de uma Classe

Registre todos os métodos anotados com `[AiFunction]` de um objeto de uma vez:

```csharp
var tools = new MyTools();
service.WithFunctions(tools);  // varre métodos de instância com [AiFunction]
```

Para métodos estáticos:

```csharp
service.WithStaticFunctions<MyTools>();
```

## Handlers de Função Assíncronos

Todos os overloads de `WithFunction` têm contrapartes `WithFunctionAsync` que aceitam `Func<..., Task<string>>`:

```csharp
service.WithFunctionAsync<string>(
    "fetch_data",
    "Busca dados de uma API externa",
    ("url", "A URL para buscar", required: true),
    async (string url) =>
    {
        var result = await httpClient.GetStringAsync(url);
        return result;
    }
);
```

## Desabilitando Funções Temporariamente

Desative a chamada de funções para uma única requisição sem remover os registros:

```csharp
string answer = await service.AskWithoutFunctionsAsync("Responda diretamente");

// Ou alterne a propriedade
service.WithoutFunctions();
```

## Usando FunctionBuilder

Construa definições de funções programaticamente:

```csharp
using Mythosia.AI.Builders;
using Mythosia.AI.Extensions;

var fn = FunctionBuilder
    .Create("get_stock_price")
    .WithDescription("Retorna o preço atual de uma ação")
    .AddParameter("ticker", "string", "Símbolo do ticker da ação", required: true)
    .WithHandler(args => FetchStockPrice(args["ticker"].ToString() ?? string.Empty))
    .Build();

service.WithFunction(fn);
```

## Chamadas assíncronas de ferramentas

Uma consulta lenta não precisa interromper toda a resposta. Enquanto os dados do clima são carregados, por exemplo, o modelo pode explicar dicas gerais de viagem que não dependem do resultado. As chamadas assíncronas de ferramentas permitem esse trabalho independente; afirmações que precisam do resultado ainda devem aguardá-lo.

O suporte ao GPT-6 Astra e às chamadas assíncronas de ferramentas está disponível a partir de `Mythosia.AI` 7.1.0, com tipos compartilhados em `Mythosia.AI.Abstractions` 3.1.0.

`FunctionDefinition.AllowAsync` é `false` por padrão. Defina como `true` ou use `FunctionBuilder.WithAsync()` somente quando o modelo puder continuar trabalhando enquanto essa função é executada. `WithAsync(false)` desativa a permissão. A mesma definição e o mesmo handler podem ser reutilizados entre provedores.

O registro por atributos aceita a mesma permissão: `[AiFunction("lookup", "Consultar dados", AllowAsync = true)]`.

```csharp
using System.Threading.Tasks;
using Mythosia.AI.Builders;
using Mythosia.AI.Extensions;
using Mythosia.AI.Models;

service.ChangeModel(AIModels.OpenAI.Gpt6Astra);
var weatherTool = FunctionBuilder.Create("get_demo_weather")
    .WithDescription("Retorna um exemplo do clima em Seul")
    .WithAsync()
    .WithHandler(async _ =>
    {
        await Task.Delay(5000);
        return "{\"city\":\"Seoul\",\"temperature_c\":24,\"source\":\"demo\"}";
    })
    .Build();
service.WithFunction(weatherTool);
var answer = await service.GetCompletionAsync(
    "Consulte o exemplo do clima em Seul. Enquanto isso, liste três itens essenciais para uma viagem.");
```

Mythosia envia `async: true` para GPT-6 Astra pela API Responses. Para modelos e APIs sem suporte, omite o campo e aguarda o resultado do mesmo handler, sem alterar `AllowAsync`. O provedor também precisa marcar a chamada real como assíncrona (`FunctionCall.IsAsync`); ativar a permissão não garante execução assíncrona.

`WithFunctionAsync` registra um handler assíncrono de .NET, e `FunctionExecutionMode.Parallel` controla a execução local dos handlers. Nenhum deles ativa automaticamente essa permissão. `AllowAsync` permite que o modelo continue antes de receber o resultado da função. `FunctionExecutionMode` continua controlando as chamadas comuns. Os trabalhos assíncronos habilitados podem se sobrepor mesmo no modo `Sequential` e compartilham um limite separado de trabalhos definido por `MaxConcurrency`.

Os trabalhos pendentes pertencem à requisição ativa: `GetCompletionAsync`, o antigo `service.StreamAsync` ou um `AIRun` iniciado com `StartRunAsync`. Cada resultado é associado depois ao ID original da chamada. A conclusão bem-sucedida da requisição ou de `run.Result` aguarda o processamento dos resultados pendentes. Não existe uma sessão pública de trabalhos em segundo plano independente da requisição.

Com ferramentas assíncronas, `GetCompletionAsync` retorna ao término da solicitação o texto intermediário independente e o texto final acumulados na ordem. `StreamAsync` entrega o texto de cada rodada conforme ele chega. `run.StreamAsync()` também transmite o texto conforme ele chega; `run.Result` concatena todos os eventos de texto do run.

Os handlers não recebem tokens de cancelamento. Após cancelamento, timeout ou erro, a limpeza aguarda os handlers já iniciados. Encerrar antecipadamente o fluxo antigo do serviço termina sua execução; encerrar `run.StreamAsync()` interrompe somente a observação. Para cancelar o run, use `run.Cancel()` ou libere-o. A integração abrange os handlers registrados; consulte o [protocolo da API](https://developers.openai.com/api/docs/guides/async-tool-calling) e o [guia de Run](execution-api-transition.md).

No streaming, os handlers começam após a confirmação de chamadas de função completas e de um limite válido da resposta do provedor. Depois, a próxima rodada do modelo pode avançar enquanto os trabalhos assíncronos são executados; chamadas incompletas não iniciam a execução. Se o modelo não retornar novas chamadas e houver trabalhos pendentes, Mythosia aguarda seus resultados. Novas tentativas com resumo automático por excesso de contexto permanecem desativadas durante chamadas pendentes para preservar as chamadas inacabadas no histórico.
