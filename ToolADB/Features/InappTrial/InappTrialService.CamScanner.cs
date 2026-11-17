using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json.Nodes;

namespace ToolAdb.Features.InappTrial
{
    internal sealed partial class InappTrialService
    {
        private const string CamScannerPackage = "com.intsig.camscanner";
        private const string CamScannerGuide2YearlySku = "com.intsig.camscanner.yearly.svip.3trail.guide2";
        private const string CamScanner149OfferId = "firstyeardiscount";

        /// <summary>
        /// Ghim offer 'firstyeardiscount' (TRY 149.00/năm, bỏ trial - trả tiền luôn) cho CamScanner
        /// trong cache Google Play Billing và khởi động lại app.
        /// </summary>
        private string PatchCamScanner149Try(string serial)
        {
            const string productInfoSp = "/data/data/" + CamScannerPackage + "/shared_prefs/cs_product_info_sp.xml";
            string prefsPatchCmd =
                $"am force-stop com.android.vending; am force-stop {CamScannerPackage}; " +
                $"if [ -f {productInfoSp} ]; then " +
                $"sed -i 's/\\(price_list_style[^:]*:\\)[0-9][0-9]*/\\1113/g' {productInfoSp}; " +
                $"CUID=$(stat -c '%u' /data/data/{CamScannerPackage} 2>/dev/null); " +
                $"if [ -n \"$CUID\" ]; then chown $CUID:$CUID {productInfoSp} 2>/dev/null; chmod 660 {productInfoSp} 2>/dev/null; restorecon {productInfoSp} 2>/dev/null; fi; " +
                $"fi";

            CaptureAdb(serial, 15000, "shell", prefsPatchCmd);

            return RunOfferTransform(
                serial,
                CamScannerPackage,
                (_, srcOffers) =>
                {
                    foreach (JsonNode node in srcOffers)
                    {
                        if (node is not JsonObject obj) continue;
                        string oid = obj["offerId"]?.GetValue<string>() ?? string.Empty;
                        if (string.Equals(oid, CamScanner149OfferId, StringComparison.Ordinal))
                            return (new JsonArray { obj.DeepClone() }, "pinned");
                    }
                    return (null, null);
                },
                (updatedRows, totalRows, counts) =>
                {
                    if (counts.GetValueOrDefault("pinned") == 0)
                        throw new InvalidDataException($"Không tìm thấy offer '{CamScanner149OfferId}' của '{CamScannerGuide2YearlySku}' trong cache Google Play.");
                    return $"📄 CamScanner: Đã ghim gói năm TRY 149.00/năm (bỏ trial, {CamScannerGuide2YearlySku} / {CamScanner149OfferId}) [{updatedRows}/{totalRows} SKU].";
                },
                skuIdFilter: CamScannerGuide2YearlySku,
                relaunchApp: true,
                throwIfNotFound: true);
        }
    }
}
