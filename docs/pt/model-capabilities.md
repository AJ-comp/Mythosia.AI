# Mostrar opções compatíveis com o modelo escolhido

> Grok 4.7: Requer Mythosia.AI 8.1.0 / Abstractions 4.1.0. [seleção do modelo, raciocínio e velocidade](providers.md#grok-47)

> GPT-6 Sol/Luna ainda não foram publicados. Veja [seleção do modelo e requisitos](providers.md#gpt-6-sol-luna).

Para [Claude Opus 5.5](providers.md#claude-opus-55), as capabilities expõem `Low` a `Max`, incluindo `XHigh`; `None`, `Minimal` e `ThinkingToggle` não são suportados. `MaxOutputTokens` é 128000. Ocultar o texto não desativa o raciocínio. Requer Mythosia.AI 8.1.0 / Abstractions 4.1.0.

Uma interface de chat deve oferecer raciocínio, pesquisa, ferramentas e imagens conforme a conexão escolhida. Listas de modelos mantidas em cada aplicativo duplicam regras da biblioteca e divergem quando provedor, protocolo ou implantação muda. As cópias de capacidades compartilham definições entre interface e validação da execução.

Esta API pertence a Mythosia.AI 8.0.0. São descrições locais imutáveis do suporte conhecido, não consultas em tempo real à conta ou ao servidor. Os tipos estão em `Mythosia.AI.Models.Capabilities`.

Para pedidos sensíveis ao tempo de espera, escolha a [velocidade de processamento](request-building.md#inference-speed). `WithSpeed` mantém modelo e esforço; `Processing` informa o modo aplicado. Fast é pago nas combinações suportadas.

## Before / After

Before: o aplicativo mantém suas listas. As listas abaixo são código do aplicativo, não APIs da biblioteca.

```csharp
bool showReasoning = mySupportedReasoningModels.Contains(service.Model);
bool showSearch = mySupportedSearchModels.Contains(service.Model);
```

After: inspecione a requisição configurada e escolha opções compatíveis. Somente a chamada final de completion envia a requisição ao modelo; consultar capacidades não chama a API.

```csharp
using Mythosia.AI.Models;
using Mythosia.AI.Models.Capabilities;

var request = service.CreateRequest("Explique os documentos.");
AIModelCapabilities capabilities = request.GetCapabilities();

if (capabilities.GetReasoningSupport(ReasoningLevel.High)
    == CapabilitySupport.Supported)
{
    request = request.WithReasoning(ReasoningLevel.High);
}

string answer = await request.GetCompletionAsync();
```

`CapabilitySupport` distingue `Supported`, `Unsupported` e `Unknown`. Uma implantação própria ou seleção pelo servidor pode ter dados insuficientes: `Unknown` não significa incompatível. O exemplo ativa raciocínio extra apenas com suporte conhecido. Para casos desconhecidos, o aplicativo decide manter padrões ou permitir uma tentativa.

`request.GetCapabilities()` lê modelo, opções do provedor e perfil capturados pelo builder. `service.GetCapabilities()` examina padrões do serviço sem consumir opções da próxima chamada. Nenhum envia HTTP, chama callbacks de contexto ou validadores de execução, altera histórico ou inicia trabalho. As listas também são cópias somente leitura. A consulta do serviço também examina funções pendentes para a próxima chamada e as preserva para a requisição real. A consulta não serializa valores padrão de funções nem parâmetros de ferramentas hospedadas, e não prepara o perfil de execução nem reserva orçamento de tokens.

As capacidades descrevem o que a conexão pode suportar, não o que está ativado. Provedor, protocolo e modo importam além do nome. A identidade reflete substituições e tradução de ID Qwen/Ollama; sem um modelo único pode ser `null`. O exemplo Chat UI atualiza os controles a partir da conexão ativa e de suas configurações atuais, incluindo ferramentas registradas, em vez de depender apenas do catálogo de modelos. O suporte à amostragem pode mudar conforme o modo de raciocínio ou a disponibilidade de ferramentas; consulte novamente após essas alterações. Suporte desconhecido permanece distinto de não suportado.

| API | Significado |
| --- | --- |
| `Reasoning`, `ReasoningLevels`, `GetReasoningSupport(...)` | Suporte e níveis do `WithReasoning` comum. |
| `NativeReasoning`, `NativeReasoningLevels`, `ThinkingBudgetPresets`, `ThinkingToggle` | Controles nativos do provedor e orçamentos sugeridos. |
| `Streaming`, `FunctionCalling`, `AsyncFunctionCalling`, `Steering` | Streaming, ferramentas, ferramentas assíncronas nativas e instruções durante a execução. |
| `WebSearch`, `FileSearch`, `ReasoningCachePreservation`, `ImageInput`, `StructuredOutput` | Pesquisa hospedada, mudanças de raciocínio preservando cache, imagens de entrada e saída estruturada. |
| `Temperature`, `TopP`, `FrequencyPenalty`, `PresencePenalty`, `MaxOutputTokens` | Amostragem suportada e limite conhecido de tokens de saída, nullable. |
| `StandardSpeed`, `FastSpeed`, `GetSpeedSupport(...)` | Mythosia.AI 8.1.0 / Abstractions 4.1.0: modos Supported/Unsupported/Unknown; verificar acesso da conta separadamente. |
| `Provider`, `Model` | Provedor e modelo enviado; as identidades podem ser desconhecidas. |

`ReasoningLevels` descreve `WithReasoning` comum; `NativeReasoningLevels`, os controles nativos. `ThinkingBudgetPresets` oferece sugestões para UI, não todos os orçamentos válidos ou uma faixa exaustiva. `AsyncFunctionCalling` indica execução assíncrona nativa de ferramentas, não apenas handlers locais retornando `Task` ou execução paralela. `StructuredOutput` inclui a API comum de saída tipada com alternativa por prompt e reparo; não garante decodificação restrita nativa. Ambas as listas de níveis usam `ReasoningLevel`; os orçamentos sugeridos são inteiros.

Uma cópia não garante acesso da conta nem servidor pronto e não torna combinações inválidas aceitáveis. Validações e erros de execução permanecem. Confira `run.CanSteer` na sessão real: suporte do modelo não garante que o Run ainda esteja ativo.

## Consultar geração de imagens separadamente

O modelo de imagem é independente do chat. `service.GetImageCapabilities(imageModel)` consulta um específico; sem argumento, o padrão de imagens do provedor. O builder de chat não escolhe esse modelo. `Generation`, `Editing` e `Mask` ajudam a apresentar ações de imagem.

```csharp
using Mythosia.AI.Models.Capabilities;

ImageModelCapabilities capabilities = service.GetImageCapabilities();
bool showEditing = capabilities.Editing == CapabilitySupport.Supported;
bool showMask = capabilities.Mask == CapabilitySupport.Supported;

foreach (var quality in capabilities.Qualities)
    Console.WriteLine(quality);

int? maximumImages = capabilities.MaxImages;
```

`Qualities`, `Backgrounds`, `OutputFormats`, `SizeKinds`, `Resolutions` e `AspectRatios` são listas tipadas somente leitura. `MaxImages` e `MaxInputImages` são limites conhecidos nullables. Um valor listado não garante todas as combinações: seguem as validações de tamanho, formato, qualidade, máscara e modelo. Modelos próprios ou desconhecidos continuam desconhecidos, sem serem marcados como incompatíveis.

No Google, `Resolutions` e `AspectRatios` dependem do modelo de imagem selecionado e também orientam a validação de geração e edição. Consulte a [tabela por modelo](providers.md#google-image-options), incluindo a política conservadora de 1K para Flash-Lite. Valores explícitos sem suporte falham antes do HTTP; modelos personalizados desconhecidos mantêm `Unknown` e a validação geral do provedor.

Um `AIService` próprio com definições confiáveis pode sobrescrever o hook protected `ResolveRequestCapabilities()`. O padrão é `AIModelCapabilities.Unknown`. Não estar no catálogo não torna uma implantação incompatível. `IAIService` não ganha membros obrigatórios; as consultas pertencem a `AIService` e seu builder.

Se o perfil de um provedor personalizado alterar indicadores de modo nativo, sobrescreva `ApplyCapabilityRequestProfile(AIRequestProfile)` e aplique com `SetExecutionSetting(...)` apenas os indicadores necessários ao resolvedor. O hook padrão não faz nada. O builder já capturou as configurações comuns do perfil; a consulta nunca chama `ApplyRequestProfile` nem `ApplyProviderSpecificRequestProfile`. Esse hook não deve validar, executar callbacks, serializar, reservar orçamento nem alterar o estado do serviço ou do chamador. As configurações temporárias são restauradas após a consulta, inclusive quando a sobrescrita lança uma exceção.

[Configuração de requisições](request-building.md) · [Opções de provedor e imagem](providers.md) · [Controle do Run](execution-api-transition.md)
