# แสดงตัวเลือกที่โมเดลที่เลือกสนับสนุน

หน้าจอแชตควรแสดงการคิด ค้นหา เครื่องมือ และรูปภาพให้ตรงกับการเชื่อมต่อ การเก็บรายชื่อโมเดลในแต่ละแอปซ้ำกับกฎของไลบรารีและคลาดเคลื่อนได้เมื่อผู้ให้บริการ โปรโตคอล หรือการติดตั้งเปลี่ยน ข้อมูลความสามารถแบบ snapshot ช่วยให้หน้าจอและการตรวจสอบตอนทำงานใช้คำนิยามโมเดลเดียวกัน

API นี้อยู่ในMythosia.AI 8.0.0 เป็นคำอธิบายการรองรับที่ทราบในเครื่องและแก้ไขไม่ได้ ไม่ใช่การตรวจบัญชีหรือเซิร์ฟเวอร์แบบสด ชนิดข้อมูลอยู่ใน `Mythosia.AI.Models.Capabilities`

## Before / After

Before: แอปดูแลรายชื่อเอง รายชื่อด้านล่างเป็นโค้ดของแอป ไม่ใช่ API ของไลบรารี

```csharp
bool showReasoning = mySupportedReasoningModels.Contains(service.Model);
bool showSearch = mySupportedSearchModels.Contains(service.Model);
```

After: ตรวจคำขอที่ตั้งค่าแล้วและเลือกตัวเลือกที่รองรับ เฉพาะ completion บรรทัดสุดท้ายเท่านั้นที่ส่งคำขอโมเดล การดูความสามารถไม่เรียก API

```csharp
using Mythosia.AI.Models;
using Mythosia.AI.Models.Capabilities;

var request = service.CreateRequest("อธิบายเอกสารเหล่านี้");
AIModelCapabilities capabilities = request.GetCapabilities();

if (capabilities.GetReasoningSupport(ReasoningLevel.High)
    == CapabilitySupport.Supported)
{
    request = request.WithReasoning(ReasoningLevel.High);
}

string answer = await request.GetCompletionAsync();
```

`CapabilitySupport` แยก `Supported`, `Unsupported` และ `Unknown` การติดตั้งเองหรือโมเดลที่เซิร์ฟเวอร์เลือกอาจมีข้อมูลไม่พอ โดย `Unknown` ไม่ได้แปลว่าไม่รองรับ ตัวอย่างเปิดการคิดเพิ่มเมื่อทราบว่ารองรับเท่านั้น หากไม่ทราบ แอปเลือกนโยบายคงค่าเริ่มต้นหรืออนุญาตให้ลองส่งคำขอได้

`request.GetCapabilities()` อ่านโมเดล ตัวเลือกผู้ให้บริการ และ profile ที่ builder บันทึกไว้ ส่วน `service.GetCapabilities()` ดูค่าเริ่มต้นโดยไม่ใช้ตัวเลือกที่รอสำหรับการเรียกครั้งถัดไป ทั้งสองไม่ส่ง HTTP ไม่เรียก callback บริบทหรือตัวตรวจตอนทำงาน ไม่เปลี่ยนประวัติ และไม่เริ่มงาน รายการที่คืนก็เป็น snapshot แบบอ่านอย่างเดียว การดูข้อมูลของบริการจะอ่านการตั้งค่าฟังก์ชันที่รอใช้ในการเรียกครั้งถัดไปด้วย และคงไว้ให้คำขอจริงใช้ต่อได้ การตรวจสอบจะไม่ซีเรียลไลซ์ค่าเริ่มต้นของฟังก์ชันหรือพารามิเตอร์ของเครื่องมือที่โฮสต์ และจะไม่เตรียมโปรไฟล์สำหรับการทำงานหรือจองงบประมาณโทเคน

ความสามารถบอกว่าการเชื่อมต่อรองรับอะไรได้ ไม่ใช่ตัวเลือกที่เปิดอยู่ ผู้ให้บริการ โปรโตคอล API และโหมดสำคัญเช่นเดียวกับชื่อโมเดล ID สะท้อนค่าทับของผู้ให้บริการและการแปลง Qwen/Ollama หากไม่ได้เลือกโมเดลเดียวอาจเป็น `null` Chat UI ตัวอย่างจะปรับตัวเลือกตามการเชื่อมต่อที่ใช้งานและการตั้งค่าปัจจุบัน รวมถึงเครื่องมือที่ลงทะเบียน แทนที่จะอาศัยเพียงรายการโมเดล การรองรับการสุ่มตัวอย่างอาจเปลี่ยนตามโหมดการให้เหตุผลหรือเครื่องมือที่มี จึงควรตรวจสอบใหม่หลังเปลี่ยนการตั้งค่าเหล่านี้ สถานะที่ยังไม่ทราบจะแสดงแยกจากสถานะไม่รองรับ

| API | ความหมาย |
| --- | --- |
| `Reasoning`, `ReasoningLevels`, `GetReasoningSupport(...)` | การรองรับและระดับของ `WithReasoning` แบบกลาง |
| `NativeReasoning`, `NativeReasoningLevels`, `ThinkingBudgetPresets`, `ThinkingToggle` | ตัวควบคุมการคิดเฉพาะผู้ให้บริการและงบประมาณแนะนำ |
| `Streaming`, `FunctionCalling`, `AsyncFunctionCalling`, `Steering` | สตรีม เครื่องมือ เครื่องมืออะซิงโครนัสของผู้ให้บริการ และคำสั่งระหว่างทำงาน |
| `WebSearch`, `FileSearch`, `ReasoningCachePreservation`, `ImageInput`, `StructuredOutput` | ค้นหาแบบโฮสต์ การเปลี่ยนการคิดโดยคงแคช ภาพขาเข้า และผลลัพธ์มีโครงสร้าง |
| `Temperature`, `TopP`, `FrequencyPenalty`, `PresencePenalty`, `MaxOutputTokens` | การสุ่มที่รองรับและเพดานโทเคนขาออกที่ทราบ ซึ่งเป็น null ได้ |
| `Provider`, `Model` | ผู้ให้บริการและโมเดลที่ส่ง โดยอาจยังไม่ทราบค่า |

`ReasoningLevels` ใช้กับ `WithReasoning` กลาง ส่วน `NativeReasoningLevels` เป็นค่าของผู้ให้บริการ `ThinkingBudgetPresets` เสนอทางเลือกใน UI ไม่ใช่งบประมาณที่ใช้ได้ทั้งหมดหรือช่วงตัวเลขครบถ้วน `AsyncFunctionCalling` หมายถึงเครื่องมืออะซิงโครนัสของผู้ให้บริการ ไม่ใช่เพียง handler ในเครื่องคืน `Task` หรือทำงานขนาน `StructuredOutput` ครอบคลุม API ผลลัพธ์มีชนิดแบบกลาง รวมวิธีใช้ prompt และซ่อมผลลัพธ์ ไม่รับประกันการถอดรหัสแบบจำกัดของผู้ให้บริการโดยตรง รายการระดับทั้งสองใช้ `ReasoningLevel` และงบประมาณแนะนำเป็นจำนวนเต็ม

Snapshot ไม่รับประกันสิทธิ์บัญชีหรือเซิร์ฟเวอร์พร้อม และไม่ทำให้ตัวเลือกที่ผสมผิดถูกต้อง การตรวจสอบและข้อผิดพลาดตอนทำงานยังคงเดิม ก่อนส่งคำสั่งเพิ่มให้ตรวจ `run.CanSteer` ของเซสชันจริง การรองรับของโมเดลไม่ยืนยันว่า Run ยังทำงานอยู่

## ตรวจการสร้างภาพแยกต่างหาก

โมเดลสร้างภาพแยกจากแชต ใช้ `service.GetImageCapabilities(imageModel)` เพื่อระบุโมเดล หรือไม่ส่งอาร์กิวเมนต์เพื่อดูโมเดลภาพเริ่มต้นของผู้ให้บริการ builder ของแชตไม่เลือกโมเดลสร้างภาพ ใช้ `Generation`, `Editing` และ `Mask` เพื่อตัดสินใจแสดงการทำงานกับภาพ

```csharp
using Mythosia.AI.Models.Capabilities;

ImageModelCapabilities capabilities = service.GetImageCapabilities();
bool showEditing = capabilities.Editing == CapabilitySupport.Supported;
bool showMask = capabilities.Mask == CapabilitySupport.Supported;

foreach (var quality in capabilities.Qualities)
    Console.WriteLine(quality);

int? maximumImages = capabilities.MaxImages;
```

`Qualities`, `Backgrounds`, `OutputFormats`, `SizeKinds`, `Resolutions` และ `AspectRatios` เป็นรายการมีชนิดแบบอ่านอย่างเดียว `MaxImages` และ `MaxInputImages` เป็นขีดจำกัดที่ทราบหรือ null ค่าที่อยู่ในรายการไม่ได้รับประกันว่าผสมกันได้ทุกแบบ ยังตรวจขนาด รูปแบบ คุณภาพ mask และโมเดลตามเดิม โมเดลภาพกำหนดเองหรือไม่ทราบจะไม่ถูกตัดสินว่าไม่รองรับ

`AIService` ที่เขียนเองและมีข้อมูลน่าเชื่อถือสามารถ override protected `ResolveRequestCapabilities()` ค่าเริ่มต้นคือ `AIModelCapabilities.Unknown` การไม่มีในรายการไม่ควรทำให้การติดตั้งกลายเป็นไม่รองรับ ไม่มีสมาชิกบังคับใหม่ใน `IAIService` เมธอดอยู่บน `AIService` และ builder ของมัน

หากโปรไฟล์ของผู้ให้บริการที่กำหนดเองเปลี่ยนแฟล็กโหมดเฉพาะ ให้โอเวอร์ไรด์ `ApplyCapabilityRequestProfile(AIRequestProfile)` และใช้ `SetExecutionSetting(...)` เฉพาะกับแฟล็กที่ตัวตรวจสอบความสามารถต้องใช้ ฮุกเริ่มต้นจะไม่ทำอะไร Builder จับค่าการตั้งค่าโปรไฟล์ส่วนกลางไว้แล้ว และการตรวจสอบจะไม่เรียก `ApplyRequestProfile` หรือ `ApplyProviderSpecificRequestProfile` ฮุกนี้ต้องไม่ตรวจสอบความถูกต้อง เรียก callback ซีเรียลไลซ์ จองงบประมาณ หรือเปลี่ยนสถานะของบริการหรือผู้เรียก การตั้งค่าชั่วคราวจะถูกคืนค่าหลังตรวจสอบ แม้เมธอดที่โอเวอร์ไรด์จะโยนข้อยกเว้นก็ตาม

[ตั้งค่าคำขอ](request-building.md) · [ตัวเลือกผู้ให้บริการและภาพ](providers.md) · [ควบคุม Run](execution-api-transition.md)
