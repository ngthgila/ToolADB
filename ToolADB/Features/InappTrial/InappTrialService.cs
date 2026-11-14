using ToolAdb.Infrastructure;

namespace ToolAdb.Features.InappTrial
{
    internal enum InappTrialAction
    {
        GrammarlyNoTrial,
        ChessNoTrial
        // Thêm app mới: bổ sung giá trị tại đây, viết InappTrialService.<App>.cs
        // và khai báo nút trong InappTrialTab.
    }

    internal sealed partial class InappTrialService
    {
        private readonly AdbClient _adb;

        internal InappTrialService(AdbClient adb) => _adb = adb;

        internal string Run(string serial, InappTrialAction action)
        {
            // Giống ProcessSingleDevice gốc: "adb root" 5 giây, không kiểm tra kết quả.
            CaptureAdb(serial, 5000, "root");
            return action switch
            {
                InappTrialAction.GrammarlyNoTrial => PatchGrammarlyNoTrial(serial),
                InappTrialAction.ChessNoTrial => PatchChessNoTrial(serial),
                _ => throw new ArgumentOutOfRangeException(nameof(action))
            };
        }

        // Ngữ nghĩa giống RunAdb của Inapp-Patch: trả về stdout, không kiểm tra exit code.
        // Mọi bước port nguyên văn (Chess, Grammarly, quét offer) dùng hàm này để không đổi hành vi so với mã gốc.
        private string CaptureAdb(string serial, int timeoutMs, params string[] args)
        {
            var command = new List<string> { "-s", serial };
            command.AddRange(args);
            return _adb.Capture(command, timeoutMs).stdout ?? "";
        }
    }
}
