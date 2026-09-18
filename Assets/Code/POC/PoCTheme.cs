using System;
using UnityEngine;
using UnityEngine.UI;

namespace LoRClone.View
{
    /// <summary>
    /// MỐC 13 — THEME dùng chung cho Con Đường Anh Hùng: bảng màu "Tối Lam Băng (Honkai)"
    /// + BỘ SINH SPRITE (ngôi sao, mũi tên, kim cương, vòng, đĩa) vẽ THẬT bằng texture.
    ///
    /// Lý do: font TMP thiếu glyph ★ ▼ ◆ ⬡ → hiện ô vuông. Ở đây vẽ hình thật = hết ô vuông,
    /// đồng thời tất cả view đọc chung 1 bảng màu → nhất quán, sửa 1 chỗ đổi toàn bộ.
    /// Style: PHẲNG & SẠCH (không gradient nặng/glow) — chỉ màu đặc + bo tròn + viền mảnh.
    /// </summary>
    public static class PoCTheme
    {
        // ── Bảng màu ──────────────────────────────────────────────
        public static readonly Color Bg0 = Hex("0B1A2E"); // nền sâu nhất
        public static readonly Color Bg1 = Hex("102A44"); // nền panel lớn
        public static readonly Color Surface = Hex("14304A"); // thẻ/panel nổi
        public static readonly Color Surface2 = Hex("1C4064"); // node/ô nổi hơn
        public static readonly Color Line = Hex("2E5273"); // đường/viền mảnh
        public static readonly Color Cyan = Hex("4FC5E0"); // nhấn chính (lạnh)
        public static readonly Color Gold = Hex("E7B24B"); // nhấn phụ (điểm nhấn/target)
        public static readonly Color Cream = Hex("EAF2F8"); // chữ sáng
        public static readonly Color TextDim = Hex("8AA4BC"); // chữ phụ
        public static readonly Color Success = Hex("46C08A"); // đã qua
        public static readonly Color Danger = Hex("D9556A"); // khoá/cảnh báo
        public static readonly Color Locked = Hex("233748"); // node khoá
        public static readonly Color Ink = Hex("081521"); // chữ trên nền vàng/cyan

        // ── V3 "Bản Đồ Cổ" (parchment) ────────────────────────────
        public static readonly Color Paper = Hex("E7D6B0");
        public static readonly Color PaperMid = Hex("CBB07A");
        public static readonly Color InkBrown = Hex("4A3316"); // chữ/nét trên giấy
        public static readonly Color Seal = Hex("8A5A30"); // con dấu thường
        public static readonly Color SealDark = Hex("5A3A1C"); // viền dấu
        public static readonly Color SealGold = Hex("B98F3C"); // dấu thưởng/elite
        public static readonly Color SealRed = Hex("9C3A22"); // dấu boss
        public static readonly Color SealGreen = Hex("5C6E30"); // dấu đã qua

        // ── Helpers màu ───────────────────────────────────────────
        public static Color Hex(string h)
        {
            if (ColorUtility.TryParseHtmlString("#" + h, out var c)) return c;
            return Color.magenta;
        }
        public static Color A(Color c, float a) { c.a = a; return c; }
        public static Color Lerp(Color a, Color b, float t) => Color.Lerp(a, b, t);

        // Màu độ khó theo sao 0★→10★ (xanh băng → cyan → vàng → đỏ).
        public static Color DiffColor(float star)
        {
            float t = Mathf.Clamp01(star / 10f);
            if (t < 0.5f) return Color.Lerp(Success, Cyan, t / 0.5f);
            return Color.Lerp(Cyan, Danger, (t - 0.5f) / 0.5f);
        }

        // ── Sinh sprite (cache) ───────────────────────────────────
        static Sprite _star, _diamond, _triangle, _ring, _disc, _rounded;

        public static Sprite Star() => _star ?? (_star = Build(64, StarA));
        public static Sprite Diamond() => _diamond ?? (_diamond = Build(64, DiamondA));
        public static Sprite Triangle() => _triangle ?? (_triangle = Build(64, TriUpA)); // mặc định chỉ LÊN
        public static Sprite Ring() => _ring ?? (_ring = Build(96, RingA));
        public static Sprite Disc() => _disc ?? (_disc = Build(96, DiscA));
        public static Sprite Rounded() => _rounded ?? (_rounded = BuildRounded(48, 14));

        // insideness → alpha, toạ độ chuẩn hoá [-1,1], tâm 0.
        static float StarA(float x, float y)
        {
            float ang = Mathf.Atan2(y, x) - Mathf.PI / 2f;   // 1 đỉnh hướng lên
            float r = Mathf.Sqrt(x * x + y * y) * 1.06f;      // chừa mép
            float step = Mathf.PI * 2f / 5f;
            float a = Mathf.Repeat(ang, step);
            float h = step * 0.5f;
            float pa = a <= h ? a / h : (step - a) / h;        // 0 ở đỉnh, 1 ở đáy
            float boundary = Mathf.Lerp(1f, 0.46f, pa);
            return Mathf.Clamp01((boundary - r) / 0.06f + 0.5f);
        }

        static float DiamondA(float x, float y)
            => Mathf.Clamp01((0.92f - (Mathf.Abs(x) + Mathf.Abs(y))) / 0.06f + 0.5f);

        static float TriUpA(float x, float y)
        {
            const float top = 0.86f, bot = -0.78f;
            float t = Mathf.InverseLerp(top, bot, y);          // 0 ở đỉnh, 1 ở đáy
            float halfw = Mathf.Lerp(0.02f, 0.82f, Mathf.Clamp01(t));
            float inTri = Mathf.Clamp01((halfw - Mathf.Abs(x)) / 0.06f + 0.5f);
            float inY = Mathf.Clamp01((y - bot) / 0.06f + 0.5f) * Mathf.Clamp01((top - y) / 0.06f + 0.5f);
            return inTri * inY;
        }

        static float RingA(float x, float y)
        {
            float r = Mathf.Sqrt(x * x + y * y);
            float outer = Mathf.Clamp01((0.96f - r) / 0.05f + 0.5f);
            float inner = Mathf.Clamp01((r - 0.74f) / 0.05f + 0.5f);
            return outer * inner;
        }

        static float DiscA(float x, float y)
            => Mathf.Clamp01((0.96f - Mathf.Sqrt(x * x + y * y)) / 0.04f + 0.5f);

        static Sprite Build(int size, Func<float, float, float> alphaFn)
        {
            var tex = new Texture2D(size, size, TextureFormat.RGBA32, false)
            { filterMode = FilterMode.Bilinear, wrapMode = TextureWrapMode.Clamp };
            var px = new Color32[size * size];
            float c = (size - 1) * 0.5f;
            for (int yy = 0; yy < size; yy++)
                for (int xx = 0; xx < size; xx++)
                {
                    float nx = (xx - c) / c, ny = (yy - c) / c;
                    float a = Mathf.Clamp01(alphaFn(nx, ny));
                    px[yy * size + xx] = new Color32(255, 255, 255, (byte)(a * 255f));
                }
            tex.SetPixels32(px); tex.Apply();
            return Sprite.Create(tex, new Rect(0, 0, size, size), new Vector2(0.5f, 0.5f), 100f);
        }

        // Rounded-rect 9-slice (viền bo) — dùng cho panel/thẻ.
        static Sprite BuildRounded(int size, int radius)
        {
            var tex = new Texture2D(size, size, TextureFormat.RGBA32, false)
            { filterMode = FilterMode.Bilinear, wrapMode = TextureWrapMode.Clamp };
            var px = new Color32[size * size];
            for (int y = 0; y < size; y++)
                for (int x = 0; x < size; x++)
                {
                    float dx = Mathf.Max(radius - x, x - (size - 1 - radius), 0);
                    float dy = Mathf.Max(radius - y, y - (size - 1 - radius), 0);
                    float d = Mathf.Sqrt(dx * dx + dy * dy);
                    float a = Mathf.Clamp01(radius - d + 0.5f);
                    px[y * size + x] = new Color32(255, 255, 255, (byte)(a * 255f));
                }
            tex.SetPixels32(px); tex.Apply();
            var s = Sprite.Create(tex, new Rect(0, 0, size, size), new Vector2(0.5f, 0.5f), 100f,
                0, SpriteMeshType.FullRect, new Vector4(radius, radius, radius, radius));
            return s;
        }

        // ── AMPHOREUS (trời đêm + vàng kim) ───────────────────────
        public static readonly Color Obsid = Hex("0A0F1E");
        public static readonly Color ObsidLit = Hex("16203C");
        public static readonly Color AmGold = Hex("E8C06A");
        public static readonly Color AmGoldHi = Hex("F5D98A");
        public static readonly Color AmCream = Hex("EAF0FF");
        public static readonly Color AmDim = Hex("9FB0D0");
        public static readonly Color AmRed = Hex("C0432E");
        public static readonly Color AmGreen = Hex("4FB07E");
        public static readonly Color NodeNavy = Hex("141D38");
        public static readonly Color PlaqNavy = Hex("0A1226");

        // Nền TRỜI ĐÊM: xanh obsidian sáng giữa → tối mép + quầng vàng nhẹ phía trên.
        static Sprite _night;
        public static Sprite NightSky() => _night ?? (_night = BuildNight(160));
        static Sprite BuildNight(int size)
        {
            var tex = new Texture2D(size, size, TextureFormat.RGBA32, false)
            { wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Bilinear };
            var px = new Color32[size * size];
            for (int y = 0; y < size; y++)
                for (int x = 0; x < size; x++)
                {
                    float nx = x / (float)size, ny = y / (float)size;
                    float dx = nx - 0.5f, dy = ny - 0.45f;
                    float d = Mathf.Sqrt(dx * dx + dy * dy);
                    Color c = Color.Lerp(ObsidLit, Obsid, Mathf.Clamp01(d * 1.5f));
                    // quầng vàng mờ ở vùng trên-giữa
                    float gx = nx - 0.5f, gy = ny - 0.72f;
                    float gd = Mathf.Sqrt(gx * gx + gy * gy);
                    float glow = Mathf.Clamp01(1f - gd * 3.2f) * 0.18f;
                    c = Color.Lerp(c, AmGold, glow);
                    c.a = 1f;
                    px[y * size + x] = c;
                }
            tex.SetPixels32(px); tex.Apply();
            return Sprite.Create(tex, new Rect(0, 0, size, size), new Vector2(0.5f, 0.5f), 100f);
        }

        // Nền GIẤY DA: mottle bằng Perlin + tối dần ở mép (vignette). Cache 1 lần.
        static Sprite _parch;
        public static Sprite Parchment() => _parch ?? (_parch = BuildParchment(256));
        static Sprite BuildParchment(int size)
        {
            var tex = new Texture2D(size, size, TextureFormat.RGBA32, false)
            { wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Bilinear };
            var px = new Color32[size * size];
            Color edge = Hex("3A2A12");
            for (int y = 0; y < size; y++)
                for (int x = 0; x < size; x++)
                {
                    float nx = x / (float)size, ny = y / (float)size;
                    float m = Mathf.PerlinNoise(nx * 5f, ny * 5f) * 0.6f + Mathf.PerlinNoise(nx * 17f, ny * 17f) * 0.4f;
                    Color c = Color.Lerp(PaperMid, Paper, m);
                    float dx = nx - 0.5f, dy = ny - 0.5f;
                    float d = Mathf.Sqrt(dx * dx + dy * dy);
                    float vig = Mathf.Clamp01(1f - Mathf.Max(0f, d - 0.28f) * 1.9f); // sáng giữa, tối mép
                    c = Color.Lerp(edge, c, vig);
                    c.a = 1f;
                    px[y * size + x] = c;
                }
            tex.SetPixels32(px); tex.Apply();
            return Sprite.Create(tex, new Rect(0, 0, size, size), new Vector2(0.5f, 0.5f), 100f);
        }

        // ── Đặt 1 Image sprite vào parent theo anchor (0-1) ───────
        public static Image Put(RectTransform parent, string name, Vector2 aMin, Vector2 aMax,
            Sprite sp, Color col, float rotZ = 0f, bool preserveAspect = true)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            var rt = go.AddComponent<RectTransform>();
            rt.anchorMin = aMin; rt.anchorMax = aMax; rt.offsetMin = rt.offsetMax = Vector2.zero;
            if (Mathf.Abs(rotZ) > 0.01f) rt.localEulerAngles = new Vector3(0, 0, rotZ);
            var img = go.AddComponent<Image>();
            img.sprite = sp; img.color = col; img.preserveAspect = preserveAspect; img.raycastTarget = false;
            return img;
        }

        // Panel/thẻ bo góc màu 'col'. Có 'border' → vẽ viền mảnh (fill thụt vào 2px).
        public static Image Panel(RectTransform parent, string name, Vector2 aMin, Vector2 aMax,
            Color col, Color? border = null)
        {
            if (border.HasValue)
            {
                var bd = Put(parent, name + "_bd", aMin, aMax, Rounded(), border.Value, 0f, false);
                bd.type = Image.Type.Sliced;
                var fill = Put((RectTransform)bd.transform, name + "_fill", Vector2.zero, Vector2.one, Rounded(), col, 0f, false);
                fill.type = Image.Type.Sliced;
                var frt = (RectTransform)fill.transform;
                frt.offsetMin = new Vector2(2f, 2f); frt.offsetMax = new Vector2(-2f, -2f);
                return fill;
            }
            var img = Put(parent, name, aMin, aMax, Rounded(), col, 0f, false);
            img.type = Image.Type.Sliced;
            return img;
        }

        // Hàng SAO rating: 'total' sao, tô vàng 'filled' cái đầu.
        public static void StarRating(RectTransform parent, int filled, int total,
            Vector2 aMin, Vector2 aMax)
        {
            if (total <= 0) return;
            float w = (aMax.x - aMin.x) / total;
            for (int i = 0; i < total; i++)
            {
                float x0 = aMin.x + i * w, x1 = x0 + w;
                Put(parent, "St_" + i, new Vector2(x0, aMin.y), new Vector2(x1, aMax.y),
                    Star(), i < filled ? Gold : A(Cream, 0.20f));
            }
        }
    }
}