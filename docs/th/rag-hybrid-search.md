# Hybrid Search

> 📍 **Q&A Pipeline:** [การเขียนคำถามใหม่](rag-query-rewriting.md) → Embedding → การกรอง → **`การดึงข้อมูล`** → [Reranking](rag-reranking.md) → การสร้าง Context

## ทำไมต้องใช้ Hybrid Search?

Vector search ล้วน ๆ เก่งเรื่องจับความหมาย — "ยกเลิกการสมัครสมาชิก" ตรงกับ "ยุติสมาชิกภาพ" แม้จะไม่มีคำเดียวกัน แต่อาจพลาด **คำที่ระบุได้ชัดเจน** เช่น ชื่อผลิตภัณฑ์ รหัสข้อผิดพลาด หรือตัวระบุนโยบายที่ผู้ใช้พิมพ์ตรง ๆ

BM25 keyword search รับมือกรณีเหล่านี้ได้ดีแต่ไม่เข้าใจความหมาย **Hybrid search รวมทั้งสอง** ให้ประโยชน์ทั้งการเข้าใจความหมายและการจับคู่คำแม่นยำ

## การตั้งค่า

ผสม dense vector search กับ BM25 keyword search ด้วยคำสั่งเดียว:

```csharp
.WithRag(rag => rag
    .UseHybridSearch(vectorWeight: 0.6f)  // 60% vector, 40% BM25
    .AddDocument("knowledge-base.txt")
)
```

`vectorWeight` ตั้งแต่ 0.0 (BM25 ล้วน) ถึง 1.0 (vector ล้วน) ค่า **0.5–0.7** ใช้ได้ดีในกรณีส่วนใหญ่

## เมื่อไหรควรใช้แบบไหน

| สถานการณ์ | น้ำหนักที่แนะนำ |
| --- | --- |
| Q&A ทั่วไปด้วยภาษาธรรมชาติ | 0.7–0.8 (เน้น vector) |
| เอกสารเทคนิคที่มีคำเฉพาะ | 0.4–0.5 (สมดุล) |
| ค้นหา code หรือรหัสข้อผิดพลาด | 0.2–0.3 (เน้น BM25) |

## ตัวอย่าง

```csharp
var service = new OpenAIService(apiKey, http)
    .WithRag(rag => rag
        .UseHybridSearch(vectorWeight: 0.5f)
        .AddDocument("product-catalog.txt")
        .AddDocument("error-codes.txt")
    );

// "ERR-4012" ถูกจับโดย BM25 ส่วน context เชิงความหมายถูกจับโดย vector
var answer = await service.GetCompletionAsync("จะแก้ ERR-4012 ได้อย่างไร?");
```
