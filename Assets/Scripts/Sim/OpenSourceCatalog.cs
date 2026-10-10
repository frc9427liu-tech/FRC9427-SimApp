using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Threading;

namespace FrcSim
{
    // 開源機器人程式庫:不打包別人的程式(多數 repo 沒標明授權),而是在使用者電腦上 git clone 到 C:\FRC\opensource\<名>,
    // 必要時自動修補(例如把被註解掉的遙控開車指令打開)。狀態欄是我實測的結果。
    public static class OpenSourceCatalog
    {
        public class Entry
        {
            public string Id, Team, Repo, Note, Status;      // Status: ok / partial / untested
            public string PatchFile, PatchFind, PatchReplace;
            public string Dir => Path.Combine(Root, Id);
            public volatile string State = "";                // 下載中 / 失敗訊息
        }
        public const string Root = @"C:\FRC\opensource";
        public static readonly Entry[] All =
        {
            new Entry { Id = "6901-Rebuilt", Team = "FRC 6901", Repo = "https://github.com/frc-6901/2026-Rebuilt.git", Status = "ok",
                Note = "swerve + 吸球/飛輪/送球全可用(實測)",
                PatchFile = @"src\main\java\frc\robot\RobotContainer.java",
                PatchFind = "// drivetrain.setDefaultCommand(drivetrain.applyRequest(() ->\n\t\t// getDriverInput()));" },
            new Entry { Id = "1710-Robot", Team = "FRC 1710", Repo = "https://github.com/frc-Team-1710/2026-Robot.git", Status = "partial",
                Note = "能開車;吸球/射擊走狀態機,尚未對上機構" },
            new Entry { Id = "1405-Robot", Team = "FRC 1405", Repo = "https://github.com/FRC-Team-1405/2026Robot.git", Status = "partial",
                Note = "能開車、飛輪會轉、LB 吸球;送球條件未對上" },
            new Entry { Id = "364-Fusion", Team = "FRC 364", Repo = "https://github.com/TeamFusion364/2026RobotCode.git", Status = "untested",
                Note = "旋轉砲塔自動追 HUB;尚未測試" },
        };

        public static bool Installed(Entry e) { return File.Exists(Path.Combine(e.Dir, "gradlew.bat")); }

        public static void Install(Entry e)
        {
            if (e.State == "…") return;
            e.State = "…";
            new Thread(() =>
            {
                try
                {
                    Directory.CreateDirectory(Root);
                    if (!Installed(e))
                    {
                        var psi = new ProcessStartInfo("git", "clone --depth 1 -q " + e.Repo + " \"" + e.Dir + "\"") { UseShellExecute = false, CreateNoWindow = true };
                        using (var p = Process.Start(psi)) { p.WaitForExit(180000); if (p.ExitCode != 0) { e.State = "git clone 失敗"; return; } }
                    }
                    if (e.PatchFile != null && e.PatchFind != null)
                    {
                        string f = Path.Combine(e.Dir, e.PatchFile);
                        if (File.Exists(f))
                        {
                            string t = File.ReadAllText(f);
                            string t2 = System.Text.RegularExpressions.Regex.Replace(t, @"//\s*(drivetrain\.setDefaultCommand\(drivetrain\.applyRequest\(\(\) ->)\s*//\s*(getDriverInput\(\)\)\);)", "$1 $2");
                            if (t2 != t) File.WriteAllText(f, t2);
                        }
                    }
                    e.State = "";
                }
                catch (Exception ex) { e.State = "失敗:" + ex.Message; }
            }) { IsBackground = true }.Start();
        }
    }
}