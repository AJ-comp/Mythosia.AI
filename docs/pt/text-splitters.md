# Text Splitters

Um resultado de busca precisa de contexto suficiente para responder sem representar o documento inteiro como uma única unidade. O chunking equilibra tamanho e contexto. Estes splitters usam regras locais, sem modelo de IA. Escolha conforme a estrutura e avalie a busca com suas próprias perguntas.

## Splitters Disponíveis

### CharacterTextSplitter

Para texto simples quando basta um limite de tamanho. Prioriza o separador configurado, mas pode cortar uma frase. `RagBuilder` usa `CharacterTextSplitter(300, 30)` por padrão; a extensão `.md` não seleciona automaticamente o splitter Markdown.

```csharp
.WithTextSplitter(new CharacterTextSplitter(500, 50))
```

### RecursiveTextSplitter (padrão recomendado)

Para prosa cujos parágrafos e palavras devem permanecer juntos quando possível. A ordem padrão é linha vazia → quebra de linha → `. ` → espaço → caracteres. São regras textuais, não um modelo avaliando significado; ponto seguido de espaço apenas aproxima o fim de uma frase.

Entradas repetidas em `Separators` são aplicadas uma vez, na ordem da primeira ocorrência. Duplicar um separador não acrescenta outra passagem de divisão. Listas longas são processadas sem aninhar chamadas recursivas.

```csharp
.WithTextSplitter(new RecursiveTextSplitter(500, 50))
```

### TokenTextSplitter

Somente para contar aproximadamente palavras separadas por espaços em branco. Apesar do nome, `MaxTokensPerChunk` e `TokenOverlap` contam unidades de `TokenSeparators` (espaços, tabulações e quebras de linha por padrão), não tokens do modelo. A saída normaliza os separadores para espaços. Texto sem espaços pode continuar como uma unidade longa; não garante limites de tokens do modelo.

```csharp
.WithTextSplitter(new TokenTextSplitter(256, 32))
```

### MarkdownTextSplitter

Para documentação Markdown ou Markdown produzido por loaders Office/HWP quando títulos, linhas de tabelas e blocos de código devem ser preservados. Reconhece títulos ATX (`#`–`######`), blocos delimitados e tabelas. É um splitter por regras, não um parser completo da árvore sintática Markdown. O construtor recebe apenas `chunkSize`; não há argumento nem opção de overlap para Markdown.

```csharp
.WithTextSplitter(new MarkdownTextSplitter(500))
```

#### Qualidade de Divisão de Tabelas

Tabelas GFM reconhecidas são divididas entre linhas, repetindo cabeçalho e linha delimitadora em cada chunk de tabela. Pipes externos são opcionais (`Name | Value` também funciona). Isso mantém os nomes das colunas; a qualidade da busca depende dos documentos, embeddings e perguntas.

Uma célula em negrito pertence à sua linha: por exemplo, `**Sem reembolso**` na linha da empresa A não deve virar uma condição para B. Células e blocos de código nunca viram rótulos de texto repetidos. Uma linha independente `**rótulo**` só é reconhecida no início de um parágrafo ou bloco de texto, após uma linha em branco ou limite estrutural. Uma linha em negrito no meio de um parágrafo com quebras de linha não cria outro parágrafo nem um rótulo repetido. Um rótulo reconhecido pode se repetir nos fragmentos do seu bloco; uma tabela, cerca de código, título ou próximo rótulo reconhecido encerra seu alcance.

```
Tabela original:
| Nome   | Depto  | Salário |
|--------|--------|---------|
| Alice  | Dev    | R$9.000 |
| Bob    | PM     | R$8.500 |
| Carol  | Design | R$8.000 |

→ Chunk 1:
| Nome   | Depto  | Salário |
|--------|--------|---------|
| Alice  | Dev    | R$9.000 |
| Bob    | PM     | R$8.500 |

→ Chunk 2:
| Nome   | Depto  | Salário |
|--------|--------|---------|
| Carol  | Design | R$8.000 |
```

#### Proteção de blocos de código

Blocos delimitados por crases ou tils permanecem inteiros. O fechamento usa o mesmo caractere e deve ser pelo menos tão longo quanto a abertura; um delimitador menor dentro do bloco não o fecha. Preservar o bloco inteiro pode exceder `ChunkSize`.

A indentação da cerca de abertura é preservada junto com o código, para que a divisão não altere a indentação do código renderizado. As informações de abertura são lidas uma vez por bloco, sem analisar novamente uma cerca longa a cada linha do conteúdo.

#### Caminho dos títulos

`IncludeHeadingBreadcrumb` é `true` por padrão: cada chunk repete o caminho dos títulos para manter o contexto do trecho recuperado. Com `false`, apenas a repetição é desativada; os títulos originais continuam presentes. Seções contendo somente um título também são preservadas.

`MinSplitHeadingLevel` aceita 1–6 para definir quais níveis de título iniciam seções; o padrão é 1. Quando um título superior muda, a subseção anterior é encerrada para que seu caminho antigo de títulos não seja aplicado ao novo conteúdo.

```csharp
.WithTextSplitter(new MarkdownTextSplitter(500)
{
    IncludeHeadingBreadcrumb = false
})
```

## Escolhendo Parâmetros

`CharacterTextSplitter`, `RecursiveTextSplitter` e `MarkdownTextSplitter` medem unidades de código UTF-16 (`string.Length`), não tokens nem glifos visíveis. Pares substitutos, como emojis, nunca são partidos. Com tamanho 1, um par de 2 unidades pode exceder o limite. Caracteres combinantes e grafemas completos não têm garantia de permanecer juntos.

Tamanhos devem ser positivos e overlaps não negativos. Configurações inválidas geram `ArgumentOutOfRangeException` antes do processamento; propriedades mutáveis são verificadas novamente ao dividir. Overlap maior ou igual ao tamanho o desativa por compatibilidade. Em Character/Recursive, é uma meta ajustada a separadores, limites Unicode e espaço no próximo chunk; `0` significa sem overlap. Não é emitido um chunk final contendo apenas o último overlap.

No Markdown, `ChunkSize` é o orçamento de conteúdo **sem o caminho de títulos repetido**. Um bloco de código inteiro ou o cabeçalho de uma tabela com uma linha completa pode excedê-lo. Texto comum respeita o tamanho, exceto pelo caso dos pares substitutos.

Títulos e cabeçalhos de tabela repetidos não devem transformar um documento pequeno em entrada de embedding sem limite. Por isso, Markdown tem um orçamento total por documento de `max(65536, 32 × document.Content.Length)` unidades UTF-16, somando todos os fragmentos finais, incluindo caminhos de títulos, cabeçalhos e rótulos repetidos. O limite é verificado antes de criar repetições excessivas; se seria excedido, lança `InvalidOperationException`, sem truncar conteúdo nem devolver resultados parciais. `ChunkSize` e suas exceções para blocos indivisíveis continuam sujeitos a esse limite global. Na indexação RAG padrão, a falha ocorre antes do embedding ou da substituição de registros, preservando o índice existente do documento. É um limite de texto de saída, não de tokens do modelo ou memória do processo. O orçamento vale para cada chamada a `Split` e cresce com a entrada; não é um tamanho máximo fixo do documento.

Comece, por exemplo, com `RecursiveTextSplitter(500, 50)` para prosa ou `MarkdownTextSplitter(500)` para Markdown e avalie perguntas representativas. Chunks maiores guardam mais contexto; overlap repete conteúdo e aumenta o trabalho de embedding. Nenhum garante melhores resultados.

Para limites estritos, conte cada chunk final, incluindo títulos e cabeçalhos repetidos, com o tokenizer do modelo. Contagens de caracteres/palavras e proporções por idioma não são orçamentos seguros. Implemente `ITextSplitter` com esse tokenizer se precisar de um limite rígido.

Estas correções alteram os limites dos chunks afetados. Reindexe os mesmos IDs de documento para substituir chunks antigos e atualize os caches de embeddings e referências de avaliação pertinentes. Chunks armazenados não são reescritos automaticamente.

## Splitter por Documento

Splitters diferentes podem ser aplicados por documento no `RagBuilder`:

```csharp
.WithRag(rag => rag
    .AddDocuments(new PlainTextDocumentLoader(), "readme.md", new MarkdownTextSplitter(600))
    .AddDocuments(new PlainTextDocumentLoader(), "dados.txt",  new RecursiveTextSplitter(300, 30))
    .WithTextSplitter(new RecursiveTextSplitter(500, 50))  // padrão para o restante
)
```

## Splitter Personalizado

Se quiser criar um módulo de divisão personalizado e integrá-lo, implemente `ITextSplitter`:

A indexação não deve indicar sucesso enquanto um fragmento sobrescreve outro. Atribua a cada fragmento um ID não vazio e único na coleção e copie os metadados do documento para preservar os filtros de empresa ou acesso. Este exemplo combina o ID do documento e o índice do fragmento. O pipeline rejeita IDs ausentes e duplicados dentro de um documento; não gera IDs substitutos. Consulte a [validação da indexação](rag-pipeline.md#indexing-validation).

```csharp
public class SentenceSplitter : ITextSplitter
{
    public IReadOnlyList<RagChunk> Split(RagDocument document)
    {
        var sentences = document.Content.Split(". ");
        return sentences.Select((s, i) => new RagChunk
        {
            Id = $"{document.Id}_chunk_{i}",
            Content = s,
            Index = i,
            DocumentId = document.Id,
            Metadata = new Dictionary<string, string>(document.Metadata)
        }).ToList();
    }
}

// Registrar:
.WithTextSplitter(new SentenceSplitter())
```
