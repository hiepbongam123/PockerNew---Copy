using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using TMPro;
using LoRClone.Controller;

namespace LoRClone.View
{
    /// <summary>
    /// THANH THÔNG BÁO hành động — hiện note ngắn giữa-trên màn hình rồi tự mờ đi.
    ///
    /// TỰ hoạt động, KHÔNG cần setup, KHÔNG chặn input:
    ///   • Tự nghe GameController.OnActionRejected → hiện lý do bị từ chối
    ///     (Thiếu mana / Không phải lượt của bạn / Đang có spell trên stack — không triệu hồi unit... ).
    ///   • Tự bám lại GameController mới mỗi khi vào game scene.
    ///
    /// Gọi thủ công ở bất cứ đâu:
    ///   GameToast.Note("Sang lượt mới");   // thông tin (xanh)
    ///   GameToast.Warn("Thiếu mana!");     // cảnh báo (đỏ)
    /// </summary>
    public class GameToast : MonoBehaviour
    {
        [Header("Vị trí — neo TÂM màn hình (≈ chỗ spell zone)")]
        [Tooltip("Lệch NGANG so với tâm (px, 1080-ref). Dương = sang phải.\n" +
                 "Dùng để canh khớp spell slot khi board bị viền đen lệch.")]
        public float horizontalOffset = 0f;
        [Tooltip("Lệch DỌC so với tâm (px). Dương = lên trên.")]
        public float verticalOffset = 0f;
        public float bannerWidth = 560f;
        public float bannerHeight = 50f;
        public float spacing = 8f;
        public int maxVisible = 3;

        [Header("Thời lượng (giây)")]
        public float fadeIn = 0.15f;
        public float hold = 1.8f;
        public float fadeOut = 0.4f;

        [Header("Màu")]
        public Color bgColor = new Color(0.07f, 0.08f, 0.12f, 0.95f);
        public Color infoColor = new Color(0.42f, 0.68f, 1.00f);   // xanh — thông tin
        public Color warnColor = new Color(0.98f, 0.55f, 0.30f);   // cam đỏ — cảnh báo
        public Color textColor = new Color(0.95f, 0.97f, 1.00f);

        static GameToast _instance;
        RectTransform _container;
        GameController _subscribed;
        string _lastMsg;
        float _lastTime;
        readonly List<GameObject> _active = new List<GameObject>();

        // ── Bootstrap tự động (không cần đặt vào scene) ───────────
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        static void Bootstrap()
        {
            if (_instance != null) return;
            var go = new GameObject("GameToastSystem");
            DontDestroyOnLoad(go);
            _instance = go.AddComponent<GameToast>();
        }

        void Awake()
        {
            if (_instance != null && _instance != this) { Destroy(gameObject); return; }
            _instance = this;
            BuildCanvas();
        }

        // Tự (re)subscribe GameController — instance mới mỗi lần vào game scene.
        void Update()
        {
            var gc = GameController.Instance;
            if (gc == _subscribed) return;
            if (_subscribed != null) _subscribed.OnActionRejected -= OnRejected;
            _subscribed = gc;
            if (gc != null) gc.OnActionRejected += OnRejected;
        }

        void OnDestroy()
        {
            if (_subscribed != null) _subscribed.OnActionRejected -= OnRejected;
        }

        void OnRejected(string reason)
        {
            if (!string.IsNullOrEmpty(reason)) Warn(reason);
        }

        void BuildCanvas()
        {
            var canvasGO = new GameObject("ToastCanvas");
            canvasGO.transform.SetParent(transform, false);
            var canvas = canvasGO.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 900; // trên gameplay, dưới popup kết quả (999)
            var scaler = canvasGO.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920f, 1080f);
            scaler.matchWidthOrHeight = 0.5f;
            // KHÔNG thêm GraphicRaycaster → click xuyên qua, không chặn thao tác.

            var c = new GameObject("Container").AddComponent<RectTransform>();
            c.SetParent(canvasGO.transform, false);
            c.anchorMin = c.anchorMax = new Vector2(0.5f, 0.5f); // neo TÂM màn hình
            c.pivot = new Vector2(0.5f, 0.5f);
            c.anchoredPosition = new Vector2(horizontalOffset, verticalOffset);
            c.sizeDelta = new Vector2(bannerWidth, 0f);
            var vlg = c.gameObject.AddComponent<VerticalLayoutGroup>();
            vlg.spacing = spacing;
            vlg.childAlignment = TextAnchor.UpperCenter;
            vlg.childControlWidth = true; vlg.childForceExpandWidth = true;
            vlg.childControlHeight = false; vlg.childForceExpandHeight = false;
            _container = c;
        }

        // ── Public API ────────────────────────────────────────────
        public static void Note(string msg) => Push(msg, false);
        public static void Warn(string msg) => Push(msg, true);

        static void Push(string msg, bool warn)
        {
            if (_instance == null) Bootstrap();
            _instance?.Enqueue(msg, warn ? _instance.warnColor : _instance.infoColor);
        }

        void Enqueue(string msg, Color accent)
        {
            if (_container == null || string.IsNullOrEmpty(msg)) return;

            // Chống lặp: cùng message trong 0.5s → bỏ (tránh spam khi bấm liên tục)
            if (msg == _lastMsg && Time.unscaledTime - _lastTime < 0.5f) return;
            _lastMsg = msg; _lastTime = Time.unscaledTime;

            // Dọn banner cũ — xóa khỏi LIST ngay (Destroy là deferred nên KHÔNG dựa vào
            // childCount, tránh vòng lặp vô hạn khi spam làm treo game).
            int limit = Mathf.Max(1, maxVisible);
            while (_active.Count >= limit)
            {
                var old = _active[0];
                _active.RemoveAt(0);
                if (old != null) { old.SetActive(false); Destroy(old); }
            }

            var banner = BuildBanner(msg, accent);
            _active.Add(banner.gameObject);
            StartCoroutine(Animate(banner));
        }

        RectTransform BuildBanner(string msg, Color accent)
        {
            var root = new GameObject("Banner").AddComponent<RectTransform>();
            root.SetParent(_container, false);
            root.gameObject.AddComponent<LayoutElement>().preferredHeight = bannerHeight;

            var bg = root.gameObject.AddComponent<Image>();
            bg.color = bgColor; bg.raycastTarget = false;
            var cg = root.gameObject.AddComponent<CanvasGroup>();
            cg.alpha = 0f; cg.interactable = false; cg.blocksRaycasts = false;

            // Vạch màu mỏng ở đáy (báo trạng thái info/warn, KHÔNG làm lệch chữ)
            var bar = new GameObject("Bar").AddComponent<RectTransform>();
            bar.SetParent(root, false);
            bar.anchorMin = new Vector2(0.5f, 0f); bar.anchorMax = new Vector2(0.5f, 0f);
            bar.pivot = new Vector2(0.5f, 0f);
            bar.anchoredPosition = new Vector2(0f, 3f);
            bar.sizeDelta = new Vector2(bannerWidth * 0.5f, 3f);
            var barImg = bar.gameObject.AddComponent<Image>();
            barImg.color = accent; barImg.raycastTarget = false;

            // Text — CANH GIỮA hoàn toàn
            var tRT = new GameObject("Text").AddComponent<RectTransform>();
            tRT.SetParent(root, false);
            tRT.anchorMin = new Vector2(0f, 0f); tRT.anchorMax = new Vector2(1f, 1f);
            tRT.offsetMin = new Vector2(20f, 5f); tRT.offsetMax = new Vector2(-20f, -3f);
            var tmp = tRT.gameObject.AddComponent<TextMeshProUGUI>();
            tmp.text = msg; tmp.fontSize = 20f; tmp.color = textColor;
            tmp.alignment = TextAlignmentOptions.Center;
            tmp.raycastTarget = false;
            tmp.textWrappingMode = TextWrappingModes.Normal;
            tmp.overflowMode = TextOverflowModes.Ellipsis;
            tmp.enableAutoSizing = true; tmp.fontSizeMin = 13f; tmp.fontSizeMax = 20f;

            return root;
        }

        System.Collections.IEnumerator Animate(RectTransform banner)
        {
            var cg = banner.GetComponent<CanvasGroup>();
            Vector2 basePos = banner.anchoredPosition;
            float t = 0f;
            while (t < fadeIn && banner != null)
            {
                t += Time.unscaledDeltaTime;
                float k = Mathf.SmoothStep(0f, 1f, t / fadeIn);
                if (cg != null) cg.alpha = k;
                banner.anchoredPosition = basePos + new Vector2(0f, (1f - k) * -12f);
                yield return null;
            }
            if (cg != null) cg.alpha = 1f;

            float held = 0f;
            while (held < hold && banner != null) { held += Time.unscaledDeltaTime; yield return null; }

            t = 0f;
            while (t < fadeOut && banner != null)
            {
                t += Time.unscaledDeltaTime;
                if (cg != null) cg.alpha = 1f - (t / fadeOut);
                yield return null;
            }
            if (banner != null)
            {
                _active.Remove(banner.gameObject);
                Destroy(banner.gameObject);
            }
        }
    }
}