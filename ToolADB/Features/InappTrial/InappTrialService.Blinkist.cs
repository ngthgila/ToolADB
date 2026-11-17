using System.IO;

namespace ToolAdb.Features.InappTrial
{
    internal sealed partial class InappTrialService
    {
        private const string BlinkistPackage = "com.blinkslabs.blinkist.android";
        private const string BlinkistPro90Sku = "2024.google.pro.12m.intro_offer_90.v1";
        private const string BlinkistPro90Offer = "upgrade-90";

        private static readonly string[] BlinkistReplaceableSkus =
        {
            "2024.google.pro.12m.intro_offer_25.v2",
            "2026.google.premium.12m.intro_offer_25",
            "2024.google.pro.12m.no_trial.v2",
            "2023.google.premium.12m.no_trial.v1",
            "2024.google.pro.12m.trial.v2",
            "2024.google.pro.1m.no_trial.v2",
        };

        /// <summary>
        /// Ép toàn bộ các paywall phổ biến của Blinkist (gồm nút Your Gift và paywall khóa nội dung)
        /// trỏ sang SKU Pro 1 năm giảm 90% (TRY 84.00/năm đầu, không trial) và ghim offer trong cache Play nếu có.
        /// </summary>
        private string PatchBlinkistPro90Off(string serial)
        {
            string prefsFile = $"/data/data/{BlinkistPackage}/shared_prefs/Blinkist.xml";

            var sedParts = new List<string>
            {
                "s/\\(name=\"LastSyncedTime\">\\)[0-9]\\{4\\}-/\\12099-/g",
                "s/\\(name=\"MultiUserPlanInfoLastFetchedAt\">\\)[0-9]\\{4\\}-/\\12099-/g",
                "s/intro-offer-25/" + BlinkistPro90Offer + "/g",
                "s/premium-yearly-25-off/" + BlinkistPro90Offer + "/g",
            };
            foreach (string src in BlinkistReplaceableSkus)
                sedParts.Add("s/" + src.Replace(".", "\\.") + "/" + BlinkistPro90Sku + "/g");

            string sedScript = string.Join("; ", sedParts);
            string cmd =
                $"am force-stop com.android.vending; am force-stop {BlinkistPackage}; " +
                $"if [ ! -f {prefsFile} ]; then echo MISSING_BLINKIST_XML; exit 0; fi; " +
                $"sed -i '{sedScript}' {prefsFile}; " +
                $"BUID=$(stat -c '%u' /data/data/{BlinkistPackage} 2>/dev/null); " +
                $"if [ -n \"$BUID\" ]; then chown $BUID:$BUID {prefsFile}; fi; " +
                $"chmod 660 {prefsFile} 2>/dev/null; restorecon {prefsFile} 2>/dev/null; " +
                $"grep -o '{BlinkistPro90Sku}' {prefsFile} | wc -l";

            string output = CaptureAdb(serial, 15000, "shell", cmd).Trim();
            if (output.Contains("MISSING_BLINKIST_XML", StringComparison.Ordinal))
                throw new InvalidDataException("Không tìm thấy Blinkist.xml. Hãy mở app Blinkist đăng nhập vào màn chính trước rồi thử lại.");

            bool pinned = PinSingleOfferForSkuIfCached(serial, BlinkistPackage, BlinkistPro90Sku, BlinkistPro90Offer);

            CaptureAdb(serial, 8000, "shell", $"monkey -p {BlinkistPackage} -c android.intent.category.LAUNCHER 1");

            string pinNote = pinned ? " + đã ghim offer trong cache Play" : " (bấm Your Gift trong app để mở gói 90% OFF)";
            return $"📚 Blinkist Pro 90% OFF (TRY 84.00/năm, bỏ trial): đã ép {BlinkistPro90Sku}{pinNote}.";
        }
    }
}
