# Completions Básicas

## Turno Único

O uso mais simples — envie uma mensagem, receba uma resposta:

```csharp
var response = await service.GetCompletionAsync("Qual é a capital do Brasil?");
Console.WriteLine(response); // Brasília
```

## Prompt do Sistema

Defina um prompt do sistema para dar ao modelo uma persona ou instruções:

```csharp
service.SystemPrompt = "Você é um assistente conciso. Responda em uma frase.";

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
service.ClearMessages();
```

## Construindo Mensagens Manualmente

Use `MessageBuilder` para construir mensagens explicitamente:

```csharp
using Mythosia.AI.Builders;

var message = MessageBuilder.User("Resuma este texto: ...")
    .Build();

var response = await service.GetCompletionAsync(message);
```

## Multimodal (Entrada de Imagem)

Provedores que suportam visão aceitam conteúdo de imagem junto com texto:

```csharp
var imageBytes = await File.ReadAllBytesAsync("diagrama.png");

var message = MessageBuilder.User("O que este diagrama mostra?")
    .WithImage(imageBytes, "image/png")
    .Build();

var response = await service.GetCompletionAsync(message);
```

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
    model: AIModels.OpenAI.Gpt4Vision
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
