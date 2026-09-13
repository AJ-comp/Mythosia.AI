# Lange Aufgaben mit Claude Fable 5.1 beobachten

> Die Fable-5.1-Steuerung benötigt `Mythosia.AI` 8.0.0 und `Mythosia.AI.Abstractions` 4.0.0 oder neuer. Für bestehende Run-, Reasoning-/Such- und GPT-6-APIs gelten weiterhin die Mindestversionen 7.1.0 / 3.1.0.

## Wann sind diese Einstellungen hilfreich?

Eine Dokumentenrecherche kann mehrere Suchvorgänge und Tool-Aufrufe benötigen, bevor eine Antwort entsteht. Deine Anwendung soll vielleicht den Fortschritt zeigen, eine Prüfung nur für die aktuelle Gesprächsrunde verlangen oder nach Änderungen am bisherigen Gespräch weiterarbeiten. Fable 5.1 bietet dafür Einstellungen. Bei der Wiederverwendung gespeicherter Thinking-Blöcke gehört aber auch der Gesprächsverlauf zum Anfragevertrag.

Verwende die [Run-API](execution-api-transition.md) zum Beobachten und Abbrechen, die [gemeinsamen Reasoning- und Suchoptionen](reasoning-and-search.md) für Aufwand und Quellen sowie die folgenden Claude-Einstellungen für Fortschritt und Verlauf. Native Modellfähigkeiten bedeuten nicht, dass Mythosia sämtliche Anbieter-APIs bereitstellt.

## Modell und Aufwand ausdrücklich wählen

```csharp
using Mythosia.AI.Models;
using Mythosia.AI.Services.Anthropic;

var service = new AnthropicService(apiKey, httpClient);
service.ChangeModel(AIModels.Anthropic.ClaudeFable5_1);
service.WithAdaptiveThinkingParameters(ClaudeReasoningEffort.High);

string answer = await service.GetCompletionAsync("Review this migration plan.");
```

`ClaudeFable5_1` wählt `claude-fable-5-1`. `ClaudeMythos5_1` wählt `claude-mythos-5-1` und benötigt Project-Glasswing-Zugang. Die bisherigen Konstanten für Fable 5 und Mythos 5 bleiben erhalten. Beide 5.1-Modelle verarbeiten Text und Bilder und erzeugen Text; das Kontextfenster umfasst 1M Tokens, die maximale Ausgabe 128K Tokens. [Modellübersicht](https://platform.claude.com/docs/en/models/fable-5-1/overview).

Der native Modellstandard ist `high`. Mythosia behält für `ClaudeReasoningEffort.Auto` jedoch die bisherige Zuordnung von `ThinkingBudget` bei: aktivierte Budgets ergeben `High`, ab 32.768 `XHigh` und ab 100.000 `Max`. Eine Anfrage zum Abschalten des Reasonings verwendet niedrigen adaptiven Aufwand ohne lesbare Thinking-Ausgabe. Wähle `High` ausdrücklich, wenn du dieses Verhalten brauchst. `Auto` bedeutet nicht, dass die Bibliothek effort immer weglässt und dem Modellstandard überlässt.

## Fortschritt zwischen Tool-Aufrufen anzeigen

`ClaudeThinkingDisplay.Updates` fordert lesbare Fortschrittsmeldungen an, während das Reasoning verborgen bleibt. `Summarized` enthält zusätzlich zusammengefasstes Reasoning; `Omitted` unterdrückt lesbare Thinking-Blöcke. Meldungen entstehen nur, wenn das Modell sie erzeugt; ein festes Zeitintervall ist nicht garantiert. [Fortschrittsmeldungen](https://platform.claude.com/docs/en/models/fable-5-1/whats-new-fable-5-1#progress-updates-between-tool-calls-beta).

```csharp
using Mythosia.AI.Models.Streaming;

service.WithAdaptiveThinkingParameters(
    ClaudeReasoningEffort.High, ClaudeThinkingDisplay.Updates);

await using var run = await service.StartRunAsync(
    "Use the registered tools to investigate the report.",
    options: StreamOptions.FullOptions);

await foreach (var item in run.StreamAsync())
{
    if (item.Type == StreamingContentType.Reasoning)
        Console.WriteLine($"Progress: {item.Content}");
    else if (item.Type == StreamingContentType.Text)
        Console.Write(item.Content);
}
string result = (await run.Result).Text;
```

Die Meldungen verwenden das bestehende Ereignis `StreamingContentType.Reasoning`. Aktiviere die Beobachtung mit `StreamOptions.FullOptions` oder `StreamOptions.Default.WithReasoning()`. Bei einer Antwort ohne Streaming liest du danach `service.LastThinkingContent`. Fortschrittstext ist von der endgültigen Antwort getrennt und legt keine unverarbeitete Gedankenkette offen.

## Anweisungen für einzelne Runden ohne Änderung früherer Nachrichten

Thinking-Blöcke von Fable 5.1 sind an den Systemprompt, die Tools und die vorhergehenden Nachrichten ihrer Entstehung gebunden. Werden diese Eingaben geändert, während spätere Thinking-Blöcke erhalten bleiben, können die Blöcke ungültig werden. Eine auf eine Runde begrenzte Anweisung eignet sich beispielsweise für die Pflicht, vor der aktuellen Antwort die Supportrichtlinie zu prüfen. Sie wird angehängt und im Verlauf behalten, wirkt aber nach einer späteren Benutzernachricht nicht mehr. So musst du den obersten Systemprompt nicht wiederholt umschreiben. Aufwandänderungen und Rundenanweisungen sind getrennte Einstellungen.

```csharp
await service
    .WithTurnInstruction("Check the registered support-policy tool before answering this turn.")
    .GetCompletionAsync("Can I return an opened product?");

await service
    .WithConversationInstruction("Use Korean for the remaining conversation.")
    .GetCompletionAsync("Explain the next step.");
```

Beide Helfer übernehmen Anweisungen für die nächste logische Anfrage. Mythosia hängt nach Benutzereingaben oder Tool-Ergebnissen eine Systemnachricht an und behält frühere Nachrichten bei. `WithTurnInstruction` verwendet `clear_at: "next_user_message"`. Innerhalb derselben logischen Anfrage wird die Anweisung nach jeder Tool-Ergebnisrunde erneut angehängt, damit sie bis zum Anfrageende gilt. `WithConversationInstruction` bleibt auch für spätere Runden wirksam. Konfiguriere beides vor dem Start; es ist weder `run.SteerAsync` noch eine Anweisung an eine bereits laufende Antwort.

Für Aufwandänderungen zwischen Anfragen unter Erhalt eines wiederverwendbaren Cache-Präfixes nutzt du `.WithReasoning(ReasoningLevel.High, cache: CachePreservation.Required)` aus `Mythosia.AI.Extensions`. Die Bibliothek sendet ein nachrichtenbezogenes effort-Update und behält dessen Verlauf. Unterstützte Kombinationen beschreibt der [gemeinsame Leitfaden](reasoning-and-search.md). Bei 5.1 werden die anfragebezogenen Systempräfixe/-suffixe aus `AIRequestContext` zu angehängten Rundenanweisungen, statt einen früheren Systemprompt umzuschreiben.

Fable 5.1 kann Thinking früherer Claude-Modelle lesen; frühere Modelle können seine Blöcke dagegen nicht lesen. Mythos 5.1 bietet dieselben 5.1-Fähigkeiten, erzwingt aber Fables Präfix-Bindungsprüfung nicht. Beobachte Verlaufsänderungen, Modellwechsel und verworfene Blöcke, statt von unverändert erhaltenem Reasoning auszugehen. [Migrationsleitfaden](https://platform.claude.com/docs/en/models/fable-5-1/migration-guide).

## Bewusste Änderungen am Verlauf diagnostizieren

`ThinkingPrefixMismatchBehavior = null` überlässt die Prüfung der Kontorichtlinie des Anbieters. `WithThinkingBinding(ClaudeThinkingPrefixMismatchBehavior.Error)` fordert ausdrücklich die serverseitige Prüfung an. Benutzeränderungen an Verlauf, `SystemMessage` oder Tools werden an Anthropic gesendet. Stimmt das Präfix bei `Error` nicht überein, antwortet der Anbieter mit 400. Die identische ungültige Anfrage erneut zu senden behebt den Fehler nicht.

Wenn deine Anwendung frühere Inhalte absichtlich ändert und den Verlust des betroffenen Reasonings akzeptiert, wähle `DropBlock`. Mythosia sendet diese Einstellung an Anthropic und entfernt Thinking nicht stillschweigend vor der Anfrage.

```csharp
service.WithThinkingBinding(ClaudeThinkingPrefixMismatchBehavior.DropBlock);
service.SystemMessage = "You are reviewing the revised policy.";
await service.GetCompletionAsync("Reassess the recommendation.");

foreach (ClaudeInputTransformation change in service.LastInputTransformations)
    Console.WriteLine($"{change.Type}: {change.Reason} ({change.Model})");
```

`LastInputTransformations` enthält die vom Anbieter gemeldeten Felder `Type`, `Path` und `Reason` sowie `ResponseId` und `Model` zur Zuordnung. `prefix_binding_mismatch` bezeichnet ein geändertes Präfix, `model_binding_mismatch` Thinking, das das Zielmodell nicht lesen kann. Verwerfen bedeutet Verlust, keine Reparatur des Reasonings. Halte den Verlauf unverändert, wenn du ihn bewahren musst, oder beginne zum Zurücksetzen ein neues Gespräch.

Mythosia bewahrt den übertragenen Verlauf, damit interne RAG-/Context-Verarbeitung ihn nicht unbeabsichtigt verändert. In normalen Fable-5.1-Gesprächen ist automatische lokale Komprimierung beim Standardverhalten und bei `Error` gesperrt. `DropBlock` erlaubt sie, kann aber Reasoning verwerfen und garantiert keine Cache-Treffer. Die separate Option `CachePreservation.Required` behält ihre strengeren Verlaufsschutzregeln. Gemeinsame Optionen wie `WithWebSearch()` werden nach jeder Anfrage verbraucht. Lässt du sie in der nächsten Runde weg, ändert sich das native tools-Array und das Präfix kann ungültig werden. Wende dieselben Tool-/Sucheinstellungen erneut an, um den Verlauf zu bewahren; nutze `DropBlock` oder ein neues Gespräch für bewusste Änderungen. Die Optionen werden nicht automatisch übernommen.

Der gespeicherte Übertragungs-Snapshot gehört zum Service und seinem `ChatBlock`. Das Kopieren nur des `ChatBlock` in einen neuen Service überträgt frühere RAG-/Context- oder Runden-System-Snapshots nicht. Verwende zum Bewahren des Reasonings denselben Service und Chat; hast du nur den unverarbeiteten Verlauf übertragen, beginne ein neues Gespräch, statt von erhaltener Zustandsinformation auszugehen.

## Reguläre Tool-Auswahl verwenden

Fable 5.1 und Mythos 5.1 lehnen erzwungene Tool-Auswahl ab. Lass `ForceFunctionName` ungesetzt und beschreibe in der Anfrage, wann das registrierte Tool verwendet werden soll. `FunctionsDisabled` bleibt für Runden verfügbar, die keine Tools aufrufen dürfen. Nutze für typisierte Antworten die vorhandene API für strukturierte Ausgabe, statt nur zur JSON-Erzeugung eine Funktion zu erzwingen.

## Änderungen auf der Serverseite erkennen

| Native Option | Erforderliche Anthropic-Beta |
| --- | --- |
| Nachrichtenbezogener effort | `mid-conversation-output-config-2026-07-01` |
| Auf eine Runde begrenzte Systemnachricht | `mid-conversation-system-clear-at-2026-08-21` |
| `thinking.display: "updates"` | `thinking-display-updates-2026-08-18` |
| Thinking-Bindungssteuerung und `input_transformations` | `thinking-binding-controls-2026-08-01` |

Mythosia ergänzt den jeweiligen Header, wenn die zugehörige unterstützte Einstellung aktiv ist. Eine Beta zu aktivieren schaltet nicht sämtliche anderen ein. Diese Integration führt weder serverseitige Compaction noch native Tool-Hinzufügungs-/Entfernungsblöcke oder automatische Modell-Fallbacks ein.

Beide Modelle benötigen die geltende 30-tägige Datenaufbewahrung des Anbieters; ZDR erfordert ausdrückliche Genehmigung durch Anthropic. Adaptives Thinking ist immer aktiv. Manuelle `budget_tokens` und deaktiviertes Thinking sind nicht verfügbar; eigene Samplingparameter werden nicht gesendet. Kontozugang und Aufbewahrung sind serverseitige Anforderungen. [Migrationsanforderungen](https://platform.claude.com/docs/en/models/fable-5-1/migration-guide).

Anthropic wendet Textwasserzeichen, Herkunftsangaben unterstützter Medien und Cache-Lesepreise an. Dafür brauchst du keine neue Mythosia-Anfrageoption. Die Integration ergänzt keine API zur Erzeugung von Medienherkunft, keinen Wasserzeichenschalter und keine Abrechnungssteuerung. Siehe [Neuerungen in Fable 5.1](https://platform.claude.com/docs/en/models/fable-5-1/whats-new-fable-5-1).
