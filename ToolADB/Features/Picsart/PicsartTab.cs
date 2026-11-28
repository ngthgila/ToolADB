using ToolAdb.UI;

namespace ToolAdb.Features.Picsart
{
    internal sealed class PicsartTab : TabPage
    {
        // Bảng màu: xám = chỉ đọc, xanh dương = mở app, tím/lục/lam = test gói, hổ phách/đỏ = reset.
        private static readonly Color Slate = Color.FromArgb(71, 85, 105);
        private static readonly Color Blue = Color.FromArgb(37, 99, 235);
        private static readonly Color Teal = Color.FromArgb(14, 116, 144);
        private static readonly Color Emerald = Color.FromArgb(5, 150, 105);
        private static readonly Color Violet = Color.FromArgb(124, 58, 237);
        private static readonly Color Fuchsia = Color.FromArgb(192, 38, 211);
        private static readonly Color Amber = Color.FromArgb(180, 83, 9);
        private static readonly Color Red = Color.FromArgb(220, 38, 38);

        private const int ButtonWidth = 160;
        private const int ButtonHeight = 40;

        private readonly PicsartService _service;
        private readonly Func<List<string>> _getSelectedDevices;
        private readonly FlowLayoutPanel _actions;
        private readonly FlowLayoutPanel _testBar;
        private readonly ComboBox _testPresetCombo;
        private readonly DataGridView _results;
        private readonly Label _summary;
        private readonly ToolTip _toolTip = new();

        internal PicsartTab(PicsartService service, Func<List<string>> getSelectedDevices)
        {
            _service = service;
            _getSelectedDevices = getSelectedDevices;
            Text = "Picsart";
            BackColor = Color.White;

            _actions = new FlowLayoutPanel
            {
                Dock = DockStyle.Fill, AutoSize = true, WrapContents = true,
                Padding = new Padding(4)
            };
            var picsartIcon = TabUi.LoadAppIcon("picsart.png");
            AddAction("🔍 Kiểm tra", PicsartAction.Inspect, Slate, null, "Kiểm tra cấu hình");
            AddAction("💳 Mở bảng giá", PicsartAction.OpenPricing, Blue, null, "Mở bảng giá Picsart");
            AddAction("Pro 78/155 (4Y)", PicsartAction.Pro4YearlyIntro, Violet, picsartIcon,
                "Pro Năm 78/155 TRY (subscription_pro_4_yearly - đủ 500Cr/tháng × 1 năm)",
                "Thao tác sẽ xóa token thuê bao cũ, ép gói Pro Năm Intro (subscription_pro_4_yearly, intro-price: 78 TRY nếu còn intro, hoặc 155.99 TRY gốc).");
            AddPresetButton("Pro 78/155 (Intro)", "tr_pro_78", Violet, picsartIcon,
                "Test Thổ (TRY): Mở thẳng picsart_pro_yearly_intro [intro-price] (78.00 -> 155.99 TRY/năm)",
                "Thao tác sẽ xóa token cũ và mở thẳng checkout sheet picsart_pro_yearly_intro (intro-price: 78 TRY / 155.99 TRY).");
            AddPresetButton("Pro 109.19 (-30%)", "tr_pro_109", Teal, picsartIcon,
                "Test Thổ (TRY): Mở thẳng picsart_pro_yearly_promo [30-percent-discount] (109.19 TRY/năm)",
                "Thao tác sẽ xóa token cũ và mở thẳng checkout sheet picsart_pro_yearly_promo (30-percent-discount: TRY 109.19/năm).");
            AddAction("Pro 132.5 (NoTrial)", PicsartAction.Pro1YearlyNoTrial, Emerald, picsartIcon,
                "Pro Năm 132.50 TRY (bỏ trial)",
                "Thao tác sẽ xóa token thuê bao cũ, bỏ trial 7 ngày của subscription_pro_1_yearly và mở thẳng gói Pro Năm 132.50 TRY.");
            AddAction("Pro 155.99 (Base)", PicsartAction.ProYearlyNoTrial, Blue, picsartIcon,
                "Pro Năm 155.99 TRY (base không trial)",
                "Thao tác sẽ xóa token thuê bao cũ và mở thẳng gói Pro Năm 155.99 TRY (subscription_pro_yearly_no_trial).");
            AddAction("BF 50% 120 TRY", PicsartAction.ProBf2026Yearly, Fuchsia, picsartIcon,
                "Pro Năm BF 50% (120 TRY)",
                "Thao tác sẽ xóa token thuê bao cũ, ép gói BF2026 (sub_pro_yearly_250_winback_bf2026) và ghim offer discount-50 (TRY 120.00/năm nếu acc đủ điều kiện, gốc 239.99).");
            AddPresetButton("Gold 24.99 TRY/T", "tr_gold_p8", Emerald, picsartIcon,
                "Test Thổ (TRY): Mở thẳng subscription_picsart_monthly_p8 (TRY 24.99/tháng)",
                "Thao tác sẽ xóa token cũ và mở thẳng checkout sheet Gold 24.99 TRY/tháng (subscription_picsart_monthly_p8).");
            AddAction("🔄 Reset giữ login", PicsartAction.ResetKeepLogin, Amber, null, "Reset giữ đăng nhập",
                "Thao tác sẽ đổi ID ứng dụng và sửa hoặc xóa cache thuê bao cục bộ.");
            AddAction("🧹 Xóa dữ liệu", PicsartAction.Reset, Red, null, "Xóa dữ liệu Picsart",
                "Thao tác sẽ xóa dữ liệu cục bộ của Picsart, gồm phiên đăng nhập và dữ liệu ứng dụng (giữ cấu hình bảng giá).");

            _testBar = new FlowLayoutPanel
            {
                Dock = DockStyle.Fill, AutoSize = true, WrapContents = true,
                Padding = new Padding(6, 4, 6, 4),
                BackColor = Color.FromArgb(248, 250, 252)
            };
            var lblPreset = new Label
            {
                Text = "Test SKU Thổ (TRY):",
                AutoSize = true,
                Font = new Font("Segoe UI Semibold", 9.5f),
                ForeColor = Color.FromArgb(30, 41, 59),
                Margin = new Padding(2, 8, 8, 4)
            };
            _testPresetCombo = new ComboBox
            {
                DropDownStyle = ComboBoxStyle.DropDownList,
                Width = 340,
                Font = new Font("Segoe UI", 9.5f),
                Margin = new Padding(0, 5, 8, 4)
            };
            _testPresetCombo.Items.AddRange(PicsartService.TestPresets);
            if (_testPresetCombo.Items.Count > 0) _testPresetCombo.SelectedIndex = 0;

            var btnRunPreset = TabUi.ActionButton("🚀 Mở Test SKU", Teal, 155, null, 34);
            _toolTip.SetToolTip(btnRunPreset, "Xóa token cũ, đổi device_id và mở thẳng deeplink checkout cho SKU/Offer đang chọn");
            btnRunPreset.Click += async (_, _) => await RunSelectedPresetAsync();

            _testBar.Controls.Add(lblPreset);
            _testBar.Controls.Add(_testPresetCombo);
            _testBar.Controls.Add(btnRunPreset);

            _summary = TabUi.SummaryLabel("Tích thiết bị ở danh sách bên trái. Kiểm tra cấu hình đọc dữ liệu cục bộ trên máy.");
            _results = TabUi.ResultsGrid("Kết quả");

            var layout = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 4, Padding = new Padding(8) };
            layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            layout.Controls.Add(_actions, 0, 0);
            layout.Controls.Add(_testBar, 0, 1);
            layout.Controls.Add(_summary, 0, 2);
            layout.Controls.Add(_results, 0, 3);
            Controls.Add(layout);
        }

        private void AddAction(string text, PicsartAction action, Color color, Image icon, string tooltip, string confirmation = null)
        {
            var button = TabUi.ActionButton(text, color, ButtonWidth, icon, ButtonHeight);
            _toolTip.SetToolTip(button, tooltip);
            button.Click += async (_, _) => await ExecuteTargetsAsync(confirmation, serial => _service.Run(serial, action));
            _actions.Controls.Add(button);
        }

        private void AddPresetButton(string text, string presetKey, Color color, Image icon, string tooltip, string confirmation = null)
        {
            PicsartTestPreset preset = PicsartService.FindTestPreset(presetKey);
            if (preset == null) return;
            var button = TabUi.ActionButton(text, color, ButtonWidth, icon, ButtonHeight);
            _toolTip.SetToolTip(button, tooltip);
            button.Click += async (_, _) => await ExecuteTargetsAsync(confirmation, serial => _service.RunTestPreset(serial, preset));
            _actions.Controls.Add(button);
        }

        private async Task RunSelectedPresetAsync()
        {
            if (_testPresetCombo.SelectedItem is not PicsartTestPreset preset) return;
            string offerInfo = string.IsNullOrEmpty(preset.OfferId) ? "" : $"\nOffer: {preset.OfferId}";
            string confirm = $"Mở test gói: {preset.Label}\nSKU: {preset.Sku}{offerInfo}\n(Tool sẽ tự xóa token gói cũ & đổi device_id trước khi mở)";
            await ExecuteTargetsAsync(confirm, serial => _service.RunTestPreset(serial, preset));
        }

        private async Task ExecuteTargetsAsync(string confirmation, Func<string, string> runner)
        {
            if (!_actions.Enabled) return;

            var targets = _getSelectedDevices().Distinct(StringComparer.Ordinal).ToArray();
            if (targets.Length == 0)
            {
                _summary.Text = "Hãy tích ít nhất một thiết bị ở danh sách bên trái.";
                return;
            }

            if (confirmation != null && MessageBox.Show(this,
                $"{confirmation}{Environment.NewLine}Tiếp tục trên {targets.Length} thiết bị đã chọn?",
                "Picsart", MessageBoxButtons.YesNo, MessageBoxIcon.Warning, MessageBoxDefaultButton.Button2) != DialogResult.Yes)
                return;

            SetUiEnabled(false);
            try
            {
                _results.Rows.Clear();
                foreach (string serial in targets)
                    _results.Rows.Add(serial, "⏳ Đang chờ…");
                _summary.Text = $"Đang xử lý {targets.Length} thiết bị…";

                var results = new (bool success, string message)[targets.Length];
                await Task.WhenAll(Enumerable.Range(0, targets.Length).Select(i => Task.Run(() =>
                {
                    try { results[i] = (true, runner(targets[i])); }
                    catch (Exception ex) { results[i] = (false, "❌ Lỗi: " + ex.Message); }
                    SetRowMessage(i, results[i].message);
                })));

                if (IsDisposed) return;
                _summary.Text = $"Hoàn tất {results.Count(result => result.success)}/{targets.Length} thiết bị.";
            }
            finally
            {
                if (!IsDisposed) SetUiEnabled(true);
            }
        }

        private void SetUiEnabled(bool enabled)
        {
            _actions.Enabled = enabled;
            _testBar.Enabled = enabled;
        }

        private void SetRowMessage(int row, string message)
        {
            if (IsDisposed || !IsHandleCreated) return;
            BeginInvoke(new Action(() =>
            {
                if (!IsDisposed && row < _results.Rows.Count) _results.Rows[row].Cells[1].Value = message;
            }));
        }
    }
}
