using ToolAdb.Infrastructure;
using static ToolAdb.UI.DashboardCards;

namespace ToolAdb.Features.GeoSpoof
{
    internal sealed class GeoSpoofTab : TabPage
    {
        private readonly AdbClient _adb;
        private readonly Func<List<string>> _getTargetDevices;
        private readonly Action<string> _setStatus;
        private readonly Action _startProgress;
        private readonly Action _stopProgress;

        internal GeoSpoofTab(AdbClient adb, Func<List<string>> getTargetDevices, Action<string> setStatus, Action startProgress, Action stopProgress)
        {
            _adb = adb;
            _getTargetDevices = getTargetDevices;
            _setStatus = setStatus;
            _startProgress = startProgress;
            _stopProgress = stopProgress;
            Text = "Spoof Quốc Gia";
            BackColor = Color.White;

            var flowDash = new FlowLayoutPanel
            {
                Dock = DockStyle.Fill,
                AutoScroll = true,
                Padding = new Padding(10),
                BackColor = Color.FromArgb(241, 245, 249)
            };

            var cardAsia = CreateGroupbox("Châu Á");
            AddBtn(cardAsia, "Philippines (Smart)", Color.FromArgb(14, 165, 233), async () => await ActionSpoofCountry("ph"));
            AddBtn(cardAsia, "Ấn Độ (Jio)", Color.FromArgb(14, 165, 233), async () => await ActionSpoofCountry("in"));
            AddBtn(cardAsia, "Pakistan (Jazz)", Color.FromArgb(14, 165, 233), async () => await ActionSpoofCountry("pk"));
            AddBtn(cardAsia, "Việt Nam (Viettel)", Color.FromArgb(14, 165, 233), async () => await ActionSpoofCountry("vn"));
            ReflowCardCompact(cardAsia, Color.FromArgb(34, 197, 94), flowDash);

            var cardEuropeAfrica = CreateGroupbox("Châu Âu & Châu Phi");
            AddBtn(cardEuropeAfrica, "Nigeria (MTN)", Color.FromArgb(249, 115, 22), async () => await ActionSpoofCountry("ng"));
            AddBtn(cardEuropeAfrica, "Ai Cập (Vodafone)", Color.FromArgb(249, 115, 22), async () => await ActionSpoofCountry("eg"));
            AddBtn(cardEuropeAfrica, "Thổ Nhĩ Kỳ (Turkcell)", Color.FromArgb(249, 115, 22), async () => await ActionSpoofCountry("tr"));
            AddBtn(cardEuropeAfrica, "Anh (Vodafone UK)", Color.FromArgb(249, 115, 22), async () => await ActionSpoofCountry("gb"));
            ReflowCardCompact(cardEuropeAfrica, Color.FromArgb(168, 85, 247), flowDash);

            var cardAmerica = CreateGroupbox("Châu Mỹ & Công cụ");
            AddBtn(cardAmerica, "Mỹ (T-Mobile)", Color.FromArgb(236, 72, 153), async () => await ActionSpoofCountry("us"));
            AddBtn(cardAmerica, "Khôi Phục Mạng (Clear)", Color.FromArgb(239, 68, 68), async () => await ActionSpoofCountry("clear"));
            ReflowCardCompact(cardAmerica, Color.FromArgb(99, 102, 241), flowDash);

            Controls.Add(flowDash);
        }

        private async Task ActionSpoofCountry(string countryCode)
        {
            var targets = _getTargetDevices();
            if (targets.Count == 0) { MessageBox.Show("Chưa chọn thiết bị!"); return; }

            var geoDb = new Dictionary<string, (string numeric, string iso, string name, string tz, string lang)>()
            {
                {"ph", ("51503", "ph", "Smart", "Asia/Manila", "en-PH")},
                {"ng", ("62130", "ng", "MTN NG", "Africa/Lagos", "en-NG")},
                {"tr", ("28601", "tr", "Turkcell", "Europe/Istanbul", "tr-TR")},
                {"in", ("405840", "in", "Jio", "Asia/Kolkata", "en-IN")},
                {"pk", ("41001", "pk", "Jazz", "Asia/Karachi", "en-PK")},
                {"eg", ("60202", "eg", "Vodafone", "Africa/Cairo", "en-EG")},
                {"vn", ("45204", "vn", "Viettel", "Asia/Ho_Chi_Minh", "vi-VN")},
                {"us", ("310260", "us", "T-Mobile", "America/New_York", "en-US")},
                {"gb", ("23415", "gb", "Vodafone UK", "Europe/London", "en-GB")},
                {"clear", ("", "", "Clear", "Asia/Ho_Chi_Minh", "vi-VN")}
            };

            if (!geoDb.ContainsKey(countryCode))
            {
                MessageBox.Show($"Quốc gia '{countryCode}' không được hỗ trợ!");
                return;
            }

            var config = geoDb[countryCode];
            var apps = new[] { "com.zhiliaoapp.musically", "com.android.vending", "com.google.android.gms" };

            _setStatus($"Đang chuyển sang {config.name} ({countryCode.ToUpper()})...");
            _startProgress();

            await Task.Run(() => {
                Parallel.ForEach(targets, deviceId => {
                    _adb.RunOnDevice(deviceId, "shell", $"setprop persist.qa.fake_sim_numeric '{config.numeric}'");
                    _adb.RunOnDevice(deviceId, "shell", $"setprop persist.qa.fake_sim_iso '{config.iso}'");
                    _adb.RunOnDevice(deviceId, "shell", $"setprop persist.qa.fake_sim_name '{config.name}'");
                    _adb.RunOnDevice(deviceId, "shell", $"setprop persist.sys.timezone '{config.tz}'");
                    _adb.RunOnDevice(deviceId, "shell", $"setprop persist.sys.locale '{config.lang}'");
                    _adb.RunOnDevice(deviceId, "shell", $"setprop ro.product.locale '{config.lang}'");

                    _adb.RunOnDevice(deviceId, "shell", "cmd connectivity airplane-mode enable");
                    Thread.Sleep(1500);
                    _adb.RunOnDevice(deviceId, "shell", "cmd connectivity airplane-mode disable");

                    foreach (var app in apps)
                    {
                        _adb.RunOnDevice(deviceId, "shell", $"am force-stop {app}");
                        _adb.RunOnDevice(deviceId, "shell", $"rm -rf /data/user/0/{app}/cache/*");
                        _adb.RunOnDevice(deviceId, "shell", $"rm -rf /data/user_de/0/{app}/code_cache/*");
                    }

                    _adb.RunOnDevice(deviceId, "shell", "am start -n com.android.vending/.AssetBrowserActivity > /dev/null 2>&1 || monkey -p com.android.vending -c android.intent.category.LAUNCHER 1 > /dev/null 2>&1");
                });
            });

            _stopProgress();
            _setStatus($"Đã chuyển thiết bị sang: {countryCode.ToUpper()} ({config.name})");
            MessageBox.Show($"Đã chuyển {targets.Count} thiết bị sang {countryCode.ToUpper()} ({config.name})", "Hoàn tất");
        }
    }
}
