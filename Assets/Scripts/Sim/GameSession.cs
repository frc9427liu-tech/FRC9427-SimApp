using UnityEngine;

namespace FrcSim
{
    // 一場遊戲的生命週期:Begin() 建立球/機器人/計分/HUD,End() 全部清掉回到主畫面。
    public static class GameSession
    {
        public static bool Active;
        public static SwerveDrive Drive;
        public static RobotMechanisms Mech;
        public static HalSim Hal;

        static GameObject robot, robot2, score, hud;

        public static void Begin(CameraRig rig, bool selfTest = false, string projectOverride = null)
        {
            if (Active) return;
            Active = true;
            Time.timeScale = 1f;

            FuelManager.Init();
            FuelManager.SpawnStart();
            score = new GameObject("Score");
            score.AddComponent<ScoreManager>();

            robot = SimBootstrap.BuildRobot(out var turretVis, out var armVis);
            Drive = robot.GetComponent<SwerveDrive>();
            Mech = robot.AddComponent<RobotMechanisms>();
            Mech.Drive = Drive;
            Mech.RobotCollider = robot.GetComponent<Collider>();
            Mech.TurretVisual = turretVis;
            Mech.ArmVisual = armVis;

            // 第二台機器人(紅方,內建行為,方向鍵操控)
            if (PlayerPrefs.GetInt("secondRobot", 1) == 1)
            {
                robot2 = SimBootstrap.BuildRobot(out var t2, out var a2, "Robot2", new Color(0.9f, 0.2f, 0.2f));
                var d2 = robot2.GetComponent<SwerveDrive>();
                d2.ResetPose();
                d2.SetPose(new Vector2(SimConstants.FieldLength - 2.0f, SimConstants.FieldWidth / 2f), 180f);
                var m2 = robot2.AddComponent<RobotMechanisms>();
                m2.Drive = d2; m2.RobotCollider = robot2.GetComponent<Collider>(); m2.TurretVisual = t2; m2.ArmVisual = a2;
                m2.TargetHub = new Vector2(SimConstants.FieldLength - 4.42586f, SimConstants.FieldWidth / 2f);
                var in2 = robot2.AddComponent<Robot2Input>(); in2.Drive = d2; in2.Mech = m2;
            }

            // 真實機器人程式(設定畫面選的專案;沒選就用內建行為)
            // 預設關閉:真實程式模式還在實驗(閉環時序不穩),內建行為才是穩定版
            string proj = projectOverride ?? (PlayerPrefs.GetInt("useRealCode", 0) == 1 ? PlayerPrefs.GetString("robotProject", "") : "");
            bool tankMode = PlayerPrefs.GetInt("tankMode", 1) == 1;
            if (projectOverride == null && tankMode && !string.IsNullOrEmpty(proj) && !IsTankProfile(proj)) proj = "";   // 坦克模式只跑有坦克設定檔的專案(如 LEO)
            if (string.IsNullOrEmpty(proj) && PlayerPrefs.GetInt("useRealCode", 0) == 1 && !tankMode)
            {
                foreach (var c in new[] { @"C:\Users\frc94\2026_FRC9427_offseasonBot\FRC9427_offseasonBot" })
                    if (System.IO.File.Exists(System.IO.Path.Combine(c, "gradlew.bat"))) { proj = c; break; }
            }
            if (string.IsNullOrEmpty(proj) && PlayerPrefs.GetInt("useRealCode", 0) == 1 && tankMode)
            {
                // 沒選專案時自動找桌面上的 LEO(資料夾名開頭 LEO、內有 gradlew.bat)
                try
                {
                    string desk = System.Environment.GetFolderPath(System.Environment.SpecialFolder.DesktopDirectory);
                    foreach (var dd in System.IO.Directory.GetDirectories(desk, "LEO*"))
                        if (System.IO.File.Exists(System.IO.Path.Combine(dd, "gradlew.bat")) && IsTankProfile(dd)) { proj = dd; break; }
                }
                catch { }
            }
            if (System.Array.IndexOf(System.Environment.GetCommandLineArgs(), "-norealcode") >= 0) proj = "";
            if (!string.IsNullOrEmpty(proj) && System.IO.File.Exists(System.IO.Path.Combine(proj, "gradlew.bat")))
            {
                Hal = robot.AddComponent<HalSim>();
                Hal.StartRobot(proj);
                var ms = robot.AddComponent<MechSim>();
                ms.Hal = Hal; ms.Drive = Drive; Mech.Sim = ms;
                var nt = robot.AddComponent<NtSim>();     // 模擬 Limelight(NetworkTables)讓程式能定位
                nt.Drive = Drive; nt.Begin();
                ms.MechPath = System.IO.Path.Combine(System.IO.Path.GetDirectoryName(Application.dataPath), "Sim", SimProject.Profile(proj) + ".mech.json");
            }

            // 匯入的機器人模型(沒有就用內建方塊)
            string model = RobotModels.Selected;
            Debug.Log($"[RobotModels] selected='{model}' dir={RobotModels.Dir} count={RobotModels.List().Length}");
            if (!string.IsNullOrEmpty(model))
                RobotModels.Attach(robot.transform, model, RobotModels.YawDeg, ok =>
                {
                    if (!ok) Debug.LogWarning("robot model load failed: " + RobotModels.LastError);
                });

            rig.Orbit = false;
            rig.Target = robot.transform;
            rig.Mode = 1;

            hud = new GameObject("SimUI");
            var ui = hud.AddComponent<SimHud>();
            ui.Drive = Drive; ui.Rig = rig; ui.Mech = Mech;

            var input = robot.AddComponent<RobotInput>();
            input.Drive = Drive;
            input.Mech = Mech;

            if (selfTest)
            {
                var st = new GameObject("SelfTest").AddComponent<SelfTest>();
                st.Drive = Drive; st.Mech = Mech;
            }
        }

        static bool IsTankProfile(string dir)
        {
            try
            {
                string f = System.IO.Path.Combine(System.IO.Path.GetDirectoryName(Application.dataPath), "Sim", SimProject.Profile(dir) + ".mech.json");
                return System.IO.File.Exists(f) && System.IO.File.ReadAllText(f).Contains("\"tank\"");
            }
            catch { return false; }
        }

        public static void End(CameraRig rig)
        {
            if (!Active) return;
            Active = false;
            Time.timeScale = 1f;
            if (robot != null) Object.Destroy(robot);
            if (robot2 != null) Object.Destroy(robot2);
            if (score != null) Object.Destroy(score);
            if (hud != null) Object.Destroy(hud);
            FuelManager.Clear();
            Hal?.Stop(); Hal = null;
            Drive = null; Mech = null;
            rig.Target = null;
            rig.Orbit = true;
        }
    }
}
