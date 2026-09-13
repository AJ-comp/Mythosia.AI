# Passende Funktionen für das gewählte Modell anzeigen

Eine Chatoberfläche sollte Reasoning, Suche, Tools und Bilder passend zur gewählten Verbindung anbieten. Modelllisten in jeder Anwendung verdoppeln Bibliotheksregeln und weichen bei Anbieter-, Protokoll- oder Deploymentänderungen ab. Fähigkeits-Snapshots geben Oberfläche und Ausführungsvalidierung dieselben Modelldefinitionen.

Diese API gehört zu Mythosia.AI 8.0.0. Snapshots sind unveränderliche lokale Beschreibungen bekannten Supports, keine Liveabfrage von Konto oder Server. Die Typen liegen in `Mythosia.AI.Models.Capabilities`.

## Before / After

Before: Die Anwendung pflegt eigene Modelllisten. Die Listen unten sind Anwendungscode, keine Bibliotheks-APIs.

```csharp
bool showReasoning = mySupportedReasoningModels.Contains(service.Model);
bool showSearch = mySupportedSearchModels.Contains(service.Model);
```

After: Die konfigurierte Anfrage prüfen und unterstützte Optionen wählen. Erst der abschließende Completion-Aufruf sendet die Modellanfrage; die Fähigkeitsabfrage kontaktiert keine API.

```csharp
using Mythosia.AI.Models;
using Mythosia.AI.Models.Capabilities;

var request = service.CreateRequest("Erkläre die Dokumente.");
AIModelCapabilities capabilities = request.GetCapabilities();

if (capabilities.GetReasoningSupport(ReasoningLevel.High)
    == CapabilitySupport.Supported)
{
    request = request.WithReasoning(ReasoningLevel.High);
}

string answer = await request.GetCompletionAsync();
```

`CapabilitySupport` unterscheidet `Supported`, `Unsupported` und `Unknown`. Bei eigenen Deployments oder Serverauswahl können Informationen fehlen: `Unknown` heißt nicht unsupported. Das Beispiel aktiviert zusätzliches Reasoning nur bei bekanntem Support. Bei unbekanntem Support entscheidet die Anwendung über Standardwerte oder einen Anfrageversuch.

`request.GetCapabilities()` liest erfasstes Modell, Anbieteroptionen und Profil des Builders. `service.GetCapabilities()` prüft Dienststandardwerte, ohne Optionen für den nächsten Aufruf zu verbrauchen. Beide senden kein HTTP, rufen keine Kontext-Callbacks oder Ausführungsvalidierungen auf, ändern keinen Verlauf und starten keine Arbeit. Listen sind schreibgeschützte Snapshots. Die Dienstabfrage berücksichtigt auch wartende Funktionseinstellungen für den nächsten Aufruf und lässt sie für die eigentliche Anfrage verfügbar. Die Abfrage serialisiert weder Funktionsstandardwerte noch Parameter gehosteter Tools und führt keine Ausführungsvorbereitung des Profils oder Reservierung von Tokenbudgets durch.

Fähigkeiten beschreiben möglichen Support, keine bereits aktivierten Optionen. Anbieter, API-Protokoll und Modus zählen neben dem Modellnamen. Die Modellidentität berücksichtigt Überschreibungen und Qwen/Ollama-ID-Umsetzung; ohne einzelnes ausgewähltes Modell kann sie `null` sein. Die Beispielanwendung Chat UI aktualisiert die Steuerelemente anhand der aktiven Verbindung und ihrer aktuellen Einstellungen einschließlich registrierter Tools, statt nur den Modellkatalog zu verwenden. Die Unterstützung für Sampling kann sich mit dem Denkmodus oder verfügbaren Tools ändern; fragen Sie nach solchen Änderungen erneut ab. Unbekannte Unterstützung wird von fehlender Unterstützung unterschieden.

| API | Bedeutung |
| --- | --- |
| `Reasoning`, `ReasoningLevels`, `GetReasoningSupport(...)` | Support und Stufen des gemeinsamen `WithReasoning`. |
| `NativeReasoning`, `NativeReasoningLevels`, `ThinkingBudgetPresets`, `ThinkingToggle` | Anbietereigene Reasoning-Regler und Budgetvorschläge. |
| `Streaming`, `FunctionCalling`, `AsyncFunctionCalling`, `Steering` | Streaming, Tools, native asynchrone Tools und Anweisungen während der Ausführung. |
| `WebSearch`, `FileSearch`, `ReasoningCachePreservation`, `ImageInput`, `StructuredOutput` | Gehostete Suche, cacheerhaltende Reasoning-Änderungen, Bildeingabe und strukturierte Ausgabe. |
| `Temperature`, `TopP`, `FrequencyPenalty`, `PresencePenalty`, `MaxOutputTokens` | Unterstützte Samplingoptionen und bekannte Ausgabetokengrenze; nullable. |
| `Provider`, `Model` | Anbieter und gesendetes Modell; Identitäten können unbekannt sein. |

`ReasoningLevels` beschreibt gemeinsames `WithReasoning`, `NativeReasoningLevels` anbietereigene Regler. `ThinkingBudgetPresets` liefert UI-Vorschläge, keine vollständige Menge zulässiger Budgets oder Zahlenbereiche. `AsyncFunctionCalling` meint native asynchrone Toolausführung, nicht lediglich lokale `Task`-Handler oder parallele Ausführung. `StructuredOutput` umfasst die gemeinsame typisierte Ausgabe einschließlich Prompt-/Reparaturfallback und garantiert kein natives eingeschränktes Decoding. Beide Reasoning-Listen verwenden `ReasoningLevel`; Budgetvorschläge sind Ganzzahlen.

Snapshots garantieren weder Kontozugriff noch Serverbereitschaft und erlauben keine ungültigen Optionskombinationen. Ausführungsvalidierungen und Fehler bleiben. Prüfen Sie `run.CanSteer` auf der tatsächlichen Sitzung: Modellsupport garantiert nicht, dass der Run noch aktiv ist.

## Bildgenerierung getrennt prüfen

Das Bildmodell wird unabhängig vom Chat gewählt. `service.GetImageCapabilities(imageModel)` prüft ein bestimmtes Modell; ohne Argument das Standardbildmodell des Anbieters. Der Chat-Builder wählt kein Bildmodell. `Generation`, `Editing` und `Mask` helfen beim Anzeigen der Bildaktionen.

```csharp
using Mythosia.AI.Models.Capabilities;

ImageModelCapabilities capabilities = service.GetImageCapabilities();
bool showEditing = capabilities.Editing == CapabilitySupport.Supported;
bool showMask = capabilities.Mask == CapabilitySupport.Supported;

foreach (var quality in capabilities.Qualities)
    Console.WriteLine(quality);

int? maximumImages = capabilities.MaxImages;
```

`Qualities`, `Backgrounds`, `OutputFormats`, `SizeKinds`, `Resolutions` und `AspectRatios` sind typisierte schreibgeschützte Listen. `MaxImages` und `MaxInputImages` sind bekannte nullable Grenzen. Gelistete Werte erlauben nicht jede Kombination: Größen-, Format-, Qualitäts-, Masken- und Modellprüfungen bleiben. Eigene oder unbekannte Bildmodelle bleiben unbekannt statt unsupported.

Eigene `AIService`-Implementierungen können bei zuverlässigen Definitionen den protected Hook `ResolveRequestCapabilities()` überschreiben. Standard ist `AIModelCapabilities.Unknown`. Ein nicht gelistetes Deployment darf nicht deshalb unsupported werden. `IAIService` erhält keine Pflichtmitglieder; die Abfragen liegen auf `AIService` und seinem Builder.

Ändert das Profil eines eigenen Anbieters native Modusflags, überschreiben Sie `ApplyCapabilityRequestProfile(AIRequestProfile)` und setzen Sie mit `SetExecutionSetting(...)` nur die vom Resolver benötigten Flags. Der Standardhook tut nichts. Gemeinsame Profilwerte hat der Builder bereits erfasst; die Abfrage ruft weder `ApplyRequestProfile` noch `ApplyProviderSpecificRequestProfile` auf. Dieser Hook darf keine Validierung, Callbacks, Serialisierung oder Budgetreservierung ausführen und keinen Zustand des Dienstes oder Aufrufers ändern. Seine temporären Einstellungen werden nach der Abfrage auch dann wiederhergestellt, wenn die Überschreibung eine Ausnahme auslöst.

[Anfrageeinstellungen](request-building.md) · [Anbieter- und Bildoptionen](providers.md) · [Run-Steuerung](execution-api-transition.md)
