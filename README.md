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
