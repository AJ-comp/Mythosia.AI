# Den Playground erkunden

Mit dem Playground können Sie Modelloptionen vergleichen und die Dokumentensuche konfigurieren, bevor Sie Anwendungscode schreiben. Dieses Video zeigt die aktuelle Oberfläche in einer lokalen Arbeitsumgebung: von der Modellsuche bis zu den Einstellungen der RAG-Pipeline.

<video class="playground-video" controls playsinline preload="metadata" poster="../assets/playground-demo.png" aria-label="Rundgang durch die Oberfläche des Mythosia.AI Playground">
  <source src="../assets/playground-demo.mp4?v=6" type="video/mp4">
  <track kind="subtitles" src="../assets/playground-demo.vtt?v=6" srclang="en" label="English">
  <track kind="subtitles" src="../assets/playground-demo.ko.vtt?v=6" srclang="ko" label="한국어">
  <track kind="subtitles" src="../assets/playground-demo.ja.vtt?v=6" srclang="ja" label="日本語">
  <track kind="subtitles" src="../assets/playground-demo.zh-Hans.vtt?v=6" srclang="zh-Hans" label="简体中文">
  <track kind="subtitles" src="../assets/playground-demo.zh-Hant.vtt?v=6" srclang="zh-Hant" label="繁體中文">
  <track kind="subtitles" src="../assets/playground-demo.de.vtt?v=6" srclang="de" label="Deutsch" default>
  <track kind="subtitles" src="../assets/playground-demo.fr.vtt?v=6" srclang="fr" label="Français">
  <track kind="subtitles" src="../assets/playground-demo.es.vtt?v=6" srclang="es" label="Español">
  <track kind="subtitles" src="../assets/playground-demo.pt.vtt?v=6" srclang="pt" label="Português">
  <track kind="subtitles" src="../assets/playground-demo.ru.vtt?v=6" srclang="ru" label="Русский">
  <track kind="subtitles" src="../assets/playground-demo.uk.vtt?v=6" srclang="uk" label="Українська">
  <track kind="subtitles" src="../assets/playground-demo.vi.vtt?v=6" srclang="vi" label="Tiếng Việt">
  <track kind="subtitles" src="../assets/playground-demo.th.vtt?v=6" srclang="th" label="ไทย">
  Ihr Browser unterstützt keine eingebetteten Videos. <a href="../assets/playground-demo.mp4?v=6">Video herunterladen</a>.
</video>

[Video herunterladen (MP4)](../assets/playground-demo.mp4?v=6) · [Untertitel lesen](../assets/playground-demo.de.vtt?v=6)

Das Video ist ohne Ton. Die Untertitel erscheinen automatisch in der Sprache dieser Seite. Im Menü des Players können Sie eine andere Sprache wählen oder die Untertitel ausschalten.

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
