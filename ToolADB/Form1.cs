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
        private string IniFile => Path.Combine(BaseDir, "config", "userdata.ini");
        private string AdbPath => Path.Combine(BaseDir, "adb.exe");
        private object _saveLock = new object();
        private System.Windows.Forms.Timer _inputScanTimer = null!;
        private Panel pnlContent;
        private Guna2TabControl tabControl;
        private CheckedListBox _clbSidebarDevices; // List danh sách thiết bị
        private bool _isWatcherBusy = false; // Biến này giúp Timer không chạy chồng chéo

        // UI Controls
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
        private bool _isTransferring = false;
        // Account Manager Storage
        private Guna2DataGridView _gridStorage = null!;
        private Guna2DataGridView _gridUsed = null!;
        private Guna2TabControl _tabStorage = null!;
        private Label _lblStorageCount = null!;
        // Tab Convert
        private TextBox _txtConvertIn = null!;
        private TextBox _txtConvertOut = null!;
        // Thay CheckedListBox bằng Grid cho đẹp
        private Guna2DataGridView _gridDevices;
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

            // 1. SIDEBAR (THANH BÊN TRÁI - DẠNG DỌC)
            // ==================================================
            var pnlSidebar = splitMain.Panel1;
            pnlSidebar.Controls.Clear(); // Xóa sạch cái cũ
            pnlSidebar.BackColor = Color.White;
            pnlSidebar.Padding = new Padding(10);

            var lblSideTitle = new Label { Text = "Thiết Bị", Dock = DockStyle.Top, Height = 30, Font = new Font("Segoe UI", 10f, FontStyle.Bold), ForeColor = Color.Green };

            // 1. Panel chứa nút (Tăng chiều cao lên để chứa đủ 4 dòng)
            var flowSideBtns = new FlowLayoutPanel
            {
                Dock = DockStyle.Bottom,
                Height = 180, // 4 nút x 40px + khoảng cách = ~180
                BackColor = Color.White,
                FlowDirection = FlowDirection.TopDown, // QUAN TRỌNG: Xếp từ trên xuống dưới
                WrapContents = false, // Không cho nhảy sang cột bên cạnh
                Padding = new Padding(0, 5, 0, 0),
                AutoSize = false
            };

            // 2. Tính toán kích thước nút (Lấy Full chiều rộng Sidebar)
            int btnWidth = pnlSidebar.Width - 20; // Trừ padding 2 bên ra

            // Hàm tạo nút nhanh
            Guna2Button MkSideBtn(string txt, Color c) => new Guna2Button
            {
                Text = txt,
                Width = btnWidth, // Nút rộng bằng Sidebar
                Height = 38,      // Chiều cao vừa phải
                BorderRadius = 4,
                FillColor = c,
                ForeColor = Color.White,
                Font = new Font("Segoe UI", 9f, FontStyle.Bold),
                Margin = new Padding(0, 0, 0, 5), // Cách nút dưới 5px
                Cursor = Cursors.Hand
            };

            // 3. Tạo các nút
            var btnSelectAll = MkSideBtn("Chọn Tất Cả", Color.FromArgb(59, 130, 246));
            btnSelectAll.Click += (s, e) => {
                foreach (DataGridViewRow r in _gridDevices.Rows) r.Cells[0].Value = true;
            };

            var btnSelectNone = MkSideBtn("Bỏ Chọn", Color.FromArgb(100, 116, 139));
            btnSelectNone.Click += (s, e) => {
                foreach (DataGridViewRow r in _gridDevices.Rows) r.Cells[0].Value = false;
            };

            var btnRefreshSide = MkSideBtn("Làm mới (Refresh)", Color.FromArgb(16, 185, 129));
            btnRefreshSide.Click += async (s, e) => await ReloadDevices();

            var btnRenameSide = MkSideBtn("Đổi tên", Color.FromArgb(245, 158, 11));
            btnRenameSide.Click += (s, e) => ActionRenameSelected();

            // Thêm vào FlowLayout
            flowSideBtns.Controls.AddRange(new Control[] { btnSelectAll, btnSelectNone, btnRefreshSide, btnRenameSide });

            // 4. Bảng Thiết bị (Guna2DataGridView)
            _gridDevices = new Guna2DataGridView
            {
                Dock = DockStyle.Fill,
                BackgroundColor = Color.White,
                BorderStyle = BorderStyle.None,
                CellBorderStyle = DataGridViewCellBorderStyle.SingleHorizontal,

                // Ẩn tiêu đề cột cho gọn
                ColumnHeadersVisible = false,

                RowHeadersVisible = false,
                AllowUserToAddRows = false,
                AllowUserToResizeRows = false,
                SelectionMode = DataGridViewSelectionMode.FullRowSelect,
                MultiSelect = true,
                RowTemplate = { Height = 28 },
                Theme = Guna.UI2.WinForms.Enums.DataGridViewPresetThemes.Light,
                GridColor = Color.FromArgb(231, 229, 255),
                Cursor = Cursors.Hand
            };

            // Cột 0: Checkbox
            var colCheck = new DataGridViewCheckBoxColumn
            {
                HeaderText = "",
                Width = 30,
                TrueValue = true,
                FalseValue = false,
                AutoSizeMode = DataGridViewAutoSizeColumnMode.None
            };

            // Cột 1: Tên thiết bị (Đã bỏ HeaderText)
            var colName = new DataGridViewTextBoxColumn
            {
                HeaderText = "",
                ReadOnly = true,
                Width = 180, // Độ rộng cố định
                AutoSizeMode = DataGridViewAutoSizeColumnMode.None
            };
            colName.DefaultCellStyle.Font = new Font("Segoe UI Semibold", 9f);
            colName.DefaultCellStyle.ForeColor = Color.FromArgb(51, 65, 85);

            // Cột 2: ID ẩn
            var colId = new DataGridViewTextBoxColumn
            {
                Visible = false,
                ReadOnly = true
            };

            _gridDevices.Columns.AddRange(colCheck, colName, colId);

            // SỰ KIỆN: Click vào dòng là tự tích Checkbox
            _gridDevices.CellClick += (s, e) => {
                if (e.RowIndex >= 0)
                {
                    var cell = _gridDevices.Rows[e.RowIndex].Cells[0];
                    bool isChecked = Convert.ToBoolean(cell.Value);
                    cell.Value = !isChecked;
                }
            };

            // MENU CHUỘT PHẢI (CHỈ CÒN COPY VÀ RENAME)
            var ctxMenu = new ContextMenuStrip();
            var itemCopy = ctxMenu.Items.Add("Copy Device ID");
            var itemRename = ctxMenu.Items.Add("Đổi tên (Rename)");
            // Đã xóa dòng "Mở QScrcpyPlus" tại đây

            _gridDevices.ContextMenuStrip = ctxMenu;

            itemCopy.Click += (s, e) => {
                if (_gridDevices.CurrentRow == null) return;
                string id = _gridDevices.CurrentRow.Cells[2].Value.ToString();
                Clipboard.SetText(id);
                SetStatus($"Đã copy: {id}");
            };

            itemRename.Click += (s, e) => ActionRenameSelected();

            // Đã xóa sự kiện click của QScrcpyPlus

            pnlSidebar.Controls.Add(lblSideTitle);
            pnlSidebar.Controls.Add(flowSideBtns);
            pnlSidebar.Controls.Add(_gridDevices);
            _gridDevices.BringToFront();

            // 2. CONTENT (BÊN PHẢI)
            // Gán vào biến toàn cục (không dùng var)
            pnlContent = splitMain.Panel2;
            pnlContent.BackColor = Color.FromArgb(243, 244, 246);
            pnlContent.Controls.Clear();

            tabControl = new Guna2TabControl
            {
                Dock = DockStyle.Fill,
                TabButtonIdleState = { FillColor = Color.Transparent, ForeColor = Color.Gray, Font = new Font("Segoe UI Semibold", 10f) },
                TabButtonHoverState = { FillColor = Color.White, InnerColor = Color.White, ForeColor = Color.FromArgb(79, 70, 229) },
                TabButtonSelectedState = { FillColor = Color.White, ForeColor = Color.FromArgb(79, 70, 229), InnerColor = Color.FromArgb(79, 70, 229) },
                Alignment = TabAlignment.Top,
                ItemSize = new Size(120, 40)
            };
            pnlContent.Controls.Add(tabControl);

            // Thêm tabControl vào panel
            pnlContent.Controls.Add(tabControl);

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

            // AddBtn(cardApps, "Đóng User Apps", Color.FromArgb(100, 116, 139), async () => await ActionKillAllUserApps());

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

            // Chỉ cần thêm thẳng giao diện chính vào Form
            this.Controls.Add(splitMain);

            // Đảm bảo nó luôn nằm trên cùng (dù thực ra giờ chỉ còn một mình nó)
            splitMain.BringToFront();
        }

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
            // Nếu lượt quét trước chưa xong thì bỏ qua lượt này (Tránh treo tool)
            if (_isWatcherBusy) return;

            _isWatcherBusy = true;

            Task.Run(() => {
                try
                {
                    // Lấy danh sách ID hiện tại
                    var currentIds = GetAdbDeviceIds();

                    // Sắp xếp để so sánh chính xác (Máy A,B giống Máy B,A)
                    currentIds.Sort();
                    string currentHash = string.Join(",", currentIds);

                    // So sánh với danh sách cũ
                    if (currentHash != _lastDeviceHash)
                    {
                        // Có sự thay đổi (Cắm thêm HOẶC Rút ra)
                        _lastDeviceHash = currentHash;

                        // Cập nhật giao diện
                        if (IsHandleCreated && !Disposing)
                        {
                            Invoke(new Action(() => {
                                // Gọi hàm Refresh nhưng không cần await để tránh block UI
                                _ = ReloadDevices();
                                SetStatus($"Phát hiện thay đổi: {currentIds.Count} thiết bị.");
                            }));
                        }
                    }
                }
                catch { }
                finally
                {
                    _isWatcherBusy = false; // Mở khóa cho lượt quét sau
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
                ForeColor = Color.Red, // Màu xanh cổ vịt đậm (hoặc dùng Color.Red nếu thích)
                TextAlign = ContentAlignment.MiddleCenter,
                // Chỉnh Padding Top = 5 để căn giữa theo chiều dọc với các nút
                Padding = new Padding(5, 5, 5, 0),
                Margin = new Padding(0)
            };

            // --- TẠO Ô CHỌN SỐ LƯỢNG (MỚI) ---
            var numTakeCount = new Guna2NumericUpDown
            {
                Value = 1,          // Mặc định lấy 1
                Minimum = 1,        // Tối thiểu 1
                Maximum = 1000,     // Tối đa 1000
                DecimalPlaces = 0,  // Chỉ nhập số nguyên
                Width = 70,         // Chiều rộng vừa phải
                Height = compactH,  // Chiều cao bằng các nút khác
                Font = new Font("Segoe UI", 10f, FontStyle.Bold),
                Margin = new Padding(0, 0, 5, 0), // Cách lề phải một chút
                BorderRadius = 5,
                FillColor = Color.White,
                ForeColor = Color.FromArgb(14, 165, 233), // Màu chữ xanh
                UpDownButtonFillColor = Color.FromArgb(240, 244, 248), // Màu nền nút lên xuống
                UpDownButtonForeColor = Color.FromArgb(14, 165, 233)   // Màu mũi tên
            };

            // --- NÚT SEND (SỬA LẠI SỰ KIỆN CLICK) ---
            var btnPushToInput = new Guna2Button
            {
                Text = "↩",
                Height = compactH,
                Width = 45,
                FillColor = Color.FromArgb(14, 165, 233),
                ForeColor = Color.White,
                Font = new Font("Segoe UI", 13f, FontStyle.Bold),
                Margin = compactMargin,
                BorderRadius = 5,
                Cursor = Cursors.Hand
            };

            // SỰ KIỆN QUAN TRỌNG: Lấy giá trị từ ô numTakeCount truyền vào hàm
            btnPushToInput.Click += (s, e) => {
                // Lấy giá trị số lượng người dùng đang chọn
                int countToTake = (int)numTakeCount.Value;

                // Gọi hàm lấy hàng loạt (đã sửa ở bước trước)
                TransferAccountsToInput(countToTake);
            };
            // --- NÚT XÓA INPUT ---
            var btnClearInput = new Guna2Button
            {
                Text = "🗑",
                Height = compactH,
                Width = 45,
                FillColor = Color.FromArgb(239, 68, 68),
                ForeColor = Color.White,
                Font = new Font("Segoe UI", 13f, FontStyle.Bold),
                Margin = new Padding(5, 0, 0, 0),
                BorderRadius = 5,
                Cursor = Cursors.Hand
                // ❌ Đã xóa dòng ToolTipText gây lỗi
            };

            // ▼▼▼ THÊM ĐOẠN NÀY ĐỂ TẠO TOOLTIP ▼▼▼
            var tt = new System.Windows.Forms.ToolTip();
            tt.SetToolTip(btnClearInput, "Xóa trắng ô Input và 2FA");
            // ▲▲▲▲▲▲▲▲▲▲▲▲▲▲▲▲▲▲▲▲▲▲▲▲▲▲▲▲▲▲▲▲▲▲▲

            // Sự kiện Click (Giữ nguyên)
            btnClearInput.Click += (s, e) => {
                if (_txtAccountInput.TextLength == 0 && _txtSecretInput.TextLength == 0) return;

                var ask = MessageBox.Show("Bạn muốn xóa trắng toàn bộ dữ liệu đang nhập (Account & 2FA)?",
                                          "Xác nhận xóa", MessageBoxButtons.YesNo, MessageBoxIcon.Question);

                if (ask == DialogResult.Yes)
                {
                    _txtAccountInput.Clear();
                    _txtSecretInput.Clear();
                    if (_grid2Fa != null) _grid2Fa.Rows.Clear();
                    SetStatus("Đã dọn dẹp Input.");
                }
            };
            // Thêm vào Panel (Thứ tự: Nạp -> Paste -> Label -> Send)
            pnlStoreAction.Controls.Add(btnLoadAcc);
            pnlStoreAction.Controls.Add(btnImportClipboard);
            pnlStoreAction.Controls.Add(_lblStorageCount);
            pnlStoreAction.Controls.Add(btnPushToInput);
            pnlStoreAction.Controls.Add(numTakeCount);   // 1. Ô nhập số
            pnlStoreAction.Controls.Add(btnPushToInput); // 2. Nút Send
            pnlStoreAction.Controls.Add(btnClearInput);  // Nút Thùng rác (🗑)

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
                _tabStorage.TabPages[0].Text = $"Kho";
        }

        private void TransferAccountsToInput(int count = 1)
        {
            // 1. CHỐNG SPAM: Nếu đang xử lý thì không nhận lệnh mới
            if (_isTransferring) return;
            _isTransferring = true;

            if (_gridStorage.Rows.Count == 0)
            {
                SetStatus("Kho tài khoản trống!");
                _isTransferring = false;
                return;
            }

            try
            {
                // Tạm dừng giao diện để xử lý mượt (đặc biệt khi lấy 20-50 dòng)
                _gridStorage.SuspendLayout();
                _txtAccountInput.SuspendLayout();

                int actualToTake = Math.Min(count, _gridStorage.Rows.Count);
                List<string> linesTaken = new List<string>();
                List<DataGridViewRow> rowsToRemove = new List<DataGridViewRow>();

                // BƯỚC 1: Duyệt và gom dữ liệu (Không xóa ngay để tránh lệch Index)
                for (int i = 0; i < actualToTake; i++)
                {
                    var row = _gridStorage.Rows[i];
                    if (row.IsNewRow) continue;

                    string e = row.Cells[0].Value?.ToString()?.Trim() ?? "";
                    string p = row.Cells[1].Value?.ToString()?.Trim() ?? "";

                    if (!string.IsNullOrEmpty(e))
                    {
                        linesTaken.Add($"{e}|{p}");
                        rowsToRemove.Add(row); // Lưu lại dòng này để xóa sau
                    }
                }

                // BƯỚC 2: Cập nhật vào ô Input
                if (linesTaken.Count > 0)
                {
                    string bulkText = string.Join(Environment.NewLine, linesTaken);
                    if (_txtAccountInput.TextLength > 0)
                        _txtAccountInput.AppendText(Environment.NewLine + bulkText);
                    else
                        _txtAccountInput.AppendText(bulkText);

                    // Cuộn xuống cuối
                    _txtAccountInput.SelectionStart = _txtAccountInput.TextLength;
                    _txtAccountInput.ScrollToCaret();

                    // BƯỚC 3: Xóa khỏi kho Storage và thêm vào kho Đã dùng
                    foreach (var row in rowsToRemove)
                    {
                        // Thêm vào tab "Đã dùng" trên giao diện
                        string email = row.Cells[0].Value?.ToString() ?? "";
                        string pass = row.Cells[1].Value?.ToString() ?? "";
                        _gridUsed.Rows.Add(email, pass, "Used", DateTime.Now.ToString("HH:mm:ss"));

                        // Xóa chính xác dòng này khỏi kho (Cực kỳ an toàn)
                        _gridStorage.Rows.Remove(row);
                    }

                    // Ghi file Used (Ghi nối đuôi - Append)
                    File.AppendAllLines(UsedAccountFile, linesTaken);

                    // BƯỚC 4: Cập nhật con số hiển thị
                    _lblStorageCount.Text = _gridStorage.Rows.Count.ToString();
                    SetStatus($"Đã lấy {linesTaken.Count} tài khoản.");
                }
            }
            catch (Exception ex)
            {
                SetStatus("Lỗi lấy tài khoản: " + ex.Message);
            }
            finally
            {
                _txtAccountInput.ResumeLayout();
                _gridStorage.ResumeLayout();
                _isTransferring = false; // Mở khóa cho lần bấm sau
            }

            // BƯỚC 5: Lưu lại file kho (Chạy ngầm để không lag)
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
        // --- CÁC HÀM XỬ LÝ LOGIC CÒN THIẾU ---

        // 1. Hàm Refresh danh sách thiết bị
        private async Task ReloadDevices()
        {
            if (_gridDevices == null) return;
            SetStatus("Đang quét...");

            // Nạp tên từ userdata.ini
            LoadDeviceNames();

            var deviceIds = await Task.Run(() => GetAdbDeviceIds());

            // Dùng Invoke để thao tác UI an toàn
            _gridDevices.Invoke((MethodInvoker)delegate {
                _gridDevices.Rows.Clear();
                foreach (var id in deviceIds)
                {
                    string display = id;
                    if (_deviceNameById.ContainsKey(id))
                        display = $"{_deviceNameById[id]} ({id})";

                    // Thêm dòng: [Checkbox=False], [Tên hiển thị], [ID ẩn]
                    _gridDevices.Rows.Add(false, display, id);
                }
            });
            SetStatus($"Tìm thấy {deviceIds.Count} máy.");
        }
    
        // 2. Hàm Đổi tên thiết bị (Ví dụ cơ bản)
        private void ActionRenameSelected()
        {
            var targets = GetTargetDevices();
            if (targets.Count == 0) { MessageBox.Show("Chưa chọn máy nào!"); return; }

            string prefix = ShowInputDialog("Nhập tên gốc (Ví dụ: Máy):", "Đổi tên");
            if (string.IsNullOrWhiteSpace(prefix)) return;

            int count = 1;
            foreach (DataGridViewRow row in _gridDevices.Rows)
            {
                // Chỉ đổi tên những dòng được tích
                if (Convert.ToBoolean(row.Cells[0].Value))
                {
                    string realId = row.Cells[2].Value.ToString();
                    string newName = $"{prefix} {count}";

                    // 1. Cập nhật bộ nhớ
                    _deviceNameById[realId] = newName;

                    // 2. Cập nhật giao diện (Cột 1)
                    row.Cells[1].Value = $"{newName} ({realId})";

                    count++;
                }
            }
            // 3. Lưu xuống file userdata.ini
            SaveDeviceNames();
        }

        // --- HÀM HỖ TRỢ: TẠO HỘP THOẠI NHẬP LIỆU (KHÔNG CẦN THƯ VIỆN NGOÀI) ---
        // Bạn copy hàm này để dưới cùng file Form1.cs để dùng thay cho Microsoft.VisualBasic
        private string ShowInputDialog(string text, string caption)
        {
            Form prompt = new Form()
            {
                Width = 400,
                Height = 180,
                FormBorderStyle = FormBorderStyle.FixedDialog,
                Text = caption,
                StartPosition = FormStartPosition.CenterScreen,
                MaximizeBox = false,
                MinimizeBox = false
            };

            Label textLabel = new Label() { Left = 20, Top = 20, Text = text, Width = 350, Font = new Font("Segoe UI", 10f) };
            TextBox textBox = new TextBox() { Left = 20, Top = 50, Width = 340, Font = new Font("Segoe UI", 11f) };

            Button confirmation = new Button() { Text = "OK", Left = 240, Width = 120, Top = 90, DialogResult = DialogResult.OK, Height = 35, BackColor = Color.FromArgb(14, 165, 233), ForeColor = Color.White, FlatStyle = FlatStyle.Flat };

            confirmation.Click += (sender, e) => { prompt.Close(); };
            prompt.Controls.Add(textBox);
            prompt.Controls.Add(confirmation);
            prompt.Controls.Add(textLabel);
            prompt.AcceptButton = confirmation;

            return prompt.ShowDialog() == DialogResult.OK ? textBox.Text : "";
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
        private async Task ActionPushFile() { var t = GetTargetDevices(); if (t.Count == 0) { MessageBox.Show("Chưa chọn thiết bị!"); return; } using var d = new OpenFileDialog { Title = "Chọn file" }; if (d.ShowDialog() == DialogResult.OK) { StartProgress(); SetStatus($"Đang gửi file..."); await Task.Run(() => Parallel.ForEach(t, id => RunAdbWaitNoCapture(id, "push", d.FileName, "/sdcard/Download/"))); StopProgress(); MessageBox.Show("Gửi file xong."); } }
        private async Task ActionRunAdbOnSelectedAsync(string name, string cmd) { var t = GetTargetDevices(); if (t.Count == 0) { MessageBox.Show("Chưa chọn thiết bị!"); return; } SetStatus($"Running {name}..."); StartProgress(); await Task.Run(() => Parallel.ForEach(t, id => RunAdbNoWait($"-s {id} {cmd}"))); StopProgress(); SetStatus($"Done {name}"); }

        private async void RefreshDeviceList()
        {
            // Kiểm tra Grid đã được khởi tạo chưa để tránh lỗi Null
            if (_gridDevices == null || this.Disposing || this.IsDisposed) return;

            // 1. Nạp tên từ file cấu hình (userdata.ini)
            LoadDeviceNames();

            // 2. Lấy danh sách ID thật từ ADB
            // (Chạy luồng riêng để không đơ giao diện)
            var deviceIds = await Task.Run(() => GetAdbDeviceIds());

            // 3. Cập nhật vào Grid (Dùng Invoke để an toàn)
            if (this.IsHandleCreated)
            {
                this.Invoke(new Action(() =>
                {
                    try
                    {
                        // Lưu lại các ID đang được tích chọn hiện tại để tích lại sau khi refresh
                        var currentChecked = new HashSet<string>();
                        foreach (DataGridViewRow row in _gridDevices.Rows)
                        {
                            if (Convert.ToBoolean(row.Cells[0].Value))
                                currentChecked.Add(row.Cells[2].Value?.ToString() ?? "");
                        }

                        _gridDevices.Rows.Clear();

                        foreach (var id in deviceIds)
                        {
                            string display = id;
                            // Nếu có tên trong file INI thì hiển thị: Tên (ID)
                            if (_deviceNameById.ContainsKey(id))
                            {
                                display = $"{_deviceNameById[id]} ({id})";
                            }

                            // Kiểm tra xem máy này trước đó có được chọn không
                            bool isChecked = currentChecked.Contains(id);

                            // Thêm dòng vào Grid: [Checkbox], [Tên hiển thị], [ID ẩn]
                            _gridDevices.Rows.Add(isChecked, display, id);
                        }

                        SetStatus($"Tìm thấy {deviceIds.Count} thiết bị.");
                    }
                    catch { }
                }));
            }
        }

        private void RunBat(string bat, string args = "") { var p = Path.Combine(BaseDir, bat); if (!File.Exists(p)) { MessageBox.Show("Missing: " + p); return; } Process.Start(new ProcessStartInfo { FileName = "cmd.exe", Arguments = $"/c \"\"{p}\" {args}\"", UseShellExecute = false, CreateNoWindow = false, WorkingDirectory = BaseDir }); }

        private void ValidateAdbExists() { if (!File.Exists(AdbPath)) MessageBox.Show("Thiếu adb.exe trong thư mục tool: " + AdbPath); }
        private void TryWarmAdbServer() { try { RunAdbNoWait("start-server"); } catch { } }
        private void RunAdbNoWait(string args) => Process.Start(new ProcessStartInfo { FileName = AdbPath, Arguments = args, UseShellExecute = false, CreateNoWindow = true });
        private int RunAdbWaitNoCapture(string id, params string[] args) { var l = new List<string> { "-s", id }; l.AddRange(args); return RunProcessWaitNoCapture(AdbPath, l); }
        private string RunProcessReturnOutput(string exe, string args) { try { var p = new ProcessStartInfo { FileName = exe, Arguments = args, UseShellExecute = false, RedirectStandardOutput = true, CreateNoWindow = true, StandardOutputEncoding = System.Text.Encoding.UTF8 }; using var proc = Process.Start(p); string output = proc.StandardOutput.ReadToEnd(); proc.WaitForExit(); return output; } catch { return ""; } }
        private int RunProcessWaitNoCapture(string f, IEnumerable<string> a, string w = null) { var p = new ProcessStartInfo { FileName = f, UseShellExecute = false, CreateNoWindow = true, WorkingDirectory = w ?? BaseDir }; foreach (var x in a) p.ArgumentList.Add(x); var proc = Process.Start(p); proc.WaitForExit(); return proc.ExitCode; }
        private List<string> GetAdbDeviceIds() { try { var p = Process.Start(new ProcessStartInfo { FileName = AdbPath, Arguments = "devices", UseShellExecute = false, RedirectStandardOutput = true, CreateNoWindow = true }); var o = p.StandardOutput.ReadToEnd(); p.WaitForExit(); return o.Split('\n').Where(l => l.Contains("\tdevice")).Select(l => l.Split('\t')[0]).ToList(); } catch { return new List<string>(); } }
        // 1. ĐỌC FILE INI (Lấy NickName, bỏ qua WindowRect)
        private void LoadDeviceNames()
        {
            _deviceNameById.Clear();
            try
            {
                if (!File.Exists(IniFile)) return;

                var lines = File.ReadAllLines(IniFile);
                string currentSection = "";

                foreach (var line in lines)
                {
                    string trim = line.Trim();
                    // Phát hiện Section ID: [DeviceID]
                    if (trim.StartsWith("[") && trim.EndsWith("]"))
                    {
                        currentSection = trim.Substring(1, trim.Length - 2);
                    }
                    // Phát hiện dòng NickName
                    else if (trim.StartsWith("NickName="))
                    {
                        if (!string.IsNullOrEmpty(currentSection))
                        {
                            var name = trim.Substring("NickName=".Length).Trim();
                            if (!string.IsNullOrEmpty(name))
                            {
                                _deviceNameById[currentSection] = name;
                            }
                        }
                    }
                }
            }
            catch { }
        }

        // 2. GHI FILE INI (Chỉ sửa NickName, GIỮ NGUYÊN WindowRect)
        private void SaveDeviceNames()
        {
            try
            {
                // Tạo thư mục config nếu chưa có
                string dir = Path.GetDirectoryName(IniFile);
                if (!Directory.Exists(dir)) Directory.CreateDirectory(dir);

                // Đọc nội dung cũ lên để sửa (tránh mất các dòng WindowRect)
                List<string> lines = new List<string>();
                if (File.Exists(IniFile)) lines = File.ReadAllLines(IniFile).ToList();

                // Duyệt qua các máy cần lưu tên
                foreach (var kvp in _deviceNameById)
                {
                    string id = kvp.Key;
                    string name = kvp.Value;
                    string sectionHeader = $"[{id}]";
                    string nickNameLine = $"NickName={name}";

                    // Tìm xem ID này đã có trong file chưa
                    int sectionIndex = -1;
                    for (int i = 0; i < lines.Count; i++)
                    {
                        if (lines[i].Trim() == sectionHeader)
                        {
                            sectionIndex = i;
                            break;
                        }
                    }

                    if (sectionIndex != -1)
                    {
                        // -- ĐÃ CÓ MÁY NÀY --
                        // Tìm dòng NickName bên dưới để sửa
                        bool foundNick = false;
                        for (int i = sectionIndex + 1; i < lines.Count; i++)
                        {
                            string l = lines[i].Trim();
                            if (l.StartsWith("[")) break; // Sang máy khác rồi -> Dừng

                            if (l.StartsWith("NickName="))
                            {
                                lines[i] = nickNameLine; // Ghi đè tên mới
                                foundNick = true;
                                break;
                            }
                        }
                        // Nếu có Section mà chưa có dòng NickName -> Chèn vào
                        if (!foundNick) lines.Insert(sectionIndex + 1, nickNameLine);
                    }
                    else
                    {
                        // -- CHƯA CÓ MÁY NÀY --
                        // Thêm mới xuống cuối file (Kèm các thông số mặc định)
                        if (lines.Count > 0 && lines.Last() != "") lines.Add("");
                        lines.Add(sectionHeader);
                        lines.Add("WindowRectX=100"); // Tọa độ mặc định
                        lines.Add("WindowRectY=100");
                        lines.Add("WindowRectW=258");
                        lines.Add("WindowRectH=528");
                        lines.Add(nickNameLine); // Dòng tên
                    }
                }

                // Ghi lại xuống đĩa
                File.WriteAllLines(IniFile, lines);
            }
            catch { }
        }

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

        #region Helpers

        // ==========================================
        // 1. TAB CONVERT (CHUYỂN ĐỔI DỮ LIỆU)
        // ==========================================
        private void BuildConvertTab(TabPage page)
        {
            page.BackColor = Color.FromArgb(248, 250, 252);

            // 1. THANH CÔNG CỤ (Cho phép cuộn ngang nếu màn hình bé)
            var pnlTop = new FlowLayoutPanel
            {
                Dock = DockStyle.Top,
                Height = 60,
                Padding = new Padding(10, 12, 10, 10),
                BackColor = Color.White,
                FlowDirection = FlowDirection.LeftToRight,
                AutoScroll = true, // Quan trọng: Nếu bị che sẽ hiện thanh cuộn
                WrapContents = false // Quan trọng: Ép nằm trên 1 dòng
            };

            // ComboBox (Thu gọn width từ 320 -> 260 cho thoáng)
            var cboMode = new Guna2ComboBox
            {
                Width = 260,
                Height = 36,
                BorderRadius = 4,
                BorderColor = Color.FromArgb(203, 213, 225),
                Font = new Font("Segoe UI", 9.5f),
                StartIndex = 1 // Mặc định: 3 Cột
            };

            cboMode.Items.AddRange(new object[] {
        "0. 3 dòng -> User|Pass|2FA",
        "1. 3 dòng -> 3 CỘT (Google Sheet)",
        "2. 1 dòng (|) -> 3 CỘT (Tab)",
        "3. Lấy riêng cột 2FA",
        "4. Lấy User|Pass (Bỏ 2FA)"
    });

            // Các nút chức năng (Thu gọn width)
            var btnConvert = CreateButton("Chuyển đổi", Color.FromArgb(14, 165, 233), 90);
            var btnCopy = CreateButton("Copy Output", Color.FromArgb(34, 197, 94), 100);

            // ▼▼▼ NÚT XÓA (MÀU ĐỎ)
            var btnClear = CreateButton("Xóa", Color.FromArgb(239, 68, 68), 70);

            // Thêm theo thứ tự
            pnlTop.Controls.AddRange(new Control[] { cboMode, btnConvert, btnCopy, btnClear });

            // 2. MAIN AREA (GIỮ NGUYÊN)
            var split = new SplitContainer
            {
                Dock = DockStyle.Fill,
                Orientation = Orientation.Vertical,
                SplitterDistance = 450,
                SplitterWidth = 10,
                BackColor = Color.FromArgb(241, 245, 249)
            };

            var pnlLeft = new Panel { Dock = DockStyle.Fill, Padding = new Padding(15, 10, 5, 15) };
            var lblIn = new Label { Text = "Input:", Dock = DockStyle.Top, Height = 25, Font = new Font("Segoe UI", 9.5f, FontStyle.Bold), ForeColor = Color.FromArgb(71, 85, 105) };
            var txtIn = new TextBox { Multiline = true, ScrollBars = ScrollBars.Vertical, Dock = DockStyle.Fill, Font = new Font("Consolas", 10f), BorderStyle = BorderStyle.FixedSingle, PlaceholderText = "Email\r\nPass\r\n2FA..." };
            pnlLeft.Controls.Add(txtIn); pnlLeft.Controls.Add(lblIn);

            var pnlRight = new Panel { Dock = DockStyle.Fill, Padding = new Padding(5, 10, 15, 15) };
            var lblOut = new Label { Text = "Output:", Dock = DockStyle.Top, Height = 25, Font = new Font("Segoe UI", 9.5f, FontStyle.Bold), ForeColor = Color.FromArgb(71, 85, 105) };
            var txtOut = new TextBox { Multiline = true, ReadOnly = true, ScrollBars = ScrollBars.Vertical, Dock = DockStyle.Fill, Font = new Font("Consolas", 10f), BackColor = Color.WhiteSmoke, BorderStyle = BorderStyle.FixedSingle };
            pnlRight.Controls.Add(txtOut); pnlRight.Controls.Add(lblOut);

            split.Panel1.Controls.Add(pnlLeft);
            split.Panel2.Controls.Add(pnlRight);

            // 3. LOGIC XỬ LÝ (GIỮ NGUYÊN)
            btnConvert.Click += (s, e) =>
            {
                string input = txtIn.Text.Trim();
                if (string.IsNullOrEmpty(input)) return;
                var lines = input.Split(new[] { '\n', '\r' }, StringSplitOptions.RemoveEmptyEntries).Select(x => x.Trim()).Where(x => x.Length > 0).ToList();
                var results = new List<string>();
                int mode = cboMode.SelectedIndex;

                try
                {
                    if (mode == 0) for (int i = 0; i + 2 < lines.Count; i += 3) results.Add($"{lines[i]}|{lines[i + 1]}|{lines[i + 2]}");
                    else if (mode == 1) for (int i = 0; i + 2 < lines.Count; i += 3) results.Add($"{lines[i]}\t{lines[i + 1]}\t{lines[i + 2]}");
                    else if (mode == 2) foreach (var line in lines) results.Add(line.Replace("|", "\t"));
                    else if (mode == 3) foreach (var line in lines) { var p = line.Split('|'); if (p.Length >= 3) results.Add(p[2]); }
                    else if (mode == 4) foreach (var line in lines) { var p = line.Split('|'); if (p.Length >= 2) results.Add($"{p[0]}|{p[1]}"); }

                    txtOut.Text = string.Join(Environment.NewLine, results);
                    SetStatus($"Đã xong: {results.Count} dòng.");
                }
                catch (Exception ex) { MessageBox.Show("Lỗi: " + ex.Message); }
            };

            btnCopy.Click += (s, e) => { if (txtOut.TextLength > 0) { Clipboard.SetText(txtOut.Text); SetStatus("Đã Copy Output."); } };

            // SỰ KIỆN NÚT XÓA
            btnClear.Click += (s, e) => { txtIn.Clear(); txtOut.Clear(); SetStatus("Đã xóa"); };

            page.Controls.Add(split);
            page.Controls.Add(pnlTop);
        }

        // ==========================================
        // 2. CÁC HÀM HELPER CHUNG (UI)
        // ==========================================

        // Helper 1: Tạo nút cho Sidebar (Menu trái)
        private Guna2Button CreateSideButton(string text, Color color)
        {
            return new Guna2Button
            {
                Text = text,
                Height = 40,
                BorderRadius = 4,
                FillColor = color,
                ForeColor = Color.White,
                Font = new Font("Segoe UI Semibold", 9.5f),
                Cursor = Cursors.Hand,
                Margin = new Padding(0, 5, 0, 5),
                TextAlign = HorizontalAlignment.Left,
                TextOffset = new Point(10, 0)
            };
        }

        // Helper 2: Tạo nút chức năng cho Tab Convert
        private Guna2Button CreateButton(string text, Color color, int width)
        {
            return new Guna2Button
            {
                Text = text,
                FillColor = color,
                ForeColor = Color.White,
                Height = 36,
                Width = width,
                BorderRadius = 4,
                Font = new Font("Segoe UI Semibold", 9f),
                Margin = new Padding(0, 0, 10, 0),
                Cursor = Cursors.Hand
            };
        }

        // Helper 3: Tạo GroupBox cho Dashboard
        private Guna2GroupBox CreateGroupbox(string title)
        {
            return new Guna2GroupBox
            {
                Text = title,
                Font = new Font("Segoe UI Semibold", 9.5f),
                ForeColor = Color.Black,
                CustomBorderColor = Color.FromArgb(241, 245, 249),
                CustomBorderThickness = new Padding(0, 35, 0, 0),
                FillColor = Color.White,
                BorderColor = Color.FromArgb(226, 232, 240),
                BorderRadius = 6,
                Width = 340,
                Margin = new Padding(10)
            };
        }

        // Helper 4: Thêm nút vào GroupBox Dashboard
        private void AddBtn(Guna2GroupBox group, string text, Color color, Action onClick)
        {
            var btn = new Guna2Button
            {
                Text = text,
                FillColor = color,
                ForeColor = Color.White,
                Tag = onClick
            };
            btn.Click += (s, e) => onClick();
            group.Controls.Add(btn);
        }

        // Helper 5: Sắp xếp nút trong GroupBox (Responsive)
        private void ReflowCardCompact(Guna2GroupBox card, Color themeColor, FlowLayoutPanel parent)
        {
            var buttons = card.Controls.OfType<Guna2Button>().ToList();
            int gap = 10, padding = 15, headerHeight = 40, btnHeight = 38;
            int colWidth = (card.Width - (padding * 2) - gap) / 2;

            for (int i = 0; i < buttons.Count; i++)
            {
                var btn = buttons[i];
                btn.Height = btnHeight;
                btn.Width = colWidth;
                btn.BorderRadius = 4;
                btn.Font = new Font("Segoe UI", 9f, FontStyle.Bold);
                if (btn.FillColor == Color.FromArgb(94, 148, 255)) btn.FillColor = themeColor;

                int row = i / 2;
                int col = i % 2;
                int x = padding + (col * (colWidth + gap));
                int y = headerHeight + padding + (row * (btnHeight + gap));
                btn.Location = new Point(x, y);
            }
            int totalRows = (int)Math.Ceiling(buttons.Count / 2.0);
            card.Height = headerHeight + padding + (totalRows * (btnHeight + gap)) + 5;
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

        // Hàm lấy danh sách thiết bị đang chọn từ bảng _gridDevices
        private List<string> GetTargetDevices()
        {
            var list = new List<string>();
            if (_gridDevices == null) return list;

            if (_gridDevices.InvokeRequired)
                return (List<string>)_gridDevices.Invoke(new Func<List<string>>(() => GetTargetDevices()));

            foreach (DataGridViewRow row in _gridDevices.Rows)
            {
                // Cột 0 là Checkbox. Nếu True -> Lấy ID ở cột 2
                if (Convert.ToBoolean(row.Cells[0].Value))
                {
                    string id = row.Cells[2].Value?.ToString() ?? "";
                    if (!string.IsNullOrEmpty(id)) list.Add(id);
                }
            }
            return list;
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