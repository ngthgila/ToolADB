using System;
using System.IO;
using System.Threading;

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
        /// qua deeplink Flex nội bộ của Blinkist; đồng thời ghim offer 'upgrade-90' kèm base plan gốc trong cache Play.
        /// </summary>
        private string PatchBlinkistPro90Off(string serial)
        {
            bool pinned = PinSingleOfferForSkuIfCached(
                serial, BlinkistPackage, BlinkistPro90Sku, BlinkistPro90Offer, preserveBasePlan: true);

            if (!pinned)
            {
                // Nếu chưa có cache hoặc cache cũ bị thiếu base plan (từ bản cũ chỉ giữ 1 offer),
                // xóa dòng SKU hỏng để Play Store tải lại đầy đủ offer gốc khi mở deeplink.
                ClearIncompleteSkuCache(serial, BlinkistPackage, BlinkistPro90Sku);
            }

            string launchOut = LaunchBlinkistPro90Deeplink(serial);

            if (!pinned)
            {
                // Chờ Play Store fetch & ghi xong ProductDetails vào sku_details.db rồi ghim lại upgrade-90 + base plan.
                Thread.Sleep(6500);
                pinned = PinSingleOfferForSkuIfCached(
                    serial, BlinkistPackage, BlinkistPro90Sku, BlinkistPro90Offer, preserveBasePlan: true);
                if (pinned)
                    launchOut = LaunchBlinkistPro90Deeplink(serial);
            }

            string pinNote = pinned
                ? " + đã ghim upgrade-90 & base plan trong cache Play"
                : " (chưa ghim được offer trong cache Play, hãy thử bấm lại)";
            return $"📚 Blinkist Pro 90% OFF (TRY 84.00/năm, bỏ trial): đã mở thẳng màn ưu đãi ({BlinkistPro90Sku}{pinNote}).";
        }

        private string LaunchBlinkistPro90Deeplink(string serial)
        {
            string launchOut = CaptureAdb(
                serial,
                12000,
                "shell",
                $"am start -a android.intent.action.VIEW -d '{BlinkistPro90Deeplink}' {BlinkistPackage}").Trim();

            if (launchOut.Contains("Error:", StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException(
                    $"Không mở được paywall Blinkist 90% OFF ({launchOut}). Hãy kiểm tra đã cài {BlinkistPackage}.");

            return launchOut;
        }

        private void ClearIncompleteSkuCache(string serial, string packageName, string skuId)
        {
            CaptureAdb(
                serial,
                8000,
                "shell",
                $"am force-stop {packageName}; am force-stop com.android.vending; " +
                $"if [ -f {PlayBillingDb} ] && command -v sqlite3 >/dev/null 2>&1; then " +
                $"sqlite3 {PlayBillingDb} \"PRAGMA wal_checkpoint(TRUNCATE); DELETE FROM cache_table WHERE cache_key LIKE '%callingPackageName={packageName}#%' AND cache_key LIKE '%skuId={skuId}#%' AND cache_key LIKE '%skuType=subs%'; PRAGMA wal_checkpoint(TRUNCATE);\"; " +
                $"VUID=$(stat -c '%u' /data/data/com.android.vending); chown $VUID:$VUID {PlayBillingDb}* 2>/dev/null; restorecon {PlayBillingDb}* 2>/dev/null; fi");
        }
    }
}
