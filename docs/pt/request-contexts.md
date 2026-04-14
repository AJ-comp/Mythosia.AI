# AIRequestContext

## O que É?

`AIRequestContext` permite modificar **o que o modelo vê** para uma única requisição — injetar instruções extras, adicionar documentos de referência ou substituir completamente a mensagem do usuário — sem alterar permanentemente a mensagem do sistema ou o histórico de conversa do serviço.

## O Problema que Resolve

Considere um pipeline RAG que recupera documentos relevantes e precisa incluí-los no prompt. **Sem** `AIRequestContext`, você teria que modificar a mensagem do sistema diretamente — poluindo o histórico e causando race conditions em aplicações multi-usuário.

**Com** `AIRequestContext`, a injeção fica restrita a exatamente uma requisição:

```csharp
// ✅ Com AIRequestContext — limpo, restrito, sem efeitos colaterais
var answer = await service.GetCompletionAsync(userQuestion,
    new AIRequestContext
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
        MessageBuilder.User("Doc de referência: A política de reembolso permite devoluções em 30 dias.").Build()
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
            MessageBuilder.User("Exemplo: ...").Build()
        }
    }
);
```

Consulte [AIRequestProfile](request-profiles.md) para detalhes sobre como sobrescrever parâmetros de geração.
