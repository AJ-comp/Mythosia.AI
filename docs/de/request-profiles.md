# AIRequestProfile

## Was ist das?

`AIRequestProfile` ermöglicht es, Generierungsparameter — Temperatur, maximale Tokens, statusloser Modus, Funktionsaufruf — **nur für eine einzelne Anfrage** zu überschreiben. Die globalen Einstellungen des Services bleiben unberührt.

## Das Problem, das es löst

Stell dir vor, du hast einen Chatbot für kreative Gespräche konfiguriert:

```csharp
var service = new OpenAIService(apiKey, http)
    .WithTemperature(0.8f)
    .WithMaxTokens(2048)
    .WithSystemMessage("Du bist ein kreativer Schreibassistent.");
```

Jetzt muss deine RAG-Pipeline die Benutzeranfrage mit niedriger Temperatur und ohne Verlauf umschreiben. **Ohne** `AIRequestProfile` müsstest du das so machen:

```csharp
// ❌ Ohne AIRequestProfile — manuelle Zustandsverwaltung
var savedTemp = service.Temperature;
var savedMax = service.MaxTokens;
var savedStateless = service.StatelessMode;

service.Temperature = 0.1f;
service.MaxTokens = 256;
service.StatelessMode = true;

var rewritten = await service.GetCompletionAsync("Schreibe diese Anfrage um: ...");

// Alles wiederherstellen — leicht vergessbar, nicht thread-sicher
service.Temperature = savedTemp;
service.MaxTokens = savedMax;
service.StatelessMode = savedStateless;
```

Das ist ausführlich, fehleranfällig und **bricht in Multi-Thread-Szenarien** (z. B. ein Webserver mit gleichzeitigen Nutzern). Wenn eine Exception vor der Wiederherstellung ausgelöst wird, bleibt der Service in einem fehlerhaften Zustand.

**Mit** `AIRequestProfile` ist es eine Zeile:

```csharp
// ✅ Mit AIRequestProfile — sauber und sicher
var rewritten = await service.GetCompletionAsync("Schreibe diese Anfrage um: ...",
    new AIRequestProfile { Temperature = 0.1f, MaxTokens = 256, Stateless = true });
```

Die globalen Einstellungen des Services werden nie angefasst. Kein Aufräumen nötig. Thread-sicher.

## Verfügbare Eigenschaften

```csharp
var profile = new AIRequestProfile
{
    Temperature = 0.1f,       // Temperatur überschreiben
    MaxTokens = 256,          // Maximale Ausgabe-Tokens überschreiben
    Stateless = true,         // Austausch nicht zum Gesprächsverlauf hinzufügen
    DisableFunctions = true,  // Funktionsaufruf für diese Anfrage überspringen
    DisableReasoning = true   // Reasoning/Chain-of-Thought für diese Anfrage überspringen
};

var response = await service.GetCompletionAsync("Dein Prompt", profile);
```

Alle Eigenschaften sind optional — setze nur, was du überschreiben möchtest. Alles andere nutzt den aktuellen Wert des Services.

## Vordefinierte Profile

Für gängige Szenarien gibt es eingebaute Profile, damit du keine Eigenschaften manuell konfigurieren musst:

```csharp
// Query-Umschreibung: niedrige Temperatur, kleines Token-Budget, statuslos
var rewritten = await service.GetCompletionAsync(query, RequestProfiles.QueryRewrite);

// Zusammenfassung: etwas höhere Temperatur, moderate Tokens
var summary = await service.GetCompletionAsync(text, RequestProfiles.Summarization);
```

## Praxisbeispiele

### Interne Query-Umschreibung in einer RAG-Pipeline

```csharp
// Haupt-Service für nutzerseitige Gespräche konfiguriert
var service = new OpenAIService(apiKey, http)
    .WithTemperature(0.7f)
    .WithMaxTokens(4096);

// Anfrage mit anderen Einstellungen umschreiben — Service bleibt unverändert
var betterQuery = await service.GetCompletionAsync(
    $"Optimiere für die Suche: {userQuery}",
    RequestProfiles.QueryRewrite);

// Normales Gespräch fortsetzen — noch Temperature 0.7, MaxTokens 4096
var answer = await service.GetCompletionAsync(userQuery);
```

### Funktionen für einen bestimmten Schritt deaktivieren

```csharp
// Service hat registrierte Funktionen
service.WithFunction("search_web", "Im Web suchen", ...);

// Für diesen einen Aufruf Funktionsaufruf überspringen — direkt antworten
var directAnswer = await service.GetCompletionAsync(
    "Was ist 2 + 2?",
    new AIRequestProfile { DisableFunctions = true });
```

## Kombinieren mit AIRequestContext

Beide können zusammen übergeben werden für maximale Kontrolle:

```csharp
var response = await service.GetCompletionAsync(
    prompt,
    profile: RequestProfiles.QueryRewrite,
    context: new AIRequestContext { SystemMessageSuffix = "\nSei prägnant." }
);
```

Weitere Details zur Injektion von Inhalten in Anfragen unter [AIRequestContext](request-contexts.md).
