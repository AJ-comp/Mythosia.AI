# Umstieg auf Mythosia.AI 8

Diese Version hilft, Anfrageeinstellungen getrennt zu halten, laufende Arbeit abzubrechen und Antworten samt Verbrauch und Quellen zu speichern. Sie bündelt sechs Architekturänderungen, Anbieter- und Modellaktualisierungen sowie Korrekturen aus drei adversarialen Prüfungen in einem Major-Upgrade.

Aktualisiere nur die verwendeten Pakete gemeinsam und baue die Verbraucher neu. Mythosia.AI bringt die passende Abstractions-Abhängigkeit mit. Die Tabelle ordnet der veröffentlichten Basis die kompatiblen Versionen dieses Releases zu.

| Paket | Veröffentlichte Basis | Zielversion |
| --- | --- | --- |
| `Mythosia.AI` | 7.1.0 | [8.0.0](../../src/core/Mythosia.AI/RELEASE_NOTES.md#v800) |
| `Mythosia.AI.Abstractions` | 3.1.0 | [4.0.0](../../src/core/Mythosia.AI.Abstractions/RELEASE_NOTES.md#v400) |
| `Mythosia.AI.Providers.Alibaba` | 2.0.1 | [3.0.0](../../src/core/Mythosia.AI.Providers.Alibaba/RELEASE_NOTES.md#v300) |
| `Mythosia.AI.Rag` | 7.6.0 | [8.0.0](../../src/rag/Mythosia.AI.Rag/RELEASE_NOTES.md#v800) |
| `Mythosia.AI.Mcp` | 0.0.1-preview | [0.1.0-preview](../../src/integrations/Mythosia.AI.Mcp/RELEASE_NOTES.md#v010-preview) |
| `Mythosia.AI.Serving.Vllm` | 1.0.0-preview | [1.0.0](../../src/serving/Mythosia.AI.Serving.Vllm/RELEASE_NOTES.md#v100) |

`Mythosia.AI.Serving.Vllm` wechselt in diesem Release von `1.0.0-preview` zur stabilen Version `1.0.0`. Die bestehenden APIs für Modelle, Status, Serverversion und Metriken bleiben in einem eigenständigen Paket ohne Abhängigkeit vom AI-Kernpaket erhalten.

## Vom Bedarf zur Änderung

| Bedarf | Änderung und Migration |
| --- | --- |
| Fehlerhafte Bildoptionen vor dem Senden erkennen | Strings durch `ImageQuality`, `ImageBackground`, `ImageOutputFormat` und `ImageSize.Pixels(...)` / `ImageSize.Preset(...)` ersetzen. Die Anbieter unterstützen unterschiedliche Optionen. |
| Mehrere Anfragen unabhängig vorbereiten | Mit `CreateRequest(...)` beginnen und den von jedem `With...` zurückgegebenen Builder behalten. Service-Setter ändern weiterhin gemeinsame Standardwerte. |
| Daten direkt aus asynchronen Tools zurückgeben | Attributregistrierte Methoden können Objekte über `Task<T>` / `ValueTask<T>` zurückgeben und einen injizierten `CancellationToken` erhalten. Ausnahmen gelten als Fehler; String-Handler bleiben unterstützt. |
| Bei Abbruch nicht weiter warten | `cancellationToken` an Completion, Run und unterstützte RAG-Einstiege übergeben. Das beendet lokale Arbeit und kooperative Tools, garantiert aber keinen Remote-Abbruch und macht externe Aktionen nicht rückgängig. |
| Antwort, Verbrauch und Quellen zusammen speichern | `AIRun.Result` liefert `Task<AIRunResult>`. Für Text `(await run.Result).Text` lesen. Das Ergebnis entsteht auch ohne Stream-Leser. |
| Passende Modelloptionen anzeigen | `request.GetCapabilities()` oder Service-/Bildabfragen verwenden. `Supported`, `Unsupported` und `Unknown` beschreiben lokales Bibliothekswissen, keinen Live-Kontozugriff. |

## Aufrufer und eigene Anbieter anpassen

Bildoptionstypen, `AIRun.Result` und geänderte Cancellation-Signaturen brechen Verträge. Eigene `IAIService`-Implementierungen und Overrides geänderter öffentlicher Überladungen müssen den Token ergänzen und weitergeben. Der Anbieter-Override `GetCompletionAsync(Message)` behält seine Signatur und reicht `RequestCancellationToken` weiter. Eigene `AIRun`-Implementierungen liefern `AIRunResult`. GetCompletionAsync behält das String-Ergebnis, typisierte Completion und `StructuredStreamRun<T>.Result` ihre typisierten Ergebnisse. Eingabeannehmende Service-/RAG-StreamAsync-Methoden bleiben in v8 öffentlich. RunAgentAsync und RunAgentStreamAsync behalten Kompatibilitätsverhalten und obsolete-Warnungen. Für neue Fortschritts-, Abbruch- und unterstützte Steuerungsabläufe Run verwenden.

## Eine Anfrage mit Ergebnis und optionalem Fortschritt

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

OpenAI verwendet Pixel, Google und xAI `ImageSize.Preset(...)`. Auto nur bei unterstützter expliziter Formatwahl ändern; beim Speichern das zurückgegebene `GeneratedImage.MediaType` beachten.

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

Registrierte Tools können wie unten Anwendungsobjekte liefern. Der Low-Level-Handler `HandlerWithCancellation` liefert weiterhin `Task<string>`; ein neuer Objekt-Wrapper ist nicht nötig.

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

## Anbieteränderungen und Prüfung

Enthalten sind auch die vorbereiteten Integrationen für Fable 5.1, Gemini 3.7/3.8 Flash, Grok 4.6, DeepSeek Flash und Perplexity Agent sowie gemeinsame Bilderzeugung und -bearbeitung für OpenAI, Google und xAI. Entfernte Modellkonstanten und der Perplexity-Endpunktwechsel können Aufruferänderungen verlangen; Einzelheiten stehen im Anbieterleitfaden und den Paket-Release-Notes.

Perplexity-Recherche mit `PerplexityAgentOptions` konfigurieren. Tests für Profile, Custom Skill und Connector sind vorbereitet, benötigen aber registrierte Kontoressourcen. MCP bleibt preview. Nach Beginn der Freigabe scheitern Aufrufe mit `ObjectDisposedException`; nach Ende der Leseschleife scheitern neue Aufrufe mit `McpException`, statt unbegrenzt zu warten.

Drei adversariale Prüfungen verbesserten Anfragekopien, Tool-Ergebnisse, Abbruch/Bereinigung, Tokenzählung, Antwortprüfung und den MCP-Lebenszyklus. Die dritte ergänzte 43 Regressionstests; alle 2.703 Tests bestanden. Dokumentation wurde in 13 Sprachen geprüft. Dabei wurden keine Live-Anbieter-APIs aufgerufen; Unit-Tests belegen nicht sämtliche kontogebundenen Integrationen.

## Ausführliche Leitfäden

- [CreateRequest / AIRequestBuilder](request-building.md)
- [AIRun / AIRunResult](execution-api-transition.md)
- [ImageQuality / ImageSize](providers.md#image-options-migration)
- [CancellationToken](completions.md#completion-cancellation)
- [Task<T> / ValueTask<T> / HandlerWithCancellation](function-calling.md#tool-execution-contract)
- [GetCapabilities](model-capabilities.md)
- [PerplexityAgentOptions](perplexity.md)
