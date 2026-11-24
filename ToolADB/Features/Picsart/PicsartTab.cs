using ToolAdb.UI;

namespace ToolAdb.Features.Picsart
{
    internal sealed class PicsartTab : TabPage
    {
        // Bảng màu: xám = chỉ đọc, xanh dương = mở app, tím/cam = ép gói, hổ phách/đỏ = thao tác xóa.
        private static readonly Color Slate = Color.FromArgb(71, 85, 105);
        private static readonly Color Blue = Color.FromArgb(37, 99, 235);
        private static readonly Color Teal = Color.FromArgb(14, 116, 144);
        private static readonly Color Emerald = Color.FromArgb(5, 150, 105);
        private static readonly Color Violet = Color.FromArgb(124, 58, 237);
        private static readonly Color Fuchsia = Color.FromArgb(192, 38, 211);
        private static readonly Color Orange = Color.FromArgb(234, 88, 12);
        private static readonly Color Amber = Color.FromArgb(180, 83, 9);
        private static readonly Color Red = Color.FromArgb(220, 38, 38);

        private const int ButtonWidth = 160;
        private const int ButtonHeight = 40;

        private readonly PicsartService _service;
        private readonly Func<List<string>> _getSelectedDevices;
        private readonly FlowLayoutPanel _actions;
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
            AddAction("Pro 64.99 (500Cr)", PicsartAction.ProMonthly, Violet, picsartIcon, "Pro tháng 64.99 TRY (500 credit)",
                "Thao tác sẽ xóa token thuê bao cũ, đổi device id, loại bỏ gói 100 credit / 599.99 TRY và mở thẳng gói Pro 1 tháng 64.99 TRY (500 credit - subscription_pro_monthly_50).");
            AddAction("Ultra (1500Cr)", PicsartAction.UltraMonthly, Orange, picsartIcon, "Ultra tháng (1500 credit)",
                "Thao tác sẽ xóa token thuê bao cũ và ép cấu hình bảng giá sang gói Ultra 1 tháng (1500 credit).");
            AddAction("Pro 132.5 (NoTrial)", PicsartAction.Pro1YearlyNoTrial, Emerald, picsartIcon, "Pro Năm 132.50 TRY (bỏ trial)",
                "Thao tác sẽ xóa token thuê bao cũ, bỏ trial 7 ngày của subscription_pro_1_yearly và mở thẳng gói Pro Năm 132.50 TRY.");
            AddAction("Pro 155.99 (Base)", PicsartAction.ProYearlyNoTrial, Blue, picsartIcon, "Pro Năm 155.99 TRY (base không trial)",
                "Thao tác sẽ xóa token thuê bao cũ và mở thẳng gói Pro Năm 155.99 TRY (subscription_pro_yearly_no_trial).");
            AddAction("Pro 78/155 TRY", PicsartAction.Pro4YearlyIntro, Violet, picsartIcon, "Pro Năm 78/155 TRY",
                "Thao tác sẽ xóa token thuê bao cũ, ép gói Pro Năm Intro (subscription_pro_4_yearly) và ghim offer intro-price (TRY 78.00/năm × 2 năm nếu acc còn đủ điều kiện).");
            AddAction("BF 50% 120 TRY", PicsartAction.ProBf2026Yearly, Fuchsia, picsartIcon, "Pro Năm BF 50% (120 TRY)",
                "Thao tác sẽ xóa token thuê bao cũ, ép gói BF2026 (sub_pro_yearly_250_winback_bf2026) và ghim offer discount-50 (TRY 120.00/năm nếu acc đủ điều kiện, gốc 239.99).");
            AddAction("Trial 7N (132.5)", PicsartAction.Pro1YearlyTrial, Teal, picsartIcon, "Pro Trial 7N (132.5 TRY)",
                "Thao tác sẽ xóa token thuê bao cũ và ép gói Pro Năm Trial 7 ngày (subscription_pro_1_yearly - gia hạn sau trial TRY 132.50/năm).");
            AddAction("🔄 Reset giữ login", PicsartAction.ResetKeepLogin, Amber, null, "Reset giữ đăng nhập",
                "Thao tác sẽ đổi ID ứng dụng và sửa hoặc xóa cache thuê bao cục bộ.");
            AddAction("🧹 Xóa dữ liệu", PicsartAction.Reset, Red, null, "Xóa dữ liệu Picsart",
                "Thao tác sẽ xóa dữ liệu cục bộ của Picsart, gồm phiên đăng nhập và dữ liệu ứng dụng (giữ cấu hình bảng giá).");

            _summary = TabUi.SummaryLabel("Tích thiết bị ở danh sách bên trái. Kiểm tra cấu hình đọc dữ liệu cục bộ trên máy.");
            _results = TabUi.ResultsGrid("Kết quả");

            var layout = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 3, Padding = new Padding(8) };
            layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            layout.Controls.Add(_actions, 0, 0);
            layout.Controls.Add(_summary, 0, 1);
            layout.Controls.Add(_results, 0, 2);
            Controls.Add(layout);
        }

        private void AddAction(string text, PicsartAction action, Color color, Image icon, string tooltip, string confirmation = null)
        {
            var button = TabUi.ActionButton(text, color, ButtonWidth, icon, ButtonHeight);
            _toolTip.SetToolTip(button, tooltip);
            button.Click += async (sender, args) => await RunAsync(action, confirmation);
            _actions.Controls.Add(button);
        }

        private async Task RunAsync(PicsartAction action, string confirmation)
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

            _actions.Enabled = false;
            try
            {
                _results.Rows.Clear();
                foreach (string serial in targets)
                    _results.Rows.Add(serial, "⏳ Đang chờ…");
                _summary.Text = $"Đang xử lý {targets.Length} thiết bị…";

                var results = new (bool success, string message)[targets.Length];
                // Giống RunActionOnSelectedAsync gốc: mọi máy chạy đồng thời (Task.Run + Task.WhenAll),
                // máy nào xong thì cập nhật dòng của máy đó ngay.
                await Task.WhenAll(Enumerable.Range(0, targets.Length).Select(i => Task.Run(() =>
                {
                    try { results[i] = (true, _service.Run(targets[i], action)); }
                    catch (Exception ex) { results[i] = (false, "❌ Lỗi: " + ex.Message); }
                    SetRowMessage(i, results[i].message);
                })));

                if (IsDisposed) return;
                _summary.Text = $"Hoàn tất {results.Count(result => result.success)}/{targets.Length} thiết bị.";
            }
            finally
            {
                if (!IsDisposed) _actions.Enabled = true;
            }
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
