# Parâmetros de Geração

## Propriedades Comuns

Todas as instâncias de serviço de IA expõem estas propriedades:

```csharp
service.Temperature = 0.7f;        // Aleatoriedade [0, 2]. Menor = mais determinístico
service.TopP = 1.0f;               // Limiar de nucleus sampling
service.MaxTokens = 1024;          // Máximo de tokens de saída
service.FrequencyPenalty = 0.0f;   // Penaliza tokens repetidos
service.PresencePenalty = 0.0f;    // Penaliza tokens já presentes
service.MaxMessageCount = 20;      // Tamanho da janela de conversa
```

## Métodos de Extensão Fluentes

Retornam `this` para encadeamento:

```csharp
var service = new OpenAIService(apiKey, http)
    .WithSystemMessage("Você é um assistente útil.")
    .WithTemperature(0.3f)
    .WithMaxTokens(2048)
    .WithStatelessMode(true);
```

| Método | Descrição |
|--------|-------------|
| `.WithSystemMessage(string)` | Define o prompt do sistema |
| `.WithTemperature(float)` | Limitado a [0, 2] |
| `.WithMaxTokens(uint)` | Máximo de tokens de saída |
| `.WithStatelessMode(bool)` | Desativa o acúmulo de histórico de conversa |

## Modo Sem Estado (Stateless)

Quando ativado, cada requisição é independente — nenhum histórico de conversa é enviado ou armazenado:

```csharp
service.StatelessMode = true;

// Equivalente:
var service = new OpenAIService(apiKey, http).WithStatelessMode(true);
```

Útil para consultas únicas onde você não quer sobrecarga de histórico.

## Consultas Únicas (One-Shot)

Estes métodos de extensão executam uma única consulta sem afetar ou usar o histórico de conversa:

```csharp
// Prompt de texto
string response = await service.AskOnceAsync("Quanto é 2+2?");

// Mensagem (multimodal)
string response = await service.AskOnceAsync(message);

// Imagem a partir do caminho do arquivo
string response = await service.AskOnceWithImageAsync("Descreva isto", "foto.jpg");
```

## Troca de Modelos

Mude o modelo durante uma sessão preservando o histórico de conversa:

```csharp
service.ChangeModel(AIModels.OpenAI.Gpt4_1);

// Ou via método de extensão — limpa o histórico e começa do zero:
service.StartNewConversation(AIModels.Anthropic.ClaudeSonnet4_6);
```

## Gerenciando Múltiplas Conversas

Uma única instância de serviço pode manter múltiplas threads de conversa independentes:

```csharp
// Inicia um novo bloco de conversa
var chat1 = service.AddNewChat();

// Muda para um bloco diferente
service.SetActivateChat(chat2Id);

// Acessa todos os blocos
var allChats = service.ChatRequests;
```

## Inspecionando o Estado da Conversa

Recupera a última resposta do assistente ou um resumo da sessão atual:

```csharp
// Obtém a última mensagem do assistente (ou null se não houver)
string? lastReply = service.GetLastAssistantResponse();

// Obtém um resumo textual do estado atual do serviço
string info = service.GetConversationSummary();
// → Model: gpt-4o-mini
// → Messages: 12
// → Stateless Mode: False
// → System: Você é um assistente útil.
```

## Copiando Configuração de Serviço

Clone todas as configurações de outra instância de serviço (sem o histórico de conversa):

```csharp
var newService = new AnthropicService(apiKey, http);
newService.CopyFrom(existingService);
```
