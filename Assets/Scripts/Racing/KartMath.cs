using UnityEngine;

namespace FrcSim
{
    /// 賽車物理的純函式(模組 K,01 §2)。不依賴場景、不依賴 Rigidbody,可以單獨測。
    public static class KartMath
    {
        public const float WallScrapeDeg = 25f;       // 入射角小於此 = 擦牆
        public const float DriftLv1 = 1.0f, DriftLv2 = 2.0f, DriftLv3 = 3.2f;
        public const float DriftMinSteer = 0.35f;     // 鎖定漂移方向所需的最小轉向輸入
        public const float DriftMinSpeed = 9f;        // 鎖定漂移所需的最低車速 m/s
        public const float DriftEndSpeed = 8f;        // 漂移中低於此速度就結束
        public const float HopDriftDelay = 0.15f;     // 按下漂移鍵後,最快這麼久(或落地)才鎖定方向

        /// 轉向曲線:s = sign(x)·|x|^1.4
        public static float SteerCurve(float x)
        {
            float a = Mathf.Abs(x);
            return Mathf.Sign(x) * Mathf.Pow(a, 1.4f);
        }

        /// 轉向率(度/秒,帶號;正 = 右轉)。vF 為沿車頭帶號速度,steerShaped 為過曲線的轉向(-1..1)。
        /// 低速時轉向率縮小(clamp01(|vF|/3));倒車方向反轉。
        public static float YawRate(KartStats st, float vF, float steerShaped)
        {
            float top = Mathf.Max(0.1f, st.TopSpeed);
            float k = Mathf.Clamp01(Mathf.Abs(vF) / top);
            float rate = Mathf.Lerp(st.TurnRateLow, st.TurnRateHigh, k);
            float lowSpeed = Mathf.Clamp01(Mathf.Abs(vF) / 3f);
            float dir = vF < -0.3f ? -1f : 1f;
            return steerShaped * rate * lowSpeed * dir;
        }

        /// 向前加速度:a = min(A0, (目標極速 - vF)/tau)。加速狀態 A0 取至少 35、tau 縮成 20%(迷你渦輪要「噴」得出去:
        /// 24 -> 30 m/s 的差 6 m/s 在約 0.3 s 內補上 80%)。
        public static float Accel(KartStats st, float vF, float targetSpeed, bool boosting)
        {
            float tau = Mathf.Max(0.05f, boosting ? st.AccelTau * 0.2f : st.AccelTau);
            float a0 = boosting ? Mathf.Max(st.Accel, 35f) : st.Accel;
            return Mathf.Min(a0, (targetSpeed - vF) / tau);
        }

        /// 漂移充能等級 0..3(門檻 1.0 / 2.0 / 3.2)。
        public static int DriftLevel(float charge)
        {
            return charge >= DriftLv3 ? 3 : charge >= DriftLv2 ? 2 : charge >= DriftLv1 ? 1 : 0;
        }

        /// 漂移充能速率(每秒):1 + 0.7*steer*dir,夾在 0.3~1.7。
        public static float DriftChargeRate(float steer, int dir)
        {
            return Mathf.Clamp(1f + 0.7f * steer * dir, 0.3f, 1.7f);
        }

        /// 漂移中的轉向率(度/秒,帶號;正 = 右)。dir = 鎖定方向 -1/+1,steer = 目前轉向輸入 -1..1。
        /// 往內壓(steer*dir = 1)105、中立 70、往外推 35;永遠不會變成反方向。
        public static float DriftYawRate(float steer, int dir)
        {
            return dir * (70f + 35f * Mathf.Clamp(steer * dir, -1f, 1f));
        }

        /// 能不能鎖定漂移:|steer| > 0.35 且車速 > 9 m/s。
        public static bool CanStartDrift(float steer, float vF)
        {
            return Mathf.Abs(steer) > DriftMinSteer && vF > DriftMinSpeed;
        }

        /// 側向抓地:vL 以 exp(-G*dt) 衰減。被消掉的側向動能只有 scrub 比例真的變成速度損失,其餘「轉」進車頭方向
        /// (輪胎把側滑速度拉回車頭,不是憑空吃掉速度)。轉彎不會把極速吃掉一成以上,漂移也不會一轉就掉速。
        /// vFCap = 轉進車頭方向的速度上限(不會因為側向衝擊而超過極速)。vF 太小(< 2 m/s)時不轉換,只衰減。
        public static void ApplyGrip(ref float vF, ref float vL, float grip, float dt, float scrub, float vFCap)
        {
            float vL0 = vL;
            vL *= Mathf.Exp(-Mathf.Max(0f, grip) * dt);
            if (vF <= 2f) return;
            float lost = vL0 * vL0 - vL * vL;
            if (lost <= 0f) return;
            float nv = Mathf.Sqrt(vF * vF + (1f - Mathf.Clamp01(scrub)) * lost);
            vF = Mathf.Min(nv, Mathf.Max(vF, vFCap));
        }

        /// 撞牆後車頭要往彈開的速度方向轉多少度(帶號,正 = 右轉)。彈開方向與車頭差超過 100 度(正撞、整個倒著彈)就不轉。
        /// 轉 70%:45 度撞牆後車頭會變成幾乎貼著牆、順著牆滑出去,不會頭朝牆黏住。
        public static float BounceTurnDeg(Vector3 headingH, Vector3 newVelH)
        {
            if (newVelH.sqrMagnitude < 1f || headingH.sqrMagnitude < 1e-6f) return 0f;
            float ang = Vector3.SignedAngle(headingH, newVelH, Vector3.up);
            if (Mathf.Abs(ang) > 100f) return 0f;
            return ang * 0.7f;
        }

        /// 排氣量速度倍率:50 / 100 / 150 / 200cc = 0.55 / 0.75 / 1.0 / 1.3(其他 cc 在相鄰點之間線性內插,範圍外夾住)。
        public static float CcSpeedMul(int cc)
        {
            if (cc <= 50) return 0.55f;
            if (cc <= 100) return Mathf.Lerp(0.55f, 0.75f, (cc - 50) / 50f);
            if (cc <= 150) return Mathf.Lerp(0.75f, 1.0f, (cc - 100) / 50f);
            if (cc <= 200) return Mathf.Lerp(1.0f, 1.3f, (cc - 150) / 50f);
            return 1.3f;
        }

        /// AI 起跑加速機率(AiLevel 0..3 = 簡單 20% / 40% / 60% / 困難 80%)。
        public static float AiStartBoostChance(int aiLevel)
        {
            return Mathf.Lerp(0.2f, 0.8f, Mathf.Clamp(aiLevel, 0, 3) / 3f);
        }

        /// 迷你渦輪持續秒數(Lv1/2/3 = 0.5/1.0/1.6)。
        public static float DriftBoostSeconds(int level)
        {
            return level >= 3 ? 1.6f : level == 2 ? 1.0f : level == 1 ? 0.5f : 0f;
        }

        /// 撞牆回應(水平面上)。vH = 撞前水平速度,nH = 牆面單位法線(水平、指向車這一側)。
        /// 回傳新的水平速度;approach = 沿法線接近速度(0 = 沒有在接近、不處理);scrape = 擦牆(入射角小於 25 度)。
        /// 擦牆:v = vt*0.92;回彈:v = vt*0.6 - n*vn*0.35(vn 為負)。
        public static Vector3 WallResponse(Vector3 vH, Vector3 nH, out float approach, out bool scrape)
        {
            scrape = false; approach = 0f;
            float speed = vH.magnitude;
            float vn = Vector3.Dot(vH, nH);
            if (speed < 0.01f || vn >= -0.05f) return vH;
            approach = -vn;
            float theta = Mathf.Asin(Mathf.Clamp01(approach / speed)) * Mathf.Rad2Deg;
            Vector3 vt = vH - nH * vn;
            if (theta < WallScrapeDeg) { scrape = true; return vt * 0.92f; }
            return vt * 0.6f - nH * vn * 0.35f;
        }

        /// 車對車(沿碰撞法線的一維衝量):closing = 接近速度(大於 0 = 正在靠近)。
        /// 回傳兩車各自被改變的速度大小 dv1、dv2(皆 >= 0,mass1*dv1 = mass2*dv2)。e = 0.6,分離速度至少 3 m/s,重的被推得少。
        /// 分離速度不超過接近速度(sep <= closing),所以碰撞不會增加總動能(慢速碰撞等於 e = 1 的彈性碰撞)。
        public static void KartVsKart(float m1, float m2, float closing, out float dv1, out float dv2)
        {
            KartContact(m1, m2, closing, true, out dv1, out dv2);
        }

        /// bounce = true:一般碰撞(上面那個);false = 無彈性接觸(只消掉接近速度,同一對冷卻期間用,不會穿也不會彈)。
        public static void KartContact(float m1, float m2, float closing, bool bounce, out float dv1, out float dv2)
        {
            dv1 = 0f; dv2 = 0f;
            if (closing <= 0f) return;
            m1 = Mathf.Max(1f, m1); m2 = Mathf.Max(1f, m2);
            float sep = bounce ? Mathf.Min(closing, Mathf.Max(3f, 0.6f * closing)) : 0f;
            float j = (closing + sep) / (1f / m1 + 1f / m2);
            dv1 = j / m1; dv2 = j / m2;
        }

        // ---- 世界式移動技:集氣跳 / 跑牆 / 軌道滑行(純函式,測試可單獨驗)
        public const float ChargeLv1 = 0.5f, ChargeLv2 = 1.1f, ChargeLv3 = 1.8f;   // 集氣秒數門檻(藍 / 橘 / 紫)
        public const float ChargeMinSteer = DriftMinSteer;                           // 集氣只在 |轉向| 不超過此值時開始 / 持續
        public const float WallAttachMaxDeg = 35f;                                   // 與牆面夾角超過此值 = 撞牆(不吸附)
        public const float WallClimbSpeed = 8f, WallClimbDecel = 9f;                 // 夾角達上限時的爬升初速 / 爬升減速

        /// 集氣等級 0..3。
        public static int ChargeLevel(float t)
        {
            return t >= ChargeLv3 ? 3 : t >= ChargeLv2 ? 2 : t >= ChargeLv1 ? 1 : 0;
        }

        /// 集氣跳起跳初速(m/s):Lv1/2/3 = 8 / 10.5 / 13(重力 30 時頂點約 1.1 / 1.8 / 2.8 m)。Lv0 = 0(不跳)。
        public static float ChargeJumpSpeed(int level)
        {
            return level >= 3 ? 13f : level == 2 ? 10.5f : level == 1 ? 8f : 0f;
        }

        /// 跑牆:水平速度方向與牆面(水平法線 nH,指向車這一側)的夾角(度)。0 = 平行,90 = 正撞;背離牆回 -1。
        public static float WallApproachDeg(Vector3 vH, Vector3 nH)
        {
            float sp = vH.magnitude;
            if (sp < 0.01f) return -1f;
            float vn = Vector3.Dot(vH, nH);
            if (vn >= 0f) return -1f;
            return Mathf.Asin(Mathf.Clamp01(-vn / sp)) * Mathf.Rad2Deg;
        }

        /// 吸附得上牆嗎(夾角在 0..35 度)。
        public static bool CanWallAttach(float deg) { return deg >= 0f && deg <= WallAttachMaxDeg; }

        /// 跑牆爬升初速:夾角越大爬越高(夾角 / 35 度 x 8 m/s);近乎平行 = 水平跑。
        public static float WallClimbVy(float deg)
        {
            return Mathf.Clamp01(deg / WallAttachMaxDeg) * WallClimbSpeed;
        }
    }
}
