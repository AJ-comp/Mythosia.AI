# การกรองผลลัพธ์

> 📍 **Q&A Pipeline:** [การเขียนคำถามใหม่](rag-query-rewriting.md) → [Embedding](rag-embedding.md) → **`การกรอง`** → [การดึงข้อมูล](rag-hybrid-search.md) → [Reranking](rag-reranking.md) → [การสร้าง Context](rag-context-build.md)

## การกรองคืออะไร?

การกรองจำกัด **chunk ที่จะถูกพิจารณา** ก่อนที่ similarity search จะทำงาน แทนที่จะค้นหาทั้ง vector store คุณสามารถจำกัดการค้นหาไว้เฉพาะกลุ่มย่อยตาม metadata หรือ score threshold

ลองนึกถึงการค้นหาในห้องสมุด โดยไม่มีการกรอง คุณค้นทุกหนังสือในตึก ด้วยการกรอง คุณเดินไปยังส่วนที่ถูกต้องก่อน (เช่น "การแพทย์" หรือ "กฎหมาย") แล้วค้นเฉพาะชั้นนั้น ค้นหาเร็วกว่าและผลเกี่ยวข้องกว่า

RAG pipeline ใช้การกรองสองประเภท:

1. **การกรอง metadata** — รวมหรือยกเว้น chunk ตาม metadata (เช่น หมวดหมู่ tenant วันที่)
2. **การกรอง score** — กำหนดเกณฑ์ขั้นต่ำเพื่อทิ้งผลที่ไม่เกี่ยวข้อง

## การกรอง Metadata

แต่ละ chunk ที่เก็บใน vector store สามารถมี metadata — คู่ key-value ที่แนบมาตอน index การกรองให้คุณ query เฉพาะ chunk ที่ตรงเงื่อนไข

### Filter ต่อ Query

ส่ง `VectorFilter` ตอน query เพื่อจำกัดขอบเขตการค้นหา:

```csharp
var filter = new VectorFilter()
    .Where("category", "refund-policy");

var result = await pipeline.QueryAsync("จะขอคืนเงินได้อย่างไร?", filter: filter);
```

### Fluent Filter API

`VectorFilter` รองรับ operator หลากหลาย:

```csharp
var filter = new VectorFilter()
    .Where("department", "engineering")         // ตรงกันแน่นอน
    .WhereNot("status", "archived")             // ไม่เท่ากัน
    .WhereIn("region", "us-east", "eu-west")    // ค่าในกลุ่ม
    .WhereGreaterThan("year", "2023")           // เปรียบเทียบช่วง
    .WhereLike("title", "%kubernetes%");        // จับคู่ pattern
```

Operator ที่ใช้ได้:

| Method | SQL Equivalent | คำอธิบาย |
| --- | --- | --- |
| `Where` | `=` | ตรงกันแน่นอน |
| `WhereNot` | `!=` | ไม่เท่ากัน |
| `WhereIn` | `IN (...)` | ค่าอยู่ในกลุ่ม |
| `WhereNotIn` | `NOT IN (...)` | ค่าไม่อยู่ในกลุ่ม |
| `WhereGreaterThan` | `>` | มากกว่า |
| `WhereGreaterThanOrEqual` | `>=` | มากกว่าหรือเท่ากับ |
| `WhereLessThan` | `<` | น้อยกว่า |
| `WhereLessThanOrEqual` | `<=` | น้อยกว่าหรือเท่ากับ |
| `WhereLike` | `LIKE` | จับคู่ pattern (`%` = อะไรก็ได้, `_` = หนึ่งตัวอักษร) |
| `WhereExists` | `IS NOT NULL` | key metadata มีอยู่ |
| `WhereNotExists` | `IS NULL` | key metadata ไม่มี |

### การจัดกลุ่มแบบ Logic

รวมเงื่อนไขด้วย AND/OR:

```csharp
var filter = new VectorFilter()
    .Where("tenant", "acme")
    .Or(f => f
        .Where("category", "billing")
        .Where("category", "refund")
    );
// ตรงกัน: tenant = "acme" AND (category = "billing" OR category = "refund")
```

## Store Filter ระดับ Pipeline

สำหรับเงื่อนไขที่ **ใช้เสมอ** (เช่น การแยก tenant) ให้ตั้ง `StoreFilter` ใน `RagQueryOptions` filter นี้จะถูกรวมอัตโนมัติกับ filter ต่อ query:

```csharp
var options = new RagQueryOptions
{
    StoreFilter = new VectorFilter().Where("tenant_id", currentTenantId)
};

var response = await ragService.GetCompletionAsync("คำถาม", ragOptions: options);
```

### วิธีที่ Filter รวมกัน

เมื่อมีทั้ง `StoreFilter` ระดับ pipeline และ filter ต่อ query จะถูก AND รวมกัน:

```
Filter สุดท้าย = เงื่อนไข StoreFilter AND เงื่อนไข filter ต่อ query
```

## การกรอง Score

Threshold `MinScore` ทิ้ง chunk ที่มีคะแนนความคล้ายต่ำกว่าระดับที่กำหนด:

```csharp
var options = new RagQueryOptions
{
    FinalFilter = new RagFilter
    {
        TopK = 5,
        MinScore = 0.7   // ทิ้งทุกอย่างต่ำกว่า 0.7
    }
};
```

## กรณีใช้งานที่พบบ่อย

### การแยก Multi-tenant

ให้แน่ใจว่าแต่ละ tenant เห็นเฉพาะเอกสารของตัวเอง:

```csharp
// ตอน index — แนบ metadata tenant
var doc = new RagDocument
{
    Id = "doc-1",
    Content = "...",
    Metadata = { ["tenant_id"] = "tenant-abc" }
};

// ตอน query — กรองตาม tenant
var options = new RagQueryOptions
{
    StoreFilter = new VectorFilter().Where("tenant_id", "tenant-abc")
};
```

### ค้นหาตามหมวดหมู่

ค้นเฉพาะหมวดหมู่เอกสารที่ต้องการ:

```csharp
var filter = new VectorFilter().Where("category", "troubleshooting");
var result = await pipeline.QueryAsync("error 404", filter: filter);
```

### การกรองตามเวลา

จำกัดผลลัพธ์ให้เป็นเอกสารล่าสุด:

```csharp
var filter = new VectorFilter()
    .WhereGreaterThanOrEqual("updated_at", "2024-01-01");
```

## ขั้นตอนต่อไป

- [การดึงข้อมูล (Hybrid Search)](rag-hybrid-search.md) — รวม vector และ keyword search
- [VectorFilter Reference](vector-filter.md) — เอกสาร API ฉบับเต็ม
- [Reranking](rag-reranking.md) — ปรับปรุงผลลัพธ์หลังดึงข้อมูล
