# Game Caro LAN (WinForms, .NET 8)

Game Caro 15x15 cho 2 người chơi trên cùng mạng WiFi/LAN.

## Chạy bản WinForms
- **Visual Studio 2022**: mở `GameCaro.sln`, chọn project `GameCaro`, bấm F5. (Cần workload ".NET desktop development" và .NET 8 SDK.)
- **Dòng lệnh** (Windows, từ thư mục gốc repository):

```powershell
dotnet run --project .\GameCaro.csproj
```

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

Giao diện web có chat trực tuyến, đồng hồ lượt, điểm số theo loạt tối đa 3 ván, bật/tắt âm thanh và nút xin thua. Hoàn tác nước gần nhất và chơi lại trong phòng hai người đều gửi yêu cầu qua SignalR; đối phương phải đồng ý trước khi máy chủ cập nhật trạng thái. Khi ván kết thúc, trang hiển thị hộp thoại kết quả và cho phép gửi yêu cầu chơi ván mới. Tin nhắn chat không được lưu sau khi rời phòng hoặc khởi động lại máy chủ.

Khi mở web, người chơi nhập nickname (tối đa 20 ký tự) rồi chọn tạo phòng, vào phòng bằng mã hoặc chơi với bot. Nickname được lưu trong sessionStorage để điền lại khi tải lại trang, gửi tới máy chủ SignalR và hiển thị cho cả hai người trong phòng.

Giao diện hiển thị rõ lượt X/O, tên người chơi, điểm và đồng hồ. Khi người chơi mất kết nối, SignalR tự kết nối lại; mã phiên được lưu trong sessionStorage của tab để khôi phục phòng và bàn cờ. Sau khi máy chủ phát hiện ngắt kết nối, phòng được giữ tối đa 45 giây. Mặc định trạng thái ở trong bộ nhớ và chỉ phù hợp với một instance; để đồng bộ trạng thái phòng qua nhiều instance, cấu hình Redis bằng biến môi trường `ConnectionStrings__Redis` (ví dụ `localhost:6379`). Khi cấu hình Redis, ứng dụng dùng Redis cho cả SignalR backplane và lưu trạng thái phòng. Redis cần được triển khai bền vững riêng; một khóa phân tán hiện tuần tự hóa thao tác phòng trên toàn ứng dụng để ưu tiên tính đúng đắn, nên có thể trở thành nút thắt khi số phòng/lượt chơi tăng cao.

Bản web cũng hỗ trợ chơi với bot nội bộ ở ba mức dễ, trung bình và khó; không cần API key hoặc dịch vụ AI bên ngoài. Bot chờ khoảng 400–500 ms trước mỗi nước đi, đồng thời ưu tiên nước thắng và chặn nước thắng trực tiếp ở hai mức trung bình/khó. Nước đi của người chơi được hiển thị ngay trên giao diện và sẽ được hoàn tác nếu máy chủ từ chối. Các nước đi mới có hiệu ứng xuất hiện. Lịch sử lưu 5 kết quả gần nhất trên localStorage của trình duyệt. Có thể rời phòng để chủ phòng đóng phòng hoặc khách nhường chỗ cho người chơi khác. Có thể đổi độ khó trong ván với máy; lựa chọn mới áp dụng từ nước đi bot tiếp theo và được giữ khi chơi lại, kể cả sau khi người chơi xin thua.

### Chạy bản web trên máy phát triển

Cần cài .NET 8 SDK. Mở PowerShell tại thư mục gốc repository (`LapTrinhMang_nhom8`) rồi chạy:

```powershell
dotnet --version
dotnet restore .\CaroWeb\CaroWeb.csproj
dotnet run --project .\CaroWeb\CaroWeb.csproj
```

Mở URL được in ra trong terminal (thường là `http://localhost:5000` hoặc `https://localhost:5001`). Có thể cố định HTTP port 5000 bằng lệnh:

```powershell
dotnet run --project .\CaroWeb\CaroWeb.csproj -- --urls http://localhost:5000
```

Giữ cửa sổ terminal đang chạy để máy chủ hoạt động; nhấn `Ctrl+C` để dừng. Để thử trên cùng máy, mở hai cửa sổ/tab trình duyệt, tạo phòng ở tab thứ nhất rồi nhập mã phòng ở tab thứ hai. Để người khác truy cập qua Internet, ứng dụng web phải được triển khai lên máy chủ có HTTPS và hỗ trợ WebSocket/SignalR.

Nếu Windows Application Control chặn `CaroWeb.exe` khi chạy `dotnet run`, có thể chạy assembly trực tiếp:

```powershell
dotnet build .\CaroWeb\CaroWeb.csproj --configuration Release
Set-Location .\CaroWeb
dotnet .\bin\Release\net8.0\CaroWeb.dll --urls http://localhost:5000
```

Giữ terminal chạy server; nhấn `Ctrl+C` để dừng.

Mặc định bản local không cần Redis và dùng trạng thái trong bộ nhớ của một instance. Chỉ cấu hình Redis khi thử nghiệm nhiều instance; cần Redis đang chạy và thiết lập connection string trước khi khởi động:

```powershell
$env:ConnectionStrings__Redis = "localhost:6379"
dotnet run --project .\CaroWeb\CaroWeb.csproj
```

### Triển khai máy chủ

Có thể triển khai `CaroWeb` trên dịch vụ hosting hỗ trợ Docker. Chọn thư mục `CaroWeb` làm thư mục build/root; Dockerfile đã có sẵn. Sau khi deploy, chia sẻ URL HTTPS của ứng dụng. Dùng một instance nếu không cấu hình Redis. Khi scale nhiều instance, hãy cấu hình `ConnectionStrings__Redis` trỏ đến Redis có mạng riêng và xác thực phù hợp. Mã phòng, token người chơi, bàn cờ, điểm số và thời điểm mất kết nối được lưu trong một JSON state chung ở Redis; chat và lịch sử trình duyệt vẫn chưa được lưu vào cơ sở dữ liệu. Khóa Redis hiện dùng chung cho toàn bộ phòng, vì vậy đây là triển khai cơ sở để thử nghiệm multi-instance chứ chưa phải thiết kế tối ưu thông lượng cao.

Để chạy nhiều instance, cung cấp `ConnectionStrings__Redis` cho từng instance và dùng Redis sẵn sàng cho môi trường triển khai.

### Chạy kiểm thử tự động

Chạy bộ kiểm thử xUnit từ thư mục gốc repository:

```powershell
dotnet test .\CaroWeb.Tests\CaroWeb.Tests.csproj
```

Các kiểm thử bao gồm luật thắng/chặn hai đầu, nước đi thắng/chặn của bot, nickname hợp lệ/không hợp lệ, từ chối nước đi sai lượt, mã phòng sai định dạng và khôi phục bàn cờ sau khi người chơi ngắt kết nối đột ngột.
