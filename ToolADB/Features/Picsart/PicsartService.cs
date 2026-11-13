using System.Security.Cryptography;
using System.Text.RegularExpressions;
using ToolAdb.Infrastructure;

namespace ToolAdb.Features.Picsart
{
    internal enum PicsartAction { Inspect, OpenPricing, PlusMonthly, ProMonthly, Reset, ResetKeepLogin }

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
                if (Shell(serial, "pm clear " + Package).Trim() != "Success")
                    throw new InvalidOperationException("Picsart không xác nhận đã xóa dữ liệu.");

                string preferences = DataPath + "/shared_prefs";
                string file = preferences + "/com.picsart.analytics.xml";
                Shell(serial, $"set -e; mkdir -p {preferences}; " +
                    $"printf '%s' '<map><string name=\"device_id\">{id}</string></map>' > {file}; " +
                    $"chmod 660 {file}; " + FixPermissions);
                Launch(serial);
                return "Đã xóa dữ liệu Picsart và gửi lệnh mở lại.";
            }

            // Check the settings file before making any of the optional preference changes.
            Shell(serial, $"test -f {DataPath}/files/settings.json || {{ echo 'Chưa có settings.json của Picsart' >&2; exit 1; }}");
            if (action == PicsartAction.ResetKeepLogin)
                ResetPreferences(serial, id);

            string tabPatch = action switch
            {
                PicsartAction.PlusMonthly => @"s/content_id\(.,.\)pro_tab_default\(.\)}/content_id\1plus_tab_default\2}/g",
                PicsartAction.ProMonthly => @"s/content_id\(.,.\)plus_tab_default\(.\)}/content_id\1pro_tab_default\2}/g",
                _ => ""
            };
            Shell(serial, "set -e; " + PatchMonthly +
                (tabPatch.Length == 0 ? "" : $"sed -i '{tabPatch}' {DataPath}/files/settings.json; ") +
                FixPermissions);
            Launch(serial);
            return "Đã chạy lệnh cập nhật cấu hình cục bộ và gửi lệnh mở lại.";
        }

        private void ResetPreferences(string serial, string id)
        {
            Shell(serial, "set -e; " +
                $"for file in {DataPath}/shared_prefs/com.picsart.analytics.xml {DataPath}/shared_prefs/participated_experiments_prefs_key.xml {DataPath}/cache/bugsnag/user-info; do " +
                $"if [ -f \"$file\" ]; then sed -i 's/d_[0-9a-f]\\{{32\\}}/{id}/g' \"$file\"; fi; done; " +
                EditIfPresent("com.picsart.analytics.tag_settings_cache.xml", "s/\"subscription_status\":\"subscribed\"/\"subscription_status\":\"not_subscribed\"/g") +
                EditIfPresent("com.picsart.studio_preferences.xml", "s/app_start_subscription_status\" value=\"true/app_start_subscription_status\" value=\"false/g; s/SUBSCRIPTION\" value=\"true/SUBSCRIPTION\" value=\"false/g") +
                $"rm -f {DataPath}/shared_prefs/subscription.shared.preferences.xml {DataPath}/shared_prefs/com.facebook.internal.iap.IAP_CACHE_GPBLV2V7.xml; ");
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
            string google = Shell(serial, "dumpsys account | grep 'type=com.google' | head -n 1");
            string picsart = Shell(serial, $"if [ -f {DataPath}/files/user_data ]; then " +
                $"grep -o '\"email\"[[:space:]]*:[[:space:]]*\"[^\"]*\"' {DataPath}/files/user_data | head -n 1; fi");
            var status = new List<string>();
            Match googleAccount = Regex.Match(google, @"name=([^,]+),\s*type=com\.google");
            Match picsartAccount = Regex.Match(picsart, @"""email""\s*:\s*""([^""]+)""");
            if (googleAccount.Success) status.Add("Google: " + googleAccount.Groups[1].Value);
            if (picsartAccount.Success) status.Add("Picsart: " + picsartAccount.Groups[1].Value);
            int accountFields = status.Count;
            if (Regex.IsMatch(output, @"""accessScopes""\s*:\s*\[\s*""premium"""))
                status.Add("Có dấu hiệu premium trong cache");
            if (output.Contains("pro_weekly", StringComparison.Ordinal) || output.Contains("plus_weekly", StringComparison.Ordinal))
                status.Add("Có cấu hình tuần");
            if (output.Contains("pro_monthly", StringComparison.Ordinal) || output.Contains("plus_monthly", StringComparison.Ordinal))
                status.Add("Có cấu hình tháng");
            if (status.Count == accountFields) status.Add("Chưa thấy dấu hiệu gói trong dữ liệu cục bộ.");
            return string.Join(" · ", status);
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

        private const string PatchMonthly =
            @"sed -i 's/pro_weekly_100\(.,.\)is_selected/pro_monthly\1is_selected/g; " +
            @"s/pro_weekly\(.,.\)is_selected/pro_monthly\1is_selected/g; " +
            @"s/plus_weekly\(.,.\)is_selected/plus_monthly\1is_selected/g; " +
            @"s/package_id\(.,.\)subscription_pro_weekly/package_id\1subscription_pro_monthly/g; " +
            @"s/package_id\(.,.\)subscription_plus_weekly/package_id\1subscription_plus_tier_monthly/g' " +
            DataPath + "/files/settings.json; ";

        private const string FixPermissions =
            "PUID=$(stat -c '%u' " + DataPath + "); " +
            "for dir in shared_prefs files cache; do path=" + DataPath + "/$dir; " +
            "if [ -d \"$path\" ]; then chown -R \"$PUID:$PUID\" \"$path\"; restorecon -R \"$path\"; fi; done; ";
    }
}
