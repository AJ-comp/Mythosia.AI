# Знакомство с Playground

В Playground можно сравнить параметры моделей и настроить поиск по документам до написания кода приложения. В этом видео показан текущий интерфейс в локальной рабочей среде: от поиска модели до настроек конвейера RAG.

<video class="playground-video" controls playsinline preload="metadata" poster="../assets/playground-demo.png" aria-label="Обзор интерфейса Mythosia.AI Playground">
  <source src="../assets/playground-demo.mp4?v=6" type="video/mp4">
  <track kind="subtitles" src="../assets/playground-demo.vtt?v=6" srclang="en" label="English">
  <track kind="subtitles" src="../assets/playground-demo.ko.vtt?v=6" srclang="ko" label="한국어">
  <track kind="subtitles" src="../assets/playground-demo.ja.vtt?v=6" srclang="ja" label="日本語">
  <track kind="subtitles" src="../assets/playground-demo.zh-Hans.vtt?v=6" srclang="zh-Hans" label="简体中文">
  <track kind="subtitles" src="../assets/playground-demo.zh-Hant.vtt?v=6" srclang="zh-Hant" label="繁體中文">
  <track kind="subtitles" src="../assets/playground-demo.de.vtt?v=6" srclang="de" label="Deutsch">
  <track kind="subtitles" src="../assets/playground-demo.fr.vtt?v=6" srclang="fr" label="Français">
  <track kind="subtitles" src="../assets/playground-demo.es.vtt?v=6" srclang="es" label="Español">
  <track kind="subtitles" src="../assets/playground-demo.pt.vtt?v=6" srclang="pt" label="Português">
  <track kind="subtitles" src="../assets/playground-demo.ru.vtt?v=6" srclang="ru" label="Русский" default>
  <track kind="subtitles" src="../assets/playground-demo.uk.vtt?v=6" srclang="uk" label="Українська">
  <track kind="subtitles" src="../assets/playground-demo.vi.vtt?v=6" srclang="vi" label="Tiếng Việt">
  <track kind="subtitles" src="../assets/playground-demo.th.vtt?v=6" srclang="th" label="ไทย">
  Ваш браузер не поддерживает встроенное видео. <a href="../assets/playground-demo.mp4?v=6">Скачать видео</a>.
</video>

[Скачать видео (MP4)](../assets/playground-demo.mp4?v=6) · [Прочитать субтитры](../assets/playground-demo.ru.vtt?v=6)

Видео без звука. Субтитры автоматически отображаются на языке страницы; в меню плеера можно выбрать другой язык или отключить их.

## Что показано в видео

1. Просмотр семи групп поставщиков и поиск модели по названию или поставщику.
2. Открытие диалога ввода ключа поставщика перед подключением модели.
3. Переключение между английским и корейским языками; интерфейс поддерживает 13 языков.
4. Просмотр настроек регистрации документов и разделения текста.
5. Просмотр поставщиков эмбеддингов, векторных хранилищ, гибридного поиска и настроек переранжирования.

Это обзор интерфейса: в записи не отправляются API-ключи, не индексируются документы и не генерируются ответы модели.

## Запуск на своём компьютере

В корневой папке репозитория, используя SDK, указанный в `global.json`, выполните:

```sh
dotnet run --project apps/Mythosia.AI.Samples.ChatUi
```

Откройте адрес, выведенный приложением. Сначала изучите модели и настройки, а когда будете готовы отправлять запросы, добавьте ключ поставщика.

Сведения о подключении, языках и локальной разработке приведены в [руководстве по Playground](https://github.com/AJ-comp/Mythosia.AI/blob/main/apps/Mythosia.AI.Samples.ChatUi/README.md). Чтобы ознакомиться с соответствующими API библиотеки, начните с разделов [Быстрый старт](getting-started.md) или [Основы RAG](rag.md).
