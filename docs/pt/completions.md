# Completions Básicas

Para configurações independentes e variações reutilizáveis, use o [builder de solicitações](request-building.md). Chame `CreateRequest(...)` antes de `With...`. Propriedades e métodos fluent do serviço mantêm o comportamento existente.

Se a aplicação precisa apenas da resposta concluída, `GetCompletionAsync` continua adequado. Para mostrar o progresso, cancelar ou acrescentar instruções durante o trabalho em modelos compatíveis, consulte o [guia de Run](execution-api-transition.md).

<a id="completion-cancellation"></a>

## Cancelar uma resposta que já não é necessária

Quando o utilizador fecha o ecrã, carrega em Parar ou termina o tempo de espera da aplicação, a resposta pode deixar de ser útil. Passe um `CancellationToken` para parar a comunicação e o trabalho do cliente e evitar ferramentas e chamadas posteriores ao modelo. `GetCompletionAsync` continua adequado para a resposta final; só cancelar não exige um Run.

### Before: sem sinal de cancelamento do chamador

```csharp
string answer = await service.CreateRequest("Resume este documento.")
    .GetCompletionAsync();
```

### After: cancelar por ação do utilizador ou após 30 segundos

```csharp
using System;
using System.Threading;

using var cancellation = new CancellationTokenSource(TimeSpan.FromSeconds(30));
try
{
    string answer = await service.CreateRequest("Resume este documento.")
        .GetCompletionAsync(cancellationToken: cancellation.Token);
    Console.WriteLine(answer);
}
catch (OperationCanceledException) when (cancellation.IsCancellationRequested)
{
    Console.WriteLine("Cancelado.");
}
```

Mantenha a fonte durante a chamada e ligue Parar ou o fecho a `cancellation.Cancel()`. O exemplo também agenda cancelamento após 30 segundos. O chamador recebe `OperationCanceledException` após a limpeza. Um prazo com `CancellationTokenSource` também é cancelamento do chamador; `FunctionCallingPolicy.TimeoutSeconds` mantém o comportamento de erro de tempo limite existente.

As sobrecargas do serviço para texto e `Message`, respostas tipadas, o builder e `MessageChain.SendAsync` / `SendOnceAsync` aceitam o token. As chamadas que o omitem continuam válidas. Entradas alternativas:

```csharp
using System.Collections.Generic;
using System.Threading;
using Mythosia.AI.Extensions;

using var cancellation = new CancellationTokenSource();
CancellationToken token = cancellation.Token;

string answer = await service.GetCompletionAsync(
    "Resume este documento.", cancellationToken: token);

Dictionary<string, string> data = await service.GetCompletionAsync<Dictionary<string, string>>(
    "Devolve o título e autor como JSON.", cancellationToken: token);

string messageAnswer = await service.BeginMessage().AddText("Resume este documento.")
    .SendAsync(cancellationToken: token);

string oneOffAnswer = await service.BeginMessage().AddText("Traduz esta frase.")
    .SendOnceAsync(cancellationToken: token);
```

O token chega à preparação, envio e leitura HTTP, ferramentas locais cooperativas e rondas posteriores. Ao detetar o cancelamento, ferramentas pendentes e futuras rondas são ignoradas. A limpeza mantém os pares de chamadas registadas e resultados; uma ferramenta iniciada que ignore o token pode atrasá-la. Ações concluídas e histórico não são desfeitos. Consulte o [contrato de ferramentas](function-calling.md#tool-execution-contract).

Não há garantia de parar a inferência ou faturação do fornecedor. OpenAI documenta terminar a ligação para Responses normais; Google indica cancelamento apenas do cliente e cobrança do uso aplicável. [OpenAI Responses](https://developers.openai.com/api/docs/guides/background#limits) · [Google GenerateContentConfig.abortSignal](https://googleapis.github.io/js-genai/release_docs/interfaces/types.GenerateContentConfig.html#abortsignal). Um trabalho em segundo plano requer `CancelAsync()` explícito; cancelar `WaitForCompletionAsync(cancellationToken: ...)` só termina a espera. A chamada normal não é convertida em execução em segundo plano. Consulte [Perplexity](perplexity.md).

<a id="completion-cancellation-migration"></a>

Esta adição faz parte de Mythosia.AI 8.0.0. Chamadas sem token e argumentos posicionais profile/context mantêm a compatibilidade de código-fonte, mas é necessário recompilar. Implementações próprias de `IAIService` devem acrescentar `CancellationToken cancellationToken = default` ao fim de ambas as assinaturas e propagá-lo. Fornecedores derivados de `AIService` mantêm o override `GetCompletionAsync(Message)` e transmitem o `RequestCancellationToken` protegido ao transporte. O builder e Run por si só não exigiam esta alteração da interface. Subclasses que redefinem sobrecargas public virtual alteradas para respostas string/profile/context, helpers de imagem ou `RunAgentAsync` também devem acrescentar e transmitir o novo `CancellationToken`; apenas o override do fornecedor com um único `Message` mantém a assinatura. Delegados ligados diretamente a assinaturas alteradas podem precisar de uma lambda explícita que transmita ou omita o token.

## Turno Único

O uso mais simples — envie uma mensagem, receba uma resposta:

```csharp
var response = await service.GetCompletionAsync("Qual é a capital do Brasil?");
Console.WriteLine(response); // Brasília
```

## Prompt do Sistema

Defina um prompt do sistema para dar ao modelo uma persona ou instruções:

```csharp
service.SystemMessage = "Você é um assistente conciso. Responda em uma frase.";

var response = await service.GetCompletionAsync("Explique recursão.");
```

## Conversa com Múltiplos Turnos

As mensagens são acumuladas automaticamente. Cada chamada a `GetCompletionAsync` é adicionada ao histórico da conversa:

```csharp
await service.GetCompletionAsync("Meu nome é Carlos.");
var response = await service.GetCompletionAsync("Qual é o meu nome?");
// → "Seu nome é Carlos."
```

Para limpar o histórico da conversa:

```csharp
service.ActivateChat.ClearMessages();
```

## Construindo Mensagens Manualmente

Use `MessageBuilder` para construir mensagens explicitamente:

```csharp
using Mythosia.AI.Builders;

var message = MessageBuilder.Create().AddText("Resuma este texto: ...")
    .Build();

var response = await service.GetCompletionAsync(message);
```

## Multimodal (Entrada de Imagem)

Provedores que suportam visão aceitam conteúdo de imagem junto com texto:

```csharp
var imageBytes = await File.ReadAllBytesAsync("diagrama.png");

var message = MessageBuilder.Create().AddText("O que este diagrama mostra?")
    .AddImage(imageBytes, "image/png")
    .Build();

var response = await service.GetCompletionAsync(message);
```

Para analisar gráficos e capturas, chamar funções locais ou revisar uma resposta rápida, use [DeepSeek Flash](providers.md#deepseek-deepseekservice) (`AIModels.DeepSeek.Flash`, V4.1 Flash). O raciocínio fica desativado por padrão; ative com `WithDeepSeekReasoning(...)` ou `WithReasoning(...)` por solicitação.

## Consulta Rápida (API Estática)

Para consultas únicas sem construir uma instância de serviço, use o `QuickAskAsync` estático. O provedor é detectado automaticamente pelo nome do modelo:

```csharp
string answer = await AIService.QuickAskAsync(
    apiKey: "sk-...",
    prompt: "Qual é a capital do Brasil?",
    model: AIModels.OpenAI.Gpt4oMini
);
```

Variante com imagem:

```csharp
string description = await AIService.QuickAskWithImageAsync(
    apiKey: "sk-...",
    prompt: "Descreva esta imagem",
    imagePath: "foto.jpg",
    model: AIModels.OpenAI.Gpt4_1
);
```

## Métodos de Conveniência para Imagens

Analise imagens sem `MessageBuilder` — o serviço lê o arquivo e resolve o tipo MIME automaticamente:

```csharp
// A partir do caminho do arquivo
var response = await service.GetCompletionWithImageAsync(
    "O que este diagrama mostra?", "diagrama.png");

// A partir de URL
var response = await service.GetCompletionWithImageUrlAsync(
    "Descreva esta foto", "https://example.com/foto.jpg");
```

## Reenviar Última Mensagem

Remove a última resposta do assistente e reenviar a última mensagem do usuário:

```csharp
string regenerated = await service.RetryLastMessageAsync();
```

Útil quando a resposta anterior foi insatisfatória e você quer que o modelo tente novamente.

## Contagem de Tokens

Estime o uso de tokens antes de enviar uma requisição. Disponível em **todos os provedores**:

```csharp
// Contagem de tokens para o histórico atual da conversa
uint conversationTokens = await service.GetInputTokenCountAsync();

// Contagem de tokens para um prompt específico
uint promptTokens = await service.GetInputTokenCountAsync("Seu prompt aqui");
```

## Cadeia de Mensagens Fluente

`BeginMessage()` fornece uma API fluente para construir e enviar mensagens em uma única cadeia:

```csharp
// Texto + imagem → enviar
string response = await service.BeginMessage()
    .AddText("O que este diagrama mostra?")
    .AddImage("diagrama.png")
    .SendAsync();

// Consulta única (sem histórico de conversa)
string answer = await service.BeginMessage()
    .AddText("Traduza para o português")
    .SendOnceAsync();

// Streaming
await service.BeginMessage()
    .AddText("Escreva um poema sobre a primavera")
    .StreamAsync(chunk => Console.Write(chunk));
```

## Controlando Comprimento de Saída e Temperatura

```csharp
service.MaxTokens = 512;
service.Temperature = 0.2f;  // menor = mais determinístico
```

Perplexity: [Responder com um preset Agent / Fontes, imagens e respostas estruturadas](perplexity.md).
