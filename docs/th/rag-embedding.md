# Embedding

> 📍 **Q&A Pipeline:** [การเขียนคำถามใหม่](rag-query-rewriting.md) → **`Embedding`** → [การกรอง](rag-filtering.md) → [การดึงข้อมูล](rag-hybrid-search.md) → [Reranking](rag-reranking.md) → [การสร้าง Context](rag-context-build.md)

## Embedding คืออะไร?

Embedding คือการแปลงข้อความเป็น vector ตัวเลข (อาร์เรย์ตัวเลข) ที่จับความหมายไว้ Vector เหล่านี้อยู่ในพื้นที่มิติสูงซึ่ง **ข้อความที่มีความหมายคล้ายกันจะอยู่ใกล้กัน**

ลองนึกถึงการวาดเมืองบนแผนที่ เมืองที่ใกล้กันทางภูมิศาสตร์จะอยู่ใกล้กันบนแผนที่ เช่นเดียวกัน ประโยค "จะยกเลิกการสมัครสมาชิกได้อย่างไร?" และ "อยากยุติสมาชิกภาพ" สร้าง vector ที่ใกล้กัน แม้จะใช้คำต่างกันโดยสิ้นเชิง

ใน RAG pipeline embedding เกิดขึ้นสองจุด:

1. **การ index เอกสาร** — แต่ละ chunk ถูก embed และเก็บใน vector store
2. **เวลา query** — คำถามของผู้ใช้ถูก embed เพื่อเปรียบเทียบกับ chunk ที่เก็บไว้

หน้านี้เน้น embedding เวลา query (ขั้นตอนที่ 2) ซึ่งแปลงคำถามผู้ใช้เป็น vector สำหรับ similarity search

## Embedding Provider ที่มาพร้อม

Mythosia.AI.Rag มี embedding provider สี่ตัว เลือกตามความต้องการ:

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

## Dimensions

Property `Dimensions` ควบคุมขนาดของแต่ละ embedding vector สิ่งนี้สำคัญเพราะ:

- **Vector store ต้องตรงกัน** — ถ้า embedding มี 1536 มิติ คอลัมน์ใน vector store ก็ต้องเป็น 1536 ด้วย
- **มิติสูง = ละเอียดกว่า** — แต่เปลืองพื้นที่และค้นหาช้ากว่า
- **มิติต่ำ = เร็วกว่า** — แต่อาจสูญเสียความแตกต่างของความหมายที่ละเอียดอ่อน

ขนาด dimension ที่ใช้กัน:

| Provider | Model | Dimensions เริ่มต้น |
| --- | --- | --- |
| OpenAI | text-embedding-3-small | 1536 |
| OpenAI | text-embedding-3-large | 3072 |
| Ollama | qwen3-embedding:4b | 1024 (32–2560) |
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
