using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

namespace FrcSim
{
    // 計時賽幽靈(模組 G):錄製 / 存檔 / 重播。
    //   - 錄製:固定時間步(RaceSession.FixedStep)餵進 Position / Rotation,每 0.1 s 取一個樣本(線性內插到整數倍時間點)。
    //   - 存檔:Application.persistentDataPath/ghosts/*.bin 二進位。me = 玩家最佳圈,coach = RaceAI(難度 2)跑一圈。
    //   - 重播:半透明的同款車(RaceArt.BuildKart + 淡出材質),沒有碰撞體、不參與名次。每圈開始從頭重播。
    public sealed class GhostData
    {
        public const float Dt = 0.1f;
        public int Track, Cc;
        public bool Mirror, Reverse;
        public float Duration;             // 這一圈的模擬秒數(= 最後一個樣本的時間)
        public float[] T;                  // 每個樣本的時間(0, 0.1, 0.2 ... Duration)
        public Vector3[] Pos;
        public Quaternion[] Rot;
        public int Count { get { return Pos != null ? Pos.Length : 0; } }

        /// 取 t 秒時的位置/朝向(線性 / Slerp 內插;範圍外夾到頭尾)。
        public void Sample(float t, out Vector3 p, out Quaternion r)
        {
            int n = Count;
            if (n == 0) { p = Vector3.zero; r = Quaternion.identity; return; }
            if (n == 1 || t <= 0f) { p = Pos[0]; r = Rot[0]; return; }
            if (t >= T[n - 1]) { p = Pos[n - 1]; r = Rot[n - 1]; return; }
            int i = Mathf.Clamp(Mathf.FloorToInt(t / Dt), 0, n - 2);
            while (i < n - 2 && T[i + 1] <= t) i++;
            while (i > 0 && T[i] > t) i--;
            float span = Mathf.Max(1e-5f, T[i + 1] - T[i]);
            float a = Mathf.Clamp01((t - T[i]) / span);
            p = Vector3.Lerp(Pos[i], Pos[i + 1], a);
            r = Quaternion.Slerp(Rot[i], Rot[i + 1], a);
        }
    }

    /// 固定時間步錄製器。Begin(起點)→ 每個物理步 Step(dt, 位置, 朝向) → Finish() 取回資料。
    public sealed class GhostRecorder
    {
        readonly List<float> t = new List<float>();
        readonly List<Vector3> p = new List<Vector3>();
        readonly List<Quaternion> r = new List<Quaternion>();
        float clock, prevT; Vector3 prevP; Quaternion prevR; int next;
        public bool Running { get; private set; }
        public float Clock { get { return clock; } }

        public void Begin(Vector3 pos, Quaternion rot)
        {
            t.Clear(); p.Clear(); r.Clear();
            clock = 0f; prevT = 0f; prevP = pos; prevR = rot; next = 1;
            t.Add(0f); p.Add(pos); r.Add(rot);
            Running = true;
        }

        public void Step(float dt, Vector3 pos, Quaternion rot)
        {
            if (!Running || dt <= 0f) return;
            float newT = clock + dt;
            while (next * GhostData.Dt <= newT + 1e-5f)
            {
                float st = next * GhostData.Dt;
                float a = Mathf.Clamp01((st - prevT) / dt);
                t.Add(st); p.Add(Vector3.Lerp(prevP, pos, a)); r.Add(Quaternion.Slerp(prevR, rot, a));
                next++;
            }
            clock = newT; prevT = newT; prevP = pos; prevR = rot;
        }

        public GhostData Finish()
        {
            if (!Running) return null;
            Running = false;
            if (t[t.Count - 1] < clock - 1e-4f) { t.Add(clock); p.Add(prevP); r.Add(prevR); }
            return new GhostData { Duration = t[t.Count - 1], T = t.ToArray(), Pos = p.ToArray(), Rot = r.ToArray() };
        }
    }

    /// 幽靈檔案(二進位)。測試時把 RaceGhost.DirOverride 指到暫存資料夾。
    public static class GhostStore
    {
        const int Magic = 0x54534847;   // "GHST"
        const int Version = 1;

        public static string Dir
        {
            get
            {
                string d = RaceGhost.DirOverride;
                if (string.IsNullOrEmpty(d)) d = Path.Combine(Application.persistentDataPath, "ghosts");
                return d;
            }
        }

        public static string FileFor(string kind, int track, bool mirror, bool reverse, int cc)
        {
            return Path.Combine(Dir, "ghost_" + kind + "_" + track + "_" + (mirror ? "m" : "n") + (reverse ? "r" : "f") + "_" + cc + ".bin");
        }

        public static bool Exists(string path) { try { return File.Exists(path); } catch { return false; } }

        public static bool Save(string path, GhostData d)
        {
            if (d == null || d.Count < 2) return false;
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(path));
                string tmp = path + ".tmp";
                using (var fs = new FileStream(tmp, FileMode.Create, FileAccess.Write))
                using (var w = new BinaryWriter(fs))
                {
                    w.Write(Magic); w.Write(Version);
                    w.Write(d.Track); w.Write(d.Cc); w.Write(d.Mirror); w.Write(d.Reverse);
                    w.Write(d.Duration); w.Write(d.Count);
                    for (int i = 0; i < d.Count; i++)
                    {
                        w.Write(d.T[i]);
                        w.Write(d.Pos[i].x); w.Write(d.Pos[i].y); w.Write(d.Pos[i].z);
                        w.Write(d.Rot[i].x); w.Write(d.Rot[i].y); w.Write(d.Rot[i].z); w.Write(d.Rot[i].w);
                    }
                }
                if (File.Exists(path)) File.Delete(path);
                File.Move(tmp, path);
                return true;
            }
            catch (Exception e) { Debug.LogWarning("[RaceGhost] save failed: " + e.Message); return false; }
        }

        public static GhostData Load(string path)
        {
            try
            {
                if (!File.Exists(path)) return null;
                using (var fs = new FileStream(path, FileMode.Open, FileAccess.Read))
                using (var rd = new BinaryReader(fs))
                {
                    if (rd.ReadInt32() != Magic || rd.ReadInt32() != Version) return null;
                    var d = new GhostData();
                    d.Track = rd.ReadInt32(); d.Cc = rd.ReadInt32(); d.Mirror = rd.ReadBoolean(); d.Reverse = rd.ReadBoolean();
                    d.Duration = rd.ReadSingle();
                    int n = rd.ReadInt32();
                    if (n < 2 || n > 200000) return null;
                    d.T = new float[n]; d.Pos = new Vector3[n]; d.Rot = new Quaternion[n];
                    for (int i = 0; i < n; i++)
                    {
                        d.T[i] = rd.ReadSingle();
                        d.Pos[i] = new Vector3(rd.ReadSingle(), rd.ReadSingle(), rd.ReadSingle());
                        d.Rot[i] = new Quaternion(rd.ReadSingle(), rd.ReadSingle(), rd.ReadSingle(), rd.ReadSingle());
                    }
                    return d;
                }
            }
            catch (Exception e) { Debug.LogWarning("[RaceGhost] load failed: " + e.Message); return null; }
        }

        /// 只有比現有檔更快(或沒有檔)才覆蓋。回傳是否寫入。
        public static bool SaveIfBetter(string path, GhostData d)
        {
            var old = Load(path);
            if (old != null && d.Duration >= old.Duration - 0.0005f) return false;
            return Save(path, d);
        }
    }

    /// 一台半透明的幽靈車。
    public sealed class GhostCar
    {
        public GameObject Go;
        readonly Dictionary<long, Material> cache = new Dictionary<long, Material>();

        public static GhostCar Build(KartStyle style, Color color, float alpha, Transform parent, string name)
        {
            var g = new GhostCar();
            g.Go = new GameObject(name);
            if (parent != null) g.Go.transform.SetParent(parent, false);
            GameObject vis = null;
            try { vis = RaceArt.BuildKart(style, color); } catch (Exception e) { Debug.LogWarning("[RaceGhost] BuildKart failed: " + e.Message); }
            if (vis != null)
            {
                vis.transform.SetParent(g.Go.transform, false);
                vis.transform.localPosition = new Vector3(0f, -RaceKart.RideDist, 0f);
                foreach (var c in vis.GetComponentsInChildren<Collider>(true)) UnityEngine.Object.Destroy(c);
                foreach (var mb in vis.GetComponentsInChildren<MonoBehaviour>(true)) UnityEngine.Object.Destroy(mb);
                foreach (var rd in vis.GetComponentsInChildren<Renderer>(true))
                {
                    rd.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                    rd.receiveShadows = false;
                    var src = rd.sharedMaterials;
                    var dst = new Material[src.Length];
                    for (int i = 0; i < src.Length; i++) dst[i] = g.Ghostify(src[i], alpha, color);
                    rd.sharedMaterials = dst;
                }
            }
            g.Go.SetActive(false);
            return g;
        }

        Material Ghostify(Material m, float alpha, Color fallback)
        {
            Color c = fallback;
            if (m != null && m.HasProperty("_Color")) c = m.color;
            c.a = alpha;
            Color32 q = c;
            long key = ((long)q.r << 24) ^ ((long)q.g << 16) ^ ((long)q.b << 8) ^ q.a;
            if (cache.TryGetValue(key, out var gm) && gm != null) return gm;
            var sh = Shader.Find("Standard");
            if (sh == null) return m;
            gm = new Material(sh) { name = "GhostMat", color = c };
            gm.SetFloat("_Mode", 2f);
            gm.SetInt("_SrcBlend", (int)UnityEngine.Rendering.BlendMode.SrcAlpha);
            gm.SetInt("_DstBlend", (int)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
            gm.SetInt("_ZWrite", 0);
            gm.DisableKeyword("_ALPHATEST_ON");
            gm.EnableKeyword("_ALPHABLEND_ON");
            gm.DisableKeyword("_ALPHAPREMULTIPLY_ON");
            gm.renderQueue = 3000;
            gm.SetFloat("_Glossiness", 0.1f);
            cache[key] = gm;
            return gm;
        }

        public void Show(bool on) { if (Go != null && Go.activeSelf != on) Go.SetActive(on); }
        public void Set(Vector3 p, Quaternion r) { if (Go != null) Go.transform.SetPositionAndRotation(p, r); }
        public void Destroy()
        {
            foreach (var m in cache.Values) if (m != null) UnityEngine.Object.Destroy(m);
            cache.Clear();
            if (Go != null) UnityEngine.Object.Destroy(Go);
            Go = null;
        }
    }

    /// 計時賽幽靈的總管(RaceSession 呼叫)。
    public static class RaceGhost
    {
        public static string DirOverride;        // 測試用:幽靈檔資料夾
        public static GhostData Me, Coach;       // 目前重播的兩隻
        public static bool NewBest;              // 本場刷新了最佳圈(覆蓋了幽靈)
        public static Action<float, Vector3> TraceHook;   // 測試用:每個物理步回報(錄製時鐘, 原始位置)
        public static string Status = "";        // 教練幽靈生成中的提示文字
        public static bool PlayerRecording { get { return rec != null && rec.Running; } }
        public static float ReplayClock { get { return clock; } }
        public static GhostCar MeCar { get { return meCar; } }
        public static GhostCar CoachCar { get { return coachCar; } }

        static RaceConfig cfg; static Transform parent; static KartStyle style; static Color color;
        static readonly GhostRecorder rec = new GhostRecorder();
        static GhostCar meCar, coachCar;
        static float clock;

        public static string MePath { get { return GhostStore.FileFor("me", cfg.TrackId, cfg.Mirror, cfg.Reverse, cfg.Infinite ? 999 : cfg.CC); } }
        public static string CoachPath { get { return GhostStore.FileFor("coach", cfg.TrackId, cfg.Mirror, cfg.Reverse, cfg.Infinite ? 999 : cfg.CC); } }
        public static bool NeedCoach { get { return cfg != null && !GhostStore.Exists(CoachPath); } }

        public static void Init(RaceConfig c, Transform root)
        {
            Shutdown();
            cfg = c; parent = root; style = c.Kart;
            color = RacePalette.Kart[Mathf.Clamp(c.ColorIndex, 0, RacePalette.Kart.Length - 1)];
            NewBest = false; Status = "";
            Me = GhostStore.Load(MePath);
            Coach = GhostStore.Load(CoachPath);
            Rebuild();
        }

        static void Rebuild()
        {
            if (meCar != null) { meCar.Destroy(); meCar = null; }
            if (coachCar != null) { coachCar.Destroy(); coachCar = null; }
            if (Me != null) meCar = GhostCar.Build(style, color, 0.42f, parent, "GhostMe");
            if (Coach != null) coachCar = GhostCar.Build(KartStyle.Robot9427, new Color(1f, 0.62f, 0.2f), 0.38f, parent, "GhostCoach");
            clock = 0f; Place(0f);
        }

        /// 教練幽靈(RaceSession 跑完教練那圈後呼叫)。
        public static void SaveCoach(GhostData d)
        {
            if (d == null || cfg == null) return;
            d.Track = cfg.TrackId; d.Mirror = cfg.Mirror; d.Reverse = cfg.Reverse; d.Cc = cfg.Infinite ? 999 : cfg.CC;
            if (GhostStore.SaveIfBetter(CoachPath, d)) Coach = d;
            else Coach = GhostStore.Load(CoachPath);
            Rebuild();
        }

        static void Place(float t)
        {
            if (Me != null && meCar != null) { Me.Sample(t, out var p, out var r); meCar.Set(p, r); meCar.Show(t <= Me.Duration + 0.3f); }
            if (Coach != null && coachCar != null) { Coach.Sample(t, out var p, out var r); coachCar.Set(p, r); coachCar.Show(t <= Coach.Duration + 0.3f); }
        }

        /// 比賽(重新)開始前:停止錄製、幽靈回到起點。
        public static void ResetRun()
        {
            if (cfg == null) return;
            rec.Finish();
            clock = 0f; Place(0f);
        }

        /// 一圈開始(起跑 GO 或剛過線):重新錄製、幽靈回到 0 秒。
        public static void StartLap(Vector3 pos, Quaternion rot)
        {
            if (cfg == null) return;
            rec.Begin(pos, rot);
            clock = 0f; Place(0f);
        }

        /// 每個物理步呼叫(比賽中)。
        public static void Step(float dt, Vector3 pos, Quaternion rot)
        {
            if (cfg == null) return;
            if (!rec.Running) rec.Begin(pos, rot);
            rec.Step(dt, pos, rot);
            if (TraceHook != null) TraceHook(rec.Clock, pos);
            clock += dt;
            Place(clock);
        }

        /// 剛跑完一圈。回傳是否刷新最佳圈(刷新就覆蓋幽靈檔與正在重播的幽靈),並開始錄下一圈。
        public static bool LapDone(Vector3 pos, Quaternion rot)
        {
            if (cfg == null) return false;
            if (!rec.Running) { StartLap(pos, rot); return false; }
            var d = rec.Finish();
            bool better = false;
            if (d != null && d.Count >= 2)
            {
                d.Track = cfg.TrackId; d.Mirror = cfg.Mirror; d.Reverse = cfg.Reverse; d.Cc = cfg.Infinite ? 999 : cfg.CC;
                better = Me == null || d.Duration < Me.Duration - 0.0005f;
                if (better)
                {
                    GhostStore.Save(MePath, d);
                    bool hadCar = meCar != null;
                    Me = d; NewBest = true;
                    if (!hadCar) meCar = GhostCar.Build(style, color, 0.42f, parent, "GhostMe");
                }
            }
            StartLap(pos, rot);
            return better;
        }

        public static void Shutdown()
        {
            if (meCar != null) { meCar.Destroy(); meCar = null; }
            if (coachCar != null) { coachCar.Destroy(); coachCar = null; }
            rec.Finish();
            Me = null; Coach = null; cfg = null; parent = null; Status = ""; NewBest = false; clock = 0f;
        }
    }
}
