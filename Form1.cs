using System;
using System.Collections.Generic;
using System.Drawing;
using System.Windows.Forms;
using System.Net;
using System.Net.Sockets;
using System.IO;
using System.Threading.Tasks;

namespace GameCaro
{
    public partial class Form1 : Form
    {
        private const int SoDong = 15;
        private const int SoCot = 15;
        private const int KichThuocO = 30;
        private const int Port = 9999;

        // true  = luật Việt Nam: 5 quân liên tiếp nhưng bị đối thủ chặn CẢ 2 ĐẦU thì không thắng
        // false = luật tự do: 5 quân liên tiếp là thắng
        private const bool LuatChan2Dau = true;

        private TcpListener? hostListener;
        private TcpClient? tcpClient;
        private StreamReader? reader;
        private StreamWriter? writer;

        private readonly Button[,] oCoButtons = new Button[SoDong, SoCot];
        private readonly char[,] trangThaiBanCo = new char[SoDong, SoCot];

        private bool isHost = false;
        private bool daKetNoi = false;
        private bool dangLuotMinh = false;
        private bool tranDauKetThuc = false;
        private bool dangDongForm = false;
        private char kyHieuCuaMinh = 'X';
        private char kyHieuDoiThu = 'O';

        private int soVan = 0;                 // số thứ tự ván: chẵn -> Host đi trước, lẻ -> Client đi trước
        private int soThang = 0, soThua = 0, soHoa = 0;
        private Point? nuocCuoi = null;        // nước đi gần nhất (để tô sáng)

        public Form1()
        {
            InitializeComponent();
            VeBanCo();
            CapNhatTrangThaiGiaoDien();

            FormClosing += Form1_FormClosing;
            txtMessage.KeyDown += TxtMessage_KeyDown;
        }

        // ===================== GIAO DIỆN BÀN CỜ =====================

        // 1. Vẽ bàn cờ Caro 15x15
        private void VeBanCo()
        {
            pnlBoard.Controls.Clear();
            for (int i = 0; i < SoDong; i++)
            {
                for (int j = 0; j < SoCot; j++)
                {
                    Button btn = new Button()
                    {
                        Width = KichThuocO,
                        Height = KichThuocO,
                        Location = new Point(j * KichThuocO, i * KichThuocO),
                        Tag = new Point(i, j),
                        Font = new Font("Segoe UI", 12F, FontStyle.Bold),
                        TabStop = false,
                        Padding = Padding.Empty
                    };
                    // Không dùng Enabled=false vì nút bị vô hiệu hóa sẽ hiện chữ màu xám,
                    // làm mất màu xanh/đỏ của quân cờ. Việc chặn click do OCo_Click đảm nhiệm.

                    btn.Click += OCo_Click;
                    oCoButtons[i, j] = btn;
                    trangThaiBanCo[i, j] = '\0';
                    pnlBoard.Controls.Add(btn);
                }
            }
        }

        // 2. Làm mới bàn cờ khi bắt đầu ván mới
        private void LamMoiBanCo()
        {
            for (int i = 0; i < SoDong; i++)
            {
                for (int j = 0; j < SoCot; j++)
                {
                    oCoButtons[i, j].Text = "";
                    oCoButtons[i, j].BackColor = SystemColors.Control;
                    trangThaiBanCo[i, j] = '\0';
                }
            }
            nuocCuoi = null;
            tranDauKetThuc = false;
        }

        // 3. Người chơi click vào 1 ô
        private void OCo_Click(object? sender, EventArgs e)
        {
            if (!daKetNoi || tranDauKetThuc || !dangLuotMinh) return;
            if (sender is not Button btn || btn.Tag is not Point viTri) return;

            int row = viTri.X;
            int col = viTri.Y;
            if (trangThaiBanCo[row, col] != '\0') return;

            DanhCo(row, col, kyHieuCuaMinh);
            GuiTinNhan("MOVE:" + row + "," + col);

            var duongThang = KiemTraThang(row, col, kyHieuCuaMinh);
            if (duongThang != null)
            {
                KetThucVan(KetQua.Thang, duongThang);
            }
            else if (KiemTraHoa())
            {
                KetThucVan(KetQua.Hoa, null);
            }
            else
            {
                dangLuotMinh = false;
                CapNhatTrangThaiGiaoDien();
            }
        }

        // 4. Đặt quân cờ lên bàn (dùng chung cho nước đi của mình và đối thủ)
        private void DanhCo(int row, int col, char kyHieu)
        {
            // Bỏ tô sáng nước đi trước
            if (nuocCuoi.HasValue)
                oCoButtons[nuocCuoi.Value.X, nuocCuoi.Value.Y].BackColor = SystemColors.Control;

            trangThaiBanCo[row, col] = kyHieu;
            oCoButtons[row, col].Text = kyHieu.ToString();
            oCoButtons[row, col].ForeColor = kyHieu == 'X' ? Color.Blue : Color.Red;
            oCoButtons[row, col].BackColor = Color.LightYellow;
            nuocCuoi = new Point(row, col);
        }

        // ===================== LUẬT CHƠI =====================

        private enum KetQua { Thang, Thua, Hoa }

        // 5. Hòa cờ (hết ô trống)
        private bool KiemTraHoa()
        {
            for (int i = 0; i < SoDong; i++)
                for (int j = 0; j < SoCot; j++)
                    if (trangThaiBanCo[i, j] == '\0') return false;
            return true;
        }

        // 6. Kiểm tra thắng. Trả về danh sách các ô tạo thành đường thắng, hoặc null nếu chưa thắng
        private List<Point>? KiemTraThang(int row, int col, char kyHieu)
        {
            char doiThu = kyHieu == 'X' ? 'O' : 'X';
            int[][] huongs = new int[][]
            {
                new int[] { 0, 1 },  // ngang
                new int[] { 1, 0 },  // dọc
                new int[] { 1, 1 },  // chéo xuống phải
                new int[] { 1, -1 }  // chéo xuống trái
            };

            foreach (var h in huongs)
            {
                int truoc = DemLienTiep(row, col, -h[0], -h[1], kyHieu);
                int sau = DemLienTiep(row, col, h[0], h[1], kyHieu);
                int dem = 1 + truoc + sau;
                if (dem < 5) continue;

                if (LuatChan2Dau)
                {
                    bool chanDau1 = BiDoiThuChan(row - (truoc + 1) * h[0], col - (truoc + 1) * h[1], doiThu);
                    bool chanDau2 = BiDoiThuChan(row + (sau + 1) * h[0], col + (sau + 1) * h[1], doiThu);
                    if (chanDau1 && chanDau2) continue; // bị chặn 2 đầu -> không tính thắng
                }

                var duong = new List<Point>();
                for (int k = -truoc; k <= sau; k++)
                    duong.Add(new Point(row + k * h[0], col + k * h[1]));
                return duong;
            }
            return null;
        }

        private int DemLienTiep(int row, int col, int dRow, int dCol, char kyHieu)
        {
            int dem = 0;
            int r = row + dRow;
            int c = col + dCol;
            while (r >= 0 && r < SoDong && c >= 0 && c < SoCot && trangThaiBanCo[r, c] == kyHieu)
            {
                dem++;
                r += dRow;
                c += dCol;
            }
            return dem;
        }

        // Đầu mút có quân đối thủ -> bị chặn (mép bàn cờ KHÔNG tính là chặn)
        private bool BiDoiThuChan(int r, int c, char doiThu)
        {
            return r >= 0 && r < SoDong && c >= 0 && c < SoCot && trangThaiBanCo[r, c] == doiThu;
        }

        // 7. Kết thúc ván: cập nhật điểm, tô sáng đường thắng, thông báo
        private void KetThucVan(KetQua ketQua, List<Point>? duongThang)
        {
            tranDauKetThuc = true;

            if (duongThang != null)
                foreach (var p in duongThang)
                    oCoButtons[p.X, p.Y].BackColor = Color.LightGreen;

            string thongBao;
            switch (ketQua)
            {
                case KetQua.Thang: soThang++; thongBao = "Chúc mừng! Bạn đã thắng!"; break;
                case KetQua.Thua: soThua++; thongBao = "Rất tiếc, đối thủ đã thắng!"; break;
                default: soHoa++; thongBao = "Ván đấu hòa!"; break;
            }

            CapNhatTrangThaiGiaoDien();
            MessageBox.Show(thongBao, "Kết thúc ván đấu");
        }

        // ===================== KẾT NỐI MẠNG =====================

        // Lấy các địa chỉ IPv4 trong mạng LAN/WiFi của máy này để người kia nhập vào
        private static List<string> LayDiaChiIPCucBo()
        {
            var ds = new List<string>();
            try
            {
                foreach (var ip in Dns.GetHostEntry(Dns.GetHostName()).AddressList)
                    if (ip.AddressFamily == AddressFamily.InterNetwork && !IPAddress.IsLoopback(ip))
                        ds.Add(ip.ToString());
            }
            catch { }
            return ds;
        }

        // 8. Tạo phòng (Host) - lượt đầu tiên của Host là quân X
        private async void btnHost_Click(object? sender, EventArgs e)
        {
            try
            {
                btnHost.Enabled = false;
                btnJoin.Enabled = false;
                isHost = true;

                hostListener = new TcpListener(IPAddress.Any, Port);
                hostListener.Start();

                rtbChat.AppendText("Đã tạo phòng. Hãy bảo đối thủ nhập một trong các IP sau:\n");
                var dsIP = LayDiaChiIPCucBo();
                if (dsIP.Count == 0) rtbChat.AppendText("  (Không lấy được IP, hãy gõ ipconfig để xem)\n");
                foreach (var ip in dsIP) rtbChat.AppendText("  " + ip + "\n");
                rtbChat.AppendText("Đang chờ đối thủ kết nối...\n");

                tcpClient = await hostListener.AcceptTcpClientAsync();
                hostListener.Stop(); // chỉ nhận 1 đối thủ
                hostListener = null;

                rtbChat.AppendText("Đã có đối thủ vào phòng! Bắt đầu ván đấu.\n");

                kyHieuCuaMinh = 'X';
                kyHieuDoiThu = 'O';
                daKetNoi = true;
                soThang = soThua = soHoa = 0;
                BatDauVanMoi(0);

                ThietLapLuongDuLieu();
            }
            catch (Exception ex)
            {
                if (dangDongForm) return;
                MessageBox.Show("Lỗi tạo phòng: " + ex.Message +
                    "\n\nNếu lỗi do cổng đang bị dùng hoặc Firewall, hãy đóng chương trình cũ / cho phép ứng dụng qua Windows Firewall.");
                DongKetNoi();
            }
        }

        // 9. Kết nối tới phòng (Client) - luôn là quân O
        private async void btnJoin_Click(object? sender, EventArgs e)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(txtIP.Text))
                {
                    MessageBox.Show("Vui lòng nhập địa chỉ IP của người tạo phòng.");
                    return;
                }

                btnHost.Enabled = false;
                btnJoin.Enabled = false;
                isHost = false;

                tcpClient = new TcpClient();
                rtbChat.AppendText("Đang kết nối tới " + txtIP.Text.Trim() + "...\n");

                var ketNoi = tcpClient.ConnectAsync(txtIP.Text.Trim(), Port);
                if (await Task.WhenAny(ketNoi, Task.Delay(5000)) != ketNoi)
                    throw new TimeoutException("Hết thời gian chờ. Kiểm tra IP, cùng mạng WiFi và Firewall của máy Host.");
                await ketNoi; // ném lại exception nếu kết nối lỗi

                rtbChat.AppendText("Kết nối thành công! Bắt đầu ván đấu.\n");

                kyHieuCuaMinh = 'O';
                kyHieuDoiThu = 'X';
                daKetNoi = true;
                soThang = soThua = soHoa = 0;
                BatDauVanMoi(0);

                ThietLapLuongDuLieu();
            }
            catch (Exception ex)
            {
                if (dangDongForm) return;
                MessageBox.Show("Lỗi kết nối: " + ex.Message);
                DongKetNoi();
            }
        }

        // 10. Khởi tạo luồng đọc/ghi dữ liệu
        private void ThietLapLuongDuLieu()
        {
            if (tcpClient == null) return;
            var stream = tcpClient.GetStream();
            reader = new StreamReader(stream);
            writer = new StreamWriter(stream) { AutoFlush = true };

            _ = NhanDuLieuLienTucAsync();
        }

        // 11. Lắng nghe đối thủ liên tục (continuation đã chạy trên luồng UI nên không cần Invoke)
        private async Task NhanDuLieuLienTucAsync()
        {
            try
            {
                while (reader != null)
                {
                    string? data = await reader.ReadLineAsync();
                    if (data == null) break;
                    XuLyDuLieuNhanDuoc(data);
                }
            }
            catch
            {
                // Kết nối bị ngắt đột ngột
            }

            if (!dangDongForm && daKetNoi)
            {
                rtbChat.AppendText("Đối thủ đã ngắt kết nối.\n");
                MessageBox.Show("Đối thủ đã ngắt kết nối.", "Mất kết nối");
                DongKetNoi();
            }
        }

        // 12. Đóng kết nối, đưa giao diện về trạng thái ban đầu để tạo/vào phòng lại
        private void DongKetNoi()
        {
            daKetNoi = false;
            dangLuotMinh = false;
            tranDauKetThuc = true;

            try { writer?.Close(); } catch { }
            try { reader?.Close(); } catch { }
            try { tcpClient?.Close(); } catch { }
            try { hostListener?.Stop(); } catch { }
            writer = null;
            reader = null;
            tcpClient = null;
            hostListener = null;

            if (!dangDongForm)
            {
                btnHost.Enabled = true;
                btnJoin.Enabled = true;
                CapNhatTrangThaiGiaoDien();
            }
        }

        private void Form1_FormClosing(object? sender, FormClosingEventArgs e)
        {
            dangDongForm = true;
            DongKetNoi();
        }

        // 13. Xử lý dữ liệu nhận được từ đối thủ
        private void XuLyDuLieuNhanDuoc(string data)
        {
            if (data.StartsWith("MOVE:"))
            {
                string[] parts = data.Substring(5).Split(',');
                if (parts.Length != 2) return;
                if (!int.TryParse(parts[0], out int row)) return;
                if (!int.TryParse(parts[1], out int col)) return;
                if (row < 0 || row >= SoDong || col < 0 || col >= SoCot) return;

                // Bỏ qua nước đi sai luật (không phải lượt đối thủ / ô đã có quân / ván đã kết thúc)
                if (dangLuotMinh || tranDauKetThuc || trangThaiBanCo[row, col] != '\0') return;

                DanhCo(row, col, kyHieuDoiThu);

                var duongThang = KiemTraThang(row, col, kyHieuDoiThu);
                if (duongThang != null)
                {
                    KetThucVan(KetQua.Thua, duongThang);
                }
                else if (KiemTraHoa())
                {
                    KetThucVan(KetQua.Hoa, null);
                }
                else
                {
                    dangLuotMinh = true;
                    CapNhatTrangThaiGiaoDien();
                }
            }
            else if (data.StartsWith("CHAT:"))
            {
                rtbChat.AppendText("Đối thủ: " + data.Substring(5) + "\n");
            }
            else if (data == "ASK_RESET")
            {
                // Đối thủ muốn chơi lại khi ván đang dở -> hỏi ý kiến
                var tl = MessageBox.Show("Đối thủ muốn bắt đầu lại ván mới. Bạn đồng ý không?",
                    "Yêu cầu chơi lại", MessageBoxButtons.YesNo, MessageBoxIcon.Question);
                if (tl == DialogResult.Yes)
                {
                    BatDauVanMoi(soVan + 1);
                    GuiTinNhan("RESET:" + soVan);
                    rtbChat.AppendText("Bạn đã đồng ý chơi lại.\n");
                }
                else
                {
                    GuiTinNhan("DECLINE");
                }
            }
            else if (data == "DECLINE")
            {
                rtbChat.AppendText("Đối thủ từ chối chơi lại.\n");
            }
            else if (data.StartsWith("RESET:"))
            {
                if (int.TryParse(data.Substring(6), out int vanMoi))
                {
                    BatDauVanMoi(vanMoi);
                    rtbChat.AppendText("Ván mới bắt đầu.\n");
                }
            }
        }

        // 14. Gửi dữ liệu qua mạng
        private void GuiTinNhan(string noiDung)
        {
            try
            {
                writer?.WriteLine(noiDung);
            }
            catch
            {
                rtbChat.AppendText("Lỗi gửi dữ liệu, có thể đối thủ đã ngắt kết nối.\n");
            }
        }

        // ===================== CHAT & CHƠI LẠI =====================

        // 15. Gửi tin nhắn chat
        private void btnSend_Click(object? sender, EventArgs e)
        {
            if (daKetNoi && !string.IsNullOrWhiteSpace(txtMessage.Text))
            {
                string message = txtMessage.Text.Replace("\r", " ").Replace("\n", " ");
                GuiTinNhan("CHAT:" + message);
                rtbChat.AppendText("Tôi: " + message + "\n");
                txtMessage.Clear();
            }
        }

        // Nhấn Enter trong ô chat để gửi
        private void TxtMessage_KeyDown(object? sender, KeyEventArgs e)
        {
            if (e.KeyCode == Keys.Enter)
            {
                e.SuppressKeyPress = true;
                btnSend_Click(this, EventArgs.Empty);
            }
        }

        // 16. Bắt đầu ván mới
        //  - Ván đã kết thúc: bắt đầu ngay, đồng bộ 2 bên
        //  - Ván đang chơi dở: gửi yêu cầu, đối thủ đồng ý mới chơi lại
        private void btnRestart_Click(object? sender, EventArgs e)
        {
            if (!daKetNoi) return;

            if (tranDauKetThuc)
            {
                BatDauVanMoi(soVan + 1);
                GuiTinNhan("RESET:" + soVan);
                rtbChat.AppendText("Bạn đã bắt đầu ván mới.\n");
            }
            else
            {
                GuiTinNhan("ASK_RESET");
                rtbChat.AppendText("Đã gửi yêu cầu chơi lại, chờ đối thủ đồng ý...\n");
            }
        }

        // Ván chẵn: Host đi trước. Ván lẻ: Client đi trước (luân phiên công bằng)
        private void BatDauVanMoi(int soVanMoi)
        {
            soVan = soVanMoi;
            LamMoiBanCo();
            dangLuotMinh = (soVan % 2 == 0) ? isHost : !isHost;
            CapNhatTrangThaiGiaoDien();
        }

        // ===================== CẬP NHẬT GIAO DIỆN =====================

        // 17. Cập nhật toàn bộ giao diện theo trạng thái ván đấu
        private void CapNhatTrangThaiGiaoDien()
        {
            bool choPhepDanh = daKetNoi && !tranDauKetThuc && dangLuotMinh;

            for (int i = 0; i < SoDong; i++)
                for (int j = 0; j < SoCot; j++)
                    oCoButtons[i, j].Cursor = (choPhepDanh && trangThaiBanCo[i, j] == '\0')
                        ? Cursors.Hand : Cursors.Default;

            btnRestart.Enabled = daKetNoi;
            btnSend.Enabled = daKetNoi;

            if (!daKetNoi)
            {
                lblStatus.Text = "Chưa kết nối";
                Text = "Game Caro";
                return;
            }

            string tiSo = $"Thắng {soThang} - Thua {soThua} - Hòa {soHoa}";
            Text = "Game Caro  |  " + tiSo;

            if (tranDauKetThuc)
                lblStatus.Text = "Ván đấu kết thúc. Bấm \"Chơi lại\" để chơi tiếp.  [" + tiSo + "]";
            else
                lblStatus.Text = (dangLuotMinh
                    ? $"Lượt của bạn (quân {kyHieuCuaMinh})"
                    : $"Đang chờ đối thủ (quân {kyHieuDoiThu}) đánh...") + "  [" + tiSo + "]";
        }
    }
}
