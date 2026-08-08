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
