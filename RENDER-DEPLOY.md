# Deploy Render

`render.yaml` triển khai Booking API và Key Value trên Render Free. Container API khởi động Pricing Service dưới tiến trình riêng, chỉ lắng nghe ở `127.0.0.1:5081`. Booking API gọi Pricing qua gRPC-Web và chạy Worker nền trong cùng tiến trình. Hai database MySQL nằm trên Aiven.

## 0. Tạo database Aiven

Tạo một dịch vụ **Aiven for MySQL**. Trong **Connect → Databases**, tạo hai database: `booking_db` và `pricing_db`. Từ Workbench, chạy lần lượt `database/01-booking.sql` đến `database/07-deposit-check-in.sql` theo đúng thứ tự.

Lấy `Host`, `Port`, `User`, `Password` ở Aiven Overview và tạo hai connection string sau (đổi các giá trị trong `<...>`):

```text
Server=<AIVEN_HOST>;Port=<AIVEN_PORT>;Database=booking_db;User ID=<AIVEN_USER>;Password=<AIVEN_PASSWORD>;SslMode=Required
Server=<AIVEN_HOST>;Port=<AIVEN_PORT>;Database=pricing_db;User ID=<AIVEN_USER>;Password=<AIVEN_PASSWORD>;SslMode=Required
```

Đặt IP allowlist của Aiven là `0.0.0.0/0` trong giai đoạn demo để Render có thể kết nối; đừng commit hai chuỗi này vào Git.

## 1. Backend

1. Push toàn bộ thay đổi repo `SmartBooking-Football-BE` lên GitHub.
2. Render Dashboard → **New** → **Blueprint** → chọn repository backend. Render tạo web service `football-api` và Key Value `football-redis` ở Singapore.
3. Tại bước Blueprint, điền các biến có trạng thái `sync: false`:
   - `BootstrapAdmin__Password`: tối thiểu 8 ký tự.
   - `ConnectionStrings__Booking`: connection string `booking_db` của Aiven.
   - `ConnectionStrings__Pricing`: connection string `pricing_db` của Aiven.
   - `Web__Origin`: để trống lúc tạo nếu chưa có frontend URL; sau bước 2 frontend, cập nhật thành URL thật, ví dụ `https://football-web.onrender.com`.
   - `Sepay__AccountNumber`, `Sepay__WebhookKey`: điền khi dùng thanh toán thật. Có thể đặt giá trị demo nếu đang demo giao dịch fake.
4. Sau khi deploy, mở `/health` để kiểm tra Booking API và `/swagger` để thử API. Gọi `GET /api/branches` để lấy ID chi nhánh, rồi gọi `GET /api/branches/{id}/quote?date=<yyyy-MM-dd>&hour=17` và thử lại với `hour=19`. Với chi nhánh mẫu Cộng Hòa Tân Bình và ngày giá có hiệu lực, hai giá lần lượt là 250.000đ và 340.000đ.

### Cập nhật web service đang chạy

Trong Dashboard, mở web service backend đang phục vụ frontend để giữ URL hiện có. Chọn `Dockerfile.api` và dùng lệnh khởi động mặc định của image. Trong **Environment**, thêm `Pricing__RunLocal=true` và `ConnectionStrings__Pricing` trỏ tới `pricing_db` của Aiven. Biến `sync: false` mới cần nhập thủ công trên Blueprint đã tạo. Khi cần Worker chạy trong web service, thêm `RunBookingWorker=true` và `ConnectionStrings__Redis` từ **Internal URL** của Key Value. Nếu đã có Worker riêng đang chạy, dùng Worker đó hoặc bật Worker trong web service để chỉ chạy một bản. Sau khi lưu và deploy, kiểm tra hai khung giờ như bước 4.

Nếu báo giá trả `503`, xem log của web service để phân biệt lỗi khởi động Pricing, kết nối MySQL hoặc lỗi truy vấn schema `pricing_db`. Nếu trả `404` kèm thông báo chưa có bảng giá hiệu lực, thêm bảng giá cho chi nhánh qua màn Quản lý.

## 2. Frontend

1. Push repo `SmartBooking-Football-FE` lên GitHub.
2. Trong project frontend trên Vercel, mở **Settings → Environment Variables**.
3. Cập nhật destination của rewrite /api trong vercel.json thành URL Booking API ở bước 1, rồi deploy frontend. Vercel dùng build mode vercel để frontend gọi API cùng domain.
4. Sau khi frontend online, quay lại service `football-api` → **Environment**, cập nhật `Web__Origin` thành URL frontend, chọn **Save and deploy**. Việc này cho phép CORS đúng một origin.

## 3. SePay thật

Trong SePay tạo webhook HTTPS:

`https://<football-api>.onrender.com/api/payments/sepay/webhook`

Chọn API Key có giá trị trùng `Sepay__WebhookKey`. Khi chưa có key, giao dịch fake vẫn dùng được trong demo local; không dùng key giả trên môi trường thật.

## 4. Lưu ý demo

- Khi web service Free ngủ, Pricing và Worker trong cùng container cũng tạm dừng. Chế độ Docker Compose vẫn chạy Booking API, Pricing và Worker thành các service riêng cho demo local.
- Aiven giữ database độc lập với Render, vì vậy redeploy API không ảnh hưởng dữ liệu. Chạy script migration mới trên Aiven Workbench trước khi deploy code phụ thuộc migration đó.

## Migration cho bản cọc 50%

Áp dụng database/07-deposit-check-in.sql trên booking_db trước khi deploy API/Worker mới. Script giữ payment cũ như khoản đã thu toàn bộ, bổ sung số tiền còn lại cho booking mới và thông tin check-in. Kiểm tra Swagger có hai endpoint check-in và collect-balance.

Giá báo qua gRPC là tổng giá sân. Khi tạo booking mới, payment.amountVnd là tiền cọc 50%, balanceAmountVnd là khoản còn lại thu tại sân. Kiểm tra cả hai trường khi đối chiếu số tiền.
