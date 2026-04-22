# Grundkonzepte

Diese Seite sammelt grundlegende Konzepte, auf die in der restlichen Dokumentation immer wieder verwiesen wird. Weitere Konzepte werden mit der Zeit hier ergänzt.

## Was ist ein Round?

> [!NOTE]
> Ein **Round** ist ein vollständiger Anfrage-Antwort-Zyklus zwischen deiner App und dem Modell — deine App schickt einen Prompt, das Modell antwortet, und dieser Austausch ist ein Round. Eine einfache Chat-Nachricht ist 1 Round. Function Calling und Agenten können für eine einzelne Nutzernachricht mehrere Rounds verketten.

### Einfachster Fall: 1 Round

Bei einer normalen Chat-Nachricht läuft die gesamte Konversation in einem Round ab.

```
App  →  "Was ist 2 + 2?"       →  Modell
App  ←  "Es ist 4."             ←  Modell
```

`RoundUsage` wird einmal mit den Tokens dieses Rounds ausgelöst. `Completion.Usage` wird am Streamende mit derselben Summe ausgelöst, weil es nur einen Round gibt.

### Mehrere Rounds: Function Calling

Rounds summieren sich, wenn das Modell nicht alleine antworten kann. Ein Nutzer fragt zum Beispiel *„Wie ist das Wetter gerade in Berlin?"* — das Modell kennt das aktuelle Wetter nicht und muss deshalb ein Tool aufrufen.

**Round 1 — das Modell entscheidet, ein Tool aufzurufen**

Deine App schickt die Nutzernachricht zusammen mit der Liste der registrierten Tools (z. B. `GetWeather`) an das Modell. Das Modell sieht diese Konversation:

```
system: Du bist ein Wetter-Assistent. Du kannst GetWeather(city) aufrufen.
user:   Wie ist das Wetter gerade in Berlin?
```

Statt einer endgültigen Antwort gibt das Modell eine **Tool-Aufruf-Anfrage** zurück:

```
tool_call: GetWeather(city="Berlin")
```

Der Zug des Modells endet und Round 1 ebenfalls. `RoundUsage` wird mit den Tokens aus Round 1 ausgelöst. **Es gibt noch keine finale Antwort für den Nutzer.**

**Zwischen den Rounds — deine App führt die Funktion aus**

Dieser Schritt ist **kein** LLM-Aufruf. Die Mythosia.AI-Runtime ruft deine registrierte `GetWeather`-Implementierung auf und bekommt `„15°C, bewölkt"` zurück. Es werden keine Tokens verbraucht.

**Round 2 — das Modell schreibt die finale Antwort**

Deine App hängt **den function_call des Modells aus Round 1 zusammen mit dem Tool-Ergebnis** an die Konversation an und ruft das Modell **ein zweites Mal** auf. Das Modell sieht jetzt:

```
system:      Du bist ein Wetter-Assistent. Du kannst GetWeather(city) aufrufen.
user:        Wie ist das Wetter gerade in Berlin?
assistant:   [GetWeather(city="Berlin") aufgerufen]
tool_result: 15°C, bewölkt
```

Mit den nötigen Informationen schreibt das Modell jetzt Text:

```
In Berlin sind es gerade 15°C und bewölkt.
```

Round 2 endet. `RoundUsage` wird ein zweites Mal ausgelöst — diesmal nur mit den Tokens aus Round 2 (die Eingabe ist typischerweise größer als in Round 1, weil die Konversation länger geworden ist). Wenn der Stream endet, wird `Completion.Usage` einmal mit der **Summe aus Round 1 und Round 2** ausgelöst.

### Auf einen Blick

| Schritt | LLM-Aufruf? | Was passiert | Event |
|---|---|---|---|
| Round 1 | ✅ | Modell entscheidet, `GetWeather` aufzurufen | `RoundUsage` (`RoundIndex=1`) |
| Zwischen den Rounds | ❌ | App führt die Funktion aus, bekommt `„15°C, bewölkt"` | `FunctionCall`, `FunctionResult` |
| Round 2 | ✅ | Modell sieht das Ergebnis und schreibt die finale Antwort | `RoundUsage` (`RoundIndex=2`, `IsFinalRound=true`) |
| Stream endet | — | — | `Completion` (Usage = Round 1 + Round 2) |

### Mehr Tools bedeuten mehr Rounds

Wenn das Modell mehrere Tool-Aufrufe verketten muss, summieren sich die Rounds. Für *„Vergleiche das Wetter in Berlin und München"*:

1. **Round 1** — Modell ruft `GetWeather("Berlin")` auf
2. App führt es aus → `„15°C, bewölkt"`
3. **Round 2** — Modell sieht das Ergebnis und ruft zusätzlich `GetWeather("Munich")` auf
4. App führt es aus → `„18°C, sonnig"`
5. **Round 3** — Modell kombiniert beide Ergebnisse zur finalen Antwort

Insgesamt drei Rounds, und `Completion.Usage` ist die Summe aller drei. Eine UI-Kontextanzeige sollte den `RoundUsage.TotalTokens` des letzten Rounds verwenden — in diesem Beispiel den Wert von Round 3.
