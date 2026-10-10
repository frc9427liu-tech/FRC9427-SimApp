using System;
using System.Diagnostics;
using System.IO;
using System.Threading;

namespace FrcSim
{
    // 開源機器人 3D 模型庫:模型來自 AdvantageScope 的公開資產(Littleton Robotics / FRC 6328,BSD-3 授權,散佈需保留版權聲明),
    // 不打包進模擬器,而是在使用者按下時從 GitHub 下載到 C:\FRC\models。
    public static class ModelLibrary
    {
        public class M
        {
            public string Id, ZhName, EnName, Tag, Zip, Credit; public float Pitch, YawAdd;   // 依 AdvantageScope config.json 的旋轉換算到 Unity:Z-up 模型要 Pitch -90;車頭方向不同的再加 YawAdd
            public volatile string State = "";   // "…" 下載中 / 錯誤訊息
            public string File => Id + ".glb";
            public string Path => System.IO.Path.Combine(LibDir, File);
        }
        public const string LibDir = @"C:\FRC\models";
        const string Base = "https://github.com/Mechanical-Advantage/AdvantageScopeAssets/releases/download/";
        public static readonly M[] All =
        {
            new M { Id = "as_kitbot2026", ZhName = "2026 KitBot(FIRST 官方)", EnName = "2026 KitBot (FIRST official)", Tag = "default-assets-v2", Zip = "Robot_2026FRCKitBotV2.zip", Credit = "FIRST / AdvantageScope assets" },
            new M { Id = "as_crabbot", ZhName = "CrabBot", EnName = "CrabBot", Tag = "default-assets-v2", Zip = "Robot_CrabBotV4.zip", Pitch = -90f, YawAdd = 0f, Credit = "FRC 6328 / AdvantageScope assets" },
            new M { Id = "as_duckbot", ZhName = "DuckBot", EnName = "DuckBot", Tag = "default-assets-v2", Zip = "Robot_DuckBotV4.zip", Pitch = -90f, YawAdd = 0f, Credit = "FRC 6328 / AdvantageScope assets" },
            new M { Id = "as_frogbot", ZhName = "FrogBot", EnName = "FrogBot", Tag = "default-assets-v2", Zip = "Robot_FrogBotV2.zip", Credit = "FRC 6328 / AdvantageScope assets" },
            new M { Id = "as_bananasplit", ZhName = "BananaSplit(6328)", EnName = "BananaSplit (6328)", Tag = "frc-6328-assets-v2", Zip = "Robot_BananaSplitV4.zip", YawAdd = 180f, Credit = "FRC 6328 / AdvantageScope assets" },
            new M { Id = "as_manta", ZhName = "Manta(6328)", EnName = "Manta (6328)", Tag = "frc-6328-assets-v2", Zip = "Robot_MantaV1.zip", YawAdd = 180f, Credit = "FRC 6328 / AdvantageScope assets" },
            new M { Id = "as_presto", ZhName = "Presto(6328)", EnName = "Presto (6328)", Tag = "frc-6328-assets-v2", Zip = "Robot_PrestoV3.zip", Credit = "FRC 6328 / AdvantageScope assets" },
        };

        public static float Yaw(string file) { var x = Find(file); return x == null ? 0f : 90f + x.YawAdd; }
        public static M Find(string file) { foreach (var m in All) if (m.File == file || m.Id == file) return m; return null; }
        public static bool Is(string file) { return Find(file) != null; }
        public static bool Installed(M m) { return System.IO.File.Exists(m.Path); }
        public static string Name(M m) { return Loc.Lang == "en" ? m.EnName : m.ZhName; }

        public static void Install(M m, Action done = null)
        {
            if (m.State == "…" || Installed(m)) { done?.Invoke(); return; }
            m.State = "…";
            new Thread(() =>
            {
                try
                {
                    Directory.CreateDirectory(LibDir);
                    string tmp = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "frcsim_" + m.Id);
                    string script = "$ErrorActionPreference='Stop'; [Net.ServicePointManager]::SecurityProtocol=[Net.SecurityProtocolType]::Tls12; " +
                        "if(Test-Path '" + tmp + "'){Remove-Item '" + tmp + "' -Recurse -Force}; New-Item -ItemType Directory -Force '" + tmp + "' | Out-Null; " +
                        "Invoke-WebRequest '" + Base + m.Tag + "/" + m.Zip + "' -OutFile '" + tmp + "\\m.zip' -UseBasicParsing; " +
                        "Expand-Archive '" + tmp + "\\m.zip' '" + tmp + "\\x' -Force; Copy-Item '" + tmp + "\\x\\model.glb' '" + m.Path + "' -Force; Remove-Item '" + tmp + "' -Recurse -Force";
                    var psi = new ProcessStartInfo("powershell", "-NoProfile -EncodedCommand " + Convert.ToBase64String(System.Text.Encoding.Unicode.GetBytes(script))) { UseShellExecute = false, CreateNoWindow = true };
                    using (var p = Process.Start(psi)) { if (!p.WaitForExit(300000)) { try { p.Kill(); } catch { } m.State = "下載逾時"; return; } if (p.ExitCode != 0) { m.State = "下載失敗(要連網)"; return; } }
                    m.State = Installed(m) ? "" : "下載失敗";
                }
                catch (Exception ex) { m.State = "失敗:" + ex.Message; }
                if (m.State == "") done?.Invoke();
            }) { IsBackground = true }.Start();
        }
    }
}