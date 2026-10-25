using UnityEngine;

namespace FrcSim
{
    // 賽車追尾攝影機(04 擁有)。CameraRig.LateUpdate 在 Mode 10(追尾)/ 11(高空俯瞰)時呼叫 Compute,
    // 直接用回傳的位置/角度(平滑在這裡做完,位置不經 SmoothDamp,避免高速時鏡頭落後),JuiceFX 的震動照常疊在上面。
    // 內容:速度方向/車頭混合追尾、漂移外擺(偏航+橫移+滾轉)、加速 FOV(彈簧回彈)、回望(按鍵 / 倒車自動)、
    //       倒數時的起跑運鏡(繞行俯衝到車後)、終點環繞、防穿牆(排除所有車)+ 離地。
    public static class RaceCamera
    {
        public const float BaseFov = 62f;
        public static float MaxSpeedHint = 24f;   // RaceSession 每幀設成 Player.MaxSpeed

        static bool init; static float yaw; static Transform lastT; static Rigidbody rb; static RaceKart kart;
        static float driftBlend, lookBlend, revBlend, revTimer, fovExtra, fovVel, boostPull, orbitAng;
        static bool wasFinished;
        static readonly RaycastHit[] hitBuf = new RaycastHit[16];

        /// 換目標或重開時呼叫,下一幀直接跳到定位(不從舊位置甩過來)。
        public static void Reset()
        {
            init = false; lastT = null; rb = null; kart = null;
            driftBlend = lookBlend = revBlend = revTimer = fovExtra = fovVel = boostPull = 0f; wasFinished = false;
        }

        public static void Compute(Transform t, int mode, out Vector3 pos, out Quaternion rot)
        {
            if (t == null) { pos = Vector3.zero; rot = Quaternion.identity; return; }
            if (t != lastT) { lastT = t; rb = t.GetComponent<Rigidbody>(); kart = t.GetComponent<RaceKart>(); init = false; }
            float dt = Mathf.Min(Time.unscaledDeltaTime, 0.05f);
            if (MenuSystem.Blocking || Time.timeScale < 0.01f) dt = 0f;
            Vector3 vel = rb != null ? rb.linearVelocity : Vector3.zero; vel.y = 0f;
            float spd = vel.magnitude, k = Mathf.Clamp01(spd / Mathf.Max(1f, MaxSpeedHint));

            if (mode == 11)
            {   // 高空俯瞰:車上方 45 m、後方 20 m,看著車
                pos = t.position + new Vector3(0f, 45f, -20f);
                rot = Quaternion.LookRotation(t.position - pos, Vector3.up);
                JuiceFX.BaseFovOverride = BaseFov;
                return;
            }

            var ph = RaceSession.Phase;
            bool finished = ph == RacePhase.Finished;

            // 航向:速度 > 3 m/s 時追速度方向(和車頭各半),否則追車頭;指數平滑 6/s
            Vector3 f = t.forward; f.y = 0f; if (f.sqrMagnitude < 1e-4f) f = Vector3.forward; f.Normalize();
            Vector3 h = spd > 3f && Vector3.Dot(vel, f) > 0f ? (f + vel / spd).normalized : f;
            float want = Mathf.Atan2(h.x, h.z) * Mathf.Rad2Deg;
            yaw = init ? Mathf.LerpAngle(yaw, want, 1f - Mathf.Exp(-6f * dt)) : want;
            init = true;

            // ---- 漂移外擺 / 回望 / 加速
            float driftWant = kart != null && kart.Drifting ? -kart.DriftDir : 0f;
            driftBlend = Mathf.Lerp(driftBlend, driftWant, 1f - Mathf.Exp(-8f * dt));   // 約 0.3 s 緩入緩出
            bool look = kart != null && kart.Input.LookBack;
            if (kart != null && kart.Speed < -2f) revTimer += dt; else revTimer = 0f;
            float revWant = revTimer > 0.5f ? 1f : 0f;
            revBlend = Mathf.Lerp(revBlend, revWant, 1f - Mathf.Exp(-3f * dt));
            lookBlend = Mathf.MoveTowards(lookBlend, look ? 1f : 0f, dt / 0.1f);
            float back = Mathf.Max(lookBlend, revBlend);

            bool boosting = kart != null && kart.Boosting;
            // FOV 彈簧:目標 = 速度 6° + 加速 10°;欠阻尼讓「噴出去再回彈」
            float fovTarget = 6f * k + (boosting ? 10f : 0f);
            float acc = (fovTarget - fovExtra) * 70f - fovVel * 9f;
            fovVel += acc * dt; fovExtra += fovVel * dt;
            boostPull = Mathf.Lerp(boostPull, boosting ? 0.8f : 0f, 1f - Mathf.Exp(-5f * dt));

            float yawFinal = yaw + driftBlend * 12f + back * 180f;
            Vector3 fw = Quaternion.Euler(0f, yawFinal, 0f) * Vector3.forward;
            Vector3 side = Quaternion.Euler(0f, yawFinal, 0f) * Vector3.right;

            float dist = Mathf.Lerp(4.9f, 6.4f, k) + boostPull;    // 拉近:車在畫面裡更大(MK 式近距追尾)
            Vector3 look3 = t.position + Vector3.up * 0.9f + fw * 4.0f;
            pos = t.position - fw * dist + Vector3.up * 2.2f + side * (driftBlend * 0.6f);

            // ---- 起跑運鏡:倒數期間從側前方高處繞到車後(最後 0.35 的時間才完全貼合)
            if (ph == RacePhase.Countdown && !SuperSample.IsTestRun())
            {
                float u = 1f - Mathf.Clamp01(RaceSession.CountdownLeft / Mathf.Max(0.1f, RaceSession.CountdownLen));
                float e = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(u / 0.85f));
                float a = (1f - e) * 150f, d = Mathf.Lerp(10f, dist, e), hh = Mathf.Lerp(5f, 2.2f, e);
                Vector3 off = Quaternion.Euler(0f, yawFinal + a, 0f) * Vector3.back * d + Vector3.up * hh;
                pos = Vector3.Lerp(t.position + off, pos, e * e);
                look3 = Vector3.Lerp(t.position + Vector3.up * 0.8f, look3, e);
            }

            // ---- 終點:慢速環繞玩家車
            if (finished)
            {
                if (!wasFinished) orbitAng = yawFinal;
                orbitAng += 0.15f * Mathf.Rad2Deg * dt;
                pos = t.position + Quaternion.Euler(0f, orbitAng, 0f) * Vector3.back * 5f + Vector3.up * 2.2f;
                look3 = t.position + Vector3.up * 0.8f;
            }
            wasFinished = finished;

            // ---- 防遮擋:車和鏡頭之間有東西就拉近(排除所有車;不新增 Layer,用元件判斷)
            Vector3 org = t.position + Vector3.up * 1.0f, to = pos - org; float dd = to.magnitude;
            if (dd > 0.01f)
            {
                int n = Physics.SphereCastNonAlloc(org, 0.25f, to / dd, hitBuf, dd, ~0, QueryTriggerInteraction.Ignore);
                float near = dd;
                for (int i = 0; i < n; i++)
                {
                    var col = hitBuf[i].collider;
                    if (col == null || col.transform.IsChildOf(t) || col.GetComponentInParent<RaceKart>() != null) continue;
                    if (hitBuf[i].distance > 0.05f) near = Mathf.Min(near, hitBuf[i].distance);
                }
                if (near < dd) pos = org + to / dd * Mathf.Max(1.2f, near - 0.2f);
            }
            // 離地至少 0.6 m
            if (Physics.Raycast(pos + Vector3.up * 20f, Vector3.down, out var g, 40f, ~0, QueryTriggerInteraction.Ignore) && g.collider.GetComponentInParent<RaceKart>() == null)
                pos.y = Mathf.Max(pos.y, g.point.y + 0.6f);

            rot = Quaternion.LookRotation(look3 - pos, Vector3.up) * Quaternion.Euler(0f, 0f, driftBlend * 2f);
            JuiceFX.BaseFovOverride = BaseFov + fovExtra;
        }
    }
}
