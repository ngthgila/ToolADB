using Guna.UI2.WinForms;

namespace ToolAdb.UI
{
    // Giao diện thống nhất cho các tab feature: nút hành động và bảng kết quả.
    internal static class TabUi
    {
        internal static Guna2Button ActionButton(string text, Color color, int width = 170)
        {
            var button = new Guna2Button
            {
                Text = text, Height = 38, Width = width, BorderRadius = 6,
                FillColor = color, ForeColor = Color.White,
                Font = new Font("Segoe UI Semibold", 9f),
                Margin = new Padding(4)
            };
            button.HoverState.FillColor = Darken(color);
            return button;
        }

        internal static DataGridView ResultsGrid(string valueHeader)
        {
            var grid = new DataGridView
            {
                Dock = DockStyle.Fill, ReadOnly = true, RowHeadersVisible = false,
                AllowUserToAddRows = false, AllowUserToDeleteRows = false,
                AllowUserToResizeRows = false, MultiSelect = false,
                SelectionMode = DataGridViewSelectionMode.FullRowSelect,
                AutoSizeRowsMode = DataGridViewAutoSizeRowsMode.AllCells,
                BorderStyle = BorderStyle.None,
                BackgroundColor = Color.White,
                GridColor = Color.FromArgb(226, 232, 240),
                CellBorderStyle = DataGridViewCellBorderStyle.SingleHorizontal,
                EnableHeadersVisualStyles = false,
                ColumnHeadersHeight = 34
            };
            grid.ColumnHeadersDefaultCellStyle = new DataGridViewCellStyle
            {
                BackColor = Color.FromArgb(241, 245, 249),
                ForeColor = Color.FromArgb(51, 65, 85),
                SelectionBackColor = Color.FromArgb(241, 245, 249),
                Font = new Font("Segoe UI Semibold", 9f),
                Padding = new Padding(8, 0, 8, 0)
            };
            grid.DefaultCellStyle = new DataGridViewCellStyle
            {
                Font = new Font("Segoe UI", 9.5f),
                ForeColor = Color.FromArgb(30, 41, 59),
                BackColor = Color.White,
                SelectionBackColor = Color.FromArgb(219, 234, 254),
                SelectionForeColor = Color.FromArgb(30, 41, 59),
                Padding = new Padding(8, 6, 8, 6),
                WrapMode = DataGridViewTriState.True
            };
            grid.AlternatingRowsDefaultCellStyle = new DataGridViewCellStyle
            {
                BackColor = Color.FromArgb(248, 250, 252)
            };
            grid.Columns.Add(new DataGridViewTextBoxColumn
            {
                HeaderText = "Thiết bị", Width = 200, SortMode = DataGridViewColumnSortMode.NotSortable
            });
            grid.Columns.Add(new DataGridViewTextBoxColumn
            {
                HeaderText = valueHeader, SortMode = DataGridViewColumnSortMode.NotSortable,
                AutoSizeMode = DataGridViewAutoSizeColumnMode.Fill
            });
            return grid;
        }

        internal static Label SummaryLabel(string text) => new Label
        {
            Dock = DockStyle.Fill, AutoSize = true, Padding = new Padding(8, 6, 8, 6),
            Font = new Font("Segoe UI", 9.5f), ForeColor = Color.FromArgb(71, 85, 105),
            Text = text
        };

        private static Color Darken(Color color) => Color.FromArgb(
            (int)(color.R * 0.82), (int)(color.G * 0.82), (int)(color.B * 0.82));
    }
}
