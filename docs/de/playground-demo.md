# Den Playground erkunden

Mit dem Playground können Sie Modelloptionen vergleichen und die Dokumentensuche konfigurieren, bevor Sie Anwendungscode schreiben. Dieses Video zeigt die aktuelle Oberfläche in einer lokalen Arbeitsumgebung: von der Modellsuche bis zu den Einstellungen der RAG-Pipeline.

<video controls playsinline preload="metadata" poster="../assets/playground-demo.png" aria-label="Rundgang durch die Oberfläche des Mythosia.AI Playground" style="display: block; width: 100%; height: auto; border-radius: 12px;">
  <source src="../assets/playground-demo.mp4" type="video/mp4">
  <track kind="captions" src="../assets/playground-demo.vtt" srclang="en" label="English">
  Ihr Browser unterstützt keine eingebetteten Videos. <a href="../assets/playground-demo.mp4">Video herunterladen</a>.
</video>

[Video herunterladen (MP4)](../assets/playground-demo.mp4) · [Untertitel lesen](../assets/playground-demo.vtt)

Die Aufnahme enthält keinen gesprochenen Kommentar. Unterhalb der Anwendung erscheinen englische Beschreibungen der einzelnen Schritte. Im Player steht außerdem eine separate Untertitelspur zur Verfügung.

## Was das Video zeigt

1. Die sieben Anbietergruppen durchsehen und ein Modell nach Name oder Anbieter suchen.
2. Den Dialog für den Anbieterschlüssel öffnen, bevor ein Modell verbunden wird.
3. Zwischen Englisch und Koreanisch wechseln; die Oberfläche unterstützt 13 Sprachen.
4. Die Dokumentregistrierung und die Optionen zur Textaufteilung erkunden.
5. Die Einstellungen für Embedding-Anbieter, Vektorspeicher, hybride Suche und Reranking ansehen.

Das Video führt durch die Oberfläche: Es werden keine API-Schlüssel übermittelt, Dokumente indexiert oder Modellantworten generiert.

## Lokal ausprobieren

Führen Sie im Stammverzeichnis des Repositorys mit dem in `global.json` angegebenen SDK Folgendes aus:

```sh
dotnet run --project apps/Mythosia.AI.Samples.ChatUi
```

Öffnen Sie die von der Anwendung ausgegebene Adresse. Erkunden Sie zunächst die Modelle und Einstellungen und fügen Sie Ihren Anbieterschlüssel hinzu, wenn Sie Anfragen senden möchten.

Details zu Verbindungen, Sprachen und lokaler Entwicklung finden Sie im [Playground-Leitfaden](https://github.com/AJ-comp/Mythosia.AI/blob/main/apps/Mythosia.AI.Samples.ChatUi/README.md). Für die entsprechenden Bibliotheks-APIs beginnen Sie mit dem [Schnellstart](getting-started.md) oder den [RAG-Grundlagen](rag.md).
