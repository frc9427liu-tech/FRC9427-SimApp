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
            public string Id, Team, Repo, Note, Status, Model;      // Status: ok / partial / untested
            public string PatchFile, PatchFind, PatchReplace;
            public string Dir => Path.Combine(Root, Id);
            public volatile string State = "";                // 下載中 / 失敗訊息
        }
        public const string Root = @"C:\FRC\opensource";
        public static readonly Entry[] All =
        {
            new Entry { Id = "6901-Rebuilt", Model = "proc:0.72,0.74,0.55,#1E8CFF", Team = "FRC 6901", Repo = "https://github.com/frc-6901/2026-Rebuilt.git", Status = "ok",
                Note = "swerve + 吸球/飛輪/送球全可用(實測)",
                PatchFile = @"src\main\java\frc\robot\RobotContainer.java",
                PatchFind = @"//\s*(drivetrain\.setDefaultCommand\(drivetrain\.applyRequest\(\(\) ->)\s*//\s*(getDriverInput\(\)\)\);)", PatchReplace = "$1 $2" },
            new Entry { Id = "1710-Robot", Model = "proc:0.62,0.86,0.60,#F2B600", Team = "FRC 1710", Repo = "https://github.com/frc-Team-1710/2026-Robot.git", Status = "partial",
                Note = "能開車;吸球/射擊走狀態機,尚未對上機構" },
            new Entry { Id = "1405-Robot", Model = "proc:0.76,0.76,0.58,#FF6A00", Team = "FRC 1405", Repo = "https://github.com/FRC-Team-1405/2026Robot.git", Status = "partial",
                Note = "能開車、飛輪會轉、LB 吸球;送球條件未對上" },
            new Entry { Id = "364-Fusion", Model = "proc:0.64,0.64,0.62,#8A3BFF", Team = "FRC 364", Repo = "https://github.com/TeamFusion364/2026RobotCode.git", Status = "ok",
                Note = "swerve + 吸球/飛輪/送球可用(實測);自動把 simMode 改成 REAL",
                PatchFile = @"src\main\java\frc\robot\Constants.java",
                PatchFind = @"Mode simMode = Mode\.SIM;", PatchReplace = "Mode simMode = Mode.REAL;" },
            new Entry { Id = "REV-ION-StarterBot", Team = "REV ION", Repo = "https://github.com/REVrobotics/2026-REV-ION-FRC-StarterBot.git", Status = "untested",
                Model = "proc:0.70,0.70,0.50,#FF8A00",
                Note = "官方入門機(BSD-3);用 SparkMax/Flex,模擬器目前只模擬 TalonFX,暫時無法驅動" },
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
                            string t2 = System.Text.RegularExpressions.Regex.Replace(t, e.PatchFind, e.PatchReplace);
                            if (t2 != t) File.WriteAllText(f, t2);
                        }
                    }
                    // 第一次要連網把模擬用的函式庫(Phoenix sim 等)抓進 Gradle 快取;模擬器之後是離線啟動的
                    e.State = "…";
                    string jdk = @"C:\Users\Public\wpilib\2026\jdk";
                    var gp = new ProcessStartInfo(Path.Combine(e.Dir, "gradlew.bat"), "simulateJava --console=plain")
                    { WorkingDirectory = e.Dir, UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true };
                    gp.EnvironmentVariables["JAVA_HOME"] = jdk; gp.EnvironmentVariables["JAVA_TOOL_OPTIONS"] = "-Dfile.encoding=UTF-8";
                    using (var g = Process.Start(gp))
                    {
                        bool up = false; var sw = Stopwatch.StartNew();
                        g.OutputDataReceived += (s, a) => { if (a.Data != null && a.Data.Contains("Robot program startup complete")) up = true; };
                        g.ErrorDataReceived += (s, a) => { };
                        g.BeginOutputReadLine(); g.BeginErrorReadLine();
                        while (!up && !g.HasExited && sw.Elapsed.TotalSeconds < 420) Thread.Sleep(500);
                        try { Process.Start(new ProcessStartInfo("taskkill", "/PID " + g.Id + " /T /F") { UseShellExecute = false, CreateNoWindow = true })?.WaitForExit(10000); } catch { }
                        if (!up) { e.State = "下載函式庫失敗(要連網,且已安裝 WPILib 2026)"; return; }
                    }
                    e.State = "";
                }
                catch (Exception ex) { e.State = "失敗:" + ex.Message; }
            }) { IsBackground = true }.Start();
        }
    }
    public class InstallTest : UnityEngine.MonoBehaviour
    {
        System.Collections.IEnumerator Start()
        {
            var a = Environment.GetCommandLineArgs(); int i = Array.IndexOf(a, "-installtest"); string id = a[i + 1];
            foreach (var en in OpenSourceCatalog.All) if (en.Id == id)
            {
                OpenSourceCatalog.Install(en);
                float t = 0f; while ((en.State == "…" || t < 2f) && t < 600f) { t += 1f; yield return new UnityEngine.WaitForSecondsRealtime(1f); }
                File.WriteAllText(Path.Combine(Path.GetDirectoryName(UnityEngine.Application.dataPath), "installtest.txt"), "state='" + en.State + "' installed=" + OpenSourceCatalog.Installed(en) + " secs=" + t);
            }
            UnityEngine.Application.Quit();
        }
    }
}