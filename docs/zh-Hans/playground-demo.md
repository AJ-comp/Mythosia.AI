# 探索 Playground

使用 Playground，可以在编写应用程序代码之前比较模型选项并配置文档检索。本视频展示了本地工作区中的当前界面，带你从查找模型开始，逐步了解 RAG 管道设置。

<video class="playground-video" controls playsinline preload="metadata" poster="../assets/playground-demo.png" aria-label="Mythosia.AI Playground 界面操作指南">
  <source src="../assets/playground-demo.mp4?v=4" type="video/mp4">
  <track kind="subtitles" src="../assets/playground-demo.vtt?v=4" srclang="en" label="English">
  <track kind="subtitles" src="../assets/playground-demo.ko.vtt?v=4" srclang="ko" label="한국어">
  <track kind="subtitles" src="../assets/playground-demo.ja.vtt?v=4" srclang="ja" label="日本語">
  <track kind="subtitles" src="../assets/playground-demo.zh-Hans.vtt?v=4" srclang="zh-Hans" label="简体中文" default>
  <track kind="subtitles" src="../assets/playground-demo.zh-Hant.vtt?v=4" srclang="zh-Hant" label="繁體中文">
  <track kind="subtitles" src="../assets/playground-demo.de.vtt?v=4" srclang="de" label="Deutsch">
  <track kind="subtitles" src="../assets/playground-demo.fr.vtt?v=4" srclang="fr" label="Français">
  <track kind="subtitles" src="../assets/playground-demo.es.vtt?v=4" srclang="es" label="Español">
  <track kind="subtitles" src="../assets/playground-demo.pt.vtt?v=4" srclang="pt" label="Português">
  <track kind="subtitles" src="../assets/playground-demo.ru.vtt?v=4" srclang="ru" label="Русский">
  <track kind="subtitles" src="../assets/playground-demo.uk.vtt?v=4" srclang="uk" label="Українська">
  <track kind="subtitles" src="../assets/playground-demo.vi.vtt?v=4" srclang="vi" label="Tiếng Việt">
  <track kind="subtitles" src="../assets/playground-demo.th.vtt?v=4" srclang="th" label="ไทย">
  你的浏览器不支持嵌入式视频。<a href="../assets/playground-demo.mp4?v=4">下载操作指南视频</a>。
</video>

[下载视频 (MP4)](../assets/playground-demo.mp4?v=4) · [阅读字幕](../assets/playground-demo.zh-Hans.vtt?v=4)

本视频没有声音，字幕会自动使用页面语言。你可以在播放器菜单中切换字幕语言或关闭字幕。

## 视频展示的内容

1. 浏览七个提供商分组，并按模型名称或提供商搜索。
2. 在连接模型之前，打开提供商密钥输入对话框。
3. 在英语和韩语之间切换；界面支持 13 种语言。
4. 了解文档注册和文本分割选项。
5. 查看嵌入提供商、向量存储、混合检索和重排序设置。

这是界面操作指南：视频中不会提交 API 密钥、为文档建立索引或生成模型响应。

## 在本地试用

使用 `global.json` 指定的 SDK，在仓库根目录运行：

```sh
dotnet run --project apps/Mythosia.AI.Samples.ChatUi
```

打开应用程序输出的地址。先浏览模型和设置，准备好发送请求后，再添加提供商密钥。

有关连接、语言和本地开发的详细信息，请参阅 [Playground 指南](https://github.com/AJ-comp/Mythosia.AI/blob/main/apps/Mythosia.AI.Samples.ChatUi/README.md)。要了解对应的库 API，请从[开始使用](getting-started.md)或 [RAG 基础](rag.md)开始。
