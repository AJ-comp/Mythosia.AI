# Perplexity: คำตอบพร้อมแหล่งอ้างอิง การค้นหา และ embedding

ใช้ Perplexity เมื่อคำตอบต้องอาศัยข้อมูลล่าสุดและมีแหล่งอ้างอิงให้ผู้อ่านตรวจสอบ `PerplexityService` เรียก Agent API ส่วนการค้นหาและ embedding แบบแยกใช้สร้างการดึงเอกสารให้โมเดลตอบคำถามที่คุณเลือกได้

## เลือกงานที่ต้องทำก่อน

การตอบด้วยข้อมูลล่าสุด การดึงรายการเว็บ และสร้างเวกเตอร์ให้ดัชนีเอกสารเป็นคนละงาน เลือกองค์ประกอบที่รับผิดชอบ แทนเรียกโมเดลตอบคำถามทุกครั้งที่ค้นหา

| สิ่งที่ต้องการ | องค์ประกอบ |
| --- | --- |
| คำตอบจากการวิจัยพร้อมแหล่งอ้างอิง | `PerplexityService` |
| หน้าเว็บสำหรับโมเดลหรือหน้าจออื่น | `PerplexitySearchClient` |
| เวกเตอร์ข้อความอิสระสำหรับ RAG | `PerplexityEmbeddingProvider` |
| เวกเตอร์ที่ใช้บริบทของส่วนข้างเคียงในเอกสารเดียวกัน | `PerplexityContextualizedEmbeddingProvider` |

ติดตั้ง `Mythosia.AI` และเพิ่ม `Mythosia.AI.Rag` สำหรับ embedding เตรียม API key และ `HttpClient` ที่แอปดูแล ตัวอย่างใช้ `apiKey`, `httpClient`, `cancellationToken` จากแอป

## ตอบด้วย preset ของ Agent

Preset รวมโมเดล คำสั่ง เครื่องมือ ระดับการคิด และงบประมาณที่ผู้ให้บริการดูแล ใช้ `Fast` สำหรับค้นหาสั้น ๆ, `Low` สำหรับวิจัยทั่วไป, `Medium` สำหรับเปรียบเทียบหลายขั้น, `High` / `XHigh` สำหรับงานเชิงลึก `WideResearch` ใช้สำหรับการค้นคว้าในวงกว้าง หากคาดว่างานจะใช้เวลานาน แนะนำให้ทำงานเบื้องหลัง ค่าเหล่านี้ไม่ใช่ ID โมเดล

```csharp
using Mythosia.AI.Models.Perplexity;
using Mythosia.AI.Services.Perplexity;

var service = new PerplexityService(apiKey, httpClient)
    .WithPerplexityOptions(new PerplexityAgentOptions
    {
        Preset = PerplexityPreset.Low
    });

await using var run = await service.StartRunAsync(
    "เปรียบเทียบวิธีรีไซเคิลแบตเตอรี่ล่าสุดและระบุแหล่งอ้างอิง",
    onText: text => Console.Write(text),
    cancellationToken: cancellationToken);
string answer = (await run.Result).Text;
foreach (var source in run.Citations)
    Console.WriteLine(source.Url);
```

ใช้ `GetCompletionAsync` สำหรับคำตอบ, `StreamAsync` ของบริการสำหรับ streaming เดิม หรือ `StartRunAsync` เพื่อดูผลและยกเลิก `(await run.Result).Text` รวมข้อความที่ส่งออก `run.Citations` และ `LastCitations` เก็บแหล่งอ้างอิงแม้ไม่อ่านเหตุการณ์ citation เหตุการณ์ reasoning มีเฉพาะเนื้อหาที่ผู้ให้บริการเปิดเผยและขึ้นกับโมเดล

`AIRunResult.RequestedModel` คือโมเดลเดียวที่ระบุชัดเจนและส่งในคำขอ โดยบันทึกเมื่อเริ่ม รวมถึงการตั้งค่าโมเดลแทนของผู้ให้บริการ หากใช้ preset, profile หรือการเลือกโมเดลฝั่งเซิร์ฟเวอร์โดยไม่ส่งฟิลด์โมเดลเดียวที่ระบุชัดเจน ค่าจะเป็น `null` (เช่นรายการ `Models` ของ Perplexity) ค่านี้แยกจากโมเดลจริงในคำตอบที่อยู่ใน `Model`

## ควบคุมการวิจัยและเครื่องมือ

`WithPerplexityOptions(...)` ตั้งค่าคงอยู่บนบริการและคัดลอกในแต่ละคำขอเชิงตรรกะ `WithReasoning(...)` และ `WithWebSearch(...)` ใช้กับคำขอถัดไป รวมรอบฟังก์ชันฝั่งแอปและการซ่อมผลลัพธ์แบบมีชนิด การเขียนคำค้น RAG ภายในใหม่ไม่รับการค้นหาของคำตอบสุดท้าย

`UsePreset(...)` เลือก preset ได้โดยตรง Preset/profile เลือกโมเดลของตนและใช้ `ModelOverride` เปลี่ยนได้ `DisableWebSearch` นำออกเฉพาะเครื่องมือเริ่มต้นของ adapter ไม่รับประกันปิดการค้นหาใน preset ระดับตามโมเดลคือ `Minimal`, `Low`, `Medium`, `High`, `XHigh`, `Max` โดยปฏิเสธ `None` และการระบุ effort กับ Sonar โดยตรง `DisableReasoning` ภายในใช้ระดับต่ำที่รองรับหรือไม่ส่งค่า ไม่รับประกันปิดการคิดทั้งหมด

| ตัวเลือก | การใช้งาน |
| --- | --- |
| `Preset` / `ModelOverride` | เลือกชุดวิจัยหรือเปลี่ยนโมเดลด้วย ID ผู้ให้บริการ/โมเดล |
| `MaxSteps` | จำกัดลูปฝั่งผู้ให้บริการ 0 ใช้ค่าเริ่มต้น แยกจาก `WithMaxRounds` สำหรับการต่อรอบฟังก์ชันในแอป |
| `ReasoningEffort` | ปรับการคิด `Auto` ไม่ส่งค่าทับ ระดับที่ใช้ได้ขึ้นกับโมเดลจริง |
| `DisableWebSearch` / `Tools` | ควบคุมเครื่องมือเว็บเริ่มต้นของ adapter และเครื่องมือฝั่งผู้ให้บริการที่ระบุเอง |
| `Models` | กำหนดโมเดลสำรอง 1–5 ตัวตามลำดับ แทนโมเดลเดี่ยว ทุกตัวต้องรองรับฟีเจอร์ที่ขอ |
| `Profile` | ใช้ชุดตั้งค่าที่บันทึกบนเซิร์ฟเวอร์และตรึงรุ่นได้ ใช้ร่วมกับ `Preset` ไม่ได้ |
| `ServiceTier` | ขอการประมวลผลปกติ flex หรือ priority ผู้ให้บริการอาจละเว้นระดับที่โมเดลไม่รองรับ |
| `Skills` | ส่ง skill แบบในตัว inline หรือแบบกำหนดเองที่อัปโหลดแล้ว ทรัพยากรกำหนดเองเป็นของบัญชี Perplexity |
| `LanguagePreference` / `PromptCacheKey` | กำหนดภาษาหรือคำใบ้สำหรับเส้นทาง cache โดยไม่รับประกัน cache hit |
| `PreviousResponseId` / `Store` | ต่อจากคำตอบที่เสร็จหรือควบคุมการเรียกดู ใช้ `StatelessMode` และส่งเฉพาะเทิร์นใหม่ `Store = false` ไม่ปิดการเก็บข้อมูลของผู้ให้บริการ |

`PerplexityHostedTool` รับ `Type` ที่รองรับและ `Parameters` JSON ตามเอกสาร: `web_search`, `fetch_url`, `finance_search`, `people_search`, `sandbox`, `mcp` MCP และ connector ที่จัดการให้รันผ่านผู้ให้บริการ ข้อมูลยืนยันตัวตน สิทธิ์ และทรัพยากรบัญชีต้องตรงกับการเชื่อมต่อ ฟังก์ชันแอปลงทะเบียนผ่าน `Functions` / ตัวสร้างฟังก์ชัน ขั้นฝั่งผู้ให้บริการกับ handler ในแอปมีผู้รันต่างกัน

สร้างเครื่องมือด้วย `PerplexityHostedTools.WebSearch`, `FetchUrl`, `Sandbox`, `FinanceSearch`, `PeopleSearch`, `Mcp`, `Connector` MCP รันโดยไม่รออนุมัติ จึงควรจำกัด `allowedTools` เมื่อต้องการ Connector เป็นฟีเจอร์ preview ที่อ้างถึงการเชื่อมต่อที่มีอยู่แล้ว

```csharp
service.WithPerplexityOptions(new PerplexityAgentOptions
{
    Preset = PerplexityPreset.Low,
    MaxSteps = 8,
    Tools = new List<PerplexityHostedTool>
    {
        PerplexityHostedTools.WebSearch(),
        PerplexityHostedTools.FetchUrl()
    }
});
string comparison = await service.GetCompletionAsync(
    "อ่านเอกสารโครงการและเปรียบเทียบความสามารถที่เกี่ยวข้อง");
```

การรองรับเครื่องมือ reasoning ภาพ และ schema ขึ้นกับโมเดล `WithFileSearch` ไม่ใช่ adapter ของ vector store Perplexity ไฟล์ sandbox ไฟล์แนบ และข้อมูล MCP เป็นทรัพยากรแยก ไม่กลายเป็นคลังค้นหาไฟล์ร่วมโดยอัตโนมัติ

## แหล่งอ้างอิง ภาพ และคำตอบที่มีโครงสร้าง

ใช้ completion หรือ streaming แบบมีชนิดเมื่อต้องการฟิลด์ JSON Adapter ส่ง schema ต้นทางและคงการซ่อมผลลัพธ์ เก็บรายการตอบกลับและ ID เครื่องมือเพื่อต่อรอบ อย่าลบหรือสลับประวัติโปรโตคอลเอง ภาพป้อนผ่าน `Message` และ `ImageContent` เป็นไบต์ JPEG/PNG/WebP/GIF หรือ HTTPS URL ตามโมเดล นี่ไม่ใช่คำขอสร้างภาพ

ร่องรอยคำตอบเดิมยังอยู่ในเมทาดาทาของประวัติ แต่คำขอถัดไปส่งซ้ำเฉพาะรายการอินพุตที่อนุญาตคือ `message`, `function_call` และ `function_call_output` หากต้องการต่อสถานะงานที่ผู้ให้บริการโฮสต์ไว้ทั้งหมด ให้ใช้ `PreviousResponseId`

Citation อาจชี้ไปยังเว็บหรือแหล่งอื่น ตำแหน่งเป็นของแต่ละคำตอบและส่วนเนื้อหา ไม่ใช่ผล Run ที่ต่อกัน เก็บ URL และชื่อเพื่อแสดงและตรวจสอบ การมีแหล่งอ้างอิงไม่ยืนยันทุกข้อความที่โมเดลสร้าง

## ให้งานที่ใช้เวลานานทำต่อ

ใช้การรันเบื้องหลังของผู้ให้บริการเพื่อให้งานต่อหลังขาดการเชื่อมต่อชั่วคราว หรือเรียกดูภายหลังด้วย ID `AIRun` ในแอปควบคุม client ปัจจุบัน ส่วนคำตอบเบื้องหลังมีวงจรชีวิตบนเซิร์ฟเวอร์แยก การหยุดอ่าน stream หยุดเพียงการดูผล ต้องยกเลิกงานฝั่งผู้ให้บริการอย่างชัดเจนเพื่อหยุดงานระยะไกล

```csharp
var researcher = new PerplexityService(apiKey, httpClient)
    .UsePreset(PerplexityPreset.High);
var job = await researcher.StartBackgroundAsync(
    "เปรียบเทียบวิธีรีไซเคิลแบตเตอรี่ล่าสุดและระบุแหล่งอ้างอิง", cancellationToken: cancellationToken);
Console.WriteLine(job.Id);
var completed = await job.WaitForCompletionAsync(
    cancellationToken: cancellationToken);
if (completed.Status != "completed")
    throw new InvalidOperationException(completed.Error ?? completed.Status);
Console.WriteLine(completed.Text);
```

`StartBackgroundAsync` เก็บอินพุตโดยไม่เพิ่มประวัติและปฏิเสธฟังก์ชันในแอปที่เปิดอยู่หรือ `Store = false` ใช้ `GetResponseAsync` ดูครั้งเดียว หรือ `WaitForCompletionAsync` ตรวจจนจบ เก็บ `Id` และ `LastSequenceNumber` แล้วต่อด้วย `ResumeBackgroundRun(id).StreamAsync(startingAfter: cursor)` ส่วน `CancelAsync` ยกเลิกงานระยะไกล การยกเลิก token อ่าน/ตรวจสถานะหยุดเฉพาะ client นั้น `LastResponse` มีข้อความ สถานะ usage citation และ `OutputJson` ตรวจสถานะสุดท้ายก่อนใช้คำตอบ

อ่านไฟล์ sandbox ด้วย `ListFilesAsync` และ `DownloadFileAsync(fileId)` บริการยังมี `GetAgentResponseAsync`, `GetResponseFilesAsync`, `GetResponseFileContentAsync` สำหรับผลลัพธ์ของคำตอบ ไม่ใช่สร้างหรือค้น vector store

สำหรับ skill Office ในตัว ให้ใช้เส้นทางเบื้องหลังในคู่มือนี้: `StartBackgroundAsync` จากนั้นใช้ `WaitForCompletionAsync` / `GetResponseAsync` และเมธอดไฟล์ ร่องรอยเครื่องมือภายในของคำตอบเหล่านี้อาจแยกไม่ออกจากการเรียกฟังก์ชันท้องถิ่นทั่วไป

การส่ง เรียกดู ยกเลิก และเชื่อม stream ใหม่ในเบื้องหลังไม่เปิด `SteerAsync` ระหว่างตอบหรือเครื่องมือ client แบบ asynchronous ต้นทาง การเชื่อมใหม่อ่านคำตอบเดิมต่อโดยไม่ส่งงานซ้ำ เก็บ ID คำตอบและ cursor ที่ได้รับ

## ค้นหาโดยไม่สร้างคำตอบ

ใช้ `PerplexitySearchClient` ดึงเว็บเพื่อจัดอันดับเอง แสดงในหน้าจอ หรือให้ LLM อื่น โดยไม่เรียกโมเดลตอบคำถามหรือเปลี่ยนประวัติ `PerplexityService`

```csharp
var search = new PerplexitySearchClient(apiKey, httpClient);
var pages = await search.SearchAsync(
    "วิธีรีไซเคิลแบตเตอรี่",
    new PerplexitySearchOptions
    {
        MaxResults = 5,
        DomainFilter = new[] { "nature.com", "science.org" },
        ContentSize = PerplexitySearchContentSize.Medium
    }, cancellationToken);
foreach (var page in pages.Results)
    Console.WriteLine($"{page.Rank}: {page.Title} — {page.Url}");
```

`SearchAsync` รับคำค้นเดียวหรือหลายคำค้น มี Web/People ประเทศ โดเมน ภาษา ช่วงวันที่เผยแพร่/อัปเดต และความใหม่ เลือก `ContentSize` หรือ `MaxTokens` / `MaxTokensPerPage` อย่างใดอย่างหนึ่ง ผลมีลำดับ ชื่อ URL ข้อความย่อ และวันที่ของผู้ให้บริการ ลำดับไม่ใช่คะแนนความเกี่ยวข้อง

`ContentSize` รองรับเฉพาะการค้นหา Web ให้ละไว้เมื่อใช้ People โดยไคลเอนต์จะปฏิเสธการจับคู่นี้ก่อนส่งคำขอ

## ใช้เวกเตอร์กับดัชนีของคุณ

Embedding มาตรฐานประมวลผลข้อความแยกและใช้ `IEmbeddingProvider` จึงต่อกับตัวสร้าง RAG ได้ แบบ contextual เก็บลำดับส่วนข้างเคียงและกลุ่มเอกสาร ใช้ API แยกเพื่อไม่แผ่เอกสารที่ไม่เกี่ยวข้องรวมกัน

| ค่าคงที่โมเดล | ID ผู้ให้บริการ | จำนวนมิติเริ่มต้น |
| --- | --- | --- |
| `Standard0_6B` | `pplx-embed-v1-0.6b` | 1024 |
| `Standard4B` | `pplx-embed-v1-4b` | 2560 |
| `Context0_6B` | `pplx-embed-context-v1-0.6b` | 1024 |
| `Context4B` | `pplx-embed-context-v1-4b` | 2560 |

```csharp
using Mythosia.AI.Rag;
using Mythosia.AI.Rag.Embeddings;

var rag = service.WithRag(builder => builder
    .AddText("คืนสินค้าได้ภายใน 30 วัน", id: "returns")
    .UsePerplexityEmbedding(apiKey, httpClient));
string policy = await rag.GetCompletionAsync("ฉันคืนสินค้าได้ถึงเมื่อไร");
```

```csharp
var contextual = new PerplexityContextualizedEmbeddingProvider(
    apiKey, httpClient, PerplexityEmbeddingModels.Context0_6B);
var documents = new[]
{
    new[] { "คืนสินค้าได้ภายใน 30 วัน", "เก็บใบเสร็จไว้เมื่อขอคืนเงิน" },
    new[] { "การจัดส่งปกติใช้เวลาสามวัน", "การจัดส่งด่วนให้บริการในวันทำการ" }
};
var documentVectors = await contextual.GetDocumentEmbeddingsAsync(
    documents, cancellationToken);
float[] queryVector = await contextual.GetQueryEmbeddingAsync(
    "ฉันคืนสินค้าได้ถึงเมื่อไร", cancellationToken);
```

ใช้โมเดล จำนวนมิติ และการเข้ารหัสเดียวกันสำหรับเอกสารกับคำค้น `GetQueryEmbeddingAsync` ส่งคำค้นหนึ่งรายการเป็นเอกสารแยกไปยังโมเดล contextual เดิม ผลเก็บลำดับเอกสารและส่วนย่อย ไม่ต่อกับตัวสร้าง RAG อินพุตแบนอัตโนมัติ

API float ถอดเวกเตอร์ base64 signed-int8 และ normalize เพื่อคำนวณความคล้าย API binary แบบชัดเจนคืนบิตที่แพ็กและใช้ระยะ Hamming โดยไม่แปลงเป็นพิกัด float เงียบ ๆ มิติเต็มคือ 1024 สำหรับ 0.6B และ 2560 สำหรับ 4B การลดมิติตามข้อจำกัดผู้ให้บริการ ข้อจำกัด batch ความยาว token รวม และอัตราบัญชียังใช้ตามเดิม

API binary คือ `GetBinaryEmbeddingAsync` / `GetBinaryEmbeddingsAsync` และแบบ contextual `GetBinaryDocumentEmbeddingsAsync` / `GetBinaryQueryEmbeddingAsync` ส่วน `PerplexityBinaryEmbedding` มี `Dimensions`, สำเนา `ToArray()` และ `HammingDistance` ค่ายิ่งน้อยยิ่งคล้าย มิติ binary ต้องหารแปดลงตัว สูงสุด 512 ข้อความมาตรฐาน หรือ 512 เอกสาร/16,000 ส่วน contextual ผู้ให้บริการตรวจขีดจำกัด 32K token ต่อข้อความ/เอกสาร และ 120K รวม

## ย้ายโค้ด Sonar เดิม

รุ่นนี้นำ adapter เดิมออกก่อนวันที่ประกาศปิด endpoint 27 กันยายน 2026 `PerplexityService` เรียก `/v1/agent` และ `AIModels.Perplexity.Sonar` หมายถึง `perplexity/sonar` ฟังก์ชันค้นหาและชนิดคำตอบเฉพาะ Sonar เดิมถูกนำออก ให้ใช้ completion/Run/citation ร่วม preset Agent และ `PerplexitySearchClient` แยก

จับคู่ Sonar → `Fast`, Sonar Pro → `Low`, Sonar Reasoning Pro → `Medium`, Sonar Deep Research → `High` โดยไม่รับประกันข้อความ ค่าใช้จ่าย หรือพฤติกรรมเหมือนเดิม Preset เปลี่ยนได้ตามผู้ให้บริการ หากต้องการตรึงให้ระบุโมเดลหรือ profile พร้อมรุ่น

ไม่รองรับ steering ต้นทาง เครื่องมือ client แบบ asynchronous ต้นทาง หรือ `CachePreservation.Required` ส่วน Router/Gateway อยู่นอกขอบเขต การใช้งานขึ้นกับผู้ให้บริการ โมเดล และบัญชี คู่มือนี้ไม่อ้างว่าทุกชุดผ่าน API จริงแบบมีค่าใช้จ่ายแล้ว

Profile, skill แบบกำหนดเอง และ connector ต้องมีทรัพยากรที่ลงทะเบียนไว้ในบัญชีแล้ว รูปแบบคำขอผ่าน unit test แต่ยังไม่ได้ยืนยันการเรียก API จริงที่สำเร็จด้วยทรัพยากรเหล่านี้

[Agent API](https://docs.perplexity.ai/docs/agent-api/quickstart) · [Search API](https://docs.perplexity.ai/docs/search/quickstart) · [Embeddings](https://docs.perplexity.ai/docs/embeddings/quickstart) · [Migration](https://docs.perplexity.ai/docs/agent-api/migrate-from-sonar/how-to) · [2026-09-27](https://community.perplexity.ai/t/sonar-is-moving-to-the-agent-api/5802)
