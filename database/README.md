# Database SmartBooking

MySQL chứa hai schema `booking_db` và `pricing_db`, tổng cộng 10 bảng. Booking API và Worker dùng `booking_db`, Pricing Service dùng `pricing_db`.

## Khởi tạo và nâng cấp

Docker Compose chạy script 01–07 khi tạo volume mới. Với MySQL/Aiven mới, chạy lần lượt các script trong thư mục `database`. Script 06 thêm dữ liệu demo gồm ba chi nhánh, mỗi chi nhánh ba sân.

```sql
SOURCE 01-booking.sql;
SOURCE 02-pricing.sql;
SOURCE 03-sepay.sql;
SOURCE 04-pitch-capacity.sql;
SOURCE 05-camel-case-naming.sql;
SOURCE 06-demo-data.sql;
SOURCE 07-deposit-check-in.sql;
```

Các migration chạy một lần. Với database đã có script 01–06, sao lưu dữ liệu rồi chạy script 07 trước khi khởi động API/Worker mới. Trên Docker local, chạy từ thư mục backend.

```powershell
Get-Content -Raw database/07-deposit-check-in.sql | docker compose exec -T mysql sh -c 'MYSQL_PWD="$MYSQL_ROOT_PASSWORD" mysql -uroot'
docker compose up -d --build
```

Script 07 giữ nguyên số tiền của payment cũ và đặt `balanceAmountVnd = 0`. Các lịch `Confirmed` cũ đã kết thúc chuyển sang `Completed` trước khi áp dụng no-show. Sân có số lớn hơn ba được ngưng hoạt động để giữ lịch sử. Các bản ghi này tiếp tục xuất hiện trong quản lý dưới dạng sân lưu trữ.

## booking_db

| Bảng | Trách nhiệm |
|---|---|
| `users` | Tài khoản và vai trò User, Manager, Admin. |
| `branches` | Địa chỉ, khu vực, giờ hoạt động, trạng thái chi nhánh. |
| `branchmanagers` | Một Manager quản lý tối đa một chi nhánh và mỗi chi nhánh có tối đa một Manager. |
| `pitches` | Số sân duy nhất trong chi nhánh, tên và trạng thái. |
| `bookings` | Lịch đặt, tổng giá đã chốt, hạn giữ sân, check-in và thông tin hủy. |
| `payments` | Tiền cọc, khoản còn lại, thời điểm thanh toán và nhân viên thu phần còn lại. |
| `paymentreceipts` | Giao dịch SePay đã đối chiếu, chống xử lý trùng bằng `sepayId`. |
| `outboxmessages` | Sự kiện xác nhận, hủy và no-show chờ phát vào Redis Streams. |
| `notifications` | Thông báo cho người dùng và thời điểm đã đọc. |

Booking mới cọc 50% tổng giá, làm tròn lên một VND. `payments.amountVnd` là khoản cần chuyển qua SePay, `balanceAmountVnd` là khoản thu tại sân. Tổng hai khoản bằng `bookings.priceVnd`. `payments.bookingId` vẫn có UNIQUE, mỗi booking tạo một payment chứa cả hai khoản.

Webhook đúng mã, tài khoản, số tiền cọc và còn hạn chuyển booking từ `PendingPayment` sang `Confirmed`, payment từ `Pending` sang `Paid`. `Paid` biểu thị khoản SePay đã nhận. API trả thêm `balanceStatus`, `totalAmountVnd`, `paidAmountVnd` và `remainingAmountVnd` để frontend hiển thị đủ tình trạng thu tiền.

Staff đúng chi nhánh hoặc Admin dùng `POST /api/management/bookings/{id}/check-in` để ghi nhận khách đến trước mốc giờ bắt đầu cộng mười phút. Sau check-in, `POST /api/management/bookings/{id}/collect-balance` xác nhận khoản đã thu tại sân và lưu `balancePaidAt`, `balanceCollectedByUserId`. Hai thao tác khóa booking trong transaction và hỗ trợ gọi lại an toàn.

Worker chạy chu kỳ ba giây.

- `PendingPayment` hết hạn chuyển sang `Expired`, payment sang `Expired`.
- `Confirmed` quá mười phút sau giờ bắt đầu mà chưa check-in chuyển sang `NoShow`. Khoản cọc được giữ lại, phần còn lại không thu. Worker tạo sự kiện `BookingNoShow` và thông báo.
- `CheckedIn` đã qua giờ kết thúc chuyển sang `Completed`. Khoản tại sân chưa thu vẫn hiển thị để staff hoàn tất.

`endTime` là cột sinh bằng `startTime + 1 giờ`. `occupiedStartTime` bằng `startTime` với `PendingPayment`, `Confirmed`, `CheckedIn`, `Completed` và bằng NULL với trạng thái giải phóng sân. `UNIQUE (pitchId, occupiedStartTime)` chống đặt trùng.

API khóa chi nhánh trong transaction khi thêm sân. API chặn tạo quá ba bản ghi sân trong một chi nhánh. SQL dùng `UNIQUE (branchId, pitchNumber)` và CHECK cho phép số sân 1–3 hoạt động. Sân cũ có số lớn hơn ba giữ trạng thái lưu trữ.

Cập nhật xác nhận/hủy/no-show và sự kiện outbox nằm trong cùng transaction. Worker phát sự kiện, ghi notification theo `UNIQUE (eventId, userId)` rồi ACK Redis. Sau script 07, `booking_db` có 13 FK vật lý, bao gồm người check-in và người thu phần còn lại.

## pricing_db và quan hệ logic

`pricingpolicies` lưu phiên bản giá theo chi nhánh. Chọn `effectiveFrom` mới nhất không vượt quá ngày đá. Giá cao điểm áp dụng trong `[peakStartHour, peakEndHour)`. Giá được lưu bằng số nguyên VND.

`UNIQUE (branchId, effectiveFrom)` bảo vệ phiên bản. Booking lưu giá đã chốt và `pricingPolicyId`. API kiểm `expectedAmountVnd` và trả `409 PriceChanged` nếu giá lấy lại qua gRPC thay đổi.

Các FK nằm trong `booking_db`. `pricingpolicies.branchId`, `bookings.pricingPolicyId` và `notifications.eventId` là tham chiếu logic.

## Thời gian

API và Worker lưu thời điểm UTC, giao diện hiển thị giờ Việt Nam. Giờ mở cửa, khung cao điểm và ngày hiệu lực dùng `Asia/Ho_Chi_Minh`. Giờ đóng cửa 24 biểu thị hết ngày.
