using System.Text.Json.Nodes;

namespace ToolAdb.Features.InappTrial
{
    internal sealed partial class InappTrialService
    {
        private string PatchCanva50Off(string serial, bool monthlyInstallments)
        {
            const string pro3mOffer = "pro-solo-v2-monthly-ghc-2026-3m50p";
            const string biz3mOffer = "business-monthly-ghc-2026-3m50p";
            const string bizMonthlyOffer = "business-monthly-ghc-2025-3m-50pc";

            string activeBizOffer = monthlyInstallments ? bizMonthlyOffer : biz3mOffer;
            string modeLabel = monthlyInstallments ? "thu từng tháng (105k/tháng x3)" : "trả 1 lần 3 tháng";

            // Trên tài khoản mới chưa dùng trial, nếu còn freetrial hoặc base plan (offerTags: []) thì Canva ưu tiên chúng
            // thay vì offer giảm 50% (tag "pub"). Dùng RunOfferTransform (có snapshot + Play 54.x protobuf) để:
            // - Ở Pro/Business Monthly: chỉ giữ DUY NHẤT offer giảm 50% mục tiêu với offerTags=["pub"].
            // - Ở các SKU khác (Yearly / Pro Lite): chỉ giữ base plan, bỏ freetrial/ocs để không bị nhảy về màn 14 ngày trial.
            return RunOfferTransform(
                serial,
                "com.canva.editor",
                (srcProduct, srcOffers) =>
                {
                    JsonObject targetOffer = null;
                    foreach (JsonNode node in srcOffers)
                    {
                        if (node is not JsonObject obj) continue;
                        string oid = obj["offerId"]?.GetValue<string>() ?? string.Empty;
                        if (oid == pro3mOffer || oid == activeBizOffer)
                        {
                            targetOffer = (JsonObject)obj.DeepClone();
                            break;
                        }
                    }

                    if (targetOffer != null)
                    {
                        targetOffer["offerTags"] = new JsonArray { "pub" };
                        return (new JsonArray { targetOffer }, "canva50");
                    }

                    JsonArray baseOnly = FilterSubscriptionOffers(srcOffers, winbackMode: false, out string _);
                    return (baseOnly, "base");
                },
                (updatedRows, totalRows, counts) =>
                {
                    int hits50 = counts.GetValueOrDefault("canva50");
                    if (hits50 == 0)
                        throw new InvalidDataException("Không tìm thấy offer giảm 50% phù hợp của Canva trong cache Billing.");
                    return $"🎨 Đã bật ưu đãi Canva 50% ({modeLabel}): {hits50} SKU giảm 50%, cập nhật {updatedRows}/{totalRows} SKU.";
                });
        }
    }
}
