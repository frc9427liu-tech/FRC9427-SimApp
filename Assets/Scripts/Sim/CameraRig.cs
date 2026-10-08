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
                pos = Target.position + new Vector3(-3.2f, 3.2f, 0f);
                rot = Quaternion.LookRotation(Target.position + new Vector3(1.0f, 0f, 0f) - pos, Vector3.up);
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
