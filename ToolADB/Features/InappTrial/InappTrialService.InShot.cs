using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json.Nodes;

namespace ToolAdb.Features.InappTrial
{
    internal sealed partial class InappTrialService
    {
        private const string InShotPackage = "com.camerasideas.instashot";
        private const string InShotYearlySku = "com.camerasideas.instashot.vip.yearly.freetrail";
        private const string InShotWinbackOfferId = "discount-yearly-winback";

        private string PatchInShotYearly50Off(string serial)
        {
            string result = RunOfferTransform(
                serial,
                InShotPackage,
                (_, srcOffers) =>
                {
                    JsonObject picked = null;
                    foreach (JsonNode node in srcOffers)
                    {
                        if (node is not JsonObject obj) continue;
                        string oid = obj["offerId"]?.GetValue<string>() ?? string.Empty;
                        if (string.Equals(oid, InShotWinbackOfferId, StringComparison.Ordinal))
                        {
                            picked = (JsonObject)obj.DeepClone();
                            break;
                        }
                    }

                    if (picked == null && srcOffers.Count == 1 && srcOffers[0] is JsonObject single)
                    {
                        if (string.IsNullOrEmpty(single["offerId"]?.GetValue<string>()) && IsDiscountedMultiPhaseOffer(single))
                            picked = (JsonObject)single.DeepClone();
                    }

                    if (picked == null)
                        return (null, null);

                    // InShot SubscribeProPresenter truyền offerId=null khi tắt Free Trial;
                    // xóa offerId khỏi JSON để BillingManager khớp đúng offerToken của winback 50% (vd TRY 250.00/năm đầu).
                    picked.Remove("offerId");
                    return (new JsonArray { picked }, "inshot_winback");
                },
                (updatedRows, _, counts) => counts.GetValueOrDefault("inshot_winback") > 0
                    ? $"✅ InShot Pro Năm ({updatedRows} SKU): đã ghim ưu đãi giảm 50% ('{InShotWinbackOfferId}', bỏ trial — VD TRY 250.00/năm đầu) và khởi động lại app."
                    : null,
                skuIdFilter: InShotYearlySku,
                relaunchApp: true,
                throwIfNotFound: true);

            if (string.IsNullOrEmpty(result))
                throw new InvalidDataException(
                    $"Không tìm thấy offer '{InShotWinbackOfferId}' trong cache Google Play của InShot. Hãy mở màn hình InShot Pro một lần rồi thử lại.");

            return result;
        }

        private static bool IsDiscountedMultiPhaseOffer(JsonObject offer)
        {
            if (offer?["pricingPhases"] is not JsonArray phases || phases.Count < 2)
                return false;
            if (!long.TryParse(phases[0]?["priceAmountMicros"]?.ToString(), out long introMicros))
                return false;
            if (!long.TryParse(phases[1]?["priceAmountMicros"]?.ToString(), out long renewMicros))
                return false;
            return introMicros > 0 && introMicros < renewMicros;
        }
    }
}
