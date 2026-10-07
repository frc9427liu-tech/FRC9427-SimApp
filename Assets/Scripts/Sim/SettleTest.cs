using System.Collections;
using System.IO;
using UnityEngine;

namespace FrcSim
{
    // 啟動參數 -settletest:球剛擺好 1 秒後記錄位置,15 秒後比對,統計「自己滾動」的球數,寫 settletest.txt 後離開。
    public class SettleTest : MonoBehaviour
    {
        IEnumerator Start()
        {
            yield return new WaitForSeconds(1.0f);
            var f0 = new System.Collections.Generic.List<Fuel>(FuelManager.All);
            var p0 = new Vector3[f0.Count];
            for (int i = 0; i < f0.Count; i++) p0[i] = f0[i].transform.position;
            yield return new WaitForSeconds(15f);
            int moved = 0, outside = 0; float maxD = 0f; float sum = 0f;
            for (int i = 0; i < f0.Count; i++)
            {
                if (f0[i] == null) continue;
                Vector3 p = f0[i].transform.position;
                float d = new Vector2(p.x - p0[i].x, p.z - p0[i].z).magnitude;
                sum += d; if (d > maxD) maxD = d;
                if (d > 0.3f) moved++;
                if (p.x < -0.1f || p.x > SimConstants.FieldLength + 0.1f || p.z < -0.1f || p.z > SimConstants.FieldWidth + 0.1f || p.y < -0.5f) outside++;
            }
            string r = $"balls={f0.Count}\nmoved_more_than_0.3m={moved}\nmax_move_m={maxD:0.00}\navg_move_m={sum / Mathf.Max(1, f0.Count):0.000}\noutside_field={outside}\n";
            File.WriteAllText(Path.Combine(Path.GetDirectoryName(Application.dataPath), "settletest.txt"), r);
            Application.Quit();
        }
    }
}
