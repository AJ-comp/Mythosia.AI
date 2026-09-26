# Playground を使ってみる

Playground では、アプリケーションのコードを書く前にモデルのオプションを比較し、ドキュメント検索を設定できます。この動画では、ローカルの作業環境で現在の画面を操作し、モデルの検索から RAG パイプラインの設定までを紹介します。

<video class="playground-video" controls playsinline preload="metadata" poster="../assets/playground-demo.png" aria-label="Mythosia.AI Playground の画面操作ガイド">
  <source src="../assets/playground-demo.mp4?v=3" type="video/mp4">
  <track kind="subtitles" src="../assets/playground-demo.vtt?v=3" srclang="en" label="English">
  <track kind="subtitles" src="../assets/playground-demo.ko.vtt?v=3" srclang="ko" label="한국어">
  <track kind="subtitles" src="../assets/playground-demo.ja.vtt?v=3" srclang="ja" label="日本語" default>
  <track kind="subtitles" src="../assets/playground-demo.zh-Hans.vtt?v=3" srclang="zh-Hans" label="简体中文">
  <track kind="subtitles" src="../assets/playground-demo.zh-Hant.vtt?v=3" srclang="zh-Hant" label="繁體中文">
  <track kind="subtitles" src="../assets/playground-demo.de.vtt?v=3" srclang="de" label="Deutsch">
  <track kind="subtitles" src="../assets/playground-demo.fr.vtt?v=3" srclang="fr" label="Français">
  <track kind="subtitles" src="../assets/playground-demo.es.vtt?v=3" srclang="es" label="Español">
  <track kind="subtitles" src="../assets/playground-demo.pt.vtt?v=3" srclang="pt" label="Português">
  <track kind="subtitles" src="../assets/playground-demo.ru.vtt?v=3" srclang="ru" label="Русский">
  <track kind="subtitles" src="../assets/playground-demo.uk.vtt?v=3" srclang="uk" label="Українська">
  <track kind="subtitles" src="../assets/playground-demo.vi.vtt?v=3" srclang="vi" label="Tiếng Việt">
  <track kind="subtitles" src="../assets/playground-demo.th.vtt?v=3" srclang="th" label="ไทย">
  このブラウザーは埋め込み動画に対応していません。<a href="../assets/playground-demo.mp4?v=3">操作ガイドをダウンロード</a>してください。
</video>

[動画をダウンロード (MP4)](../assets/playground-demo.mp4?v=3) · [字幕を読む](../assets/playground-demo.ja.vtt?v=3)

この動画に音声はありません。ページの言語に合わせた字幕が自動で表示されます。プレーヤーのメニューで字幕の言語を変更したり、字幕をオフにしたりできます。

## 動画で紹介する操作

1. 7 つのプロバイダーグループを参照し、モデル名やプロバイダーで検索します。
2. モデルに接続する前に、プロバイダーキーの入力ダイアログを開きます。
3. 英語と韓国語を切り替えます。インターフェイスは 13 言語に対応しています。
4. ドキュメント登録とテキスト分割のオプションを確認します。
5. 埋め込みプロバイダー、ベクターストア、ハイブリッド検索、再ランキングの設定を確認します。

この動画は画面操作のガイドです。API キーの送信、ドキュメントのインデックス作成、モデル応答の生成は行いません。

## ローカルで試す

`global.json` で指定された SDK を使用し、リポジトリのルートから実行します。

```sh
dotnet run --project apps/Mythosia.AI.Samples.ChatUi
```

アプリケーションが表示したアドレスを開きます。まずモデルと設定を確認し、リクエストを送る準備ができたらプロバイダーキーを追加してください。

接続、言語、ローカル開発の詳細については、[Playground ガイド](https://github.com/AJ-comp/Mythosia.AI/blob/main/apps/Mythosia.AI.Samples.ChatUi/README.md)を参照してください。対応するライブラリ API については、[クイックスタート](getting-started.md)または [RAG 基礎](rag.md)から始めてください。
