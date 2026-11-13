# Thiết kế database đặt sân

Các script khởi tạo hai database trên cùng một MySQL 8.4 instance. Chạy bằng tài khoản có quyền tạo database và bảng.

```sql
SOURCE 01-booking.sql;
SOURCE 02-pricing.sql;
SOURCE 03-sepay.sql;
SOURCE 04-pitch-capacity.sql;
```

Chạy các lệnh trên trong MySQL client mở từ thư mục `database`. Khi chuyển sang EF Core, tạo một bộ migration cho mỗi DbContext và chọn một cách khởi tạo cho từng môi trường để tránh tạo bảng hai lần.

Với volume Docker đã tạo trước khi bỏ giới hạn sân, chạy migration 04 một lần từ thư mục backend trước khi build lại API:

```powershell
Get-Content -Raw database/04-pitch-capacity.sql | docker compose exec -T mysql sh -c 'MYSQL_PWD="$MYSQL_ROOT_PASSWORD" mysql -uroot booking_db'
docker compose up -d --build
```

## Booking DB

| Bảng | Vai trò |
|---|---|
| `users` | Tài khoản, mật khẩu đã băm, trạng thái và một trong ba vai trò User, Manager, Admin. |
| `branches` | Thông tin chi nhánh, khu vực và giờ mở cửa. |
| `branchManagers` | Mỗi Manager quản lý tối đa một chi nhánh và mỗi chi nhánh có tối đa một Manager. |
| `pitches` | Sân thuộc chi nhánh, có số thứ tự dương và duy nhất trong chi nhánh. |
| `bookings` | Lịch đặt, sân, người đặt, giá đã chốt, mã bảng giá và thông tin hủy. |
| `outbox_messages` | Sự kiện đặt hoặc hủy chờ phát lên Redis Streams. |
| `notifications` | Thông báo trong ứng dụng và thời điểm đã đọc. |

- Chi nhánh mới có thể bắt đầu với 0 sân. Admin hoặc Manager của chi nhánh thêm sân qua API. Cột `pitch_number` là `BIGINT`. API khóa bản ghi chi nhánh trong transaction khi cấp số tiếp theo để hai lần thêm đồng thời nhận hai số khác nhau. Dùng `is_active` khi sân tạm ngừng hoạt động.
- Chỉ Admin được gán Manager. Booking API kiểm tra tài khoản được gán có role Manager, kiểm tra chi nhánh được giao khi thao tác và cập nhật hoặc xóa phân công trong cùng transaction khi thay đổi vai trò.
- Mỗi lượt đặt có một slot 60 phút, bắt đầu đúng đầu giờ. API tính giờ trống từ giờ hoạt động và các booking của sân. API kiểm tra sân và chi nhánh đang hoạt động, giờ đặt nằm trong giờ mở cửa và thời điểm bắt đầu còn ở tương lai.
- `occupiedStartTime` có giá trị cho lịch `Confirmed` và `Completed`. Khi hủy lịch, cột này thành `NULL`. Unique index cho phép giữ nhiều lịch sử đã hủy và chỉ cho một lịch chưa hủy trên cùng sân và giờ. API chuyển lỗi trùng của index này thành HTTP 409.
- Booking có trạng thái ban đầu `Confirmed`. Hủy trước giờ bắt đầu sẽ ghi cả thời điểm lẫn người hủy. Worker định kỳ cập nhật các lịch `Confirmed` đã qua giờ kết thúc sang `Completed`. Điều kiện trạng thái phải nằm trong câu lệnh cập nhật để các lần chạy lại không đổi lịch đã hủy.
- Lưu booking hoặc thao tác hủy cùng sự kiện outbox trong một transaction. Worker phát các bản ghi chưa có `publishedAt`, rồi đánh dấu đã phát. Consumer lưu thông báo theo khóa `(eventId, userId)` và xác nhận Redis sau khi transaction lưu thông báo đã commit.

## Pricing DB

`pricingPolicies` lưu các phiên bản bảng giá theo chi nhánh. Pricing Service chọn phiên bản có `effectiveFrom` mới nhất mà vẫn nhỏ hơn hoặc bằng **ngày đá tại địa phương**. Giá cao điểm áp dụng khi giờ bắt đầu slot nằm trong khoảng `[peakStartHour, peakEndHour)`. Giá tính bằng số nguyên VND.

Booking API kiểm tra quyền Manager hoặc Admin trước khi gọi Pricing Service qua gRPC để cập nhật giá. Khi tạo booking, API lấy mã chi nhánh từ sân, gọi gRPC lấy giá và lưu `priceVnd` cùng `pricingPolicyId`. Chi nhánh phải có bảng giá hiệu lực trước khi nhận lịch. Hai database dùng mã tham chiếu qua gRPC, với các khóa ngoại chỉ nằm trong từng database.

## Thanh toán SePay

Sau migration SePay, bảng `bookings` có thêm `expires_at_utc` và hai trạng thái `PendingPayment`, `Expired`. Trạng thái chờ thanh toán giữ slot, trạng thái hết hạn giải phóng slot. API xác nhận lịch sau khi webhook khớp số tiền và mã thanh toán. Hai bảng mới gồm `payments` lưu khoản cần thu theo booking và `payment_receipts` lưu các giao dịch SePay đã xử lý, với `sepay_id` là khóa chống trùng. Khoản nhận sai số tiền hoặc nhận khi lịch đã đóng xuất hiện trong mục đối soát của Admin.

## Quy ước thời gian

Booking API và Worker lưu thời điểm dưới dạng UTC và đặt timezone của các session MySQL thành UTC để các giá trị `CURRENT_TIMESTAMP` trong schema được ghi đúng. Giờ mở cửa, giờ cao điểm và ngày hiệu lực được tính theo múi giờ `Asia/Ho_Chi_Minh`. Bản đầu dùng giờ nguyên và giờ hoạt động trong cùng ngày. Giá trị giờ đóng cửa 24 biểu thị hết ngày.
