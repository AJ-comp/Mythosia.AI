# Concepts fondamentaux

Cette page rassemble les concepts de base qui sont référencés dans tout le reste de la documentation. D'autres concepts seront ajoutés au fil du temps.

## Qu'est-ce qu'un round ?

> [!NOTE]
> Un **round** est un aller-retour complet entre ton application et le modèle — ton app envoie un prompt, le modèle répond, et cet échange constitue un round. Un simple message de chat correspond à 1 round. Le function calling et les agents peuvent enchaîner plusieurs rounds pour un seul message utilisateur.

### Le cas le plus simple : 1 round

Pour un message de chat normal, toute la conversation tient en un round.

```
app  →  "Combien font 2 + 2 ?"  →  modèle
app  ←  "4."                     ←  modèle
```

`RoundUsage` se déclenche une fois avec les tokens de ce round. `Completion.Usage` se déclenche à la fin du stream avec le même total, puisqu'il n'y a qu'un seul round.

### Plusieurs rounds : function calling

Les rounds se multiplient quand le modèle ne peut pas répondre seul. Imaginons qu'un utilisateur demande *« Quel temps fait-il à Paris en ce moment ? »* — le modèle n'a pas accès à la météo en temps réel, il doit donc appeler un outil.

**Round 1 — le modèle décide d'appeler un outil**

Ton app envoie le message utilisateur ainsi que la liste des outils enregistrés (par exemple `GetWeather`) au modèle. Le modèle voit la conversation suivante :

```
system: Tu es un assistant météo. Tu peux appeler GetWeather(city).
user:   Quel temps fait-il à Paris en ce moment ?
```

Au lieu d'écrire une réponse finale, le modèle retourne une **demande d'appel d'outil** :

```
tool_call: GetWeather(city="Paris")
```

Le tour du modèle se termine, et le round 1 aussi. `RoundUsage` se déclenche avec les tokens consommés pendant le round 1. **Il n'y a pas encore de réponse finale pour l'utilisateur.**

**Entre les rounds — ton app exécute la fonction**

Cette étape **n'est pas** un appel LLM. Le runtime de Mythosia.AI invoque ton implémentation de `GetWeather` et reçoit `« 15°C, nuageux »`. Aucun token n'est consommé.

**Round 2 — le modèle rédige la réponse finale**

Ton app ajoute le résultat de l'outil à la conversation et appelle le modèle **une seconde fois**. Le modèle voit maintenant :

```
system:      Tu es un assistant météo. Tu peux appeler GetWeather(city).
user:        Quel temps fait-il à Paris en ce moment ?
assistant:   [a appelé GetWeather(city="Paris")]
tool_result: 15°C, nuageux
```

Avec les informations nécessaires, le modèle écrit du texte :

```
Il fait actuellement 15°C et nuageux à Paris.
```

Le round 2 se termine. `RoundUsage` se déclenche une seconde fois — cette fois avec les tokens du round 2 uniquement (l'entrée est généralement plus grande que celle du round 1 puisque la conversation s'est allongée). Quand le stream se ferme, `Completion.Usage` se déclenche une fois avec la **somme du round 1 et du round 2**.

### En un coup d'œil

| Étape | Appel LLM ? | Ce qui se passe | Événement |
|---|---|---|---|
| Round 1 | ✅ | Le modèle décide d'appeler `GetWeather` | `RoundUsage` (`RoundIndex=1`) |
| Entre les rounds | ❌ | L'app exécute la fonction, reçoit `« 15°C, nuageux »` | `FunctionCall`, `FunctionResult` |
| Round 2 | ✅ | Le modèle voit le résultat et rédige la réponse finale | `RoundUsage` (`RoundIndex=2`, `IsFinalRound=true`) |
| Fin du stream | — | — | `Completion` (Usage = round 1 + round 2) |

### Plus d'outils signifie plus de rounds

Si le modèle doit enchaîner plusieurs appels d'outils, les rounds s'additionnent. Pour *« Compare la météo à Paris et à Lyon »* :

1. **Round 1** — le modèle appelle `GetWeather("Paris")`
2. L'app l'exécute → `« 15°C, nuageux »`
3. **Round 2** — le modèle voit le résultat et appelle aussi `GetWeather("Lyon")`
4. L'app l'exécute → `« 18°C, ensoleillé »`
5. **Round 3** — le modèle combine les deux résultats dans la réponse finale

Trois rounds au total, et `Completion.Usage` est la somme des trois. Un compteur de contexte dans l'UI devrait utiliser le `RoundUsage.TotalTokens` du dernier round — dans cet exemple, celui du round 3.
