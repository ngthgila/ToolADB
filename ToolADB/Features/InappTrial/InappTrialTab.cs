using System.Diagnostics;
using ToolAdb.UI;

namespace ToolAdb.Features.InappTrial
{
    internal sealed class InappTrialTab : TabPage
    {
        private static readonly Color Slate = Color.FromArgb(71, 85, 105);
        private static readonly Color Emerald = Color.FromArgb(5, 150, 105);
        private static readonly Color Blue = Color.FromArgb(37, 99, 235);

        private readonly InappTrialService _service;
        private readonly Func<List<string>> _getSelectedDevices;
        private readonly FlowLayoutPanel _actions;
        private readonly DataGridView _results;
        private readonly Label _summary;

        internal InappTrialTab(InappTrialService service, Func<List<string>> getSelectedDevices)
        {
            _service = service;
            _getSelectedDevices = getSelectedDevices;
            Text = "Inapp Trial";
            BackColor = Color.White;

            _actions = new FlowLayoutPanel
            {
                Dock = DockStyle.Fill, AutoSize = true, WrapContents = true,
                Padding = new Padding(4)
            };

            // Danh sách app bỏ trial. Thêm app mới: thêm một dòng AddAction tại đây,
            // thêm giá trị enum trong InappTrialService.cs và file InappTrialService.<App>.cs.
            AddAction("🟢 Grammarly: Tắt trial", InappTrialAction.GrammarlyNoTrial, Emerald,
                "Thao tác sẽ sửa cấu hình thử nghiệm cục bộ và khởi động lại Grammarly.");
            AddAction("♟ Chess.com: Bỏ trial", InappTrialAction.ChessNoTrial, Blue,
                "Thao tác sẽ sửa cache Google Play Billing (có sao lưu) để bảng giá Chess.com bỏ offer trial, mua thẳng.");

            var scanButton = TabUi.ActionButton("🔎 GPlay: Quét mọi offer", Slate, 180);
            scanButton.Click += async (sender, args) => await ScanOffersAsync();
            _actions.Controls.Add(scanButton);

            _summary = TabUi.SummaryLabel("Tích thiết bị ở danh sách bên trái. Các thao tác cần ADB root và sửa dữ liệu cục bộ trên máy.");
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

        private void AddAction(string text, InappTrialAction action, Color color, string confirmation = null)
        {
            var button = TabUi.ActionButton(text, color, 180);
            button.Click += async (sender, args) => await RunAsync(action, confirmation);
            _actions.Controls.Add(button);
        }

        private async Task RunAsync(InappTrialAction action, string confirmation)
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
                "Inapp Trial", MessageBoxButtons.YesNo, MessageBoxIcon.Warning, MessageBoxDefaultButton.Button2) != DialogResult.Yes)
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

        private async Task ScanOffersAsync()
        {
            if (!_actions.Enabled) return;

            var targets = _getSelectedDevices().Distinct(StringComparer.Ordinal).ToArray();
            if (targets.Length == 0)
            {
                _summary.Text = "Hãy tích ít nhất một thiết bị ở danh sách bên trái.";
                return;
            }

            string packageName = PromptForPackageName();
            if (packageName == null) return;

            _actions.Enabled = false;
            try
            {
                _results.Rows.Clear();
                foreach (string serial in targets)
                    _results.Rows.Add(serial, "🔎 Đang quét…");
                _summary.Text = $"Đang quét offer của {packageName} trên {targets.Length} thiết bị…";

                var offers = new List<BillingOfferInfo>[targets.Length];
                var messages = new string[targets.Length];
                await Task.Run(() => Parallel.For(0, targets.Length, new ParallelOptions { MaxDegreeOfParallelism = 4 }, i =>
                {
                    try
                    {
                        offers[i] = _service.ScanOffers(targets[i], packageName);
                        messages[i] = offers[i].Count > 0
                            ? $"✅ Đã quét {offers[i].Count} pricing phase."
                            : "⚠️ Chưa có cache offer. Hãy mở app và màn hình paywall một lần rồi quét lại.";
                    }
                    catch (Exception ex)
                    {
                        offers[i] = new List<BillingOfferInfo>();
                        messages[i] = "❌ Lỗi: " + ex.Message;
                    }
                }));

                if (IsDisposed) return;
                for (int i = 0; i < targets.Length; i++)
                    _results.Rows[i].Cells[1].Value = messages[i];

                var all = offers.SelectMany(x => x).ToList();
                if (all.Count == 0)
                {
                    _summary.Text = $"Không tìm thấy offer của {packageName} trong cache Billing.";
                    return;
                }

                string reportPath = InappTrialService.SaveOfferReport(all, packageName);
                int offerCount = all.Select(x => x.Serial + "|" + x.ProductId + "|" + x.BasePlanId + "|" + x.OfferId).Distinct().Count();
                _summary.Text = $"Đã quét {offerCount} offer/base plan ({all.Count} pricing phase) của {packageName}.";
                ShowOfferResults(all, packageName, reportPath);
            }
            finally
            {
                if (!IsDisposed) _actions.Enabled = true;
            }
        }

        private string PromptForPackageName()
        {
            using var prompt = new Form
            {
                Text = "Quét offer Google Play",
                ClientSize = new Size(520, 145),
                StartPosition = FormStartPosition.CenterParent,
                FormBorderStyle = FormBorderStyle.FixedDialog,
                MaximizeBox = false, MinimizeBox = false
            };
            var label = new Label { Text = "Package ứng dụng (ví dụ: com.openai.chatgpt):", Location = new Point(18, 16), AutoSize = true };
            var input = new TextBox { Text = "com.openai.chatgpt", Location = new Point(20, 43), Width = 478 };
            var ok = new Button { Text = "Quét offer", DialogResult = DialogResult.OK, Location = new Point(318, 92), Width = 85 };
            var cancel = new Button { Text = "Hủy", DialogResult = DialogResult.Cancel, Location = new Point(413, 92), Width = 85 };
            prompt.Controls.AddRange(new Control[] { label, input, ok, cancel });
            prompt.AcceptButton = ok;
            prompt.CancelButton = cancel;

            if (prompt.ShowDialog(this) != DialogResult.OK) return null;

            string packageName = input.Text.Trim();
            if (!InappTrialService.IsValidPackageName(packageName))
            {
                MessageBox.Show(this, "Package không hợp lệ. Chỉ dùng chữ, số, dấu gạch dưới và dấu chấm.",
                    "Package không hợp lệ", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return null;
            }
            return packageName;
        }

        private void ShowOfferResults(List<BillingOfferInfo> offers, string packageName, string reportPath)
        {
            var form = new Form
            {
                Text = "Google Play Offers - " + packageName,
                Size = new Size(1320, 680),
                MinimumSize = new Size(950, 500),
                StartPosition = FormStartPosition.CenterParent,
                Font = new Font("Segoe UI", 9f)
            };
            var summary = new Label
            {
                Dock = DockStyle.Top, Height = 45, Padding = new Padding(10, 7, 10, 5),
                Text = $"{offers.Count} pricing phase | Báo cáo: {reportPath}", AutoEllipsis = true
            };
            var offerGrid = new DataGridView
            {
                Dock = DockStyle.Fill, ReadOnly = true, AllowUserToAddRows = false, AllowUserToDeleteRows = false,
                RowHeadersVisible = false, AutoGenerateColumns = true,
                AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.DisplayedCells,
                SelectionMode = DataGridViewSelectionMode.FullRowSelect,
                DataSource = offers
            };
            var openReport = new Button { Dock = DockStyle.Bottom, Height = 38, Text = "Mở thư mục báo cáo" };
            openReport.Click += (sender, args) =>
            {
                try { Process.Start("explorer.exe", "/select,\"" + reportPath + "\""); }
                catch (Exception ex) { MessageBox.Show(this, ex.Message, "Không mở được báo cáo"); }
            };
            form.Controls.Add(offerGrid);
            form.Controls.Add(summary);
            form.Controls.Add(openReport);
            form.ShowDialog(this);
        }
    }
}
