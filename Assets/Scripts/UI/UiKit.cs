using System;
using UnityEngine;
using UnityEngine.UI;

namespace FrcSim
{
    // 主題色集中在這裡,之後想調顏色只改這一個檔
    public static class UiTheme
    {
        public static Color Backdrop = new Color(0.02f, 0.03f, 0.05f, 0.82f);
        public static Color Panel = new Color(0.05f, 0.08f, 0.12f, 1f);
        public static Color Line = new Color(0.30f, 0.50f, 0.75f, 0.55f);
        public static Color Accent = new Color(0.25f, 0.65f, 1.00f, 1.00f);
        public static Color Text = new Color(0.92f, 0.95f, 1.00f, 1.00f);
        public static Color TextDim = new Color(0.58f, 0.65f, 0.75f, 1.00f);
        public static Color Disabled = new Color(0.38f, 0.42f, 0.48f, 1.00f);

        static Font font;
        public static Font Font
        {
            get
            {
                if (font == null)
                    font = Font.CreateDynamicFontFromOSFont(
                        new[] { "Microsoft JhengHei UI", "Microsoft JhengHei", "Segoe UI", "Arial" }, 32);
                return font;
            }
        }
    }

    public static class UiKit
    {
        public static RectTransform Rect(string name, Transform parent)
        {
            var g = new GameObject(name, typeof(RectTransform));
            g.transform.SetParent(parent, false);
            return g.GetComponent<RectTransform>();
        }

        public static Image Img(string name, Transform parent, Color c)
        {
            var rt = Rect(name, parent);
            var im = rt.gameObject.AddComponent<Image>();
            im.color = c;
            im.raycastTarget = false;
            return im;
        }

        // ---- 液態玻璃用的圓角精靈(9-slice):實心圓角 / 細邊框環;radius 用 Image.pixelsPerUnitMultiplier 縮放
        static Sprite solidSprite, ringSprite;
        const int GN = 64, GR = 28;
        static Sprite MakeRound(bool ring)
        {
            var tex = new Texture2D(GN, GN, TextureFormat.RGBA32, false) { filterMode = FilterMode.Bilinear, wrapMode = TextureWrapMode.Clamp };
            var px = new Color32[GN * GN];
            for (int y = 0; y < GN; y++)
                for (int x = 0; x < GN; x++)
                {
                    float dx = Mathf.Max(GR - x - 0.5f, x + 0.5f - (GN - GR), 0f);
                    float dy = Mathf.Max(GR - y - 0.5f, y + 0.5f - (GN - GR), 0f);
                    float d = Mathf.Sqrt(dx * dx + dy * dy);
                    float a = Mathf.Clamp01(GR + 0.5f - d);
                    if (ring) a = Mathf.Min(a, Mathf.Clamp01(d - (GR - 3f) + 0.5f)) * (d >= GR - 3f ? 1f : 0f);
                    // 直邊上的環:距邊 < 3px
                    if (ring && d <= 0f) { float e = Mathf.Min(Mathf.Min(x + 0.5f, GN - x - 0.5f), Mathf.Min(y + 0.5f, GN - y - 0.5f)); a = Mathf.Clamp01(3f - e + 0.5f); }
                    px[y * GN + x] = new Color(1f, 1f, 1f, a);
                }
            tex.SetPixels32(px); tex.Apply();
            return Sprite.Create(tex, new Rect(0, 0, GN, GN), new Vector2(0.5f, 0.5f), 100f, 0, SpriteMeshType.FullRect, new Vector4(GR, GR, GR, GR));
        }
        public static Image Glass(string name, Transform parent, Color c, float radius, bool ring = false)
        {
            if (solidSprite == null) { solidSprite = MakeRound(false); ringSprite = MakeRound(true); }
            var im = Img(name, parent, c);
            im.sprite = ring ? ringSprite : solidSprite;
            im.type = Image.Type.Sliced;
            im.pixelsPerUnitMultiplier = GR / Mathf.Max(4f, radius);
            return im;
        }

        public static Text Label(string name, Transform parent, string s, int size, Color c, TextAnchor a)
        {
            var rt = Rect(name, parent);
            var t = rt.gameObject.AddComponent<Text>();
            t.font = UiTheme.Font;
            t.fontSize = size;
            t.color = c;
            t.alignment = a;
            t.horizontalOverflow = HorizontalWrapMode.Overflow;
            t.verticalOverflow = VerticalWrapMode.Overflow;
            t.raycastTarget = false;
            t.supportRichText = false;
            t.text = s;
            return t;
        }

        // 以父物件左上角為原點的絕對定位(x 向右、y 向下)
        public static void PlaceTL(RectTransform rt, float x, float y, float w, float h)
        {
            rt.anchorMin = rt.anchorMax = new Vector2(0f, 1f);
            rt.pivot = new Vector2(0f, 1f);
            rt.anchoredPosition = new Vector2(x, -y);
            rt.sizeDelta = new Vector2(w, h);
        }

        public static void Stretch(RectTransform rt)
        {
            rt.anchorMin = Vector2.zero; rt.anchorMax = Vector2.one;
            rt.offsetMin = rt.offsetMax = Vector2.zero;
        }
    }

    public class UiButton
    {
        public RectTransform Rt;
        public Image Bg, Bar;
        public Text Label;
        public Func<string> TextFn;
        public Action OnClick;
        public bool Enabled = true;

        public void Refresh(bool selected)
        {
            Label.text = TextFn();
            Bg.color = selected && Enabled ? new Color(0.45f, 0.72f, 1f, 0.30f)
                                           : new Color(1f, 1f, 1f, Enabled ? 0.07f : 0.03f);
            Bar.color = selected && Enabled ? new Color(1f, 1f, 1f, 0.85f) : new Color(1f, 1f, 1f, Enabled ? 0.22f : 0.10f);
            Label.color = !Enabled ? UiTheme.Disabled : selected ? UiTheme.Text : UiTheme.TextDim;
        }
    }
}
