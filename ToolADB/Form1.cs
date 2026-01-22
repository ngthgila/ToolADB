using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text.Json;
using System.Windows.Forms;
using System.Linq;
using System.Threading.Tasks;
using System.IO.Compression;
using System.Drawing;
using System.Threading;
using System.Security.Cryptography;
using System.Text.RegularExpressions;
using Guna.UI2.WinForms;

namespace ToolAdb
{
    public partial class Form1 : Form
    {
        // ==========================================
        // 1. CẤU HÌNH & BIẾN
        // ==========================================
        private Dictionary<string, string> _deviceNameById = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        private string BaseDir => AppDomain.CurrentDomain.BaseDirectory;
        private string ApkDir => Path.Combine(BaseDir, "APK");
        private string AccountFile => Path.Combine(BaseDir, "accounts.txt");
        private string UsedAccountFile => Path.Combine(BaseDir, "used_accounts.txt");
        private string AdbPath => Path.Combine(BaseDir, "adb.exe");
        private object _saveLock = new object();
        private System.Windows.Forms.Timer _inputScanTimer = null!;

        // UI Controls (Null Forgiving)
        private CheckedListBox _clbSidebarDevices = null!;
        private Label _lblStatusInfo = null!;
        private Guna2ProgressBar _progressBar = null!;

        // Auto Refresh Timer
        private System.Windows.Forms.Timer _deviceWatcherTimer = null!;
        private string _lastDeviceHash = "";

        // Tab Auto Login & 2FA
        private TextBox _txtAccountInput = null!;
        private TextBox _txtSecretInput = null!;
        private Guna2DataGridView _grid2Fa = null!;
        private System.Windows.Forms.Timer _totpTimer = null!;

        // Account Manager Storage
        private Guna2DataGridView _gridStorage = null!;
        private Guna2DataGridView _gridUsed = null!;
        private Guna2TabControl _tabStorage = null!;
        private Label _lblStorageCount = null!;

        // Tab Convert
        private TextBox _txtConvertIn = null!;
        private TextBox _txtConvertOut = null!;

        public Form1()
        {
            InitializeComponent();
            // --- THÊM ĐOẠN NÀY ĐỂ HIỆN ICON TRÊN TASKBAR & TIÊU ĐỀ ---
            try
            {
                // Thay 'adbtools.ico' bằng đúng tên file icon của bạn
                this.Icon = new Icon("icon.ico");
            }
            catch
            {
                // Nếu không thấy file icon thì thôi, không báo lỗi
            }
            CheckForIllegalCrossThreadCalls = false;


            BuildModernLayout();
            ValidateAdbExists();
            TryWarmAdbServer();
            RefreshDeviceList();

            // Timer 2FA (1s)
            _totpTimer = new System.Windows.Forms.Timer { Interval = 1000 };
            _totpTimer.Tick += (s, e) => Update2FaGridCodes();
            _totpTimer.Start();

            // Timer Auto Detect Devices (3s)
            _deviceWatcherTimer = new System.Windows.Forms.Timer { Interval = 3000 };
            _deviceWatcherTimer.Tick += DeviceWatcher_Tick;
            _deviceWatcherTimer.Start();
            // Timer quét Input (Delay 500ms để không làm phiền khi đang gõ)
            _inputScanTimer = new System.Windows.Forms.Timer { Interval = 500 };
            _inputScanTimer.Tick += (s, e) => ScanInputFor2Fa();
        }

        // ==========================================
        // 2. GIAO DIỆN CHÍNH
        // ==========================================
        private void BuildModernLayout()
        {
            this.Controls.Clear();
            this.Size = new Size(800, 600);
            this.Text = "ADB Pro v2.5";
            this.BackColor = Color.FromArgb(243, 244, 246);
            this.Font = new Font("Segoe UI", 9f);

            var splitMain = new SplitContainer
            {
                Dock = DockStyle.Fill,
                FixedPanel = FixedPanel.Panel1,
                SplitterDistance = 260,
                IsSplitterFixed = true,
                BackColor = Color.FromArgb(229, 231, 235)
            };

            // --- SIDEBAR ---
            var pnlSidebar = splitMain.Panel1;
            pnlSidebar.BackColor = Color.White;
            pnlSidebar.Padding = new Padding(10);
            var lblSideTitle = new Label { Text = "Thhiết Bị", Dock = DockStyle.Top, Height = 30, Font = new Font("Segoe UI", 10f, FontStyle.Bold), ForeColor = Color.Green };

            _clbSidebarDevices = new CheckedListBox
            {
                Dock = DockStyle.Fill,
                BorderStyle = BorderStyle.None,
                CheckOnClick = true,
                Font = new Font("Segoe UI", 10f),
                IntegralHeight = false
            };

            var pnlSideBtns = new Panel { Dock = DockStyle.Bottom, Height = 140 };

            var btnSelectAll = CreateSideButton("Chọn Tất Cả", Color.FromArgb(59, 130, 246));
            btnSelectAll.Dock = DockStyle.Top;
            btnSelectAll.Click += (s, e) => ToggleSidebarSelection(true);

            var btnSelectNone = CreateSideButton("Bỏ Chọn", Color.FromArgb(100, 116, 139));
            btnSelectNone.Dock = DockStyle.Top;
            btnSelectNone.Click += (s, e) => ToggleSidebarSelection(false);

            var btnRenameSide = CreateSideButton("Đổi tên", Color.FromArgb(245, 158, 11));
            btnRenameSide.Dock = DockStyle.Top;
            btnRenameSide.Click += (s, e) => RenameSelectedDevice();

            var btnRefreshSide = CreateSideButton("Refresh", Color.FromArgb(16, 185, 129));
            btnRefreshSide.Dock = DockStyle.Bottom;
            btnRefreshSide.Click += (s, e) => RefreshDeviceList();

            var pnlSpacer = new Panel { Dock = DockStyle.Fill };
            pnlSideBtns.Controls.AddRange(new Control[] { pnlSpacer, btnRefreshSide, btnRenameSide, btnSelectNone, btnSelectAll });
            pnlSidebar.Controls.AddRange(new Control[] { _clbSidebarDevices, pnlSideBtns, lblSideTitle });

            // --- CONTENT ---
            var pnlContent = splitMain.Panel2;
            pnlContent.BackColor = Color.FromArgb(243, 244, 246);

            var tabControl = new Guna2TabControl
            {
                Dock = DockStyle.Fill,
                TabButtonIdleState = { FillColor = Color.Transparent, ForeColor = Color.Gray, Font = new Font("Segoe UI Semibold", 10f) },
                TabButtonHoverState = { FillColor = Color.White, InnerColor = Color.White, ForeColor = Color.FromArgb(79, 70, 229) },
                TabButtonSelectedState = { FillColor = Color.White, ForeColor = Color.FromArgb(79, 70, 229), InnerColor = Color.FromArgb(79, 70, 229) },
                Alignment = TabAlignment.Top,
                ItemSize = new Size(120, 40)
            };

            // TAB 1: DASHBOARD
            var tabDash = new TabPage { Text = "Dashboard", BackColor = Color.FromArgb(248, 250, 252) };
            var flowDash = new FlowLayoutPanel { Dock = DockStyle.Fill, AutoScroll = true, Padding = new Padding(15), FlowDirection = FlowDirection.LeftToRight };

            var cardSetup = CreateGroupbox("Thiết lập & Cài đặt");
            AddBtn(cardSetup, "Setup Android (All)", Color.FromArgb(16, 185, 129), async () => await ActionSetupAll());
            AddBtn(cardSetup, "Cài File .APKM", Color.FromArgb(16, 185, 129), async () => await ActionInstallApkmFileDialog());
            AddBtn(cardSetup, "Gửi File vào máy", Color.FromArgb(16, 185, 129), async () => await ActionPushFile());
            AddBtn(cardSetup, "Mở Sync Settings", Color.FromArgb(219, 39, 119), async () => await ActionRunAdbOnSelectedAsync("Open Sync", "shell am start -a android.settings.SYNC_SETTINGS"));
            ReflowCardCompact(cardSetup, Color.FromArgb(16, 185, 129), flowDash);

            // --- CARD 2: DỌN DẸP (Tông Cam/Vàng & Đỏ) ---
            var cardApps = CreateGroupbox("Dọn dẹp & Ứng dụng");

            // ... (Các nút Clear Chrome, Store giữ nguyên) ...
            AddBtn(cardApps, "Clear Chrome", Color.FromArgb(245, 158, 11), async () => await ActionRunAdbOnSelectedAsync("Clear Chrome", "shell pm clear com.android.chrome"));
            AddBtn(cardApps, "Clear Play Store", Color.FromArgb(249, 115, 22), async () => await ActionRunAdbOnSelectedAsync("Clear Store", "shell pm clear com.android.vending"));

            // ▼▼▼ SỬA LẠI ĐOẠN NÀY ▼▼▼
            AddBtn(cardApps, "Clear Data Google", Color.FromArgb(220, 38, 38), async () => {
                // Thêm 'await' để code chờ lệnh này chạy xong
                await ActionRunAdbOnSelectedAsync("Clear GMS", "shell pm clear com.google.android.gms");
                // Rồi mới chạy tiếp lệnh này
                await ActionRunAdbOnSelectedAsync("Clear Store", "shell pm clear com.android.vending");
            });
            // ▲▲▲▲▲▲▲▲▲▲▲▲▲▲▲▲▲▲▲▲▲▲▲▲

            AddBtn(cardApps, "Đóng User Apps", Color.FromArgb(100, 116, 139), async () => await ActionKillAllUserApps());

            // --- KHU VỰC CHATGPT ---

            // 1. Mở ChatGPT (Mới thêm) - Màu Xanh Lá
            AddBtn(cardApps, "Mở ChatGPT", Color.FromArgb(34, 197, 94), async () =>
                await ActionRunAdbOnSelectedAsync("Open ChatGPT", "shell monkey -p com.openai.chatgpt -c android.intent.category.LAUNCHER 1"));

            // 2. Đóng ChatGPT - Màu Xanh Cổ Vịt
            AddBtn(cardApps, "Đóng ChatGPT", Color.FromArgb(20, 184, 166), async () =>
                await ActionRunAdbOnSelectedAsync("Stop ChatGPT", "shell am force-stop com.openai.chatgpt"));

            // 3. Xóa Data - Màu Xanh Cyan
            AddBtn(cardApps, "Xóa Data ChatGPT", Color.FromArgb(30, 144, 255), async () =>
                await ActionRunAdbOnSelectedAsync("Clear ChatGPT", "shell pm clear com.openai.chatgpt"));

            // Reflow lại giao diện
            ReflowCardCompact(cardApps, Color.FromArgb(249, 115, 22), flowDash);

            var cardSystem = CreateGroupbox("Hệ thống & ADB");
            AddBtn(cardSystem, "Set Automation IME", Color.FromArgb(168, 85, 247), () => {
                var targets = GetTargetDevices();
                if (targets.Count == 0) { SetStatus("No devices selected"); return; }
                ForceSetAutomationIme(targets);
                SetStatus($"Set IME OK: {targets.Count} device(s)");
                MessageBox.Show("Đã set IME cho các máy đã chọn.");
            });
            AddBtn(cardSystem, "Reboot System", Color.FromArgb(100, 116, 139), async () => await ActionRunAdbOnSelectedAsync("Reboot", "reboot"));
            AddBtn(cardSystem, "Reboot Recovery", Color.FromArgb(124, 58, 237), async () => await ActionRunAdbOnSelectedAsync("Recovery", "reboot recovery"));
            AddBtn(cardSystem, "⚡ Restart ADB Server", Color.FromArgb(71, 85, 105), async () => await ActionRestartAdb());
            AddBtn(cardSystem, "WIPE ALL DEVICE", Color.FromArgb(220, 38, 38), () => {
                if (Confirm("CẢNH BÁO: WIPE SẠCH DỮ LIỆU TẤT CẢ MÁY?")) RunBat(@"bat\ResetMayAll.bat");
            });
            ReflowCardCompact(cardSystem, Color.FromArgb(124, 58, 237), flowDash);

            tabDash.Controls.Add(flowDash);
            tabControl.TabPages.Add(tabDash);

            // TAB 2: AUTO LOGIN & 2FA
            var tabLogin = new TabPage { Text = "Auto Login & 2FA", BackColor = Color.White };
            BuildAutoLoginTab(tabLogin);
            tabControl.TabPages.Add(tabLogin);

            // TAB 3: CHUYỂN ĐỔI
            var tabConvert = new TabPage { Text = "Công cụ Text", BackColor = Color.White };
            BuildConvertTab(tabConvert);
            tabControl.TabPages.Add(tabConvert);

            pnlContent.Controls.Add(tabControl);

            pnlContent.Controls.Add(tabControl);

            // =========================================================
            // CHỐT HẠ: CODE GỌN NHẤT - KHÔNG CẦN CONTAINER TRUNG GIAN
            // =========================================================

            // Chỉ cần thêm thẳng giao diện chính vào Form
            this.Controls.Add(splitMain);

            // Đảm bảo nó luôn nằm trên cùng (dù thực ra giờ chỉ còn một mình nó)
            splitMain.BringToFront();
        }

        // ==========================================
        // 3. LOGIC SETUP ANDROID
        // ==========================================
        private async Task ActionSetupAll()
        {
            var targets = GetAdbDeviceIds();
            if (targets.Count == 0) { MessageBox.Show("Không tìm thấy thiết bị nào!"); return; }
            if (!Confirm($"Bắt đầu Setup {targets.Count} máy (Batch 4)?")) return;

            SetStatus("Starting Setup All...");
            StartProgress();

            await Task.Run(() => Parallel.ForEach(targets, new ParallelOptions { MaxDegreeOfParallelism = 4 }, id =>
            {
                if (Directory.Exists(ApkDir))
                {
                    foreach (var f in Directory.GetFiles(ApkDir, "*.apk"))
                        RunProcessWaitNoCapture(AdbPath, new[] { "-s", id, "install-multiple", "-i", "com.android.vending", f });
                    foreach (var folder in Directory.GetDirectories(ApkDir, "*_apkm"))
                        InstallSplitApksFromFolder(id, folder);
                }

                string cmdConfig = "settings put global device_provisioned 1; " +
                                   "settings put secure --user 0 user_setup_complete 1; " +
                                   "settings put global development_settings_enabled 1; " +
                                   "settings put global stay_on_while_plugged_in 3; " +
                                   "settings put secure ui_night_mode 1";
                RunAdbWaitNoCapture(id, "shell", cmdConfig);

                SetDpiTo411dp(id);

                SetStatus($"Rebooting {id}...");
                RunAdbWaitNoCapture(id, "reboot");
            }));

            StopProgress();
            SetStatus("Setup Commands Sent!");
            MessageBox.Show($"Đã gửi lệnh Setup + Reboot cho {targets.Count} thiết bị.");
            RefreshDeviceList();
        }

        private void InstallSplitApksFromFolder(string deviceId, string folderPath)
        {
            try
            {
                if (!File.Exists(Path.Combine(folderPath, "base.apk"))) return;
                var apkFiles = Directory.GetFiles(folderPath, "*.apk");
                if (apkFiles.Length == 0) return;
                var args = new List<string> { "-s", deviceId, "install-multiple", "-i", "com.android.vending" };
                args.AddRange(apkFiles);
                RunProcessWaitNoCapture(AdbPath, args);
            }
            catch { }
        }

        private void SetDpiTo411dp(string deviceId)
        {
            try
            {
                string output = RunProcessReturnOutput(AdbPath, $"-s {deviceId} shell wm size");
                var match = Regex.Match(output, @"Physical size:\s*(\d+)x(\d+)");
                if (match.Success)
                {
                    int w = int.Parse(match.Groups[1].Value);
                    int h = int.Parse(match.Groups[2].Value);
                    int minPx = Math.Min(w, h);
                    int targetDpi = (int)Math.Round(minPx * 160.0 / 411.0);
                    RunAdbWaitNoCapture(deviceId, "shell", "wm", "density", targetDpi.ToString());
                }
            }
            catch { }
        }

        // ==========================================
        // 4. AUTO REFRESH
        // ==========================================
        private void DeviceWatcher_Tick(object? sender, EventArgs e)
        {
            Task.Run(() => {
                var currentIds = GetAdbDeviceIds();
                currentIds.Sort();
                string currentHash = string.Join(",", currentIds);
                if (currentHash != _lastDeviceHash)
                {
                    _lastDeviceHash = currentHash;
                    Invoke(new Action(() => {
                        RefreshDeviceList();
                        SetStatus($"Tìm Thấy: {currentIds.Count} Tổng.");
                    }));
                }
            });
        }

        // ==========================================
        // 5. AUTO LOGIN (FIX LAYOUT + BUTTON + HIGHLIGHT)
        // ==========================================
        private void BuildAutoLoginTab(TabPage page)
        {
            var splitMain = new SplitContainer
            {
                Dock = DockStyle.Fill,
                Orientation = Orientation.Vertical,
                SplitterWidth = 5,
                BackColor = Color.FromArgb(241, 245, 249)
            };
            splitMain.HandleCreated += (s, e) => {
                if (splitMain.Width > 0) splitMain.SplitterDistance = (int)(splitMain.Width * 0.6);
            };

            var pnlLeftContainer = new Panel { Dock = DockStyle.Fill, Padding = new Padding(0) };
            var splitLeft = new SplitContainer
            {
                Dock = DockStyle.Fill,
                Orientation = Orientation.Horizontal,
                SplitterDistance = 400,
                BackColor = Color.FromArgb(229, 231, 235)
            };

            // 1. STORAGE
            var pnlStorage = new Panel { Dock = DockStyle.Fill, BackColor = Color.White, Padding = new Padding(5) };
            _tabStorage = new Guna2TabControl
            {
                Dock = DockStyle.Fill,
                ItemSize = new Size(100, 30),
                // ▼▼▼ YÊU CẦU 1: CHUYỂN TAB XUỐNG DƯỚI CÙNG ▼▼▼
                Alignment = TabAlignment.Bottom
            };

            var tabNewAcc = new TabPage { Text = "Kho", BackColor = Color.White };
            _gridStorage = CreateEditableAccountGrid(true);

            // --- ACTION BAR (PHIÊN BẢN COMPACT & ĐÃ FIX LỖI) ---
            var pnlStoreAction = new FlowLayoutPanel
            {
                Dock = DockStyle.Bottom,
                AutoSize = true,
                AutoSizeMode = AutoSizeMode.GrowAndShrink,
                FlowDirection = FlowDirection.LeftToRight,
                Padding = new Padding(2), // Padding nhỏ để các nút không dính sát viền
                WrapContents = false      // QUAN TRỌNG: Chống xuống dòng lung tung
            };

            // --- CẤU HÌNH STYLE NHỎ GỌN ---
            int compactH = 28; // Chiều cao giữ nguyên hoặc giảm xuống 26 nếu muốn bé hơn nữa
            var iconFont = new Font("Segoe UI", 11.5f, FontStyle.Bold); // Font icon vừa vặn
            var compactMargin = new Padding(1); // Khoảng cách giữa các nút siêu nhỏ (1px)

            // 1. Nút Nạp (Refresh) - Thu nhỏ tối đa
            var btnLoadAcc = new Guna2Button
            {
                Text = "🔄",
                Height = compactH,
                Width = 38, // Giảm từ 50 -> 38 (vừa khít icon)
                FillColor = Color.DimGray,
                Font = iconFont,
                Margin = compactMargin,
                TextOffset = new Point(0, -1) // Căn chỉnh icon cho giữa
            };
            btnLoadAcc.Click += (s, e) => LoadAccountsToGrid();

            // 2. Nút Paste (Thêm) - Thu nhỏ
            var btnImportClipboard = new Guna2Button
            {
                Text = "➕",
                Height = compactH,
                Width = 38, // Giảm từ 60 -> 38
                FillColor = Color.SeaGreen,
                Font = iconFont,
                Margin = compactMargin,
                TextOffset = new Point(0, -1)
            };
            btnImportClipboard.Click += (s, e) => ActionPasteImportToStorage();

            // 3. Label đếm - TO, RÕ RÀNG, DỄ NHÌN
            _lblStorageCount = new Label
            {
                Text = "0",
                AutoSize = true,
                // Tăng font lên 10.5 hoặc 11, dùng màu Xanh Đậm hoặc Đỏ Đậm để nổi bật trên nền trắng
                Font = new Font("Segoe UI", 10.5f, FontStyle.Bold),
                ForeColor = Color.Teal, // Màu xanh cổ vịt đậm (hoặc dùng Color.Red nếu thích)
                TextAlign = ContentAlignment.MiddleCenter,
                // Chỉnh Padding Top = 5 để căn giữa theo chiều dọc với các nút
                Padding = new Padding(5, 5, 5, 0),
                Margin = new Padding(0)
            };

            // 4. Nút Send (Mũi tên) - Thu gọn
            var btnPushToInput = new Guna2Button
            {
                Text = "⏬",
                Height = compactH,
                Width = 45, // Vừa đủ cho ngón tay bấm hoặc click chuột
                FillColor = Color.FromArgb(14, 165, 233),
                // Icon này để to một chút (13f) nhìn cho sướng mắt
                Font = new Font("Segoe UI", 13f, FontStyle.Bold),
                Margin = compactMargin,
                TextOffset = new Point(0, -2) // Đẩy icon lên trên 1 chút
            };
            // Đừng quên dòng này
            btnPushToInput.Click += (s, e) => TransferAccountsToInput();

            // Thêm vào Panel (Thứ tự: Nạp -> Paste -> Label -> Send)
            pnlStoreAction.Controls.Add(btnLoadAcc);
            pnlStoreAction.Controls.Add(btnImportClipboard);
            pnlStoreAction.Controls.Add(_lblStorageCount);
            pnlStoreAction.Controls.Add(btnPushToInput);

            // Add Panel vào Tab
            tabNewAcc.Controls.Add(_gridStorage);
            tabNewAcc.Controls.Add(pnlStoreAction);

            var tabUsedAcc = new TabPage { Text = "Đã dùng", BackColor = Color.WhiteSmoke };
            _gridUsed = CreateEditableAccountGrid(false);
            tabUsedAcc.Controls.Add(_gridUsed);

            _tabStorage.TabPages.Add(tabNewAcc);
            _tabStorage.TabPages.Add(tabUsedAcc);
            pnlStorage.Controls.Add(_tabStorage);

            // =========================================================
            // 2. INPUT AREA (ĐÃ CĂN CHỈNH: THẲNG HÀNG & CÂN ĐỐI)
            // =========================================================

            var pnlInput = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                BackColor = Color.White,
                // Padding: Trên/Trái/Phải = 3px, Dưới = 0px (Để nút sát đáy, không bị hở)
                Padding = new Padding(3, 3, 3, 0),
                RowCount = 3,
                ColumnCount = 1
            };

            // Dòng 1: Tiêu đề (AutoSize)
            pnlInput.RowStyles.Add(new RowStyle(SizeType.AutoSize));

            // Dòng 2: Textbox (100% không gian còn lại)
            pnlInput.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));

            // Dòng 3: Nút bấm (Cao 32px - Chuẩn đẹp, không quá bé, không quá to)
            pnlInput.RowStyles.Add(new RowStyle(SizeType.Absolute, 32F));

            // --- 1. Tiêu đề ---
            var lblInputTitle = new Label
            {
                Text = "Input Area",
                Dock = DockStyle.Fill,
                Font = new Font("Segoe UI", 8.25f, FontStyle.Bold),
                ForeColor = Color.DimGray,
                TextAlign = ContentAlignment.BottomLeft,
                Margin = new Padding(2, 0, 0, 2)
            };

            // --- 2. Textbox ---
            _txtAccountInput = new TextBox
            {
                Multiline = true,
                ScrollBars = ScrollBars.Vertical,
                Dock = DockStyle.Fill,
                Font = new Font("Consolas", 9.5f),
                PlaceholderText = "Nhập list mail|pass|2fa..."
            };
            // Mỗi khi nội dung thay đổi, reset timer (đợi 500ms mới chạy)
            _txtAccountInput.TextChanged += (s, e) => { _inputScanTimer.Stop(); _inputScanTimer.Start(); };

            // --- 3. Panel Nút bấm ---
            var pnlInputBtns = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                Margin = new Padding(0), // Xóa sạch Margin để không bị thụt
                Padding = new Padding(0),
                ColumnCount = 2,
                RowCount = 1
            };
            pnlInputBtns.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50F));
            pnlInputBtns.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50F));

            var btnRunEmail = new Guna2Button
            {
                Text = "Email",
                Dock = DockStyle.Fill,
                FillColor = Color.Teal,
                Margin = new Padding(0, 1, 1, 0), // Cách trên 1px, Phải 1px
                Font = new Font("Segoe UI", 8.25f, FontStyle.Bold),
                BorderRadius = 1
            };
            btnRunEmail.Click += async (s, e) => await ActionSendEmailAsync();

            var btnRunPass = new Guna2Button
            {
                Text = "Pass",
                Dock = DockStyle.Fill,
                FillColor = Color.OrangeRed,
                Margin = new Padding(1, 1, 0, 0), // Cách trên 1px, Trái 1px
                Font = new Font("Segoe UI", 8.25f, FontStyle.Bold),
                BorderRadius = 1
            };
            btnRunPass.Click += async (s, e) => await ActionSendPassAsync();

            pnlInputBtns.Controls.Add(btnRunEmail, 0, 0);
            pnlInputBtns.Controls.Add(btnRunPass, 1, 0);

            // Add vào bảng chính
            pnlInput.Controls.Add(lblInputTitle, 0, 0);
            pnlInput.Controls.Add(_txtAccountInput, 0, 1);
            pnlInput.Controls.Add(pnlInputBtns, 0, 2);

            // Kết nối vào giao diện
            splitLeft.Panel1.Controls.Add(pnlStorage);
            splitLeft.Panel2.Controls.Add(pnlInput);
            pnlLeftContainer.Controls.Add(splitLeft);
            splitMain.Panel1.Controls.Add(pnlLeftContainer);

            // =========================================================
            // 3. RIGHT (2FA MANAGEMENT) - FULL CODE FIX
            // =========================================================

            // 1. Khai báo pnlRight (Đây là dòng bạn đang thiếu)
            var pnlRight = new Panel
            {
                Dock = DockStyle.Fill,
                Padding = new Padding(5), // Padding nhỏ gọn 5px
                BackColor = Color.White
            };

            // 2. Ô nhập Secret (Dock Top)
            _txtSecretInput = new TextBox
            {
                Multiline = true,
                ScrollBars = ScrollBars.Vertical,
                Dock = DockStyle.Top,
                Height = 80, // Giảm chiều cao chút cho gọn
                Font = new Font("Consolas", 9f),
                PlaceholderText = "Nhập danh sách Secret Key (Mỗi dòng 1 key)..."
            };
            _txtSecretInput.TextChanged += (s, e) => ParseSecretsToGrid();

            // 3. Khởi tạo Bảng 2FA (Đã sửa lỗi hiển thị cột)
            _grid2Fa = new Guna2DataGridView();
            _grid2Fa.Dock = DockStyle.Fill;
            _grid2Fa.BackgroundColor = Color.White;
            _grid2Fa.AllowUserToAddRows = false;
            _grid2Fa.RowHeadersVisible = false;
            _grid2Fa.ReadOnly = true;
            _grid2Fa.Theme = Guna.UI2.WinForms.Enums.DataGridViewPresetThemes.Default;
            _grid2Fa.ColumnHeadersHeight = 35;
            _grid2Fa.RowTemplate.Height = 30;

            // QUAN TRỌNG: Tắt tự động giãn cột để chỉnh tay
            _grid2Fa.AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.None;

            // --- 1. Cột Số thứ tự (#) - SIÊU NHỎ (25px) ---
            var colIdx = new DataGridViewTextBoxColumn
            {
                HeaderText = "#",
                Name = "idx",
                Width = 13, // <--- Cực nhỏ, chỉ vừa đủ hiện số
                AutoSizeMode = DataGridViewAutoSizeColumnMode.None
            };
            // Căn giữa số thứ tự và tiêu đề để nhìn cân đối trong không gian hẹp
            colIdx.DefaultCellStyle.Alignment = DataGridViewContentAlignment.MiddleCenter;
            colIdx.HeaderCell.Style.Alignment = DataGridViewContentAlignment.MiddleCenter;

            // --- 2. Cột Mã 2FA (CODE) - CHIẾM TOÀN BỘ KHÔNG GIAN CÒN LẠI ---
            var colCode = new DataGridViewTextBoxColumn
            {
                HeaderText = "CODE",
                Name = "code",
                AutoSizeMode = DataGridViewAutoSizeColumnMode.Fill // <--- Quan trọng: Tự động phình to ra
            };
            colCode.DefaultCellStyle.Font = new Font("Segoe UI", 10f, FontStyle.Bold);
            colCode.DefaultCellStyle.ForeColor = Color.Teal;
            // Căn lề trái + Padding một chút để mã không dính sát vào cột số thứ tự
            colCode.DefaultCellStyle.Padding = new Padding(5, 0, 0, 0);

            // --- 3. Cột Nút Copy - Cố định (Vừa đủ chữ Copy) ---
            var colBtnCopy = new DataGridViewButtonColumn
            {
                Text = "Copy",
                UseColumnTextForButtonValue = true,
                HeaderText = "",
                Width = 50, // 50px là đủ cho chữ Copy
                AutoSizeMode = DataGridViewAutoSizeColumnMode.None
            };

            // --- 4. Cột Nút Gửi - Cố định (Vừa đủ chữ Gửi) ---
            var colBtnSend = new DataGridViewButtonColumn
            {
                Text = "Gửi >",
                UseColumnTextForButtonValue = true,
                HeaderText = "",
                Width = 50,
                AutoSizeMode = DataGridViewAutoSizeColumnMode.None
            };

            // Thêm vào bảng
            _grid2Fa.Columns.Add(colIdx);
            _grid2Fa.Columns.Add(colCode);
            _grid2Fa.Columns.Add(colBtnCopy);
            _grid2Fa.Columns.Add(colBtnSend);
            _grid2Fa.CellContentClick += Grid2Fa_CellContentClick;

            // 4. Nút Gửi All (Dock Bottom)
            var btnSendAll2Fa = new Guna2Button
            {
                Text = "Gửi 2FA All",
                Height = 35,
                Dock = DockStyle.Bottom,
                FillColor = Color.FromArgb(124, 58, 237),
                Font = new Font("Segoe UI", 9f, FontStyle.Bold)
            };
            btnSendAll2Fa.Click += async (s, e) => await ActionSend2FaToAll();

            // 5. Tiêu đề (Dock Top)
            var lblTitleRight = new Label { Text = "Quản lý 2FA", Dock = DockStyle.Top, Height = 25, Font = new Font("Segoe UI", 10f, FontStyle.Bold) };

            // 6. SẮP XẾP VÀO PANEL (Thứ tự Add quan trọng cho Docking)

            // Add Grid (Fill) trước để nó nằm nền
            pnlRight.Controls.Add(_grid2Fa);

            // Add Spacer (Khoảng trắng đệm giữa Grid và Textbox)
            pnlRight.Controls.Add(new Panel { Height = 5, Dock = DockStyle.Top });

            // Add Textbox Input (Dock Top -> Nằm trên Grid)
            pnlRight.Controls.Add(_txtSecretInput);

            // Add Title (Dock Top -> Nằm trên cùng)
            pnlRight.Controls.Add(lblTitleRight);

            // Add Button (Dock Bottom -> Nằm dưới cùng)
            pnlRight.Controls.Add(btnSendAll2Fa);

            // 7. Thêm Grid vào Grid
            // Đưa các thành phần Dock Fill lên trên để hiển thị đúng
            _grid2Fa.BringToFront();
            btnSendAll2Fa.SendToBack(); // Đẩy nút xuống dưới

            // 8. Add vào giao diện chính
            splitMain.Panel2.Controls.Add(pnlRight);
            page.Controls.Add(splitMain);

            // Load dữ liệu
            LoadAccountsToGrid();
            LoadUsedAccountsToGrid();
        }

        private Guna2DataGridView CreateEditableAccountGrid(bool isEditable)
        {
            var g = new Guna2DataGridView
            {
                Dock = DockStyle.Fill,
                BackgroundColor = Color.White,
                AllowUserToAddRows = false,
                RowHeadersVisible = false,
                ReadOnly = !isEditable,
                AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill,
                Theme = Guna.UI2.WinForms.Enums.DataGridViewPresetThemes.Light,
                ColumnHeadersHeight = 30,
                RowTemplate = { Height = 25 },
                SelectionMode = DataGridViewSelectionMode.FullRowSelect,
                MultiSelect = true
            };

            var colEmail = new DataGridViewTextBoxColumn { HeaderText = "Email", Name = "email", SortMode = DataGridViewColumnSortMode.NotSortable };
            var colPass = new DataGridViewTextBoxColumn { HeaderText = "Pass", Name = "pass", SortMode = DataGridViewColumnSortMode.NotSortable };

            g.Columns.Add(colEmail);
            g.Columns.Add(colPass);
            g.Columns.Add(new DataGridViewButtonColumn { Text = "Email", UseColumnTextForButtonValue = true, Width = 25, SortMode = DataGridViewColumnSortMode.NotSortable, ToolTipText = "Copy Email" });
            g.Columns.Add(new DataGridViewButtonColumn { Text = "Pass", UseColumnTextForButtonValue = true, Width = 25, SortMode = DataGridViewColumnSortMode.NotSortable, ToolTipText = "Copy Pass" });

            if (isEditable)
                g.Columns.Add(new DataGridViewButtonColumn { Text = "➔", UseColumnTextForButtonValue = true, Width = 20, HeaderText = "Xoá", SortMode = DataGridViewColumnSortMode.NotSortable, ToolTipText = "Move to Used" });

            g.CellContentClick += (s, e) => {
                if (e.RowIndex < 0) return;

                // ▼▼▼ YÊU CẦU 3: SỬA LỖI TÔ MÀU (THÊM SELECTION BACKCOLOR) ▼▼▼
                if (e.ColumnIndex == 2)
                {
                    Clipboard.SetText(g.Rows[e.RowIndex].Cells[0].Value?.ToString() ?? "");
                    var cell = g.Rows[e.RowIndex].Cells[0];
                    cell.Style.BackColor = Color.FromArgb(220, 252, 231);
                    cell.Style.SelectionBackColor = Color.FromArgb(220, 252, 231); // Đổi cả màu khi đang chọn
                    SetStatus("Copied Email");
                }
                else if (e.ColumnIndex == 3)
                {
                    Clipboard.SetText(g.Rows[e.RowIndex].Cells[1].Value?.ToString() ?? "");
                    var cell = g.Rows[e.RowIndex].Cells[1];
                    cell.Style.BackColor = Color.FromArgb(220, 252, 231);
                    cell.Style.SelectionBackColor = Color.FromArgb(220, 252, 231); // Đổi cả màu khi đang chọn
                    SetStatus("Copied Pass");
                }
                else if (isEditable && e.ColumnIndex == 4) MoveSingleRowToUsed(e.RowIndex);
            };

            if (isEditable) g.CellEndEdit += (s, e) => SaveStorageFile();
            return g;
        }

        private void ActionPasteImportToStorage()
        {
            string clip = "";
            try { clip = Clipboard.GetText(); } catch { }
            if (string.IsNullOrWhiteSpace(clip)) { MessageBox.Show("Clipboard trống!"); return; }

            var lines = clip.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
            int added = 0;
            foreach (var line in lines)
            {
                if (string.IsNullOrWhiteSpace(line)) continue;
                var p = ParseLine(line);
                if (!string.IsNullOrEmpty(p.email))
                {
                    _gridStorage.Rows.Add(p.email, p.pass);
                    added++;
                }
            }

            if (added > 0)
            {
                SaveStorageFile();
                UpdateStorageCount();
                MessageBox.Show($"Đã thêm {added} tài khoản vào kho!");
            }
        }

        private void MoveSingleRowToUsed(int rowIndex)
        {
            try
            {
                // 1. Lấy dữ liệu
                var row = _gridStorage.Rows[rowIndex];
                string e = row.Cells[0].Value?.ToString() ?? "";
                string p = row.Cells[1].Value?.ToString() ?? "";

                // 2. Thêm vào bảng "Đã dùng" (Thao tác RAM, nhanh)
                _gridUsed.Rows.Add(e, p, "Cp User", "Cp Pass");

                // 3. Ghi nối vào file Used (Thao tác Append, rất nhanh)
                try
                {
                    File.AppendAllText(UsedAccountFile, $"{e}|{p}{Environment.NewLine}");
                }
                catch { }

                // 4. Xóa dòng khỏi Grid (Thao tác UI)
                _gridStorage.Rows.RemoveAt(rowIndex);

                // 5. Cập nhật số lượng
                UpdateStorageCount();

                // 6. QUAN TRỌNG: Lưu file chạy ngầm (Không bao giờ lag nữa)
                // Thay thế SaveStorageFile() bằng SaveStorageFileAsync()
                SaveStorageFileAsync();

                SetStatus("Đã chuyển 1 dòng sang Đã dùng.");
            }
            catch { }
        }

        private void LoadAccountsToGrid()
        {
            _gridStorage.Rows.Clear();
            if (!File.Exists(AccountFile)) return;
            var lines = File.ReadAllLines(AccountFile);
            foreach (var line in lines)
            {
                if (string.IsNullOrWhiteSpace(line)) continue;
                var p = ParseLine(line);
                if (!string.IsNullOrEmpty(p.email)) _gridStorage.Rows.Add(p.email, p.pass);
            }
            UpdateStorageCount();
        }

        private void LoadUsedAccountsToGrid()
        {
            _gridUsed.Rows.Clear();
            if (!File.Exists(UsedAccountFile)) return;
            var lines = File.ReadAllLines(UsedAccountFile);
            foreach (var line in lines)
            {
                if (string.IsNullOrWhiteSpace(line)) continue;
                var p = ParseLine(line);
                if (!string.IsNullOrEmpty(p.email)) _gridUsed.Rows.Add(p.email, p.pass);
            }
        }

        private void SaveStorageFile()
        {
            var lines = new List<string>();
            foreach (DataGridViewRow row in _gridStorage.Rows)
            {
                string e = row.Cells[0].Value?.ToString() ?? "";
                string p = row.Cells[1].Value?.ToString() ?? "";
                if (!string.IsNullOrWhiteSpace(e)) lines.Add($"{e}|{p}");
            }
            try { File.WriteAllText(AccountFile, string.Join(Environment.NewLine, lines)); } catch { }
        }

        // --- CẬP NHẬT BỘ ĐẾM VÀ TITLE ---
        private void UpdateStorageCount()
        {
            int count = _gridStorage.Rows.Count;
            if (_lblStorageCount != null) _lblStorageCount.Text = $"Còn: {count}";
            if (_tabStorage != null && _tabStorage.TabPages.Count > 0)
                _tabStorage.TabPages[0].Text = $"Kho ({count})";
        }

        private void TransferAccountsToInput()
        {
            // 1. Kiểm tra: Nếu kho rỗng thì thoát
            if (_gridStorage.Rows.Count == 0) return;

            // --- BẮT ĐẦU TỐI ƯU UI (Quan trọng) ---
            // Tạm dừng vẽ giao diện để thao tác nhanh hơn
            _gridStorage.SuspendLayout();
            _txtAccountInput.SuspendLayout();

            // 2. Lấy dữ liệu dòng đầu tiên
            var row = _gridStorage.Rows[0];
            string e = row.Cells[0].Value?.ToString() ?? "";
            string p = row.Cells[1].Value?.ToString() ?? "";
            string accLine = $"{e}|{p}";

            // 3. Đẩy sang ô Input
            // Nếu ô input đang có chữ thì xuống dòng
            if (_txtAccountInput.TextLength > 0)
            {
                _txtAccountInput.AppendText(Environment.NewLine + accLine);
            }
            else
            {
                _txtAccountInput.AppendText(accLine);
            }

            // Cuộn xuống cuối để thấy dòng mới thêm
            _txtAccountInput.SelectionStart = _txtAccountInput.TextLength;
            _txtAccountInput.ScrollToCaret();

            // 4. Lưu vào tab "Đã dùng" & Ghi file Used
            // (Thao tác này nhanh nên có thể để đây)
            _gridUsed.Rows.Add(e, p, "Cp User", "Cp Pass");
            try { File.AppendAllText(UsedAccountFile, accLine + Environment.NewLine); } catch { }

            // 5. Xóa khỏi kho (Đoạn bạn hỏi)
            _gridStorage.Rows.RemoveAt(0); // Xóa dòng đầu tiên (Index 0)

            // 6. Cập nhật số lượng (Đoạn bạn hỏi)
            // Cập nhật trực tiếp Text, không cần gọi hàm UpdateStorageCount phức tạp
            _lblStorageCount.Text = _gridStorage.Rows.Count.ToString();

            // --- KẾT THÚC TỐI ƯU UI ---
            // 7. Cho phép vẽ lại giao diện (Đoạn bạn hỏi)
            _txtAccountInput.ResumeLayout();
            _gridStorage.ResumeLayout();

            // 8. Lưu file chạy ngầm (Bắt buộc để không bị delay)
            SaveStorageFileAsync();
        }

        // ==========================================
        // 6. UTILITIES (ADB, UI, ETC)
        // ==========================================
        #region Utilities
        private async Task ActionKillAllUserApps()
        {
            var targets = GetTargetDevices();
            if (targets.Count == 0) { MessageBox.Show("Chưa chọn thiết bị!"); return; }
            SetStatus("Đang quét và tắt ứng dụng (user apps)..."); StartProgress();
            await Task.Run(() => Parallel.ForEach(targets, deviceId =>
            {
                string output = RunProcessReturnOutput(AdbPath, $"-s {deviceId} shell pm list packages -3 --user 0");
                if (string.IsNullOrWhiteSpace(output)) output = RunProcessReturnOutput(AdbPath, $"-s {deviceId} shell pm list packages -3");
                if (!string.IsNullOrWhiteSpace(output))
                {
                    var lines = output.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
                    foreach (var line in lines)
                    {
                        var pkgLine = line.Trim();
                        if (!pkgLine.StartsWith("package:", StringComparison.OrdinalIgnoreCase)) continue;
                        string pkg = pkgLine.Substring("package:".Length).Trim();
                        if (!string.IsNullOrWhiteSpace(pkg)) RunAdbWaitNoCapture(deviceId, "shell", "am", "force-stop", pkg);
                    }
                    RunAdbWaitNoCapture(deviceId, "shell", "am", "kill-all");
                }
                else RunAdbWaitNoCapture(deviceId, "shell", "am", "kill-all");
            }));
            StopProgress(); SetStatus("Đã đóng tất cả User Apps.");
        }

        private async Task ActionRestartAdb()
        {
            SetStatus("Killing ADB Server..."); StartProgress();
            await Task.Run(() => {
                RunProcessWaitNoCapture(AdbPath, new[] { "kill-server" });
                Thread.Sleep(1500);
                RunProcessWaitNoCapture(AdbPath, new[] { "start-server" });
            });
            StopProgress(); SetStatus("ADB Server Restarted."); RefreshDeviceList();
            MessageBox.Show("ADB Server đã khởi động lại.");
        }

        private async Task ActionInstallApkmFileDialog()
        {
            var t = GetTargetDevices(); if (t.Count == 0) { MessageBox.Show("Chưa chọn thiết bị!"); return; }
            using var d = new OpenFileDialog { Filter = "APKM/ZIP|*.apkm;*.zip" };
            if (d.ShowDialog() == DialogResult.OK)
            {
                StartProgress();
                await Task.Run(() => Parallel.ForEach(t, id => InstallApkmZipLogic(id, d.FileName)));
                StopProgress(); MessageBox.Show("Cài APKM xong.");
            }
        }

        private void InstallApkmZipLogic(string id, string path)
        {
            var tmp = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
            try
            {
                Directory.CreateDirectory(tmp); ZipFile.ExtractToDirectory(path, tmp);
                var baseApk = Directory.GetFiles(tmp, "base.apk", SearchOption.AllDirectories).FirstOrDefault();
                if (baseApk != null)
                {
                    var splits = Directory.GetFiles(tmp, "*.apk", SearchOption.AllDirectories).Where(x => !x.EndsWith("base.apk"));
                    var args = new List<string> { "-s", id, "install-multiple", "-r", baseApk };
                    args.AddRange(splits);
                    RunProcessWaitNoCapture(AdbPath, args);
                }
            }
            catch { }
            finally { try { Directory.Delete(tmp, true); } catch { } }
        }

        private void RenameSelectedDevice() { var t = GetTargetDevices(); if (t.Count != 1) { MessageBox.Show("Chọn 1 máy thôi."); return; } var id = t[0]; _deviceNameById.TryGetValue(id, out var old); string inp = Microsoft.VisualBasic.Interaction.InputBox("Tên mới:", "Đổi tên", old ?? ""); if (!string.IsNullOrWhiteSpace(inp)) { _deviceNameById[id] = inp; SaveDeviceNames(); RefreshDeviceList(); } }
        private async Task ActionPushFile() { var t = GetTargetDevices(); if (t.Count == 0) { MessageBox.Show("Chưa chọn thiết bị!"); return; } using var d = new OpenFileDialog { Title = "Chọn file" }; if (d.ShowDialog() == DialogResult.OK) { StartProgress(); SetStatus($"Đang gửi file..."); await Task.Run(() => Parallel.ForEach(t, id => RunAdbWaitNoCapture(id, "push", d.FileName, "/sdcard/Download/"))); StopProgress(); MessageBox.Show("Gửi file xong."); } }
        private async Task ActionRunAdbOnSelectedAsync(string name, string cmd) { var t = GetTargetDevices(); if (t.Count == 0) { MessageBox.Show("Chưa chọn thiết bị!"); return; } SetStatus($"Running {name}..."); StartProgress(); await Task.Run(() => Parallel.ForEach(t, id => RunAdbNoWait($"-s {id} {cmd}"))); StopProgress(); SetStatus($"Done {name}"); }

        private void RefreshDeviceList()
        {
            LoadDeviceNames();
            var currentChecked = GetTargetDevices();

            _clbSidebarDevices.Items.Clear();

            // 1. Lấy danh sách ID
            var rawIds = GetAdbDeviceIds();

            // 2. Tạo danh sách tạm chứa ID và Tên hiển thị
            // Dùng NaturalComparer để so sánh trực tiếp tên hiển thị
            var items = rawIds.Select(id =>
            {
                _deviceNameById.TryGetValue(id, out var name);

                // Nếu có tên thì hiển thị tên, nếu không thì hiển thị ID
                // Logic hiển thị: "Tên (ID)" hoặc "ID"
                string displayLabel = string.IsNullOrEmpty(name) ? id : $"{name} ({id})";

                // Trả về object chứa thông tin cần thiết
                return new { Id = id, Display = displayLabel };
            }).ToList();

            // 3. Sắp xếp danh sách bằng NaturalComparer
            // Lúc này "Máy 2" sẽ được hiểu là nhỏ hơn "Máy 10"
            var sorter = new NaturalComparer();
            var sortedItems = items.OrderBy(x => x.Display, sorter).ToList();

            // 4. Đưa lên giao diện
            foreach (var item in sortedItems)
            {
                _clbSidebarDevices.Items.Add(item.Display, currentChecked.Contains(item.Id));
            }

            SetStatus($"Tìm thấy {_clbSidebarDevices.Items.Count} thiết bị.");
        }

        private void ToggleSidebarSelection(bool check) { for (int i = 0; i < _clbSidebarDevices.Items.Count; i++) _clbSidebarDevices.SetItemChecked(i, check); }
        private List<string> GetTargetDevices() { var list = new List<string>(); foreach (var item in _clbSidebarDevices.CheckedItems) { var s = item.ToString(); if (s.Contains("(") && s.EndsWith(")")) list.Add(s.Substring(s.LastIndexOf('(') + 1).Trim(')')); else list.Add(s); } return list; }
        private void RunBat(string bat, string args = "") { var p = Path.Combine(BaseDir, bat); if (!File.Exists(p)) { MessageBox.Show("Missing: " + p); return; } Process.Start(new ProcessStartInfo { FileName = "cmd.exe", Arguments = $"/c \"\"{p}\" {args}\"", UseShellExecute = false, CreateNoWindow = false, WorkingDirectory = BaseDir }); }

        private void ValidateAdbExists() { if (!File.Exists(AdbPath)) MessageBox.Show("Thiếu adb.exe trong thư mục tool: " + AdbPath); }
        private void TryWarmAdbServer() { try { RunAdbNoWait("start-server"); } catch { } }
        private void RunAdbNoWait(string args) => Process.Start(new ProcessStartInfo { FileName = AdbPath, Arguments = args, UseShellExecute = false, CreateNoWindow = true });
        private int RunAdbWaitNoCapture(string id, params string[] args) { var l = new List<string> { "-s", id }; l.AddRange(args); return RunProcessWaitNoCapture(AdbPath, l); }
        private string RunProcessReturnOutput(string exe, string args) { try { var p = new ProcessStartInfo { FileName = exe, Arguments = args, UseShellExecute = false, RedirectStandardOutput = true, CreateNoWindow = true, StandardOutputEncoding = System.Text.Encoding.UTF8 }; using var proc = Process.Start(p); string output = proc.StandardOutput.ReadToEnd(); proc.WaitForExit(); return output; } catch { return ""; } }
        private int RunProcessWaitNoCapture(string f, IEnumerable<string> a, string w = null) { var p = new ProcessStartInfo { FileName = f, UseShellExecute = false, CreateNoWindow = true, WorkingDirectory = w ?? BaseDir }; foreach (var x in a) p.ArgumentList.Add(x); var proc = Process.Start(p); proc.WaitForExit(); return proc.ExitCode; }
        private List<string> GetAdbDeviceIds() { try { var p = Process.Start(new ProcessStartInfo { FileName = AdbPath, Arguments = "devices", UseShellExecute = false, RedirectStandardOutput = true, CreateNoWindow = true }); var o = p.StandardOutput.ReadToEnd(); p.WaitForExit(); return o.Split('\n').Where(l => l.Contains("\tdevice")).Select(l => l.Split('\t')[0]).ToList(); } catch { return new List<string>(); } }
        private void LoadDeviceNames() { _deviceNameById.Clear(); try { if (File.Exists("devices.json")) { var d = JsonSerializer.Deserialize<Dictionary<string, string>>(File.ReadAllText("devices.json")); foreach (var k in d) _deviceNameById[k.Key] = k.Value; } } catch { } }
        private void SaveDeviceNames() { try { File.WriteAllText("devices.json", JsonSerializer.Serialize(_deviceNameById)); } catch { } }


        // --- CẬP NHẬT TRẠNG THÁI LÊN TIÊU ĐỀ CỬA SỔ ---

        private void SetStatus(string t)
        {
            // Hiện thông báo lên tiêu đề cửa sổ thay vì Label
            string title = $"ADB Pro v2.5 - [{t}]";
            if (InvokeRequired) Invoke(new Action(() => this.Text = title));
            else this.Text = title;
        }

        private void StartProgress()
        {
            // Đổi con trỏ chuột thành hình xoay xoay (Loading)
            if (InvokeRequired) Invoke(new Action(StartProgress));
            else this.Cursor = Cursors.WaitCursor;
        }

        private void StopProgress()
        {
            // Trả lại con trỏ chuột bình thường
            if (InvokeRequired) Invoke(new Action(StopProgress));
            else this.Cursor = Cursors.Default;
        }
        private bool Confirm(string m) => MessageBox.Show(m, "Xác nhận", MessageBoxButtons.YesNo, MessageBoxIcon.Question) == DialogResult.Yes;
        #endregion

        // ==========================================
        // 7. INPUT & IME & 2FA
        // ==========================================
        #region InputLogic
        private async Task ActionSendEmailAsync() => await SendText(true);
        private async Task ActionSendPassAsync() => await SendText(false);
        private async Task SendText(bool isEmail)
        {
            var t = GetTargetDevices(); var l = _txtAccountInput.Text.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
            if (t.Count == 0) { MessageBox.Show("Chưa chọn thiết bị!"); return; }
            if (l.Length == 0) { MessageBox.Show("Chưa có dữ liệu!"); return; }
            StartProgress();
            await Task.Run(() => Parallel.ForEach(t, (id, s, i) => {
                if (i >= l.Length) return;
                var p = ParseLine(l[i]); var txt = isEmail ? p.email : p.pass;
                if (string.IsNullOrWhiteSpace(txt)) return;
                EnsureAutomationIme(id); ImeClearText(id); Thread.Sleep(30); ImeSendText(id, txt.Trim());
                Thread.Sleep(80); RunAdbWaitNoCapture(id, "shell", "input", "keyevent", "66");
            }));
            StopProgress();
        }
        private (string email, string pass) ParseLine(string l)
        {
            l = l.Trim();
            var m = Regex.Match(l, @"[\w\.-]+@[\w\.-]+\.\w+");
            if (m.Success)
            {
                string email = m.Value;
                string rawRest = l.Replace(email, "").Trim();
                string pass = Regex.Replace(rawRest, @"^[:|\s]+", "");
                return (email, pass);
            }
            return (l, "");
        }

        private const string AutomationImeId = "com.cuong.automationime/.AutomationIME";
        private const string AutomationImeReceiverComponent = "com.cuong.automationime/.B64Receiver";
        private const string AutomationImeSendB64Action = "com.cuong.automationime.SEND_B64";
        private const string AutomationImeClearAction = "com.cuong.automationime.CLEAR_TEXT";
        private const string AutomationImeB64ExtraKey = "b64";
        private readonly HashSet<string> _imeEnsured = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        private readonly object _imeEnsuredLock = new object();
        private bool IsAutomationImeActive(string deviceId) { var cur = RunProcessReturnOutput(AdbPath, $"-s {deviceId} shell settings get secure default_input_method"); return (cur ?? "").Trim().Equals(AutomationImeId, StringComparison.OrdinalIgnoreCase); }
        private void EnsureAutomationIme(string deviceId)
        {
            if (string.IsNullOrWhiteSpace(deviceId)) return;
            lock (_imeEnsuredLock) { if (_imeEnsured.Contains(deviceId) && IsAutomationImeActive(deviceId)) return; }
            RunAdbWaitNoCapture(deviceId, "shell", "ime", "enable", AutomationImeId); RunAdbWaitNoCapture(deviceId, "shell", "ime", "set", AutomationImeId);
            lock (_imeEnsuredLock) _imeEnsured.Add(deviceId);
        }
        // --- 1. XỬ LÝ ADB & IME ---
        private void ForceSetAutomationIme(IEnumerable<string> deviceIds)
        {
            if (deviceIds == null) return;
            // Chuyển sang chạy song song (Parallel) để set IME nhanh hơn nếu danh sách nhiều máy
            Parallel.ForEach(deviceIds, id =>
            {
                RunAdbWaitNoCapture(id, "shell", "ime", "enable", AutomationImeId);
                RunAdbWaitNoCapture(id, "shell", "ime", "set", AutomationImeId);
                lock (_imeEnsuredLock) _imeEnsured.Add(id);
            });
        }

        private void ImeClearText(string deviceId) =>
            RunAdbWaitNoCapture(deviceId, "shell", "am", "broadcast", "-n", AutomationImeReceiverComponent, "-a", AutomationImeClearAction);

        private void ImeSendText(string deviceId, string text)
        {
            var b64 = Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes(text ?? ""));
            RunAdbWaitNoCapture(deviceId, "shell", "am", "broadcast", "-n", AutomationImeReceiverComponent, "-a", AutomationImeSendB64Action, "--es", AutomationImeB64ExtraKey, b64);
        }

        // --- 2. XỬ LÝ GRID & DATA 2FA ---
        private void ParseSecretsToGrid()
        {
            _grid2Fa.Rows.Clear();
            if (string.IsNullOrWhiteSpace(_txtSecretInput.Text)) return;

            var l = _txtSecretInput.Text.Split(new[] { '\n', '\r' }, StringSplitOptions.RemoveEmptyEntries);
            int i = 1;
            foreach (var s in l)
            {
                if (string.IsNullOrWhiteSpace(s)) continue;
                _grid2Fa.Rows.Add(i++, "Calc...", "Copy", "Gửi");
            }
            Update2FaGridCodes();
        }

        private void Update2FaGridCodes()
        {
            if (_grid2Fa == null || _grid2Fa.Rows.Count == 0) return;

            // Lấy danh sách secret từ textbox để tính toán (Tránh truy cập Cell nhiều lần)
            var lines = _txtSecretInput.Text.Split(new[] { '\n', '\r' }, StringSplitOptions.RemoveEmptyEntries);

            // Chỉ lặp qua những dòng có dữ liệu
            int limit = Math.Min(lines.Length, _grid2Fa.Rows.Count);

            for (int i = 0; i < limit; i++)
            {
                try
                {
                    // Tính toán Code
                    string code = ComputeTotp6(lines[i].Trim());
                    string time = $"({GetTotpRemainingSeconds()}s)";
                    _grid2Fa.Rows[i].Cells[1].Value = $"{code} {time}";
                }
                catch
                {
                    _grid2Fa.Rows[i].Cells[1].Value = "Error";
                }
            }
        }

        // --- 3. SỰ KIỆN CLICK (ĐÃ SỬA LỖI NULL) ---
        private void Grid2Fa_CellContentClick(object? s, DataGridViewCellEventArgs e)
        {
            if (e.RowIndex < 0) return;

            // Sửa lỗi Null Reference: Kiểm tra kỹ trước khi ToString
            var rawVal = _grid2Fa.Rows[e.RowIndex].Cells[1].Value?.ToString();
            if (string.IsNullOrEmpty(rawVal) || rawVal == "Error") return;

            // Lấy mã Code (bỏ phần giây đi)
            var code = rawVal.Split(' ')[0];

            if (e.ColumnIndex == 2) // Nút Copy
            {
                Clipboard.SetText(code);
                SetStatus($"Copied 2FA: {code}");
            }
            else if (e.ColumnIndex == 3) // Nút Gửi
            {
                // Gọi hàm async để không đơ máy
                _ = Send2FaSingleAsync(e.RowIndex, code);
            }
        }

        // --- 4. GỬI 1 MÁY (CHUYỂN SANG ASYNC ĐỂ KHÔNG LAG) ---
        private async Task Send2FaSingleAsync(int rowIndex, string code)
        {
            var devices = GetTargetDevices();
            if (rowIndex < devices.Count)
            {
                var id = devices[rowIndex];
                SetStatus($"Sending 2FA to {id}...");

                // Chạy ngầm để giao diện vẫn mượt
                await Task.Run(() =>
                {
                    EnsureAutomationIme(id);
                    ImeClearText(id);
                    Thread.Sleep(50); // Delay nhỏ để máy kịp xóa
                    ImeSendText(id, code);
                    // Có thể thêm Enter nếu cần
                    // RunAdbWaitNoCapture(id, "shell", "input", "keyevent", "66"); 
                });

                SetStatus($"Sent 2FA to {id}");
            }
            else
            {
                SetStatus("⚠️ Không đủ thiết bị (Dòng 2FA > Số lượng máy)");
            }
        }

        // --- 5. GỬI TẤT CẢ (QUAN TRỌNG: FIX LỖI CROSS-THREAD) ---
        private async Task ActionSend2FaToAll()
        {
            var targets = GetTargetDevices();
            if (targets.Count == 0) { MessageBox.Show("Chưa chọn thiết bị"); return; }

            // BƯỚC 1: Lấy dữ liệu từ Grid ra List TRƯỚC (Thao tác trên UI Thread)
            var codesToSend = new List<string>();
            for (int i = 0; i < _grid2Fa.Rows.Count; i++)
            {
                var val = _grid2Fa.Rows[i].Cells[1].Value?.ToString();
                if (!string.IsNullOrEmpty(val) && val != "Error")
                {
                    codesToSend.Add(val.Split(' ')[0]); // Chỉ lấy code, bỏ (30s)
                }
                else
                {
                    codesToSend.Add(""); // Dòng lỗi hoặc trống
                }
            }

            SetStatus("Sending 2FA to all...");
            StartProgress();

            // BƯỚC 2: Đưa List dữ liệu vào luồng chạy ngầm (An toàn tuyệt đối)
            await Task.Run(() =>
            {
                Parallel.For(0, targets.Count, i =>
                {
                    // Chỉ gửi nếu có mã tương ứng với thứ tự máy
                    if (i < codesToSend.Count && !string.IsNullOrEmpty(codesToSend[i]))
                    {
                        var id = targets[i];
                        var code = codesToSend[i];

                        EnsureAutomationIme(id);
                        ImeClearText(id);
                        Thread.Sleep(50);
                        ImeSendText(id, code);
                    }
                });
            });

            StopProgress();
            SetStatus("Sent 2FA Done.");
        }

        // --- 6. CÁC HÀM TÍNH TOÁN TOTP (GIỮ NGUYÊN) ---
        private static string ComputeTotp6(string secretKey)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(secretKey)) return "ERR";
                // Clean key: Bỏ khoảng trắng, chuyển chữ hoa
                secretKey = Regex.Replace(secretKey.Trim().ToUpperInvariant(), @"[^A-Z2-7=]", "");

                byte[] keyBytes = Base32DecodeRfc4648(secretKey);
                if (keyBytes == null || keyBytes.Length == 0) return "ERR";

                long counter = DateTimeOffset.UtcNow.ToUnixTimeSeconds() / 30;
                byte[] counterBytes = BitConverter.GetBytes(System.Net.IPAddress.HostToNetworkOrder(counter));

                using var hmac = new HMACSHA1(keyBytes);
                byte[] hash = hmac.ComputeHash(counterBytes);
                int offset = hash[hash.Length - 1] & 0x0F;
                int binary = ((hash[offset] & 0x7F) << 24) |
                             ((hash[offset + 1] & 0xFF) << 16) |
                             ((hash[offset + 2] & 0xFF) << 8) |
                             (hash[offset + 3] & 0xFF);

                return (binary % 1_000_000).ToString("D6");
            }
            catch { return "ERR"; }
        }

        private static int GetTotpRemainingSeconds() => 30 - (int)(DateTimeOffset.UtcNow.ToUnixTimeSeconds() % 30);

        private static byte[] Base32DecodeRfc4648(string input)
        {
            input = input.TrimEnd('=');
            var result = new List<byte>();
            int buffer = 0;
            int bitsLeft = 0;
            foreach (char c in input)
            {
                int val = "ABCDEFGHIJKLMNOPQRSTUVWXYZ234567".IndexOf(c);
                if (val < 0) continue;
                buffer = (buffer << 5) | val;
                bitsLeft += 5;
                if (bitsLeft >= 8)
                {
                    bitsLeft -= 8;
                    result.Add((byte)((buffer >> bitsLeft) & 0xFF));
                }
            }
            return result.ToArray();
        }

        // ==========================================
        // 8. HELPERS (UI COMPONENTS)
        // ==========================================
        #region Helpers
        private void BuildConvertTab(TabPage page)
        {
            var split = new SplitContainer { Dock = DockStyle.Fill, Orientation = Orientation.Vertical, SplitterDistance = 400, SplitterWidth = 35, BackColor = Color.FromArgb(241, 245, 249) };
            double? userRatio = null; bool userDragging = false;
            split.SplitterMoving += (s, e) => userDragging = true;
            split.SplitterMoved += (s, e) => { userDragging = false; var w = split.ClientSize.Width; if (w > 0) userRatio = split.SplitterDistance / (double)w; };
            split.SizeChanged += (s, e) => { if (userDragging) return; if (userRatio == null) return; var w = split.ClientSize.Width; if (w <= 0) return; var d = (int)(w * userRatio.Value); split.SplitterDistance = Math.Max(0, Math.Min(d, w)); };

            var pnlLeft = new Panel { Dock = DockStyle.Fill, Padding = new Padding(15), BackColor = Color.White };
            _txtConvertIn = new TextBox { Multiline = true, ScrollBars = ScrollBars.Vertical, Dock = DockStyle.Fill, Font = new Font("Consolas", 10f), PlaceholderText = "Mỗi record 3 dòng:\r\nemail\r\npassword\r\n2fa_secret\r\n..." };
            pnlLeft.Controls.Add(_txtConvertIn); pnlLeft.Controls.Add(new Label { Text = "Input (3 dòng = 1 record)", Dock = DockStyle.Top, Height = 30, Font = new Font("Segoe UI", 10f, FontStyle.Bold), TextAlign = ContentAlignment.MiddleLeft });
            split.Panel1.Controls.Add(pnlLeft);

            var pnlRight = new Panel { Dock = DockStyle.Fill, Padding = new Padding(15), BackColor = Color.White };
            _txtConvertOut = new TextBox { Multiline = true, ReadOnly = true, ScrollBars = ScrollBars.Vertical, Dock = DockStyle.Fill, Font = new Font("Consolas", 10f), BackColor = SystemColors.Window, Cursor = Cursors.Hand };
            _txtConvertOut.Click += (s, e) => { if (!string.IsNullOrWhiteSpace(_txtConvertOut.Text)) { Clipboard.SetText(_txtConvertOut.Text); SetStatus("Copied output"); } };
            var btnConvert = new Guna2Button { Text = "Chuyển đổi", Height = 36, Width = 120, FillColor = Color.FromArgb(14, 165, 233), BorderRadius = 4 };
            btnConvert.Click += (s, e) => {
                var lines = (_txtConvertIn.Text ?? "").Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries).Select(x => x.Trim()).Where(x => x.Length > 0).ToList();
                if (lines.Count == 0) { _txtConvertOut.Text = ""; return; }
                var outLines = new List<string>();
                for (int i = 0; i + 2 < lines.Count; i += 3) outLines.Add($"{lines[i]}\t{lines[i + 1]}\t{lines[i + 2]}");
                _txtConvertOut.Text = string.Join(Environment.NewLine, outLines);
                SetStatus($"Đã chuyển đổi {outLines.Count} dòng.");
            };
            var btnCopy = new Guna2Button { Text = "Copy", Height = 36, Width = 130, FillColor = Color.FromArgb(34, 197, 94), BorderRadius = 4 };
            btnCopy.Click += (s, e) => { if (!string.IsNullOrWhiteSpace(_txtConvertOut.Text)) { Clipboard.SetText(_txtConvertOut.Text); SetStatus("Copied output"); } };
            var pnlBtns = new FlowLayoutPanel { Dock = DockStyle.Top, Height = 46, FlowDirection = FlowDirection.LeftToRight, WrapContents = false };
            pnlBtns.Controls.AddRange(new Control[] { btnConvert, btnCopy });
            pnlRight.Controls.AddRange(new Control[] { _txtConvertOut, new Panel { Height = 10, Dock = DockStyle.Top }, pnlBtns, new Label { Text = "Output (TAB-separated)", Dock = DockStyle.Top, Height = 25, Font = new Font("Segoe UI", 10f, FontStyle.Bold) } });
            split.Panel2.Controls.Add(pnlRight);
            page.Controls.Add(split);
        }

        private Guna2Button CreateSideButton(string text, Color color)
        {
            return new Guna2Button
            {
                Text = text,
                Height = 35,
                BorderRadius = 4,
                FillColor = color,
                ForeColor = Color.White,
                Font = new Font("Segoe UI Semibold", 9f),
                Cursor = Cursors.Hand,
                Margin = new Padding(0, 5, 0, 5)
            };
        }

        private Guna2GroupBox CreateGroupbox(string title)
        {
            return new Guna2GroupBox
            {
                Text = title,
                Size = new Size(340, 100)
            };
        }

        private void AddBtn(Guna2GroupBox group, string text, Color color, Action onClick)
        {
            var btn = new Guna2Button
            {
                Text = text,
                FillColor = color,
                ForeColor = Color.White
            };
            btn.Click += (s, e) => onClick();
            group.Controls.Add(btn);
        }

        private void ReflowCardCompact(Guna2GroupBox card, Color themeColor, FlowLayoutPanel parent)
        {
            card.Font = new Font("Segoe UI Semibold", 9.5f); card.ForeColor = Color.Black;
            card.CustomBorderColor = Color.FromArgb(229, 231, 235); card.CustomBorderThickness = new Padding(0, 35, 0, 0);
            card.FillColor = Color.White; card.BorderColor = Color.FromArgb(229, 231, 235); card.BorderRadius = 6;
            card.Width = 340; card.Margin = new Padding(8);
            var buttons = card.Controls.OfType<Guna2Button>().ToList();
            int startY = 45, gap = 8, colWidth = (card.Width - 20 - gap) / 2;
            for (int i = 0; i < buttons.Count; i++) { var btn = buttons[i]; btn.Height = 36; btn.BorderRadius = 4; btn.Font = new Font("Segoe UI", 9f); if (btn.FillColor == Color.FromArgb(94, 148, 255)) btn.FillColor = themeColor; btn.Location = new Point(10 + (i % 2) * (colWidth + gap), startY + (i / 2) * (36 + gap)); btn.Width = colWidth; }
            card.Height = startY + (int)Math.Ceiling(buttons.Count / 2.0) * (36 + gap) + 10;
            parent.Controls.Add(card);
        }
        #endregion
        private void SaveStorageFileAsync()
        {
            // Copy dữ liệu ra list tạm (Thao tác trên RAM, cực nhanh)
            var lines = new List<string>();
            foreach (DataGridViewRow row in _gridStorage.Rows)
            {
                string e = row.Cells[0].Value?.ToString() ?? "";
                string p = row.Cells[1].Value?.ToString() ?? "";
                if (!string.IsNullOrWhiteSpace(e)) lines.Add($"{e}|{p}");
            }

            // Đẩy việc ghi đĩa xuống luồng phụ (Background Thread)
            Task.Run(() =>
            {
                lock (_saveLock) // Đảm bảo an toàn dữ liệu
                {
                    try { File.WriteAllText(AccountFile, string.Join(Environment.NewLine, lines)); } catch { }
                }
            });
        }
        private void ScanInputFor2Fa()
        {
            _inputScanTimer.Stop(); // Dừng timer

            if (string.IsNullOrWhiteSpace(_txtAccountInput.Text)) return;

            var lines = _txtAccountInput.Text.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
            var newAccountLines = new List<string>();
            var newSecretLines = new List<string>();
            bool hasChange = false;

            foreach (var line in lines)
            {
                string l = line.Trim();
                string[] parts = null;

                // Ưu tiên 1: Tách bằng dấu gạch đứng | (Chuẩn nhất)
                if (l.Contains("|"))
                {
                    parts = l.Split(new[] { '|' }, StringSplitOptions.RemoveEmptyEntries);
                }
                // Ưu tiên 2: Tách bằng Tab (Google Sheets) HOẶC Dấu cách
                // Code cũ bị lỗi vì chỉ check Contains(" ") mà quên check Tab
                else
                {
                    // Cắt bằng cả Tab (\t) và Space (' ')
                    parts = l.Split(new[] { '\t', ' ' }, StringSplitOptions.RemoveEmptyEntries);
                }

                // Logic xử lý: Nếu phát hiện đủ 3 thành phần (User, Pass, 2FA)
                if (parts != null && parts.Length >= 3)
                {
                    // Ghép User|Pass giữ lại bên này
                    newAccountLines.Add($"{parts[0].Trim()}|{parts[1].Trim()}");

                    // Lấy phần thứ 3 (2FA) đẩy sang bên kia
                    string secret = parts[2].Trim();
                    // Nếu bị cắt vụn quá (ví dụ secret có dấu cách) thì nối lại
                    if (parts.Length > 3) secret = string.Join("", parts.Skip(2));

                    newSecretLines.Add(secret);
                    hasChange = true;
                }
                else
                {
                    // Nếu không đủ 3 phần thì giữ nguyên dòng đó
                    newAccountLines.Add(l);
                }
            }

            // Chỉ cập nhật giao diện nếu CÓ sự thay đổi
            if (hasChange && newSecretLines.Count > 0)
            {
                // 1. Cập nhật lại ô Account (đã bị cắt mất 2FA)
                // Dùng SuspendLayout để tránh nháy
                _txtAccountInput.SuspendLayout();
                int oldSelection = _txtAccountInput.SelectionStart;
                _txtAccountInput.Text = string.Join(Environment.NewLine, newAccountLines);
                try { _txtAccountInput.SelectionStart = Math.Min(oldSelection, _txtAccountInput.TextLength); } catch { }
                _txtAccountInput.ResumeLayout();

                // 2. Đẩy 2FA sang ô Secret
                if (!string.IsNullOrWhiteSpace(_txtSecretInput.Text))
                    _txtSecretInput.AppendText(Environment.NewLine);

                _txtSecretInput.AppendText(string.Join(Environment.NewLine, newSecretLines));

                // Thông báo
                SetStatus($"Đã tách {newSecretLines.Count} mã 2FA từ Google Sheet/Text.");
            }
        }
    }
    public class NaturalComparer : IComparer<string>
    {
        [System.Runtime.InteropServices.DllImport("shlwapi.dll", CharSet = System.Runtime.InteropServices.CharSet.Unicode)]
        private static extern int StrCmpLogicalW(string psz1, string psz2);

        public int Compare(string? x, string? y)
        {
            return StrCmpLogicalW(x ?? "", y ?? "");
        }
    }
}
#endregion