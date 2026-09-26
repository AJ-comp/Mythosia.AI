# 探索 Playground

使用 Playground，可以在编写应用程序代码之前比较模型选项并配置文档检索。本视频展示了本地工作区中的当前界面，带你从查找模型开始，逐步了解 RAG 管道设置。

<video controls playsinline preload="metadata" poster="../assets/playground-demo.png" aria-label="Mythosia.AI Playground 界面操作指南" style="display: block; width: 100%; height: auto; border-radius: 12px;">
  <source src="../assets/playground-demo.mp4" type="video/mp4">
  <track kind="captions" src="../assets/playground-demo.vtt" srclang="en" label="English">
  你的浏览器不支持嵌入式视频。<a href="../assets/playground-demo.mp4">下载操作指南视频</a>。
</video>

[下载视频 (MP4)](../assets/playground-demo.mp4) · [阅读英文字幕](../assets/playground-demo.vtt)

本视频没有旁白。应用界面下方会显示英文步骤说明，播放器还提供独立的英文字幕轨道。

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
