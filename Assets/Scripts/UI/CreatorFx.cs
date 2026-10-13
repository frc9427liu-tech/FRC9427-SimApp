using UnityEngine;
using UnityEngine.UI;

namespace FrcSim
{
    // 「創作者」專屬動態效果:金色漸層脈動 + 掃光(Shimmer),頭像外框發光脈動(Pulse)。只掛在 Account.IsCreator 的畫面上。
    public class CreatorFx : MonoBehaviour
    {
        public enum Kind { Pulse, Shimmer }
        public Kind K; public Image Img; public RectTransform Bar; public float W = 400f; public Color A = new Color(0.95f, 0.62f, 0.08f, 1f), B = new Color(1f, 0.93f, 0.50f, 1f); public float Speed = 2.0f;

        void Update()
        {
            float t = Time.unscaledTime;
            if (K == Kind.Pulse && Img != null) Img.color = Color.Lerp(A, B, (Mathf.Sin(t * Speed) + 1f) * 0.5f);
            else if (K == Kind.Shimmer && Bar != null) { float p = Mathf.Repeat(t * 0.5f, 1.5f) / 1.5f; var ap = Bar.anchoredPosition; ap.x = Mathf.Lerp(-90f, W + 30f, p); Bar.anchoredPosition = ap; }
        }

        // 金色動態稱號條:「★ CREATOR 創作者 · FRC9427」
        public static void Chip(RectTransform parent, float x, float y, float w, float h)
        {
            var chip = UiKit.Glass("CreatorChip", parent, new Color(0.95f, 0.62f, 0.08f, 1f), h / 2f); UiKit.PlaceTL(chip.rectTransform, x, y, w, h);
            chip.gameObject.AddComponent<Mask>().showMaskGraphic = true;
            var fx = chip.gameObject.AddComponent<CreatorFx>(); fx.K = Kind.Pulse; fx.Img = chip; fx.Speed = 2.2f;
            var bar = UiKit.Img("Shine", chip.transform, new Color(1f, 1f, 0.85f, 0.55f)); UiKit.PlaceTL(bar.rectTransform, -90f, 0f, 46f, h);
            bar.rectTransform.localRotation = Quaternion.Euler(0, 0, 0);
            var sh = chip.gameObject.AddComponent<CreatorFx>(); sh.K = Kind.Shimmer; sh.Bar = bar.rectTransform; sh.W = w;
            var t = UiKit.Label("CreatorT", chip.transform, "★ CREATOR 創作者 · FRC9427", Mathf.RoundToInt(h * 0.52f), new Color(0.30f, 0.16f, 0.0f), TextAnchor.MiddleCenter); t.fontStyle = FontStyle.Bold;
            t.resizeTextForBestFit = true; t.resizeTextMinSize = 10; t.resizeTextMaxSize = Mathf.RoundToInt(h * 0.52f);
            UiKit.PlaceTL(t.rectTransform, 6, 0, w - 12, h);
        }

        // 頭像外框:金色脈動發光(在頭像後面再墊一圈較大的半透明金光)
        public static void Glow(RectTransform parent, float x, float y, float size)
        {
            var g = UiKit.Glass("CreatorGlow", parent, new Color(1f, 0.8f, 0.2f, 0.35f), (size + 36f) / 2f); UiKit.PlaceTL(g.rectTransform, x - 18f, y - 18f, size + 36f, size + 36f);
            var fx = g.gameObject.AddComponent<CreatorFx>(); fx.K = Kind.Pulse; fx.Img = g; fx.A = new Color(1f, 0.78f, 0.15f, 0.12f); fx.B = new Color(1f, 0.9f, 0.4f, 0.55f); fx.Speed = 2.2f;
        }
        public static void RingPulse(Image ring)
        {
            var rf = ring.gameObject.AddComponent<CreatorFx>(); rf.K = Kind.Pulse; rf.Img = ring; rf.A = new Color(0.95f, 0.62f, 0.08f, 1f); rf.B = new Color(1f, 0.95f, 0.55f, 1f); rf.Speed = 2.2f;
        }
    }
}
