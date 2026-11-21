using System.Text.Json.Nodes;
using ToolAdb.Features.InappTrial;
using ToolAdb.Infrastructure;

namespace ToolAdb.Features.Grok
{
    internal enum GrokAction
    {
        Inspect,
        OpenPricing,
        OpenGrokBot,
        PinAllBest,
        Plus30Off3Mo,
        Plus25Off6Mo,
        PremiumPay1Get3,
        PremiumBfPay1Get2,
        Premium50Off1Mo,
        Premium30Off3Mo,
        Super50Off1Mo,
        Super30Off3Mo,
        UltraPay1Get3,
        Ultra50Off1Mo,
        Ultra30Off3Mo,
        RestoreBase
    }

    internal sealed class GrokService
    {
        private const string GrokPackage = "ai.x.grok";
        private const string GrokBotPackage = "ai.x.grok.bot";
        private static readonly string[] TargetPackages = { GrokPackage, GrokBotPackage };

        private const string PlansDeeplinkCmd =
            "am start -a android.intent.action.VIEW -d 'https://grok.com/plans' " + GrokPackage;

        private const string PlusSku = "x.subscription.premium";
        private const string PremiumSku = "x.subscription.plus";
        private const string SuperSku = "x.subscription.super";
        private const string UltraSku = "x.subscription.ultra";

        private const string OfferDiscount30 = "x-subscription-benefit-discount-30pct";
        private const string OfferPlusLaunch25 = "x-subscription-benefit-plus-launch-25off-6mo";
        private const string OfferPay1Get3 = "x-subscription-benefit-test-pay1get3-premium-ultra";
        private const string OfferBfPay1Get2 = "x-subscription-benefit-bf2026-pay1get2-premium";
        private const string OfferLaunch50 = "x-subscription-benefit-launch-50off-1mo";

        private sealed record PinRule(string SkuId, string OfferId, string TierName, string Description);

        private static readonly Dictionary<GrokAction, PinRule> PinRules = new()
        {
            [GrokAction.Plus30Off3Mo] = new(PlusSku, OfferDiscount30, "Plus", "Plus -30% × 3 tháng (vd 265.99 TRY/T)"),
            [GrokAction.Plus25Off6Mo] = new(PlusSku, OfferPlusLaunch25, "Plus", "Plus -25% × 6 tháng (vd 284.99 TRY/T)"),

            [GrokAction.PremiumPay1Get3] = new(PremiumSku, OfferPay1Get3, "Premium", "Premium 1 trả 3 tháng (vd 1,749.99 TRY/3T)"),
            [GrokAction.PremiumBfPay1Get2] = new(PremiumSku, OfferBfPay1Get2, "Premium", "Premium BF 1 tặng 1 (vd 1,749.99 TRY/2T)"),
            [GrokAction.Premium50Off1Mo] = new(PremiumSku, OfferLaunch50, "Premium", "Premium -50% tháng đầu (vd 875 TRY)"),
            [GrokAction.Premium30Off3Mo] = new(PremiumSku, OfferDiscount30, "Premium", "Premium -30% × 3 tháng (vd 1,224.99 TRY/T)"),

            [GrokAction.Super50Off1Mo] = new(SuperSku, OfferLaunch50, "Super", "Super -50% tháng đầu"),
            [GrokAction.Super30Off3Mo] = new(SuperSku, OfferDiscount30, "Super", "Super -30% × 3 tháng"),

            [GrokAction.UltraPay1Get3] = new(UltraSku, OfferPay1Get3, "Ultra", "Ultra 1 trả 3 tháng"),
            [GrokAction.Ultra50Off1Mo] = new(UltraSku, OfferLaunch50, "Ultra", "Ultra -50% tháng đầu"),
            [GrokAction.Ultra30Off3Mo] = new(UltraSku, OfferDiscount30, "Ultra", "Ultra -30% × 3 tháng"),
        };

        private static readonly Dictionary<string, string> BestOfferBySku = new(StringComparer.Ordinal)
        {
            [PlusSku] = OfferDiscount30,
            [PremiumSku] = OfferPay1Get3,
            [SuperSku] = OfferLaunch50,
            [UltraSku] = OfferPay1Get3,
        };

        private static readonly Dictionary<string, string> TierDisplayBySku = new(StringComparer.Ordinal)
        {
            [PlusSku] = "Plus",
            [PremiumSku] = "Premium",
            [SuperSku] = "Super",
            [UltraSku] = "Ultra",
        };

        private readonly AdbClient _adb;

        internal GrokService(AdbClient adb) => _adb = adb;

        internal string Run(string serial, GrokAction action)
        {
            CaptureAdb(serial, 5000, "root");
            var billing = new InappTrialService(_adb);

            return action switch
            {
                GrokAction.Inspect => Inspect(serial, billing),
                GrokAction.OpenPricing => OpenPricing(serial),
                GrokAction.OpenGrokBot => OpenGrokBot(serial),
                GrokAction.PinAllBest => PinAllBest(serial, billing),
                GrokAction.RestoreBase => RestoreBase(serial, billing),
                _ when PinRules.TryGetValue(action, out var rule) => PinSingleOffer(serial, billing, rule),
                _ => throw new ArgumentOutOfRangeException(nameof(action))
            };
        }

        private static string AppLabel(string pkg) => pkg == GrokBotPackage ? "Grok Bot" : "Grok";

        private string OpenPricing(string serial)
        {
            string outText = CaptureAdb(serial, 8000, "shell", PlansDeeplinkCmd);
            if (outText.Contains("Error:", StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException($"Không mở được bảng giá Grok ({outText.Trim()}).");
            return "💳 Đã mở bảng giá Grok! Chọn các tab Plus / Premium / Super / Ultra → bấm Upgrade (rồi Back) để tải cache vào sku_details.db.";
        }

        private string OpenGrokBot(string serial)
        {
            string outText = CaptureAdb(serial, 8000, "shell", $"monkey -p {GrokBotPackage} -c android.intent.category.LAUNCHER 1");
            if (outText.Contains("No activities found", StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException($"Chưa cài ứng dụng {GrokBotPackage} trên thiết bị.");
            return "🤖 Đã mở Grok Bot! Vào màn hình Upgrade để Play Store tải cache gói vào sku_details.db.";
        }

        private static string Inspect(string serial, InappTrialService billing)
        {
            var lines = new List<string>();
            foreach (string pkg in TargetPackages)
            {
                List<BillingOfferInfo> offers = billing.ScanOffers(serial, pkg)
                    .Where(o => TierDisplayBySku.ContainsKey(o.ProductId))
                    .ToList();

                foreach (var group in offers.GroupBy(o => o.ProductId))
                {
                    string tier = TierDisplayBySku.GetValueOrDefault(group.Key, group.Key);
                    var p1m = group.Where(o => o.BasePlanId == "p1m" && o.Phase == 1).ToList();
                    var p1y = group.FirstOrDefault(o => o.BasePlanId == "p1y" && o.Phase == 1);

                    string monthSummary;
                    if (p1m.Count == 1 && string.IsNullOrEmpty(p1m[0].OfferId))
                    {
                        var renewal = group.FirstOrDefault(o => o.BasePlanId == "p1m" && o.Phase == 2);
                        string renTxt = renewal != null ? $" → gia hạn {renewal.Price}/{renewal.BillingPeriod}" : string.Empty;
                        string tag = renewal != null || p1m[0].BillingPeriod != "P1M" ? "🎯 Đang ghim" : "Gốc";
                        monthSummary = $"{tag}: {p1m[0].Price}/{p1m[0].BillingPeriod}{renTxt}";
                    }
                    else
                    {
                        var promos = p1m.Where(o => !string.IsNullOrEmpty(o.OfferId))
                            .Select(o => $"{o.OfferId}={o.Price}/{o.BillingPeriod}");
                        var baseOffer = p1m.FirstOrDefault(o => string.IsNullOrEmpty(o.OfferId));
                        string baseTxt = baseOffer != null ? $"gốc {baseOffer.Price}" : "";
                        monthSummary = $"{baseTxt} | {p1m.Count} offer ({string.Join(", ", promos)})";
                    }

                    string yearTxt = p1y != null ? $" | Năm: {p1y.Price}" : string.Empty;
                    lines.Add($"• [{AppLabel(pkg)}] {tier} ({group.Key}): {monthSummary}{yearTxt}");
                }
            }

            if (lines.Count == 0)
                return "Chưa có cache x.subscription.* của Grok / Grok Bot trong sku_details.db. Hãy mở bảng giá và bấm Upgrade ở tab gói cần mua (rồi Back lại).";

            return string.Join(Environment.NewLine, lines);
        }

        private static string PinSingleOffer(string serial, InappTrialService billing, PinRule rule)
        {
            var results = new List<string>();
            foreach (string pkg in TargetPackages)
            {
                string res = billing.RunOfferTransform(
                    serial,
                    pkg,
                    (_, srcOffers) => BuildPinnedOffers(srcOffers, rule.OfferId),
                    (updatedRows, _, counts) => counts.GetValueOrDefault("pinned") > 0
                        ? $"{AppLabel(pkg)} ({updatedRows} dòng)"
                        : null,
                    skuIdFilter: rule.SkuId,
                    relaunchApp: true,
                    throwIfNotFound: false);
                if (!string.IsNullOrEmpty(res))
                    results.Add(res);
            }

            if (results.Count == 0)
                throw new InvalidDataException(
                    $"Chưa tìm thấy SKU '{rule.SkuId}' ({rule.TierName}) kèm offer '{rule.OfferId}' trong cache của Grok hay Grok Bot. Hãy mở app → bấm Upgrade ở tab {rule.TierName} (rồi Back lại) hoặc bấm '📦 Về giá gốc' trước.");

            return $"✅ Đã ghim {rule.Description} cho: {string.Join(", ", results)}.";
        }

        private static string PinAllBest(string serial, InappTrialService billing)
        {
            var results = new List<string>();
            foreach (string pkg in TargetPackages)
            {
                var pinnedTiers = new List<string>();
                string res = billing.RunOfferTransform(
                    serial,
                    pkg,
                    (srcProduct, srcOffers) =>
                    {
                        string pid = srcProduct["productId"]?.GetValue<string>() ?? string.Empty;
                        if (!BestOfferBySku.TryGetValue(pid, out string targetOfferId))
                            return (null, null);

                        var built = BuildPinnedOffers(srcOffers, targetOfferId);
                        if (built.filtered != null)
                            pinnedTiers.Add(TierDisplayBySku.GetValueOrDefault(pid, pid));
                        return built;
                    },
                    (updatedRows, _, counts) => counts.GetValueOrDefault("pinned") > 0
                        ? $"{AppLabel(pkg)} [{string.Join("+", pinnedTiers.Distinct())}]"
                        : null,
                    relaunchApp: true,
                    throwIfNotFound: false);
                if (!string.IsNullOrEmpty(res))
                    results.Add(res);
            }

            if (results.Count == 0)
                throw new InvalidDataException("Không tìm thấy offer giảm giá nào của Grok / Grok Bot trong sku_details.db. Hãy mở bảng giá app hoặc bấm '📦 Về giá gốc' trước.");

            return $"⭐ Đã ghim ưu đãi ngon nhất cho: {string.Join(" | ", results)}.";
        }

        private static string RestoreBase(string serial, InappTrialService billing)
        {
            var restored = new List<string>();
            foreach (string pkg in TargetPackages)
            {
                string res = billing.RunOfferTransform(
                    serial,
                    pkg,
                    (_, srcOffers) => (InappTrialService.FilterSubscriptionOffers(srcOffers, winbackMode: false, out string note), note),
                    (updatedRows, totalRows, _) => $"{AppLabel(pkg)} ({updatedRows}/{totalRows} SKU)",
                    relaunchApp: true,
                    throwIfNotFound: false);
                if (!string.IsNullOrEmpty(res))
                    restored.Add(res);
            }

            if (restored.Count == 0)
                throw new InvalidDataException("Chưa có SKU nào của Grok hoặc Grok Bot trong sku_details.db để khôi phục.");

            return $"📦 Đã khôi phục giá gốc (Base): {string.Join(", ", restored)}.";
        }

        /// <summary>
        /// Chỉ giữ duy nhất offer giảm giá mục tiêu cho gói tháng (p1m) và xóa trường "offerId" để cầu nối Billing
        /// của Grok / Grok Bot (complat) khớp với offer mặc định (null) và truyền đúng offerIdToken sang Google Play;
        /// đồng thời giữ lại gói năm gốc (p1y) nếu có.
        /// </summary>
        private static (JsonArray filtered, string note) BuildPinnedOffers(JsonArray srcOffers, string targetOfferId)
        {
            JsonObject matchedPromo = null;
            JsonObject yearlyBase = null;

            foreach (JsonNode node in srcOffers)
            {
                if (node is not JsonObject obj) continue;
                string basePlanId = obj["basePlanId"]?.GetValue<string>() ?? string.Empty;
                string offerId = obj["offerId"]?.GetValue<string>();

                if (basePlanId == "p1m" && string.Equals(offerId, targetOfferId, StringComparison.Ordinal))
                {
                    matchedPromo = (JsonObject)obj.DeepClone();
                    matchedPromo.Remove("offerId");
                }
                else if (basePlanId == "p1y" && string.IsNullOrEmpty(offerId) && yearlyBase == null)
                {
                    yearlyBase = (JsonObject)obj.DeepClone();
                }
            }

            if (matchedPromo == null)
                return (null, null);

            var kept = new JsonArray { matchedPromo };
            if (yearlyBase != null)
                kept.Add(yearlyBase);
            return (kept, "pinned");
        }

        private string CaptureAdb(string serial, int timeoutMs, params string[] args)
        {
            var command = new List<string> { "-s", serial };
            command.AddRange(args);
            return _adb.Capture(command, timeoutMs).stdout ?? "";
        }
    }
}
