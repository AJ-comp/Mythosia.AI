# AIRequestContext

## O que É?

`AIRequestContext` permite modificar **o que o modelo vê** para uma única requisição — injetar instruções extras, adicionar documentos de referência ou substituir completamente a mensagem do usuário — sem alterar permanentemente a mensagem do sistema ou o histórico de conversa do serviço.

## O Problema que Resolve

Considere um pipeline RAG que recupera documentos relevantes e precisa incluí-los no prompt. **Sem** `AIRequestContext`, você teria que modificar a mensagem do sistema diretamente — poluindo o histórico e causando race conditions em aplicações multi-usuário.

**Com** `AIRequestContext`, a injeção fica restrita a exatamente uma requisição:

```csharp
// ✅ Com AIRequestContext — limpo, restrito, sem efeitos colaterais
var answer = await service.GetCompletionAsync(userQuestion,
    context: new AIRequestContext
    {
        SystemMessageSuffix = $"\n\nUse o seguinte contexto para responder:\n{retrievedDocs}"
    });
```

## Propriedades Disponíveis

### SystemMessagePrefix

Adiciona texto ao início da mensagem do sistema apenas para esta requisição:

```csharp
var context = new AIRequestContext
{
    SystemMessagePrefix = "A data de hoje é 2026-03-31.\n"
};
```

**Quando usar:** Injetar metadados dinâmicos (data, fuso horário do usuário, informações da sessão).

### SystemMessageSuffix

Adiciona texto ao final da mensagem do sistema apenas para esta requisição:

```csharp
var context = new AIRequestContext
{
    SystemMessageSuffix = "\nSempre responda em português."
};
```

**Quando usar:** Adicionar instruções comportamentais por requisição, contexto RAG ou preferências de idioma.

### AdditionalMessages

Insere mensagens extras na conversa apenas para esta requisição:

```csharp
var context = new AIRequestContext
{
    AdditionalMessages = new List<Message>
    {
        MessageBuilder.Create().AddText("Doc de referência: A política de reembolso permite devoluções em 30 dias.").Build()
    }
};
```

### RequestMessageOverride

Substitui completamente a mensagem do usuário para esta requisição:

```csharp
var context = new AIRequestContext
{
    RequestMessageOverride = MessageBuilder
        .User($"Com base no contexto a seguir, responda à pergunta.\n\nContexto: {docs}\n\nPergunta: {userQuery}")
        .Build()
};
```

## Combinando com AIRequestProfile

Ambos podem ser passados juntos para controle máximo sobre uma única requisição:

```csharp
var response = await service.GetCompletionAsync(
    prompt,
    profile: new AIRequestProfile { Temperature = 0.1f, Stateless = true },
    context: new AIRequestContext
    {
        SystemMessageSuffix = $"\nContexto:\n{docs}",
        AdditionalMessages = new List<Message>
        {
            MessageBuilder.Create().AddText("Exemplo: ...").Build()
        }
    }
);
```

Consulte [AIRequestProfile](request-profiles.md) para detalhes sobre como sobrescrever parâmetros de geração.

## Injeção automática com `SystemMessageProvider`

### O problema que resolve

Uma aplicação de chat típica tem vários pontos de entrada ao LLM que precisam da mesma baseline — data de hoje, pasta ativa, info de sessão. **Sem** `SystemMessageProvider`, cada local de chamada precisa lembrar de construir e passar esse contexto:

```csharp
// ❌ Sem SystemMessageProvider — cada ponto de entrada deve lembrar de injetar
var today = $"Today is {DateTime.UtcNow:yyyy-MM-dd}.";

// 1. Resposta principal do chat
var answer = await service.GetCompletionAsync(userMessage,
    context: new AIRequestContext { SystemMessageSuffix = today });

// 2. Gerador de títulos (adicionado depois)
var title = await service.GetCompletionAsync("Summarize as a title: " + conversation,
    context: new AIRequestContext { SystemMessageSuffix = today });

// 3. Sumarizador (adicionado ainda mais tarde)
var summary = await service.GetCompletionAsync("Summarize: " + conversation,
    context: new AIRequestContext { SystemMessageSuffix = today });

// 4. Chamada de agent — fácil de esquecer! O compilador não avisa
var agentResult = await service.RunAgentAsync(goal);  // ← data faltando, bug silencioso
```

Problemas desta abordagem:

- O mesmo snippet de construção de contexto é **duplicado** em cada ponto de chamada
- Novos pontos de entrada (o `RunAgentAsync` acima) são **fáceis de omitir** — sem verificação em tempo de compilação
- Cada nova feature que adiciona uma chamada ao LLM tem que lembrar da convenção
- Os testes também precisam replicar o setup de contexto em cada ponto de chamada

Com `SystemMessageProvider`, você registra a baseline **uma vez** e cada chamada de saída a recebe automaticamente:

```csharp
// ✅ Com SystemMessageProvider — registrar uma vez, aplicado em todo lugar
service.WithSystemMessageProvider(() => new AIRequestContext
{
    SystemMessageSuffix = $"Today is {DateTime.UtcNow:yyyy-MM-dd}."
});

// Todos recebem automaticamente a baseline — sem boilerplate por chamada
var answer      = await service.GetCompletionAsync(userMessage);
var title       = await service.GetCompletionAsync("Summarize as a title: " + conversation);
var summary     = await service.GetCompletionAsync("Summarize: " + conversation);
var agentResult = await service.RunAgentAsync(goal);  // ← também recebe a baseline

// Os pontos de entrada streaming também — mesma baseline, sem boilerplate por chamada
await foreach (var chunk in service.StreamAsync(userMessage)) { /* ... */ }
await foreach (var token in service.RunAgentStreamAsync(goal)) { /* ... */ }
```

### Como funciona

Registre o callback uma vez via o helper fluent `WithSystemMessageProvider`. Cada chamada de saída (`GetCompletionAsync`, `StreamAsync`, `RunAgentAsync`, `RunAgentStreamAsync`) o invoca automaticamente para construir um contexto base:

```csharp
// Tipicamente na construção do serviço / configuração DI
service.WithSystemMessageProvider(() => new AIRequestContext
{
    SystemMessageSuffix =
        $"Today is {DateTime.UtcNow:yyyy-MM-dd}.\n" +
        $"Current folder: {_uiContext.CurrentFolder}"
});

var answer = await service.GetCompletionAsync(userQuery);
await foreach (var chunk in service.StreamAsync(msg, options)) { /* ... */ }
var agentResult = await service.RunAgentAsync(goal);
```

### Sobrecarga async para providers baseados em IO

Quando o contexto base vem de um banco de dados, cache ou chamada HTTP, use a sobrecarga async para que o provider não precise bloquear com `.Result` / `.GetAwaiter().GetResult()`. A resolução de sobrecarga escolhe a certa pela arity do lambda — sem argumento para sync, um `CancellationToken` para async:

```csharp
service.WithSystemMessageProvider(async ct =>
{
    var prefs = await _db.UserPreferences.FirstOrDefaultAsync(ct);
    return new AIRequestContext
    {
        SystemMessageSuffix = $"User language: {prefs?.Language ?? "en"}"
    };
});
```

Os caminhos não-streaming (`GetCompletionAsync`, `RunAgentAsync`) não suportam cancelamento por design — suas assinaturas não aceitam um `CancellationToken`, e `CancellationToken.None` é sempre passado ao provider. Se seu provider precisa de cancelamento (ex: uma consulta DB longa), use os caminhos de streaming (`StreamAsync`, `RunAgentStreamAsync`), que propagam o token do chamador até o callback do provider.

### Fusão com um contexto per-call explícito

Quando uma chamada tem um provider registrado **e** também passa um `AIRequestContext` explícito, os dois se fundem campo a campo:

| Campo | Regra de fusão |
|---|---|
| `SystemMessagePrefix` | explícito vence se non-null, senão provider |
| `SystemMessageSuffix` | explícito vence se non-null, senão provider |
| `RequestMessageOverride` | explícito vence se non-null, senão provider |
| `AdditionalMessages` | concatenados (provider primeiro, depois explícito) |

Razão: o caso comum é "provider fornece uma base, uma chamada específica quer substituir um campo escalar ou adicionar mensagens extras" — override em nível de campo mantém a semântica previsível sem concatenação surpreendente.

### Invocação por chamada

O provider é invocado **uma vez por requisição**, assim os valores de retorno podem refletir o estado no momento (timestamp, sessão, etc.). Retornar `null` é um no-op — idêntico a deixar `SystemMessageProvider` não configurado para aquela chamada.

### Em resumo: quando escolher esta ferramenta — a interseção de três condições

Dando um passo atrás em relação aos exemplos e às regras de fusão acima, `SystemMessageProvider` é a ferramenta dedicada quando **três condições se cumprem simultaneamente**:

1. **Deve haver uma baseline em toda chamada ao LLM** — não se quer lembrar da injeção em cada ponto de entrada
2. **O valor deve ser avaliado dinamicamente no momento da chamada** — hora atual, pasta ativa, usuário logado e outros valores que não podem ser fixados na inicialização
3. **O estado permanente (`SystemMessage`, histórico de conversa) não pode ser contaminado** — o valor não pode vazar para chamadas posteriores

Se faltar qualquer uma das três condições, uma ferramenta mais simples é a resposta certa:

| Situação | Ferramenta certa | Motivo |
|---|---|---|
| A baseline é **fixa (não muda)** durante toda a sessão | `service.SystemMessage = "..."` | Uma atribuição única basta, provider desnecessário |
| **Apenas uma chamada específica** precisa de tratamento especial | Passar `AIRequestContext` explicitamente no ponto de chamada | Não é uma baseline compartilhada, é uma injeção pontual |
| Compartilhada + dinâmica + sem contaminação **(as três)** | **`SystemMessageProvider`** | A ferramenta dedicada para esta interseção tripla |

#### Por que isto não conflita com o princípio de "uso único" do `AIRequestContext`

A essência do `AIRequestContext` não é "usado apenas uma vez", mas **"nunca contamina o estado permanente"**. `SystemMessageProvider` é uma fábrica que **re-executa o callback a cada requisição**, produzindo **um novo `AIRequestContext` escopado para essa requisição**. O contexto resultante continua per-request scoped, o valor nunca vaza para o histórico de conversa, e na próxima chamada o callback é re-executado refletindo o valor **daquele momento**. O provider, portanto, não viola o princípio de design do `AIRequestContext` — apenas **automatiza-o**.

Concretamente, registrar o provider abaixo **não** modifica `service.SystemMessage` nem `service.ActivateChat.Messages`:

```csharp
service.WithSystemMessageProvider(() => new AIRequestContext
{
    SystemMessageSuffix = $"Today is {DateTime.UtcNow:yyyy-MM-dd}"
});
```

- Passada a meia-noite, a re-execução do provider na próxima chamada reflete automaticamente a **nova data** (não é estático)
- Uma semana depois, abrindo o histórico de conversa, não se encontra "Today is ..." incrustado em requisições passadas
- Mesmo usando um serviço compartilhado em ambiente multi-usuário, cada chamada produz seu próprio contexto independente

> Disponível em Mythosia.AI v6.3.0+.
