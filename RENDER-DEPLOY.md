# Deploy Render

Repository này có `render.yaml` để triển khai backend theo cloud deployment của đề: Booking REST API, Pricing gRPC (private), Worker và Redis Streams. MySQL cũng là private service trên Render, gắn persistent disk nên dữ liệu không mất khi redeploy.

## 1. Backend

1. Push toàn bộ thay đổi repo `SmartBooking-Football-BE` lên GitHub.
2. Render Dashboard → **New** → **Blueprint** → chọn repository backend. Render đọc `render.yaml` và tạo 5 service ở Singapore.
3. Tại bước Blueprint, điền các biến có trạng thái `sync: false`:
   - `BootstrapAdmin__Password`: tối thiểu 8 ký tự.
   - `Web__Origin`: để trống lúc tạo nếu chưa có frontend URL; sau bước 2 frontend, cập nhật thành URL thật, ví dụ `https://football-web.onrender.com`.
   - `Sepay__AccountNumber`, `Sepay__WebhookKey`: điền khi dùng thanh toán thật. Có thể đặt giá trị demo nếu đang demo giao dịch fake.
4. Chờ theo thứ tự: `football-mysql` → `football-pricing` → `football-api` → `football-worker`. Mở `https://<football-api>.onrender.com/health` phải nhận `{ "status": "ok" }`; Swagger là `/swagger`.

Không nhập mật khẩu database bằng tay: Blueprint tạo mật khẩu và truyền private giữa các service. Không public MySQL hoặc Pricing gRPC.

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
- MySQL cần persistent disk để giữ dữ liệu. Đây là dịch vụ stateful nên không dùng plan free/không disk.
- Các script `database/01` đến `04` chỉ chạy ở lần khởi tạo disk đầu tiên. Với migration mới sau này, chạy script bằng Render Shell vào `football-mysql` trước khi deploy code phụ thuộc migration đó.
- Render cho phép private service giao tiếp qua private network; Pricing gRPC không cần public URL.
