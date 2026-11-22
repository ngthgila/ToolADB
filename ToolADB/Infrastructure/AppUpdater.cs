using System.Diagnostics;
using System.Net.Http;
using System.Reflection;
using System.Text;
using System.Text.Json;

namespace ToolAdb.Infrastructure
{
    /// <summary>
    /// Kiểm tra và tự động cập nhật file ToolADB.exe lẻ từ GitHub Releases (ngthgila/ToolADB).
    /// </summary>
    internal static class AppUpdater
    {
        private const string LatestReleaseUrl = "https://api.github.com/repos/ngthgila/ToolADB/releases/latest";
        private static readonly HttpClient Http = CreateHttpClient();
        private static int _checking;

        internal static string CurrentVersionString
        {
            get
            {
                string raw = typeof(AppUpdater).Assembly
                    .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion
                    ?? typeof(AppUpdater).Assembly.GetName().Version?.ToString(3)
                    ?? "1.0.0";
                int plus = raw.IndexOf('+');
                return plus >= 0 ? raw[..plus] : raw;
            }
        }

        private static HttpClient CreateHttpClient()
        {
            var client = new HttpClient { Timeout = TimeSpan.FromSeconds(10) };
            client.DefaultRequestHeaders.UserAgent.ParseAdd("ToolADB-Updater/1.0");
            client.DefaultRequestHeaders.Accept.ParseAdd("application/vnd.github+json");
            return client;
        }

        internal static async Task CheckAndPromptAsync(Form owner, bool silent)
        {
            if (Interlocked.Exchange(ref _checking, 1) == 1)
                return;

            try
            {
                Version current = ParseVersion(CurrentVersionString);
                using var req = new HttpRequestMessage(HttpMethod.Get, LatestReleaseUrl);
                using var resp = await Http.SendAsync(req);
                if (!resp.IsSuccessStatusCode)
                {
                    if (!silent) ShowInfo(owner, $"Không kiểm tra được bản cập nhật (HTTP {(int)resp.StatusCode}).");
                    return;
                }

                using var doc = JsonDocument.Parse(await resp.Content.ReadAsStringAsync());
                var root = doc.RootElement;
                string tag = root.TryGetProperty("tag_name", out var tagEl) ? tagEl.GetString() ?? "" : "";
                Version latest = ParseVersion(tag);
                if (latest == null || current == null || latest <= current)
                {
                    if (!silent) ShowInfo(owner, $"Bạn đang dùng phiên bản mới nhất (v{CurrentVersionString}).");
                    return;
                }

                string exeUrl = FindExeAssetUrl(root);
                if (string.IsNullOrEmpty(exeUrl))
                {
                    if (!silent) ShowInfo(owner, $"Đã có bản {tag}, nhưng release chưa đính kèm file ToolADB.exe lẻ.");
                    return;
                }

                if (!ConfirmUpdate(owner, tag))
                    return;

                string currentExe = Environment.ProcessPath;
                if (string.IsNullOrEmpty(currentExe) ||
                    !currentExe.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(Path.GetFileName(currentExe), "dotnet.exe", StringComparison.OrdinalIgnoreCase))
                {
                    ShowError(owner, "Chỉ hỗ trợ tự cập nhật khi chạy từ file ToolADB.exe.");
                    return;
                }

                string stagePath = Path.Combine(Path.GetDirectoryName(currentExe)!, "ToolADB.exe.new");
                byte[] payload = await Http.GetByteArrayAsync(exeUrl);
                if (payload.Length < 100_000 || payload[0] != (byte)'M' || payload[1] != (byte)'Z')
                    throw new InvalidDataException("File tải về không phải ToolADB.exe hợp lệ.");
                await File.WriteAllBytesAsync(stagePath, payload);

                LaunchReplaceAndRestart(currentExe, stagePath);
                if (owner != null && !owner.IsDisposed)
                    owner.BeginInvoke(new Action(Application.Exit));
                else
                    Application.Exit();
            }
            catch (Exception ex)
            {
                if (!silent) ShowError(owner, "Lỗi kiểm tra/cập nhật: " + ex.Message);
            }
            finally
            {
                Interlocked.Exchange(ref _checking, 0);
            }
        }

        private static string FindExeAssetUrl(JsonElement root)
        {
            if (!root.TryGetProperty("assets", out var assets) || assets.ValueKind != JsonValueKind.Array)
                return null;
            foreach (var asset in assets.EnumerateArray())
            {
                string name = asset.TryGetProperty("name", out var n) ? n.GetString() ?? "" : "";
                if (string.Equals(name, "ToolADB.exe", StringComparison.OrdinalIgnoreCase) &&
                    asset.TryGetProperty("browser_download_url", out var u))
                    return u.GetString();
            }
            return null;
        }

        private static Version ParseVersion(string raw)
        {
            if (string.IsNullOrWhiteSpace(raw)) return null;
            string clean = raw.Trim().TrimStart('v', 'V');
            int dash = clean.IndexOf('-');
            if (dash >= 0) clean = clean[..dash];
            return Version.TryParse(clean, out var v) ? v : null;
        }

        private static void LaunchReplaceAndRestart(string currentExe, string stagePath)
        {
            int pid = Environment.ProcessId;
            string scriptPath = Path.Combine(Path.GetTempPath(), $"tooladb_update_{pid}.cmd");
            string script =
                "@echo off\r\n" +
                ":wait\r\n" +
                $"tasklist /FI \"PID eq {pid}\" 2>nul | find \"{pid}\" >nul\r\n" +
                "if not errorlevel 1 (\r\n" +
                "  timeout /t 1 /nobreak >nul\r\n" +
                "  goto wait\r\n" +
                ")\r\n" +
                "for /L %%i in (1,1,10) do (\r\n" +
                $"  move /Y \"{stagePath}\" \"{currentExe}\" >nul 2>nul && goto run\r\n" +
                "  timeout /t 1 /nobreak >nul\r\n" +
                ")\r\n" +
                "goto end\r\n" +
                ":run\r\n" +
                $"start \"\" \"{currentExe}\"\r\n" +
                ":end\r\n" +
                "del \"%~f0\"\r\n";

            File.WriteAllText(scriptPath, script, Encoding.ASCII);
            Process.Start(new ProcessStartInfo
            {
                FileName = "cmd.exe",
                Arguments = $"/c \"{scriptPath}\"",
                CreateNoWindow = true,
                WindowStyle = ProcessWindowStyle.Hidden,
                UseShellExecute = false
            });
        }

        private static bool ConfirmUpdate(Form owner, string tag)
        {
            bool yes = false;
            RunOnUi(owner, () =>
            {
                yes = MessageBox.Show(
                    owner,
                    $"Đã có phiên bản mới {tag} (hiện tại v{CurrentVersionString}).\n\n" +
                    "Tool chỉ tải file ToolADB.exe mới và tự khởi động lại, giữ nguyên adb/APK.\n" +
                    "Bạn có muốn cập nhật ngay không?",
                    "Cập nhật ToolADB",
                    MessageBoxButtons.YesNo,
                    MessageBoxIcon.Information) == DialogResult.Yes;
            });
            return yes;
        }

        private static void ShowInfo(Form owner, string msg) =>
            RunOnUi(owner, () => MessageBox.Show(owner, msg, "Cập nhật ToolADB", MessageBoxButtons.OK, MessageBoxIcon.Information));

        private static void ShowError(Form owner, string msg) =>
            RunOnUi(owner, () => MessageBox.Show(owner, msg, "Cập nhật ToolADB", MessageBoxButtons.OK, MessageBoxIcon.Warning));

        private static void RunOnUi(Form owner, Action action)
        {
            if (owner == null || owner.IsDisposed) return;
            if (owner.InvokeRequired) owner.Invoke(action);
            else action();
        }
    }
}
