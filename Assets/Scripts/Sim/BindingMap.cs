using System.Collections.Generic;
using System.IO;
using System.Text.RegularExpressions;
using UnityEngine;

namespace FrcSim
{
    // 讀專案的 RobotContainer.java,抓出「哪個手把的哪個鍵綁了什麼」:
    //  1. 操作說明面板由它自動產生(不再手寫,程式改了說明就跟著變)
    //  2. 單手把模式需要知道「收球」綁在駕駛 RT 還是 B,才知道怎麼把 B 轉給程式
    public static class BindingMap
    {
        public class Bind { public bool Driver; public string Key; public string Desc; }
        public static readonly List<Bind> List = new List<Bind>();
        public static bool Loaded;
        public static bool IntakeOnDriverRT;

        static string KeyName(string k)
        {
            switch (k)
            {
                case "a": return "A"; case "b": return "B"; case "x": return "X"; case "y": return "Y";
                case "leftBumper": return "LB"; case "rightBumper": return "RB";
                case "leftTrigger": return "LT"; case "rightTrigger": return "RT";
                case "povUp": return "↑"; case "povDown": return "↓"; case "povLeft": return "←"; case "povRight": return "→";
                case "start": return "Start"; case "back": return "Back";
                case "getLeftX": return "左搖桿X"; case "getLeftY": return "左搖桿Y"; case "getRightX": return "右搖桿X"; case "getRightY": return "右搖桿Y";
            }
            return k;
        }

        static string Describe(string cmd, string comment)
        {
            if (!string.IsNullOrEmpty(comment))
            {
                string c = comment.Trim();
                int i = c.IndexOfAny(new[] { ':', '：' });
                if (i >= 0 && i < 14) c = c.Substring(i + 1).Trim();
                int j = c.IndexOf('(');   // 括號內的補充省略,說明列才不會太長
                if (j > 3) c = c.Substring(0, j).Trim();
                return c;
            }
            // 沒寫註解時:把常見的指令名翻成中文,其餘顯示原名
            string[,] zh = { { "feedWhenReady", "到速送球" }, { "spinCommand", "飛輪" }, { "flywheel.spin", "飛輪" }, { "raise", "收手臂" }, { "lower", "放手臂" }, { "eject", "吐球" }, { "intake", "收球" }, { "orbit.run", "送球" }, { "orbit.", "送球" }, { "turret.manual", "砲塔旋轉" }, { "manual", "旋轉" }, { "tankDrive", "左/右輪(坦克)" } };
            var found = new System.Collections.Generic.List<string>();
            for (int z = 0; z < zh.GetLength(0); z++) if (cmd.Contains(zh[z, 0]) && !found.Contains(zh[z, 1])) found.Add(zh[z, 1]);
            if (found.Count > 1) { found.Remove("收球"); found.Remove("旋轉"); found.Remove("送球"); if (found.Count == 0) found.Add("送球"); }
            if (found.Count > 0) return string.Join("+", found.ToArray());
            var m = Regex.Match(cmd, @"m_(\w+)\.(\w+?)(Command)?\(");
            return m.Success ? m.Groups[1].Value + "." + m.Groups[2].Value : cmd.Trim();
        }

        public static void Load(string projectDir)
        {
            List.Clear(); Loaded = false; IntakeOnDriverRT = false;
            try
            {
                string f = Path.Combine(projectDir, "src", "main", "java", "frc", "robot", "RobotContainer.java");
                if (!File.Exists(f)) return;
                var vars = new Dictionary<string, KeyValuePair<bool, string>>();
                foreach (var raw in File.ReadAllLines(f, System.Text.Encoding.UTF8))
                {
                    string line = raw.Trim();
                    if (line.StartsWith("*") || line.StartsWith("/")) continue;
                    string comment = null; int ci = line.IndexOf("//"); if (ci >= 0) { comment = line.Substring(ci + 2); line = line.Substring(0, ci); }
                    // var flywheelOn = m_operator.rightTrigger(...);
                    var mv = Regex.Match(line, @"var\s+(\w+)\s*=\s*m_(driver|operator)\.(\w+)\(");
                    if (mv.Success) { vars[mv.Groups[1].Value] = new KeyValuePair<bool, string>(mv.Groups[2].Value == "driver", mv.Groups[3].Value); continue; }
                    // m_driver.a().whileTrue(cmd);   或   flywheelOn.whileTrue(cmd);
                    var mb = Regex.Match(line, @"m_(driver|operator)\.(\w+)\(([^)]*)\)\s*\.\s*(whileTrue|onTrue|toggleOnTrue|onFalse|whileFalse)\((.*)\)\s*;");
                    if (mb.Success) { List.Add(new Bind { Driver = mb.Groups[1].Value == "driver", Key = KeyName(mb.Groups[2].Value), Desc = Describe(mb.Groups[5].Value, comment) }); continue; }
                    var mw = Regex.Match(line, @"(\w+)\s*\.\s*(whileTrue|onTrue|toggleOnTrue)\((.*)\)\s*;");
                    if (mw.Success && vars.TryGetValue(mw.Groups[1].Value, out var kv)) { List.Add(new Bind { Driver = kv.Key, Key = KeyName(kv.Value), Desc = Describe(mw.Groups[3].Value, comment) }); continue; }
                    // setDefaultCommand(...::getLeftX)
                    var md = Regex.Match(line, @"m_(\w+)\.setDefaultCommand\(m_\w+\.(\w+)\(.*m_(driver|operator)::(get\w+)");
                    if (md.Success) List.Add(new Bind { Driver = md.Groups[3].Value == "driver", Key = KeyName(md.Groups[4].Value), Desc = Describe(md.Groups[1].Value + "." + md.Groups[2].Value, comment) });
                    var mt = Regex.Match(line, @"tankDriveCommand\(\(\) -> m_driver\.getLeftY\(\), \(\) -> m_driver\.getRightY\(\)\)");
                    if (mt.Success) List.Add(new Bind { Driver = true, Key = "左/右搖桿", Desc = "左/右輪(坦克)" });
                }
                foreach (var b in List) if (b.Driver && b.Key == "RT" && b.Desc.ToLower().Contains("intake")) IntakeOnDriverRT = true;
                Loaded = List.Count > 0;
                Debug.Log("[BindingMap] " + List.Count + " bindings, intakeOnDriverRT=" + IntakeOnDriverRT);
            }
            catch (System.Exception e) { Debug.LogWarning("[BindingMap] " + e.Message); }
        }

        public static string Line(bool driver)
        {
            var d = new Dictionary<string, string>(); var order = new List<string>();
            foreach (var b in List)
            {
                if (b.Driver != driver) continue;
                if (d.ContainsKey(b.Key)) d[b.Key] += "+" + b.Desc; else { d[b.Key] = b.Desc; order.Add(b.Key); }
            }
            var sb = new System.Text.StringBuilder();
            foreach (var k in order) { if (sb.Length > 0) sb.Append("   "); sb.Append(k).Append(' ').Append(d[k]); }
            return sb.ToString();
        }
    }
}