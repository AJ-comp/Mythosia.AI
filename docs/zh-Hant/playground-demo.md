# 探索 Playground

使用 Playground，可以在撰寫應用程式程式碼之前比較模型選項並設定文件檢索。本影片展示本機工作區中的目前介面，帶你從尋找模型開始，逐步了解 RAG 管線設定。

<video controls playsinline preload="metadata" poster="../assets/playground-demo.png" aria-label="Mythosia.AI Playground 介面操作指南" style="display: block; width: 100%; height: auto; border-radius: 12px;">
  <source src="../assets/playground-demo.mp4" type="video/mp4">
  <track kind="captions" src="../assets/playground-demo.vtt" srclang="en" label="English">
  你的瀏覽器不支援嵌入式影片。<a href="../assets/playground-demo.mp4">下載操作指南影片</a>。
</video>

[下載影片 (MP4)](../assets/playground-demo.mp4) · [閱讀英文字幕](../assets/playground-demo.vtt)

本影片沒有旁白。應用程式介面下方會顯示英文步驟說明，播放器也提供獨立的英文字幕軌。

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
