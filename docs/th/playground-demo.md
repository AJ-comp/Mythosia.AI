# สำรวจ Playground

ใช้ Playground เพื่อเปรียบเทียบตัวเลือกของโมเดลและตั้งค่าการค้นคืนเอกสารก่อนเขียนโค้ดแอปพลิเคชัน วิดีโอนี้แสดงอินเทอร์เฟซปัจจุบันในพื้นที่ทำงานบนเครื่อง ตั้งแต่การค้นหาโมเดลไปจนถึงการสำรวจการตั้งค่าไปป์ไลน์ RAG

<video class="playground-video" controls playsinline preload="metadata" poster="../assets/playground-demo.png" aria-label="วิดีโอแนะนำอินเทอร์เฟซ Mythosia.AI Playground">
  <source src="../assets/playground-demo.mp4?v=4" type="video/mp4">
  <track kind="subtitles" src="../assets/playground-demo.vtt?v=4" srclang="en" label="English">
  <track kind="subtitles" src="../assets/playground-demo.ko.vtt?v=4" srclang="ko" label="한국어">
  <track kind="subtitles" src="../assets/playground-demo.ja.vtt?v=4" srclang="ja" label="日本語">
  <track kind="subtitles" src="../assets/playground-demo.zh-Hans.vtt?v=4" srclang="zh-Hans" label="简体中文">
  <track kind="subtitles" src="../assets/playground-demo.zh-Hant.vtt?v=4" srclang="zh-Hant" label="繁體中文">
  <track kind="subtitles" src="../assets/playground-demo.de.vtt?v=4" srclang="de" label="Deutsch">
  <track kind="subtitles" src="../assets/playground-demo.fr.vtt?v=4" srclang="fr" label="Français">
  <track kind="subtitles" src="../assets/playground-demo.es.vtt?v=4" srclang="es" label="Español">
  <track kind="subtitles" src="../assets/playground-demo.pt.vtt?v=4" srclang="pt" label="Português">
  <track kind="subtitles" src="../assets/playground-demo.ru.vtt?v=4" srclang="ru" label="Русский">
  <track kind="subtitles" src="../assets/playground-demo.uk.vtt?v=4" srclang="uk" label="Українська">
  <track kind="subtitles" src="../assets/playground-demo.vi.vtt?v=4" srclang="vi" label="Tiếng Việt">
  <track kind="subtitles" src="../assets/playground-demo.th.vtt?v=4" srclang="th" label="ไทย" default>
  เบราว์เซอร์ของคุณไม่รองรับวิดีโอแบบฝัง <a href="../assets/playground-demo.mp4?v=4">ดาวน์โหลดวิดีโอแนะนำ</a>
</video>

[ดาวน์โหลดวิดีโอ (MP4)](../assets/playground-demo.mp4?v=4) · [อ่านคำบรรยาย](../assets/playground-demo.th.vtt?v=4)

วิดีโอไม่มีเสียง คำบรรยายจะแสดงตามภาษาของหน้าเว็บโดยอัตโนมัติ และสามารถเปลี่ยนภาษาหรือปิดคำบรรยายได้ในเมนูของโปรแกรมเล่นวิดีโอ

## สิ่งที่แสดงในวิดีโอ

1. เรียกดูกลุ่มผู้ให้บริการทั้งเจ็ดและค้นหาโมเดลตามชื่อหรือผู้ให้บริการ
2. เปิดกล่องโต้ตอบสำหรับป้อนคีย์ของผู้ให้บริการก่อนเชื่อมต่อโมเดล
3. สลับระหว่างภาษาอังกฤษและภาษาเกาหลี โดยอินเทอร์เฟซรองรับ 13 ภาษา
4. สำรวจตัวเลือกการลงทะเบียนเอกสารและการแบ่งข้อความ
5. ดูผู้ให้บริการ embedding, ที่เก็บเวกเตอร์, การค้นคืนแบบผสม และการตั้งค่าจัดอันดับใหม่

นี่คือวิดีโอแนะนำอินเทอร์เฟซ ไม่มีการส่งคีย์ API สร้างดัชนีเอกสาร หรือสร้างคำตอบจากโมเดล

## ลองใช้งานบนเครื่องของคุณ

เรียกใช้จากโฟลเดอร์รากของที่เก็บโค้ด โดยใช้ SDK ที่ระบุไว้ใน `global.json`:

```sh
dotnet run --project apps/Mythosia.AI.Samples.ChatUi
```

เปิดที่อยู่ที่แอปพลิเคชันแสดง สำรวจโมเดลและการตั้งค่าก่อน แล้วเพิ่มคีย์ของผู้ให้บริการเมื่อพร้อมส่งคำขอ

ดูรายละเอียดการเชื่อมต่อ ภาษา และการพัฒนาบนเครื่องได้ใน [คู่มือ Playground](https://github.com/AJ-comp/Mythosia.AI/blob/main/apps/Mythosia.AI.Samples.ChatUi/README.md) สำหรับ API ที่เกี่ยวข้องของไลบรารี ให้เริ่มจาก [เริ่มต้นอย่างรวดเร็ว](getting-started.md) หรือ [พื้นฐาน RAG](rag.md)
