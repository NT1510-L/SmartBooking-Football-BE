# SmartBooking — báo cáo kiểm thử ngày 18/11/2026

Bản kiểm thử chạy trên stack Docker QA riêng, MySQL 8.4 thật, Redis 7, Pricing gRPC-Web, Booking API/Worker và Chrome desktop. Dữ liệu và webhook đều là giả lập. Tái chạy bằng `./scripts/test.ps1` sau `npm ci` trong repo frontend.

## Kết quả

Bộ regression thực thi cả 35 thao tác Swagger qua 171 yêu cầu API, đồng thời kiểm tra giao diện của User, Manager và Admin. Migration 07 đã chạy thành công trên dữ liệu cũ giả lập trước khi chạy regression.

| Phần | Bằng chứng đã kiểm tra |
|---|---|
| JWT và phân quyền | Đăng ký/đăng nhập, token sai, quyền User/Admin, Manager khác chi nhánh bị 403; User không xem/hủy lịch người khác. |
| Danh mục và CRUD | Tìm kiếm, khu vực, phân trang, chi nhánh/sân, gán/gỡ Manager, giới hạn ba sân kể cả sân đang ngừng hoạt động. |
| Pricing gRPC | CRUD giá, ngày hiệu lực, biên giờ cao điểm, lỗi trùng ngày, fallback giá, dừng dịch vụ rồi thử lại trên UI. |
| Booking/MySQL | Giá đổi yêu cầu xác nhận lại, hai yêu cầu cùng slot trả 201/409, hủy và đặt lại, lọc/sắp xếp/phân trang. |
| Cọc 50% | Tổng 270.000đ có cọc 135.000đ; tổng lẻ 270.001đ có cọc 135.001đ và khoản còn lại 135.000đ. |
| Webhook giả | Sai key, sai tài khoản/loại giao dịch, sai tiền, thành công, replay, đến muộn, biên nhận và đối soát Admin. |
| Check-in/thu tiền | Chỉ Manager đúng chi nhánh hoặc Admin; cần cọc Paid; không thu trước check-in; thao tác đồng thời/lặp không ghi nhận hai lần. |
| Worker/Redis | Hết hạn và mở lại slot; chỉ lịch đã check-in được Completed; no-show từ giờ bắt đầu +10 phút; outbox, consumer và thông báo không trùng. |
| Migration cũ | Giữ payment thu toàn bộ với balance=0; lịch đã kết thúc thành Completed; sân số 4 được giữ lại ở trạng thái ngừng hoạt động. |
| User UI | Đăng ký/đăng nhập, tìm sân, giữ lựa chọn, đặt cọc, khôi phục checkout sau reload, lịch cá nhân, đọc thông báo. Thông báo tự hiện khi Worker xử lý chậm trong lúc màn hình vẫn mở. |
| Manager/Admin UI | Chi nhánh, phân công, sửa sân, CRUD giá, khóa thêm sân thứ tư, lịch theo sân/ngày/trạng thái, check-in và xác nhận khoản còn lại. |
| Container kiểu Render | Dockerfile.api khởi động Pricing cục bộ và Worker cùng API. Khi tắt Pricing/Worker độc lập, báo giá gRPC, cọc, webhook và thông báo Redis vẫn hoạt động. |

## Quy tắc nghiệp vụ

- Giữ sân tối đa 10 phút hoặc đến giờ bắt đầu, tùy mốc nào sớm hơn.
- Báo giá qua gRPC là tổng giá. `payment.amountVnd` là cọc 50% làm tròn lên một VND; `balanceAmountVnd` là khoản còn lại.
- Khoản còn lại thu trực tiếp tại sân; staff xác nhận sau khi thực tế nhận tiền. Lưu người thu và thời điểm thu. Đây là phương án triển khai khoản còn lại của bản này.
- Check-in trước hạn giờ bắt đầu +10 phút. NoShow giữ tiền cọc, không thu khoản còn lại. Worker tạo thông báo. Không tự hoàn tiền.
- Lịch CheckedIn có thể chuyển Completed khi hết giờ dù khoản còn lại chưa thu; staff vẫn có thể ghi nhận khoản thu sau đó.
- Mỗi chi nhánh mới có tối đa ba sân. Dữ liệu sân số lớn hơn ba từ bản cũ được lưu trữ để giữ lịch sử, không được bật lại.

## Kiến trúc và ERD

Compose có sáu dịch vụ: web, api, pricing, worker, mysql, redis. Booking API dùng Api → Services → Repositories → EF Core. Pricing dùng hợp đồng `pricing.proto` qua gRPC-Web. Render có thể chạy Pricing trong tiến trình riêng và Worker trong API container; database vẫn tách hai schema.

`booking_db` có chín bảng: users, branches, branchmanagers, pitches, bookings, payments, paymentreceipts, outboxmessages, notifications. `pricing_db` có pricingpolicies. Sau migration 07 có **10 bảng, 13 FK vật lý**. Ba tham chiếu logic là pricingpolicies.branchId, bookings.pricingPolicyId và notifications.eventId. Chi tiết trong [database/README.md](database/README.md).

## Giới hạn xác minh

- Kiểm thử bằng dữ liệu QA local và webhook giả; chưa xác minh SePay Live, chuyển tiền thật hoặc tải diện rộng.
- Database cloud hiện có phải chạy migration 07 trước khi deploy API/Worker mới. Không xem kết quả QA là bằng chứng đã nâng cấp Aiven/Render.
- Build .NET, FE thường và FE mode Vercel thành công. `npm audit --omit=dev` không có cảnh báo. Full audit còn cảnh báo picomatch kéo theo fdir/tinyglobby/Vite; [advisory GHSA-fc5q-39xc-vjpf](https://github.com/advisories/GHSA-fc5q-39xc-vjpf) chưa có bản vá tại thời điểm kiểm tra. Đây là công cụ build, không nằm trong dependency runtime của web tĩnh. Không dùng `npm audit fix --force` để hạ Vite tự động.
- Giao dịch sai hoặc đến muộn cần Admin đối soát. Bản này chưa tự hoàn tiền và chưa hủy booking đã thanh toán.
