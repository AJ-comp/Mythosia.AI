# Controlar tarefas de IA em andamento com Run

> Estas APIs exigem `Mythosia.AI` 7.1.0 ou posterior, que inclui `Mythosia.AI.Abstractions` 3.1.0 ou posterior. Os exemplos de RAG exigem `Mythosia.AI.Rag` 7.6.0 ou posterior.

## Por que controlar uma tarefa durante a execução?

Um relatório pode exigir várias buscas em documentos, chamadas de API e etapas de escrita. Durante esse período, o usuário pode querer acompanhar o progresso, interromper o trabalho ou acrescentar uma condição como “Incluir apenas os dados deste ano”. A aplicação precisa associar essas ações à tarefa que já está em andamento.

Run fornece um objeto que a aplicação pode manter para essa tarefa. Por exemplo, uma tela de chat pode mostrar o texto recebido, indicar quando uma ferramenta está sendo usada, conectar um botão Parar ao cancelamento e enviar uma instrução adicional quando o modelo oferecer suporte. Todas essas ações se referem à mesma execução.

| Necessidade da aplicação | O que usar |
| --- | --- |
| Receber uma resposta concluída sem controlar o trabalho em andamento | Continuar usando `GetCompletionAsync`, incluindo as sobrecargas tipadas e RAG. |
| Mostrar o texto conforme chega e recebê-lo completo ao terminar | Iniciar um run com `onText` e depois aguardar `run.Result`. |
| Mostrar a atividade das ferramentas ou aguardar um tratamento assíncrono da saída | Ler os eventos de `run.StreamAsync()`. |
| Permitir que o usuário interrompa o trabalho | Chamar `run.Cancel()` no objeto mantido. |
| Acrescentar uma condição antes do término | Verificar `run.CanSteer` e usar `run.SteerAsync(...)` em um modelo compatível. |

`StartRunAsync` inicia uma tarefa do modelo e retorna um `AIRun`. A tarefa continua mesmo que a saída não seja observada. O mesmo objeto permite acompanhar o streaming, obter o resultado acumulado, cancelar e, nos modelos compatíveis, enviar instruções durante a resposta. `GetCompletionAsync`, incluindo as sobrecargas tipadas e RAG, continua sendo uma API pública de conveniência para receber o resultado após a conclusão.

## Mostrar texto com um callback

Em uma tela de chat ou no console, mostrar o texto assim que ele chega permite acompanhar uma resposta longa enquanto ela está sendo escrita.

```csharp
await using var run = await service.WithMaxRounds(10).StartRunAsync(
    "Leia os documentos e escreva um relatório.",
    onText: text => Console.Write(text),
    cancellationToken: cancellationToken);

string answer = await run.Result;
```

`onText` é um callback opcional do tipo `Action<string>`, registrado antes do início. Ele recebe o texto em ordem e não executa ferramentas. Omita-o quando precisar apenas do resultado. Uma exceção no callback cancela o run e faz `Result` falhar. Não passe uma lambda `async` para `onText`: ela se tornaria `async void`, cujo trabalho e erros não poderiam ser aguardados pelo run. Use o fluxo de eventos para saída assíncrona. Os callbacks não são transferidos automaticamente para a thread da interface.

`Result` concatena os eventos de texto do run, incluindo o texto intermediário entre chamadas de ferramentas e o texto produzido antes de uma instrução adicional. Não é uma segunda requisição ao modelo nem uma resposta reescrita. Quem precisa apenas aguardar o resultado pode continuar usando `GetCompletionAsync` quando preferir sua semântica de resposta existente.

## Ler eventos de texto, ferramentas e uso

Quando uma tarefa busca documentos ou chama uma API de negócio, somente o texto pode não explicar a espera. Eventos tipados permitem mostrar a atividade das ferramentas junto da resposta e registrar os dados de uso fornecidos pelo provedor.

```csharp
await using var run = await service.StartRunAsync(
    "Busque nos documentos e explique o resultado.",
    cancellationToken: cancellationToken);

await foreach (var item in run.StreamAsync())
{
    switch (item.Type)
    {
        case StreamingContentType.Text:
            Console.Write(item.Content);
            break;
        case StreamingContentType.FunctionCall:
            Console.WriteLine("\n[Chamando uma ferramenta]");
            break;
        case StreamingContentType.FunctionResult:
            Console.WriteLine("\n[Resultado da ferramenta recebido]");
            break;
        case StreamingContentType.Completion when item.Usage != null:
            Console.WriteLine($"\n[Total de tokens: {item.Usage.TotalTokens}]");
            break;
    }
}

string answer = await run.Result;
```

`run.StreamAsync()` aceita um token opcional de cancelamento da observação, sem prompt. Ele observa a tarefa que já foi iniciada por `StartRunAsync`. A biblioteca executa os handlers de funções registrados; nunca execute novamente uma ferramenta ao receber seu evento de exibição. As opções de apresentação de texto não desativam as ferramentas registradas do run.

O callback de início e `run.StreamAsync()` podem observar o mesmo run simultaneamente; o fluxo de eventos aceita um leitor. Por exemplo, exiba o texto em `onText` e processe apenas eventos de ferramentas no fluxo para evitar texto duplicado. Até 1.024 eventos não lidos ficam no buffer, mesmo com um callback. Um leitor iniciado mais tarde recebe os eventos desde o começo enquanto eles couberem no buffer. Se o limite for ultrapassado, a observação do fluxo falha explicitamente, mas o callback, a execução e `Result` continuam. O fluxo não é um registro ilimitado para reprodução. Aguardar `Result` nunca exige consumir todo o fluxo de eventos.

## Saída assíncrona

Para tratar a saída de forma assíncrona, aguarde a operação dentro do leitor em vez de usar um callback `onText` assíncrono:

```csharp
using var writer = new StreamWriter("relatorio.txt");
await using var run = await service.StartRunAsync(
    "Escreva um relatório.", cancellationToken: cancellationToken);

await foreach (var item in run.StreamAsync())
{
    if (item.Type == StreamingContentType.Text && item.Content is string text)
        await writer.WriteAsync(text);
}
string answer = await run.Result;
```

## Cancelar e liberar recursos

- Sair de `await foreach` ou cancelar o token passado somente para `run.StreamAsync(token)` interrompe a observação; a tarefa continua.
- `run.Cancel()`, o token passado para `StartRunAsync` e a liberação de um run ativo cancelam a execução.
- `await using` garante que `DisposeAsync()` aguarde o produtor e a limpeza do provedor. Ferramentas sem suporte a cancelamento podem demorar para terminar; liberar recursos não desfaz ações concluídas.
- Um serviço permite uma tarefa `StartRunAsync` ativa. Uma tentativa de início simultâneo é rejeitada. Use serviços separados para tarefas concorrentes independentes e não misture chamadas antigas nem altere configurações do serviço enquanto um run estiver ativo.

O run captura a entrada e a política pendente para aquela requisição antes da execução em segundo plano. Conteúdos integrados de texto, imagem e áudio, assim como os arrays de bytes das mídias, são copiados. Subclasses personalizadas de `MessageContent` mantêm sua identidade e devem permanecer inalteradas até o término do run.

O valor capturado de `FunctionCallingPolicy.TimeoutSeconds` define um único prazo para a preparação e todas as rodadas de modelo e ferramentas. O vencimento gera `AIServiceException`; o cancelamento pelo usuário cancela o resultado. A limpeza continua aguardando handlers sem suporte a cancelamento.

## Enviar outra instrução durante o trabalho

Imagine que um usuário inicia um plano de projeto e depois percebe que ele precisa caber em duas semanas. As instruções durante a execução permitem enviar essa nova condição enquanto o modelo ainda trabalha. São úteis para correções e mudanças de escopo descobertas durante tarefas longas. Para uma nova pergunta após o término, inicie normalmente a próxima requisição.

```csharp
await using var run = await service.StartRunAsync(
    "Elabore um plano de projeto.",
    onText: text => Console.Write(text),
    cancellationToken: cancellationToken);

// Chame pelo handler de instruções adicionais da interface enquanto o run estiver ativo.
async Task SendUpdateAsync(string instruction)
{
    if (!run.CanSteer)
        throw new NotSupportedException("Este run não oferece suporte a instruções adicionais.");

    await run.SteerAsync(instruction, cancellationToken);
}

string answer = await run.Result;
```

As instruções durante a resposta estão disponíveis para GPT-6 Astra pela conexão WebSocket da Responses. Outros provedores e modelos sem suporte podem executar runs normais, mas `CanSteer` é false e a tentativa de enviar instruções informa a falta de suporte, em vez de criar silenciosamente um próximo turno comum. `CanSteer` não garante que o run ainda estará ativo quando uma chamada posterior for feita.

Os runs de Astra abrem um socket dedicado. O `HttpClient` fornecido e seus handlers de mensagens continuam atendendo às chamadas HTTP e não interceptam esse socket. Transportes personalizados podem sobrescrever `OpenAIService.ConnectRunWebSocketAsync`.

O sucesso de `SteerAsync` significa que o servidor aceitou a entrada na fila, não que o modelo já a aplicou. Continue observando o mesmo run ou aguardando seu resultado durante a continuação. O texto já entregue e as ações concluídas não são desfeitos, e ferramentas iniciadas não são canceladas apenas porque uma nova instrução foi enviada. A biblioteca gerencia a continuação e a associação dos resultados de ferramentas na mesma conexão. Consulte o [guia de instruções durante a resposta](https://developers.openai.com/api/docs/guides/steering) e o [modo WebSocket](https://developers.openai.com/api/docs/guides/websocket-mode) da OpenAI. Não presuma que entradas em espera vinculadas à conexão sobrevivam a uma desconexão, nem reenvie sem verificar uma instrução já aceita.

## Tarefas com ferramentas e métodos antigos de agente

Perguntas como “Confira a política de reembolso e o status deste pedido” exigem mais de uma fonte. Registre ferramentas de busca em documentos e consulta de pedidos e deixe o modelo escolher as chamadas necessárias. Um limite de rodadas restringe por quanto tempo ele pode continuar solicitando ferramentas antes de concluir ou informar um erro.

As chamadas de funções comuns já oferecem rodadas repetidas entre modelo e ferramentas. `StartRunAsync` usa as mesmas funções registradas e políticas de execução; não exige um modo de agente separado, um planejador ou uma opção `WithAgentic`.

`RunAgentAsync` e `RunAgentStreamAsync` continuam disponíveis, mas agora apresentam avisos `[Obsolete]`. Suas assinaturas, o padrão `maxSteps = 10` e o comportamento de erro antigo ao atingir o limite de etapas são mantidos durante a migração. Nas novas chamadas, use:

```csharp
await using var run = await service.WithMaxRounds(10).StartRunAsync(
    "Encontre a política, confira o pedido e explique o resultado.",
    onText: text => Console.Write(text),
    cancellationToken: cancellationToken);
string answer = await run.Result;
```

O padrão geral de `FunctionCallingPolicy.MaxRounds` é 20; portanto, informe 10 para preservar o limite do agente antigo. `WithMaxRounds` configura uma política para uma única requisição e não altera `DefaultPolicy`. Configure-a antes de iniciar o trabalho. Os métodos antigos de agente, por sua vez, copiam a política padrão atual e aplicam o `maxSteps` daquela chamada. Um novo run utiliza o contrato comum de erros de execução e não garante a conversão antiga em `AgentMaxStepsExceededException`/`PartialResponse`. Se depender desse contrato, mantenha a chamada antiga até migrar também seu tratamento de exceções.

## RAG, MCP e limites entre pacotes

- `RagEnabledService.StartRunAsync` aceita entrada como string ou `Message`, `onText`, `RagQueryOptions` por consulta, `streamOptions` e cancelamento. A busca acontece antes do run subjacente, preservando imagens, áudio e metadados, mantendo a entrada original no histórico e enviando o texto enriquecido pelo contexto da requisição. Esse enriquecimento fica vinculado à pergunta original do usuário, de modo que resultados posteriores de ferramentas e instruções adicionais não sejam substituídos pelo prompt RAG original. Instruções enviadas ao run retornado atualizam o modelo, mas não repetem automaticamente a busca RAG.
- `WithAgenticRag` continua registrando uma ferramenta de busca. Ao usá-la por `StartRunAsync`, o modelo pode solicitar novas buscas conforme necessário. O registro MCP por `WithMcpServerAsync` também permanece inalterado. Libere conexões MCP compartilhadas separadamente dos runs que as utilizam.
- `IAIRunService` é uma capacidade opcional de `Mythosia.AI.Abstractions`; `IAIService` não recebe novos membros obrigatórios. Um serviço personalizado deve implementar `IAIRunService` para iniciar runs RAG. Serviços sem suporte são rejeitados antes do início da indexação RAG.
- RAG mantém sua dependência de Abstractions. Provedores distribuídos em pacotes separados mantêm suas sobrescritas públicas de completion e pontos de extensão acessíveis. Esta mudança não descontinua nenhuma API de armazenamento vetorial, carregamento de documentos ou administração de servidores.

Para configurar raciocínio, busca na Web ou de arquivos antes de iniciar o Run e exibir suas fontes, consulte [Raciocínio e respostas com fontes](reasoning-and-search.md).

## Compatibilidade e próxima versão principal

| API | Situação atual |
| --- | --- |
| `GetCompletionAsync` / `GetCompletionAsync<T>` | Pública e suportada, incluindo variantes da interface, dos provedores e de RAG. |
| `StartRunAsync` / `AIRun` | API comum de execução e controle. |
| `RunAgentAsync` / `RunAgentStreamAsync` | Avisos Obsolete; comportamento existente mantido por compatibilidade. |
| `service.StreamAsync` e `StreamAsync` de RAG que recebem entrada | Continuam disponíveis nesta versão secundária; retirada pública planejada para a próxima versão principal. |
| `run.StreamAsync()` | Observação da saída de uma tarefa existente, sem nova entrada de requisição. |
| `BeginStream(...).As<T>()` / `StructuredStreamRun<T>` | API de streaming tipado preservada; seu `Stream()` somente de saída não é um método antigo de requisição do serviço. |

A próxima transição principal altera os pontos de entrada públicos de streaming e preserva a execução e os pontos de extensão necessários dos provedores. Tornar um método público privado ou protegido continua quebrando a compatibilidade de código-fonte e binária mesmo que seu corpo seja mantido. Auxiliares de cadeias de mensagens, chamadas pontuais, resumo, reescrita de consultas e reordenação não ficam obsoletos apenas por usarem os métodos de execução existentes.
