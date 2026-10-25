using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace FrcSim
{
    /// 為什麼被裁判重置(RaceCheckpoints 寫入,RaceReferee 讀)。
    public enum RefereeCause { None, Fell, OutOfBounds, OffCourse, SkippedGate, WrongWay, Stuck, Manual }

    /// 車子目前的移動狀態(選用):RaceCheckpoints 的裁判規則用它判斷「離開道路是不是合法」。RaceKart 不實作也行(RaceCheckpoints 直接讀 RaceKart);
    /// 測試用的幽靈車實作它來模擬滑翔 / 軌道滑行。
    public interface IRacerAirState
    {
        bool LegitOffRoad { get; }   // 滑翔 / 軌道滑行 / 跑牆 / 特技 / 滯空:離路不計時
        bool Gliding { get; }        // 滑翔中:斜切過幾個門時幫忙補記,不算跳門
    }

    // 裁判(瑪利歐賽車式的雲朵裁判,原創造型):車子迷路 / 抄近路 / 逆向 / 掉出去時,
    // 一朵拿旗子和哨子的小雲飄到車旁(哨聲)、用光束把車吊起來、送到最後通過的檢查點重置點、放下、車子無敵 1.5 s 並閃爍,畫面上提示「請走正確路線」。
    //   - RaceSession 開賽建立進度時呼叫 Attach(Progress, Root);RaceCheckpoints.RefereeHook 把「要重置」的車交給這裡。
    //   - 同時最多 2 個裁判(池):玩家優先;沒空位時 AI 直接立刻重置(不播動畫)。
    //   - 快速模式(Fast 或 RaceSession.TestFast)與非 RaceKart 的車(幽靈車):不播動畫,立刻重置,但狀態語意不變(Reset 事件、無敵 1.5 s、提示、計數)。
    //   - 動畫期間車子 Frozen,每個物理步用 Teleport 吊著走;檢查點端 Held = true(不判定、不過門);結束時 FinishHold 才真正重置。
    public sealed class RaceReferee : MonoBehaviour
    {
        public const float InvincibleSeconds = 1.5f;
        public const int PoolSize = 2;
        public static bool Fast;                                   // 測試:不播動畫
        public static bool IsFast { get { return Fast || RaceSession.TestFast; } }
        public static RaceReferee Active;
        // 測試用統計
        public static int HookCalls, Animated, InstantResets;
        public static RefereeCause LastCause = RefereeCause.None;
        public static string LastMessage = "";

        enum Phase { Arrive, Carry, Release, Leave, Done }

        sealed class Job
        {
            public IRacerBody Body; public RaceKart Kart; public RefereeCause Cause; public bool Quick;
            public Phase Ph; public float T;
            public Vector3 P0, P1, Side; public Quaternion R0, R1;
            public Vector3 CloudPos;
            public GameObject Cloud; public Transform Beam; public bool Used;
            public float Dur(Phase p)
            {
                switch (p)
                {
                    case Phase.Arrive: return Quick ? 0.25f : 0.7f;
                    case Phase.Carry: return Quick ? 0.5f : 1.5f;
                    case Phase.Release: return Quick ? 0.15f : 0.4f;
                    default: return Quick ? 0.4f : 0.8f;
                }
            }
        }

        sealed class Blinker { public RaceKart Kart; public Renderer[] R; public float Until; }

        RaceCheckpoints cps;
        readonly Job[] jobs = new Job[PoolSize];
        readonly Dictionary<int, IRacerBody> pending = new Dictionary<int, IRacerBody>();
        readonly List<Blinker> blinkers = new List<Blinker>();
        static Mesh cloudBody, cloudEye, cloudFlag, cloudWhistle, beamMesh;

        // ------------------------------------------------------------------ 掛上 / 拆下

        /// 把裁判接到一份檢查點進度上(RaceSession 每次 BuildProgress 呼叫;會先拆掉舊的)。parent 可為 null。
        public static RaceReferee Attach(RaceCheckpoints c, Transform parent)
        {
            if (Active == null)
            {
                var go = new GameObject("RaceReferee");
                if (parent != null) go.transform.SetParent(parent, false);
                Active = go.AddComponent<RaceReferee>();
            }
            Active.Bind(c);
            return Active;
        }

        void Bind(RaceCheckpoints c)
        {
            Unbind();
            cps = c;
            if (cps == null) return;
            cps.RefereeRules = true;
            cps.RefereeHook = OnHook;
            cps.Reset += OnReset;
        }

        void Unbind()
        {
            for (int i = 0; i < jobs.Length; i++) if (jobs[i] != null && jobs[i].Used) Finish(jobs[i], false);
            if (cps != null) { cps.RefereeHook = null; cps.Reset -= OnReset; }
            cps = null;
            pending.Clear();
        }

        void OnDestroy()
        {
            Unbind();
            for (int i = 0; i < jobs.Length; i++) if (jobs[i] != null && jobs[i].Cloud != null) Destroy(jobs[i].Cloud);
            for (int i = 0; i < blinkers.Count; i++) SetVisible(blinkers[i], true);
            if (Active == this) Active = null;
        }

        // ------------------------------------------------------------------ 檢查點端的呼叫

        bool OnHook(IRacerBody body, Pose pose, RefereeCause cause)
        {
            HookCalls++;
            LastCause = cause;
            pending[body.RacerId] = body;
            var kart = body as RaceKart;
            if (IsFast || kart == null) { InstantResets++; return false; }   // 立刻重置;OnReset 補上無敵 / 提示

            Job j = FreeJob();
            if (j == null && kart.IsPlayer) { j = StealFromAi(); }
            if (j == null) { InstantResets++; return false; }                // AI 沒空位:直接重置

            Animated++;
            j.Used = true; j.Body = body; j.Kart = kart; j.Cause = cause; j.Quick = !kart.IsPlayer;
            j.Ph = Phase.Arrive; j.T = 0f;
            j.P0 = kart.Position; if (j.P0.y < 0.8f) j.P0.y = 0.8f;           // 掉進水 / 深坑:從水面上方吊起
            j.R0 = kart.transform.rotation;
            j.P1 = pose.position + Vector3.up * (RaceKart.RideDist + 3.5f);
            j.R1 = pose.rotation;
            Vector3 side = Vector3.Cross(Vector3.up, pose.rotation * Vector3.forward);
            j.Side = side.sqrMagnitude > 1e-4f ? side.normalized : Vector3.right;
            kart.Frozen = true;
            kart.Teleport(j.P0, j.R0, false);
            EnsureCloud(j);
            j.Cloud.SetActive(true);
            Snd("count_beep", 0.8f, 2.3f);
            SfxBus.PlayDelayed("count_beep", 0.18f, 0.8f, 2.3f);
            return true;
        }

        void OnReset(int id, ResetReason why)
        {
            IRacerBody body;
            if (!pending.TryGetValue(id, out body)) return;     // 不是裁判經手的(例如手動重置)
            pending.Remove(id);
            RefereeCause cause = cps != null ? cps.LastCause(id) : RefereeCause.None;
            if (cause == RefereeCause.Manual || cause == RefereeCause.None) return;
            var kart = body as RaceKart;
            if (kart != null)
            {
                kart.SetInvincible(InvincibleSeconds);
                StartBlink(kart);
                if (kart.IsPlayer)
                {
                    string zh, en; Message(cause, out zh, out en);
                    RaceHud.Toast(zh, en);
                }
            }
            string z2, e2; Message(cause, out z2, out e2);
            LastMessage = Loc.Lang == "en" ? e2 : z2;
        }

        public static void Message(RefereeCause c, out string zh, out string en)
        {
            switch (c)
            {
                case RefereeCause.WrongWay: zh = "裁判:逆向了!請朝正確方向行駛"; en = "Referee: wrong way!"; break;
                case RefereeCause.Fell:
                case RefereeCause.OutOfBounds: zh = "裁判:掉出賽道,送你回去"; en = "Referee: back on track"; break;
                case RefereeCause.Stuck: zh = "裁判:卡住了,幫你一把"; en = "Referee: stuck, giving you a hand"; break;
                default: zh = "裁判:請走正確路線"; en = "Referee: stay on course"; break;
            }
        }

        // ------------------------------------------------------------------ 池

        Job FreeJob()
        {
            for (int i = 0; i < jobs.Length; i++)
            {
                if (jobs[i] == null) jobs[i] = new Job();
                if (!jobs[i].Used) return jobs[i];
            }
            return null;
        }

        Job StealFromAi()
        {
            for (int i = 0; i < jobs.Length; i++)
                if (jobs[i] != null && jobs[i].Used && jobs[i].Quick) { Finish(jobs[i], true); return jobs[i]; }
            return null;
        }

        public int ActiveJobs { get { int n = 0; for (int i = 0; i < jobs.Length; i++) if (jobs[i] != null && jobs[i].Used) n++; return n; } }

        // ------------------------------------------------------------------ 動畫(物理步推進,和渲染無關)

        void FixedUpdate()
        {
            float dt = Time.fixedDeltaTime;
            for (int i = 0; i < jobs.Length; i++) if (jobs[i] != null && jobs[i].Used) StepJob(jobs[i], dt);
        }

        void StepJob(Job j, float dt)
        {
            if (j.Kart == null) { Finish(j, false); return; }
            j.T += dt;
            float d = j.Dur(j.Ph);
            float u = Mathf.Clamp01(j.T / d);
            float e = u * u * (3f - 2f * u);
            switch (j.Ph)
            {
                case Phase.Arrive:
                    j.Kart.Teleport(j.P0, j.R0, false);
                    j.CloudPos = Vector3.Lerp(j.P0 + j.Side * 7f + Vector3.up * 13f, j.P0 + Vector3.up * 3.4f, e);
                    break;
                case Phase.Carry:
                {
                    Vector3 p = Vector3.Lerp(j.P0, j.P1, e) + Vector3.up * (3f * Mathf.Sin(u * Mathf.PI));
                    j.Kart.Teleport(p, Quaternion.Slerp(j.R0, j.R1, e), false);
                    j.CloudPos = p + Vector3.up * 3.4f;
                    break;
                }
                case Phase.Release:
                {
                    Vector3 end = j.P1 - Vector3.up * 3.2f;
                    Vector3 p = Vector3.Lerp(j.P1, end, e);
                    j.Kart.Teleport(p, j.R1, false);
                    j.CloudPos = j.P1 + Vector3.up * 3.4f;
                    break;
                }
                default:
                    j.CloudPos = Vector3.Lerp(j.P1 + Vector3.up * 3.4f, j.P1 + Vector3.up * 16f + j.Side * 6f, e);
                    break;
            }
            if (j.T >= d)
            {
                j.T = 0f;
                if (j.Ph == Phase.Release)
                {
                    j.Kart.Frozen = false;
                    if (cps != null) cps.FinishHold(j.Body.RacerId);       // 真正重置(Respawn + Reset 事件)
                    Snd("jump_land", 0.7f, 1f);
                    j.Ph = Phase.Leave;
                }
                else if (j.Ph == Phase.Leave) { Finish(j, false); return; }
                else { j.Ph = j.Ph + 1; if (j.Ph == Phase.Carry) Snd("item_get", 0.7f, 0.8f); }
            }
        }

        // 提前結束(拆掉 / 搶位):車子還被抓著就直接重置
        void Finish(Job j, bool reuse)
        {
            if (j.Used && (j.Ph == Phase.Arrive || j.Ph == Phase.Carry || j.Ph == Phase.Release) && j.Body != null)
            {
                if (j.Kart != null) j.Kart.Frozen = false;
                if (cps != null) cps.FinishHold(j.Body.RacerId);
            }
            j.Used = false; j.Body = null; j.Kart = null;
            if (j.Cloud != null) j.Cloud.SetActive(false);
        }

        // 雲朵和光束(渲染用,每幀跟著 Job)
        void LateUpdate()
        {
            for (int i = 0; i < jobs.Length; i++)
            {
                Job j = jobs[i];
                if (j == null || !j.Used || j.Cloud == null) continue;
                float bob = Mathf.Sin(Time.time * 4f + i) * 0.15f;
                j.Cloud.transform.position = j.CloudPos + Vector3.up * bob;
                Vector3 look = j.Kart != null ? j.Kart.transform.position - j.Cloud.transform.position : Vector3.forward;
                look.y = 0f;
                if (look.sqrMagnitude > 1e-3f) j.Cloud.transform.rotation = Quaternion.LookRotation(look.normalized, Vector3.up);
                float sc = j.Ph == Phase.Leave ? Mathf.Lerp(1f, 0.2f, j.T / j.Dur(Phase.Leave)) : 1f;
                j.Cloud.transform.localScale = Vector3.one * sc;
                bool beam = j.Kart != null && (j.Ph == Phase.Carry || j.Ph == Phase.Release || (j.Ph == Phase.Arrive && j.T > j.Dur(Phase.Arrive) * 0.6f));
                j.Beam.gameObject.SetActive(beam);
                if (beam)
                {
                    Vector3 a = j.Cloud.transform.position - Vector3.up * 0.6f, b = j.Kart.transform.position + Vector3.up * 0.5f;
                    Vector3 dv = b - a; float len = dv.magnitude;
                    if (len > 0.05f)
                    {
                        j.Beam.position = (a + b) * 0.5f;
                        j.Beam.rotation = Quaternion.LookRotation(dv / len, Vector3.up);
                        j.Beam.localScale = new Vector3(0.12f, 0.12f, len);
                    }
                }
            }
            float now = Time.time;
            for (int i = blinkers.Count - 1; i >= 0; i--)
            {
                Blinker b = blinkers[i];
                if (b.Kart == null || now >= b.Until) { SetVisible(b, true); blinkers.RemoveAt(i); continue; }
                SetVisible(b, ((int)(now * 12f)) % 2 == 0);
            }
        }

        // ------------------------------------------------------------------ 閃爍

        void StartBlink(RaceKart k)
        {
            for (int i = 0; i < blinkers.Count; i++) if (blinkers[i].Kart == k) { blinkers[i].Until = Time.time + InvincibleSeconds; return; }
            blinkers.Add(new Blinker { Kart = k, R = k.GetComponentsInChildren<Renderer>(true), Until = Time.time + InvincibleSeconds });
        }

        static void SetVisible(Blinker b, bool on)
        {
            if (b.R == null) return;
            for (int i = 0; i < b.R.Length; i++) if (b.R[i] != null) b.R[i].enabled = on;
        }

        static void Snd(string n, float v, float pitch) { if (SfxBus.Enabled) SfxBus.Play(n, v, pitch); }

        // ------------------------------------------------------------------ 造型(程式生成,原創的小雲朵;三角形約 280)

        void EnsureCloud(Job j)
        {
            if (j.Cloud != null) return;
            if (cloudBody == null) BuildMeshes();
            var go = new GameObject("RefereeCloud");
            go.transform.SetParent(transform, false);
            AddPart(go.transform, "Body", cloudBody, new Color(0.97f, 0.98f, 1f), 0.2f);
            AddPart(go.transform, "Eyes", cloudEye, new Color(0.08f, 0.08f, 0.12f), 0.1f);
            AddPart(go.transform, "Flag", cloudFlag, new Color(1f, 0.82f, 0.1f), 0.1f);
            AddPart(go.transform, "Whistle", cloudWhistle, new Color(0.9f, 0.2f, 0.2f), 0.4f);
            var beam = new GameObject("Beam");
            beam.transform.SetParent(go.transform, false);
            beam.AddComponent<MeshFilter>().sharedMesh = beamMesh;
            var r = beam.AddComponent<MeshRenderer>();
            r.sharedMaterial = RaceArt.Mat(new Color(1f, 0.95f, 0.5f), 0.1f);
            r.shadowCastingMode = ShadowCastingMode.Off;
            j.Beam = beam.transform;
            j.Cloud = go;
            go.SetActive(false);
        }

        static void AddPart(Transform parent, string name, Mesh m, Color c, float smooth)
        {
            var g = new GameObject(name);
            g.transform.SetParent(parent, false);
            g.AddComponent<MeshFilter>().sharedMesh = m;
            var r = g.AddComponent<MeshRenderer>();
            r.sharedMaterial = RaceArt.Mat(c, smooth);
            r.shadowCastingMode = ShadowCastingMode.Off;
        }

        static void Puff(RaceMeshAcc acc, Vector3 c, Vector3 radii)
        {
            const int Seg = 8, Ring = 4;   // 8 x 4 -> 8 + 8 + 2 x 16 = 48 個三角形
            for (int ri = 0; ri < Ring; ri++)
            {
                float a0 = Mathf.PI * ri / Ring, a1 = Mathf.PI * (ri + 1) / Ring;
                for (int s = 0; s < Seg; s++)
                {
                    float b0 = 2f * Mathf.PI * s / Seg, b1 = 2f * Mathf.PI * (s + 1) / Seg;
                    Vector3 p00 = SP(c, radii, a0, b0), p01 = SP(c, radii, a0, b1), p10 = SP(c, radii, a1, b0), p11 = SP(c, radii, a1, b1);
                    Vector3 n = ((p00 + p01 + p10 + p11) * 0.25f - c).normalized;
                    if (ri == 0) acc.Tri(p00, p10, p11, n);
                    else if (ri == Ring - 1) acc.Tri(p00, p10, p01, n);
                    else acc.Quad(p00, p10, p11, p01, n);
                }
            }
        }

        static Vector3 SP(Vector3 c, Vector3 r, float a, float b)
        {
            return c + new Vector3(Mathf.Sin(a) * Mathf.Cos(b) * r.x, Mathf.Cos(a) * r.y, Mathf.Sin(a) * Mathf.Sin(b) * r.z);
        }

        static void BuildMeshes()
        {
            var body = new RaceMeshAcc();
            Puff(body, new Vector3(0f, 0f, 0f), new Vector3(1.0f, 0.75f, 0.9f));
            Puff(body, new Vector3(-0.95f, -0.15f, 0f), new Vector3(0.65f, 0.55f, 0.6f));
            Puff(body, new Vector3(0.95f, -0.15f, 0f), new Vector3(0.65f, 0.55f, 0.6f));
            Puff(body, new Vector3(0f, 0.55f, 0.1f), new Vector3(0.6f, 0.5f, 0.55f));
            Puff(body, new Vector3(0f, -0.2f, -0.55f), new Vector3(0.7f, 0.5f, 0.45f));
            cloudBody = body.ToMesh("RefereeBody");

            var eye = new RaceMeshAcc();
            Vector3 r = Vector3.right, u = Vector3.up, f = Vector3.forward;
            eye.Box(new Vector3(-0.3f, 0.12f, 0.88f), r, u, f, new Vector3(0.1f, 0.16f, 0.05f));
            eye.Box(new Vector3(0.3f, 0.12f, 0.88f), r, u, f, new Vector3(0.1f, 0.16f, 0.05f));
            cloudEye = eye.ToMesh("RefereeEyes");

            var flag = new RaceMeshAcc();
            flag.Box(new Vector3(0.9f, 1.1f, 0f), r, u, f, new Vector3(0.04f, 0.9f, 0.04f));     // 旗桿
            flag.Quad(new Vector3(0.94f, 1.95f, 0f), new Vector3(0.94f, 1.35f, 0f), new Vector3(1.9f, 1.55f, 0f), new Vector3(1.9f, 1.8f, 0f), r);   // 旗面(雙面)
            flag.Quad(new Vector3(0.94f, 1.95f, 0f), new Vector3(1.9f, 1.8f, 0f), new Vector3(1.9f, 1.55f, 0f), new Vector3(0.94f, 1.35f, 0f), -r);
            cloudFlag = flag.ToMesh("RefereeFlag");

            var wh = new RaceMeshAcc();
            wh.Box(new Vector3(0f, -0.12f, 0.95f), r, u, f, new Vector3(0.1f, 0.07f, 0.16f));   // 嘴上的哨子
            wh.Box(new Vector3(0f, -0.02f, 1.1f), r, u, f, new Vector3(0.05f, 0.05f, 0.06f));
            cloudWhistle = wh.ToMesh("RefereeWhistle");

            var bm = new RaceMeshAcc();
            bm.Box(Vector3.zero, r, u, f, new Vector3(0.5f, 0.5f, 0.5f));                        // 單位光束(縮放成細長)
            beamMesh = bm.ToMesh("RefereeBeam");
        }

        /// 三角形總數(預算檢查用)。
        public static int CloudTriangles
        {
            get
            {
                if (cloudBody == null) BuildMeshes();
                return (int)(cloudBody.GetIndexCount(0) + cloudEye.GetIndexCount(0) + cloudFlag.GetIndexCount(0) + cloudWhistle.GetIndexCount(0) + beamMesh.GetIndexCount(0)) / 3;
            }
        }
    }
}
