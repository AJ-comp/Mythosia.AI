# Khám phá Playground

Dùng Playground để so sánh các tùy chọn mô hình và cấu hình truy xuất tài liệu trước khi viết mã ứng dụng. Video này giới thiệu giao diện hiện tại trong môi trường làm việc cục bộ, từ tìm mô hình đến khám phá các thiết lập của quy trình RAG.

<video class="playground-video" controls playsinline preload="metadata" poster="../assets/playground-demo.png" aria-label="Video hướng dẫn giao diện Mythosia.AI Playground">
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
  <track kind="subtitles" src="../assets/playground-demo.vi.vtt?v=4" srclang="vi" label="Tiếng Việt" default>
  <track kind="subtitles" src="../assets/playground-demo.th.vtt?v=4" srclang="th" label="ไทย">
  Trình duyệt của bạn không hỗ trợ video nhúng. <a href="../assets/playground-demo.mp4?v=4">Tải video hướng dẫn</a>.
</video>

[Tải video (MP4)](../assets/playground-demo.mp4?v=4) · [Đọc phụ đề](../assets/playground-demo.vi.vtt?v=4)

Video không có âm thanh. Phụ đề tự động hiển thị theo ngôn ngữ của trang; bạn có thể đổi ngôn ngữ hoặc tắt phụ đề trong menu của trình phát.

## Nội dung video

1. Duyệt bảy nhóm nhà cung cấp và tìm mô hình theo tên hoặc nhà cung cấp.
2. Mở hộp thoại nhập khóa của nhà cung cấp trước khi kết nối mô hình.
3. Chuyển giữa tiếng Anh và tiếng Hàn; giao diện hỗ trợ 13 ngôn ngữ.
4. Khám phá các tùy chọn đăng ký tài liệu và chia nhỏ văn bản.
5. Xem các nhà cung cấp embedding, kho vector, truy xuất kết hợp và thiết lập xếp hạng lại.

Đây là video giới thiệu giao diện: không gửi khóa API, lập chỉ mục tài liệu hay tạo phản hồi từ mô hình.

## Chạy trên máy của bạn

Từ thư mục gốc của kho mã, với SDK được chỉ định trong `global.json`:

```sh
dotnet run --project apps/Mythosia.AI.Samples.ChatUi
```

Mở địa chỉ do ứng dụng hiển thị. Trước tiên, hãy xem các mô hình và thiết lập, rồi thêm khóa của nhà cung cấp khi bạn sẵn sàng gửi yêu cầu.

Xem [hướng dẫn Playground](https://github.com/AJ-comp/Mythosia.AI/blob/main/apps/Mythosia.AI.Samples.ChatUi/README.md) để biết chi tiết về kết nối, ngôn ngữ và phát triển cục bộ. Với các API tương ứng của thư viện, hãy bắt đầu từ [Bắt đầu nhanh](getting-started.md) hoặc [Cơ bản về RAG](rag.md).
