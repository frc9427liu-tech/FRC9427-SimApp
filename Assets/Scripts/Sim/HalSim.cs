using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Net.WebSockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;
using UnityEngine;
using Debug = UnityEngine.Debug;

namespace FrcSim
{

    // 測試用:-portoffset N 讓 HALSim(3300)與物理 agent(3399)的連線埠各加 N,這樣自動測試可以和使用者開著的模擬器同時跑
    public static class SimPorts
    {
        public static readonly int Offset = Parse();
        static int Parse()
        {
            var a = Environment.GetCommandLineArgs(); int i = Array.IndexOf(a, "-portoffset");
            return i >= 0 && i + 1 < a.Length && int.TryParse(a[i + 1], out int v) ? v : 0;
        }
    }

    // 把機器人專案準備成模擬器能 build 的「工作副本」:
    //  - GradleRIO 不能在 OneDrive 或含中文的路徑 build → 複製到 C:\FRC9427SimWork\<名稱>
    //  - 這台電腦的 Windows 應用程式控制原則會擋 REVLibDriver.dll(SparkMax 載不起來)→ 用 Sim\overlay 的替身檔覆蓋(只改副本)
    public static class SimProject
    {
        static string SimDir => Path.Combine(Path.GetDirectoryName(Application.dataPath), "Sim");

        // 專案資料夾名 → 模擬設定名(Sim\<名>.mech.json 的 <名> 是資料夾名的開頭即可,例如 LEO-新版 → LEO)
        public static string Profile(string dir)
        {
            string name = Path.GetFileName(dir.TrimEnd('\\', '/'));
            try
            {
                foreach (var f in Directory.GetFiles(SimDir, "*.mech.json"))
                {
                    string b = Path.GetFileName(f); b = b.Substring(0, b.Length - ".mech.json".Length);
                    if (name.StartsWith(b, StringComparison.OrdinalIgnoreCase)) return b;
                }
            }
            catch { }
            return name;
        }

        public static string Prepare(string dir, out string profile)
        {
            profile = Profile(dir);
            string name = Path.GetFileName(dir.TrimEnd('\\', '/'));
            bool hasSpark = File.Exists(Path.Combine(dir, "src", "main", "java", "frc", "robot", "subsystems", "drive", "DriveIOSparkMax.java"));
            bool needCopy = dir.IndexOf("OneDrive", StringComparison.OrdinalIgnoreCase) >= 0 || System.Linq.Enumerable.Any(name, c => c > 127) || hasSpark;
            if (!needCopy) return dir;
            string dst = @"C:\FRC9427SimWork\" + System.Text.RegularExpressions.Regex.Replace(profile, "[^A-Za-z0-9_]", "_") + (SimPorts.Offset != 0 ? "_p" + SimPorts.Offset : "");   // 測試用 -portoffset 時用獨立副本,不干擾使用者開著的模擬器
            try
            {
                var psi = new ProcessStartInfo("robocopy", "\"" + dir.TrimEnd('\\') + "\" \"" + dst + "\" /MIR /XD .gradle build .git bin /NFL /NDL /NJH /NJS /NP")
                { UseShellExecute = false, CreateNoWindow = true };
                using (var p = Process.Start(psi)) p.WaitForExit(120000);
                string ov = Path.Combine(SimDir, "overlay");
                if (Directory.Exists(ov))
                    foreach (var o in Directory.GetFiles(ov, "*.java"))
                        foreach (var target in Directory.GetFiles(Path.Combine(dst, "src"), Path.GetFileName(o), SearchOption.AllDirectories))
                            File.Copy(o, target, true);
            }
            catch (Exception e) { UnityEngine.Debug.LogError("SimProject.Prepare: " + e.Message); return dir; }
            return dst;
        }
    }
    // 連到使用者「真實、沒改過」的機器人 Java 程式(WPILib simulateJava + HALSim WebSocket),
    // 把手把輸入/Enable 送進去,讀回馬達輸出。不在機器人專案裡加任何模擬專用程式。
    public class HalSim : MonoBehaviour
    {
        public static HalSim I;

        public string ProjectDir;                       // 機器人 Java 專案資料夾(內有 gradlew.bat)
        public bool Enabled = true;                     // 送 DS Enable
        public bool Autonomous;
        public float[] Axes = new float[6];             // 0 LX,1 LY,2 LT,3 RT,4 RX,5 RY
        public bool[] Buttons = new bool[12];
        // 第二支手把(操作手,device 1):LEO 這類兩支手把的程式用
        public float[] Axes2 = new float[6];
        public bool[] Buttons2 = new bool[12];
        public int Pov2 = -1;

        public string Status = "idle";
        // ---- 載入進度(給 HUD 顯示 %):依 gradle 輸出的階段 + 上次載入花的時間估算
        public volatile float StageFloor; public DateTime StartUtc = DateTime.UtcNow; public float EstSecs = 45f; public volatile float LoadedSecs;
        public bool Failed => Status.StartsWith("robot process exited") || Status.StartsWith("could not connect") || Status.StartsWith("error") || Status.StartsWith("gradlew");
        public float LoadProgress { get { if (Connected) return 1f; float t = (float)(DateTime.UtcNow - StartUtc).TotalSeconds; return Mathf.Max(Mathf.Min(0.95f, t / Mathf.Max(10f, EstSecs)), StageFloor); } }
        void ParseStage(string l) { float f = 0f; if (l.Contains("Daemon")) f = 0.08f; if (l.Contains("compileJava")) f = 0.3f; if (l.Contains("classes") || l.Contains("processResources")) f = 0.5f; if (l.Contains("simulateExternalJava") || l.Contains("simulateJava") || l.Contains("robotRunMain")) f = 0.75f; if (l.Contains("Robot program starting") || l.Contains("HALSim")) f = 0.9f; if (f > StageFloor) StageFloor = f; }
        public int MessagesIn;
        public readonly Dictionary<string, JObject> Devices = new Dictionary<string, JObject>();

        const string InitScript =
@"allprojects {
    afterEvaluate { p ->
        def wpi = p.extensions.findByName('wpi')
        if (wpi != null) {
            def exts = wpi.sim.halExtensions
            def ws = exts.findByName('Sim WebSockets Server')
            if (ws == null) { try { ws = wpi.sim.addWebsocketsServer() } catch (Exception e) { println ""ws server: ${e.message}"" } }
            if (ws != null) ws.defaultEnabled.set(true)
            def gui = exts.findByName('Sim GUI')
            if (gui != null) gui.defaultEnabled.set(false)
        }
    }
}
";

        Process gradle;
        WebSocket ws;

        // 自己做 WebSocket 握手,好把 TCP_NODELAY 打開:預設的 ClientWebSocket 有 Nagle,小訊息會被攢著等 ACK,
        // 造成 Unity→機器人的回授延遲 1 秒以上(閉環失控的主因)
        static async Task<WebSocket> ConnectNoDelay(Uri uri, CancellationToken ct)
        {
            var tcp = new System.Net.Sockets.TcpClient { NoDelay = true };
            try
            {
                await tcp.ConnectAsync(uri.Host, uri.Port);
                var stream = tcp.GetStream();
                string key = Convert.ToBase64String(Guid.NewGuid().ToByteArray());
                string req = "GET " + uri.PathAndQuery + " HTTP/1.1\r\nHost: " + uri.Host + ":" + uri.Port +
                             "\r\nUpgrade: websocket\r\nConnection: Upgrade\r\nSec-WebSocket-Key: " + key +
                             "\r\nSec-WebSocket-Version: 13\r\n\r\n";
                var rb = Encoding.ASCII.GetBytes(req);
                await stream.WriteAsync(rb, 0, rb.Length, ct);
                var sbh = new StringBuilder(); var one = new byte[1];
                while (!sbh.ToString().EndsWith("\r\n\r\n"))
                {
                    int n = await stream.ReadAsync(one, 0, 1, ct);
                    if (n <= 0) throw new IOException("closed during handshake");
                    sbh.Append((char)one[0]);
                    if (sbh.Length > 8192) throw new IOException("handshake too long");
                }
                if (!sbh.ToString().StartsWith("HTTP/1.1 101")) throw new IOException("handshake failed: " + sbh.ToString().Split('\r')[0]);
                return WebSocket.CreateFromStream(stream, false, null, TimeSpan.FromSeconds(30));
            }
            catch { tcp.Dispose(); throw; }
        }
        CancellationTokenSource cts;
        readonly ConcurrentQueue<string> inbox = new ConcurrentQueue<string>();
        readonly ConcurrentQueue<string> outbox = new ConcurrentQueue<string>();
        string logPath;

        public bool Connected => ws != null && ws.State == WebSocketState.Open;

        void Awake() { I = this; }

        public void StartRobot(string projectDir)
        {
            ProjectDir = projectDir;
            StartUtc = DateTime.UtcNow; EstSecs = PlayerPrefs.GetFloat("robotLoadSecs", 45f);
            cts = new CancellationTokenSource();
            Task.Run(() => Run(cts.Token));
        }

        void OnDestroy() { Stop(); if (I == this) I = null; }
        void OnApplicationQuit() { Stop(); }

        bool launched;   // 只有「我自己啟動過機器人程式」才會在結束時去殺埠上的 JVM(沒啟動過就什麼都不殺,避免誤殺別的模擬器)
        public void Stop()
        {
            try { cts?.Cancel(); } catch { }
            try { ws?.Abort(); } catch { }
            try
            {
                if (gradle != null && !gradle.HasExited)
                {
                    // 連同子程序(gradle daemon 啟動的 java)一起結束
                    var k = Process.Start(new ProcessStartInfo("taskkill", "/PID " + gradle.Id + " /T /F") { CreateNoWindow = true, UseShellExecute = false });
                    k?.WaitForExit(3000);
                }
            }
            catch { }
            // 機器人 JVM 是 Gradle daemon 啟動的,不在上面的程序樹裡:依命令列找出機器人程式的 java 一併結束
            if (!launched) return;
            try
            {
                // 只結束「自己這個模擬器」的機器人 JVM:用它佔住的 HALSim 埠(3300 + 埠偏移)找擁有者。以前是依命令列比對所有 java,
                // 會把使用者另一個正在跑的模擬器的機器人程式一起殺掉(自動測試收尾時發生過:使用者畫面跳「WebSocket 被遠端關閉」)
                int myPort = 3300 + SimPorts.Offset;
                string cmd = "Get-NetTCPConnection -LocalPort " + myPort + " -State Listen -ErrorAction SilentlyContinue | ForEach-Object { Stop-Process -Id $_.OwningProcess -Force -ErrorAction SilentlyContinue }";
                var ps = new ProcessStartInfo("powershell", "-NoProfile -EncodedCommand " + System.Convert.ToBase64String(System.Text.Encoding.Unicode.GetBytes(cmd)))
                { CreateNoWindow = true, UseShellExecute = false };
                Process.Start(ps);
            }
            catch { }
        }

        async Task Run(CancellationToken ct)
        {
            try
            {
                Status = "starting robot code";
                logPath = Path.Combine(Path.GetTempPath(), SimPorts.Offset != 0 ? "frc9427-sim-robot-p" + SimPorts.Offset + ".log" : "frc9427-sim-robot.log");
                string init = Path.Combine(Path.GetTempPath(), "frc9427-enable-ws.init.gradle");
                File.WriteAllText(init, InitScript);

                string workDir = SimProject.Prepare(ProjectDir, out string profile);
                ProjectDir = workDir;
                string gradlew = Path.Combine(ProjectDir, "gradlew.bat");
                if (!File.Exists(gradlew)) { Status = "gradlew.bat not found in project"; return; }

                var psi = new ProcessStartInfo(gradlew, "simulateJava --offline -I \"" + init + "\"")
                {
                    WorkingDirectory = ProjectDir,
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true
                };
                psi.EnvironmentVariables["JAVA_HOME"] = @"C:\Users\Public\wpilib\2026\jdk";
                psi.EnvironmentVariables["JAVA_TOOL_OPTIONS"] = "-Dfile.encoding=UTF-8 -Xlog:gc,safepoint:file=" + Path.Combine(Path.GetTempPath(), "frc9427-sim-gc.log").Replace('\\', '/') + ":uptime";
                // 同進程物理 agent(Sim\simagent.jar):用 Phoenix SimState 在機器人 JVM 裡驅動轉向馬達,避開網路延遲。Sim\noagent.txt 存在則停用
                string agentJar = Path.Combine(Path.GetDirectoryName(Application.dataPath), "Sim", "simagent.jar");
                if (File.Exists(agentJar) && !File.Exists(Path.Combine(Path.GetDirectoryName(agentJar), "noagent.txt")))
                {
                    psi.EnvironmentVariables["JAVA_TOOL_OPTIONS"] += " -javaagent:" + agentJar.Replace('\\', '/');
                    string motorsTxt = Path.Combine(Path.GetDirectoryName(agentJar), profile + ".agent-motors.txt");
                    if (!File.Exists(motorsTxt)) motorsTxt = Path.Combine(Path.GetDirectoryName(agentJar), "agent-motors.txt");
                    if (File.Exists(motorsTxt)) psi.EnvironmentVariables["JAVA_TOOL_OPTIONS"] += " -Dsimagent.motors=" + motorsTxt.Replace('\\', '/');
                }
                // 只讓機器人送我們要用的訊息:預設它每個週期把所有 HAL 裝置狀態都丟過來(~11k 則/秒),會擠掉 Unity→機器人的回授
                psi.EnvironmentVariables["HALSIMWS_PORT"] = (3300 + SimPorts.Offset).ToString();
                if (SimPorts.Offset != 0) psi.EnvironmentVariables["JAVA_TOOL_OPTIONS"] += " -Dsimagent.port=" + (3399 + SimPorts.Offset);
                psi.EnvironmentVariables["HALSIMWS_FILTERS"] = "CANMotor,CANEncoder,CANGyro,Gyro,DriverStation";
                // 埠已被另一個模擬器佔用:不要再啟動第二份(否則結束時會把對方的機器人程式一起殺掉,對方畫面會跳「WebSocket 被遠端關閉」)
                { bool inUse = false; try { var pl = new System.Net.Sockets.TcpListener(System.Net.IPAddress.Loopback, 3300 + SimPorts.Offset); pl.Start(); pl.Stop(); } catch { inUse = true; } if (inUse) { Status = "error: another simulator is already running (port " + (3300 + SimPorts.Offset) + " in use). Close it and retry."; return; } }
                gradle = Process.Start(psi); launched = true;
                var w = new StreamWriter(logPath, false);
                gradle.OutputDataReceived += (s, e) => { if (e.Data != null) { lock (w) { w.WriteLine(e.Data); w.Flush(); } ParseStage(e.Data); } };
                gradle.ErrorDataReceived += (s, e) => { if (e.Data != null) lock (w) { w.WriteLine(e.Data); w.Flush(); } };
                gradle.BeginOutputReadLine();
                gradle.BeginErrorReadLine();

                // 等 HALSim 伺服器(編譯需要約 1 分鐘)
                Status = "compiling/starting robot";
                var uri = new Uri("ws://127.0.0.1:" + (3300 + SimPorts.Offset) + "/wpilibws");
                for (int i = 0; i < 240 && !ct.IsCancellationRequested; i++)
                {
                    ws?.Dispose();
                    try { ws = await ConnectNoDelay(uri, ct); break; }
                    catch { await Task.Delay(1000, ct); }
                    if (gradle.HasExited) { Status = "robot process exited (see " + logPath + ")"; return; }
                }
                if (!Connected) { Status = "could not connect to HALSim"; return; }
                LoadedSecs = (float)(DateTime.UtcNow - StartUtc).TotalSeconds;
                Status = "connected";

                _ = Task.Run(() => SendLoop(ct));
                var buf = new byte[1 << 16];
                var sb = new StringBuilder();
                // 接收迴圈一定要跑在執行緒池:放在 Unity 主執行緒的同步環境裡,每個 await 的後續都要等主執行緒的下一幀,
                // 訊息(~5k/s)一多就堆在 TCP 緩衝區,連帶讓機器人端的送出被擋住 → 回授延遲好幾秒
                await Task.Run(async () =>
                {
                    while (Connected && !ct.IsCancellationRequested)
                    {
                        var r = await ws.ReceiveAsync(new ArraySegment<byte>(buf), ct).ConfigureAwait(false);
                        if (r.MessageType == WebSocketMessageType.Close) break;
                        sb.Append(Encoding.UTF8.GetString(buf, 0, r.Count));
                        if (r.EndOfMessage) { inbox.Enqueue(sb.ToString()); sb.Length = 0; }
                    }
                }).ConfigureAwait(false);
                Status = "disconnected";
            }
            catch (OperationCanceledException) { }
            catch (Exception e) { Status = "error: " + e.Message; Debug.LogWarning("HalSim: " + e); }
        }

        struct Sent { public double v1, v2; public int skips; }
        readonly Dictionary<string, Sent> lastSent = new Dictionary<string, Sent>();
        public int Backlog, MaxBacklog, MaxOutbox;
        public float MaxDt; public string Sample;
        public readonly Dictionary<string, int> TypeCounts = new Dictionary<string, int>();
        readonly List<string> drain = new List<string>();
        readonly Dictionary<string, int> keyCount = new Dictionary<string, int>();
        static string QuickKey(string s)
        {
            int e = s.IndexOf("\"data\"", StringComparison.Ordinal);
            return e > 0 ? s.Substring(0, e) : s;
        }

        int sendTick;
        async Task SendLoop(CancellationToken ct)
        {
            while (Connected && !ct.IsCancellationRequested)
            {
                try
                {
                    // 馬達/感測器回授越快送越好(降低閉環延遲);DS 與搖桿每 20ms 送一次就夠
                    if (sendTick++ % 1 == 0)
                    {
                        string ds = "{\"type\":\"DriverStation\",\"device\":\"\",\"data\":{\">enabled\":" + (Enabled ? "true" : "false")
                            + ",\">autonomous\":" + (Autonomous ? "true" : "false") + ",\">new_data\":true}}";
                        await Send(ds, ct);
                        await Send(JoystickJson(), ct);
                        await Send(JoystickJson2(), ct);
                    }
                    if (outbox.Count > MaxOutbox) MaxOutbox = outbox.Count;
                    int guard = 0;
                    while (guard++ < 400 && outbox.TryDequeue(out var o)) await Send(o, ct);
                }
                catch { break; }
                await Task.Delay(20, ct);   // 試過 4ms(回授更快)反而讓閉環更不穩,先維持 20ms
            }
        }

        string JoystickJson2()
        {
            var sb = new StringBuilder();
            sb.Append("{\"type\":\"Joystick\",\"device\":\"1\",\"data\":{\">axes\":[");
            for (int i = 0; i < Axes2.Length; i++) { if (i > 0) sb.Append(','); sb.Append(Axes2[i].ToString("0.####", System.Globalization.CultureInfo.InvariantCulture)); }
            sb.Append("],\">povs\":[" + Pov2 + "],\">buttons\":[");
            for (int i = 0; i < Buttons2.Length; i++) { if (i > 0) sb.Append(','); sb.Append(Buttons2[i] ? "true" : "false"); }
            sb.Append("]}}");
            return sb.ToString();
        }

        string JoystickJson()
        {
            var sb = new StringBuilder();
            sb.Append("{\"type\":\"Joystick\",\"device\":\"0\",\"data\":{\">axes\":[");
            for (int i = 0; i < Axes.Length; i++) { if (i > 0) sb.Append(','); sb.Append(Axes[i].ToString("0.####", System.Globalization.CultureInfo.InvariantCulture)); }
            sb.Append("],\">povs\":[-1],\">buttons\":[");
            for (int i = 0; i < Buttons.Length; i++) { if (i > 0) sb.Append(','); sb.Append(Buttons[i] ? "true" : "false"); }
            sb.Append("]}}");
            return sb.ToString();
        }

        async Task Send(string s, CancellationToken ct)
        {
            var b = Encoding.UTF8.GetBytes(s);
            await ws.SendAsync(new ArraySegment<byte>(b), WebSocketMessageType.Text, true, ct);
        }

        void Update()
        {
            // 主執行緒解析收到的訊息(最多一次處理 400 筆)
            // 幀率低時訊息會堆積 → 機器人看到的回授延遲變成好幾秒(閉環失控)。
            // 所以一次清空整個佇列,每個裝置只解析最後幾筆(舊的被新的蓋掉,不影響結果)。
            if (Time.unscaledDeltaTime > MaxDt) MaxDt = Time.unscaledDeltaTime;
            Backlog = inbox.Count; if (Backlog > MaxBacklog) MaxBacklog = Backlog;
            drain.Clear();
            while (inbox.TryDequeue(out var raw))
            {
                MessagesIn++; drain.Add(raw);
                int ti = raw.IndexOf("\"type\"", StringComparison.Ordinal);
                if (ti >= 0)
                {
                    int q1 = raw.IndexOf('"', ti + 6), q2 = q1 < 0 ? -1 : raw.IndexOf('"', q1 + 1);
                    if (q2 > q1) { string ty = raw.Substring(q1 + 1, q2 - q1 - 1); TypeCounts.TryGetValue(ty, out int tc); TypeCounts[ty] = tc + 1; }
                }
                if (Sample == null) Sample = raw.Length > 200 ? raw.Substring(0, 200) : raw;
            }
            const int keep = 3;
            keyCount.Clear();
            for (int i = drain.Count - 1; i >= 0; i--)
            {
                string k = QuickKey(drain[i]);
                keyCount.TryGetValue(k, out int c); keyCount[k] = c + 1;
                if (c >= keep) drain[i] = null;
            }
            for (int n = 0; n < drain.Count; n++)
            {
                var s = drain[n]; if (s == null) continue;
                try
                {
                    var o = JObject.Parse(s);
                    string key = (string)o["type"] + "/" + (string)o["device"];
                    var data = o["data"] as JObject;
                    if (data == null) continue;
                    if (!Devices.TryGetValue(key, out var d)) { d = new JObject(); Devices[key] = d; }
                    d.Merge(data, new JsonMergeSettings { MergeArrayHandling = MergeArrayHandling.Replace });
                }
                catch { }
            }
        }

        // 把一組輸入值送給機器人程式的虛擬裝置:type=CANEncoder 等,device 名,再接 key/value 成對(>開頭=給程式的輸入)
        public void QueueDevice(string type, string device, string k1, double v1, string k2 = null, double v2 = 0)
        {
            // 數值沒變就少送(每 10 次補送一次保持新鮮):機器人端 HALSim 伺服器是單執行緒,訊息太多會讓搖桿/回授延遲好幾百毫秒
            string dk = type + "|" + device;
            if (lastSent.TryGetValue(dk, out var ls) && ls.v1 == v1 && ls.v2 == v2 && ++ls.skips < 1) { lastSent[dk] = ls; return; }   // (去重已停用:skips<1 永不成立)
            lastSent[dk] = new Sent { v1 = v1, v2 = v2, skips = 0 };
            var d = new JObject { [k1] = v1 };
            if (k2 != null) d[k2] = v2;
            outbox.Enqueue(new JObject { ["type"] = type, ["device"] = device, ["data"] = d }.ToString(Newtonsoft.Json.Formatting.None));
        }

        public float Num(string key, string prop, float def = 0f)
        {
            if (Devices.TryGetValue(key, out var d) && d[prop] != null) return (float)d[prop];
            return def;
        }

        public string Summary()
        {
            var sb = new StringBuilder();
            sb.AppendLine("status=" + Status + " messages=" + MessagesIn + " devices=" + Devices.Count);
            var keys = new List<string>(Devices.Keys); keys.Sort();
            foreach (var k in keys)
            {
                var d = Devices[k];
                string s = d.ToString(Newtonsoft.Json.Formatting.None);
                sb.AppendLine(k + "  " + (s.Length > 180 ? s.Substring(0, 180) + "..." : s));
            }
            return sb.ToString();
        }
    }
}
