using ToolAdb.UI;

namespace ToolAdb.Features.Picsart
{
    internal sealed class PicsartTab : TabPage
    {
        // Bảng màu: xám = chỉ đọc, xanh dương = mở app, tím/cam = ép gói, hổ phách/đỏ = thao tác xóa.
        private static readonly Color Slate = Color.FromArgb(71, 85, 105);
        private static readonly Color Blue = Color.FromArgb(37, 99, 235);
        private static readonly Color Violet = Color.FromArgb(124, 58, 237);
        private static readonly Color Orange = Color.FromArgb(234, 88, 12);
        private static readonly Color Amber = Color.FromArgb(180, 83, 9);
        private static readonly Color Red = Color.FromArgb(220, 38, 38);

        private readonly PicsartService _service;
        private readonly Func<List<string>> _getSelectedDevices;
        private readonly FlowLayoutPanel _actions;
        private readonly DataGridView _results;
        private readonly Label _summary;

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
            AddAction("🔍 Kiểm tra cấu hình", PicsartAction.Inspect, Slate);
            AddAction("💳 Mở bảng giá", PicsartAction.OpenPricing, Blue);
            AddAction("💎 Pro tháng (500Cr)", PicsartAction.ProMonthly, Violet,
                "Thao tác sẽ xóa token thuê bao cũ, đổi device id và ép cấu hình bảng giá sang gói Pro 1 tháng (500 credit).");
            AddAction("🔥 Ultra tháng (1500Cr)", PicsartAction.UltraMonthly, Orange,
                "Thao tác sẽ xóa token thuê bao cũ và ép cấu hình bảng giá sang gói Ultra 1 tháng (1500 credit).");
            AddAction("🔄 Reset giữ đăng nhập", PicsartAction.ResetKeepLogin, Amber,
                "Thao tác sẽ đổi ID ứng dụng và sửa hoặc xóa cache thuê bao cục bộ.");
            AddAction("🧹 Xóa dữ liệu Picsart", PicsartAction.Reset, Red,
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

        private void AddAction(string text, PicsartAction action, Color color, string confirmation = null)
        {
            var button = TabUi.ActionButton(text, color, 180);
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
                await Task.Run(() => Parallel.For(0, targets.Length, new ParallelOptions { MaxDegreeOfParallelism = 4 }, i =>
                {
                    try { results[i] = (true, _service.Run(targets[i], action)); }
                    catch (Exception ex) { results[i] = (false, "❌ Lỗi: " + ex.Message); }
                }));

                if (IsDisposed) return;
                for (int i = 0; i < results.Length; i++)
                    _results.Rows[i].Cells[1].Value = results[i].message;
                _summary.Text = $"Hoàn tất {results.Count(result => result.success)}/{targets.Length} thiết bị.";
            }
            finally
            {
                if (!IsDisposed) _actions.Enabled = true;
            }
        }
    }
}
