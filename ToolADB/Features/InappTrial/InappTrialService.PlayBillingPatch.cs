using System.Text;
using System.Text.RegularExpressions;

namespace ToolAdb.Features.InappTrial
{
    internal sealed partial class InappTrialService
    {
        private string ExecutePlayBillingCachePatch(
            string serial,
            string backupAppFolder,
            string packageName,
            string notCachedMessage,
            string statusDone,
            string sqlFileName,
            string remoteSqlPrefix,
            string logFileName,
            string sql,
            Action<string, string> validateOutput)
        {
            const string playDb = PlayBillingDb;

            string safeSerial = Regex.Replace(serial, @"[^A-Za-z0-9._-]", "_");
            string backupDir = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "ToolADB", "Backups", backupAppFolder, safeSerial,
                DateTime.Now.ToString("yyyyMMdd_HHmmss"));
            string dbBefore = Path.Combine(backupDir, "sku_details.before.db");
            string dbAfter = Path.Combine(backupDir, "sku_details.after.db");
            string sqlFile = Path.Combine(backupDir, sqlFileName);
            string remoteSql = "/data/local/tmp/" + remoteSqlPrefix + "_" + safeSerial + ".sql";
            string logFile = Path.Combine(backupDir, logFileName);

            Directory.CreateDirectory(backupDir);
            var log = new StringBuilder();
            log.AppendLine("serial=" + serial + "  time=" + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"));
            try
            {
                string preflight = CaptureAdb(serial, 8000, "shell",
                    "test -f " + playDb + " && echo PLAY_DB_OK; command -v sqlite3; pm path " + packageName);
                log.AppendLine("--- preflight").AppendLine(preflight.TrimEnd());
                if (!preflight.Contains("PLAY_DB_OK"))
                    throw new FileNotFoundException(notCachedMessage);
                if (!preflight.Contains("sqlite3"))
                    throw new FileNotFoundException("Thiết bị không có sqlite3 để sửa cache Billing an toàn.");
                if (!preflight.Contains("package:"))
                    throw new FileNotFoundException("Không tìm thấy ứng dụng " + packageName + " trên thiết bị.");

                CaptureAdb(serial, 8000, "shell", "am force-stop " + packageName + "; am force-stop com.android.vending");
                CaptureAdb(serial, 10000, "pull", playDb, dbBefore);
                CaptureAdb(serial, 10000, "pull", playDb + "-wal", Path.Combine(backupDir, "sku_details.before.db-wal"));
                CaptureAdb(serial, 10000, "pull", playDb + "-shm", Path.Combine(backupDir, "sku_details.before.db-shm"));
                if (!File.Exists(dbBefore) || new FileInfo(dbBefore).Length == 0)
                    throw new IOException("Không thể sao lưu sku_details.db trước khi sửa.");

                log.AppendLine("--- backup before: " + new FileInfo(dbBefore).Length + " bytes"
                    + (File.Exists(dbBefore + "-wal") ? ", wal " + new FileInfo(dbBefore + "-wal").Length + " bytes" : ", no wal"));

                File.WriteAllText(sqlFile, sql, new UTF8Encoding(false));
                CaptureAdb(serial, 10000, "push", sqlFile, remoteSql);

                string patchOutput = CaptureAdb(serial, 20000, "shell",
                    "sqlite3 " + playDb + " < " + remoteSql + "; rm -f " + remoteSql +
                    "; VUID=$(stat -c '%u' /data/data/com.android.vending); chown $VUID:$VUID " + playDb + "* 2>/dev/null; restorecon " + playDb + "* 2>/dev/null");
                log.AppendLine("--- sqlite3 output").AppendLine(patchOutput.TrimEnd());

                string markers = string.Join(", ", Regex.Matches(patchOutput, @"\b[A-Z_]+=\d+").Select(m => m.Value));
                if (markers.Length == 0) markers = "không có marker nào (sqlite3 không trả output)";

                validateOutput(patchOutput, markers);

                CaptureAdb(serial, 10000, "pull", playDb, dbAfter);
                CaptureAdb(serial, 10000, "pull", playDb + "-wal", Path.Combine(backupDir, "sku_details.after.db-wal"));
                CaptureAdb(serial, 10000, "pull", playDb + "-shm", Path.Combine(backupDir, "sku_details.after.db-shm"));
                CaptureAdb(serial, 8000, "shell", "monkey -p " + packageName + " -c android.intent.category.LAUNCHER 1");
                log.AppendLine("--- OK");
                return statusDone + " [" + markers + "] Backup: " + backupDir;
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
