# Explorar el Playground

Utilice el Playground para comparar las opciones de los modelos y configurar la búsqueda de documentos antes de escribir el código de su aplicación. Este recorrido muestra la interfaz actual en un entorno de trabajo local, desde la búsqueda de un modelo hasta la exploración de los ajustes del pipeline RAG.

<video class="playground-video" controls playsinline preload="metadata" poster="../assets/playground-demo.png" aria-label="Recorrido por la interfaz del Playground de Mythosia.AI">
  <source src="../assets/playground-demo.mp4?v=6" type="video/mp4">
  <track kind="subtitles" src="../assets/playground-demo.vtt?v=6" srclang="en" label="English">
  <track kind="subtitles" src="../assets/playground-demo.ko.vtt?v=6" srclang="ko" label="한국어">
  <track kind="subtitles" src="../assets/playground-demo.ja.vtt?v=6" srclang="ja" label="日本語">
  <track kind="subtitles" src="../assets/playground-demo.zh-Hans.vtt?v=6" srclang="zh-Hans" label="简体中文">
  <track kind="subtitles" src="../assets/playground-demo.zh-Hant.vtt?v=6" srclang="zh-Hant" label="繁體中文">
  <track kind="subtitles" src="../assets/playground-demo.de.vtt?v=6" srclang="de" label="Deutsch">
  <track kind="subtitles" src="../assets/playground-demo.fr.vtt?v=6" srclang="fr" label="Français">
  <track kind="subtitles" src="../assets/playground-demo.es.vtt?v=6" srclang="es" label="Español" default>
  <track kind="subtitles" src="../assets/playground-demo.pt.vtt?v=6" srclang="pt" label="Português">
  <track kind="subtitles" src="../assets/playground-demo.ru.vtt?v=6" srclang="ru" label="Русский">
  <track kind="subtitles" src="../assets/playground-demo.uk.vtt?v=6" srclang="uk" label="Українська">
  <track kind="subtitles" src="../assets/playground-demo.vi.vtt?v=6" srclang="vi" label="Tiếng Việt">
  <track kind="subtitles" src="../assets/playground-demo.th.vtt?v=6" srclang="th" label="ไทย">
  Su navegador no admite vídeos integrados. <a href="../assets/playground-demo.mp4?v=6">Descargar el recorrido</a>.
</video>

El vídeo no tiene sonido. Los subtítulos aparecen automáticamente en el idioma de esta página. Puede cambiar su idioma o desactivarlos desde el menú del reproductor.

## Qué muestra el recorrido

1. Explorar los siete grupos de proveedores y buscar un modelo por nombre o proveedor.
2. Abrir el diálogo para introducir la clave del proveedor antes de conectar un modelo.
3. Cambiar entre inglés y coreano; la interfaz admite 13 idiomas.
4. Explorar el registro de documentos y las opciones de división del texto.
5. Consultar los proveedores de embeddings, los almacenes vectoriales y los ajustes de búsqueda híbrida y reranking.

Este es un recorrido por la interfaz: no se envían claves API, no se indexan documentos ni se generan respuestas de los modelos.

## Probarlo en local

Desde la raíz del repositorio, con el SDK indicado en `global.json`:

```sh
dotnet run --project apps/Mythosia.AI.Samples.ChatUi
```

Abra la dirección que muestra la aplicación. Explore primero los modelos y los ajustes y, cuando desee realizar solicitudes, añada la clave de su proveedor.

Consulte la [guía del Playground](https://github.com/AJ-comp/Mythosia.AI/blob/main/apps/Mythosia.AI.Samples.ChatUi/README.md) para obtener detalles sobre las conexiones, los idiomas y el desarrollo local. Para las API correspondientes de la biblioteca, empiece por [Primeros pasos](getting-started.md) o la [Visión general del RAG](rag.md).
