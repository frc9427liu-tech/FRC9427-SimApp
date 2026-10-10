using System.Collections.Generic;
using UnityEngine;

namespace FrcSim
{
    // 模擬比賽的對手 AI(紅方):自己撿球 → 跑到射擊點 → 射進自己這邊的 HUB。難度 0~3 = 簡單 / 普通 / 困難 / 超困難。
    // 只用 Drive.Drive(場地座標速度 + 轉速)與 Mech.IntakeDown / Mech.Shooting 操作,和玩家同一套規則(車速、抓地、HUB 啟動輪替照樣適用)。
    public class Robot2AI : MonoBehaviour
    {
        public SwerveDrive Drive;
        public RobotMechanisms Mech;
        public int Level = 1;

        enum St { Collect, GoShoot, Shoot }
        St st = St.Collect;
        float speed, shootAt, spread, think;
        Vector2 lastPos; float stuckPosT, moved = 9f, escapeT; Vector2 escapeDir;
        float nextThink, stuckT, shootStart, lastShootEnd;
        Vector2 target; bool hasTarget;
        Vector2 hub;
        float aiEnd = 100f; float aiTestT; bool aiTest; float dbgT; System.Text.StringBuilder log;

        static readonly float[] Speed = { 0.45f, 0.65f, 0.85f, 1.0f };
        static readonly int[] ShootAt = { 5, 9, 14, 20 };          // 持球幾顆就去射
        static readonly float[] Spread = { 9f, 4.5f, 1.8f, 0.5f };  // 出球散布(度)
        static readonly float[] FireInt = { 0.45f, 0.30f, 0.22f, 0.16f };   // 發射間隔(秒)
        static readonly float[] CollectRate = { 0.8f, 1.6f, 3.0f, 4.5f };      // 吸球速率上限(顆/秒)
        static readonly float[] Think = { 1.2f, 0.7f, 0.35f, 0.15f }; // 決策反應時間(秒)

        void Start()
        {
            Level = Mathf.Clamp(Level, 0, 3);
            speed = Speed[Level]; shootAt = ShootAt[Level]; spread = Spread[Level]; think = Think[Level];
            Mech.SpreadDeg = spread; Mech.FireInterval = FireInt[Level]; Mech.CollectPerSec = CollectRate[Level];
            hub = Mech.TargetHub;
            aiTest = System.Array.IndexOf(System.Environment.GetCommandLineArgs(), "-aitest") >= 0;
            if (aiTest) { log = new System.Text.StringBuilder(); var ca = System.Environment.GetCommandLineArgs(); int si = System.Array.IndexOf(ca, "-aitestsec"); if (si >= 0 && si + 1 < ca.Length) float.TryParse(ca[si + 1], out aiEnd); }
        }

        static float Wrap(float a) { while (a > Mathf.PI) a -= 2f * Mathf.PI; while (a < -Mathf.PI) a += 2f * Mathf.PI; return a; }

        void Update()
        {
            if (Drive == null || Mech == null) return;
            if (MenuSystem.Blocking || ScoreManager.Ended || (ScoreManager.ClockOn && ScoreManager.MatchTime <= 0.01f))
            { Drive.Drive(0f, 0f, 0f); Mech.Shooting = false; Mech.IntakeDown = false; return; }

            Vector2 p = Drive.Pose2d;
            Vector2 want = Vector2.zero; float wantHeading = Drive.HeadingRad; bool turn = false;
            bool active = !ScoreManager.ClockOn || ScoreManager.RedActive;   // HUB 沒啟動時射了不算分:改成繼續撿球

            if (Time.time >= nextThink)
            {
                nextThink = Time.time + think;
                if (st == St.Collect && Mech.Held >= shootAt && active) st = St.GoShoot;
                else if (st == St.Collect && Mech.Held >= RobotMechanisms.Capacity - 2) st = St.GoShoot;
                if (st == St.Shoot && (Mech.Held <= 0 || !active)) { st = St.Collect; hasTarget = false; lastShootEnd = Time.time; }
                if (st == St.GoShoot && Mech.Held <= 0) { st = St.Collect; hasTarget = false; }
                if (st == St.Collect && (!hasTarget || TargetGone())) PickBall(p);
            }

            switch (st)
            {
                case St.Collect:
                    Mech.Shooting = false; Mech.IntakeDown = true;
                    if (hasTarget)
                    {
                        Vector2 d = Route(p, target) - p;
                        wantHeading = Mathf.Atan2(d.y, d.x); turn = true;
                        float face = Mathf.Abs(Wrap(wantHeading - Drive.HeadingRad));
                        want = d.normalized * speed * (face < 0.7f ? 1f : 0.35f);   // 還沒轉向球時慢一點
                    }
                    break;
                case St.GoShoot:
                    {
                        Mech.Shooting = false; Mech.IntakeDown = false;
                        Vector2 spot = ShootSpot(p);
                        Vector2 d = Route(p, spot) - p;
                        wantHeading = d.magnitude > 2.5f ? Mathf.Atan2(d.y, d.x) : Mathf.Atan2(hub.y - p.y, hub.x - p.x); turn = true;
                        want = (spot - p).magnitude > 0.3f ? d.normalized * speed * Mathf.Clamp01((spot - p).magnitude / 1.2f + 0.25f) : Vector2.zero;
                        if ((spot - p).magnitude < 0.5f && Drive.Speed < 0.35f) { st = St.Shoot; shootStart = Time.time; }
                    }
                    break;
                case St.Shoot:
                    Mech.IntakeDown = false;
                    Mech.Shooting = true;
                    wantHeading = Mathf.Atan2(hub.y - p.y, hub.x - p.x); turn = true;
                    break;
            }

            // 避開兩個 HUB 方柱(把它們當圓形障礙推開)
            float W = SimConstants.FieldWidth, L = SimConstants.FieldLength;
            float hd = SimConstants.AllianceZoneDepth + SimConstants.HubSize / 2f;
            foreach (var h in new[] { new Vector2(hd, W / 2f), new Vector2(L - hd, W / 2f) })
            {
                Vector2 away = p - h; float dist = away.magnitude;
                float R = 1.55f;
                if (dist < R && dist > 0.01f && !(st != St.Collect && h == hub && dist > 1.3f)) want += away.normalized * (R - dist) * 1.6f * speed;
            }
            want = Vector2.ClampMagnitude(want, 1f);

            // 卡住偵測:想動卻幾乎沒動 1.2 秒 → 往旁邊閃一下並重選目標
            stuckPosT += Time.deltaTime;
            if (stuckPosT >= 1.5f) { moved = (p - lastPos).magnitude; lastPos = p; stuckPosT = 0f; }
            if (want.magnitude > 0.2f && moved < 0.35f && st != St.Shoot) stuckT += Time.deltaTime; else if (st == St.Shoot || moved >= 0.35f) stuckT = 0f;
            if (stuckT > 1.2f) { stuckT = 0f; hasTarget = false; escapeT = 1.4f; escapeDir = (new Vector2(SimConstants.FieldLength / 2f + 1.5f, SimConstants.FieldWidth / 2f + (Random.value < 0.5f ? 1.7f : -1.7f)) - p).normalized; }
            if (escapeT > 0f) { escapeT -= Time.deltaTime; want = escapeDir; }

            float rot = turn ? Mathf.Clamp(Wrap(wantHeading - Drive.HeadingRad) * 3.0f, -1f, 1f) * Mathf.Lerp(0.6f, 1f, speed) : 0f;
            // 場地座標移動:Drive.Drive(fwd=+x, strafe=+z?) → 與 Robot2Input 一樣的軸向(fwd 沿場地 x、strafe 往 z 的反向,FieldCentric)
            Drive.Drive(want.x, want.y, rot);

            if (aiTest)
            {
                aiTestT += Time.deltaTime;
                if (aiTestT > Mathf.Floor(aiTestT - Time.deltaTime) && Mathf.Floor(aiTestT) != Mathf.Floor(aiTestT - Time.deltaTime))
                    log.AppendLine($"t={aiTestT:0} st={st} pos=({p.x:0.0},{p.y:0.0}) hd={Drive.HeadingRad * Mathf.Rad2Deg:0} held={Mech.Held} shots={Mech.ShotsFired} red={ScoreManager.RedScore} blue={ScoreManager.BlueScore} spd={Drive.Speed:0.0} tgt=({target.x:0.0},{target.y:0.0}){(hasTarget ? "" : "!")} want=({want.x:0.0},{want.y:0.0}) esc={escapeT:0.0}");
                if (aiTestT > aiEnd)
                {
                    System.IO.File.WriteAllText(System.IO.Path.Combine(System.IO.Path.GetDirectoryName(Application.dataPath), "aitest-L" + Level + ".txt"), log.ToString());
                    Application.Quit();
                }
            }
        }

        // 穿越紅方 HUB 前後(x 方向)時走 BUMP 車道(z = 場寬中線 ± 1.7),不貼牆撞 TRENCH 立柱
        Vector2 Route(Vector2 p, Vector2 goal)
        {
            float hx = hub.x, W = SimConstants.FieldWidth;
            float sp = Mathf.Sign(p.x - hx), sg = Mathf.Sign(goal.x - hx);
            bool inBand = Mathf.Abs(p.x - hx) < 1.9f;
            bool crossing = (sp != sg && Mathf.Abs(goal.x - hx) > 0.2f) || (inBand && Mathf.Abs(goal.x - hx) > 1.9f);
            if (!crossing) return goal;
            float lane = W / 2f + (p.y >= W / 2f ? 1.7f : -1.7f);
            if (!inBand)   // 先走到帶外側的車道入口
            {
                Vector2 a = new Vector2(hx + sp * 2.1f, lane);
                if ((a - p).magnitude > 0.6f && Mathf.Abs(p.y - lane) > 0.5f) return a;
            }
            if (inBand && Mathf.Abs(p.y - lane) > 0.45f) return new Vector2(p.x + 0.5f * sp, lane);   // 在帶內:先橫移到車道再前進(避免貼著 HUB 角)
            return new Vector2(hx + (inBand ? Mathf.Sign(goal.x - hx) : -sp) * 2.3f, lane);
        }

        bool TargetGone()
        {
            foreach (var f in FuelManager.All)
                if (f != null && ((Vector2)new Vector2(f.transform.position.x, f.transform.position.z) - target).sqrMagnitude < 0.04f) return false;
            return true;
        }

        void PickBall(Vector2 p)
        {
            float best = 1e9f; hasTarget = false;
            float L = SimConstants.FieldLength, W = SimConstants.FieldWidth;
            float hd = SimConstants.AllianceZoneDepth + SimConstants.HubSize / 2f;
            var hubs = new[] { new Vector2(hd, W / 2f), new Vector2(L - hd, W / 2f) };
            foreach (var f in FuelManager.All)
            {
                if (f == null) continue;
                var q = new Vector2(f.transform.position.x, f.transform.position.z);
                if (f.transform.position.y > 0.35f || f.transform.position.x < 6.5f || f.transform.position.x > hub.x + 0.8f) continue;                    // 還在空中/架上
                bool nearHub = false; foreach (var h in hubs) if ((q - h).magnitude < 1.5f) nearHub = true;
                if (nearHub) continue;                                            // HUB 腳下不去撿(會卡住)
                if (q.x < 0.5f || q.x > L - 0.5f || q.y < 0.5f || q.y > W - 0.5f) continue;   // 貼牆的不撿
                float d = (q - p).magnitude + (q.x < L / 2f ? 2.5f : 0f);       // 偏好自己這半邊與中立區
                if (d < best) { best = d; target = q; hasTarget = true; }
            }
        }

        Vector2 ShootSpot(Vector2 p)
        {
            // 離自己 HUB 約 2.8m、在己方聯盟區那一側;依目前位置選靠近的角度(±40°)
            Vector2 toMe = p - hub;
            float ang = Mathf.Atan2(toMe.y, toMe.x);   // 從 HUB 看過去的方位
            float baseAng = 0f;                         // 真實規則:只能從己方聯盟區射(紅方 HUB 靠右牆那一側 = +x),中立區那面有球網擋板
            float delta = Mathf.Clamp(Wrap(ang - baseAng), -0.7f, 0.7f);
            float a = baseAng + delta;
            return hub + new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * 2.8f;
        }
    }
}
