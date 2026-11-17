# Checklist nộp bài và demo PRN232

Đề PRN232 Final Assignment, mục 2–7. Yêu cầu bổ sung của giảng viên là hai database. Bản demo dùng Docker Compose trên máy, một trong hai hình thức triển khai được đề chấp nhận.

Xem [báo cáo rà soát chức năng, database và lỗi đã sửa](AUDIT-DEMO.md).

| Yêu cầu bắt buộc | Đã làm trong project | Cách kiểm tra khi demo |
|---|---|---|
| ASP.NET Core REST API .NET 8+, CRUD, JWT, tìm kiếm/lọc/sắp xếp/phân trang, kiến trúc phân lớp | Booking API có ba thư mục `Api/`, `Services/` và `Repositories/`. Các endpoint chuyển yêu cầu cho service, service dùng `BookingRepository` để truy cập dữ liệu, repository giữ `BookingDb`. | Mở Swagger, đăng nhập và Authorize. Lọc lịch theo ngày/trạng thái, thử Manager truy cập chi nhánh khác. |
| EF Core, DB quan hệ, DI, cấu hình, logging và xử lý lỗi | EF Core/Pomelo với MySQL, cấu hình qua biến môi trường, ASP.NET Core DI và middleware lỗi. Hai schema `booking_db` và `pricing_db`, có script tạo bảng và tài khoản Compose tách quyền. | Xem SQL trong `database/`. Thử đặt trùng cùng sân/giờ để thấy HTTP 409. |
| gRPC service độc lập và REST API gọi được | Booking API gọi Pricing Service qua gRPC để quản lý bảng giá và tính tiền theo ngày, giờ và chi nhánh. | Với Cộng Hòa Football Tân Bình, so sánh giá 250.000đ lúc 17:00 và 340.000đ lúc 19:00. |
| Message broker có producer và consumer | Worker đọc SQL outbox, publish vào Redis Streams rồi consume bằng consumer group và lưu thông báo. | Xem log `Published` và `Consumed` sau khi xác nhận lịch. |
| Ít nhất một background job | Worker giải phóng giữ chỗ quá hạn và chuyển lịch đã qua giờ đá sang `Completed`. | Tạo một lượt giữ sân rồi để hết 10 phút. Slot sẽ mở lại. |
| Triển khai toàn bộ hệ thống bằng Docker Compose hoặc cloud | Compose gồm web, Booking API, Pricing, Worker, MySQL và Redis. | Chạy `docker compose ps`, mở web và chạy luồng đặt sân. |
| Mã nguồn, SQL/migrations, Dockerfile, Compose nếu áp dụng, Swagger/OpenAPI và README | Có mã nguồn hai repo, sáu script SQL, Dockerfile, `compose.yaml`, Swagger và README mô tả kiến trúc, cài đặt và triển khai. | Trước khi nộp, điền **tên và nhiệm vụ thực tế của các thành viên** vào README. |
| Demo luồng nghiệp vụ từ đầu đến cuối | React chọn sân, giữ chỗ, backend nhận webhook giả, lịch xác nhận và Worker tạo thông báo. | Dùng kịch bản bên dưới. |

## Chuẩn bị môi trường demo

Đặt hai repo cạnh nhau và cấu hình `.env` ở repo backend theo [README](README.md#chạy-bằng-docker-compose). Để dùng thanh toán giả lập, đặt `SEPAY_ACCOUNT_NUMBER=0000000000`, điền `SEPAY_BANK` và đặt một giá trị cho `SEPAY_WEBHOOK_KEY`. Điền `ADMIN_EMAIL` và `ADMIN_PASSWORD` để API tạo tài khoản Admin khi khởi động lần đầu.

Chạy ở repo backend.

```powershell
docker compose up --build --wait
docker compose ps
```

Kiểm tra đủ `web`, `api`, `pricing`, `worker`, `mysql` và `redis`, rồi mở [web local](http://localhost:5173) và [Swagger](http://localhost:5045/swagger). Compose nạp dữ liệu mẫu khi khởi tạo volume MySQL mới.

Nếu cổng `5045` hoặc `5173` đang được dùng, tạo `compose.demo.yaml` cạnh `compose.yaml` để Docker tự chọn cổng loopback.

```yaml
services:
  api:
    ports: !override
      - '127.0.0.1::8080'
  web:
    ports: !override
      - '127.0.0.1::80'
```

Thêm `-f compose.yaml -f compose.demo.yaml` sau `docker compose` trong các lệnh. Dùng `docker compose -f compose.yaml -f compose.demo.yaml port web 80` và `docker compose -f compose.yaml -f compose.demo.yaml port api 8080` để lấy cổng web và API.

Dữ liệu trong [script seed](database/06-demo-data.sql) gồm ba chi nhánh, mỗi chi nhánh có bốn sân. Các mức dưới đây áp dụng từ `01/01/2026`, với giờ cao điểm từ 18:00 đến trước 22:00.

| Chi nhánh | Giá lúc 17:00 | Giá lúc 19:00 |
|---|---:|---:|
| Cộng Hòa Football Tân Bình | 250.000đ | 340.000đ |
| Green Field Thủ Đức | 220.000đ | 300.000đ |
| Riverside Football Quận 7 | 280.000đ | 380.000đ |

Tài khoản mẫu `minh.anh@smartbooking.demo` (User) và `manager.tanbinh@smartbooking.demo` (Manager Cộng Hòa) dùng mật khẩu `AuditFootball2026!`. Tài khoản Admin dùng thông tin trong `.env` của môi trường demo.

## Kịch bản trình bày

1. **Kiến trúc và phân quyền.** Dùng `docker compose ps` để giới thiệu sáu dịch vụ. Trong Swagger, gọi `POST /api/auth/login` rồi nhập token bằng **Authorize**. Với Manager Cộng Hòa, gọi `GET /api/management/branches` để xem chi nhánh được giao. Gọi `GET /api/management/branches/{id}/pitches` với ID của chi nhánh Thủ Đức lấy từ `GET /api/branches` và kiểm tra `403`.
2. **Báo giá và giữ sân.** Đăng nhập User trên web. Chọn Cộng Hòa, một sân và ngày tương lai. So sánh báo giá 17:00 với 19:00, rồi chọn giờ 17:00 còn trống và bấm **Giữ sân và thanh toán**. Ghi lại mã lịch sau dấu `#`. Tải lại trang để mở lại đúng lịch. Trong Swagger, gửi `POST /api/bookings` cho cùng sân, ngày và giờ với số tiền đã báo để minh họa `409` khi đặt trùng.
3. **Thanh toán giả lập và Redis Streams.** Thay `123` bằng mã lịch vừa tạo, rồi chạy lệnh ở repo backend.

   ```powershell
   pwsh -File ./scripts/demo.ps1 -BookingId 123
   ```

   Nếu dùng cổng API hoặc file cấu hình khác cho môi trường demo, truyền `-Api http://127.0.0.1:<cổng>` và `-EnvFile <đường-dẫn>`.

   Script gửi webhook giả lập vào API local. Kiểm tra kết quả `Confirmed` và `Paid`, xem trạng thái trên web, rồi mở **Thông báo**. Chạy `docker compose logs --tail 100 worker` để chỉ ra hai dòng `Published BookingConfirmed` và `Consumed` có cùng mã sự kiện.
4. **Lịch vận hành.** Đăng nhập Manager Cộng Hòa, mở **Lịch sân** rồi chọn ngày đã đặt ở bước 2. Chỉ ra lịch được nhóm theo sân, xếp theo giờ và có thể lọc theo trạng thái.
5. **Background Job giải phóng sân.** Tạo lượt giữ chỗ thứ hai ở một khung giờ khác và để hết hạn. Có thể tạo lượt này sớm để tận dụng thời gian thuyết trình. Sau khi đủ 10 phút hoặc đến giờ bắt đầu, tùy mốc nào sớm hơn, kiểm tra `Expired`, dòng `Expired booking` trong log Worker và khung giờ đã chọn lại được.

## Trọng số của đề

Kiến trúc 20%, REST API 20%, Background Job 10%, Message Broker 15%, gRPC 15%, triển khai 10%, tài liệu và trình bày 10%.
