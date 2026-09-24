# Chamada de Funções

> GPT-6 Sol/Luna ainda não foram publicados. Veja [seleção do modelo e requisitos](providers.md#gpt-6-sol-luna).

Para uma resposta final com botão Parar, passe `cancellationToken` a `GetCompletionAsync`. Use Run para eventos de progresso ou instruções adicionais suportadas. Consulte [cancelamento](completions.md#completion-cancellation).

Para configurações independentes e variações reutilizáveis, use o [builder de solicitações](request-building.md). Chame `CreateRequest(...)` antes de `With...`. Propriedades e métodos fluent do serviço mantêm o comportamento existente.

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

[Claude Fable 5.1](fable-5-1.md) adiciona atualizações de progresso, instruções por turno e diagnósticos de vinculação do raciocínio a partir de `Mythosia.AI` 8.0.0 / `Mythosia.AI.Abstractions` 4.0.0. Mythos 5.1 exige convite. Ambos rejeitam a seleção forçada de ferramentas.

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

<a id="tool-execution-contract"></a>

## Retornar objetos de ferramentas assíncronas e cancelar o trabalho

Uma ferramenta de arquivos ou banco de dados costuma retornar um objeto após E/S assíncrona. O botão Parar também deve alcançar a operação que ainda está em execução. Funções síncronas já podiam retornar objetos; esta atualização torna os retornos assíncronos consistentes e registra exceções como falhas.

Before: uma ferramenta assíncrona precisava serializar seu próprio resultado. Retornar `Task<FileResult>` perdia o valor e entregava apenas `"Success"`.

```csharp
using System.IO;
using System.Text.Json;
using System.Threading.Tasks;
using Mythosia.AI.Attributes;

public sealed class FileToolsBefore
{
    [AiFunction("read_file", "Ler um arquivo de texto")]
    public async Task<string> ReadFileAsync(string path)
    {
        string text = await File.ReadAllTextAsync(path);
        return JsonSerializer.Serialize(new { Path = path, Text = text });
    }
}
```

After: retorne o objeto diretamente e passe o token de cancelamento injetado para a operação de E/S. A aplicação não precisa implementar novos wrappers de resultado ou adaptadores.

```csharp
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Mythosia.AI.Attributes;

public sealed record FileResult(string Path, string Text);

public sealed class FileTools
{
    [AiFunction("read_file", "Ler um arquivo de texto")]
    public async Task<FileResult> ReadFileAsync(
        string path, CancellationToken cancellationToken)
    {
        string text = await File.ReadAllTextAsync(path, cancellationToken);
        return new FileResult(path, text);
    }
}
```

O registro com `[AiFunction]` aceita objetos, `Task<T>` e `ValueTask<T>`; valores que não são strings são convertidos em JSON. `string`, `Task<string>` e `ValueTask<string>` permanecem texto puro, sem aspas JSON adicionais. `Task` e `ValueTask` sem resultado também são aguardados. Os retornos síncronos de objetos continuam disponíveis. Um retorno null vira `"Done"`; `Task` / `ValueTask` concluídos sem resultado viram `"Success"`.

A biblioteca também reconhece valores assíncronos em tempo de execução: aguarda um `Task<T>` retornado como `Task` ou `object`, ou um `ValueTask<T>` retornado como `object`, e serializa o resultado pelas mesmas regras. Cada `ValueTask` é consumido uma única vez.

A biblioteca fornece o parâmetro `CancellationToken` e o exclui do esquema de argumentos apresentado ao modelo. Registre os métodos com `WithFunctions(...)` ou `WithStaticFunctions<T>()` no serviço ou no construtor de requisições.

Métodos de ferramenta `async void` são rejeitados no registro. Retorne `Task` ou `ValueTask` para permitir aguardar a conclusão, observar erros e concluir a limpeza após cancelamento.

```csharp
using Mythosia.AI.Extensions;

await using var run = await service
    .CreateRequest("Leia report.txt e faça um resumo.")
    .WithFunctions(new FileTools())
    .StartRunAsync(cancellationToken: cancellationToken);

string answer = (await run.Result).Text;
```

`run.Cancel()`, cancelar o token passado para `StartRunAsync` ou liberar um run ativo alcança ferramentas locais que aceitam cancelamento. A função precisa usar o token; código que o ignora não pode ser encerrado à força. Chamadas ainda não iniciadas são ignoradas e recebem resultados de cancelamento; funções já iniciadas são aguardadas para preservar os pares chamada/resultado no histórico. O run cancelado não inicia outra rodada do modelo.

Se um callback de cancelamento lançar uma exceção durante uma falha ao iniciar o run ou ao liberar uma conexão MCP, a limpeza da sessão ou do transporte ainda será tentada. O erro original e os erros de limpeza são preservados, juntos em uma `AggregateException` quando necessário. Chamadas assíncronas simultâneas a `McpConnection.DisposeAsync()` aguardam a mesma limpeza. O transporte é fechado antes de aguardar o fim do laço de leitura, permitindo concluir leituras que dependem do fechamento da conexão.

Para evitar que uma chamada tardia de ferramenta fique esperando durante o encerramento, assim que a liberação da conexão começa, novas operações `InitializeAsync`, `RefreshToolsAsync` e `CallToolAsync` são rejeitadas com `ObjectDisposedException`. Uma resposta com o ID esperado, mas com corpo malformado, é ignorada sem remover a solicitação pendente: uma resposta válida posterior, o cancelamento pelo chamador ou a limpeza da conexão ainda podem encerrar a chamada. Se a leitura já terminou porque o servidor fechou o fluxo ou houve uma falha de leitura no transporte, novas operações falham com `McpException` em vez de esperar uma resposta que não pode chegar; crie uma nova conexão para continuar.

Falhas reais devem lançar exceções. O executor registra `FunctionCallResult.IsError = true`, em vez de tratar uma string `"Error: ..."` como sucesso. Uma string retornada intencionalmente continua sendo um resultado normal. Resultados cancelados recebem `IsCancelled = true` e `IsError = true`.

Para registrar por código, use a sobrecarga de `WithHandler` com dois argumentos:

```csharp
using System.IO;
using Mythosia.AI.Builders;

var readText = FunctionBuilder.Create("read_text")
    .WithDescription("Ler um arquivo de texto")
    .AddParameter("path", "string", "Caminho do arquivo", required: true)
    .WithHandler(async (args, token) =>
        await File.ReadAllTextAsync(args["path"].ToString()!, token))
    .Build();
```

Os handlers existentes de um argumento que retornam strings continuam suportados. Uma definição direta pode atribuir `Func<Dictionary<string, object>, CancellationToken, Task<string>>` a `HandlerWithCancellation`. Definir `Handler` ou `HandlerWithCancellation` substitui o mesmo handler, sem registrar duas execuções. Essa API de baixo nível continua retornando strings; a serialização automática de objetos pertence ao registro de métodos.

Isso trata retornos e cancelamento de funções .NET locais, sem exigir o `AllowAsync` nativo do provedor. Parar apenas o leitor `run.StreamAsync(token)` interrompe a observação, não o run. Veja o [guia de Run](execution-api-transition.md) e o [protocolo do provedor](https://developers.openai.com/api/docs/guides/async-tool-calling).

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

Mythosia envia `async: true` para GPT-6 Astra / Sol / Luna pela API Responses. Para modelos e APIs sem suporte, omite o campo e aguarda o resultado do mesmo handler, sem alterar `AllowAsync`. O provedor também precisa marcar a chamada real como assíncrona (`FunctionCall.IsAsync`); ativar a permissão não garante execução assíncrona.

`WithFunctionAsync` registra um handler assíncrono de .NET, e `FunctionExecutionMode.Parallel` controla a execução local dos handlers. Nenhum deles ativa automaticamente essa permissão. `AllowAsync` permite que o modelo continue antes de receber o resultado da função. `FunctionExecutionMode` continua controlando as chamadas comuns. Os trabalhos assíncronos habilitados podem se sobrepor mesmo no modo `Sequential` e compartilham um limite separado de trabalhos definido por `MaxConcurrency`.

Os trabalhos pendentes pertencem à requisição ativa: `GetCompletionAsync`, o antigo `service.StreamAsync` ou um `AIRun` iniciado com `StartRunAsync`. Cada resultado é associado depois ao ID original da chamada. A conclusão bem-sucedida da requisição ou de `run.Result` aguarda o processamento dos resultados pendentes. Não existe uma sessão pública de trabalhos em segundo plano independente da requisição.

Com ferramentas assíncronas, `GetCompletionAsync` retorna ao término da solicitação o texto intermediário independente e o texto final acumulados na ordem. `StreamAsync` entrega o texto de cada rodada conforme ele chega. `run.StreamAsync()` também transmite o texto conforme ele chega; `(await run.Result).Text` concatena todos os eventos de texto do run.

Ferramentas locais podem retornar objetos por `Task<T>` / `ValueTask<T>` e receber um `CancellationToken` injetado. `run.Cancel()` ou o token inicial alcança ferramentas cooperativas; parar apenas o leitor não. Exceções são falhas. O cancelamento ignora chamadas pendentes, e a limpeza aguarda ferramentas iniciadas que ignoram o token. Veja [resultados, erros e cancelamento](function-calling.md#tool-execution-contract).

No streaming, os handlers começam após a confirmação de chamadas de função completas e de um limite válido da resposta do provedor. Depois, a próxima rodada do modelo pode avançar enquanto os trabalhos assíncronos são executados; chamadas incompletas não iniciam a execução. Se o modelo não retornar novas chamadas e houver trabalhos pendentes, Mythosia aguarda seus resultados. Novas tentativas com resumo automático por excesso de contexto permanecem desativadas durante chamadas pendentes para preservar as chamadas inacabadas no histórico.

Perplexity: [Controlar a pesquisa e as ferramentas](perplexity.md).
