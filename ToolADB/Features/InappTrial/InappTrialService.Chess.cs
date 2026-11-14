using System.Text;
using System.Text.RegularExpressions;

namespace ToolAdb.Features.InappTrial
{
    internal sealed partial class InappTrialService
    {
        private const string PlayBillingDb = "/data/data/com.android.vending/databases/sku_details.db";

        // Port nguyên văn PatchChessNoTrial của Inapp-Patch/PicsartToolGUI.cs:
        // cùng chuỗi lệnh device-side, cùng thứ tự, cùng timeout; adb được gọi
        // qua CaptureAdb (không kiểm tra exit code) đúng như RunAdb của mã gốc.
        // Phần thêm chỉ ở phía máy tính: ghi chess_patch.log (preflight + output sqlite3)
        // vào thư mục backup và hiện các marker trong kết quả để đối chiếu khi cần.
        private string PatchChessNoTrial(string serial)
        {
            const string playDb = PlayBillingDb;
            const string farFutureExpiration = "4102444800000"; // 2100-01-01 UTC

            string safeSerial = Regex.Replace(serial, @"[^A-Za-z0-9._-]", "_");
            string backupDir = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "ToolADB", "Backups", "Chess", safeSerial,
                DateTime.Now.ToString("yyyyMMdd_HHmmss"));
            string dbBefore = Path.Combine(backupDir, "sku_details.before.db");
            string dbAfter = Path.Combine(backupDir, "sku_details.after.db");
            string sqlFile = Path.Combine(backupDir, "patch_chess_no_trial.sql");
            string remoteSql = "/data/local/tmp/chess_no_trial_" + safeSerial + ".sql";
            string logFile = Path.Combine(backupDir, "chess_patch.log");

            Directory.CreateDirectory(backupDir);
            var log = new StringBuilder();
            log.AppendLine("serial=" + serial + "  time=" + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"));
            try
            {
                string preflight = CaptureAdb(serial, 8000, "shell",
                    "test -f " + playDb + " && echo PLAY_DB_OK; command -v sqlite3; pm path com.chess");
                log.AppendLine("--- preflight").AppendLine(preflight.TrimEnd());
                if (!preflight.Contains("PLAY_DB_OK"))
                {
                    throw new FileNotFoundException("Chưa có cache Google Play Billing. Hãy mở Chess.com và trang Premium một lần rồi chạy lại.");
                }
                if (!preflight.Contains("sqlite3"))
                {
                    throw new FileNotFoundException("Thiết bị không có sqlite3 để sửa cache Billing an toàn.");
                }
                if (!preflight.Contains("package:"))
                {
                    throw new FileNotFoundException("Không tìm thấy ứng dụng com.chess trên thiết bị.");
                }

                CaptureAdb(serial, 8000, "shell", "am force-stop com.chess; am force-stop com.android.vending");
                CaptureAdb(serial, 10000, "pull", playDb, dbBefore);
                CaptureAdb(serial, 10000, "pull", playDb + "-wal", Path.Combine(backupDir, "sku_details.before.db-wal"));
                CaptureAdb(serial, 10000, "pull", playDb + "-shm", Path.Combine(backupDir, "sku_details.before.db-shm"));
                if (!File.Exists(dbBefore) || new FileInfo(dbBefore).Length == 0)
                {
                    throw new IOException("Không thể sao lưu sku_details.db trước khi sửa.");
                }
                log.AppendLine("--- backup before: " + new FileInfo(dbBefore).Length + " bytes"
                    + (File.Exists(dbBefore + "-wal") ? ", wal " + new FileInfo(dbBefore + "-wal").Length + " bytes" : ", no wal"));

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
                    "expiration_timestamp=" + farFutureExpiration + " " +
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
                File.WriteAllText(sqlFile, sql, new UTF8Encoding(false));
                CaptureAdb(serial, 10000, "push", sqlFile, remoteSql);

                string patchOutput = CaptureAdb(serial, 20000, "shell",
                    "sqlite3 " + playDb + " < " + remoteSql + "; rm -f " + remoteSql);
                log.AppendLine("--- sqlite3 output").AppendLine(patchOutput.TrimEnd());
                string markers = string.Join(", ", Regex.Matches(patchOutput, @"\b[A-Z_]+=\d+").Select(m => m.Value));
                if (markers.Length == 0) markers = "không có marker nào (sqlite3 không trả output)";

                Match cacheRows = Regex.Match(patchOutput, @"CHESS_CACHE_ROWS=(\d+)");
                Match patchedRows = Regex.Match(patchOutput, @"PATCHED_ROWS_PRESENT=(\d+)");
                Match baseRows = Regex.Match(patchOutput, @"BASE_PLAN_ROWS=(\d+)");
                Match bytesBefore = Regex.Match(patchOutput, @"BYTES_BEFORE=(\d+)");
                Match bytesAfter = Regex.Match(patchOutput, @"BYTES_AFTER=(\d+)");
                if (!patchOutput.Contains("TRIAL_REMAINING=0"))
                {
                    throw new InvalidDataException("Cache Billing vẫn còn offer trial sau khi vá. [" + markers + "]");
                }
                if (!cacheRows.Success || Convert.ToInt32(cacheRows.Groups[1].Value) == 0)
                {
                    throw new InvalidDataException("Không tìm thấy dòng subscription của Chess trong cache Billing. [" + markers + "]");
                }
                if (!patchedRows.Success || Convert.ToInt32(patchedRows.Groups[1].Value) == 0)
                {
                    throw new InvalidDataException("Không tìm thấy offer trial để vô hiệu hóa. [" + markers + "]");
                }
                if (!baseRows.Success || Convert.ToInt32(baseRows.Groups[1].Value) == 0)
                {
                    throw new InvalidDataException("Không còn base plan để thanh toán trực tiếp; đã dừng để tránh lỗi mua hàng. [" + markers + "]");
                }
                if (!bytesBefore.Success || !bytesAfter.Success || bytesBefore.Groups[1].Value != bytesAfter.Groups[1].Value)
                {
                    throw new InvalidDataException("Kích thước cache Billing thay đổi ngoài dự kiến. [" + markers + "]");
                }

                CaptureAdb(serial, 10000, "pull", playDb, dbAfter);
                CaptureAdb(serial, 10000, "pull", playDb + "-wal", Path.Combine(backupDir, "sku_details.after.db-wal"));
                CaptureAdb(serial, 10000, "pull", playDb + "-shm", Path.Combine(backupDir, "sku_details.after.db-shm"));
                CaptureAdb(serial, 8000, "shell", "monkey -p com.chess -c android.intent.category.LAUNCHER 1");
                log.AppendLine("--- OK");
                return "♟ Đã bỏ offer trial của Chess.com, giữ base plan để mua trực tiếp. [" + markers + "] Backup: " + backupDir;
            }
            catch (Exception ex)
            {
                log.AppendLine("--- ERROR").AppendLine(ex.Message);
                throw new InvalidOperationException(ex.Message + " — log: " + logFile, ex);
            }
            finally
            {
                try { File.WriteAllText(logFile, log.ToString(), new UTF8Encoding(false)); } catch (IOException) { }
            }
        }
    }
}
