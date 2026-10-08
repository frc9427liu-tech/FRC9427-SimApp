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

        public string Status = "idle";
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
            cts = new CancellationTokenSource();
            Task.Run(() => Run(cts.Token));
        }

        void OnDestroy() { Stop(); if (I == this) I = null; }
        void OnApplicationQuit() { Stop(); }

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
            try
            {
                var ps = new ProcessStartInfo("powershell",
                    "-NoProfile -Command \"Get-CimInstance Win32_Process -Filter \\\"Name='java.exe'\\\" | Where-Object { $_.CommandLine -match 'frc\\.robot|robotRunMain|simulateJava|halsim' } | ForEach-Object { Stop-Process -Id $_.ProcessId -Force }\"")
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
                logPath = Path.Combine(Path.GetTempPath(), "frc9427-sim-robot.log");
                string init = Path.Combine(Path.GetTempPath(), "frc9427-enable-ws.init.gradle");
                File.WriteAllText(init, InitScript);

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
                    psi.EnvironmentVariables["JAVA_TOOL_OPTIONS"] += " -javaagent:" + agentJar.Replace('\\', '/');
                // 只讓機器人送我們要用的訊息:預設它每個週期把所有 HAL 裝置狀態都丟過來(~11k 則/秒),會擠掉 Unity→機器人的回授
                psi.EnvironmentVariables["HALSIMWS_FILTERS"] = "CANMotor,CANEncoder,CANGyro,Gyro,DriverStation";
                gradle = Process.Start(psi);
                var w = new StreamWriter(logPath, false);
                gradle.OutputDataReceived += (s, e) => { if (e.Data != null) lock (w) { w.WriteLine(e.Data); w.Flush(); } };
                gradle.ErrorDataReceived += (s, e) => { if (e.Data != null) lock (w) { w.WriteLine(e.Data); w.Flush(); } };
                gradle.BeginOutputReadLine();
                gradle.BeginErrorReadLine();

                // 等 HALSim 伺服器(編譯需要約 1 分鐘)
                Status = "compiling/starting robot";
                var uri = new Uri("ws://127.0.0.1:3300/wpilibws");
                for (int i = 0; i < 240 && !ct.IsCancellationRequested; i++)
                {
                    ws?.Dispose();
                    try { ws = await ConnectNoDelay(uri, ct); break; }
                    catch { await Task.Delay(1000, ct); }
                    if (gradle.HasExited) { Status = "robot process exited (see " + logPath + ")"; return; }
                }
                if (!Connected) { Status = "could not connect to HALSim"; return; }
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
                    }
                    if (outbox.Count > MaxOutbox) MaxOutbox = outbox.Count;
                    int guard = 0;
                    while (guard++ < 400 && outbox.TryDequeue(out var o)) await Send(o, ct);
                }
                catch { break; }
                await Task.Delay(20, ct);   // 試過 4ms(回授更快)反而讓閉環更不穩,先維持 20ms
            }
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
