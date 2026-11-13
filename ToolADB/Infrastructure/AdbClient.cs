using System.Diagnostics;
using System.Text;

namespace ToolAdb.Infrastructure
{
    internal sealed class AdbClient
    {
        private readonly string _workingDirectory;

        internal AdbClient(string workingDirectory)
        {
            _workingDirectory = workingDirectory;
            ExecutablePath = Path.Combine(workingDirectory, "adb.exe");
        }

        internal string ExecutablePath { get; }

        internal void Start(string args)
        {
            using var process = Process.Start(new ProcessStartInfo
            {
                FileName = ExecutablePath,
                Arguments = args,
                UseShellExecute = false,
                CreateNoWindow = true
            });
        }

        internal int RunOnDevice(string serial, params string[] args)
        {
            var command = new List<string> { "-s", serial };
            command.AddRange(args);
            return Run(command);
        }

        internal static List<string> ParseAdbDevices(string output, string state)
        {
            return output.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries)
                .Select(line => line.Split('\t', 2))
                .Where(fields => fields.Length == 2 && fields[0].Trim().Length > 0 &&
                    fields[1].Trim().Equals(state, StringComparison.OrdinalIgnoreCase))
                .Select(fields => fields[0].Trim())
                .ToList();
        }

        internal string ReadOutput(string args, int timeoutMs = 5000)
        {
            var result = CaptureProcess(new ProcessStartInfo(ExecutablePath, args), timeoutMs);
            return result.exitCode == 0 ? result.stdout : "";
        }

        internal (int exitCode, string stdout, string stderr) Capture(
            IEnumerable<string> args, int timeoutMs = 300000)
        {
            var startInfo = new ProcessStartInfo(ExecutablePath) { WorkingDirectory = _workingDirectory };
            foreach (var arg in args) startInfo.ArgumentList.Add(arg);
            return CaptureProcess(startInfo, timeoutMs);
        }

        private static (int exitCode, string stdout, string stderr) CaptureProcess(
            ProcessStartInfo startInfo, int timeoutMs)
        {
            try
            {
                startInfo.UseShellExecute = false;
                startInfo.RedirectStandardOutput = true;
                startInfo.RedirectStandardError = true;
                startInfo.CreateNoWindow = true;
                startInfo.StandardOutputEncoding = Encoding.UTF8;
                startInfo.StandardErrorEncoding = Encoding.UTF8;

                using var process = Process.Start(startInfo);
                if (process == null) return (-1, "", "Không khởi chạy được tiến trình.");

                using var timeout = new CancellationTokenSource(timeoutMs);
                var stdout = process.StandardOutput.ReadToEndAsync(timeout.Token);
                var stderr = process.StandardError.ReadToEndAsync(timeout.Token);
                try
                {
                    Task.WhenAll(process.WaitForExitAsync(timeout.Token), stdout, stderr)
                        .WaitAsync(timeout.Token).GetAwaiter().GetResult();
                }
                catch (OperationCanceledException)
                {
                    try { process.Kill(entireProcessTree: true); }
                    catch (InvalidOperationException) { }
                    catch (System.ComponentModel.Win32Exception) { }
                    return (-1, "", $"Tiến trình quá thời gian chờ ({timeoutMs} ms).");
                }

                return (process.ExitCode, stdout.Result, stderr.Result);
            }
            catch (Exception ex)
            {
                return (-1, "", ex.Message);
            }
        }

        internal int Run(IEnumerable<string> args, int timeoutMs = 30000)
        {
            try
            {
                var p = new ProcessStartInfo { FileName = ExecutablePath, UseShellExecute = false, CreateNoWindow = true, WorkingDirectory = _workingDirectory };
                foreach (var arg in args) p.ArgumentList.Add(arg);
                using var proc = Process.Start(p);
                if (proc == null) return -1;
                if (!proc.WaitForExit(timeoutMs))
                {
                    try { proc.Kill(true); } catch { }
                    return -1;
                }
                return proc.ExitCode;
            }
            catch { return -1; }
        }
    }
}
