# Game Caro LAN (WinForms, .NET 8)

Game Caro 15x15 cho 2 người chơi trên cùng mạng WiFi/LAN.

## Cách mở và chạy
- **Visual Studio 2022**: mở `GameCaro.sln`, bấm F5. (Cần workload ".NET desktop development" và .NET 8 SDK.)
- **Dòng lệnh** (Windows): `dotnet run`

## Cách chơi qua WiFi
1. Máy A bấm **Tạo phòng** -> khung chat hiện IP của máy A.
2. Máy B nhập IP đó vào ô IP, bấm **Vào phòng**.
3. Lần đầu Host chạy, Windows Firewall hỏi -> chọn **Cho phép (Private network)**. Cổng dùng: TCP 9999.

Thử trên 1 máy: mở 2 cửa sổ game, một bên Tạo phòng, bên kia giữ IP 127.0.0.1 rồi Vào phòng.

## Luật
- 5 quân liên tiếp (ngang/dọc/chéo) là thắng.
- `LuatChan2Dau = true` (trong Form1.cs): 5 quân bị chặn cả 2 đầu thì không thắng (luật VN). Đặt `false` cho luật tự do.
- Ván chẵn Host đi trước, ván lẻ Client đi trước.
- Chơi lại giữa ván cần đối thủ đồng ý.

## Bản web: chơi bằng trình duyệt

Thư mục `CaroWeb` chứa phiên bản web dùng ASP.NET Core SignalR. Người chơi mở cùng một địa chỉ web, một người tạo phòng rồi gửi mã 6 ký tự cho người còn lại. Không cần cài ứng dụng hoặc cấu hình IP/cổng trên máy người chơi. Bản WinForms hiện có vẫn được giữ nguyên.

### Chơi trực tuyến

Mở [Caro Club](https://laptrinhmang-nhom8.onrender.com) để chơi trực tuyến. Một người tạo phòng rồi chia sẻ mã phòng cho người còn lại.

Giao diện web có chat trực tuyến, đồng hồ lượt, điểm số theo loạt tối đa 3 ván, bật/tắt âm thanh và nút xin thua. Nút hoàn tác đang bị khóa vì chưa có hỗ trợ đồng bộ nước đi ngược trên máy chủ. Lịch sử chat/điểm số chưa được lưu sau khi rời phòng hoặc khởi động lại máy chủ.

### Chạy local

Cài .NET 8 SDK, mở terminal tại thư mục gốc repository rồi chạy:

```powershell
dotnet run --project .\CaroWeb\CaroWeb.csproj
```

Mở [http://localhost:5000](http://localhost:5000) trong trình duyệt. Để thử hai người trên cùng máy, mở URL trong hai cửa sổ trình duyệt. Nhấn `Ctrl+C` trong terminal để dừng server.

Để người khác truy cập qua Internet, dùng bản đã triển khai: [Caro Club](https://laptrinhmang-nhom8.onrender.com). Ứng dụng web cần được host trên máy chủ có HTTPS và hỗ trợ WebSocket/SignalR.

### Triển khai máy chủ

Có thể triển khai `CaroWeb` trên dịch vụ hosting hỗ trợ Docker. Chọn thư mục `CaroWeb` làm thư mục build/root; Dockerfile đã có sẵn. Sau khi deploy, chia sẻ URL HTTPS của ứng dụng. Các phòng hiện được lưu trong bộ nhớ của một tiến trình máy chủ; dùng một instance, không bật scale nhiều instance nếu chưa cấu hình kho trạng thái dùng chung. Restart ứng dụng sẽ đóng các phòng đang chơi.
