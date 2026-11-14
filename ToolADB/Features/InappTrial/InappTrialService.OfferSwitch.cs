using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

namespace ToolAdb.Features.InappTrial
{
    internal sealed partial class InappTrialService
    {
        private static string BytesToHex(byte[] bytes) => Convert.ToHexString(bytes);

        private static byte[] HexToBytes(string hex)
        {
            if (string.IsNullOrEmpty(hex) || (hex.Length % 2) != 0)
                return Array.Empty<byte>();
            return Convert.FromHexString(hex);
        }

        private static bool TryReadProtobufVarint(byte[] buf, int pos, out int value, out int nextPos)
        {
            value = 0;
            nextPos = pos;
            int shift = 0;
            while (nextPos < buf.Length && shift < 35)
            {
                byte b = buf[nextPos++];
                value |= (b & 0x7F) << shift;
                if ((b & 0x80) == 0)
                    return true;
                shift += 7;
            }
            return false;
        }

        private static byte[] EncodeProtobufVarint(int value)
        {
            var bytes = new List<byte>();
            uint v = (uint)value;
            while (v >= 0x80)
            {
                bytes.Add((byte)((v & 0x7F) | 0x80));
                v >>= 7;
            }
            bytes.Add((byte)v);
            return bytes.ToArray();
        }

        private static bool TryExtractProductJsonFromBlob(byte[] blob, out string jsonText, out int outerLen, out int outerPayloadStart, out int jsonStart, out int jsonLen)
        {
            jsonText = null;
            outerLen = 0;
            outerPayloadStart = 0;
            jsonStart = 0;
            jsonLen = 0;

            if (blob == null || blob.Length < 6 || blob[0] != 0x0A)
                return false;
            if (!TryReadProtobufVarint(blob, 1, out outerLen, out outerPayloadStart))
                return false;
            if (outerPayloadStart + outerLen > blob.Length || outerPayloadStart >= blob.Length || blob[outerPayloadStart] != 0x0A)
                return false;
            if (!TryReadProtobufVarint(blob, outerPayloadStart + 1, out jsonLen, out jsonStart))
                return false;
            if (jsonLen <= 0 || jsonStart + jsonLen > outerPayloadStart + outerLen)
                return false;

            jsonText = Encoding.UTF8.GetString(blob, jsonStart, jsonLen);
            return jsonText.StartsWith("{", StringComparison.Ordinal) && jsonText.EndsWith("}", StringComparison.Ordinal);
        }

        private static byte[] RebuildBlobWithNewProductJson(byte[] originalBlob, string newJsonText)
        {
            if (!TryExtractProductJsonFromBlob(originalBlob, out _, out int outerLen, out int outerPayloadStart, out int jsonStart, out int jsonLen))
                return null;

            byte[] newJsonBytes = Encoding.UTF8.GetBytes(newJsonText);
            byte[] newJsonLenVarint = EncodeProtobufVarint(newJsonBytes.Length);
            int restOfOuterLen = (outerPayloadStart + outerLen) - (jsonStart + jsonLen);
            int newOuterLen = 1 + newJsonLenVarint.Length + newJsonBytes.Length + restOfOuterLen;
            byte[] newOuterLenVarint = EncodeProtobufVarint(newOuterLen);

            int trailingStart = jsonStart + jsonLen;
            int trailingLen = originalBlob.Length - trailingStart;
            byte[] rebuilt = new byte[1 + newOuterLenVarint.Length + 1 + newJsonLenVarint.Length + newJsonBytes.Length + trailingLen];
            int offset = 0;
            rebuilt[offset++] = 0x0A;
            Buffer.BlockCopy(newOuterLenVarint, 0, rebuilt, offset, newOuterLenVarint.Length);
            offset += newOuterLenVarint.Length;
            rebuilt[offset++] = 0x0A;
            Buffer.BlockCopy(newJsonLenVarint, 0, rebuilt, offset, newJsonLenVarint.Length);
            offset += newJsonLenVarint.Length;
            Buffer.BlockCopy(newJsonBytes, 0, rebuilt, offset, newJsonBytes.Length);
            offset += newJsonBytes.Length;
            if (trailingLen > 0)
                Buffer.BlockCopy(originalBlob, trailingStart, rebuilt, offset, trailingLen);
            return rebuilt;
        }

        private static bool HasFreePricingPhase(JsonObject offer)
        {
            if (offer == null || offer["pricingPhases"] is not JsonArray phases)
                return false;
            foreach (JsonNode p in phases)
            {
                if (p is not JsonObject phase) continue;
                string microsStr = phase["priceAmountMicros"]?.ToString();
                if (long.TryParse(microsStr, out long micros) && micros == 0)
                    return true;
            }
            return false;
        }

        private static JsonArray FilterSubscriptionOffers(JsonArray offers, bool winbackMode, out string modeNote)
        {
            var result = new JsonArray();
            if (winbackMode)
            {
                var winbacks = new List<JsonNode>();
                foreach (JsonNode o in offers)
                {
                    if (o is not JsonObject obj) continue;
                    string oid = obj["offerId"]?.GetValue<string>() ?? string.Empty;
                    if (!string.IsNullOrEmpty(oid) && oid.Contains("winback", StringComparison.OrdinalIgnoreCase))
                        winbacks.Add(obj.DeepClone());
                }
                if (winbacks.Count > 0)
                {
                    foreach (var w in winbacks) result.Add(w);
                    modeNote = "winback";
                    return result;
                }
                foreach (JsonNode o in offers) if (o != null) result.Add(o.DeepClone());
                modeNote = "no_winback_kept_all";
                return result;
            }
            else
            {
                var bases = new List<JsonNode>();
                foreach (JsonNode o in offers)
                {
                    if (o is not JsonObject obj) continue;
                    string oid = obj["offerId"]?.GetValue<string>() ?? string.Empty;
                    if (string.IsNullOrEmpty(oid))
                        bases.Add(obj.DeepClone());
                }
                if (bases.Count > 0)
                {
                    foreach (var b in bases) result.Add(b);
                    modeNote = "base";
                    return result;
                }

                var paidNonWinback = new List<JsonNode>();
                foreach (JsonNode o in offers)
                {
                    if (o is not JsonObject obj) continue;
                    string oid = obj["offerId"]?.GetValue<string>() ?? string.Empty;
                    if (!HasFreePricingPhase(obj) && !oid.Contains("winback", StringComparison.OrdinalIgnoreCase))
                        paidNonWinback.Add(obj.DeepClone());
                }
                if (paidNonWinback.Count > 0)
                {
                    foreach (var p in paidNonWinback) result.Add(p);
                    modeNote = "paid_fallback";
                    return result;
                }

                foreach (JsonNode o in offers) if (o != null) result.Add(o.DeepClone());
                modeNote = "kept_all";
                return result;
            }
        }

        internal string SwitchOfferMode(string serial, string packageFilter, bool winbackMode)
        {
            if (packageFilter != "*" && !IsValidPackageName(packageFilter))
                throw new ArgumentException("Tên package không hợp lệ.", nameof(packageFilter));

            const string playDb = PlayBillingDb;
            const string farFutureExpiration = "4102444800000";
            string modeLabel = winbackMode ? "🎁 Winback" : "📦 Gói Gốc (Base)";

            string safeSerial = Regex.Replace(serial, @"[^A-Za-z0-9._-]", "_");
            string snapshotDir = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "ToolADB", "Backups", "OfferSwitch", safeSerial);
            string snapshotFile = Path.Combine(snapshotDir, "offer_snapshots.json");
            string opId = Guid.NewGuid().ToString("N");
            string localQuerySql = Path.Combine(Path.GetTempPath(), "offer_query_" + safeSerial + "_" + opId + ".sql");
            string remoteQuerySql = "/data/local/tmp/offer_query_" + safeSerial + "_" + opId + ".sql";
            string localPatchSql = Path.Combine(Path.GetTempPath(), "offer_patch_" + safeSerial + "_" + opId + ".sql");
            string remotePatchSql = "/data/local/tmp/offer_patch_" + safeSerial + "_" + opId + ".sql";

            try
            {
                CaptureAdb(serial, 5000, "root");
                string preflight = CaptureAdb(serial, 8000, "shell",
                    "test -f " + playDb + " && echo PLAY_DB_OK; command -v sqlite3");
                if (!preflight.Contains("PLAY_DB_OK"))
                    throw new FileNotFoundException("Chưa có sku_details.db. Hãy mở app và màn hình mua hàng một lần trước.");
                if (!preflight.Contains("sqlite3"))
                    throw new FileNotFoundException("Thiết bị không có sqlite3.");

                string stopCmd = packageFilter == "*"
                    ? "am force-stop com.android.vending"
                    : $"am force-stop {packageFilter}; am force-stop com.android.vending";
                CaptureAdb(serial, 8000, "shell", stopCmd);

                string whereClause = packageFilter == "*"
                    ? "cache_key LIKE '%callingPackageName=%' AND cache_key LIKE '%skuType=subs%'"
                    : $"cache_key LIKE '%callingPackageName={packageFilter}#%' AND cache_key LIKE '%skuType=subs%'";

                string querySql =
                    ".bail on\n" +
                    ".timeout 10000\n" +
                    "SELECT hex(cache_key) || '|' || hex(cache_data) FROM cache_table WHERE " + whereClause + ";\n";
                File.WriteAllText(localQuerySql, querySql, new UTF8Encoding(false));
                CaptureAdb(serial, 10000, "push", localQuerySql, remoteQuerySql);
                string rawRows = CaptureAdb(serial, 20000, "shell", $"sqlite3 {playDb} < {remoteQuerySql}; rm -f {remoteQuerySql}");

                Directory.CreateDirectory(snapshotDir);
                Dictionary<string, string> snapshots = new();
                if (File.Exists(snapshotFile))
                {
                    try
                    {
                        snapshots = JsonSerializer.Deserialize<Dictionary<string, string>>(File.ReadAllText(snapshotFile, Encoding.UTF8)) ?? new();
                    }
                    catch { snapshots = new(); }
                }

                var patchSql = new StringBuilder();
                patchSql.AppendLine(".bail on");
                patchSql.AppendLine(".timeout 10000");
                patchSql.AppendLine("BEGIN IMMEDIATE;");

                int totalRows = 0;
                int updatedRows = 0;
                int winbackHits = 0;
                int baseHits = 0;

                foreach (string line in rawRows.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries))
                {
                    string[] parts = line.Split('|', 2);
                    if (parts.Length != 2) continue;
                    string keyHex = parts[0].Trim();
                    string currentDataHex = parts[1].Trim();
                    if (string.IsNullOrEmpty(keyHex) || string.IsNullOrEmpty(currentDataHex)) continue;
                    totalRows++;

                    byte[] currentBlob = HexToBytes(currentDataHex);
                    if (!TryExtractProductJsonFromBlob(currentBlob, out string curJson, out _, out _, out _, out _))
                        continue;

                    if (JsonNode.Parse(curJson) is not JsonObject curProduct || curProduct["subscriptionOfferDetails"] is not JsonArray curOffers || curOffers.Count == 0)
                        continue;

                    if (curOffers.Count > 1 || !snapshots.ContainsKey(keyHex))
                        snapshots[keyHex] = currentDataHex;

                    byte[] sourceBlob = snapshots.TryGetValue(keyHex, out string snapHex) ? HexToBytes(snapHex) : currentBlob;
                    if (!TryExtractProductJsonFromBlob(sourceBlob, out string srcJson, out _, out _, out _, out _))
                    {
                        sourceBlob = currentBlob;
                        srcJson = curJson;
                    }

                    if (JsonNode.Parse(srcJson) is not JsonObject srcProduct || srcProduct["subscriptionOfferDetails"] is not JsonArray srcOffers || srcOffers.Count == 0)
                        continue;

                    JsonArray filtered = FilterSubscriptionOffers(srcOffers, winbackMode, out string modeNote);
                    if (modeNote == "winback") winbackHits++;
                    else if (modeNote == "base" || modeNote == "paid_fallback") baseHits++;

                    srcProduct["subscriptionOfferDetails"] = filtered;
                    string newJson = srcProduct.ToJsonString(new JsonSerializerOptions { Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping });
                    byte[] newBlob = RebuildBlobWithNewProductJson(sourceBlob, newJson);
                    if (newBlob == null) continue;

                    string newDataHex = BytesToHex(newBlob);
                    patchSql.AppendFormat(
                        "UPDATE cache_table SET cache_data=X'{0}', expiration_timestamp={1}, space_occupied_in_bytes={2} WHERE cache_key=CAST(X'{3}' AS TEXT);\n",
                        newDataHex, farFutureExpiration, newBlob.Length, keyHex);
                    updatedRows++;
                }

                patchSql.AppendLine("SELECT 'TOUCHED_ROWS=' || total_changes();");
                patchSql.AppendLine("COMMIT;");
                patchSql.AppendLine("PRAGMA wal_checkpoint(FULL);");

                if (totalRows == 0 || updatedRows == 0)
                    throw new InvalidDataException($"Không tìm thấy gói subscription nào của '{packageFilter}' trong sku_details.db. Hãy mở app và màn hình mua hàng trước.");

                File.WriteAllText(snapshotFile, JsonSerializer.Serialize(snapshots), new UTF8Encoding(false));
                File.WriteAllText(localPatchSql, patchSql.ToString(), new UTF8Encoding(false));
                CaptureAdb(serial, 10000, "push", localPatchSql, remotePatchSql);
                string patchOut = CaptureAdb(serial, 20000, "shell",
                    $"sqlite3 {playDb} < {remotePatchSql}; rm -f {remotePatchSql}; VUID=$(stat -c '%u' /data/data/com.android.vending); chown $VUID:$VUID {playDb}* 2>/dev/null; restorecon {playDb}* 2>/dev/null");
                Match touchedMatch = Regex.Match(patchOut ?? string.Empty, @"TOUCHED_ROWS=(\d+)");
                if (!touchedMatch.Success || Convert.ToInt32(touchedMatch.Groups[1].Value) == 0)
                    throw new InvalidDataException("SQLite không cập nhật được dòng nào trong sku_details.db (" + (patchOut ?? string.Empty).Trim() + ").");

                if (packageFilter != "*")
                    CaptureAdb(serial, 8000, "shell", $"monkey -p {packageFilter} -c android.intent.category.LAUNCHER 1");

                return $"✅ {modeLabel} ({packageFilter}): cập nhật {updatedRows}/{totalRows} SKU (Base={baseHits}, Winback={winbackHits}).";
            }
            finally
            {
                try { if (File.Exists(localQuerySql)) File.Delete(localQuerySql); } catch { }
                try { if (File.Exists(localPatchSql)) File.Delete(localPatchSql); } catch { }
            }
        }
    }
}
