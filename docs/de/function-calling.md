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

[AiFunction("search_products", "Durchsucht den Produktkatalog")]
public static string SearchProducts(
    [AiParameter("Suchanfrage", required: true)] string query,
    [AiParameter("Maximale Anzahl Ergebnisse")] int limit = 5)
{
    // ... deine Implementierung
    return JsonSerializer.Serialize(results);
}
```

Dann registrieren:

```csharp
service.AddFunction(SearchProducts);
```

## Funktionsaufruf-Policy

Steuere, wann das Modell Funktionen aufrufen darf:

```csharp
using Mythosia.AI.Models.Functions;

// Modell entscheidet selbst (Standard)
service.FunctionCallingPolicy = FunctionCallingPolicy.Auto;

// Modell muss immer eine Funktion aufrufen
service.FunctionCallingPolicy = FunctionCallingPolicy.Required;

// Funktionsaufruf deaktivieren
service.FunctionCallingPolicy = FunctionCallingPolicy.None;
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

var fn = FunctionBuilder
    .Create("get_stock_price", "Gibt den aktuellen Aktienkurs zurück")
    .AddParameter("ticker", "Aktien-Ticker-Symbol", required: true)
    .Build();

service.AddFunction(fn, ticker => FetchStockPrice(ticker));
```
