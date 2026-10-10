using System.Collections.Generic;
using UnityEngine;

namespace FrcSim
{
    // FUEL(球):剛體小球。直徑 5.91in 是約值(筆記記為「未驗證」,拿到官方手冊再核對)。
    public class Fuel : MonoBehaviour
    {
        public const float Radius = 0.0750f;
        public const float Mass = 0.215f;
        public bool Scored;
        public float IgnoreRobotUntil;
        public Collider RobotCollider;
        public Collider Col;

        Rigidbody rb;

        void Awake() { rb = GetComponent<Rigidbody>(); }

        void Update()
        {
            if (RobotCollider != null && Time.time > IgnoreRobotUntil)
            {
                Physics.IgnoreCollision(Col, RobotCollider, false);
                RobotCollider = null;
            }
        }

        // 滾動阻力:球在地上慢速時快速減速直到停住,避免自己慢慢滾開(PhysX 的球沒有滾動阻力)
        void FixedUpdate()
        {
            if (rb == null) return;
            if (transform.position.y < Radius + 0.06f)
            {
                Vector3 v = rb.linearVelocity;
                float planar = new Vector2(v.x, v.z).magnitude;
                if (planar < 0.5f)
                {
                    float k = planar < 0.12f ? 0.80f : 0.97f;   // 越慢越快停
                    rb.linearVelocity = new Vector3(v.x * k, v.y, v.z * k);
                    rb.angularVelocity *= k;
                }
            }
        }
    }

    // 被吸進車內的球:純視覺,飛向機器人內部後消失
    public class AbsorbAnim : MonoBehaviour
    {
        Transform target; Vector3 local; Vector3 start; float t;
        public void Init(Transform robot, Vector3 localTarget) { target = robot; local = localTarget; start = transform.position; }
        void Update()
        {
            t += Time.deltaTime / 0.18f;
            if (target == null || t >= 1f) { Destroy(gameObject); return; }
            transform.position = Vector3.Lerp(start, target.TransformPoint(local), t * t);
        }
    }

    public static class FuelManager
    {
        public static readonly List<Fuel> All = new List<Fuel>();
        static Material mat;
        public static Material BallMat => mat;
        static PhysicsMaterial pm;
        static Transform root;

        public static void Init()
        {
            root = new GameObject("Fuel").transform;
            mat = FieldBuilder.MakeMat(new Color(0.96f, 0.92f, 0.0f));
            mat.enableInstancing = true;
            mat.SetFloat("_Glossiness", 0.55f);   // 球有塑膠亮面,反光比方塊場地明顯
            pm = new PhysicsMaterial("Fuel") { bounciness = 0.35f, dynamicFriction = 0.6f, staticFriction = 0.7f,
                bounceCombine = PhysicsMaterialCombine.Average };
        }

        public static void Clear()
        {
            All.Clear();
            if (root != null) Object.Destroy(root.gameObject);
            root = null;
        }

        public static Fuel Spawn(Vector3 pos, Vector3 vel, Collider ignoreRobot = null)
        {
            var g = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            g.name = "Fuel";
            g.transform.SetParent(root, false);
            g.transform.position = pos;
            g.transform.localScale = Vector3.one * (Fuel.Radius * 2f);
            g.GetComponent<Renderer>().sharedMaterial = mat;
            var col = g.GetComponent<SphereCollider>();
            col.sharedMaterial = pm;
            var rb = g.AddComponent<Rigidbody>();
            rb.mass = Fuel.Mass;
            rb.linearDamping = 0.375f;   // 空氣阻力(與機器人程式 ShooterCalculator 的 linearDragTimeConstant 一致)
            rb.angularDamping = 0.8f;
            rb.sleepThreshold = 0.25f;
            rb.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;
            rb.linearVelocity = vel;
            var f = g.AddComponent<Fuel>();
            f.Col = col;
            if (ignoreRobot != null)
            {
                Physics.IgnoreCollision(col, ignoreRobot, true);
                f.RobotCollider = ignoreRobot;
                f.IgnoreRobotUntil = Time.time + 0.35f;
            }
            All.Add(f);
            return f;
        }

        public static void Remove(Fuel f)
        {
            All.Remove(f);
            Object.Destroy(f.gameObject);
        }

        // 用官方模型裡的預擺球位置(場地 CAD 的起始擺法)取代格狀擺法
        public static void ReplaceStartLayout()
        {
            foreach (var f in new List<Fuel>(All)) if (f != null) Object.Destroy(f.gameObject);
            All.Clear();
            SpawnStart();
        }

        // 起始擺法:優先用官方模型的預擺球位置;沒有模型才用格狀(中立區約 360 顆 + 每個 DEPOT 24 顆)
        public static void SpawnStart()
        {
            if (FieldModel.FuelPositions != null && FieldModel.FuelPositions.Count > 100)
            {
                // 模型裡 HUB / OUTPOST 內部也有裝飾用的球(高處或在 HUB 腳印內),不是場上起始球,跳過,否則一開場就被算進 HUB 得分
                float Lf = SimConstants.FieldLength, Wf = SimConstants.FieldWidth;
                var hubs = new[] { new Vector2(4.62f, Wf / 2f), new Vector2(Lf - 4.62f, Wf / 2f) };
                int kept = 0;
                foreach (var p in FieldModel.FuelPositions)
                {
                    if (p.y > 0.45f) continue;
                    bool inHub = false;
                    foreach (var h in hubs) if (Mathf.Abs(p.x - h.x) < 0.95f && Mathf.Abs(p.z - h.y) < 0.95f) inHub = true;
                    if (inHub) continue;
                    Spawn(new Vector3(p.x, Mathf.Max(p.y, Fuel.Radius) + 0.001f, p.z), Vector3.zero);
                    kept++;
                }
                Debug.Log($"[Fuel] official layout: {kept} of {FieldModel.FuelPositions.Count} staged pieces kept");
                return;
            }
            float L = SimConstants.FieldLength, W = SimConstants.FieldWidth;
            // 中立區堆(約 206in x 72in)
            // 球與球之間至少留 0.16m(直徑 0.15),不重疊就不會被物理引擎彈開亂滾
            int nx = 30, nz = 11;
            float sx = 0.175f, sz = 0.16f;
            var rng = new System.Random(7);
            for (int i = 0; i < nx; i++)
                for (int j = 0; j < nz; j++)
                {
                    float x = L / 2f - (nx - 1) * sx / 2f + i * sx + (float)(rng.NextDouble() - 0.5) * 0.004f;
                    float z = W / 2f - (nz - 1) * sz / 2f + j * sz + (float)(rng.NextDouble() - 0.5) * 0.004f;
                    Spawn(new Vector3(x, Fuel.Radius + 0.002f, z), Vector3.zero);
                }
            // DEPOT:沿聯盟牆,6 顆寬 x 4 顆深 = 24,間距 0.16 不重疊
            foreach (bool blue in new[] { true, false })
            {
                float dz = blue ? 5.965f : W - 5.965f;   // 官方圖面 FE-2026:DEPOT 中心離計分台側牆 234.85in
                for (int i = 0; i < 4; i++)
                    for (int j = 0; j < 6; j++)
                    {
                        float depth = 0.09f + i * 0.16f;                 // 離聯盟牆的距離
                        float x = blue ? depth : L - depth;
                        Spawn(new Vector3(x, Fuel.Radius + 0.002f, dz + (j - 2.5f) * 0.16f), Vector3.zero);
                    }
            }
        }
    }
}
