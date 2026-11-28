using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using ToolAdb.Features.InappTrial;
using ToolAdb.Infrastructure;

namespace ToolAdb.Features.Picsart
{
    internal enum PicsartAction
    {
        Inspect,
        OpenPricing,
        Pro1YearlyNoTrial,
        ProYearlyNoTrial,
        Pro4YearlyIntro,
        ProBf2026Yearly,
        Reset,
        ResetKeepLogin
    }

    internal sealed class PicsartTestPreset
    {
        internal string Key { get; }
        internal string Label { get; }
        internal string Sku { get; }
        internal string OfferId { get; }
        internal bool PinOffer { get; }
        internal bool PinBaseOnly { get; }

        internal PicsartTestPreset(
            string key,
            string label,
            string sku,
            string offerId = "",
            bool pinOffer = false,
            bool pinBaseOnly = false)
        {
            Key = key;
            Label = label;
            Sku = sku;
            OfferId = offerId;
            PinOffer = pinOffer;
            PinBaseOnly = pinBaseOnly;
        }

        public override string ToString() => Label;
    }

    internal sealed class PicsartService
    {
        private const string Package = "com.picsart.studio";
        private const string DataPath = "/data/data/" + Package;
        private const string ProMonthlyBaseSku = "subscription_pro_monthly_100";
        private const string ProYearly132Sku = "subscription_pro_1_yearly";
        private const string ProYearly156Sku = "subscription_pro_yearly_no_trial";
        private readonly AdbClient _adb;

        internal static readonly PicsartTestPreset[] TestPresets =
        {
            new("tr_pro_78", "🏆 Pro 78/155 TRY (Intro -> 155.99)", "picsart_pro_yearly_intro", "intro-price"),
            new("tr_pro_4y", "⚡ Pro 78/155 TRY (4Y Intro 500Cr)", "subscription_pro_4_yearly", "intro-price", pinOffer: true),
            new("tr_pro_109", "🏷️ Pro 109.19 TRY (-30% -> 155.99)", "picsart_pro_yearly_promo", "30-percent-discount"),
            new("tr_pro_124", "🏷️ Pro 124.79 TRY (-20% -> 155.99)", "picsart_pro_yearly_promo", "20-percent-discount"),
            new("tr_pro_140", "🏷️ Pro 140.39 TRY (-10% -> 155.99)", "picsart_pro_yearly_promo", "10-percent-discount"),
            new("tr_pro_120_250", "🎁 Pro 120 TRY (-50% Winback 250Cr)", "sub_pro_yearly_250_winback_bf2026", "discount-50"),
            new("tr_pro_93", "🏷️ Pro 93.59 TRY (Campaign Intro)", "picsart_pro_yearly_promo_campaign", "intro-price"),
            new("tr_pro_132", "📅 Pro 132.50 TRY/năm (No Trial)", "subscription_pro_1_yearly", "", pinBaseOnly: true),
            new("tr_pro_155", "💎 Pro 155.99 TRY/năm (Base No Trial)", "subscription_pro_yearly_no_trial"),
            new("tr_pro_m50", "📆 Pro 64.99 TRY/tháng (monthly_50)", "subscription_pro_monthly_50"),
            new("tr_pro_m100", "📆 Pro 49.99 TRY/tháng (monthly_100)", "subscription_pro_monthly_100"),
            new("tr_gold_59_wb", "🧪 Gold 59.99 TRY/năm (winback 75%)", "subscription_picsart_yearly_winback_75discount"),
            new("tr_gold_59_p7", "🧪 Gold 59.99 TRY/năm (p7)", "subscription_picsart_yearly_p7"),
            new("tr_gold_92", "🧪 Gold 92.99 TRY/năm (am)", "subscription_picsart_yearly_am"),
            new("tr_gold_q3m", "🧪 Gold 29.99 TRY/3T (quarterly_in)", "subscription_picsart_quarterly_in"),
            new("tr_gold_p8", "🧪 Gold 24.99 TRY/tháng (p8)", "subscription_picsart_monthly_p8")
        };

        internal PicsartService(AdbClient adb) => _adb = adb;

        internal static PicsartTestPreset FindTestPreset(string key) =>
            TestPresets.FirstOrDefault(p => string.Equals(p.Key, key, StringComparison.OrdinalIgnoreCase));

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
                        BuildPatchMonthlySnippet(ProMonthlyBaseSku) +
                        FixPermissionsSnippet +
                        "monkey -p com.picsart.studio -c android.intent.category.LAUNCHER 1");
                    Thread.Sleep(4500);
                    return "✅ Đã Fix Dính Pro & xóa token gói cũ (giữ nguyên Gmail & nick Picsart)."
                        + TaintWarning(serial);

                case PicsartAction.Pro1YearlyNoTrial:
                    return ForcePromoPlan(
                        serial, newDeviceId,
                        ProYearly132Sku,
                        preferredOfferId: null,
                        isProTab: true,
                        $"🏆 Đã bỏ trial, xóa token cũ & mở thẳng Pro Năm 132.50 TRY ({ProYearly132Sku})!",
                        directSubscribe: true,
                        pinBaseOnly: true);

                case PicsartAction.ProYearlyNoTrial:
                    return ForcePromoPlan(
                        serial, newDeviceId,
                        ProYearly156Sku,
                        preferredOfferId: null,
                        isProTab: true,
                        $"💎 Đã xóa token cũ & mở thẳng Pro Năm 155.99 TRY ({ProYearly156Sku})!",
                        directSubscribe: true);

                case PicsartAction.Pro4YearlyIntro:
                    return ForcePromoPlan(
                        serial, newDeviceId,
                        "subscription_pro_4_yearly",
                        preferredOfferId: "intro-price",
                        isProTab: true,
                        "⚡ Đã ép Gói Pro Năm Intro (subscription_pro_4_yearly - TRY 78.00/năm × 2, gốc 155.99)!");

                case PicsartAction.ProBf2026Yearly:
                    return ForcePromoPlan(
                        serial, newDeviceId,
                        "sub_pro_yearly_250_winback_bf2026",
                        preferredOfferId: "discount-50",
                        isProTab: true,
                        "🎁 Đã ép Gói Pro Năm BF 50% 250Cr (sub_pro_yearly_250_winback_bf2026 - TRY 120.00/năm)!");

                default:
                    throw new ArgumentOutOfRangeException(nameof(action));
            }
        }

        internal string RunTestPreset(string serial, PicsartTestPreset preset)
        {
            if (preset == null) throw new ArgumentNullException(nameof(preset));

            CaptureAdb(serial, 5000, "root");
            string newDeviceId = GetRandomHex(32);

            if (preset.PinBaseOnly)
            {
                return ForcePromoPlan(
                    serial, newDeviceId, preset.Sku,
                    preferredOfferId: null, isProTab: true,
                    $"🚀 Đã bỏ trial, xóa token cũ & mở test {preset.Label} ({preset.Sku})!",
                    directSubscribe: true, pinBaseOnly: true);
            }

            if (preset.PinOffer)
            {
                return ForcePromoPlan(
                    serial, newDeviceId, preset.Sku,
                    preferredOfferId: preset.OfferId, isProTab: true,
                    $"🚀 Đã xóa token cũ & mở test {preset.Label} ({preset.Sku})!");
            }

            return LaunchDirectTestPlan(serial, newDeviceId, preset);
        }

        private static readonly string[] PromoYearlySkus =
        {
            "subscription_pro_yearly_250_credit_2",
            "subscription_pro_yearly_250_winback",
            "subscription_pro_yearly_250_skip_trial",
            "subscription_pro_yearly_wo_trial_250",
            "subscription_pro_yearly_wo_trial",
            "subscription_pro_yearly_no_trial",
            "subscription_pro_yearly",
            "subscription_plus_tier_yearly",
            "subscription_plus_yearly_without_trial",
            "subscription_ultra_yearly",
            "subscription_ultra_yearly_750_credits",
            "subscription_pro_5_yearly",
            "subscription_pro_4_yearly",
            "sub_pro_yearly_250_winback_bf2026",
            "subscription_pro_1_yearly",
        };

        private static string BuildPromoSettingsSed(string targetSku, bool isProTab)
        {
            var sed = new StringBuilder();
            sed.Append("s/\"ultra_yearly\",\"is_selected\":false/\"pro_yearly_trial\",\"is_selected\":true/g; ");
            sed.Append("s/\"pro_yearly_trial\",\"is_selected\":false/\"pro_yearly_trial\",\"is_selected\":true/g; ");
            sed.Append("s/\"ultra_monthly\",\"is_selected\":true/\"pro_weekly_100\",\"is_selected\":false/g; ");
            sed.Append("s/\"pro_monthly\",\"is_selected\":true/\"pro_weekly_100\",\"is_selected\":false/g; ");
            sed.Append("s/\"pro_weekly_100\",\"is_selected\":true/\"pro_weekly_100\",\"is_selected\":false/g; ");

            foreach (string src in PromoYearlySkus)
            {
                if (src == targetSku) continue;
                sed.Append("s/\"package_sku\":\"").Append(src).Append("\"/\"package_sku\":\"").Append(targetSku).Append("\"/g; ");
                sed.Append("s/\"package_id\":\"").Append(src).Append("\"/\"package_id\":\"").Append(targetSku).Append("\"/g; ");
            }
            if (targetSku == "subscription_pro_1_yearly")
                sed.Append("s/\"cta_text\":\"Continue with 3-day free trial\"/\"cta_text\":\"Continue with 7-day free trial\"/g; ");
            else
                sed.Append("s/\"cta_text\":\"Continue with 7-day free trial\"/\"cta_text\":\"Continue with 3-day free trial\"/g; ");
            if (isProTab)
                sed.Append("s/\"content_id\":\"plus_tab_default\"}/\"content_id\":\"pro_tab_default\"}/g; ");
            return "sed -i '" + sed.ToString().TrimEnd() + "' /data/data/com.picsart.studio/files/settings.json 2>/dev/null; ";
        }

        private string ForcePromoPlan(
            string serial,
            string newDeviceId,
            string targetSku,
            string preferredOfferId,
            bool isProTab,
            string successPrefix,
            bool directSubscribe = false,
            bool pinBaseOnly = false)
        {
            CaptureAdb(serial, 25000, "shell",
                "am force-stop com.picsart.studio; " +
                GetCleanSubscriptionCacheSnippet(newDeviceId) +
                BuildPromoSettingsSed(targetSku, isProTab) +
                FixPermissionsSnippet);

            var billing = new InappTrialService(_adb);
            bool needPin = pinBaseOnly || !string.IsNullOrEmpty(preferredOfferId);
            bool pinned = false;
            if (pinBaseOnly)
                pinned = billing.PinBasePlanForSkuIfCached(serial, Package, targetSku);
            else if (!string.IsNullOrEmpty(preferredOfferId))
                pinned = billing.PinSingleOfferForSkuIfCached(serial, Package, targetSku, preferredOfferId);

            string launchCmd = directSubscribe
                ? DirectSubscribeDeepLinkCmd(targetSku, preferredOfferId)
                : "monkey -p com.picsart.studio -c android.intent.category.LAUNCHER 1";
            CaptureAdb(serial, 8000, "shell", launchCmd);
            Thread.Sleep(directSubscribe ? 5500 : 4500);

            if (needPin && !pinned)
            {
                pinned = pinBaseOnly
                    ? billing.PinBasePlanForSkuIfCached(serial, Package, targetSku)
                    : billing.PinSingleOfferForSkuIfCached(serial, Package, targetSku, preferredOfferId);
                if (pinned)
                {
                    CaptureAdb(serial, 8000, "shell", launchCmd);
                    Thread.Sleep(3500);
                }
            }

            string offerNote = !needPin
                ? ""
                : pinBaseOnly
                    ? (pinned ? " (Đã bỏ trial, giữ base plan trong cache Play)" : " (Chưa thấy cache Play để bỏ trial, bấm lại lần nữa nếu Play vẫn hiện trial 7 ngày)")
                    : (pinned ? $" (Đã ghim offer '{preferredOfferId}' trong cache Play)" : $" (Chưa thấy cache '{preferredOfferId}' trên Play, bấm 'Mở bảng giá' rồi chạy lại nếu chưa hiện đúng giá ưu đãi)");
            return successPrefix + offerNote + TaintWarning(serial);
        }

        private string LaunchDirectTestPlan(string serial, string newDeviceId, PicsartTestPreset preset)
        {
            CaptureAdb(serial, 25000, "shell",
                "am force-stop com.picsart.studio; " +
                GetCleanSubscriptionCacheSnippet(newDeviceId) +
                BuildPatchMonthlySnippet(ProMonthlyBaseSku) +
                "sed -i 's/\"content_id\":\"plus_tab_default\"}/\"content_id\":\"pro_tab_default\"}/g' /data/data/com.picsart.studio/files/settings.json 2>/dev/null; " +
                FixPermissionsSnippet +
                DirectSubscribeDeepLinkCmd(preset.Sku, preset.OfferId));
            Thread.Sleep(5500);

            string offerPart = string.IsNullOrEmpty(preset.OfferId) ? "" : $" [offer: {preset.OfferId}]";
            return $"🚀 Đã xóa token cũ & mở test {preset.Label} ({preset.Sku}{offerPart})!" + TaintWarning(serial);
        }

        private string TaintWarning(string serial)
        {
            string check = CaptureAdb(serial, 5000, "shell",
                "grep -o 'accessScopes.*premium' /data/data/com.picsart.studio/shared_prefs/subscription.shared.preferences.xml 2>/dev/null");
            return !string.IsNullOrEmpty(check) && check.Contains("premium")
                ? Environment.NewLine + "⚠️ LƯU Ý: Tài khoản Picsart đang đăng nhập đã bị dính gói Pro cũ trên server của Picsart! Để mua gói mới không bị lỗi 'Unable to change your subscription plan', hãy bấm '🧹 Xóa dữ liệu' (hoặc đăng xuất nick Picsart này) rồi đăng nhập nick Picsart mới!"
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

        private static string BuildPatchMonthlySnippet(string targetMonthlySku)
        {
            string[] srcSkus =
            {
                "subscription_pro_weekly",
                "subscription_ultra_monthly",
                "subscription_pro_monthly",
                "subscription_pro_1_monthly",
                "subscription_pro_monthly_100",
                "subscription_pro_weekly_100",
                "subscription_ultra_monthly_750_credits",
                "subscription_pro_monthly_50",
                "subscription_pro_monthly_1",
            };
            var sed = new StringBuilder();
            sed.Append("s/\"pro_weekly_100\",\"is_selected\"/\"pro_monthly\",\"is_selected\"/g; ");
            sed.Append("s/\"pro_weekly\",\"is_selected\"/\"pro_monthly\",\"is_selected\"/g; ");
            sed.Append("s/\"ultra_monthly\",\"is_selected\"/\"pro_monthly\",\"is_selected\"/g; ");
            sed.Append("s/\"ultra_yearly\",\"is_selected\"/\"pro_yearly_trial\",\"is_selected\"/g; ");
            sed.Append("s/\"plus_weekly\",\"is_selected\"/\"plus_monthly\",\"is_selected\"/g; ");
            foreach (string src in srcSkus)
            {
                if (src == targetMonthlySku) continue;
                sed.Append("s/\"package_id\":\"").Append(src).Append("\"/\"package_id\":\"").Append(targetMonthlySku).Append("\"/g; ");
                sed.Append("s/\"package_sku\":\"").Append(src).Append("\"/\"package_sku\":\"").Append(targetMonthlySku).Append("\"/g; ");
            }
            sed.Append("s/\"package_id\":\"subscription_plus_weekly\"/\"package_id\":\"subscription_plus_tier_monthly\"/g; ");
            sed.Append("s/title_12/title_3/g; s/subtitle_8/subtitle_2/g");
            return "sed -i '" + sed + "' /data/data/com.picsart.studio/files/settings.json 2>/dev/null; ";
        }

        private const string OpenPaywallDeepLinkCmd =
            "am start -a android.intent.action.VIEW -d 'picsart://subscription?type=full-screen-promotion&subscription_force_enabled=true&source=profile' com.picsart.studio";

        private static string DirectSubscribeDeepLinkCmd(string sku, string offerId = "")
        {
            string offerQuery = string.IsNullOrEmpty(offerId) ? "" : $"&identifier={offerId}";
            return $"am start -a android.intent.action.VIEW -d 'picsart://subscription?type=subscribe&id={sku}{offerQuery}&direct=true' {Package}";
        }

        private string Inspect(string serial)
        {
            string output = Shell(serial, "set -e; " +
                $"for file in {DataPath}/shared_prefs/subscription.shared.preferences.xml {DataPath}/files/settings.json; do " +
                "if [ -f \"$file\" ]; then cat \"$file\"; printf '\\n'; fi; done");

            var lines = new List<string>();
            var plans = new List<string>();
            if (output.Contains("\"package_sku\":\"subscription_pro_yearly_no_trial\"", StringComparison.Ordinal) || output.Contains("\"package_id\":\"subscription_pro_yearly_no_trial\"", StringComparison.Ordinal))
                plans.Add("💎 Pro Năm 155.99 TRY (500Cr/T)");
            if (output.Contains("\"package_sku\":\"subscription_pro_4_yearly\"", StringComparison.Ordinal) || output.Contains("\"package_id\":\"subscription_pro_4_yearly\"", StringComparison.Ordinal))
                plans.Add("⚡ Pro Năm 78/155 TRY (4Y - 500Cr/T)");
            if (output.Contains("\"package_sku\":\"picsart_pro_yearly_intro\"", StringComparison.Ordinal))
                plans.Add("🏆 Pro Năm Intro 78/155 TRY");
            if (output.Contains("\"package_sku\":\"picsart_pro_yearly_promo\"", StringComparison.Ordinal))
                plans.Add("🏷️ Pro Năm Promo (-10/-20/-30%)");
            if (output.Contains("\"package_sku\":\"sub_pro_yearly_250_winback_bf2026\"", StringComparison.Ordinal) || output.Contains("\"package_id\":\"sub_pro_yearly_250_winback_bf2026\"", StringComparison.Ordinal))
                plans.Add("🎁 Pro Năm BF 50% (120 TRY)");
            if (output.Contains("\"package_sku\":\"subscription_pro_1_yearly\"", StringComparison.Ordinal) || output.Contains("\"package_id\":\"subscription_pro_1_yearly\"", StringComparison.Ordinal))
                plans.Add("📅 Pro Năm 132.50 TRY");
            if (output.Contains("\"package_sku\":\"subscription_picsart_monthly_p8\"", StringComparison.Ordinal))
                plans.Add("🧪 Gold 24.99 TRY/T (p8)");
            if (output.Contains("\"package_sku\":\"subscription_pro_monthly_100\"", StringComparison.Ordinal))
                plans.Add("📆 Pro Tháng _100");
            if (output.Contains("\"package_sku\":\"subscription_pro_monthly\"", StringComparison.Ordinal) || output.Contains("\"package_id\":\"subscription_pro_monthly\"", StringComparison.Ordinal))
                plans.Add("gói tháng gốc");
            else if (output.Contains("pro_monthly", StringComparison.Ordinal) || output.Contains("plus_monthly", StringComparison.Ordinal))
                plans.Add("gói tháng");
            if (output.Contains("pro_weekly", StringComparison.Ordinal) || output.Contains("plus_weekly", StringComparison.Ordinal))
                plans.Add("gói tuần (dính A/B test)");
            lines.Add("💳 Bảng giá:  " + (plans.Count > 0
                ? string.Join(" + ", plans)
                : "chưa tải cấu hình (hãy mở Picsart một lần)"));

            if (Regex.IsMatch(output, @"""accessScopes""\s*:\s*\[\s*""premium"""))
                lines.Add("⚠️ Cache còn dấu hiệu premium — nên Reset giữ login trước khi mua gói mới.");

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

        private string CaptureAdb(string serial, int timeoutMs, params string[] args)
        {
            var command = new List<string> { "-s", serial };
            command.AddRange(args);
            return _adb.Capture(command, timeoutMs).stdout ?? "";
        }
    }
}
