# AIRequestProfile

## O que É?

`AIRequestProfile` permite sobrescrever parâmetros de geração — temperatura, máximo de tokens, modo sem estado, chamada de funções — **apenas para uma única requisição**. As configurações globais do serviço não são alteradas.

## O Problema que Resolve

Imagine que você tem um chatbot configurado para conversação criativa:

```csharp
var service = new OpenAIService(apiKey, http)
    .WithTemperature(0.8f)
    .WithMaxTokens(2048)
    .WithSystemMessage("Você é um assistente de escrita criativa.");
```

Agora seu pipeline RAG precisa reescrever a consulta do usuário com baixa temperatura e sem histórico. **Sem** `AIRequestProfile`, você teria que fazer isso:

```csharp
// ❌ Sem AIRequestProfile — gerenciamento manual de estado
var savedTemp = service.Temperature;
// ...salva, modifica, usa, restaura — frágil e não thread-safe
```

**Com** `AIRequestProfile`, é uma linha:

```csharp
// ✅ Com AIRequestProfile — limpo e seguro
var rewritten = await service.GetCompletionAsync("Reescreva esta consulta: ...",
    new AIRequestProfile { Temperature = 0.1f, MaxTokens = 256, Stateless = true });
```

As configurações globais do serviço nunca são tocadas. Sem necessidade de limpeza. Thread-safe.

## Propriedades Disponíveis

```csharp
var profile = new AIRequestProfile
{
    Temperature = 0.1f,       // Sobrescreve temperatura
    MaxTokens = 256,          // Sobrescreve tokens de saída máximos
    Stateless = true,         // Não adiciona esta troca ao histórico
    DisableFunctions = true,  // Ignora chamada de funções para esta requisição
    DisableReasoning = true   // Ignora reasoning para esta requisição
};

var response = await service.GetCompletionAsync("Seu prompt", profile);
```

## Perfis Predefinidos

```csharp
// Reescrita de consulta: baixa temperatura, orçamento pequeno de tokens, sem estado
var rewritten = await service.GetCompletionAsync(query, RequestProfiles.QueryRewrite);

// Resumo: temperatura ligeiramente maior, tokens moderados
var summary = await service.GetCompletionAsync(text, RequestProfiles.Summarization);
```

## Combinando com AIRequestContext

Ambos podem ser passados juntos para controle máximo:

```csharp
var response = await service.GetCompletionAsync(
    prompt,
    profile: RequestProfiles.QueryRewrite,
    context: new AIRequestContext { SystemMessageSuffix = "\nSeja conciso." }
);
```

Consulte [AIRequestContext](request-contexts.md) para detalhes sobre como injetar conteúdo nas requisições.
