# Deploy Render

Repository này có `render.yaml` để triển khai backend theo cloud deployment của đề: Booking REST API, Pricing gRPC (private), Worker và Redis Streams. MySQL chạy trên Aiven; Render chỉ nhận hai connection string bí mật.

## 0. Tạo database Aiven

Tạo một dịch vụ **Aiven for MySQL**. Trong **Connect → Databases**, tạo hai database: `booking_db` và `pricing_db`. Từ Workbench, chạy lần lượt `database/01-booking.sql` đến `database/06-demo-data.sql` theo đúng thứ tự.

Lấy `Host`, `Port`, `User`, `Password` ở Aiven Overview và tạo hai connection string sau (đổi các giá trị trong `<...>`):

```text
Server=<AIVEN_HOST>;Port=<AIVEN_PORT>;Database=booking_db;User ID=<AIVEN_USER>;Password=<AIVEN_PASSWORD>;SslMode=Required
Server=<AIVEN_HOST>;Port=<AIVEN_PORT>;Database=pricing_db;User ID=<AIVEN_USER>;Password=<AIVEN_PASSWORD>;SslMode=Required
```

Đặt IP allowlist của Aiven là `0.0.0.0/0` trong giai đoạn demo để Render có thể kết nối; đừng commit hai chuỗi này vào Git.

## 1. Backend

1. Push toàn bộ thay đổi repo `SmartBooking-Football-BE` lên GitHub.
2. Render Dashboard → **New** → **Blueprint** → chọn repository backend. Render đọc `render.yaml` và tạo 4 service ở Singapore.
3. Tại bước Blueprint, điền các biến có trạng thái `sync: false`:
   - `BootstrapAdmin__Password`: tối thiểu 8 ký tự.
   - `ConnectionStrings__Booking`: connection string `booking_db` của Aiven.
   - `ConnectionStrings__Pricing`: connection string `pricing_db` của Aiven.
   - `Web__Origin`: để trống lúc tạo nếu chưa có frontend URL; sau bước 2 frontend, cập nhật thành URL thật, ví dụ `https://football-web.onrender.com`.
   - `Sepay__AccountNumber`, `Sepay__WebhookKey`: điền khi dùng thanh toán thật. Có thể đặt giá trị demo nếu đang demo giao dịch fake.
4. Chờ theo thứ tự: `football-pricing` → `football-api` → `football-worker`. Mở `https://<football-api>.onrender.com/health` phải nhận `{ "status": "ok" }`; Swagger là `/swagger`.

Không commit connection string Aiven. Pricing gRPC vẫn private và chỉ API Render gọi được.

## 2. Frontend

1. Push repo `SmartBooking-Football-FE` lên GitHub.
2. Render → **New** → **Blueprint** → chọn repo frontend.
3. Khi được hỏi `VITE_API_URL`, dán URL API ở bước 1, không có dấu `/` cuối: `https://<football-api>.onrender.com`.
4. Sau khi frontend online, quay lại service `football-api` → **Environment**, cập nhật `Web__Origin` thành URL frontend, chọn **Save and deploy**. Việc này cho phép CORS đúng một origin.

## 3. SePay thật

Trong SePay tạo webhook HTTPS:

`https://<football-api>.onrender.com/api/payments/sepay/webhook`

Chọn API Key có giá trị trùng `Sepay__WebhookKey`. Khi chưa có key, giao dịch fake vẫn dùng được trong demo local; không dùng key giả trên môi trường thật.

## 4. Lưu ý demo

- Render free service có thể sleep/có giới hạn tài nguyên. Trước buổi bảo vệ, mở API và frontend trước vài phút.
- Aiven giữ database độc lập với Render, vì vậy redeploy API không ảnh hưởng dữ liệu. Chạy script migration mới trên Aiven Workbench trước khi deploy code phụ thuộc migration đó.
- Render cho phép private service giao tiếp qua private network; Pricing gRPC không cần public URL.
