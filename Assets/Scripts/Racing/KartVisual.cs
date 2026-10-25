using UnityEngine;

namespace FrcSim
{
    // 車的外觀動畫(模組 P)。輪子依車速轉動、前輪依轉向偏 ±25°;
    // 車手(KartModels 生成的 Driver):身體與頭隨轉向微微傾斜/搖擺,方向盤跟著轉。
    // 掛在 Visual 物件上,由 RaceSession 生成時 AddComponent 並設定 Kart。
    public class KartVisual : MonoBehaviour
    {
        public RaceKart Kart;
        public float WheelRadius = RaceArt.WheelRadius;
        Transform fl, fr, rl, rr, torso, head, steerWheel, body, driver;
        Vector3 bodyBase, driverBase;
        float bobY;
        Quaternion steerBase = Quaternion.identity;
        float spin, steerDeg, lean, bobT;
        // 使用道具閃爍(Flicker):倒數期間以 14 Hz 開關 Renderer;只動「閃爍開始時本來就開著」的,結束 / 停用時還原
        float flashLeft;
        Renderer[] flashRs;
        bool[] flashWas;
        bool flashHidden, spinning;

        /// 車身短暫閃爍 seconds 秒(重複呼叫取較長者)。
        public void Flicker(float seconds)
        {
            if (seconds <= 0f) return;
            if (flashLeft <= 0f)
            {
                flashRs = GetComponentsInChildren<Renderer>(true);
                flashWas = new bool[flashRs.Length];
                for (int i = 0; i < flashRs.Length; i++)
                {
                    var r = flashRs[i];
                    flashWas[i] = r != null && r.enabled && !(r is ParticleSystemRenderer) && !(r is LineRenderer) && !(r is TrailRenderer);
                }
            }
            flashLeft = Mathf.Max(flashLeft, seconds);
        }

        void SetFlash(bool hidden)
        {
            flashHidden = hidden;
            if (flashRs == null) return;
            for (int i = 0; i < flashRs.Length; i++) if (flashRs[i] != null && flashWas[i]) flashRs[i].enabled = !hidden;
        }

        void OnDisable() { flashLeft = 0f; if (flashHidden) SetFlash(false); if (blobT != null) blobT.gameObject.SetActive(false); }
        void OnEnable() { if (blobT != null) blobT.gameObject.SetActive(true); }
        void OnDestroy() { if (blobT != null) Destroy(blobT.gameObject); }

        // ---- 車底接觸陰影(柔邊黑影貼在地面,離地時變淡變小);真正的方向光陰影另外由太陽燈處理
        static Mesh blobMesh; static readonly RaycastHit[] blobHits = new RaycastHit[8];
        Transform blobT; MeshRenderer blobR; float blobK = 1f;
        void UpdateBlob(float dt)
        {
            if (blobT == null)
            {
                var bm = RaceShaders.Blob();
                if (bm == null) return;
                if (blobMesh == null)
                {
                    blobMesh = new Mesh { name = "KartBlobQuad" };
                    blobMesh.vertices = new[] { new Vector3(-0.5f, 0, -0.5f), new Vector3(0.5f, 0, -0.5f), new Vector3(0.5f, 0, 0.5f), new Vector3(-0.5f, 0, 0.5f) };
                    blobMesh.uv = new[] { new Vector2(0, 0), new Vector2(1, 0), new Vector2(1, 1), new Vector2(0, 1) };
                    var c32 = new Color32(255, 255, 255, 215);
                    blobMesh.colors32 = new[] { c32, c32, c32, c32 };
                    blobMesh.triangles = new[] { 0, 2, 1, 0, 3, 2 };
                    blobMesh.RecalculateBounds();
                }
                var g = new GameObject("KartBlob");
                g.AddComponent<MeshFilter>().sharedMesh = blobMesh;
                blobR = g.AddComponent<MeshRenderer>();
                blobR.sharedMaterial = bm; blobR.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off; blobR.receiveShadows = false;
                blobT = g.transform;
            }
            Vector3 o = transform.position + Vector3.up * 0.8f;
            RaycastHit h = default(RaycastHit);
            int nh = Physics.RaycastNonAlloc(o, Vector3.down, blobHits, 4f, ~0, QueryTriggerInteraction.Ignore);
            float best = 1e9f; bool found = false;
            for (int q = 0; q < nh; q++)
            {
                var hq = blobHits[q];
                if (hq.rigidbody != null || hq.collider == null || hq.collider.transform.IsChildOf(transform.parent != null ? transform.parent : transform)) continue;
                if (hq.distance < best) { best = hq.distance; h = hq; found = true; }
            }
            if (found)
            {
                float k = Mathf.Clamp01(1f - (h.distance - 0.8f) / 2.5f);
                blobK = Mathf.Lerp(blobK, k, 1f - Mathf.Exp(-14f * dt));
                blobT.position = new Vector3(h.point.x, Mathf.Max(h.point.y, transform.position.y - 0.02f), h.point.z) + h.normal * 0.07f;
                blobT.rotation = Quaternion.FromToRotation(Vector3.up, h.normal) * Quaternion.Euler(0f, transform.eulerAngles.y, 0f);
                blobT.localScale = new Vector3(2.0f, 1f, 2.7f) * (0.8f + 0.2f * blobK);
                blobR.enabled = blobK > 0.03f;
            }
            else blobR.enabled = false;
        }

        void Awake()
        {
            fl = transform.Find("WheelFL"); fr = transform.Find("WheelFR");
            rl = transform.Find("WheelRL"); rr = transform.Find("WheelRR");
            torso = transform.Find("Driver/Torso"); head = transform.Find("Driver/Torso/Head"); steerWheel = transform.Find("Driver/SteeringWheel");
            body = transform.Find("Body"); driver = transform.Find("Driver");
            if (body != null) bodyBase = body.localPosition;
            if (driver != null) driverBase = driver.localPosition;
            if (steerWheel != null) steerBase = steerWheel.localRotation;
            // 每款車輪徑不同:用輪子網格的實際半徑,轉速才對得上車速
            if (fl != null)
            {
                var mf = fl.GetComponent<MeshFilter>();
                if (mf != null && mf.sharedMesh != null && mf.sharedMesh.bounds.extents.y > 0.05f) WheelRadius = mf.sharedMesh.bounds.extents.y;
            }
        }

        void LateUpdate()
        {
            if (Kart == null) return;
            float dt = Time.deltaTime;
            UpdateBlob(dt);
            // 被打中(暈眩 / 香蕉皮)時車身原地打轉 2 圈(視覺,不影響物理位置);結束時剛好回到 0 度
            if (Kart.StunRemaining > 0f && Kart.StunTotal > 0.01f)
            {
                float u = Mathf.Clamp01(1f - Kart.StunRemaining / Kart.StunTotal);
                transform.localRotation = Quaternion.Euler(0f, 720f * (1f - (1f - u) * (1f - u)), 0f);
                spinning = true;
            }
            else if (spinning) { transform.localRotation = Quaternion.identity; spinning = false; }
            if (flashLeft > 0f)
            {
                flashLeft -= dt;
                bool hide = flashLeft > 0f && ((int)(flashLeft * 28f) & 1) == 1;   // 每秒切換 28 次 = 14 Hz 閃
                if (hide != flashHidden) SetFlash(hide);
            }
            else if (flashHidden) SetFlash(false);
            spin += Kart.Speed / Mathf.Max(0.05f, WheelRadius) * Mathf.Rad2Deg * dt;
            if (spin > 100000f || spin < -100000f) spin %= 360f;
            float st = Mathf.Clamp(Kart.Input.Steer, -1f, 1f);
            float want = st * 30f;
            steerDeg = Mathf.MoveTowards(steerDeg, want, 240f * dt);
            Set(fl, steerDeg); Set(fr, steerDeg); Set(rl, 0f); Set(rr, 0f);

            // 車手動畫:速度越快傾斜越明顯;身體與頭朝轉彎方向傾,頭多轉一點(看向彎道),方向盤跟著轉
            float speedK = Mathf.Clamp01(Mathf.Abs(Kart.Speed) / 12f);
            float leanT = st * speedK + (Kart.Drifting ? Kart.DriftDir * 0.55f : 0f);
            lean = Mathf.Lerp(lean, Mathf.Clamp(leanT, -1.3f, 1.3f), 1f - Mathf.Exp(-9f * dt));
            bobT += dt * (2f + Mathf.Abs(Kart.Speed) * 0.35f);
            if (torso != null) torso.localRotation = Quaternion.Euler(0f, 0f, -lean * 7f);
            if (head != null) head.localRotation = Quaternion.Euler(Mathf.Sin(bobT) * 1.5f * speedK, lean * 16f, -lean * 9f);
            // 小彈跳:車身與車手隨速度上下輕顫(離地時不顫),駕駛身體再多一點點起伏
            float bobTarget = Kart.Grounded ? Mathf.Sin(bobT * 4.3f) * 0.012f * speedK + Mathf.Sin(bobT * 9.1f) * 0.005f * speedK : 0f;
            bobY = Mathf.Lerp(bobY, bobTarget, 1f - Mathf.Exp(-18f * dt));
            if (body != null) body.localPosition = bodyBase + new Vector3(0f, bobY, 0f);
            if (driver != null) driver.localPosition = driverBase + new Vector3(lean * 0.015f, bobY * 1.6f, 0f);
            if (steerWheel != null) steerWheel.localRotation = steerBase * Quaternion.Euler(0f, 0f, -steerDeg * 2.2f);
        }

        void Set(Transform w, float steer)
        {
            if (w != null) w.localRotation = Quaternion.Euler(0f, steer, 0f) * Quaternion.Euler(spin, 0f, 0f);
        }
    }
}
