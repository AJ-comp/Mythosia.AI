# Anbieterspezifische Funktionen

> Die `CreateRequest`-Beispiele benötigen Mythosia.AI 8.0.0 / Abstractions 4.0.0. Die frühere Version 7.1 mit Run und gemeinsamen Anfrageoptionen enthält den Builder noch nicht. Ältere Pakete können ihre bisherigen Service-Überladungen verwenden.

<a id="image-options-migration"></a>
Verfügbare Modi und gemeldete Metadaten hängen von Anbieter, Modell und API ab. Prüfen Sie die [gemeinsame Geschwindigkeitsoption](request-building.md#inference-speed) und Capabilities; eine Fast-Anfrage allein beweist noch keine Fast-Verarbeitung.

## Migration zu typisierten Bildoptionen

Wählen Sie Qualität und Dateiformat per Enum und Autovervollständigung. Exakte Pixelmaße und Auflösungsstufen sind getrennt, damit Tippfehler auffallen und Pixelangaben nicht stillschweigend zu anderen Größen werden.

Inkompatible Änderung in Mythosia.AI 8.0.0: `Quality`, `Background` und `OutputFormat` sind Enums; `Size` ist `ImageSize`. Die separate Request-Eigenschaft `AspectRatio` entfällt. `OutputFormat` verwendet jetzt standardmäßig `ImageOutputFormat.Auto`. Die Methoden zum Erzeugen und Bearbeiten bleiben bestehen.

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

`Pixels(width, height)` fordert exakte Maße an. `Preset(resolution, aspectRatio)` legt Auflösungsstufe und Verhältnis fest; die Pixelmaße bestimmt der Anbieter. Ohne Größenvorgabe verwenden Sie `ImageSize.Auto`. Migrieren Sie Pixelangaben nur zu Presets, wenn ungefähre Maße genügen.

| Provider | `Size` | `OutputFormat` |
| --- | --- | --- |
| OpenAI | `ImageSize.Auto`, `Pixels(...)` | `Auto` → PNG, `Png`, `Jpeg`, `WebP` |
| Google | `ImageSize.Auto`, `Preset(...)` | `Auto`, `Jpeg` |
| xAI | `ImageSize.Auto`, `Preset(...)` | `Auto` |

Undefinierte Enum-Werte und nicht unterstützte Anbieter-/Modellkombinationen werden vor HTTP abgelehnt. Nicht jedes Modell unterstützt jeden Wert. Google unterstützt nur `ImageQuality.Auto`, xAI `Auto`, `Low` und `Medium`.

Nach der Rückgabe des `Task` durch `EditImagesAsync` können Sie die Eingabepuffer wiederverwenden. Die gestartete Anfrage hält eigene Bilddaten einschließlich der OpenAI-Maskenbytes vor; spätere Änderungen an den ursprünglichen `ImageInput.Data`-Arrays verändern den Upload nicht.

Damit unterbrochene Ausgaben nicht als fertige Bilder gespeichert werden, müssen bei der Google-Bilderzeugung und -bearbeitung alle zurückgegebenen Kandidaten mit `finishReason: STOP` enden. Ist ein Kandidat blockiert, unvollständig oder ohne diesen Abschlussstatus, schlägt der gesamte Aufruf mit `AIServiceException` fehl. Fehlende oder fehlerhafte Inline-Base64-Daten oder Bild-MIME-Angaben lassen ebenfalls den gesamten Aufruf fehlschlagen; PNG wird nicht angenommen. Diese Prüfungen bestätigen nicht, dass die Dateibytes dem angegebenen Bildformat entsprechen.

## OpenAI (OpenAIService)

> GPT-6 Astra und Async-Tool-Aufrufe werden ab `Mythosia.AI` 7.1.0 unterstützt; die gemeinsamen Typen sind ab `Mythosia.AI.Abstractions` 3.1.0 verfügbar.

Eine langsame Abfrage muss die Antwort nicht vollständig anhalten. Während etwa Wetterdaten geladen werden, kann das Modell bereits allgemeine Reisetipps formulieren, die nicht vom Ergebnis abhängen.

Mit `FunctionDefinition.AllowAsync = true` oder `FunctionBuilder.WithAsync()` erlaubst du GPT-6 Astra / Sol / Luna über Responses asynchrone Tool-Aufrufe. Standard ist `false`; nicht unterstützte Modelle warten auf das Ergebnis desselben Handlers. Beispiele und Details zur Lebensdauer des Requests stehen im [Leitfaden für Funktionsaufrufe](function-calling.md).

<a id="gpt-6-sol-luna"></a>

### GPT-6 Sol / Luna

Wähle GPT-6 Sol für anspruchsvolle Programmier-, Tool- und Agentenaufgaben und Luna für große Mengen von Text- oder Bildeingaben mit niedrigeren Kosten. Beide verwenden die bestehenden APIs für vollständige Antworten, Streaming und Runs; der Ablauf der Anwendung bleibt gleich.

> Benötigt Mythosia.AI 8.1.0 / Abstractions 4.1.0. Die bisherigen Mindestversionen für Astra und das Standardmodell des Dienstes bleiben unverändert.

Verwende `AIModels.OpenAI.Gpt6Sol` (`gpt-6-sol`) oder `AIModels.OpenAI.Gpt6Luna` (`gpt-6-luna`). Beide verarbeiten Text- und Bildeingaben und erzeugen Text: 1.050.000 Tokens Kontext, höchstens 922.000 Eingabe- und 128.000 Ausgabetokens. Eingabe, Reasoning und Ausgabe müssen gemeinsam in den Kontext passen. `MaxTokens` bestimmt das angeforderte Ausgabebudget, nicht die Kontextgröße.

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

`Auto` entspricht `Medium`. Sol/Luna unterstützen `None`, `Low`, `Medium`, `High`, `XHigh` und `Max`, aber kein `Minimal`. Nutze pro Anfrage `WithReasoning(ReasoningLevel.None)` oder `Gpt6Reasoning.None` in `WithGpt6Parameters`. Nur Sol/Luna mit `None` senden `Temperature` / `TopP`; bei aktivem Reasoning entfallen beide. Astra benötigt stets Reasoning und lässt Sampling weg. `AIRequestProfile.DisableReasoning` wählt für Sol/Luna `None` und für Astra `Low` im Standard-Modus und lässt Reasoning-Zusammenfassungen weg.

`Gpt6ReasoningMode.Standard` und `.Pro` verwenden dieselbe ausgewählte Modell-ID. Alle drei GPT-6-Modelle unterstützen Tool-Aufrufe über Responses, optionale asynchrone Tools, zusätzliche Anweisungen über WebSocket-Runs und cacheerhaltende Reasoning-Änderungen im Standard-Modus mit einem Agenten. Prüfe vorher `run.CanSteer`; eine Annahme widerruft keine frühere Ausgabe. `WithSpeed(InferenceSpeed.Fast)` fordert unabhängig vom Reasoning kostenpflichtige Fast-Verarbeitung an. `result.Processing` zeigt den angewendeten Modus; Kontoberechtigungen und serverseitige Herabstufungen sind getrennt von lokalen Fähigkeiten.

[GPT-6 Sol](https://developers.openai.com/api/docs/models/gpt-6-sol) · [GPT-6 Luna](https://developers.openai.com/api/docs/models/gpt-6-luna) · [Reasoning](https://developers.openai.com/api/docs/guides/reasoning) · [Fast](https://developers.openai.com/api/docs/guides/fast-mode)

### Reasoning-Aufwand

GPT-6 Astra / Sol / Luna und GPT-5.1–5.6 unterstützen die Steuerung des Reasoning-Aufwands. Wähle die Stufe passend zu Geschwindigkeit und Tiefe:

```csharp
using Mythosia.AI.Models;

// GPT-6 Astra
service.ChangeModel(AIModels.OpenAI.Gpt6Astra);
service.WithGpt6Parameters(
    reasoningEffort: Gpt6Reasoning.Medium, // Auto, Low, Medium, High, XHigh, Max
    verbosity: Verbosity.Medium,
    reasoningSummary: ReasoningSummary.Auto,
    reasoningMode: Gpt6ReasoningMode.Standard);

// GPT-5.6: Sol ist das Flaggschiff; Terra und Luna sind kostengünstigere Optionen.
service.ChangeModel(AIModels.OpenAI.Gpt5_6Sol);
service.WithGpt5_6Parameters(
    reasoningEffort: Gpt5_6Reasoning.Medium, // None, Low, Medium, High, XHigh, Max
    verbosity: Verbosity.Medium);            // Low, Medium, High

// GPT-5.4-Serie
service.ChangeModel(AIModels.OpenAI.Gpt5_4);
service.Gpt5_4ReasoningEffort = Gpt5_4Reasoning.High; // None, Low, Medium, High, XHigh

// GPT-5.2-Serie
service.ChangeModel(AIModels.OpenAI.Gpt5_2);
service.Gpt5_2ReasoningEffort = Gpt5_2Reasoning.Medium;

```

GPT-6 Astra verwendet standardmäßig die Responses API; Funktionsaufrufe erfordern sie. `Auto` entspricht dem Bibliotheksstandard `Medium`; `None` und `Minimal` sind nicht verfügbar. `AIRequestProfile.DisableReasoning = true` verwendet `Low` im Modus `Standard` und lässt die Zusammenfassung der Schlussfolgerungen weg. Mit `Gpt6ReasoningMode.Pro` wird der Pro-Modus für dieselbe Modell-ID `gpt-6-astra` aktiviert.

Für gemeinsame Aufgaben können Sie [Reasoning und native Suche](reasoning-and-search.md) verwenden; die folgenden anbieterspezifischen Einstellungen bleiben verfügbar.

### Text-to-Speech

```csharp
byte[] audio = await service.GetSpeechAsync(
    inputText: "Hallo, Welt!",
    voice: "alloy",   // alloy, echo, fable, onyx, nova, shimmer
    model: "tts-1"
);

await File.WriteAllBytesAsync("ausgabe.mp3", audio);
```

### Speech-to-Text (Transkription)

```csharp
byte[] audioData = await File.ReadAllBytesAsync("aufnahme.mp3");

string transcript = await service.TranscribeAudioAsync(
    audioData: audioData,
    fileName: "aufnahme.mp3",
    language: "de"  // optional, ISO-639-1
);
```

`TranscribeAudioAsync` verwendet `gpt-transcribe`; die öffentliche Signatur bleibt unverändert.

### Bildgenerierung

#### GPT Image 2.5

Wählen Sie Flare für schnelle Bildentwürfe und Sunburst, wenn Änderungen detaillierte Bearbeitungsanweisungen präzise umsetzen müssen. Beide Modelle erzeugen und bearbeiten Bilder über den vorhandenen `IImageGenerationService`; das Chatmodell bleibt dabei gleich.

| Modell | Geeigneter Einsatz |
| --- | --- |
| `AIModels.OpenAI.GptImage2_5Flare` | Schnelle, hochwertige Bilderzeugung für alltägliche Aufgaben. |
| `AIModels.OpenAI.GptImage2_5Sunburst` | Bilderzeugung und Änderungen mit besonderem Anspruch an Bearbeitungspräzision. |

Setzen Sie `ImageGenerationRequest.Model` oder die geerbte Eigenschaft von `ImageEditRequest` ausdrücklich. OpenAI verwendet weiterhin standardmäßig `AIModels.OpenAI.GptImage2`. Die Aliase sind `gpt-image-2.5-flare` und `gpt-image-2.5-sunburst`; für feste Snapshots vom 8. September 2026 verwenden Sie `GptImage2_5Flare_260908` oder `GptImage2_5Sunburst_260908` (IDs mit Endung `-2026-09-08`).

Erstellen Sie einen Entwurf mit Flare:

```csharp
using Mythosia.AI.Models;
using Mythosia.AI.Models.Images;
using Mythosia.AI.Services;
using Mythosia.AI.Services.OpenAI;

IImageGenerationService images = new OpenAIService(apiKey, httpClient);
var generated = await images.GenerateImagesAsync(new ImageGenerationRequest
{
    Model = AIModels.OpenAI.GptImage2_5Flare,
    Prompt = "Ein Glaspavillon bei Sonnenaufgang, architektonische Konzeptzeichnung",
    Size = ImageSize.Pixels(1024, 1024),
    Quality = ImageQuality.Low,
    OutputFormat = ImageOutputFormat.Png
});

await File.WriteAllBytesAsync("pavilion.png", generated.Images[0].Data);
```

Bearbeiten Sie das erzeugte Bild anschließend mit Sunburst:

```csharp
var edited = await images.EditImagesAsync(new ImageEditRequest
{
    Model = AIModels.OpenAI.GptImage2_5Sunburst,
    Prompt = "Behalte den Pavillonentwurf bei, entferne die Umgebung und mache den Hintergrund transparent.",
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

`Quality` unterstützt bei beiden Modellen und ihren Snapshots `Auto`, `Low`, `Medium`, `High`, `XHigh` und `Max`. Nutzen Sie niedrige Qualität für Entwürfe und vergleichen Sie höhere Stufen für fertige Motive. `OutputFormat` erlaubt `Auto` / `Png`, `Jpeg` oder `WebP`; `OutputCompression` von 0–100 gilt nur für JPEG/WebP. Ein Hintergrund `Transparent` erfordert PNG/WebP. `Count` liegt zwischen 1 und 10.

`Size` ist `ImageSize.Auto` oder `ImageSize.Pixels(width, height)`: beide Seiten Vielfache von 16, Verhältnis 1:3–3:1, höchstens 3840 Pixel pro Seite und Fläche 655360–8294400 Pixel. Größen über 2560×1440 sind experimentell. OpenAI lehnt `Preset` ab.

Die Bearbeitung akzeptiert 1–16 nicht leere JPEG/PNG/WebP-Referenzen mit jeweils weniger als 50 MiB. Eine optionale Maske muss PNG/WebP sein, unter 50 MiB bleiben, Format und Pixelmaße der ersten Referenz haben und einen Alphakanal enthalten. Die Bibliothek prüft MIME und Bytelänge; Pixelmaße und Alpha prüft der Anbieter.

Die Beispiele nutzen die bestehenden Image-API-Pfade für Byteausgabe und Multipart-Bearbeitung. Responses-Werkzeuge vom Typ `image_generation`, Teilbild-Streaming und `input_fidelity` werden hier nicht angeboten. Lesen Sie `GeneratedImage.Data` und `MediaType` aus dem Ergebnis.

Siehe den offiziellen [Bildleitfaden](https://developers.openai.com/api/docs/guides/image-generation) sowie [Sunburst](https://developers.openai.com/api/docs/models/gpt-image-2.5-sunburst) und [Flare](https://developers.openai.com/api/docs/models/gpt-image-2.5-flare).

---

## Anthropic (AnthropicService)

[Claude Fable 5.1](fable-5-1.md) bietet ab `Mythosia.AI` 8.0.0 / `Mythosia.AI.Abstractions` 4.0.0 Fortschrittsmeldungen, Anweisungen für einzelne Gesprächsrunden und Thinking-Binding-Diagnosen. Mythos 5.1 erfordert eine Einladung. Beide lehnen erzwungene Tool-Auswahl ab.

<a id="claude-opus-55"></a>

### Claude Opus 5.5: Fortschritt langer Werkzeugaufgaben anzeigen

Opus 5.5 eignet sich für Codeprüfungen und Dokumentrecherchen mit mehreren Werkzeugrunden. Die bestehenden Completion- und Run-APIs bleiben nutzbar, aber Fortschritt ist standardmäßig verborgen und gespeichertes Denken bindet den Verlauf. Benötigt Mythosia.AI 8.1.0 / Abstractions 4.1.0.

`ClaudeOpus5_5` wählt `claude-opus-5-5`: Text/Bilder als Eingabe, Text als Ausgabe, 1M Kontext und maximal 128K Ausgabetokens. Am 2026-09-24 lagen die regulären Ein-/Ausgabepreise bei $4/$20 pro Million Tokens; Sondermodi und Werkzeuge werden gesondert berechnet. [Offizielle Modelldaten](https://platform.claude.com/docs/en/models/opus-5-5/overview).

Bei unveränderten Diensteinstellungen verwendet `Auto` den Aufwand `Medium` und lässt lesbares Denken weg. Adaptives Denken bleibt immer aktiv. Wählen Sie `Low`, `Medium`, `High`, `XHigh` oder `Max`; die gemeinsamen Werte `ReasoningLevel.None` und `Minimal` werden abgelehnt. Das Standardmodell des Dienstes bleibt unverändert.

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

Das Beispiel fordert `Updates` an und beobachtet `StreamingContentType.Reasoning`. `Summarized` liefert Denkzusammenfassungen, `Omitted` verbirgt sie. Der Anzeigeparameter von `WithAdaptiveThinkingParameters(effort)` bleibt standardmäßig `Summarized`, anders als beim unveränderten Dienst. Nach einer normalen Completion lesen Sie `LastThinkingContent`. Regelmäßige Fortschrittsintervalle sind nicht garantiert.

Ein positiver alter `ThinkingBudget` wird auf high/xhigh/max abgebildet, nicht auf ein genaues Tokenbudget. Null oder negative Werte schalten Denken nicht aus. Ein Profil mit deaktiviertem Denken nutzt niedrigen Aufwand und ausgeblendete Denkausgabe. `MaxTokens` umfasst verborgenes Denken und Antworttext; prüfen Sie daher beim Wechsel Ausgabelimit und Kosten erneut.

Mythosia erhält signierte Thinking-Blöcke einschließlich leerer Blöcke über Gesprächsrunden und Werkzeugergebnisse hinweg. Verwenden Sie denselben Dienst und Chat; ändern Sie frühere Nachrichten, Systemtext oder Werkzeuge nicht, wenn Denken erhalten bleiben soll. `WithTurnInstruction`, `WithConversationInstruction` und `CachePreservation.Required` nutzen die vorhandenen Gesprächskontrollen. Mit `WithThinkingBinding` wählen Sie `Error` oder `DropBlock`; `LastInputTransformations` meldet verworfene Blöcke. Drop verwirft Denken. Der [Verlaufsleitfaden](fable-5-1.md) erklärt diese gemeinsamen Kontrollen; Standardwerte und Modellkompatibilität folgen Opus 5.5.

Lassen Sie `ForceFunctionName` ungesetzt; normale Werkzeugauswahl und `FunctionsDisabled` bleiben verfügbar. Assistant-Prefills werden abgelehnt, Sampling-Parameter nicht gesendet. Opus 5.5 liest kein Fable/Mythos-Denken; Fable 5.1 und Mythos 5.1 lesen auf der Claude API jedoch Opus-5.5-Denken. Modellwechsel können früheres Denken verlieren. Natives Computer-Toolset, Task-Budgets, Werkzeugänderungen im Gespräch, native Komprimierung und automatischer Server-Fallback sind nicht Teil dieser Integration. [Migrationsanforderungen](https://platform.claude.com/docs/en/models/opus-5-5/migration-guide) · [Native Funktionen](https://platform.claude.com/docs/en/models/opus-5-5/whats-new-opus-5-5).

Bei Opus 5.5 führt direktes Bearbeiten des Inhalts einer gespeicherten Assistant-Antwort vor dem HTTP-Aufruf zu `InvalidOperationException`; auch `DropBlock` erlaubt kein Umschreiben signierter Antworten. Senden Sie Korrekturen als neue Benutzereingabe oder beginnen Sie ein neues Gespräch. Änderungen früherer User-/System-Inhalte folgen dagegen der Präfixbindung des Anbieters.

Opus 5.5 Fast Mode ist mit erforderlicher Kontoberechtigung über [WithSpeed](request-building.md#inference-speed) auf der direkten Claude API verfügbar. Denkaufwand bleibt erhalten; es gelten Premiumpreise.

### Token-Zählung (Native API)

`GetInputTokenCountAsync` ist bei allen Anbietern verfügbar (siehe [Textvervollständigung](completions.md#token-zählung)). Anthropics Implementierung ruft den offiziellen `messages/count_tokens`-Endpunkt auf und liefert **exakte** Token-Zahlen statt lokaler Schätzungen:

```csharp
uint tokens = await service.GetInputTokenCountAsync("Dein Prompt hier");
uint total = await service.GetInputTokenCountAsync();
```

---

## Google (GoogleAIService)

Für lange Dokumentprüfungen und Aufgaben mit wiederholten Tool-Aufrufen kannst du Gemini 3.7 Flash oder 3.8 Flash über den bestehenden Google-Adapter auswählen. Die Unterstützung beginnt mit `Mythosia.AI` 8.0.0 / `Mythosia.AI.Abstractions` 4.0.0; Standard bleibt Gemini 3.6 Flash.

### Denk-Niveau

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
    .CreateRequest("Vergleiche Rolling- und Blue-Green-Deployments einschließlich der Risiken beim Rollback.")
    .WithReasoning(ReasoningLevel.High)
    .GetCompletionAsync();
```

Nutze `Low` für einen ersten Überblick und `High` für anspruchsvollere Prüfungen; mehr Reasoning kann Latenz und Tokenverbrauch erhöhen. Beide Modelle unterstützen `Low`, `Medium` und `High`, aber weder `Minimal` noch `None`. `GeminiThinkingLevel.Auto` lässt die Vorgabe aus; der Anbieterstandard für 3.8 ist `Medium`. `ThinkingLevel` legt den Servicestandard fest, `WithReasoning(...)` überschreibt einen logischen Request. Der Adapter lässt bei beiden Modellen `temperature`, `topP` und `topK` weg. Die Anbietergrenzen betragen 1.048.576 Eingabe- und 65.536 Ausgabetokens. [Gemini 3.7 Flash](https://ai.google.dev/gemini-api/docs/models/gemini-3.7-flash), [Gemini 3.8 Flash](https://ai.google.dev/gemini-api/docs/models/gemini-3.8-flash).

<a id="google-image-options"></a>

### Auflösungen und Seitenverhältnisse der Google-Bildmodelle

| Modell | `Resolutions` | `AspectRatios` |
| --- | --- | --- |
| `gemini-3.1-flash-image` | `Auto`, `FiveTwelve` (512), `OneK` (1K), `TwoK` (2K), `FourK` (4K) | 14 + `Auto` |
| `gemini-3.1-flash-lite-image` | `Auto`, `OneK` (1K) | 14 + `Auto` |
| `gemini-3-pro-image` | `Auto`, `OneK` (1K), `TwoK` (2K), `FourK` (4K) | 10 + `Auto` |

Die 10 Standardverhältnisse sind `1:1`, `2:3`, `3:2`, `3:4`, `4:3`, `4:5`, `5:4`, `9:16`, `16:9`, `21:9`. Der Satz mit 14 Verhältnissen ergänzt `1:4`, `4:1`, `1:8`, `8:1`. Alle Modelle erlauben außerdem `ImageAspectRatio.Auto`.

Verwenden Sie `ImageSize.Auto` oder `ImageSize.Preset(resolution, aspectRatio)`. `Auto` lässt die jeweilige Vorgabe aus. `GetImageCapabilities(model)` und `GenerateImagesAsync` / `EditImagesAsync` verwenden dieselben modellspezifischen Optionen. Nicht unterstützte explizite Werte lösen vor HTTP eine `NotSupportedException` aus; es gibt keine Größenanpassung oder Ersatzanfrage. Unbekannte eigene Modell-IDs behalten `Unknown` und werden nach der anbieterweiten Optionsprüfung unverändert weitergegeben.

Bei Flash-Lite nennen die [Modellseite](https://ai.google.dev/gemini-api/docs/models/gemini-3.1-flash-lite-image) und der Fließtext des Leitfadens 1K, die [Tabelle](https://ai.google.dev/gemini-api/docs/generate-content/image-generation#aspect_ratios_and_image_size) enthält jedoch auch eine 512-Spalte. Bis dieser Widerspruch geprüft ist, erlaubt die Bibliothek vorsichtshalber nur 1K. Damit wird keine beobachtete serverseitige Ablehnung von 512 behauptet.

---

## xAI (XAIService)

<a id="grok-47"></a>

### Grok 4.7

Für einen schnellen Entwurf mit anschließender gründlicher Code- oder Dokumentprüfung wählen Sie Grok 4.7 und passen den Aufwand je Anfrage an. Die bestehenden APIs für Antworten, Streaming, Run, lokale Tools, strukturierte Ausgaben und Bildeingaben bleiben nutzbar. `grok-4.7` verarbeitet Text/Bilder und gibt Text aus; das Kontextfenster umfasst 500.000 Tokens. Grok 4.5 bleibt das Standardmodell. Benötigt Mythosia.AI 8.1.0 / Abstractions 4.1.0.

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

`Low`, `Medium`, `High` und `XHigh` werden unterstützt. Das native `GrokReasoning.Auto` lässt `reasoning_effort` aus und verwendet den Anbieterstandard `High`; das gemeinsame `ReasoningLevel.Auto` lässt das Feld ebenfalls aus und verwendet für diese Anfrage den Anbieterstandard `High`. `None`, `Minimal` und `Max` werden vor dem Versand abgelehnt. `WithReasoning(...)` gilt für die logische Anfrage einschließlich Tool-Runden und Korrekturen strukturierter Ausgaben; `WithGrokReasoning(...)` setzt die Service-Grundeinstellung. Interne `DisableReasoning`-Profile verwenden `Low`. Optionale Reasoning-Zusammenfassungen sind nicht das vollständige interne Reasoning.

`WithSpeed(InferenceSpeed.Standard)` sendet `service_tier: "default"`; `Fast` sendet an unterstützten xAI-Endpunkten `"priority"` und kann mehr kosten. `ProviderDefault` überschreibt nichts. Der Server kann auf normale Verarbeitung zurückstufen; den gemeldeten Modus zeigt `result.Processing`. Das ist priorisierte Verarbeitung von `grok-4.7`, nicht die Cursor/Grok Build vorbehaltene Variante „Grok 4.7 Fast“, für die keine öffentliche API-Modell-ID existiert.

`GetCapabilities()` beschreibt die gewählte Anfrage lokal, ohne Kontoberechtigungen zu prüfen. Die Integration verwendet Chat Completions. Responses-spezifisches verschlüsseltes Reasoning, gehostete Web-/X-Suche, native asynchrone Tools, cacheerhaltende Änderungen und `SteerAsync` sind hier nicht angebunden. Client-Funktionen verwenden die bestehende lokale Tool-Schleife; `run.CanSteer` ist false.

[Grok 4.7](https://docs.x.ai/developers/grok-4-7) · [reasoning_effort](https://docs.x.ai/developers/model-capabilities/text/reasoning) · [Priority Processing](https://docs.x.ai/developers/advanced-api-usage/priority-processing)

### Den Aufwand passend zur Aufgabe wählen

Verwende weniger Aufwand für einen schnellen ersten Entwurf und mehr Reasoning für schwierige Prüfungen, bei denen die Antwortqualität wichtiger als die Antwortzeit ist. Wähle Grok 4.6 ausdrücklich aus, um die zusätzliche Stufe `XHigh` zu nutzen.

```csharp
using Mythosia.AI.Extensions;
using Mythosia.AI.Models;
using Mythosia.AI.Services.xAI;

var grok = new XAIService(apiKey, httpClient);
grok.ChangeModel(AIModels.xAI.Grok4_6);
grok.WithGrokReasoning(GrokReasoning.Low);

string review = await grok
    .CreateRequest("Vergleiche Rolling- und Blue-Green-Deployments einschließlich der Wiederherstellung nach Ausfällen.")
    .WithReasoning(ReasoningLevel.XHigh)
    .GetCompletionAsync();
```

Grok 4.6 unterstützt `Low`, `Medium`, `High` und `XHigh` (`GrokReasoning.XHigh`). `Auto` lässt `reasoning_effort` weg und übernimmt den Anbieterstandard `High`; mit `None` lässt sich Reasoning nicht abschalten. Höherer Aufwand kann Latenz und Tokenverbrauch erhöhen. Aus Kompatibilitätsgründen bleibt Grok 4.5 das Standardmodell von `XAIService`. Version 4.5 unterstützt `Low` bis `High`, Version 4.3 `None` bis `High`; der Adapter lehnt `XHigh` bei diesen älteren Modellen vor dem Senden ab.

`WithGrokReasoning(...)` und das bestehende `WithGrokParameters(...)` legen die Grundeinstellung des Dienstes fest. Bei Grok 4.6 überschreibt das gemeinsame `WithReasoning(...)` sie für eine logische Anfrage einschließlich Tool-Runden und Korrekturen strukturierter Ausgaben und stellt sie danach wieder her. Interne `DisableReasoning`-Profile verwenden bei diesem stets schlussfolgernden Modell `Low`. Cache-erhaltende Updates und gehostete Web-/Dateisuche sind für xAI über diese gemeinsamen Optionen nicht integriert.

Grok 4.6 kann vom Anbieter erzeugte Reasoning-Zusammenfassungen als `StreamingContentType.Reasoning` liefern, wenn die Beobachtung `new StreamOptions().WithReasoning()` aktiviert. Zusammenfassungen sind optional und enthalten nicht das vollständige interne Reasoning. Dieselben Optionen gelten für Run; eine Änderung der Streamanzeige verändert nicht den angeforderten Aufwand.

[Grok 4.6](https://docs.x.ai/developers/grok-4-6) · [reasoning_effort](https://docs.x.ai/developers/model-capabilities/text/reasoning)

### Grok Imagine Image 2.0

Nutze die Bilderzeugung, um aus einer Produktbeschreibung einen visuellen Entwurf zu machen, oder kombiniere beim Bearbeiten Motiv und Hintergrund aus Referenzfotos. `XAIService` bietet beides über dasselbe `IImageGenerationService` wie OpenAI und Google, ab `Mythosia.AI` 8.0.0 / `Mythosia.AI.Abstractions` 4.0.0.

Das unabhängige Standardmodell für Bilder ist `AIModels.xAI.GrokImagineImage2_0` (`grok-imagine-image-2.0`). Bildanfragen ändern weder das Chatmodell noch dessen Gesprächsverlauf.

```csharp
using Mythosia.AI.Models;
using Mythosia.AI.Models.Images;
using Mythosia.AI.Services;
using Mythosia.AI.Services.xAI;

IImageGenerationService images = new XAIService(apiKey, httpClient);
var generated = await images.GenerateImagesAsync(new ImageGenerationRequest
{
    Model = AIModels.xAI.GrokImagineImage2_0,
    Prompt = "Ein Glaspavillon bei Sonnenaufgang, breite Bildkomposition",
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

Übergebe zur Bearbeitung die Bildbytes in der Reihenfolge, auf die sich der Prompt bezieht:

```csharp
var edited = await images.EditImagesAsync(new ImageEditRequest
{
    Prompt = "Setze das Motiv aus Bild 1 in die Szene aus Bild 2.",
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

Jedes `GeneratedImage.Data` enthält dekodierte Bildbytes; wähle die Dateiendung anhand von `MediaType`. Der Adapter fordert eingebettete Base64-Ausgaben an und lädt keine Bild-URLs des Anbieters herunter. `Count` erlaubt 1–10 Ergebnisse; die Bearbeitung akzeptiert 1–5 Referenzbilder im Format JPEG, PNG oder WebP.

Für xAI verwenden Sie `ImageSize.Auto` oder `ImageSize.Preset(ImageResolution.OneK, ImageAspectRatio.SixteenByNine)`. Auflösungen: `Auto`, `OneK`, `TwoK`; Verhältnisse müssen vom Modell unterstützt werden. `Pixels(...)` wird abgelehnt, da exakte Maße nicht angefordert werden können.

xAI unterstützt nur den neuen gemeinsamen Standard `ImageOutputFormat.Auto`. Ohne Codec-Auswahl werden explizites `Jpeg`, `Png` und `WebP` vor dem Senden abgelehnt. Wählen Sie die Dateiendung nach `GeneratedImage.MediaType`; die Bibliothek transkodiert nicht. Qualität: `ImageQuality.Auto`, `Low`, `Medium`; Hintergrund: nur `ImageBackground.Auto`. Explizite Kompression und eine separate `Mask` sind nicht unterstützt.

Google akzeptiert `ImageSize.Auto` oder `Preset` mit modellspezifischen Auflösungen und Verhältnissen; siehe [Modellspezifische Google-Bildoptionen](#google-image-options). Ausgabe: `ImageOutputFormat.Auto` oder `Jpeg`; `Png`/`WebP` werden abgelehnt. Google und xAI lehnen `Pixels` ab; OpenAI akzeptiert `Auto`/`Pixels` und lehnt `Preset` ab. Siehe [Migration](#image-options-migration).

[Grok Imagine Image 2.0](https://docs.x.ai/developers/models/grok-imagine-image-2.0) · [Image API](https://docs.x.ai/developers/rest-api-reference/inference/images)

---

## DeepSeek (DeepSeekService)

Verwende DeepSeek Flash für schnelle Antworten mit anschließender gründlicher Prüfung oder zum Erklären von Diagrammen und Screenshots. `AIModels.DeepSeek.Flash` (`deepseek-flash`) wählt V4.1 Flash mit nativer Bilderkennung, veröffentlicht am 10. September 2026. Die bestehenden APIs für Completion, Streaming, Run, Funktionen und RAG gelten weiterhin, ab `Mythosia.AI` 8.0.0 / `Mythosia.AI.Abstractions` 4.0.0.

> Die veröffentlichten Pakete `Mythosia.AI` 8.0.0 / `Mythosia.AI.Abstractions` 4.0.0 unterstützen Flash bereits grundlegend. `AIModels.DeepSeek.V4Pro`, `UseResponsesApi`, Files API, `DeepSeekImageFileContent`: Benötigt Mythosia.AI 8.1.0 / Abstractions 4.1.0. [v8.1.0](../../src/core/Mythosia.AI/RELEASE_NOTES.md#v810).

Für reine Textaufgaben steht `AIModels.DeepSeek.V4Pro` (`deepseek-v4-pro`, V4-Pro-0813) bereit. Flash bleibt Standard und unterstützt Bilder; beide bieten Low/High/Max-Reasoning und dieselbe Ausgabegrenze. Mit `UseResponsesApi = true` vor dem Erstellen einer Anfrage verwenden die bestehenden Completion-, Streaming-, Run- und lokalen Funktions-APIs Responses. Der Standard bleibt `false`, damit bestehende Anwendungen Chat Completions behalten. Die Wahl wird für die Anfrage samt Tool-Runden festgehalten. Responses überträgt den gesamten Gesprächs- und ursprünglichen Reasoning-Verlauf erneut, ohne gespeicherte Antwort-IDs vorauszusetzen.

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

Ein Bild einmal hochzuladen lohnt sich, wenn mehrere Fragen oder Gespräche es wiederverwenden. `UploadFileAsync` akzeptiert einen Pfad oder einen vom Aufrufer verwalteten Stream mit Dateiname; purpose ist `user_data`. JPEG, PNG, GIF und WebP dürfen höchstens 64 MiB groß sein. `DeepSeekImageFileContent` referenziert das Bild bei Flash über beide Übertragungswege; PDF-/Dokumenteingaben sind damit nicht möglich, V4 Pro lehnt es ab. Ohne Ablaufzeit bleibt die Datei dauerhaft; `expiresAfterSeconds` erlaubt 3600–2592000 Sekunden. Erst löschen, wenn kein Gespräch die Datei mehr benötigt.

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

`GetFileAsync` liest Metadaten, `ListFilesAsync(new DeepSeekFileListOptions { After = lastId, Limit = 20, Order = DeepSeekFileOrder.Ascending })` eine Seite und `DeleteFileAsync` löscht. Solange `HasMore` true ist, wird `LastId` zum nächsten `After`; auch `Descending` ist möglich. Ein Download-Endpunkt für Dateiinhalte ist nicht dokumentiert. Die Chat UI bietet Flash und V4 Pro und nutzt denselben aktuellen Katalog für Query-Rewriting. Der frühere gespeicherte UI-Name `DeepSeekChat` wird zu Flash migriert; beliebige Modell-IDs bleiben erhalten.

[Responses](https://api-docs.deepseek.com/guides/responses_api/) · [Files](https://api-docs.deepseek.com/guides/files_api/) · [Models and limits](https://api-docs.deepseek.com/quick_start/pricing/)

```csharp
using Mythosia.AI.Extensions;
using Mythosia.AI.Models;
using Mythosia.AI.Models.Streaming;
using Mythosia.AI.Services.DeepSeek;

var deepseek = new DeepSeekService(apiKey, httpClient);
deepseek.ChangeModel(AIModels.DeepSeek.Flash);
deepseek.WithDeepSeekReasoning(DeepSeekReasoning.Low);

string draft = await deepseek.GetCompletionAsync("Vergleiche Rolling und Blue-Green Deployment einschließlich der Rollback-Risiken.");

await using var run = await deepseek
    .CreateRequest("Prüfe die Annahmen dieses Vergleichs.")
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

`ThinkingEnabled` bleibt standardmäßig `false`. `WithDeepSeekReasoning(...)` aktiviert Reasoning und setzt den dauerhaften `ReasoningEffort` (`Auto`, `Low`, `High`, `Max`); dessen `Auto` lässt den Aufwand weg und nutzt den Anbieterstandard `High`. Das gemeinsame `WithReasoning(...)` gilt für eine logische Anfrage mit Tool-Runden: `None` deaktiviert, `Minimal`/`Low` wird `Low`, `Medium`/`High`/`XHigh` wird `High`, `Max` bleibt `Max`. Gemeinsames `Auto` behält die Grundkonfiguration. Mehr Aufwand kann Latenz und Tokenverbrauch erhöhen. Das Ändern von `ReasoningEffort` allein aktiviert Reasoning nicht.

Registriere lokale Funktionen mit `WithFunction(...)`, damit das Modell Daten über deinen Code abruft oder Aktionen ausführt. Tools funktionieren mit und ohne Reasoning. Chat Completions lehnt bei Reasoning eine erzwungene/verpflichtende Auswahl ab; dort automatische Auswahl verwenden. Mit `UseResponsesApi = true` kann `ForceFunctionName` auch bei Reasoning eine Funktion auswählen; der Adapter sendet `type` und `name` direkt im Responses-Objekt `tool_choice`. Native asynchrone Tools werden dadurch nicht aktiviert. Der Adapter bewahrt `reasoning_content` und Aufruf-IDs für weitere Runden. Run und bestehendes Streaming zeigen `StreamingContentType.Reasoning` bei `StreamOptions.WithReasoning()`; diese Beobachtungsoption aktiviert Reasoning nicht selbst. Nutzungsdaten enthalten gemeldete Cache- und Reasoning-Tokens. Automatische Kontextwiederherstellung verwendet die gemeinsame Streaming-Schleife. Benötigen Tools frühere native Reasoning-Historie, wird automatische Verdichtung zum Erhalt dieser Historie blockiert; der Überlauffehler bleibt sichtbar.

Sende Diagramme oder Screenshots mit den vorhandenen Nachrichtentypen:

```csharp
using Mythosia.AI.Models.Messages;

var message = new Message(ActorRole.User, new List<MessageContent>
{
    new TextContent("Erkläre den Trend in diesem Diagramm und benenne unleserliche Beschriftungen."),
    new ImageContent(await File.ReadAllBytesAsync("chart.png"), "image/png")
});
string description = await deepseek.GetCompletionAsync(message);
```

`ImageContent` akzeptiert JPEG-, PNG-, GIF- oder WebP-Bytes oder eine öffentliche HTTP(S)-URL, die der Anbieter abruft. Das Beispiel verwendet eine Benutzernachricht. Die aktuelle API akzeptiert Bilder auch in Tool-Nachrichten; registrierte Funktionen geben über den gemeinsamen Vertrag weiterhin Text zurück. Manuelle Bildnachrichten mit `ActorRole.Function` benötigen die passende Aufruf-ID in `MessageMetadataKeys.FunctionId` (`tool_call_id` auf der Leitung). Größen- und Gesamtgrenzen stehen im aktuellen Vision-Leitfaden. Bilderzeugung bleibt nicht unterstützt.

Beide Modelle bieten 1M Kontext und bis zu 384K (`393216`) Ausgabetokens; das Standardbudget bleibt 8.000. Reasoning lässt temperature/penalty weg und nutzt `top_p` mindestens 0,95; ohne Reasoning entfällt `top_p`. Responses verwendet die bestehenden typisierten Ausgabe-APIs für natives JSON schema. Hintergrundausführung, serverseitiges `store`/`previous_response_id`, gehostete Suche, `CachePreservation.Required`, native asynchrone Tools, `SteerAsync` und Bilderzeugung sind nicht unterstützt. Lokales RAG und gewöhnliche Tool-Runden bleiben verfügbar.

`V4Flash`, `Chat` und `Reasoner` bleiben obsolete Konstanten mit Warnung und unveränderten Wire-IDs. Der Anbieter leitet den eingestellten Alias `deepseek-v4-flash` vorübergehend zu V4.1 Flash; die Bibliothek schreibt die Konstante nicht um. Wähle im neuen Code `Flash`. `UseReasonerModel()` wählt Flash mit `High`-Reasoning.

[DeepSeek V4.1 Flash](https://api-docs.deepseek.com/updates/) · [Thinking](https://api-docs.deepseek.com/guides/thinking_mode/) · [Vision](https://api-docs.deepseek.com/guides/vision/) · [Tools](https://api-docs.deepseek.com/guides/tool_calls/) · [Limits](https://api-docs.deepseek.com/quick_start/pricing/)

---

## Perplexity (PerplexityService)

Verwenden Sie Perplexity, wenn Antworten aktuelle Informationen und überprüfbare Quellen benötigen. `PerplexityService` ruft die Agent API auf. Die eigenständige Suche und Embeddings ermöglichen eine Dokumentensuche mit einem selbst gewählten Antwortmodell.

```csharp
using Mythosia.AI.Models;
using Mythosia.AI.Services.Perplexity;

var service = new PerplexityService(apiKey, httpClient);
service.ChangeModel(AIModels.Perplexity.Sonar);

await using var run = await service.StartRunAsync("Vergleiche aktuelle Verfahren zum Batterierecycling und nenne die Quellen.");
Console.WriteLine((await run.Result).Text);
foreach (var source in run.Citations)
    Console.WriteLine(source.Url);
```

Der [Perplexity-Leitfaden](perplexity.md) erklärt Recherche-Presets, lokale Funktionen, gehostete Werkzeuge und längere Hintergrundaufgaben. Die gewohnten APIs für Completion, Streaming, Run und Quellenangaben stehen weiterhin zur Verfügung.

Diese Version stellt den Dienst auf `/v1/agent` um. `AIModels.Perplexity.Sonar` wählt jetzt `perplexity/sonar`. Der Anbieter hat die Abschaltung der alten Sonar-Endpunkte zum 27. September 2026 angekündigt; bestehende Integrationen müssen migriert werden. [Perplexity](https://community.perplexity.ai/t/sonar-is-moving-to-the-agent-api/5802)

---

## Alibaba / Qwen (QwenService)

Installiere das separate Paket:

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

Verfügbare Modelle: `QwenMax`, `QwenPlus`, `QwenTurbo`, `Qwen3` und Varianten.

Wähle beim Erstellen des Dienstes mit `EndpointPlatform` einen kompatiblen Endpunkt aus:

```csharp
var vllmService = new QwenService(
    "http://localhost:8000",
    EndpointPlatform.Vllm,
    http);
```

[Modelloptionen mit gemeinsamen Fähigkeitsdefinitionen aufbauen](model-capabilities.md).
