<div align="center">

🌐 [English](../../README.md) · [한국어](../ko/README.md) · [日本語](../ja/README.md) · [Français](../fr/README.md) · [Deutsch](../de/README.md) · [Русский](../ru/README.md) · [Українська](../uk/README.md) · [简体中文](../zh-Hans/README.md) · [繁體中文](../zh-Hant/README.md) · [Tiếng Việt](../vi/README.md) · [ภาษาไทย](../th/README.md) · [Português](README.md) · [Español](../es/README.md)

<br>

[![OPEN SOURCE](https://img.shields.io/badge/OPEN%20SOURCE%20·%20.NET%20·%20NUGET-111827?style=flat-square&labelColor=111827&color=111827)](https://github.com/AJ-comp/Mythosia.AI)

<img width="694" height="181" alt="title_60" src="https://github.com/user-attachments/assets/57fd8c63-5b9b-46f6-be30-274354808c0d" />

### Biblioteca .NET modular para construir aplicações de IA inteligentes

**Troque de provider, conecte RAG, carregue documentos — tudo por uma API unificada.**

<br>

[![NuGet](https://img.shields.io/nuget/v/Mythosia.AI.svg?style=for-the-badge&logo=nuget&label=NuGet&color=512BD4)](https://www.nuget.org/packages/Mythosia.AI)
[![Downloads](https://img.shields.io/nuget/dt/Mythosia.AI.svg?style=for-the-badge&logo=nuget&color=512BD4)](https://www.nuget.org/packages/Mythosia.AI)
[![Docs](https://img.shields.io/badge/Docs-GitHub%20Pages-0ea5e9?style=for-the-badge&logo=readthedocs&logoColor=white)](https://aj-comp.github.io/Mythosia.AI/)
[![.NET](https://img.shields.io/badge/.NET-Standard%202.1-6d28d9?style=for-the-badge&logo=dotnet&logoColor=white)](https://dotnet.microsoft.com)

<br>

**[📖 Primeiros Passos](https://aj-comp.github.io/Mythosia.AI/)** &nbsp;·&nbsp; **[Referência de API](https://aj-comp.github.io/Mythosia.AI/api/)** &nbsp;·&nbsp; **[GitHub ↗](https://github.com/AJ-comp/Mythosia.AI)**

<br>

</div>

## Demo / Experimente (Chat UI)

Experimente modelos e pesquisa de documentos no Playground antes de escrever o código de integração.

Esta demonstração gravada na interface atual do Playground mostra os modelos, a troca de idioma e as configurações de documentos e do pipeline RAG. O vídeo inclui legendas em inglês.

https://github.com/user-attachments/assets/4cf90210-b000-41be-8317-a467e93e7504

### Executar o exemplo

Inicie o **`Mythosia.AI.Samples.ChatUi`** na sua máquina:

```bash
# a partir do diretório raiz do repositório
dotnet run --project apps/Mythosia.AI.Samples.ChatUi
```

<details>
<summary>Controles e idiomas do Playground</summary>

Pesquise modelos por nome ou fornecedor e ajuste a solicitação à esquerda, converse no centro e confira as informações de processamento no Inspector à direita antes de integrar um modelo ao aplicativo. Use Stop para parar de esperar pela resposta; as opções de velocidade só podem ser selecionadas para modelos e endpoints compatíveis, e Fast pode ter custo adicional. Em telas menores, Models e Inspector abrem como painéis deslizantes; veja o [guia do Chat UI](https://github.com/AJ-comp/Mythosia.AI/blob/main/apps/Mythosia.AI.Samples.ChatUi/README.md) para execução local, documentos e configurações do pipeline.

O painel Pipeline permite configurar chaves, dimensões e tempo limite para Voyage Context 4, Gemini Embedding 2 e embeddings contextuais do Perplexity. Documents mostra as contagens de trechos e vetores e permite cancelar a indexação. As configurações salvas, a reconexão do banco e os exemplos de código usam essa configuração. Reindexe ao mudar o modelo ou as dimensões.

Use o seletor de idioma no cabeçalho para alternar entre 13 idiomas sem perder os dados inseridos ou as configurações. Os sete fornecedores aparecem como grupos recolhidos; expanda um grupo ou pesquise um modelo.

</details>

## Por que Mythosia.AI?

- **Trocar de fornecedor de IA com uma única API** para chat, streaming, chamadas de ferramentas e respostas estruturadas.
- **Criar respostas com base nos seus documentos** usando carregadores, embeddings, recuperação e reordenação.
- **Manter as configurações de cada solicitação independentes** e controlar o trabalho em andamento pela API Run comum.
- **Escolher os pacotes necessários**, desde a biblioteca principal até integrações opcionais de RAG e armazenamento vetorial.

## Qual pacote instalar?

```
dotnet add package Mythosia.AI                    # comece por aqui (só este é suficiente)
dotnet add package Mythosia.AI.Rag                # opcional: quando precisar de RAG
dotnet add package Mythosia.VectorDb.Postgres     # opcional: vector store para produção
```

| Passo | Pacote | Quando |
| :--: | --- | --- |
| **1** | **`Mythosia.AI`** | **Comece aqui** — geração de texto, streaming, function calling, structured output (OpenAI / Claude / Gemini / Grok / DeepSeek / Perplexity) |
| **2** | **`Mythosia.AI.Rag`** | Quando precisar de RAG — chunking, embedding, hybrid search, reranking, InMemory store, document loaders (Word / Excel / PowerPoint / PDF) |
| **3** | **`Mythosia.VectorDb.Postgres`** / **`Qdrant`** / **`Pinecone`** | Quando precisar de vector store de produção em vez de InMemory — escolha um |

Prepare configurações independentes com `CreateRequest(...).WithTemperature(...).GetCompletionAsync()`. O [guia de solicitações](request-building.md) explica Before/After, Run, perfis e limites das conversas compartilhadas.

Para pedidos sensíveis ao tempo de espera, escolha a [velocidade de processamento](request-building.md#inference-speed). `WithSpeed` mantém modelo e esforço; `Processing` informa o modo aplicado. Fast é pago nas combinações suportadas.

## Início Rápido

### Geração de texto básica

```csharp
using Mythosia.AI.Services.OpenAI;

var service = new OpenAIService(apiKey, httpClient);
var response = await service.GetCompletionAsync("Olá!");
```

### Streaming

```csharp
await using var run = await service.StartRunAsync(
    "Me conte uma história",
    onText: text => Console.Write(text));
string answer = (await run.Result).Text;
```

### Streaming com raciocínio

OpenAI, Claude, Gemini, Grok e DeepSeek Flash expõem o raciocínio do provedor pelo mesmo padrão de streaming. Ative-o no serviço ou solicitação e observe com `StreamOptions.WithReasoning()`:

```csharp
await using var run = await service.StartRunAsync(
    message, options: new StreamOptions().WithReasoning());
await foreach (var content in run.StreamAsync())
{
    if (content.Type == StreamingContentType.Reasoning)
        Console.Write($"[Raciocínio] {content.Content}");
    else if (content.Type == StreamingContentType.Text)
        Console.Write(content.Content);
}
```

### Chamadas de funções

```csharp
using Mythosia.AI.Extensions;
using Mythosia.AI.Services.OpenAI;

var service = new OpenAIService(apiKey, httpClient)
    .WithFunction(
        "get_weather",
        "Obter informações climáticas atuais para um local",
        ("location", "Nome da cidade e país", required: true),
        (string location) => $"O clima em {location} está ensolarado, 28°C"
    );

var response = await service.GetCompletionAsync("Como está o tempo em São Paulo?");
```

As chamadas retornadas na mesma resposta do modelo são executadas sequencialmente por padrão. Quando as funções registradas forem independentes, você pode ativar a execução paralela com um limite de concorrência:

```csharp
using Mythosia.AI.Models.Functions;

service.DefaultPolicy = new FunctionCallingPolicy
{
    ExecutionMode = FunctionExecutionMode.Parallel,
    MaxConcurrency = 3
};
```

Os resultados de um lote comum são enviados ao modelo na ordem original das chamadas. O cancelamento ignora as chamadas que ainda não começaram e fornece os resultados de cancelamento correspondentes. As ferramentas já iniciadas recebem o token quando há suporte; a execução delas é aguardada para manter cada chamada junto do seu resultado no histórico. `FunctionCallingPolicy.TimeoutSeconds` abrange todo o ciclo de rodadas de streaming, incluindo cabeçalhos da resposta e o corpo SSE, sem reiniciar entre rodadas de ferramentas. A expiração gera `AIServiceException`; o cancelamento solicitado pelo chamador continua sendo uma `OperationCanceledException` associada ao token dele.

Uma consulta lenta não precisa interromper toda a resposta. Enquanto os dados do clima são carregados, por exemplo, o modelo pode explicar dicas gerais de viagem que não dependem do resultado.

`FunctionDefinition.AllowAsync = true` ou `FunctionBuilder.WithAsync()` permite habilitar chamadas assíncronas para GPT-6 Astra / Sol / Luna via Responses. O padrão é `false`; modelos sem suporte aguardam o resultado do mesmo handler. Veja exemplos e o ciclo de vida da solicitação no [guia de chamadas de função](function-calling.md).

Os modelos sem suporte não recebem essa opção de API. Esse recurso é separado dos manipuladores C# `async` e da execução paralela deles. Veja as [chamadas assíncronas de ferramentas](function-calling.md#async-tool-calling) para exemplos e o ciclo de vida da solicitação.

### Geração e edição de imagens

Crie rascunhos visuais a partir de texto ou edite imagens existentes com uma capacidade opcional compartilhada por OpenAI, Google e xAI. O modelo de imagem é independente do modelo de chat selecionado:

```csharp
using Mythosia.AI.Models.Images;
using Mythosia.AI.Services;
using Mythosia.AI.Services.OpenAI;

IImageGenerationService images = new OpenAIService(apiKey, httpClient);
var generated = await images.GenerateImagesAsync(new ImageGenerationRequest
{
    Prompt = "Um pavilhão de vidro ao nascer do sol",
    Size = ImageSize.Pixels(1024, 1024),
    OutputFormat = ImageOutputFormat.Png
});

await File.WriteAllBytesAsync("pavilion.png", generated.Images[0].Data);
```

Consulte o [guia de fornecedores](providers.md#image-generation) para geração e edição, ou as [opções de imagem tipadas e a migração](providers.md#image-options-migration) para essa mudança principal de API. xAI usa `ImageOutputFormat.Auto`; escolha a extensão do arquivo com base em `GeneratedImage.MediaType`.

Para escolher tamanhos válidos ao gerar ou editar imagens, consulte as [opções de imagem do Google por modelo](providers.md#google-image-options). Flash aceita 512/1K/2K/4K, Flash-Lite atualmente 1K e Pro 1K/2K/4K. Flash/Lite oferecem 14 proporções e Pro as 10 padrão; todos aceitam `Auto`. Tamanhos ou proporções explícitos não compatíveis são rejeitados antes da requisição HTTP. Consulte `GetImageCapabilities(model)` antes de exibir as opções. A [matriz de modelos](providers.md#google-image-options) também explica a divergência na documentação do Flash-Lite.

### Saída estruturada (básica)

```csharp
// Desserializa a resposta do LLM diretamente em um POCO C# com auto-recuperação
var result = await service.GetCompletionAsync<WeatherResponse>(
    "Como está o tempo em São Paulo?");
```

### Saída estruturada (lista)

```csharp
// Collections funcionam diretamente — sem wrapper necessário
var items = await service.GetCompletionAsync<List<ItemDto>>(
    "Extraia todas as entidades deste documento...");
```

### Saída estruturada (streaming)

```csharp
// Transmite cada trecho de texto em tempo real + recebe o objeto desserializado ao final
var run = service.BeginStream(prompt).As<MyDto>();

await foreach (var chunk in run.Stream())
    Console.Write(chunk);          // interface em tempo real

MyDto dto = await run.Result;      // parseado e auto-recuperado
```

### Política de Resumo de Conversa

```csharp
// Resume automaticamente mensagens antigas quando a conversa fica longa
service.ConversationPolicy = SummaryConversationPolicy.ByMessage(
    triggerCount: 20,
    keepRecentCount: 5
);

// Dispara por contagem de tokens
service.ConversationPolicy = SummaryConversationPolicy.ByToken(
    triggerTokens: 3000,
    keepRecentTokens: 1000
);

// Use normalmente — o resumo acontece automaticamente
await service.GetCompletionAsync("Continue a conversa...");

// No streaming, aplique a política de resumo antes de StreamAsync()
await service.ApplySummaryPolicyIfNeededAsync();
await foreach (var chunk in service.StreamAsync("Continue..."))
    Console.Write(chunk.Content);

// Salvar/restaurar resumo entre sessões
string saved = service.ConversationPolicy.CurrentSummary;
policy.LoadSummary(saved);
```

### RAG (Retrieval-Augmented Generation)

Escolha busca por palavras-chave, semântica ou híbrida sem exigir embeddings em todas as consultas. `UseKeywordSearch()` dispensa o embedding da consulta; `UseRetriever(...)` conecta um índice externo; `UseHybridSearch(HybridSearchOptions)` transmite pesos e configurações explícitas dos candidatos. A ingestão de documentos continua gerando vetores. Veja os [modos de recuperação e os armazenamentos compatíveis](rag-hybrid-search.md).

```bash
dotnet add package Mythosia.AI.Rag
```

```csharp
using Mythosia.AI.Rag;
using Mythosia.AI.Services.Anthropic;

var service = new AnthropicService(apiKey, httpClient)
    .WithRag(rag => rag
        .AddDocument("manual.txt")
        .AddDocument("policy.txt")
    );

var response = await service.GetCompletionAsync("Qual é a política de reembolso?");
```

Para a recuperação controlada por um agente, registre o armazenamento com `WithAgenticRag(...)` e inicie o trabalho com `service.WithMaxRounds(10).StartRunAsync(...)`. Aguarde `run.Result` ou acompanhe `run.StreamAsync()` na mesma execução. Veja exemplos completos no [README de Mythosia.AI.Rag](../../src/rag/Mythosia.AI.Rag/README.md).

#### Preservar o contexto do documento e a intenção da consulta

Um fragmento pode depender das passagens vizinhas, e uma pergunta de busca tem um papel diferente do documento indexado. O RAG 8.2.0 adiciona embeddings contextuais Voyage e Gemini Embedding 2 para texto extraído de TXT, Markdown e PDF.

```csharp
using Mythosia.AI.Rag;

var store = await RagStore.BuildAsync(rag => rag
    .UseVoyageEmbedding(voyageApiKey, httpClient)
    .AddDocument("policy.pdf"));
var result = await store.QueryAsync("What is the refund period?");
```

[Configuração e contratos dos provedores](rag-embedding.md#retrieval-aware-embeddings): `UseVoyageEmbedding(...)`, `UseGeminiEmbedding(...)`, `IRetrievalEmbeddingProvider` / `EmbeddingDocument` (RAG 8.2.0, RAG Abstractions 6.4.0).

## Fornecedores compatíveis

> Grok 4.7: Requer Mythosia.AI 8.1.0 / Abstractions 4.1.0. [seleção do modelo, raciocínio e velocidade](providers.md#grok-47)

> GPT-6 Sol/Luna exigem Mythosia.AI 8.1.0 e Abstractions 4.1.0; veja a [seleção do modelo e os requisitos](providers.md#gpt-6-sol-luna).

> Claude Opus 5.5: Requer Mythosia.AI 8.1.0 / Abstractions 4.1.0. [configuração e migração](providers.md#claude-opus-55)

| Fornecedor | Pacote | Modelos |
| --- | --- | --- |
| **OpenAI** | `Mythosia.AI` | GPT-6 Astra / Sol / Luna, GPT-5.6 Sol / Terra / Luna, GPT-5.5 / 5.5 Pro / 5.4 / 5.4 Mini / 5.4 Nano / 5.4 Pro / 5.3 Codex / 5.2 / 5.2 Pro / 5.1, GPT-4.1 / 4.1 Mini, GPT-4o / 4o Mini |
| **Anthropic** | `Mythosia.AI` | Claude Fable 5.1 / 5, Mythos 5.1 / 5 (acesso limitado), [Opus 5.5](providers.md#claude-opus-55) / 5 / 4.8 / 4.7 / 4.6 / 4.5, Sonnet 5 / 4.6 / 4.5, Haiku 4.5 |
| **Google** | `Mythosia.AI` | Gemini 3.8 Flash, Gemini 3.7 Flash, Gemini 3.6 Flash, Gemini 3.5 Flash/Flash-Lite, Gemini 3.1 Pro Preview/Flash-Lite, Gemini 3 Flash Preview, Gemini 2.5 Pro/Flash/Flash-Lite, Gemini 3.1 Flash Image, Gemini 3.1 Flash-Lite Image, Gemini 3 Pro Image |
| **xAI** | `Mythosia.AI` | Grok 4.7, Grok 4.6, Grok 4.5 (padrão), Grok 4.3, Grok 4.20 (com / sem raciocínio), Grok Build |
| **DeepSeek** | `Mythosia.AI` | Flash (V4.1 Flash), V4 Pro |
| **Perplexity** | `Mythosia.AI` | Presets da Agent API e `perplexity/sonar` |
| **Alibaba / Qwen** | `Mythosia.AI.Providers.Alibaba` | Variantes Qwen Max / Plus / Turbo / Qwen3 / Qwen3.5 |

Use o Perplexity quando a resposta precisar de informações recentes e fontes que o leitor possa verificar. `PerplexityService` chama a Agent API; a pesquisa e os embeddings independentes permitem montar a recuperação de documentos com o modelo de resposta que preferir. [Perplexity Agent API, pesquisa e embeddings](perplexity.md).

Para revisar documentos longos e realizar tarefas com várias rodadas de ferramentas, escolha Gemini 3.7 Flash ou 3.8 Flash pelo adaptador Google existente. O suporte começa em `Mythosia.AI` 8.0.0 / `Mythosia.AI.Abstractions` 4.0.0; o padrão continua sendo Gemini 3.6 Flash.

Para passar de um rascunho rápido a uma revisão exigente, selecione Grok 4.6 explicitamente e um esforço de `Low` a `XHigh`. Disponível a partir de `Mythosia.AI` 8.0.0 / `Mythosia.AI.Abstractions` 4.0.0; Grok 4.5 continua como padrão de `XAIService`. Consulte a [configuração do Grok](providers.md#xai-xaiservice).

Para criar rascunhos ou combinar referências, use [Grok Imagine Image 2.0](providers.md#grok-imagine-image-20) via `IImageGenerationService`. Mantenha `OutputFormat = ImageOutputFormat.Auto` e escolha a extensão por `MediaType`; xAI não permite escolher codec. Veja a [migração das opções de imagem](providers.md#image-options-migration). O modelo de chat não muda.

Escolha Flare para rascunhos visuais rápidos e Sunburst para alterações precisas. A [geração e edição com GPT Image 2.5](providers.md#gpt-image-25) usa a API existente com seleção explícita por requisição; o padrão OpenAI permanece GPT Image 2.

Para analisar gráficos e capturas, chamar funções locais ou revisar uma resposta rápida, use [DeepSeek Flash](providers.md#deepseek-deepseekservice) (`AIModels.DeepSeek.Flash`, V4.1 Flash). O raciocínio fica desativado por padrão; ative com `WithDeepSeekReasoning(...)` ou `WithReasoning(...)` por solicitação.

Para texto, selecione `AIModels.DeepSeek.V4Pro` (`deepseek-v4-pro`, V4-Pro-0813). Flash continua como padrão e aceita imagens; ambos oferecem raciocínio Low/High/Max e o mesmo limite de saída. Defina `UseResponsesApi = true` antes de criar a solicitação para usar Responses com as APIs existentes de completion, streaming, Run e funções locais. O padrão permanece `false` para preservar Chat Completions nas aplicações atuais; a escolha é capturada para toda a solicitação e suas rodadas. Responses reenvia o histórico completo da conversa e do raciocínio nativo sem depender de IDs de respostas armazenadas.

Reutilize uma imagem enviada em várias perguntas ao Flash com `DeepSeekImageFileContent`, via Chat Completions ou Responses; o V4 Pro, exclusivo para texto, rejeita imagens. Consulte [envio, reutilização e limites de imagens](providers.md#deepseek-deepseekservice). Requer Mythosia.AI 8.1.0 / Abstractions 4.1.0.

> Claude Fable 5 e Claude Mythos 5 exigem retenção de dados por 30 dias e não se qualificam para acordos sem retenção. O raciocínio adaptativo fica sempre ativo; quando o chamador solicita sua desativação, Mythosia usa esforço baixo e omite o resumo do raciocínio. Mythos 5 é limitado a clientes aprovados do Project Glasswing.

## Guias e migração

Para TXT e Markdown, escolha um [splitter por regras](text-splitters.md) adequado à estrutura. Tamanho, overlap e limites Unicode são verificados; Markdown preserva títulos, código e linhas de tabelas. Caracteres e palavras não são limites de tokens do modelo. Condições de tabela e indentação mantêm seu significado; a repetição excessiva de contexto Markdown para com uma exceção explícita.

Para evitar que uma indexação aparentemente correta sobrescreva fragmentos ou os associe a vetores errados, a [validação da indexação](rag-pipeline.md#indexing-validation) rejeita IDs e lotes de embeddings inválidos antes da persistência. Divisores personalizados devem fornecer IDs únicos e herdar os metadados do documento.

[Identidades de arquivo estáveis](document-loaders.md#file-source-identity), [vetores de pergunta validados](rag-embedding.md#query-embedding-validation) e [persistência por documento com cancelamento URL](rag-pipeline.md#custom-persistence) evitam duplicações, buscas inválidas e chunks obsoletos.

A prévia opcional `Mythosia.AI.Rag.Search.Pixie` permite comparar busca neural esparsa local com a busca existente. Mantém o provedor de embeddings densos e um índice PIXIE em memória, sem migrar armazenamentos persistentes nem substituir a busca padrão. [Guia PIXIE e comparação (inglês)](../rag-pixie-search.md).

A [infraestrutura de avaliação de recuperação](https://github.com/AJ-comp/Mythosia.AI/blob/main/tests/Mythosia.AI.Rag.Evaluation/README.md) oferece conjuntos de dados reutilizáveis, adaptadores de busca, relatórios persistentes e verificações de regressão. Amplie o mesmo avaliador para novos métodos de busca e suas próprias coleções de documentos.

Isole as configurações, interrompa o trabalho em curso e receba respostas com consumo e fontes. O [guia de migração v8](v8-migration.md) reúne seis mudanças de arquitetura, exemplos e o alcance da validação.

> Versões dos pacotes documentadas aqui: [Mythosia.AI 8.1.0](../../src/core/Mythosia.AI/RELEASE_NOTES.md#v810), [Abstractions 4.1.0](../../src/core/Mythosia.AI.Abstractions/RELEASE_NOTES.md#v410), [Alibaba 3.0.1](../../src/core/Mythosia.AI.Providers.Alibaba/RELEASE_NOTES.md#v301), [RAG 8.2.0](../../src/rag/Mythosia.AI.Rag/RELEASE_NOTES.md#v820), [PostgreSQL 10.8.1](../../src/vectordb/Mythosia.VectorDb.Postgres/RELEASE_NOTES.md#v1081), [MCP 0.1.1-preview](../../src/integrations/Mythosia.AI.Mcp/RELEASE_NOTES.md#v011-preview), [Serving.Abstractions 1.0.0](../../src/serving/Mythosia.AI.Serving.Abstractions/RELEASE_NOTES.md#v100), [Serving.Ollama 1.0.0](../../src/serving/Mythosia.AI.Serving.Ollama/RELEASE_NOTES.md#v100), [Serving.LlamaCpp 1.0.0](../../src/serving/Mythosia.AI.Serving.LlamaCpp/RELEASE_NOTES.md#v100), [Serving.Vllm 1.1.0](../../src/serving/Mythosia.AI.Serving.Vllm/RELEASE_NOTES.md#v110). Consulte a [matriz do patch anterior](https://github.com/AJ-comp/Mythosia.AI/blob/main/RELEASE_NOTES.md#v811) e a [publicação coordenada anterior](https://github.com/AJ-comp/Mythosia.AI/blob/main/RELEASE_NOTES.md#v810) para as versões dos outros pacotes de recuperação, documentos e vetores.

> [Patch RAG 8.1.1 / PostgreSQL 10.8.1](https://github.com/AJ-comp/Mythosia.AI/blob/main/RELEASE_NOTES.md#v811): os wrappers RAG existentes passam a refletir mudanças do reescritor em tempo de execução, e a busca híbrida mista do PostgreSQL respeita as configurações vetoriais. O pacote principal `Mythosia.AI` permanece em 8.1.0.

---

## Arquitetura

<a href="../assets/architecture.svg">
  <picture>
    <source media="(prefers-color-scheme: dark)" srcset="../assets/architecture-dark.svg">
    <img src="../assets/architecture.svg" alt="Arquitetura Mythosia.AI: IA central, orquestração RAG, carregadores de documentos, armazenamentos vetoriais, contratos compartilhados, integração MCP e gerenciamento independente de Ollama, llama.cpp e vLLM." width="1600">
  </picture>
</a>

### Detalhes das dependências dos pacotes

As setas indicam referências diretas. Pacotes compartilhados aparecem em várias vistas; os clientes Serving compartilham contratos de gerenciamento e são independentes da IA central.

#### IA principal e extensões

```mermaid
%%{init: {"theme":"base","look":"classic","htmlLabels":false,"themeVariables":{"fontFamily":"Arial, sans-serif","fontSize":"18px","primaryTextColor":"#172c46","lineColor":"#64748b","clusterBkg":"#f8fafc","clusterBorder":"#cbd5e1"},"flowchart":{"htmlLabels":false,"curve":"linear","nodeSpacing":20,"rankSpacing":36,"padding":16,"wrappingWidth":300},"fontFamily":"Arial, sans-serif","fontSize":18}}%%
flowchart LR
    subgraph Extensions["Extensões de fornecedores e ferramentas"]
        Alibaba["Mythosia.AI.<br/>Providers.Alibaba"]:::extension
        Mcp["Mythosia.AI.Mcp"]:::extension
    end
    AI["Mythosia.AI"]:::core
    AIAbs["Mythosia.AI.<br/>Abstractions"]:::contract
    subgraph Independent["Gerenciamento independente do servidor"]
        ServingAbs["Mythosia.AI.Serving.<br/>Abstractions"]:::contract
        OllamaServing["Mythosia.AI.<br/>Serving.Ollama"]:::extension
        LlamaCppServing["Mythosia.AI.<br/>Serving.LlamaCpp"]:::extension
        VllmServing["Mythosia.AI.<br/>Serving.Vllm"]:::extension
        OllamaServing --> ServingAbs
        LlamaCppServing --> ServingAbs
        VllmServing --> ServingAbs
    end
    Alibaba --> AI
    Mcp --> AI
    AI --> AIAbs
    classDef core fill:#eff6ff,stroke:#93b4de,color:#172c46,stroke-width:1.5px
    classDef rag fill:#eef8f5,stroke:#83b4a4,color:#164638,stroke-width:1.5px
    classDef extension fill:#f5f0fc,stroke:#b9a4d4,color:#403054,stroke-width:1.5px
    classDef documents fill:#fff8e9,stroke:#d4b879,color:#61491d,stroke-width:1.5px
    classDef store fill:#edf7fb,stroke:#8dbaca,color:#194758,stroke-width:1.5px
    classDef contract fill:#f8fafc,stroke:#a7b2c2,color:#334155,stroke-width:1.5px
```

#### RAG e carregamento de documentos

```mermaid
%%{init: {"theme":"base","look":"classic","htmlLabels":false,"themeVariables":{"fontFamily":"Arial, sans-serif","fontSize":"18px","primaryTextColor":"#172c46","lineColor":"#64748b","clusterBkg":"#f8fafc","clusterBorder":"#cbd5e1"},"flowchart":{"htmlLabels":false,"curve":"linear","nodeSpacing":20,"rankSpacing":36,"padding":16,"wrappingWidth":300},"fontFamily":"Arial, sans-serif","fontSize":18}}%%
flowchart LR
    Rag["Mythosia.AI.Rag"]:::rag
    subgraph Contracts["Contratos de IA e RAG"]
        AIAbs["Mythosia.AI.<br/>Abstractions"]:::contract
        RagAbs["Mythosia.AI.Rag.<br/>Abstractions"]:::contract
    end
    InMem["Mythosia.VectorDb.<br/>InMemory"]:::store
    subgraph Documents["Carregamento de documentos"]
        Office["Mythosia.Documents.<br/>Office"]:::documents
        Pdf["Mythosia.Documents.<br/>Pdf"]:::documents
        LoaderAbs["Mythosia.Documents.<br/>Abstractions"]:::contract
        Office --> LoaderAbs
        Pdf --> LoaderAbs
    end
    Rag --> AIAbs
    Rag --> RagAbs
    Rag --> InMem
    Rag --> Office
    Rag --> Pdf
    classDef core fill:#eff6ff,stroke:#93b4de,color:#172c46,stroke-width:1.5px
    classDef rag fill:#eef8f5,stroke:#83b4a4,color:#164638,stroke-width:1.5px
    classDef extension fill:#f5f0fc,stroke:#b9a4d4,color:#403054,stroke-width:1.5px
    classDef documents fill:#fff8e9,stroke:#d4b879,color:#61491d,stroke-width:1.5px
    classDef store fill:#edf7fb,stroke:#8dbaca,color:#194758,stroke-width:1.5px
    classDef contract fill:#f8fafc,stroke:#a7b2c2,color:#334155,stroke-width:1.5px
```

#### Armazenamento vetorial e pesquisa

```mermaid
%%{init: {"theme":"base","look":"classic","htmlLabels":false,"themeVariables":{"fontFamily":"Arial, sans-serif","fontSize":"18px","primaryTextColor":"#172c46","lineColor":"#64748b","clusterBkg":"#f8fafc","clusterBorder":"#cbd5e1"},"flowchart":{"htmlLabels":false,"curve":"linear","nodeSpacing":20,"rankSpacing":36,"padding":16,"wrappingWidth":300},"fontFamily":"Arial, sans-serif","fontSize":18}}%%
flowchart LR
    subgraph Stores["Armazenamentos vetoriais"]
        InMem["Mythosia.VectorDb.<br/>InMemory"]:::store
        Pg["Mythosia.VectorDb.<br/>Postgres"]:::store
        Qd["Mythosia.VectorDb.<br/>Qdrant"]:::store
        Pine["Mythosia.VectorDb.<br/>Pinecone"]:::store
    end
    subgraph Search["Busca neural opcional"]
        Pixie["Mythosia.AI.Rag.<br/>Search.Pixie"]:::rag
    end
    RagAbs["Mythosia.AI.Rag.<br/>Abstractions"]:::contract
    VdbAbs["Mythosia.VectorDb.<br/>Abstractions"]:::contract
    InMem --> RagAbs
    InMem --> VdbAbs
    RagAbs --> VdbAbs
    Pg --> VdbAbs
    Qd --> VdbAbs
    Pine --> VdbAbs
    Pixie --> VdbAbs
    classDef core fill:#eff6ff,stroke:#93b4de,color:#172c46,stroke-width:1.5px
    classDef rag fill:#eef8f5,stroke:#83b4a4,color:#164638,stroke-width:1.5px
    classDef extension fill:#f5f0fc,stroke:#b9a4d4,color:#403054,stroke-width:1.5px
    classDef documents fill:#fff8e9,stroke:#d4b879,color:#61491d,stroke-width:1.5px
    classDef store fill:#edf7fb,stroke:#8dbaca,color:#194758,stroke-width:1.5px
    classDef contract fill:#f8fafc,stroke:#a7b2c2,color:#334155,stroke-width:1.5px
```

## Pacotes

### Núcleo

| Pacote | NuGet | Descrição |
| --- | --- | --- |
| [Mythosia.AI](../../src/core/Mythosia.AI/README.md) | [![NuGet](https://img.shields.io/nuget/v/Mythosia.AI.svg)](https://www.nuget.org/packages/Mythosia.AI) | Biblioteca core — providers integrados, streaming, function calling e suporte multimodal |
| [Mythosia.AI.Abstractions](../../src/core/Mythosia.AI.Abstractions/README.md) | [![NuGet](https://img.shields.io/nuget/v/Mythosia.AI.Abstractions.svg)](https://www.nuget.org/packages/Mythosia.AI.Abstractions) | Interface `IAIService` e modelos compartilhados — pacote de contrato leve para bibliotecas |
| [Mythosia.AI.Providers.Alibaba](../../src/core/Mythosia.AI.Providers.Alibaba/README.md) | [![NuGet](https://img.shields.io/nuget/v/Mythosia.AI.Providers.Alibaba.svg)](https://www.nuget.org/packages/Mythosia.AI.Providers.Alibaba) | Pacote provider Alibaba / Qwen baseado em `Mythosia.AI` |

### RAG

| Pacote | NuGet | Descrição |
| --- | --- | --- |
| [Mythosia.AI.Rag](../../src/rag/Mythosia.AI.Rag/README.md) | [![NuGet](https://img.shields.io/nuget/v/Mythosia.AI.Rag.svg)](https://www.nuget.org/packages/Mythosia.AI.Rag) | Extensão RAG fluente para IAIService com API `.WithRag()` |
| [Mythosia.AI.Rag.Abstractions](../../src/rag/Mythosia.AI.Rag.Abstractions/README.md) | [![NuGet](https://img.shields.io/nuget/v/Mythosia.AI.Rag.Abstractions.svg)](https://www.nuget.org/packages/Mythosia.AI.Rag.Abstractions) | Interfaces e modelos dos componentes do pipeline RAG |

### Carregadores de documentos

| Pacote | NuGet | Descrição |
| --- | --- | --- |
| [Mythosia.Documents.Abstractions](../../src/loaders/Mythosia.Documents.Abstractions/README.md) | [![NuGet](https://img.shields.io/nuget/v/Mythosia.Documents.Abstractions.svg)](https://www.nuget.org/packages/Mythosia.Documents.Abstractions) | Interfaces e modelos do loader de documentos (`IDocumentLoader`, `DoclingDocument`) |
| [Mythosia.Documents.Office](../../src/loaders/Mythosia.Documents.Office/README.md) | [![NuGet](https://img.shields.io/nuget/v/Mythosia.Documents.Office.svg)](https://www.nuget.org/packages/Mythosia.Documents.Office) | Parser OpenXml para Word / Excel / PowerPoint |
| [Mythosia.Documents.Pdf](../../src/loaders/Mythosia.Documents.Pdf/README.md) | [![NuGet](https://img.shields.io/nuget/v/Mythosia.Documents.Pdf.svg)](https://www.nuget.org/packages/Mythosia.Documents.Pdf) | Parser PDF baseado em PdfPig |

### Armazenamentos vetoriais

> **Escolha um ou mais** — todos implementam `IVectorStore` do pacote Abstractions.

| Pacote | NuGet | Descrição |
| --- | --- | --- |
| [Mythosia.VectorDb.Abstractions](../../src/vectordb/Mythosia.VectorDb.Abstractions/README.md) | [![NuGet](https://img.shields.io/nuget/v/Mythosia.VectorDb.Abstractions.svg)](https://www.nuget.org/packages/Mythosia.VectorDb.Abstractions) | Contrato `IVectorStore` · `VectorRecord` · `VectorFilter` |
| [Mythosia.VectorDb.InMemory](../../src/vectordb/Mythosia.VectorDb.InMemory/README.md) | [![NuGet](https://img.shields.io/nuget/v/Mythosia.VectorDb.InMemory.svg)](https://www.nuget.org/packages/Mythosia.VectorDb.InMemory) | Store em memória — sem infraestrutura, ideal para prototipagem |
| [Mythosia.VectorDb.Pinecone](../../src/vectordb/Mythosia.VectorDb.Pinecone/README.md) | [![NuGet](https://img.shields.io/nuget/v/Mythosia.VectorDb.Pinecone.svg)](https://www.nuget.org/packages/Mythosia.VectorDb.Pinecone) | Pinecone HTTP API — isolamento por index/namespace/scope |
| [Mythosia.VectorDb.Postgres](../../src/vectordb/Mythosia.VectorDb.Postgres/README.md) | [![NuGet](https://img.shields.io/nuget/v/Mythosia.VectorDb.Postgres.svg)](https://www.nuget.org/packages/Mythosia.VectorDb.Postgres) | PostgreSQL + pgvector — índices HNSW / IVFFlat, pronto para produção |
| [Mythosia.VectorDb.Qdrant](../../src/vectordb/Mythosia.VectorDb.Qdrant/README.md) | [![NuGet](https://img.shields.io/nuget/v/Mythosia.VectorDb.Qdrant.svg)](https://www.nuget.org/packages/Mythosia.VectorDb.Qdrant) | Qdrant gRPC client — Cosine / Euclidean / Dot, provisionamento automático |

### Serving — Plano de controle

Crie seletores de modelos e telas de status com uma API de gerenciamento comum para instâncias de Ollama, llama.cpp e vLLM em execução. `IModelServer` consulta a saúde, os modelos e as capacidades; a descoberta nunca carrega nem baixa modelos. Esses clientes se conectam a servidores existentes, sem hospedar mecanismos nem enviar solicitações de chat.

As interfaces opcionais `IModelLifecycle`, `IModelDownloader` e `IModelMetricsProvider` oferecem operações explícitas quando disponíveis. Verifique as capacidades do servidor conectado: `Unknown` significa evidência insuficiente, não `Unsupported`; `Supported` também não garante sucesso para todos os modelos. Estados desconhecidos de instalação e carregamento permanecem desconhecidos.

As verificações em servidores reais passaram com Ollama **0.34.4** (`qwen2.5:0.5b`), llama.cpp **b11146** nos modos Router e de modelo único (Qwen2.5 0.5B, Q4_K_M) e vLLM **0.30.0** (um modelo Qwen pequeno). Os resultados se aplicam às configurações testadas. Consulte o [guia de gerenciamento de servidores](serving.md) para as operações verificadas e as limitações de cada mecanismo.

| Pacote | NuGet | Descrição |
| --- | --- | --- |
| [Mythosia.AI.Serving.Abstractions](../../src/serving/Mythosia.AI.Serving.Abstractions/README.md) | [![NuGet](https://img.shields.io/nuget/v/Mythosia.AI.Serving.Abstractions.svg)](https://www.nuget.org/packages/Mythosia.AI.Serving.Abstractions) | Contratos comuns e snapshots imutáveis de servidores, modelos e capacidades. |
| [Mythosia.AI.Serving.Ollama](../../src/serving/Mythosia.AI.Serving.Ollama/README.md) | [![NuGet](https://img.shields.io/nuget/v/Mythosia.AI.Serving.Ollama.svg)](https://www.nuget.org/packages/Mythosia.AI.Serving.Ollama) | Inventário e saúde Ollama, pré-carga/descarga explícitas e downloads em streaming. |
| [Mythosia.AI.Serving.LlamaCpp](../../src/serving/Mythosia.AI.Serving.LlamaCpp/README.md) | [![NuGet](https://img.shields.io/nuget/v/Mythosia.AI.Serving.LlamaCpp.svg)](https://www.nuget.org/packages/Mythosia.AI.Serving.LlamaCpp) | Consultas llama.cpp, operações do roteador verificadas e métricas sem carga automática. |
| [Mythosia.AI.Serving.Vllm](../../src/serving/Mythosia.AI.Serving.Vllm/README.md) | [![NuGet](https://img.shields.io/nuget/v/Mythosia.AI.Serving.Vllm.svg)](https://www.nuget.org/packages/Mythosia.AI.Serving.Vllm) | Cartões, saúde, versão e métricas com rótulos do vLLM; API concreta preservada. |

## Estrutura do Repositório

```text
src/
  core/
    Mythosia.AI/                        # Biblioteca AI core
    Mythosia.AI.Abstractions/           # Interface IAIService e modelos compartilhados
    Mythosia.AI.Providers.Alibaba/      # Pacote provider Alibaba / Qwen
  loaders/
    Mythosia.Documents.Abstractions/    # Contrato document loader (IDocumentLoader, DoclingDocument)
    Mythosia.Documents.Office/          # Loader de documentos Office (Word/Excel/PowerPoint)
    Mythosia.Documents.Pdf/             # Loader de documentos PDF
  rag/
    Mythosia.AI.Rag/                    # RAG Fluent API e pipeline
    Mythosia.AI.Rag.Abstractions/       # Interfaces e modelos RAG (RagDocument)
  serving/
    Mythosia.AI.Serving.Abstractions/  # Contratos comuns de gerenciamento de modelos
    Mythosia.AI.Serving.Ollama/        # Gerenciamento Ollama e downloads explícitos
    Mythosia.AI.Serving.LlamaCpp/      # Gerenciamento llama.cpp de modelo único e roteador
    Mythosia.AI.Serving.Vllm/          # Gerenciamento e métricas vLLM
  vectordb/
    Mythosia.VectorDb.Abstractions/     # Contrato vector store
    Mythosia.VectorDb.InMemory/         # Vector store em memória
    Mythosia.VectorDb.Pinecone/         # Vector store Pinecone
    Mythosia.VectorDb.Postgres/         # PostgreSQL + pgvector
    Mythosia.VectorDb.Qdrant/           # Vector store Qdrant
apps/                                   # Aplicações (exemplos e ferramentas)
tests/                                  # Projetos de teste unitário / integração
```

## Instalação

```bash
dotnet add package Mythosia.AI
```

Para operações LINQ avançadas com streams:

```bash
dotnet add package System.Linq.Async
```

## Documentação

Para passar de um rascunho rápido a uma revisão aprofundada, ou responder com base em informações atuais e documentos hospedados, veja o [raciocínio e a busca com fontes](reasoning-and-search.md).

- **[📖 Site completo de documentação](https://aj-comp.github.io/Mythosia.AI/)** — documentação gerada com DocFX que abrange todos os recursos, o pipeline RAG, os armazenamentos vetoriais e a referência da API
- [Guia de introdução](getting-started.md)
- [README Mythosia.AI](../../src/core/Mythosia.AI/README.md) — Referência completa de API: function calling, streaming e configuração de modelos
- [README Mythosia.AI.Rag](../../src/rag/Mythosia.AI.Rag/README.md) — Uso do pipeline RAG e implementações customizadas
- [Guia de loaders](document-loaders.md)
- [Notas de versão](../../src/core/Mythosia.AI/RELEASE_NOTES.md)

## Validar a velocidade de processamento com fornecedores reais

Na raiz do repositório, execute:

```powershell
./build/test-inference-speed-live.ps1
```

A suíte paga usa a configuração existente do Key Vault e prompts sintéticos. Ela verifica Anthropic Opus 5.5, OpenAI GPT-6 Astra, Gemini 3.8 Flash e Grok 4.6 com ProviderDefault/Standard/Fast nos caminhos de completion e Run: 24 casos. Erros de acesso da conta, ausência de informação sobre o modo aplicado e reduções de modo pelo servidor não contam como validação bem-sucedida de Fast; todos os casos precisam passar sem serem ignorados. Os relatórios são gravados em `artifacts/test-results/inference-speed-live`. Use `-NoBuild` somente depois de compilar os testes Release atuais. Esse comando documenta como executar a suíte, sem afirmar que a conta atual já passou nela.

## Licença

Este projeto é distribuído sob a [licença MIT](https://github.com/AJ-comp/Mythosia.AI/blob/main/LICENSE).

## Origem

Este projeto era originalmente parte do [Mythosia](https://github.com/AJ-comp/Mythosia).

[Criar opções de modelo com definições compartilhadas](model-capabilities.md).
