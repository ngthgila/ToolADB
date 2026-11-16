using System.Text.RegularExpressions;

namespace ToolAdb.Features.InappTrial
{
    internal sealed partial class InappTrialService
    {
        private string PatchCrunchyrollNoTrial(string serial)
        {
            // Crunchyroll (dh0.s.m / dh0.j) tìm offer có tag "free-trial" trước khi fallback sang
            // base plan (dh0.s.d: offerTags rỗng []). Đổi nhãn bằng chuỗi cùng độ dài giữ nguyên protobuf framing.
            // Không sửa expiration_timestamp và chạy wal_checkpoint(TRUNCATE) để tương thích Google Play 54.x.
            string sql =
                ".bail on\n" +
                ".timeout 10000\n" +
                "PRAGMA wal_checkpoint(TRUNCATE);\n" +
                "SELECT 'CRUNCHY_CACHE_ROWS=' || count(*) FROM cache_table " +
                "WHERE cache_key LIKE '%callingPackageName=com.crunchyroll.crunchyroid%' AND cache_key LIKE '%skuType=subs%';\n" +
                "SELECT 'BYTES_BEFORE=' || ifnull(sum(length(cache_data)),0) FROM cache_table " +
                "WHERE cache_key LIKE '%callingPackageName=com.crunchyroll.crunchyroid%' AND cache_key LIKE '%skuType=subs%';\n" +
                "BEGIN IMMEDIATE;\n" +
                "UPDATE cache_table SET " +
                "cache_data=CAST(replace(replace(replace(CAST(cache_data AS TEXT)," +
                "'\"offerId\":\"freetrial\"','\"offerId\":\"notrialxx\"')," +
                "'\"extended-free-trial\"','\"no-extended-trialxx\"')," +
                "'\"free-trial\"','\"no-trialxx\"') AS BLOB) " +
                "WHERE cache_key LIKE '%callingPackageName=com.crunchyroll.crunchyroid%' " +
                "AND cache_key LIKE '%skuType=subs%' " +
                "AND (instr(cache_data,'\"offerId\":\"freetrial\"')>0 " +
                "OR instr(cache_data,'\"extended-free-trial\"')>0 " +
                "OR instr(cache_data,'\"free-trial\"')>0 " +
                "OR instr(cache_data,'\"offerId\":\"notrialxx\"')>0 " +
                "OR instr(cache_data,'\"no-extended-trialxx\"')>0 " +
                "OR instr(cache_data,'\"no-trialxx\"')>0);\n" +
                "SELECT 'TOUCHED_ROWS=' || changes();\n" +
                "COMMIT;\n" +
                "PRAGMA wal_checkpoint(TRUNCATE);\n" +
                "SELECT 'TRIAL_REMAINING=' || count(*) FROM cache_table " +
                "WHERE cache_key LIKE '%callingPackageName=com.crunchyroll.crunchyroid%' " +
                "AND (instr(cache_data,'\"offerId\":\"freetrial\"')>0 " +
                "OR instr(cache_data,'\"extended-free-trial\"')>0 " +
                "OR instr(cache_data,'\"free-trial\"')>0);\n" +
                "SELECT 'PATCHED_ROWS_PRESENT=' || count(*) FROM cache_table " +
                "WHERE cache_key LIKE '%callingPackageName=com.crunchyroll.crunchyroid%' " +
                "AND (instr(cache_data,'\"offerId\":\"notrialxx\"')>0 " +
                "OR instr(cache_data,'\"no-extended-trialxx\"')>0 " +
                "OR instr(cache_data,'\"no-trialxx\"')>0);\n" +
                "SELECT 'BASE_PLAN_ROWS=' || count(*) FROM cache_table " +
                "WHERE cache_key LIKE '%callingPackageName=com.crunchyroll.crunchyroid%' " +
                "AND instr(cache_data,'\"offerTags\":[]')>0;\n" +
                "SELECT 'BYTES_AFTER=' || ifnull(sum(length(cache_data)),0) FROM cache_table " +
                "WHERE cache_key LIKE '%callingPackageName=com.crunchyroll.crunchyroid%' AND cache_key LIKE '%skuType=subs%';\n";

            return ExecutePlayBillingCachePatch(
                serial,
                "Crunchyroll",
                "com.crunchyroll.crunchyroid",
                "Chưa có cache Google Play Billing. Hãy mở Crunchyroll và bảng gói một lần rồi chạy lại.",
                "🍙 Đã bỏ offer trial của Crunchyroll, giữ base plan để mua trực tiếp.",
                "patch_crunchyroll_no_trial.sql",
                "crunchyroll_no_trial",
                "crunchyroll_patch.log",
                sql,
                (patchOutput, markers) =>
                {
                    Match cacheRows = Regex.Match(patchOutput, @"CRUNCHY_CACHE_ROWS=(\d+)");
                    Match patchedRows = Regex.Match(patchOutput, @"PATCHED_ROWS_PRESENT=(\d+)");
                    Match baseRows = Regex.Match(patchOutput, @"BASE_PLAN_ROWS=(\d+)");
                    Match bytesBefore = Regex.Match(patchOutput, @"BYTES_BEFORE=(\d+)");
                    Match bytesAfter = Regex.Match(patchOutput, @"BYTES_AFTER=(\d+)");
                    if (!patchOutput.Contains("TRIAL_REMAINING=0"))
                        throw new InvalidDataException("Cache Billing vẫn còn offer trial sau khi vá. [" + markers + "]");
                    if (!cacheRows.Success || Convert.ToInt32(cacheRows.Groups[1].Value) == 0)
                        throw new InvalidDataException("Không tìm thấy dòng subscription của Crunchyroll trong cache Billing. Hãy mở Crunchyroll và bảng gói trước rồi thử lại. [" + markers + "]");
                    if (!patchedRows.Success || Convert.ToInt32(patchedRows.Groups[1].Value) == 0)
                        throw new InvalidDataException("Không tìm thấy offer trial để vô hiệu hóa. [" + markers + "]");
                    if (!baseRows.Success || Convert.ToInt32(baseRows.Groups[1].Value) == 0)
                        throw new InvalidDataException("Không còn base plan để thanh toán trực tiếp; đã dừng để tránh lỗi mua hàng. [" + markers + "]");
                    if (!bytesBefore.Success || !bytesAfter.Success || bytesBefore.Groups[1].Value != bytesAfter.Groups[1].Value)
                        throw new InvalidDataException("Kích thước cache Billing thay đổi ngoài dự kiến. [" + markers + "]");
                });
        }
    }
}
