using System.Text.Json.Nodes;

namespace ToolAdb.Features.InappTrial
{
    internal sealed partial class InappTrialService
    {
        private const string CamScannerPackage = "com.intsig.camscanner";
        private const string CamScannerNewYearlySku = "com.intsig.camscanner.newyearly";
        private const string CamScannerGuide2YearlySku = "com.intsig.camscanner.yearly.svip.3trail.guide2";

        /// <summary>
        /// Ghim offer giảm giá năm đầu không trial cho CamScanner trong cache Play và điều chỉnh
        /// price_list_style cục bộ để mở đúng paywall chứa SKU mục tiêu.
        /// </summary>
        private string PatchCamScannerOffer(
            string serial,
            string targetSku,
            string targetOfferId,
            string priceLabel,
            int priceListStyle)
        {
            const string sharedPrefsDir = "/data/data/" + CamScannerPackage + "/shared_prefs";
            string productInfoSp = sharedPrefsDir + "/cs_product_info_sp.xml";
            string mainPrefsSp = sharedPrefsDir + "/com.intsig.camscanner_preferences.xml";

            // Với 80/99 TRY (newyearly), chuyển price_list_style sang 124 để nút nâng cấp mở trang dự phòng trỏ thẳng
            // tới com.intsig.camscanner.newyearly, đồng thời đẩy các mốc kiểm tra cập nhật ra xa để app không ghi đè lại.
            string prefsPatchCmd =
                $"am force-stop com.android.vending; am force-stop {CamScannerPackage}; " +
                $"if [ -f {productInfoSp} ]; then " +
                $"sed -i 's/\\(price_list_style[^:]*:\\)[0-9][0-9]*/\\1{priceListStyle}/g' {productInfoSp}; " +
                $"fi; " +
                (priceListStyle == 124
                    ? $"if [ -f {mainPrefsSp} ]; then " +
                      $"sed -i 's/name=\"last_check_pay_version_time\" value=\"[0-9]*\"/name=\"last_check_pay_version_time\" value=\"2099999999999\"/g; s/name=\"setting_last_update_time\" value=\"[0-9]*\"/name=\"setting_last_update_time\" value=\"2099999999999\"/g' {mainPrefsSp}; " +
                      $"fi; "
                    : string.Empty) +
                $"CUID=$(stat -c '%u' /data/data/{CamScannerPackage} 2>/dev/null); " +
                $"if [ -n \"$CUID\" ]; then " +
                $"chown $CUID:$CUID {productInfoSp} {mainPrefsSp} 2>/dev/null; " +
                $"chmod 660 {productInfoSp} {mainPrefsSp} 2>/dev/null; " +
                $"restorecon {productInfoSp} {mainPrefsSp} 2>/dev/null; " +
                $"fi";

            CaptureAdb(serial, 15000, "shell", prefsPatchCmd);

            return RunOfferTransform(
                serial,
                CamScannerPackage,
                (srcProduct, srcOffers) =>
                {
                    foreach (JsonNode node in srcOffers)
                    {
                        if (node is not JsonObject obj) continue;
                        string oid = obj["offerId"]?.GetValue<string>() ?? string.Empty;
                        if (string.Equals(oid, targetOfferId, StringComparison.Ordinal))
                            return (new JsonArray { obj.DeepClone() }, "pinned");
                    }
                    return (null, null);
                },
                (updatedRows, totalRows, counts) =>
                {
                    if (counts.GetValueOrDefault("pinned") == 0)
                        throw new InvalidDataException($"Không tìm thấy offer '{targetOfferId}' của '{targetSku}' trong cache Google Play.");
                    return $"📄 CamScanner: Đã ghim gói năm {priceLabel} (bỏ trial, {targetSku} / {targetOfferId}) [{updatedRows}/{totalRows} SKU].";
                },
                skuIdFilter: targetSku,
                relaunchApp: true,
                throwIfNotFound: true);
        }
    }
}
