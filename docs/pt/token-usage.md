# Uso de tokens

O uso de tokens mostra quanto uma chamada ao modelo consumiu em entrada, saída, cache e raciocínio. No Mythosia.AI, essas informações chegam em `TokenUsage` nos eventos de streaming.

Isso fica especialmente importante quando a resposta não termina em uma única chamada ao LLM. Uma resposta simples costuma ter um round. Já um agente ou um fluxo com function calling pode chamar o modelo, executar uma ferramenta e depois chamar o modelo de novo com o resultado. Por isso, há dois números diferentes para observar.

- `RoundUsage` mostra o uso de um único round do LLM.
- `Completion.Usage` mostra o uso acumulado de todo o stream.

## Por que isso é importante

Para um medidor de contexto numa UI de chat, o valor mais útil costuma ser o último `RoundUsage.Usage.TotalTokens`. Ele é o mais próximo de "qual seria o tamanho da próxima entrada do modelo se a conversa continuasse agora".

Para logs, diagnóstico e análise de custo, use `Completion.Usage.TotalTokens`. Esse valor permanece acumulado para todo o run, inclusive quando function calling ou agentes geram vários rounds.

Para ajuste de performance, os campos de cache e raciocínio ajudam a entender se o provider reutilizou entrada em cache ou gastou tokens adicionais em raciocínio interno.

## Modelo de eventos

| Evento | Significado | Melhor uso |
|---|---|---|
| `StreamingContentType.RoundUsage` | Uso do round do LLM que acabou de terminar | Medidor de contexto, depuração por round |
| `StreamingContentType.Completion` | Evento final com uso acumulado | Logs, diagnóstico, relatórios de custo |

`RoundUsage.Usage` não é acumulado. Se o round 1 usa 10.100 tokens e o round 2 usa 14.000, o `Completion.Usage.TotalTokens` final pode ser 24.100, enquanto o último `RoundUsage.Usage.TotalTokens` continua sendo 14.000.

| Propriedade | Significado |
|---|---|
| `RoundIndex` | Número do round do LLM, começando em 1 |
| `IsFinalRound` | `true` quando este é o último round do stream |

O uso de tokens é emitido quando o provider retorna dados de usage. Você não precisa ativar `IncludeMetadata = true` para receber eventos de usage.

## Uso acumulado final

Use `Completion.Usage` quando quiser o total da requisição em streaming.

```csharp
await foreach (var chunk in service.StreamAsync("Explique computação quântica", StreamOptions.FullOptions))
{
    if (chunk.Type == StreamingContentType.Text)
        Console.Write(chunk.Content);

    if (chunk.Type == StreamingContentType.Completion && chunk.Usage is not null)
    {
        Console.WriteLine($"Input:  {chunk.Usage.InputTokens}");
        Console.WriteLine($"Output: {chunk.Usage.OutputTokens}");
        Console.WriteLine($"Total:  {chunk.Usage.TotalTokens}");
    }
}
```

Em um único round do LLM, esse valor costuma ficar perto do `RoundUsage`. Em um agente, ele soma todos os rounds.

## Medidor de tokens na UI

Para um medidor de tamanho de contexto, use o `RoundUsage` mais recente.

```csharp
await foreach (var chunk in service.StreamAsync(message, StreamOptions.FullOptions))
{
    if (chunk.Type == StreamingContentType.RoundUsage && chunk.Usage is not null)
    {
        UpdateContextTokenMeter(chunk.Usage.TotalTokens);

        if (chunk.IsFinalRound)
            MarkTokenMeterAsFinal();

        continue;
    }

    if (chunk.Type == StreamingContentType.Text)
        AppendToChat(chunk.Content);
}
```

O último round do modelo enxerga o estado mais recente da conversa, inclusive resultados de ferramentas adicionados durante o run. Por isso, o último `RoundUsage.TotalTokens` é o melhor valor para uma UI de chat.

## Function Calling e agentes

Em fluxos com function calling, o modelo pode rodar várias vezes. Leia cada `RoundUsage`, mantenha o último para a UI e use `Completion.Usage` no fim para o total acumulado.

```csharp
TokenUsage? latestRound = null;
TokenUsage? cumulative = null;

await foreach (var chunk in service.StreamAsync(message, StreamOptions.WithFunctions))
{
    if (chunk.Type == StreamingContentType.RoundUsage && chunk.Usage is not null)
    {
        latestRound = chunk.Usage;
        Console.WriteLine($"Round {chunk.RoundIndex}: {latestRound.TotalTokens} tokens");
        continue;
    }

    if (chunk.Type == StreamingContentType.Completion)
        cumulative = chunk.Usage;
}

Console.WriteLine($"UI meter: {latestRound?.TotalTokens}");
Console.WriteLine($"Run total: {cumulative?.TotalTokens}");
```

## Cache e raciocínio

Quando o provider fornece esses dados, `TokenUsage` também traz campos de cache e raciocínio.

| Propriedade | Significado |
|---|---|
| `InputTokens` | Tokens no prompt ou entrada |
| `OutputTokens` | Tokens gerados pelo modelo |
| `TotalTokens` | Entrada + saída no escopo do evento |
| `CachedInputTokens` | Tokens de entrada servidos a partir do cache |
| `CacheCreationTokens` | Tokens gravados no cache |
| `ReasoningTokens` | Tokens usados em raciocínio interno oculto |
| `VisibleOutputTokens` | Tokens de saída sem contar raciocínio |

## Observações por provider

Cada provider anexa dados de usage a chunks diferentes do stream. O Mythosia.AI normaliza isso em eventos `RoundUsage` e `Completion.Usage`.

Gemini é o caso mais delicado: usage pode vir em chunks de texto ou status e, às vezes, chegar depois de um chunk de function call. A biblioteca continua lendo o stream pelo tempo necessário para capturar esse usage antes de passar ao próximo round.

Como consumidor, prefira usar os eventos normalizados `RoundUsage` e `Completion.Usage` em vez de fazer parsing direto de metadata específica de cada provider.
