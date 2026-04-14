# VectorFilter

`VectorFilter` คือ fluent API สำหรับกรองผลลัพธ์ใน vector store ตาม metadata ใช้ได้กับ `IVectorStore.SearchAsync`, `HybridSearchAsync` และ query ของ RAG

## เปรียบเทียบความเท่ากันพื้นฐาน

```csharp
var filter = new VectorFilter()
    .Where("source", "manual.pdf")
    .Where("language", "th");
```

## ตัวดำเนินการเปรียบเทียบ

```csharp
var filter = new VectorFilter()
    .WhereGreaterThan("date", "2024-01-01")
    .WhereLessThanOrEqual("priority", "3")
    .WhereNot("status", "archived");
```

| Method | เทียบเท่า SQL |
|--------|---------------|
| `.Where(key, value)` | `key = value` |
| `.WhereNot(key, value)` | `key != value` |
| `.WhereGreaterThan(key, value)` | `key > value` |
| `.WhereGreaterThanOrEqual(key, value)` | `key >= value` |
| `.WhereLessThan(key, value)` | `key < value` |
| `.WhereLessThanOrEqual(key, value)` | `key <= value` |
| `.WhereLike(key, pattern)` | `key LIKE pattern` |

## การตรวจสอบสมาชิกของเซต

```csharp
var filter = new VectorFilter()
    .WhereIn("category", "legal", "compliance", "policy")
    .WhereNotIn("type", "draft", "archived");
```

## การตรวจสอบการมีอยู่ของ Key

```csharp
var filter = new VectorFilter()
    .WhereExists("reviewed_by")      // Key ต้องมีอยู่
    .WhereNotExists("deprecated");   // Key ต้องไม่มีอยู่
```

## การจัดกลุ่มเงื่อนไขตรรกะ (AND / OR)

เงื่อนไขในระดับเดียวกันจะถูกรวมด้วย AND โดยค่าเริ่มต้น ใช้ `.Or()` เพื่อสร้างกลุ่ม OR:

```csharp
var filter = new VectorFilter()
    .Where("source", "manual.pdf")
    .Or(f => f
        .Where("type", "urgent")
        .Where("priority", "high")
    );
// source = "manual.pdf" AND (type = "urgent" OR priority = "high")
```

AND แบบซ้อน:

```csharp
var filter = new VectorFilter()
    .Or(f => f
        .And(a => a.Where("lang", "en").Where("region", "us"))
        .And(a => a.Where("lang", "th").Where("region", "th"))
    );
// (lang = "en" AND region = "us") OR (lang = "th" AND region = "th")
```

## ค่าเกณฑ์คะแนน

```csharp
var filter = new VectorFilter()
    .Where("source", "faq.pdf")
    .WithMinScore(0.75);
```

## ใช้กับ Vector Store

```csharp
var filter = new VectorFilter()
    .Where("document_type", "contract")
    .WhereGreaterThan("year", "2023");

var results = await vectorStore.SearchAsync(
    queryVector: embedding,
    topK: 5,
    filter: filter
);
```

## ใช้กับ RAG

ส่งผ่าน `StoreFilter` ใน `RagQueryOptions`:

```csharp
var options = new RagQueryOptions
{
    StoreFilter = new VectorFilter()
        .Where("source", "product-manual.pdf")
        .WithMinScore(0.7)
};

var response = await ragService.GetCompletionAsync("รีเซ็ตอุปกรณ์อย่างไร?", options);
```

## รวม Filter

ใช้ `AppendConditionsFrom` เพื่อรวม filter สองตัว (เช่น รวม filter ระดับ pipeline กับ filter ระดับ query):

```csharp
var baseFilter = new VectorFilter().Where("tenant", "acme");
var queryFilter = new VectorFilter().Where("language", "th");

baseFilter.AppendConditionsFrom(queryFilter);
// baseFilter มีเงื่อนไขทั้งสองแล้ว
```
