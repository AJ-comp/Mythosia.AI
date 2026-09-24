# Funktionsaufruf

> GPT-6 Sol/Luna sind noch nicht veröffentlicht. Siehe [Modellwahl und Voraussetzungen](providers.md#gpt-6-sol-luna).

Für eine fertige Antwort mit Stoppschaltfläche übergeben Sie `cancellationToken` an `GetCompletionAsync`. Run dient Fortschrittsereignissen oder unterstützten zusätzlichen Anweisungen. Siehe [Completion-Abbruch](completions.md#completion-cancellation).

Für unabhängige Einstellungen und wiederverwendbare Varianten verwenden Sie den [Anfrage-Builder](request-building.md). Rufen Sie `CreateRequest(...)` vor `With...` auf. Service-Eigenschaften und dessen Fluent-Methoden behalten ihr bisheriges Verhalten.

## Wozu Funktionsaufruf?

LLMs können nur Text generieren — sie können keine Wetterdaten abrufen, Datenbanken abfragen oder selbstständig APIs aufrufen. **Ohne** Funktionsaufruf müsstest du die Absicht des Modells manuell interpretieren:

```csharp
// ❌ Ohne Funktionsaufruf — manuelle Absichtserkennung
var reply = await service.GetCompletionAsync("Wie ist das Wetter in Berlin?");
// reply = "Dafür müsste ich einen Wetterdienst abfragen."

// Du musst selbst herausfinden, dass der Nutzer Wetterdaten möchte, "Berlin" extrahieren und die API aufrufen
if (reply.Contains("Wetter"))
{
    var city = ExtractCity(reply); // fragile Regex- oder Keyword-Erkennung
    var weather = await weatherApi.GetAsync(city);
    // Dann nochmal fragen mit den Wetterdaten...
}
```

Das ist fehleranfällig, skaliert nicht und erfordert, dass du jeden möglichen Nutzerauftrag vorhersehst. **Mit** Funktionsaufruf entscheidet das Modell **wann** und **mit welchen Argumenten** es deinen Code aufruft:

```csharp
// ✅ Mit Funktionsaufruf — das Modell übernimmt Absicht + Extraktion
var service = new OpenAIService(apiKey, http)
    .WithFunction(
        "get_weather",
        "Gibt das aktuelle Wetter für einen Ort zurück",
        ("location", "Stadt und Land", required: true),
        (string location) => weatherApi.Get(location)
    );

var response = await service.GetCompletionAsync("Wie ist das Wetter in Berlin?");
// Das Modell ruft get_weather("Berlin, Deutschland") auf und antwortet natürlich.
```

Du definierst **was** dein Code kann; das Modell entscheidet **wann** und **wie** es genutzt wird.

## Schnellbeispiel

```csharp
var service = new OpenAIService(apiKey, http)
    .WithFunction(
        "get_weather",
        "Gibt das aktuelle Wetter für einen Ort zurück",
        ("location", "Stadt und Land", required: true),
        (string location) => $"Das Wetter in {location} ist sonnig, 22°C"
    );

var response = await service.GetCompletionAsync("Wie ist das Wetter in Berlin?");
// Das Modell ruft get_weather("Berlin, Deutschland") auf und verarbeitet das Ergebnis.
```

## Funktionen mit Attributen definieren

Für komplexere Funktionen verwende die Attribute `[AiFunction]` und `[AiParameter]`:

```csharp
using Mythosia.AI.Attributes;
using Mythosia.AI.Extensions;

public sealed class ProductFunctions
{
    [AiFunction("search_products", "Durchsucht den Produktkatalog")]
    public string SearchProducts(
        [AiParameter("Suchanfrage", required: true)] string query,
        [AiParameter("Maximale Anzahl Ergebnisse")] int limit = 5)
    {
        // ... deine Implementierung
        return JsonSerializer.Serialize(results);
    }
}
```

Dann registrieren:

```csharp
service.WithFunctions(new ProductFunctions());
```

## Funktionsaufruf-Policy

Steuere, wann das Modell Funktionen aufrufen darf:

```csharp
using Mythosia.AI.Models.Functions;

// Modell entscheidet selbst (Standard)
service.FunctionCallMode = FunctionCallMode.Auto;

// Modell muss immer eine Funktion aufrufen
service.ForceFunctionName = "search_products";

// Funktionsaufruf deaktivieren
service.FunctionCallMode = FunctionCallMode.None;
```

[Claude Fable 5.1](fable-5-1.md) bietet ab `Mythosia.AI` 8.0.0 / `Mythosia.AI.Abstractions` 4.0.0 Fortschrittsmeldungen, Anweisungen für einzelne Gesprächsrunden und Thinking-Binding-Diagnosen. Mythos 5.1 erfordert eine Einladung. Beide lehnen erzwungene Tool-Auswahl ab.

## Massenregistrierung aus einer Klasse

Alle mit `[AiFunction]` markierten Methoden eines Objekts auf einmal registrieren:

```csharp
var tools = new MyTools();
service.WithFunctions(tools);  // Scannt Instanzmethoden mit [AiFunction]
```

Für statische Methoden:

```csharp
service.WithStaticFunctions<MyTools>();  // Scannt statische Methoden mit [AiFunction]
```

## Asynchrone Funktions-Handler

Alle `WithFunction`-Überladungen haben `WithFunctionAsync`-Entsprechungen, die `Func<..., Task<string>>` akzeptieren:

```csharp
service.WithFunctionAsync<string>(
    "fetch_data",
    "Ruft Daten von einer externen API ab",
    ("url", "Die abzurufende URL", required: true),
    async (string url) =>
    {
        var result = await httpClient.GetStringAsync(url);
        return result;
    }
);
```

Unterstützt 0 bis 3 Parameter, genau wie die synchronen Varianten.

## Funktionen vorübergehend deaktivieren

Funktionsaufruf für eine einzelne Anfrage deaktivieren, ohne Registrierungen zu entfernen:

```csharp
// Erweiterungsmethode — gibt Ergebnis ohne Funktionen zurück
string answer = await service.AskWithoutFunctionsAsync("Antworte direkt");

// Oder Property umschalten
service.WithoutFunctions();  // setzt FunctionsDisabled = true
```

## FunctionBuilder verwenden

Funktionsdefinitionen programmatisch aufbauen:

```csharp
using Mythosia.AI.Builders;
using Mythosia.AI.Extensions;

var fn = FunctionBuilder
    .Create("get_stock_price")
    .WithDescription("Gibt den aktuellen Aktienkurs zurück")
    .AddParameter("ticker", "string", "Aktien-Ticker-Symbol", required: true)
    .WithHandler(args => FetchStockPrice(args["ticker"].ToString() ?? string.Empty))
    .Build();

service.WithFunction(fn);
```

<a id="tool-execution-contract"></a>

## Objekte aus asynchronen Tools zurückgeben und Arbeit abbrechen

Ein Datei- oder Datenbanktool liefert oft nach asynchroner Ein-/Ausgabe ein Objekt. Eine Stopp-Schaltfläche sollte außerdem die noch laufende Operation erreichen. Synchrone Objektrückgaben waren bereits möglich; diese Änderung vereinheitlicht asynchrone Rückgaben und erfasst Ausnahmen als Fehler.

Before: Ein asynchrones Tool musste sein Ergebnis selbst serialisieren. Bei `Task<FileResult>` ging der Wert verloren; stattdessen wurde `"Success"` geliefert.

```csharp
using System.IO;
using System.Text.Json;
using System.Threading.Tasks;
using Mythosia.AI.Attributes;

public sealed class FileToolsBefore
{
    [AiFunction("read_file", "Eine Textdatei lesen")]
    public async Task<string> ReadFileAsync(string path)
    {
        string text = await File.ReadAllTextAsync(path);
        return JsonSerializer.Serialize(new { Path = path, Text = text });
    }
}
```

After: Gib das Objekt direkt zurück und reiche das injizierte Abbruchtoken an die Ein-/Ausgabe weiter. Anwendungscode braucht keinen neuen Ergebnis-Wrapper oder Adapter.

```csharp
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Mythosia.AI.Attributes;

public sealed record FileResult(string Path, string Text);

public sealed class FileTools
{
    [AiFunction("read_file", "Eine Textdatei lesen")]
    public async Task<FileResult> ReadFileAsync(
        string path, CancellationToken cancellationToken)
    {
        string text = await File.ReadAllTextAsync(path, cancellationToken);
        return new FileResult(path, text);
    }
}
```

Die Registrierung mit `[AiFunction]` unterstützt Objekte, `Task<T>` und `ValueTask<T>`; Werte außer Zeichenfolgen werden in JSON umgewandelt. `string`, `Task<string>` und `ValueTask<string>` bleiben unverändert und erhalten keine zusätzlichen JSON-Anführungszeichen. Auch `Task` und `ValueTask` ohne Ergebnis werden abgewartet. Synchrone Objektrückgaben bleiben erhalten. Ein null-Rückgabewert wird zu `"Done"`; abgeschlossene `Task` / `ValueTask` ohne Ergebnis werden zu `"Success"`.

Auch zur Laufzeit erkannte Rückgaben werden abgewartet und nach denselben Regeln serialisiert: `Task<T>` als `Task` oder `object` sowie `ValueTask<T>` als `object`. Jeder `ValueTask` wird nur einmal konsumiert.

Die Bibliothek stellt einen `CancellationToken`-Parameter bereit und lässt ihn im Argumentschema des Modells weg. Registriere die Methoden mit `WithFunctions(...)` oder `WithStaticFunctions<T>()` am Dienst oder Request-Builder.

Tool-Methoden mit `async void` werden bei der Registrierung abgelehnt. Gib `Task` oder `ValueTask` zurück, damit die Ausführung den Abschluss abwarten, Fehler beobachten und nach einem Abbruch aufräumen kann.

```csharp
using Mythosia.AI.Extensions;

await using var run = await service
    .CreateRequest("Lies report.txt und fasse die Datei zusammen.")
    .WithFunctions(new FileTools())
    .StartRunAsync(cancellationToken: cancellationToken);

string answer = (await run.Result).Text;
```

`run.Cancel()`, das Abbrechen des an `StartRunAsync` übergebenen Tokens oder das Freigeben eines aktiven Runs erreicht lokale Tools mit Abbruchunterstützung. Funktionen müssen das Token verwenden; ignorierender Code lässt sich nicht zwangsweise stoppen. Noch nicht gestartete Aufrufe werden übersprungen und erhalten Abbruchergebnisse. Gestartete Funktionen werden abgewartet, damit Aufrufe und Ergebnisse im Verlauf zusammenpassen. Der Run bleibt nach einem Ausführungsabbruch abgebrochen und startet keine weitere Modellrunde.

Wirft ein Abbruch-Callback beim fehlgeschlagenen Run-Start oder beim Freigeben einer MCP-Verbindung eine Ausnahme, wird die Bereinigung von Sitzung oder Transport trotzdem versucht. Der ursprüngliche Fehler und Bereinigungsfehler bleiben erhalten, bei Bedarf gemeinsam in einer `AggregateException`. Gleichzeitige asynchrone Aufrufe von `McpConnection.DisposeAsync()` warten auf dieselbe Bereinigung. Der Transport wird geschlossen, bevor auf das Ende der Leseschleife gewartet wird, damit Lesevorgänge enden können, die einen Verbindungsabbau benötigen.

Damit verspätete Toolaufrufe beim Herunterfahren nicht dauerhaft warten, werden neue Aufrufe von `InitializeAsync`, `RefreshToolsAsync` und `CallToolAsync` ab Beginn der Verbindungsfreigabe mit `ObjectDisposedException` abgewiesen. Eine Antwort mit passender Anfrage-ID, aber fehlerhaftem Inhalt wird übersprungen, während die Anfrage registriert bleibt. Eine spätere gültige Antwort, ein Abbruch durch den Aufrufer oder die Verbindungsbereinigung kann den Aufruf weiterhin beenden. Hat der Server den Stream geschlossen oder ist das Lesen vom Transport fehlgeschlagen, scheitern neue Operationen mit `McpException`, statt auf eine nicht mehr mögliche Antwort zu warten. Erstellen Sie zum Fortfahren eine neue Verbindung.

Lass tatsächliche Fehler eine Ausnahme auslösen. Die Ausführung erfasst sie mit `FunctionCallResult.IsError = true`, statt sie als erfolgreiche Zeichenfolge `"Error: ..."` zu behandeln. Bewusst zurückgegebener Text bleibt ein normales Ergebnis. Abgebrochene Tool-Ergebnisse tragen `IsCancelled = true` und `IsError = true`.

Für die programmatische Registrierung verwende die `WithHandler`-Überladung mit zwei Argumenten:

```csharp
using System.IO;
using Mythosia.AI.Builders;

var readText = FunctionBuilder.Create("read_text")
    .WithDescription("Eine Textdatei lesen")
    .AddParameter("path", "string", "Dateipfad", required: true)
    .WithHandler(async (args, token) =>
        await File.ReadAllTextAsync(args["path"].ToString()!, token))
    .Build();
```

Bisherige String-Handler mit einem Argument bleiben unterstützt. Direkte Definitionen können `HandlerWithCancellation` mit `Func<Dictionary<string, object>, CancellationToken, Task<string>>` setzen. `Handler` und `HandlerWithCancellation` ersetzen denselben Handler und registrieren keine zwei Ausführungen. Diese API liefert weiterhin Zeichenfolgen; die automatische Objektserialisierung erfolgt bei der Methodenregistrierung.

Dies betrifft Rückgaben und Abbruch lokaler .NET-Funktionen und benötigt keine native `AllowAsync`-Funktion des Anbieters. Nur den Leser `run.StreamAsync(token)` zu stoppen beendet die Beobachtung, nicht den Run. Siehe [Run-Anleitung](execution-api-transition.md) und [Anbieterprotokoll](https://developers.openai.com/api/docs/guides/async-tool-calling).

## Asynchrone Tool-Aufrufe

Eine langsame Abfrage muss die Antwort nicht vollständig anhalten. Während etwa Wetterdaten geladen werden, kann das Modell bereits allgemeine Reisetipps formulieren, die nicht vom Ergebnis abhängen. Asynchrone Werkzeugaufrufe erlauben diese unabhängige Arbeit; Aussagen, die das Ergebnis benötigen, müssen weiterhin darauf warten.

GPT-6 Astra und Async-Tool-Aufrufe werden ab `Mythosia.AI` 7.1.0 unterstützt; die gemeinsamen Typen sind ab `Mythosia.AI.Abstractions` 3.1.0 verfügbar.

`FunctionDefinition.AllowAsync` ist standardmäßig `false`. Setze es auf `true` oder rufe `FunctionBuilder.WithAsync()` auf, wenn das Modell während dieser Funktion weiterarbeiten darf. `WithAsync(false)` deaktiviert die Erlaubnis. Funktionsdefinition und Handler lassen sich unverändert bei mehreren Anbietern verwenden.

Bei der Registrierung per Attribut setzt `[AiFunction("lookup", "Daten abfragen", AllowAsync = true)]` dieselbe Erlaubnis.

```csharp
using System.Threading.Tasks;
using Mythosia.AI.Builders;
using Mythosia.AI.Extensions;
using Mythosia.AI.Models;

service.ChangeModel(AIModels.OpenAI.Gpt6Astra);
var weatherTool = FunctionBuilder.Create("get_demo_weather")
    .WithDescription("Liefert Beispielwetter für Seoul")
    .WithAsync()
    .WithHandler(async _ =>
    {
        await Task.Delay(5000);
        return "{\"city\":\"Seoul\",\"temperature_c\":24,\"source\":\"demo\"}";
    })
    .Build();
service.WithFunction(weatherTool);
var answer = await service.GetCompletionAsync(
    "Prüfe das Beispielwetter für Seoul. Nenne währenddessen drei wichtige Dinge fürs Reisegepäck.");
```

Mythosia sendet `async: true` für GPT-6 Astra / Sol / Luna über die Responses API. Bei nicht unterstützten Modellen und APIs wird das Feld weggelassen und das Ergebnis desselben Handlers abgewartet; `AllowAsync` bleibt unverändert. Der Anbieter muss auch den tatsächlichen Aufruf als asynchron kennzeichnen (`FunctionCall.IsAsync`). Die Erlaubnis garantiert daher keine asynchrone Ausführung.

`WithFunctionAsync` registriert einen asynchronen .NET-Handler; `FunctionExecutionMode.Parallel` steuert die lokale Handler-Ausführung. Beide aktivieren diese Erlaubnis nicht automatisch. `AllowAsync` erlaubt dem Modell, vor Eingang des Funktionsergebnisses weiterzuarbeiten. `FunctionExecutionMode` steuert weiterhin gewöhnliche Aufrufe. Erlaubte asynchrone Aufgaben können auch im Modus `Sequential` überlappen; für ihren separaten Aufgabenpool gilt gemeinsam die Grenze `MaxConcurrency`.

Ausstehende Aufgaben gehören zur laufenden Anfrage: `GetCompletionAsync`, der bisherige `service.StreamAsync` oder ein mit `StartRunAsync` gestarteter `AIRun`. Jedes Ergebnis wird später seiner ursprünglichen Aufruf-ID zugeordnet. Erfolgreicher Abschluss beziehungsweise `run.Result` wartet auf die Verarbeitung ausstehender Ergebnisse. Eine vom Auftrag losgelöste öffentliche Sitzung für Hintergrundaufgaben gibt es nicht.

Bei asynchronen Tools gibt `GetCompletionAsync` nach Abschluss des Requests die unabhängigen Zwischentexte und den abschließenden Text in ihrer Reihenfolge gesammelt zurück. `StreamAsync` liefert die Texte der einzelnen Runden, sobald sie eintreffen. `run.StreamAsync()` liefert ebenfalls eintreffende Texte; `(await run.Result).Text` verbindet die gesamten Textereignisse des Runs.

Lokale Tools können Objekte über `Task<T>` / `ValueTask<T>` zurückgeben und ein injiziertes `CancellationToken` erhalten. `run.Cancel()` oder das Starttoken erreicht kooperative Tools; das Beenden des Stream-Lesers allein nicht. Ausnahmen gelten als Fehler. Bei Abbruch werden wartende Aufrufe übersprungen, und die Bereinigung wartet weiterhin auf gestartete Tools, die das Token ignorieren. Siehe [Ergebnisse, Fehler und Abbruch](function-calling.md#tool-execution-contract).

Beim Streaming starten Handler erst, nachdem vollständige Funktionsaufrufe und ein gültiger Abschluss der Anbieterantwort bestätigt wurden. Die nächste Modellrunde kann anschließend während laufender asynchroner Aufgaben beginnen; unvollständige Funktionsaufrufe lösen keine Ausführung aus. Gibt das Modell keine neuen Aufrufe zurück, obwohl Aufgaben ausstehen, wartet Mythosia auf Ergebnisse. Automatische Zusammenfassungs- und Wiederholungsversuche bei Kontextüberlauf bleiben während ausstehender Aufrufe deaktiviert, damit unvollständige Aufrufe im Verlauf erhalten bleiben.

Perplexity: [Recherche und Werkzeuge steuern](perplexity.md).
