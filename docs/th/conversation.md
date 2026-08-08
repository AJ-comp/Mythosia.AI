# การจัดการการสนทนา

## ประวัติการสนทนาทำงานอย่างไร

ทุกครั้งที่เรียก `GetCompletionAsync` หรือ `StreamAsync` จะเพิ่มข้อความเข้าไปในรายการข้อความภายในของ service หมายความว่า model มี context จากทุกรอบก่อนหน้า

```csharp
await service.GetCompletionAsync("สีโปรดของฉันคือสีน้ำเงิน");
var reply = await service.GetCompletionAsync("สีโปรดของฉันคืออะไร?");
// → "สีโปรดของคุณคือสีน้ำเงิน"
```

หากต้องการเริ่มต้นใหม่:

```csharp
service.ActivateChat.ClearMessages();
```

## Summary Policy

### ทำไมต้องมีการสรุปอัตโนมัติ?

ข้อความทุกข้อในประวัติการสนทนาจะถูกส่งไปยัง model ในทุก request เมื่อการสนทนายาวขึ้น จะเกิดปัญหาสองอย่าง:

1. **ค่าใช้จ่าย** — ประวัติที่ยาวขึ้นหมายถึง input token ที่มากขึ้นต่อ request
2. **Context เกิน** — เมื่อประวัติเกิน context window ของ model (เช่น 128K token สำหรับ GPT-4o) request จะล้มเหลว

คุณอาจตัดข้อความเก่าเอง แต่นั่นทำให้สูญเสีย context ที่ model อาจต้องการ **`SummaryConversationPolicy`** แก้ปัญหานี้ด้วยการบีบอัดข้อความเก่าให้เป็นบทสรุปกระชับ ขณะที่เก็บข้อความล่าสุดไว้ครบถ้วน

### เงื่อนไขตามจำนวนข้อความ

```csharp
service.ConversationPolicy = SummaryConversationPolicy.ByMessage(
    triggerCount: 20,   // สรุปเมื่อประวัติเกิน 20 ข้อความ
    keepRecentCount: 5  // เก็บ 5 ข้อความล่าสุดไว้ครบ
);
```

### เงื่อนไขตามจำนวน Token

```csharp
service.ConversationPolicy = SummaryConversationPolicy.ByToken(
    triggerTokens: 3000,    // สรุปเมื่อ token เกิน 3000
    keepRecentTokens: 1000  // เก็บข้อความล่าสุดไว้ถึง 1000 token
);
```

### เงื่อนไขทั้งสอง (OR)

สรุปเมื่อ **ข้อใดข้อหนึ่ง** — token เกินหรือข้อความเกิน:

```csharp
service.ConversationPolicy = SummaryConversationPolicy.ByBoth(
    triggerTokens: 4000,
    triggerCount: 30,
    keepRecentTokens: 1300,  // optional ค่าเริ่มต้น triggerTokens / 3
    keepRecentCount: 7       // optional ค่าเริ่มต้น triggerCount / 4
);
```

เมื่อตั้งค่าแล้ว การสรุปจะเกิดขึ้นอัตโนมัติใน `GetCompletionAsync` ไม่ต้องเปลี่ยนโค้ดอื่น

### หลักการทำงาน

1. ก่อนแต่ละ completion policy จะตรวจว่าการสนทนาเกินเกณฑ์หรือไม่
2. ถ้าเกิน ข้อความเก่าจะถูกสรุปเป็นข้อความสั้นด้วย stateless LLM call
3. บทสรุปถูก inject เป็น prefix ของ system message — model เห็นเป็น context ก่อนหน้า
4. ข้อความล่าสุด (ควบคุมด้วย `KeepRecentCount` หรือ `KeepRecentTokens`) ถูกเก็บไว้ครบ

เมื่อใช้เงื่อนไขตาม token policy จะใช้ **จำนวน input token จริง** ที่รายงานโดย API แทนการประมาณแบบ local

### Streaming

การสรุปไม่เกิดอัตโนมัติระหว่าง `StreamAsync` ให้เรียกก่อนอย่างชัดเจน:

```csharp
await service.ApplySummaryPolicyIfNeededAsync();

await foreach (var chunk in service.StreamAsync("ต่อจากที่คุยไว้..."))
    Console.Write(chunk.Content);
```

## บันทึกและเรียกคืนบทสรุป

บันทึกบทสรุปข้ามเซสชันเพื่อให้ model ยังคง context หลังรีสตาร์ท:

```csharp
// บันทึก
string saved = service.ConversationPolicy.CurrentSummary;
// → เก็บใน database หรือไฟล์

// เรียกคืนในเซสชันใหม่
service.ConversationPolicy.LoadSummary(saved);
```
