# Gerenciamento de Conversas

## Como Funciona o Histórico de Conversas

Cada chamada a `GetCompletionAsync` ou `StreamAsync` adiciona à lista de mensagens interna do serviço. Isso significa que o modelo tem contexto de todos os turnos anteriores.

```csharp
await service.GetCompletionAsync("Minha cor favorita é azul.");
var reply = await service.GetCompletionAsync("Qual é a minha cor favorita?");
// → "Sua cor favorita é azul."
```

Para começar do zero:

```csharp
service.ClearMessages();
```

## Política de Resumo

### Por que Resumo Automático?

Cada mensagem no histórico de conversa é enviada ao modelo em cada requisição. Conforme as conversas crescem, isso cria dois problemas:

1. **Custo** — históricos mais longos significam mais tokens de entrada cobrados por requisição
2. **Overflow de contexto** — uma vez que o histórico excede a janela de contexto do modelo, as requisições falham

**`SummaryConversationPolicy`** resolve isso condensando automaticamente mensagens mais antigas em um resumo compacto, mantendo as mensagens recentes literalmente.

### Disparar por Contagem de Mensagens

```csharp
service.ConversationPolicy = SummaryConversationPolicy.ByMessage(
    triggerCount: 20,   // resume quando o histórico excede 20 mensagens
    keepRecentCount: 5  // mantém as 5 mensagens mais recentes literalmente
);
```

### Disparar por Contagem de Tokens

```csharp
service.ConversationPolicy = SummaryConversationPolicy.ByToken(
    triggerTokens: 3000,    // resume quando o uso de tokens excede 3000
    keepRecentTokens: 1000  // mantém mensagens recentes até 1000 tokens
);
```

### Disparar por Ambos (Condição OU)

```csharp
service.ConversationPolicy = SummaryConversationPolicy.ByBoth(
    triggerTokens: 4000,
    triggerCount: 30,
    keepRecentTokens: 1300,
    keepRecentCount: 7
);
```

Uma vez configurado, o resumo acontece automaticamente em `GetCompletionAsync`.

### Como Funciona

1. Antes de cada completion, a política verifica se a conversa excede o limite configurado
2. Se acionada, mensagens mais antigas são resumidas em um texto conciso usando uma chamada LLM sem estado
3. O resumo é injetado como prefixo de mensagem do sistema
4. Mensagens recentes são preservadas literalmente

### Streaming

O resumo não é acionado automaticamente durante `StreamAsync`. Chame-o explicitamente antes:

```csharp
await service.ApplySummaryPolicyIfNeededAsync();

await foreach (var chunk in service.StreamAsync("Continue nossa conversa..."))
    Console.Write(chunk.Content);
```

## Salvando e Restaurando Resumo

Persista o resumo entre sessões para que o modelo retenha contexto após uma reinicialização:

```csharp
// Salvar
string saved = service.ConversationPolicy.CurrentSummary;
// → armazene no banco de dados, arquivo, etc.

// Restaurar em uma nova sessão
service.ConversationPolicy.LoadSummary(saved);
```
