namespace ToolAdb.Features.InappTrial
{
    internal sealed class DevicePackages
    {
        public HashSet<string> Installed { get; } = new(StringComparer.Ordinal);
        public HashSet<string> WithOfferCache { get; } = new(StringComparer.Ordinal);
    }

    internal sealed partial class InappTrialService
    {
        private const string CacheMarker = "===OFFER_CACHE===";

        // Tính năng riêng của ToolADB (không có trong Inapp-Patch), chỉ ĐỌC:
        // app người dùng cài (pm list packages -3) và các package đã có cache offer trong sku_details.db.
        internal DevicePackages ListPackages(string serial)
        {
            CaptureAdb(serial, 5000, "root");
            string sql =
                "SELECT DISTINCT substr(cache_key, instr(cache_key,'callingPackageName=')+19, " +
                "instr(substr(cache_key, instr(cache_key,'callingPackageName=')+19),'#')-1) " +
                "FROM cache_table WHERE cache_key LIKE '%callingPackageName=%#%';";
            string output = CaptureAdb(serial, 20000, "shell",
                "pm list packages -3; echo " + CacheMarker + "; " +
                "test -f " + PlayBillingDb + " && sqlite3 " + PlayBillingDb + " \"" + sql + "\"");

            var result = new DevicePackages();
            bool inCache = false;
            foreach (string raw in output.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries))
            {
                string line = raw.Trim();
                if (line == CacheMarker) { inCache = true; continue; }
                string name = inCache ? line : (line.StartsWith("package:") ? line.Substring(8) : null);
                if (!IsValidPackageName(name)) continue;
                (inCache ? result.WithOfferCache : result.Installed).Add(name);
            }
            return result;
        }
    }
}
