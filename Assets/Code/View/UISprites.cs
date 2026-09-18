using UnityEngine;

namespace LoRClone.View
{
    /// <summary>
    /// Sinh sprite UI bằng code (tròn + bo góc 9-slice) — thay cho Resources.GetBuiltinResource
    /// (UI/Skin/Knob.psd, UISprite.psd) vốn KHÔNG có trong nhiều phiên bản Unity / URP.
    /// Sprite trắng, tint bằng Image.color. Cache tĩnh, chỉ sinh 1 lần.
    /// </summary>
    public static class UISprites
    {
        static Sprite _circle, _round;

        /// <summary>Hình tròn mịn (anti-alias viền). Dùng cho nút tròn / núm slider.</summary>
        public static Sprite Circle()
        {
            if (_circle) return _circle;
            int S = 128;
            var t = new Texture2D(S, S, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp };
            float r = S / 2f - 1f;
            var c = new Vector2(S / 2f, S / 2f);
            for (int y = 0; y < S; y++)
                for (int x = 0; x < S; x++)
                {
                    float d = Vector2.Distance(new Vector2(x + 0.5f, y + 0.5f), c);
                    float a = Mathf.Clamp01(r - d + 0.5f); // mép mềm
                    t.SetPixel(x, y, new Color(1f, 1f, 1f, a));
                }
            t.Apply();
            _circle = Sprite.Create(t, new Rect(0, 0, S, S), new Vector2(0.5f, 0.5f), 100f);
            return _circle;
        }

        /// <summary>Chữ nhật bo góc, có border → dùng Image.type = Sliced để co giãn không méo góc.</summary>
        public static Sprite RoundedRect()
        {
            if (_round) return _round;
            int S = 48, rad = 14;
            var t = new Texture2D(S, S, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp };
            for (int y = 0; y < S; y++)
                for (int x = 0; x < S; x++)
                    t.SetPixel(x, y, new Color(1f, 1f, 1f, RoundedAlpha(x, y, S, S, rad)));
            t.Apply();
            _round = Sprite.Create(t, new Rect(0, 0, S, S), new Vector2(0.5f, 0.5f), 100f,
                                   0, SpriteMeshType.FullRect, new Vector4(rad, rad, rad, rad));
            return _round;
        }

        static float RoundedAlpha(int x, int y, int w, int h, int rad)
        {
            float fx = x + 0.5f, fy = y + 0.5f;
            float cx = Mathf.Clamp(fx, rad, w - rad);
            float cy = Mathf.Clamp(fy, rad, h - rad);
            float d = Mathf.Sqrt((fx - cx) * (fx - cx) + (fy - cy) * (fy - cy));
            return Mathf.Clamp01(rad - d + 0.5f);
        }
    }
}
