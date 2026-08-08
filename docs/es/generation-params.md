# Parámetros de Generación

## Propiedades Comunes

Todas las instancias de servicio de IA exponen estas propiedades:

```csharp
service.Temperature = 0.7f;        // Aleatoriedad [0, 2]. Menor = más determinista
service.TopP = 1.0f;               // Umbral de nucleus sampling
service.MaxTokens = 1024;          // Máximo de tokens de salida
service.FrequencyPenalty = 0.0f;   // Penaliza tokens repetidos
service.PresencePenalty = 0.0f;    // Penaliza tokens ya presentes
```


## Métodos de Extensión Fluentes

Devuelven `this` para encadenamiento:

```csharp
var service = new OpenAIService(apiKey, http)
    .WithSystemMessage("Eres un asistente útil.")
    .WithTemperature(0.3f)
    .WithMaxTokens(2048)
    .WithStatelessMode(true);
```

| Método | Descripción |
|--------|-------------|
| `.WithSystemMessage(string)` | Establece el prompt de sistema |
| `.WithTemperature(float)` | Limitado a [0, 2] |
| `.WithMaxTokens(uint)` | Máximo de tokens de salida |
| `.WithStatelessMode(bool)` | Desactiva la acumulación del historial de conversación |

## Modo Sin Estado (Stateless)

Cuando está habilitado, cada solicitud es independiente — no se envía ni almacena historial de conversación:

```csharp
service.StatelessMode = true;

// Equivalente:
var service = new OpenAIService(apiKey, http).WithStatelessMode(true);
```

Útil para consultas puntuales donde no quieres la sobrecarga del historial.

## Consultas Únicas (One-Shot)

Estos métodos de extensión ejecutan una sola consulta sin afectar ni usar el historial de conversación:

```csharp
// Prompt de texto
string response = await service.AskOnceAsync("¿Cuánto es 2+2?");

// Mensaje (multimodal)
string response = await service.AskOnceAsync(message);

// Imagen desde ruta de archivo
string response = await service.AskOnceWithImageAsync("Describe esto", "foto.jpg");
```

## Cambiar de Modelo

Cambia el modelo a mitad de sesión preservando el historial de conversación:

```csharp
service.ChangeModel(AIModels.OpenAI.Gpt4_1);

// O mediante método de extensión — limpia el historial y empieza de cero:
service.StartNewConversation(AIModels.Anthropic.ClaudeSonnet4_6);
```

## Gestionar Múltiples Conversaciones

Una sola instancia de servicio puede mantener múltiples hilos de conversación independientes:

```csharp
// Inicia un nuevo bloque de conversación
service.AddNewChat();
var chat1 = service.ActivateChat;

// Cambia a un bloque diferente
service.SetActivateChat(chat2Id);

// Accede a todos los bloques
var allChats = service.ChatRequests;
```

## Inspeccionar el Estado de la Conversación

Recupera la última respuesta del asistente o un resumen de la sesión actual:

```csharp
// Obtiene el último mensaje del asistente (o null si no hay ninguno)
string? lastReply = service.GetLastAssistantResponse();

// Obtiene un resumen textual del estado actual del servicio
string info = service.GetConversationSummary();
// → Model: gpt-4o-mini
// → Messages: 12
// → Stateless Mode: False
// → System: Eres un asistente útil.
```

## Copiar Configuración de Servicio

Clona toda la configuración de otra instancia de servicio (sin el historial de conversación):

```csharp
var newService = new AnthropicService(apiKey, http);
newService.CopyFrom(existingService);
```
