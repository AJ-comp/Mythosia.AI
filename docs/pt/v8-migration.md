# Migrar para Mythosia.AI 8

Use esta versão para isolar configurações de pedidos, interromper trabalho em curso e guardar respostas com consumo e fontes. Ela reúne seis mudanças de arquitetura, atualizações de provedores e modelos e correções de três revisões adversariais em uma versão principal.

Atualize juntos apenas os pacotes usados e recompile seus consumidores. Mythosia.AI inclui a dependência Abstractions correspondente. A tabela relaciona a base publicada às versões compatíveis desta versão.

| Pacote | Base publicada | Versão alvo |
| --- | --- | --- |
| `Mythosia.AI` | 7.1.0 | [8.0.0](../../src/core/Mythosia.AI/RELEASE_NOTES.md#v800) |
| `Mythosia.AI.Abstractions` | 3.1.0 | [4.0.0](../../src/core/Mythosia.AI.Abstractions/RELEASE_NOTES.md#v400) |
| `Mythosia.AI.Providers.Alibaba` | 2.0.1 | [3.0.0](../../src/core/Mythosia.AI.Providers.Alibaba/RELEASE_NOTES.md#v300) |
| `Mythosia.AI.Rag` | 7.6.0 | [8.0.0](../../src/rag/Mythosia.AI.Rag/RELEASE_NOTES.md#v800) |
| `Mythosia.AI.Mcp` | 0.0.1-preview | [0.1.0-preview](../../src/integrations/Mythosia.AI.Mcp/RELEASE_NOTES.md#v010-preview) |
| `Mythosia.AI.Serving.Vllm` | 1.0.0-preview | [1.0.0](../../src/serving/Mythosia.AI.Serving.Vllm/RELEASE_NOTES.md#v100) |

`Mythosia.AI.Serving.Vllm` passa de `1.0.0-preview` para a versão estável `1.0.0`. Mantém as APIs existentes de modelos, estado, versão do servidor e métricas como pacote independente, sem dependência do pacote AI principal.

## Escolher pela necessidade

| Necessidade | Mudança e migração |
| --- | --- |
| Detectar erros em opções de imagem antes do envio | Troque strings por `ImageQuality`, `ImageBackground`, `ImageOutputFormat` e `ImageSize.Pixels(...)` / `ImageSize.Preset(...)`. O suporte ainda varia por provedor. |
| Preparar pedidos sem alterar as configurações uns dos outros | Comece com `CreateRequest(...)` e guarde o novo builder retornado por cada `With...`. Setters do serviço continuam alterando padrões compartilhados. |
| Retornar dados de ferramentas assíncronas | Métodos registrados por atributos retornam objetos em `Task<T>` / `ValueTask<T>` e recebem `CancellationToken` injetado. Exceções são falhas; handlers de string continuam suportados. |
| Parar de esperar ao cancelar | Passe `cancellationToken` a completion, Run e entradas RAG compatíveis. Ele interrompe trabalho local e ferramentas cooperativas; não garante cancelamento remoto nem desfaz ações externas concluídas. |
| Guardar resposta, consumo e fontes juntos | `AIRun.Result` retorna `Task<AIRunResult>`. Use `(await run.Result).Text` para a string. O resultado é coletado mesmo sem ler o stream. |
| Exibir controles adequados ao modelo | Use `request.GetCapabilities()` ou consultas do serviço/imagem. `Supported`, `Unsupported` e `Unknown` descrevem conhecimento local da biblioteca, não acesso real à conta. |

## Atualizar chamadas e provedores próprios

Tipos de imagem, `AIRun.Result` e assinaturas de cancelamento alteradas quebram contratos. Implementações próprias de `IAIService` e overrides de sobrecargas públicas alteradas devem adicionar e encaminhar o token. O override do provedor `GetCompletionAsync(Message)` mantém a assinatura e encaminha `RequestCancellationToken`. Um `AIRun` próprio deve retornar `AIRunResult`. GetCompletionAsync mantém string; completion tipada e `StructuredStreamRun<T>.Result` mantêm resultados tipados. Os StreamAsync de serviço/RAG que recebem entrada continuam públicos na v8. RunAgentAsync e RunAgentStreamAsync preservam compatibilidade e avisos obsolete. Use Run em novos fluxos de progresso, cancelamento e instruções durante a execução quando suportadas.

## Um pedido, resultado e progresso opcional

```csharp
using Mythosia.AI.Builders;
using Mythosia.AI.Models.Runs;
using Mythosia.AI.Models.Capabilities;

AIRequestBuilder request = service.CreateRequest("Summarize this document.");
var capabilities = request.GetCapabilities();
if (capabilities.Temperature == CapabilitySupport.Supported)
    request = request.WithTemperature(0.2f);

await using var run = await request
    .StartRunAsync(
        onText: text => Console.Write(text),
        cancellationToken: cancellationToken);

AIRunResult result = await run.Result;
Console.WriteLine(result.Text);
```

OpenAI usa pixels; Google e xAI usam `ImageSize.Preset(...)`. Mude Auto apenas se houver suporte a formato explícito; salve conforme o `GeneratedImage.MediaType` retornado.

```csharp
using Mythosia.AI.Models.Images;

var imageRequest = new ImageGenerationRequest
{
    Prompt = "A simple architectural study",
    Quality = ImageQuality.Auto,
    Background = ImageBackground.Auto,
    OutputFormat = ImageOutputFormat.Auto,
    Size = ImageSize.Pixels(1536, 1024)
};
var generated = await images.GenerateImagesAsync(imageRequest, cancellationToken);
```

Uma ferramenta registrada pode retornar o objeto da aplicação como abaixo. O handler de baixo nível `HandlerWithCancellation` continua retornando `Task<string>`; não exige um novo wrapper de objetos.

```csharp
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Mythosia.AI.Attributes;

public sealed record FileResult(string Path, string Text);

public sealed class FileTools
{
    [AiFunction("read_file", "Read a text file")]
    public async Task<FileResult> ReadFileAsync(
        string path, CancellationToken cancellationToken)
    {
        string text = await File.ReadAllTextAsync(path, cancellationToken);
        return new FileResult(path, text);
    }
}
```

## Provedores e validação

Também inclui as integrações preparadas de Fable 5.1, Gemini 3.7/3.8 Flash, Grok 4.6, DeepSeek Flash e Perplexity Agent, além de geração/edição de imagens comum a OpenAI, Google e xAI. Constantes removidas e o novo endpoint Perplexity podem exigir alterações nas chamadas; veja o guia de provedores e as notas dos pacotes.

Configure pesquisa com `PerplexityAgentOptions`. Os testes Profile, Custom Skill e Connector estão preparados, mas exigem recursos registrados. MCP permanece preview. Após iniciar a liberação, chamadas falham com `ObjectDisposedException`; após parar o leitor, novas chamadas falham com `McpException` em vez de esperar indefinidamente.

Três revisões adversariais reforçaram cópias, resultados de ferramentas, cancelamento/limpeza, contagem de tokens, validação de respostas e ciclo MCP. A terceira acrescentou 43 casos de regressão; todos os 2.703 testes passaram. A documentação abrange 13 idiomas. Não houve chamadas reais às APIs nessa revisão; testes unitários não comprovam todas as integrações dependentes de recursos de conta.

## Guias detalhados

- [CreateRequest / AIRequestBuilder](request-building.md)
- [AIRun / AIRunResult](execution-api-transition.md)
- [ImageQuality / ImageSize](providers.md#image-options-migration)
- [CancellationToken](completions.md#completion-cancellation)
- [Task<T> / ValueTask<T> / HandlerWithCancellation](function-calling.md#tool-execution-contract)
- [GetCapabilities](model-capabilities.md)
- [PerplexityAgentOptions](perplexity.md)
