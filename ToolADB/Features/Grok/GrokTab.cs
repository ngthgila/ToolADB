using Guna.UI2.WinForms;
using ToolAdb.UI;

namespace ToolAdb.Features.Grok
{
    internal sealed class GrokTab : TabPage
    {
        private static readonly Color Slate = Color.FromArgb(71, 85, 105);
        private static readonly Color DarkSlate = Color.FromArgb(15, 23, 42);
        private static readonly Color Blue = Color.FromArgb(37, 99, 235);
        private static readonly Color Amber = Color.FromArgb(180, 83, 9);
        private static readonly Color Emerald = Color.FromArgb(5, 150, 105);
        private static readonly Color Violet = Color.FromArgb(124, 58, 237);
        private static readonly Color Orange = Color.FromArgb(234, 88, 12);
        private static readonly Color Rose = Color.FromArgb(190, 24, 93);

        private const int ButtonWidth = 148;
        private const int ButtonHeight = 32;

        private readonly GrokService _service;
        private readonly Func<List<string>> _getSelectedDevices;
        private readonly FlowLayoutPanel _actions;
        private readonly DataGridView _results;
        private readonly Label _summary;
        private readonly ToolTip _toolTip = new();
        private readonly CheckBox _chkGrok;
        private readonly CheckBox _chkGrokBot;
        private readonly Image _grokIcon;
        private readonly Image _grokBotIcon;
        private readonly List<Guna2Button> _promoButtons = new();

        internal GrokTab(GrokService service, Func<List<string>> getSelectedDevices)
        {
            _service = service;
            _getSelectedDevices = getSelectedDevices;
            Text = "Grok";
            BackColor = Color.White;

            _grokIcon = TabUi.LoadAppIcon("grok.png");
            _grokBotIcon = TabUi.LoadAppIcon("grokbot.png");

            // Thanh chọn app mục tiêu với badge icon riêng biệt cho Grok vs Grok Bot
            var targetBar = new FlowLayoutPanel
            {
                Dock = DockStyle.Fill, AutoSize = true, WrapContents = true,
                Padding = new Padding(6, 2, 6, 4),
                BackColor = Color.FromArgb(248, 250, 252)
            };
            var lblTarget = new Label
            {
                Text = "App mục tiêu:",
                AutoSize = true,
                Font = new Font("Segoe UI Semibold", 9.5f),
                ForeColor = Color.FromArgb(30, 41, 59),
                Margin = new Padding(2, 7, 10, 4)
            };
            _chkGrok = CreateTargetCheckBox("Grok (ai.x.grok)", _grokIcon, DarkSlate);
            _chkGrokBot = CreateTargetCheckBox("Grok Bot (ai.x.grok.bot)", _grokBotIcon, Color.FromArgb(30, 64, 175));
            _chkGrok.CheckedChanged += (_, _) => UpdateTargetVisuals();
            _chkGrokBot.CheckedChanged += (_, _) => UpdateTargetVisuals();

            targetBar.Controls.Add(lblTarget);
            targetBar.Controls.Add(WrapTargetControl(_chkGrok, _grokIcon, DarkSlate));
            targetBar.Controls.Add(WrapTargetControl(_chkGrokBot, _grokBotIcon, Color.FromArgb(30, 64, 175)));

            _actions = new FlowLayoutPanel
            {
                Dock = DockStyle.Fill,
                AutoSize = true,
                AutoSizeMode = AutoSizeMode.GrowAndShrink,
                WrapContents = true,
                AutoScroll = true,
                Padding = new Padding(2)
            };

            AddAction("🔍 Kiểm tra", GrokAction.Inspect, Slate, null, "Kiểm tra cache & offer của app đang tích chọn", isPromo: false);
            AddAction("Mở Grok", GrokAction.OpenPricing, Blue, _grokIcon, "Mở thẳng trang bảng giá app Grok (ai.x.grok)", isPromo: false);
            AddAction("Mở Grok Bot", GrokAction.OpenGrokBot, DarkSlate, _grokBotIcon, "Mở app Grok Bot (ai.x.grok.bot)", isPromo: false);
            AddAction("⭐ Ghim tốt nhất", GrokAction.PinAllBest, Amber, null,
                "Ghim cùng lúc ưu đãi ngon nhất cho mọi gói đã cache của app đang chọn (Plus -30%x3T / fallback -25%x6T, Premium 1 trả 3T, Super -50% 1T, Ultra 1 trả 3T)",
                "Thao tác sẽ ghim ưu đãi tháng tốt nhất (Plus tự fallback sang -25% × 6T nếu thiếu -30% × 3T) cho toàn bộ SKU đã cache.\n⚠️ Lưu ý: Nếu trên app Grok vẫn hiện giá gốc (server chưa cấp ưu đãi), thanh toán xong có thể bị từ chối STORE_PROOF_OFFER_MISMATCH.", isPromo: true);
            AddAction("📦 Về giá gốc", GrokAction.RestoreBase, Slate, TabUi.LoadAppIcon("base.png"),
                "Khôi phục toàn bộ offer gốc cho app đang chọn từ bản sao lưu snapshot.",
                "Thao tác sẽ khôi phục lại toàn bộ offer gốc trong sku_details.db cho app đang chọn.", isPromo: false);
            AddAction("🔄 Xóa cache Play", GrokAction.ClearPlayCache, Slate, TabUi.LoadAppIcon("refresh.png"),
                "Xóa cache sku_details.db của app đang chọn để Play Store tải lại đầy đủ toàn bộ offer gốc (dùng khi chuyển gói khác báo không tìm thấy offer).",
                "Thao tác sẽ xóa cache SKU trong sku_details.db của app đang chọn. Sau đó bạn mở app → bấm Upgrade (rồi Back lại) để Play tải lại toàn bộ offer.", isPromo: false);

            // Plus (x.subscription.premium)
            AddAction("Plus -30% ×3T", GrokAction.Plus30Off3Mo, Emerald, null,
                "Plus (x.subscription.premium): Giảm 30% trong 3 tháng đầu (x-subscription-benefit-discount-30pct)",
                "Ghim gói Plus giảm 30% trong 3 tháng đầu (vd 265.99 TRY/tháng × 3) và khởi động lại app.", isPromo: true);
            AddAction("Plus -25% ×6T", GrokAction.Plus25Off6Mo, Emerald, null,
                "Plus (x.subscription.premium): Giảm 25% trong 6 tháng đầu (x-subscription-benefit-plus-launch-25off-6mo)",
                "Ghim gói Plus giảm 25% trong 6 tháng đầu (vd 284.99 TRY/tháng × 6) và khởi động lại app.", isPromo: true);

            // Premium (x.subscription.plus)
            AddAction("Prem 1 trả 3T", GrokAction.PremiumPay1Get3, Violet, null,
                "Premium (x.subscription.plus): Trả tiền 1 tháng dùng 3 tháng (x-subscription-benefit-test-pay1get3-premium-ultra)",
                "Ghim gói Premium trả 1 tháng dùng 3 tháng (vd 1,749.99 TRY/3 tháng) và khởi động lại app.", isPromo: true);
            AddAction("Prem BF 1 tặng 1", GrokAction.PremiumBfPay1Get2, Violet, null,
                "Premium (x.subscription.plus): Black Friday trả 1 tháng dùng 2 tháng (x-subscription-benefit-bf2026-pay1get2-premium)",
                "Ghim gói Premium Black Friday 1 tặng 1 tháng (vd 1,749.99 TRY/2 tháng) và khởi động lại app.", isPromo: true);
            AddAction("Prem -50% T1", GrokAction.Premium50Off1Mo, Violet, null,
                "Premium (x.subscription.plus): Giảm 50% tháng đầu (x-subscription-benefit-launch-50off-1mo)",
                "Ghim gói Premium giảm 50% tháng đầu (vd 875.00 TRY) và khởi động lại app.", isPromo: true);
            AddAction("Prem -30% ×3T", GrokAction.Premium30Off3Mo, Violet, null,
                "Premium (x.subscription.plus): Giảm 30% trong 3 tháng đầu (x-subscription-benefit-discount-30pct)",
                "Ghim gói Premium giảm 30% trong 3 tháng đầu (vd 1,224.99 TRY/tháng × 3) và khởi động lại app.", isPromo: true);

            // Super (x.subscription.super)
            AddAction("Super -50% T1", GrokAction.Super50Off1Mo, Orange, null,
                "Super (x.subscription.super): Giảm 50% tháng đầu (x-subscription-benefit-launch-50off-1mo)",
                "Ghim gói Super giảm 50% tháng đầu (vd 3,150.00 TRY) và khởi động lại app.", isPromo: true);
            AddAction("Super -30% ×3T", GrokAction.Super30Off3Mo, Orange, null,
                "Super (x.subscription.super): Giảm 30% trong 3 tháng đầu (x-subscription-benefit-discount-30pct)",
                "Ghim gói Super giảm 30% trong 3 tháng đầu (vd 4,409.99 TRY/tháng × 3) và khởi động lại app.", isPromo: true);

            // Ultra (x.subscription.ultra)
            AddAction("Ultra 1 trả 3T", GrokAction.UltraPay1Get3, Rose, null,
                "Ultra (x.subscription.ultra): Trả tiền 1 tháng dùng 3 tháng (x-subscription-benefit-test-pay1get3-premium-ultra)",
                "Ghim gói Ultra trả 1 tháng dùng 3 tháng (vd 12,499.99 TRY/3 tháng) và khởi động lại app.", isPromo: true);
            AddAction("Ultra -50% T1", GrokAction.Ultra50Off1Mo, Rose, null,
                "Ultra (x.subscription.ultra): Giảm 50% tháng đầu (x-subscription-benefit-launch-50off-1mo)",
                "Ghim gói Ultra giảm 50% tháng đầu (vd 6,250.00 TRY) và khởi động lại app.", isPromo: true);
            AddAction("Ultra -30% ×3T", GrokAction.Ultra30Off3Mo, Rose, null,
                "Ultra (x.subscription.ultra): Giảm 30% trong 3 tháng đầu (x-subscription-benefit-discount-30pct)",
                "Ghim gói Ultra giảm 30% trong 3 tháng đầu (vd 8,749.99 TRY/tháng × 3) và khởi động lại app.", isPromo: true);

            _summary = TabUi.SummaryLabel("Chọn app ở trên (Grok / Grok Bot). Mở tab gói → bấm Upgrade rồi Back để tải cache (nếu app chỉ hiện giá gốc sẽ bị STORE_PROOF_OFFER_MISMATCH).");
            _summary.Font = new Font("Segoe UI", 8.5f);
            _summary.Padding = new Padding(4, 3, 4, 3);
            _results = TabUi.ResultsGrid("Kết quả");

            var layout = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 4, Padding = new Padding(6) };
            layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            layout.Controls.Add(targetBar, 0, 0);
            layout.Controls.Add(_actions, 0, 1);
            layout.Controls.Add(_summary, 0, 2);
            layout.Controls.Add(_results, 0, 3);

            void SyncFlowConstraints()
            {
                int availW = Math.Max(220, layout.ClientSize.Width - layout.Padding.Horizontal);
                int maxActionsH = Math.Max(140, layout.ClientSize.Height * 52 / 100);
                targetBar.MaximumSize = new Size(availW, 0);
                _actions.MaximumSize = new Size(availW, maxActionsH);
                _summary.MaximumSize = new Size(availW, 0);
            }

            layout.SizeChanged += (_, _) => SyncFlowConstraints();
            SizeChanged += (_, _) => SyncFlowConstraints();
            Controls.Add(layout);
            SyncFlowConstraints();

            UpdateTargetVisuals();
        }

        private static CheckBox CreateTargetCheckBox(string text, Image _, Color __) => new()
        {
            Text = text,
            Checked = true,
            AutoSize = true,
            Font = new Font("Segoe UI Semibold", 9.5f),
            ForeColor = Color.FromArgb(30, 41, 59),
            Margin = new Padding(2, 4, 8, 2),
            Cursor = Cursors.Hand
        };

        private static Control WrapTargetControl(CheckBox checkBox, Image icon, Color badgeColor)
        {
            var panel = new FlowLayoutPanel
            {
                AutoSize = true,
                WrapContents = false,
                Margin = new Padding(0, 2, 14, 2),
                Padding = new Padding(4, 2, 6, 2),
                BackColor = Color.White
            };
            if (icon != null)
            {
                var badge = new PictureBox
                {
                    Image = icon,
                    SizeMode = PictureBoxSizeMode.Zoom,
                    Size = new Size(24, 24),
                    BackColor = badgeColor,
                    Padding = new Padding(3),
                    Margin = new Padding(0, 2, 4, 2)
                };
                panel.Controls.Add(badge);
            }
            panel.Controls.Add(checkBox);
            return panel;
        }

        private GrokTargets SelectedTargets
        {
            get
            {
                var t = GrokTargets.None;
                if (_chkGrok.Checked) t |= GrokTargets.Grok;
                if (_chkGrokBot.Checked) t |= GrokTargets.GrokBot;
                return t;
            }
        }

        private void UpdateTargetVisuals()
        {
            GrokTargets t = SelectedTargets;
            Image icon = t == GrokTargets.Grok
                ? _grokIcon
                : (t == GrokTargets.GrokBot ? _grokBotIcon : null);

            foreach (var btn in _promoButtons)
            {
                btn.Image = icon;
                if (icon != null)
                {
                    btn.ImageSize = new Size(18, 18);
                    btn.ImageAlign = HorizontalAlignment.Left;
                    btn.TextAlign = HorizontalAlignment.Left;
                    btn.ImageOffset = new Point(4, 0);
                    btn.TextOffset = new Point(2, 0);
                }
                else
                {
                    btn.TextAlign = HorizontalAlignment.Center;
                    btn.TextOffset = Point.Empty;
                }
            }
        }

        private void AddAction(string text, GrokAction action, Color color, Image icon, string tooltip, string confirmation = null, bool isPromo = false)
        {
            var button = TabUi.ActionButton(text, color, ButtonWidth, icon, ButtonHeight);
            button.Font = new Font("Segoe UI Semibold", 8.5f);
            button.Margin = new Padding(3);
            if (icon != null)
            {
                button.ImageSize = new Size(18, 18);
                button.ImageOffset = new Point(4, 0);
                button.TextOffset = new Point(2, 0);
            }
            _toolTip.SetToolTip(button, tooltip);
            button.Click += async (sender, args) => await RunAsync(action, confirmation);
            _actions.Controls.Add(button);
            if (isPromo)
                _promoButtons.Add(button);
        }

        private async Task RunAsync(GrokAction action, string confirmation)
        {
            if (!_actions.Enabled) return;

            GrokTargets grokTargets = SelectedTargets;
            if (grokTargets == GrokTargets.None)
            {
                _summary.Text = "Hãy tích chọn ít nhất một app mục tiêu (Grok hoặc Grok Bot).";
                return;
            }

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
                    try { results[i] = (true, _service.Run(targets[i], action, grokTargets)); }
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
