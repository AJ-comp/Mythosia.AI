# เลือกระดับการให้เหตุผลและตอบพร้อมแหล่งอ้างอิง

> Grok 4.7: ต้องใช้ Mythosia.AI 8.1.0 / Abstractions 4.1.0 [การเลือกโมเดล การให้เหตุผล และความเร็ว](providers.md#grok-47)

> GPT-6 Sol/Luna: ต้องใช้ Mythosia.AI 8.1.0 / Abstractions 4.1.0 [การเลือกโมเดลและรุ่นที่ต้องใช้](providers.md#gpt-6-sol-luna)

[Claude Opus 5.5](providers.md#claude-opus-55) รองรับตั้งแต่ Mythosia.AI 8.1.0 / Abstractions 4.1.0 การคิดเปิดตลอดเวลา effort เริ่มต้นเป็น medium และซ่อนการแสดงผล ต้องร้องขอความคืบหน้าที่อ่านได้โดยตรง ค่าเริ่มต้นและกฎการผูกกับโมเดลต่างจาก Fable 5.1

ใช้ [request builder](request-building.md) เพื่อแยกการตั้งค่าและสร้างรูปแบบที่ใช้ซ้ำได้ เรียก `CreateRequest(...)` ก่อน `With...` ส่วน property และ fluent method บน service ยังคงพฤติกรรมเดิม

> API เหล่านี้ต้องใช้ `Mythosia.AI` 7.1.0 ขึ้นไป ซึ่งรวม `Mythosia.AI.Abstractions` 3.1.0 ขึ้นไป ตัวอย่าง RAG ต้องใช้ `Mythosia.AI.Rag` 7.6.0 ขึ้นไป

> ตัวอย่าง `CreateRequest` ต้องใช้Mythosia.AI 8.0.0 / Abstractions 4.0.0 รุ่น 7.1 เดิมที่เพิ่ม Run และตัวเลือกคำขอทั่วไปยังไม่มี builder แพ็กเกจเดิมใช้ overload ของ service ต่อได้

[Claude Fable 5.1](fable-5-1.md) เพิ่มข้อความความคืบหน้า คำสั่งเฉพาะเทิร์น และการวินิจฉัย thinking binding ตั้งแต่ `Mythosia.AI` 8.0.0 / `Mythosia.AI.Abstractions` 4.0.0 ส่วน Mythos 5.1 ต้องได้รับเชิญ และทั้งสองรุ่นไม่รองรับการบังคับเลือกเครื่องมือ

คำขอที่ต้องคำนึงถึงเวลารอสามารถเลือก[ความเร็วในการประมวลผล](request-building.md#inference-speed) ได้ `WithSpeed` คงโมเดลและระดับการคิด ส่วน `Processing` รายงานโหมดที่ใช้จริง Fast เป็นตัวเลือกเสียเงินสำหรับการผสมที่รองรับ

## ทำไมจึงต้องใช้ตัวเลือกเหล่านี้?

แต่ละขั้นตอนต้องการความช่วยเหลือต่างกัน ร่างแรกอาจต้องการคำตอบที่รวดเร็ว ส่วนการตรวจสอบสมมติฐานอาจคุ้มค่าที่จะใช้การให้เหตุผลมากขึ้น คำถามเกี่ยวกับเหตุการณ์วันนี้ต้องใช้ข้อมูลปัจจุบัน ขณะที่คำถามเกี่ยวกับผลิตภัณฑ์ต้องใช้เอกสารที่อธิบายผลิตภัณฑ์นั้น การเพิ่มระดับการให้เหตุผลเพียงอย่างเดียวไม่ได้ทำให้โมเดลเข้าถึงแหล่งข้อมูลทั้งสองประเภทนี้

ใช้ Fluent API ร่วมเพื่อระบุว่างานถัดไปต้องการอะไร ผู้ให้บริการที่เลือกจะแปลงตัวเลือกที่รองรับเป็น API ของตน แอปพลิเคชันยังใช้ `GetCompletionAsync` เพื่อรับคำตอบที่เสร็จสมบูรณ์ หรือใช้ `StartRunAsync` เพื่อแสดงความคืบหน้าและควบคุมงานเดียวกันได้

| สิ่งที่งานต้องการ | การตั้งค่า |
| --- | --- |
| ร่างอย่างรวดเร็วแล้วตรวจทานอย่างละเอียด | `WithReasoning(...)` |
| เปลี่ยนระดับการให้เหตุผลโดยรักษาส่วนต้นของแคชบทสนทนาที่เข้าเงื่อนไข | `WithReasoning(..., cache: CachePreservation.Required)` |
| ข้อมูลปัจจุบันจากเว็บ | `WithWebSearch()` |
| คำตอบที่อิงเอกสารซึ่งผู้ให้บริการจัดทำดัชนีไว้แล้ว | `WithFileSearch(store)` |

ตัวอย่างสมมติว่ามีบริการที่เริ่มต้นแล้วและใช้โมเดลที่รองรับ ให้นำเข้า `Mythosia.AI.Extensions` และ `Mythosia.AI.Models` ส่วนเหตุการณ์สตรีมใช้ `Mythosia.AI.Models.Streaming` ด้วย

## เปลี่ยนจากการร่างอย่างรวดเร็วไปสู่การตรวจทานอย่างละเอียด

คุณสามารถใช้การให้เหตุผลน้อยลงสำหรับโครงร่าง แล้วขอให้บทสนทนาเดิมตรวจสอบรายละเอียดที่ซับซ้อนได้:

```csharp
string outline = await service
    .CreateRequest("ร่างโครงของแผนการย้ายระบบ")
    .WithReasoning(ReasoningLevel.Low)
    .GetCompletionAsync();

string review = await service
    .CreateRequest("ตรวจทานแผนนั้นเพื่อหาสถานการณ์ที่อาจล้มเหลวและขั้นตอนการกู้คืน")
    .WithReasoning(ReasoningLevel.High)
    .GetCompletionAsync();
```

Gemini 3.7/3.8 Flash รองรับ `Low`, `Medium`, `High` ผ่าน `WithReasoning` แต่ไม่รองรับ `Minimal`, `None`, `CachePreservation.Required` โดยใช้เส้นทาง completion, streaming, Run, เครื่องมือ และการค้นหาเดิม พร้อมข้อจำกัดการใช้งานร่วมกันของ Google ดู[ตัวอย่างการตั้งค่า Google](providers.md#google-googleaiservice)

`ReasoningLevel` ระบุระดับที่ต้องการ ไม่ใช่งบประมาณโทเคนคงที่หรือการรับประกันคุณภาพคำตอบ แต่ละโมเดลรองรับชุดระดับต่างกัน `Auto` คงพฤติกรรมที่ตั้งค่าไว้หรือค่าเริ่มต้นของผู้ให้บริการ ไม่ได้หมายความว่าจะเปลี่ยนระดับที่ไม่รองรับเป็นระดับอื่นโดยอัตโนมัติ โมเดลที่เปิดให้ตั้งงบประมาณโทเคนแทนระดับที่มีชื่อยังใช้พร็อพเพอร์ตีงบประมาณเฉพาะของผู้ให้บริการได้ตามเดิม

ในบทสนทนาที่ยาว การเปลี่ยนค่าการให้เหตุผลที่ระดับบนสุดของคำขออาจทำให้ส่วนต้นของพรอมป์ต์ที่ใช้ซ้ำได้หมดสภาพใช้งาน บนโมเดลที่รองรับ คุณสามารถกำหนดให้ใช้กลไกของผู้ให้บริการเพื่อเปลี่ยนระดับโดยยังคงส่วนต้นนั้นไว้:

```csharp
string review = await service
    .CreateRequest("ตรวจสอบสมมติฐานในคำตอบก่อนหน้าอีกครั้ง")
    .WithReasoning(ReasoningLevel.High, cache: CachePreservation.Required)
    .GetCompletionAsync();
```

`Required` เป็นข้อตกลงเกี่ยวกับวิธีส่งการเปลี่ยนแปลง โดย**ไม่รับประกัน**ว่าจะพบแคช ใช้โทเคนฟรี หรือมีความหน่วงต่ำลง เงื่อนไขการใช้แคช ระยะเวลาเก็บ และราคาของผู้ให้บริการยังมีผล โมเดลที่ไม่รองรับจะโยน `NotSupportedException` ก่อนส่งคำขอ ให้ใช้บทสนทนาที่ระบบติดตามอยู่ โมเดล และเอนด์พอยต์เดิม ห้ามตัดทอนหรือเรียงลำดับประวัติที่มีการอัปเดตเหล่านี้ใหม่ หากต้องการเปลี่ยนเงื่อนไขดังกล่าว ให้เริ่มบทสนทนาใหม่ ระบบจะระงับการย่อประวัติโดยอัตโนมัติขณะที่ต้องรักษาส่วนต้นนี้ไว้

ระดับการให้เหตุผลแบบรักษาแคชที่ได้รับการยอมรับจะเป็นระดับที่ใช้ในบทสนทนาจนกว่าจะมีการเปลี่ยนอย่างชัดเจนอีกครั้ง ส่วน `WithReasoning(level)` แบบปกติใช้กับคำขอเชิงตรรกะของมันเท่านั้น และไม่แทนที่ค่าที่คงอยู่โดยไม่แจ้ง การเปลี่ยนนี้เกิดขึ้น**ระหว่างการตอบของโมเดลแต่ละครั้ง** ไม่ได้เปลี่ยนระดับการให้เหตุผลของคำตอบที่กำลังสร้าง และแยกจาก `run.SteerAsync` ซึ่งใช้ส่งคำสั่งเพิ่มเติมไปยัง Run ที่กำลังทำงานและรองรับความสามารถนี้

## ตอบคำถามที่ต้องการข้อมูลปัจจุบัน

เปิดการค้นหาเว็บของผู้ให้บริการเมื่อคำตอบควรใช้ข้อมูลที่อยู่นอกข้อมูลฝึกของโมเดล:

```csharp
string answer = await service
    .CreateRequest("ค้นหาประกาศออกรุ่นล่าสุดและระบุแหล่งอ้างอิง")
    .WithWebSearch()
    .GetCompletionAsync();

foreach (AICitation source in service.GetLastCitations())
    Console.WriteLine($"{source.Title}: {source.Url}");
```

ผู้ให้บริการเป็นผู้เรียกใช้เครื่องมือที่โฮสต์ไว้นี้ ไม่ต้องลงทะเบียนหรือเรียกใช้ตัวจัดการฟังก์ชันภายในแอป การเปิดค้นหาทำให้โมเดลเลือกใช้ได้ แต่โมเดลอาจตัดสินว่าพรอมป์ต์บางรายการไม่จำเป็นต้องค้นหา แหล่งอ้างอิงจะมีให้เมื่อผู้ให้บริการส่งกลับมา

OpenAI และ Anthropic รองรับ `WithWebSearch(new WebSearchOptions { AllowedDomains = new[] { "example.com" } })` ด้วย ส่วนเครื่องมือ Google ที่เชื่อมต่ออยู่นี้ไม่มีตัวเลือกรายการโดเมนที่อนุญาต คำขอที่กำหนดข้อจำกัดนี้จึงถูกปฏิเสธ แทนที่จะค้นหาทั่วทั้งเว็บ

## ตอบจากเอกสารที่ผู้ให้บริการจัดทำดัชนีไว้แล้ว

หากแอปพลิเคชันมีดัชนีเอกสารที่โฮสต์โดยผู้ให้บริการอยู่แล้ว สามารถใช้ที่เก็บนั้นเป็นหลักฐานประกอบคำตอบโดยไม่ต้องสร้างรอบการค้นคืนเอง:

```csharp
var documents = new FileSearchStore("OpenAI", "vs_your_existing_store");

string answer = await service
    .CreateRequest("ค้นหาเอกสารนโยบายของเรา ระยะเวลาที่ยกเลิกบริการได้คือเท่าใด?")
    .WithFileSearch(documents)
    .GetCompletionAsync();

foreach (AICitation source in service.GetLastCitations())
    Console.WriteLine($"{source.Title}: {source.FileId ?? source.Url}");
```

สำหรับ Google ให้ใช้ `new FileSearchStore("Google", "fileSearchStores/your-existing-store")` กับบริการ Google ที่เก็บเป็นของผู้ให้บริการ บัญชี และสภาพแวดล้อมการปรับใช้ที่ระบุ จึงนำ ID ที่เก็บของ OpenAI ไปให้ Google ไม่ได้ ก่อนใช้ที่นี่ ให้สร้างที่เก็บ อัปโหลดเอกสาร และจัดทำดัชนีผ่าน API หรือคอนโซลของผู้ให้บริการ API นี้ค้นหาเฉพาะที่เก็บที่มีอยู่แล้ว และไม่อัปโหลดไฟล์ภายในเครื่อง

`CreateRequest(...).With...` เก็บตัวเลือกใน builder อิสระ การใช้ซ้ำจะนำตัวเลือกไปใช้กับแต่ละการทำงานและรอบเครื่องมือ ส่วน `service.WithReasoning`, `service.WithWebSearch` และ `service.WithFileSearch` เดิมยังคืนชนิด service เดิมและใช้ตัวเลือกกับคำขอเชิงตรรกะถัดไป ใช้ต่อได้กับ `IAIRequestFeatureService` และ RAG wrapper ทั้งสองรูปแบบไม่รับประกันการทำงานพร้อมกันบน service เดียว

## แสดงความคืบหน้าและเก็บแหล่งอ้างอิง

ใช้ตัวเลือกเดียวกันก่อน `StartRunAsync` ได้ คอลแบ็กข้อความช่วยอัปเดตหน้าจอ ขณะที่ Run เก็บแหล่งอ้างอิงสำหรับคำตอบที่เสร็จแล้ว:

```csharp
await using var run = await service
    .CreateRequest("ค้นหาประกาศล่าสุดแล้วเปรียบเทียบการเปลี่ยนแปลง")
    .WithReasoning(ReasoningLevel.High)
    .WithWebSearch()
    .StartRunAsync(
        onText: text => Console.Write(text),
        cancellationToken: cancellationToken);

string answer = (await run.Result).Text;
foreach (AICitation source in run.Citations)
    Console.WriteLine($"{source.Title}: {source.Url ?? source.FileId}");
```

`run.Citations` ยังใช้งานได้แม้ไม่ได้อ่านสตรีม เลือกสังเกตเฉพาะข้อความ หรือบัฟเฟอร์สำหรับสังเกตผลลัพธ์เต็มแล้ว โดยเก็บแหล่งอ้างอิงจากผู้ให้บริการที่รวบรวมระหว่าง Run รวมถึงคำตอบระหว่างทาง `service.LastCitations` หรือ `GetLastCitations()` ผ่าน `IAIService` หมายถึงคำขอเชิงตรรกะล่าสุด เมื่อต้องแสดงหลายคำตอบ ให้เก็บ Run ที่เกี่ยวข้องหรือคัดลอกสแนปช็อตแหล่งอ้างอิงไว้

หากต้องการรับเหตุการณ์แหล่งอ้างอิงทันทีที่มาถึง ให้ใช้ตัวอ่านเหตุการณ์เพียงตัวเดียว:

```csharp
await using var run = await service
    .CreateRequest("ค้นหาและอธิบายการเปลี่ยนแปลงล่าสุด")
    .WithWebSearch()
    .StartRunAsync(cancellationToken: cancellationToken);

await foreach (var item in run.StreamAsync())
{
    if (item.Type == StreamingContentType.Text)
        Console.Write(item.Content);
    else if (item.Type == StreamingContentType.Citation && item.Citation is AICitation source)
        Console.WriteLine($"\nแหล่งอ้างอิง: {source.Title} {source.Url ?? source.FileId}");
}
string answer = (await run.Result).Text;
```

ฟิลด์แหล่งอ้างอิงเป็น null ได้หากผู้ให้บริการไม่ได้ส่งค่า `ResponseId`, `OutputIndex` และ `ContentIndex` ระบุคำตอบและส่วนเนื้อหาต้นทาง `StartIndex` และ `EndIndex` รักษาตำแหน่งภายในส่วนเนื้อหาและรูปแบบการนับดัชนีของผู้ให้บริการ โดย**ไม่ใช่**ตำแหน่งใน `(await run.Result).Text` ที่นำข้อความมาต่อกัน อย่านำค่าเหล่านี้ไปใช้เป็นดัชนีของคำตอบทั้งหมดเพื่อวางแหล่งอ้างอิงโดยตรง

## ตรวจสอบการรองรับของผู้ให้บริการและขอบเขตคำขอ

| ผู้ให้บริการที่เชื่อมต่อ | ระดับการให้เหตุผลแบบมีชื่อ | การเปลี่ยนที่รักษาแคช | ค้นหาเว็บ | ค้นหาไฟล์ |
| --- | --- | --- | --- | --- |
| OpenAI | โมเดลการให้เหตุผลที่รองรับ โดยระดับขึ้นอยู่กับโมเดล | GPT-6 Astra / Sol / Luna Standard ในโหมดเอเจนต์เดียว | โมเดล Responses ที่รองรับ | โมเดล Responses ที่รองรับและที่เก็บเวกเตอร์ที่มีอยู่แล้ว |
| Anthropic | โมเดลที่มีการควบคุม effort โดยตรง | Opus 5 / 5.5 / Fable 5.1 / Mythos 5.1 ที่รองรับ พร้อมฟีเจอร์เบตาของผู้ให้บริการ | โมเดล Claude ที่รองรับ | ไม่มีอะแดปเตอร์ที่เก็บแบบเนทีฟ ให้ใช้ RAG |
| Google | ระดับของ Gemini 3; Gemini 2.5 ยังคงใช้งบประมาณเฉพาะผู้ให้บริการ | ไม่รองรับ | โมเดลข้อความ Gemini ที่รองรับ | โมเดลข้อความ Gemini ที่รองรับและที่เก็บค้นหาไฟล์ที่มีอยู่แล้ว |
| xAI | Grok 4.7 / 4.6: `Auto`, `Low`, `Medium`, `High`, `XHigh` | ไม่รองรับ | ไม่มีอะแดปเตอร์ร่วม | ไม่มีอะแดปเตอร์ร่วม |
| DeepSeek | Flash / V4 Pro: `Auto`, `None`, `Minimal`/`Low`, `Medium`/`High`/`XHigh`, `Max`; จับคู่กับ Low/High/Max เนทีฟ | ไม่รองรับ | ไม่มีอะแดปเตอร์ร่วม | ไม่มีอะแดปเตอร์ร่วม |
| Perplexity | `Auto` หรือ `Minimal`/`Low`/`Medium`/`High`/`XHigh`/`Max` ที่โมเดลรองรับ; Sonar ไม่รองรับ effort แบบระบุชัด | ไม่รองรับ | Agent `web_search` | ไม่มีอะแดปเตอร์ร่วม |
| บริการอื่น | ยังใช้การตั้งค่าเฉพาะผู้ให้บริการเดิมได้ ตัวเลือกร่วมเหล่านี้ต้องมีอะแดปเตอร์ | อะแดปเตอร์ชุดนี้ไม่รองรับ | ไม่มีอะแดปเตอร์ร่วม | ไม่มีอะแดปเตอร์ร่วม |

อะแดปเตอร์ตรวจสอบข้อจำกัดของโมเดล ระดับ ช่องทาง และชุดตัวเลือกที่ทราบก่อนส่ง ส่วนกฎเฉพาะโมเดลที่ตรวจสอบในเครื่องไม่ได้จะให้ผู้ให้บริการตรวจสอบ โดยเฉพาะ **Google ไม่สามารถใช้การค้นหาเว็บและการค้นหาไฟล์ร่วมกันในคำขอเดียว** ไลบรารีจะไม่ลบฟีเจอร์ ลดระดับการให้เหตุผล ละเลยข้อจำกัดโดเมน หรือเปลี่ยนไปใช้บริการค้นหาภายนอกโดยไม่แจ้ง เมื่อรองรับ เครื่องมือของผู้ให้บริการใช้ร่วมกับฟังก์ชันฝั่งไคลเอนต์ที่ลงทะเบียนไว้ได้ รอบเครื่องมือของ Run ยังเป็นไปตามนโยบายฟังก์ชันและ `WithMaxRounds`

`CreateRequest(...).With...` เก็บตัวเลือกใน builder อิสระ การใช้ซ้ำจะนำตัวเลือกไปใช้กับแต่ละการทำงานและรอบเครื่องมือ ส่วน `service.WithReasoning`, `service.WithWebSearch` และ `service.WithFileSearch` เดิมยังคืนชนิด service เดิมและใช้ตัวเลือกกับคำขอเชิงตรรกะถัดไป ใช้ต่อได้กับ `IAIRequestFeatureService` และ RAG wrapper ทั้งสองรูปแบบไม่รับประกันการทำงานพร้อมกันบน service เดียว

การติดตั้งใช้งาน `IAIService` แบบกำหนดเองยังเข้ากันได้ โดยเลือกเปิดความสามารถนี้ผ่าน `IAIRequestFeatureService` หากเรียกเมธอดช่วยเหล่านี้กับการติดตั้งใช้งานที่ไม่มีความสามารถดังกล่าว ระบบจะโยนข้อยกเว้นอย่างชัดเจน API สำหรับคำตอบสมบูรณ์ สตรีม และการตั้งค่าเฉพาะผู้ให้บริการเดิมยังใช้ได้ ดูเรื่องการยกเลิก การสังเกตผลลัพธ์ และการส่งคำสั่งเพิ่มเติมใน [การควบคุม Run](execution-api-transition.md)

โปรโตคอลผู้ให้บริการ: [การเปลี่ยนการให้เหตุผลของ OpenAI](https://developers.openai.com/api/docs/guides/reasoning#change-reasoning-mid-conversation), [เครื่องมือ OpenAI](https://developers.openai.com/api/docs/guides/tools), [การเปลี่ยน effort ของ Anthropic](https://platform.claude.com/docs/en/build-with-claude/effort#change-effort-mid-conversation), [ค้นหาเว็บของ Anthropic](https://platform.claude.com/docs/en/agents-and-tools/tool-use/web-search-tool), [อ้างอิงข้อมูลด้วย Google Search](https://ai.google.dev/gemini-api/docs/google-search), [Google File Search](https://ai.google.dev/gemini-api/docs/file-search)

ระดับ effort ของ Perplexity ขึ้นอยู่กับโมเดลที่เลือกจริง และเซิร์ฟเวอร์อาจปฏิเสธชุดตัวเลือกที่ไม่เข้ากัน ไม่รองรับ `None` การค้นหาเริ่มต้นและเครื่องมือ preset/profile เป็นการตั้งค่าถาวรของผู้ให้บริการ ตัวเลือกคำขอร่วมไม่ได้ปิดค่าเริ่มต้นเหล่านี้

Perplexity: [Perplexity Agent API การค้นหา และ embedding](perplexity.md).
