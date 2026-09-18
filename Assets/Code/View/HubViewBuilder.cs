using LoRClone.View;
using TMPro;
using UnityEngine;
using UnityEngine;
using UnityEngine.UI;
using TMPro;
using LoRClone.Controller;   // GameController.Instance / .model  (đã có sẵn trong project bạn)
using LoRClone.Model;        // GameModel

namespace LoRClone.View
{
    /// <summary>
    /// CHỈ là VIEW: dựng hubview (chrome + HUDView đầy đủ field) trên CANVAS của BẠN.
    /// KHÔNG tạo canvas, KHÔNG tạo/ghi đè zone hay GameView. Đây CHỈ là view + HUD.
    ///
    /// HUD CHẠY THẬT: Update() đọc GameController.Instance.model mỗi frame → hud.Render(model).
    /// (Không dùng event OnStateChanged để tránh sai kiểu → không bao giờ lỗi compile.)
    ///
    /// DÙNG: đặt GameObject này làm CON của Canvas game (hoặc kéo Canvas vào 'targetCanvas') → Play.
    /// (⋮ component → Rebuild để dựng lại trong edit mode.)
    /// </summary>
    public class HubViewBuilder : MonoBehaviour
    {
        const float DESIGN_H = 481f;
        static float S;
        const float BW = 882f, BH = 481f;

        [Header("★ Canvas của BẠN — kéo Canvas game vào đây. (Hoặc đặt GameObject này làm CON của Canvas.)")]
        [Tooltip("KHÔNG tạo canvas mới. Nếu để trống → tự lấy Canvas cha của GameObject này.")]
        public Canvas targetCanvas;

        [Header("Art thật (tuỳ chọn)")]
        public Sprite bossBackground, enemyAvatarArt, playerAvatarArt, redGemArt, blueGemArt;

        [Header("HUDView tạo ra (tự động dùng ở Play — không cần gán đi đâu)")]
        public HUDView hud;

        RectTransform _board;
        bool _hudInited;

        static Color Bezel1 = C(0x60, 0x67, 0x71), Bezel2 = C(0x2C, 0x31, 0x39);
        static Color Bar1 = C(0x74, 0x7B, 0x85), Bar2 = C(0x24, 0x28, 0x2F);
        static Color Groove1 = C(0x24, 0x29, 0x31), Groove2 = C(0x11, 0x14, 0x1A);
        static Color Gold = C(0xD8, 0xB4, 0x5A);
        static Color PillR1 = C(0xE2, 0x53, 0x6A), PillR2 = C(0x8E, 0x15, 0x26);
        static Color PillG1 = C(0x3F, 0xC4, 0x7E), PillG2 = C(0x0F, 0x6A, 0x3C);
        static Color Panel1 = C(0x62, 0x69, 0x73), Panel2 = C(0x30, 0x35, 0x3D);
        static Color Ink = C(0xEE, 0xF2, 0xF7);

        void Start() { Build(); }
        [ContextMenu("Rebuild")] void Rebuild() { if (_board) DestroyImmediate(_board.gameObject); _hudInited = false; Build(); }

        // ── HUD sống: đọc model mỗi frame, render. An toàn, không cần GameView đụng tới. ──
        void Update()
        {
            if (hud == null) return;
            var gc = GameController.Instance;
            if (gc == null || gc.model == null) return;
            if (!_hudInited) { hud.Init(); _hudInited = true; }
            hud.Render(gc.model);
        }

        void Build()
        {
            var canvas = EnsureCanvas();   // canvas CỦA BẠN — KHÔNG tạo mới
            if (canvas == null)
            {
                Debug.LogError("[HubViewBuilder] Không tìm thấy Canvas! Kéo Canvas game vào field 'targetCanvas', " +
                               "hoặc đặt GameObject này làm CON của Canvas. (Tôi KHÔNG tự tạo canvas mới.)");
                return;
            }

            Canvas.ForceUpdateCanvases();
            var crt = canvas.GetComponent<RectTransform>();
            float canvasH = crt.rect.height; if (canvasH < 1f) canvasH = 1080f;
            S = canvasH / DESIGN_H;

            _board = Rect("HubBoard", canvas.transform);
            _board.anchorMin = _board.anchorMax = new Vector2(.5f, .5f);
            _board.pivot = new Vector2(.5f, .5f);
            _board.sizeDelta = new Vector2(BW * S, BH * S);
            _board.anchoredPosition = Vector2.zero;
            _board.SetAsFirstSibling();   // nằm SAU card, không che bài
            AddSprite(_board.gameObject, Tex.RoundRect(441, 240, 20, Bezel1, Bezel2, C(0x18, 0x1C, 0x22), 3));

            hud = _board.gameObject.AddComponent<HUDView>();

            var field = Node("Field", 112, 90, 610, 302);
            if (bossBackground) AddSprite(field.gameObject, bossBackground);
            else AddSpriteRaw(field.gameObject, Tex.BossField(320, 160));

            BenchBar("EBench", 150, 14, 572, 60);
            BenchBar("PBench", 150, 404, 572, 60);

            var eAv = Node("EnemyAvatar", 10, 10, 84, 84);
            if (enemyAvatarArt) AddSprite(eAv.gameObject, enemyAvatarArt); else AddSpriteRaw(eAv.gameObject, Tex.Octagon(120, Panel1, Panel2, C(0xC8, 0xE0, 0x58)));
            var pAv = Node("PlayerAvatar", 782, 390, 88, 80);
            if (playerAvatarArt) AddSprite(pAv.gameObject, playerAvatarArt); else AddSpriteRaw(pAv.gameObject, Tex.Octagon(120, Panel1, Panel2, C(0xC8, 0xE0, 0x58)));

            BuildDeck("Enemy", 714, 10, out hud.enemyDeckButton, out hud.enemyDeckPopup, out hud.enemyDeckPopupText, C(0x5A, 0x3F, 0x86), C(0x28, 0x1B, 0x4A), true);
            BuildDeck("Player", 70, 391, out hud.playerDeckButton, out hud.playerDeckPopup, out hud.playerDeckPopupText, C(0x3A, 0x54, 0x74), C(0x16, 0x22, 0x3A), false);

            GPanel("RoundBadge", 790, 26, 64, 64, 10, Panel1, Panel2, default, 0);
            hud.roundText = Label("RoundText", 786, 42, 72, 22, "Round 1", 14, Ink, TextAlignmentOptions.Center);
            hud.phaseText = Label("PhaseText", 786, 66, 72, 18, "", 11, C(0x9A, 0xA6, 0xB6), TextAlignmentOptions.Center);

            hud.enemyHealthText = Nexus("EnemyHP", 30, 152, C(0xF0, 0x6A, 0x80), C(0xB0, 0x1E, 0x34), C(0xFF, 0x4A, 0x60), redGemArt, "20");
            var emb = Node("Emblem", 82, 224, 46, 46);
            AddSpriteRaw(emb.gameObject, Tex.Circle(96, C(0x2E, 0x27, 0x18), C(0x10, 0x0D, 0x08), Gold, 3));
            Label("EmblemMark", 82, 234, 46, 24, "✦", 18, Gold, TextAlignmentOptions.Center);
            hud.playerHealthText = Nexus("PlayerHP", 30, 288, C(0x7A, 0xB2, 0xFF), C(0x1E, 0x52, 0xB0), C(0x4A, 0x9E, 0xFF), blueGemArt, "20");

            hud.enemyAttackToken = TokenIcon("EnemyToken", 726, 20, C(0xE0, 0xB0, 0x40));
            hud.playerAttackToken = TokenIcon("PlayerToken", 726, 438, C(0xE0, 0xB0, 0x40));

            BuildActionCluster(800f, 245f);
            hud.priorityText = Label("PriorityText", 596, 150, 140, 20, "", 12, Ink, TextAlignmentOptions.Center);
            hud.priorityText.gameObject.SetActive(false);

            BuildGameOver();
        }

        // ================= builders =================
        void BenchBar(string n, float x, float y, float w, float h)
        {
            GPanel(n + "_bar", x, y, w, h, 26, Bar1, Bar2, C(0x14, 0x17, 0x1C), 2);
            GPanel(n + "_groove", x + 16, y + 8, w - 32, h - 16, 18, Groove1, Groove2, default, 0);
        }

        void BuildDeck(string side, float x, float y, out Button btn, out GameObject popup, out TextMeshProUGUI popupText, Color a, Color b, bool popupBelow)
        {
            var img = GPanel(side + "Deck", x, y, 60, 80, 8, a, b, Gold, 1);
            img.raycastTarget = true;
            btn = img.gameObject.AddComponent<Button>();
            btn.targetGraphic = img;
            float py = popupBelow ? y + 86 : y - 96;
            var pop = GPanel(side + "DeckPopup", x - 20, py, 160, 90, 10, C(0x1A, 0x1E, 0x26, 0xF2), C(0x0C, 0x0F, 0x14, 0xF2), Gold, 1);
            popupText = Label(side + "DeckPopupText", x - 14, py + 10, 148, 70, "BÀI CÒN LẠI: 0\nTRÊN TAY: 0", 12, Ink, TextAlignmentOptions.TopLeft);
            popupText.transform.SetParent(pop.transform, true);
            popup = pop.gameObject;
            popup.SetActive(false);
        }

        // Attack token: kiếm chéo (⚔) sáng + quầng vàng — nhìn rõ bên nào đang cầm lượt tấn công.
        // Sprite để TRẮNG-NGÀ để HUDView.Render tô: trắng = sẵn sàng, xám = đã dùng.
        GameObject TokenIcon(string n, float x, float y, Color col)
        {
            var t = Node(n, x, y, 40, 40);
            // quầng sáng nền (con của token → tự bật/tắt theo bên)
            var glow = Rect(n + "_glow", t);
            glow.anchorMin = glow.anchorMax = new Vector2(.5f, .5f); glow.pivot = new Vector2(.5f, .5f);
            glow.anchoredPosition = Vector2.zero; glow.sizeDelta = new Vector2(58 * S, 58 * S);
            AddSpriteRaw(glow.gameObject, Tex.Glow(72, C(0xFF, 0xC8, 0x50)));
            glow.SetAsFirstSibling();
            // kiếm chéo (Image gốc — cái mà Render tô màu)
            AddSpriteRaw(t.gameObject, Tex.Sword(72, C(0xFF, 0xF4, 0xDC), C(0x5A, 0x3E, 0x10)));
            t.gameObject.SetActive(false);
            return t.gameObject;
        }

        void BuildActionCluster(float cx, float cy)
        {
            hud.enemyPriorityIndicator = Pill("EnemyTurnPill", 598, 172, "Enemy Turn", PillR1, PillR2);
            hud.playerPriorityIndicator = Pill("PlayerTurnPill", 598, 288, "Player Turn", PillG1, PillG2);

            hud.enemySpellManaIcons = SpellRow("ESpell", cx, cy - 88, out hud.enemySpellManaText);
            hud.playerSpellManaIcons = SpellRow("PSpell", cx, cy + 80, out hud.playerSpellManaText);

            var pipW = Tex.RoundRect(26, 11, 5, C(0xFF, 0xFF, 0xFF), C(0xC8, 0xC8, 0xD4), default, 0);
            hud.enemyManaIcons = ArcPips("EMana", cx, cy, 62f, -150f, -30f, pipW, hud.manaFilled);
            hud.playerManaIcons = ArcPips("PMana", cx, cy, 62f, 150f, 30f, pipW, hud.manaFilled);

            hud.enemyManaText = Label("EManaNum", cx - 86, cy - 42, 42, 34, "8/9", 18, C(0xCF, 0xE6, 0xFF), TextAlignmentOptions.Center);
            hud.playerManaText = Label("PManaNum", cx - 86, cy + 8, 42, 34, "9/9", 18, Color.white, TextAlignmentOptions.Center);

            var bez = Node("EndTurnBezel", cx - 58, cy - 58, 116, 116);
            AddSpriteRaw(bez.gameObject, Tex.Circle(128, C(0xF0, 0xDD, 0x9A), C(0x8A, 0x66, 0x22), C(0x5A, 0x42, 0x14), 5));
            var btn = Node("EndTurnBtn", cx - 46, cy - 46, 92, 92);
            var orb = AddSpriteRaw(btn.gameObject, Tex.Circle(128, C(0xFF, 0xFF, 0xFF), C(0xC6, 0xCB, 0xD2), C(0x86, 0x8C, 0x94), 3));
            orb.color = hud.colorPass;
            orb.raycastTarget = true;
            hud.actionButton = btn.gameObject.AddComponent<Button>();
            hud.actionButton.targetGraphic = orb;
            hud.actionButtonImage = orb;
            hud.actionButtonText = Label("EndTurnLabel", cx - 46, cy - 12, 92, 24, "PASS", 13, Color.white, TextAlignmentOptions.Center);
            hud.actionButtonText.transform.SetParent(btn, true);
        }

        Image[] ArcPips(string tag, float cx, float cy, float r, float startDeg, float endDeg, Sprite spr, Color fill)
        {
            var arr = new Image[9];
            for (int i = 0; i < 9; i++)
            {
                float deg = Mathf.Lerp(startDeg, endDeg, i / 8f);
                float a = deg * Mathf.Deg2Rad;
                float px = cx + r * Mathf.Cos(a), py = cy + r * Mathf.Sin(a);
                var pip = Node(tag + i, px - 9f, py - 5.5f, 18, 11);
                var img = AddSpriteRaw(pip.gameObject, spr);
                img.color = fill;
                pip.localRotation = Quaternion.Euler(0, 0, -(deg + 90f));
                arr[i] = img;
            }
            return arr;
        }

        Image[] SpellRow(string n, float cx, float y, out TextMeshProUGUI num)
        {
            num = Label(n + "_n", cx - 60, y - 2, 22, 20, "0/", 13, Ink, TextAlignmentOptions.Left);
            var w = Tex.Circle(28, C(0xFF, 0xFF, 0xFF), C(0xD4, 0xD6, 0xDC), C(0x9A, 0xA0, 0xAA), 2);
            var arr = new Image[5];
            for (int i = 0; i < 5; i++)
            {
                var c = Node(n + i, cx - 34 + i * 15, y, 12, 12);
                var img = AddSpriteRaw(c.gameObject, w);
                img.color = new Color(1, 1, 1, 0.28f);
                arr[i] = img;
            }
            return arr;
        }

        void BuildGameOver()
        {
            var panel = Node("GameOverPanel", 0, 0, BW, BH);
            var img = panel.gameObject.AddComponent<Image>();
            img.color = new Color(0, 0, 0, 0.82f);
            hud.gameOverText = Label("GameOverText", 200, 180, 482, 60, "VICTORY", 40, Gold, TextAlignmentOptions.Center);
            hud.gameOverText.transform.SetParent(panel, true);
            var rb = GPanel("RestartBtn", 371, 270, 140, 44, 10, C(0x3F, 0xC4, 0x7E), C(0x0F, 0x6A, 0x3C), Gold, 1);
            rb.raycastTarget = true;
            rb.transform.SetParent(panel, true);
            hud.restartButton = rb.gameObject.AddComponent<Button>();
            hud.restartButton.targetGraphic = rb;
            var rl = Label("RestartLabel", 371, 280, 140, 24, "CHƠI LẠI", 15, Color.white, TextAlignmentOptions.Center);
            rl.transform.SetParent(rb.transform, true);
            hud.gameOverPanel = panel.gameObject;
            hud.gameOverPanel.SetActive(false);
        }

        TextMeshProUGUI Nexus(string n, float x, float y, Color a, Color b, Color glow, Sprite art, string num)
        {
            var g = Node(n + "_glow", x - 14, y - 14, 84, 84);
            AddSpriteRaw(g.gameObject, Tex.Glow(96, glow));
            var gem = Node(n + "_gem", x, y, 56, 56);
            if (art) AddSprite(gem.gameObject, art); else AddSpriteRaw(gem.gameObject, Tex.Diamond(112, a, b));
            return Label(n, x + 62, y + 8, 66, 40, num, 30, Color.white, TextAlignmentOptions.Left);
        }

        // ================= low-level =================
        RectTransform Rect(string n, Transform p) { var g = new GameObject(n, typeof(RectTransform)); g.transform.SetParent(p, false); return (RectTransform)g.transform; }

        RectTransform Node(string n, float x, float y, float w, float h, Transform parent = null)
        {
            var rt = Rect(n, parent ? parent : _board);
            rt.anchorMin = rt.anchorMax = new Vector2(0, 1);
            rt.pivot = new Vector2(0, 1);
            rt.anchoredPosition = new Vector2(x * S, -y * S);
            rt.sizeDelta = new Vector2(w * S, h * S);
            return rt;
        }

        Image GPanel(string n, float x, float y, float w, float h, float radius, Color top, Color bot, Color border, float bw)
        {
            var rt = Node(n, x, y, w, h);
            var img = rt.gameObject.AddComponent<Image>();
            int gw = Mathf.Min((int)(w * 2), 512), gh = Mathf.Min((int)(h * 2), 512);
            img.sprite = Tex.RoundRect(gw, gh, radius * 2f * gw / (w * 2), top, bot, border, bw * 2);
            img.raycastTarget = false;
            return img;
        }

        Image AddSprite(GameObject go, Sprite s) { var i = go.AddComponent<Image>(); i.sprite = s; i.raycastTarget = false; return i; }
        Image AddSpriteRaw(GameObject go, Sprite s) { var i = go.AddComponent<Image>(); i.sprite = s; i.raycastTarget = false; return i; }

        GameObject Pill(string n, float x, float y, string text, Color a, Color b)
        {
            var p = GPanel(n, x, y, 132, 30, 9, a, b, C(0xFF, 0xFF, 0xFF, 0x33), 1);
            var t = Label(n + "_t", x, y + 5, 132, 20, text, 15, Color.white, TextAlignmentOptions.Center);
            t.transform.SetParent(p.transform, true);
            return p.gameObject;
        }

        TextMeshProUGUI Label(string n, float x, float y, float w, float h, string text, float size, Color c, TextAlignmentOptions al)
        {
            var rt = Node(n, x, y, w, h);
            var t = rt.gameObject.AddComponent<TextMeshProUGUI>();
            t.text = text; t.fontSize = size * S; t.color = c; t.alignment = al; t.enableWordWrapping = false; t.raycastTarget = false;
            return t;
        }

        // KHÔNG tạo canvas mới. Lấy canvas bạn chỉ định, hoặc canvas cha của GameObject này.
        Canvas EnsureCanvas()
        {
            if (targetCanvas != null) return targetCanvas.rootCanvas != null ? targetCanvas.rootCanvas : targetCanvas;
            var parent = GetComponentInParent<Canvas>();
            return parent != null ? (parent.rootCanvas != null ? parent.rootCanvas : parent) : null;
        }
        static Color C(int r, int g, int b, int a = 255) => new Color(r / 255f, g / 255f, b / 255f, a / 255f);
    }

    /// <summary>Sinh sprite bằng Texture2D (bo góc/tròn/thoi/sao/glow/nền boss) — không cần asset.</summary>
    public static class Tex
    {
        public static Sprite RoundRect(int w, int h, float r, Color top, Color bot, Color border, float bw)
        {
            w = Mathf.Max(2, w); h = Mathf.Max(2, h); r = Mathf.Min(r, Mathf.Min(w, h) / 2f);
            var t = New(w, h); var px = new Color32[w * h]; float hw = w / 2f, hh = h / 2f;
            for (int y = 0; y < h; y++)
            {
                Color grad = Color.Lerp(top, bot, (float)y / (h - 1));
                for (int x = 0; x < w; x++)
                {
                    float dx = Mathf.Abs(x + .5f - hw) - (hw - r), dy = Mathf.Abs(y + .5f - hh) - (hh - r);
                    float outside = Mathf.Sqrt(Mathf.Max(dx, 0) * Mathf.Max(dx, 0) + Mathf.Max(dy, 0) * Mathf.Max(dy, 0));
                    float d = outside + Mathf.Min(Mathf.Max(dx, dy), 0f) - r;
                    float aa = Mathf.Clamp01(0.75f - d);
                    Color col = grad;
                    if (bw > 0 && border.a > 0 && d > -bw) col = Color.Lerp(border, grad, Mathf.Clamp01((-d) / bw));
                    col.a = aa * Mathf.Max(grad.a, (bw > 0 ? border.a : 0f)); px[y * w + x] = col;
                }
            }
            return Finish(t, px);
        }
        public static Sprite Circle(int size, Color center, Color edge, Color ring, float rw)
        {
            var t = New(size, size); var px = new Color32[size * size]; float c = size / 2f, R = size / 2f - 1;
            for (int y = 0; y < size; y++) for (int x = 0; x < size; x++)
                {
                    float dist = Mathf.Sqrt((x + .5f - c) * (x + .5f - c) + (y + .5f - c) * (y + .5f - c));
                    float aa = Mathf.Clamp01(R - dist + .5f), tg = Mathf.Clamp01(dist / R);
                    Color col = Color.Lerp(center, edge, tg * tg);
                    if (rw > 0 && ring.a > 0 && dist > R - rw) col = Color.Lerp(col, ring, Mathf.Clamp01((dist - (R - rw)) / rw));
                    float gl = Mathf.Clamp01(1f - ((x - c) * (x - c) + (y - c * .55f) * (y - c * .55f)) / (R * R * .5f));
                    col = Color.Lerp(col, Color.white, gl * .28f); col.a = aa; px[y * size + x] = col;
                }
            return Finish(t, px);
        }
        public static Sprite Diamond(int size, Color a, Color b)
        {
            var t = New(size, size); var px = new Color32[size * size]; float c = size / 2f;
            for (int y = 0; y < size; y++) for (int x = 0; x < size; x++)
                {
                    float m = Mathf.Abs(x + .5f - c) + Mathf.Abs(y + .5f - c); float aa = Mathf.Clamp01(c - m + .5f);
                    Color col = Color.Lerp(a, b, (float)y / (size - 1)); col.a = aa; px[y * size + x] = col;
                }
            return Finish(t, px);
        }
        // Hai thanh kiếm chéo nhau (⚔): 2 đường chéo dày + chuôi tròn ở đáy.
        public static Sprite Sword(int size, Color blade, Color edge)
        {
            var t = New(size, size); var px = new Color32[size * size]; float c = size / 2f, reach = size * 0.34f, th = size * 0.10f;
            Vector2 a1 = new Vector2(c - reach, c + reach), a2 = new Vector2(c + reach, c - reach); // '/'
            Vector2 b1 = new Vector2(c + reach, c + reach), b2 = new Vector2(c - reach, c - reach); // '\'
            for (int y = 0; y < size; y++) for (int x = 0; x < size; x++)
                {
                    Vector2 p = new Vector2(x + .5f, y + .5f);
                    float d = Mathf.Min(SegDist(p, a1, a2), SegDist(p, b1, b2));
                    float aa = Mathf.Clamp01(th - d + .5f);
                    // chuôi tròn ở 2 đầu dưới
                    float pd = Mathf.Min(Vector2.Distance(p, a1), Vector2.Distance(p, b1));
                    aa = Mathf.Max(aa, Mathf.Clamp01(th * 1.3f - pd));
                    Color col = (d > th - 2f && aa > 0f) ? edge : blade;
                    col.a = aa; px[y * size + x] = col;
                }
            return Finish(t, px);
        }
        static float SegDist(Vector2 p, Vector2 a, Vector2 b)
        {
            Vector2 ab = b - a; float denom = Vector2.Dot(ab, ab);
            float u = denom < 1e-4f ? 0f : Mathf.Clamp01(Vector2.Dot(p - a, ab) / denom);
            return Vector2.Distance(p, a + ab * u);
        }
        public static Sprite Octagon(int size, Color a, Color b, Color ring)
        {
            var t = New(size, size); var px = new Color32[size * size]; float c = size / 2f, R = size / 2f - 1;
            for (int y = 0; y < size; y++) for (int x = 0; x < size; x++)
                {
                    float ax = Mathf.Abs(x + .5f - c), ay = Mathf.Abs(y + .5f - c);
                    float oct = Mathf.Max(Mathf.Max(ax, ay), (ax + ay) * .707f);
                    float aa = Mathf.Clamp01(R - oct + .75f);
                    Color col = Color.Lerp(a, b, (float)y / (size - 1));
                    if (ring.a > 0 && oct > R - 3) col = Color.Lerp(col, ring, Mathf.Clamp01((oct - (R - 3)) / 3f));
                    col.a = aa; px[y * size + x] = col;
                }
            return Finish(t, px);
        }
        public static Sprite Glow(int size, Color col)
        {
            var t = New(size, size); var px = new Color32[size * size]; float c = size / 2f, R = size / 2f;
            for (int y = 0; y < size; y++) for (int x = 0; x < size; x++)
                {
                    float dist = Mathf.Sqrt((x + .5f - c) * (x + .5f - c) + (y + .5f - c) * (y + .5f - c));
                    float a = Mathf.Clamp01(1f - dist / R); a = a * a * .9f; var cc = col; cc.a = a; px[y * size + x] = cc;
                }
            return Finish(t, px);
        }
        public static Sprite BossField(int w, int h)
        {
            var t = New(w, h); var px = new Color32[w * h]; float cx = w * .5f, cy = h * .45f;
            for (int y = 0; y < h; y++) for (int x = 0; x < w; x++)
                {
                    Color b = Color.Lerp(new Color(.20f, .24f, .32f), new Color(.07f, .10f, .15f), (float)y / (h - 1));
                    float d = Mathf.Sqrt((x - cx) * (x - cx) + (y - cy) * (y - cy)) / (w * .5f);
                    float g = Mathf.Clamp01(1f - d * 1.6f);
                    Color col = Color.Lerp(b, new Color(1f, .92f, .66f), g * .7f); col.a = 1f; px[y * w + x] = col;
                }
            return Finish(t, px);
        }
        static Texture2D New(int w, int h) { return new Texture2D(w, h, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Bilinear }; }
        static Sprite Finish(Texture2D t, Color32[] px) { t.SetPixels32(px); t.Apply(); return Sprite.Create(t, new Rect(0, 0, t.width, t.height), new Vector2(.5f, .5f), 100f, 0, SpriteMeshType.FullRect); }
    }
}
