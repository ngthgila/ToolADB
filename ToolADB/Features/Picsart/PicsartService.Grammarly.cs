namespace ToolAdb.Features.Picsart
{
    internal sealed partial class PicsartService
    {
        private const string GrammarlyPackage = "com.grammarly.android.keyboard";
        private const string GrammarlyData = "/data/data/" + GrammarlyPackage;
        private const string TreatmentsPath = GrammarlyData + "/files/datastore/manakin-treatments";
        private const string OpenGrammarly = "am start -n " + GrammarlyPackage + "/com.grammarly.widget.ui.settings.MainCompanionActivity";

        private string PatchGrammarlyNoTrial(string serial)
        {
            Shell(serial, $"test -f {TreatmentsPath} || {{ echo 'Thiếu cấu hình Grammarly. Hãy mở ứng dụng và đăng nhập trước.' >&2; exit 1; }}");
            string local = Path.GetTempFileName();
            string staged = TreatmentsPath + ".tooladb-" + Guid.NewGuid().ToString("N");
            bool stageStarted = false;

            try
            {
                Execute(serial, 8000, "pull", TreatmentsPath, local);
                byte[] original = File.ReadAllBytes(local);
                byte[] updated = UpdateGrammarlyTreatment(original);
                if (updated != null)
                {
                    File.WriteAllBytes(local, updated);
                    stageStarted = true;
                    Execute(serial, 8000, "push", local, staged);
                    Shell(serial, "set -e; " +
                        $"am force-stop {GrammarlyPackage}; " +
                        $"owner=$(stat -c '%u:%g' {TreatmentsPath}); " +
                        $"chown \"$owner\" {staged}; chmod 440 {staged}; restorecon {staged}; " +
                        $"mv -f {staged} {TreatmentsPath}");
                }

                Shell(serial, OpenGrammarly);
                return updated == null
                    ? "Cấu hình cục bộ đã ở nhóm control_1. Đã gửi lệnh mở Grammarly."
                    : "Đã cập nhật cấu hình cục bộ và gửi lệnh mở Grammarly.";
            }
            finally
            {
                if (stageStarted)
                    _adb.RunOnDevice(serial, "shell", $"rm -f {staged}");
                File.Delete(local);
            }
        }

        internal static byte[] UpdateGrammarlyTreatment(ReadOnlySpan<byte> data)
        {
            byte[] originalHeader = Convert.FromHexString("1A660A12616E64726F69645F667265655F747269616C");
            byte[] updatedHeader = Convert.FromHexString("1A690A12616E64726F69645F667265655F747269616C");
            byte[] test = Convert.FromHexString("2A06746573745F31");
            byte[] control = Convert.FromHexString("2A09636F6E74726F6C5F31");

            int start = data.IndexOf(originalHeader);
            int updatedStart = data.IndexOf(updatedHeader);
            if (start < 0 && updatedStart >= 0 &&
                data.Length - updatedStart >= 2 + updatedHeader[1] &&
                data.Slice(updatedStart + updatedHeader.Length, 2 + updatedHeader[1] - updatedHeader.Length).IndexOf(control) >= 0)
                return null;

            if (start < 0 || updatedStart >= 0 || data.Length - start < 2 + originalHeader[1] ||
                data[(start + originalHeader.Length)..].IndexOf(originalHeader) >= 0)
                throw new InvalidOperationException("Không nhận diện được cấu hình thử nghiệm Grammarly.");

            int entryEnd = start + 2 + originalHeader[1];
            int fieldsStart = start + originalHeader.Length;
            ReadOnlySpan<byte> fields = data[fieldsStart..entryEnd];
            int match = fields.IndexOf(test);
            if (match < 0 || fields[(match + test.Length)..].IndexOf(test) >= 0)
                throw new InvalidOperationException("Không xác định được nhóm thử nghiệm Grammarly trong cấu hình.");

            int group = fieldsStart + match;
            byte[] result = new byte[data.Length + control.Length - test.Length];
            data[..start].CopyTo(result);
            updatedHeader.CopyTo(result.AsSpan(start));
            data[fieldsStart..group].CopyTo(result.AsSpan(fieldsStart));
            control.CopyTo(result.AsSpan(group));
            data[(group + test.Length)..].CopyTo(result.AsSpan(group + control.Length));
            return result;
        }
    }
}
