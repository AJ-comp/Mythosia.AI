# Streaming

## Streaming Básico

Use `StreamAsync` para receber tokens à medida que são gerados:

```csharp
await foreach (var token in service.StreamAsync("Conte-me uma história"))
{
    Console.Write(token);
}
```

## Streaming com Tipo de Conteúdo

`StreamAsync` pode retornar objetos `StreamingContent` que carregam tanto o texto quanto seu tipo:

```csharp
await foreach (var content in service.StreamAsync("Explique computação quântica"))
{
    Console.Write(content.Content);
}
```

## Streaming com Reasoning

Todos os provedores com capacidade de reasoning (OpenAI, Claude, Gemini, Grok, DeepSeek) compartilham o mesmo padrão. Passe `StreamOptions` com reasoning habilitado:

```csharp
using Mythosia.AI.Models.Streaming;

await foreach (var content in service.StreamAsync("Resolva: 2x + 5 = 13", new StreamOptions().WithReasoning()))
{
    if (content.Type == StreamingContentType.Reasoning)
        Console.Write($"[Pensando] {content.Content}");
    else if (content.Type == StreamingContentType.Text)
        Console.Write(content.Content);
}
```

## Streaming com Saída Estruturada

Transmita texto em tempo real e obtenha um objeto desserializado ao final:

```csharp
var run = service.BeginStream(prompt).As<MyDto>();

// Transmite tokens para a UI conforme chegam
await foreach (var chunk in run.Stream())
    Console.Write(chunk);

// Obtém o resultado completamente analisado após o streaming
MyDto result = await run.Result;
```

## Uso de Tokens

Ao concluir o streaming, o evento `Completion` final carrega um objeto `TokenUsage` com métricas detalhadas:

```csharp
await foreach (var content in service.StreamAsync("Explique computação quântica"))
{
    if (content.Type == StreamingContentType.Text)
        Console.Write(content.Content);

    if (content.Type == StreamingContentType.Completion && content.Usage != null)
    {
        Console.WriteLine($"\nTokens de entrada:  {content.Usage.InputTokens}");
        Console.WriteLine($"Tokens de saída: {content.Usage.OutputTokens}");
        Console.WriteLine($"Total de tokens:  {content.Usage.TotalTokens}");
    }
}
```

### Propriedades de TokenUsage

| Propriedade | Descrição |
|---|---|
| `InputTokens` | Tokens no input/prompt |
| `OutputTokens` | Tokens no output/completion |
| `TotalTokens` | Input + Output |
| `CachedInputTokens` | Tokens servidos do cache (custo reduzido) |
| `CacheCreationTokens` | Tokens gravados no cache (Anthropic) |
| `ReasoningTokens` | Tokens usados para reasoning interno |
| `CacheHitRatio` | Taxa de acerto do cache (0.0–1.0) |
| `VisibleOutputTokens` | Tokens de saída excluindo reasoning |

## Predefinições de StreamOptions

```csharp
// Completo — metadados, chamadas de função, reasoning
await foreach (var c in service.StreamAsync("prompt", StreamOptions.FullOptions))
    Console.Write(c.Content);

// Mínimo — somente texto, sem metadados
await foreach (var c in service.StreamAsync("prompt", StreamOptions.Minimal))
    Console.Write(c.Content);
```

Construtor fluente para combinações personalizadas:

```csharp
var options = new StreamOptions()
    .WithReasoning()       // inclui chain-of-thought
    .WithMetadata()        // inclui informações do modelo no Completion
    .WithFunctionCalls();  // habilita chamada de funções durante o stream
```

## Streaming Sem Estado (StreamOnceAsync)

Transmita uma resposta sem afetar o histórico de conversa:

```csharp
await foreach (var chunk in service.StreamOnceAsync("Traduza para o português"))
    Console.Write(chunk);
```

## Resumo de Conversa Antes do Streaming

A política de resumo automático não é acionada durante o streaming. Chame `ApplySummaryPolicyIfNeededAsync` explicitamente antes de `StreamAsync`:

```csharp
await service.ApplySummaryPolicyIfNeededAsync();

await foreach (var chunk in service.StreamAsync("Continue nossa conversa..."))
    Console.Write(chunk.Content);
```
