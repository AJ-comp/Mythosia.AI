# Observar tarefas longas com Claude Fable 5.1

[Claude Opus 5.5](providers.md#claude-opus-55) está disponível com Mythosia.AI 8.1.0 / Abstractions 4.1.0: raciocínio sempre ativo, esforço medium por padrão e exibição omitida. Solicite progresso legível explicitamente; padrões e regras de vínculo diferem do Fable 5.1.

> Os controles do Fable 5.1 exigem `Mythosia.AI` 8.0.0 e `Mythosia.AI.Abstractions` 4.0.0 ou posteriores. As APIs existentes de Run, raciocínio/pesquisa e GPT-6 Astra mantêm as versões mínimas 7.1.0 / 3.1.0.

## Quando usar estes controles?

Uma investigação de documentos pode precisar de várias pesquisas e chamadas de ferramentas antes de responder. O aplicativo pode precisar mostrar o progresso, exigir uma verificação apenas no turno atual ou continuar após modificar conteúdo anterior da conversa. O Fable 5.1 oferece controles para esses casos, mas, ao reutilizar o thinking preservado, o próprio histórico faz parte do contrato da solicitação.

Use a [API Run](execution-api-transition.md) para observar e cancelar a tarefa, as [opções comuns de raciocínio e pesquisa](reasoning-and-search.md) para escolher esforço e fontes, e os ajustes de Claude abaixo para progresso e histórico. As capacidades nativas do modelo não significam que o Mythosia exponha todas as APIs do provedor.

## Escolher explicitamente o modelo e o esforço

```csharp
using Mythosia.AI.Models;
using Mythosia.AI.Services.Anthropic;

var service = new AnthropicService(apiKey, httpClient);
service.ChangeModel(AIModels.Anthropic.ClaudeFable5_1);
service.WithAdaptiveThinkingParameters(ClaudeReasoningEffort.High);

string answer = await service.GetCompletionAsync("Review this migration plan.");
```

`ClaudeFable5_1` seleciona `claude-fable-5-1`. `ClaudeMythos5_1` seleciona `claude-mythos-5-1` e exige acesso ao Project Glasswing. As constantes de Fable 5 e Mythos 5 continuam disponíveis. Ambos os modelos 5.1 recebem texto e imagens e produzem texto, com contexto de 1M de tokens e saída máxima de 128K tokens. [Visão geral do modelo](https://platform.claude.com/docs/en/models/fable-5-1/overview).

O esforço padrão nativo do modelo é `high`, mas o `ClaudeReasoningEffort.Auto` do Mythosia preserva o mapeamento existente de `ThinkingBudget`: orçamentos habilitados correspondem a `High`, a `XHigh` a partir de 32.768 e a `Max` a partir de 100.000. Uma solicitação para desligar o raciocínio usa esforço adaptativo baixo e omite thinking legível. Escolha `High` explicitamente quando precisar desse comportamento; `Auto` não significa que a biblioteca sempre omita effort e delegue ao padrão do modelo.

## Mostrar progresso entre chamadas de ferramentas

`ClaudeThinkingDisplay.Updates` solicita atualizações legíveis mantendo o raciocínio oculto. `Summarized` também inclui raciocínio resumido; `Omitted` suprime blocos thinking legíveis. As atualizações dependem de o modelo produzi-las, portanto não garantem notificações em intervalos fixos. [Atualizações de progresso](https://platform.claude.com/docs/en/models/fable-5-1/whats-new-fable-5-1#progress-updates-between-tool-calls-beta).

```csharp
using Mythosia.AI.Models.Streaming;

service.WithAdaptiveThinkingParameters(
    ClaudeReasoningEffort.High, ClaudeThinkingDisplay.Updates);

await using var run = await service.StartRunAsync(
    "Use the registered tools to investigate the report.",
    options: StreamOptions.FullOptions);

await foreach (var item in run.StreamAsync())
{
    if (item.Type == StreamingContentType.Reasoning)
        Console.WriteLine($"Progress: {item.Content}");
    else if (item.Type == StreamingContentType.Text)
        Console.Write(item.Content);
}
string result = (await run.Result).Text;
```

As atualizações usam o evento existente `StreamingContentType.Reasoning`. Habilite a observação com `StreamOptions.FullOptions` ou `StreamOptions.Default.WithReasoning()`. Após uma chamada sem streaming, leia `service.LastThinkingContent`. O texto de progresso é separado da resposta final e não revela a cadeia de pensamento bruta.

## Alterar instruções do turno sem reescrever o histórico

Os blocos thinking do Fable 5.1 são vinculados ao system prompt, às ferramentas e às mensagens que os precederam na geração. Reescrever essas entradas mantendo thinking posterior pode invalidá-lo. Uma instrução limitada ao turno serve, por exemplo, para exigir a consulta da política de suporte antes da resposta atual: ela é acrescentada ao final e mantida no histórico, mas deixa de valer quando surge outra mensagem do usuário. Isso evita reescrever repetidamente o system prompt principal. Alterar effort e adicionar instruções por turno são controles separados.

```csharp
await service
    .WithTurnInstruction("Check the registered support-policy tool before answering this turn.")
    .GetCompletionAsync("Can I return an opened product?");

await service
    .WithConversationInstruction("Use Korean for the remaining conversation.")
    .GetCompletionAsync("Explain the next step.");
```

Os dois métodos capturam instruções para a próxima solicitação lógica. O Mythosia adiciona uma mensagem system depois da entrada do usuário ou dos resultados de ferramentas, preservando mensagens anteriores. `WithTurnInstruction` usa `clear_at: "next_user_message"`; dentro da mesma solicitação, a biblioteca adiciona novamente a instrução após cada turno de resultados para mantê-la válida até o fim da solicitação. `WithConversationInstruction` continua valendo nos turnos seguintes. Configure antes de iniciar: esses métodos não são `run.SteerAsync` nem injetam instruções em uma resposta já em execução.

Para mudar o esforço entre solicitações preservando um prefixo de cache reutilizável, use `.WithReasoning(ReasoningLevel.High, cache: CachePreservation.Required)` de `Mythosia.AI.Extensions`. A biblioteca envia uma atualização de effort por mensagem e preserva seu histórico. Consulte as combinações suportadas no [guia comum](reasoning-and-search.md). No 5.1, prefixos/sufixos system por solicitação de `AIRequestContext` tornam-se instruções de turno acrescentadas ao final, sem reescrever um system prompt anterior.

O Fable 5.1 consegue ler thinking de modelos Claude anteriores, mas eles não conseguem ler o seu. O Mythos 5.1 possui as mesmas capacidades 5.1, mas não impõe a verificação de vínculo ao prefixo do Fable. Observe edições do histórico, trocas de modelo e blocos descartados, em vez de pressupor que o mesmo raciocínio foi preservado. [Guia de migração](https://platform.claude.com/docs/en/models/fable-5-1/migration-guide).

## Diagnosticar uma alteração intencional do histórico

`ThinkingPrefixMismatchBehavior = null` deixa a verificação a cargo da política da conta do provedor. `WithThinkingBinding(ClaudeThinkingPrefixMismatchBehavior.Error)` solicita explicitamente a validação do servidor. Edições do usuário no histórico, em `SystemMessage` ou nas ferramentas são enviadas à Anthropic; com `Error`, um prefixo incompatível gera a resposta 400 do provedor. Reenviar a mesma solicitação inválida não resolve o problema.

Se o aplicativo altera conteúdo anterior de propósito e aceita perder o raciocínio afetado, escolha `DropBlock`. O Mythosia envia esse controle à Anthropic; não remove thinking silenciosamente antes da solicitação.

```csharp
service.WithThinkingBinding(ClaudeThinkingPrefixMismatchBehavior.DropBlock);
service.SystemMessage = "You are reviewing the revised policy.";
await service.GetCompletionAsync("Reassess the recommendation.");

foreach (ClaudeInputTransformation change in service.LastInputTransformations)
    Console.WriteLine($"{change.Type}: {change.Reason} ({change.Model})");
```

`LastInputTransformations` expõe `Type`, `Path` e `Reason` relatados pelo provedor, além de `ResponseId` e `Model` para identificar a origem. `prefix_binding_mismatch` indica um prefixo alterado; `model_binding_mismatch`, thinking que o modelo de destino não consegue ler. Descartar não repara o raciocínio. Mantenha a transcrição intacta quando precisar preservá-la, ou inicie uma conversa nova para reiniciá-la.

O Mythosia preserva o histórico transmitido para evitar mudanças acidentais causadas pelo processamento interno de RAG/context. A compactação local automática é bloqueada em conversas normais do Fable 5.1 no caminho padrão/`Error`. `DropBlock` permite compactar, mas pode descartar raciocínio e não garante acertos de cache. A opção separada `CachePreservation.Required` mantém proteções mais rígidas do histórico. Opções comuns como `WithWebSearch()` são consumidas após cada solicitação. Omiti-las no próximo turno altera o array nativo tools e pode causar incompatibilidade do prefixo. Reaplique os mesmos ajustes de ferramentas/pesquisa para preservar o histórico; use `DropBlock` ou uma conversa nova para mudanças intencionais. Essas opções não passam automaticamente à próxima solicitação.

O snapshot do histórico transmitido pertence ao serviço e ao seu `ChatBlock`. Copiar apenas o `ChatBlock` para um serviço novo não transfere snapshots anteriores de RAG/context ou de system por turno. Continue com o mesmo serviço e conversa para preservar o raciocínio; se moveu apenas o histórico bruto, inicie uma conversa nova em vez de pressupor que o estado foi preservado.

## Usar a seleção normal de ferramentas

Fable 5.1 e Mythos 5.1 rejeitam a seleção forçada de ferramentas. Deixe `ForceFunctionName` sem definição e descreva na solicitação quando a ferramenta registrada deve ser usada. `FunctionsDisabled` continua disponível para turnos que não devem chamar ferramentas. Para uma resposta tipada, use a API existente de saída estruturada em vez de forçar uma função apenas para obter JSON.

## Identificar mudanças que pertencem ao servidor

| Opção nativa | Beta da Anthropic necessária |
| --- | --- |
| Effort por mensagem | `mid-conversation-output-config-2026-07-01` |
| Mensagem system limitada ao turno | `mid-conversation-system-clear-at-2026-08-21` |
| `thinking.display: "updates"` | `thinking-display-updates-2026-08-18` |
| Controles de vínculo do thinking e `input_transformations` | `thinking-binding-controls-2026-08-01` |

O Mythosia adiciona o cabeçalho correspondente quando a configuração suportada é habilitada. Ativar uma beta não ativa todas as outras. Esta integração não introduz compactação do servidor, blocos nativos de adição/remoção de ferramentas nem fallback automático de modelos.

Os dois modelos exigem as condições de retenção de 30 dias aplicáveis do provedor; ZDR precisa de autorização explícita da Anthropic. O thinking adaptativo fica sempre ativo; `budget_tokens` manuais e sua desativação não estão disponíveis, e parâmetros de amostragem personalizados não são enviados. Acesso à conta e retenção são requisitos do servidor. [Requisitos de migração](https://platform.claude.com/docs/en/models/fable-5-1/migration-guide).

A Anthropic aplica a marca d’água de texto, a procedência de mídias suportadas e o preço de leitura de cache. Não é necessária uma nova opção de solicitação do Mythosia. A integração não adiciona API de criação de procedência de mídia, controle para ligar ou desligar marcas d’água ou controle de cobrança. Veja as [novidades do Fable 5.1](https://platform.claude.com/docs/en/models/fable-5-1/whats-new-fable-5-1).
