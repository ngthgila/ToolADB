using Guna.UI2.WinForms;

namespace ToolAdb.UI
{
    internal static class DashboardCards
    {
        internal static Guna2GroupBox CreateGroupbox(string title)
        {
            return new Guna2GroupBox
            {
                Text = title,
                Font = new Font("Segoe UI Semibold", 9.5f),
                ForeColor = Color.Black,
                CustomBorderColor = Color.FromArgb(241, 245, 249),
                CustomBorderThickness = new Padding(0, 35, 0, 0),
                FillColor = Color.White,
                BorderColor = Color.FromArgb(226, 232, 240),
                BorderRadius = 6,
                Width = 340,
                Margin = new Padding(10)
            };
        }

        // Helper 4: Thêm nút vào GroupBox Dashboard
        internal static void AddBtn(Guna2GroupBox group, string text, Color color, Action onClick)
        {
            var btn = new Guna2Button
            {
                Text = text,
                FillColor = color,
                ForeColor = Color.White,
                Tag = onClick
            };
            btn.Click += (s, e) => onClick();
            group.Controls.Add(btn);
        }

        // Helper 5: Sắp xếp nút trong GroupBox (Responsive)
        internal static void ReflowCardCompact(Guna2GroupBox card, Color themeColor, FlowLayoutPanel parent)
        {
            var buttons = card.Controls.OfType<Guna2Button>().ToList();
            int gap = 10, padding = 15, headerHeight = 40, btnHeight = 38;
            int colWidth = (card.Width - (padding * 2) - gap) / 2;

            for (int i = 0; i < buttons.Count; i++)
            {
                var btn = buttons[i];
                btn.Height = btnHeight;
                btn.Width = colWidth;
                btn.BorderRadius = 4;
                btn.Font = new Font("Segoe UI", 9f, FontStyle.Bold);
                if (btn.FillColor == Color.FromArgb(94, 148, 255)) btn.FillColor = themeColor;

                int row = i / 2;
                int col = i % 2;
                int x = padding + (col * (colWidth + gap));
                int y = headerHeight + padding + (row * (btnHeight + gap));
                btn.Location = new Point(x, y);
            }
            int totalRows = (int)Math.Ceiling(buttons.Count / 2.0);
            card.Height = headerHeight + padding + (totalRows * (btnHeight + gap)) + 5;
            parent.Controls.Add(card);
        }
    }
}
