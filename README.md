# ToolADB

Tool quản lý thiết bị Android qua ADB (WinForms, .NET 8, Windows). Gom nhiều tiện ích vào một giao diện, chạy đồng loạt trên nhiều thiết bị đã tích chọn.

## Các tab

| Tab | Chức năng |
|---|---|
| **Dashboard** | Quản lý thiết bị: liệt kê, chọn, các thao tác chung |
| **Auto Login & 2FA** | Tự động đăng nhập tài khoản, quản lý kho tài khoản (Kho / Đã dùng) |
| **GeoSpoof** | Giả lập vị trí |
| **Picsart** | Xử lý riêng Picsart: kiểm tra cấu hình, mở bảng giá, ép gói Plus/Pro tháng, reset giữ đăng nhập, xóa dữ liệu |
| **Inapp Trial** | Bỏ bước trial của các app để mua thẳng: Grammarly (tắt trial 7 ngày), Chess.com (bỏ offer trial, có sao lưu cache Billing) |

## Yêu cầu

- Windows + .NET 8 SDK (build bằng `dotnet build`)
- Thiết bị Android cấp được **ADB root** (`adb root`) cho các tab Picsart / Inapp Trial
- Chess.com cần thiết bị có sẵn `sqlite3`

## Thêm app mới vào tab Inapp Trial

1. Thêm giá trị vào enum `InappTrialAction` trong `ToolADB/Features/InappTrial/InappTrialService.cs`
2. Tạo file `InappTrialService.<TênApp>.cs` chứa logic vá (partial class)
3. Thêm một dòng `AddAction(...)` trong `InappTrialTab.cs`

## Quy trình phát triển

Tính năng mới được viết và thử nghiệm trước ở repo [Inapp-Patch](https://github.com/ngthgila/Inapp-Patch) (tool standalone), sau đó port sang ToolADB thành tab/feature tương ứng trong thư mục `ToolADB/Features/`.
