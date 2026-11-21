using ToolAdb.UI;

namespace ToolAdb.Features.Grok
{
    internal sealed class GrokTab : TabPage
    {
        private static readonly Color Slate = Color.FromArgb(71, 85, 105);
        private static readonly Color Blue = Color.FromArgb(37, 99, 235);
        private static readonly Color Amber = Color.FromArgb(180, 83, 9);
        private static readonly Color Emerald = Color.FromArgb(5, 150, 105);
        private static readonly Color Violet = Color.FromArgb(124, 58, 237);
        private static readonly Color Orange = Color.FromArgb(234, 88, 12);
        private static readonly Color Rose = Color.FromArgb(190, 24, 93);

        private const int ButtonWidth = 172;
        private const int ButtonHeight = 40;

        private readonly GrokService _service;
        private readonly Func<List<string>> _getSelectedDevices;
        private readonly FlowLayoutPanel _actions;
        private readonly DataGridView _results;
        private readonly Label _summary;
        private readonly ToolTip _toolTip = new();

        internal GrokTab(GrokService service, Func<List<string>> getSelectedDevices)
        {
            _service = service;
            _getSelectedDevices = getSelectedDevices;
            Text = "Grok";
            BackColor = Color.White;

            _actions = new FlowLayoutPanel
            {
                Dock = DockStyle.Fill, AutoSize = true, WrapContents = true,
                Padding = new Padding(4)
            };

            var grokIcon = TabUi.LoadAppIcon("grok.png");

            AddAction("🔍 Kiểm tra", GrokAction.Inspect, Slate, null, "Kiểm tra cache & offer của cả Grok và Grok Bot trong sku_details.db");
            AddAction("💳 Mở bảng giá Grok", GrokAction.OpenPricing, Blue, null, "Mở trang gói đăng ký Grok (https://grok.com/plans)");
            AddAction("🤖 Mở Grok Bot", GrokAction.OpenGrokBot, Blue, null, "Mở ứng dụng Grok Bot (ai.x.grok.bot) để tải cache gói thanh toán");
            AddAction("⭐ Ghim all ngon nhất", GrokAction.PinAllBest, Amber, grokIcon,
                "Ghim cùng lúc ưu đãi ngon nhất cho mọi gói đã cache của cả Grok & Grok Bot (Plus -30%x3T, Premium 1 trả 3T, Super -50% 1T, Ultra 1 trả 3T)",
                "Thao tác sẽ ghim ưu đãi tháng tốt nhất cho tất cả SKU đã có trong sku_details.db (cả Grok & Grok Bot) và mở lại app.");
            AddAction("📦 Về giá gốc", GrokAction.RestoreBase, Slate, TabUi.LoadAppIcon("base.png"),
                "Khôi phục toàn bộ offer gốc cho cả Grok và Grok Bot từ bản sao lưu snapshot.",
                "Thao tác sẽ khôi phục lại toàn bộ offer gốc trong sku_details.db cho cả Grok và Grok Bot.");

            // Plus (x.subscription.premium)
            AddAction("Plus -30% ×3T", GrokAction.Plus30Off3Mo, Emerald, grokIcon,
                "Plus (x.subscription.premium): Giảm 30% trong 3 tháng đầu (x-subscription-benefit-discount-30pct)",
                "Ghim gói Plus giảm 30% trong 3 tháng đầu (vd 265.99 TRY/tháng × 3) và khởi động lại Grok.");
            AddAction("Plus -25% ×6T", GrokAction.Plus25Off6Mo, Emerald, grokIcon,
                "Plus (x.subscription.premium): Giảm 25% trong 6 tháng đầu (x-subscription-benefit-plus-launch-25off-6mo)",
                "Ghim gói Plus giảm 25% trong 6 tháng đầu (vd 284.99 TRY/tháng × 6) và khởi động lại Grok.");

            // Premium (x.subscription.plus)
            AddAction("Prem 1 trả 3T", GrokAction.PremiumPay1Get3, Violet, grokIcon,
                "Premium (x.subscription.plus): Trả tiền 1 tháng dùng 3 tháng (x-subscription-benefit-test-pay1get3-premium-ultra)",
                "Ghim gói Premium trả 1 tháng dùng 3 tháng (vd 1,749.99 TRY/3 tháng) và khởi động lại Grok.");
            AddAction("Prem BF 1 tặng 1", GrokAction.PremiumBfPay1Get2, Violet, grokIcon,
                "Premium (x.subscription.plus): Black Friday trả 1 tháng dùng 2 tháng (x-subscription-benefit-bf2026-pay1get2-premium)",
                "Ghim gói Premium Black Friday 1 tặng 1 tháng (vd 1,749.99 TRY/2 tháng) và khởi động lại Grok.");
            AddAction("Prem -50% T1", GrokAction.Premium50Off1Mo, Violet, grokIcon,
                "Premium (x.subscription.plus): Giảm 50% tháng đầu (x-subscription-benefit-launch-50off-1mo)",
                "Ghim gói Premium giảm 50% tháng đầu (vd 875.00 TRY) và khởi động lại Grok.");
            AddAction("Prem -30% ×3T", GrokAction.Premium30Off3Mo, Violet, grokIcon,
                "Premium (x.subscription.plus): Giảm 30% trong 3 tháng đầu (x-subscription-benefit-discount-30pct)",
                "Ghim gói Premium giảm 30% trong 3 tháng đầu (vd 1,224.99 TRY/tháng × 3) và khởi động lại Grok.");

            // Super (x.subscription.super)
            AddAction("Super -50% T1", GrokAction.Super50Off1Mo, Orange, grokIcon,
                "Super (x.subscription.super): Giảm 50% tháng đầu (x-subscription-benefit-launch-50off-1mo)",
                "Ghim gói Super giảm 50% tháng đầu và khởi động lại Grok.");
            AddAction("Super -30% ×3T", GrokAction.Super30Off3Mo, Orange, grokIcon,
                "Super (x.subscription.super): Giảm 30% trong 3 tháng đầu (x-subscription-benefit-discount-30pct)",
                "Ghim gói Super giảm 30% trong 3 tháng đầu và khởi động lại Grok.");

            // Ultra (x.subscription.ultra)
            AddAction("Ultra 1 trả 3T", GrokAction.UltraPay1Get3, Rose, grokIcon,
                "Ultra (x.subscription.ultra): Trả tiền 1 tháng dùng 3 tháng (x-subscription-benefit-test-pay1get3-premium-ultra)",
                "Ghim gói Ultra trả 1 tháng dùng 3 tháng và khởi động lại Grok.");
            AddAction("Ultra -50% T1", GrokAction.Ultra50Off1Mo, Rose, grokIcon,
                "Ultra (x.subscription.ultra): Giảm 50% tháng đầu (x-subscription-benefit-launch-50off-1mo)",
                "Ghim gói Ultra giảm 50% tháng đầu và khởi động lại Grok.");
            AddAction("Ultra -30% ×3T", GrokAction.Ultra30Off3Mo, Rose, grokIcon,
                "Ultra (x.subscription.ultra): Giảm 30% trong 3 tháng đầu (x-subscription-benefit-discount-30pct)",
                "Ghim gói Ultra giảm 30% trong 3 tháng đầu và khởi động lại Grok.");

            _summary = TabUi.SummaryLabel("Áp dụng cho cả Grok (ai.x.grok) & Grok Bot (ai.x.grok.bot). Mở tab gói cần mua → bấm Upgrade rồi Back để Play tải cache trước khi ghim.");
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

        private void AddAction(string text, GrokAction action, Color color, Image icon, string tooltip, string confirmation = null)
        {
            var button = TabUi.ActionButton(text, color, ButtonWidth, icon, ButtonHeight);
            _toolTip.SetToolTip(button, tooltip);
            button.Click += async (sender, args) => await RunAsync(action, confirmation);
            _actions.Controls.Add(button);
        }

        private async Task RunAsync(GrokAction action, string confirmation)
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
                "Grok", MessageBoxButtons.YesNo, MessageBoxIcon.Warning, MessageBoxDefaultButton.Button2) != DialogResult.Yes)
                return;

            _actions.Enabled = false;
            try
            {
                _results.Rows.Clear();
                foreach (string serial in targets)
                    _results.Rows.Add(serial, "⏳ Đang chờ…");
                _summary.Text = $"Đang xử lý {targets.Length} thiết bị…";

                var results = new (bool success, string message)[targets.Length];
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
