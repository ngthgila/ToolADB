using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace ToolAdb.Features.InappTrial
{
    internal sealed class BillingOfferInfo
    {
        public string Serial { get; set; }
        public string PackageName { get; set; }
        public string ProductType { get; set; }
        public string ProductId { get; set; }
        public string BasePlanId { get; set; }
        public string OfferId { get; set; }
        public string OfferTags { get; set; }
        public int Phase { get; set; }
        public string Price { get; set; }
        public string Currency { get; set; }
        public string BillingPeriod { get; set; }
        public string Recurrence { get; set; }
        public string CacheExpires { get; set; }
    }

    internal sealed partial class InappTrialService
    {
        internal static bool IsValidPackageName(string packageName) =>
            packageName != null && Regex.IsMatch(packageName, @"^[A-Za-z0-9_]+(?:\.[A-Za-z0-9_]+)+$");

        internal List<BillingOfferInfo> ScanOffers(string serial, string packageName)
        {
            if (!IsValidPackageName(packageName))
                throw new ArgumentException("Package không hợp lệ.", nameof(packageName));

            EnsureRoot(serial);
            // CaptureAdb vì exit code của chuỗi lệnh là của "command -v sqlite3":
            // thiết bị thiếu sqlite3 phải rơi vào thông báo bên dưới, không phải lỗi ADB thô.
            string preflight = CaptureAdb(serial, 15000, "shell",
                $"test -f {PlayBillingDb} && echo PLAY_DB_OK; command -v sqlite3");
            if (!preflight.Contains("PLAY_DB_OK"))
                throw new InvalidOperationException("Không tìm thấy sku_details.db của Google Play.");
            if (!preflight.Contains("sqlite3"))
                throw new InvalidOperationException("Thiết bị không có sqlite3.");

            string safeSerial = Regex.Replace(serial, @"[^A-Za-z0-9._-]", "_");
            string scanId = Guid.NewGuid().ToString("N");
            string localSql = Path.Combine(Path.GetTempPath(), $"offer_scan_{safeSerial}_{scanId}.sql");
            string remoteSql = $"/data/local/tmp/offer_scan_{safeSerial}_{scanId}.sql";

            string sql =
                ".bail on\n" +
                ".timeout 10000\n" +
                "SELECT hex(cache_key) || '|' || hex(cache_data) || '|' || expiration_timestamp " +
                "FROM cache_table WHERE cache_key LIKE '%callingPackageName=" + packageName + "#%' " +
                "AND (cache_key LIKE '%skuType=subs%' OR cache_key LIKE '%skuType=inapp%') " +
                "ORDER BY cache_key;\n";

            var result = new List<BillingOfferInfo>();
            try
            {
                File.WriteAllText(localSql, sql, new UTF8Encoding(false));
                Execute(serial, 10000, "push", localSql, remoteSql);
                string output = Execute(serial, 30000, "shell", $"sqlite3 {PlayBillingDb} < {remoteSql}");
                foreach (string line in output.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries))
                    ParseCacheRow(result, line, serial, packageName);
            }
            finally
            {
                _adb.RunOnDevice(serial, "shell", $"rm -f {remoteSql}");
                try { File.Delete(localSql); } catch (IOException) { }
            }

            return result
                .GroupBy(x => string.Join("\u001f", x.Serial, x.ProductType, x.ProductId, x.BasePlanId,
                    x.OfferId, x.OfferTags, x.Phase.ToString(), x.Price, x.Currency, x.BillingPeriod, x.Recurrence))
                .Select(g => g.First())
                .OrderBy(x => x.ProductId).ThenBy(x => x.BasePlanId).ThenBy(x => x.OfferId).ThenBy(x => x.Phase)
                .ToList();
        }

        private static void ParseCacheRow(List<BillingOfferInfo> result, string line, string serial, string packageName)
        {
            string[] parts = line.Split('|', 3);
            if (parts.Length != 3) return;

            string cacheText;
            try { cacheText = Encoding.UTF8.GetString(Convert.FromHexString(parts[1].Trim())); }
            catch (FormatException) { return; }

            string json = ExtractFirstJsonObject(cacheText);
            if (json == null) return;

            JsonDocument document;
            try { document = JsonDocument.Parse(json); }
            catch (JsonException) { return; }

            using (document)
            {
                JsonElement product = document.RootElement;
                if (product.ValueKind != JsonValueKind.Object) return;
                string expires = CacheExpirationText(parts[2].Trim());
                AddSubscriptionOffers(result, product, serial, packageName, expires);
                AddOneTimeOffers(result, product, serial, packageName, expires);
            }
        }

        private static void AddSubscriptionOffers(
            List<BillingOfferInfo> result, JsonElement product, string serial, string packageName, string expires)
        {
            if (!product.TryGetProperty("subscriptionOfferDetails", out JsonElement offers) ||
                offers.ValueKind != JsonValueKind.Array)
                return;

            foreach (JsonElement offer in offers.EnumerateArray())
            {
                if (offer.ValueKind != JsonValueKind.Object) continue;
                string basePlanId = JsonString(offer, "basePlanId");
                string offerId = JsonString(offer, "offerId");
                if (string.IsNullOrEmpty(offerId)) offerId = "(base plan)";
                string tags = JsonTags(offer);

                var phases = new List<JsonElement>();
                if (offer.TryGetProperty("pricingPhases", out JsonElement phasesElement))
                {
                    if (phasesElement.ValueKind == JsonValueKind.Array) phases.AddRange(phasesElement.EnumerateArray());
                    else if (phasesElement.ValueKind == JsonValueKind.Object) phases.Add(phasesElement);
                }
                if (phases.Count == 0) phases.Add(default);

                for (int i = 0; i < phases.Count; i++)
                {
                    JsonElement phase = phases[i];
                    result.Add(new BillingOfferInfo
                    {
                        Serial = serial,
                        PackageName = packageName,
                        ProductType = JsonString(product, "type", "subs"),
                        ProductId = JsonString(product, "productId"),
                        BasePlanId = basePlanId,
                        OfferId = offerId,
                        OfferTags = tags,
                        Phase = i + 1,
                        Price = JsonString(phase, "formattedPrice"),
                        Currency = JsonString(phase, "priceCurrencyCode"),
                        BillingPeriod = JsonString(phase, "billingPeriod"),
                        Recurrence = RecurrenceText(phase),
                        CacheExpires = expires
                    });
                }
            }
        }

        private static void AddOneTimeOffers(
            List<BillingOfferInfo> result, JsonElement product, string serial, string packageName, string expires)
        {
            bool hasDetails = product.TryGetProperty("oneTimePurchaseOfferDetails", out JsonElement detailsElement) &&
                detailsElement.ValueKind != JsonValueKind.Null && detailsElement.ValueKind != JsonValueKind.Undefined;
            var details = new List<JsonElement>();
            if (!hasDetails) details.Add(product);
            else if (detailsElement.ValueKind == JsonValueKind.Array) details.AddRange(detailsElement.EnumerateArray());
            else details.Add(detailsElement);

            int index = 0;
            foreach (JsonElement detail in details)
            {
                if (detail.ValueKind != JsonValueKind.Object) continue;
                string price = JsonString(detail, "formattedPrice", JsonString(detail, "price"));
                if (string.IsNullOrEmpty(price) && !hasDetails) continue;
                index++;
                string offerId = JsonString(detail, "offerId");
                if (string.IsNullOrEmpty(offerId)) offerId = "(one-time)";
                result.Add(new BillingOfferInfo
                {
                    Serial = serial,
                    PackageName = packageName,
                    ProductType = JsonString(product, "type", "inapp"),
                    ProductId = JsonString(product, "productId"),
                    BasePlanId = JsonString(detail, "purchaseOptionId"),
                    OfferId = offerId,
                    OfferTags = JsonTags(detail),
                    Phase = index,
                    Price = price,
                    Currency = JsonString(detail, "priceCurrencyCode", JsonString(detail, "price_currency_code")),
                    BillingPeriod = string.Empty,
                    Recurrence = "Một lần",
                    CacheExpires = expires
                });
            }
        }

        private static string JsonString(JsonElement element, string key, string fallback = "")
        {
            if (element.ValueKind == JsonValueKind.Object && element.TryGetProperty(key, out JsonElement value))
            {
                if (value.ValueKind == JsonValueKind.String) return value.GetString();
                if (value.ValueKind != JsonValueKind.Null && value.ValueKind != JsonValueKind.Undefined)
                    return value.ToString();
            }
            return fallback;
        }

        private static int JsonInt(JsonElement element, string key)
        {
            if (element.ValueKind == JsonValueKind.Object && element.TryGetProperty(key, out JsonElement value))
            {
                if (value.ValueKind == JsonValueKind.Number && value.TryGetInt32(out int number)) return number;
                if (value.ValueKind == JsonValueKind.String && int.TryParse(value.GetString(), out int parsed)) return parsed;
            }
            return 0;
        }

        private static string JsonTags(JsonElement element)
        {
            if (element.ValueKind != JsonValueKind.Object ||
                !element.TryGetProperty("offerTags", out JsonElement tags) || tags.ValueKind != JsonValueKind.Array)
                return string.Empty;
            return string.Join(", ", tags.EnumerateArray()
                .Where(t => t.ValueKind == JsonValueKind.String)
                .Select(t => t.GetString())
                .Where(t => !string.IsNullOrEmpty(t)));
        }

        private static string RecurrenceText(JsonElement phase)
        {
            int mode = JsonInt(phase, "recurrenceMode");
            int cycles = JsonInt(phase, "billingCycleCount");
            if (mode == 1) return "Lặp vô hạn";
            if (mode == 2) return cycles > 0 ? "Lặp " + cycles + " kỳ" : "Lặp hữu hạn";
            if (mode == 3) return "Một lần";
            return mode == 0 ? string.Empty : "Mode " + mode;
        }

        private static string CacheExpirationText(string milliseconds)
        {
            if (!long.TryParse(milliseconds, out long value) || value <= 0) return string.Empty;
            try
            {
                return DateTimeOffset.FromUnixTimeMilliseconds(value).ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss");
            }
            catch (ArgumentOutOfRangeException)
            {
                return milliseconds;
            }
        }

        private static string ExtractFirstJsonObject(string text)
        {
            if (string.IsNullOrEmpty(text)) return null;
            int start = text.IndexOf('{');
            if (start < 0) return null;

            int depth = 0;
            bool inString = false;
            bool escaped = false;
            for (int i = start; i < text.Length; i++)
            {
                char c = text[i];
                if (inString)
                {
                    if (escaped) escaped = false;
                    else if (c == '\\') escaped = true;
                    else if (c == '"') inString = false;
                    continue;
                }

                if (c == '"') inString = true;
                else if (c == '{') depth++;
                else if (c == '}')
                {
                    depth--;
                    if (depth == 0) return text.Substring(start, i - start + 1);
                }
            }
            return null;
        }

        internal static string SaveOfferReport(List<BillingOfferInfo> offers, string packageName)
        {
            string safePackage = Regex.Replace(packageName, @"[^A-Za-z0-9._-]", "_");
            string reportDir = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "ToolADB", "Reports", "Offers");
            Directory.CreateDirectory(reportDir);
            string path = Path.Combine(reportDir, DateTime.Now.ToString("yyyyMMdd_HHmmss") + "_" + safePackage + ".csv");
            var csv = new StringBuilder();
            csv.AppendLine("Serial,Package,Type,ProductId,BasePlanId,OfferId,OfferTags,Phase,Price,Currency,BillingPeriod,Recurrence,CacheExpires");
            foreach (BillingOfferInfo item in offers)
            {
                csv.AppendLine(string.Join(",",
                    Csv(item.Serial), Csv(item.PackageName), Csv(item.ProductType), Csv(item.ProductId),
                    Csv(item.BasePlanId), Csv(item.OfferId), Csv(item.OfferTags), item.Phase.ToString(),
                    Csv(item.Price), Csv(item.Currency), Csv(item.BillingPeriod), Csv(item.Recurrence),
                    Csv(item.CacheExpires)));
            }
            File.WriteAllText(path, csv.ToString(), new UTF8Encoding(true));
            return path;
        }

        private static string Csv(string value) => "\"" + (value ?? string.Empty).Replace("\"", "\"\"") + "\"";
    }
}
