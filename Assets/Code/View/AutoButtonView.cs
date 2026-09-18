using System.Collections;
using UnityEngine;
using UnityEngine.UI;
using TMPro;
using LoRClone.Controller;
using LoRClone.AI;   // EnemyAI (bộ điều khiển auto)

namespace LoRClone.View
{
    /// <summary>
    /// Nút AUTO (tự động chiến đấu) — TỰ DỰNG bằng code theo style Honkai: Star Rail.
    /// Chỉ cần tạo 1 GameObject trong scene game + gắn component này. KHÔNG dựng UI / kéo ref tay.
    ///
    /// ★ FIX 1 (đảm bảo tồn tại): nút tự tạo PlayerAI (EnemyAI controlsPlayer=true) nếu scene chưa có.
    /// ★ FIX 2 (chống bị TẮT): watchdog canh PlayerAI, tự bật lại khi bị inactive/disable.
    /// ★ FIX 3 (PvP): trong networkMode, KHÔNG tạo/chạy PlayerAI và ẩn nút — auto chỉ dành cho PvE.
    ///   Lý do: PvP là 2 người thật; để AI can thiệp sẽ làm 2 máy lệch state (desync).
    ///
    /// Bấm → GameController.TogglePlayerAutoPlay(). OFF = xám mờ; ON = vàng cam + ring sáng + nhấp nháy nhẹ.
    /// </summary>
    public class AutoButtonView : MonoBehaviour
    {
        [Header("Vị trí (tâm nút, toạ độ 0..1 màn hình) + kích thước px")]
        public Vector2 anchor = new Vector2(0.955f, 0.905f);
        public float size = 96f;

        [Header("Font (tùy chọn — thả TMP font HSR)")]
        public TMP_FontAsset uiFont;

        [Header("Màu")]
        public Color onColor = new Color(0.97f, 0.66f, 0.18f, 1f);   // vàng cam khi ON
        public Color offColor = new Color(1f, 1f, 1f, 0.6f);          // xám khi OFF
        public Color bgOn = new Color(0.22f, 0.15f, 0.04f, 0.92f);
        public Color bgOff = new Color(0.10f, 0.11f, 0.13f, 0.85f);

        [Header("Auto driver")]
        [Tooltip("true = nút tự tạo PlayerAI (EnemyAI controlsPlayer=true) nếu scene chưa có.")]
        public bool autoCreatePlayerAI = true;
        [Tooltip("Copy thông số delay từ enemy AI có sẵn (nếu tìm thấy) sang PlayerAI mới tạo.")]
        public bool copyEnemyAITiming = true;
        [Tooltip("Watchdog: khi auto ON, tự bật lại PlayerAI nếu bị inactive/disable.")]
        public bool keepPlayerAIAlive = true;

        Canvas _canvas;
        Image _ring, _bg, _icon1, _icon2;
        TextMeshProUGUI _label;
        RectTransform _root;
        bool _lastState;
        bool _disabledForPvP; // đã ẩn nút vì đang PvP

        EnemyAI _playerAI;   // tham chiếu tới bộ điều khiển auto phe player
        float _nextWatchLog; // throttle log cảnh báo watchdog

        static Sprite _circle, _tri;
        static Sprite CircleSp { get { if (!_circle) _circle = UISprites.Circle(); return _circle; } }

        GameController GC => GameController.Instance;
        bool IsNetwork => GC != null && GC.networkMode;

        void Start()
        {
            EnsureEventSystem();

            var cgo = new GameObject("AutoButtonCanvas", typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
            _canvas = cgo.GetComponent<Canvas>();
            _canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            _canvas.sortingOrder = 850; // trên HUD, dưới PauseMenu(900) → tự ẩn khi pause
            var scaler = cgo.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920f, 1080f);
            scaler.matchWidthOrHeight = 0.5f;

            Build();
            Refresh(force: true);

            // ★ Đảm bảo có PlayerAI để auto thật sự chạy (đợi GameController + model sẵn sàng).
            StartCoroutine(EnsurePlayerAIWhenReady());
        }

        // ── ĐẢM BẢO PLAYER AI TỒN TẠI ─────────────────────────────
        IEnumerator EnsurePlayerAIWhenReady()
        {
            var gc = GameController.Instance;
            while (gc == null) { yield return null; gc = GameController.Instance; }
            while (gc.model == null) yield return null;

            // ★ PvP: tắt hẳn nút auto (ẩn canvas) — không tạo PlayerAI.
            if (gc.networkMode)
            {
                DisableForPvP();
                yield break;
            }

            EnsurePlayerAI();
        }

        void DisableForPvP()
        {
            if (_disabledForPvP) return;
            _disabledForPvP = true;
            if (_canvas != null) _canvas.gameObject.SetActive(false); // ẩn nút trong PvP
            Debug.Log("[AutoButton] networkMode = PvP → ẩn nút AUTO, không tạo PlayerAI (auto chỉ dành cho PvE).");
        }

        /// <summary>
        /// Tìm EnemyAI điều khiển phe PLAYER (controlsPlayer=true) — KỂ CẢ object đang inactive.
        /// Nếu tìm thấy mà bị tắt → bật lại. Nếu không có và autoCreatePlayerAI=true → tạo runtime
        /// ở ROOT (không cha) để không bị object khác SetActive(false) kèm.
        /// </summary>
        void EnsurePlayerAI()
        {
            if (IsNetwork) { DisableForPvP(); return; } // ★ PvP: không đụng gì.

            if (_playerAI != null) { ReactivatePlayerAI("ref sẵn có"); return; }

            var all = FindObjectsByType<EnemyAI>(FindObjectsInactive.Include, FindObjectsSortMode.None);
            EnemyAI enemySideAI = null;
            foreach (var ai in all)
            {
                if (ai == null) continue;
                if (ai.controlsPlayer) _playerAI = ai;
                else if (enemySideAI == null) enemySideAI = ai;
            }

            if (_playerAI != null)
            {
                ReactivatePlayerAI("tìm thấy trong scene");
                return;
            }

            if (!autoCreatePlayerAI)
            {
                Debug.LogWarning("[AutoButton] KHÔNG có PlayerAI và autoCreatePlayerAI=false → nút AUTO " +
                                 "sẽ không đánh. Bật autoCreatePlayerAI hoặc đặt 1 EnemyAI controlsPlayer=true.");
                return;
            }

            var go = new GameObject("PlayerAI (auto)");
            _playerAI = go.AddComponent<EnemyAI>();
            _playerAI.controlsPlayer = true;

            if (copyEnemyAITiming && enemySideAI != null)
            {
                _playerAI.thinkDelay = enemySideAI.thinkDelay;
                _playerAI.actionDelay = enemySideAI.actionDelay;
            }

            Debug.Log("[AutoButton] Không tìm thấy PlayerAI → đã TẠO MỚI 'PlayerAI (auto)' ở root " +
                      $"(controlsPlayer=true, thinkDelay={_playerAI.thinkDelay}, actionDelay={_playerAI.actionDelay}).");
        }

        bool ReactivatePlayerAI(string reason)
        {
            if (_playerAI == null) return false;
            bool wasOff = false;

            if (!_playerAI.gameObject.activeSelf)
            {
                _playerAI.gameObject.SetActive(true);
                wasOff = true;
            }
            if (!_playerAI.enabled)
            {
                _playerAI.enabled = true;
                wasOff = true;
            }

            if (wasOff)
                Debug.LogWarning($"[AutoButton] PlayerAI đang bị TẮT ({reason}) → đã bật lại. " +
                                 "Có script/panel nào đó SetActive(false) nó — nên tách PlayerAI ra object riêng ở root.");
            return wasOff;
        }

        static void EnsureEventSystem()
        {
            if (FindFirstObjectByType<UnityEngine.EventSystems.EventSystem>() == null)
                new GameObject("EventSystem",
                    typeof(UnityEngine.EventSystems.EventSystem),
                    typeof(UnityEngine.EventSystems.StandaloneInputModule));
        }

        void Build()
        {
            _root = MakeRect("AutoButton", _canvas.transform);
            _root.anchorMin = _root.anchorMax = anchor;
            _root.pivot = new Vector2(0.5f, 0.5f);
            _root.sizeDelta = new Vector2(size, size);

            // Ring (viền tròn)
            _ring = AddImage(_root, CircleSp, offColor);
            var btn = _root.gameObject.AddComponent<Button>(); btn.targetGraphic = _ring;
            btn.transition = Selectable.Transition.None;
            btn.onClick.AddListener(OnAutoClicked);

            // Nền tối bên trong (chừa ring ~6px)
            var bgRt = MakeRect("BG", _root); bgRt.anchorMin = Vector2.zero; bgRt.anchorMax = Vector2.one;
            bgRt.offsetMin = new Vector2(6, 6); bgRt.offsetMax = new Vector2(-6, -6);
            _bg = AddImage(bgRt, CircleSp, bgOff); _bg.raycastTarget = false;

            // Chữ "AUTO"
            var lrt = MakeRect("Label", _root); lrt.anchorMin = new Vector2(0.1f, 0.46f); lrt.anchorMax = new Vector2(0.9f, 0.84f);
            lrt.offsetMin = lrt.offsetMax = Vector2.zero;
            _label = lrt.gameObject.AddComponent<TextMeshProUGUI>();
            if (uiFont) _label.font = uiFont;
            _label.text = "AUTO"; _label.fontSize = 22f; _label.fontStyle = FontStyles.Bold;
            _label.alignment = TextAlignmentOptions.Center; _label.color = offColor; _label.raycastTarget = false;
            _label.characterSpacing = 4f;

            // Icon tua nhanh (2 tam giác) ở nửa dưới
            var iconArea = MakeRect("Icon", _root); iconArea.anchorMin = new Vector2(0.32f, 0.18f); iconArea.anchorMax = new Vector2(0.68f, 0.44f);
            iconArea.offsetMin = iconArea.offsetMax = Vector2.zero;
            var t1 = MakeRect("Tri1", iconArea); t1.anchorMin = new Vector2(0f, 0f); t1.anchorMax = new Vector2(0.6f, 1f); t1.offsetMin = t1.offsetMax = Vector2.zero;
            _icon1 = AddImage(t1, TriangleSprite(), offColor); _icon1.preserveAspect = true; _icon1.raycastTarget = false;
            var t2 = MakeRect("Tri2", iconArea); t2.anchorMin = new Vector2(0.4f, 0f); t2.anchorMax = new Vector2(1f, 1f); t2.offsetMin = t2.offsetMax = Vector2.zero;
            _icon2 = AddImage(t2, TriangleSprite(), offColor); _icon2.preserveAspect = true; _icon2.raycastTarget = false;
        }

        // ── Click handler ─────────────────────────────────────────
        void OnAutoClicked()
        {
            if (GC == null)
            {
                Debug.LogWarning("[AutoButton] GameController.Instance null — không toggle được auto.");
                return;
            }

            if (IsNetwork) { DisableForPvP(); return; } // ★ PvP: nút vô hiệu.

            // Đảm bảo PlayerAI có mặt & đang bật TRƯỚC khi toggle.
            EnsurePlayerAI();

            GC.TogglePlayerAutoPlay();
            Debug.Log($"[AutoButton] Toggle → playerAutoPlay={GC.playerAutoPlay}, " +
                      $"networkMode={GC.networkMode}, playerAI={(_playerAI != null ? "OK" : "NULL")}.");

            GC.RaiseStateChanged();
            Refresh(force: true);
        }

        void Update()
        {
            // ★ PvP: nếu networkMode bật muộn (sau handshake) → ẩn nút ngay.
            if (IsNetwork) { DisableForPvP(); return; }

            bool on = GC != null && GC.playerAutoPlay;
            if (on != _lastState) Refresh(force: true);

            // ★ Watchdog NHẸ: chỉ đảm bảo PlayerAI tồn tại & đang bật.
            //   KHÔNG gọi RaiseStateChanged() trong Update — gọi mỗi frame ở frame chuyển tiếp
            //   (đang animate/targeting) làm subscriber OnStateChanged (GameView/EnemyAI) NRE spam.
            //   AI sẽ tự bắt nhịp ở OnStateChanged tự nhiên kế tiếp (mỗi action đều Notify).
            if (on && keepPlayerAIAlive)
            {
                if (_playerAI == null)
                {
                    EnsurePlayerAI();
                }
                else if (!_playerAI.gameObject.activeInHierarchy || !_playerAI.enabled)
                {
                    ReactivatePlayerAI("watchdog");
                    if (Time.unscaledTime >= _nextWatchLog)
                    {
                        _nextWatchLog = Time.unscaledTime + 2f;
                        Debug.LogWarning("[AutoButton] Watchdog bật lại PlayerAI khi auto ON.");
                    }
                }
            }

            // Nhấp nháy nhẹ khi ON
            if (on && _root)
            {
                float p = 0.5f + 0.5f * Mathf.Sin(Time.unscaledTime * 4f);
                _ring.color = Color.Lerp(onColor, Color.Lerp(onColor, Color.white, 0.4f), p);
                float s = 1f + 0.04f * p;
                _root.localScale = new Vector3(s, s, 1f);
            }
            else if (_root && _root.localScale.x != 1f)
            {
                _root.localScale = Vector3.one;
            }
        }

        void Refresh(bool force)
        {
            bool on = GC != null && GC.playerAutoPlay;
            _lastState = on;
            var main = on ? onColor : offColor;
            if (_ring) _ring.color = main;
            if (_bg) _bg.color = on ? bgOn : bgOff;
            if (_label) _label.color = main;
            if (_icon1) _icon1.color = main;
            if (_icon2) _icon2.color = main;
            if (!on && _root) _root.localScale = Vector3.one;
        }

        // ── helpers ──
        RectTransform MakeRect(string name, Transform parent)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            return go.AddComponent<RectTransform>();
        }

        static Image AddImage(RectTransform rt, Sprite sp, Color c)
        {
            if (rt.anchorMin == Vector2.zero && rt.anchorMax == Vector2.zero) { rt.anchorMin = Vector2.zero; rt.anchorMax = Vector2.one; rt.offsetMin = rt.offsetMax = Vector2.zero; }
            var img = rt.gameObject.AddComponent<Image>();
            img.sprite = sp; img.type = Image.Type.Simple; img.color = c;
            return img;
        }

        // Tam giác trỏ phải (base trái, đỉnh phải) — silhouette trắng, tint bằng color
        static Sprite TriangleSprite()
        {
            if (_tri) return _tri;
            int S = 64; var t = new Texture2D(S, S, TextureFormat.RGBA32, false);
            var clear = new Color(0, 0, 0, 0);
            for (int y = 0; y < S; y++)
                for (int x = 0; x < S; x++)
                {
                    float half = (S / 2f) * (1f - x / (float)(S - 1)); // hẹp dần về phải
                    bool inside = Mathf.Abs(y - S / 2f) <= half;
                    t.SetPixel(x, y, inside ? Color.white : clear);
                }
            t.Apply();
            _tri = Sprite.Create(t, new Rect(0, 0, S, S), new Vector2(0.5f, 0.5f), 100f);
            return _tri;
        }
    }
}