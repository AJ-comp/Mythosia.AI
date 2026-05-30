# แนวคิดหลัก

หน้านี้รวบรวมแนวคิดพื้นฐานที่ถูกอ้างถึงตลอดส่วนอื่นๆ ของเอกสาร แนวคิดเพิ่มเติมจะถูกเพิ่มเข้ามาในภายหลัง

## Round คืออะไร?

> [!NOTE]
> **Round** คือการรับ-ส่งข้อมูลครบหนึ่งรอบระหว่าง app กับ model — app ส่ง prompt ไป model ตอบกลับ และการรับ-ส่งนั้นก็คือหนึ่ง round ข้อความ chat ทั่วไปคือ 1 round ส่วน function calling และ agent สามารถร้อย round หลายๆ รอบเข้าด้วยกันสำหรับข้อความเดียวของผู้ใช้ได้

### กรณีที่ง่ายที่สุด: 1 round

ใน chat ทั่วไป การสนทนาทั้งหมดเกิดขึ้นในหนึ่ง round

```
app  →  "2 บวก 2 เท่ากับเท่าไร?"  →  model
app  ←  "เท่ากับ 4"                 ←  model
```

`RoundUsage` จะ emit หนึ่งครั้งพร้อม token ของ round นี้ `Completion.Usage` จะ emit ที่ stream จบด้วยยอดรวมเท่ากัน เพราะมีเพียง round เดียว

### หลาย round: function calling

Round จะทวีจำนวนขึ้นเมื่อ model ไม่สามารถตอบเองได้ สมมติว่าผู้ใช้ถามว่า *«ตอนนี้กรุงเทพฯ อากาศเป็นยังไง?»* — model ไม่มีทางรู้สภาพอากาศปัจจุบัน จึงต้องเรียก tool

**Round 1 — model ตัดสินใจเรียก tool**

App ส่งข้อความของผู้ใช้พร้อมรายการ tool ที่ลงทะเบียนไว้ (เช่น `GetWeather`) ให้ model ณ จุดนี้ model เห็นบทสนทนาดังนี้:

```
system: คุณคือ weather assistant คุณสามารถเรียก GetWeather(city) ได้
user:   ตอนนี้กรุงเทพฯ อากาศเป็นยังไง?
```

แทนที่จะเขียนคำตอบสุดท้าย model ส่ง **คำขอเรียก tool** กลับมา:

```
tool_call: GetWeather(city="Bangkok")
```

เทิร์นของ model จบลง และ round 1 ก็จบเช่นกัน `RoundUsage` จะ emit พร้อม token ที่ใช้ใน round 1 **ยังไม่มีคำตอบสุดท้ายสำหรับผู้ใช้**

**ระหว่าง round — app รันฟังก์ชัน**

ขั้นตอนนี้ **ไม่ใช่** การเรียก LLM runtime ของ Mythosia.AI จะเรียก `GetWeather` ที่คุณลงทะเบียนไว้โดยตรง และได้รับผลลัพธ์ `«15°C, มีเมฆมาก»` กลับมา ไม่มีการใช้ token

**Round 2 — model เขียนคำตอบสุดท้าย**

App เพิ่ม **function_call ที่ model ส่งออกมาใน Round 1 พร้อมกับผลลัพธ์ของ tool** เข้าไปในบทสนทนา และเรียก model **เป็นครั้งที่สอง** ตอนนี้ model เห็นดังนี้:

```
system:      คุณคือ weather assistant คุณสามารถเรียก GetWeather(city) ได้
user:        ตอนนี้กรุงเทพฯ อากาศเป็นยังไง?
assistant:   [เรียก GetWeather(city="Bangkok") ไปแล้ว]
tool_result: 15°C, มีเมฆมาก
```

เมื่อได้ข้อมูลที่ต้องการครบแล้ว model จะเขียนคำตอบเป็นข้อความ:

```
ตอนนี้กรุงเทพฯ อุณหภูมิ 15°C มีเมฆมาก
```

Round 2 จบลง `RoundUsage` emit เป็นครั้งที่สอง — คราวนี้บรรจุเฉพาะ token ของ round 2 (input มักจะใหญ่กว่า round 1 เพราะบทสนทนายาวขึ้น) เมื่อ stream ปิดลง `Completion.Usage` จะ emit หนึ่งครั้งด้วย **ผลรวมของ round 1 และ round 2**

### สรุปรวม

| ขั้นตอน | เรียก LLM? | เกิดอะไรขึ้น | Event |
|---|---|---|---|
| Round 1 | ✅ | Model ตัดสินใจเรียก `GetWeather` | `RoundUsage` (`RoundIndex=1`) |
| ระหว่าง round | ❌ | App รันฟังก์ชัน ได้ `«15°C, มีเมฆมาก»` | `FunctionCall`, `FunctionResult` |
| Round 2 | ✅ | Model เห็นผลและเขียนคำตอบสุดท้าย | `RoundUsage` (`RoundIndex=2`, `IsFinalRound=true`) |
| Stream จบ | — | — | `Completion` (Usage = round 1 + round 2) |

### Tool มากขึ้นหมายถึง round มากขึ้น

หาก model ต้องเรียก tool ต่อเนื่องกันหลายตัว จำนวน round ก็จะเพิ่มขึ้น ตัวอย่างคำถาม *«เปรียบเทียบอากาศกรุงเทพฯ กับเชียงใหม่»*:

1. **Round 1** — model เรียก `GetWeather("Bangkok")`
2. App รัน → `«15°C, มีเมฆมาก»`
3. **Round 2** — model เห็นผลและเรียก `GetWeather("Chiang Mai")` เพิ่ม
4. App รัน → `«20°C, แดด»`
5. **Round 3** — model รวมผลทั้งสองเข้าเป็นคำตอบสุดท้าย

รวมสาม round และ `Completion.Usage` จะเป็นผลรวมของทั้งสาม มิเตอร์ context ของ UI ควรใช้ `RoundUsage.Usage.InputTokens` ของ round สุดท้าย — ในตัวอย่างนี้คือ round 3

ดูตัวอย่างตัวเลขของการเปลี่ยนแปลง context meter ในแต่ละ round ได้ที่ [Token Usage — การเปลี่ยนแปลงของขนาด context](token-usage.md#how-context-size-changes)
