# Agentisches RAG

## Warum agentisches RAG?

Beim Standard-RAG löst jede Nutzernachricht genau **eine** Retrieval-Anfrage aus. Das System sucht, baut Kontext auf und generiert eine Antwort — egal was. Das funktioniert gut für einfache Fragen, stößt aber an Grenzen, wenn:

- Die Frage **mehrere Suchen** über verschiedene Themen erfordert (z. B. „Vergleiche die Rückgaberichtlinie für Hardware und Software")
- Das erste Suchergebnis **unzureichend** ist und das System verfeinern und es erneut versuchen sollte
- Manche Fragen **gar kein Retrieval benötigen** (z. B. „Fasse unser bisheriges Gespräch zusammen")
- Die Antwort vom **Kombination aus Dokumenten-Retrieval und Live-Daten** von APIs abhängt

Agentisches RAG löst all das. Statt einer festen Retrieve-dann-Antwort-Pipeline **entscheidet der Agent autonom** — wann er sucht, wonach er sucht, ob er nochmal sucht und wann er andere Tools aufruft — alles innerhalb eines ReAct-Loops.

## Schnellstart

Den `RagStore` mit `WithAgenticRag` als Tool registrieren, dann `RunAgentAsync` aufrufen:

```csharp
// Index einmal aufbauen
var ragStore = await RagStore.BuildAsync(cfg => cfg
    .AddDocument("handbuch.pdf")
    .AddDocument("richtlinie.docx")
    .UseOpenAIEmbedding(apiKey));

// RAG als Tool registrieren und den Agenten starten
var service = new AnthropicService(apiKey, http);
service.WithAgenticRag(ragStore);

var answer = await service.RunAgentAsync("Fasse die Rückgaberichtlinie zusammen.");
```

Der Agent ruft `search_documents` automatisch auf, wenn er Dokumentenkontext benötigt, und synthetisiert dann die endgültige Antwort aus den abgerufenen Ausschnitten.

## Kombination mit anderen Tools

Agentisches RAG glänzt in Kombination mit zusätzlichen Tools — der Agent wählt für jede Teilaufgabe das richtige Tool:

```csharp
var service = new AnthropicService(apiKey, http);

service.WithAgenticRag(ragStore)
       .WithFunctionAsync("get_order_status", "Bestellstatus anhand der Bestellnummer abfragen.",
           ("order_id", "Die zu suchende Bestellnummer.", required: true),
           async id => await orderApi.GetStatusAsync(id));

// Der Agent sucht in Dokumenten nach der Richtlinie UND ruft die API für Live-Bestelldaten auf
var answer = await service.RunAgentAsync(
    "Bestellung #12345 — habe ich gemäß der aktuellen Richtlinie Anspruch auf Erstattung?");
```

In diesem Beispiel geht der Agent autonom vor:

1. Sucht in Dokumenten nach der Rückgaberichtlinie
2. Ruft die Bestell-API für den Status von Bestellung #12345 auf
3. Kombiniert beide Informationen zu einer endgültigen Antwort

## Benutzerdefinierte Tool-Beschreibung

Die Tool-Beschreibung steuert, wann der Agent entscheidet, RAG aufzurufen. Passe sie für mehr Genauigkeit an deine Domäne an:

```csharp
service.WithAgenticRag(ragStore,
    toolDescription:
        "Durchsucht interne HR-Richtlinien, Produkthandbücher und Compliance-Dokumente. " +
        "Dieses Tool aufrufen, wenn unternehmensspezifische Richtlinien oder Produktinformationen benötigt werden.");
```

Eine vage Beschreibung wie „Dokumente durchsuchen" kann dazu führen, dass der Agent RAG zu oft oder zu selten aufruft. Sei spezifisch, **welche Art von Informationen** die Dokumente enthalten.

## Unterschied zum Standard-RAG

| | Standard-RAG | Agentisches RAG |
| --- | --- | --- |
| Such-Zeitpunkt | Jede Nachricht | Agent entscheidet |
| Query-Formulierung | QueryRewriter | Agent selbst |
| Anzahl Suchen | Einmal pro Runde | Ein- oder mehrmals nach Bedarf |
| Tool-Kombination | Nicht anwendbar | Jedes registrierte Tool |
| Einrichtung | `.WithRag()` | `.WithAgenticRag()` + `RunAgentAsync` |

> **Hinweis:** `QueryRewriter` wird beim agentischen RAG bewusst umgangen. Der Agent formuliert seine eigene eigenständige Suchanfrage, weshalb ein separater Umschreibungsschritt redundant wäre und die Absicht des Agenten verzerren könnte.

## Wann was wählen

- **Standard-RAG** — jede Frage ist dokumentenbasiert, einthemig und du willst minimale Latenz
- **Agentisches RAG** — Fragen erstrecken sich über mehrere Themen, erfordern die Kombination von Dokumenten und Live-Daten oder benötigen iteratives Retrieval
