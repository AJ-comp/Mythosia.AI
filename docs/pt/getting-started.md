# Primeiros Passos

## Instalação

Instale o pacote principal:

```bash
dotnet add package Mythosia.AI
```

Se você planeja usar streaming com operadores LINQ (ex.: `ToListAsync`), adicione também:

```bash
dotnet add package System.Linq.Async
```

## Sua Primeira Completion

Escolha um provedor e crie uma instância de serviço com sua chave de API e um `HttpClient`:

```csharp
using Mythosia.AI;

var http = new HttpClient();

// OpenAI
var service = new OpenAIService("your-openai-api-key", http);

// Anthropic
// var service = new AnthropicService("your-anthropic-api-key", http);

// Google
// var service = new GoogleAIService("your-google-api-key", http);
```

Em seguida, chame `GetCompletionAsync`:

```csharp
var response = await service.GetCompletionAsync("Olá!");
Console.WriteLine(response);
```

## Escolhendo um Modelo

Cada serviço usa um modelo padrão sensato, mas você pode especificar um explicitamente:

```csharp
var service = new OpenAIService("your-api-key", http)
{
    Model = AIModels.OpenAI.Gpt4_1
};
```

Consulte a [Referência de API](../../api/Mythosia.AI.Models.AIModels.yml) para todas as constantes de modelos disponíveis.

## Próximos Passos

- [Completions Básicas](completions.md) — prompts do sistema, histórico de conversas, multimodal
- [Streaming](streaming.md) — saída token a token e streaming de reasoning
- [Chamada de Funções](function-calling.md) — deixe o modelo chamar seu código
- [Saída Estruturada](structured-output.md) — desserialize respostas em tipos C#
