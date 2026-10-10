using UnityEngine;

namespace FrcSim
{
    // C 切換:chase(跟隨) / top-down(正上方) / overview(俯瞰全場)。Orbit=true 時是選單背景的緩慢環繞。
    public class CameraRig : MonoBehaviour
    {
        public Transform Target;
        public bool Orbit;
        public int Mode = 1;
        Vector3 velPos;
        float chaseYaw; bool chaseInit;
        public static int ActiveMode; float orbitT; int dirScore; float dirHold, dirHub;

        public string ModeName => Mode == 0 ? "overview" : Mode == 1 ? "chase" : Mode == 7 ? "operator" : Mode == 8 ? "broadcast" : "top-down";

        // ---- 進場動畫(賽車遊戲式):鏡頭從高處繞著車俯衝到追車位、上下黑邊、名牌淡入淡出;測試/截圖參數下略過
        float introT = 99f; const float IntroLen = 4.2f;
        public void StartIntro()
        {
            if (System.Array.IndexOf(System.Environment.GetCommandLineArgs(), "-intro") < 0)
            foreach (var a in System.Environment.GetCommandLineArgs())
                if (a == "-batchmode" || a == "-shot" || a.EndsWith("test") || a == "-leoauto" || a == "-vsai" || a == "-noclock") return;
            introPending = true; introWait = 0f;   // 等「載入程式」的畫面結束(真實程式連上線)才開始放,不然會被載入 UI 擋住
        }
        bool introPending; float introWait;
        void TickIntroPending()
        {
            if (!introPending) return;
            introWait += Time.unscaledDeltaTime;
            bool ready = GameSession.Hal == null || GameSession.Hal.Connected || GameSession.Hal.Failed;
            if (ready && introWait > 0.8f || introWait > 400f) { introPending = false; introT = 0f; chaseInit = false; }
        }
        bool IntroOn => introT < IntroLen;
        GUIStyle gBig, gSmall;
        void OnGUI()
        {
            if (!IntroOn || Orbit) return;
            float t = introT / IntroLen, bar = Screen.height * 0.11f * Mathf.Clamp01(Mathf.Min(t * 6f, (1f - t) * 5f));
            GUI.color = Color.black; GUI.DrawTexture(new Rect(0, 0, Screen.width, bar), Texture2D.whiteTexture); GUI.DrawTexture(new Rect(0, Screen.height - bar, Screen.width, bar), Texture2D.whiteTexture);
            float a = Mathf.Clamp01(Mathf.Min((t - 0.12f) * 8f, (0.85f - t) * 8f));
            if (a > 0f)
            {
                string proj = Prefs.GetString("robotProject", ""); string nm = string.IsNullOrEmpty(proj) ? "FRC 9427" : System.IO.Path.GetFileName(proj.TrimEnd('\\', '/'));
                if (gBig == null) { gBig = new GUIStyle(GUI.skin.label) { fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleLeft }; gSmall = new GUIStyle(GUI.skin.label) { alignment = TextAnchor.MiddleLeft }; }
                gBig.fontSize = Mathf.RoundToInt(Screen.height * 0.07f); gSmall.fontSize = Mathf.RoundToInt(Screen.height * 0.028f);
                float x = Screen.width * 0.07f + (1f - a) * -80f, y = Screen.height * 0.70f;
                GUI.color = new Color(0.25f, 0.55f, 1f, a); GUI.DrawTexture(new Rect(x - 18f, y, 6f, Screen.height * 0.13f), Texture2D.whiteTexture);
                GUI.color = new Color(1f, 1f, 1f, a); GUI.Label(new Rect(x, y - 4f, Screen.width * 0.7f, Screen.height * 0.08f), nm, gBig);
                GUI.color = new Color(1f, 1f, 1f, a * 0.75f); GUI.Label(new Rect(x, y + Screen.height * 0.08f, Screen.width * 0.7f, Screen.height * 0.05f), Loc.Lang == "en" ? "REBUILT 2026  ·  Match starting" : "REBUILT 2026  ·  比賽即將開始", gSmall);
            }
            GUI.color = Color.white;
        }

        void Update()
        {
            TickIntroPending();
            if (IntroOn) introT += Mathf.Min(Time.unscaledDeltaTime, 0.033f);   // 載入卡頓的大幀不能把進場動畫一次吃掉
            if (!Orbit && !MenuSystem.Blocking && Input.GetKeyDown(KeyCode.C)) Mode = Mode == 1 ? 2 : Mode == 2 ? 0 : Mode == 0 ? 7 : Mode == 7 ? 8 : 1;
        }

        void LateUpdate()
        {
            float L = SimConstants.FieldLength, W = SimConstants.FieldWidth;
            if (Orbit || Target == null)
            {
                orbitT += Time.unscaledDeltaTime * 0.12f;
                Vector3 c = new Vector3(L / 2f, 0.5f, W / 2f);
                Vector3 p = c + new Vector3(Mathf.Cos(orbitT) * 13f, 6.5f, Mathf.Sin(orbitT) * 8.5f);
                transform.position = p;
                transform.rotation = Quaternion.LookRotation(c - p, Vector3.up);
                return;
            }

            ActiveMode = Mode;
            Vector3 pos; Quaternion rot;
            if (Mode == 0)
            {
                pos = new Vector3(L / 2f, 14f, W / 2f - 3.5f);
                rot = Quaternion.LookRotation(new Vector3(L / 2f, 0f, W / 2f) - pos, Vector3.up);
            }
            else if (Mode == 1)
            {
                // 車尾上方、跟著車頭轉(航向平滑,原地快速旋轉時鏡頭不會亂甩);從上往下看,不會被場邊低矮方塊擋住車子
                float targetYaw = Target.eulerAngles.y + 90f;   // 機器人往前 = transform.right
                chaseYaw = chaseInit ? Mathf.LerpAngle(chaseYaw, targetYaw, 1f - Mathf.Exp(-2.5f * Time.unscaledDeltaTime)) : targetYaw;
                chaseInit = true;
                Vector3 fwdC = Quaternion.Euler(0f, chaseYaw, 0f) * Vector3.forward;
                pos = Target.position - fwdC * 2.1f + Vector3.up * 3.3f;
                // 鏡頭碰撞:車和鏡頭之間有場地方塊/牆時,把鏡頭拉近到擋住物的前面(再往上抬一點),車才不會被擋住
                Vector3 org = Target.position + Vector3.up * 0.6f;
                Vector3 toCam = pos - org; float dCam = toCam.magnitude;
                var hits = Physics.SphereCastAll(org, 0.2f, toCam / dCam, dCam, ~0, QueryTriggerInteraction.Ignore);
                float nearest = dCam;
                foreach (var h in hits)
                {
                    if (h.collider.transform.IsChildOf(Target) || h.collider.GetComponentInParent<Fuel>() != null || h.collider.GetComponentInParent<SwerveDrive>() != null) continue;
                    if (h.distance > 0.05f) nearest = Mathf.Min(nearest, h.distance);
                }
                if (nearest < dCam) pos = org + toCam / dCam * Mathf.Max(0.9f, nearest - 0.15f) + Vector3.up * 0.8f;
                rot = Quaternion.LookRotation(Target.position + fwdC * 0.9f - pos, Vector3.up);
            }
            else if (Mode == 7)
            {
                // 操作者視角:站在己方聯盟牆後的駕駛台,抬高一點看向場中央(真實比賽駕駛員的位置)
                bool nearBlue = Target.position.x < L / 2f;
                pos = new Vector3(nearBlue ? -2.4f : L + 2.4f, 3.5f, Mathf.Clamp(Target.position.z, 1.5f, W - 1.5f) * 0.6f + W * 0.2f);
                Vector3 look = Vector3.Lerp(new Vector3(L / 2f, 0.6f, W / 2f), Target.position, 0.45f);
                rot = Quaternion.LookRotation(look - pos, Vector3.up);
            }
            else if (Mode == 8)
            {
                // 導播視角:自動切鏡。進球 -> 籃框特寫;平時在側面高機位 / 追車低機位 / 全景輪流,每 7 秒換鏡
                int sc = ScoreManager.BlueScore + ScoreManager.RedScore;
                if (sc != dirScore) { dirScore = sc; dirHold = Time.unscaledTime + 3.5f; dirHub = Target.position.x < L / 2f ? 4.62f : L - 4.62f; if (ScoreManager.RedScore != 0 || ScoreManager.BlueScore != 0) dirHub = (Random.value < 0.5f) ? 4.62f : L - 4.62f; }
                if (Time.unscaledTime < dirHold)
                {
                    float k = 1f - Mathf.Clamp01((dirHold - Time.unscaledTime) / 3.5f); float dd = Mathf.Lerp(6.5f, 3.2f, k); pos = new Vector3(dirHub + (dirHub < L / 2f ? dd : -dd), Mathf.Lerp(4f, 2.6f, k), W / 2f - Mathf.Lerp(6.5f, 3.5f, k));
                    rot = Quaternion.LookRotation(new Vector3(dirHub, 1.9f, W / 2f) - pos, Vector3.up);
                }
                else
                {
                    int shot = (int)(Time.unscaledTime / 7f) % 3;
                    if (shot == 0) { pos = new Vector3(Mathf.Lerp(L * 0.3f, L * 0.7f, Mathf.PingPong(Time.unscaledTime * 0.05f, 1f)), 5.5f, -1.2f); rot = Quaternion.LookRotation(new Vector3(Target.position.x, 0.5f, W / 2f) - pos, Vector3.up); }
                    else if (shot == 1) { pos = Target.position + new Vector3(-1.8f, 2.8f, -3.6f); rot = Quaternion.LookRotation(Target.position + Vector3.up * 0.4f - pos, Vector3.up); }
                    else { pos = new Vector3(L / 2f + Mathf.Sin(Time.unscaledTime * 0.25f) * 4f, 9f - Mathf.PingPong(Time.unscaledTime * 0.3f, 1.5f), W / 2f - 8f); rot = Quaternion.LookRotation(new Vector3(L / 2f, 0f, W / 2f) - pos, Vector3.up); }
                }
            }            else if (Mode == 9)
            {
                pos = new Vector3(L / 2f - 7f, 2.5f, W / 2f - 5f);   // 除錯:看場地上方的計分板與桁架(-camera 9)
                rot = Quaternion.LookRotation(new Vector3(L / 2f, 7.2f, W / 2f) - pos, Vector3.up);
            }
            else if (Mode == 6)
            {
                // 觀眾席視角(-camera 6):站在場內低處看對面看台,檢查觀眾外觀
                pos = new Vector3(L * 0.5f, 1.7f, 0.8f);
                rot = Quaternion.LookRotation(new Vector3(L * 0.25f, 3.2f, W + 3f) - pos, Vector3.up);
            }
            else if (Mode == 5)
            {
                pos = Target.position + new Vector3(0f, 2.4f, 0f);
                rot = Quaternion.LookRotation(Vector3.down, Vector3.forward);
            }
            else if (Mode == 3 || Mode == 4)
            {
                // 除錯用近距離側視(-camera 3)/斜前視(-camera 4),檢查模型貼地與車頭方向
                Vector3 off = Mode == 3 ? new Vector3(0f, 0.5f, -2.6f) : new Vector3(2.0f, 1.2f, -2.0f);
                pos = Target.position + off;
                rot = Quaternion.LookRotation(Target.position + new Vector3(0f, 0.35f, 0f) - pos, Vector3.up);
            }
            else
            {
                // 俯視跟隨機器人,但把鏡頭限制在場地內,靠近邊界時不會露出空白(場地 L×W)
                pos = new Vector3(Mathf.Clamp(Target.position.x, Mathf.Min(5f, L / 2f), Mathf.Max(L - 5f, L / 2f)),
                                  9f,
                                  Mathf.Clamp(Target.position.z, Mathf.Min(3.2f, W / 2f), Mathf.Max(W - 3.2f, W / 2f)));
                rot = Quaternion.LookRotation(Vector3.down, Vector3.forward);
            }
            if (IntroOn)
            {
                float t = introT / IntroLen, ee = t * t * (3f - 2f * t);
                float ang = Mathf.Lerp(200f, 0f, ee) * Mathf.Deg2Rad; Vector3 fw = Target.right; fw.y = 0f; fw.Normalize(); Vector3 rt = Vector3.Cross(Vector3.up, fw);
                Vector3 off = (-fw * Mathf.Cos(ang) + rt * Mathf.Sin(ang)) * Mathf.Lerp(7.5f, 3.2f, ee) + Vector3.up * Mathf.Lerp(5.5f, 2.6f, ee);
                Vector3 ip = Target.position + off; Quaternion ir = Quaternion.LookRotation(Target.position + Vector3.up * 0.4f - ip, Vector3.up);
                transform.position = Vector3.Lerp(ip, pos, ee * ee); transform.rotation = Quaternion.Slerp(ir, rot, ee * ee); velPos = Vector3.zero;
                return;
            }            transform.position = Vector3.SmoothDamp(transform.position, pos, ref velPos, 0.12f, Mathf.Infinity, Time.unscaledDeltaTime);
            transform.rotation = Quaternion.Slerp(transform.rotation, rot, 8f * Time.unscaledDeltaTime);
        }
    }
}
