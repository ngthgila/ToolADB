using System.Text;
using System.Text.RegularExpressions;

namespace ToolAdb.Features.InappTrial
{
    internal sealed partial class InappTrialService
    {
        private const string ChessPackage = "com.chess";
        private const string PlayBillingDb = "/data/data/com.android.vending/databases/sku_details.db";
        private const string FarFutureExpiration = "4102444800000"; // 2100-01-01 UTC

        private string PatchChessNoTrial(string serial)
        {
            string preflight = Shell(serial,
                $"test -f {PlayBillingDb} && echo PLAY_DB_OK; command -v sqlite3; pm path {ChessPackage}");
            if (!preflight.Contains("PLAY_DB_OK"))
                throw new InvalidOperationException("Chưa có cache Google Play Billing. Hãy mở Chess.com và trang Premium một lần rồi chạy lại.");
            if (!preflight.Contains("sqlite3"))
                throw new InvalidOperationException("Thiết bị không có sqlite3 để sửa cache Billing an toàn.");
            if (!preflight.Contains("package:"))
                throw new InvalidOperationException("Không tìm thấy ứng dụng com.chess trên thiết bị.");

            string safeSerial = Regex.Replace(serial, @"[^A-Za-z0-9._-]", "_");
            string backupDir = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "ToolADB", "Backups", "Chess", safeSerial,
                DateTime.Now.ToString("yyyyMMdd_HHmmss"));
            Directory.CreateDirectory(backupDir);

            Shell(serial, $"am force-stop {ChessPackage}; am force-stop com.android.vending");
            PullBillingDb(serial, backupDir, "before");
            string dbBefore = Path.Combine(backupDir, "sku_details.before.db");
            if (!File.Exists(dbBefore) || new FileInfo(dbBefore).Length == 0)
                throw new IOException("Không thể sao lưu sku_details.db trước khi sửa.");

            // Chess ưu tiên offerId từ incentive, sau đó offerId "freetrial", rồi mới tới base plan.
            // Đổi nhãn trial bằng chuỗi cùng độ dài giữ nguyên framing protobuf trong BLOB cache.
            string sql =
                ".bail on\n" +
                ".timeout 10000\n" +
                "SELECT 'CHESS_CACHE_ROWS=' || count(*) FROM cache_table " +
                "WHERE cache_key LIKE '%callingPackageName=com.chess%' AND cache_key LIKE '%skuType=subs%';\n" +
                "SELECT 'BYTES_BEFORE=' || ifnull(sum(length(cache_data)),0) FROM cache_table " +
                "WHERE cache_key LIKE '%callingPackageName=com.chess%' AND cache_key LIKE '%skuType=subs%';\n" +
                "BEGIN IMMEDIATE;\n" +
                "UPDATE cache_table SET " +
                "cache_data=CAST(replace(replace(CAST(cache_data AS TEXT)," +
                "'\"offerId\":\"freetrial\"','\"offerId\":\"notrialxx\"')," +
                "'free7days','no7trials') AS BLOB), " +
                "expiration_timestamp=" + FarFutureExpiration + " " +
                "WHERE cache_key LIKE '%callingPackageName=com.chess%' " +
                "AND cache_key LIKE '%skuType=subs%' " +
                "AND (instr(cache_data,'\"offerId\":\"freetrial\"')>0 " +
                "OR instr(cache_data,'free7days')>0 " +
                "OR instr(cache_data,'\"offerId\":\"notrialxx\"')>0 " +
                "OR instr(cache_data,'no7trials')>0);\n" +
                "SELECT 'TOUCHED_ROWS=' || changes();\n" +
                "COMMIT;\n" +
                "PRAGMA wal_checkpoint(FULL);\n" +
                "SELECT 'TRIAL_REMAINING=' || count(*) FROM cache_table " +
                "WHERE cache_key LIKE '%callingPackageName=com.chess%' " +
                "AND (instr(cache_data,'\"offerId\":\"freetrial\"')>0 OR instr(cache_data,'free7days')>0);\n" +
                "SELECT 'PATCHED_ROWS_PRESENT=' || count(*) FROM cache_table " +
                "WHERE cache_key LIKE '%callingPackageName=com.chess%' " +
                "AND (instr(cache_data,'\"offerId\":\"notrialxx\"')>0 OR instr(cache_data,'no7trials')>0);\n" +
                "SELECT 'BASE_PLAN_ROWS=' || count(*) FROM cache_table " +
                "WHERE cache_key LIKE '%callingPackageName=com.chess%' " +
                "AND instr(cache_data,'\"basePlanId\"')>0;\n" +
                "SELECT 'BYTES_AFTER=' || ifnull(sum(length(cache_data)),0) FROM cache_table " +
                "WHERE cache_key LIKE '%callingPackageName=com.chess%' AND cache_key LIKE '%skuType=subs%';\n";

            string sqlFile = Path.Combine(backupDir, "patch_chess_no_trial.sql");
            string remoteSql = "/data/local/tmp/chess_no_trial_" + safeSerial + ".sql";
            File.WriteAllText(sqlFile, sql, new UTF8Encoding(false));
            Execute(serial, 10000, "push", sqlFile, remoteSql);

            string patchOutput;
            try
            {
                patchOutput = Execute(serial, 20000, "shell", $"sqlite3 {PlayBillingDb} < {remoteSql}");
            }
            finally
            {
                _adb.RunOnDevice(serial, "shell", $"rm -f {remoteSql}");
            }

            Match cacheRows = Regex.Match(patchOutput, @"CHESS_CACHE_ROWS=(\d+)");
            Match patchedRows = Regex.Match(patchOutput, @"PATCHED_ROWS_PRESENT=(\d+)");
            Match baseRows = Regex.Match(patchOutput, @"BASE_PLAN_ROWS=(\d+)");
            Match bytesBefore = Regex.Match(patchOutput, @"BYTES_BEFORE=(\d+)");
            Match bytesAfter = Regex.Match(patchOutput, @"BYTES_AFTER=(\d+)");
            if (!patchOutput.Contains("TRIAL_REMAINING=0"))
                throw new InvalidOperationException("Cache Billing vẫn còn offer trial sau khi vá.");
            if (!cacheRows.Success || Convert.ToInt32(cacheRows.Groups[1].Value) == 0)
                throw new InvalidOperationException("Không tìm thấy dòng subscription của Chess trong cache Billing.");
            if (!patchedRows.Success || Convert.ToInt32(patchedRows.Groups[1].Value) == 0)
                throw new InvalidOperationException("Không tìm thấy offer trial để vô hiệu hóa.");
            if (!baseRows.Success || Convert.ToInt32(baseRows.Groups[1].Value) == 0)
                throw new InvalidOperationException("Không còn base plan để thanh toán trực tiếp; đã dừng để tránh lỗi mua hàng.");
            if (!bytesBefore.Success || !bytesAfter.Success || bytesBefore.Groups[1].Value != bytesAfter.Groups[1].Value)
                throw new InvalidOperationException("Kích thước cache Billing thay đổi ngoài dự kiến.");

            PullBillingDb(serial, backupDir, "after");
            Shell(serial, $"monkey -p {ChessPackage} -c android.intent.category.LAUNCHER 1");
            return $"Đã bỏ offer trial của Chess.com, giữ base plan để mua trực tiếp. Backup: {backupDir}";
        }

        private void PullBillingDb(string serial, string backupDir, string stage)
        {
            Execute(serial, 10000, "pull", PlayBillingDb, Path.Combine(backupDir, $"sku_details.{stage}.db"));
            // File -wal/-shm có thể không tồn tại; bỏ qua lỗi khi pull.
            foreach (string suffix in new[] { "-wal", "-shm" })
                _adb.Capture(new[] { "-s", serial, "pull", PlayBillingDb + suffix,
                    Path.Combine(backupDir, $"sku_details.{stage}.db{suffix}") }, 10000);
        }
    }
}
