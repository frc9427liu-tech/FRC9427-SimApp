using UnityEngine;

namespace FrcSim
{
    /// 賽車物理測試場(模組 K,01 §5):一大塊平地 + 可隨時加的牆、斜坡、平台。全部是 Cube 基本體(自帶 BoxCollider),
    /// 地面都掛 RaceSurface(Road)。Origin 放在離 FRC 場地很遠的地方,避免和既有碰撞體互相干擾。
    public sealed class KartTestTrack
    {
        public readonly GameObject Root;
        public readonly Vector3 Origin;

        KartTestTrack(Vector3 origin)
        {
            Origin = origin;
            Root = new GameObject("KartTestTrack");   // 位置維持 (0,0,0),子物件直接用世界座標
        }

        /// 建立測試場:800 x 800 m 平地,頂面在 Origin.y。
        public static KartTestTrack Build(Vector3 origin)
        {
            var t = new KartTestTrack(origin);
            t.AddBox("Floor", Vector3.zero, 0f, 0f, new Vector3(800f, 2f, 800f), Surface.Road);
            return t;
        }

        public Vector3 W(Vector3 local) { return Origin + local; }

        /// 起跑 Pose(position = 地面接觸點,RaceKart.Create 會自己抬高)。yaw 0 = 朝 +Z。
        public Pose Start(Vector3 local, float yawDeg)
        {
            return new Pose(W(local), Quaternion.Euler(0f, yawDeg, 0f));
        }

        /// 加一塊板子:topCenter = 頂面中心(相對 Origin),yaw 繞 Y 轉,pitchUp = +Z 端抬高的角度(度),
        /// size = (寬 X, 厚 Y, 長 Z)。頂面通過 topCenter;厚度往下長,所以斜坡底端剛好貼地、沒有台階。
        public GameObject AddBox(string name, Vector3 topCenter, float yawDeg, float pitchUp, Vector3 size, Surface surf)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
            go.name = name;
            go.transform.SetParent(Root.transform, false);
            Quaternion rot = Quaternion.Euler(0f, yawDeg, 0f) * Quaternion.Euler(-pitchUp, 0f, 0f);
            Vector3 center = W(topCenter) - (rot * Vector3.up) * (size.y * 0.5f);
            go.transform.SetPositionAndRotation(center, rot);
            go.transform.localScale = size;
            go.AddComponent<RaceSurface>().Type = surf;
            return go;
        }

        /// 加一面牆:baseCenter = 牆底中心(相對 Origin),厚度沿牆的本地 Z 軸。牆本身不是地面(沒有 RaceSurface)。
        public GameObject AddWall(string name, Vector3 baseCenter, float yawDeg, float width, float height, float thick)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
            go.name = name;
            go.transform.SetParent(Root.transform, false);
            go.transform.SetPositionAndRotation(W(baseCenter) + Vector3.up * (height * 0.5f), Quaternion.Euler(0f, yawDeg, 0f));
            go.transform.localScale = new Vector3(width, height, thick);
            return go;
        }

        public void Destroy()
        {
            if (Root != null) UnityEngine.Object.Destroy(Root);
        }
    }
}
