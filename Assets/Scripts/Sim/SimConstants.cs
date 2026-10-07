using UnityEngine;

namespace FrcSim
{
    // 座標:WPILib 場地座標 (x 沿場長, y 沿場寬, 藍方原點),對應 Unity (X, 0, Z)。
    // 角度:WPILib 逆時針為正;Unity 繞 Y 軸順時針為正,所以 Unity yaw = -heading。
    public static class SimConstants
    {
        // 場地 (REBUILT, 官方手冊 317.7in x 651.2in)
        public const float FieldLength = 16.54f;
        public const float FieldWidth = 8.07f;

        public const float AllianceZoneDepth = 4.0284f;   // 158.6 in
        public const float HubSize = 1.194f;               // 47 in
        public const float HubRimHeight = 1.8288f;         // 72 in
        public const float BumpWidth = 1.854f;             // 73 in
        public const float BumpDepth = 1.128f;             // 44.4 in
        public const float BumpHeight = 0.1654f;           // 6.513 in
        public const float TowerWidth = 1.2510f;           // 49.25 in
        public const float TowerDepth = 1.143f;            // 45 in
        public const float TowerHeight = 1.9876f;          // 78.25 in
        public const float DepotWidth = 1.0668f;           // 42 in
        public const float DepotDepth = 0.6858f;           // 27 in

        // 底盤 (Constants.java SwerveConstants:輪距 22.75in x 22.75... 前後 20.75in)
        public const float TrackWidth = 0.57785f;
        public const float WheelBase = 0.52705f;
        public const float BumperLength = 0.80f;           // 含保險桿的外形(約值,待實車量測)
        public const float BumperWidth = 0.80f;
        public const float BumperHeight = 0.20f;
        public const float RobotMass = 52f;

        // 速度/加速度上限 (Constants.java:SwerveConstants 4 m/s、DriveConstants 6 m/s、22 m/s^2)
        // 兩套數字矛盾,先用 4 m/s,待確認實際生效值
        public static float MaxSpeed = 4.0f;
        public const float MaxAccel = 22f;
        public const float MaxAngularSpeed = 10.0f;        // rad/s ≈ vmax / 外接圓半徑
        public const float MaxAngularAccel = 60f;
    }
}
