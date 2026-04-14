# Construção de Contexto

> 📍 **Pipeline de Pergunta e Resposta:** [Reescrita de Consulta](rag-query-rewriting.md) → [Embedding](rag-embedding.md) → [Filtragem](rag-filtering.md) → [Recuperação](rag-hybrid-search.md) → [Re-ranking](rag-reranking.md) → **`Construção de Contexto`**

## O que é a Construção de Contexto?

A Construção de Contexto é o estágio final do pipeline RAG. Após recuperar e classificar os chunks mais relevantes, este estágio **os monta em um prompt** que o LLM pode entender e usar para gerar uma resposta.

## Context Builder Padrão

Quando nenhuma configuração personalizada é definida, o pipeline usa `DefaultContextBuilder`:

```
Responda à pergunta com base no seguinte contexto:

[1] (Fonte: manual.txt)
Os reembolsos estão disponíveis dentro de 30 dias após a compra...

[2] (Fonte: politica.txt)
Produtos digitais não têm reembolso...

Pergunta: Qual é a política de reembolso?
```

O builder padrão tem propriedades configuráveis:

```csharp
var contextBuilder = new DefaultContextBuilder
{
    Header = "Responda à pergunta com base no seguinte contexto:",
    QueryPrefix = "Pergunta:",
    IncludeScores = false,
    IncludeSource = true
};

.WithRag(rag => rag
    .WithContextBuilder(contextBuilder)
    .AddDocument("docs.txt")
)
```

## Templates de Prompt

Para mais controle sobre o prompt final, use um **template de prompt** com marcadores `{context}` e `{question}`:

```csharp
.WithRag(rag => rag
    .WithPromptTemplate("""
        Você é um assistente de suporte ao cliente. Use APENAS os seguintes documentos
        para responder à pergunta. Se a resposta não estiver nos documentos, diga
        "Não tenho essa informação."

        Documentos:
        {context}

        Pergunta do Cliente: {question}
        """)
    .AddDocument("suporte-kb.txt")
)
```

### Quando Usar Templates

Os templates são especialmente poderosos quando você precisa:

- **Restringir o comportamento** — "Se a resposta não estiver no contexto, diga 'Não sei'"
- **Definir o tom** — "Responda de forma profissional e concisa"
- **Adicionar contexto de função** — "Você é um assistente médico"
- **Controlar o idioma** — "Sempre responda em português"

## Context Builder Personalizado

Para controle completo, implemente `IContextBuilder`:

```csharp
public class MyContextBuilder : IContextBuilder
{
    public string BuildContext(string query, IReadOnlyList<VectorSearchResult> searchResults)
    {
        var sb = new StringBuilder();
        sb.AppendLine("### Informações Relevantes ###");

        foreach (var result in searchResults)
        {
            var source = result.Record.Metadata.TryGetValue("source", out var s) ? s : "desconhecido";
            sb.AppendLine($"📄 De: {source} (relevância: {result.Score:P0})");
            sb.AppendLine(result.Record.Content);
            sb.AppendLine("---");
        }

        sb.AppendLine($"\nCom base nas informações acima, responda: {query}");
        return sb.ToString();
    }
}
```
