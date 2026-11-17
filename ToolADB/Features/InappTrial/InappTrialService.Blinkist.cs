using System;
using System.IO;

namespace ToolAdb.Features.InappTrial
{
    internal sealed partial class InappTrialService
    {
        private const string BlinkistPackage = "com.blinkslabs.blinkist.android";
        private const string BlinkistPro90Sku = "2024.google.pro.12m.intro_offer_90.v1";
        private const string BlinkistPro90Offer = "upgrade-90";
        private const string BlinkistPro90Deeplink =
            "blinkistapp://paywall?paywalls_slot=paywall_deeplink_discount_pro_crm_90&ui_slot_suffix=pro";

        /// <summary>
        /// Mở trực tiếp paywall khuyến mãi 90% OFF (Pro 1 năm, TRY 84.00/năm đầu, không trial)
        /// qua deeplink Flex nội bộ của Blinkist; đồng thời ghim offer 'upgrade-90' trong cache Play nếu đã có sẵn.
        /// </summary>
        private string PatchBlinkistPro90Off(string serial)
        {
            bool pinned = PinSingleOfferForSkuIfCached(serial, BlinkistPackage, BlinkistPro90Sku, BlinkistPro90Offer);

            string launchOut = CaptureAdb(
                serial,
                12000,
                "shell",
                $"am start -a android.intent.action.VIEW -d '{BlinkistPro90Deeplink}' {BlinkistPackage}").Trim();

            if (launchOut.Contains("Error:", StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException(
                    $"Không mở được paywall Blinkist 90% OFF ({launchOut}). Hãy kiểm tra đã cài {BlinkistPackage}.");

            string pinNote = pinned ? " + đã ghim offer trong cache Play" : string.Empty;
            return $"📚 Blinkist Pro 90% OFF (TRY 84.00/năm, bỏ trial): đã mở thẳng màn ưu đãi ({BlinkistPro90Sku}{pinNote}).";
        }
    }
}
