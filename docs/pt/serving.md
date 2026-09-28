# Gerenciar servidores de modelos existentes

Um seletor de modelos ou uma tela de operações precisa conhecer a saúde do servidor, os modelos disponíveis e seu estado de carregamento antes de enviar um prompt. Os pacotes Serving unificam essas consultas para Ollama, llama.cpp e vLLM, mantendo explícitas as operações próprias de cada mecanismo.

Use esses pacotes para preencher um seletor de modelos, indicar se um servidor está acessível, gerenciar a permanência dos modelos na memória quando o runtime permitir ou consultar métricas do mecanismo. Ao trocar de runtime, o código comum de consulta da aplicação pode permanecer o mesmo.

Esses clientes se conectam a um servidor HTTP existente. A instalação ou hospedagem do mecanismo, o aluguel de GPU, o chat e a geração de embeddings pertencem a componentes separados. O chat continua pelo serviço de IA apropriado, como `QwenService` para vLLM; os provedores de embeddings de RAG permanecem separados. A descoberta não carrega modelos automaticamente. SGLang não está implementado.

## Escolher um pacote

| Pacote | Versão | Finalidade |
| --- | --- | --- |
| `Mythosia.AI.Serving.Abstractions` | 1.0.0 | Contratos comuns para o código da aplicação ou um adaptador de gerenciamento próprio. Sem dependências de pacotes. |
| `Mythosia.AI.Serving.Ollama` | 1.0.0 | Consultar o Ollama, baixar modelos e carregá-los ou descarregá-los explicitamente. |
| `Mythosia.AI.Serving.LlamaCpp` | 1.0.0 | Consultar o llama.cpp, ler métricas e gerenciar modelos no modo Router. |
| `Mythosia.AI.Serving.Vllm` | 1.1.0 | Consultar o vLLM e ler métricas pelas APIs comuns ou pelas APIs específicas de vLLM existentes. |

Os quatro pacotes têm como destino .NET Standard 2.1. Instale o adaptador que será usado; ele inclui automaticamente o pacote de abstrações. Os adaptadores dependem dos contratos compartilhados e de Newtonsoft.Json, independentemente dos pacotes principais de IA e RAG.

## Consultar sem alterar o estado do servidor

Instale o pacote concreto do seu runtime. O exemplo usa Ollama; para outros servidores, escolha `VllmServer` ou `LlamaCppServer` no namespace correspondente. A descoberta usa apenas solicitações de leitura e não envia comandos de carregamento, geração ou download.

```bash
dotnet add package Mythosia.AI.Serving.Ollama --version 1.0.0
```

```csharp
using System;
using System.Net.Http;
using System.Threading;
using Mythosia.AI.Serving;
using Mythosia.AI.Serving.Ollama;

using var http = new HttpClient { Timeout = TimeSpan.FromMinutes(10) };
using var cancellation = new CancellationTokenSource();
IModelServer server = new OllamaServer("http://localhost:11434", http,
    apiKey: Environment.GetEnvironmentVariable("MODEL_SERVER_API_KEY"));

var health = await server.GetHealthAsync(cancellation.Token);
var info = await server.GetInfoAsync(cancellation.Token);
var capabilities = await server.GetCapabilitiesAsync(cancellation.Token);
var models = await server.GetModelsAsync(cancellation.Token);

foreach (var model in models)
    Console.WriteLine($"{model.Id}: {model.InstallationState} / {model.LoadState}");
```

O endpoint é a raiz do servidor, opcionalmente com um prefixo de caminho de proxy reverso. A chave de API é opcional e enviada como credencial Bearer em cada solicitação. O cliente não altera `HttpClient.DefaultRequestHeaders` nem descarta o `HttpClient` fornecido; reutilize-o e descarte-o conforme o ciclo de vida da aplicação. Neste exemplo do Ollama, o timeout também abrange os corpos de resposta em streaming, portanto reserve tempo suficiente para baixar um modelo.

## Contratos comuns e opcionais

| Contrato | Finalidade |
| --- | --- |
| `IModelServer` | Informações, saúde, modelos e capacidades observadas do servidor. |
| `IModelLifecycle` | Comandos explícitos para carregar e descarregar; opcional. |
| `IModelDownloader` | Download explícito com progresso; opcional. |
| `IModelMetricsProvider` | Amostras de métricas com rótulos; opcional. |

Uma interface implementada indica que o cliente oferece a operação; `ServingCapabilities` informa o que pode ser comprovado no endpoint conectado. `Supported` não garante autorização nem sucesso para todos os modelos. `Unsupported` significa indisponível no modo ou endpoint observado. `Unknown` indica evidências insuficientes, incluindo falhas de autenticação ou conexão, e não deve ser tratado como falta de suporte.

`InstallationState` e `LoadState` descrevem observações diferentes. `Unknown` não significa ausente nem descarregado. Valores não informados de `SizeBytes`, `MemoryBytes` ou `ContextLength` permanecem `null`, não zero. Um endpoint de gerenciamento saudável não prova que um modelo específico esteja pronto para inferência.

## Diferenças entre runtimes

| Operação | Ollama | llama.cpp com modelo único | llama.cpp Router | vLLM |
| --- | --- | --- | --- | --- |
| Informações, saúde e lista de modelos | Sim | Sim | Sim | Sim |
| Carregamento / descarregamento explícito | Sim, por solicitações de geração vazias | Não suportado | Sim, após confirmar a identidade Router | Não suportado por este cliente |
| Download de modelo | Sim, com progresso em streaming | Não suportado | Operação explícita; requer endpoint de download e eventos SSE | Não suportado por este cliente |
| Métricas | Não implementadas | Métricas do servidor quando habilitadas | Sobrecarga concreta por modelo; o modelo já deve estar carregado | Métricas do servidor quando disponíveis |

Esta tabela descreve as operações dos clientes, sem prometer que todas as versões do servidor, permissões ou modelos as suportem. Consulte as capacidades do endpoint conectado e trate as falhas das operações.

**Ollama:** `/api/tags` fornece modelos registrados e `/api/ps` os modelos em execução. Um modelo remoto pode estar registrado sem pesos locais; sem execução local, seu estado de carregamento continua desconhecido. A pré-carga usa uma solicitação vazia a `/api/generate` e o keep-alive padrão do servidor. Modelos exclusivos de embeddings não são redirecionados a outra API. O descarregamento usa `keep_alive: 0` e não apaga arquivos. Métricas não estão implementadas.

**llama.cpp:** `/props` deve confirmar explicitamente o modo roteador antes de comandos de ciclo de vida ou download. O modo de modelo único não oferece esses comandos e preserva o estado de suspensão observado. Downloads do roteador assinam `/models/sse`, enviam `POST /models` e só têm sucesso ao receber `download_finished` para o modelo solicitado. A disponibilidade de SSE, sozinha, mantém o suporte a download desconhecido. Métricas globais servem ao modo de modelo único; no roteador é necessária a sobrecarga concreta `GetMetricsAsync(modelId, token)`, que envia `autoload=false` para impedir carregamento durante a consulta.

**vLLM:** os aliases publicados e o campo opcional `root` são preservados, mas os estados comuns de instalação e carregamento continuam desconhecidos. Modelos e métricas são verificados pelas respostas reais; ciclo de vida e download não são suportados. Os métodos e DTOs existentes de `VllmServer` continuam no cliente concreto; saúde, modelos e métricas comuns usam interfaces explícitas.

## Executar uma operação de gerenciamento explícita

Downloads e alterações na permanência dos modelos na memória consomem rede, disco ou memória do dispositivo. Execute essas operações quando a aplicação precisar delas. A continuação do exemplo de Ollama abaixo baixa um modelo pequeno e o carrega brevemente para observar seu estado. Use os IDs exatos dos modelos do servidor, incluindo a tag do Ollama ou a tag de quantização do llama.cpp.

```csharp
if (server is IModelDownloader downloader &&
    capabilities.ModelDownloading == ServingFeatureSupport.Supported)
{
    var progress = new Progress<ModelDownloadProgress>(p =>
        Console.WriteLine($"{p.ModelId}: {p.Stage} {p.CompletedBytes}/{p.TotalBytes}"));
    await downloader.DownloadModelAsync("qwen2.5:0.5b", progress, cancellation.Token);
}

if (server is IModelLifecycle lifecycle &&
    capabilities.ModelLoading == ServingFeatureSupport.Supported &&
    capabilities.ModelUnloading == ServingFeatureSupport.Supported)
{
    try
    {
        await lifecycle.LoadModelAsync("qwen2.5:0.5b", cancellation.Token);
        var afterLoad = await server.GetModelsAsync(cancellation.Token);
        foreach (var model in afterLoad)
            Console.WriteLine($"{model.Id}: {model.LoadState}");
    }
    finally
    {
        using var cleanup = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        await lifecycle.UnloadModelAsync("qwen2.5:0.5b", cleanup.Token);
    }
}
```

O exemplo usa um modelo dedicado a testes e o descarrega depois. Uma aplicação em produção decide quando liberar um modelo; não descarregue um modelo ainda usado por outras solicitações. A limpeza tem seu próprio prazo e pode falhar se o servidor estiver indisponível.

O progresso descreve um artefato ou uma etapa individual. Contadores de bytes nulos não representam zero nem uma porcentagem do modelo inteiro. Uma chamada de carregamento bem-sucedida confirma o comando, não a prontidão nem a permanência indefinida na memória; observe `LoadState` com uma espera limitada quando a prontidão for importante. Consulte o guia do pacote concreto para o protocolo de download do Router do llama.cpp e suas restrições de versão. Uma operação solicitada explicitamente pode ser tentada com suporte `Unknown` após verificar a configuração do servidor; a descoberta de capacidades, por si só, nunca a inicia.

## Cancelamento e erros

Passe um token de cancelamento às consultas e aos comandos. Cancelar interrompe o trabalho HTTP e a espera deste cliente; não garante cancelamento remoto, reversão nem remoção de camadas baixadas. Configure o `HttpClient` fornecido para a duração da operação; os clientes não assumem sua propriedade.

Preserve os rótulos das métricas ao comparar modelos ou mecanismos. Métricas ausentes não são zero, e os valores podem incluir `NaN` ou infinito. `ServingException` é o tipo de erro comum; erros comuns de gerenciamento omitem o corpo bruto da resposta e credenciais. Chamadas vLLM existentes mantêm seus detalhes de erro anteriores.

`GetHealthAsync` classifica falhas do endpoint como estados de saúde; ainda assim, propaga o cancelamento solicitado pelo chamador. Outras operações podem lançar `ServingException`, enquanto um modo do llama.cpp sabidamente não suportado pode lançar `NotSupportedException`. Nem um timeout nem uma solicitação malsucedida comprovam que a ação remota foi revertida. Um método de download só retorna com sucesso depois que o runtime informa a conclusão: sucesso terminal seguido de EOF no Ollama, ou o evento `download_finished` correspondente no Router do llama.cpp.

## O que foi verificado

Os testes offline cobrem sucesso controlado, respostas malformadas, erros e cancelamento. Verificações separadas em servidores reais usaram uma única NVIDIA A40, pequenos modelos Qwen públicos e estas versões dos mecanismos:

| Runtime | Modelo testado | Operações de gerenciamento verificadas |
| --- | --- | --- |
| Ollama 0.34.4 | `qwen2.5:0.5b` | Descoberta, download sem cópia local, carregamento/descarregamento, erros de modelo ausente sem dados sensíveis, cancelamento prévio e cancelamento após progresso parcial do download. |
| llama.cpp b11146, Router | `Qwen/Qwen2.5-0.5B-Instruct-GGUF:Q4_K_M` | Descoberta, eventos de download, carregamento/descarregamento, métricas por modelo sem carregamento automático, erros e cancelamento de download. |
| llama.cpp b11146, modelo único | Mesmo modelo GGUF | Descoberta, métricas do servidor, cancelamento e rejeição explícita de comandos de ciclo de vida do Router. |
| vLLM 0.30.0 | `Qwen/Qwen2.5-0.5B-Instruct` | Descoberta, métricas do servidor e cancelamento prévio. |

Solicitações curtas de inferência HTTP nativa também retornaram texto gerado nas quatro configurações. Elas confirmam o funcionamento dos mecanismos, não os adaptadores de chat dos serviços de IA, a qualidade dos modelos, a taxa de processamento ou a compatibilidade com todas as versões dos mecanismos. Os perfis acima são configurações testadas, não versões mínimas suportadas. As verificações de cancelamento de download usaram modelos de teste maiores e separados, sem afirmar reversão remota. O primeiro download no Ollama falhou; uma nova tentativa e um novo download após remover o modelo passaram, sem determinar a causa exata da falha inicial.

Use o [guia de verificação real com ativação explícita](https://github.com/AJ-comp/Mythosia.AI/blob/main/tests/Mythosia.AI.Serving.Live/README.md) para verificar seu endpoint implantado. Ele distingue o executor de gerenciamento incluído no repositório dos testes adicionais de inferência e cancelamento usados na validação. Relatórios detalhados de execução ficam fora da documentação publicada.

## Guias dos pacotes

- [Mythosia.AI.Serving.Abstractions](../../src/serving/Mythosia.AI.Serving.Abstractions/README.md) — Contratos comuns e snapshots imutáveis de servidores, modelos e capacidades.
- [Mythosia.AI.Serving.Ollama](../../src/serving/Mythosia.AI.Serving.Ollama/README.md) — Inventário e saúde Ollama, pré-carga/descarga explícitas e downloads em streaming.
- [Mythosia.AI.Serving.LlamaCpp](../../src/serving/Mythosia.AI.Serving.LlamaCpp/README.md) — Consultas llama.cpp, operações do roteador verificadas e métricas sem carga automática.
- [Mythosia.AI.Serving.Vllm](../../src/serving/Mythosia.AI.Serving.Vllm/README.md) — Cartões, saúde, versão e métricas com rótulos do vLLM; API concreta preservada.
