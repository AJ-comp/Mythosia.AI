# Découvrir le Playground

Utilisez le Playground pour comparer les options des modèles et configurer la recherche de documents avant d'écrire le code de votre application. Cette vidéo présente l'interface actuelle dans un espace de travail local, de la recherche d'un modèle à l'exploration des paramètres du pipeline RAG.

<video class="playground-video" controls playsinline preload="metadata" poster="../assets/playground-demo.png" aria-label="Visite de l'interface du Playground Mythosia.AI">
  <source src="../assets/playground-demo.mp4?v=6" type="video/mp4">
  <track kind="subtitles" src="../assets/playground-demo.vtt?v=6" srclang="en" label="English">
  <track kind="subtitles" src="../assets/playground-demo.ko.vtt?v=6" srclang="ko" label="한국어">
  <track kind="subtitles" src="../assets/playground-demo.ja.vtt?v=6" srclang="ja" label="日本語">
  <track kind="subtitles" src="../assets/playground-demo.zh-Hans.vtt?v=6" srclang="zh-Hans" label="简体中文">
  <track kind="subtitles" src="../assets/playground-demo.zh-Hant.vtt?v=6" srclang="zh-Hant" label="繁體中文">
  <track kind="subtitles" src="../assets/playground-demo.de.vtt?v=6" srclang="de" label="Deutsch">
  <track kind="subtitles" src="../assets/playground-demo.fr.vtt?v=6" srclang="fr" label="Français" default>
  <track kind="subtitles" src="../assets/playground-demo.es.vtt?v=6" srclang="es" label="Español">
  <track kind="subtitles" src="../assets/playground-demo.pt.vtt?v=6" srclang="pt" label="Português">
  <track kind="subtitles" src="../assets/playground-demo.ru.vtt?v=6" srclang="ru" label="Русский">
  <track kind="subtitles" src="../assets/playground-demo.uk.vtt?v=6" srclang="uk" label="Українська">
  <track kind="subtitles" src="../assets/playground-demo.vi.vtt?v=6" srclang="vi" label="Tiếng Việt">
  <track kind="subtitles" src="../assets/playground-demo.th.vtt?v=6" srclang="th" label="ไทย">
  Votre navigateur ne prend pas en charge les vidéos intégrées. <a href="../assets/playground-demo.mp4?v=6">Télécharger la vidéo</a>.
</video>

[Télécharger la vidéo (MP4)](../assets/playground-demo.mp4?v=6) · [Lire les sous-titres](../assets/playground-demo.fr.vtt?v=6)

La vidéo est sans son. Les sous-titres s'affichent automatiquement dans la langue de cette page. Le menu du lecteur permet de changer leur langue ou de les désactiver.

## Ce que montre la vidéo

1. Parcourir les sept groupes de fournisseurs et rechercher un modèle par nom ou par fournisseur.
2. Ouvrir la boîte de dialogue de saisie de la clé du fournisseur avant de connecter un modèle.
3. Passer de l'anglais au coréen et inversement ; l'interface prend en charge 13 langues.
4. Explorer l'ajout de documents et les options de découpage du texte.
5. Examiner les fournisseurs d'embeddings, les bases vectorielles et les paramètres de recherche hybride et de reranking.

Cette vidéo présente l'interface : elle ne transmet aucune clé API, n'indexe aucun document et ne génère aucune réponse de modèle.

## Essayer en local

Depuis la racine du dépôt, avec le SDK indiqué dans `global.json` :

```sh
dotnet run --project apps/Mythosia.AI.Samples.ChatUi
```

Ouvrez l'adresse affichée par l'application. Explorez d'abord les modèles et les paramètres, puis ajoutez la clé de votre fournisseur lorsque vous souhaitez envoyer des requêtes.

Consultez le [guide du Playground](https://github.com/AJ-comp/Mythosia.AI/blob/main/apps/Mythosia.AI.Samples.ChatUi/README.md) pour les détails sur les connexions, les langues et le développement local. Pour les API correspondantes de la bibliothèque, commencez par la [prise en main](getting-started.md) ou les [bases du RAG](rag.md).
