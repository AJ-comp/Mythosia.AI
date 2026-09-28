# Gérer des serveurs de modèles existants

Un sélecteur de modèles ou un outil d’exploitation doit connaître l’état du serveur, les modèles disponibles et leur chargement avant d’envoyer une requête. Les paquets Serving unifient ces vérifications pour Ollama, llama.cpp et vLLM, tout en gardant les opérations propres à chaque moteur explicites.

Utilisez-les pour remplir un sélecteur de modèles, indiquer si un serveur est accessible, gérer la résidence des modèles lorsque le moteur le permet ou lire les métriques du moteur. Un changement de moteur peut laisser intact le code d’inspection commun de l’application.

Ces clients se connectent à un serveur HTTP existant. L’installation et l’hébergement du moteur, la location de GPU, le chat et la génération d’embeddings relèvent de composants distincts. Le chat continue via le service d’IA approprié, par exemple `QwenService` pour vLLM ; les fournisseurs d’embeddings RAG restent séparés. La découverte ne charge pas automatiquement les modèles. SGLang n’est pas implémenté.

## Choisir un paquet

| Paquet | Version | Utilisation |
| --- | --- | --- |
| `Mythosia.AI.Serving.Abstractions` | 1.0.0 | Contrats communs pour le code applicatif ou un adaptateur de gestion personnalisé. Aucune dépendance de paquet. |
| `Mythosia.AI.Serving.Ollama` | 1.0.0 | Inspecter Ollama, télécharger des modèles et les précharger ou décharger explicitement. |
| `Mythosia.AI.Serving.LlamaCpp` | 1.0.0 | Inspecter llama.cpp, lire les métriques et gérer les modèles en mode Router. |
| `Mythosia.AI.Serving.Vllm` | 1.1.0 | Inspecter vLLM et lire les métriques via les API communes ou les API vLLM spécifiques existantes. |

Les quatre paquets ciblent .NET Standard 2.1. Installez l’adaptateur utilisé ; il ajoute automatiquement le paquet d’abstractions. Les adaptateurs dépendent des contrats communs et de Newtonsoft.Json, indépendamment des paquets principaux d’IA et de RAG.

## Découvrir sans modifier l’état du serveur

Installez le paquet concret de votre moteur. Cet exemple utilise Ollama ; pour les autres serveurs, choisissez `VllmServer` ou `LlamaCppServer` dans leur espace de noms. La découverte utilise des requêtes en lecture seule, sans commande de chargement, de génération ou de téléchargement.

```bash
dotnet add package Mythosia.AI.Serving.Ollama --version 1.0.0
```

```csharp
using System;
using System.Net.Http;
using System.Threading;
using Mythosia.AI.Serving;
using Mythosia.AI.Serving.Ollama;

using var http = new HttpClient { Timeout = TimeSpan.FromMinutes(10) };
using var cancellation = new CancellationTokenSource();
IModelServer server = new OllamaServer("http://localhost:11434", http,
    apiKey: Environment.GetEnvironmentVariable("MODEL_SERVER_API_KEY"));

var health = await server.GetHealthAsync(cancellation.Token);
var info = await server.GetInfoAsync(cancellation.Token);
var capabilities = await server.GetCapabilitiesAsync(cancellation.Token);
var models = await server.GetModelsAsync(cancellation.Token);

foreach (var model in models)
    Console.WriteLine($"{model.Id}: {model.InstallationState} / {model.LoadState}");
```

Le point d’accès correspond à la racine du serveur, avec éventuellement un préfixe de chemin de proxy inverse. La clé API est facultative et envoyée comme identifiant Bearer à chaque requête. Le client ne modifie pas `HttpClient.DefaultRequestHeaders` et ne libère pas le `HttpClient` fourni ; réutilisez-le et libérez-le selon la durée de vie de votre application. Dans cet exemple Ollama, le délai d’expiration couvre aussi les corps de réponse en flux : prévoyez assez de temps pour télécharger un modèle.

## Contrats communs et facultatifs

| Contrat | Rôle |
| --- | --- |
| `IModelServer` | Informations, état, modèles et capacités observées du serveur. |
| `IModelLifecycle` | Commandes explicites de chargement et déchargement ; facultatif. |
| `IModelDownloader` | Téléchargement explicite avec progression ; facultatif. |
| `IModelMetricsProvider` | Échantillons de métriques avec leurs étiquettes ; facultatif. |

Une interface implémentée indique que le client possède l’opération ; `ServingCapabilities` rapporte ce qui peut être établi pour le point d’accès connecté. `Supported` ne garantit ni l’autorisation ni le succès pour chaque modèle. `Unsupported` signifie indisponible dans le mode ou le point d’accès observé. `Unknown` signifie que les preuves sont insuffisantes, notamment après une erreur d’authentification ou de connexion ; ce n’est pas une absence de prise en charge.

`InstallationState` et `LoadState` décrivent des observations distinctes. `Unknown` ne signifie ni absent ni déchargé. Les valeurs manquantes de `SizeBytes`, `MemoryBytes` ou `ContextLength` restent `null`, et non zéro. Un point d’accès de gestion sain ne prouve pas qu’un modèle précis est prêt pour l’inférence.

## Différences entre moteurs

| Opération | Ollama | llama.cpp à modèle unique | llama.cpp Router | vLLM |
| --- | --- | --- | --- | --- |
| Informations, état et liste de modèles | Oui | Oui | Oui | Oui |
| Chargement / déchargement explicites | Oui, avec des requêtes de génération vides | Non pris en charge | Oui, après confirmation du mode Router | Non pris en charge par ce client |
| Téléchargement de modèle | Oui, avec progression en flux | Non pris en charge | Opération explicite ; nécessite le point d’accès de téléchargement et les événements SSE | Non pris en charge par ce client |
| Métriques | Non implémentées | Métriques du serveur si activées | Surcharge concrète par modèle ; le modèle doit être déjà chargé | Métriques du serveur si disponibles |

Ce tableau décrit les opérations du client, sans promettre leur prise en charge par chaque version de serveur, jeu d’autorisations ou modèle. Vérifiez les capacités du point d’accès connecté et traitez les échecs des opérations.

**Ollama :** `/api/tags` fournit les modèles enregistrés et `/api/ps` les modèles en cours d’exécution. Un modèle distant peut être enregistré sans poids locaux ; sans processus local, son chargement reste inconnu. Le préchargement utilise une requête vide à `/api/generate` et la durée de maintien par défaut du serveur. Les modèles réservés aux embeddings ne sont pas redirigés vers une autre API. Le déchargement utilise `keep_alive: 0` sans supprimer de fichiers. Les métriques ne sont pas implémentées.

**llama.cpp :** `/props` doit confirmer explicitement le mode routeur avant les commandes de cycle de vie ou de téléchargement. Le mode à modèle unique ne les prend pas en charge ; l’état de veille observé est conservé. Le téléchargement routeur s’abonne à `/models/sse`, puis envoie `POST /models`, et ne réussit que sur l’événement `download_finished` du modèle ciblé. La seule disponibilité SSE laisse cette capacité inconnue. Les métriques globales concernent le mode à modèle unique ; celles du routeur exigent la surcharge concrète `GetMetricsAsync(modelId, token)`, qui envoie `autoload=false` pour éviter tout chargement à l’inspection.

**vLLM :** les alias servis et le champ facultatif `root` sont conservés, mais les états communs d’installation et de chargement restent inconnus. Les modèles et métriques sont vérifiés sur les réponses réelles ; cycle de vie et téléchargement ne sont pas pris en charge. Les méthodes et DTO existants de `VllmServer` restent disponibles sur le client concret ; les méthodes communes d’état, de modèles et de métriques passent par des interfaces explicites.


## Exécuter une opération de gestion explicite

Les téléchargements et changements de résidence consomment du réseau, du disque ou de la mémoire matérielle. Exécutez-les lorsque votre application en a besoin. La suite de l’exemple Ollama télécharge un petit modèle et le charge brièvement en mémoire pour observer son état. Utilisez les identifiants exacts du serveur, y compris le tag Ollama ou le tag de quantification llama.cpp.

```csharp
if (server is IModelDownloader downloader &&
    capabilities.ModelDownloading == ServingFeatureSupport.Supported)
{
    var progress = new Progress<ModelDownloadProgress>(p =>
        Console.WriteLine($"{p.ModelId}: {p.Stage} {p.CompletedBytes}/{p.TotalBytes}"));
    await downloader.DownloadModelAsync("qwen2.5:0.5b", progress, cancellation.Token);
}

if (server is IModelLifecycle lifecycle &&
    capabilities.ModelLoading == ServingFeatureSupport.Supported &&
    capabilities.ModelUnloading == ServingFeatureSupport.Supported)
{
    try
    {
        await lifecycle.LoadModelAsync("qwen2.5:0.5b", cancellation.Token);
        var afterLoad = await server.GetModelsAsync(cancellation.Token);
        foreach (var model in afterLoad)
            Console.WriteLine($"{model.Id}: {model.LoadState}");
    }
    finally
    {
        using var cleanup = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        await lifecycle.UnloadModelAsync("qwen2.5:0.5b", cleanup.Token);
    }
}
```

L’exemple utilise un modèle réservé aux tests et le décharge ensuite. En production, l’application décide quand libérer un modèle ; ne déchargez pas un modèle encore utilisé par d’autres requêtes. Le nettoyage a son propre délai et peut échouer si le serveur est indisponible.

La progression décrit un artefact ou une étape. Un compteur d’octets absent n’est ni zéro ni un pourcentage du modèle entier. Le succès d’un chargement confirme la commande, pas la disponibilité ni une résidence indéfinie ; observez `LoadState` avec une attente limitée lorsque la disponibilité importe. Consultez le guide du paquet pour le protocole de téléchargement du Router llama.cpp et ses limites de version. Une opération explicitement demandée peut être tentée avec une prise en charge `Unknown` après vérification de la configuration du serveur ; la découverte des capacités seule ne la lance jamais.

## Annulation et erreurs

Transmettez un jeton d’annulation aux requêtes et commandes. L’annulation arrête le travail HTTP et l’attente de ce client, sans garantir l’arrêt distant, le retour en arrière ou la suppression des couches téléchargées. Configurez le `HttpClient` fourni pour la durée de l’opération ; ces clients n’en deviennent pas propriétaires.

Conservez les étiquettes des métriques pour comparer modèles ou moteurs. Une métrique absente n’est pas zéro et les valeurs peuvent contenir `NaN` ou l’infini. `ServingException` est le type d’erreur commun ; les erreurs communes de gestion omettent les corps bruts et les identifiants. Les appels vLLM existants conservent leurs anciens détails d’erreur.

`GetHealthAsync` classe les échecs du point d’accès en états de santé et continue de propager l’annulation demandée par l’appelant. Les autres opérations peuvent lever `ServingException`, et un mode llama.cpp connu comme non pris en charge peut lever `NotSupportedException`. Ni un délai expiré ni une requête échouée ne prouvent l’annulation de l’action distante. Une méthode de téléchargement ne réussit qu’après confirmation de fin par le moteur : succès terminal suivi de EOF pour Ollama, ou événement `download_finished` correspondant pour llama.cpp Router.

## Ce qui a été vérifié

Les tests hors ligne couvrent des succès contrôlés, des réponses mal formées, les erreurs et l’annulation. Des vérifications distinctes sur serveurs réels ont utilisé une seule NVIDIA A40, de petits modèles Qwen publics et les versions suivantes des moteurs :

| Moteur | Modèle testé | Opérations de gestion vérifiées |
| --- | --- | --- |
| Ollama 0.34.4 | `qwen2.5:0.5b` | Découverte, nouveau téléchargement, chargement/déchargement, erreurs de modèle absent expurgées, annulation préalable et annulation après une progression partielle du téléchargement. |
| llama.cpp b11146, Router | `Qwen/Qwen2.5-0.5B-Instruct-GGUF:Q4_K_M` | Découverte, événements de téléchargement, chargement/déchargement, métriques par modèle sans chargement automatique, erreurs et annulation du téléchargement. |
| llama.cpp b11146, modèle unique | Le même modèle GGUF | Découverte, métriques du serveur, annulation et rejet explicite des commandes de cycle de vie Router. |
| vLLM 0.30.0 | `Qwen/Qwen2.5-0.5B-Instruct` | Découverte, métriques du serveur et annulation préalable. |

De courtes requêtes d’inférence HTTP natives ont aussi renvoyé du texte généré dans les quatre configurations. Elles confirment le fonctionnement du moteur, pas les adaptateurs de chat des services d’IA, la qualité du modèle, le débit ni la compatibilité avec toutes les versions du moteur. Les profils ci-dessus sont des configurations testées, pas des versions minimales prises en charge. Les vérifications d’annulation du téléchargement ont utilisé d’autres modèles de test plus volumineux, sans affirmer de retour en arrière distant. Un premier téléchargement Ollama a échoué ; une nouvelle tentative et un nouveau téléchargement après suppression du modèle ont réussi, sans établir la cause exacte du premier échec.

Utilisez le [guide de validation réelle à activer explicitement](https://github.com/AJ-comp/Mythosia.AI/blob/main/tests/Mythosia.AI.Serving.Live/README.md) pour vérifier votre point d’accès déployé. Il distingue l’exécuteur de gestion versionné dans le dépôt des sondes supplémentaires d’inférence et d’annulation utilisées pendant la vérification. Les rapports d’exécution détaillés restent hors de la documentation publiée.

## Guides des paquets

- [Mythosia.AI.Serving.Abstractions](../../src/serving/Mythosia.AI.Serving.Abstractions/README.md) — Contrats de gestion communs et instantanés immuables des serveurs, modèles et capacités.
- [Mythosia.AI.Serving.Ollama](../../src/serving/Mythosia.AI.Serving.Ollama/README.md) — Inventaire et état Ollama, préchargement/déchargement explicites et téléchargements en flux.
- [Mythosia.AI.Serving.LlamaCpp](../../src/serving/Mythosia.AI.Serving.LlamaCpp/README.md) — Inspection llama.cpp, commandes routeur vérifiées et métriques sans chargement automatique.
- [Mythosia.AI.Serving.Vllm](../../src/serving/Mythosia.AI.Serving.Vllm/README.md) — Fiches de modèles, état, version et métriques étiquetées vLLM ; API concrète conservée.
