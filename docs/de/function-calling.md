# Funktionsaufruf

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

Mythosia sendet `async: true` für GPT-6 Astra über die Responses API. Bei nicht unterstützten Modellen und APIs wird das Feld weggelassen und das Ergebnis desselben Handlers abgewartet; `AllowAsync` bleibt unverändert. Der Anbieter muss auch den tatsächlichen Aufruf als asynchron kennzeichnen (`FunctionCall.IsAsync`). Die Erlaubnis garantiert daher keine asynchrone Ausführung.

`WithFunctionAsync` registriert einen asynchronen .NET-Handler; `FunctionExecutionMode.Parallel` steuert die lokale Handler-Ausführung. Beide aktivieren diese Erlaubnis nicht automatisch. `AllowAsync` erlaubt dem Modell, vor Eingang des Funktionsergebnisses weiterzuarbeiten. `FunctionExecutionMode` steuert weiterhin gewöhnliche Aufrufe. Erlaubte asynchrone Aufgaben können auch im Modus `Sequential` überlappen; für ihren separaten Aufgabenpool gilt gemeinsam die Grenze `MaxConcurrency`.

Ausstehende Aufgaben gehören zur laufenden Anfrage: `GetCompletionAsync`, der bisherige `service.StreamAsync` oder ein mit `StartRunAsync` gestarteter `AIRun`. Jedes Ergebnis wird später seiner ursprünglichen Aufruf-ID zugeordnet. Erfolgreicher Abschluss beziehungsweise `run.Result` wartet auf die Verarbeitung ausstehender Ergebnisse. Eine vom Auftrag losgelöste öffentliche Sitzung für Hintergrundaufgaben gibt es nicht.

Bei asynchronen Tools gibt `GetCompletionAsync` nach Abschluss des Requests die unabhängigen Zwischentexte und den abschließenden Text in ihrer Reihenfolge gesammelt zurück. `StreamAsync` liefert die Texte der einzelnen Runden, sobald sie eintreffen. `run.StreamAsync()` liefert ebenfalls eintreffende Texte; `run.Result` verbindet die gesamten Textereignisse des Runs.

Handler erhalten keine Abbruchtokens. Bei Abbruch, Zeitüberschreitung oder Fehlern wartet die Bereinigung deshalb auf bereits gestartete Handler. Das frühe Beenden des bisherigen Service-Streams beendet dessen Ausführung; das Beenden von `run.StreamAsync()` beendet dagegen nur die Beobachtung. Zum Abbrechen des Runs verwende `run.Cancel()` oder gib ihn frei. Die Integration gilt für registrierte Funktionshandler; siehe [API-Protokoll](https://developers.openai.com/api/docs/guides/async-tool-calling) und [Run-Anleitung](execution-api-transition.md).

Beim Streaming starten Handler erst, nachdem vollständige Funktionsaufrufe und ein gültiger Abschluss der Anbieterantwort bestätigt wurden. Die nächste Modellrunde kann anschließend während laufender asynchroner Aufgaben beginnen; unvollständige Funktionsaufrufe lösen keine Ausführung aus. Gibt das Modell keine neuen Aufrufe zurück, obwohl Aufgaben ausstehen, wartet Mythosia auf Ergebnisse. Automatische Zusammenfassungs- und Wiederholungsversuche bei Kontextüberlauf bleiben während ausstehender Aufrufe deaktiviert, damit unvollständige Aufrufe im Verlauf erhalten bleiben.
