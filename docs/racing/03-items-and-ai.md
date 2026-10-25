# 賽車模式設計書 03:道具與 AI 對手

> 範圍:道具系統、AI 對手、對外介面契約、檔案清單、測試。
> 慣例沿用現有程式:namespace `FrcSim`、全部用程式生成(沒有美術素材)、難度用 `static readonly float[]` 陣列以 `Level` 0~3 索引(同 `Robot2AI`)、手把一律透過 `Pad`/`Pad2` 讀取、音效走 `SfxBus.Play`、特效走 `Fx`/`Juice`、震動走 `Rumble`。
> 預設 8 台車(1 玩家 + 7 AI),一圈約 60~90 秒,3 圈。

---

## 1. 道具

### 1.1 道具箱
- 沿賽道每隔約 150 m 放一排,每排 4 個,跨整條路寬。箱子用 `GameObject.CreatePrimitive(Cube)` 生成(0.8 m,自轉 90°/s,上下浮動 0.15 m,用 HSV 彩虹循環的 emission 顏色)。
- 撞到後:箱子消失 2.5 秒再重生;車子進入 1.2 秒「抽獎」(HUD 圖示輪流切換,`SfxBus "tick"`),時間到才決定拿到什麼。
- 每台車**只能持有 1 個**道具;已經有道具時穿過箱子不抽。
- 抽獎**在決定的那一刻**依名次查表(不是撞箱的那一刻),避免抽獎期間名次變動造成不公平。

### 1.2 道具一覽

| # | 名稱 | 效果 | 數值 | 使用 | 程式生成視覺 |
|---|------|------|------|------|------|
| 1 | 加速(Boost) | 推力上限提高 | 極速 ×1.45、持續 1.5 s,期間抓地 +20% | 按下立即生效 | 車尾 2 條拉長的 Cylinder 噴焰(橘→白漸層)、`Fx.Dust` 尾塵、相機 FOV +8° |
| 2 | 三連加速 | 同上,可用 3 次 | 每次 1.2 s,兩次間至少 0.3 s | 每按一次用一發 | 同上,HUD 顯示剩餘發數 |
| 3 | 護盾(Shield) | 擋下一次攻擊或障礙 | 持續 8 s 或吸收 1 次 | 立即生效 | 半透明 Sphere(半徑 1.6 m,菲涅耳感用 alpha 0.25 + 外圈 LineRenderer 旋轉) |
| 4 | 油漬(Oil) | 丟在身後的障礙物 | 撞到的車:抓地 ×0.3 持續 1.0 s,強制側滑 + 速度 ×0.6 | 往後放(手把下 + 使用 = 往前丟 8 m) | 扁 Cylinder(直徑 2 m、高 0.02 m),黑色高光澤材質;存在 25 s 或被撞 1 次 |
| 5 | 直線彈(Bumper Ball) | 直線往前飛,撞牆反彈 3 次 | 速度 40 m/s,壽命 4 s;命中:暈眩 1.0 s | 往前射(手把下 = 往後) | 紅色 Sphere 0.5 m + TrailRenderer |
| 6 | 追蹤彈(Homing) | 沿賽道中心線追名次在前一名的車 | 速度 32 m/s(比極速快約 30%),壽命 8 s;命中:暈眩 1.4 s、速度歸零 | 按下發射,自動鎖定 | 藍色 Sphere + 前方 Cone,飛行時沿中心線取點;被鎖定的車頭上出現閃爍紅色三角(HUD 警告 + `Rumble.Driver`) |

**暈眩(Stun)**:油門被鎖 0、方向盤輸入 ×0.3、車身原地轉 1 圈(視覺動畫,不影響物理位置)。暈眩結束後 1.5 s 內**無敵**(防止連鎖被打)。護盾或無敵期間命中 → 只播 `Fx.Bump` 不生效。

### 1.3 依名次發放的機率表(%)
名次以 8 台車為準;若車數不同,用「名次比例 r = (名次-1)/(車數-1)」查最近的一列。

| 名次 | 加速 | 三連加速 | 護盾 | 油漬 | 直線彈 | 追蹤彈 |
|------|----:|----:|----:|----:|----:|----:|
| 1 | 5 | 0 | 30 | 45 | 20 | 0 |
| 2 | 15 | 0 | 25 | 30 | 25 | 5 |
| 3~4 | 25 | 5 | 15 | 15 | 25 | 15 |
| 5~6 | 30 | 15 | 10 | 5 | 15 | 25 |
| 7~8 | 30 | 30 | 5 | 0 | 5 | 30 |

規則:同時在場的追蹤彈最多 2 顆(超過就改抽三連加速);第 1 圈前 10 秒不出追蹤彈。表格放在 `ItemTable` 的 `static readonly int[,]`,方便調整與測試(每列總和必須 = 100,自動測試會檢查)。

---

## 2. AI 對手

### 2.1 追線(依中心線 + 檢查點)
1. `Track` 提供中心線取樣點(每 2 m 一點,含路寬、曲率),以「沿賽道距離 s」為參數。
2. AI 每一幀查自己的 `s`(`Track.ProjectToCenterline`),往前看 `lookahead = 6 m + 0.45 × 速度(m/s)` 取目標點(Pure Pursuit)。
3. 目標點加上**橫向偏移 lane**(−1~+1,乘以半路寬 × 0.7),讓 8 台車不會全部擠在中心線上。lane 平常是每台車固定的個性值,超車/避障時才改。
4. 方向盤 = `clamp(角度差 × 2.2, -1, 1)`(同 `Robot2AI` 的 `Wrap(wantHeading - heading) * 3.0` 寫法)。
5. 檢查點用來計算名次與防逆走:若 `s` 的下一個檢查點 30 m 內沒有前進 5 秒 → 視為卡住,照 `Robot2AI` 的卡住偵測做法:倒車 0.8 s + 往路中央打方向,再失敗就 `Track.RespawnPose` 重置到上一個檢查點。

### 2.2 速度控制(彎前減速)
- 往前看 `brakeLook = 速度² / (2 × 減速度) + 10 m` 範圍內的最大曲率 κmax。
- 目標速度 `vTarget = min(極速 × speedMul, sqrt(grip × 9.81 / κmax))`。
- 實際速度 > vTarget + 1 m/s → 煞車(油門 −1);在 ±1 內 → 依比例;低於 → 全油門。
- `grip` 是 AI 自己「以為」的抓地,難度越低越保守(見 2.5)。

### 2.3 漂移判斷
- 條件:前方 15 m 內曲率 κ > `driftKappa`(約 1/25 m),速度 > 60% 極速,且彎道持續長度 > 12 m。
- 進彎點前 2 m 按住漂移鍵,方向盤往彎內;出彎點(曲率降到 driftKappa 的一半)放開,取得漂移加速(由 KartController 決定藍/橘火花等級)。
- 低難度不漂移或常常放太早(見 `DriftSkill`)。

### 2.4 超車與避障
- 每 0.2 s(依難度 `Think`)掃描前方 25 m、自己路寬內的車與障礙(油漬、彈)。
- 前車在 12 m 內且比自己慢 > 1.5 m/s → 選前車另一側的 lane(哪邊空間大選哪邊),lane 以 1.5/s 速率平滑移動。
- 障礙物(油漬)在路徑 ±1.2 m 內 → 暫時 lane 往遠離方向偏 0.6,通過後回原 lane。
- 被追蹤彈鎖定且手上有護盾/油漬 → 立刻使用(油漬往後擋)。
- 用道具時機:加速 = 直線段且前方 40 m 曲率小;直線彈 = 前車在正前方 ±5° 且 < 40 m;油漬 = 後方 15 m 內有車;追蹤彈 = 拿到 1.0~3.0 s 後隨機使用;護盾 = 保留到被鎖定或在第 1 名時開啟。

### 2.5 四級難度參數(`Level` 0~3,同 Robot2AI 陣列寫法)

| 參數 | 0 簡單 | 1 普通 | 2 困難 | 3 超困難 | 說明 |
|------|------:|------:|------:|------:|------|
| `SpeedMul` | 0.82 | 0.90 | 0.97 | 1.00 | 極速倍率 |
| `GripBelief` | 0.70 | 0.80 | 0.90 | 0.97 | 彎道速度估計的保守程度 |
| `Think` | 0.60 | 0.35 | 0.20 | 0.10 | 決策間隔(秒) |
| `SteerNoise` | 0.12 | 0.07 | 0.03 | 0.01 | 方向盤雜訊(Perlin,非白雜訊) |
| `MistakeRate` | 0.30 | 0.15 | 0.06 | 0.02 | 每圈犯錯次數期望值(晚煞車 0.6 s / 走錯 lane 撞牆) |
| `DriftSkill` | 0.0 | 0.4 | 0.8 | 1.0 | 0 = 不漂移;其餘 = 漂移成功率 |
| `ItemSmart` | 0.2 | 0.5 | 0.8 | 1.0 | 用「好時機」的機率,否則拿到就亂用 |
| `Overtake` | 0.3 | 0.6 | 0.9 | 1.0 | 遇到慢車時嘗試超車的機率 |

新手模式(`GameSession.Rookie`)比照 Robot2AI:另設一組更慢的值(SpeedMul 0.70、無漂移、不用攻擊型道具)。

### 2.6 橡皮筋規則
以「與玩家的沿賽道距離 Δs」(正 = AI 在玩家前面)調整 **AI 的極速倍率**,不是直接改位置:
- Δs > +80 m:`×(1 − 0.10 × 強度)`;Δs < −80 m:`×(1 + 0.12 × 強度)`;中間線性內插,0 附近不調整(±15 m 死區)。
- 強度依難度:0 簡單 = 1.0(讓玩家追得上/甩得掉)、1 = 0.7、2 = 0.4、3 超困難 = 0.15。
- **最後一圈最後 25% 橡皮筋關閉**(避免終點前被不合理追上)。
- 玩家看得見的 AI(相機視野 60 m 內)上限 ×1.06,避免明顯「瞬移」感。
- 道具表依名次發放本身就是第二層橡皮筋;兩者疊加後由自動測試確認(見 5)。

---

## 3. 對外介面契約

```csharp
namespace FrcSim.Racing
{
    // AI 與玩家共用:KartController 每個 FixedUpdate 讀一次
    public interface IKartInput
    {
        float Throttle { get; }   // -1(倒車/煞車)~ +1
        float Steer { get; }      // -1 左 ~ +1 右
        bool Drift { get; }       // 按住
        bool UseItemDown { get; } // 這一幀剛按下
        bool AimBack { get; }     // 道具往後丟/射
        void Tick(float dt);      // 由 RaceManager 在 KartController 之前呼叫
    }

    public sealed class PlayerKartInput : IKartInput   // 讀 Pad:RT 油門、LT 煞車、LX 方向、RB 漂移、A 道具、LY<-0.5 往後;沒手把時讀鍵盤
    { public PlayerKartInput(bool useOperatorPad = false); /* 實作 IKartInput */ }

    public enum ItemType { None, Boost, TripleBoost, Shield, Oil, Bumper, Homing }

    public class ItemSystem : MonoBehaviour
    {
        public static ItemSystem I { get; }
        public ItemType Held(KartController k);
        public int Charges(KartController k);
        public bool IsRolling(KartController k);           // 抽獎中
        public void OnBoxHit(KartController k);            // ItemBox 觸發時呼叫
        public bool TryUse(KartController k, bool aimBack);
        public void ApplyHit(KartController victim, float stunSec, KartController by); // 檢查護盾/無敵後才呼叫 k.Stun
        public IReadOnlyList<Hazard> ActiveHazards { get; } // 給 AI 避障
        public event System.Action<KartController, ItemType> ItemGot, ItemUsed;
        public static ItemType Roll(int place, int count, System.Random rng); // 純函式,方便測試
    }

    public class RaceAI : MonoBehaviour, IKartInput
    {
        public int Level = 1;                  // 0~3
        public float LanePersonality;          // -1~+1,生成時隨機
        public int Seed;                       // 決定雜訊/犯錯,自動測試可重現
        public KartController Kart;
        public float RubberbandMul { get; }    // 給 HUD 偵錯 / 測試讀
        public string DebugState { get; }      // "Follow/Overtake/Avoid/Recover"
        // IKartInput 實作...
    }
}
```

### 3.1 需要 KartController 提供
| 成員 | 用途 |
|------|------|
| `Vector3 Position`、`Vector3 Forward`、`float Speed`(m/s)、`float TopSpeed` | 追線、速度控制 |
| `float SpeedMul { get; set; }` | 橡皮筋、加速道具(由 RaceManager 每幀乘總) |
| `float GripMul { get; set; }` | 油漬、加速期間抓地 |
| `void Stun(float sec)`、`bool IsStunned`、`bool Invulnerable` | 被命中 |
| `bool Shielded { get; set; }` | 護盾 |
| `void Boost(float mul, float sec)` | 加速道具 |
| `IKartInput Input { get; set; }` | 玩家/AI 切換(Demo 也能讓 AI 開玩家的車,同 Robot2AI.Blue) |
| `int Place`、`int Lap`、`float TrackS` | 由 RaceManager 寫入,道具表與橡皮筋讀取 |

### 3.2 需要 Track 提供
| 成員 | 用途 |
|------|------|
| `float Length` | 一圈總長 |
| `float ProjectToCenterline(Vector3 p, float hintS, out float lateral)` | 求 s 與橫向偏移;用 hintS 局部搜尋,O(1) 不掃全圈 |
| `Vector3 PointAt(float s)`、`Vector3 TangentAt(float s)`、`float HalfWidthAt(float s)` | 追線目標點、lane 換算 |
| `float CurvatureAt(float s)`、`float MaxCurvature(float s0, float s1)` | 彎前減速、漂移判斷(帶正負號 = 左右彎) |
| `int CheckpointCount`、`int NextCheckpoint(float s)` | 名次、防逆走 |
| `Pose RespawnPose(int checkpoint)` | 卡住重置 |
| `IReadOnlyList<Vector3> ItemBoxRows` | 道具箱位置 |

開放世界注意:玩家可離開賽道,但名次只依 `TrackS`(投影到中心線);離中心線 > 半路寬 × 3 超過 4 s 時 HUD 提示「回到賽道」,不強制拉回。

---

## 4. 新檔案清單(`Assets/Scripts/Racing/`)

| 檔案 | 責任 |
|------|------|
| `IKartInput.cs` | 輸入介面 |
| `PlayerKartInput.cs` | Pad/Pad2/鍵盤 → IKartInput |
| `RaceAI.cs` | 追線、速度、漂移、超車、避障、用道具、難度陣列 |
| `Rubberband.cs` | 依 Δs、圈數、難度算 SpeedMul(純函式 static class,好測) |
| `ItemSystem.cs` | 持有/抽獎/使用/命中判定、`Roll` |
| `ItemTable.cs` | 名次機率表與數值常數 |
| `ItemBox.cs` | 道具箱生成、旋轉、重生、觸發 |
| `Projectiles.cs` | 直線彈(反彈)、追蹤彈(沿中心線)、油漬 Hazard |
| `ItemFx.cs` | 噴焰、護盾球、暈眩旋轉、鎖定警告(包 `Fx`/`Juice`/`SfxBus`/`Rumble`) |
| `RaceTest.cs` | `-racetest` 自動測試(見 5) |

---

## 5. 風險與自動測試

### 5.1 風險
| 風險 | 對策 |
|------|------|
| 橡皮筋太明顯,玩家覺得被作弊 | 只調極速不調位置;視野內上限 ×1.06;最後 25% 關閉 |
| 追蹤彈太強(第 1 名一直被打) | 同場上限 2 顆、命中後 1.5 s 無敵、第 1 名護盾機率高 |
| AI 在急彎集體撞牆 / 卡住 | GripBelief < 1、卡住偵測 + 重置到檢查點,沿用 Robot2AI 做法 |
| `ProjectToCenterline` 在 8 字形交叉處跳錯段 | 一定帶 hintS 只在 ±30 m 局部搜尋 |
| 8 車 + 彈道的效能 | AI 決策用 `Think` 間隔而不是每幀;投射物用物件池 |
| 隨機造成測試不穩 | 所有隨機都用 `System.Random(Seed)`,不用 `UnityEngine.Random` |

### 5.2 自動測試(`-batchmode -nographics`)
仿照 `Robot2AI` 的 `-aitest`:命令列參數 `-racetest -racelaps 3 -raceseed 42 -racelevel 2 -racetimescale 4`。
- 玩家車也換成 `RaceAI`(Level 1)代打,以固定 `Time.fixedDeltaTime` 加速跑完整場。
- 每秒記一行:`t 名次 車號 lap s speed item state rubber`;比賽結束寫 `racetest-L{level}-S{seed}.txt` 到專案根目錄(同 `aitest-L*.txt` 位置),內容有最終名次、各車完賽時間、道具使用次數、命中次數、卡住重置次數,然後 `Application.Quit(失敗 ? 1 : 0)`。
- 自動判定(任一失敗 → exit code 1):
  1. 所有車在 `圈長 × 圈數 / (0.5 × 極速)` 秒內完賽。
  2. 每台車卡住重置 ≤ 2 次/場。
  3. 機率表每列總和 = 100;`Roll` 抽 10 萬次,各道具比例誤差 < 1%。
  4. 難度單調:Level 3 平均完賽時間 < Level 0(各跑 5 個 seed)。
  5. 橡皮筋:Level 0 第 1 名與最後一名完賽時間差 < 20 s;關掉道具與橡皮筋時差距要明顯更大(證明機制有作用)。
  6. 同一個 seed 跑兩次,名次與完賽時間完全相同(可重現)。
- 批次腳本跑 4 難度 × 5 seed = 20 場,彙整成 `racetest-summary.txt`,可接在現有 `SelfTest`/`SoakTest` 流程後面。
