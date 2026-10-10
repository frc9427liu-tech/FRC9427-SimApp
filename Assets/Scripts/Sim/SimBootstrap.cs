using UnityEngine;

namespace FrcSim
{
    // 進入場景時:套用設定、建場地/燈光/相機,然後交給選單系統(啟動畫面→語言→主畫面)。
    public static class SimBootstrap
    {
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Init()
        {
            SettingsStore.Apply();
            Time.fixedDeltaTime = 1f / 60f; Time.maximumDeltaTime = 0.05f;   // 卡頓後最多補 5 步,避免越卡越補   // 物理 60Hz(原本 100Hz 太吃 CPU;機器人程式本身的 20ms 週期由 MechSim 累計)
            Physics.defaultSolverIterations = 8;
            Physics.defaultSolverVelocityIterations = 2;

            new GameObject("UpdateCheck").AddComponent<UpdateCheck>();
            FieldBuilder.Build();
            if (System.Array.IndexOf(System.Environment.GetCommandLineArgs(), "-noarena") < 0) ArenaBuilder.Build();   // 場館環境(看台/後牆/天花板燈)
            if (System.Array.IndexOf(System.Environment.GetCommandLineArgs(), "-boxfield") < 0) FieldModel.Load();   // 官方場地模型(-boxfield 可退回方塊外觀)

            var light = new GameObject("Sun").AddComponent<Light>();
            light.transform.rotation = Quaternion.Euler(52f, -35f, 0f);
            Look.SetupLighting(light);   // 太陽/三色環境光/霧(數值見 Look.cs)

            var camObj = Camera.main != null ? Camera.main.gameObject : new GameObject("Main Camera", typeof(Camera), typeof(AudioListener));
            camObj.tag = "MainCamera";
            var cam = camObj.GetComponent<Camera>();
            cam.backgroundColor = new Color(0.045f, 0.06f, 0.10f);
            cam.allowHDR = true;
            cam.allowMSAA = true;
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.nearClipPlane = 0.1f;
            cam.farClipPlane = 80f;
            camObj.AddComponent<PostFX>();   // 先加 PostFX,SuperSample 重建時 PostFX.Active 才會是 true
            camObj.AddComponent<SuperSample>();
            var rig = camObj.AddComponent<CameraRig>();
            rig.Orbit = true;
            Juice.Boot(camObj);
            new GameObject("Commentary").AddComponent<Commentary>();
                new GameObject("PerfOverlay").AddComponent<PerfOverlay>();   // F3:幀時間圖與 1% low   // 音效/粒子/鏡頭回饋(Juice 套件)

            bool selfTest = System.Array.IndexOf(System.Environment.GetCommandLineArgs(), "-selftest") >= 0;
            if (selfTest)
            {
                // 自動測試:略過選單,直接開始並由 SelfTest 控制
                GameSession.Begin(rig, true);
                return;
            }

            {
                var a = System.Environment.GetCommandLineArgs();
                int ri = System.Array.IndexOf(a, "-realtest");
                if (ri >= 0 && ri + 1 < a.Length)
                {
                    GameSession.Begin(rig, false, a[ri + 1]);
                    new GameObject("RealTest").AddComponent<RealTest>();
                    return;
                }
                if (System.Array.IndexOf(a, "-installtest") >= 0) { new GameObject("InstallTest").AddComponent<InstallTest>(); return; }
                { int mi = System.Array.IndexOf(a, "-model"); if (mi >= 0 && mi + 1 < a.Length) { Prefs.SetString("robotModel", a[mi + 1]); PlayerPrefs.DeleteKey("modelYaw"); } }
                { int li0 = System.Array.IndexOf(a, "-lang"); if (li0 >= 0 && li0 + 1 < a.Length) Loc.Lang = a[li0 + 1]; }
                { int my = System.Array.IndexOf(a, "-modelyaw"); if (my >= 0 && my + 1 < a.Length) PlayerPrefs.SetFloat("modelYaw", float.Parse(a[my + 1])); }
                int pti = System.Array.IndexOf(a, "-projtest");
                if (pti >= 0 && pti + 1 < a.Length) { GameSession.Begin(rig, false, a[pti + 1]); new GameObject("ProjTest").AddComponent<ProjTest>(); return; }
                int bi2 = System.Array.IndexOf(a, "-bindtest");
                if (bi2 >= 0 && bi2 + 1 < a.Length)
                {
                    Prefs.SetInt("tankMode", 1);
                    GameSession.Begin(rig, false, a[bi2 + 1]);
                    new GameObject("BindTest").AddComponent<BindTest>();
                    return;
                }
                int li = System.Array.IndexOf(a, "-leotest");
                if (li >= 0 && li + 1 < a.Length)
                {
                    Prefs.SetInt("tankMode", 1);
                    GameSession.Begin(rig, false, a[li + 1]);
                    new GameObject("LeoTest").AddComponent<LeoTest>();
                    return;
                }
                int la = System.Array.IndexOf(a, "-leoauto");
                if (la >= 0 && la + 1 < a.Length)
                {
                    Prefs.SetInt("tankMode", 1);
                    GameSession.Begin(rig, false, a[la + 1]);
                    new GameObject("LeoAuto").AddComponent<LeoAuto>();
                    return;
                }
                int hi = System.Array.IndexOf(a, "-halsimtest");
                if (hi >= 0 && hi + 1 < a.Length)
                {
                    new GameObject("HalSimTest").AddComponent<HalSimTest>().Project = a[hi + 1];
                    return;
                }
            }

            if (System.Array.IndexOf(System.Environment.GetCommandLineArgs(), "-soaktest") >= 0)
            {
                GameSession.Begin(rig);
                new GameObject("SoakTest").AddComponent<SoakTest>();
                return;
            }

            if (System.Array.IndexOf(System.Environment.GetCommandLineArgs(), "-walltest") >= 0)
            {
                GameSession.Begin(rig);
                new GameObject("WallTest").AddComponent<WallTest>();
                return;
            }

            if (System.Array.IndexOf(System.Environment.GetCommandLineArgs(), "-balltest") >= 0)
            {
                GameSession.Begin(rig);
                new GameObject("BallTest").AddComponent<BallTest>();
                return;
            }

            if (System.Array.IndexOf(System.Environment.GetCommandLineArgs(), "-autostart") >= 0)
            {
                GameSession.Begin(rig);
                var shot = new GameObject("AutoShot").AddComponent<AutoShot>();
                shot.Rig = rig;
                return;
            }

            if (System.Array.IndexOf(System.Environment.GetCommandLineArgs(), "-fieldtest") >= 0)
            {
                GameSession.Begin(rig);
                new GameObject("FieldTest").AddComponent<FieldTest>();
                return;
            }

            if (System.Array.IndexOf(System.Environment.GetCommandLineArgs(), "-padtest") >= 0)
            {
                GameSession.Begin(rig);
                new GameObject("PadTest").AddComponent<PadTest>();
                return;
            }

            if (System.Array.IndexOf(System.Environment.GetCommandLineArgs(), "-settletest") >= 0)
            {
                GameSession.Begin(rig);
                new GameObject("SettleTest").AddComponent<SettleTest>();
                return;
            }

            if (System.Array.IndexOf(System.Environment.GetCommandLineArgs(), "-shot") >= 0)
                new GameObject("AutoShot").AddComponent<AutoShot>().Rig = rig;
            var menu = new GameObject("Menu").AddComponent<MenuSystem>();
            menu.Rig = rig;
        }

        public static GameObject BuildRobot(out Transform turretVis, out Transform armVis, string name = "Robot", Color? bodyColor = null)
        {
            var r = new GameObject(name);
            r.AddComponent<Rigidbody>();
            var col = r.AddComponent<BoxCollider>();
            col.size = new Vector3(SimConstants.BumperLength, SimConstants.BumperHeight, SimConstants.BumperWidth);
            r.AddComponent<SwerveDrive>();
            // 車身高度由 SwerveDrive 依 BUMP 高度強制設定(過 BUMP 時車身會傾斜 15°),傾斜的車身前緣/後緣會戳進地板碰撞體而被卡住。
            // 機器人不和地板碰撞(高度本來就由程式控制);球、牆、HUB 等其他碰撞不受影響。
            var floorGo = GameObject.Find("Field/Floor");
            if (floorGo != null) { var fc = floorGo.GetComponent<Collider>(); if (fc != null) Physics.IgnoreCollision(col, fc, true); }
            var fieldGo = GameObject.Find("Field"); if (fieldGo != null) foreach (var bc in fieldGo.GetComponentsInChildren<MeshCollider>()) Physics.IgnoreCollision(col, bc, true);   // 機器人不和 BUMP 碰撞體碰撞(車身高度由 BumpHeightAt 強制)

            // 車身
            var body = GameObject.CreatePrimitive(PrimitiveType.Cube);
            body.name = "Body";
            Object.Destroy(body.GetComponent<Collider>());
            body.transform.SetParent(r.transform, false);
            body.transform.localScale = new Vector3(SimConstants.BumperLength, SimConstants.BumperHeight, SimConstants.BumperWidth);
            body.GetComponent<Renderer>().sharedMaterial = FieldBuilder.MakeMat(bodyColor ?? new Color(0.15f, 0.35f, 0.9f));

            // Intake 手臂(+X 前方,會伸縮)
            var arm = GameObject.CreatePrimitive(PrimitiveType.Cube);
            arm.name = "IntakeArm";
            Object.Destroy(arm.GetComponent<Collider>());
            arm.transform.SetParent(r.transform, false);
            arm.GetComponent<Renderer>().sharedMaterial = FieldBuilder.MakeMat(new Color(0.95f, 0.5f, 0.1f));
            armVis = arm.transform;

            // 砲塔(會轉,指向發射方向)
            var turret = new GameObject("Turret").transform;
            turret.SetParent(r.transform, false);
            turret.localPosition = new Vector3(0f, 0.3f, 0f);
            var tb = GameObject.CreatePrimitive(PrimitiveType.Cube);
            tb.name = "TurretBody";
            Object.Destroy(tb.GetComponent<Collider>());
            tb.transform.SetParent(turret, false);
            tb.transform.localScale = new Vector3(0.40f, 0.22f, 0.40f);
            tb.GetComponent<Renderer>().sharedMaterial = FieldBuilder.MakeMat(new Color(0.8f, 0.8f, 0.85f));
            var barrel = GameObject.CreatePrimitive(PrimitiveType.Cube);
            barrel.name = "Barrel";
            Object.Destroy(barrel.GetComponent<Collider>());
            barrel.transform.SetParent(turret, false);
            barrel.transform.localScale = new Vector3(0.28f, 0.10f, 0.14f);
            barrel.transform.localPosition = new Vector3(0.28f, 0.04f, 0f);
            barrel.GetComponent<Renderer>().sharedMaterial = FieldBuilder.MakeMat(new Color(0.15f, 0.15f, 0.18f));
            turretVis = turret;
            return r;
        }
    }
}
