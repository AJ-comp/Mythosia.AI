# 探索 Playground

使用 Playground，可以在撰寫應用程式程式碼之前比較模型選項並設定文件檢索。本影片展示本機工作區中的目前介面，帶你從尋找模型開始，逐步了解 RAG 管線設定。

<video class="playground-video" controls playsinline preload="metadata" poster="../assets/playground-demo.png" aria-label="Mythosia.AI Playground 介面操作指南">
  <source src="../assets/playground-demo.mp4?v=6" type="video/mp4">
  <track kind="subtitles" src="../assets/playground-demo.vtt?v=6" srclang="en" label="English">
  <track kind="subtitles" src="../assets/playground-demo.ko.vtt?v=6" srclang="ko" label="한국어">
  <track kind="subtitles" src="../assets/playground-demo.ja.vtt?v=6" srclang="ja" label="日本語">
  <track kind="subtitles" src="../assets/playground-demo.zh-Hans.vtt?v=6" srclang="zh-Hans" label="简体中文">
  <track kind="subtitles" src="../assets/playground-demo.zh-Hant.vtt?v=6" srclang="zh-Hant" label="繁體中文" default>
  <track kind="subtitles" src="../assets/playground-demo.de.vtt?v=6" srclang="de" label="Deutsch">
  <track kind="subtitles" src="../assets/playground-demo.fr.vtt?v=6" srclang="fr" label="Français">
  <track kind="subtitles" src="../assets/playground-demo.es.vtt?v=6" srclang="es" label="Español">
  <track kind="subtitles" src="../assets/playground-demo.pt.vtt?v=6" srclang="pt" label="Português">
  <track kind="subtitles" src="../assets/playground-demo.ru.vtt?v=6" srclang="ru" label="Русский">
  <track kind="subtitles" src="../assets/playground-demo.uk.vtt?v=6" srclang="uk" label="Українська">
  <track kind="subtitles" src="../assets/playground-demo.vi.vtt?v=6" srclang="vi" label="Tiếng Việt">
  <track kind="subtitles" src="../assets/playground-demo.th.vtt?v=6" srclang="th" label="ไทย">
  你的瀏覽器不支援嵌入式影片。<a href="../assets/playground-demo.mp4?v=6">下載操作指南影片</a>。
</video>

本影片沒有聲音，字幕會自動使用頁面語言。你可以在播放器選單中切換字幕語言或關閉字幕。

## 影片展示的內容

1. 瀏覽七個供應商群組，並依模型名稱或供應商搜尋。
2. 在連接模型之前，開啟供應商金鑰輸入對話方塊。
3. 在英語和韓語之間切換；介面支援 13 種語言。
4. 了解文件登錄和文字分割選項。
5. 查看嵌入供應商、向量儲存、混合檢索和重排序設定。

這是介面操作指南：影片中不會提交 API 金鑰、為文件建立索引或產生模型回應。

## 在本機試用

使用 `global.json` 指定的 SDK，在儲存庫根目錄執行：

```sh
dotnet run --project apps/Mythosia.AI.Samples.ChatUi
```

開啟應用程式輸出的位址。先瀏覽模型和設定，準備好傳送請求後，再新增供應商金鑰。

有關連線、語言和本機開發的詳細資訊，請參閱 [Playground 指南](https://github.com/AJ-comp/Mythosia.AI/blob/main/apps/Mythosia.AI.Samples.ChatUi/README.md)。若要了解對應的程式庫 API，請從[開始使用](getting-started.md)或 [RAG 基礎](rag.md)開始。
