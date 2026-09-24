# Возможности провайдеров

> Примеры с `CreateRequest` требуют Mythosia.AI 8.0.0 / Abstractions 4.0.0. В прежней версии 7.1, добавившей Run и общие параметры, билдера нет. Старые пакеты могут использовать прежние перегрузки сервиса.

<a id="image-options-migration"></a>
Режимы и возвращаемые данные зависят от провайдера, модели и API. Проверяйте [общую настройку скорости](request-building.md#inference-speed) и capabilities; запрос Fast ещё не подтверждает его применение.

## Миграция на типизированные параметры изображений

Выбирайте качество и формат через enum с автодополнением и различайте точные пиксели и класс разрешения. Это исключает опечатки в строках и незаметное преобразование заданных размеров в другую категорию.

Несовместимое изменение Mythosia.AI 8.0.0: `Quality`, `Background` и `OutputFormat` становятся enum, `Size` — `ImageSize`, отдельное свойство запроса `AspectRatio` удаляется. Значение по умолчанию для `OutputFormat` теперь `ImageOutputFormat.Auto`. Методы генерации и редактирования сохраняются.

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

`Pixels(width, height)` запрашивает точные размеры. `Preset(resolution, aspectRatio)` задаёт класс разрешения и пропорции; пиксельные размеры определяет провайдер. Без ограничений используйте `ImageSize.Auto`. Переходите от пикселей к пресетам только если допустимы приблизительные размеры.

| Provider | `Size` | `OutputFormat` |
| --- | --- | --- |
| OpenAI | `ImageSize.Auto`, `Pixels(...)` | `Auto` → PNG, `Png`, `Jpeg`, `WebP` |
| Google | `ImageSize.Auto`, `Preset(...)` | `Auto`, `Jpeg` |
| xAI | `ImageSize.Auto`, `Preset(...)` | `Auto` |

Неопределённые enum и неподдерживаемые сочетания отклоняются до HTTP. Наличие значения не означает поддержку всеми моделями. Google принимает только `ImageQuality.Auto`, xAI — `Auto`, `Low`, `Medium`.

После того как `EditImagesAsync` вернёт `Task`, входные буферы можно использовать повторно. Начатый запрос хранит собственные данные изображений, включая байты маски OpenAI; последующие изменения исходных массивов `ImageInput.Data` не меняют загружаемые данные.

Чтобы прерванный результат не сохранялся как готовое изображение, генерация и редактирование Google требуют, чтобы каждый возвращённый кандидат завершался с `finishReason: STOP`. Если хотя бы один кандидат заблокирован, не завершён или не содержит этого конечного статуса, весь вызов завершается исключением `AIServiceException`. Отсутствующие или некорректные встроенные данные base64 либо MIME-метаданные изображения также приводят к ошибке всего вызова; формат PNG не предполагается. Эти проверки не подтверждают соответствие байтов файла заявленному формату изображения.

## OpenAI (OpenAIService)

> Поддержка GPT-6 Astra и асинхронных вызовов инструментов доступна с `Mythosia.AI` 7.1.0; общие типы включены в `Mythosia.AI.Abstractions` 3.1.0.

Если инструмент долго загружает данные, GPT-6 Astra / Sol / Luna может в это время продолжать независимые пояснения или другие части задачи. `FunctionDefinition.AllowAsync = true` или `FunctionBuilder.WithAsync()` разрешает асинхронные вызовы для GPT-6 Astra / Sol / Luna через Responses. По умолчанию используется `false`; неподдерживаемые модели ждут результата того же обработчика. Примеры и жизненный цикл запроса описаны в [руководстве по вызовам функций](function-calling.md).

<a id="gpt-6-sol-luna"></a>

### GPT-6 Sol / Luna (ещё не опубликовано)

Выбирайте GPT-6 Sol для сложного программирования, работы с инструментами и агентных задач, а Luna — для больших объёмов текста или изображений при ограниченном бюджете. Оба используют существующие API полного ответа, потоковой передачи и Run, поэтому смена модели не меняет порядок вызовов приложения.

> Это ещё не опубликованное дополнение; нужны совместимые сборки core и abstractions. Опубликованные Mythosia.AI 8.0.0 / Abstractions 4.0.0 не содержат `Gpt6Sol`, `Gpt6Luna` или `Gpt6Reasoning.None`. Минимальные версии для прежних функций Astra и модель службы по умолчанию не меняются.

Используйте `AIModels.OpenAI.Gpt6Sol` (`gpt-6-sol`) или `AIModels.OpenAI.Gpt6Luna` (`gpt-6-luna`). Обе модели принимают текст и изображения и выводят текст: контекст 1 050 000 токенов, максимум входа 922 000, выхода 128 000. Вход, рассуждение и выход вместе должны укладываться в контекст. `MaxTokens` задаёт запрошенный бюджет вывода, а не размер контекста.

```csharp
using Mythosia.AI.Models;
using Mythosia.AI.Services.OpenAI;

var service = new OpenAIService(apiKey, httpClient);
service.ChangeModel(AIModels.OpenAI.Gpt6Sol);
await using var run = await service.CreateRequest("Review this design.")
    .WithReasoning(ReasoningLevel.High)
    .StartRunAsync(onText: text => Console.Write(text));
var result = await run.Result;

service.ChangeModel(AIModels.OpenAI.Gpt6Luna);
string answer = await service.CreateRequest("Summarize this paragraph.")
    .WithReasoning(ReasoningLevel.None)
    .WithTemperature(0.2f)
    .GetCompletionAsync();
```

`Auto` соответствует `Medium`. Sol/Luna поддерживают `None`, `Low`, `Medium`, `High`, `XHigh`, `Max`, но не `Minimal`. Для запроса используйте `WithReasoning(ReasoningLevel.None)`, а в `WithGpt6Parameters` — `Gpt6Reasoning.None`. Только Sol/Luna с `None` передают `Temperature` / `TopP`; при включённом рассуждении поля опускаются. Astra всегда требует рассуждения и не передаёт параметры сэмплирования. `AIRequestProfile.DisableReasoning` выбирает `None` для Sol/Luna и `Low` в режиме Standard для Astra, без сводки рассуждений.

`Gpt6ReasoningMode.Standard` и `.Pro` используют тот же ID выбранной модели. Все три GPT-6 поддерживают инструменты через Responses, необязательные асинхронные инструменты, дополнительные указания через WebSocket Run и смену рассуждения с сохранением кеша в одноагентном режиме Standard. Проверьте `run.CanSteer`; принятие указания не отменяет прошлый вывод. `WithSpeed(InferenceSpeed.Fast)` запрашивает платную обработку Fast независимо от уровня рассуждения. Проверяйте фактический режим в `result.Processing`: права аккаунта и снижение уровня сервером не определяются локальной поддержкой.

[GPT-6 Sol](https://developers.openai.com/api/docs/models/gpt-6-sol) · [GPT-6 Luna](https://developers.openai.com/api/docs/models/gpt-6-luna) · [Reasoning](https://developers.openai.com/api/docs/guides/reasoning) · [Fast](https://developers.openai.com/api/docs/guides/fast-mode)

### Уровень рассуждений

Баланс между скоростью и глубиной анализа:

```csharp
using Mythosia.AI.Models;

// GPT-6 Astra
service.ChangeModel(AIModels.OpenAI.Gpt6Astra);
service.WithGpt6Parameters(
    reasoningEffort: Gpt6Reasoning.Medium, // Auto, Low, Medium, High, XHigh, Max
    verbosity: Verbosity.Medium,
    reasoningSummary: ReasoningSummary.Auto,
    reasoningMode: Gpt6ReasoningMode.Standard);

// GPT-5.6: Sol — флагманская модель; Terra и Luna — более экономичные варианты.
service.ChangeModel(AIModels.OpenAI.Gpt5_6Sol);
service.WithGpt5_6Parameters(
    reasoningEffort: Gpt5_6Reasoning.Medium, // None, Low, Medium, High, XHigh, Max
    verbosity: Verbosity.Medium);            // Low, Medium, High

service.ChangeModel(AIModels.OpenAI.Gpt5_4);
service.Gpt5_4ReasoningEffort = Gpt5_4Reasoning.High;

service.ChangeModel(AIModels.OpenAI.Gpt5_2);
service.Gpt5_2ReasoningEffort = Gpt5_2Reasoning.Medium;

```

GPT-6 Astra по умолчанию использует Responses API; вызовы функций требуют этот API. `Auto` соответствует значению библиотеки по умолчанию — `Medium`; значения `None` и `Minimal` недоступны. `AIRequestProfile.DisableReasoning = true` задаёт уровень `Low` в режиме `Standard` и исключает сводку рассуждений. Выберите `Gpt6ReasoningMode.Pro` для режима Pro с тем же идентификатором модели `gpt-6-astra`.

Для общих задач используйте [рассуждение и нативный поиск](reasoning-and-search.md); описанные ниже настройки отдельных провайдеров остаются доступны.

### Преобразование текста в речь (TTS)

```csharp
byte[] audio = await service.GetSpeechAsync(
    inputText: "Привет!",
    voice: "alloy",
    model: "tts-1"
);

await File.WriteAllBytesAsync("output.mp3", audio);
```

### Распознавание речи (STT)

```csharp
byte[] audioData = await File.ReadAllBytesAsync("recording.mp3");

string transcript = await service.TranscribeAudioAsync(
    audioData: audioData,
    fileName: "recording.mp3",
    language: "ru"
);
```

`TranscribeAudioAsync` использует `gpt-transcribe`; публичная сигнатура не меняется.

### Генерация изображений

#### GPT Image 2.5

Выбирайте Flare для быстрого создания визуальных эскизов, а Sunburst — когда правки должны точно следовать подробным инструкциям. Обе модели создают и редактируют изображения через существующий `IImageGenerationService`; выбор модели изображений не меняет модель чата.

| Модель | Когда выбирать |
| --- | --- |
| `AIModels.OpenAI.GptImage2_5Flare` | Быстрая и качественная генерация изображений для повседневных задач. |
| `AIModels.OpenAI.GptImage2_5Sunburst` | Генерация и правки, для которых особенно важна точность редактирования. |

Явно задайте `ImageGenerationRequest.Model` или унаследованное свойство в `ImageEditRequest`. По умолчанию OpenAI по-прежнему использует `AIModels.OpenAI.GptImage2`. Псевдонимы: `gpt-image-2.5-flare` и `gpt-image-2.5-sunburst`. Для фиксации снимков от 8 сентября 2026 года используйте `GptImage2_5Flare_260908` или `GptImage2_5Sunburst_260908` (соответствующие ID оканчиваются на `-2026-09-08`).

Создайте эскиз с Flare:

```csharp
using Mythosia.AI.Models;
using Mythosia.AI.Models.Images;
using Mythosia.AI.Services;
using Mythosia.AI.Services.OpenAI;

IImageGenerationService images = new OpenAIService(apiKey, httpClient);
var generated = await images.GenerateImagesAsync(new ImageGenerationRequest
{
    Model = AIModels.OpenAI.GptImage2_5Flare,
    Prompt = "Стеклянный павильон на рассвете, архитектурный концепт",
    Size = ImageSize.Pixels(1024, 1024),
    Quality = ImageQuality.Low,
    OutputFormat = ImageOutputFormat.Png
});

await File.WriteAllBytesAsync("pavilion.png", generated.Images[0].Data);
```

Затем отредактируйте созданное изображение с Sunburst:

```csharp
var edited = await images.EditImagesAsync(new ImageEditRequest
{
    Model = AIModels.OpenAI.GptImage2_5Sunburst,
    Prompt = "Сохрани конструкцию павильона, убери окружающий пейзаж и сделай фон прозрачным.",
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

Для обеих моделей и их снимков `Quality` принимает `Auto`, `Low`, `Medium`, `High`, `XHigh` и `Max`. Используйте низкое качество для черновиков и сравнивайте высокие уровни для итоговых материалов. `OutputFormat`: `Auto` / `Png`, `Jpeg` или `WebP`; `OutputCompression` — 0–100 только для JPEG/WebP. Фон `Transparent` требует PNG/WebP. `Count` — 1–10.

`Size`: `ImageSize.Auto` или `ImageSize.Pixels(width, height)`. Стороны кратны 16, пропорции 1:3–3:1, максимум 3840 пикселей на сторону, площадь 655360–8294400 пикселей. Размеры выше 2560×1440 экспериментальны. OpenAI отклоняет `Preset`.

Редактирование принимает 1–16 непустых JPEG/PNG/WebP-образцов размером менее 50 MiB каждый. Необязательная маска должна быть PNG/WebP меньше 50 MiB, совпадать с первым образцом по формату и пиксельным размерам и содержать альфа-канал. Библиотека проверяет MIME и длину в байтах; размеры и альфа-канал проверяет провайдер.

Примеры используют существующие пути Image API: возврат байтов и multipart-редактирование. Инструменты Responses `image_generation`, поток частичных изображений и `input_fidelity` в этой интеграции не предоставляются. Читайте `GeneratedImage.Data` и `MediaType` из результата.

См. официальное [руководство по изображениям](https://developers.openai.com/api/docs/guides/image-generation), страницы [Sunburst](https://developers.openai.com/api/docs/models/gpt-image-2.5-sunburst) и [Flare](https://developers.openai.com/api/docs/models/gpt-image-2.5-flare).

---

## Anthropic (AnthropicService)

[Claude Fable 5.1](fable-5-1.md) поддерживает сообщения о ходе работы, инструкции для одного хода и диагностику привязки thinking начиная с `Mythosia.AI` 8.0.0 / `Mythosia.AI.Abstractions` 4.0.0. Mythos 5.1 доступен по приглашению. Оба отклоняют принудительный выбор инструмента.

<a id="claude-opus-55"></a>

### Claude Opus 5.5: показывать ход длительных задач с инструментами

Opus 5.5 подходит для проверки кода и исследования документов с несколькими раундами инструментов. API completion и Run остаются прежними, но прогресс по умолчанию скрыт, а сохранение рассуждений требует аккуратной работы с историей. Это ещё не выпущенное дополнение; опубликованные пакеты 8.0.0 / 4.0.0 его не содержат.

`ClaudeOpus5_5` выбирает `claude-opus-5-5`: текст/изображения на входе, текст на выходе, контекст 1M и максимум 128K выходных токенов. На 2026-09-24 стандартная цена ввода/вывода — $4/$20 за миллион токенов; специальные режимы и инструменты оплачиваются отдельно. [Официальные данные модели](https://platform.claude.com/docs/en/models/opus-5-5/overview).

При неизменённых настройках сервиса `Auto` использует усилие `Medium` и не возвращает читаемые рассуждения. Адаптивное мышление всегда включено. Выберите `Low`, `Medium`, `High`, `XHigh` или `Max`; общие значения `ReasoningLevel.None` и `Minimal` отклоняются. Модель сервиса по умолчанию не меняется.

```csharp
using Mythosia.AI.Models;
using Mythosia.AI.Models.Streaming;
using Mythosia.AI.Services.Anthropic;

var claude = new AnthropicService(apiKey, httpClient);
claude.ChangeModel(AIModels.Anthropic.ClaudeOpus5_5);
claude.WithAdaptiveThinkingParameters(
    ClaudeReasoningEffort.Medium, ClaudeThinkingDisplay.Updates);

await using var run = await claude.StartRunAsync(
    "Review the migration plan using the registered tools.",
    options: StreamOptions.FullOptions);

await foreach (var item in run.StreamAsync())
{
    if (item.Type == StreamingContentType.Reasoning)
        Console.WriteLine(item.Content);
    else if (item.Type == StreamingContentType.Text)
        Console.Write(item.Content);
}
string answer = (await run.Result).Text;
```

Пример запрашивает `Updates` и наблюдает `StreamingContentType.Reasoning`. `Summarized` возвращает краткое изложение рассуждений, `Omitted` скрывает его. У `WithAdaptiveThinkingParameters(effort)` аргумент отображения по-прежнему равен `Summarized` по умолчанию, в отличие от ненастроенного сервиса. После обычного completion читайте `LastThinkingContent`. Регулярный интервал обновлений не гарантируется.

Положительный прежний `ThinkingBudget` преобразуется в high/xhigh/max, а не в точный бюджет токенов. Ноль и отрицательные значения не отключают мышление. Профиль с отключением рассуждений использует low и скрывает читаемый текст. `MaxTokens` включает скрытые рассуждения и ответ; при миграции заново оцените лимит вывода и стоимость.

Mythosia сохраняет подписанные блоки thinking, включая пустые, между ходами разговора и результатами инструментов. Продолжайте тот же сервис и чат; не переписывайте ранние сообщения, system и tools, если хотите сохранить рассуждения. Доступны `WithTurnInstruction`, `WithConversationInstruction` и `CachePreservation.Required`. `WithThinkingBinding` выбирает `Error` или `DropBlock`, а `LastInputTransformations` показывает сообщённые удаления. Drop отбрасывает рассуждения. [Руководство по истории](fable-5-1.md) описывает общие настройки; значения по умолчанию и совместимость моделей определяются правилами Opus 5.5.

Не задавайте `ForceFunctionName`; обычный выбор инструментов и `FunctionsDisabled` поддерживаются. Prefill ассистента отклоняется, параметры sampling не отправляются. Opus 5.5 не читает thinking Fable/Mythos; в Claude API Fable 5.1 и Mythos 5.1 читают thinking Opus 5.5. При смене модели прежние рассуждения могут потеряться. Дополнение не предоставляет нативный computer toolset, task budgets, изменение инструментов в диалоге, серверное сжатие и автоматический серверный fallback. [Требования миграции](https://platform.claude.com/docs/en/models/opus-5-5/migration-guide) · [Нативные возможности](https://platform.claude.com/docs/en/models/opus-5-5/whats-new-opus-5-5).

В Opus 5.5 прямое изменение содержимого сохранённого ответа assistant вызывает `InvalidOperationException` до HTTP-запроса; `DropBlock` также не разрешает переписывать подписанный ответ. Передайте исправление новым сообщением пользователя или начните новый разговор. Изменения прежнего содержимого user/system подчиняются политике привязки префикса провайдера.

Opus 5.5 fast mode доступен через [WithSpeed](request-building.md#inference-speed) в прямой Claude API при наличии прав. Уровень рассуждений сохраняется, применяется платный ускоренный режим.

### Подсчёт токенов (нативный API)

`GetInputTokenCountAsync` доступен у всех провайдеров ([см. генерация текста](completions.md#подсчёт-токенов)). Anthropic вызывает официальный эндпоинт `messages/count_tokens`, возвращая **точное** количество токенов:

```csharp
uint tokens = await service.GetInputTokenCountAsync("Текст промпта");
uint total = await service.GetInputTokenCountAsync();
```

---

## Google (GoogleAIService)

Для анализа длинных документов и задач с несколькими раундами инструментов можно выбрать Gemini 3.7 Flash или 3.8 Flash в существующем адаптере Google. Поддержка доступна с `Mythosia.AI` 8.0.0 / `Mythosia.AI.Abstractions` 4.0.0; модель по умолчанию остаётся Gemini 3.6 Flash.

### Глубина рассуждений

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
    .CreateRequest("Сравни постепенное и blue-green развёртывание, включая риски отката.")
    .WithReasoning(ReasoningLevel.High)
    .GetCompletionAsync();
```

Используйте `Low` для первого обзора и `High` для сложной проверки; дополнительные рассуждения могут увеличить задержку и расход токенов. Обе модели принимают `Low`, `Medium` и `High`, но не `Minimal` и `None`. `GeminiThinkingLevel.Auto` не передаёт переопределение; у 3.8 значение провайдера по умолчанию — `Medium`. `ThinkingLevel` задаёт основу сервиса, а `WithReasoning(...)` переопределяет её для одного логического запроса. Адаптер не отправляет `temperature`, `topP` и `topK`. Лимиты провайдера: 1 048 576 входных и 65 536 выходных токенов. [Gemini 3.7 Flash](https://ai.google.dev/gemini-api/docs/models/gemini-3.7-flash), [Gemini 3.8 Flash](https://ai.google.dev/gemini-api/docs/models/gemini-3.8-flash).

<a id="google-image-options"></a>

### Разрешения и пропорции изображений по моделям Google

| Модель | `Resolutions` | `AspectRatios` |
| --- | --- | --- |
| `gemini-3.1-flash-image` | `Auto`, `FiveTwelve` (512), `OneK` (1K), `TwoK` (2K), `FourK` (4K) | 14 + `Auto` |
| `gemini-3.1-flash-lite-image` | `Auto`, `OneK` (1K) | 14 + `Auto` |
| `gemini-3-pro-image` | `Auto`, `OneK` (1K), `TwoK` (2K), `FourK` (4K) | 10 + `Auto` |

10 стандартных пропорций: `1:1`, `2:3`, `3:2`, `3:4`, `4:3`, `4:5`, `5:4`, `9:16`, `16:9`, `21:9`. Набор из 14 дополнительно включает `1:4`, `4:1`, `1:8`, `8:1`. Все модели также допускают `ImageAspectRatio.Auto`.

Используйте `ImageSize.Auto` или `ImageSize.Preset(resolution, aspectRatio)`. `Auto` не отправляет соответствующий параметр. `GetImageCapabilities(model)` и `GenerateImagesAsync` / `EditImagesAsync` используют одинаковые списки для выбранной модели. Неподдерживаемые явные значения вызывают `NotSupportedException` до HTTP; изменения размера и запасного запроса нет. Неизвестные пользовательские ID сохраняют `Unknown` и передаются без изменений после общей проверки параметров провайдера.

Для Flash-Lite [страница модели](https://ai.google.dev/gemini-api/docs/models/gemini-3.1-flash-lite-image) и текст руководства указывают 1K, но в [таблице](https://ai.google.dev/gemini-api/docs/generate-content/image-generation#aspect_ratios_and_image_size) есть и столбец 512. Пока расхождение не проверено, библиотека осторожно допускает только 1K; это не утверждение о наблюдавшемся отказе сервера для 512.

---

## xAI (XAIService)

<a id="grok-47"></a>

### Grok 4.7

Чтобы после быстрого черновика тщательно проверить код или документы, выберите Grok 4.7 и настройте уровень рассуждения для каждого запроса. Используются прежние API ответа, потоковой передачи, Run, локальных инструментов, структурированного вывода и изображений. `grok-4.7` принимает текст и изображения, возвращает текст; окно контекста — 500 000 токенов. Нужны согласованные неопубликованные сборки core и abstractions; опубликованные пакеты 8.0.0 / 4.0.0 этого дополнения не содержат. Модель по умолчанию остаётся Grok 4.5.

```csharp
using Mythosia.AI.Extensions;
using Mythosia.AI.Models;
using Mythosia.AI.Services.xAI;

var grok = new XAIService(apiKey, httpClient);
grok.ChangeModel(AIModels.xAI.Grok4_7);
grok.WithGrokReasoning(GrokReasoning.Low);

var request = grok.CreateRequest("Review this deployment plan and its rollback risks.")
    .WithReasoning(ReasoningLevel.XHigh)
    .WithSpeed(InferenceSpeed.Fast);

await using var run = await request.StartRunAsync();
await foreach (var content in run.StreamAsync())
    Console.Write(content.Content);
var result = await run.Result;
Console.WriteLine(result.Text);
foreach (var processing in result.Processing)
    Console.WriteLine(processing.AppliedSpeed);
```

Поддерживаются `Low`, `Medium`, `High` и `XHigh`. Нативный `GrokReasoning.Auto` опускает `reasoning_effort`, сохраняя значение провайдера `High`; общий `ReasoningLevel.Auto` также опускает поле и использует значение провайдера `High` для этого запроса. `None`, `Minimal` и `Max` отклоняются до отправки. `WithReasoning(...)` действует на логический запрос, включая раунды инструментов и исправления структурированного вывода; `WithGrokReasoning(...)` задаёт базовые настройки. Внутренний профиль `DisableReasoning` использует `Low`. Необязательные сводки не являются полным внутренним рассуждением.

`WithSpeed(InferenceSpeed.Standard)` отправляет `service_tier: "default"`; `Fast` отправляет `"priority"` на поддерживаемые адреса xAI и может стоить дороже. `ProviderDefault` ничего не переопределяет. Сервер может снизить приоритет до обычного режима; фактически сообщённый уровень смотрите в `result.Processing`. Это приоритетная обработка `grok-4.7`, а не отдельная версия «Grok 4.7 Fast» для Cursor/Grok Build, у которой нет публичного API ID.

`GetCapabilities()` описывает выбранный запрос локально и не проверяет права аккаунта. Интеграция использует Chat Completions. Шифрованное рассуждение Responses, размещённый поиск Web/X, нативные асинхронные инструменты, изменения с сохранением кеша и `SteerAsync` здесь не подключены. Клиентские функции используют существующий локальный цикл инструментов; `run.CanSteer` равен false.

[Grok 4.7](https://docs.x.ai/developers/grok-4-7) · [reasoning_effort](https://docs.x.ai/developers/model-capabilities/text/reasoning) · [Priority Processing](https://docs.x.ai/developers/advanced-api-usage/priority-processing)

### Выбор усилия под задачу

Для быстрого черновика используйте меньший уровень усилия, а для сложных проверок, где качество важнее времени ответа, выделите больше рассуждения. Чтобы использовать дополнительный уровень `XHigh`, явно выберите Grok 4.6.

```csharp
using Mythosia.AI.Extensions;
using Mythosia.AI.Models;
using Mythosia.AI.Services.xAI;

var grok = new XAIService(apiKey, httpClient);
grok.ChangeModel(AIModels.xAI.Grok4_6);
grok.WithGrokReasoning(GrokReasoning.Low);

string review = await grok
    .CreateRequest("Сравни поэтапное и сине-зелёное развёртывание, включая восстановление после отказов.")
    .WithReasoning(ReasoningLevel.XHigh)
    .GetCompletionAsync();
```

Grok 4.6 поддерживает `Low`, `Medium`, `High` и `XHigh` (`GrokReasoning.XHigh`). `Auto` опускает `reasoning_effort`, сохраняя стандартное значение провайдера `High`; `None` не отключает рассуждение. Большее усилие может увеличить задержку и расход токенов. Для совместимости моделью по умолчанию в `XAIService` остаётся Grok 4.5. Версия 4.5 поддерживает от `Low` до `High`, а 4.3 — от `None` до `High`; адаптер отклоняет `XHigh` для этих прежних моделей до отправки.

`WithGrokReasoning(...)` и существующий `WithGrokParameters(...)` задают базовую настройку сервиса. В Grok 4.6 общий `WithReasoning(...)` переопределяет её для одного логического запроса, включая раунды инструментов и исправления структурированного вывода, а затем восстанавливает. Внутренние профили `DisableReasoning` используют `Low` для этой всегда рассуждающей модели. Обновления с сохранением кеша и размещённый веб-поиск/поиск файлов для xAI через эти общие параметры не интегрированы.

Grok 4.6 может возвращать сводки рассуждения от провайдера как `StreamingContentType.Reasoning`, если наблюдение включает `new StreamOptions().WithReasoning()`. Сводки необязательны и не являются полным внутренним рассуждением. Эти же параметры работают с Run; изменение отображения потока не меняет запрошенное усилие.

[Grok 4.6](https://docs.x.ai/developers/grok-4-6) · [reasoning_effort](https://docs.x.ai/developers/model-capabilities/text/reasoning)

### Grok Imagine Image 2.0

Генерация помогает превратить описание товара в визуальный эскиз, а редактирование — совместить объект и фон из разных фотографий. `XAIService` предоставляет обе операции через тот же `IImageGenerationService`, что и OpenAI и Google, начиная с `Mythosia.AI` 8.0.0 / `Mythosia.AI.Abstractions` 4.0.0.

Независимая модель изображений по умолчанию — `AIModels.xAI.GrokImagineImage2_0` (`grok-imagine-image-2.0`). Запросы изображений не меняют модель чата и не добавляются в историю разговора.

```csharp
using Mythosia.AI.Models;
using Mythosia.AI.Models.Images;
using Mythosia.AI.Services;
using Mythosia.AI.Services.xAI;

IImageGenerationService images = new XAIService(apiKey, httpClient);
var generated = await images.GenerateImagesAsync(new ImageGenerationRequest
{
    Model = AIModels.xAI.GrokImagineImage2_0,
    Prompt = "Стеклянный павильон на рассвете, широкая композиция",
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

Для редактирования передайте байты изображений в порядке, указанном в запросе:

```csharp
var edited = await images.EditImagesAsync(new ImageEditRequest
{
    Prompt = "Поместите объект с изображения 1 в сцену с изображения 2.",
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

Каждый `GeneratedImage.Data` содержит декодированные байты; выбирайте расширение файла по `MediaType`. Адаптер запрашивает встроенный base64 и не загружает изображения по URL провайдера. `Count` допускает 1–10 результатов; редактирование принимает 1–5 исходных изображений JPEG, PNG или WebP.

Для xAI используйте `ImageSize.Auto` или `ImageSize.Preset(ImageResolution.OneK, ImageAspectRatio.SixteenByNine)`. Разрешения: `Auto`, `OneK`, `TwoK`; пропорции должны поддерживаться моделью. `Pixels(...)` отклоняется, поскольку точные размеры запросить нельзя.

xAI поддерживает только новое общее значение по умолчанию `ImageOutputFormat.Auto`. Выбора кодека нет; явные `Jpeg`, `Png`, `WebP` отклоняются до отправки. Выбирайте расширение по `GeneratedImage.MediaType`; библиотека не перекодирует данные. Качество: `ImageQuality.Auto`, `Low`, `Medium`; фон: только `ImageBackground.Auto`. Явное сжатие и отдельная `Mask` не поддерживаются.

Google принимает `ImageSize.Auto` или `Preset` с разрешениями и пропорциями выбранной модели; см. [Параметры изображений по моделям Google](#google-image-options). Формат: `ImageOutputFormat.Auto` или `Jpeg`; `Png`/`WebP` отклоняются. Google и xAI отклоняют `Pixels`; OpenAI принимает `Auto`/`Pixels` и отклоняет `Preset`. См. [миграцию](#image-options-migration).

[Grok Imagine Image 2.0](https://docs.x.ai/developers/models/grok-imagine-image-2.0) · [Image API](https://docs.x.ai/developers/rest-api-reference/inference/images)

---

## DeepSeek (DeepSeekService)

Используйте DeepSeek Flash для быстрого ответа с последующей глубокой проверкой или анализа графиков и снимков экрана. `AIModels.DeepSeek.Flash` (`deepseek-flash`) выбирает V4.1 Flash с нативным зрением, выпущенный 10 сентября 2026 года. Сохраняются API completion, стриминга, Run, функций и RAG; поддержка с `Mythosia.AI` 8.0.0 / `Mythosia.AI.Abstractions` 4.0.0.

> Опубликованные пакеты `Mythosia.AI` 8.0.0 / `Mythosia.AI.Abstractions` 4.0.0 уже включают базовую поддержку Flash. `AIModels.DeepSeek.V4Pro`, `UseResponsesApi`, API Files и `DeepSeekImageFileContent` — ещё не выпущенные дополнения в исходном коде, требующие согласованных сборок ядра и абстракций из исходников; в этих опубликованных пакетах их нет. [Невыпущенные изменения](../../src/core/Mythosia.AI/RELEASE_NOTES.md#unreleased).

Для текстовых задач выберите `AIModels.DeepSeek.V4Pro` (`deepseek-v4-pro`, V4-Pro-0813). Flash остаётся моделью по умолчанию и поддерживает изображения; обе модели имеют Low/High/Max и одинаковый предел вывода. Установите `UseResponsesApi = true` до создания запроса, чтобы использовать Responses через существующие API ответов, потоков, Run и локальных функций. Значение по умолчанию — `false`: текущие приложения сохраняют Chat Completions. Выбор фиксируется на запрос и все раунды инструментов. Responses повторно передаёт всю историю диалога и исходных рассуждений, не опираясь на сохранённые сервером ID ответов.

```csharp
using Mythosia.AI.Extensions;
using Mythosia.AI.Models;
using Mythosia.AI.Services.DeepSeek;

var pro = new DeepSeekService(apiKey, AIModels.DeepSeek.V4Pro, httpClient)
{
    UseResponsesApi = true
};
pro.WithDeepSeekReasoning(DeepSeekReasoning.High);
string answer = await pro.CreateRequest("Review this deployment plan.").GetCompletionAsync();
```

Загрузите изображение один раз для повторного использования в вопросах и диалогах. `UploadFileAsync` принимает путь либо поток, принадлежащий вызывающему коду, и имя файла; purpose — `user_data`. JPEG, PNG, GIF и WebP ограничены 64 MiB. `DeepSeekImageFileContent` ссылается на изображение во Flash через оба транспорта; это не PDF/документ, V4 Pro его отклоняет. Без срока файл хранится постоянно; `expiresAfterSeconds` принимает 3600–2592000 секунд. Сохраняйте файл, пока не завершатся все диалоги со ссылками на него.

```csharp
using Mythosia.AI.Models.Messages;

var vision = new DeepSeekService(apiKey, AIModels.DeepSeek.Flash, httpClient)
{
    UseResponsesApi = true
};
var file = await vision.UploadFileAsync("chart.png", expiresAfterSeconds: 3600);
var question = new Message(ActorRole.User, new List<MessageContent>
{
    new TextContent("Explain the trend in this chart."),
    new DeepSeekImageFileContent(file.Id)
});
string uploadedDescription = await vision.GetCompletionAsync(question);
var metadata = await vision.GetFileAsync(file.Id);
```

`GetFileAsync` получает метаданные, `ListFilesAsync(new DeepSeekFileListOptions { After = lastId, Limit = 20, Order = DeepSeekFileOrder.Ascending })` — страницу, `DeleteFileAsync` удаляет файл. Пока `HasMore` равен true, передавайте `LastId` как следующий `After`; доступен и `Descending`. Endpoint скачивания содержимого не описан в официальной документации. Chat UI предлагает Flash и V4 Pro и использует текущий каталог для перезаписи запросов. Старое сохранённое имя `DeepSeekChat` переводится на Flash, произвольные ID сохраняются.

[Responses](https://api-docs.deepseek.com/guides/responses_api/) · [Files](https://api-docs.deepseek.com/guides/files_api/) · [Models and limits](https://api-docs.deepseek.com/quick_start/pricing/)

```csharp
using Mythosia.AI.Extensions;
using Mythosia.AI.Models;
using Mythosia.AI.Models.Streaming;
using Mythosia.AI.Services.DeepSeek;

var deepseek = new DeepSeekService(apiKey, httpClient);
deepseek.ChangeModel(AIModels.DeepSeek.Flash);
deepseek.WithDeepSeekReasoning(DeepSeekReasoning.Low);

string draft = await deepseek.GetCompletionAsync("Сравните постепенное и blue-green развёртывание, включая риски отката.");

await using var run = await deepseek
    .CreateRequest("Проверьте допущения этого сравнения.")
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

`ThinkingEnabled` по умолчанию остаётся `false`. `WithDeepSeekReasoning(...)` включает рассуждение и задаёт постоянный `ReasoningEffort` (`Auto`, `Low`, `High`, `Max`); его `Auto` опускает effort и использует стандарт провайдера `High`. Общий `WithReasoning(...)` действует на один логический запрос с раундами инструментов: `None` отключает, `Minimal`/`Low` → `Low`, `Medium`/`High`/`XHigh` → `High`, `Max` → `Max`. Общий `Auto` сохраняет настройки. Большее усилие может увеличить задержку и расход токенов. Изменение только `ReasoningEffort` не включает рассуждение.

Регистрируйте локальные функции через `WithFunction(...)`, чтобы получать данные и выполнять действия своим кодом. Инструменты работают с рассуждением и без него. Chat Completions отклоняет принудительный/обязательный выбор при рассуждении; в этом транспорте используйте автоматический выбор. С `UseResponsesApi = true` можно задать функцию через `ForceFunctionName` даже при рассуждении; адаптер передаёт `type` и `name` непосредственно в `tool_choice` Responses. Нативные асинхронные инструменты это не включает. Адаптер сохраняет `reasoning_content` и ID вызовов для следующих раундов. Run и стриминг выводят `StreamingContentType.Reasoning` при `StreamOptions.WithReasoning()`; наблюдение само не включает рассуждение. Учитываются сообщённые провайдером токены кэша и рассуждения. Автовосстановление контекста использует общий цикл стриминга. Если инструментам нужна прежняя нативная история рассуждения, автоматическое сжатие блокируется для её сохранения, а ошибка переполнения передаётся вызывающему коду.

Передавайте графики и снимки экрана через существующие типы сообщений:

```csharp
using Mythosia.AI.Models.Messages;

var message = new Message(ActorRole.User, new List<MessageContent>
{
    new TextContent("Объясните тенденцию графика и укажите неясные подписи."),
    new ImageContent(await File.ReadAllBytesAsync("chart.png"), "image/png")
});
string description = await deepseek.GetCompletionAsync(message);
```

`ImageContent` принимает байты JPEG, PNG, GIF, WebP или публичный HTTP(S) URL, загружаемый провайдером. Пример использует сообщение пользователя. Текущий API также принимает изображения в сообщениях инструментов, но зарегистрированные обработчики возвращают текст через общий контракт. Ручное сообщение с изображением `ActorRole.Function` должно содержать соответствующий ID в `MessageMetadataKeys.FunctionId` (`tool_call_id` на сервере). Актуальные ограничения размеров и суммы см. в руководстве по зрению. Генерация изображений не поддерживается.

Обе модели имеют контекст 1M и до 384K (`393216`) выходных токенов; стандартный бюджет остаётся 8 000. При рассуждении temperature/penalty пропускаются, `top_p` не ниже 0,95; без рассуждения `top_p` пропускается. Responses использует существующий API типизированного вывода для нативной JSON schema. Фоновые задачи, серверные `store`/`previous_response_id`, размещённый поиск, `CachePreservation.Required`, нативные асинхронные инструменты, `SteerAsync` и генерация изображений не поддерживаются. Локальный RAG и обычные раунды доступны.

`V4Flash`, `Chat`, `Reasoner` остаются obsolete-константами с предупреждением и исходными wire ID. Провайдер временно направляет снятый `deepseek-v4-flash` в V4.1 Flash; библиотека не переписывает константу. В новом коде выбирайте `Flash`. `UseReasonerModel()` выбирает Flash с рассуждением `High`.

[DeepSeek V4.1 Flash](https://api-docs.deepseek.com/updates/) · [Thinking](https://api-docs.deepseek.com/guides/thinking_mode/) · [Vision](https://api-docs.deepseek.com/guides/vision/) · [Tools](https://api-docs.deepseek.com/guides/tool_calls/) · [Limits](https://api-docs.deepseek.com/quick_start/pricing/)

---

## Perplexity (PerplexityService)

Используйте Perplexity, когда ответу нужны свежие сведения и источники, которые читатель может проверить. `PerplexityService` вызывает Agent API, а отдельные поиск и эмбеддинги позволяют построить извлечение документов для выбранной вами модели ответов.

```csharp
using Mythosia.AI.Models;
using Mythosia.AI.Services.Perplexity;

var service = new PerplexityService(apiKey, httpClient);
service.ChangeModel(AIModels.Perplexity.Sonar);

await using var run = await service.StartRunAsync("Сравни современные методы переработки аккумуляторов и укажи источники.");
Console.WriteLine((await run.Result).Text);
foreach (var source in run.Citations)
    Console.WriteLine(source.Url);
```

[Руководство Perplexity](perplexity.md) описывает пресеты исследования, локальные функции, размещённые инструменты и длительные фоновые задачи. Сохраняются привычные API completion, потоковой выдачи, Run и цитирования.

В этом выпуске сервис переходит на `/v1/agent`. `AIModels.Perplexity.Sonar` теперь выбирает `perplexity/sonar`. Провайдер объявил об отключении прежних эндпоинтов Sonar 27 сентября 2026 года; существующие интеграции необходимо перенести. [Perplexity](https://community.perplexity.ai/t/sonar-is-moving-to-the-agent-api/5802)

---

## Alibaba / Qwen (QwenService)

Установите отдельный пакет:

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

Доступные модели: `QwenMax`, `QwenPlus`, `QwenTurbo`, `Qwen3` и их варианты.

При создании сервиса выберите совместимый эндпоинт с помощью `EndpointPlatform`:

```csharp
var vllmService = new QwenService(
    "http://localhost:8000",
    EndpointPlatform.Vllm,
    http);
```

[Формировать настройки модели по общим определениям возможностей](model-capabilities.md).
