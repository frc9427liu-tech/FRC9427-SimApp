using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

// 無頭建置:Unity.exe -batchmode -quit -executeMethod BuildTool.BuildWindows
public static class BuildTool
{
    const string ScenePath = "Assets/Scenes/Main.unity";
    const string OutDir = "Build/FRC9427-Sim";

    static void EnsureScene()
    {
        Directory.CreateDirectory("Assets/Scenes");
        var scene = EditorSceneManager.NewScene(NewSceneSetup.DefaultGameObjects, NewSceneMode.Single);
        EditorSceneManager.SaveScene(scene, ScenePath);
        EditorBuildSettings.scenes = new[] { new EditorBuildSettingsScene(ScenePath, true) };
    }

    static void IncludeShaders()
    {
        var gs = AssetDatabase.LoadAssetAtPath<Object>("ProjectSettings/GraphicsSettings.asset");
        var so = new SerializedObject(gs);
        var arr = so.FindProperty("m_AlwaysIncludedShaders");
        foreach (var name in new[] { "Standard", "Unlit/Color", "glTF/PbrMetallicRoughness", "glTF/PbrSpecularGlossiness", "glTF/Unlit",
                                     "Shader Graphs/glTF-pbrMetallicRoughness", "Shader Graphs/glTF-pbrSpecularGlossiness", "Shader Graphs/glTF-unlit" })
        {
            var sh = Shader.Find(name);
            if (sh == null) continue;
            bool have = false;
            for (int i = 0; i < arr.arraySize; i++)
                if (arr.GetArrayElementAtIndex(i).objectReferenceValue == sh) have = true;
            if (have) continue;
            arr.InsertArrayElementAtIndex(arr.arraySize);
            arr.GetArrayElementAtIndex(arr.arraySize - 1).objectReferenceValue = sh;
        }
        so.ApplyModifiedPropertiesWithoutUndo();
    }

    // Xbox 手把右搖桿與扳機:舊版 Input 預設沒有定義,這裡補進 InputManager(軸編號:右搖桿 X=4、Y=5、LT=9、RT=10)
    static void AddInputAxes()
    {
        var so = new SerializedObject(AssetDatabase.LoadAssetAtPath<Object>("ProjectSettings/InputManager.asset"));
        var axes = so.FindProperty("m_Axes");
        foreach (var def in new[] { ("PadRX", 4), ("PadRY", 5), ("PadLT", 9), ("PadRT", 10) })
        {
            bool have = false;
            for (int i = 0; i < axes.arraySize; i++) if (axes.GetArrayElementAtIndex(i).FindPropertyRelative("m_Name").stringValue == def.Item1) have = true;
            if (have) continue;
            axes.InsertArrayElementAtIndex(axes.arraySize);
            var a = axes.GetArrayElementAtIndex(axes.arraySize - 1);
            a.FindPropertyRelative("m_Name").stringValue = def.Item1;
            a.FindPropertyRelative("descriptiveName").stringValue = "";
            a.FindPropertyRelative("descriptiveNegativeName").stringValue = "";
            a.FindPropertyRelative("negativeButton").stringValue = "";
            a.FindPropertyRelative("positiveButton").stringValue = "";
            a.FindPropertyRelative("altNegativeButton").stringValue = "";
            a.FindPropertyRelative("altPositiveButton").stringValue = "";
            a.FindPropertyRelative("gravity").floatValue = 0f;
            a.FindPropertyRelative("dead").floatValue = 0.15f;
            a.FindPropertyRelative("sensitivity").floatValue = 1f;
            a.FindPropertyRelative("snap").boolValue = false;
            a.FindPropertyRelative("invert").boolValue = false;
            a.FindPropertyRelative("type").intValue = 2;   // Joystick Axis
            a.FindPropertyRelative("axis").intValue = def.Item2 - 1;
            a.FindPropertyRelative("joyNum").intValue = 0;
        }
        so.ApplyModifiedPropertiesWithoutUndo();
    }

    public static void BuildWindows()
    {
        EnsureScene();
        IncludeShaders();
        AddInputAxes();

        PlayerSettings.productName = "FRC9427 Simulator";
        PlayerSettings.companyName = "FRC9427";
        PlayerSettings.fullScreenMode = FullScreenMode.Windowed;
        PlayerSettings.defaultScreenWidth = 1600;
        PlayerSettings.defaultScreenHeight = 900;
        PlayerSettings.resizableWindow = true;
        PlayerSettings.runInBackground = true;

        Directory.CreateDirectory(OutDir);
        var opts = new BuildPlayerOptions
        {
            scenes = new[] { ScenePath },
            locationPathName = OutDir + "/FRC9427-Sim.exe",
            target = BuildTarget.StandaloneWindows64,
            options = BuildOptions.None
        };
        var report = BuildPipeline.BuildPlayer(opts);
        // 把匯入的機器人模型(ImportSamples 內已轉好的 glb)放到 exe 旁的 Robots 資料夾
        try
        {
            string robots = OutDir + "/Robots";
            Directory.CreateDirectory(robots);
            foreach (var g in Directory.GetFiles("ImportSamples", "*.glb", SearchOption.AllDirectories))
                File.Copy(g, robots + "/" + Path.GetFileName(g), true);
        }
        catch (System.Exception e) { Debug.LogWarning("copy robots failed: " + e.Message); }
        try
        {
            string simDir = OutDir + "/Sim";
            Directory.CreateDirectory(simDir);
            foreach (var g in Directory.GetFiles("Tools/sim", "*.mech.json")) File.Copy(g, simDir + "/" + Path.GetFileName(g), true);
            if (Directory.Exists("Tools/sim/overlay")) { Directory.CreateDirectory(simDir + "/overlay"); foreach (var g in Directory.GetFiles("Tools/sim/overlay", "*.java")) File.Copy(g, simDir + "/overlay/" + Path.GetFileName(g), true); }
            if (File.Exists("Tools/sim/agent/simagent.jar")) File.Copy("Tools/sim/agent/simagent.jar", simDir + "/simagent.jar", true);
            // 依 mech.json 產生 agent 的馬達表:每行 id coderId ratio inertia friction invert min max
            foreach (var mj in Directory.GetFiles("Tools/sim", "*.mech.json"))
            {
                var j = Newtonsoft.Json.Linq.JObject.Parse(File.ReadAllText(mj));
                var ids = (Newtonsoft.Json.Linq.JArray)j["agentMotors"];
                if (ids == null) continue;
                var inv = System.Globalization.CultureInfo.InvariantCulture;
                string F(double d) => double.IsPositiveInfinity(d) ? "Infinity" : double.IsNegativeInfinity(d) ? "-Infinity" : d.ToString("R", inv);
                var sb = new System.Text.StringBuilder("# id coderId ratio inertia friction invert min max\n");
                foreach (var idTok in ids)
                {
                    string name = (string)idTok;
                    var mm = System.Text.RegularExpressions.Regex.Match(name, @"\[(\d+)\]$");
                    if (!mm.Success) continue;
                    var ld = j["loads"]?["[" + mm.Groups[1].Value + "]"] as Newtonsoft.Json.Linq.JObject;
                    int coder = -1; double ratio = 26.09090909090909;
                    foreach (var l in (Newtonsoft.Json.Linq.JArray)j["links"] ?? new Newtonsoft.Json.Linq.JArray())
                        if ((string)l["motor"] == name)
                        {
                            var cm = System.Text.RegularExpressions.Regex.Match((string)l["sensor"], @"\[(\d+)\]$");
                            if (cm.Success) coder = int.Parse(cm.Groups[1].Value);
                            ratio = (double?)l["ratio"] ?? ratio;
                        }
                    double J = (double?)ld?["inertia"] ?? 0.002, fr = (double?)ld?["friction"] ?? 0.01;
                    bool iv = (bool?)ld?["invert"] ?? false;
                    double mn = (double?)ld?["minRot"] ?? double.NegativeInfinity, mx = (double?)ld?["maxRot"] ?? double.PositiveInfinity;
                    sb.Append(mm.Groups[1].Value).Append(' ').Append(coder).Append(' ').Append(F(ratio)).Append(' ').Append(F(J)).Append(' ').Append(F(fr)).Append(' ').Append(iv ? 1 : 0).Append(' ').Append(F(mn)).Append(' ').Append(F(mx)).Append('\n');
                }
                string pj = Path.GetFileName(mj); pj = pj.Substring(0, pj.Length - ".mech.json".Length);
                File.WriteAllText(simDir + "/" + pj + ".agent-motors.txt", sb.ToString());
                if (pj == "FRC9427_offseasonBot") File.WriteAllText(simDir + "/agent-motors.txt", sb.ToString());
            }
        }
        catch (System.Exception e) { Debug.LogWarning("copy mech failed: " + e.Message); }
        Debug.Log("BUILD RESULT: " + report.summary.result + " errors=" + report.summary.totalErrors);
        EditorApplication.Exit(report.summary.result == UnityEditor.Build.Reporting.BuildResult.Succeeded ? 0 : 1);
    }
}
