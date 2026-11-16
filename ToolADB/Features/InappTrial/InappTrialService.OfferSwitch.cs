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

        private static bool TryReadProtobufVarint64(byte[] buf, int pos, out ulong value, out int nextPos)
        {
            value = 0;
            nextPos = pos;
            int shift = 0;
            while (nextPos < buf.Length && shift < 70)
            {
                byte b = buf[nextPos++];
                value |= (ulong)(b & 0x7F) << shift;
                if ((b & 0x80) == 0)
                    return true;
                shift += 7;
            }
            return false;
        }

        private static byte[] EncodeProtobufVarint64(ulong value)
        {
            var bytes = new List<byte>();
            while (value >= 0x80)
            {
                bytes.Add((byte)((value & 0x7F) | 0x80));
                value >>= 7;
            }
            bytes.Add((byte)value);
            return bytes.ToArray();
        }

        private readonly struct ProtobufField
        {
            internal readonly int FieldNumber;
            internal readonly int WireType;
            internal readonly ulong Scalar;
            internal readonly byte[] Bytes;

            internal ProtobufField(int fieldNumber, int wireType, ulong scalar, byte[] bytes)
            {
                FieldNumber = fieldNumber;
                WireType = wireType;
                Scalar = scalar;
                Bytes = bytes;
            }
        }

        private static bool TryDecodeProtobufFields(byte[] buf, out List<ProtobufField> fields)
        {
            fields = new List<ProtobufField>();
            if (buf == null || buf.Length == 0)
                return false;

            int i = 0;
            while (i < buf.Length)
            {
                if (!TryReadProtobufVarint64(buf, i, out ulong tag, out i))
                    return false;
                int fn = (int)(tag >> 3);
                int wt = (int)(tag & 7);
                if (fn <= 0)
                    return false;

                switch (wt)
                {
                    case 0:
                        if (!TryReadProtobufVarint64(buf, i, out ulong v, out i))
                            return false;
                        fields.Add(new ProtobufField(fn, wt, v, null));
                        break;
                    case 1:
                        if (i + 8 > buf.Length) return false;
                        byte[] fixed64 = new byte[8];
                        Buffer.BlockCopy(buf, i, fixed64, 0, 8);
                        i += 8;
                        fields.Add(new ProtobufField(fn, wt, 0, fixed64));
                        break;
                    case 2:
                        if (!TryReadProtobufVarint64(buf, i, out ulong len64, out i))
                            return false;
                        if (len64 > (ulong)(buf.Length - i))
                            return false;
                        int len = (int)len64;
                        byte[] payload = new byte[len];
                        Buffer.BlockCopy(buf, i, payload, 0, len);
                        i += len;
                        fields.Add(new ProtobufField(fn, wt, 0, payload));
                        break;
                    case 5:
                        if (i + 4 > buf.Length) return false;
                        byte[] fixed32 = new byte[4];
                        Buffer.BlockCopy(buf, i, fixed32, 0, 4);
                        i += 4;
                        fields.Add(new ProtobufField(fn, wt, 0, fixed32));
                        break;
                    default:
                        return false;
                }
            }
            return true;
        }

        private static byte[] EncodeProtobufFields(List<ProtobufField> fields)
        {
            using var ms = new MemoryStream();
            foreach (var f in fields)
            {
                byte[] tag = EncodeProtobufVarint64((ulong)((f.FieldNumber << 3) | f.WireType));
                ms.Write(tag, 0, tag.Length);
                switch (f.WireType)
                {
                    case 0:
                        byte[] v = EncodeProtobufVarint64(f.Scalar);
                        ms.Write(v, 0, v.Length);
                        break;
                    case 1:
                    case 5:
                        ms.Write(f.Bytes, 0, f.Bytes.Length);
                        break;
                    case 2:
                        byte[] len = EncodeProtobufVarint64((ulong)f.Bytes.Length);
                        ms.Write(len, 0, len.Length);
                        ms.Write(f.Bytes, 0, f.Bytes.Length);
                        break;
                }
            }
            return ms.ToArray();
        }

        private static bool TryExtractProductJsonFromBlob(byte[] blob, out string jsonText)
        {
            jsonText = null;
            if (!TryDecodeProtobufFields(blob, out var outer))
                return false;
            int f1Idx = outer.FindIndex(f => f.FieldNumber == 1 && f.WireType == 2);
            if (f1Idx < 0 || !TryDecodeProtobufFields(outer[f1Idx].Bytes, out var f1Fields))
                return false;
            int jsonIdx = f1Fields.FindIndex(f => f.FieldNumber == 1 && f.WireType == 2);
            if (jsonIdx < 0 || f1Fields[jsonIdx].Bytes.Length == 0)
                return false;

            jsonText = Encoding.UTF8.GetString(f1Fields[jsonIdx].Bytes);
            return jsonText.StartsWith("{", StringComparison.Ordinal) && jsonText.EndsWith("}", StringComparison.Ordinal);
        }

        private static List<ProtobufField> SelectMatchingOfferSubmessages(List<ProtobufField> f2Items, JsonArray keptOffers)
        {
            if (f2Items.Count == 0 || keptOffers == null || keptOffers.Count == 0)
                return f2Items;

            var used = new bool[f2Items.Count];
            var selected = new List<ProtobufField>();

            foreach (JsonNode node in keptOffers)
            {
                string token = node?["offerIdToken"]?.GetValue<string>();
                if (string.IsNullOrEmpty(token)) continue;
                byte[] tokenBytes = Encoding.UTF8.GetBytes(token);

                int matchIdx = -1;
                for (int i = 0; i < f2Items.Count; i++)
                {
                    if (!used[i] && f2Items[i].Bytes.AsSpan().IndexOf(tokenBytes) >= 0)
                    {
                        matchIdx = i;
                        used[i] = true;
                        break;
                    }
                }
                if (matchIdx < 0)
                {
                    for (int i = 0; i < f2Items.Count; i++)
                    {
                        if (f2Items[i].Bytes.AsSpan().IndexOf(tokenBytes) >= 0)
                        {
                            matchIdx = i;
                            break;
                        }
                    }
                }
                if (matchIdx >= 0)
                    selected.Add(f2Items[matchIdx]);
            }

            return selected.Count > 0 ? selected : f2Items;
        }

        // Cập nhật f1.f1 (JSON ProductDetails) và đồng bộ lại mảng f1.f2[] (offer submessages trên Play 54.x),
        // đồng thời giữ nguyên các trường khác (f1.f3, f3 timestamp...).
        private static byte[] RebuildBlobWithNewProductJson(byte[] originalBlob, string newJsonText, JsonArray keptOffers)
        {
            if (!TryDecodeProtobufFields(originalBlob, out var outer))
                return null;
            int f1Idx = outer.FindIndex(f => f.FieldNumber == 1 && f.WireType == 2);
            if (f1Idx < 0 || !TryDecodeProtobufFields(outer[f1Idx].Bytes, out var f1Fields))
                return null;
            int jsonIdx = f1Fields.FindIndex(f => f.FieldNumber == 1 && f.WireType == 2);
            if (jsonIdx < 0)
                return null;

            var f2Items = f1Fields.Where(f => f.FieldNumber == 2 && f.WireType == 2).ToList();
            var matchedF2 = SelectMatchingOfferSubmessages(f2Items, keptOffers);

            byte[] newJsonBytes = Encoding.UTF8.GetBytes(newJsonText);
            var newF1Fields = new List<ProtobufField>(f1Fields.Count);
            bool f2Inserted = false;

            for (int i = 0; i < f1Fields.Count; i++)
            {
                var field = f1Fields[i];
                if (i == jsonIdx)
                {
                    newF1Fields.Add(new ProtobufField(1, 2, 0, newJsonBytes));
                    continue;
                }
                if (field.FieldNumber == 2 && field.WireType == 2)
                {
                    if (!f2Inserted)
                    {
                        newF1Fields.AddRange(matchedF2);
                        f2Inserted = true;
                    }
                    continue;
                }
                newF1Fields.Add(field);
            }

            byte[] newF1Bytes = EncodeProtobufFields(newF1Fields);
            outer[f1Idx] = new ProtobufField(1, 2, 0, newF1Bytes);
            return EncodeProtobufFields(outer);
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
                    "PRAGMA wal_checkpoint(TRUNCATE);\n" +
                    "SELECT hex(cache_key) || '|' || hex(cache_data) || '|' || ifnull(space_occupied_in_bytes, 0) FROM cache_table WHERE " + whereClause + ";\n";
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
                patchSql.AppendLine("PRAGMA wal_checkpoint(TRUNCATE);");
                patchSql.AppendLine("BEGIN IMMEDIATE;");

                int totalRows = 0;
                int updatedRows = 0;
                int winbackHits = 0;
                int baseHits = 0;

                foreach (string line in rawRows.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries))
                {
                    string[] parts = line.Split('|', 3);
                    if (parts.Length < 2) continue;
                    string keyHex = parts[0].Trim();
                    string currentDataHex = parts[1].Trim();
                    int oldSpace = (parts.Length >= 3 && int.TryParse(parts[2].Trim(), out int parsedSpace)) ? parsedSpace : 0;
                    if (string.IsNullOrEmpty(keyHex) || string.IsNullOrEmpty(currentDataHex)) continue;
                    totalRows++;

                    byte[] currentBlob = HexToBytes(currentDataHex);
                    if (!TryExtractProductJsonFromBlob(currentBlob, out string curJson))
                        continue;

                    if (JsonNode.Parse(curJson) is not JsonObject curProduct || curProduct["subscriptionOfferDetails"] is not JsonArray curOffers || curOffers.Count == 0)
                        continue;

                    if (curOffers.Count > 1 || !snapshots.ContainsKey(keyHex))
                        snapshots[keyHex] = currentDataHex;

                    byte[] sourceBlob = snapshots.TryGetValue(keyHex, out string snapHex) ? HexToBytes(snapHex) : currentBlob;
                    if (!TryExtractProductJsonFromBlob(sourceBlob, out string srcJson))
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
                    byte[] newBlob = RebuildBlobWithNewProductJson(sourceBlob, newJson, filtered);
                    if (newBlob == null) continue;

                    string newDataHex = BytesToHex(newBlob);
                    int keyLen = keyHex.Length / 2;
                    int newSpace = oldSpace > 0
                        ? Math.Max(newBlob.Length, oldSpace + (newBlob.Length - currentBlob.Length))
                        : keyLen + newBlob.Length + 24;

                    // Giữ nguyên expiration_timestamp vì Play 54.x phát hiện sửa TTL và fetch đè lại ngay từ server.
                    patchSql.AppendFormat(
                        "UPDATE cache_table SET cache_data=X'{0}', space_occupied_in_bytes={1} WHERE cache_key=CAST(X'{2}' AS TEXT);\n",
                        newDataHex, newSpace, keyHex);
                    updatedRows++;
                }

                patchSql.AppendLine("SELECT 'TOUCHED_ROWS=' || total_changes();");
                patchSql.AppendLine("COMMIT;");
                patchSql.AppendLine("PRAGMA wal_checkpoint(TRUNCATE);");

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
