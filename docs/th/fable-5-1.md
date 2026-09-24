# ติดตามงานที่ใช้เวลานานด้วย Claude Fable 5.1

[Claude Opus 5.5](providers.md#claude-opus-55) เป็นส่วนเพิ่มที่ยังไม่เผยแพร่ การคิดเปิดตลอดเวลา effort เริ่มต้นเป็น medium และซ่อนการแสดงผล ต้องร้องขอความคืบหน้าที่อ่านได้โดยตรง ค่าเริ่มต้นและกฎการผูกกับโมเดลต่างจาก Fable 5.1

> การตั้งค่า Fable 5.1 ต้องใช้ `Mythosia.AI` 8.0.0 และ `Mythosia.AI.Abstractions` 4.0.0 ขึ้นไป ส่วน API Run การให้เหตุผล/ค้นหา และ GPT-6 Astra เดิมยังใช้เวอร์ชันขั้นต่ำ 7.1.0 / 3.1.0

## เมื่อใดจึงต้องใช้การตั้งค่าเหล่านี้

การศึกษาข้อมูลจากเอกสารอาจต้องค้นหาและเรียกเครื่องมือหลายครั้งก่อนตอบ แอปอาจต้องแสดงความคืบหน้า กำหนดให้ตรวจสอบบางอย่างเฉพาะเทิร์นนี้ หรือทำงานต่อหลังแก้ไขบทสนทนาก่อนหน้า Fable 5.1 มีการควบคุมสำหรับกรณีเหล่านี้ แต่เมื่อใช้ thinking ที่เก็บไว้ซ้ำ ประวัติสนทนาเองก็เป็นส่วนหนึ่งของข้อกำหนดคำขอ

ใช้ [Run API](execution-api-transition.md) เพื่อติดตามและยกเลิกงาน ใช้[ตัวเลือกการให้เหตุผลและค้นหาร่วม](reasoning-and-search.md) เพื่อเลือกระดับ effort และแหล่งข้อมูล แล้วใช้การตั้งค่าเฉพาะ Claude ด้านล่างเพื่อจัดการความคืบหน้าและประวัติ ความสามารถของโมเดลไม่ได้หมายความว่า Mythosia เปิดให้ใช้ API ของผู้ให้บริการทั้งหมด

## เลือกโมเดลและ effort อย่างชัดเจน

```csharp
using Mythosia.AI.Models;
using Mythosia.AI.Services.Anthropic;

var service = new AnthropicService(apiKey, httpClient);
service.ChangeModel(AIModels.Anthropic.ClaudeFable5_1);
service.WithAdaptiveThinkingParameters(ClaudeReasoningEffort.High);

string answer = await service.GetCompletionAsync("Review this migration plan.");
```

`ClaudeFable5_1` เลือก `claude-fable-5-1` ส่วน `ClaudeMythos5_1` เลือก `claude-mythos-5-1` ซึ่งต้องมีสิทธิ์ Project Glasswing ค่าคงที่ Fable 5 และ Mythos 5 เดิมยังอยู่ โมเดล 5.1 ทั้งสองรับข้อความและภาพ และส่งออกข้อความ โดยมีบริบท 1M โทเค็นและเอาต์พุตสูงสุด 128K โทเค็น ดู[ภาพรวมโมเดล](https://platform.claude.com/docs/en/models/fable-5-1/overview)

ค่า effort เริ่มต้นของโมเดลคือ `high` แต่ `ClaudeReasoningEffort.Auto` ของ Mythosia ยังคงใช้การแปลง `ThinkingBudget` แบบเดิม งบที่เปิดใช้จะเป็น `High` โดยเริ่มเป็น `XHigh` ที่ 32,768 และ `Max` ที่ 100,000 คำขอปิดการให้เหตุผลจะใช้ adaptive effort ต่ำและไม่แสดง thinking ที่อ่านได้ หากต้องการ `High` ให้ระบุโดยตรง `Auto` ไม่ได้หมายถึงการละ effort เพื่อใช้ค่าเริ่มต้นของโมเดลเสมอไป

## แสดงความคืบหน้าระหว่างเรียกเครื่องมือ

`ClaudeThinkingDisplay.Updates` ขอข้อความความคืบหน้าที่อ่านได้โดยยังซ่อนการให้เหตุผล ส่วน `Summarized` รวมสรุปการให้เหตุผลด้วย และ `Omitted` ไม่ส่ง thinking ที่อ่านได้ การอัปเดตขึ้นอยู่กับว่าโมเดลสร้างข้อความหรือไม่ จึงไม่ได้รับประกันการแจ้งสถานะตามช่วงเวลาคงที่ ดู[ข้อความความคืบหน้า](https://platform.claude.com/docs/en/models/fable-5-1/whats-new-fable-5-1#progress-updates-between-tool-calls-beta)

```csharp
using Mythosia.AI.Models.Streaming;

service.WithAdaptiveThinkingParameters(
    ClaudeReasoningEffort.High, ClaudeThinkingDisplay.Updates);

await using var run = await service.StartRunAsync(
    "Use the registered tools to investigate the report.",
    options: StreamOptions.FullOptions);

await foreach (var item in run.StreamAsync())
{
    if (item.Type == StreamingContentType.Reasoning)
        Console.WriteLine($"Progress: {item.Content}");
    else if (item.Type == StreamingContentType.Text)
        Console.Write(item.Content);
}
string result = (await run.Result).Text;
```

ข้อความอัปเดตใช้เหตุการณ์ `StreamingContentType.Reasoning` เดิม เปิดการรับด้วย `StreamOptions.FullOptions` หรือ `StreamOptions.Default.WithReasoning()` สำหรับคำขอที่ไม่สตรีม ให้อ่าน `service.LastThinkingContent` หลังเสร็จงาน ข้อความความคืบหน้าแยกจากคำตอบสุดท้ายและไม่เปิดเผยกระบวนการคิดดิบ

## อย่าแก้ประวัติเก่าเพื่อเปลี่ยนคำสั่งเฉพาะเทิร์น

บล็อก thinking ของ Fable 5.1 ผูกกับ system prompt เครื่องมือ และข้อความก่อนหน้าที่ใช้สร้างบล็อก การแก้ข้อมูลเหล่านี้โดยยังเก็บ thinking ภายหลังไว้อาจทำให้ใช้ไม่ได้ คำสั่งเฉพาะเทิร์นเหมาะกับเงื่อนไข เช่น ตรวจนโยบายฝ่ายสนับสนุนก่อนตอบครั้งนี้ โดยเพิ่มคำสั่งต่อท้ายและเก็บไว้ในประวัติ แล้วหยุดใช้เมื่อมีข้อความผู้ใช้ถัดไป จึงไม่ต้องเขียน system prompt ระดับบนใหม่ซ้ำ ๆ การเปลี่ยน effort และการเพิ่มคำสั่งเฉพาะเทิร์นเป็นคนละการควบคุม

```csharp
await service
    .WithTurnInstruction("Check the registered support-policy tool before answering this turn.")
    .GetCompletionAsync("Can I return an opened product?");

await service
    .WithConversationInstruction("Use Korean for the remaining conversation.")
    .GetCompletionAsync("Explain the next step.");
```

ทั้งสองเมธอดเก็บคำสั่งสำหรับคำขอเชิงตรรกะถัดไป Mythosia เพิ่มข้อความ system หลังอินพุตผู้ใช้หรือผลเครื่องมือโดยคงข้อความเดิมไว้ `WithTurnInstruction` ใช้ `clear_at: "next_user_message"` และในคำขอเดียวกันจะเพิ่มคำสั่งใหม่หลังแต่ละเทิร์นผลเครื่องมือ เพื่อให้มีผลจนคำขอนั้นจบ `WithConversationInstruction` มีผลต่อเทิร์นถัด ๆ ไปด้วย ต้องตั้งค่าก่อนเริ่มงาน ทั้งสองไม่ใช่ `run.SteerAsync` และไม่แทรกคำสั่งลงในคำตอบที่กำลังทำงาน

หากต้องการเปลี่ยน effort ระหว่างคำขอพร้อมรักษาส่วนต้นแคชที่ใช้ซ้ำได้ ให้ใช้ `.WithReasoning(ReasoningLevel.High, cache: CachePreservation.Required)` จาก `Mythosia.AI.Extensions` ไลบรารีส่งการอัปเดต effort รายข้อความและเก็บไว้ในประวัติ ดูเงื่อนไขที่รองรับใน[คู่มือร่วม](reasoning-and-search.md) สำหรับ 5.1 คำนำหน้า/ต่อท้าย system รายคำขอของ `AIRequestContext` จะเปลี่ยนเป็นคำสั่งเฉพาะเทิร์นที่เพิ่มต่อท้าย แทนการแก้ system prompt ก่อนหน้า

Fable 5.1 อ่าน thinking ของ Claude รุ่นก่อนหน้าได้ แต่รุ่นก่อนหน้าอ่าน thinking ของ Fable 5.1 ไม่ได้ Mythos 5.1 มีความสามารถ 5.1 เหมือนกันแต่ไม่บังคับตรวจ prefix binding แบบ Fable เมื่อแก้ประวัติ เปลี่ยนโมเดล หรือมี thinking ถูกทิ้ง ควรตรวจการเปลี่ยนแปลงเหล่านี้ อย่าสมมติว่าการให้เหตุผลเดิมยังอยู่ ดู[คู่มือย้ายรุ่น](https://platform.claude.com/docs/en/models/fable-5-1/migration-guide)

## ตรวจสอบการแก้ประวัติโดยตั้งใจ

`ThinkingPrefixMismatchBehavior = null` ใช้นโยบายตรวจสอบตามบัญชีของผู้ให้บริการ `WithThinkingBinding(ClaudeThinkingPrefixMismatchBehavior.Error)` ขอให้เซิร์ฟเวอร์ตรวจสอบอย่างชัดเจน การแก้ประวัติ `SystemMessage` หรือเครื่องมือโดยผู้ใช้ยังส่งไปที่ Anthropic หากตั้ง `Error` แล้วส่วนต้นไม่ตรง ผู้ให้บริการจะตอบ 400 การส่งคำขอผิดเดิมซ้ำไม่แก้ปัญหา

หากแอปตั้งใจแก้เนื้อหาเก่าและยอมให้สูญเสียการให้เหตุผลที่ได้รับผลกระทบ ให้เลือก `DropBlock` Mythosia ส่งตัวเลือกนี้ไปที่ Anthropic โดยไม่ลบ thinking อย่างเงียบ ๆ ก่อนส่งคำขอ

```csharp
service.WithThinkingBinding(ClaudeThinkingPrefixMismatchBehavior.DropBlock);
service.SystemMessage = "You are reviewing the revised policy.";
await service.GetCompletionAsync("Reassess the recommendation.");

foreach (ClaudeInputTransformation change in service.LastInputTransformations)
    Console.WriteLine($"{change.Type}: {change.Reason} ({change.Model})");
```

`LastInputTransformations` แสดง `Type`, `Path`, `Reason` ที่ผู้ให้บริการรายงาน พร้อม `ResponseId` และ `Model` สำหรับระบุที่มา `prefix_binding_mismatch` หมายถึงส่วนต้นเปลี่ยน ส่วน `model_binding_mismatch` หมายถึงโมเดลเป้าหมายอ่าน thinking นั้นไม่ได้ การทิ้งคือการสูญเสียการให้เหตุผล ไม่ใช่การซ่อม หากต้องการเก็บไว้ให้คงประวัติครบถ้วน และเริ่มบทสนทนาใหม่เมื่อต้องการรีเซ็ต

Mythosia เก็บประวัติที่ส่งจริงเพื่อหลีกเลี่ยงการเปลี่ยนโดยไม่ตั้งใจจาก RAG/context ภายใน การสนทนา Fable 5.1 ปกติจะบล็อกการย่อประวัติอัตโนมัติในเครื่องเมื่อใช้ค่าเริ่มต้นหรือ `Error` ส่วน `DropBlock` อนุญาต แต่อาจทิ้งการให้เหตุผลและไม่รับประกัน cache hit ตัวเลือก `CachePreservation.Required` แยกต่างหากยังคงใช้การป้องกันประวัติที่เข้มงวดกว่า ตัวเลือกทั่วไป เช่น `WithWebSearch()` ใช้สำหรับแต่ละคำขอแล้วถูกใช้หมด หากไม่ตั้งอีกในเทิร์นถัดไป อาร์เรย์ tools ที่ส่งจริงจะเปลี่ยนและอาจทำให้ส่วนต้นไม่ตรง หากต้องรักษาประวัติให้ตั้งค่าเครื่องมือ/ค้นหาเดิมซ้ำ ส่วนการเปลี่ยนโดยตั้งใจให้ใช้ `DropBlock` หรือบทสนทนาใหม่ ตัวเลือกเหล่านี้ไม่สืบทอดไปคำขอถัดไปโดยอัตโนมัติ

สแนปช็อตประวัติที่ส่งจริงเป็นของบริการและ `ChatBlock` นั้น การคัดลอกเฉพาะ `ChatBlock` ไปบริการใหม่ไม่ได้ย้ายสแนปช็อต RAG/context หรือ system รายเทิร์นก่อนหน้า หากต้องรักษาการให้เหตุผลให้ใช้บริการและบทสนทนาเดิมต่อ ถ้าย้ายมาเพียงประวัติดิบ ควรเริ่มบทสนทนาใหม่แทนการสมมติว่าสถานะที่เก็บไว้ย้ายมาด้วย

## ใช้การเลือกเครื่องมือตามปกติ

Fable 5.1 และ Mythos 5.1 ไม่รับการบังคับเลือกเครื่องมือ อย่าตั้ง `ForceFunctionName` ให้ระบุในคำขอว่าเมื่อใดควรใช้เครื่องมือที่ลงทะเบียนไว้ ยังใช้ `FunctionsDisabled` ได้เมื่อเทิร์นนั้นต้องไม่เรียกเครื่องมือ หากต้องการคำตอบตามชนิดข้อมูล ให้ใช้ structured-output API เดิม แทนการบังคับเรียกฟังก์ชันเพื่อเอา JSON เท่านั้น

## แยกสิ่งที่เซิร์ฟเวอร์เป็นผู้จัดการ

| ตัวเลือกของผู้ให้บริการ | Anthropic beta ที่ต้องใช้ |
| --- | --- |
| Effort รายข้อความ | `mid-conversation-output-config-2026-07-01` |
| ข้อความ system เฉพาะเทิร์น | `mid-conversation-system-clear-at-2026-08-21` |
| `thinking.display: "updates"` | `thinking-display-updates-2026-08-18` |
| Thinking binding และ `input_transformations` | `thinking-binding-controls-2026-08-01` |

Mythosia เพิ่ม header ที่เกี่ยวข้องเมื่อเปิดการตั้งค่าที่รองรับ การเปิด beta หนึ่งตัวไม่ได้เปิดตัวอื่นทั้งหมด การเชื่อมต่อนี้ไม่ได้เพิ่ม compaction ฝั่งเซิร์ฟเวอร์ บล็อกเพิ่ม/ลบเครื่องมือแบบเนทีฟ หรือการเปลี่ยนโมเดลสำรองอัตโนมัติ

ทั้งสองโมเดลต้องใช้เงื่อนไขเก็บข้อมูล 30 วันที่ผู้ให้บริการกำหนด ส่วน ZDR ต้องมีการอนุญาตอย่างชัดเจนจาก Anthropic Adaptive thinking เปิดเสมอ ไม่รองรับ `budget_tokens` แบบกำหนดเองหรือการปิดการให้เหตุผล และไม่ส่ง sampling parameter ที่ผู้ใช้กำหนด สิทธิ์บัญชีและการเก็บข้อมูลเป็นข้อกำหนดของเซิร์ฟเวอร์ ดู[เงื่อนไขย้ายรุ่น](https://platform.claude.com/docs/en/models/fable-5-1/migration-guide)

Anthropic เป็นผู้ใช้ลายน้ำข้อความ ข้อมูลแหล่งที่มาของสื่อที่รองรับ และราคาอ่านแคช โดยไม่ต้องเพิ่มตัวเลือกคำขอของ Mythosia การเชื่อมต่อนี้ไม่ได้เพิ่ม API สร้างข้อมูลแหล่งที่มาของสื่อ สวิตช์ลายน้ำ หรือการควบคุมการคิดเงิน ดู[สิ่งที่เปลี่ยนใน Fable 5.1](https://platform.claude.com/docs/en/models/fable-5-1/whats-new-fable-5-1)
