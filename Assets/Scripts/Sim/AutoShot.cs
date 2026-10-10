using System.Collections;
using UnityEngine;

namespace FrcSim
{
    // 測試用啟動參數(不需要手動點選單,也不需要視窗取得焦點):
    //   -autostart                直接進自由練習(略過選單)
    //   -shot <路徑.png>          等 -shotdelay 秒(預設 20)後存遊戲畫面並離開
    //   -camera N                 視角 0 俯瞰 / 1 跟隨 / 2 正上方
    public class AutoShot : MonoBehaviour
    {
        public CameraRig Rig;

        static string Arg(string name)
        {
            var a = System.Environment.GetCommandLineArgs();
            int i = System.Array.IndexOf(a, name);
            return i >= 0 && i + 1 < a.Length ? a[i + 1] : null;
        }

        IEnumerator Start()
        {
            string cam = Arg("-camera");
            string shot = Arg("-shot");
            float delay = 20f; float.TryParse(Arg("-shotdelay"), out delay); if (delay <= 0f) delay = 20f;
            yield return new WaitForSecondsRealtime(1f);
            if (cam != null && int.TryParse(cam, out int m)) Rig.Mode = m;
            if (shot == null) yield break;
            if (System.Array.IndexOf(System.Environment.GetCommandLineArgs(), "-freeze") >= 0) { yield return new WaitForSecondsRealtime(1.5f); foreach (var ri in FindObjectsByType<RobotInput>(FindObjectsSortMode.None)) ri.enabled = false; }   // 測試:關掉手把輸入(接著的手把飄移會讓車亂動)
            yield return new WaitForSecondsRealtime(delay);
            if (System.Array.IndexOf(System.Environment.GetCommandLineArgs(), "-fxtest") >= 0) { for (int k = 0; k < 3; k++) Juice.OnScore(true, true, new Vector3(4.62f, 2.0f, SimConstants.FieldWidth / 2f)); yield return new WaitForSecondsRealtime(0.12f); }   // 測試:連續 4 次進球特效後截圖
            if (System.Array.IndexOf(System.Environment.GetCommandLineArgs(), "-spilltest") >= 0 && GameSession.Mech != null) { GameSession.Mech.Held = 20; int before = FuelManager.All.Count; GameSession.Mech.Spill(4); UnityEngine.Debug.Log("[SpillTest] held=" + GameSession.Mech.Held + " fuel " + before + "->" + FuelManager.All.Count); yield return new WaitForSecondsRealtime(0.5f); }
            if (System.Array.IndexOf(System.Environment.GetCommandLineArgs(), "-hptest") >= 0 && GameSession.Drive != null) { GameSession.Drive.SetPose(new Vector2(1.6f, 1.3f), 0f); int b0 = FuelManager.All.Count; yield return new WaitForSecondsRealtime(4f); UnityEngine.Debug.Log("[HpTest] fuel " + b0 + "->" + FuelManager.All.Count + " blueChute=" + HumanPlayer.BlueChute); }
            if (System.Array.IndexOf(System.Environment.GetCommandLineArgs(), "-showpause") >= 0) { MenuSystem.PauseNow(); yield return new WaitForSecondsRealtime(1.5f); }
            ScreenCapture.CaptureScreenshot(shot);
            yield return new WaitForSecondsRealtime(2f);
            Application.Quit();
        }
    }
}
