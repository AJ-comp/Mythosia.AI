# Laufende Modellserver verwalten

Eine Modellauswahl oder Betriebsoberfläche benötigt vor dem Senden eines Prompts Informationen über Erreichbarkeit, verfügbare Modelle und Ladezustand. Die Serving-Pakete vereinheitlichen diese Prüfungen für Ollama, llama.cpp und vLLM; laufzeitspezifische Aktionen bleiben ausdrücklich auszuführen.

Verwenden Sie die Pakete für eine Modellauswahl, die Anzeige der Erreichbarkeit, die Verwaltung der Speicherresidenz bei entsprechender Laufzeitunterstützung oder das Lesen von Engine-Metriken. Beim Wechsel der Laufzeit kann der gemeinsame Abfragecode Ihrer Anwendung erhalten bleiben.

Diese Clients verbinden sich mit einem vorhandenen HTTP-Server. Installation und Hosting der Engine, GPU-Miete, Chat und Embedding-Erzeugung gehören zu anderen Komponenten. Chat läuft weiterhin über den passenden KI-Dienst, etwa `QwenService` für vLLM; RAG-Embedding-Provider bleiben getrennt. Die Erkennung lädt Modelle nicht automatisch. SGLang ist nicht implementiert.

## Paket auswählen

| Paket | Version | Einsatz |
| --- | --- | --- |
| `Mythosia.AI.Serving.Abstractions` | 1.0.0 | Gemeinsame Verträge für Anwendungscode oder einen eigenen Verwaltungsadapter. Keine Paketabhängigkeiten. |
| `Mythosia.AI.Serving.Ollama` | 1.0.0 | Ollama abfragen, Modelle herunterladen und ausdrücklich vorladen oder entladen. |
| `Mythosia.AI.Serving.LlamaCpp` | 1.0.0 | llama.cpp abfragen, Metriken lesen und Modelle im Routermodus verwalten. |
| `Mythosia.AI.Serving.Vllm` | 1.1.0 | vLLM abfragen und Metriken über gemeinsame oder bestehende vLLM-spezifische APIs lesen. |

Alle vier Pakete richten sich an .NET Standard 2.1. Installieren Sie den benötigten Adapter; das Abstraktionspaket wird automatisch mitinstalliert. Die Adapter hängen von den gemeinsamen Verträgen und Newtonsoft.Json ab, unabhängig von den Kern-KI- und RAG-Paketen.

## Abfragen ohne Zustandsänderung

Installieren Sie das konkrete Paket Ihrer Laufzeit. Das Beispiel verwendet Ollama; für andere Server wählen Sie `VllmServer` oder `LlamaCppServer` aus dem jeweiligen Namespace. Die Erkennung verwendet ausschließlich lesende Anfragen und sendet keine Lade-, Generierungs- oder Downloadbefehle.

```bash
dotnet add package Mythosia.AI.Serving.Ollama --version 1.0.0
```

```csharp
using System;
using System.Net.Http;
using System.Threading;
using Mythosia.AI.Serving;
using Mythosia.AI.Serving.Ollama;

using var http = new HttpClient { Timeout = TimeSpan.FromMinutes(10) };
using var cancellation = new CancellationTokenSource();
IModelServer server = new OllamaServer("http://localhost:11434", http,
    apiKey: Environment.GetEnvironmentVariable("MODEL_SERVER_API_KEY"));

var health = await server.GetHealthAsync(cancellation.Token);
var info = await server.GetInfoAsync(cancellation.Token);
var capabilities = await server.GetCapabilitiesAsync(cancellation.Token);
var models = await server.GetModelsAsync(cancellation.Token);

foreach (var model in models)
    Console.WriteLine($"{model.Id}: {model.InstallationState} / {model.LoadState}");
```

Der Endpunkt ist die Basisadresse des Servers, gegebenenfalls mit dem Pfadpräfix eines Reverse-Proxys. Der optionale API-Schlüssel wird pro Anfrage als Bearer-Zugangsdaten gesendet. Der Client ändert `HttpClient.DefaultRequestHeaders` nicht und gibt den übergebenen `HttpClient` nicht frei; verwenden und entsorgen Sie ihn entsprechend der Lebensdauer Ihrer Anwendung. In diesem Ollama-Beispiel gilt das Timeout auch für gestreamte Antwortinhalte. Planen Sie daher genügend Zeit für Modelldownloads ein.

## Gemeinsame und optionale Verträge

| Vertrag | Zweck |
| --- | --- |
| `IModelServer` | Serverinformationen, Zustand, Modelle und beobachtete Fähigkeiten. |
| `IModelLifecycle` | Explizite Lade- und Entladebefehle; optional. |
| `IModelDownloader` | Expliziter Download mit Fortschritt; optional. |
| `IModelMetricsProvider` | Messwerte mit erhaltenen Labels; optional. |

Eine implementierte Schnittstelle bedeutet, dass der Client die Operation anbietet. `ServingCapabilities` beschreibt die belegbare Unterstützung des verbundenen Endpunkts. `Supported` garantiert weder Berechtigung noch Erfolg für jedes Modell. `Unsupported` bedeutet im beobachteten Modus oder Endpunkt nicht verfügbar. `Unknown` steht für fehlende Belege, etwa bei Authentifizierungs- oder Verbindungsfehlern, und darf nicht als nicht unterstützt gelten.

`InstallationState` und `LoadState` beschreiben unterschiedliche Beobachtungen. `Unknown` bedeutet weder fehlend noch entladen. Nicht gemeldete `SizeBytes`, `MemoryBytes` und `ContextLength` bleiben `null`, nicht null Byte. Ein gesunder Verwaltungsendpunkt beweist nicht die Inferenzbereitschaft eines bestimmten Modells.

## Unterschiede zwischen Laufzeiten

| Operation | Ollama | llama.cpp mit einem Modell | llama.cpp Router | vLLM |
| --- | --- | --- | --- | --- |
| Informationen, Zustand und Modellliste | Ja | Ja | Ja | Ja |
| Explizites Laden / Entladen | Ja, über leere Generierungsanfragen | Nicht unterstützt | Ja, nach Bestätigung des Routermodus | Von diesem Client nicht unterstützt |
| Modelldownload | Ja, mit gestreamtem Fortschritt | Nicht unterstützt | Explizite Operation; benötigt Downloadendpunkt und SSE-Ereignisse | Von diesem Client nicht unterstützt |
| Metriken | Nicht implementiert | Servermetriken, wenn aktiviert | Konkrete modellspezifische Überladung; Modell muss bereits geladen sein | Servermetriken, wenn verfügbar |

Die Tabelle beschreibt Clientoperationen und garantiert keine Unterstützung durch jede Serverversion, Berechtigungskonfiguration oder jedes Modell. Prüfen Sie die Fähigkeiten des verbundenen Endpunkts und behandeln Sie Fehler bei Operationen.

**Ollama:** `/api/tags` liefert registrierte Modelle, `/api/ps` aktive Ausführungen. Ein entferntes Modell kann ohne lokale Gewichte registriert sein; ohne lokalen Eintrag bleibt sein Ladezustand unbekannt. Vorladen nutzt eine leere `/api/generate`-Anfrage mit dem Standard-Keep-alive des Servers. Reine Embedding-Modelle werden nicht auf einen anderen Endpunkt umgeleitet. Entladen verwendet `keep_alive: 0` und löscht keine Dateien. Metriken sind nicht implementiert.

**llama.cpp:** Vor Lebenszyklus- oder Downloadbefehlen muss `/props` den Routermodus ausdrücklich bestätigen. Der Einzelmodellmodus unterstützt diese Befehle nicht; ein beobachteter Schlafzustand bleibt erhalten. Routerdownloads abonnieren zuerst `/models/sse`, senden dann `POST /models` und gelten nur beim Ereignis `download_finished` des Zielmodells als erfolgreich. SSE allein belegt keinen Downloadsupport. Serverweite Metriken gelten für den Einzelmodellmodus; Routermetriken benötigen die konkrete Überladung `GetMetricsAsync(modelId, token)`, die mit `autoload=false` ein Laden durch die Abfrage verhindert.

**vLLM:** Bereitgestellte Aliasse und das optionale Feld `root` bleiben verfügbar, gemeinsame Installations- und Ladezustände jedoch unbekannt. Modellliste und Metriken werden anhand tatsächlicher Antworten geprüft; Lebenszyklus und Download sind nicht unterstützt. Die bisherigen Methoden und DTOs von `VllmServer` bleiben am konkreten Client erhalten; gemeinsame Zustands-, Modell- und Metrikmethoden sind explizite Schnittstellenimplementierungen.


## Eine Verwaltungsaktion ausdrücklich ausführen

Downloads und Änderungen der Speicherresidenz verbrauchen Netzwerk, Datenträger oder Gerätespeicher. Führen Sie sie aus, wenn Ihre Anwendung diese Aktion benötigt. Die folgende Fortsetzung des Ollama-Beispiels lädt ein kleines Modell herunter und kurzzeitig in den Speicher, um seinen Zustand abzufragen. Verwenden Sie exakte Servermodell-IDs einschließlich des Ollama-Tags oder llama.cpp-Quantisierungstags.

```csharp
if (server is IModelDownloader downloader &&
    capabilities.ModelDownloading == ServingFeatureSupport.Supported)
{
    var progress = new Progress<ModelDownloadProgress>(p =>
        Console.WriteLine($"{p.ModelId}: {p.Stage} {p.CompletedBytes}/{p.TotalBytes}"));
    await downloader.DownloadModelAsync("qwen2.5:0.5b", progress, cancellation.Token);
}

if (server is IModelLifecycle lifecycle &&
    capabilities.ModelLoading == ServingFeatureSupport.Supported &&
    capabilities.ModelUnloading == ServingFeatureSupport.Supported)
{
    try
    {
        await lifecycle.LoadModelAsync("qwen2.5:0.5b", cancellation.Token);
        var afterLoad = await server.GetModelsAsync(cancellation.Token);
        foreach (var model in afterLoad)
            Console.WriteLine($"{model.Id}: {model.LoadState}");
    }
    finally
    {
        using var cleanup = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        await lifecycle.UnloadModelAsync("qwen2.5:0.5b", cleanup.Token);
    }
}
```

Das Beispiel verwendet ein eigenes Testmodell und entlädt es anschließend. Eine Produktionsanwendung entscheidet selbst, wann sie ein Modell freigibt; entladen Sie kein Modell, das andere Anfragen noch verwenden. Die Bereinigung hat ein eigenes Zeitlimit und kann scheitern, wenn der Server nicht erreichbar ist.

Fortschritt beschreibt ein einzelnes Artefakt oder eine Phase. Fehlende Bytezähler sind weder null Byte noch ein Prozentsatz des Gesamtmodells. Ein erfolgreicher Ladeaufruf bestätigt den Befehl, nicht die Bereitschaft oder unbegrenzte Residenz. Beobachten Sie `LoadState` mit begrenzter Wartezeit, wenn die Bereitschaft wichtig ist. Das Downloadprotokoll und die Versionsgrenzen des llama.cpp-Routers beschreibt die konkrete Paketanleitung. Eine ausdrücklich angeforderte Operation kann bei `Unknown` versucht werden, nachdem die Serverkonfiguration geprüft wurde; die Fähigkeitserkennung allein löst sie niemals aus.

## Abbruch und Fehler

Übergeben Sie Abbruchtoken an Abfragen und Befehle. Ein Abbruch beendet HTTP-Arbeit und Warten dieses Clients, garantiert aber weder entfernten Abbruch noch Rollback oder das Entfernen heruntergeladener Schichten. Konfigurieren Sie den übergebenen `HttpClient` für die benötigte Dauer; die Clients übernehmen ihn nicht.

Bewahren Sie Metriklabels beim Vergleich von Modellen oder Engines auf. Fehlende Metriken sind nicht null, und Werte können `NaN` oder unendlich sein. `ServingException` ist der gemeinsame Fehlertyp; gemeinsame Verwaltungsfehler enthalten weder rohe Antwortinhalte noch Zugangsdaten. Bestehende vLLM-spezifische Aufrufe behalten ihre bisherigen Fehlerdetails.

`GetHealthAsync` ordnet Endpunktfehler Zustandswerten zu und gibt einen Abbruch durch den Aufrufer weiterhin weiter. Andere Operationen können `ServingException` auslösen; ein bekanntlich nicht unterstützter llama.cpp-Modus kann `NotSupportedException` auslösen. Weder ein Timeout noch eine fehlgeschlagene Anfrage beweist ein Zurückrollen der entfernten Aktion. Ein Downloadaufruf kehrt erst erfolgreich zurück, wenn die Laufzeit den Abschluss meldet: abschließender Erfolg gefolgt von EOF bei Ollama oder das passende `download_finished`-Ereignis beim llama.cpp-Router.

## Was geprüft wurde

Offline-Tests decken kontrollierte Erfolgsfälle, fehlerhafte Antworten, Fehler und Abbruch ab. Separate Prüfungen an realen Servern verwendeten eine einzelne NVIDIA A40, kleine öffentliche Qwen-Modelle und folgende Engine-Builds:

| Laufzeit | Geprüftes Modell | Geprüfte Verwaltungsoperationen |
| --- | --- | --- |
| Ollama 0.34.4 | `qwen2.5:0.5b` | Erkennung, neuer Download, Laden/Entladen, bereinigte Fehler für fehlende Modelle, vorab ausgelöster Abbruch und Abbruch nach teilweisem Downloadfortschritt. |
| llama.cpp b11146, Router | `Qwen/Qwen2.5-0.5B-Instruct-GGUF:Q4_K_M` | Erkennung, Downloadereignisse, Laden/Entladen, modellspezifische Metriken ohne Autoload, Fehler und Downloadabbruch. |
| llama.cpp b11146, Einzelmodell | Dasselbe GGUF-Modell | Erkennung, Servermetriken, Abbruch und ausdrückliche Ablehnung von Router-Lebenszyklusbefehlen. |
| vLLM 0.30.0 | `Qwen/Qwen2.5-0.5B-Instruct` | Erkennung, Servermetriken und vorab ausgelöster Abbruch. |

Kurze native HTTP-Inferenzanfragen lieferten in allen vier Konfigurationen generierten Text. Sie bestätigen den Enginebetrieb, nicht die Chatadapter der KI-Dienste, Modellqualität, Durchsatz oder Kompatibilität mit jedem Engine-Build. Die obigen Profile sind geprüfte Konfigurationen, keine Mindestversionen. Für Downloadabbrüche wurden andere, größere Testmodelle verwendet; ein entferntes Rollback wurde nicht zugesichert. Ein erster Ollama-Download schlug fehl. Wiederholung und neuer Download nach Entfernen des Modells waren erfolgreich, ohne die genaue Ursache des ersten Fehlers zu klären.

Prüfen Sie Ihren bereitgestellten Endpunkt mit der [Anleitung zur ausdrücklich aktivierten Live-Prüfung](https://github.com/AJ-comp/Mythosia.AI/blob/main/tests/Mythosia.AI.Serving.Live/README.md). Sie unterscheidet den eingecheckten Verwaltungstestläufer von den zusätzlichen Inferenz- und Abbruchprüfungen der Verifikation. Detaillierte Ausführungsberichte bleiben außerhalb der veröffentlichten Dokumentation.

## Paketanleitungen

- [Mythosia.AI.Serving.Abstractions](../../src/serving/Mythosia.AI.Serving.Abstractions/README.md) — Gemeinsame Verwaltungsverträge und unveränderliche Server-, Modell- und Fähigkeitsdaten.
- [Mythosia.AI.Serving.Ollama](../../src/serving/Mythosia.AI.Serving.Ollama/README.md) — Ollama-Inventar, Zustand, explizites Vorladen/Entladen und gestreamte Downloads.
- [Mythosia.AI.Serving.LlamaCpp](../../src/serving/Mythosia.AI.Serving.LlamaCpp/README.md) — llama.cpp-Abfragen, geprüfte Routeraktionen und Metriken ohne automatisches Laden.
- [Mythosia.AI.Serving.Vllm](../../src/serving/Mythosia.AI.Serving.Vllm/README.md) — vLLM-Modellkarten, Zustand, Version und Metriken mit Labels; konkrete API bleibt erhalten.
