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
        float orbitT;

        public string ModeName => Mode == 0 ? "overview" : Mode == 1 ? "chase" : "top-down";

        void Update()
        {
            if (!Orbit && !MenuSystem.Blocking && Input.GetKeyDown(KeyCode.C)) Mode = (Mode + 1) % 3;
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
            transform.position = Vector3.SmoothDamp(transform.position, pos, ref velPos, 0.12f, Mathf.Infinity, Time.unscaledDeltaTime);
            transform.rotation = Quaternion.Slerp(transform.rotation, rot, 8f * Time.unscaledDeltaTime);
        }
    }
}
