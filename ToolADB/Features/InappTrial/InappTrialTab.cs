using System.Diagnostics;
using ToolAdb.UI;

namespace ToolAdb.Features.InappTrial
{
    internal sealed class InappTrialTab : TabPage
    {
        private static readonly Color Slate = Color.FromArgb(71, 85, 105);
        private static readonly Color Emerald = Color.FromArgb(5, 150, 105);
        private static readonly Color Blue = Color.FromArgb(37, 99, 235);
        private static readonly Color Fuchsia = Color.FromArgb(192, 38, 211);

        private readonly InappTrialService _service;
        private readonly Func<List<string>> _getSelectedDevices;
        private readonly FlowLayoutPanel _actions;
        private readonly ComboBox _targetPackageBox;
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
            AddAction("🦉 Duolingo: Bỏ trial", InappTrialAction.DuolingoNoTrial, Emerald,
                "Thao tác sẽ sửa cache Google Play Billing (có sao lưu) để bảng giá Duolingo bỏ offer trial, mua thẳng.");
            AddAction("📹 Zoom: Bỏ trial", InappTrialAction.ZoomNoTrial, Blue,
                "Thao tác sẽ sửa cache Google Play Billing (có sao lưu) để Zoom bỏ offer trial/winback, giữ gói gốc mua thẳng.");

            var scanButton = TabUi.ActionButton("🔎 GPlay: Quét mọi offer", Slate, 180);
            scanButton.Click += async (sender, args) => await ScanOffersAsync();
            _actions.Controls.Add(scanButton);

            _targetPackageBox = new ComboBox
            {
                Width = 210,
                DropDownStyle = ComboBoxStyle.DropDown,
                Margin = new Padding(4, 7, 4, 4)
            };
            _targetPackageBox.Items.AddRange(new object[]
            {
                "us.zoom.videomeetings",
                "com.duolingo",
                "com.chess",
                "*"
            });
            _targetPackageBox.Text = "us.zoom.videomeetings";
            _actions.Controls.Add(_targetPackageBox);

            var baseButton = TabUi.ActionButton("📦 Đưa về Gói Gốc (Base)", Slate, 195);
            baseButton.Click += async (sender, args) => await RunOfferSwitchAsync(false);
            _actions.Controls.Add(baseButton);

            var winbackButton = TabUi.ActionButton("🎁 Áp dụng Winback", Fuchsia, 175);
            winbackButton.Click += async (sender, args) => await RunOfferSwitchAsync(true);
            _actions.Controls.Add(winbackButton);

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

        private async Task RunOfferSwitchAsync(bool winbackMode)
        {
            if (!_actions.Enabled) return;

            string rawPkg = (_targetPackageBox.Text ?? string.Empty).Trim();
            if (string.IsNullOrEmpty(rawPkg) || (rawPkg != "*" && !InappTrialService.IsValidPackageName(rawPkg)))
            {
                _summary.Text = "Hãy nhập tên package hợp lệ (ví dụ us.zoom.videomeetings) hoặc '*' cho tất cả app trong cache.";
                return;
            }

            var targets = _getSelectedDevices().Distinct(StringComparer.Ordinal).ToArray();
            if (targets.Length == 0)
            {
                _summary.Text = "Hãy tích ít nhất một thiết bị ở danh sách bên trái.";
                return;
            }

            string label = winbackMode ? "Áp dụng Winback" : "Đưa về Gói Gốc (Base)";
            _actions.Enabled = false;
            try
            {
                _results.Rows.Clear();
                foreach (string serial in targets)
                    _results.Rows.Add(serial, "⏳ Đang chờ…");
                _summary.Text = $"Đang {label} ({rawPkg}) trên {targets.Length} thiết bị…";

                var results = new (bool success, string message)[targets.Length];
                await Task.WhenAll(Enumerable.Range(0, targets.Length).Select(i => Task.Run(() =>
                {
                    try { results[i] = (true, _service.SwitchOfferMode(targets[i], rawPkg, winbackMode)); }
                    catch (Exception ex) { results[i] = (false, "❌ Lỗi: " + ex.Message); }
                    SetRowMessage(i, results[i].message);
                })));

                if (IsDisposed) return;
                _summary.Text = $"Hoàn tất {label}: {results.Count(result => result.success)}/{targets.Length} thiết bị.";
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

            _actions.Enabled = false;
            _summary.Text = $"Đang đọc danh sách app trên {targets.Length} thiết bị…";
            DevicePackages[] packageLists;
            try
            {
                packageLists = await Task.WhenAll(targets.Select(serial => Task.Run(() =>
                {
                    try { return _service.ListPackages(serial); }
                    catch (Exception) { return new DevicePackages(); }
                })));
            }
            finally
            {
                if (!IsDisposed) _actions.Enabled = true;
            }
            if (IsDisposed) return;

            string packageName = PromptForPackageName(packageLists);
            if (packageName == null) { _summary.Text = "Đã hủy quét offer."; return; }

            _actions.Enabled = false;
            try
            {
                _results.Rows.Clear();
                foreach (string serial in targets)
                    _results.Rows.Add(serial, "🔎 Đang quét…");
                _summary.Text = $"Đang quét offer của {packageName} trên {targets.Length} thiết bị…";

                var offers = new List<BillingOfferInfo>[targets.Length];
                var messages = new string[targets.Length];
                await Task.WhenAll(Enumerable.Range(0, targets.Length).Select(i => Task.Run(() =>
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
                    SetRowMessage(i, messages[i]);
                })));

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

        private sealed class PackageChoice
        {
            public string Name;
            public int InstalledOn;
            public int CachedOn;
            public int DeviceCount;

            public override string ToString()
            {
                string where = DeviceCount > 1 ? $" ({InstalledOn}/{DeviceCount} máy)" : "";
                return CachedOn > 0
                    ? $"★ {Name} — có cache offer{(DeviceCount > 1 ? $" trên {CachedOn}/{DeviceCount} máy" : "")}"
                    : Name + where;
            }
        }

        private static List<PackageChoice> BuildPackageChoices(DevicePackages[] lists)
        {
            var names = lists.SelectMany(l => l.Installed.Concat(l.WithOfferCache)).Distinct(StringComparer.Ordinal);
            return names
                .Select(name => new PackageChoice
                {
                    Name = name,
                    InstalledOn = lists.Count(l => l.Installed.Contains(name)),
                    CachedOn = lists.Count(l => l.WithOfferCache.Contains(name)),
                    DeviceCount = lists.Length
                })
                .OrderByDescending(c => c.CachedOn > 0)
                .ThenBy(c => c.Name, StringComparer.OrdinalIgnoreCase)
                .ToList();
        }

        private string PromptForPackageName(DevicePackages[] lists)
        {
            var choices = BuildPackageChoices(lists);
            int cached = choices.Count(c => c.CachedOn > 0);

            using var prompt = new Form
            {
                Text = "Quét offer Google Play",
                ClientSize = new Size(560, 440),
                StartPosition = FormStartPosition.CenterParent,
                FormBorderStyle = FormBorderStyle.FixedDialog,
                MaximizeBox = false, MinimizeBox = false
            };
            var label = new Label
            {
                Text = choices.Count == 0
                    ? "Không đọc được danh sách app. Nhập package thủ công:"
                    : $"Chọn app ({choices.Count} app, {cached} app có cache offer ★). Gõ để lọc hoặc nhập package khác:",
                Location = new Point(18, 14), Size = new Size(524, 20)
            };
            var input = new TextBox { Location = new Point(20, 38), Width = 520 };
            var list = new ListBox { Location = new Point(20, 68), Size = new Size(520, 310), IntegralHeight = false };
            var hint = new Label
            {
                Text = "★ = đã có cache offer, quét được ngay. App khác: mở paywall trong app một lần rồi quét.",
                Location = new Point(18, 384), Size = new Size(524, 18), ForeColor = Slate
            };
            var ok = new Button { Text = "Quét offer", DialogResult = DialogResult.OK, Location = new Point(360, 405), Width = 85 };
            var cancel = new Button { Text = "Hủy", DialogResult = DialogResult.Cancel, Location = new Point(455, 405), Width = 85 };
            prompt.Controls.AddRange(new Control[] { label, input, list, hint, ok, cancel });
            prompt.AcceptButton = ok;
            prompt.CancelButton = cancel;

            void Refill()
            {
                string filter = input.Text.Trim();
                list.BeginUpdate();
                list.Items.Clear();
                foreach (var choice in choices)
                    if (filter.Length == 0 || choice.Name.Contains(filter, StringComparison.OrdinalIgnoreCase))
                        list.Items.Add(choice);
                list.EndUpdate();
                if (list.Items.Count > 0) list.SelectedIndex = 0;
            }
            input.TextChanged += (_, _) => Refill();
            input.KeyDown += (_, e) =>
            {
                if (list.Items.Count == 0) return;
                if (e.KeyCode == Keys.Down) { list.SelectedIndex = Math.Min(list.SelectedIndex + 1, list.Items.Count - 1); e.Handled = true; }
                else if (e.KeyCode == Keys.Up) { list.SelectedIndex = Math.Max(list.SelectedIndex - 1, 0); e.Handled = true; }
            };
            list.DoubleClick += (_, _) => { if (list.SelectedItem != null) prompt.DialogResult = DialogResult.OK; };
            Refill();
            prompt.Shown += (_, _) => input.Focus();

            if (prompt.ShowDialog(this) != DialogResult.OK) return null;

            string typed = input.Text.Trim();
            // Danh sách chỉ chứa app khớp chữ đã gõ: có dòng được chọn thì dùng dòng đó, không khớp gì thì dùng chữ đã gõ.
            string packageName = list.SelectedItem is PackageChoice picked ? picked.Name : typed;
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
