# Conceitos básicos

Esta página reúne os conceitos fundamentais referenciados ao longo do restante da documentação. Novos conceitos serão adicionados aqui com o tempo.

## O que é um round?

> [!NOTE]
> Um **round** é uma viagem completa de ida e volta entre sua aplicação e o modelo — seu app envia um prompt, o modelo responde, e essa troca constitui um round. Uma mensagem de chat simples é 1 round. Function calling e agentes podem encadear vários rounds para uma única mensagem do usuário.

### O caso mais simples: 1 round

Numa mensagem de chat normal, toda a conversa acontece em um único round.

```
app  →  "Quanto é 2 + 2?"      →  modelo
app  ←  "É 4."                  ←  modelo
```

`RoundUsage` é disparado uma vez com os tokens deste round. `Completion.Usage` é disparado ao final do stream com o mesmo total, já que existe apenas um round.

### Vários rounds: function calling

Os rounds se multiplicam quando o modelo não pode responder sozinho. Suponha que um usuário pergunte *«Qual é o clima em São Paulo agora?»* — o modelo não tem acesso ao clima em tempo real, então precisa chamar uma ferramenta.

**Round 1 — o modelo decide chamar uma ferramenta**

Seu app envia a mensagem do usuário junto com a lista de ferramentas registradas (por exemplo `GetWeather`) para o modelo. O modelo vê esta conversa:

```
system: Você é um assistente de clima. Você pode chamar GetWeather(city).
user:   Qual é o clima em São Paulo agora?
```

Em vez de escrever uma resposta final, o modelo retorna uma **solicitação de chamada de ferramenta**:

```
tool_call: GetWeather(city="São Paulo")
```

O turno do modelo termina e o round 1 também. `RoundUsage` é disparado com os tokens consumidos no round 1. **Ainda não há resposta final para o usuário.**

**Entre os rounds — seu app executa a função**

Esta etapa **não** é uma chamada LLM. O runtime do Mythosia.AI invoca sua implementação registrada de `GetWeather` e recebe `«15°C, nublado»`. Nenhum token é consumido.

**Round 2 — o modelo escreve a resposta final**

Seu app adiciona à conversa **o function_call que o modelo emitiu no round 1 junto com o resultado da ferramenta** e chama o modelo **pela segunda vez**. O modelo agora vê:

```
system:      Você é um assistente de clima. Você pode chamar GetWeather(city).
user:        Qual é o clima em São Paulo agora?
assistant:   [chamou GetWeather(city="São Paulo")]
tool_result: 15°C, nublado
```

Com as informações de que precisa, o modelo escreve texto:

```
Atualmente em São Paulo está 15°C e nublado.
```

O round 2 termina. `RoundUsage` é disparado pela segunda vez — desta vez apenas com os tokens do round 2 (a entrada costuma ser maior do que a do round 1, já que a conversa ficou mais longa). Quando o stream se fecha, `Completion.Usage` é disparado uma vez com a **soma do round 1 e do round 2**.

### Visão geral

| Etapa | Chamada LLM? | O que acontece | Evento |
|---|---|---|---|
| Round 1 | ✅ | Modelo decide chamar `GetWeather` | `RoundUsage` (`RoundIndex=1`) |
| Entre rounds | ❌ | App executa a função, recebe `«15°C, nublado»` | `FunctionCall`, `FunctionResult` |
| Round 2 | ✅ | Modelo vê o resultado e escreve a resposta final | `RoundUsage` (`RoundIndex=2`, `IsFinalRound=true`) |
| Fim do stream | — | — | `Completion` (Usage = round 1 + round 2) |

### Mais ferramentas significa mais rounds

Se o modelo precisar encadear várias chamadas de ferramenta, os rounds se somam. Para *«Compare o clima em São Paulo e no Rio de Janeiro»*:

1. **Round 1** — modelo chama `GetWeather("São Paulo")`
2. O app executa → `«15°C, nublado»`
3. **Round 2** — modelo vê o resultado e chama também `GetWeather("Rio de Janeiro")`
4. O app executa → `«25°C, ensolarado»`
5. **Round 3** — modelo combina os dois resultados na resposta final

Três rounds no total, e `Completion.Usage` soma os três. Um medidor de contexto na UI deve usar `RoundUsage.Usage.InputTokens` do último round — neste exemplo, o do round 3.

Para ver um exemplo numérico de como o medidor de contexto muda de round para round, consulte [Token Usage — Como o tamanho do contexto muda](token-usage.md#how-context-size-changes).
