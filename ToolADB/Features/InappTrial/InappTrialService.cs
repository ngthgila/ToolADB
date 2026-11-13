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
            EnsureRoot(serial);
            return action switch
            {
                InappTrialAction.GrammarlyNoTrial => PatchGrammarlyNoTrial(serial),
                InappTrialAction.ChessNoTrial => PatchChessNoTrial(serial),
                _ => throw new ArgumentOutOfRangeException(nameof(action))
            };
        }

        private void EnsureRoot(string serial)
        {
            if (Shell(serial, "id -u").Trim() == "0") return;

            string root = Execute(serial, 8000, "root");
            if (root.Contains("cannot run as root", StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException(root.Trim());
            Execute(serial, 12000, "wait-for-device");
            if (Shell(serial, "id -u").Trim() != "0")
                throw new InvalidOperationException("Thiết bị không cấp quyền ADB root.");
        }

        private string Shell(string serial, string command) => Execute(serial, 15000, "shell", command);

        private string Execute(string serial, int timeoutMs, params string[] args)
        {
            var command = new List<string> { "-s", serial };
            command.AddRange(args);
            var result = _adb.Capture(command, timeoutMs);
            if (result.exitCode != 0 || result.stdout.TrimStart().StartsWith("Error:", StringComparison.OrdinalIgnoreCase))
            {
                string message = string.IsNullOrWhiteSpace(result.stderr) ? result.stdout : result.stderr;
                throw new InvalidOperationException(string.IsNullOrWhiteSpace(message)
                    ? $"ADB trả mã lỗi {result.exitCode}." : message.Trim());
            }
            return result.stdout;
        }
    }
}
