# แนะนำ

Mythosia.AI คือไลบรารี .NET AI แบบ modular ที่มี interface เดียวรองรับ AI provider หลายราย พร้อม RAG pipeline, document loader และ vector database

## ทำไมต้องใช้ Mythosia.AI?

SDK ของแต่ละ AI provider มี API ที่แตกต่างกัน ทำให้การเปลี่ยน provider หรือรวมฟีเจอร์ต่าง ๆ เป็นเรื่องยุ่งยาก Mythosia.AI ห่อหุ้มทั้งหมดไว้ภายใต้ interface `IAIService` ตัวเดียว — โค้ดของคุณไม่ต้องเปลี่ยนแม้จะสลับ model หรือ provider

## โครงสร้าง package

ติดตั้งเฉพาะสิ่งที่จำเป็น:

| ขั้นตอน | Package | วัตถุประสงค์ |
|:----:|---------|---------|
| **1** | `Mythosia.AI` | เริ่มต้นที่นี่ — completions, streaming, function calling, structured output |
| **2** | `Mythosia.AI.Rag` | เพิ่มเมื่อต้องการ RAG — splitter, embedding, hybrid search, reranking |
| **3** | `Mythosia.VectorDb.*` | เพิ่มเมื่อต้องการ vector store สำหรับ production — Postgres, Qdrant หรือ Pinecone |

## Provider ที่รองรับ

Provider ทั้งหมดอยู่ใน package `Mythosia.AI` (ยกเว้น Alibaba):

| Provider | Models |
|----------|--------|
| **OpenAI** | GPT-5.x, GPT-4.1, GPT-4o, o3 series |
| **Anthropic** | Claude Fable 5, Mythos 5 (limited), Opus / Sonnet 5 and 4.x, Haiku 4.5 |
| **Google** | Gemini 2.5 / 3 series |
| **xAI** | Grok 4 series, Grok Build |
| **DeepSeek** | Chat, Reasoner |
| **Perplexity** | Sonar, Sonar Pro, Sonar Reasoning Pro |
| **Alibaba / Qwen** | Qwen Max / Plus / Turbo / Qwen3 (`Mythosia.AI.Providers.Alibaba`) |

## ภาพรวมสถาปัตยกรรม

```
Mythosia.AI                     ← Core AI services (ทุก provider)
    └── Mythosia.AI.Abstractions   ← Interface IAIService

Mythosia.AI.Rag                 ← RAG pipeline, orchestration
    ├── Mythosia.AI.Abstractions
    ├── Mythosia.AI.Rag.Abstractions
    │   └── Mythosia.VectorDb.Abstractions
    ├── Mythosia.Documents.Office / Mythosia.Documents.Pdf
    │   └── Mythosia.Documents.Abstractions
    └── Mythosia.VectorDb.InMemory
        ├── Mythosia.VectorDb.Abstractions
        └── Mythosia.AI.Rag.Abstractions

Mythosia.VectorDb.*             ← Vector store (เลือกหนึ่งหรือหลายตัว)
    └── Mythosia.VectorDb.Abstractions

Mythosia.Documents.*            ← Document loader (Word, Excel, PDF, ...)
    └── Mythosia.Documents.Abstractions
```
