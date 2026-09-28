# Administrar servidores de modelos existentes

Un selector de modelos o una pantalla de operaciones necesita conocer la salud del servidor, los modelos disponibles y su estado de carga antes de enviar un prompt. Los paquetes Serving unifican estas consultas para Ollama, llama.cpp y vLLM, manteniendo explícitas las operaciones propias de cada motor.

Úselos para llenar un selector de modelos, mostrar si un servidor está accesible, gestionar la residencia de modelos cuando el motor lo permita o leer métricas. Al cambiar de motor, el código común de consulta de la aplicación puede permanecer intacto.

Estos clientes se conectan a un servidor HTTP existente. La instalación y el alojamiento del motor, el alquiler de GPU, el chat y la generación de embeddings corresponden a componentes separados. El chat sigue usando el servicio de IA adecuado, como `QwenService` para vLLM; los proveedores de embeddings de RAG siguen separados. El descubrimiento no carga modelos automáticamente. SGLang no está implementado.

## Elegir un paquete

| Paquete | Versión | Uso |
| --- | --- | --- |
| `Mythosia.AI.Serving.Abstractions` | 1.0.0 | Contratos comunes para aplicaciones o un adaptador de administración propio. Sin dependencias de paquetes. |
| `Mythosia.AI.Serving.Ollama` | 1.0.0 | Consultar Ollama, descargar modelos y precargarlos o retirarlos de memoria explícitamente. |
| `Mythosia.AI.Serving.LlamaCpp` | 1.0.0 | Consultar llama.cpp, leer métricas y administrar modelos en modo Router. |
| `Mythosia.AI.Serving.Vllm` | 1.1.0 | Consultar vLLM y leer métricas mediante las API comunes o las API existentes específicas de vLLM. |

Los cuatro paquetes están destinados a .NET Standard 2.1. Instale el adaptador que utilice; incorpora automáticamente el paquete de abstracciones. Los adaptadores dependen de los contratos comunes y Newtonsoft.Json, de forma independiente de los paquetes principales de IA y RAG.

## Consultar sin cambiar el estado del servidor

Instale el paquete concreto de su motor. El ejemplo usa Ollama; para los otros servidores seleccione `VllmServer` o `LlamaCppServer` en su espacio de nombres correspondiente. El descubrimiento utiliza solicitudes de solo lectura y no envía órdenes de carga, generación ni descarga.

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

El endpoint es la raíz del servidor, con un prefijo de ruta de proxy inverso si lo necesita. La clave API es opcional y se envía como credencial Bearer en cada solicitud. El cliente no modifica `HttpClient.DefaultRequestHeaders` ni libera el `HttpClient` proporcionado; reutilícelo y libérelo según el ciclo de vida de su aplicación. En este ejemplo de Ollama, el tiempo de espera también se aplica al cuerpo de las respuestas en streaming, por lo que debe permitir suficiente tiempo para descargar modelos.

## Contratos comunes y opcionales

| Contrato | Función |
| --- | --- |
| `IModelServer` | Información, salud, modelos y capacidades observadas del servidor. |
| `IModelLifecycle` | Órdenes explícitas de carga y retirada de memoria; opcional. |
| `IModelDownloader` | Descarga explícita con progreso; opcional. |
| `IModelMetricsProvider` | Muestras de métricas con etiquetas; opcional. |

Implementar una interfaz indica que el cliente ofrece la operación; `ServingCapabilities` informa de lo que puede comprobarse en el endpoint conectado. `Supported` no garantiza autorización ni éxito con todos los modelos. `Unsupported` significa que no está disponible en el modo o endpoint observado. `Unknown` indica pruebas insuficientes, incluidos fallos de autenticación o conexión; no debe interpretarse como falta de soporte.

`InstallationState` y `LoadState` describen observaciones distintas. `Unknown` no significa ausente ni retirado de memoria. Los valores no informados de `SizeBytes`, `MemoryBytes` o `ContextLength` siguen siendo `null`, no cero. Un endpoint de administración saludable no demuestra que un modelo concreto esté listo para inferencia.

## Diferencias entre motores

| Operación | Ollama | llama.cpp con un solo modelo | llama.cpp Router | vLLM |
| --- | --- | --- | --- | --- |
| Información, salud y lista de modelos | Sí | Sí | Sí | Sí |
| Carga / retirada de memoria explícitas | Sí, con solicitudes de generación vacías | No compatible | Sí, tras confirmar la identidad Router | No compatible con este cliente |
| Descarga de modelos | Sí, con progreso en streaming | No compatible | Operación explícita; requiere endpoint de descarga y eventos SSE | No compatible con este cliente |
| Métricas | No implementadas | Métricas del servidor cuando están activadas | Sobrecarga concreta por modelo; debe estar ya cargado | Métricas del servidor cuando están disponibles |

La tabla describe las operaciones del cliente; no garantiza que todas las versiones del servidor, configuraciones de permisos o modelos las admitan. Consulte las capacidades del endpoint conectado y gestione los errores de las operaciones.

**Ollama:** `/api/tags` proporciona los modelos registrados y `/api/ps` los que se están ejecutando. Un modelo remoto puede registrarse sin pesos locales; sin ejecución local, su estado de carga sigue siendo desconocido. La precarga usa una solicitud vacía a `/api/generate` y el keep-alive predeterminado del servidor. Los modelos exclusivos de embeddings no se redirigen a otra API. Retirar un modelo usa `keep_alive: 0` y no elimina archivos. Las métricas no están implementadas.

**llama.cpp:** `/props` debe confirmar expresamente el modo router antes de las órdenes de ciclo de vida o descarga. El modo de un solo modelo no admite estas órdenes y conserva el estado de reposo observado. Las descargas del router se suscriben a `/models/sse`, envían `POST /models` y solo finalizan con éxito al recibir `download_finished` para ese modelo. La disponibilidad de SSE por sí sola deja la capacidad de descarga desconocida. Las métricas globales corresponden al modo de un solo modelo; las del router requieren la sobrecarga concreta `GetMetricsAsync(modelId, token)`, que envía `autoload=false` para no cargar al inspeccionar.

**vLLM:** se conservan los alias publicados y el campo opcional `root`, pero los estados comunes de instalación y carga siguen siendo desconocidos. Los modelos y las métricas se comprueban mediante respuestas reales; no se admiten ciclo de vida ni descargas. Los métodos y DTO existentes de `VllmServer` siguen disponibles en el cliente concreto; los métodos comunes de salud, modelos y métricas usan interfaces explícitas.


## Ejecutar una operación de administración explícita

Las descargas y los cambios de residencia consumen red, disco o memoria del dispositivo. Ejecútelos cuando su aplicación necesite esa acción. Esta continuación del ejemplo de Ollama descarga un modelo pequeño y lo carga brevemente en memoria para observar su estado. Use los identificadores exactos del servidor, incluida la etiqueta de Ollama o la de cuantización de llama.cpp.

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

El ejemplo usa un modelo exclusivo de prueba y lo retira de memoria al terminar. Una aplicación de producción decide cuándo liberar un modelo; no retire uno que aún usan otras solicitudes. La limpieza tiene su propio plazo y puede fallar si el servidor no está disponible.

El progreso describe un artefacto o una etapa. Los contadores de bytes nulos no son cero ni un porcentaje del modelo completo. Una carga correcta confirma la orden, no la disponibilidad ni la residencia indefinida; observe `LoadState` con una espera limitada cuando la disponibilidad importe. Consulte la guía del paquete para conocer el protocolo de descarga del Router llama.cpp y sus restricciones de versión. Una operación solicitada explícitamente puede intentarse con soporte `Unknown` tras verificar la configuración del servidor; el descubrimiento de capacidades nunca la inicia por sí solo.

## Cancelación y errores

Pase un token de cancelación a consultas y órdenes. Cancelar detiene el trabajo HTTP y la espera del cliente; no garantiza cancelación remota, reversión ni eliminación de capas descargadas. Configure el `HttpClient` proporcionado para la duración de la operación; los clientes no adquieren su propiedad.

Conserve las etiquetas de las métricas al comparar modelos o motores. Las métricas ausentes no son cero y sus valores pueden incluir `NaN` o infinito. `ServingException` es el error común; los errores comunes de administración omiten cuerpos de respuesta sin procesar y credenciales. Las llamadas vLLM existentes mantienen sus detalles de error anteriores.

`GetHealthAsync` clasifica los fallos del endpoint como estados de salud y sigue propagando la cancelación del llamador. Otras operaciones pueden lanzar `ServingException`; un modo de llama.cpp que se sabe no compatible puede lanzar `NotSupportedException`. Ni un tiempo de espera agotado ni una solicitud fallida prueban que la acción remota se haya revertido. Un método de descarga solo termina correctamente después de que el motor informe de su finalización: éxito terminal seguido de EOF en Ollama o el evento `download_finished` correspondiente en llama.cpp Router.

## Qué se ha verificado

Las pruebas sin conexión cubren éxitos controlados, respuestas malformadas, errores y cancelación. Las comprobaciones separadas con servidores reales usaron una NVIDIA A40, modelos Qwen públicos pequeños y estas compilaciones de los motores:

| Motor | Modelo probado | Operaciones de administración verificadas |
| --- | --- | --- |
| Ollama 0.34.4 | `qwen2.5:0.5b` | Descubrimiento, descarga nueva, carga/retirada de memoria, errores depurados de modelo inexistente, cancelación previa y cancelación tras progreso parcial de descarga. |
| llama.cpp b11146, Router | `Qwen/Qwen2.5-0.5B-Instruct-GGUF:Q4_K_M` | Descubrimiento, eventos de descarga, carga/retirada de memoria, métricas por modelo sin carga automática, errores y cancelación de descarga. |
| llama.cpp b11146, un solo modelo | El mismo modelo GGUF | Descubrimiento, métricas del servidor, cancelación y rechazo explícito de las órdenes de ciclo de vida del Router. |
| vLLM 0.30.0 | `Qwen/Qwen2.5-0.5B-Instruct` | Descubrimiento, métricas del servidor y cancelación previa. |

Las solicitudes breves de inferencia HTTP nativa también devolvieron texto generado en las cuatro configuraciones. Confirman el funcionamiento del motor, no los adaptadores de chat de los servicios de IA, la calidad del modelo, el rendimiento ni la compatibilidad con todas las compilaciones. Los perfiles anteriores son configuraciones probadas, no versiones mínimas compatibles. Las pruebas de cancelación de descarga usaron otros modelos de prueba más grandes y no afirmaron una reversión remota. La primera descarga de Ollama falló; el reintento y una descarga nueva tras eliminar el modelo funcionaron, sin determinar la causa exacta del primer fallo.

Utilice la [guía de verificación real con activación explícita](https://github.com/AJ-comp/Mythosia.AI/blob/main/tests/Mythosia.AI.Serving.Live/README.md) para comprobar el endpoint desplegado. Distingue el ejecutor de administración incluido en el repositorio de las pruebas adicionales de inferencia y cancelación usadas durante la verificación. Los informes detallados de ejecución se mantienen fuera de la documentación publicada.

## Guías de los paquetes

- [Mythosia.AI.Serving.Abstractions](../../src/serving/Mythosia.AI.Serving.Abstractions/README.md) — Contratos comunes e instantáneas inmutables de servidores, modelos y capacidades.
- [Mythosia.AI.Serving.Ollama](../../src/serving/Mythosia.AI.Serving.Ollama/README.md) — Inventario y salud de Ollama, precarga/retirada explícitas y descargas en streaming.
- [Mythosia.AI.Serving.LlamaCpp](../../src/serving/Mythosia.AI.Serving.LlamaCpp/README.md) — Consultas llama.cpp, operaciones router verificadas y métricas sin carga automática.
- [Mythosia.AI.Serving.Vllm](../../src/serving/Mythosia.AI.Serving.Vllm/README.md) — Fichas, salud, versión y métricas etiquetadas de vLLM; se conserva la API concreta.
