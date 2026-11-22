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
using ToolAdb.Features.GeoSpoof;
using ToolAdb.Features.Grok;
using ToolAdb.Features.InappTrial;
using ToolAdb.Features.Picsart;
using ToolAdb.Infrastructure;
using static ToolAdb.UI.DashboardCards;

namespace ToolAdb
{
    public partial class Form1 : Form
    {
        // ==========================================
        // 1. CẤU HÌNH & BIẾN
        // ==========================================
        private Dictionary<string, string> _deviceNameById = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        private string BaseDir { get; } =
            string.Equals(Path.GetFileName(Environment.ProcessPath), "dotnet.exe", StringComparison.OrdinalIgnoreCase)
                ? AppContext.BaseDirectory
                : Path.GetDirectoryName(Environment.ProcessPath) ?? AppContext.BaseDirectory;
        private string ApkDir => Path.Combine(AppContext.BaseDirectory, "APK");
        private string AccountFile => Path.Combine(BaseDir, "accounts.txt");
        private string SaleAccountFile => Path.Combine(BaseDir, "accounts_sale.txt");
        private string UsedAccountFile => Path.Combine(BaseDir, "used_accounts.txt");
        private string IniFile => Path.Combine(BaseDir, "config", "userdata.ini");
        private readonly AdbClient _adb;
        private System.Windows.Forms.Timer _inputScanTimer = null!;
        private Panel pnlContent;
        private Guna2TabControl tabControl;
        private CheckedListBox _clbSidebarDevices; // List danh sách thiết bị
        private bool _isWatcherBusy; // Biến này giúp Timer không chạy chồng chéo

        // UI Controls
        private Label _lblStatusInfo = null!;
        private Guna2ProgressBar _progressBar = null!;
        // Auto Refresh Timer
        private System.Windows.Forms.Timer _deviceWatcherTimer = null!;
        private List<string> _displayedDeviceIds;
        // Tab Auto Login & 2FA
        private TextBox _txtAccountInput = null!;
        private TextBox _txtSecretInput = null!;
        private Guna2DataGridView _grid2Fa = null!;
        private System.Windows.Forms.Timer _totpTimer = null!;
        private bool _isTransferring = false;
        // Account Manager Storage
        private sealed class MailVault
        {
            public MailVault(string name, string filePath, Color accentColor, Color accentSoftColor, Color surfaceColor)
            {
                Name = name;
                FilePath = filePath;
                AccentColor = accentColor;
                AccentSoftColor = accentSoftColor;
                SurfaceColor = surfaceColor;
            }
            public string Name { get; }
            public string FilePath { get; }
            public Color AccentColor { get; }
            public Color AccentSoftColor { get; }
            public Color SurfaceColor { get; }
            public object SaveLock { get; } = new object();
            public Guna2DataGridView Grid { get; set; } = null!;
            public Label CountLabel { get; set; } = null!;
        }
        private MailVault _vaultNew = null!;
        private MailVault _vaultSale = null!;
        private Guna2DataGridView _gridUsed = null!;
        private Guna2TabControl _tabStorage = null!;
        // Thay CheckedListBox bằng Grid cho đẹp
        private Guna2DataGridView _gridDevices;
        private RichTextBox _txtMagiskLog = null!;
        private Guna2Button _btnMagiskPhase1 = null!;
        private Guna2Button _btnMagiskPhase2 = null!;
        private const int MagiskMaxWorkers = 5;
        public Form1()
        {
            InitializeComponent();
            _adb = new AdbClient(AppContext.BaseDirectory);
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
            Shown += async (s, e) =>
            {
                await ReloadDevices();
                _ = AppUpdater.CheckAndPromptAsync(this, silent: true);
            };

            // Timer 2FA (1s)
            _totpTimer = new System.Windows.Forms.Timer(components) { Interval = 1000 };
            _totpTimer.Tick += (s, e) => Update2FaGridCodes();
            _totpTimer.Start();

            // Timer Auto Detect Devices (3s)
            _deviceWatcherTimer = new System.Windows.Forms.Timer(components) { Interval = 3000 };
            _deviceWatcherTimer.Tick += DeviceWatcher_Tick;
            _deviceWatcherTimer.Start();
            // Timer quét Input (Delay 500ms để không làm phiền khi đang gõ)
            _inputScanTimer = new System.Windows.Forms.Timer(components) { Interval = 500 };
            _inputScanTimer.Tick += (s, e) => ScanInputFor2Fa();
        }

        // ==========================================
        // 2. GIAO DIỆN CHÍNH
        // ==========================================
        private void BuildModernLayout()
        {
            this.Controls.Clear();
            this.Size = new Size(960, 640);
            this.MinimumSize = new Size(760, 520);
            this.Text = $"ToolADB v{AppUpdater.CurrentVersionString}";
            this.BackColor = Color.FromArgb(243, 244, 246);
            this.Font = new Font("Segoe UI", 9f);

            const int sidebarWidth = 170;
            var splitMain = new SplitContainer
            {
                Dock = DockStyle.Fill,
                FixedPanel = FixedPanel.Panel1,
                IsSplitterFixed = true,
                BackColor = Color.FromArgb(229, 231, 235)
            };

            // 1. SIDEBAR (THANH BÊN TRÁI - DẠNG DỌC GỌN)
            // ==================================================
            var pnlSidebar = splitMain.Panel1;
            pnlSidebar.Controls.Clear(); // Xóa sạch cái cũ
            pnlSidebar.BackColor = Color.White;
            pnlSidebar.Padding = new Padding(6);

            var lblSideTitle = new Label { Text = "Thiết Bị", Dock = DockStyle.Top, Height = 26, Font = new Font("Segoe UI", 9.5f, FontStyle.Bold), ForeColor = Color.Green };

            // 1. Panel chứa 5 nút nhỏ gọn xếp thành 1 cột dọc (không đẩy ngang sidebar)
            var sideBtnTable = new TableLayoutPanel
            {
                Dock = DockStyle.Bottom,
                Height = 136,
                ColumnCount = 1,
                RowCount = 5,
                BackColor = Color.White,
                Padding = new Padding(0, 4, 0, 0),
                Margin = new Padding(0),
                AutoSize = false
            };
            sideBtnTable.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
            for (int r = 0; r < 5; r++)
                sideBtnTable.RowStyles.Add(new RowStyle(SizeType.Absolute, 26F));

            Guna2Button MkSideBtn(string txt, Color c) => new Guna2Button
            {
                Text = txt,
                Dock = DockStyle.Fill,
                BorderRadius = 4,
                FillColor = c,
                ForeColor = Color.White,
                Font = new Font("Segoe UI", 8.25f, FontStyle.Bold),
                Margin = new Padding(0, 2, 0, 2),
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

            var btnRefreshSide = MkSideBtn("Làm mới", Color.FromArgb(16, 185, 129));
            btnRefreshSide.Click += async (s, e) => await ReloadDevices();

            var btnRenameSide = MkSideBtn("Đổi tên", Color.FromArgb(245, 158, 11));
            btnRenameSide.Click += (s, e) => ActionRenameSelected();

            var btnUpdateSide = MkSideBtn($"Cập nhật (v{AppUpdater.CurrentVersionString})", Color.FromArgb(79, 70, 229));
            btnUpdateSide.Click += async (s, e) =>
            {
                btnUpdateSide.Enabled = false;
                try { await AppUpdater.CheckAndPromptAsync(this, silent: false); }
                finally { if (!btnUpdateSide.IsDisposed) btnUpdateSide.Enabled = true; }
            };

            sideBtnTable.Controls.Add(btnSelectAll, 0, 0);
            sideBtnTable.Controls.Add(btnSelectNone, 0, 1);
            sideBtnTable.Controls.Add(btnRefreshSide, 0, 2);
            sideBtnTable.Controls.Add(btnRenameSide, 0, 3);
            sideBtnTable.Controls.Add(btnUpdateSide, 0, 4);

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
            _gridDevices.CurrentCellDirtyStateChanged += (s, e) =>
            {
                if (_gridDevices.IsCurrentCellDirty && _gridDevices.CurrentCell is DataGridViewCheckBoxCell)
                    _gridDevices.CommitEdit(DataGridViewDataErrorContexts.Commit);
            };

            // Cột 0: Checkbox
            var colCheck = new DataGridViewCheckBoxColumn
            {
                HeaderText = "",
                Width = 26,
                TrueValue = true,
                FalseValue = false,
                AutoSizeMode = DataGridViewAutoSizeColumnMode.None
            };

            // Cột 1: Tên thiết bị (tự co giãn vừa khít chiều ngang sidebar)
            var colName = new DataGridViewTextBoxColumn
            {
                HeaderText = "",
                ReadOnly = true,
                AutoSizeMode = DataGridViewAutoSizeColumnMode.Fill
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
            pnlSidebar.Controls.Add(sideBtnTable);
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

            // TAB 1: DASHBOARD
            var tabDash = new TabPage { Text = "Dashboard", BackColor = Color.FromArgb(248, 250, 252) };
            var flowDash = new FlowLayoutPanel { Dock = DockStyle.Fill, AutoScroll = true, Padding = new Padding(15), FlowDirection = FlowDirection.LeftToRight };

            var cardSetup = CreateGroupbox("Thiết lập & Cài đặt");
            AddBtn(cardSetup, "Setup Android (All)", Color.FromArgb(16, 185, 129), async () => await ActionSetupAll());
            AddBtn(cardSetup, "Gửi File vào máy", Color.FromArgb(16, 185, 129), async () => await ActionPushFile());
            AddBtn(cardSetup, "Cài File .APK", Color.FromArgb(16, 185, 129), async () => await ActionInstallApkFileDialog());
            AddBtn(cardSetup, "Cài File .APKM", Color.FromArgb(16, 185, 129), async () => await ActionInstallApkmFileDialog());
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
            AddBtn(cardSystem, "Set Automation IME", Color.FromArgb(168, 85, 247), async () => {
                var targets = GetTargetDevices();
                if (targets.Count == 0) { SetStatus("No devices selected"); return; }
                SetStatus($"Đang reset & Set IME ({targets.Count} máy)...");
                StartProgress();
                var failed = await Task.Run(() => ForceSetAutomationIme(targets));
                StopProgress();
                if (failed.Count == 0)
                {
                    SetStatus($"Set IME OK: {targets.Count}/{targets.Count} máy");
                    MessageBox.Show($"Đã reset & set IME thành công cho {targets.Count} máy.");
                }
                else
                {
                    string summary = string.Join(", ", failed);
                    SetStatus($"⚠️ Set IME lỗi {failed.Count}/{targets.Count}: {summary}");
                    MessageBox.Show($"Set IME xong {targets.Count - failed.Count}/{targets.Count} máy.\nMáy lỗi/treo:\n{string.Join(Environment.NewLine, failed)}");
                }
            });
            AddBtn(cardSystem, "Reboot System", Color.FromArgb(100, 116, 139), async () => await ActionRunAdbOnSelectedAsync("Reboot", "reboot"));
            AddBtn(cardSystem, "Reboot Recovery", Color.FromArgb(124, 58, 237), async () => await ActionRunAdbOnSelectedAsync("Recovery", "reboot recovery"));
            AddBtn(cardSystem, "Reboot To Sideload", Color.FromArgb(234, 88, 12), async () => await ActionRunAdbOnSelectedAsync("Sideload", "reboot sideload"));
            AddBtn(cardSystem, "Flash ZIP Sideload", Color.FromArgb(239, 68, 68), async () => await ActionFlashZipSideload());
            AddBtn(cardSystem, "⚡ Restart ADB Server", Color.FromArgb(71, 85, 105), async () => await ActionRestartAdb());
            AddBtn(cardSystem, "🔥 Hot Reset", Color.FromArgb(220, 38, 38), async () => await ActionHotReset());
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

            // Tạm thời ẩn tab Spoof quốc gia để giao diện gọn hơn (giữ nguyên source Features/GeoSpoof để bật lại khi cần).
            // tabControl.TabPages.Add(new GeoSpoofTab(_adb, GetTargetDevices, SetStatus, StartProgress, StopProgress));
            tabControl.TabPages.Add(new PicsartTab(new PicsartService(_adb), GetTargetDevices));
            tabControl.TabPages.Add(new GrokTab(new GrokService(_adb), GetTargetDevices));
            tabControl.TabPages.Add(new InappTrialTab(new InappTrialService(_adb), GetTargetDevices));

            // Chỉ cần thêm thẳng giao diện chính vào Form rồi mới khóa chiều ngang sidebar gọn 170px
            this.Controls.Add(splitMain);
            void ApplySidebarWidth()
            {
                if (splitMain.Width < 300) return;
                try
                {
                    splitMain.Panel1MinSize = 140;
                    splitMain.Panel2MinSize = 150;
                    splitMain.SplitterDistance = sidebarWidth;
                }
                catch { }
            }
            ApplySidebarWidth();
            splitMain.HandleCreated += (_, _) => ApplySidebarWidth();
            this.Shown += (_, _) => ApplySidebarWidth();

            // Đảm bảo nó luôn nằm trên cùng (dù thực ra giờ chỉ còn một mình nó)
            splitMain.BringToFront();
        }

        // 3. LOGIC SETUP ANDROID
        // ==========================================
        private async Task ActionSetupAll()
        {
            var targets = await Task.Run(GetAdbDeviceIds);
            if (targets.Count == 0) { MessageBox.Show("Không tìm thấy thiết bị nào!"); return; }
            if (!Confirm($"Bắt đầu Setup {targets.Count} máy (Batch 4)?")) return;

            SetStatus("Starting Setup All...");
            StartProgress();

            await Task.Run(() => Parallel.ForEach(targets, new ParallelOptions { MaxDegreeOfParallelism = 4 }, id =>
            {
                if (Directory.Exists(ApkDir))
                {
                    foreach (var f in Directory.GetFiles(ApkDir, "*.apk"))
                        _adb.Run(new[] { "-s", id, "install-multiple", "-i", "com.android.vending", f });
                    foreach (var folder in Directory.GetDirectories(ApkDir, "*_apkm"))
                        InstallSplitApksFromFolder(id, folder);
                }

                string cmdConfig = "settings put global device_provisioned 1; " +
                                   "settings put secure --user 0 user_setup_complete 1; " +
                                   "settings put global development_settings_enabled 1; " +
                                   "settings put global stay_on_while_plugged_in 3; " +
                                   "settings put secure ui_night_mode 1";
                _adb.RunOnDevice(id, "shell", cmdConfig);

                SetDpiTo411dp(id);

                SetStatus($"Rebooting {id}...");
                _adb.RunOnDevice(id, "reboot");
            }));

            StopProgress();
            SetStatus("Setup Commands Sent!");
            MessageBox.Show($"Đã gửi lệnh Setup + Reboot cho {targets.Count} thiết bị.");
            await ReloadDevices();
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
                _adb.Run(args);
            }
            catch { }
        }

        private void SetDpiTo411dp(string deviceId)
        {
            try
            {
                string output = _adb.ReadOutput($"-s {deviceId} shell wm size");
                var match = Regex.Match(output, @"Physical size:\s*(\d+)x(\d+)");
                if (match.Success)
                {
                    int w = int.Parse(match.Groups[1].Value);
                    int h = int.Parse(match.Groups[2].Value);
                    int minPx = Math.Min(w, h);
                    int targetDpi = (int)Math.Round(minPx * 160.0 / 411.0);
                    _adb.RunOnDevice(deviceId, "shell", "wm", "density", targetDpi.ToString());
                }
            }
            catch { }
        }

        // ==========================================
        // 4. AUTO REFRESH
        // ==========================================
        private async void DeviceWatcher_Tick(object? sender, EventArgs e)
        {
            await ReloadDevices(automatic: true);
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
                FixedPanel = FixedPanel.Panel2,
                SplitterWidth = 5,
                BackColor = Color.FromArgb(241, 245, 249)
            };
            bool splitMainSized = false;
            void ApplyCompact2FaWidth()
            {
                if (splitMainSized || splitMain.Width < 420) return;
                int rightWidth = Math.Clamp((int)(splitMain.Width * 0.25), 195, 230);
                int maxLeft = splitMain.Width - splitMain.SplitterWidth - 185;
                int desiredLeft = Math.Clamp(splitMain.Width - splitMain.SplitterWidth - rightWidth, splitMain.Panel1MinSize, maxLeft);
                try
                {
                    splitMain.SplitterDistance = desiredLeft;
                    splitMain.Panel2MinSize = 185;
                    splitMainSized = true;
                }
                catch
                {
                }
            }
            splitMain.HandleCreated += (s, e) => ApplyCompact2FaWidth();
            splitMain.SizeChanged += (s, e) => ApplyCompact2FaWidth();

            var pnlLeftContainer = new Panel { Dock = DockStyle.Fill, Padding = new Padding(0) };
            var splitLeft = new SplitContainer
            {
                Dock = DockStyle.Fill,
                Orientation = Orientation.Horizontal,
                SplitterDistance = 400,
                BackColor = Color.FromArgb(229, 231, 235)
            };

            // 1. STORAGE (2 kho: New + Sale, tab Đã dùng chung)
            var pnlStorage = new Panel { Dock = DockStyle.Fill, BackColor = Color.White, Padding = new Padding(5) };
            _tabStorage = new Guna2TabControl
            {
                Dock = DockStyle.Fill,
                ItemSize = new Size(85, 30),
                Alignment = TabAlignment.Bottom,
                TabButtonIdleState = { FillColor = Color.FromArgb(241, 245, 249), ForeColor = Color.FromArgb(71, 85, 105), Font = new Font("Segoe UI Semibold", 9f) },
                TabButtonSelectedState = { Font = new Font("Segoe UI", 9.5f, FontStyle.Bold) }
            };

            _vaultNew = new MailVault(
                "New",
                AccountFile,
                Color.FromArgb(37, 99, 235),
                Color.FromArgb(219, 234, 254),
                Color.FromArgb(239, 246, 255));
            _vaultSale = new MailVault(
                "Sale",
                SaleAccountFile,
                Color.FromArgb(217, 119, 6),
                Color.FromArgb(254, 243, 199),
                Color.FromArgb(255, 251, 235));

            void ApplySelectedStorageTabStyle()
            {
                if (_tabStorage.SelectedIndex == 0)
                {
                    _tabStorage.TabButtonSelectedState.FillColor = _vaultNew.AccentSoftColor;
                    _tabStorage.TabButtonSelectedState.ForeColor = _vaultNew.AccentColor;
                    _tabStorage.TabButtonSelectedState.InnerColor = _vaultNew.AccentColor;
                }
                else if (_tabStorage.SelectedIndex == 1)
                {
                    _tabStorage.TabButtonSelectedState.FillColor = _vaultSale.AccentSoftColor;
                    _tabStorage.TabButtonSelectedState.ForeColor = _vaultSale.AccentColor;
                    _tabStorage.TabButtonSelectedState.InnerColor = _vaultSale.AccentColor;
                }
                else
                {
                    _tabStorage.TabButtonSelectedState.FillColor = Color.FromArgb(226, 232, 240);
                    _tabStorage.TabButtonSelectedState.ForeColor = Color.FromArgb(51, 65, 85);
                    _tabStorage.TabButtonSelectedState.InnerColor = Color.FromArgb(100, 116, 139);
                }
            }

            var tabNewAcc = CreateMailVaultTab(_vaultNew);
            var tabSaleAcc = CreateMailVaultTab(_vaultSale);

            var tabUsedAcc = new TabPage { Text = "Đã dùng", BackColor = Color.WhiteSmoke };
            _gridUsed = CreateEditableAccountGrid(null);
            tabUsedAcc.Controls.Add(_gridUsed);

            _tabStorage.TabPages.Add(tabNewAcc);
            _tabStorage.TabPages.Add(tabSaleAcc);
            _tabStorage.TabPages.Add(tabUsedAcc);
            _tabStorage.SelectedIndexChanged += (s, e) => ApplySelectedStorageTabStyle();
            ApplySelectedStorageTabStyle();
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
                Padding = new Padding(3),
                BackColor = Color.White
            };

            // 2. Ô nhập Secret (Dock Top)
            _txtSecretInput = new TextBox
            {
                Multiline = true,
                ScrollBars = ScrollBars.Vertical,
                Dock = DockStyle.Top,
                Height = 76,
                Font = new Font("Consolas", 8.5f),
                PlaceholderText = "Secret Key (1 dòng/key)..."
            };
            _txtSecretInput.TextChanged += (s, e) => ParseSecretsToGrid();

            // 3. Khởi tạo Bảng 2FA (Thu gọn vừa khung hẹp)
            _grid2Fa = new Guna2DataGridView();
            _grid2Fa.Dock = DockStyle.Fill;
            _grid2Fa.BackgroundColor = Color.White;
            _grid2Fa.AllowUserToAddRows = false;
            _grid2Fa.RowHeadersVisible = false;
            _grid2Fa.ReadOnly = true;
            _grid2Fa.Theme = Guna.UI2.WinForms.Enums.DataGridViewPresetThemes.Default;
            _grid2Fa.ColumnHeadersHeight = 32;
            _grid2Fa.RowTemplate.Height = 28;

            _grid2Fa.AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.None;

            var colIdx = new DataGridViewTextBoxColumn
            {
                HeaderText = "#",
                Name = "idx",
                Width = 18,
                AutoSizeMode = DataGridViewAutoSizeColumnMode.None
            };
            colIdx.DefaultCellStyle.Alignment = DataGridViewContentAlignment.MiddleCenter;
            colIdx.HeaderCell.Style.Alignment = DataGridViewContentAlignment.MiddleCenter;

            var colCode = new DataGridViewTextBoxColumn
            {
                HeaderText = "CODE",
                Name = "code",
                AutoSizeMode = DataGridViewAutoSizeColumnMode.Fill
            };
            colCode.DefaultCellStyle.Font = new Font("Segoe UI", 9.5f, FontStyle.Bold);
            colCode.DefaultCellStyle.ForeColor = Color.Teal;
            colCode.DefaultCellStyle.Padding = new Padding(3, 0, 0, 0);

            var colBtnCopy = new DataGridViewButtonColumn
            {
                Text = "Copy",
                UseColumnTextForButtonValue = true,
                HeaderText = "",
                Width = 42,
                AutoSizeMode = DataGridViewAutoSizeColumnMode.None
            };

            var colBtnSend = new DataGridViewButtonColumn
            {
                Text = "Gửi >",
                UseColumnTextForButtonValue = true,
                HeaderText = "",
                Width = 44,
                AutoSizeMode = DataGridViewAutoSizeColumnMode.None
            };

            // Thêm vào bảng
            _grid2Fa.Columns.Add(colIdx);
            _grid2Fa.Columns.Add(colCode);
            _grid2Fa.Columns.Add(colBtnCopy);
            _grid2Fa.Columns.Add(colBtnSend);
            _grid2Fa.CellContentClick += Grid2Fa_CellContentClick;

            // 4. Panel Nút Hành Động (Dock Bottom)
            var pnlBottomActions = new TableLayoutPanel
            {
                Dock = DockStyle.Bottom,
                Height = 35,
                ColumnCount = 2,
                RowCount = 1,
                Margin = new Padding(0),
                Padding = new Padding(0)
            };
            pnlBottomActions.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50F));
            pnlBottomActions.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50F));

            var btnCopyAll2Fa = new Guna2Button
            {
                Text = "Copy All",
                Dock = DockStyle.Fill,
                FillColor = Color.Teal,
                Font = new Font("Segoe UI", 8.5f, FontStyle.Bold),
                Margin = new Padding(0, 0, 2, 0)
            };
            btnCopyAll2Fa.Click += (s, e) => {
                var codes = new List<string>();
                foreach (DataGridViewRow row in _grid2Fa.Rows)
                {
                    if (row.Cells[1].Value != null)
                    {
                        string code = row.Cells[1].Value.ToString();
                        if (!string.IsNullOrWhiteSpace(code))
                        {
                            int idx = code.IndexOf('(');
                            if (idx >= 0) code = code.Substring(0, idx).Trim();
                            else code = code.Trim();

                            if (!string.IsNullOrEmpty(code))
                            {
                                codes.Add(code);
                            }
                        }
                    }
                }
                if (codes.Count > 0)
                {
                    Clipboard.SetText(string.Join(Environment.NewLine, codes));
                    SetStatus($"Đã copy {codes.Count} mã 2FA.");
                }
                else
                {
                    SetStatus("Không có mã 2FA nào để copy.");
                }
            };

            var btnSendAll2Fa = new Guna2Button
            {
                Text = "Gửi All",
                Dock = DockStyle.Fill,
                FillColor = Color.FromArgb(124, 58, 237),
                Font = new Font("Segoe UI", 8.5f, FontStyle.Bold),
                Margin = new Padding(2, 0, 0, 0)
            };
            btnSendAll2Fa.Click += async (s, e) => await ActionSend2FaToAll();

            pnlBottomActions.Controls.Add(btnCopyAll2Fa, 0, 0);
            pnlBottomActions.Controls.Add(btnSendAll2Fa, 1, 0);

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

            // Add Group Các Nút (Dock Bottom -> Nằm dưới cùng)
            pnlRight.Controls.Add(pnlBottomActions);

            // 7. Thêm Grid vào Grid
            // Đưa các thành phần Dock Fill lên trên để hiển thị đúng
            _grid2Fa.BringToFront();
            pnlBottomActions.SendToBack(); // Đẩy nút xuống dưới

            // 8. Add vào giao diện chính
            splitMain.Panel2.Controls.Add(pnlRight);
            page.Controls.Add(splitMain);

            // Load dữ liệu
            LoadAccountsToGrid(_vaultNew);
            LoadAccountsToGrid(_vaultSale);
            LoadUsedAccountsToGrid();
        }

        private TabPage CreateMailVaultTab(MailVault vault)
        {
            var tab = new TabPage { Text = vault.Name, BackColor = vault.SurfaceColor };
            vault.Grid = CreateEditableAccountGrid(vault);

            var pnlStoreAction = new FlowLayoutPanel
            {
                Dock = DockStyle.Bottom,
                AutoSize = true,
                AutoSizeMode = AutoSizeMode.GrowAndShrink,
                FlowDirection = FlowDirection.LeftToRight,
                BackColor = vault.SurfaceColor,
                Padding = new Padding(2),
                WrapContents = false
            };

            const int compactH = 28;
            var iconFont = new Font("Segoe UI", 11.5f, FontStyle.Bold);
            var compactMargin = new Padding(1);

            var btnLoadAcc = new Guna2Button
            {
                Text = "🔄",
                Height = compactH,
                Width = 34,
                FillColor = Color.DimGray,
                Font = iconFont,
                Margin = compactMargin,
                TextOffset = new Point(0, -1)
            };
            btnLoadAcc.Click += (s, e) => LoadAccountsToGrid(vault);

            var btnImportClipboard = new Guna2Button
            {
                Text = "➕",
                Height = compactH,
                Width = 34,
                FillColor = Color.SeaGreen,
                Font = iconFont,
                Margin = compactMargin,
                TextOffset = new Point(0, -1)
            };
            btnImportClipboard.Click += (s, e) => ActionPasteImportToStorage(vault);

            vault.CountLabel = new Label
            {
                Text = "Còn: 0",
                AutoSize = true,
                Font = new Font("Segoe UI", 10f, FontStyle.Bold),
                ForeColor = vault.AccentColor,
                TextAlign = ContentAlignment.MiddleCenter,
                Padding = new Padding(4, 5, 4, 0),
                Margin = new Padding(0)
            };

            var numTakeCount = new Guna2NumericUpDown
            {
                Value = 1,
                Minimum = 1,
                Maximum = 1000,
                DecimalPlaces = 0,
                Width = 58,
                Height = compactH,
                Font = new Font("Segoe UI", 10f, FontStyle.Bold),
                Margin = new Padding(0, 0, 4, 0),
                BorderRadius = 5,
                FillColor = Color.White,
                ForeColor = vault.AccentColor,
                UpDownButtonFillColor = vault.AccentSoftColor,
                UpDownButtonForeColor = vault.AccentColor
            };

            var btnPushToInput = new Guna2Button
            {
                Text = "↩",
                Height = compactH,
                Width = 38,
                FillColor = vault.AccentColor,
                ForeColor = Color.White,
                Font = new Font("Segoe UI", 13f, FontStyle.Bold),
                Margin = compactMargin,
                BorderRadius = 5,
                Cursor = Cursors.Hand
            };
            btnPushToInput.Click += (s, e) => TransferAccountsToInput(vault, (int)numTakeCount.Value);

            var btnSyncSetting = new Guna2Button
            {
                Text = "⚙",
                Height = compactH,
                Width = 36,
                FillColor = Color.FromArgb(71, 85, 105),
                ForeColor = Color.White,
                Font = new Font("Segoe UI", 13f, FontStyle.Bold),
                Margin = new Padding(4, 0, 0, 0),
                BorderRadius = 5,
                Cursor = Cursors.Hand
            };
            btnSyncSetting.Click += async (s, e) => await ActionRunAdbOnSelectedAsync("Open Sync", "shell am start -a android.settings.SYNC_SETTINGS");

            var btnClearInput = new Guna2Button
            {
                Text = "🗑",
                Height = compactH,
                Width = 36,
                FillColor = Color.FromArgb(239, 68, 68),
                ForeColor = Color.White,
                Font = new Font("Segoe UI", 13f, FontStyle.Bold),
                Margin = new Padding(4, 0, 0, 0),
                BorderRadius = 5,
                Cursor = Cursors.Hand
            };
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

            var tt = new System.Windows.Forms.ToolTip();
            tt.SetToolTip(btnLoadAcc, $"Nạp lại kho {vault.Name}");
            tt.SetToolTip(btnImportClipboard, $"Paste thêm mail vào kho {vault.Name}");
            tt.SetToolTip(btnPushToInput, $"Lấy từ kho {vault.Name} sang Input Area");
            tt.SetToolTip(btnSyncSetting, "Mở Sync Settings");
            tt.SetToolTip(btnClearInput, "Xóa trắng ô Input và 2FA");

            pnlStoreAction.Controls.Add(btnLoadAcc);
            pnlStoreAction.Controls.Add(btnImportClipboard);
            pnlStoreAction.Controls.Add(vault.CountLabel);
            pnlStoreAction.Controls.Add(numTakeCount);
            pnlStoreAction.Controls.Add(btnPushToInput);
            pnlStoreAction.Controls.Add(btnSyncSetting);
            pnlStoreAction.Controls.Add(btnClearInput);

            tab.Controls.Add(vault.Grid);
            tab.Controls.Add(pnlStoreAction);
            return tab;
        }

        private Guna2DataGridView CreateEditableAccountGrid(MailVault vault)
        {
            bool isEditable = vault != null;
            var g = new Guna2DataGridView
            {
                Dock = DockStyle.Fill,
                BackgroundColor = vault?.SurfaceColor ?? Color.White,
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

            if (vault != null)
            {
                g.EnableHeadersVisualStyles = false;
                g.ThemeStyle.HeaderStyle.BackColor = vault.AccentColor;
                g.ThemeStyle.HeaderStyle.ForeColor = Color.White;
                g.ThemeStyle.RowsStyle.BackColor = Color.White;
                g.ThemeStyle.AlternatingRowsStyle.BackColor = vault.SurfaceColor;
                g.ThemeStyle.RowsStyle.SelectionBackColor = vault.AccentSoftColor;
                g.ThemeStyle.RowsStyle.SelectionForeColor = Color.FromArgb(30, 41, 59);
            }

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

                if (e.ColumnIndex == 2)
                {
                    if (!TrySetClipboardText(g.Rows[e.RowIndex].Cells[0].Value?.ToString() ?? ""))
                    {
                        SetStatus("Clipboard busy, copy Email failed");
                        return;
                    }
                    var cell = g.Rows[e.RowIndex].Cells[0];
                    cell.Style.BackColor = Color.FromArgb(220, 252, 231);
                    cell.Style.SelectionBackColor = Color.FromArgb(220, 252, 231);
                    SetStatus("Copied Email");
                }
                else if (e.ColumnIndex == 3)
                {
                    if (!TrySetClipboardText(g.Rows[e.RowIndex].Cells[1].Value?.ToString() ?? ""))
                    {
                        SetStatus("Clipboard busy, copy Pass failed");
                        return;
                    }
                    var cell = g.Rows[e.RowIndex].Cells[1];
                    cell.Style.BackColor = Color.FromArgb(220, 252, 231);
                    cell.Style.SelectionBackColor = Color.FromArgb(220, 252, 231);
                    SetStatus("Copied Pass");
                }
                else if (vault != null && e.ColumnIndex == 4) MoveSingleRowToUsed(vault, e.RowIndex);
            };

            if (vault != null) g.CellEndEdit += (s, e) => SaveStorageFile(vault);
            return g;
        }

        private void ActionPasteImportToStorage(MailVault vault)
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
                    vault.Grid.Rows.Add(p.email, p.pass);
                    added++;
                }
            }

            if (added > 0)
            {
                SaveStorageFile(vault);
                UpdateStorageCount(vault);
                MessageBox.Show($"Đã thêm {added} tài khoản vào kho {vault.Name}!");
            }
        }

        private void MoveSingleRowToUsed(MailVault vault, int rowIndex)
        {
            try
            {
                var row = vault.Grid.Rows[rowIndex];
                string e = row.Cells[0].Value?.ToString() ?? "";
                string p = row.Cells[1].Value?.ToString() ?? "";

                _gridUsed.Rows.Add(e, p);

                try
                {
                    File.AppendAllText(UsedAccountFile, $"{e}|{p}{Environment.NewLine}");
                }
                catch { }

                vault.Grid.Rows.RemoveAt(rowIndex);
                UpdateStorageCount(vault);
                SaveStorageFileAsync(vault);

                SetStatus($"Đã chuyển 1 dòng từ {vault.Name} sang Đã dùng.");
            }
            catch { }
        }

        private void LoadAccountsToGrid(MailVault vault)
        {
            vault.Grid.Rows.Clear();
            if (File.Exists(vault.FilePath))
            {
                var lines = File.ReadAllLines(vault.FilePath);
                foreach (var line in lines)
                {
                    if (string.IsNullOrWhiteSpace(line)) continue;
                    var p = ParseLine(line);
                    if (!string.IsNullOrEmpty(p.email)) vault.Grid.Rows.Add(p.email, p.pass);
                }
            }
            UpdateStorageCount(vault);
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

        private static List<string> CollectVaultLines(MailVault vault)
        {
            var lines = new List<string>();
            foreach (DataGridViewRow row in vault.Grid.Rows)
            {
                string e = row.Cells[0].Value?.ToString() ?? "";
                string p = row.Cells[1].Value?.ToString() ?? "";
                if (!string.IsNullOrWhiteSpace(e)) lines.Add($"{e}|{p}");
            }
            return lines;
        }

        private static void SaveStorageFile(MailVault vault)
        {
            var lines = CollectVaultLines(vault);
            lock (vault.SaveLock)
            {
                try { File.WriteAllText(vault.FilePath, string.Join(Environment.NewLine, lines)); } catch { }
            }
        }

        private static void UpdateStorageCount(MailVault vault)
        {
            if (vault.CountLabel != null)
                vault.CountLabel.Text = $"Còn: {vault.Grid.Rows.Count}";
        }

        private void TransferAccountsToInput(MailVault vault, int count = 1)
        {
            if (_isTransferring) return;
            _isTransferring = true;

            if (vault.Grid.Rows.Count == 0)
            {
                SetStatus($"Kho {vault.Name} trống!");
                _isTransferring = false;
                return;
            }

            try
            {
                vault.Grid.SuspendLayout();
                _txtAccountInput.SuspendLayout();

                int actualToTake = Math.Min(count, vault.Grid.Rows.Count);
                List<string> linesTaken = new List<string>();
                List<DataGridViewRow> rowsToRemove = new List<DataGridViewRow>();

                for (int i = 0; i < actualToTake; i++)
                {
                    var row = vault.Grid.Rows[i];
                    if (row.IsNewRow) continue;

                    string e = row.Cells[0].Value?.ToString()?.Trim() ?? "";
                    string p = row.Cells[1].Value?.ToString()?.Trim() ?? "";

                    if (!string.IsNullOrEmpty(e))
                    {
                        linesTaken.Add($"{e}|{p}");
                        rowsToRemove.Add(row);
                    }
                }

                if (linesTaken.Count > 0)
                {
                    string bulkText = string.Join(Environment.NewLine, linesTaken);
                    if (_txtAccountInput.TextLength > 0)
                        _txtAccountInput.AppendText(Environment.NewLine + bulkText);
                    else
                        _txtAccountInput.AppendText(bulkText);

                    _txtAccountInput.SelectionStart = _txtAccountInput.TextLength;
                    _txtAccountInput.ScrollToCaret();

                    foreach (var row in rowsToRemove)
                    {
                        string email = row.Cells[0].Value?.ToString() ?? "";
                        string pass = row.Cells[1].Value?.ToString() ?? "";
                        _gridUsed.Rows.Add(email, pass);
                        vault.Grid.Rows.Remove(row);
                    }

                    File.AppendAllLines(UsedAccountFile, linesTaken);

                    UpdateStorageCount(vault);
                    SetStatus($"Đã lấy {linesTaken.Count} tài khoản từ kho {vault.Name}.");
                }
            }
            catch (Exception ex)
            {
                SetStatus("Lỗi lấy tài khoản: " + ex.Message);
            }
            finally
            {
                _txtAccountInput.ResumeLayout();
                vault.Grid.ResumeLayout();
                _isTransferring = false;
            }

            SaveStorageFileAsync(vault);
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
                string output = _adb.ReadOutput($"-s {deviceId} shell pm list packages -3 --user 0");
                if (string.IsNullOrWhiteSpace(output)) output = _adb.ReadOutput($"-s {deviceId} shell pm list packages -3");
                if (!string.IsNullOrWhiteSpace(output))
                {
                    var lines = output.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
                    foreach (var line in lines)
                    {
                        var pkgLine = line.Trim();
                        if (!pkgLine.StartsWith("package:", StringComparison.OrdinalIgnoreCase)) continue;
                        string pkg = pkgLine.Substring("package:".Length).Trim();
                        if (!string.IsNullOrWhiteSpace(pkg)) _adb.RunOnDevice(deviceId, "shell", "am", "force-stop", pkg);
                    }
                    _adb.RunOnDevice(deviceId, "shell", "am", "kill-all");
                }
                else _adb.RunOnDevice(deviceId, "shell", "am", "kill-all");
            }));
            StopProgress(); SetStatus("Đã đóng tất cả User Apps.");
        }

        private async Task ActionRestartAdb()
        {
            SetStatus("Killing ADB Server..."); StartProgress();
            await Task.Run(() => {
                _adb.Run(new[] { "kill-server" });
                Thread.Sleep(1500);
                _adb.Run(new[] { "start-server" });
            });
            StopProgress(); SetStatus("ADB Server Restarted."); await ReloadDevices();
            MessageBox.Show("ADB Server đã khởi động lại.");
        }

        private async Task ActionHotReset()
        {
            var targets = GetTargetDevices();
            if (targets.Count == 0) { MessageBox.Show("Chưa chọn thiết bị!"); return; }
            if (!Confirm("CẢNH BÁO: Hot Reset sẽ xóa/xóa bộ đệm GMS, GSF, Play Store, hủy Android ID và Accounts. Bạn chắc chắn muốn chạy?")) return;

            SetStatus("Đang thực hiện Hot Reset...");
            StartProgress();

            string scriptContent = @"#!/system/bin/sh

am broadcast -a com.yurikey.manager.CLEAR_TRACES > /dev/null 2>&1
sleep 1
am start -n com.yurikey.manager/.MainActivity --es action clear_all_detection_traces > /dev/null 2>&1
sleep 2
am start -n com.yurikey.manager/.MainActivity --es action kill_all > /dev/null 2>&1
sleep 1

am force-stop com.google.android.gms > /dev/null 2>&1
am force-stop com.google.android.gsf > /dev/null 2>&1
am force-stop com.android.vending > /dev/null 2>&1
killall com.google.android.gms > /dev/null 2>&1
killall com.google.android.gms.persistent > /dev/null 2>&1
sleep 1

rm -rf /data/data/com.google.android.gms/*
rm -rf /data/data/com.google.android.gms/.[^.]*
rm -rf /data/data/com.google.android.gsf/*
rm -rf /data/data/com.google.android.gsf/.[^.]*
rm -rf /data/data/com.android.vending/*
rm -rf /data/data/com.android.vending/.[^.]*

sqlite3 /data/system/users/0/settings_ssaid.xml ""DELETE FROM main WHERE name='android_id'"" > /dev/null 2>&1
rm -f /data/system/users/0/settings_ssaid.xml

rm -f /data/system/users/0/accounts.db*
rm -f /data/system_ce/0/accounts_ce.db*
rm -f /data/system_de/0/accounts_de.db*

rm -rf /data/misc/keystore/*
rm -rf /data/misc/keystore/.*

am broadcast -a com.yurikey.manager.CLEAR_TRACES > /dev/null 2>&1
am start -n com.yurikey.manager/.MainActivity --es action clear_all_detection_traces > /dev/null 2>&1
sleep 2
rm -rf /data/data/com.yurikey.manager/*
rm -rf /sdcard/Android/data/com.yurikey.manager/*

sleep 1
setprop sys.powerctl reboot
";
            string tempScript = Path.Combine(Path.GetTempPath(), "reset.sh");
            File.WriteAllText(tempScript, scriptContent.Replace("\r\n", "\n"));

            await Task.Run(() => {
                Parallel.ForEach(targets, deviceId => {
                    // Bước 1 & 2: Root & Remount
                    _adb.Run(new[] { "-s", deviceId, "root" });
                    Thread.Sleep(1500);
                    _adb.Run(new[] { "-s", deviceId, "remount" });
                    Thread.Sleep(1500);

                    // Bước 3: Đẩy file
                    _adb.Run(new[] { "-s", deviceId, "push", tempScript, "/system/bin/reset.sh" });

                    // Bước 4: Sửa lỗi CRLF
                    _adb.Run(new[] { "-s", deviceId, "shell", "sed -i 's/\\r//g' /system/bin/reset.sh" });

                    // Bước 5 & 6: Cấp quyền và khôi phục nhãn
                    _adb.Run(new[] { "-s", deviceId, "shell", "chmod 0755 /system/bin/reset.sh" });
                    _adb.Run(new[] { "-s", deviceId, "shell", "restorecon /system/bin/reset.sh" });

                    // Bước 7: Chạy script
                    _adb.Run(new[] { "-s", deviceId, "shell", "/system/bin/reset.sh" });
                });
            });

            StopProgress();
            SetStatus("Hot Reset hoàn tất. Chờ thiết bị khởi động lại.");
            MessageBox.Show($"Lệnh Hot Reset đã được gửi tới {targets.Count} máy.", "Xong");
        }

        // --- CÁC HÀM XỬ LÝ LOGIC CÒN THIẾU ---

        // 1. Hàm Refresh danh sách thiết bị
        private async Task ReloadDevices(bool automatic = false)
        {
            if (_isWatcherBusy || _gridDevices == null || IsDisposed || Disposing) return;

            _isWatcherBusy = true;
            try
            {
                if (!automatic) SetStatus("Đang quét thiết bị...");
                var result = await Task.Run(() => _adb.Capture(new[] { "devices" }, timeoutMs: 10000));
                if (IsDisposed || Disposing) return;

                if (result.exitCode != 0)
                {
                    if (!automatic) SetStatus("Lỗi quét ADB: " + result.stderr.Trim());
                    return;
                }

                var deviceIds = AdbClient.ParseAdbDevices(result.stdout, "device");
                if (automatic && _displayedDeviceIds != null &&
                    _displayedDeviceIds.ToHashSet(StringComparer.Ordinal).SetEquals(deviceIds)) return;

                var checkedIds = GetTargetDevices().ToHashSet(StringComparer.Ordinal);
                LoadDeviceNames();
                _gridDevices.Rows.Clear();
                foreach (var id in deviceIds)
                {
                    string display = _deviceNameById.TryGetValue(id, out var name) ? $"{name} ({id})" : id;
                    _gridDevices.Rows.Add(checkedIds.Contains(id), display, id);
                }

                _displayedDeviceIds = deviceIds;
                SetStatus($"Tìm thấy {deviceIds.Count} thiết bị.");
            }
            finally
            {
                _isWatcherBusy = false;
            }
        }

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
        private async Task ActionInstallApkFileDialog()
        {
            var targets = GetTargetDevices();
            if (targets.Count == 0)
            {
                MessageBox.Show("Chưa chọn thiết bị!");
                return;
            }

            using var dialog = new OpenFileDialog { Title = "Chọn file APK", Filter = "APK (*.apk)|*.apk" };
            if (dialog.ShowDialog() != DialogResult.OK) return;

            string file = dialog.FileName;
            await RunAdbFileActionAsync(targets, new[] { "install", "-r", file }, 300000,
                "Cài APK", "Đang cài APK...", installed => $"Đã cài APK trên {installed}/{targets.Count} thiết bị.");
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
                    _adb.Run(args);
                }
            }
            catch { }
            finally { try { Directory.Delete(tmp, true); } catch { } }
        }
        private async Task ActionPushFile()
        {
            var targets = GetTargetDevices();
            if (targets.Count == 0)
            {
                MessageBox.Show("Chưa chọn thiết bị!");
                return;
            }

            using var dialog = new OpenFileDialog { Title = "Chọn file" };
            if (dialog.ShowDialog() != DialogResult.OK) return;

            string file = dialog.FileName;
            await RunAdbFileActionAsync(targets, new[] { "push", file, "/sdcard/Download/" }, 30000,
                "Gửi file", "Đang gửi file...", sent => $"Đã gửi file đến {sent}/{targets.Count} thiết bị.");
        }

        private async Task RunAdbFileActionAsync(List<string> targets, string[] args, int timeoutMs,
            string title, string status, Func<int, string> summarize)
        {
            StartProgress();
            SetStatus(status);
            try
            {
                var failed = await Task.WhenAll(targets.Select(async id =>
                {
                    var result = await Task.Run(() => _adb.Capture(new[] { "-s", id }.Concat(args), timeoutMs));
                    if (result.exitCode == 0) return null;

                    string detail = string.IsNullOrWhiteSpace(result.stderr) ? result.stdout : result.stderr;
                    return string.IsNullOrWhiteSpace(detail) ? $"{id} (mã lỗi {result.exitCode})" : $"{id}: {detail.Trim()}";
                }));

                var errors = failed.Where(message => message != null).ToArray();
                string summary = summarize(targets.Count - errors.Length);
                SetStatus(summary);
                MessageBox.Show(errors.Length == 0 ? summary : summary + Environment.NewLine + string.Join(Environment.NewLine, errors),
                    title, MessageBoxButtons.OK, errors.Length == 0 ? MessageBoxIcon.Information : MessageBoxIcon.Warning);
            }
            finally
            {
                StopProgress();
            }
        }

        private async Task ActionRunAdbOnSelectedAsync(string name, string cmd) { var t = GetTargetDevices(); if (t.Count == 0) { MessageBox.Show("Chưa chọn thiết bị!"); return; } SetStatus($"Running {name}..."); StartProgress(); await Task.Run(() => Parallel.ForEach(t, id => _adb.Start($"-s {id} {cmd}"))); StopProgress(); SetStatus($"Done {name}"); }

        private async Task ActionFlashZipSideload()
        {
            using var dialog = new OpenFileDialog
            {
                Title = "Chọn file ZIP để flash qua ADB Sideload",
                Filter = "Zip Files (*.zip)|*.zip"
            };
            if (dialog.ShowDialog() != DialogResult.OK) return;

            StartProgress();
            try
            {
                var result = await Task.Run(() => FlashZipSideloadLogic(dialog.FileName));
                SetStatus(result.message);
                MessageBox.Show(result.message, result.success ? "Flash ZIP" : "ADB Sideload",
                    MessageBoxButtons.OK, result.success ? MessageBoxIcon.Information : MessageBoxIcon.Warning);
            }
            finally
            {
                StopProgress();
                await ReloadDevices();
            }
        }

        private (bool success, string message) FlashZipSideloadLogic(string zipPath)
        {
            if (!File.Exists(zipPath))
                return (false, "Không tìm thấy file ZIP đã chọn.");

            try
            {
                SetStatus("Làm mới ADB để nhận thiết bị sideload...");
                RestartAdbServerSync();

                var sideloadIds = WaitForSideloadDevices(30000);
                if (sideloadIds.Count == 0)
                {
                    return (false, "Không thấy thiết bị nào ở chế độ sideload. Hãy kiểm tra máy đã vào 'Apply update from ADB' chưa.");
                }

                SetStatus($"Dang kich hoat sideload cho {sideloadIds.Count} may...");
                foreach (var sideloadId in sideloadIds)
                {
                    StartSideloadCommandWindow(sideloadId, zipPath);
                    Thread.Sleep(1000);
                }

                return (true, $"Da kich hoat sideload {Path.GetFileName(zipPath)} cho {sideloadIds.Count} may.");
            }
            catch (Exception ex)
            {
                return (false, "Lỗi flash sideload: " + ex.Message);
            }
        }

        private void StartSideloadCommandWindow(string serial, string zipPath)
        {
            string title = $"Sideloading - {serial}";
            string args = $"/c title \"{title}\" && echo Dang xu ly: {serial} && \"{_adb.ExecutablePath}\" -s {serial} sideload \"{zipPath}\" && echo. && echo [XONG - TU DONG DONG SAU 5 GIAY] && timeout /t 5";
            Process.Start(new ProcessStartInfo
            {
                FileName = "cmd.exe",
                Arguments = args,
                UseShellExecute = false,
                CreateNoWindow = false,
                WorkingDirectory = BaseDir
            });
        }

        private void RestartAdbServerSync()
        {
            _adb.Run(new[] { "kill-server" });
            Thread.Sleep(1200);
            _adb.Run(new[] { "start-server" });
        }

        private List<string> WaitForSideloadDevices(int timeoutMs)
        {
            var sw = Stopwatch.StartNew();
            while (sw.ElapsedMilliseconds < timeoutMs)
            {
                var sideloadDevices = GetAdbDevicesByState("sideload");
                if (sideloadDevices.Count > 0)
                    return sideloadDevices.Distinct(StringComparer.OrdinalIgnoreCase).ToList();

                Thread.Sleep(1000);
            }
            return new List<string>();
        }

        private List<string> GetAdbDevicesByState(string state) =>
            AdbClient.ParseAdbDevices(_adb.ReadOutput("devices"), state);

        private void RunBat(string bat, string args = "") { var p = Path.Combine(BaseDir, bat); if (!File.Exists(p)) { MessageBox.Show("Missing: " + p); return; } Process.Start(new ProcessStartInfo { FileName = "cmd.exe", Arguments = $"/c \"\"{p}\" {args}\"", UseShellExecute = false, CreateNoWindow = false, WorkingDirectory = BaseDir }); }

        private void ValidateAdbExists() { if (!File.Exists(_adb.ExecutablePath)) MessageBox.Show("Thiếu adb.exe trong thư mục tool: " + _adb.ExecutablePath); }
        private void TryWarmAdbServer() { try { _adb.Start("start-server"); } catch { } }
        private bool TrySetClipboardText(string text, int timeoutMs = 1200)
        {
            bool ok = false;
            var thread = new Thread(() =>
            {
                try
                {
                    Clipboard.SetDataObject(text ?? "", true, 5, 100);
                    ok = true;
                }
                catch
                {
                    ok = false;
                }
            });
            thread.SetApartmentState(ApartmentState.STA);
            thread.IsBackground = true;
            thread.Start();
            return thread.Join(timeoutMs) && ok;
        }

        private List<string> GetAdbDeviceIds() => GetAdbDevicesByState("device");
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
            string title = $"ToolADB v{AppUpdater.CurrentVersionString} - [{t}]";
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
            var errors = new System.Collections.Concurrent.ConcurrentBag<string>();
            int attempted = 0;
            // Giới hạn 4 luồng + gộp CLEAR/SEND_B64/ENTER vào 1 lệnh adb shell (timeout 5s)
            // để tránh nghẽn adbd/transport và không giữ luồng tới 30-90s khi 1 máy bị treo.
            await Task.Run(() => Parallel.ForEach(t, new ParallelOptions { MaxDegreeOfParallelism = 4 }, (id, s, i) => {
                if (i >= l.Length) return;
                var p = ParseLine(l[i]); var txt = isEmail ? p.email : p.pass;
                if (string.IsNullOrWhiteSpace(txt)) return;
                Interlocked.Increment(ref attempted);
                if (!SendTextViaAutomationIme(id, txt.Trim(), pressEnter: true, out string err))
                    errors.Add($"{id}: {err}");
            }));
            StopProgress();
            if (errors.IsEmpty)
                SetStatus($"Gửi {(isEmail ? "Email" : "Pass")} OK ({attempted} máy).");
            else
                SetStatus($"⚠️ Lỗi gửi {(isEmail ? "Email" : "Pass")} ({errors.Count}/{attempted}): {string.Join(" | ", errors)}");
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
        private const int TextCommandTimeoutMs = 12000;
        private readonly HashSet<string> _imeEnsured = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        private readonly object _imeEnsuredLock = new object();

        private bool EnsureAutomationIme(string deviceId, out string error)
        {
            error = "";
            if (string.IsNullOrWhiteSpace(deviceId)) return true;
            // Đã set IME cho máy này thì tin cache, không probe lại mỗi lần gửi.
            // Nếu IME bị treo/đổi ngoài ý muốn, dùng nút "Set Automation IME" (ForceSetAutomationIme) để force-stop & set lại.
            lock (_imeEnsuredLock) { if (_imeEnsured.Contains(deviceId)) return true; }
            var res = _adb.Capture(new[]
            {
                "-s", deviceId, "shell",
                $"ime set {AutomationImeId} >/dev/null 2>&1 || (ime enable {AutomationImeId} >/dev/null 2>&1 && ime set {AutomationImeId})"
            }, TextCommandTimeoutMs);
            if (res.exitCode != 0)
            {
                error = string.IsNullOrWhiteSpace(res.stderr) ? "Lỗi set IME" : res.stderr.Trim();
                return false;
            }
            lock (_imeEnsuredLock) _imeEnsured.Add(deviceId);
            return true;
        }

        // --- 1. XỬ LÝ ADB & IME ---
        private List<string> ForceSetAutomationIme(IEnumerable<string> deviceIds)
        {
            var failed = new System.Collections.Concurrent.ConcurrentBag<string>();
            if (deviceIds == null) return new List<string>();
            // force-stop trước khi set để ép Android (nhất là máy chỉ có 1 IME mặc định) giải phóng service IME bị treo/stale.
            Parallel.ForEach(deviceIds, new ParallelOptions { MaxDegreeOfParallelism = 4 }, id =>
            {
                var res = _adb.Capture(new[]
                {
                    "-s", id, "shell",
                    $"am force-stop com.cuong.automationime >/dev/null 2>&1; ime set {AutomationImeId} >/dev/null 2>&1 || (ime enable {AutomationImeId} >/dev/null 2>&1 && ime set {AutomationImeId})"
                }, TextCommandTimeoutMs);
                if (res.exitCode == 0)
                {
                    lock (_imeEnsuredLock) _imeEnsured.Add(id);
                }
                else
                {
                    string reason = string.IsNullOrWhiteSpace(res.stderr) ? $"exit {res.exitCode}" : res.stderr.Trim();
                    failed.Add($"{id} ({reason})");
                }
            });
            return failed.ToList();
        }

        private bool SendTextViaAutomationIme(string deviceId, string text, bool pressEnter, out string error)
        {
            if (!EnsureAutomationIme(deviceId, out error))
                return false;

            var b64 = Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes(text ?? ""));
            var cmd = new System.Text.StringBuilder();
            // Dùng -f 0x10000000 (FLAG_RECEIVER_FOREGROUND) để tránh bị delay trong background broadcast queue của Android.
            cmd.Append($"am broadcast -f 0x10000000 -n {AutomationImeReceiverComponent} -a {AutomationImeClearAction} >/dev/null");
            cmd.Append(" && (usleep 30000 2>/dev/null || sleep 0.03 2>/dev/null || true)");
            cmd.Append($" && am broadcast -f 0x10000000 -n {AutomationImeReceiverComponent} -a {AutomationImeSendB64Action} --es {AutomationImeB64ExtraKey} {b64} >/dev/null");
            if (pressEnter)
            {
                cmd.Append(" && (usleep 60000 2>/dev/null || sleep 0.06 2>/dev/null || true)");
                cmd.Append(" && input keyevent 66");
            }

            var res = _adb.Capture(new[] { "-s", deviceId, "shell", cmd.ToString() }, TextCommandTimeoutMs);
            if (res.exitCode != 0)
            {
                // Xóa cache IME để lần sau thử set lại nếu cần, và trả rõ lỗi (vd: quá 12000ms) lên status bar.
                lock (_imeEnsuredLock) _imeEnsured.Remove(deviceId);
                error = string.IsNullOrWhiteSpace(res.stderr) ? $"exit {res.exitCode}" : res.stderr.Trim();
                return false;
            }
            error = "";
            return true;
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
                string err = "";
                bool ok = await Task.Run(() => SendTextViaAutomationIme(id, code, pressEnter: true, out err));

                SetStatus(ok ? $"Sent 2FA to {id}" : $"⚠️ Lỗi gửi 2FA {id}: {err}");
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
            var errors = new System.Collections.Concurrent.ConcurrentBag<string>();
            int attempted = 0;
            await Task.Run(() =>
            {
                // Giới hạn 4 luồng + gộp lệnh + timeout 5s để không nghẽn khi 1 máy bị treo.
                Parallel.For(0, targets.Count, new ParallelOptions { MaxDegreeOfParallelism = 4 }, i =>
                {
                    // Chỉ gửi nếu có mã tương ứng với thứ tự máy
                    if (i < codesToSend.Count && !string.IsNullOrEmpty(codesToSend[i]))
                    {
                        var id = targets[i];
                        var code = codesToSend[i];
                        Interlocked.Increment(ref attempted);
                        if (!SendTextViaAutomationIme(id, code, pressEnter: false, out string err))
                            errors.Add($"{id}: {err}");
                    }
                });
            });

            StopProgress();
            SetStatus(errors.IsEmpty
                ? $"Sent 2FA Done ({attempted} máy)."
                : $"⚠️ Lỗi gửi 2FA ({errors.Count}/{attempted}): {string.Join(" | ", errors)}");
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

        private void BuildMagiskTab(TabPage page)
        {
            page.BackColor = Color.FromArgb(248, 250, 252);

            var pnlTop = new Panel
            {
                Dock = DockStyle.Top,
                Height = 110,
                Padding = new Padding(15, 15, 15, 10),
                BackColor = Color.White
            };

            var lblTitle = new Label
            {
                Text = "Magisk Tools",
                Dock = DockStyle.Top,
                Height = 24,
                Font = new Font("Segoe UI", 11f, FontStyle.Bold),
                ForeColor = Color.FromArgb(30, 41, 59)
            };

            var pnlActions = new TableLayoutPanel
            {
                Dock = DockStyle.Top,
                Height = 48,
                ColumnCount = 2,
                RowCount = 1,
                Margin = new Padding(0),
                Padding = new Padding(0, 8, 0, 0)
            };
            pnlActions.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50F));
            pnlActions.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50F));

            _btnMagiskPhase1 = new Guna2Button
            {
                Text = "Pha 1: Magisk TWRP",
                Dock = DockStyle.Fill,
                FillColor = Color.FromArgb(14, 165, 233),
                ForeColor = Color.White,
                Font = new Font("Segoe UI", 9f, FontStyle.Bold),
                BorderRadius = 6,
                Margin = new Padding(0, 0, 8, 0)
            };
            _btnMagiskPhase1.Click += async (s, e) => await StartMagiskPhase1Async();

            _btnMagiskPhase2 = new Guna2Button
            {
                Text = "Pha 2: Cài Module",
                Dock = DockStyle.Fill,
                FillColor = Color.FromArgb(34, 197, 94),
                ForeColor = Color.White,
                Font = new Font("Segoe UI", 9f, FontStyle.Bold),
                BorderRadius = 6,
                Margin = new Padding(8, 0, 0, 0)
            };
            _btnMagiskPhase2.Click += async (s, e) => await StartMagiskPhase2Async();

            pnlActions.Controls.Add(_btnMagiskPhase1, 0, 0);
            pnlActions.Controls.Add(_btnMagiskPhase2, 1, 0);

            var lblHint = new Label
            {
                Text = "Pha 1 dùng cho máy đang ở Android để reboot recovery, cài Magisk.zip và chép magisk.db. Pha 2 cài các module zip qua su rồi reboot.",
                Dock = DockStyle.Fill,
                Font = new Font("Segoe UI", 9f),
                ForeColor = Color.FromArgb(71, 85, 105)
            };

            pnlTop.Controls.Add(lblHint);
            pnlTop.Controls.Add(pnlActions);
            pnlTop.Controls.Add(lblTitle);

            _txtMagiskLog = new RichTextBox
            {
                Dock = DockStyle.Fill,
                ReadOnly = true,
                BackColor = Color.FromArgb(15, 23, 42),
                ForeColor = Color.FromArgb(34, 197, 94),
                Font = new Font("Consolas", 9f),
                BorderStyle = BorderStyle.None
            };

            var pnlLog = new Panel
            {
                Dock = DockStyle.Fill,
                Padding = new Padding(15, 10, 15, 15),
                BackColor = Color.FromArgb(248, 250, 252)
            };
            pnlLog.Controls.Add(_txtMagiskLog);

            page.Controls.Add(pnlLog);
            page.Controls.Add(pnlTop);
        }

        private void AppendMagiskLog(string message)
        {
            if (_txtMagiskLog == null) return;
            string line = $"{DateTime.Now:HH:mm:ss} {message}{Environment.NewLine}";
            if (_txtMagiskLog.InvokeRequired)
            {
                _txtMagiskLog.Invoke(new Action<string>(AppendMagiskLog), message);
                return;
            }
            _txtMagiskLog.AppendText(line);
            _txtMagiskLog.SelectionStart = _txtMagiskLog.TextLength;
            _txtMagiskLog.ScrollToCaret();
        }

        private void SetMagiskButtonsEnabled(bool enabled)
        {
            if (_btnMagiskPhase1 == null || _btnMagiskPhase2 == null) return;
            if (InvokeRequired)
            {
                Invoke(new Action<bool>(SetMagiskButtonsEnabled), enabled);
                return;
            }
            _btnMagiskPhase1.Enabled = enabled;
            _btnMagiskPhase2.Enabled = enabled;
        }

        private async Task StartMagiskPhase1Async()
        {
            using var zipDialog = new OpenFileDialog
            {
                Title = "Chọn file Magisk.zip",
                Filter = "Zip Files (*.zip)|*.zip"
            };
            if (zipDialog.ShowDialog() != DialogResult.OK) return;

            using var dbDialog = new OpenFileDialog
            {
                Title = "Chọn file magisk.db",
                Filter = "DB Files (*.db)|*.db|All Files (*.*)|*.*"
            };
            if (dbDialog.ShowDialog() != DialogResult.OK) return;

            var devices = GetTargetDevices();
            if (devices.Count == 0)
            {
                MessageBox.Show("Không tìm thấy thiết bị Android nào!");
                return;
            }

            AppendMagiskLog(new string('=', 50));
            AppendMagiskLog($"BẮT ĐẦU PHA 1 CHO {devices.Count} THIẾT BỊ");
            SetMagiskButtonsEnabled(false);
            StartProgress();

            try
            {
                await Task.Run(() => Parallel.ForEach(devices, new ParallelOptions { MaxDegreeOfParallelism = MagiskMaxWorkers }, dev =>
                {
                    MagiskPhase1Worker(dev, zipDialog.FileName, dbDialog.FileName);
                }));
                AppendMagiskLog("--- KẾT THÚC PHA 1 ---");
            }
            finally
            {
                StopProgress();
                SetMagiskButtonsEnabled(true);
                await ReloadDevices();
            }
        }

        private async Task StartMagiskPhase2Async()
        {
            using var moduleDialog = new OpenFileDialog
            {
                Title = "Giữ Ctrl để chọn nhiều module ZIP",
                Filter = "Zip Files (*.zip)|*.zip",
                Multiselect = true
            };
            if (moduleDialog.ShowDialog() != DialogResult.OK || moduleDialog.FileNames.Length == 0) return;

            var devices = GetTargetDevices();
            if (devices.Count == 0)
            {
                MessageBox.Show("Không tìm thấy thiết bị Android nào!");
                return;
            }

            AppendMagiskLog(new string('=', 50));
            AppendMagiskLog($"BẮT ĐẦU PHA 2 CHO {devices.Count} THIẾT BỊ");
            AppendMagiskLog("Các module sẽ cài: " + string.Join(", ", moduleDialog.FileNames.Select(Path.GetFileName)));
            SetMagiskButtonsEnabled(false);
            StartProgress();

            try
            {
                await Task.Run(() => Parallel.ForEach(devices, new ParallelOptions { MaxDegreeOfParallelism = MagiskMaxWorkers }, dev =>
                {
                    MagiskPhase2Worker(dev, moduleDialog.FileNames);
                }));
                AppendMagiskLog("--- KẾT THÚC PHA 2 ---");
            }
            finally
            {
                StopProgress();
                SetMagiskButtonsEnabled(true);
                await ReloadDevices();
            }
        }

        private bool WaitForRecovery(string deviceId)
        {
            AppendMagiskLog($"[{deviceId}] Đang đợi máy vào TWRP...");
            for (int i = 0; i < 15; i++)
            {
                var recoveryDevices = GetAdbDevicesByState("recovery");
                if (recoveryDevices.Any(x => string.Equals(x, deviceId, StringComparison.OrdinalIgnoreCase)))
                {
                    Thread.Sleep(3000);
                    return true;
                }
                Thread.Sleep(2000);
            }
            return false;
        }

        private void MagiskPhase1Worker(string devId, string zipPath, string dbPath)
        {
            try
            {
                AppendMagiskLog($"[{devId}] BẮT ĐẦU PHA 1: Reboot vào Recovery...");
                _adb.RunOnDevice(devId, "reboot", "recovery");

                if (!WaitForRecovery(devId))
                {
                    AppendMagiskLog($"[{devId}] [!] Lỗi: Không thể vào chế độ Recovery.");
                    return;
                }

                AppendMagiskLog($"[{devId}] -> Đang đẩy và cài Magisk.zip...");
                _adb.RunOnDevice(devId, "push", zipPath, "/tmp/Magisk.zip");
                _adb.RunOnDevice(devId, "shell", "twrp", "install", "/tmp/Magisk.zip");

                AppendMagiskLog($"[{devId}] -> Đang tiêm magisk.db...");
                _adb.RunOnDevice(devId, "shell", "twrp", "mount", "/data");
                _adb.RunOnDevice(devId, "shell", "mkdir", "-p", "/data/adb");
                _adb.RunOnDevice(devId, "push", dbPath, "/data/adb/magisk.db");
                _adb.RunOnDevice(devId, "shell", "chmod", "600", "/data/adb/magisk.db");

                AppendMagiskLog($"[{devId}] -> HOÀN TẤT! Đang reboot về Android.");
                _adb.RunOnDevice(devId, "shell", "twrp", "reboot", "system");
            }
            catch (Exception ex)
            {
                AppendMagiskLog($"[{devId}] [!] Lỗi hệ thống: {ex.Message}");
            }
        }

        private void MagiskPhase2Worker(string devId, string[] modulePaths)
        {
            try
            {
                AppendMagiskLog($"[{devId}] BẮT ĐẦU PHA 2: Cài {modulePaths.Length} module...");
                int success = 0;

                foreach (var modPath in modulePaths)
                {
                    string modName = Path.GetFileName(modPath);
                    string tmpPath = $"/data/local/tmp/{modName}";

                    AppendMagiskLog($"[{devId}] -> Đẩy {modName}...");
                    _adb.RunOnDevice(devId, "push", modPath, tmpPath);

                    AppendMagiskLog($"[{devId}] -> Cài đặt qua su...");
                    var res = _adb.Capture(new[] { "-s", devId, "shell", "su", "-c", $"magisk --install-module '{tmpPath}'" });
                    _adb.RunOnDevice(devId, "shell", "rm", tmpPath);

                    if (res.exitCode == 0 && res.stdout.Contains("Done"))
                    {
                        success++;
                    }
                    else
                    {
                        string msg = string.IsNullOrWhiteSpace(res.stderr) ? res.stdout.Trim() : res.stderr.Trim();
                        AppendMagiskLog($"[{devId}] [!] Lỗi cài {modName}: {msg}");
                    }
                }

                if (success > 0)
                {
                    AppendMagiskLog($"[{devId}] HOÀN TẤT cài {success}/{modulePaths.Length}. Đang reboot!");
                    _adb.RunOnDevice(devId, "reboot");
                }
            }
            catch (Exception ex)
            {
                AppendMagiskLog($"[{devId}] [!] Lỗi hệ thống: {ex.Message}");
            }
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

        #endregion
        private static void SaveStorageFileAsync(MailVault vault)
        {
            var lines = CollectVaultLines(vault);

            Task.Run(() =>
            {
                lock (vault.SaveLock)
                {
                    try { File.WriteAllText(vault.FilePath, string.Join(Environment.NewLine, lines)); } catch { }
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
