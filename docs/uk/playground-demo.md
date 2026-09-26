# Знайомство з Playground

У Playground можна порівняти параметри моделей і налаштувати пошук у документах до написання коду застосунку. У цьому відео показано поточний інтерфейс у локальному робочому середовищі: від пошуку моделі до налаштувань конвеєра RAG.

<video class="playground-video" controls playsinline preload="metadata" poster="../assets/playground-demo.png" aria-label="Огляд інтерфейсу Mythosia.AI Playground">
  <source src="../assets/playground-demo.mp4?v=3" type="video/mp4">
  <track kind="subtitles" src="../assets/playground-demo.vtt?v=3" srclang="en" label="English">
  <track kind="subtitles" src="../assets/playground-demo.ko.vtt?v=3" srclang="ko" label="한국어">
  <track kind="subtitles" src="../assets/playground-demo.ja.vtt?v=3" srclang="ja" label="日本語">
  <track kind="subtitles" src="../assets/playground-demo.zh-Hans.vtt?v=3" srclang="zh-Hans" label="简体中文">
  <track kind="subtitles" src="../assets/playground-demo.zh-Hant.vtt?v=3" srclang="zh-Hant" label="繁體中文">
  <track kind="subtitles" src="../assets/playground-demo.de.vtt?v=3" srclang="de" label="Deutsch">
  <track kind="subtitles" src="../assets/playground-demo.fr.vtt?v=3" srclang="fr" label="Français">
  <track kind="subtitles" src="../assets/playground-demo.es.vtt?v=3" srclang="es" label="Español">
  <track kind="subtitles" src="../assets/playground-demo.pt.vtt?v=3" srclang="pt" label="Português">
  <track kind="subtitles" src="../assets/playground-demo.ru.vtt?v=3" srclang="ru" label="Русский">
  <track kind="subtitles" src="../assets/playground-demo.uk.vtt?v=3" srclang="uk" label="Українська" default>
  <track kind="subtitles" src="../assets/playground-demo.vi.vtt?v=3" srclang="vi" label="Tiếng Việt">
  <track kind="subtitles" src="../assets/playground-demo.th.vtt?v=3" srclang="th" label="ไทย">
  Ваш браузер не підтримує вбудоване відео. <a href="../assets/playground-demo.mp4?v=3">Завантажити відео</a>.
</video>

[Завантажити відео (MP4)](../assets/playground-demo.mp4?v=3) · [Прочитати субтитри](../assets/playground-demo.uk.vtt?v=3)

Відео без звуку. Субтитри автоматично відображаються мовою сторінки; у меню програвача можна вибрати іншу мову або вимкнути їх.

## Що показано у відео

1. Перегляд семи груп постачальників і пошук моделі за назвою або постачальником.
2. Відкриття діалогового вікна введення ключа постачальника перед підключенням моделі.
3. Перемикання між англійською та корейською мовами; інтерфейс підтримує 13 мов.
4. Перегляд налаштувань реєстрації документів і розділення тексту.
5. Перегляд постачальників ембедингів, векторних сховищ, гібридного пошуку та налаштувань повторного ранжування.

Це огляд інтерфейсу: у записі не надсилаються API-ключі, не індексуються документи та не генеруються відповіді моделі.

## Запуск на своєму комп’ютері

У кореневій папці репозиторію, використовуючи SDK, зазначений у `global.json`, виконайте:

```sh
dotnet run --project apps/Mythosia.AI.Samples.ChatUi
```

Відкрийте адресу, виведену застосунком. Спочатку ознайомтеся з моделями й налаштуваннями, а коли будете готові надсилати запити, додайте ключ постачальника.

Відомості про підключення, мови та локальну розробку наведено в [посібнику з Playground](https://github.com/AJ-comp/Mythosia.AI/blob/main/apps/Mythosia.AI.Samples.ChatUi/README.md). Щоб ознайомитися з відповідними API бібліотеки, почніть із розділів [Швидкий старт](getting-started.md) або [Основи RAG](rag.md).
