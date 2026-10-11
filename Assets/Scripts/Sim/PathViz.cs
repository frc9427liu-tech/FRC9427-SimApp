using System.Collections.Generic;
using System.IO;
using Newtonsoft.Json.Linq;
using UnityEngine;

namespace FrcSim
{
    // 把機器人專案的 PathPlanner 自動路線(src/main/deploy/pathplanner/paths/*.path)畫在地板上(藍方座標),按 P 開關。
    public class PathViz : MonoBehaviour
    {
        static GameObject root; GameObject lines;
        public static int Count;

        public static void Build(string projectDir)
        {
            if (root != null) Destroy(root);
            Count = 0; root = null;
            try
            {
                string dir = Path.Combine(projectDir, "src", "main", "deploy", "pathplanner", "paths");
                if (!Directory.Exists(dir)) return;
                root = new GameObject("PathViz"); var pv = root.AddComponent<PathViz>(); pv.lines = new GameObject("Lines"); pv.lines.transform.SetParent(root.transform, false);
                var mat = Look.GlowMaterial(new Color(0.35f, 0.9f, 1f), 0.9f);
                foreach (var f in Directory.GetFiles(dir, "*.path"))
                {
                    try
                    {
                        var j = JObject.Parse(File.ReadAllText(f)); var wps = (JArray)j["waypoints"]; if (wps == null || wps.Count < 2) continue;
                        var pts = new List<Vector3>();
                        for (int i = 0; i < wps.Count - 1; i++)
                        {
                            Vector2 a = P(wps[i]["anchor"]), c1 = P(wps[i]["nextControl"], a), c2 = P(wps[i + 1]["prevControl"], P(wps[i + 1]["anchor"])), b = P(wps[i + 1]["anchor"]);
                            for (int s = 0; s <= 24; s++)
                            {
                                float t = s / 24f, u = 1f - t;
                                Vector2 q = u * u * u * a + 3f * u * u * t * c1 + 3f * u * t * t * c2 + t * t * t * b;
                                pts.Add(new Vector3(q.x, 0.02f, q.y));
                            }
                        }
                        var g = new GameObject(Path.GetFileNameWithoutExtension(f)); g.transform.SetParent(pv.lines.transform, false);
                        var lr = g.AddComponent<LineRenderer>(); lr.useWorldSpace = true; lr.positionCount = pts.Count; lr.SetPositions(pts.ToArray());
                        lr.widthMultiplier = 0.045f; lr.sharedMaterial = mat; lr.alignment = LineAlignment.TransformZ; g.transform.rotation = Quaternion.Euler(90f, 0f, 0f);
                        lr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off; Count++;
                    }
                    catch { }
                }
                if (Count == 0) { Destroy(root); root = null; }
                else pv.lines.SetActive(Prefs.GetInt("pathviz", 1) == 1);
            }
            catch (System.Exception e) { Debug.LogWarning("[PathViz] " + e.Message); }
        }

        static Vector2 P(JToken t, Vector2? fallback = null)
        {
            if (t == null || t.Type == JTokenType.Null) return fallback ?? Vector2.zero;
            return new Vector2((float)t["x"], (float)t["y"]);
        }

        void Update()
        {
            if (Input.GetKeyDown(KeyCode.P) && !MenuSystem.Blocking) { bool on = Prefs.GetInt("pathviz", 1) != 1; Prefs.SetInt("pathviz", on ? 1 : 0); if (lines != null) lines.SetActive(on); }
        }
    }
}

