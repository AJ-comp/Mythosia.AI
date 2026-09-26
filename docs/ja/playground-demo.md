# Playground を使ってみる

Playground では、アプリケーションのコードを書く前にモデルのオプションを比較し、ドキュメント検索を設定できます。この動画では、ローカルの作業環境で現在の画面を操作し、モデルの検索から RAG パイプラインの設定までを紹介します。

<video controls playsinline preload="metadata" poster="../assets/playground-demo.png" aria-label="Mythosia.AI Playground の画面操作ガイド" style="display: block; width: 100%; height: auto; border-radius: 12px;">
  <source src="../assets/playground-demo.mp4" type="video/mp4">
  <track kind="captions" src="../assets/playground-demo.vtt" srclang="en" label="English">
  このブラウザーは埋め込み動画に対応していません。<a href="../assets/playground-demo.mp4">操作ガイドをダウンロード</a>してください。
</video>

[動画をダウンロード (MP4)](../assets/playground-demo.mp4) · [英語字幕を読む](../assets/playground-demo.vtt)

この動画にナレーションはありません。アプリケーション画面の下に英語で手順が表示されます。プレーヤーでは、別途用意された英語字幕も選択できます。

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
