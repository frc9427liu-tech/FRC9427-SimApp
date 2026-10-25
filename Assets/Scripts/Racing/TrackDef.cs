using System;
using System.Collections.Generic;
using UnityEngine;

namespace FrcSim
{
    // 賽道資料(模組 W 內部型別,見 docs/racing/02-world-and-track.md §2.1)。
    // 可用 JsonUtility 讀寫(Resources/Racing/Tracks/<name>.json),找不到檔案時由 DefaultTracks 提供寫死的賽道。

    /// 賽道控制點(x/z 為 Unity 世界座標,原點在地圖中心)。
    [Serializable]
    public class TrackNode
    {
        public float x, z;
        public float width = 12f;   // 這個控制點的全路寬 m
        public float bank;          // 側傾角 度(M1 不使用)
        public int flags;           // 保留

        public TrackNode() { }
        public TrackNode(float x, float z, float width = 12f) { this.x = x; this.z = z; this.width = width; }
    }

    /// 捷徑定義(M5)。從主線第 fromNode 個控制點分出去,經過 nodes,接回第 toNode 個控制點(fromNode &lt; toNode,不跨終點線)。
    /// 主線控制點 fromNode / toNode 之間的檢查點門會被「跳過」(Checkpoint.ShortcutId = id);車子要穿過入口門再穿過出口門才算走完捷徑。
    [Serializable]
    public class TrackShortcut
    {
        public int id;
        public int fromNode, toNode;
        public List<TrackNode> nodes = new List<TrackNode>();
        public float width = 7f;
        public float entryDist = 12f, exitDist = 12f;   // 入口 / 出口門離捷徑兩端的弧長 m
    }

    /// 路邊造景(低多邊形地標)。s = 主線弧長,lateral = 離中心線橫向位移(右正),scale = 整體縮放。
    public enum TrackLandmarkKind { BigArch = 0, Windmill = 1, Lighthouse = 2, RockArch = 3, Spire = 4 }

    [Serializable]
    public class TrackLandmark
    {
        public int kind;
        public float s;
        public float lateral;
        public float scale = 1f;
        public TrackLandmark() { }
        public TrackLandmark(TrackLandmarkKind kind, float s, float lateral, float scale = 1f) { this.kind = (int)kind; this.s = s; this.lateral = lateral; this.scale = scale; }
    }

    /// Rail = 3 是軌道滑行(模組 K):資料放在 TrackDef.rails(TrackRail),不放進 features(features 裡「非 Boost 即跳台」的判斷到處在用,Validate 也只收 0..2)。
    public enum TrackFeatureKind { Ramp = 0, Boost = 1, GliderRamp = 2, Rail = 3 }

    /// 軌道(細折線,懸在路面上方 height 公尺):主線弧長 [s0, s1](沿行進方向),lateral = 離中心線橫向位移(右正)。
    /// 只有外觀(細梁 + 立柱)和 KartRail 幾何查詢,沒有碰撞體。車從空中落上去就沿軌道走,尾端脫離加速。
    [Serializable]
    public class TrackRail
    {
        public int kind = (int)TrackFeatureKind.Rail;
        public float s0, s1;
        public float lateral = 3.5f;
        public float height = 2.2f;   // 軌道頂面離路面高度 m(合法 1.5..3.0)
        public TrackRail() { }
        public TrackRail(float s0, float s1, float lateral = 3.5f, float height = 2.2f) { this.s0 = s0; this.s1 = s1; this.lateral = lateral; this.height = height; }
    }

    /// 缺口(M7,瑪利歐賽車 8 式的飛越):主線路面在 [s0, s1](主線弧長)之間被切斷(峽谷 / 海灣 / 熔岩河),中心線照樣穿過去。
    /// s0 = 起飛前緣(滑翔跳台前緣,跳台中心在 s0 - 7);s1 = 落地邊緣(路面在這裡重新開始)。
    /// RaceWorld.Build 會把缺口範圍的地形挖到海平面以下(Surface.Water,掉下去 = 重置到缺口前的檢查點)並把這段路面網格拿掉;
    /// 缺口中央有一個檢查點,所以從旁邊繞過去拿不到門。
    [Serializable]
    public class TrackGap
    {
        public float s0, s1;
        public float halfWidth = 36f;   // 深坑橫向半寬 m(再往外 14 m 平滑接回自然地形)
        public float depth = 8f;        // 坑底在海平面下幾公尺
        public TrackGap() { }
        public TrackGap(float s0, float s1, float halfWidth = 36f, float depth = 8f) { this.s0 = s0; this.s1 = s1; this.halfWidth = halfWidth; this.depth = depth; }
    }

    /// 路面上的跳台 / 加速板(M6 新增)。s = 中心的弧長,lateral = 離中心線的橫向位移(右正),length 沿行進方向,width 橫向。
    /// pitch 只有跳台用(斜坡角度,度)。舊資料沒有這個欄位時 TrackDef.features 是空清單。
    /// GliderRamp(滑翔跳台):寬 10 m、長 14 m、坡度 22 度的大跳台,車通過前緣時由 RaceWorld.StepPickups 回報 Glide(整合者呼叫 kart.BeginGlide())。
    [Serializable]
    public class TrackFeature
    {
        public int kind;            // TrackFeatureKind
        public float s;
        public float lateral;
        public float length = 6f;
        public float width = 5f;
        public float pitch = 18f;
        public int path = -1;       // -1 = 主線;>= 0 = 捷徑編號(s 為該捷徑起點算起的弧長)
        public bool locked;         // 缺口的起飛跳台:TrackVariants 反向時不會移動 / 拿掉(它跟 TrackDef.gaps 綁在一起)

        public TrackFeature() { }
        public TrackFeature(TrackFeatureKind kind, float s, float length, float width, float lateral = 0f, float pitch = 18f)
        { this.kind = (int)kind; this.s = s; this.length = length; this.width = width; this.lateral = lateral; this.pitch = pitch; }
    }

    [Serializable]
    public class TrackDef
    {
        public string name = "Track";
        public string nameZh = "";
        public int seed = 9427;
        public int laps = 3;
        public bool closed = true;
        public List<TrackNode> nodes = new List<TrackNode>();
        public List<TrackShortcut> shortcuts = new List<TrackShortcut>();
        public float checkpointSpacing = 40f;
        public List<TrackFeature> features = new List<TrackFeature>();
        public List<TrackLandmark> landmarks = new List<TrackLandmark>();
        public List<TrackGap> gaps = new List<TrackGap>();   // M7:飛越缺口(沒有 = 空清單,舊資料相容)
        public List<TrackRail> rails = new List<TrackRail>(); // 軌道滑行(沒有 = 空清單,舊資料相容)

        // 地貌 / 外觀(M5)。theme:0 島嶼 1 峽谷(沙漠風) 2 海岸 3 草原 4 沙漠 5 雪地 6 森林 7 火山(TrackTheme.Count);其餘控制地形起伏。
        public int theme;
        public float relief = 1f;          // 路面縱剖面起伏倍率(越大坡越多,仍受 5.5 度限制)
        public float hillAmp = 24f;        // 離路面遠處的丘陵高度
        public float wallStart = 24f, wallEnd = 110f;   // 丘陵權重從離路 wallStart 公尺長到 wallEnd 公尺(峽谷用很短的距離 = 陡壁)
        public float cliff;                // 沿海懸崖:陸地整體墊高 m,海岸過渡變窄
        public float treeDensity = 1f;     // 樹木密度倍率
        public bool autoBank;              // M8:由曲率自動算路面側傾(緊彎最多 MaxBankDeg 度,外側高;路面網格與地形都照這個傾斜)

        /// 檢查資料;回傳 null = 沒問題,否則回傳第一個錯誤的說明。
        public string Validate()
        {
            if (nodes == null || nodes.Count < 4) return "nodes < 4";
            if (!closed) return "only closed tracks are supported";
            if (laps < 1) return "laps < 1";
            if (checkpointSpacing < 10f || checkpointSpacing > 200f) return "checkpointSpacing out of 10..200";
            for (int i = 0; i < nodes.Count; i++)
            {
                var n = nodes[i];
                if (n == null) return "node " + i + " is null";
                if (float.IsNaN(n.x) || float.IsNaN(n.z) || float.IsInfinity(n.x) || float.IsInfinity(n.z)) return "node " + i + " is NaN";
                if (n.width < 4f || n.width > 30f) return "node " + i + " width out of 4..30";
                if (Mathf.Abs(n.x) > 400f || Mathf.Abs(n.z) > 400f) return "node " + i + " outside the island";
            }
            if (features != null)
                foreach (var f in features)
                {
                    if (f == null) return "feature is null";
                    if (f.kind < 0 || f.kind > 2) return "feature kind out of range";
                    if (f.path < -1 || (f.path >= 0 && (shortcuts == null || f.path >= shortcuts.Count))) return "feature path out of range";
                    if (float.IsNaN(f.s) || float.IsNaN(f.lateral)) return "feature NaN";
                    if (f.length < 2f || f.length > 30f || f.width < 1f || f.width > 12f) return "feature size out of range";
                    if (Mathf.Abs(f.lateral) + f.width * 0.5f > 12f) return "feature outside the road";
                    if ((f.kind == 0 || f.kind == 2) && (f.pitch < 5f || f.pitch > 30f)) return "ramp pitch out of 5..30";
                }
            if (rails != null)
                foreach (var r in rails)
                {
                    if (r == null) return "rail is null";
                    if (float.IsNaN(r.s0) || float.IsNaN(r.s1) || float.IsNaN(r.lateral) || float.IsNaN(r.height)) return "rail NaN";
                    if (r.s0 < 0f || r.s1 - r.s0 < 12f || r.s1 - r.s0 > 120f) return "rail length out of 12..120";
                    if (r.height < 1.5f || r.height > 3f) return "rail height out of 1.5..3";
                    if (Mathf.Abs(r.lateral) > 8f) return "rail lateral out of +-8";
                }
            if (gaps != null)
                foreach (var g in gaps)
                {
                    if (g == null) return "gap is null";
                    if (float.IsNaN(g.s0) || float.IsNaN(g.s1) || g.s0 < 30f || g.s1 - g.s0 < 20f || g.s1 - g.s0 > 130f) return "gap length out of 20..130 or starts before 30 m";
                    if (g.halfWidth < 10f || g.halfWidth > 80f || g.depth < 3f || g.depth > 30f) return "gap halfWidth/depth out of range";
                }
            if (shortcuts != null)
                foreach (var sc in shortcuts)
                {
                    if (sc == null) return "shortcut is null";
                    if (sc.fromNode < 0 || sc.fromNode >= nodes.Count || sc.toNode < 0 || sc.toNode >= nodes.Count) return "shortcut " + sc.id + " node index out of range";
                    if (sc.fromNode >= sc.toNode) return "shortcut " + sc.id + " must have fromNode < toNode";
                    if (sc.nodes == null || sc.nodes.Count < 1) return "shortcut " + sc.id + " has no nodes";
                    if (sc.width < 4f || sc.width > 12f) return "shortcut " + sc.id + " width out of 4..12";
                }
            return null;
        }

        public static TrackDef FromJson(string json)
        {
            if (string.IsNullOrEmpty(json)) return null;
            try { return JsonUtility.FromJson<TrackDef>(json); }
            catch (Exception) { return null; }
        }

        public string ToJson(bool pretty = true) { return JsonUtility.ToJson(this, pretty); }
    }
}
