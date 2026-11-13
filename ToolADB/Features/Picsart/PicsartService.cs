using System.Security.Cryptography;
using System.Text.RegularExpressions;
using ToolAdb.Infrastructure;

namespace ToolAdb.Features.Picsart
{
    internal enum PicsartAction { Inspect, OpenPricing, ProMonthly, UltraMonthly, Reset, ResetKeepLogin }

    internal sealed class PicsartService
    {
        private const string Package = "com.picsart.studio";
        private const string DataPath = "/data/data/" + Package;
        private readonly AdbClient _adb;

        internal PicsartService(AdbClient adb) => _adb = adb;

        internal string Run(string serial, PicsartAction action)
        {
            if (action == PicsartAction.OpenPricing)
            {
                Shell(serial, "am start -a android.intent.action.VIEW -d 'picsart://subscription?type=full-screen-promotion&subscription_force_enabled=true&source=profile' " + Package);
                return "Đã gửi lệnh mở bảng giá.";
            }

            EnsureRoot(serial);
            if (action == PicsartAction.Inspect) return Inspect(serial);

            string id = "d_" + Convert.ToHexString(RandomNumberGenerator.GetBytes(16)).ToLowerInvariant();
            Shell(serial, "am force-stop " + Package);
            if (action == PicsartAction.Reset)
            {
                // Giữ lại settings.json (cấu hình bảng giá) qua backup /sdcard như bản Inapp-Patch gốc.
                Shell(serial, $"cp {DataPath}/files/settings.json /sdcard/.picsart_settings_bak.json 2>/dev/null || true");
                if (Shell(serial, "pm clear " + Package).Trim() != "Success")
                    throw new InvalidOperationException("Picsart không xác nhận đã xóa dữ liệu.");

                string preferences = DataPath + "/shared_prefs";
                string file = preferences + "/com.picsart.analytics.xml";
                Shell(serial, $"set -e; mkdir -p {preferences} {DataPath}/files; " +
                    "if [ -f /sdcard/.picsart_settings_bak.json ]; then " +
                    $"cp /sdcard/.picsart_settings_bak.json {DataPath}/files/settings.json; rm -f /sdcard/.picsart_settings_bak.json; fi; " +
                    $"printf '%s' '<map><string name=\"device_id\">{id}</string></map>' > {file}; " +
                    $"chmod 660 {file}; " + FixPermissions);
                Launch(serial);
                return "Đã xóa dữ liệu Picsart (giữ cấu hình bảng giá) và gửi lệnh mở lại.";
            }

            // Check the settings file before making any of the optional preference changes.
            Shell(serial, $"test -f {DataPath}/files/settings.json || {{ echo 'Chưa có settings.json của Picsart' >&2; exit 1; }}");
            // Gói Ultra luôn xóa token thuê bao cũ trước, tránh lỗi
            // "We are unable to change your subscription plan" của Google Play.
            if (action == PicsartAction.ResetKeepLogin || action == PicsartAction.UltraMonthly)
                ResetPreferences(serial, id);

            string tabPatch = action == PicsartAction.ProMonthly
                ? "s/\"content_id\":\"plus_tab_default\"}/\"content_id\":\"pro_tab_default\"}/g"
                : "";
            string patch = action == PicsartAction.UltraMonthly
                ? PatchUltra
                : PatchMonthly + (tabPatch.Length == 0 ? "" : $"sed -i '{tabPatch}' {DataPath}/files/settings.json; ");
            Shell(serial, "set -e; " + patch + FixPermissions);
            Launch(serial);
            return action == PicsartAction.UltraMonthly
                ? "Đã xóa token thuê bao cũ, ép cấu hình gói Ultra (1 tháng, 1500 credit) và gửi lệnh mở lại."
                : "Đã chạy lệnh cập nhật cấu hình cục bộ và gửi lệnh mở lại.";
        }

        private void ResetPreferences(string serial, string id)
        {
            Shell(serial, "set -e; " +
                $"for file in {DataPath}/shared_prefs/com.picsart.analytics.xml {DataPath}/shared_prefs/participated_experiments_prefs_key.xml {DataPath}/cache/bugsnag/user-info; do " +
                $"if [ -f \"$file\" ]; then sed -i 's/d_[0-9a-f]\\{{32\\}}/{id}/g' \"$file\"; fi; done; " +
                EditIfPresent("com.picsart.analytics.tag_settings_cache.xml", "s/\"subscription_status\":\"subscribed\"/\"subscription_status\":\"not_subscribed\"/g") +
                EditIfPresent("com.picsart.studio_preferences.xml", "s/app_start_subscription_status\" value=\"true/app_start_subscription_status\" value=\"false/g; s/SUBSCRIPTION\" value=\"true/SUBSCRIPTION\" value=\"false/g") +
                $"rm -f {DataPath}/files/device-id {DataPath}/files/internal-device-id " +
                $"{DataPath}/shared_prefs/subscription.shared.preferences.xml {DataPath}/shared_prefs/com.facebook.internal.iap.IAP_CACHE_GPBLV2V7.xml; ");
        }

        private static string EditIfPresent(string name, string expression)
        {
            string file = DataPath + "/shared_prefs/" + name;
            return $"if [ -f {file} ]; then sed -i '{expression}' {file}; fi; ";
        }

        private string Inspect(string serial)
        {
            string output = Shell(serial, "set -e; " +
                $"for file in {DataPath}/shared_prefs/subscription.shared.preferences.xml {DataPath}/files/settings.json; do " +
                "if [ -f \"$file\" ]; then cat \"$file\"; printf '\\n'; fi; done");
            var lines = new List<string>();

            var plans = new List<string>();
            if (output.Contains("subscription_ultra_monthly", StringComparison.Ordinal))
                plans.Add("🔥 Ultra tháng (1500 credit)");
            if (output.Contains("pro_monthly", StringComparison.Ordinal) || output.Contains("plus_monthly", StringComparison.Ordinal))
                plans.Add("gói tháng");
            if (output.Contains("pro_weekly", StringComparison.Ordinal) || output.Contains("plus_weekly", StringComparison.Ordinal))
                plans.Add("gói tuần (dính A/B test)");
            lines.Add("💳 Bảng giá:  " + (plans.Count > 0
                ? string.Join(" + ", plans)
                : "chưa tải cấu hình (hãy mở Picsart một lần)"));

            if (Regex.IsMatch(output, @"""accessScopes""\s*:\s*\[\s*""premium"""))
                lines.Add("⚠️ Cache còn dấu hiệu premium — nên Reset giữ đăng nhập trước khi mua gói mới.");

            return string.Join(Environment.NewLine, lines);
        }

        private void EnsureRoot(string serial)
        {
            if (Shell(serial, "id -u").Trim() == "0") return;

            string root = Execute(serial, 8000, "root");
            if (root.Contains("cannot run as root", StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException(root.Trim());
            Execute(serial, 12000, "wait-for-device");
            if (Shell(serial, "id -u").Trim() != "0")
                throw new InvalidOperationException("Thiết bị không cấp quyền ADB root.");
        }

        private void Launch(string serial) =>
            Shell(serial, $"monkey -p {Package} -c android.intent.category.LAUNCHER 1");

        private string Shell(string serial, string command) => Execute(serial, 15000, "shell", command);

        private string Execute(string serial, int timeoutMs, params string[] args)
        {
            var command = new List<string> { "-s", serial };
            command.AddRange(args);
            var result = _adb.Capture(command, timeoutMs);
            if (result.exitCode != 0 || result.stdout.TrimStart().StartsWith("Error:", StringComparison.OrdinalIgnoreCase))
            {
                string message = string.IsNullOrWhiteSpace(result.stderr) ? result.stdout : result.stderr;
                throw new InvalidOperationException(string.IsNullOrWhiteSpace(message)
                    ? $"ADB trả mã lỗi {result.exitCode}." : message.Trim());
            }
            return result.stdout;
        }

        // Các mẫu dưới đây phải khớp nguyên văn JSON trong settings.json
        // ("key":"value" phân cách bằng ":"), giữ đúng thứ tự như bản Inapp-Patch gốc.
        private const string PatchMonthly =
            "sed -i 's/\"pro_weekly_100\",\"is_selected\"/\"pro_monthly\",\"is_selected\"/g; " +
            "s/\"pro_weekly\",\"is_selected\"/\"pro_monthly\",\"is_selected\"/g; " +
            "s/\"ultra_monthly\",\"is_selected\"/\"pro_monthly\",\"is_selected\"/g; " +
            "s/\"ultra_yearly\",\"is_selected\"/\"pro_yearly_trial\",\"is_selected\"/g; " +
            "s/\"plus_weekly\",\"is_selected\"/\"plus_monthly\",\"is_selected\"/g; " +
            "s/\"package_id\":\"subscription_pro_weekly\"/\"package_id\":\"subscription_pro_monthly\"/g; " +
            "s/\"package_id\":\"subscription_ultra_monthly\"/\"package_id\":\"subscription_pro_monthly\"/g; " +
            "s/\"package_id\":\"subscription_plus_weekly\"/\"package_id\":\"subscription_plus_tier_monthly\"/g; " +
            "s/title_12/title_3/g; s/subtitle_8/subtitle_2/g; " +
            // Nút pro_monthly mặc định trỏ tới subscription_pro_monthly_100 (chỉ 100 credit/tháng);
            // ép về SKU chuẩn subscription_pro_monthly (500 credit/tháng).
            "s/\"package_sku\":\"subscription_pro_monthly_100\"/\"package_sku\":\"subscription_pro_monthly\"/g; " +
            "s/\"package_sku\":\"subscription_pro_1_monthly\"/\"package_sku\":\"subscription_pro_monthly\"/g' " +
            DataPath + "/files/settings.json; ";

        private const string PatchUltra =
            "sed -i 's/\"package_sku\":\"subscription_ultra_monthly_750_credits\"/\"package_sku\":\"subscription_ultra_monthly\"/g; " +
            "s/\"package_sku\":\"subscription_ultra_yearly_750_credits\"/\"package_sku\":\"subscription_ultra_yearly\"/g; " +
            "s/\"pro_weekly_100\",\"is_selected\":false/\"ultra_monthly\",\"is_selected\":true/g; " +
            "s/\"pro_weekly_100\",\"is_selected\"/\"ultra_monthly\",\"is_selected\"/g; " +
            "s/\"pro_weekly\",\"is_selected\":false/\"ultra_monthly\",\"is_selected\":true/g; " +
            "s/\"pro_weekly\",\"is_selected\"/\"ultra_monthly\",\"is_selected\"/g; " +
            "s/\"pro_monthly\",\"is_selected\":false/\"ultra_monthly\",\"is_selected\":true/g; " +
            "s/\"pro_monthly\",\"is_selected\"/\"ultra_monthly\",\"is_selected\"/g; " +
            "s/\"plus_weekly\",\"is_selected\":false/\"ultra_monthly\",\"is_selected\":true/g; " +
            "s/\"plus_weekly\",\"is_selected\"/\"ultra_monthly\",\"is_selected\"/g; " +
            "s/\"plus_monthly\",\"is_selected\":false/\"ultra_monthly\",\"is_selected\":true/g; " +
            "s/\"plus_monthly\",\"is_selected\"/\"ultra_monthly\",\"is_selected\"/g; " +
            "s/\"ultra_monthly\",\"is_selected\":false/\"ultra_monthly\",\"is_selected\":true/g; " +
            "s/\"pro_yearly_trial\",\"is_selected\":true/\"ultra_yearly\",\"is_selected\":false/g; " +
            "s/\"pro_yearly_trial\",\"is_selected\"/\"ultra_yearly\",\"is_selected\"/g; " +
            "s/\"pro_yearly_wo_trial\",\"is_selected\":true/\"ultra_yearly\",\"is_selected\":false/g; " +
            "s/\"pro_yearly_wo_trial\",\"is_selected\"/\"ultra_yearly\",\"is_selected\"/g; " +
            "s/\"plus_yearly_trial\",\"is_selected\":true/\"ultra_yearly\",\"is_selected\":false/g; " +
            "s/\"plus_yearly_trial\",\"is_selected\"/\"ultra_yearly\",\"is_selected\"/g; " +
            "s/\"plus_yearly_wo_trial\",\"is_selected\":true/\"ultra_yearly\",\"is_selected\":false/g; " +
            "s/\"plus_yearly_wo_trial\",\"is_selected\"/\"ultra_yearly\",\"is_selected\"/g; " +
            "s/\"ultra_yearly\",\"is_selected\":true/\"ultra_yearly\",\"is_selected\":false/g; " +
            "s/\"package_id\":\"subscription_pro_weekly\"/\"package_id\":\"subscription_ultra_monthly\"/g; " +
            "s/\"package_id\":\"subscription_pro_monthly\"/\"package_id\":\"subscription_ultra_monthly\"/g; " +
            "s/\"package_id\":\"subscription_plus_weekly\"/\"package_id\":\"subscription_ultra_monthly\"/g; " +
            "s/\"package_id\":\"subscription_plus_tier_monthly\"/\"package_id\":\"subscription_ultra_monthly\"/g; " +
            "s/\"content_id\":\"plus_tab_default\"}/\"content_id\":\"pro_tab_default\"}/g; " +
            "s/\"content_id\":\"pro_tab_ultra\",\"selected\"/\"content_id\":\"pro_tab_default\",\"selected\"/g; " +
            "s/\"content_id\":\"ultra_yearly\",\"badge\"/\"content_id\":\"pro_tab_default\",\"badge\"/g; " +
            "s/\"content_id\":\"ultra_yearly_upgrade\",\"selected\"/\"content_id\":\"pro_tab_default\",\"selected\"/g; " +
            "s/title_3/title_12/g; s/subtitle_2/subtitle_8/g' " +
            DataPath + "/files/settings.json; ";

        private const string FixPermissions =
            "PUID=$(stat -c '%u' " + DataPath + "); " +
            "for dir in shared_prefs files cache; do path=" + DataPath + "/$dir; " +
            "if [ -d \"$path\" ]; then chown -R \"$PUID:$PUID\" \"$path\"; restorecon -R \"$path\"; fi; done; ";
    }
}
