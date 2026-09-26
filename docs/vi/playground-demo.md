# Khám phá Playground

Dùng Playground để so sánh các tùy chọn mô hình và cấu hình truy xuất tài liệu trước khi viết mã ứng dụng. Video này giới thiệu giao diện hiện tại trong môi trường làm việc cục bộ, từ tìm mô hình đến khám phá các thiết lập của quy trình RAG.

<video controls playsinline preload="metadata" poster="../assets/playground-demo.png" aria-label="Video hướng dẫn giao diện Mythosia.AI Playground" style="display: block; width: 100%; height: auto; border-radius: 12px;">
  <source src="../assets/playground-demo.mp4" type="video/mp4">
  <track kind="captions" src="../assets/playground-demo.vtt" srclang="en" label="English">
  Trình duyệt của bạn không hỗ trợ video nhúng. <a href="../assets/playground-demo.mp4">Tải video hướng dẫn</a>.
</video>

[Tải video (MP4)](../assets/playground-demo.mp4) · [Đọc phụ đề](../assets/playground-demo.vtt)

Bản ghi không có lời thuyết minh. Tên các bước bằng tiếng Anh xuất hiện bên dưới ứng dụng; bạn cũng có thể bật phần phụ đề riêng trong trình phát.

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
