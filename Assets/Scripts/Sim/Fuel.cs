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

    public static class FuelManager
    {
        public static readonly List<Fuel> All = new List<Fuel>();
        static Material mat;
        static PhysicsMaterial pm;
        static Transform root;

        public static void Init()
        {
            root = new GameObject("Fuel").transform;
            mat = FieldBuilder.MakeMat(new Color(0.96f, 0.92f, 0.0f));
            mat.enableInstancing = true;
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

        // 起始擺法:中立區約 360 顆 + 每個 DEPOT 24 顆
        public static void SpawnStart()
        {
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
                float dz = W / 2f + (blue ? 2.2f : -2.2f);
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
