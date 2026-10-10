using UnityEngine;

namespace FrcSim
{
    // FUEL 從上方穿過 HUB 開口(72in 高、約 1.05m 見方)= 1 分;球從中立區側底部出口吐回場上。
    // 目前 HUB 一律啟動(階段/輪替規則之後再做)。
    public class ScoreManager : MonoBehaviour
    {
        public static int BlueScore, RedScore;
        const float ScoreBandTop = 1.80f, ScoreBandBottom = 1.62f;
        const float Half = 0.50f;

        // ---- 比賽時鐘(官方 2026 REBUILT:AUTO 20s、TRANSITION 10s、SHIFT1~4 各 25s、END GAME 30s;共 160s)----
        public static bool ClockOn = true;
        public static float MatchTime;                 // 已進行秒數
        public static bool BlueWonAuto;                // AUTO 得分較多者(同分:藍方)
        public static string Phase = "AUTO";
        public static bool BlueActive = true, RedActive = true, Ended;
        public static float TimeLeft => Mathf.Max(0f, 160f - MatchTime);

        static void UpdateClock()
        {
            UpdateClockCore();
            // 真實程式模式:AUTO 階段讓 DriverStation 進入 autonomous(機器人程式跑它的自動模式),其餘階段是 teleop
            if (GameSession.Hal != null)
            {
                GameSession.Hal.Autonomous = ClockOn && Phase == "AUTO";
                // 開了比賽時鐘:機器人要等「程式完全跑起來」才 Enable(否則 AUTO 在物理 agent 接管前就空轉掉了),比賽結束後再 Disable
                GameSession.Hal.Enabled = !ClockOn || (!RobotWaiting() && !Ended);
            }
        }

        // 真實程式模式:機器人程式還沒完全跑起來(沒連上 HALSim,或物理 agent 還沒接管馬達)
        static bool RobotWaiting() =>
            GameSession.Hal != null && (!GameSession.Hal.Connected || (GameSession.Mech != null && GameSession.Mech.Sim != null && !GameSession.Mech.Sim.Ready));

        static void UpdateClockCore()
        {
            if (!ClockOn) { Phase = "FREE PLAY"; BlueActive = RedActive = true; Ended = false; return; }
            float t = MatchTime;
            if (t < 20f) { Phase = "AUTO"; BlueActive = RedActive = true; BlueWonAuto = BlueScore >= RedScore; return; }
            if (t < 30f) { Phase = "TRANSITION"; BlueActive = RedActive = true; return; }
            if (t < 130f)
            {
                int shift = (int)((t - 30f) / 25f) + 1;          // 1..4
                Phase = "SHIFT " + shift;
                bool winnerActive = shift % 2 == 0;               // 贏 AUTO 的一方:SHIFT 1、3 不啟動,2、4 啟動
                BlueActive = BlueWonAuto ? winnerActive : !winnerActive;
                RedActive = !BlueActive;
                return;
            }
            if (t < 160f) { Phase = "END GAME"; BlueActive = RedActive = true; return; }
            if (t < 163f) { Phase = "SCORING GRACE"; BlueActive = RedActive = true; return; }   // 結束後 3 秒計分寬限
            Phase = "MATCH OVER"; BlueActive = RedActive = false; Ended = true;
        }

        Vector2 blueHub, redHub;
        public static readonly System.Collections.Generic.List<string> Trace = new System.Collections.Generic.List<string>();
        readonly System.Collections.Generic.HashSet<Fuel> traced = new System.Collections.Generic.HashSet<Fuel>();

        void Awake()
        {
            BlueScore = RedScore = 0;
            MatchTime = 0f; Ended = false; BlueWonAuto = true;
            ClockOn = PlayerPrefs.GetInt("matchClock", 1) == 1 && System.Array.IndexOf(System.Environment.GetCommandLineArgs(), "-noclock") < 0;
            float L = SimConstants.FieldLength, W = SimConstants.FieldWidth;
            float d = SimConstants.AllianceZoneDepth + SimConstants.HubSize / 2f;
            blueHub = new Vector2(d, W / 2f);
            redHub = new Vector2(L - d, W / 2f);
        }

        // N:跳過 AUTO / TRANSITION,直接進 TELEOP(真實程式模式下 AUTO 手把無效,想直接開車就按 N)
        void Update()
        {
            if (!MenuSystem.Blocking && ClockOn && MatchTime < 30f && Input.GetKeyDown(KeyCode.N)) MatchTime = 30f;
        }

        void FixedUpdate()
        {
            // 真實程式模式:機器人程式連上 HALSim 之後才開始計時(連線要 20~60 秒,不然 AUTO 的 20 秒早就過了,程式的自動模式根本沒跑到)
            if (ClockOn && !RobotWaiting()) MatchTime += Time.fixedDeltaTime;
            UpdateClock();
            for (int i = FuelManager.All.Count - 1; i >= 0; i--)
            {
                var f = FuelManager.All[i];
                var p = f.transform.position;
                if (p.y > ScoreBandTop || p.y < ScoreBandBottom) continue;
                var rb = f.GetComponent<Rigidbody>();
                if (rb.linearVelocity.y >= 0f) continue;
                if (traced.Add(f) && Trace.Count < 40) Trace.Add($"fuel@band x={p.x:0.00} z={p.z:0.00} y={p.y:0.00} blueHub=({blueHub.x:0.00},{blueHub.y:0.00})");

                bool inBlue = Mathf.Abs(p.x - blueHub.x) < Half && Mathf.Abs(p.z - blueHub.y) < Half;
                bool inRed = Mathf.Abs(p.x - redHub.x) < Half && Mathf.Abs(p.z - redHub.y) < Half;
                if (!inBlue && !inRed) continue;

                bool active = inBlue ? BlueActive : RedActive;
                if (active) { if (inBlue) BlueScore++; else RedScore++; }
                // 吐回場上:朝中立區那一側的底部出口
                Vector2 hub = inBlue ? blueHub : redHub;
                float dir = inBlue ? 1f : -1f;
                f.transform.position = new Vector3(hub.x + dir * 0.78f, 0.25f, hub.y + Random.Range(-0.3f, 0.3f));
                rb.linearVelocity = new Vector3(dir * Random.Range(0.4f, 0.9f), 0f, Random.Range(-0.4f, 0.4f));   // 輕輕吐出,不會滾到老遠
                rb.angularVelocity = Vector3.zero;
            }
        }
    }
}
