using System;
using System.Collections.Generic;
using System.IO;
using System.Net.WebSockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;
using UnityEngine;

namespace FrcSim
{
    // 模擬 Limelight:連到機器人程式的 NetworkTables 4 伺服器(ws://127.0.0.1:5810),
    // 以 20 Hz 發布 /<名稱>/botpose_orb_wpiblue 等,位姿取自 Unity 裡機器人的真實位置(含小雜訊)。
    // 程式經 LimelightHelpers 讀取後餵給位姿估計器,就和實車一樣自己定位;機器人專案完全不動。
    // 規格來源:舊網頁橋接 vision.mjs / server.mjs(NT4 客戶端)。
    public class NtSim : MonoBehaviour
    {
        public SwerveDrive Drive;
        public string[] Limelights = { "limelight-up" };
        public bool Connected;
        public int Published;
        public string Status = "idle";
        public int TagsSeen;
        // 程式自己記錄的位姿(AdvantageKit RealOutputs 內 Pose2d struct),除錯用:topic -> [x,y,rotRad]
        public readonly System.Collections.Concurrent.ConcurrentDictionary<string, double[]> CodePoses = new System.Collections.Concurrent.ConcurrentDictionary<string, double[]>();
        public volatile double[] CodeAxes;   // AdvantageKit 記錄的 DriverStation Joystick0 軸值(機器人「看到」的搖桿)
        public readonly System.Collections.Concurrent.ConcurrentDictionary<string, double> CodeScalars = new System.Collections.Concurrent.ConcurrentDictionary<string, double>();
        readonly System.Collections.Concurrent.ConcurrentDictionary<long, KeyValuePair<string, string>> topicById = new System.Collections.Concurrent.ConcurrentDictionary<long, KeyValuePair<string, string>>();

        // 2026 官方 AprilTag(英吋):id, x, y, z, rotZ(0 面向紅方牆 +X)
        static readonly double[][] Tags =
        {
            new double[]{1,467.08,291.79,35.0,180}, new double[]{2,468.56,182.08,44.25,90}, new double[]{3,444.80,172.32,44.25,180},
            new double[]{4,444.80,158.32,44.25,180}, new double[]{5,468.56,134.56,44.25,270}, new double[]{6,467.08,24.85,35.0,180},
            new double[]{7,470.03,24.85,35.0,0}, new double[]{8,482.56,134.56,44.25,270}, new double[]{9,492.33,144.32,44.25,0},
            new double[]{10,492.33,158.32,44.25,0}, new double[]{11,482.56,182.08,44.25,90}, new double[]{12,470.03,291.79,35.0,0},
            new double[]{13,649.58,291.02,21.75,180}, new double[]{14,649.58,274.02,21.75,180}, new double[]{15,649.57,169.78,21.75,180},
            new double[]{16,649.57,152.78,21.75,180}, new double[]{17,183.03,24.85,35.0,0}, new double[]{18,181.56,134.56,44.25,270},
            new double[]{19,205.32,144.32,44.25,0}, new double[]{20,205.32,158.32,44.25,0}, new double[]{21,181.56,182.08,44.25,90},
            new double[]{22,183.03,291.79,35.0,0}, new double[]{23,180.08,291.79,35.0,180}, new double[]{24,167.56,182.08,44.25,90},
            new double[]{25,157.79,172.32,44.25,180}, new double[]{26,157.79,158.32,44.25,180}, new double[]{27,167.56,134.56,44.25,270},
            new double[]{28,180.08,24.85,35.0,180}, new double[]{29,0.54,25.62,21.75,0}, new double[]{30,0.54,42.62,21.75,0},
            new double[]{31,0.54,147.62,21.75,0}, new double[]{32,0.54,164.62,21.75,0},
        };

        // 相機安裝(機器人座標)與光學參數(約值,待實車量測)
        const double CamX = 0.2, CamY = 0.0, CamZ = 0.5, HFov = 62.5, VFov = 48.9, MaxDist = 6.0;

        ClientWebSocket ws;
        CancellationTokenSource cts;
        volatile float px, py, pth;            // 主執行緒更新的位姿快照(弧度)
        double clockOffsetUs;                  // 伺服器時間 - 本地時間
        volatile bool synced;
        readonly System.Diagnostics.Stopwatch clock = System.Diagnostics.Stopwatch.StartNew();
        readonly Dictionary<string, int> pubs = new Dictionary<string, int>();
        volatile float orientationYawDeg = float.NaN;   // 程式用 SetRobotOrientation 送來的航向
        readonly System.Random rng = new System.Random(3);

        long NowUs() => clock.ElapsedTicks * 1000000L / System.Diagnostics.Stopwatch.Frequency;

        void Update()
        {
            if (Drive == null) return;
            var p = Drive.Pose2d; px = p.x; py = p.y; pth = Drive.HeadingRad;
        }

        public void Begin() { cts = new CancellationTokenSource(); Task.Run(() => Run(cts.Token)); }
        void OnDestroy() { try { cts?.Cancel(); ws?.Abort(); } catch { } }

        async Task Run(CancellationToken ct)
        {
            try
            {
                while (!ct.IsCancellationRequested)
                {
                    ws?.Dispose();
                    ws = new ClientWebSocket();
                    ws.Options.AddSubProtocol("v4.1.networktables.first.wpi.edu");
                    Status = "connecting";
                    try { await ws.ConnectAsync(new Uri("ws://127.0.0.1:5810/nt/frcsim"), ct); }
                    catch { await Task.Delay(1500, ct); continue; }
                    Connected = true; synced = false; pubs.Clear(); Status = "connected";

                    // 訂閱程式送來的機器人航向(MegaTag2 會原樣回報)
                    var subs = new JArray();
                    foreach (var n in Limelights)
                        subs.Add(new JObject { ["method"] = "subscribe", ["params"] = new JObject { ["topics"] = new JArray("/" + n + "/robot_orientation_set"), ["subuid"] = 1, ["options"] = new JObject { ["periodic"] = 0.05 } } });
                    subs.Add(new JObject { ["method"] = "subscribe", ["params"] = new JObject { ["topics"] = new JArray("/AdvantageKit/RealOutputs"), ["subuid"] = 2, ["options"] = new JObject { ["prefix"] = true, ["periodic"] = 0.2 } } });
                    await SendText(subs.ToString(Newtonsoft.Json.Formatting.None), ct);
                    await SendBinary(Frame(-1, 0, 2, NowUs(), null), ct);   // 時間同步請求

                    var recv = Task.Run(() => ReceiveLoop(ct));
                    int syncTick = 0;
                    while (ws.State == WebSocketState.Open && !ct.IsCancellationRequested)
                    {
                        if (++syncTick % 60 == 0) await SendBinary(Frame(-1, 0, 2, NowUs(), null), ct);
                        if (synced) await PublishAll(ct);
                        await Task.Delay(50, ct);
                    }
                    Connected = false; Status = "disconnected";
                    await Task.Delay(1000, ct);
                }
            }
            catch (OperationCanceledException) { }
            catch (Exception e) { Status = "error: " + e.Message; }
        }

        async Task ReceiveLoop(CancellationToken ct)
        {
            var buf = new byte[1 << 16];
            var ms = new MemoryStream();
            try
            {
                while (ws.State == WebSocketState.Open && !ct.IsCancellationRequested)
                {
                    var r = await ws.ReceiveAsync(new ArraySegment<byte>(buf), ct);
                    if (r.MessageType == WebSocketMessageType.Close) break;
                    ms.Write(buf, 0, r.Count);
                    if (!r.EndOfMessage) continue;
                    if (r.MessageType == WebSocketMessageType.Binary) HandleBinary(ms.ToArray());
                    else HandleText(Encoding.UTF8.GetString(ms.ToArray()));
                    ms.SetLength(0);
                }
            }
            catch { }
        }

        void HandleText(string s)
        {
            try
            {
                foreach (var m in JArray.Parse(s))
                    if ((string)m["method"] == "announce")
                        topicById[(long)m["params"]["id"]] = new KeyValuePair<string, string>((string)m["params"]["name"], (string)m["params"]["type"]);
            }
            catch { }
        }

        void HandleBinary(byte[] data)
        {
            int pos = 0;
            try
            {
                while (pos < data.Length)
                {
                    var arr = MsgPack.Read(data, ref pos) as object[];
                    if (arr == null || arr.Length < 4) continue;
                    long id = Convert.ToInt64(arr[0]);
                    if (id == -1)
                    {
                        // [-1, serverTime, ?, clientTime]:時鐘偏移 = server + rtt/2 - now
                        long clientT = Convert.ToInt64(arr[3]);
                        long serverT = Convert.ToInt64(arr[1]);
                        long rtt = NowUs() - clientT;
                        clockOffsetUs = serverT + rtt / 2.0 - NowUs();
                        synced = true;
                    }
                    else if (arr[3] is byte[] raw && raw.Length == 24 && topicById.TryGetValue(id, out var tp) && tp.Value.Contains("Pose2d"))
                        CodePoses[tp.Key] = new[] { BitConverter.ToDouble(raw, 0), BitConverter.ToDouble(raw, 8), BitConverter.ToDouble(raw, 16) };
                    else if ((arr[3] is double || arr[3] is float) && topicById.TryGetValue(id, out var tpd))
                        CodeScalars[tpd.Key] = Convert.ToDouble(arr[3]);
                    else if (arr[3] is object[] jv && jv.Length > 3 && topicById.TryGetValue(id, out var tpj) && tpj.Key.Contains("Joystick0") && tpj.Key.Contains("Axis"))
                    {
                        var dv = new double[jv.Length];
                        for (int ji = 0; ji < jv.Length; ji++) dv[ji] = Convert.ToDouble(jv[ji]);
                        CodeAxes = dv;
                    }
                    else if (arr[3] is object[] vals && vals.Length > 0 && vals[0] != null && topicById.TryGetValue(id, out var tp2) && tp2.Key.EndsWith("robot_orientation_set"))
                        orientationYawDeg = (float)Convert.ToDouble(vals[0]);
                }
            }
            catch { }
        }

        async Task PublishAll(CancellationToken ct)
        {
            double th = pth, x = px, y = py;
            foreach (var name in Limelights)
            {
                var vis = Visible(x, y, th);
                int n = vis.Count;
                double latency = 25 + rng.NextDouble() * 10;
                double avgDist = 0, avgArea = 0, span = 0;
                foreach (var t in vis) { avgDist += t.dist; avgArea += t.ta; }
                if (n > 0) { avgDist /= n; avgArea /= n; }
                for (int i = 0; i < n; i++) for (int j = i + 1; j < n; j++)
                    span = Math.Max(span, Math.Sqrt(Math.Pow(vis[i].tx2 - vis[j].tx2, 2) + Math.Pow(vis[i].ty2 - vis[j].ty2, 2)));
                double sigma = n > 0 ? (0.01 + 0.01 * avgDist * avgDist) / Math.Sqrt(n) : 0;
                double G() => (rng.NextDouble() + rng.NextDouble() + rng.NextDouble() - 1.5) * 2 * sigma;
                double trueYaw = th * 180 / Math.PI;
                double yawMt2 = float.IsNaN(orientationYawDeg) ? trueYaw : orientationYawDeg;
                var fid = new List<double>();
                foreach (var t in vis) { fid.Add(t.id); fid.Add(t.tx); fid.Add(t.ty); fid.Add(t.ta); fid.Add(t.dist); fid.Add(t.dist); fid.Add(n > 1 ? 0.05 : 0.2); }
                double px0 = n > 0 ? x + G() : 0, py0 = n > 0 ? y + G() : 0;
                var head = new double[] { px0, py0, 0, 0, 0, n > 0 ? yawMt2 : 0, latency, n, span, avgDist, avgArea };
                var mt2 = new List<double>(head); mt2.AddRange(fid);
                var mt1 = new List<double>(head) { }; mt1[5] = n > 0 ? trueYaw + G() * 20 : 0; mt1.AddRange(fid);
                TagsSeen = n;
                await Pub("/" + name + "/botpose_orb_wpiblue", 17, mt2.ToArray(), ct);
                await Pub("/" + name + "/botpose_wpiblue", 17, mt1.ToArray(), ct);
                await Pub("/" + name + "/tv", 1, n > 0 ? 1.0 : 0.0, ct);
                double tid = -1, ptx = 0, pty = 0, pta = 0;
                foreach (var t in vis) if (t.ta > pta) { pta = t.ta; tid = t.id; ptx = t.tx; pty = t.ty; }
                await Pub("/" + name + "/tid", 1, tid, ct);
                await Pub("/" + name + "/tx", 1, ptx, ct);
                await Pub("/" + name + "/ty", 1, pty, ct);
                await Pub("/" + name + "/ta", 1, pta, ct);
            }
        }

        class Vis { public double id, tx, ty, ta, dist, tx2, ty2; }

        List<Vis> Visible(double x, double y, double th)
        {
            var res = new List<Vis>();
            double c = Math.Cos(th), s = Math.Sin(th);
            double cx = x + CamX * c - CamY * s, cy = y + CamX * s + CamY * c;
            const double In = 0.0254;
            foreach (var t in Tags)
            {
                double tx = t[1] * In, ty = t[2] * In, tz = t[3] * In, rot = t[4] * Math.PI / 180;
                double dx = tx - cx, dy = ty - cy, dz = tz - CamZ;
                double flat = Math.Sqrt(dx * dx + dy * dy), dist = Math.Sqrt(flat * flat + dz * dz);
                if (dist > MaxDist || dist < 0.3) continue;
                double a = Math.Atan2(dy, dx) - th; a = Math.Atan2(Math.Sin(a), Math.Cos(a));
                double el = Math.Atan2(dz, flat);
                if (Math.Abs(a) > HFov / 2 * Math.PI / 180 || Math.Abs(el) > VFov / 2 * Math.PI / 180) continue;
                double facing = -(Math.Cos(rot) * dx + Math.Sin(rot) * dy) / flat;
                if (facing < 0.26) continue;
                double area = Math.Min(100, (0.2064 * 0.2064 * facing) / (dist * dist * 0.35) * 100);
                res.Add(new Vis { id = t[0], tx = -a * 180 / Math.PI, ty = el * 180 / Math.PI, ta = area, dist = dist, tx2 = tx, ty2 = ty });
            }
            return res;
        }

        async Task Pub(string topic, int type, object value, CancellationToken ct)
        {
            if (!pubs.TryGetValue(topic, out int id))
            {
                id = pubs.Count + 1;
                pubs[topic] = id;
                string tn = type == 17 ? "double[]" : "double";
                await SendText(new JArray(new JObject { ["method"] = "publish", ["params"] = new JObject { ["name"] = topic, ["pubuid"] = id, ["type"] = tn, ["properties"] = new JObject() } }).ToString(Newtonsoft.Json.Formatting.None), ct);
            }
            await SendBinary(Frame(id, (long)(NowUs() + clockOffsetUs), type, NowUs(), value), ct);
            Published++;
        }

        async Task SendText(string s, CancellationToken ct)
        { await ws.SendAsync(new ArraySegment<byte>(Encoding.UTF8.GetBytes(s)), WebSocketMessageType.Text, true, ct); }
        async Task SendBinary(byte[] b, CancellationToken ct)
        { await ws.SendAsync(new ArraySegment<byte>(b), WebSocketMessageType.Binary, true, ct); }

        // NT4 二進位 frame:msgpack [id, timestamp(us), type, value];double 一律寫 float64(整數會被 ntcore 拒絕)
        static byte[] Frame(long id, long ts, int type, long localNow, object value)
        {
            var b = new List<byte>();
            b.Add(0x94);
            if (id == -1)
            {
                // 時間同步請求 [-1, 0, 2, 本地時間](伺服器回 [-1, 伺服器時間, ?, 我們的時間])
                Int(b, -1); Int(b, 0); Int(b, 2); Int(b, localNow);
                return b.ToArray();
            }
            Int(b, id); Int(b, ts); Int(b, type);
            if (value is double d) Dbl(b, d);
            else if (value is double[] a) { b.Add(0xdc); b.Add((byte)(a.Length >> 8)); b.Add((byte)a.Length); foreach (var v in a) Dbl(b, v); }
            return b.ToArray();
        }

        static void Int(List<byte> b, long v)
        {
            if (v >= 0 && v < 128) { b.Add((byte)v); return; }
            if (v < 0 && v >= -32) { b.Add((byte)(0xe0 | (v + 32))); return; }
            b.Add(0xd3);
            for (int i = 7; i >= 0; i--) b.Add((byte)(v >> (8 * i)));
        }
        static void Dbl(List<byte> b, double v)
        {
            b.Add(0xcb);
            var bytes = BitConverter.GetBytes(v); Array.Reverse(bytes); b.AddRange(bytes);
        }
    }

    // 最小 msgpack 讀取器:整數/浮點/字串/二進位/陣列/map/布林/nil(夠讀 NT4 的 value frame)
    static class MsgPack
    {
        public static object Read(byte[] d, ref int p)
        {
            byte t = d[p++];
            if (t <= 0x7f) return (long)t;
            if (t >= 0xe0) return (long)(sbyte)t;
            if ((t & 0xe0) == 0xa0) return Str(d, ref p, t & 0x1f);
            if ((t & 0xf0) == 0x90) return Arr(d, ref p, t & 0x0f);
            if ((t & 0xf0) == 0x80) return Map(d, ref p, t & 0x0f);
            switch (t)
            {
                case 0xc0: return null;
                case 0xc2: return false;
                case 0xc3: return true;
                case 0xca: { var b = new[] { d[p + 3], d[p + 2], d[p + 1], d[p] }; p += 4; return (double)BitConverter.ToSingle(b, 0); }
                case 0xcb: { var b = new byte[8]; for (int i = 0; i < 8; i++) b[i] = d[p + 7 - i]; p += 8; return BitConverter.ToDouble(b, 0); }
                case 0xcc: return (long)d[p++];
                case 0xcd: { long v = (d[p] << 8) | d[p + 1]; p += 2; return v; }
                case 0xce: { long v = ((long)d[p] << 24) | ((long)d[p + 1] << 16) | ((long)d[p + 2] << 8) | d[p + 3]; p += 4; return v; }
                case 0xcf: { long v = 0; for (int i = 0; i < 8; i++) v = (v << 8) | d[p + i]; p += 8; return v; }
                case 0xd0: return (long)(sbyte)d[p++];
                case 0xd1: { long v = (short)((d[p] << 8) | d[p + 1]); p += 2; return v; }
                case 0xd2: { long v = (int)(((uint)d[p] << 24) | ((uint)d[p + 1] << 16) | ((uint)d[p + 2] << 8) | d[p + 3]); p += 4; return v; }
                case 0xd3: { long v = 0; for (int i = 0; i < 8; i++) v = (v << 8) | d[p + i]; p += 8; return v; }
                case 0xd9: { int n = d[p++]; return Str(d, ref p, n); }
                case 0xda: { int n = (d[p] << 8) | d[p + 1]; p += 2; return Str(d, ref p, n); }
                case 0xc4: { int n = d[p++]; var bb = new byte[n]; Array.Copy(d, p, bb, 0, n); p += n; return bb; }
                case 0xc5: { int n = (d[p] << 8) | d[p + 1]; p += 2; var bb = new byte[n]; Array.Copy(d, p, bb, 0, n); p += n; return bb; }
                case 0xdc: { int n = (d[p] << 8) | d[p + 1]; p += 2; return Arr(d, ref p, n); }
                case 0xdd: { int n = (int)(((uint)d[p] << 24) | ((uint)d[p + 1] << 16) | ((uint)d[p + 2] << 8) | d[p + 3]); p += 4; return Arr(d, ref p, n); }
                case 0xde: { int n = (d[p] << 8) | d[p + 1]; p += 2; return Map(d, ref p, n); }
            }
            throw new Exception("msgpack type " + t);
        }
        static string Str(byte[] d, ref int p, int n) { var s = Encoding.UTF8.GetString(d, p, n); p += n; return s; }
        static object[] Arr(byte[] d, ref int p, int n) { var a = new object[n]; for (int i = 0; i < n; i++) a[i] = Read(d, ref p); return a; }
        static object Map(byte[] d, ref int p, int n) { for (int i = 0; i < n; i++) { Read(d, ref p); Read(d, ref p); } return null; }
    }
}
