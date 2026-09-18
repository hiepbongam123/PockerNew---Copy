using System.Collections;
using UnityEngine;
using UnityEngine.UI;
using TMPro;
using LoRClone.Data;
using LoRClone.Model;
using LoRClone.Controller;

namespace LoRClone.View
{
    /// <summary>
    /// NÚT SỨC MẠNH CHỦ ĐỘNG trong trận — dùng chung cho Cộng Hưởng Vận Mệnh (theo run)
    /// và Champion Power (theo tướng). Tổng quát hoá: nhận tiêu đề + mana + charge + màu +
    /// 1 hành động (Action&lt;PlayerModel&gt;). Không phụ thuộc data cụ thể.
    ///
    /// Bấm → tốn mana → chạy hành động lên phe mình → giảm 1 charge. Hết mana/charge → mờ, khoá.
    /// Vị trí: mép TRÁI giữa màn. Muốn dời: đổi anchor trong BuildUI.
    /// </summary>
    public class ChampionPowerController : MonoBehaviour
    {
        string _title, _sub, _flash;
        int _manaCost, _chargesLeft;
        Color _accent = new Color(0.96f, 0.80f, 0.42f);
        System.Action<PlayerModel> _apply;

        Button _btn;
        Image _btnImg;
        TextMeshProUGUI _costLbl;
        GameObject _canvasGO;
        PlayerModel _player;

        static readonly Color CUnusable = new Color(0.20f, 0.22f, 0.28f, 0.95f);

        /// <summary>Tạo nút. accentHex = "RRGGBB" cho màu theo hệ (null → vàng mặc định).</summary>
        public static ChampionPowerController Spawn(string title, string sub, int manaCost, int charges,
            string accentHex, string flashText, System.Action<PlayerModel> apply)
        {
            if (apply == null) return null;
            var go = new GameObject("ChampionPowerController");
            var c = go.AddComponent<ChampionPowerController>();
            c._title = title;
            c._sub = sub;
            c._manaCost = Mathf.Max(0, manaCost);
            c._chargesLeft = Mathf.Max(1, charges);
            c._flash = string.IsNullOrEmpty(flashText) ? title : flashText;
            c._apply = apply;
            if (!string.IsNullOrEmpty(accentHex) &&
                ColorUtility.TryParseHtmlString("#" + accentHex, out var col)) c._accent = col;
            c.StartCoroutine(c.Boot());
            return c;
        }

        IEnumerator Boot()
        {
            while (GameController.Instance == null || GameController.Instance.model == null)
                yield return null;
            _player = GameController.Instance.model.player;

            BuildUI();
            GameController.Instance.OnStateChanged += OnState;
            if (_player != null) _player.OnManaChanged += OnMana;
            Refresh();
        }

        void OnDestroy()
        {
            if (GameController.Instance != null) GameController.Instance.OnStateChanged -= OnState;
            if (_player != null) _player.OnManaChanged -= OnMana;
        }

        void OnState(GameModel m) => Refresh();
        void OnMana(PlayerModel p) => Refresh();

        // ── UI ────────────────────────────────────────────────────
        void BuildUI()
        {
            _canvasGO = new GameObject("ChampionPowerCanvas");
            var cv = _canvasGO.AddComponent<Canvas>();
            cv.renderMode = RenderMode.ScreenSpaceOverlay;
            cv.sortingOrder = 600; // trên sân, dưới popup kết quả (999)
            _canvasGO.AddComponent<GraphicRaycaster>();
            var scl = _canvasGO.AddComponent<CanvasScaler>();
            scl.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scl.referenceResolution = new Vector2(1920, 1080);
            var root = (RectTransform)_canvasGO.transform;

            var rt = MakeRect("PowerBtn", root, new Vector2(0.008f, 0.38f), new Vector2(0.118f, 0.56f));
            var baseCol = new Color(_accent.r * 0.55f, _accent.g * 0.42f, _accent.b * 0.2f, 0.98f);
            _btnImg = RoundImg(rt, baseCol);
            _btn = rt.gameObject.AddComponent<Button>();
            _btn.targetGraphic = _btnImg;
            var cb = _btn.colors;
            cb.normalColor = Color.white;
            cb.highlightedColor = new Color(1f, 0.97f, 0.85f);
            cb.pressedColor = new Color(0.8f, 0.8f, 0.85f);
            cb.disabledColor = new Color(0.7f, 0.7f, 0.7f, 0.6f);
            cb.fadeDuration = 0.08f;
            _btn.colors = cb;
            _btn.onClick.AddListener(Use);

            var glow = MakeRect("Glow", rt, new Vector2(0.06f, 0.05f), new Vector2(0.94f, 0.95f));
            RoundImg(glow, new Color(_accent.r, _accent.g, _accent.b, 0.12f)).raycastTarget = false;

            // Tiêu đề (dải trên)
            SpawnLabel(rt, _title, 12.5f, Color.white, TextAlignmentOptions.Center, true,
                new Vector2(0.04f, 0.58f), new Vector2(0.96f, 0.94f)).textWrappingMode = TextWrappingModes.Normal;

            // Mô tả hiệu ứng (dải giữa)
            if (!string.IsNullOrEmpty(_sub))
                SpawnLabel(rt, _sub, 10.5f, new Color(0.88f, 0.92f, 1f), TextAlignmentOptions.Center, false,
                    new Vector2(0.04f, 0.30f), new Vector2(0.96f, 0.58f)).textWrappingMode = TextWrappingModes.Normal;

            // Chi phí + charge (dải dưới)
            _costLbl = SpawnLabel(rt, "", 11f, new Color(0.9f, 0.94f, 1f),
                TextAlignmentOptions.Center, true, new Vector2(0.03f, 0.04f), new Vector2(0.97f, 0.28f));
            _costLbl.textWrappingMode = TextWrappingModes.Normal;
        }

        // ── Trạng thái ────────────────────────────────────────────
        bool CanUseNow()
        {
            if (_player == null || _chargesLeft <= 0) return false;
            var m = GameController.Instance != null ? GameController.Instance.model : null;
            if (m == null) return false;
            if (m.phase == GamePhase.Mulligan || m.phase == GamePhase.GameOver) return false;
            return _player.CanSpend(_manaCost);
        }

        void Refresh()
        {
            if (_btn == null) return;
            bool usable = CanUseNow();
            _btn.interactable = usable;
            var baseCol = new Color(_accent.r * 0.55f, _accent.g * 0.42f, _accent.b * 0.2f, 0.98f);
            _btnImg.color = usable ? baseCol : CUnusable;
            if (_costLbl != null)
                _costLbl.text = _chargesLeft > 0
                    ? $"{_manaCost}<size=80%> mana</size>\n<color=#F0C75A>còn {_chargesLeft}</color>"
                    : "<color=#9AA7B8>đã hết</color>";
        }

        // ── Dùng ──────────────────────────────────────────────────
        void Use()
        {
            if (!CanUseNow()) return;
            if (!_player.SpendMana(_manaCost)) return;

            _apply?.Invoke(_player);
            _chargesLeft--;

            GameController.Instance?.RaiseStateChanged();
            Refresh();
            StartCoroutine(Flash(_flash + "!"));
        }

        IEnumerator Flash(string msg)
        {
            var root = (RectTransform)_canvasGO.transform;
            var t = MakeRect("Flash", root, new Vector2(0.30f, 0.55f), new Vector2(0.70f, 0.66f));
            var lbl = SpawnLabel(t, msg, 30f, _accent, TextAlignmentOptions.Center, true);
            lbl.raycastTarget = false;
            float e = 0f;
            while (e < 0.9f)
            {
                e += Time.deltaTime;
                float k = 1f - (e / 0.9f);
                lbl.color = new Color(_accent.r, _accent.g, _accent.b, Mathf.Clamp01(k));
                t.anchorMin = new Vector2(0.30f, 0.55f + (1f - k) * 0.06f);
                t.anchorMax = new Vector2(0.70f, 0.66f + (1f - k) * 0.06f);
                yield return null;
            }
            if (t != null) Destroy(t.gameObject);
        }

        // ── Micro helpers ─────────────────────────────────────────
        static RectTransform MakeRect(string name, RectTransform parent, Vector2 aMin, Vector2 aMax)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            var rt = go.AddComponent<RectTransform>();
            rt.anchorMin = aMin; rt.anchorMax = aMax;
            rt.offsetMin = rt.offsetMax = Vector2.zero;
            return rt;
        }

        static Sprite _round;
        static Sprite Round => _round != null ? _round : (_round = UISprites.RoundedRect());

        static Image RoundImg(RectTransform rt, Color color)
        {
            var img = rt.gameObject.GetComponent<Image>() ?? rt.gameObject.AddComponent<Image>();
            img.sprite = Round; img.type = Image.Type.Sliced; img.color = color;
            return img;
        }

        static TextMeshProUGUI SpawnLabel(RectTransform parent, string text, float size,
            Color color, TextAlignmentOptions align, bool bold, Vector2? aMin = null, Vector2? aMax = null)
        {
            var go = new GameObject("Label");
            go.transform.SetParent(parent, false);
            var rt = go.AddComponent<RectTransform>();
            rt.anchorMin = aMin ?? Vector2.zero; rt.anchorMax = aMax ?? Vector2.one;
            rt.offsetMin = rt.offsetMax = Vector2.zero;
            var tmp = go.AddComponent<TextMeshProUGUI>();
            tmp.text = text; tmp.fontSize = size; tmp.color = color;
            tmp.alignment = align; tmp.fontStyle = bold ? FontStyles.Bold : FontStyles.Normal;
            tmp.raycastTarget = false;
            return tmp;
        }
    }
}