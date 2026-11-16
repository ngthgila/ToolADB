using System.Text;
using System.Text.RegularExpressions;

namespace ToolAdb.Features.InappTrial
{
    internal sealed partial class InappTrialService
    {
        private static string BuildCanvaSetOfferTagSql(string offerId, string fromTag, string toTag)
        {
            string needle = "'\"offerId\":\"" + offerId + "\"'";
            string tagFrom = "'\"offerTags\":[\"" + fromTag + "\"]'";
            string tagTo = "'\"offerTags\":[\"" + toTag + "\"]'";
            const string tokenSep = "'\"offerIdToken\"'";

            // Guard instr(..., offerIdToken) = 0 đảm bảo chỉ sửa offerTags của đúng offerId này,
            // không đụng sang retrial-14d hoặc offer lân cận. Không sửa expiration_timestamp để tương thích Play 54.x.
            return
                "UPDATE cache_table SET " +
                "cache_data=CAST(" +
                "substr(CAST(cache_data AS TEXT), 1, instr(CAST(cache_data AS TEXT), " + needle + ") - 1) || " +
                "substr(CAST(cache_data AS TEXT), instr(CAST(cache_data AS TEXT), " + needle + "), instr(substr(CAST(cache_data AS TEXT), instr(CAST(cache_data AS TEXT), " + needle + ")), " + tagFrom + ") - 1) || " +
                tagTo + " || " +
                "substr(CAST(cache_data AS TEXT), instr(CAST(cache_data AS TEXT), " + needle + ") + instr(substr(CAST(cache_data AS TEXT), instr(CAST(cache_data AS TEXT), " + needle + ")), " + tagFrom + ") + 18)" +
                " AS BLOB) " +
                "WHERE cache_key LIKE '%callingPackageName=com.canva.editor%' " +
                "AND cache_key LIKE '%skuType=subs%' " +
                "AND instr(CAST(cache_data AS TEXT), " + needle + ") > 0 " +
                "AND instr(substr(CAST(cache_data AS TEXT), instr(CAST(cache_data AS TEXT), " + needle + ")), " + tagFrom + ") > 0 " +
                "AND instr(substr(CAST(cache_data AS TEXT), instr(CAST(cache_data AS TEXT), " + needle + "), instr(substr(CAST(cache_data AS TEXT), instr(CAST(cache_data AS TEXT), " + needle + ")), " + tagFrom + ")), " + tokenSep + ") = 0;\n";
        }

        private static string BuildCanvaOfferTagCheckExpr(string offerId, string expectedTag)
        {
            string needle = "'\"offerId\":\"" + offerId + "\"'";
            string tagLit = "'\"offerTags\":[\"" + expectedTag + "\"]'";
            const string tokenSep = "'\"offerIdToken\"'";
            return
                "(instr(CAST(cache_data AS TEXT), " + needle + ") > 0 " +
                "AND instr(substr(CAST(cache_data AS TEXT), instr(CAST(cache_data AS TEXT), " + needle + ")), " + tagLit + ") > 0 " +
                "AND instr(substr(CAST(cache_data AS TEXT), instr(CAST(cache_data AS TEXT), " + needle + "), instr(substr(CAST(cache_data AS TEXT), instr(CAST(cache_data AS TEXT), " + needle + ")), " + tagLit + ")), " + tokenSep + ") = 0)";
        }

        private string PatchCanva50Off(string serial, bool monthlyInstallments)
        {
            const string pro3mOffer = "pro-solo-v2-monthly-ghc-2026-3m50p";
            const string biz3mOffer = "business-monthly-ghc-2026-3m50p";
            const string bizMonthlyOffer = "business-monthly-ghc-2025-3m-50pc";

            string activeBizOffer = monthlyInstallments ? bizMonthlyOffer : biz3mOffer;
            string relockedBizOffer = monthlyInstallments ? biz3mOffer : bizMonthlyOffer;
            string[] unlockOffers = { pro3mOffer, activeBizOffer };

            // Canva WebView JS (f_n) ẩn mọi offer có tag "ocs" nếu backend không cấp chiến dịch.
            // Đổi "ocs" <-> "pub" (cùng 3 byte) để bật đúng offer mong muốn và khóa lại offer Business còn lại (tránh nyn ưu tiên P1M đè lên P3M).
            var sb = new StringBuilder();
            sb.Append(".bail on\n");
            sb.Append(".timeout 10000\n");
            sb.Append("PRAGMA wal_checkpoint(TRUNCATE);\n");
            sb.Append("SELECT 'CANVA_CACHE_ROWS=' || count(*) FROM cache_table WHERE cache_key LIKE '%callingPackageName=com.canva.editor%' AND cache_key LIKE '%skuType=subs%';\n");
            sb.Append("SELECT 'BYTES_BEFORE=' || ifnull(sum(length(cache_data)),0) FROM cache_table WHERE cache_key LIKE '%callingPackageName=com.canva.editor%' AND cache_key LIKE '%skuType=subs%';\n");
            sb.Append("BEGIN IMMEDIATE;\n");
            foreach (string oid in unlockOffers)
                sb.Append(BuildCanvaSetOfferTagSql(oid, "ocs", "pub"));
            sb.Append(BuildCanvaSetOfferTagSql(relockedBizOffer, "pub", "ocs"));
            sb.Append("COMMIT;\n");
            sb.Append("PRAGMA wal_checkpoint(TRUNCATE);\n");
            sb.Append("SELECT 'OFFERS_STILL_OCS=' || count(*) FROM cache_table WHERE cache_key LIKE '%callingPackageName=com.canva.editor%' AND cache_key LIKE '%skuType=subs%' AND (");
            sb.Append(BuildCanvaOfferTagCheckExpr(unlockOffers[0], "ocs"));
            sb.Append(" OR " + BuildCanvaOfferTagCheckExpr(unlockOffers[1], "ocs"));
            sb.Append(");\n");
            sb.Append("SELECT 'RELOCKED_STILL_PUB=' || count(*) FROM cache_table WHERE cache_key LIKE '%callingPackageName=com.canva.editor%' AND cache_key LIKE '%skuType=subs%' AND ");
            sb.Append(BuildCanvaOfferTagCheckExpr(relockedBizOffer, "pub"));
            sb.Append(";\n");
            sb.Append("SELECT 'OFFERS_UNLOCKED=' || count(*) FROM cache_table WHERE cache_key LIKE '%callingPackageName=com.canva.editor%' AND cache_key LIKE '%skuType=subs%' AND (");
            sb.Append(BuildCanvaOfferTagCheckExpr(unlockOffers[0], "pub"));
            sb.Append(" OR " + BuildCanvaOfferTagCheckExpr(unlockOffers[1], "pub"));
            sb.Append(");\n");
            sb.Append("SELECT 'BYTES_AFTER=' || ifnull(sum(length(cache_data)),0) FROM cache_table WHERE cache_key LIKE '%callingPackageName=com.canva.editor%' AND cache_key LIKE '%skuType=subs%';\n");

            string modeLabel = monthlyInstallments ? "thu từng tháng (105k/tháng x3)" : "trả 1 lần 3 tháng";
            return ExecutePlayBillingCachePatch(
                serial,
                "Canva",
                "com.canva.editor",
                "Chưa có cache Google Play Billing. Hãy mở Canva và màn hình Nâng cấp một lần rồi chạy lại.",
                "🎨 Đã bật ưu đãi Canva 50% (" + modeLabel + ").",
                monthlyInstallments ? "patch_canva_50off_monthly.sql" : "patch_canva_50off_3m.sql",
                monthlyInstallments ? "canva_50off_monthly" : "canva_50off_3m",
                monthlyInstallments ? "canva_50off_monthly.log" : "canva_50off_3m.log",
                sb.ToString(),
                (patchOutput, markers) =>
                {
                    Match cacheRows = Regex.Match(patchOutput, @"CANVA_CACHE_ROWS=(\d+)");
                    Match unlockedRows = Regex.Match(patchOutput, @"OFFERS_UNLOCKED=(\d+)");
                    Match bytesBefore = Regex.Match(patchOutput, @"BYTES_BEFORE=(\d+)");
                    Match bytesAfter = Regex.Match(patchOutput, @"BYTES_AFTER=(\d+)");
                    if (!cacheRows.Success || Convert.ToInt32(cacheRows.Groups[1].Value) == 0)
                        throw new InvalidDataException("Không tìm thấy dòng subscription của Canva trong cache Billing. Hãy mở Canva và màn hình Nâng cấp trước rồi thử lại. [" + markers + "]");
                    if (!patchOutput.Contains("OFFERS_STILL_OCS=0") || !patchOutput.Contains("RELOCKED_STILL_PUB=0"))
                        throw new InvalidDataException("Trạng thái tag \"ocs\"/\"pub\" sau khi vá chưa khớp chế độ " + modeLabel + ". [" + markers + "]");
                    if (!unlockedRows.Success || Convert.ToInt32(unlockedRows.Groups[1].Value) == 0)
                        throw new InvalidDataException("Không tìm thấy offer giảm 50% phù hợp của Canva trong cache Billing. [" + markers + "]");
                    if (!bytesBefore.Success || !bytesAfter.Success || bytesBefore.Groups[1].Value != bytesAfter.Groups[1].Value)
                        throw new InvalidDataException("Kích thước cache Billing thay đổi ngoài dự kiến. [" + markers + "]");
                });
        }
    }
}
