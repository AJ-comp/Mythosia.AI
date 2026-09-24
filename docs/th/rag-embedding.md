# Embedding

> 📍 **Q&A Pipeline:** [การเขียนคำถามใหม่](rag-query-rewriting.md) → [การกรอง](rag-filtering.md) → **`Embedding (เมื่อจำเป็น)`** → [การดึงข้อมูล](rag-hybrid-search.md) → [Reranking](rag-reranking.md) → [การสร้าง Context](rag-context-build.md)

ขั้นตอนคำถาม `Embedding` ขึ้นกับตัวค้นหา การค้นหาคำไม่รายงานขั้นตอนนี้ ตัวค้นหาที่กำหนดเองรายงานผ่าน `request.ProgressAsync` ได้ ส่วน embedding เอกสารไม่เปลี่ยนแปลง

## Embedding คืออะไร?

Embedding คือการแปลงข้อความเป็น vector ตัวเลข (อาร์เรย์ตัวเลข) ที่จับความหมายไว้ Vector เหล่านี้อยู่ในพื้นที่มิติสูงซึ่ง **ข้อความที่มีความหมายคล้ายกันจะอยู่ใกล้กัน**

ลองนึกถึงการวาดเมืองบนแผนที่ เมืองที่ใกล้กันทางภูมิศาสตร์จะอยู่ใกล้กันบนแผนที่ เช่นเดียวกัน ประโยค "จะยกเลิกการสมัครสมาชิกได้อย่างไร?" และ "อยากยุติสมาชิกภาพ" สร้าง vector ที่ใกล้กัน แม้จะใช้คำต่างกันโดยสิ้นเชิง

ใน RAG pipeline embedding เกิดขึ้นสองจุด:

1. **การ index เอกสาร** — แต่ละ chunk ถูก embed และเก็บใน vector store
2. **เวลา query** — คำถามของผู้ใช้ถูก embed เพื่อเปรียบเทียบกับ chunk ที่เก็บไว้

หน้านี้เน้น embedding เวลา query (ขั้นตอนที่ 2) ซึ่งแปลงคำถามผู้ใช้เป็น vector สำหรับ similarity search

## Embedding Provider ที่มาพร้อม

เลือกผู้ให้บริการ embedding ให้เหมาะกับภาษาของเอกสาร สภาพแวดล้อมโฮสต์ และความต้องการค้นคืน

### Perplexity

Embedding มาตรฐานประมวลผลข้อความแยกและใช้ `IEmbeddingProvider` จึงต่อกับตัวสร้าง RAG ได้ แบบ contextual เก็บลำดับส่วนข้างเคียงและกลุ่มเอกสาร ใช้ API แยกเพื่อไม่แผ่เอกสารที่ไม่เกี่ยวข้องรวมกัน

[Perplexity Agent API การค้นหา และ embedding](perplexity.md).

### OpenAI Embedding

ตัวเลือก cloud ยอดนิยม คุณภาพสูง ต้องใช้ API key:

```csharp
using Mythosia.AI.Rag.Embeddings;

var embedder = new OpenAIEmbeddingProvider(
    apiKey: "sk-...",
    httpClient: new HttpClient(),
    model: "text-embedding-3-small",   // ค่าเริ่มต้น
    dimensions: 1536                    // ค่าเริ่มต้น
);
```

ใช้ fluent builder shorthand ได้:

```csharp
.WithRag(rag => rag
    .UseOpenAIEmbedding(apiKey, model: "text-embedding-3-small", dimensions: 1536)
    .AddDocument("docs.txt")
)
```

<a id="openai-dimensions"></a>

`text-embedding-ada-002` มีขนาดคงที่ **1536 มิติ** ผู้ให้บริการจะไม่ส่งฟิลด์ `dimensions` ที่โมเดลนี้ไม่รองรับ ทั้งในคำขอเดี่ยวและคำขอแบบแบตช์ หากกำหนดขนาดอื่น จะเกิด `ArgumentOutOfRangeException` ก่อนเรียก API ส่วน `text-embedding-3-small` และ `text-embedding-3-large` ยังคงส่งค่า `dimensions` ที่กำหนดไว้ในคำขอ

### Ollama (Local)

รัน embedding บนเครื่องโดยไม่ส่งข้อมูลขึ้น cloud ต้องการ [Ollama](https://ollama.com/) ทำงานอยู่บนเครื่อง:

```csharp
var embedder = new OllamaEmbeddingProvider(
    httpClient: new HttpClient(),
    model: "qwen3-embedding:4b",       // ค่าเริ่มต้น
    dimensions: 1024,                    // ค่าเริ่มต้น
    baseUrl: "http://localhost:11434"    // ค่าเริ่มต้น
);
```

<a id="ollama-dimensions"></a>

เวกเตอร์ของเอกสารและคำถามต้องใช้โมเดลและจำนวนมิติเดียวกัน `OllamaEmbeddingProvider` ส่งค่า `dimensions` ที่ตั้งไว้ไปยัง `/api/embed` และตรวจสอบความยาวของเวกเตอร์ทุกตัวที่ได้รับ ค่าเริ่มต้นของ provider ยังคงเป็น `qwen3-embedding:4b` โดย**ร้องขอ 1024 มิติ** ส่วนผลลัพธ์ดั้งเดิมของโมเดลมี 2560 มิติ เซิร์ฟเวอร์ Ollama และโมเดลที่เลือกต้องรองรับจำนวนมิติที่ร้องขอ หากไม่รองรับหรือคำตอบละเลยการตั้งค่านี้ การเรียกจะล้มเหลว โดยไม่เปลี่ยน `Dimensions` หรือปรับความยาวเวกเตอร์ในเครื่องอย่างเงียบ ๆ

หากเปลี่ยนโมเดลหรือจำนวนมิติ ให้สร้าง embedding ของเอกสารใหม่ด้วยการตั้งค่าเดียวกับคำถาม และตั้งค่าที่เก็บเวกเตอร์ให้ตรงกัน เวกเตอร์เดิมจะไม่ถูกแปลงโดยอัตโนมัติ

### vLLM (Self-hosted)

สำหรับทีมที่รัน embedding server เองด้วย [vLLM](https://docs.vllm.ai/):

```csharp
var embedder = new VllmEmbeddingProvider(
    httpClient: new HttpClient(),
    model: "Qwen/Qwen3-Embedding-0.6B", // ค่าเริ่มต้น
    dimensions: 1024,                     // ค่าเริ่มต้น
    baseUrl: "http://localhost:8002"      // ค่าเริ่มต้น
);
```

### Local (ไม่ต้องใช้ API)

Provider น้ำหนักเบาแบบ zero-configuration ใช้ feature hashing ไม่ต้องมี API key หรือบริการภายนอก — แต่คุณภาพ embedding ต่ำกว่า neural model มาก **ไม่แนะนำสำหรับ production**

```csharp
.WithRag(rag => rag
    .UseLocalEmbedding(dimensions: 1024)
    .AddDocument("docs.txt")
)
```

> **เคล็ดลับ:** ใช้ `OpenAIEmbeddingProvider` กับ `text-embedding-3-small` ราคาถูกมาก — แทบฟรี — และให้ผลลัพธ์ดีกว่ามาก

## การประมวลผลแบบ Batch

เมื่อ index เอกสาร pipeline จะ embed chunk เป็น batch เพื่อหลีกเลี่ยงการส่งข้อความพันข้อความในการเรียก API เดียว ขนาด batch ปรับได้:

```csharp
var options = pipeline.Options.Clone();
options.EmbeddingBatchSize = 100; // ค่าเริ่มต้น: 100 chunk ต่อการเรียก API
pipeline.Options = options;
```

Batch ใหญ่ขึ้น = API call น้อยลง แต่ใช้หน่วยความจำมากต่อ call ถ้าเจอ rate limit หรือปัญหาหน่วยความจำ ให้ลดค่านี้

`EmbeddingBatchSize` ต้องเป็นค่าบวก pipeline จะตรวจสอบและเก็บค่าที่ใช้ไว้เมื่อเริ่มแต่ละการเรียกสร้างดัชนีเอกสาร ก่อนทำ embedding หรือแทนที่ระเบียน เพื่อป้องกันการวนส่งชุดว่างและการข้าม chunk เมื่อมีการเปลี่ยนค่าระหว่างรอแบบอะซิงโครนัส การเรียกครั้งถัดไปสามารถใช้ค่าใหม่ได้

<a id="embedding-validation"></a>

## ให้แต่ละเวกเตอร์ตรงกับชังก์ของตน

แม้ HTTP จะตอบกลับสำเร็จ เวกเตอร์ก็อาจขาดหายหรือเรียงลำดับผิด ทำให้ข้อความจับคู่กับความหมายของชังก์อื่น `IEmbeddingProvider` ที่กำหนดเองต้องคืน `float[]` ที่ไม่เป็น null หนึ่งชุดต่ออินพุตตามลำดับอินพุต และมี `Dimensions` เป็นค่าบวก เวกเตอร์แต่ละชุดต้องยาวเท่าจำนวนมิตินั้น และทุกค่าต้องเป็นค่าจำกัด ไม่ใช่ `NaN` หรืออนันต์

ระหว่างทำดัชนีเอกสาร Pipeline จะปฏิเสธมิติ จำนวนผลตอบกลับ หรือเวกเตอร์ที่ไม่ถูกต้องด้วย `InvalidOperationException` ก่อนจัดเก็บหรือเรียก `onDocumentEmbedded` เวกเตอร์ที่ผ่านการตรวจจะถูกคัดลอกก่อนขอ batch ถัดไป ดังนั้นการนำบัฟเฟอร์ของผู้ให้บริการกลับมาใช้ใน batch ภายหลังจะไม่เปลี่ยนชังก์ก่อนหน้า ข้อมูลที่คืนต้องคงที่ระหว่างที่ผู้เรียกอ่าน ไม่รองรับการแก้ไขพร้อมกันขณะตรวจสอบหรือคัดลอก หากการตรวจสอบล้มเหลว ระเบียนเดิมของเอกสารจะยังคงอยู่

`OpenAIEmbeddingProvider` ต้องการ `index` ที่ถูกต้องและไม่ซ้ำในทุกรายการตอบกลับ แล้วจัดกลับเป็นลำดับอินพุต `VllmEmbeddingProvider` ใช้กฎเดียวกันเมื่อมีดัชนี แต่เพื่อความเข้ากันได้ยังยอมรับผลตอบกลับที่ทุกรายการละ `index` โดยใช้ลำดับผลตอบกลับ หากขาดดัชนีเพียงบางรายการ ซ้ำ หรืออยู่นอกช่วง จะถูกปฏิเสธ ผู้ให้บริการที่กำหนดเองหรือผลตอบกลับที่ไม่มีดัชนียังต้องรับผิดชอบลำดับที่ถูกต้อง การตรวจรูปแบบไม่สามารถตรวจความหมายของเวกเตอร์ได้

<a id="query-embedding-validation"></a>

## ปกป้องเวกเตอร์คำถามก่อนค้นหา

การใช้บัฟเฟอร์ซ้ำต้องไม่เปลี่ยนคำถามระหว่างรอการแจ้งความคืบหน้าหรือการค้นหา การค้นหา dense ในตัว รวมถึงอะแดปเตอร์ `IRetrievalStrategy` ต้องใช้ `Dimensions` เป็นบวก เวกเตอร์ที่ไม่เป็น null และมีความยาวตรงกัน รวมทั้งค่าที่มีขอบเขตจำกัด ผลลัพธ์ไม่ถูกต้องจะเกิด `InvalidOperationException` ก่อนค้นหา เวกเตอร์ที่ผ่านการตรวจสอบจะถูกคัดลอกทันทีหลังส่งกลับ ก่อนแจ้งความคืบหน้าหรือค้นหาต่อ ผู้ให้บริการต้องรักษาข้อมูลให้คงที่ขณะอ่าน ส่วน `IRagRetriever` ที่กำหนดเองรับผิดชอบการเตรียมและตรวจสอบคำถามเอง

การเรียก `OllamaEmbeddingProvider` โดยตรงทั้งแบบเดี่ยวและแบบ batch ยังตรวจสอบโครงสร้าง จำนวนเวกเตอร์ที่ตรงกัน มิติ และค่าที่มีขอบเขตจำกัด JSON หรือเวกเตอร์ที่ผิดจะเกิด `InvalidOperationException` แทนการส่งผลลัพธ์ที่ไม่ครบ `HttpClient` ที่ส่งเข้ามายังเป็นของผู้เรียก การปล่อยทรัพยากรของคำขอและคำตอบ HTTP แต่ละรายการจะไม่ปล่อย client นี้

## Dimensions

Property `Dimensions` ควบคุมขนาดของแต่ละ embedding vector สิ่งนี้สำคัญเพราะ:

- **Vector store ต้องตรงกัน** — ถ้า embedding มี 1536 มิติ คอลัมน์ใน vector store ก็ต้องเป็น 1536 ด้วย
- **มิติสูง = ละเอียดกว่า** — แต่เปลืองพื้นที่และค้นหาช้ากว่า
- **มิติต่ำ = เร็วกว่า** — แต่อาจสูญเสียความแตกต่างของความหมายที่ละเอียดอ่อน

ขนาด dimension ที่ใช้กัน:

| Provider | Model | Dimensions เริ่มต้น |
| --- | --- | --- |
| OpenAI | text-embedding-3-small | 1536 |
| OpenAI | text-embedding-ada-002 | 1536 |
| Perplexity | pplx-embed-v1-0.6b | 1024 |
| Perplexity | pplx-embed-v1-4b | 2560 |
| OpenAI | text-embedding-3-large | 3072 |
| Ollama | qwen3-embedding:4b | ร้องขอ 1024 (ดั้งเดิม: 2560) |
| vLLM | Qwen/Qwen3-Embedding-0.6B | 1024 (32–1024) |
| vLLM | Qwen/Qwen3-Embedding-4B | 2560 (32–2560) |
| Local | (feature hashing) | 1024 |

## Custom Embedding Provider

ถ้าใช้บริการ embedding อื่น ให้ implement `IEmbeddingProvider`:

```csharp
public class MyEmbeddingProvider : IEmbeddingProvider
{
    public int Dimensions => 768;

    public async Task<float[]> GetEmbeddingAsync(
        string text, CancellationToken cancellationToken = default)
    {
        // เรียก embedding API ของคุณที่นี่
    }

    public async Task<IReadOnlyList<float[]>> GetEmbeddingsAsync(
        IEnumerable<string> texts, CancellationToken cancellationToken = default)
    {
        // Batch embedding call
    }
}
```

Register กับ builder:

```csharp
.WithRag(rag => rag
    .UseEmbedding(new MyEmbeddingProvider())
    .AddDocument("docs.txt")
)
```

## ขั้นตอนต่อไป

- [การกรอง](rag-filtering.md) — จำกัด chunk ที่จะค้นหา
- [การดึงข้อมูล (Hybrid Search)](rag-hybrid-search.md) — รวม vector และ keyword search
- [การปรับแต่ง Pipeline](rag-pipeline.md) — แชร์ embedding provider ข้าม service
