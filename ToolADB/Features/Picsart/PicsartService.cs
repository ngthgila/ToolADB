using System.Security.Cryptography;
using System.Text.RegularExpressions;
using ToolAdb.Infrastructure;

namespace ToolAdb.Features.Picsart
{
    internal enum PicsartAction { Inspect, OpenPricing, ProMonthly, UltraMonthly, Reset, ResetKeepLogin }

    // Port nguyên văn ProcessSingleDevice của Inapp-Patch/PicsartToolGUI.cs cho các mode
    // deeplink / monthly / ultra / full / keep: mỗi mode là MỘT lệnh adb shell nối đúng thứ tự
    // snippet gốc (force-stop → clean → patch → fix quyền → monkey), không set -e, không kiểm tra trước;
    // adb gọi qua CaptureAdb (không kiểm tra exit code) đúng như RunAdb của mã gốc.
    // Inspect là phần riêng của ToolADB (chỉ đọc).
    internal sealed class PicsartService
    {
        private const string Package = "com.picsart.studio";
        private const string DataPath = "/data/data/" + Package;
        private readonly AdbClient _adb;

        internal PicsartService(AdbClient adb) => _adb = adb;

        internal string Run(string serial, PicsartAction action)
        {
            if (action == PicsartAction.Inspect)
            {
                EnsureRoot(serial);
                return Inspect(serial);
            }

            CaptureAdb(serial, 5000, "root");
            string newDeviceId = GetRandomHex(32);

            switch (action)
            {
                case PicsartAction.OpenPricing:
                    CaptureAdb(serial, 8000, "shell", OpenPaywallDeepLinkCmd);
                    return "💎 Đã bắn DeepLink mở thẳng màn hình Bảng giá!";

                case PicsartAction.UltraMonthly:
                    CaptureAdb(serial, 25000, "shell",
                        "am force-stop com.picsart.studio; " +
                        GetCleanSubscriptionCacheSnippet(newDeviceId) +
                        PatchUltraSnippet +
                        FixPermissionsSnippet +
                        "monkey -p com.picsart.studio -c android.intent.category.LAUNCHER 1");
                    Thread.Sleep(4500);
                    return "🔥 Đã ép Gói Ultra 1 Tháng (subscription_ultra_monthly - 1500 Credit/tháng) & xóa token thuê bao cũ thành công!"
                        + TaintWarning(serial);

                case PicsartAction.ProMonthly:
                    CaptureAdb(serial, 25000, "shell",
                        "am force-stop com.picsart.studio; " +
                        GetCleanSubscriptionCacheSnippet(newDeviceId) +
                        PatchMonthlySnippet +
                        "sed -i 's/\"content_id\":\"plus_tab_default\"}/\"content_id\":\"pro_tab_default\"}/g' /data/data/com.picsart.studio/files/settings.json 2>/dev/null; " +
                        FixPermissionsSnippet +
                        "monkey -p com.picsart.studio -c android.intent.category.LAUNCHER 1");
                    Thread.Sleep(4500);
                    return "📅 Đã ép cấu hình Gói Pro 1 Tháng (500 Credit) & xóa token thuê bao cũ thành công!"
                        + TaintWarning(serial);

                case PicsartAction.Reset:
                    CaptureAdb(serial, 25000, "shell",
                        "cp /data/data/com.picsart.studio/files/settings.json /sdcard/.picsart_settings_bak.json 2>/dev/null; " +
                        "pm clear com.picsart.studio; " +
                        "mkdir -p /data/data/com.picsart.studio/shared_prefs /data/data/com.picsart.studio/files; " +
                        "if [ -f /sdcard/.picsart_settings_bak.json ]; then cp /sdcard/.picsart_settings_bak.json /data/data/com.picsart.studio/files/settings.json; rm -f /sdcard/.picsart_settings_bak.json; fi; " +
                        "printf '<?xml version=\"1.0\" encoding=\"utf-8\" standalone=\"yes\" ?>\\n<map>\\n    <string name=\"device_id\">d_" + newDeviceId + "</string>\\n</map>\\n' > /data/data/com.picsart.studio/shared_prefs/com.picsart.analytics.xml; " +
                        "chmod 660 /data/data/com.picsart.studio/shared_prefs/com.picsart.analytics.xml; " +
                        FixPermissionsSnippet +
                        "monkey -p com.picsart.studio -c android.intent.category.LAUNCHER 1");
                    Thread.Sleep(4500);
                    return "✅ Đã Reset Picsart: xóa sạch liên kết gói cũ, giữ Gmail, giữ cấu hình bảng giá, cấp device_id mới.";

                case PicsartAction.ResetKeepLogin:
                    CaptureAdb(serial, 25000, "shell",
                        "am force-stop com.picsart.studio; " +
                        GetCleanSubscriptionCacheSnippet(newDeviceId) +
                        PatchMonthlySnippet +
                        FixPermissionsSnippet +
                        "monkey -p com.picsart.studio -c android.intent.category.LAUNCHER 1");
                    Thread.Sleep(4500);
                    return "✅ Đã Fix Dính Pro + Ép Gói 1 Tháng (giữ nguyên Gmail & nick Picsart)."
                        + TaintWarning(serial);

                default:
                    throw new ArgumentOutOfRangeException(nameof(action));
            }
        }

        // Port CheckAndWarnIfAccountTainted của mã gốc.
        private string TaintWarning(string serial)
        {
            string check = CaptureAdb(serial, 5000, "shell",
                "grep -o 'accessScopes.*premium' /data/data/com.picsart.studio/shared_prefs/subscription.shared.preferences.xml 2>/dev/null");
            return !string.IsNullOrEmpty(check) && check.Contains("premium")
                ? Environment.NewLine + "⚠️ LƯU Ý: Tài khoản Picsart đang đăng nhập đã bị dính gói Pro cũ trên server của Picsart! Để mua gói mới không bị lỗi 'Unable to change your subscription plan', hãy bấm '🧹 Xóa dữ liệu Picsart' (hoặc đăng xuất nick Picsart này) rồi đăng nhập nick Picsart mới!"
                : "";
        }

        private static string GetRandomHex(int length) =>
            Convert.ToHexString(RandomNumberGenerator.GetBytes(length / 2)).ToLowerInvariant();

        private const string FixPermissionsSnippet =
            "PUID=$(stat -c '%u' /data/data/com.picsart.studio); " +
            "chown -R $PUID:$PUID /data/data/com.picsart.studio/shared_prefs /data/data/com.picsart.studio/files /data/data/com.picsart.studio/cache 2>/dev/null; " +
            "restorecon -R /data/data/com.picsart.studio 2>/dev/null; ";

        private static string GetCleanSubscriptionCacheSnippet(string newDeviceId) =>
            "sed -i 's/d_[0-9a-f]\\{32\\}/d_" + newDeviceId + "/g' /data/data/com.picsart.studio/shared_prefs/com.picsart.analytics.xml /data/data/com.picsart.studio/shared_prefs/participated_experiments_prefs_key.xml /data/data/com.picsart.studio/cache/bugsnag/user-info 2>/dev/null; " +
            "sed -i 's/\"subscription_status\":\"subscribed\"/\"subscription_status\":\"not_subscribed\"/g' /data/data/com.picsart.studio/shared_prefs/com.picsart.analytics.tag_settings_cache.xml 2>/dev/null; " +
            "sed -i 's/app_start_subscription_status\" value=\"true/app_start_subscription_status\" value=\"false/g; s/SUBSCRIPTION\" value=\"true/SUBSCRIPTION\" value=\"false/g' /data/data/com.picsart.studio/shared_prefs/com.picsart.studio_preferences.xml 2>/dev/null; " +
            "rm -f /data/data/com.picsart.studio/files/device-id /data/data/com.picsart.studio/files/internal-device-id /data/data/com.picsart.studio/shared_prefs/subscription.shared.preferences.xml /data/data/com.picsart.studio/shared_prefs/com.facebook.internal.iap.IAP_CACHE_GPBLV2V7.xml; ";

        private const string PatchMonthlySnippet =
            "sed -i 's/\"pro_weekly_100\",\"is_selected\"/\"pro_monthly\",\"is_selected\"/g; " +
            "s/\"pro_weekly\",\"is_selected\"/\"pro_monthly\",\"is_selected\"/g; " +
            "s/\"ultra_monthly\",\"is_selected\"/\"pro_monthly\",\"is_selected\"/g; " +
            "s/\"ultra_yearly\",\"is_selected\"/\"pro_yearly_trial\",\"is_selected\"/g; " +
            "s/\"plus_weekly\",\"is_selected\"/\"plus_monthly\",\"is_selected\"/g; " +
            "s/\"package_id\":\"subscription_pro_weekly\"/\"package_id\":\"subscription_pro_monthly\"/g; " +
            "s/\"package_id\":\"subscription_ultra_monthly\"/\"package_id\":\"subscription_pro_monthly\"/g; " +
            "s/\"package_id\":\"subscription_plus_weekly\"/\"package_id\":\"subscription_plus_tier_monthly\"/g; " +
            "s/title_12/title_3/g; s/subtitle_8/subtitle_2/g; " +
            "s/\"package_sku\":\"subscription_pro_monthly_100\"/\"package_sku\":\"subscription_pro_monthly\"/g; " +
            "s/\"package_sku\":\"subscription_pro_1_monthly\"/\"package_sku\":\"subscription_pro_monthly\"/g' /data/data/com.picsart.studio/files/settings.json 2>/dev/null; ";

        private const string PatchUltraSnippet =
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
            "s/title_3/title_12/g; s/subtitle_2/subtitle_8/g' /data/data/com.picsart.studio/files/settings.json 2>/dev/null; ";

        private const string OpenPaywallDeepLinkCmd =
            "am start -a android.intent.action.VIEW -d 'picsart://subscription?type=full-screen-promotion&subscription_force_enabled=true&source=profile' com.picsart.studio";

        // ----- Phần riêng của ToolADB: chỉ đọc -----

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

        // Giống RunAdb của Inapp-Patch: trả stdout, không kiểm tra exit code.
        private string CaptureAdb(string serial, int timeoutMs, params string[] args)
        {
            var command = new List<string> { "-s", serial };
            command.AddRange(args);
            return _adb.Capture(command, timeoutMs).stdout ?? "";
        }
    }
}
