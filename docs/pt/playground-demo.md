# Explorar o Playground

Use o Playground para comparar as opções dos modelos e configurar a busca de documentos antes de escrever o código da sua aplicação. Este vídeo mostra a interface atual em um ambiente de trabalho local, desde a busca de um modelo até a exploração das configurações do pipeline RAG.

<video controls playsinline preload="metadata" poster="../assets/playground-demo.png" aria-label="Demonstração da interface do Playground Mythosia.AI" style="display: block; width: 100%; height: auto; border-radius: 12px;">
  <source src="../assets/playground-demo.mp4" type="video/mp4">
  <track kind="captions" src="../assets/playground-demo.vtt" srclang="en" label="English">
  Seu navegador não oferece suporte a vídeos incorporados. <a href="../assets/playground-demo.mp4">Baixar a demonstração</a>.
</video>

[Baixar o vídeo (MP4)](../assets/playground-demo.mp4) · [Ler as legendas](../assets/playground-demo.vtt)

A gravação não tem narração. As descrições das etapas aparecem em inglês abaixo da aplicação. O player também oferece uma faixa de legendas separada.

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
