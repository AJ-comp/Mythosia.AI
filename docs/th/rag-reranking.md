# Reranking และการปรับแต่งการค้นหา

> 📍 **Q&A Pipeline:** [การเขียนคำถามใหม่](rag-query-rewriting.md) → Embedding → การกรอง → [การดึงข้อมูล](rag-hybrid-search.md) → **`Reranking`** → การสร้าง Context

## ทำไมต้องใช้ Reranking?

Vector search คืนผู้ชนะที่เรียงตาม embedding similarity แต่ similarity นั้นเป็น **การประมาณ** Chunk ที่ได้ 0.82 อาจเกี่ยวข้องกว่า chunk ที่ได้ 0.85 จริง ๆ — embedding แค่แยกไม่ออก

**Reranker** รับรายการผู้ชนะเบื้องต้นและให้คะแนนแต่ละ chunk เทียบกับ query ด้วย model ที่ทรงพลังกว่า ให้ลำดับความเกี่ยวข้องที่แม่นยำกว่ามาก มีประโยชน์โดยเฉพาะเมื่อ:

- Corpus มี chunk ที่คล้ายกันมาก (เช่น รายการ FAQ)
- ผลการค้นหาหลัก ๆ ดู "ใกล้แต่ไม่ใช่"
- ต้องการคำตอบที่แม่นยำสูงสำหรับกรณีสำคัญ

## ตัวเลือก Reranker

### LLM Reranker

ใช้ AI service ของคุณในการให้คะแนน มีประสิทธิภาพแต่เพิ่ม latency:

```csharp
.WithRag(rag => rag
    .WithReranker(new LlmReranker(aiService))
    .AddDocument("corpus.txt")
)
```

### Cohere Reranker

เรียก Cohere Rerank API — เร็วและแม่นยำ:

```csharp
.WithRag(rag => rag
    .WithReranker(new CohereReranker(cohereApiKey))
    .AddDocument("corpus.txt")
)
```

### vLLM Reranker

ใช้ vLLM reranking endpoint ที่ host เอง:

```csharp
.WithRag(rag => rag
    .WithReranker(new VllmReranker(baseUrl: "http://localhost:8000"))
    .AddDocument("corpus.txt")
)
```

## พารามิเตอร์การดึงข้อมูล

ควบคุมจำนวนผู้ชนะที่ดึงมาและวิธีกรองก่อนเลือกขั้นสุดท้าย:

```csharp
.WithRag(rag => rag
    .WithTopK(5)                   // จำนวน chunk สุดท้ายที่คืนมา
    .WithRetrievalMultiplier(3)    // ดึง topK × 3 ผู้ชนะ (สำหรับ reranking)
    .WithScoreThreshold(0.6)       // score ขั้นต่ำ
    .AddDocument("corpus.txt")
)
```

- **`TopK`** — จำนวน chunk ที่เข้าไปใน LLM context
- **`RetrievalMultiplier`** — ดึงมาให้มากขึ้นเพื่อให้ reranker มีตัวเลือก multiplier 3 หมายถึงดึงมา 15 ตัวแล้วคัด 5 ตัวที่ดีที่สุดหลัง reranking
- **`WithScoreThreshold`** — ทิ้งทุกอย่างต่ำกว่า threshold นี้ แม้จะเหลือน้อยกว่า `TopK`

## โหมดการเลือกขั้นสุดท้าย

เมื่อใช้ reranker เลือกวิธีคำนวณคะแนนจัดอันดับสุดท้าย:

```csharp
using Mythosia.AI.Rag;

// ค่าเริ่มต้น: เชื่อคะแนน reranker อย่างเดียว
.WithFinalSelectionPolicy(RagFinalSelectionMode.RerankerOnly)

// ผสมคะแนนการดึงข้อมูลและ reranker
.WithFinalSelectionPolicy(RagFinalSelectionMode.WeightedBlend, retrievalWeight: 0.65)  // 65% retrieval, 35% reranker
```

**`RerankerOnly`** คือค่าเริ่มต้นที่ปลอดภัย — การตัดสินของ reranker แทนที่คะแนนการดึงข้อมูลเดิมทั้งหมด

**`WeightedBlend`** เก็บสัญญาณการดึงข้อมูลเดิมไว้พร้อมรวมการตัดสินของ reranker ใช้ได้ดีเมื่อ embedding vector มีคุณภาพสูงอยู่แล้วและต้องการให้ reranker ช่วยตัดสินใจในกรณีที่สูสีกัน
