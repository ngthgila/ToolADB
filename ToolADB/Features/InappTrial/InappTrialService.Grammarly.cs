using System.Text.RegularExpressions;

namespace ToolAdb.Features.InappTrial
{
    internal sealed partial class InappTrialService
    {
        // Port nguyên văn PatchGrammarlyNoTrial của Inapp-Patch/PicsartToolGUI.cs:
        // cùng cách vá hex trên file manakin-treatments, cùng chuỗi lệnh device-side,
        // cùng thứ tự và timeout; adb gọi qua CaptureAdb (không kiểm tra exit code)
        // đúng như RunAdb của mã gốc.
        private string PatchGrammarlyNoTrial(string serial)
        {
            string safeSerial = Regex.Replace(serial, @"[^A-Za-z0-9._-]", "_");
            string tempFile = Path.Combine(Path.GetTempPath(), "manakin_" + safeSerial + ".pb");
            try
            {
                if (File.Exists(tempFile)) File.Delete(tempFile);
                CaptureAdb(serial, 8000, "pull",
                    "/data/data/com.grammarly.android.keyboard/files/datastore/manakin-treatments", tempFile);

                if (!File.Exists(tempFile))
                {
                    return "⚠️ Không tìm thấy file manakin-treatments (Hãy mở Grammarly và đăng nhập trước)!";
                }

                byte[] bytes = File.ReadAllBytes(tempFile);
                string hex = BitConverter.ToString(bytes).Replace("-", "");

                // Tìm protobuf field "android_free_trial" (0A 12 616E64726F69645F667265655F747269616C)
                const string targetHeader = "1A660A12616E64726F69645F667265655F747269616C";
                const string replacementHeader = "1A690A12616E64726F69645F667265655F747269616C";
                const string testGroupHex = "2A06746573745F31";       // * \x06 test_1
                const string controlGroupHex = "2A09636F6E74726F6C5F31"; // * \x09 control_1

                int headerIdx = hex.IndexOf(targetHeader);
                if (headerIdx >= 0)
                {
                    int testIdx = hex.IndexOf(testGroupHex, headerIdx);
                    if (testIdx >= 0 && testIdx - headerIdx < 240)
                    {
                        hex = hex.Substring(0, headerIdx) + replacementHeader + hex.Substring(headerIdx + targetHeader.Length);
                        int adjustedTestIdx = hex.IndexOf(testGroupHex, headerIdx);
                        hex = hex.Substring(0, adjustedTestIdx) + controlGroupHex + hex.Substring(adjustedTestIdx + testGroupHex.Length);

                        byte[] newBytes = new byte[hex.Length / 2];
                        for (int i = 0; i < newBytes.Length; i++)
                        {
                            newBytes[i] = Convert.ToByte(hex.Substring(i * 2, 2), 16);
                        }
                        File.WriteAllBytes(tempFile, newBytes);

                        CaptureAdb(serial, 5000, "shell",
                            "am force-stop com.grammarly.android.keyboard; chmod 660 /data/data/com.grammarly.android.keyboard/files/datastore/manakin-treatments 2>/dev/null");
                        CaptureAdb(serial, 8000, "push", tempFile,
                            "/data/data/com.grammarly.android.keyboard/files/datastore/manakin-treatments");
                        CaptureAdb(serial, 8000, "shell",
                            "PUID=$(stat -c '%u' /data/data/com.grammarly.android.keyboard); chown -R $PUID:$PUID /data/data/com.grammarly.android.keyboard/files/datastore; chmod 440 /data/data/com.grammarly.android.keyboard/files/datastore/manakin-treatments; restorecon -R /data/data/com.grammarly.android.keyboard/files/datastore; am start -n com.grammarly.android.keyboard/com.grammarly.widget.ui.settings.MainCompanionActivity");

                        return "🟢 Đã chuyển android_free_trial -> control_1 & khóa file thành công! Nút mua đã chuyển sang Mua Thẳng (Continue with Plan)!";
                    }
                }

                if (hex.Contains(replacementHeader))
                {
                    CaptureAdb(serial, 5000, "shell",
                        "am start -n com.grammarly.android.keyboard/com.grammarly.widget.ui.settings.MainCompanionActivity");
                    return "🟢 Máy này đã ở chế độ Mua Thẳng (control_1) sẵn rồi!";
                }

                return "⚠️ Không tìm thấy cờ android_free_trial trong manakin-treatments!";
            }
            finally
            {
                if (File.Exists(tempFile))
                {
                    try { File.Delete(tempFile); } catch { }
                }
            }
        }
    }
}
