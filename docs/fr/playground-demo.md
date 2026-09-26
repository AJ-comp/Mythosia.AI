# Découvrir le Playground

Utilisez le Playground pour comparer les options des modèles et configurer la recherche de documents avant d'écrire le code de votre application. Cette vidéo présente l'interface actuelle dans un espace de travail local, de la recherche d'un modèle à l'exploration des paramètres du pipeline RAG.

<video controls playsinline preload="metadata" poster="../assets/playground-demo.png" aria-label="Visite de l'interface du Playground Mythosia.AI" style="display: block; width: 100%; height: auto; border-radius: 12px;">
  <source src="../assets/playground-demo.mp4" type="video/mp4">
  <track kind="captions" src="../assets/playground-demo.vtt" srclang="en" label="English">
  Votre navigateur ne prend pas en charge les vidéos intégrées. <a href="../assets/playground-demo.mp4">Télécharger la vidéo</a>.
</video>

[Télécharger la vidéo (MP4)](../assets/playground-demo.mp4) · [Lire les sous-titres](../assets/playground-demo.vtt)

L'enregistrement ne comporte pas de narration. Des descriptions des étapes en anglais s'affichent sous l'application. Une piste de sous-titres distincte est également disponible dans le lecteur.

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
