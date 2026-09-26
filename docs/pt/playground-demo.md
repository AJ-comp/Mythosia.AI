# Explorar o Playground

Use o Playground para comparar as opções dos modelos e configurar a busca de documentos antes de escrever o código da sua aplicação. Este vídeo mostra a interface atual em um ambiente de trabalho local, desde a busca de um modelo até a exploração das configurações do pipeline RAG.

<video class="playground-video" controls playsinline preload="metadata" poster="../assets/playground-demo.png" aria-label="Demonstração da interface do Playground Mythosia.AI">
  <source src="../assets/playground-demo.mp4?v=4" type="video/mp4">
  <track kind="subtitles" src="../assets/playground-demo.vtt?v=4" srclang="en" label="English">
  <track kind="subtitles" src="../assets/playground-demo.ko.vtt?v=4" srclang="ko" label="한국어">
  <track kind="subtitles" src="../assets/playground-demo.ja.vtt?v=4" srclang="ja" label="日本語">
  <track kind="subtitles" src="../assets/playground-demo.zh-Hans.vtt?v=4" srclang="zh-Hans" label="简体中文">
  <track kind="subtitles" src="../assets/playground-demo.zh-Hant.vtt?v=4" srclang="zh-Hant" label="繁體中文">
  <track kind="subtitles" src="../assets/playground-demo.de.vtt?v=4" srclang="de" label="Deutsch">
  <track kind="subtitles" src="../assets/playground-demo.fr.vtt?v=4" srclang="fr" label="Français">
  <track kind="subtitles" src="../assets/playground-demo.es.vtt?v=4" srclang="es" label="Español">
  <track kind="subtitles" src="../assets/playground-demo.pt.vtt?v=4" srclang="pt" label="Português" default>
  <track kind="subtitles" src="../assets/playground-demo.ru.vtt?v=4" srclang="ru" label="Русский">
  <track kind="subtitles" src="../assets/playground-demo.uk.vtt?v=4" srclang="uk" label="Українська">
  <track kind="subtitles" src="../assets/playground-demo.vi.vtt?v=4" srclang="vi" label="Tiếng Việt">
  <track kind="subtitles" src="../assets/playground-demo.th.vtt?v=4" srclang="th" label="ไทย">
  Seu navegador não oferece suporte a vídeos incorporados. <a href="../assets/playground-demo.mp4?v=4">Baixar a demonstração</a>.
</video>

[Baixar o vídeo (MP4)](../assets/playground-demo.mp4?v=4) · [Ler as legendas](../assets/playground-demo.pt.vtt?v=4)

O vídeo não tem som. As legendas aparecem automaticamente no idioma desta página. No menu do player, você pode mudar o idioma delas ou desativá-las.

## O que o vídeo mostra

1. Navegar pelos sete grupos de provedores e buscar um modelo por nome ou provedor.
2. Abrir a caixa de diálogo da chave do provedor antes de conectar um modelo.
3. Alternar entre inglês e coreano; a interface oferece suporte a 13 idiomas.
4. Explorar o registro de documentos e as opções de divisão de texto.
5. Consultar os provedores de embeddings, os armazenamentos vetoriais e as configurações de busca híbrida e reranking.

Esta é uma demonstração da interface: não são enviadas chaves de API, não são indexados documentos e não são geradas respostas dos modelos.

## Experimentar localmente

Na raiz do repositório, com o SDK especificado em `global.json`:

```sh
dotnet run --project apps/Mythosia.AI.Samples.ChatUi
```

Abra o endereço exibido pela aplicação. Explore primeiro os modelos e as configurações e adicione a chave do seu provedor quando quiser fazer solicitações.

Consulte o [guia do Playground](https://github.com/AJ-comp/Mythosia.AI/blob/main/apps/Mythosia.AI.Samples.ChatUi/README.md) para obter detalhes sobre conexões, idiomas e desenvolvimento local. Para as APIs correspondentes da biblioteca, comece por [Primeiros passos](getting-started.md) ou pela [Visão geral do RAG](rag.md).
