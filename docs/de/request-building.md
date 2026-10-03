# Einstellungen jeder Anfrage unabhängig halten

> Claude Sonnet 5.5: Erfordert Mythosia.AI 8.2.0 / Abstractions 4.2.0. [Konfiguration und Migration](providers.md#claude-sonnet-55)

> GPT-6.1 Sol: Benötigt Mythosia.AI 8.2.0 / Abstractions 4.2.0. [Modellwahl und Migration](providers.md#gpt-61-sol)

> Grok 4.7: Benötigt Mythosia.AI 8.1.0 / Abstractions 4.1.0. [Modellwahl, Reasoning und Verarbeitungsgeschwindigkeit](providers.md#grok-47)

Eine Zusammenfassung benötigt möglicherweise eine niedrige Temperatur, ein kreativer Entwurf eine höhere. Der Entwurf darf die bereits vorbereitete Zusammenfassung nicht verändern. Verwenden Sie `CreateRequest`, wenn Aufrufe unterschiedliche Einstellungen benötigen oder Sie Varianten einer gemeinsamen Anfrage erstellen möchten.

Für die fertige Antwort mit Verbrauch und Quellen liefert `await run.Result` eine `AIRunResult`-Momentaufnahme. Die Zeichenfolge steht in `result.Text`; ein Stream-Leser ist unnötig. Diese API-Änderung gehört zu Mythosia.AI 8.0.0. Die Rückgabetypen von `GetCompletionAsync` und `StructuredStreamRun<T>.Result` bleiben erhalten. [Run-Ergebnis und Migration](execution-api-transition.md#run-result).

Für eine fertige Antwort mit Stoppschaltfläche übergeben Sie `cancellationToken` an `GetCompletionAsync`. Run dient Fortschrittsereignissen oder unterstützten zusätzlichen Anweisungen. Siehe [Completion-Abbruch](completions.md#completion-cancellation).

> Die `CreateRequest`-Beispiele benötigen Mythosia.AI 8.0.0 / Abstractions 4.0.0. Die frühere Version 7.1 mit Run und gemeinsamen Anfrageoptionen enthält den Builder noch nicht. Ältere Pakete können ihre bisherigen Service-Überladungen verwenden.

## Before: gemeinsam genutzter Service

Das bisherige `WithTemperature` des Services ändert den Service und gibt dieselbe Instanz zurück. Beide Variablen zeigen auf diese Instanz; der zuletzt gesetzte Wert gilt für beide. Diese Methoden stehen weiterhin zur Konfiguration der Service-Standardwerte bereit.

```csharp
var summary = service.WithTemperature(0.2f);
var creative = service.WithTemperature(0.8f);

bool same = ReferenceEquals(summary, creative); // true
string answer = await summary.GetCompletionAsync("Erkläre dieses Dokument."); // 0.8
```

## After: unabhängige Varianten

`CreateRequest` übernimmt die Standardwerte. Jedes `With...` des Builders gibt einen neuen Builder zurück und lässt das Original unverändert. Die Ausführung verwendet die erfassten Anfragewerte, ohne vorübergehend Service-Einstellungen zu überschreiben.

```csharp
using Mythosia.AI.Builders;

AIRequestBuilder basis = service.CreateRequest("Erkläre dieses Dokument.");
AIRequestBuilder summary = basis.WithTemperature(0.2f);
AIRequestBuilder creative = basis.WithTemperature(0.8f);

bool same = ReferenceEquals(summary, creative); // false
string answer = await summary.GetCompletionAsync();
// Verwendet 0.2; creative und die Service-Standardwerte bleiben unverändert.
```

Verwenden Sie den zurückgegebenen Builder. Wenn Sie das Ergebnis von `basis.WithTemperature(0.2f);` verwerfen, bleibt `basis` unverändert.

Der Builder prüft Werte, statt sie still zu begrenzen: Temperatur 0–2, TopP 0–1, Penalties −2–2; NaN und Unendlich sind ungültig. Token-, Runden-, Parallelitäts- und angegebene Zeitlimits müssen positiv sein. Ungültige Werte lösen `ArgumentException` / `ArgumentOutOfRangeException` aus. Der bisherige Temperatur-Helper des Services begrenzt Werte weiterhin.

## Aufgaben der Objekte

`AIService` verwaltet Anbieteranbindung, Standardwerte und Gesprächszustand. Der öffentliche Typ `Mythosia.AI.Builders.AIRequestBuilder` bietet die Fluent API. Der interne Typ `AIRequest` übergibt die festgelegte Eingabe und Konfiguration an die Ausführung. Ein `Build()`-Aufruf ist nicht erforderlich: `GetCompletionAsync()` liefert `Task<string>`, `StartRunAsync()` liefert `Task<AIRun>`. Die Antwort ist kein `AIRequest`.

```text
AIService.CreateRequest(input)
    → AIRequestBuilder.With...()
    → GetCompletionAsync() / StartRunAsync()
    → AIRequest
    → provider
    → string / AIRun
```

## Mit derselben Konfiguration einen Run starten

`GetCompletionAsync()` liefert die fertige Antwort. `StartRunAsync()` erlaubt Fortschrittsanzeige und zusätzliche Anweisungen bei unterstützten Modellen. Der Prompt gehört zu `CreateRequest` und wird der Ausführungsmethode nicht erneut übergeben. `run.StreamAsync()` beobachtet diesen Run; die Modellvoraussetzungen für `run.SteerAsync(...)` bleiben bestehen.

```csharp
await using var run = await service
    .CreateRequest("Erkläre dieses Dokument.")
    .WithMaxTokens(2048)
    .WithMaxRounds(10)
    .StartRunAsync(
        onText: text => Console.Write(text),
        cancellationToken: cancellationToken);

string answer = (await run.Result).Text;
```

Lokale Tools können Objekte über `Task<T>` / `ValueTask<T>` zurückgeben und ein injiziertes `CancellationToken` erhalten. `run.Cancel()` oder das Starttoken erreicht kooperative Tools; das Beenden des Stream-Lesers allein nicht. Ausnahmen gelten als Fehler. Bei Abbruch werden wartende Aufrufe übersprungen, und die Bereinigung wartet weiterhin auf gestartete Tools, die das Token ignorieren. Siehe [Ergebnisse, Fehler und Abbruch](function-calling.md#tool-execution-contract).

[Run](execution-api-transition.md) · [Streaming](streaming.md)

## Profile und Kontext wiederverwenden

`WithProfile` kopiert ein `AIRequestProfile`, `WithContext` ein `AIRequestContext`. Spätere Änderungen an den Originalobjekten beeinflussen die vorbereitete Anfrage nicht. Der Builder konfiguriert Sampling, Systemanweisungen, zustandslosen Modus, Funktionsrichtlinien und unterstütztes Reasoning sowie Web-/Dateisuche. Anbieterprüfungen gelten weiterhin.

`WithFunctions(params FunctionDefinition[])` fügt kopierte Definitionen hinzu. `WithFunctions(toolInstance)` und `WithStaticFunctions<T>()` aus `Mythosia.AI.Extensions` unterstützen bestehende Funktionen mit Attributen. Registrierung vor `CreateRequest` gilt als Service-Standard, danach nur für die Anfrage. `CreateRequest` übernimmt und verbraucht ausstehende Optionen für den nächsten Aufruf; zur Wiederverwendung behalten Sie den Builder.

```csharp
var request = service
    .CreateRequest("Formuliere diese Frage für die Suche um.")
    .WithProfile(RequestProfiles.QueryRewrite)
    .WithContext(new AIRequestContext
    {
        SystemMessageSuffix = "\nBehalte die ursprüngliche Bedeutung bei."
    });

string rewritten = await request.GetCompletionAsync();
```

Claude bestimmt Modell, Anfragezweck, Reasoning und Thinking-Binding gemeinsam. Bei Hilfsprofilen wie `RequestProfiles.Summarization` oder `RequestProfiles.QueryRewrite` wird die übernommene Binding-Richtlinie nur weggelassen, wenn `DisableReasoning = true` gilt, der Zweck nicht `Default` ist und die tatsächliche Anfrage zustandslos ist. So aktiviert die übernommene Richtlinie weder erneut Reasoning noch führt sie zur Ablehnung einer Anfrage ohne zu erhaltendes Gesprächspräfix. Modelle mit abschaltbarem Thinking deaktivieren es; Opus 5.5, Fable 5.1 und Mythos 5.1 mit obligatorischem Reasoning verwenden `Low` ohne lesbares Thinking. Sonnet 5.5 verwendet `between_tools` mit hohem Aufwand.

Completion, Streaming, strukturierte Ausgabe und Run bereiten Anfragen nach demselben Ablauf vor: Einstellungen erfassen, die tatsächliche Profilverarbeitung einmal anwenden und danach die resultierenden allgemeinen und anbieterspezifischen Optionen prüfen. Dies geschieht vor automatischer Zusammenfassung, dem Anhängen neuer Eingaben und dem Verbindungsaufbau. Benutzerdefinierte Profilüberschreibungen werden damit tatsächlich berücksichtigt. Claude weist auch ein verwendetes manuelles `ThinkingBudget` zurück, das die Ausgabegrenze des Modells erreicht oder überschreitet; gültige Profil- und allgemeine Reasoning-Einstellungen behalten ihren Vorrang.

Anwendungsaufrufe beginnen unabhängige logische Anfragen, auch gewöhnliche Aufrufe aus `SystemMessageProvider` oder Tool-Callbacks sowie Aufrufe, die dasselbe `AIRequestProfile` oder `Message` wiederverwenden. Ein wiederverwendetes Objekt bedeutet keine gemeinsame Ausführung. Framework-Delegation, Tool-Runden, Wiederholungen und Formatkorrekturen setzen die ursprüngliche Anfrage fort; ihr Profil wird einmal angewendet. Neue gewöhnliche Unteranfragen erfassen eigene Optionen und Service-Standardwerte, Builder behalten ihre erfassten Einstellungen. Nach Erfolg, Fehler oder Abbruch wird die übergeordnete Ausführung wiederhergestellt. Anbieterüberschreibungen, die einen Framework-Aufruf weiterleiten, folgen den [Adapterregeln unten](#provider-request-adapters).

Integrierte Anbieter behalten eine eigene Kopie der integrierten Eingabeinhalte. Ein erneut verwendetes `Message` erhält den Kontext und die Anweisungen des neuen Aufrufs, ohne bereits akzeptierten Verlauf umzuschreiben. Benutzerdefinierte Inhalte und nicht unterstützte Metadatenobjekte bleiben in der Verantwortung ihres Eigentümers. Parallele Aufrufe derselben Unterhaltung werden dadurch nicht sicher.

Nach der Rückkehr von `StartRunAsync` behält der Run eine eigene Kopie der effektiven Einstellungen. Das Wiederherstellen des Aufruferprofils verändert den aktiven Run nicht; die Ausführungs-Hooks des Profils werden weiterhin nur einmal aufgerufen.

Zustandslose Hilfsanfragen verwenden eine eigene Unterhaltung und übernehmen weder Ausgabeschema noch gehostete Tools oder einmalige Optionen der übergeordneten Anfrage. Die Isolierung überspringt niemals die native Anbieterprüfung, auch nicht bei OpenAI- und Perplexity-Runs. Einstellungen, Nachrichten, `CurrentSummary` und Beobachtungsdaten der übergeordneten Anfrage bleiben erhalten. Zustandsbehaftete Anfragen behalten ihre Binding- und Unterhaltungsprüfungen. Die bestehenden öffentlichen APIs bleiben unverändert.

Intern erzeugte Gesprächszusammenfassungen schließen auch den `SystemMessageProvider`-Callback und den Anfragekontext der übergeordneten Anfrage aus. Ein geerbtes `RequestMessageOverride` kann dadurch den internen Zusammenfassungsprompt nicht ersetzen. Von der Anwendung gestartete Anfragen verwenden ihren dynamischen Kontext weiterhin normal, auch bei expliziten Aufträgen zur Textzusammenfassung.

Zustandslose Anfragen überspringen auch die automatische Zusammenfassung des übergeordneten Gesprächs, einschließlich der bisherigen Überladung `GetCompletionAsync(string, profile)`, ebenso wie die `Message`-Überladung und der Request Builder. `CurrentSummary` und die Nachrichten des übergeordneten Gesprächs bleiben unverändert. Zustandsbehaftete Anfragen behalten die übliche automatische Zusammenfassung.

[AIRequestProfile](request-profiles.md) · [AIRequestContext](request-contexts.md) · [WithReasoning / WithWebSearch / WithFileSearch](reasoning-and-search.md)

<a id="provider-request-adapters"></a>

## Anfragen in einem benutzerdefinierten Anbieter weiterleiten

Wenn das Framework einen virtuellen Anbieteradapter aufruft, setzt dessen erster Aufruf des passenden Basiseinstiegspunkts die vorbereitete Anfrage fort, auch wenn die Überschreibung die Eingabe `Message` ersetzt. Erfasste Builder-Optionen und angewendete Profile bleiben bei dieser Weiterleitung erhalten. Auch der standardmäßige Adapter für Callback-Streaming setzt dieselbe vorbereitete Anfrage fort.

Ein Adapter darf ein geändertes `AIRequestProfile` weitergeben: unveränderte Werte werden nicht erneut angewendet; Änderungen ersetzen die vorherige Profilebene auf Basis der erfassten Einstellungen. Die erneute Prüfung erfolgt vor automatischer Zusammenfassung und Versand, auch beim Wechsel zu zustandsloser Ausführung. Zustandslose OpenAI-Hilfsanfragen überspringen fremde Verlaufskontrollen, ohne den Schutz der ursprünglichen Unterhaltung zu löschen.

Beim Ersetzen eines weitergereichten Profils bleiben spätere anfragelokale Tool-Ergänzungen, Entfernungen und Änderungen, Richtlinienänderungen und explizite Zuweisungen erhalten, auch bei erneut zugewiesenen identischen Skalarwerten. Ein vom Adapter entferntes Tool kehrt nicht allein durch ein anderes Profilfeld zurück. Dienstvorgaben werden nicht erneut eingelesen.

Bei undurchsichtigen benutzerdefinierten Einstellungsobjekten sollten Provider-Adapter den Wert über `SetExecutionSetting` ersetzen, statt interne Felder zu ändern. Die Bibliothek untersucht keine beliebigen Anwendungsobjekte und ruft deren Serialisierer nicht zur Profilverfolgung auf.

Claude bewahrt beim Kürzen Aufruf/Ergebnis-Abhängigkeiten in `RequestMessageOverride` und `AdditionalMessages`, einschließlich Serverwerkzeugen, sowie gebundene Thinking-Präfixe von Mythos 5.1. Alte parallele Werkzeugaufzeichnungen werden sowohl im Verlauf als auch in Zusatznachrichten einmal zusammengeführt; ihre jeweilige Zuordnung bleibt erhalten.

Ein unabhängiger Hilfsaufruf desselben Basiseinstiegspunkts vor der Weiterleitung ist mehrdeutig: Das Framework kann nicht erkennen, ob dieser Aufruf die Fortsetzung ist. Umschließen Sie diesen Hilfsaufruf und sein `await` mit dem geschützten `BeginIndependentRequestScope()`; bei Streaming muss der Scope während der gesamten Enumeration geöffnet bleiben. Der Hilfsaufruf beginnt mit den Service-Standardwerten. Beim Freigeben des Scopes werden die äußeren Einstellungen, Features, der Kontext und die ausstehende Delegation wiederhergestellt. Gewöhnliche verschachtelte Aufrufe aus Kontext- oder Tool-Callbacks sind bereits unabhängig und benötigen diesen Scope nicht.

Eine Unterklasse eines konkreten Anbieters kann beispielsweise Text vor der Weiterleitung umformulieren:

```csharp
public override async Task<string> GetCompletionAsync(
    Message message, AIRequestProfile? profile = null,
    AIRequestContext? context = null, CancellationToken cancellationToken = default)
{
    string rewritten;
    using (BeginIndependentRequestScope())
    {
        rewritten = await base.GetCompletionAsync(
            new Message(ActorRole.User, message.Content),
            RequestProfiles.QueryRewrite,
            cancellationToken: cancellationToken);
    }

    var replacement = new Message(message.Role, rewritten);
    return await base.GetCompletionAsync(replacement, profile, context, cancellationToken);
}
```

Dieser Scope trennt den Ausführungszustand der Anfragen; er isoliert weder den Gesprächsverlauf noch erlaubt er die gleichzeitige Nutzung des Dienstes. Das Beispiel verwendet das zustandslose Profil `QueryRewrite`, damit der Hilfsaufruf außerhalb der übergeordneten Unterhaltung bleibt.

Claude prüft beim Verdichten den beibehaltenen kanonischen Verlauf im Übertragungsformat, einschließlich signierter Thinking-Blöcke, die über `AIRequestContext.AdditionalMessages` hinzugefügt wurden. Die standardmäßige Thinking-Bindung schützt dieses Präfix vor automatischer oder expliziter Verdichtung durch Zusammenfassung. Ein ausdrücklich gesetztes `ClaudeThinkingPrefixMismatchBehavior.DropBlock` erlaubt die Verdichtung, soweit unterstützt; andere Einschränkungen für die Unterhaltung gelten weiterhin.

## Kopierte Einstellungen und geteilter Zustand

Allgemeine und anbieterspezifische Standardwerte werden bei `CreateRequest` erfasst. Spätere Änderungen verändern diese Anfrage nicht. Integrierte Nachrichteninhalte, unterstützte Optionssammlungen, Profile, Kontexte und Richtlinien werden kopiert. Funktionshandler, dynamische Kontext-Callbacks und benutzerdefinierte Nachrichteninhalte behalten ihre Referenzen. Verändern Sie benutzerdefinierte Inhalte nicht; Delegates können externen Zustand lesen. Dynamische Kontext-Callbacks werden zur Ausführungszeit aufgerufen.

Nach der Erfassung können Sie das ursprüngliche `JsonDocument` freigeben oder ursprüngliche `JsonNode`-Werte ändern, ohne die JSON-Werte in Anfragemetadaten oder Funktionsaufrufargumenten zu verändern; jede Ausführung erhält eine eigene Kopie. Eine zyklische oder mehr als 64 Ebenen tiefe `Items`-Kette im Toolschema löst bei der Erfassung (`CreateRequest` oder `WithFunctions`) eine `ArgumentException` aus. So wird ein ungültiges Schema vor der Ausführung als normaler Fehler gemeldet, statt den Prozessstack zu erschöpfen.

Die Kopie behält auch die Dimensionen und Startindizes von Arrays sowie die Regeln zum Schlüsselvergleich der Standardcontainer `Dictionary<,>`, `SortedDictionary<,>` und `SortedList<,>` bei. Eine Schlüsselsuche ohne Unterscheidung von Groß- und Kleinschreibung verhält sich daher in der Anfrage genauso. Der leere Wert `default(JsonElement)` (`Undefined`) bleibt unverändert. Unbekannte eigene Metadatenobjekte behalten ihre Referenzen; ihr Besitzer muss sie unverändert lassen oder Zugriffe koordinieren.

Standardwerte vom Typ `ReadOnlyCollection<T>` und `ReadOnlyDictionary<TKey, TValue>` behalten ihren Typ innerhalb typisierter Arrays und Wörterbücher. Unterstützte zugrunde liegende Sammlungen werden kopiert; schreibgeschützte Ansichten, gemeinsame Referenzen und Zyklen bleiben erhalten. Auch `Hashtable` und nicht generische `SortedList` behalten ihre Regeln zum Schlüsselvergleich.

Ein Builder ist kein eigenes Gespräch. Er verwendet das zur Ausführungszeit aktive Gespräch des Services; die Erstellung friert den Verlauf nicht ein. Zustandsbehaftete Aufrufe aktualisieren weiterhin den gemeinsamen Verlauf. `WithStatelessMode()` verhindert dessen Lesen und Erweitern. Die Beschränkung auf einen aktiven Run pro Service bleibt bestehen. Unabhängige Einstellungen garantieren keine parallele Ausführung auf demselben Service. Verwenden Sie getrennte Services für unabhängige gleichzeitige Gespräche.

## Bestehende Aufrufe und Erweiterungen

`GetCompletionAsync` und die bisherigen Service-Einstiegspunkte bleiben verfügbar. `BeginMessage()` / `MessageChain` behalten ihren veränderbaren Nachrichtenaufbau und nutzen zur Ausführung den neuen Anfragepfad. Für wiederverwendbare Varianten dient `CreateRequest`. Die Builder-API gehört zu `AIService` und seinen Anbieterimplementierungen; `IAIService` erhält keine Pflichtmitglieder. Aufrufer über Abstraktionen oder RAG-Wrapper nutzen weiterhin ihre Profil-, Kontext- und Ausführungs-APIs.

[Modelloptionen mit gemeinsamen Fähigkeitsdefinitionen aufbauen](model-capabilities.md).

<a id="inference-speed"></a>

## Verarbeitungsgeschwindigkeit passend zur Aufgabe wählen

Für wartende Nutzer kann sich die kostenpflichtige Verarbeitung mit geringerer Latenz lohnen; ein Hintergrundbericht kann regulär laufen. `WithSpeed` wählt den Modus bei gleichem Modell und Denkaufwand. Benötigt Mythosia.AI 8.1.0 / Abstractions 4.1.0.

`ProviderDefault` überschreibt nichts und erhält vorhandene Dienst-/Anbietereinstellungen; der Projektstandard kann bereits Fast sein. `Standard` fordert reguläre Verarbeitung ausdrücklich an. `Fast` wählt den kostenpflichtigen Modus mit niedriger Latenz und kann Mehrkosten verursachen. Behalten Sie den zurückgegebenen Builder: Die drei Zweige sind unabhängig, die Basis bleibt unverändert.

```csharp
using Mythosia.AI.Models;

var basis = service.CreateRequest("Explain this report.");
var providerDefault = basis.WithSpeed(InferenceSpeed.ProviderDefault);
var standard = basis.WithSpeed(InferenceSpeed.Standard);
var fast = basis.WithSpeed(InferenceSpeed.Fast);
```

Prüfen Sie vor dem Anzeigen der Option `GetSpeedSupport(InferenceSpeed.Fast)`. Auch `StandardSpeed` und `FastSpeed` unterscheiden Supported, Unsupported und Unknown. Lokales Supported bestätigt weder Kontoberechtigung noch Kapazität oder Latenz. Nicht unterstützte oder unbekannte Standard/Fast-Anfragen scheitern, statt Modell oder Denkaufwand still zu ändern. `ProviderDefault` behält den bisherigen Pfad.

```csharp
using Mythosia.AI.Models;
using Mythosia.AI.Models.Capabilities;

var request = service.CreateRequest("Explain this report.")
    .WithSpeed(InferenceSpeed.Fast);
if (request.GetCapabilities().GetSpeedSupport(InferenceSpeed.Fast)
    != CapabilitySupport.Supported)
    throw new NotSupportedException("Fast processing is not supported here.");

await using var run = await request.StartRunAsync();
var result = await run.Result;
Console.WriteLine(result.Text);
foreach (AIProcessingInfo processing in result.Processing)
{
    Console.WriteLine($"{processing.RequestIndex}: {processing.RequestedSpeed} -> " +
        $"{processing.AppliedSpeed?.ToString() ?? "unknown"}; " +
        $"raw={processing.RawAppliedMode}; response={processing.ResponseId}; " +
        $"downgraded={processing.IsDowngraded}");
}
```

`AIRunResult.Processing` erhält unveränderliche `AIProcessingInfo` auch ohne Stream-Leser. `RequestIndex` beginnt bei 1 und zählt Anbieter-Inferenzversuche einschließlich Server-Fortsetzungen, weder Werkzeugrunden noch HTTP-Anfragen; Folgerufe, Wiederholungen und Formatkorrekturen können weitere Einträge erzeugen. Ohne erkannten Servermodus bleibt `AppliedSpeed` null, auch bei fehlgeschlagenen Versuchen. `RawAppliedMode` und `ResponseId` erhalten die gemeldeten Werte. `IsDowngraded` ist nur wahr, wenn auf Fast ausdrücklich Standard gemeldet wurde; false beweist kein Fast.

Nach einer normalen Completion lesen Sie sofort `AIService.LastProcessing`; die nächste logische Anfrage ersetzt diese Ansicht. Bereits gelesene Einträge bleiben unveränderlich. Die Diensterweiterung gilt für die nächste logische Anfrage samt Werkzeugrunden, nicht als dauerhafter Standard. Hilfszusammenfassungen, interne Suchanfragen-Umschreibung und interne Profile übernehmen die Geschwindigkeitsvorgabe nicht und mischen ihre Beobachtungen nicht ein.

```csharp
using Mythosia.AI.Extensions;
using Mythosia.AI.Models;

string answer = await service
    .WithSpeed(InferenceSpeed.Standard)
    .GetCompletionAsync("Explain this report.");
var processing = service.LastProcessing;
```

Die Werte beschreiben den Anbietermodus, keine gemessenen Tokens pro Sekunde. OpenAI, xAI und Google können serverseitig herabstufen; Mythosia wiederholt nicht automatisch mit anderer Geschwindigkeit. Anthropic Fast Mode verlangt Zugriff über die direkte Claude API; ein Moduswechsel kann den Prompt-Cache ungültig machen. Gemini Developer API Priority erfordert Tier 2/3. Prüfen Sie Modell, API, Zugang und Preise separat. Bildgenerierung, Embeddings und native Batch-APIs werden hiermit nicht konfiguriert. [OpenAI](https://developers.openai.com/api/docs/guides/fast-mode) · [Anthropic](https://platform.claude.com/docs/en/build-with-claude/fast-mode) · [xAI](https://docs.x.ai/developers/advanced-api-usage/priority-processing) · [Gemini](https://ai.google.dev/gemini-api/docs/generate-content/priority-inference)

Bei einer `IAIService`-Referenz verwenden Sie `GetLastProcessing()` aus `Mythosia.AI.Extensions`. Es liest das optionale `IAIProcessingInfoService` und liefert ohne Diagnoseunterstützung eine leere Liste. `IAIService` erhält keine Pflichtmitglieder. In RAG gilt `RagEnabledService.WithSpeed(...)` für die nächste Antwort nach der Suche, und `LastProcessing` beschreibt diese Antwort. Interne Suchanfragen-Umschreibung bleibt getrennt; Run-Ergebnisse liefern dieselben `Processing`-Einträge.

Die implementierte Fast-Liste steht unten. Prüfen Sie Standard separat mit `GetSpeedSupport(InferenceSpeed.Standard)`. Nicht gelistete Modelle, fremde Endpunkte und OpenAI-kompatible Anbieter erhalten nicht automatisch Unterstützung für kostenpflichtige Modi.

| API | Fast |
| --- | --- |
| Anthropic — `api.anthropic.com` | `claude-opus-4-8`, `claude-opus-5`, `claude-opus-5-5` |
| OpenAI — `api.openai.com` | `gpt-6.1-sol`, `gpt-6-astra`, `gpt-6-sol`, `gpt-6-luna`, `gpt-5.6`, `gpt-5.6-sol`, `gpt-5.6-terra`, `gpt-5.6-luna`, `gpt-5.3-codex` |
| xAI — `api.x.ai` | `grok-4.7`, `grok-4.6`, `grok-4.5`, `grok-4.5-latest`, `grok-build-latest`, `grok-4.3`, `grok-4.3-latest`, `grok-latest`, `grok-4.20-0309-reasoning`, `grok-4.20-0309-non-reasoning`, `grok-build-0.1` |
| xAI — `us.api.x.ai` | `grok-4.7`, `grok-4.6` |
| Google — Gemini Developer API | `gemini-2.5-pro`, `gemini-2.5-flash`, `gemini-2.5-flash-lite`, `gemini-3-flash-preview`, `gemini-3.1-pro-preview`, `gemini-3.1-flash-lite`, `gemini-3.5-flash`, `gemini-3.5-flash-lite`, `gemini-3.6-flash`, `gemini-3.7-flash`, `gemini-3.8-flash` |

| API | `Standard` | `Fast` | `RawAppliedMode` |
| --- | --- | --- | --- |
| Anthropic — Opus 4.8 / 5 / 5.5 | `speed: "standard"` + `fast-mode-2026-02-01` | `speed: "fast"` + `fast-mode-2026-02-01` | `usage.speed` |
| Anthropic — andere bekannte Claude-Modelle, einschließlich Sonnet 5 | `speed` und Fast-Mode-Beta weggelassen | `Unsupported` | `usage.speed` |
| OpenAI | `service_tier: "default"` | `service_tier: "fast"` | `service_tier` (`fast` / `priority` → Fast) |
| xAI | `service_tier: "default"` | `service_tier: "priority"` | `service_tier` |
| Google | `serviceTier: "standard"` | `serviceTier: "priority"` | `x-gemini-service-tier` / `usageMetadata.serviceTier` |

Standard verwendet bei diesen anderen Claude-Modellen die bestehende reguläre Anfrage. Ohne gemeldete Verarbeitungsdaten bleibt `AppliedSpeed` null; aus dem angeforderten Modus wird Standard nicht abgeleitet.
