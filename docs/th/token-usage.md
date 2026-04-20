# การใช้ token

การใช้ token บอกว่า request ที่ส่งไปยัง model ใช้ token ไปเท่าไรในส่วน input, output, cache และ reasoning ใน Mythosia.AI ข้อมูลนี้จะอยู่ใน `TokenUsage` ของ streaming event

เรื่องนี้สำคัญมากเมื่อคำตอบไม่ได้จบด้วยการเรียก LLM แค่ครั้งเดียว คำตอบทั่วไปมักมีเพียงหนึ่ง round แต่ agent หรือ flow ที่มี function calling อาจเรียก model ก่อน จากนั้นเรียก tool แล้วค่อยเรียก model อีกครั้งพร้อมผลลัพธ์จาก tool ดังนั้นจึงมีตัวเลขสองแบบที่ควรแยกกันให้ชัด

- `RoundUsage` คือ usage ของ LLM round เดียวที่เพิ่งจบ
- `Completion.Usage` คือ usage รวมของ stream ทั้งหมด

## Round คืออะไร?

"Round" คือการรับ-ส่งข้อมูลหนึ่งรอบระหว่าง app กับ model: app ส่ง prompt ไป model ตอบกลับมา และการรับ-ส่งนั้นก็จบลง ข้อความ chat ทั่วไปคือหนึ่ง round พอดี

Function calling และ agent จะสร้าง round เพิ่มขึ้นโดยอัตโนมัติ ลองดูตัวอย่างที่ชัดเจน — ผู้ใช้ถามว่า: *«ตอนนี้กรุงเทพฯ อากาศเป็นยังไง?»*

**Round 1 — ตัดสินใจเลือก tool**

App ส่งข้อความของผู้ใช้ไปที่ model model ไม่รู้สภาพอากาศปัจจุบัน จึงไม่ตอบตรงๆ แต่ส่งคำขอให้เรียกฟังก์ชันแทน: *«กรุณาเรียก `GetWeather("Bangkok")`»* — ที่นี่คือจุดที่ model จบการตอบกลับ

**ระหว่าง round**

App รัน `GetWeather("Bangkok")` และได้รับผลลัพธ์: `«15°C, มีเมฆมาก»`

**Round 2 — คำตอบสุดท้าย**

App ส่งผลลัพธ์ของฟังก์ชันกลับไปหา model เป็นข้อความใหม่ ตอนนี้ model มีข้อมูลครบแล้วจึงเขียนคำตอบสุดท้ายให้ผู้ใช้: *«ตอนนี้กรุงเทพฯ อุณหภูมิ 15°C มีเมฆมาก»*

ข้อความหนึ่งข้อความของผู้ใช้ก่อให้เกิด LLM สองรอบ หาก model ต้องการเรียก tool อีกตัวหนึ่ง ก็จะมี round ที่สาม

`RoundUsage` จะถูก emit หลังจาก round แต่ละ round และมีเฉพาะ token ของ round นั้น `Completion.Usage` จะถูก emit หนึ่งครั้งเมื่อทุกอย่างเสร็จสิ้น และมี token รวมของทุก round

## ทำไมถึงต้องใช้

ถ้าเป็นตัววัด context ใน UI chat โดยทั่วไปควรใช้ `RoundUsage.Usage.TotalTokens` ล่าสุด ค่านี้ใกล้เคียงที่สุดกับคำถามว่า "ถ้าคุยต่อทันที input รอบถัดไปที่จะส่งเข้า model จะใหญ่แค่ไหน"

ถ้าเป็น log, diagnostics หรือการวิเคราะห์ cost ให้ใช้ `Completion.Usage.TotalTokens` เพราะค่านี้เป็นยอดรวมทั้ง run แม้ function calling หรือ agent จะทำให้เกิดหลาย round ก็ตาม

ถ้าเป็นการจูน performance ฟิลด์ cache และ reasoning จะช่วยดูว่า provider reuse input จาก cache หรือไม่ และ model ใช้ token เพิ่มกับ reasoning ภายในมากแค่ไหน

## รูปแบบ event

| Event | ความหมาย | เหมาะกับ |
|---|---|---|
| `StreamingContentType.RoundUsage` | Usage ของ LLM round ที่เพิ่งจบ | ตัววัด context ใน UI, debug ราย round |
| `StreamingContentType.Completion` | Event สุดท้ายพร้อม usage รวม | Log, diagnostics, รายงาน cost |

`RoundUsage.Usage` ไม่ใช่ค่าสะสม ถ้า round 1 ใช้ 10,100 token และ round 2 ใช้ 14,000 token ค่า `Completion.Usage.TotalTokens` สุดท้ายอาจเป็น 24,100 แต่ `RoundUsage.Usage.TotalTokens` ล่าสุดยังเป็น 14,000

| Property | ความหมาย |
|---|---|
| `RoundIndex` | ลำดับ LLM round เริ่มจาก 1 |
| `IsFinalRound` | เป็น `true` เมื่อ round นี้เป็น LLM round สุดท้ายของ stream |

Usage event จะถูก emit เมื่อ provider ส่ง usage data กลับมา ไม่จำเป็นต้องเปิด `IncludeMetadata = true` เพื่อรับ event เหล่านี้

## Usage รวมตอนจบ

ใช้ `Completion.Usage` เมื่อต้องการ usage รวมของ streaming request ทั้งหมด

```csharp
await foreach (var chunk in service.StreamAsync("อธิบาย quantum computing", StreamOptions.FullOptions))
{
    if (chunk.Type == StreamingContentType.Text)
        Console.Write(chunk.Content);

    if (chunk.Type == StreamingContentType.Completion && chunk.Usage is not null)
    {
        Console.WriteLine($"Input:  {chunk.Usage.InputTokens}");
        Console.WriteLine($"Output: {chunk.Usage.OutputTokens}");
        Console.WriteLine($"Total:  {chunk.Usage.TotalTokens}");
    }
}
```

ถ้ามี LLM round เดียว ค่านี้มักใกล้เคียงกับ `RoundUsage` แต่ถ้าเป็น agent ค่านี้จะเป็นผลรวมของทุก LLM round

## Token meter ใน UI

สำหรับตัววัดขนาด context ให้ใช้ `RoundUsage` ล่าสุด

```csharp
await foreach (var chunk in service.StreamAsync(message, StreamOptions.FullOptions))
{
    if (chunk.Type == StreamingContentType.RoundUsage && chunk.Usage is not null)
    {
        UpdateContextTokenMeter(chunk.Usage.TotalTokens);

        if (chunk.IsFinalRound)
            MarkTokenMeterAsFinal();

        continue;
    }

    if (chunk.Type == StreamingContentType.Text)
        AppendToChat(chunk.Content);
}
```

LLM round สุดท้ายจะเห็นสถานะล่าสุดของบทสนทนา รวมถึงผลลัพธ์จาก tool ที่ถูกเพิ่มระหว่าง run ดังนั้น `RoundUsage.TotalTokens` ล่าสุดจึงเหมาะที่สุดสำหรับ UI chat

## Function Calling และ agent

ใน flow ที่มี function calling model อาจทำงานหลายครั้ง ให้อ่าน `RoundUsage` ทุกครั้ง เก็บค่าล่าสุดไว้ใช้กับ UI แล้วใช้ `Completion.Usage` ตอนท้ายสำหรับยอดรวม

```csharp
TokenUsage? latestRound = null;
TokenUsage? cumulative = null;

await foreach (var chunk in service.StreamAsync(message, StreamOptions.WithFunctions))
{
    if (chunk.Type == StreamingContentType.RoundUsage && chunk.Usage is not null)
    {
        latestRound = chunk.Usage;
        Console.WriteLine($"Round {chunk.RoundIndex}: {latestRound.TotalTokens} tokens");
        continue;
    }

    if (chunk.Type == StreamingContentType.Completion)
        cumulative = chunk.Usage;
}
```

## Cache และ reasoning

ถ้า provider ส่งข้อมูลมา `TokenUsage` จะมี field เพิ่มเติมเกี่ยวกับ cache และ reasoning

| Property | ความหมาย |
|---|---|
| `InputTokens` | Token ใน prompt/input |
| `OutputTokens` | Token ที่ model สร้าง |
| `TotalTokens` | Input + output ในขอบเขตของ event นั้น |
| `CachedInputTokens` | Input token ที่ถูกใช้จาก cache |
| `CacheCreationTokens` | Token ที่ถูกเขียนเข้า cache |
| `ReasoningTokens` | Token ที่ใช้กับ reasoning ภายในที่ไม่แสดง |
| `VisibleOutputTokens` | Output token ที่ไม่นับ reasoning |

## หมายเหตุของแต่ละ provider

แต่ละ provider แนบ usage data มากับ stream chunk คนละแบบ Mythosia.AI จะ normalize ให้เป็น `RoundUsage` และ `Completion.Usage`

Gemini เป็นเคสที่ต้องระวังที่สุด usage อาจมากับ text หรือ status chunk และบางครั้งอาจมาหลัง function-call chunk ด้วย library จึงอ่าน stream ต่อให้พอเก็บ usage ก่อนขยับไป round ถัดไป

ฝั่ง consumer ควรอ่าน event ที่ normalize แล้วอย่าง `RoundUsage` และ `Completion.Usage` แทนการ parse metadata เฉพาะ provider เอง
