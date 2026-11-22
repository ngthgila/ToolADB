using System.Text.Json.Nodes;

namespace ToolAdb.Features.InappTrial
{
    internal sealed partial class InappTrialService
    {
        // Thứ tự ưu tiên offer giảm 50% cho từng SKU monthly (mới 2026 thu từng tháng x3 -> dự phòng/legacy).
        private static readonly string[] Canva50OffOfferIds =
        {
            "pro-solo-v2-monthly-ghc-2026-3m-50pc",
            "pro-solo-v2-monthly-ramadan-2026-3m-50pc",
            "pro-solo-v2-monthly-ghc-2026-3m50p",
            "business-monthly-ghc-2026-3m-50pc",
            "business-monthly-ghc-2025-3m-50pc",
            "business-monthly-ghc-2026-3m50p"
        };

        /// <summary>
        /// Bật ưu đãi Canva giảm 50% (3 tháng) cho cả Pro và Business trong cache Google Play Billing.
        /// </summary>
        private string PatchCanva50Off(string serial)
        {
            // Trên tài khoản mới chưa dùng trial, nếu còn freetrial hoặc base plan (offerTags: []) thì Canva ưu tiên chúng
            // thay vì offer giảm 50% (tag "pub"). Dùng RunOfferTransform (có snapshot + Play 54.x protobuf) để:
            // - Ở Pro/Business Monthly: chỉ giữ DUY NHẤT offer giảm 50% mục tiêu với offerTags=["pub"].
            // - Ở các SKU khác (Yearly / Pro Lite): chỉ giữ base plan, bỏ freetrial/ocs để không bị nhảy về màn 14 ngày trial.
            return RunOfferTransform(
                serial,
                "com.canva.editor",
                (srcProduct, srcOffers) =>
                {
                    JsonObject targetOffer = FindPreferredOffer(srcOffers, Canva50OffOfferIds);
                    if (targetOffer != null)
                    {
                        targetOffer["offerTags"] = new JsonArray { "pub" };
                        string oid = targetOffer["offerId"]?.GetValue<string>() ?? string.Empty;
                        string bucket = oid.EndsWith("3m50p", StringComparison.Ordinal) ? "canva50_3m" : "canva50_monthly";
                        return (new JsonArray { targetOffer }, bucket);
                    }

                    JsonArray baseOnly = FilterSubscriptionOffers(srcOffers, winbackMode: false, out string _);
                    return (baseOnly, "base");
                },
                (updatedRows, totalRows, counts) =>
                {
                    int hitsMonthly = counts.GetValueOrDefault("canva50_monthly");
                    int hits3m = counts.GetValueOrDefault("canva50_3m");
                    int hits50 = hitsMonthly + hits3m;
                    if (hits50 == 0)
                        throw new InvalidDataException("Không tìm thấy offer giảm 50% phù hợp của Canva trong cache Billing.");
                    string modeLabel = hitsMonthly > 0 && hits3m == 0
                        ? "thu từng tháng x3"
                        : hits3m > 0 && hitsMonthly == 0
                            ? "trả 1 lần 3 tháng"
                            : "3 tháng";
                    return $"🎨 Đã bật ưu đãi Canva 50% ({modeLabel}): {hits50} SKU giảm 50%, cập nhật {updatedRows}/{totalRows} SKU.";
                });
        }

        private static JsonObject FindPreferredOffer(JsonArray srcOffers, string[] preferredIds)
        {
            foreach (string preferredId in preferredIds)
            {
                foreach (JsonNode node in srcOffers)
                {
                    if (node is not JsonObject obj) continue;
                    string oid = obj["offerId"]?.GetValue<string>() ?? string.Empty;
                    if (string.Equals(oid, preferredId, StringComparison.Ordinal))
                        return (JsonObject)obj.DeepClone();
                }
            }
            return null;
        }
    }
}
