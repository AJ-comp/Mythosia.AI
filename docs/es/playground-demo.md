# Explorar el Playground

Utilice el Playground para comparar las opciones de los modelos y configurar la búsqueda de documentos antes de escribir el código de su aplicación. Este recorrido muestra la interfaz actual en un entorno de trabajo local, desde la búsqueda de un modelo hasta la exploración de los ajustes del pipeline RAG.

<video controls playsinline preload="metadata" poster="../assets/playground-demo.png" aria-label="Recorrido por la interfaz del Playground de Mythosia.AI" style="display: block; width: 100%; height: auto; border-radius: 12px;">
  <source src="../assets/playground-demo.mp4" type="video/mp4">
  <track kind="captions" src="../assets/playground-demo.vtt" srclang="en" label="English">
  Su navegador no admite vídeos integrados. <a href="../assets/playground-demo.mp4">Descargar el recorrido</a>.
</video>

[Descargar el vídeo (MP4)](../assets/playground-demo.mp4) · [Leer los subtítulos](../assets/playground-demo.vtt)

La grabación no tiene narración. Las descripciones de los pasos aparecen en inglés debajo de la aplicación. El reproductor también ofrece una pista de subtítulos independiente.

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
