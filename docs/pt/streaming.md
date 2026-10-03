# Streaming

O adaptador de streaming por callback cancela e aguarda o produtor na saída antecipada. Cancelamento do Run, timeout, erro do observador e `DisposeAsync` aguardam a limpeza antes de concluir `Result` e liberar o bloqueio. Trabalho não cooperativo pode atrasar o término; erros do observador e da limpeza são preservados juntos. `ContextRecoveryMaxRetries` usa o valor capturado. Encerrar apenas a observação de `run.StreamAsync()` não cancela o Run. A obtenção do corpo de uma resposta SSE bem-sucedida tem uma [limitação separada de cancelamento](#sse-acquisition-cancellation-limitation).

> Claude Sonnet 5.5: Requer Mythosia.AI 8.2.0 / Abstractions 4.2.0. [Configuração e migração](providers.md#claude-sonnet-55)

Em adaptive, use `ClaudeThinkingDisplay.Updates` para progresso legível das ferramentas ou `Summarized` para resumos do raciocínio. Observe `StreamingContentType.Reasoning`, ou `LastThinkingContent` após uma conclusão. O método adaptive usa `Summarized` quando display é omitido, diferente das configurações intocadas. `between_tools` retorna progresso automaticamente, sem intervalo fixo garantido.

> Grok 4.7: Requer Mythosia.AI 8.1.0 / Abstractions 4.1.0. [seleção do modelo, raciocínio e velocidade](providers.md#grok-47)

Para receber resposta, uso e fontes juntos, `await run.Result` retorna um `AIRunResult` com o estado final. A string fica em `result.Text`, sem ler o fluxo. É uma mudança de Mythosia.AI 8.0.0; `GetCompletionAsync` e `StructuredStreamRun<T>.Result` mantêm seus tipos de retorno. [Resultado Run e migração](execution-api-transition.md#run-result).


Para configurações independentes e variações reutilizáveis, use o [builder de solicitações](request-building.md). Chame `CreateRequest(...)` antes de `With...`. Propriedades e métodos fluent do serviço mantêm o comportamento existente.

Mostrar o texto conforme ele chega permite ler uma resposta longa enquanto ela é escrita. `StartRunAsync` também permite cancelar essa mesma tarefa e enviar instruções em modelos compatíveis; consulte o [guia de Run](execution-api-transition.md).

```csharp
await using var run = await service.StartRunAsync(
    "Resuma o documento.",
    cancellationToken: cancellationToken);

await foreach (var item in run.StreamAsync())
{
    if (item.Type == StreamingContentType.Text)
        Console.Write(item.Content);
}

string answer = (await run.Result).Text;
```

**Respostas de erro do Claude:** O cancelamento e o timeout da política de solicitação também interrompem a leitura bloqueada do corpo de um erro HTTP durante streaming ou Run. Após a limpeza do Run, outro pode ser iniciado no mesmo serviço. O cancelamento pelo chamador gera `OperationCanceledException`; o timeout da política gera `AIServiceException`. O cancelamento controla o transporte local e a limpeza cooperativa; não garante que o processamento ou a cobrança do provedor parem.

**Limpeza das respostas do Claude:** O streaming do Claude e Run aguardam a limpeza assíncrona do corpo HTTP já obtido, incluindo streams personalizados que exigem liberação assíncrona. Uma exceção posterior ao liberar a resposta ou o conteúdo não substitui uma conclusão bem-sucedida, o erro de leitura original ou um cancelamento; a liberação da resposta e do conteúdo originais continua sendo tentada.

**Timeouts HTTP:** No streaming de texto, conteúdo ou callbacks e nos Runs que usam o caminho comum de rodadas de streaming, um `HttpClient.Timeout` identificável (`TaskCanceledException` com uma `TimeoutException` interna) torna-se `AIServiceException` quando nem o cancelamento pelo chamador nem o timeout da política de solicitação foram acionados. `InnerException` preserva a exceção de transporte original, de modo que `run.Result` falha mantendo a causa do timeout. O cancelamento pelo chamador, os timeouts da política e outros cancelamentos de transporte mantêm o comportamento existente.

<a id="sse-acquisition-cancellation-limitation"></a>

## Limitação conhecida: obtenção do corpo de uma resposta SSE bem-sucedida

Em HTTP 200 SSE, um wrapper `HttpContent` que armazena o corpo em buffer, usado por um handler personalizado, pode bloquear `ReadAsStreamAsync` antes da obtenção do fluxo do corpo e da limpeza. O cancelamento pelo chamador e o timeout da política de requisição podem deixar `run.Result` pendente, a resposta sem descarte e o bloqueio de Run ativo retido até a obtenção terminar; outro Run é rejeitado por já haver um ativo. Esse problema permanece sem correção e é distinto de uma limpeza lenta. O `SocketsHttpHandler` padrão passou nos cenários testados; o cancelamento do corpo de erro HTTP também passou com o wrapper. Use conteúdo de streaming comum sem um wrapper que o armazene em buffer. A busca web nativa do Claude tem uma [limitação separada de continuação](providers.md#claude-native-continuation-limitation).

## Exemplos de compatibilidade com a API anterior

Os StreamAsync de serviço/RAG com entrada continuam públicos na v8. Use StartRunAsync para novo controle de execução; run.StreamAsync() apenas observa um run existente.

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
await foreach (var content in service.StreamAsync("Explique computação quântica", StreamOptions.Default))
{
    Console.Write(content.Content);
}
```

## Streaming com Reasoning

OpenAI, Claude, Gemini, Grok e DeepSeek Flash expõem o raciocínio do provedor pelo mesmo padrão de streaming. Ative-o no serviço ou solicitação e observe com `StreamOptions.WithReasoning()`:

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

Gemini 3.7/3.8 Flash usam os eventos existentes de streaming e Run. `StreamingContentType.Reasoning` contém resumos ou avisos de progresso expostos pelo provedor quando retornados, sem garantir acesso a todo o raciocínio interno. `StreamOptions.WithReasoning()` seleciona essa saída; `WithReasoning(ReasoningLevel...)` no serviço controla o esforço.

Grok 4.6 também pode expor resumos opcionais de raciocínio por esses eventos. A opção do fluxo seleciona a saída visível; `WithReasoning(ReasoningLevel...)` define o esforço de uma tarefa. A ausência de resumos não significa que o raciocínio foi desativado. Consulte a [configuração do Grok](providers.md#xai-xaiservice).

DeepSeek Flash expõe `reasoning_content` pelos mesmos eventos após ativar o raciocínio. `StreamOptions.WithReasoning()` controla a observação; `WithDeepSeekReasoning(...)` ou o `WithReasoning(...)` do serviço controla o raciocínio. Consulte [DeepSeek](providers.md#deepseek-deepseekservice).

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
await foreach (var content in service.StreamAsync("Explique computação quântica", StreamOptions.Default))
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

Trate os fragmentos exibidos como provisórios até que `run.Result` termine com sucesso. O caminho compartilhado de streaming compatível com OpenAI e o caminho de streaming do DeepSeek rejeitam novos textos, raciocínios ou dados de ferramentas após uma conclusão explícita, bem como mudanças no motivo de conclusão: `run.Result` lança uma exceção, a rodada com falha não é salva no histórico e suas ferramentas não são executadas. Esse tratamento da falha não desfaz rodadas anteriores nem ações já executadas externamente. O último delta pode chegar no primeiro evento de conclusão; um evento posterior contendo apenas dados de uso também é aceito.

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

await foreach (var chunk in service.StreamAsync("Continue nossa conversa...", StreamOptions.Default))
    Console.Write(chunk.Content);
```

Perplexity: [Manter uma tarefa longa em execução / Citações podem identificar páginas ou outras fontes do provedor. Posições pertencem a cada resposta e parte do conteúdo, não ao resultado Run concatenado. Guarde URL e título para apresentação e verificação; uma fonte retornada não valida sozinha todas as afirmações geradas.](perplexity.md).
