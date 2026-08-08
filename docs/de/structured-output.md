# Strukturierte Ausgabe

## Warum strukturierte Ausgabe?

LLMs geben standardmäßig Freitext zurück. Wenn deine Anwendung die Antwort **programmatisch verarbeiten** muss — sie in einer Datenbank speichern, an eine andere API weitergeben oder in einer typisierten UI anzeigen — musst du diesen Text selbst parsen. Das führt zu fragilen Regex- oder `string.Contains`-Prüfungen, die brechen, sobald das Modell die Formulierung ändert.

Strukturierte Ausgabe löst das, indem das Modell angewiesen wird, JSON entsprechend dem Schema eines C#-Typs zurückzugeben. Mythosia.AI übernimmt die Schema-Generierung, Prompt-Injektion und Deserialisierung automatisch — einschließlich **automatischer JSON-Reparatur** bei kleineren Formatierungsfehlern des Modells.

### Wann verwenden

- Entitäten, Klassifikationen oder strukturierte Daten aus unstrukturiertem Text extrahieren
- Typisierte API-Antworten aus KI-generiertem Inhalt aufbauen
- KI-Ausgaben in nachgelagerte Pipelines einspeisen, die bestimmte Datenformen erwarten
- Jedes Szenario, bei dem du **zuverlässige, maschinenlesbare** Ausgabe vom Modell benötigst

## Das Problem, das es löst

Angenommen, du musst Wetterdaten aus der Modellantwort extrahieren. **Ohne** strukturierte Ausgabe:

```csharp
// ❌ Ohne strukturierte Ausgabe — fragiles manuelles Parsen
var text = await service.GetCompletionAsync("Wie ist das Wetter in Berlin?");
// text = "Das Wetter in Berlin ist sonnig bei einer Temperatur von 22°C."

// Jetzt musst du das selbst parsen...
var city = "Berlin"; // hartcodiert? Regex?
var tempMatch = Regex.Match(text, @"(\d+)°C");
int temp = tempMatch.Success ? int.Parse(tempMatch.Groups[1].Value) : 0;
// Was, wenn das Modell "zweiundzwanzig Grad" statt "22°C" sagt? 💥
```

Das bricht bei jeder Formulierungsänderung. **Mit** strukturierter Ausgabe:

```csharp
// ✅ Mit strukturierter Ausgabe — typsicher, automatisch
public record WeatherResponse(string City, string Condition, int TemperatureC);

var result = await service.GetCompletionAsync<WeatherResponse>(
    "Wie ist das Wetter in Berlin?");

Console.WriteLine(result.City);         // Berlin
Console.WriteLine(result.Condition);    // Sonnig
Console.WriteLine(result.TemperatureC); // 22
```

Das Modell wird angewiesen, JSON entsprechend deinem C#-Typ zurückzugeben. Mythosia.AI deserialisiert es automatisch. Wenn das Modell leicht fehlerhaftes JSON produziert (fehlende Komma, nachgestellter Text), behebt die eingebaute **Auto-Reparatur** das vor der Deserialisierung — kein manuelles Fehlerhandling nötig.

## Einfaches Beispiel

Übergib einen Typparameter an `GetCompletionAsync`:

```csharp
public record WeatherResponse(string City, string Condition, int TemperatureC);

var result = await service.GetCompletionAsync<WeatherResponse>(
    "Wie ist das Wetter in Berlin?");

Console.WriteLine(result.City);        // Berlin
Console.WriteLine(result.Condition);   // Sonnig
Console.WriteLine(result.TemperatureC); // 22
```

## Collections

Collection-Typen funktionieren direkt — kein Wrapper-DTO nötig:

```csharp
public record Entity(string Name, string Type);

var entities = await service.GetCompletionAsync<List<Entity>>(
    "Extrahiere alle Personen und Organisationen aus diesem Text: ...");

foreach (var e in entities)
    Console.WriteLine($"{e.Type}: {e.Name}");
```

## Streaming + Strukturierte Ausgabe

Text in Echtzeit streamen und gleichzeitig das finale deserialisierte Objekt erhalten:

```csharp
var run = service.BeginStream("Erstelle eine Produktzusammenfassung").As<ProductDto>();

// Echtzeit-Ausgabe
await foreach (var chunk in run.Stream())
    Console.Write(chunk);

// Finales geparsertes Ergebnis
ProductDto product = await run.Result;
```

## Structured Output Policy

Steuere, wie strikt das Modell zur strukturierten Ausgabe aufgefordert wird:

```csharp
using Mythosia.AI.Extensions;
using Mythosia.AI.Models;

// Strict: bis zu drei automatische Reparaturversuche zulassen
service.WithStructuredOutputPolicy(StructuredOutputPolicy.Strict);

// NoRetry: den ersten Validierungsfehler ohne Reparaturversuch zurückgeben
service.WithStructuredOutputPolicy(StructuredOutputPolicy.NoRetry);
```
