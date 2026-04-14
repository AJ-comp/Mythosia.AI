# RAG Agêntico

## Por que RAG Agêntico?

No RAG padrão, cada mensagem do usuário dispara exatamente **uma** busca. O sistema pesquisa, monta o contexto e gera a resposta — sem exceções. Isso funciona bem para perguntas simples, mas deixa a desejar quando:

- A pergunta exige **múltiplas buscas** sobre temas diferentes (ex: "Compare a política de reembolso para produtos físicos e digitais")
- O primeiro resultado da busca é **insuficiente** e o sistema deveria refinar e tentar novamente
- Algumas perguntas **não precisam de busca** (ex: "Resuma nossa conversa até agora")
- A resposta depende de combinar **busca em documentos com dados ao vivo** de APIs

O RAG Agêntico resolve tudo isso. Em vez de um pipeline fixo de buscar-e-responder, o **agente decide de forma autônoma** — quando pesquisar, o que pesquisar, se deve pesquisar novamente e quando chamar outras ferramentas — tudo dentro de um loop ReAct.

## Início Rápido

Registre o `RagStore` como ferramenta com `WithAgenticRag` e delegue ao `RunAgentAsync`:

```csharp
// Construir o índice uma vez
var ragStore = await RagStore.BuildAsync(cfg => cfg
    .AddDocument("manual.pdf")
    .AddDocument("policy.docx")
    .UseOpenAIEmbedding(apiKey));

// Registrar RAG como ferramenta e executar o agente
var service = new AnthropicService(apiKey, http);
service.WithAgenticRag(ragStore);

var answer = await service.RunAgentAsync("Resuma a política de reembolso.");
```

O agente chama `search_documents` automaticamente sempre que precisa de contexto documental e sintetiza a resposta final a partir dos trechos recuperados.

## Combinando com Outras Ferramentas

O RAG Agêntico brilha quando combinado com ferramentas adicionais — o agente seleciona a ferramenta certa para cada sub-tarefa:

```csharp
var service = new AnthropicService(apiKey, http);

service.WithAgenticRag(ragStore)
       .WithFunctionAsync("get_order_status", "Consultar o status de um pedido pelo ID.",
           ("order_id", "O ID do pedido a consultar.", required: true),
           async id => await orderApi.GetStatusAsync(id));

// O agente busca a política nos documentos E chama a API para dados do pedido
var answer = await service.RunAgentAsync(
    "Pedido #12345 — tenho direito a reembolso com base na política atual?");
```

Neste exemplo, o agente de forma autônoma:

1. Pesquisa nos documentos a política de reembolso
2. Chama a API de pedidos para obter o status do pedido #12345
3. Combina as duas informações para produzir a resposta final

## Descrição Personalizada da Ferramenta

A descrição da ferramenta controla quando o agente decide invocar o RAG. Adapte-a ao seu domínio para uma seleção de ferramenta mais precisa:

```csharp
service.WithAgenticRag(ragStore,
    toolDescription:
        "Pesquisar políticas internas de RH, manuais de produtos e documentos de conformidade. " +
        "Use esta ferramenta sempre que precisar de informações específicas da empresa ou de produtos.");
```

Uma descrição vaga como "Pesquisar documentos" pode fazer o agente chamar o RAG com frequência excessiva ou insuficiente. Seja específico sobre **que tipo de informação** os documentos contêm.

## Diferenças em Relação ao RAG Padrão

| | RAG Padrão | RAG Agêntico |
| --- | --- | --- |
| Momento da busca | Em toda mensagem | O agente decide |
| Formulação da consulta | QueryRewriter | O próprio agente |
| Número de buscas | Uma por turno | Uma ou mais conforme necessário |
| Combinação de ferramentas | Não aplicável | Qualquer ferramenta registrada |
| Configuração | `.WithRag()` | `.WithAgenticRag()` + `RunAgentAsync` |

> **Nota:** O `QueryRewriter` é intencionalmente ignorado no RAG Agêntico. O agente formula sua própria consulta de busca autocontida, tornando uma etapa de reescrita separada redundante e potencialmente distorcida.

## Quando Usar Cada Um

- **RAG Padrão** — toda pergunta é baseada em documentos, de tema único, e você quer latência mínima
- **RAG Agêntico** — perguntas abrangem múltiplos temas, requerem combinação de documentos + dados ao vivo, ou precisam de buscas iterativas
