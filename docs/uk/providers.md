# Можливості провайдерів

> Приклади з `CreateRequest` потребують Mythosia.AI 8.0.0 / Abstractions 4.0.0. У попередній версії 7.1, що додала Run і спільні параметри, білдера немає. Старі пакети можуть використовувати попередні перевантаження сервісу.

<a id="image-options-migration"></a>
## Перехід на типізовані параметри зображень

Вибирайте якість і формат через enum з автодоповненням та розрізняйте точні пікселі й клас роздільності. Це усуває помилки в рядках і приховане перетворення розмірів на іншу категорію.

Несумісна зміна Mythosia.AI 8.0.0: `Quality`, `Background` і `OutputFormat` стають enum, `Size` — `ImageSize`, окрему властивість запиту `AspectRatio` видалено. Типове значення `OutputFormat` тепер `ImageOutputFormat.Auto`. Методи генерації та редагування зберігаються.

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

`Pixels(width, height)` запитує точні розміри. `Preset(resolution, aspectRatio)` задає клас роздільності й пропорції; фактичні пікселі визначає постачальник. Без обмежень використовуйте `ImageSize.Auto`. Переходьте з пікселів на пресети лише якщо допустимі приблизні розміри.

| Provider | `Size` | `OutputFormat` |
| --- | --- | --- |
| OpenAI | `ImageSize.Auto`, `Pixels(...)` | `Auto` → PNG, `Png`, `Jpeg`, `WebP` |
| Google | `ImageSize.Auto`, `Preset(...)` | `Auto`, `Jpeg` |
| xAI | `ImageSize.Auto`, `Preset(...)` | `Auto` |

Невизначені значення enum і непідтримувані комбінації відхиляються до HTTP. Наявність значення не означає підтримку всіма моделями. Google приймає тільки `ImageQuality.Auto`, xAI — `Auto`, `Low`, `Medium`.

Після того як `EditImagesAsync` поверне `Task`, вхідні буфери можна використовувати повторно. Розпочатий запит зберігає власні дані зображень, зокрема байти маски OpenAI; подальші зміни початкових масивів `ImageInput.Data` не змінюють завантажувані дані.

Щоб перерваний результат не зберігався як готове зображення, генерація та редагування Google вимагають, щоб кожен повернений кандидат завершувався з `finishReason: STOP`. Якщо хоча б один кандидат заблокований, незавершений або не має цього кінцевого статусу, весь виклик завершується винятком `AIServiceException`. Відсутні або некоректні вбудовані дані base64 чи MIME-метадані зображення також спричиняють помилку всього виклику; формат PNG не припускається. Ці перевірки не підтверджують відповідність байтів файлу заявленому формату зображення.

## OpenAI (OpenAIService)

> Підтримка GPT-6 Astra та асинхронних викликів інструментів доступна з `Mythosia.AI` 7.1.0; спільні типи включено до `Mythosia.AI.Abstractions` 3.1.0.

Якщо інструмент довго завантажує дані, GPT-6 Astra може тим часом продовжувати незалежні пояснення або інші частини завдання. `FunctionDefinition.AllowAsync = true` або `FunctionBuilder.WithAsync()` дозволяє асинхронні виклики для GPT-6 Astra через Responses. За замовчуванням використовується `false`; моделі без підтримки чекають результату того самого обробника. Приклади та життєвий цикл запиту описано в [посібнику з виклику функцій](function-calling.md).

Як задавати рівень міркування для різних провайдерів і використовувати актуальну інформацію або проіндексовані документи, пояснює [посібник із міркування та пошуку](reasoning-and-search.md). У ньому наведено підтримувані моделі, умови збереження кешу та обмеження поєднань.

### Рівень міркувань

Баланс між швидкістю та глибиною аналізу:

```csharp
using Mythosia.AI.Models;

// GPT-6 Astra
service.ChangeModel(AIModels.OpenAI.Gpt6Astra);
service.WithGpt6Parameters(
    reasoningEffort: Gpt6Reasoning.Medium, // Auto, Low, Medium, High, XHigh, Max
    verbosity: Verbosity.Medium,
    reasoningSummary: ReasoningSummary.Auto,
    reasoningMode: Gpt6ReasoningMode.Standard);

// GPT-5.6: Sol — флагманська модель; Terra і Luna — економніші варіанти.
service.ChangeModel(AIModels.OpenAI.Gpt5_6Sol);
service.WithGpt5_6Parameters(
    reasoningEffort: Gpt5_6Reasoning.Medium, // None, Low, Medium, High, XHigh, Max
    verbosity: Verbosity.Medium);            // Low, Medium, High

service.ChangeModel(AIModels.OpenAI.Gpt5_4);
service.Gpt5_4ReasoningEffort = Gpt5_4Reasoning.High;

service.ChangeModel(AIModels.OpenAI.Gpt5_2);
service.Gpt5_2ReasoningEffort = Gpt5_2Reasoning.Medium;

```

GPT-6 Astra за замовчуванням використовує Responses API; виклики функцій потребують цього API. `Auto` відповідає типовому значенню бібліотеки — `Medium`; значення `None` і `Minimal` недоступні. `AIRequestProfile.DisableReasoning = true` задає рівень `Low` у режимі `Standard` і пропускає підсумок міркувань. Виберіть `Gpt6ReasoningMode.Pro` для режиму Pro з тим самим ідентифікатором моделі `gpt-6-astra`.

### Перетворення тексту в мовлення (TTS)

```csharp
byte[] audio = await service.GetSpeechAsync(
    inputText: "Привіт!",
    voice: "alloy",
    model: "tts-1"
);

await File.WriteAllBytesAsync("output.mp3", audio);
```

### Розпізнавання мовлення (STT)

```csharp
byte[] audioData = await File.ReadAllBytesAsync("recording.mp3");

string transcript = await service.TranscribeAudioAsync(
    audioData: audioData,
    fileName: "recording.mp3",
    language: "uk"
);
```

`TranscribeAudioAsync` використовує `gpt-transcribe`; публічна сигнатура не змінюється.

### Генерація зображень

#### GPT Image 2.5

Вибирайте Flare для швидкого створення візуальних ескізів, а Sunburst — коли правки мають точно відповідати докладним інструкціям. Обидві моделі створюють і редагують зображення через наявний `IImageGenerationService`; вибір моделі зображень не змінює модель чату.

| Модель | Коли вибирати |
| --- | --- |
| `AIModels.OpenAI.GptImage2_5Flare` | Швидка та якісна генерація зображень для повсякденних завдань. |
| `AIModels.OpenAI.GptImage2_5Sunburst` | Генерація та зміни, для яких особливо важлива точність редагування. |

Явно задайте `ImageGenerationRequest.Model` або успадковану властивість в `ImageEditRequest`. Типовою моделлю OpenAI залишається `AIModels.OpenAI.GptImage2`. Псевдоніми: `gpt-image-2.5-flare` і `gpt-image-2.5-sunburst`. Щоб зафіксувати знімки від 8 вересня 2026 року, використовуйте `GptImage2_5Flare_260908` або `GptImage2_5Sunburst_260908` (відповідні ID закінчуються на `-2026-09-08`).

Створіть ескіз із Flare:

```csharp
using Mythosia.AI.Models;
using Mythosia.AI.Models.Images;
using Mythosia.AI.Services;
using Mythosia.AI.Services.OpenAI;

IImageGenerationService images = new OpenAIService(apiKey, httpClient);
var generated = await images.GenerateImagesAsync(new ImageGenerationRequest
{
    Model = AIModels.OpenAI.GptImage2_5Flare,
    Prompt = "Скляний павільйон на світанку, архітектурний концепт",
    Size = ImageSize.Pixels(1024, 1024),
    Quality = ImageQuality.Low,
    OutputFormat = ImageOutputFormat.Png
});

await File.WriteAllBytesAsync("pavilion.png", generated.Images[0].Data);
```

Потім відредагуйте створене зображення із Sunburst:

```csharp
var edited = await images.EditImagesAsync(new ImageEditRequest
{
    Model = AIModels.OpenAI.GptImage2_5Sunburst,
    Prompt = "Збережи конструкцію павільйону, прибери навколишній пейзаж і зроби тло прозорим.",
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

Для обох моделей та їхніх знімків `Quality` приймає `Auto`, `Low`, `Medium`, `High`, `XHigh` і `Max`. Використовуйте низьку якість для чернеток і порівнюйте вищі рівні для готових матеріалів. `OutputFormat`: `Auto` / `Png`, `Jpeg` або `WebP`; `OutputCompression` — 0–100 лише для JPEG/WebP. Тло `Transparent` потребує PNG/WebP. `Count` — 1–10.

`Size`: `ImageSize.Auto` або `ImageSize.Pixels(width, height)`. Сторони кратні 16, пропорції 1:3–3:1, максимум 3840 пікселів на сторону, площа 655360–8294400 пікселів. Розміри понад 2560×1440 експериментальні. OpenAI відхиляє `Preset`.

Редагування приймає 1–16 непорожніх JPEG/PNG/WebP-зразків розміром менш як 50 MiB кожен. Необов’язкова маска має бути PNG/WebP менш як 50 MiB, збігатися з першим зразком за форматом і піксельними розмірами та містити альфа-канал. Бібліотека перевіряє MIME і довжину в байтах; розміри та альфа-канал перевіряє провайдер.

Приклади використовують наявні шляхи Image API: повернення байтів і multipart-редагування. Інструменти Responses `image_generation`, потік часткових зображень та `input_fidelity` ця інтеграція не надає. Читайте `GeneratedImage.Data` і `MediaType` з результату.

Див. офіційний [посібник із зображень](https://developers.openai.com/api/docs/guides/image-generation), сторінки [Sunburst](https://developers.openai.com/api/docs/models/gpt-image-2.5-sunburst) і [Flare](https://developers.openai.com/api/docs/models/gpt-image-2.5-flare).

---

## Anthropic (AnthropicService)

[Claude Fable 5.1](fable-5-1.md) підтримує повідомлення про перебіг роботи, інструкції для одного ходу та діагностику прив’язки thinking від `Mythosia.AI` 8.0.0 / `Mythosia.AI.Abstractions` 4.0.0. Mythos 5.1 доступний за запрошенням. Обидва відхиляють примусовий вибір інструмента.

### Підрахунок токенів (нативний API)

`GetInputTokenCountAsync` доступний у всіх провайдерів ([див. генерація тексту](completions.md#підрахунок-токенів)). Anthropic викликає офіційний ендпоінт `messages/count_tokens`, повертаючи **точну** кількість токенів:

```csharp
uint tokens = await service.GetInputTokenCountAsync("Текст промпту");
uint total = await service.GetInputTokenCountAsync();
```

---

## Google (GoogleAIService)

Для аналізу довгих документів і завдань із кількома раундами інструментів можна вибрати Gemini 3.7 Flash або 3.8 Flash у наявному адаптері Google. Підтримка доступна з `Mythosia.AI` 8.0.0 / `Mythosia.AI.Abstractions` 4.0.0; типовою моделлю залишається Gemini 3.6 Flash.

### Глибина міркувань

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
    .CreateRequest("Порівняй поступове та blue-green розгортання, зокрема ризики відкату.")
    .WithReasoning(ReasoningLevel.High)
    .GetCompletionAsync();
```

Використовуйте `Low` для початкового огляду та `High` для складної перевірки; додаткові міркування можуть збільшити затримку й витрати токенів. Обидві моделі приймають `Low`, `Medium` і `High`, але не `Minimal` та `None`. `GeminiThinkingLevel.Auto` не надсилає перевизначення; типове значення постачальника для 3.8 — `Medium`. `ThinkingLevel` задає основу сервісу, а `WithReasoning(...)` перевизначає її для одного логічного запиту. Адаптер не надсилає `temperature`, `topP` та `topK`. Ліміти постачальника: 1 048 576 вхідних і 65 536 вихідних токенів. [Gemini 3.7 Flash](https://ai.google.dev/gemini-api/docs/models/gemini-3.7-flash), [Gemini 3.8 Flash](https://ai.google.dev/gemini-api/docs/models/gemini-3.8-flash).

---

## xAI (XAIService)

### Оберіть зусилля відповідно до завдання

Для швидкої чернетки використовуйте менше зусилля, а для складних перевірок, де якість важливіша за час відповіді, виділіть більше міркування. Щоб скористатися додатковим рівнем `XHigh`, явно виберіть Grok 4.6.

```csharp
using Mythosia.AI.Extensions;
using Mythosia.AI.Models;
using Mythosia.AI.Services.xAI;

var grok = new XAIService(apiKey, httpClient);
grok.ChangeModel(AIModels.xAI.Grok4_6);
grok.WithGrokReasoning(GrokReasoning.Low);

string review = await grok
    .CreateRequest("Порівняй поетапне та синьо-зелене розгортання, включно з відновленням після відмов.")
    .WithReasoning(ReasoningLevel.XHigh)
    .GetCompletionAsync();
```

Grok 4.6 підтримує `Low`, `Medium`, `High` і `XHigh` (`GrokReasoning.XHigh`). `Auto` пропускає `reasoning_effort`, залишаючи стандартне значення провайдера `High`; `None` не вимикає міркування. Більше зусилля може збільшити затримку та витрати токенів. Для сумісності типовою моделлю `XAIService` залишається Grok 4.5. Версія 4.5 підтримує від `Low` до `High`, а 4.3 — від `None` до `High`; адаптер відхиляє `XHigh` для цих попередніх моделей до надсилання.

`WithGrokReasoning(...)` і наявний `WithGrokParameters(...)` задають базове налаштування сервісу. У Grok 4.6 спільний `WithReasoning(...)` перевизначає його для одного логічного запиту, включно з раундами інструментів і виправленнями структурованого виводу, а потім відновлює. Внутрішні профілі `DisableReasoning` використовують `Low` для цієї моделі, яка завжди міркує. Оновлення зі збереженням кешу та розміщений вебпошук/пошук файлів для xAI через ці спільні параметри не інтегровано.

Grok 4.6 може повертати підсумки міркування провайдера як `StreamingContentType.Reasoning`, якщо спостереження вмикає `new StreamOptions().WithReasoning()`. Підсумки необов'язкові й не є повним внутрішнім міркуванням. Ці самі параметри працюють із Run; зміна відображення потоку не змінює запитане зусилля.

[Grok 4.6](https://docs.x.ai/developers/grok-4-6) · [reasoning_effort](https://docs.x.ai/developers/model-capabilities/text/reasoning)

### Grok Imagine Image 2.0

Генерація допомагає перетворити опис товару на візуальний ескіз, а редагування — поєднати об’єкт і тло з різних фотографій. `XAIService` надає обидві операції через той самий `IImageGenerationService`, що й OpenAI та Google, починаючи з `Mythosia.AI` 8.0.0 / `Mythosia.AI.Abstractions` 4.0.0.

Незалежна типова модель зображень — `AIModels.xAI.GrokImagineImage2_0` (`grok-imagine-image-2.0`). Запити зображень не змінюють модель чату й не додаються до історії розмови.

```csharp
using Mythosia.AI.Models;
using Mythosia.AI.Models.Images;
using Mythosia.AI.Services;
using Mythosia.AI.Services.xAI;

IImageGenerationService images = new XAIService(apiKey, httpClient);
var generated = await images.GenerateImagesAsync(new ImageGenerationRequest
{
    Model = AIModels.xAI.GrokImagineImage2_0,
    Prompt = "Скляний павільйон на світанку, широка композиція",
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

Для редагування передайте байти зображень у порядку, зазначеному в запиті:

```csharp
var edited = await images.EditImagesAsync(new ImageEditRequest
{
    Prompt = "Розмістіть об’єкт із зображення 1 у сцені із зображення 2.",
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

Кожен `GeneratedImage.Data` містить декодовані байти; вибирайте розширення файлу за `MediaType`. Адаптер запитує вбудований base64 і не завантажує зображення за URL провайдера. `Count` дозволяє 1–10 результатів; редагування приймає 1–5 вхідних зображень JPEG, PNG або WebP.

Для xAI використовуйте `ImageSize.Auto` або `ImageSize.Preset(ImageResolution.OneK, ImageAspectRatio.SixteenByNine)`. Роздільність: `Auto`, `OneK`, `TwoK`; пропорції мають підтримуватися моделлю. `Pixels(...)` відхиляється, оскільки точні розміри запитати неможливо.

xAI підтримує тільки нове спільне типове значення `ImageOutputFormat.Auto`. Вибору кодека немає; явні `Jpeg`, `Png`, `WebP` відхиляються до надсилання. Вибирайте розширення за `GeneratedImage.MediaType`; бібліотека не перекодовує дані. Якість: `ImageQuality.Auto`, `Low`, `Medium`; тло: тільки `ImageBackground.Auto`. Явне стиснення й окрема `Mask` не підтримуються.

Google приймає `ImageSize.Auto` або `Preset` із `ImageResolution.Auto`, `FiveTwelve`, `OneK`, `TwoK`, `FourK` залежно від моделі. Формат: `ImageOutputFormat.Auto` або `Jpeg`; `Png`/`WebP` відхиляються. Google і xAI відхиляють `Pixels`; OpenAI приймає `Auto`/`Pixels` і відхиляє `Preset`. Див. [перехід](#image-options-migration).

[Grok Imagine Image 2.0](https://docs.x.ai/developers/models/grok-imagine-image-2.0) · [Image API](https://docs.x.ai/developers/rest-api-reference/inference/images)

---

## DeepSeek (DeepSeekService)

Використовуйте DeepSeek Flash для швидкої відповіді з подальшою ретельною перевіркою або аналізу графіків і знімків екрана. `AIModels.DeepSeek.Flash` (`deepseek-flash`) вибирає V4.1 Flash із нативним зором, випущений 10 вересня 2026 року. Зберігаються API completion, стримінгу, Run, функцій і RAG; підтримка з `Mythosia.AI` 8.0.0 / `Mythosia.AI.Abstractions` 4.0.0.

```csharp
using Mythosia.AI.Extensions;
using Mythosia.AI.Models;
using Mythosia.AI.Models.Streaming;
using Mythosia.AI.Services.DeepSeek;

var deepseek = new DeepSeekService(apiKey, httpClient);
deepseek.ChangeModel(AIModels.DeepSeek.Flash);
deepseek.WithDeepSeekReasoning(DeepSeekReasoning.Low);

string draft = await deepseek.GetCompletionAsync("Порівняйте поступове та blue-green розгортання, зокрема ризики відкату.");

await using var run = await deepseek
    .CreateRequest("Перевірте припущення цього порівняння.")
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

`ThinkingEnabled` типово залишається `false`. `WithDeepSeekReasoning(...)` вмикає міркування та задає постійний `ReasoningEffort` (`Auto`, `Low`, `High`, `Max`); його `Auto` пропускає effort і використовує стандарт провайдера `High`. Спільний `WithReasoning(...)` діє на один логічний запит із раундами інструментів: `None` вимикає, `Minimal`/`Low` → `Low`, `Medium`/`High`/`XHigh` → `High`, `Max` → `Max`. Спільний `Auto` зберігає налаштування. Більше зусиль може збільшити затримку й витрати токенів. Зміна лише `ReasoningEffort` не вмикає міркування.

Реєструйте локальні функції через `WithFunction(...)`, щоб отримувати дані та виконувати дії власним кодом. Інструменти працюють із міркуванням і без нього; під час міркування примусовий/обов’язковий вибір відхиляється, використовуйте автоматичний. Адаптер зберігає `reasoning_content` та ID викликів для наступних раундів. Run і стримінг надають `StreamingContentType.Reasoning` за `StreamOptions.WithReasoning()`; спостереження саме не вмикає міркування. Враховуються повідомлені провайдером токени кешу й міркування. Автовідновлення контексту використовує спільний цикл стримінгу. Якщо інструментам потрібна попередня нативна історія міркувань, автоматичне стиснення блокується для її збереження, а помилка переповнення передається виклику.

Передавайте графіки та знімки екрана через наявні типи повідомлень:

```csharp
using Mythosia.AI.Models.Messages;

var message = new Message(ActorRole.User, new List<MessageContent>
{
    new TextContent("Поясніть тенденцію графіка та вкажіть незрозумілі підписи."),
    new ImageContent(await File.ReadAllBytesAsync("chart.png"), "image/png")
});
string description = await deepseek.GetCompletionAsync(message);
```

`ImageContent` приймає байти JPEG, PNG, GIF, WebP або публічний HTTP(S) URL, який завантажує провайдер. Приклад використовує повідомлення користувача. Поточний API також приймає зображення в повідомленнях інструментів, але зареєстровані обробники повертають текст через спільний контракт. Ручне повідомлення із зображенням `ActorRole.Function` має містити відповідний ID у `MessageMetadataKeys.FunctionId` (`tool_call_id` на сервері). Актуальні обмеження розмірів і суми див. у посібнику із зору. `file_id`, Files API та генерацію зображень не інтегровано.

Провайдер заявляє контекст 1M і вихід до 384K (`393216`) токенів; типовий бюджет бібліотеки залишається 8 000. Під час міркування temperature/penalty пропускаються, `top_p` не нижче 0,95; без міркування `top_p` пропускається. Використовується Chat Completions. Responses, хостинговий пошук, `CachePreservation.Required`, нативні асинхронні інструменти та `SteerAsync` не інтегровані. Локальний RAG і звичайні раунди доступні.

`V4Flash`, `Chat`, `Reasoner` залишаються obsolete-константами з попередженням та початковими wire ID. Провайдер тимчасово спрямовує знятий `deepseek-v4-flash` до V4.1 Flash; бібліотека не переписує константу. У новому коді вибирайте `Flash`. `UseReasonerModel()` вибирає Flash із міркуванням `High`.

[DeepSeek V4.1 Flash](https://api-docs.deepseek.com/updates/) · [Thinking](https://api-docs.deepseek.com/guides/thinking_mode/) · [Vision](https://api-docs.deepseek.com/guides/vision/) · [Tools](https://api-docs.deepseek.com/guides/tool_calls/) · [Limits](https://api-docs.deepseek.com/quick_start/pricing/)

---

## Perplexity (PerplexityService)

Використовуйте Perplexity, коли відповіді потрібні свіжі відомості та джерела, які читач може перевірити. `PerplexityService` викликає Agent API, а окремі пошук та ембеддинги дають змогу побудувати отримання документів для обраної вами моделі відповідей.

```csharp
using Mythosia.AI.Models;
using Mythosia.AI.Services.Perplexity;

var service = new PerplexityService(apiKey, httpClient);
service.ChangeModel(AIModels.Perplexity.Sonar);

await using var run = await service.StartRunAsync("Порівняй сучасні методи переробки акумуляторів і наведи джерела.");
Console.WriteLine((await run.Result).Text);
foreach (var source in run.Citations)
    Console.WriteLine(source.Url);
```

[Посібник Perplexity](perplexity.md) пояснює пресети дослідження, локальні функції, розміщені інструменти та тривалі фонові завдання. Залишаються звичні API completion, потокового виведення, Run і цитування.

У цьому випуску сервіс переходить на `/v1/agent`. `AIModels.Perplexity.Sonar` тепер обирає `perplexity/sonar`. Провайдер оголосив про вимкнення попередніх кінцевих точок Sonar 27 вересня 2026 року; наявні інтеграції потрібно перенести. [Perplexity](https://community.perplexity.ai/t/sonar-is-moving-to-the-agent-api/5802)

---

## Alibaba / Qwen (QwenService)

Встановіть окремий пакет:

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

Доступні моделі: `QwenMax`, `QwenPlus`, `QwenTurbo`, `Qwen3` та їхні варіанти.

Під час створення сервісу виберіть сумісний ендпоінт за допомогою `EndpointPlatform`:

```csharp
var vllmService = new QwenService(
    "http://localhost:8000",
    EndpointPlatform.Vllm,
    http);
```

[Створювати налаштування моделі за спільними визначеннями можливостей](model-capabilities.md).
