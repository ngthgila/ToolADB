using Guna.UI2.WinForms;

namespace ToolAdb.Features.Picsart
{
    internal sealed class PicsartTab : TabPage
    {
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
            AddAction("Kiểm tra cấu hình", PicsartAction.Inspect, Color.FromArgb(71, 85, 105));
            AddAction("Mở bảng giá", PicsartAction.OpenPricing, Color.FromArgb(29, 78, 216));
            AddAction("Plus tháng", PicsartAction.PlusMonthly, Color.FromArgb(4, 120, 87));
            AddAction("Pro tháng", PicsartAction.ProMonthly, Color.FromArgb(109, 40, 217));
            AddAction("Reset giữ đăng nhập", PicsartAction.ResetKeepLogin, Color.FromArgb(154, 52, 18),
                "Thao tác sẽ đổi ID ứng dụng và sửa hoặc xóa cache thuê bao cục bộ.");
            AddAction("Xóa dữ liệu Picsart", PicsartAction.Reset, Color.FromArgb(185, 28, 28),
                "Thao tác sẽ xóa dữ liệu cục bộ của Picsart, gồm phiên đăng nhập và dữ liệu ứng dụng.");

            _summary = new Label
            {
                Dock = DockStyle.Fill, AutoSize = true, Padding = new Padding(8),
                Text = "Tích thiết bị ở danh sách bên trái. Kiểm tra cấu hình đọc dữ liệu cục bộ trên máy."
            };
            _results = new DataGridView
            {
                Dock = DockStyle.Fill, ReadOnly = true, RowHeadersVisible = false,
                BackgroundColor = Color.White, AllowUserToAddRows = false, AllowUserToDeleteRows = false,
                SelectionMode = DataGridViewSelectionMode.FullRowSelect, AutoSizeRowsMode = DataGridViewAutoSizeRowsMode.AllCells
            };
            _results.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "Thiết bị", Width = 180, SortMode = DataGridViewColumnSortMode.NotSortable });
            _results.Columns.Add(new DataGridViewTextBoxColumn
            {
                HeaderText = "Kết quả", SortMode = DataGridViewColumnSortMode.NotSortable, AutoSizeMode = DataGridViewAutoSizeColumnMode.Fill,
                DefaultCellStyle = new DataGridViewCellStyle { WrapMode = DataGridViewTriState.True }
            });

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
            var button = new Guna2Button
            {
                Text = text, Height = 36, Width = 155, BorderRadius = 4,
                FillColor = color, ForeColor = Color.White
            };
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
                    _results.Rows.Add(serial, "Đang chờ");
                _summary.Text = $"Đang xử lý {targets.Length} thiết bị…";

                var results = new (bool success, string message)[targets.Length];
                await Task.Run(() => Parallel.For(0, targets.Length, new ParallelOptions { MaxDegreeOfParallelism = 4 }, i =>
                {
                    try { results[i] = (true, _service.Run(targets[i], action)); }
                    catch (Exception ex) { results[i] = (false, "Lỗi: " + ex.Message); }
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
