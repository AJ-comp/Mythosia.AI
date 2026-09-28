# 管理執行中的模型伺服器

模型選擇介面或維運工具需要在傳送提示詞前了解伺服器健康狀態、可用模型及載入狀態。Serving 套件為 Ollama、llama.cpp 和 vLLM 提供統一查詢介面，並讓執行階段專屬操作維持明確。

可以用它們填入模型選擇清單、顯示伺服器是否可連線、在執行階段支援時管理模型常駐，或讀取引擎指標。更換執行階段時，應用程式的共用查詢程式碼可以維持不變。

這些用戶端連接既有 HTTP 伺服器。引擎安裝與託管、GPU 租用、聊天及嵌入產生由其他元件負責。聊天繼續透過適當的 AI 服務進行，例如 vLLM 使用 `QwenService`；RAG 嵌入提供者維持獨立。探索過程不會自動載入模型。尚未實作 SGLang。

## 選擇套件

| 套件 | 版本 | 用途 |
| --- | --- | --- |
| `Mythosia.AI.Serving.Abstractions` | 1.0.0 | 為應用程式碼或自訂管理配接器提供共用契約。無套件相依性。 |
| `Mythosia.AI.Serving.Ollama` | 1.0.0 | 查詢 Ollama、下載模型，以及明確預先載入或卸載模型。 |
| `Mythosia.AI.Serving.LlamaCpp` | 1.0.0 | 查詢 llama.cpp、讀取指標，以及在 Router 模式下管理模型。 |
| `Mythosia.AI.Serving.Vllm` | 1.1.0 | 透過共用 API 或既有 vLLM 專屬 API 查詢 vLLM 並讀取指標。 |

這四個套件皆以 .NET Standard 2.1 為目標。安裝所需的配接器即可自動引入抽象契約套件。配接器相依於共用契約和 Newtonsoft.Json，獨立於核心 AI 與 RAG 套件。

## 在不變更伺服器狀態的情況下查詢

安裝所用執行階段的具體套件。以下範例使用 Ollama；其他伺服器請選擇對應命名空間中的 `VllmServer` 或 `LlamaCppServer`。探索過程只使用唯讀請求，不傳送載入、生成或下載命令。

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

端點應為伺服器根位址，可包含反向 Proxy 的路徑前置詞。API 金鑰為選用，以 Bearer 認證資訊隨每次要求傳送。用戶端不會變更 `HttpClient.DefaultRequestHeaders`，也不會處置傳入的 `HttpClient`；請依應用程式生命週期重複使用及處置它。在此 Ollama 範例中，逾時也適用於串流回應本文，因此應為模型下載預留足夠時間。

## 共用與選用契約

| 契約 | 用途 |
| --- | --- |
| `IModelServer` | 伺服器資訊、健康狀態、模型及已觀察到的功能。 |
| `IModelLifecycle` | 明確的載入與卸載命令，選用。 |
| `IModelDownloader` | 帶進度的明確下載操作，選用。 |
| `IModelMetricsProvider` | 保留標籤的指標樣本，選用。 |

實作介面表示用戶端具有該操作；`ServingCapabilities` 表示可從已連線端點確認的支援狀況。`Supported` 不保證對每個模型都有權限或都能成功。`Unsupported` 表示在觀察到的模式或端點上無法使用。`Unknown` 表示證據不足，包括驗證或連線失敗，不能將其當成不支援。

`InstallationState` 和 `LoadState` 是不同的觀察結果。`Unknown` 既不表示不存在，也不表示已卸載。缺少的 `SizeBytes`、`MemoryBytes`、`ContextLength` 保留為 `null`，而非零。管理端點正常不代表某個模型已經可以推論。

## 執行階段差異

| 操作 | Ollama | llama.cpp 單模型 | llama.cpp Router | vLLM |
| --- | --- | --- | --- | --- |
| 資訊、健康狀態與模型清單 | 支援 | 支援 | 支援 | 支援 |
| 明確載入 / 卸載 | 支援，使用空的生成要求 | 不支援 | 確認 Router 身分後支援 | 此用戶端不支援 |
| 模型下載 | 支援，帶串流進度 | 不支援 | 明確操作；需要下載端點和 SSE 事件 | 此用戶端不支援 |
| 指標 | 尚未實作 | 啟用後可讀取伺服器指標 | 具體類別的模型專屬多載；模型必須已載入 | 可用時讀取伺服器指標 |

此表說明用戶端提供的操作，不保證每個伺服器版本、權限設定或模型都支援這些操作。請檢查連線端點的功能並處理操作失敗。

**Ollama：** `/api/tags` 提供已註冊模型，`/api/ps` 提供目前執行個體。遠端模型可以在沒有本機權重的情況下註冊；若無本機執行個體，其載入狀態仍未知。預先載入使用空的 `/api/generate` 請求和伺服器預設 keep-alive。不會將僅用於嵌入的模型自動轉到其他端點。卸載使用 `keep_alive: 0`，不會刪除檔案。尚未實作指標功能。

**llama.cpp：** 生命週期或下載命令執行前，必須透過 `/props` 明確確認路由器模式。單模型模式不支援這些命令，並保留觀察到的休眠狀態。路由器下載先訂閱 `/models/sse`，再提交 `POST /models`，只有目標模型的 `download_finished` 事件才代表成功。僅有 SSE 可用仍不能確認下載功能。伺服器級指標用於單模型模式；路由器指標需要具體類別的 `GetMetricsAsync(modelId, token)` 多載，該多載傳送 `autoload=false`，避免查詢自動載入模型。

**vLLM：** 保留服務別名和選用的 `root` 欄位，但共用安裝與載入狀態均維持未知。模型和指標依實際回應檢查；不支援生命週期及下載。具體用戶端繼續提供既有 `VllmServer` 方法和 DTO，共用健康、模型及指標方法透過明確介面提供。


## 明確執行管理操作

下載和常駐狀態變更會消耗網路、磁碟或裝置記憶體。應用程式需要時再執行。以下程式碼延續前面的 Ollama 範例，下載一個小模型並短暫載入以觀察狀態。請使用伺服器的精確模型 ID，包括 Ollama 標籤或 llama.cpp 量化標籤。

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

範例使用專用的測試模型，並在結束後卸載。正式應用程式應自行決定何時釋放模型；不要卸載仍被其他要求使用的模型。清理操作有獨立的截止時間，伺服器無法使用時可能失敗。

進度描述個別檔案或階段。可為空的位元組計數不代表零，也不是整個模型的百分比。載入呼叫成功僅表示命令已獲確認，不表示就緒或無限期常駐；需要就緒狀態時，應在有限等待時間內觀察 `LoadState`。llama.cpp Router 下載協定和版本限制請參閱具體套件指南。明確要求的操作在確認伺服器設定後，即使支援狀態為 `Unknown` 也可嘗試，但功能探索本身絕不會發起該操作。

## 取消與錯誤

為查詢和命令傳遞取消權杖。取消會停止此用戶端的 HTTP 工作和等待，不保證遠端取消、復原或清除已下載層。請依操作時間配置傳入的 `HttpClient`；用戶端不接管其所有權。

比較模型或引擎時保留指標標籤。缺少指標不是零，數值可能包含 `NaN` 或無窮大。`ServingException` 是共用錯誤型別；共用管理錯誤不包含原始回應本文或認證資訊。既有 vLLM 專屬呼叫保留舊版錯誤詳細資訊。

`GetHealthAsync` 將端點失敗歸類為健康狀態，但仍會傳播呼叫端取消。其他操作可能擲回 `ServingException`，已知不支援的 llama.cpp 模式可能擲回 `NotSupportedException`。逾時或要求失敗都不能證明遠端操作已復原。下載方法僅在執行階段回報完成後才成功傳回：Ollama 需要終止成功訊息及其後的 EOF；llama.cpp Router 需要相符的 `download_finished` 事件。

## 已驗證的範圍

離線測試涵蓋受控的成功回應、格式錯誤回應、錯誤和取消。另行執行的真實伺服器檢查使用一張 NVIDIA A40、小型公開 Qwen 模型及以下引擎組建：

| 執行階段 | 測試模型 | 已驗證的管理操作 |
| --- | --- | --- |
| Ollama 0.34.4 | `qwen2.5:0.5b` | 探索、全新下載、載入/卸載、已清除敏感資訊的模型缺失錯誤、預先取消，以及部分下載進度後的取消。 |
| llama.cpp b11146, Router | `Qwen/Qwen2.5-0.5B-Instruct-GGUF:Q4_K_M` | 探索、下載事件、載入/卸載、無自動載入的模型專屬指標、錯誤及下載取消。 |
| llama.cpp b11146，單模型 | 相同的 GGUF 模型 | 探索、伺服器指標、取消，以及明確拒絕 Router 生命週期命令。 |
| vLLM 0.30.0 | `Qwen/Qwen2.5-0.5B-Instruct` | 探索、伺服器指標及預先取消。 |

簡短的原生 HTTP 推論要求也在四種設定中傳回了生成文字。這些要求驗證的是引擎運作情況，而非 AI 服務聊天配接器、模型品質、輸送量或與所有引擎組建的相容性。以上設定是實際測試設定，不是最低支援版本。下載取消檢查使用了其他較大的測試模型，未斷言遠端復原。第一次 Ollama 下載失敗；重試和刪除模型後的全新下載皆通過，但未確定首次失敗的確切原因。

請使用[需明確啟用的真實伺服器驗證指南](https://github.com/AJ-comp/Mythosia.AI/blob/main/tests/Mythosia.AI.Serving.Live/README.md)檢查部署的端點。指南區分了已納入存放庫的管理測試執行器，以及驗證期間額外使用的推論和取消探測。詳細執行報告不納入公開文件。

## 各套件指南

- [Mythosia.AI.Serving.Abstractions](../../src/serving/Mythosia.AI.Serving.Abstractions/README.md) — 共用管理契約與不可變的伺服器、模型及功能快照。
- [Mythosia.AI.Serving.Ollama](../../src/serving/Mythosia.AI.Serving.Ollama/README.md) — Ollama 模型清單、健康狀態、明確預載/卸載及串流下載。
- [Mythosia.AI.Serving.LlamaCpp](../../src/serving/Mythosia.AI.Serving.LlamaCpp/README.md) — llama.cpp 查詢、路由器確認後的生命週期/下載及無自動載入的指標。
- [Mythosia.AI.Serving.Vllm](../../src/serving/Mythosia.AI.Serving.Vllm/README.md) — vLLM 模型卡、健康狀態、版本及附標籤指標；保留既有具體 API。
