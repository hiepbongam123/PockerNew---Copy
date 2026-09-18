using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using TMPro;

namespace LoRClone
{
    /// <summary>
    /// Design system trung tâm: bảng màu + font + sprite bo góc + factory + restyle.
    ///
    /// DÙNG (khuyến nghị cho UI dựng runtime):
    ///   1) Dựng menu như cũ, sau đó gọi 1 dòng:  UITheme.ThemeUnder(myPanelTransform);
    ///      → đồng bộ font mọi text + restyle mọi Button bên dưới.
    ///   2) Hoặc tạo mới bằng factory:
    ///        var btn = UITheme.MakeButton(parent, "Chơi", UITheme.Kind.Primary, () => {...});
    ///        var panel = UITheme.MakePanel(parent);
    ///        var h = UITheme.MakeText(parent, "Tiêu đề", UITheme.FS.Header, UITheme.Primary);
    ///
    /// Bỏ qua 1 phần tử: gắn component UIThemeIgnore lên nó.
    /// Ép Kind cho 1 Button khi ThemeUnder: gắn UIThemeKind (chọn kind trong Inspector).
    /// </summary>
    public static class UITheme
    {
        // ── Palette ─────────────────────────────────────────────────────────
        public static readonly Color BgDeep     = Hex("#101319");
        public static readonly Color Panel      = Hex("#1B2130");
        public static readonly Color PanelAlt   = Hex("#262E40");
        public static readonly Color Line       = Hex("#39445F");
        public static readonly Color Primary    = Hex("#E8B65A"); // amber (LoR-ish)
        public static readonly Color PrimaryInk = Hex("#17130A"); // chữ trên nền amber
        public static readonly Color Secondary  = Hex("#3E6FB0"); // blue
        public static readonly Color Danger     = Hex("#C6482F");
        public static readonly Color Success    = Hex("#4E9E68");
        public static readonly Color TextPrimary= Hex("#E8ECF3");
        public static readonly Color TextMuted  = Hex("#97A0B2");
        public static readonly Color White      = Color.white;

        // ── Font sizes ──────────────────────────────────────────────────────
        public static class FS { public const float Title = 40, Header = 28, Sub = 22, Body = 18, Small = 14; }

        // ── Kinds ───────────────────────────────────────────────────────────
        public enum Kind { Primary, Secondary, Ghost, Danger, Success }

        static (Color bg, Color ink) Palette(Kind k)
        {
            switch (k)
            {
                case Kind.Primary:   return (Primary,   PrimaryInk);
                case Kind.Secondary: return (Secondary, White);
                case Kind.Danger:    return (Danger,    White);
                case Kind.Success:   return (Success,   White);
                default:             return (PanelAlt,  TextPrimary); // Ghost
            }
        }

        public static TMP_FontAsset Font => TMP_Settings.defaultFontAsset;

        // ── Factory ─────────────────────────────────────────────────────────
        public static Button MakeButton(Transform parent, string label, Kind kind = Kind.Primary,
                                        System.Action onClick = null, Vector2? size = null)
        {
            var go = new GameObject("Button", typeof(RectTransform), typeof(Image), typeof(Button));
            go.transform.SetParent(parent, false);
            var rt = (RectTransform)go.transform;
            rt.sizeDelta = size ?? new Vector2(180f, 48f);

            var img = go.GetComponent<Image>();
            var btn = go.GetComponent<Button>();
            ApplyButton(btn, kind, img);

            var txt = MakeText(go.transform, label, FS.Body, Palette(kind).ink, TextAlignmentOptions.Center);
            var trt = txt.rectTransform;
            trt.anchorMin = Vector2.zero; trt.anchorMax = Vector2.one;
            trt.offsetMin = new Vector2(10, 6); trt.offsetMax = new Vector2(-10, -6);
            txt.fontStyle = FontStyles.Bold;
            txt.enableAutoSizing = true; txt.fontSizeMin = 12; txt.fontSizeMax = FS.Body;

            if (onClick != null) btn.onClick.AddListener(() => onClick());
            return btn;
        }

        /// <summary>Áp style theme lên 1 Button có sẵn (không đổi size). img=null → tự lấy targetGraphic.</summary>
        public static void ApplyButton(Button btn, Kind kind, Image img = null)
        {
            if (btn == null) return;
            if (img == null) img = btn.targetGraphic as Image ?? btn.GetComponent<Image>();
            var (bg, ink) = Palette(kind);

            if (img != null)
            {
                img.sprite = RoundedSprite();
                img.type = Image.Type.Sliced;
                img.color = White; // màu thực do ColorBlock quyết định
                btn.targetGraphic = img;
            }
            btn.transition = Selectable.Transition.ColorTint;
            var cb = btn.colors;
            cb.normalColor      = bg;
            cb.highlightedColor = Lighten(bg, 0.12f);
            cb.pressedColor     = Darken(bg, 0.14f);
            cb.selectedColor    = bg;
            cb.disabledColor    = new Color(0.4f, 0.4f, 0.45f, 0.6f);
            cb.colorMultiplier  = 1f;
            cb.fadeDuration     = 0.08f;
            btn.colors = cb;

            // đồng bộ chữ trong nút
            var lbl = btn.GetComponentInChildren<TMP_Text>(true);
            if (lbl != null) { if (Font != null) lbl.font = Font; lbl.color = ink; }
        }

        public static Image MakePanel(Transform parent, Color? color = null, int radius = 18)
        {
            var go = new GameObject("Panel", typeof(RectTransform), typeof(Image));
            go.transform.SetParent(parent, false);
            var img = go.GetComponent<Image>();
            img.sprite = RoundedSprite(radius);
            img.type = Image.Type.Sliced;
            img.color = color ?? Panel;
            img.raycastTarget = false;
            return img;
        }

        public static TextMeshProUGUI MakeText(Transform parent, string text, float size,
                                               Color? color = null, TextAlignmentOptions align = TextAlignmentOptions.Left)
        {
            var go = new GameObject("Text", typeof(RectTransform));
            go.transform.SetParent(parent, false);
            var t = go.AddComponent<TextMeshProUGUI>();
            if (Font != null) t.font = Font;
            t.text = text;
            t.fontSize = size;
            t.color = color ?? TextPrimary;
            t.alignment = align;
            t.raycastTarget = false;
            return t;
        }

        // ── Restyle cây có sẵn (gọi sau khi dựng menu) ──────────────────────
        /// <summary>
        /// AN TOÀN (mặc định): CHỈ đồng bộ font mọi TMP + restyle Button có gắn UIThemeKind (opt-in).
        /// KHÔNG tự đổi màu/sprite nút nào khác → không làm đen/che layout bạn tự dựng.
        ///
        /// aggressive=true: mới restyle TẤT CẢ Button (trừ nút có sprite art riêng / UIThemeIgnore).
        /// Chỉ bật khi bạn thực sự muốn ép đồng bộ toàn bộ.
        /// </summary>
        public static void ThemeUnder(Transform root, bool aggressive = false)
        {
            if (root == null) return;

            foreach (var btn in root.GetComponentsInChildren<Button>(true))
            {
                if (btn.GetComponentInParent<UIThemeIgnore>() != null) continue;
                var km = btn.GetComponent<UIThemeKind>();
                if (km != null)                       // opt-in rõ ràng
                    ApplyButton(btn, km.kind);
                else if (aggressive)                  // ép toàn bộ (bỏ nút có art riêng)
                {
                    var img = btn.targetGraphic as Image ?? btn.GetComponent<Image>();
                    if (!HasCustomSprite(img)) ApplyButton(btn, Kind.Ghost, img);
                }
            }

            // Font: an toàn, không che/không đổi màu.
            if (Font != null)
                foreach (var t in root.GetComponentsInChildren<TMP_Text>(true))
                {
                    if (t.GetComponentInParent<UIThemeIgnore>() != null) continue;
                    t.font = Font;
                }
        }

        static readonly HashSet<string> _builtin = new HashSet<string>
        { "UISprite", "Background", "InputFieldBackground", "Knob", "UIMask", "Checkmark", "DropdownArrow" };

        static bool HasCustomSprite(Image img)
        {
            if (img == null || img.sprite == null) return false;   // trống → cho phép đổi
            string n = img.sprite.name ?? "";
            if (n.StartsWith("UITheme")) return false;             // sprite của ta
            if (_builtin.Contains(n)) return false;                // sprite mặc định Unity → cho phép đổi
            return true;                                           // còn lại = art riêng → GIỮ nguyên
        }

        // ── Rounded 9-slice sprite (sinh runtime, cache) ────────────────────
        static readonly Dictionary<int, Sprite> _round = new Dictionary<int, Sprite>();
        public static Sprite RoundedSprite(int radius = 18)
        {
            if (_round.TryGetValue(radius, out var sp) && sp != null) return sp;
            int r = Mathf.Max(2, radius);
            int size = r * 2 + 4;
            var tex = new Texture2D(size, size, TextureFormat.RGBA32, false)
            { filterMode = FilterMode.Bilinear, wrapMode = TextureWrapMode.Clamp, name = "UIThemeRound" };
            var px = new Color32[size * size];
            for (int y = 0; y < size; y++)
                for (int x = 0; x < size; x++)
                    px[y * size + x] = new Color32(255, 255, 255, (byte)(CornerAlpha(x, y, size, r) * 255f));
            tex.SetPixels32(px); tex.Apply();
            sp = Sprite.Create(tex, new Rect(0, 0, size, size), new Vector2(0.5f, 0.5f), 100f, 0,
                               SpriteMeshType.FullRect, new Vector4(r, r, r, r));
            sp.name = "UIThemeRound" + radius;
            _round[radius] = sp;
            return sp;
        }

        static float CornerAlpha(int x, int y, int size, int r)
        {
            bool lr = x < r || x > size - 1 - r;
            bool bt = y < r || y > size - 1 - r;
            if (!lr || !bt) return 1f; // không thuộc góc
            float cx = x < r ? r : size - 1 - r;
            float cy = y < r ? r : size - 1 - r;
            float d = Mathf.Sqrt((x - cx) * (x - cx) + (y - cy) * (y - cy));
            return Mathf.Clamp01(r - d + 0.5f); // AA 1px
        }

        // ── Helpers ─────────────────────────────────────────────────────────
        public static Color Lighten(Color c, float t) => Color.Lerp(c, White, t);
        public static Color Darken(Color c, float t) => Color.Lerp(c, Color.black, t);
        static Color Hex(string h) => ColorUtility.TryParseHtmlString(h, out var c) ? c : Color.magenta;
    }

    /// <summary>Gắn lên phần tử để ThemeUnder BỎ QUA nó (và con của nó).</summary>
    public class UIThemeIgnore : MonoBehaviour { }

    /// <summary>Gắn lên Button để ép Kind khi ThemeUnder restyle.</summary>
    public class UIThemeKind : MonoBehaviour { public UITheme.Kind kind = UITheme.Kind.Primary; }
}
