using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using ToolAdb.Features.InappTrial;
using ToolAdb.Infrastructure;

namespace ToolAdb.Features.Grok
{
    [Flags]
    internal enum GrokTargets
    {
        None = 0,
        Grok = 1,
        GrokBot = 2,
        Both = Grok | GrokBot
    }

    internal enum GrokAction
    {
        Inspect,
        OpenPricing,
        OpenGrokBot,
        ClearPlayCache,
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

            [GrokAction.Super50Off1Mo] = new(SuperSku, OfferLaunch50, "Super", "Super -50% tháng đầu (vd 3,150 TRY)"),
            [GrokAction.Super30Off3Mo] = new(SuperSku, OfferDiscount30, "Super", "Super -30% × 3 tháng (vd 4,409.99 TRY/T)"),

            [GrokAction.UltraPay1Get3] = new(UltraSku, OfferPay1Get3, "Ultra", "Ultra 1 trả 3 tháng (vd 12,499.99 TRY/3T)"),
            [GrokAction.Ultra50Off1Mo] = new(UltraSku, OfferLaunch50, "Ultra", "Ultra -50% tháng đầu (vd 6,250 TRY)"),
            [GrokAction.Ultra30Off3Mo] = new(UltraSku, OfferDiscount30, "Ultra", "Ultra -30% × 3 tháng (vd 8,749.99 TRY/T)"),
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

        internal string Run(string serial, GrokAction action, GrokTargets targets = GrokTargets.Both)
        {
            CaptureAdb(serial, 5000, "root");
            var billing = new InappTrialService(_adb);
            string[] packages = ResolvePackages(targets);

            return action switch
            {
                GrokAction.Inspect => Inspect(serial, billing, packages),
                GrokAction.OpenPricing => OpenPricing(serial),
                GrokAction.OpenGrokBot => OpenGrokBot(serial),
                GrokAction.ClearPlayCache => ClearPlayCache(serial, packages),
                GrokAction.PinAllBest => PinAllBest(serial, billing, packages),
                GrokAction.RestoreBase => RestoreBase(serial, billing, packages),
                _ when PinRules.TryGetValue(action, out var rule) => PinSingleOffer(serial, billing, packages, rule),
                _ => throw new ArgumentOutOfRangeException(nameof(action))
            };
        }

        private static string[] ResolvePackages(GrokTargets targets)
        {
            var list = new List<string>(2);
            if (targets.HasFlag(GrokTargets.Grok)) list.Add(GrokPackage);
            if (targets.HasFlag(GrokTargets.GrokBot)) list.Add(GrokBotPackage);
            if (list.Count == 0)
                throw new InvalidOperationException("Hãy tích chọn ít nhất một app (Grok hoặc Grok Bot).");
            return list.ToArray();
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

        private string ClearPlayCache(string serial, string[] packages)
        {
            const string playDb = "/data/data/com.android.vending/databases/sku_details.db";
            string safeSerial = Regex.Replace(serial, @"[^A-Za-z0-9._-]", "_");
            string snapshotFile = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "ToolADB", "Backups", "OfferSwitch", safeSerial, "offer_snapshots.json");

            string where = string.Join(" OR ", packages.Select(p => $"cache_key LIKE '%callingPackageName={p}#%'"));
            string stopApps = string.Join("; ", packages.Select(p => $"am force-stop {p}"));
            CaptureAdb(serial, 12000, "shell",
                $"{stopApps}; am force-stop com.android.vending; " +
                $"sqlite3 {playDb} \"PRAGMA wal_checkpoint(TRUNCATE); DELETE FROM cache_table WHERE {where}; PRAGMA wal_checkpoint(TRUNCATE);\"; " +
                $"VUID=$(stat -c '%u' /data/data/com.android.vending); chown $VUID:$VUID {playDb}* 2>/dev/null; restorecon {playDb}* 2>/dev/null");

            // Xóa cache snapshot cũ của riêng app này nếu có, để lần quét tới lưu lại đầy đủ bản gốc mới.
            try
            {
                if (File.Exists(snapshotFile))
                    File.Delete(snapshotFile);
            }
            catch { }

            return $"🔄 Đã xóa cache Play của {string.Join(" + ", packages.Select(AppLabel))}. Hãy mở app → bấm Upgrade (rồi Back lại) để Play tải lại toàn bộ offer gốc.";
        }

        private static string Inspect(string serial, InappTrialService billing, string[] packages)
        {
            var lines = new List<string>();
            foreach (string pkg in packages)
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
                return $"Chưa có cache x.subscription.* cho {string.Join(" / ", packages.Select(AppLabel))} trong sku_details.db. Hãy mở app và bấm Upgrade (rồi Back lại).";

            return string.Join(Environment.NewLine, lines);
        }

        private static string PinSingleOffer(string serial, InappTrialService billing, string[] packages, PinRule rule)
        {
            var results = new List<string>();
            foreach (string pkg in packages)
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
                    $"Không thấy SKU '{rule.SkuId}' ({rule.TierName}) kèm '{rule.OfferId}' cho {string.Join(" / ", packages.Select(AppLabel))}. Nếu cache đã bị ghim trước đó bên ngoài tool, hãy bấm '🔄 Xóa cache Play' → mở app bấm Upgrade (rồi Back) để tải lại full offer gốc.");

            return $"✅ Đã ghim {rule.Description} cho: {string.Join(", ", results)}.";
        }

        private static string PinAllBest(string serial, InappTrialService billing, string[] packages)
        {
            var results = new List<string>();
            foreach (string pkg in packages)
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
                throw new InvalidDataException($"Không tìm thấy offer giảm giá nào cho {string.Join(" / ", packages.Select(AppLabel))}. Hãy bấm '🔄 Xóa cache Play' → mở app bấm Upgrade rồi Back để tải lại cache gốc.");

            return $"⭐ Đã ghim ưu đãi ngon nhất cho: {string.Join(" | ", results)}.";
        }

        private static string RestoreBase(string serial, InappTrialService billing, string[] packages)
        {
            var restored = new List<string>();
            foreach (string pkg in packages)
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
                throw new InvalidDataException($"Chưa có SKU nào của {string.Join(" / ", packages.Select(AppLabel))} trong sku_details.db.");

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
