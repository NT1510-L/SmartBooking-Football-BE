# Checklist nộp bài và demo PRN232

Đề PRN232 Final Assignment, mục 2–7. Yêu cầu bổ sung của giảng viên là hai database. Bản demo dùng Docker Compose trên máy, một trong hai hình thức triển khai được đề chấp nhận.

Xem [báo cáo rà soát chức năng, database và lỗi đã sửa](AUDIT-DEMO.md).

| Yêu cầu bắt buộc | Đã làm trong project | Cách kiểm tra khi demo |
|---|---|---|
| ASP.NET Core REST API .NET 8+, CRUD, JWT, tìm kiếm/lọc/sắp xếp/phân trang, kiến trúc phân lớp | Booking API có ba thư mục `Api/`, `Services/` và `Repositories/`. Các endpoint chuyển yêu cầu cho service, service dùng `BookingRepository` để truy cập dữ liệu, repository giữ `BookingDb`. | Mở Swagger, đăng nhập và Authorize. Lọc lịch theo ngày/trạng thái, thử Manager truy cập chi nhánh khác. |
| EF Core, DB quan hệ, DI, cấu hình, logging và xử lý lỗi | EF Core/Pomelo với MySQL, cấu hình qua biến môi trường, ASP.NET Core DI và middleware lỗi. Hai schema `booking_db` và `pricing_db`, có script tạo bảng và tài khoản Compose tách quyền. | Xem SQL trong `database/`. Thử đặt trùng cùng sân/giờ để thấy HTTP 409. |
| gRPC service độc lập và REST API gọi được | Booking API gọi Pricing Service qua gRPC để quản lý bảng giá và tính tiền theo ngày, giờ và chi nhánh. | Chọn sân lúc 19:00, xem giá cao điểm 350.000đ. Đổi giá ở trang Quản lý rồi xem báo giá mới. |
| Message broker có producer và consumer | Worker đọc SQL outbox, publish vào Redis Streams rồi consume bằng consumer group và lưu thông báo. | Xem log `Published` và `Consumed` sau khi xác nhận lịch. |
| Ít nhất một background job | Worker giải phóng giữ chỗ quá hạn và chuyển lịch đã qua giờ đá sang `Completed`. | Tạo một lượt giữ sân rồi để hết 10 phút. Slot sẽ mở lại. |
| Triển khai toàn bộ hệ thống bằng Docker Compose hoặc cloud | Compose gồm web, Booking API, Pricing, Worker, MySQL và Redis. | Chạy `docker compose ps`, mở web và chạy luồng đặt sân. |
| Mã nguồn, SQL/migrations, Dockerfile, Compose nếu áp dụng, Swagger/OpenAPI và README | Có source hai repo, ba script SQL, Dockerfile, Compose, Swagger và README mô tả kiến trúc, cài đặt và triển khai. | Trước khi nộp, điền **tên và nhiệm vụ thực tế của các thành viên** vào README. |
| Demo luồng nghiệp vụ từ đầu đến cuối | React chọn sân, giữ chỗ, backend nhận webhook giả, lịch xác nhận và Worker tạo thông báo. | Dùng kịch bản bên dưới. |

## Kịch bản trình bày

Bản local đã chạy cả sáu service. Hai chi nhánh mẫu được seed ba sân mỗi nơi. Có thể thêm sân ở trang **Quản lý**. Booking **#1** được xác nhận từ webhook giả với giá **350.000đ**, thử đặt trùng trả **409** và Worker đã phát, nhận sự kiện rồi tạo thông báo. Manager mẫu truy cập chi nhánh ngoài phạm vi nhận **403**.

Tài khoản demo local gồm `user-demo@example.test` và `manager-demo@example.test`, cùng mật khẩu `FootballDemo2026!`. Manager mẫu được gán chi nhánh Quận 7. Mật khẩu Admin nằm trong `.env` của backend.

1. Mở [web local](http://localhost:5173) và [Swagger](http://localhost:5045/swagger). Đăng nhập Admin bằng `ADMIN_EMAIL` và `ADMIN_PASSWORD` trong file `.env` của backend. Tạo Manager và gán vào một chi nhánh để demo phân quyền.
2. Đăng ký tài khoản User trên web. Chọn chi nhánh, sân, ngày tương lai và giờ còn trống. Bấm **Giữ sân và thanh toán**, ghi lại mã lịch số hiển thị sau dấu `#`.
3. Mở PowerShell tại repo backend, chạy lệnh sau và thay `123` bằng mã lịch vừa tạo.

   ```powershell
   pwsh -File ./scripts/demo.ps1 -BookingId 123
   ```

4. Web tự cập nhật sang **Đã xác nhận**. Mở **Thông báo** để xem tin xác nhận. Chạy `docker compose logs --tail 30 worker` để chỉ ra producer và consumer.
5. Chạy `docker compose ps` để trình bày triển khai. Giải thích giao dịch được mô phỏng bằng cách gửi payload vào webhook local. Giao diện hiện thông báo mô phỏng và ẩn mã QR chuyển khoản.

Nếu khởi động lại Docker, chạy `docker compose up -d` từ repo backend. Có thể tạo lại dữ liệu mẫu bằng `pwsh -File ./scripts/demo.ps1`. Compose có volume MySQL riêng với MySQL Workbench ở cổng 3307.

## Trọng số của đề

Kiến trúc 20%, REST API 20%, Background Job 10%, Message Broker 15%, gRPC 15%, triển khai 10%, tài liệu và trình bày 10%.
