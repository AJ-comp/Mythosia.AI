# Funcionalidades por Provedor

> Os exemplos com `CreateRequest` exigem Mythosia.AI 8.0.0 / Abstractions 4.0.0. A versão 7.1 que introduziu Run e as opções comuns não inclui o builder. Pacotes anteriores podem usar as sobrecargas do serviço.

<a id="image-options-migration"></a>
## Migração para opções de imagem tipadas

Escolha qualidade e formato por enums e preenchimento automático, distinguindo pixels exatos de níveis de resolução. Isso evita erros de digitação e conversões silenciosas para outro tamanho.

Mudança incompatível para Mythosia.AI 8.0.0: `Quality`, `Background` e `OutputFormat` são enums; `Size` é `ImageSize`; a propriedade separada `AspectRatio` é removida. O padrão de `OutputFormat` passa a ser `ImageOutputFormat.Auto`. Os métodos de geração e edição continuam iguais.

**Before**

```text
var request = new ImageGenerationRequest
{
    Prompt = "A glass pavilion at sunrise",
    Quality = "max",
    Background = "transparent",
    OutputFormat = "png",
    Size = "1536x1024"
};

// Google / xAI
Size = "2K";
AspectRatio = "3:2";
```

**After**

```csharp
using Mythosia.AI.Models;
using Mythosia.AI.Models.Images;

var request = new ImageGenerationRequest
{
    Model = AIModels.OpenAI.GptImage2_5Sunburst,
    Prompt = "A glass pavilion at sunrise",
    Quality = ImageQuality.Max,
    Background = ImageBackground.Transparent,
    OutputFormat = ImageOutputFormat.Png,
    Size = ImageSize.Pixels(1536, 1024)
};

// Google / xAI
var presetRequest = new ImageGenerationRequest
{
    Prompt = "A glass pavilion at sunrise",
    Size = ImageSize.Preset(ImageResolution.TwoK, ImageAspectRatio.ThreeByTwo),
    OutputFormat = ImageOutputFormat.Auto
};
```

`Pixels(width, height)` solicita dimensões exatas. `Preset(resolution, aspectRatio)` indica nível e proporção; o provedor determina os pixels reais. Use `ImageSize.Auto` sem restrição de tamanho. Migre pixels para presets apenas quando dimensões aproximadas forem aceitáveis.

| Provider | `Size` | `OutputFormat` |
| --- | --- | --- |
| OpenAI | `ImageSize.Auto`, `Pixels(...)` | `Auto` → PNG, `Png`, `Jpeg`, `WebP` |
| Google | `ImageSize.Auto`, `Preset(...)` | `Auto`, `Jpeg` |
| xAI | `ImageSize.Auto`, `Preset(...)` | `Auto` |

Valores enum indefinidos e combinações não suportadas são rejeitados antes do HTTP. Nem todo modelo aceita todos os valores. Google aceita somente `ImageQuality.Auto`; xAI aceita `Auto`, `Low` e `Medium`.

Você pode reutilizar os buffers de entrada depois que `EditImagesAsync` retornar seu `Task`. A solicitação iniciada mantém seus próprios dados de imagem, incluindo os bytes da máscara do OpenAI; alterações posteriores nos arrays originais `ImageInput.Data` não modificam o upload.

Para evitar salvar uma saída interrompida como imagem concluída, a geração e a edição do Google exigem que todos os candidatos retornados terminem com `finishReason: STOP`. Se algum estiver bloqueado, incompleto ou sem esse estado final, toda a chamada lança `AIServiceException`. Dados base64 embutidos ou metadados MIME de imagem ausentes ou inválidos também fazem toda a chamada falhar; PNG não é presumido. Essas verificações não confirmam se os bytes do arquivo correspondem ao formato de imagem declarado.

## OpenAI (OpenAIService)

> O suporte ao GPT-6 Astra e às chamadas assíncronas de ferramentas está disponível a partir de `Mythosia.AI` 7.1.0, com tipos compartilhados em `Mythosia.AI.Abstractions` 3.1.0.

Uma consulta lenta não precisa interromper toda a resposta. Enquanto os dados do clima são carregados, por exemplo, o modelo pode explicar dicas gerais de viagem que não dependem do resultado.

`FunctionDefinition.AllowAsync = true` ou `FunctionBuilder.WithAsync()` permite habilitar chamadas assíncronas para GPT-6 Astra via Responses. O padrão é `false`; modelos sem suporte aguardam o resultado do mesmo handler. Veja exemplos e o ciclo de vida da solicitação no [guia de chamadas de função](function-calling.md).

### Nível de Esforço de Reasoning

GPT-6 Astra e GPT-5.1–5.6 permitem ajustar o esforço de raciocínio para equilibrar velocidade e profundidade:

```csharp
using Mythosia.AI.Models;

// GPT-6 Astra
service.ChangeModel(AIModels.OpenAI.Gpt6Astra);
service.WithGpt6Parameters(
    reasoningEffort: Gpt6Reasoning.Medium, // Auto, Low, Medium, High, XHigh, Max
    verbosity: Verbosity.Medium,
    reasoningSummary: ReasoningSummary.Auto,
    reasoningMode: Gpt6ReasoningMode.Standard);

// GPT-5.6: Sol é o modelo principal; Terra e Luna são opções mais econômicas.
service.ChangeModel(AIModels.OpenAI.Gpt5_6Sol);
service.WithGpt5_6Parameters(
    reasoningEffort: Gpt5_6Reasoning.Medium, // None, Low, Medium, High, XHigh, Max
    verbosity: Verbosity.Medium);            // Low, Medium, High

// Série GPT-5.4
service.ChangeModel(AIModels.OpenAI.Gpt5_4);
service.Gpt5_4ReasoningEffort = Gpt5_4Reasoning.High; // None, Low, Medium, High, XHigh

```

O GPT-6 Astra usa a API Responses por padrão; chamadas de função exigem essa API. `Auto` equivale ao padrão da biblioteca, `Medium`; `None` e `Minimal` não estão disponíveis. `AIRequestProfile.DisableReasoning = true` usa `Low` no modo `Standard` e omite o resumo do raciocínio. Selecione `Gpt6ReasoningMode.Pro` para executar o modo Pro com o mesmo ID de modelo `gpt-6-astra`.

Para tarefas comuns entre provedores, use [raciocínio e busca nativa](reasoning-and-search.md); as configurações específicas apresentadas a seguir continuam disponíveis.

### Texto para Fala

```csharp
byte[] audio = await service.GetSpeechAsync(
    inputText: "Olá, mundo!",
    voice: "alloy",
    model: "tts-1"
);

await File.WriteAllBytesAsync("output.mp3", audio);
```

### Fala para Texto (Transcrição)

```csharp
byte[] audioData = await File.ReadAllBytesAsync("gravacao.mp3");

string transcript = await service.TranscribeAudioAsync(
    audioData: audioData,
    fileName: "gravacao.mp3",
    language: "pt"  // opcional, ISO-639-1
);
```

`TranscribeAudioAsync` usa `gpt-transcribe`; sua assinatura pública permanece inalterada.

### Geração de Imagens

#### GPT Image 2.5

Escolha Flare para criar propostas visuais rapidamente, ou Sunburst quando uma revisão precisar seguir instruções de edição detalhadas com precisão. Ambos geram e editam imagens pelo `IImageGenerationService` existente; escolher um modelo de imagem não altera o modelo de chat.

| Modelo | Quando escolher |
| --- | --- |
| `AIModels.OpenAI.GptImage2_5Flare` | Geração cotidiana de imagens rápida e de alta qualidade. |
| `AIModels.OpenAI.GptImage2_5Sunburst` | Geração e edição em que a precisão das alterações é prioritária. |

Defina `ImageGenerationRequest.Model`, ou a propriedade herdada por `ImageEditRequest`. O padrão de OpenAI continua sendo `AIModels.OpenAI.GptImage2`. Os aliases são `gpt-image-2.5-flare` e `gpt-image-2.5-sunburst`; para fixar as versões de 8 de setembro de 2026, use `GptImage2_5Flare_260908` ou `GptImage2_5Sunburst_260908` (IDs terminados em `-2026-09-08`).

Crie um rascunho com Flare:

```csharp
using Mythosia.AI.Models;
using Mythosia.AI.Models.Images;
using Mythosia.AI.Services;
using Mythosia.AI.Services.OpenAI;

IImageGenerationService images = new OpenAIService(apiKey, httpClient);
var generated = await images.GenerateImagesAsync(new ImageGenerationRequest
{
    Model = AIModels.OpenAI.GptImage2_5Flare,
    Prompt = "Um pavilhão de vidro ao nascer do sol, ilustração conceitual de arquitetura",
    Size = ImageSize.Pixels(1024, 1024),
    Quality = ImageQuality.Low,
    OutputFormat = ImageOutputFormat.Png
});

await File.WriteAllBytesAsync("pavilion.png", generated.Images[0].Data);
```

Em seguida, edite a imagem gerada com Sunburst:

```csharp
var edited = await images.EditImagesAsync(new ImageEditRequest
{
    Model = AIModels.OpenAI.GptImage2_5Sunburst,
    Prompt = "Mantenha o desenho do pavilhão, remova o entorno e deixe o fundo transparente.",
    InputImages = new[]
    {
        new ImageInput(generated.Images[0].Data, "image/png", "pavilion.png")
    },
    Size = ImageSize.Pixels(1024, 1024),
    Quality = ImageQuality.XHigh,
    Background = ImageBackground.Transparent,
    OutputFormat = ImageOutputFormat.Png
});

await File.WriteAllBytesAsync("pavilion-cutout.png", edited.Images[0].Data);
```

Nesses dois modelos e suas versões fixadas, `Quality` aceita `Auto`, `Low`, `Medium`, `High`, `XHigh` e `Max`. Use qualidade baixa para rascunhos e compare níveis superiores para os materiais finais. `OutputFormat` aceita `Auto` / `Png`, `Jpeg` ou `WebP`; `OutputCompression` vai de 0 a 100 somente para JPEG/WebP. Fundo `Transparent` exige PNG/WebP. `Count` vai de 1 a 10.

`Size` usa `ImageSize.Auto` ou `ImageSize.Pixels(width, height)`: dimensões múltiplas de 16, proporção 1:3–3:1, máximo 3840 pixels por lado e área 655360–8294400 pixels. Tamanhos acima de 2560×1440 são experimentais. OpenAI rejeita `Preset`.

A edição aceita 1–16 referências JPEG/PNG/WebP não vazias, cada uma com menos de 50 MiB. A máscara opcional deve ser PNG/WebP, ter menos de 50 MiB, coincidir com o formato e as dimensões da primeira referência e conter canal alfa. A biblioteca valida MIME e comprimento em bytes; o provedor verifica dimensões e alfa.

Estes exemplos usam os caminhos existentes da Image API: retorno de bytes e edição multipart. Esta integração não expõe ferramentas Responses `image_generation`, streaming de imagens parciais nem `input_fidelity`. Leia `GeneratedImage.Data` e `MediaType` no resultado.

Consulte o [guia oficial de imagens](https://developers.openai.com/api/docs/guides/image-generation) e as páginas de [Sunburst](https://developers.openai.com/api/docs/models/gpt-image-2.5-sunburst) e [Flare](https://developers.openai.com/api/docs/models/gpt-image-2.5-flare).

---

## Anthropic (AnthropicService)

[Claude Fable 5.1](fable-5-1.md) adiciona atualizações de progresso, instruções por turno e diagnósticos de vinculação do raciocínio a partir de `Mythosia.AI` 8.0.0 / `Mythosia.AI.Abstractions` 4.0.0. Mythos 5.1 exige convite. Ambos rejeitam a seleção forçada de ferramentas.

### Contagem de Tokens (API Nativa)

A implementação da Anthropic chama o endpoint oficial `messages/count_tokens`, retornando contagens **exatas** de tokens:

```csharp
uint tokens = await service.GetInputTokenCountAsync("Seu prompt aqui");
uint total = await service.GetInputTokenCountAsync();
```

---

## Google (GoogleAIService)

Para revisar documentos longos e realizar tarefas com várias rodadas de ferramentas, escolha Gemini 3.7 Flash ou 3.8 Flash pelo adaptador Google existente. O suporte começa em `Mythosia.AI` 8.0.0 / `Mythosia.AI.Abstractions` 4.0.0; o padrão continua sendo Gemini 3.6 Flash.

### Nível de Pensamento

```csharp
using Mythosia.AI.Extensions;
using Mythosia.AI.Models;
using Mythosia.AI.Models.Enums;
using Mythosia.AI.Services.Google;

var gemini = new GoogleAIService(apiKey, httpClient);
gemini.ChangeModel(AIModels.Google.Gemini3_8Flash);
// Gemini 3.7: AIModels.Google.Gemini3_7Flash
gemini.ThinkingLevel = GeminiThinkingLevel.Low;

string review = await gemini
    .CreateRequest("Compare implantações graduais e blue-green, incluindo os riscos de reversão.")
    .WithReasoning(ReasoningLevel.High)
    .GetCompletionAsync();
```

Use `Low` para uma primeira análise leve e `High` para uma revisão exigente; mais raciocínio pode aumentar a latência e o uso de tokens. Ambos aceitam `Low`, `Medium` e `High`, mas não `Minimal` nem `None`. `GeminiThinkingLevel.Auto` omite a configuração; o padrão do provedor para 3.8 é `Medium`. `ThinkingLevel` define a base do serviço e `WithReasoning(...)` a substitui para uma solicitação lógica. O adaptador omite `temperature`, `topP` e `topK` nesses modelos. Os limites do provedor são 1.048.576 tokens de entrada e 65.536 de saída. [Gemini 3.7 Flash](https://ai.google.dev/gemini-api/docs/models/gemini-3.7-flash), [Gemini 3.8 Flash](https://ai.google.dev/gemini-api/docs/models/gemini-3.8-flash).

---

## xAI (XAIService)

### Escolher o esforço conforme a tarefa

Use menos esforço para um primeiro rascunho rápido e mais raciocínio para verificações difíceis em que a qualidade importa mais que o tempo de resposta. Selecione Grok 4.6 explicitamente para usar o nível adicional `XHigh`.

```csharp
using Mythosia.AI.Extensions;
using Mythosia.AI.Models;
using Mythosia.AI.Services.xAI;

var grok = new XAIService(apiKey, httpClient);
grok.ChangeModel(AIModels.xAI.Grok4_6);
grok.WithGrokReasoning(GrokReasoning.Low);

string review = await grok
    .CreateRequest("Compare implantações gradual e azul-verde, incluindo a recuperação de falhas.")
    .WithReasoning(ReasoningLevel.XHigh)
    .GetCompletionAsync();
```

Grok 4.6 aceita `Low`, `Medium`, `High` e `XHigh` (`GrokReasoning.XHigh`). `Auto` omite `reasoning_effort` e mantém o padrão `High` do provedor; `None` não desativa o raciocínio. Mais esforço pode aumentar a latência e o consumo de tokens. Por compatibilidade, `XAIService` mantém Grok 4.5 como padrão. A versão 4.5 aceita de `Low` a `High`, e a 4.3 de `None` a `High`; o adaptador rejeita `XHigh` nesses modelos anteriores antes do envio.

`WithGrokReasoning(...)` e o método existente `WithGrokParameters(...)` definem a configuração base do serviço. No Grok 4.6, o método comum `WithReasoning(...)` a substitui para uma única solicitação lógica, incluindo rodadas de ferramentas e reparos de saída estruturada, e depois a restaura. Os perfis internos `DisableReasoning` usam `Low` nesse modelo que sempre raciocina. Atualizações que preservam o cache e busca hospedada na web/em arquivos não estão integradas para xAI por essas opções comuns.

Grok 4.6 pode retornar resumos de raciocínio do provedor como `StreamingContentType.Reasoning` quando a observação habilita `new StreamOptions().WithReasoning()`. Os resumos são opcionais e não representam o raciocínio interno completo. As mesmas opções funcionam com Run; mudar a exibição do fluxo não altera o esforço solicitado.

[Grok 4.6](https://docs.x.ai/developers/grok-4-6) · [reasoning_effort](https://docs.x.ai/developers/model-capabilities/text/reasoning)

### Grok Imagine Image 2.0

Use a geração para transformar uma descrição de produto em um rascunho visual, ou a edição para combinar o sujeito e o cenário de fotos de referência. `XAIService` oferece as duas operações pelo mesmo `IImageGenerationService` de OpenAI e Google, a partir de `Mythosia.AI` 8.0.0 / `Mythosia.AI.Abstractions` 4.0.0.

O modelo de imagem padrão e independente é `AIModels.xAI.GrokImagineImage2_0` (`grok-imagine-image-2.0`). As solicitações de imagem não alteram o modelo de chat nem são adicionadas à conversa.

```csharp
using Mythosia.AI.Models;
using Mythosia.AI.Models.Images;
using Mythosia.AI.Services;
using Mythosia.AI.Services.xAI;

IImageGenerationService images = new XAIService(apiKey, httpClient);
var generated = await images.GenerateImagesAsync(new ImageGenerationRequest
{
    Model = AIModels.xAI.GrokImagineImage2_0,
    Prompt = "Um pavilhão de vidro ao nascer do sol, composição panorâmica",
    Size = ImageSize.Preset(ImageResolution.OneK, ImageAspectRatio.SixteenByNine),
    OutputFormat = ImageOutputFormat.Auto
});

var image = generated.Images[0];
var extension = image.MediaType switch
{
    "image/jpeg" => ".jpg",
    "image/png" => ".png",
    "image/webp" => ".webp",
    _ => throw new NotSupportedException(image.MediaType)
};
await File.WriteAllBytesAsync("pavilion" + extension, image.Data);
```

Para editar, passe os bytes das imagens na ordem mencionada pelo prompt:

```csharp
var edited = await images.EditImagesAsync(new ImageEditRequest
{
    Prompt = "Coloque o sujeito da imagem 1 no cenário da imagem 2.",
    InputImages = new[]
    {
        new ImageInput(await File.ReadAllBytesAsync("subject.png"), "image/png", "subject.png"),
        new ImageInput(await File.ReadAllBytesAsync("scene.jpg"), "image/jpeg", "scene.jpg")
    },
    Size = ImageSize.Preset(ImageResolution.OneK),
    OutputFormat = ImageOutputFormat.Auto
});

var imageEdited = edited.Images[0];
var extensionEdited = imageEdited.MediaType switch
{
    "image/jpeg" => ".jpg",
    "image/png" => ".png",
    "image/webp" => ".webp",
    _ => throw new NotSupportedException(imageEdited.MediaType)
};
await File.WriteAllBytesAsync("combined" + extensionEdited, imageEdited.Data);
```

Cada `GeneratedImage.Data` contém os bytes decodificados; escolha a extensão conforme `MediaType`. O adaptador solicita uma resposta base64 incorporada e não baixa URLs de imagens hospedadas pelo provedor. `Count` aceita de 1 a 10 resultados; a edição aceita de 1 a 5 referências JPEG, PNG ou WebP.

Para xAI, use `ImageSize.Auto` ou `ImageSize.Preset(ImageResolution.OneK, ImageAspectRatio.SixteenByNine)`. Resoluções: `Auto`, `OneK`, `TwoK`, com proporções suportadas pelo modelo. `Pixels(...)` é rejeitado porque dimensões exatas não podem ser solicitadas.

xAI aceita somente o novo padrão comum `ImageOutputFormat.Auto`. Sem seleção de codec, `Jpeg`, `Png` e `WebP` explícitos são rejeitados antes do envio. Escolha a extensão por `GeneratedImage.MediaType`; a biblioteca não transcodifica. Qualidade: `ImageQuality.Auto`, `Low`, `Medium`; fundo: somente `ImageBackground.Auto`. Compressão explícita e `Mask` separada não são suportadas.

Google aceita `ImageSize.Auto` ou `Preset` com `ImageResolution.Auto`, `FiveTwelve`, `OneK`, `TwoK`, `FourK`, conforme o modelo. Formatos: `ImageOutputFormat.Auto` ou `Jpeg`; rejeita `Png`/`WebP`. Google e xAI rejeitam `Pixels`; OpenAI aceita `Auto`/`Pixels` e rejeita `Preset`. Veja a [migração](#image-options-migration).

[Grok Imagine Image 2.0](https://docs.x.ai/developers/models/grok-imagine-image-2.0) · [Image API](https://docs.x.ai/developers/rest-api-reference/inference/images)

---

## DeepSeek (DeepSeekService)

Use DeepSeek Flash para passar de uma resposta rápida a uma revisão profunda ou interpretar gráficos e capturas de tela. `AIModels.DeepSeek.Flash` (`deepseek-flash`) seleciona V4.1 Flash, lançado em 10 de setembro de 2026 com visão nativa. As APIs de completion, streaming, Run, funções e RAG continuam disponíveis desde `Mythosia.AI` 8.0.0 / `Mythosia.AI.Abstractions` 4.0.0.

```csharp
using Mythosia.AI.Extensions;
using Mythosia.AI.Models;
using Mythosia.AI.Models.Streaming;
using Mythosia.AI.Services.DeepSeek;

var deepseek = new DeepSeekService(apiKey, httpClient);
deepseek.ChangeModel(AIModels.DeepSeek.Flash);
deepseek.WithDeepSeekReasoning(DeepSeekReasoning.Low);

string draft = await deepseek.GetCompletionAsync("Compare implantações progressivas e blue-green, incluindo riscos de reversão.");

await using var run = await deepseek
    .CreateRequest("Revise as premissas dessa comparação.")
    .WithReasoning(ReasoningLevel.High)
    .StartRunAsync(
        options: new StreamOptions().WithReasoning());
await foreach (var item in run.StreamAsync())
{
    if (item.Type == StreamingContentType.Reasoning)
        Console.Write(item.Content);
}
string review = (await run.Result).Text;
```

`ThinkingEnabled` continua `false` por padrão. `WithDeepSeekReasoning(...)` ativa o raciocínio e define `ReasoningEffort` persistente (`Auto`, `Low`, `High`, `Max`); seu `Auto` omite o esforço e usa o padrão do provedor `High`. O `WithReasoning(...)` comum vale para uma solicitação lógica e suas rodadas: `None` desativa, `Minimal`/`Low` corresponde a `Low`, `Medium`/`High`/`XHigh` a `High`, e `Max` a `Max`. O `Auto` comum mantém a configuração. Mais esforço pode aumentar latência e tokens. Alterar apenas `ReasoningEffort` não ativa o raciocínio.

Registre funções locais com `WithFunction(...)` para consultar dados ou agir pelo seu código. Ferramentas funcionam com ou sem raciocínio; com raciocínio, a seleção forçada/obrigatória é rejeitada, portanto use seleção automática. O adaptador preserva `reasoning_content` e IDs para as próximas rodadas. Run e streaming expõem `StreamingContentType.Reasoning` com `StreamOptions.WithReasoning()`; observar não ativa o raciocínio. O uso inclui cache e raciocínio quando informados pelo provedor. A recuperação automática do contexto usa o loop comum de streaming. Se as ferramentas exigirem o histórico nativo de raciocínio, a compactação é bloqueada para preservá-lo e o erro de excesso continua visível.

Envie gráficos ou capturas pelos tipos de mensagem existentes:

```csharp
using Mythosia.AI.Models.Messages;

var message = new Message(ActorRole.User, new List<MessageContent>
{
    new TextContent("Explique a tendência do gráfico e identifique rótulos pouco claros."),
    new ImageContent(await File.ReadAllBytesAsync("chart.png"), "image/png")
});
string description = await deepseek.GetCompletionAsync(message);
```

`ImageContent` aceita bytes JPEG, PNG, GIF ou WebP, ou uma URL HTTP(S) pública recuperada pelo provedor. O exemplo usa uma mensagem de usuário. A API atual também aceita imagens em mensagens de ferramentas, mas handlers registrados continuam retornando texto pelo contrato comum. Uma mensagem manual de imagem `ActorRole.Function` precisa do ID correspondente em `MessageMetadataKeys.FunctionId` (`tool_call_id` transmitido). Consulte os limites de tamanho e totais no guia de visão atual. Não são integrados `file_id`, Files API ou geração de imagens.

O provedor anuncia 1M de contexto e até 384K (`393216`) tokens de saída; o orçamento padrão permanece 8.000. No raciocínio, temperature/penalty são omitidos e `top_p` é pelo menos 0,95; sem raciocínio, `top_p` é omitido. O adaptador usa Chat Completions. Responses, busca hospedada, `CachePreservation.Required`, ferramentas assíncronas nativas e `SteerAsync` não estão integrados. RAG local e rodadas comuns continuam disponíveis.

`V4Flash`, `Chat` e `Reasoner` permanecem constantes obsolete com aviso e IDs originais. O provedor redireciona temporariamente o alias aposentado `deepseek-v4-flash` a V4.1 Flash; a biblioteca não reescreve a constante. Escolha `Flash` em código novo. `UseReasonerModel()` seleciona Flash com raciocínio `High`.

[DeepSeek V4.1 Flash](https://api-docs.deepseek.com/updates/) · [Thinking](https://api-docs.deepseek.com/guides/thinking_mode/) · [Vision](https://api-docs.deepseek.com/guides/vision/) · [Tools](https://api-docs.deepseek.com/guides/tool_calls/) · [Limits](https://api-docs.deepseek.com/quick_start/pricing/)

---

## Perplexity (PerplexityService)

Use o Perplexity quando a resposta precisar de informações recentes e fontes que o leitor possa verificar. `PerplexityService` chama a Agent API; a pesquisa e os embeddings independentes permitem montar a recuperação de documentos com o modelo de resposta que preferir.

```csharp
using Mythosia.AI.Models;
using Mythosia.AI.Services.Perplexity;

var service = new PerplexityService(apiKey, httpClient);
service.ChangeModel(AIModels.Perplexity.Sonar);

await using var run = await service.StartRunAsync("Compare os métodos recentes de reciclagem de baterias e cite as fontes.");
Console.WriteLine((await run.Result).Text);
foreach (var source in run.Citations)
    Console.WriteLine(source.Url);
```

O [guia do Perplexity](perplexity.md) explica os presets de pesquisa, as funções locais, as ferramentas hospedadas e as tarefas longas em segundo plano. As APIs usuais de completion, streaming, Run e citações continuam disponíveis.

Esta versão muda o serviço para `/v1/agent`. `AIModels.Perplexity.Sonar` agora seleciona `perplexity/sonar`. O provedor anunciou o encerramento dos endpoints Sonar antigos para 27 de setembro de 2026; as integrações existentes precisam migrar. [Perplexity](https://community.perplexity.ai/t/sonar-is-moving-to-the-agent-api/5802)

---

## Alibaba / Qwen (QwenService)

Instale o pacote separado:

```bash
dotnet add package Mythosia.AI.Providers.Alibaba
```

```csharp
using Mythosia.AI.Providers.Alibaba;

var service = new QwenService(apiKey, http)
{
    Model = AlibabaModels.QwenMax
};
```

Modelos disponíveis: `QwenMax`, `QwenPlus`, `QwenTurbo`, `Qwen3` e variantes.

[Criar opções de modelo com definições compartilhadas](model-capabilities.md).
