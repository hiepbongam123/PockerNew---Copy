using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using LoRClone.Model;

namespace LoRClone.View
{
    /// <summary>
    /// Vẽ mũi tên targeting — chuẩn hóa theo hành vi LoR gốc.
    ///
    /// THIẾT KẾ:
    ///   Locked arrows và Selecting arrow HOÀN TOÀN ĐỘC LẬP:
    ///   - Locked arrows: List toàn cục, vẽ MỌI frame bất kể mode.
    ///     Mỗi spell trên stack/staging có 1 entry. Nhiều spell = nhiều arrows đồng thời.
    ///   - Selecting arrow: chỉ bật khi player đang chọn target (mode Selecting).
    ///     Không ảnh hưởng đến locked arrows của các spell khác.
    ///
    /// Vòng đời mũi tên:
    ///   Stage spell → player chọn target → confirm → LockArrow thêm entry → arrow ở lại
    ///   Dùng spell khác → StartTargeting lại → locked arrow cũ VẪN hiện
    ///   Spell resolve → GameView gọi RemoveLockedArrow → entry bị xóa
    /// </summary>
    public class TargetingArrow : MonoBehaviour
    {
        public static TargetingArrow Instance { get; private set; }

        [Header("Canvas gốc của toàn bộ UI")]
        public Canvas rootCanvas;

        [Header("UI — line và arrowhead chính (dùng làm template cho pool)")]
        public Image lineImage;
        public Image arrowheadImage;

        [Header("Tuỳ chỉnh")]
        public float lineThickness = 6f;
        public Color selectingColor = new Color(1f, 0.55f, 0f, 0.92f); // cam
        public Color lockedColor = new Color(1f, 0.25f, 0.25f, 0.85f); // đỏ
        public Color intermediateColor = new Color(1f, 0.25f, 0.25f, 0.55f); // đỏ mờ hơn

        // ── Locked arrows — ĐỘC LẬP với Selecting state ─────────
        // Key = spell CardView (dùng làm id); Value = danh sách target CVs
        // Vẽ mỗi frame bất kể mode hiện tại.
        readonly List<LockedArrowEntry> _lockedArrows = new List<LockedArrowEntry>();

        struct LockedArrowEntry
        {
            public CardView spellCV;
            public List<CardView> targetCVs;
        }

        // ── Selecting state ───────────────────────────────────────
        bool _selecting;
        Vector2 _srcScreen;
        CardView _srcCV;           // null khi Burst
        Action<CardModel> _onConfirmed;
        Action _onCancel;
        List<CardView> _validViews = new List<CardView>();

        // Intermediate arrows (targets đã confirm trong session hiện tại)
        readonly List<(CardView srcCV, Vector2 srcFallback, CardView targetCV)> _pending = new List<(CardView, Vector2, CardView)>();

        // ── Pool — clone line+head để vẽ nhiều arrows ────────────
        readonly List<(RectTransform lineRT, Image lineImg, RectTransform headRT, Image headImg)> _pool = new List<(RectTransform, Image, RectTransform, Image)>();

        public bool IsTargeting => _selecting;

        // ── Lifecycle ─────────────────────────────────────────────
        void Awake()
        {
            if (Instance != null && Instance != this) { Destroy(gameObject); return; }
            Instance = this;
        }

        void Start() => SetMainVisible(false);

        void Update()
        {
            // 1) Locked arrows — luôn vẽ, bất kể mode
            int poolSlot = 0;
            foreach (var entry in _lockedArrows)
            {
                if (entry.spellCV == null) continue;
                var src = UIWorldToScreen(entry.spellCV.transform.position);
                foreach (var tgt in entry.targetCVs)
                {
                    if (tgt == null) continue;
                    var to = UIWorldToScreen(tgt.transform.position);
                    DrawPool(poolSlot, src, to, lockedColor);
                    poolSlot++;
                }
            }
            HidePoolFrom(poolSlot); // ẩn pool entries không dùng

            // 2) Selecting mode — intermediate + selecting arrow
            if (!_selecting) return;

            if (Input.GetKeyDown(KeyCode.Escape) || Input.GetMouseButtonDown(1))
            { Cancel(); return; }

            // Intermediate arrows (target đã confirm nhưng session chưa xong)
            foreach (var (srcCV, fallback, tgt) in _pending)
            {
                if (tgt == null) continue;
                var from = ResolveSrc(srcCV, fallback);
                var to = UIWorldToScreen(tgt.transform.position);
                DrawMain(from, to, intermediateColor);
                // Note: khi có nhiều pending, chỉ cần main để vẽ arrow cuối
                // pool đã dùng cho locked; tạm thời dùng main cho intermediate
                // TODO: nếu cần hiện tất cả pending, extend pool thêm
            }

            // Mũi tên chính: nguồn → chuột
            var srcScreen = ResolveSrc(_srcCV, _srcScreen);
            DrawMain(srcScreen, Input.mousePosition, selectingColor);
        }

        // ── Selecting API ─────────────────────────────────────────

        public void StartTargeting(
            CardView srcCV, Vector2 srcScreen,
            List<CardView> validTargets,
            Action<CardModel> onConfirmed,
            Action onCancel = null)
        {
            // KHÔNG xóa locked arrows — các spell khác trên stack vẫn hiện arrow
            _selecting = true;
            _srcCV = srcCV;
            _srcScreen = srcScreen;
            _onConfirmed = onConfirmed;
            _onCancel = onCancel;

            _validViews = validTargets != null ? new List<CardView>(validTargets) : new List<CardView>();
            foreach (var v in _validViews) v.SetSelected(true);

            SetMainVisible(true);
        }

        // Overload backward-compat (Burst — không có srcCV)
        public void StartTargeting(
            Vector2 srcScreen, List<CardView> validTargets,
            Action<CardModel> onConfirmed, Action onCancel = null)
            => StartTargeting(null, srcScreen, validTargets, onConfirmed, onCancel);

        public void Cancel()
        {
            if (!_selecting) return;
            ClearSelectingState();
            _onCancel?.Invoke();
            _onConfirmed = null;
            _onCancel = null;
        }

        public void ConfirmTarget(CardModel target)
        {
            if (!_selecting) return;
            ClearSelectingHighlights();
            SetMainVisible(false);
            _selecting = false;
            var cb = _onConfirmed;
            _onConfirmed = null;
            _onCancel = null;
            cb?.Invoke(target);
        }

        public bool IsValidTarget(CardModel card)
        {
            foreach (var v in _validViews)
                if (v != null && v.model == card) return true;
            return false;
        }

        // ── Intermediate arrows (multi-target trong session) ──────

        public void AddPendingArrow(CardView srcCV, Vector2 srcFallback, CardView targetCV)
        {
            _pending.Add((srcCV, srcFallback, targetCV));
        }

        public void ClearPendingArrows()
        {
            _pending.Clear();
        }

        // ── Locked arrows API ─────────────────────────────────────

        /// <summary>
        /// Thêm hoặc cập nhật locked arrows cho một spell.
        /// Gọi sau khi tất cả target được chọn và mỗi OnStateChanged (CV có thể rebuild).
        /// </summary>
        public void SetLockedArrows(CardView spellCV, List<CardView> targetCVs)
        {
            // Tìm entry theo spellCV — nếu có thì update, không thì thêm mới
            for (int i = 0; i < _lockedArrows.Count; i++)
            {
                if (_lockedArrows[i].spellCV == spellCV)
                {
                    _lockedArrows[i] = new LockedArrowEntry { spellCV = spellCV, targetCVs = targetCVs };
                    return;
                }
            }
            _lockedArrows.Add(new LockedArrowEntry { spellCV = spellCV, targetCVs = targetCVs });
        }

        /// <summary>Xóa locked arrows của một spell cụ thể (khi spell resolve hoặc unstage).</summary>
        public void RemoveLockedArrows(CardView spellCV)
        {
            _lockedArrows.RemoveAll(e => e.spellCV == spellCV || e.spellCV == null);
        }

        /// <summary>Xóa TẤT CẢ locked arrows (game reset hoặc end of game).</summary>
        public void ClearAllLockedArrows()
        {
            _lockedArrows.Clear();
            HidePoolFrom(0);
        }

        // Backward-compat: single target, single spell
        public void LockArrow(CardView spellCV, CardView targetCV)
            => SetLockedArrows(spellCV, new List<CardView> { targetCV });

        public void LockArrows(CardView spellCV, List<CardView> targetCVs)
            => SetLockedArrows(spellCV, targetCVs);

        public void ClearLocked()
        {
            ClearAllLockedArrows();
            if (_selecting) ClearSelectingState();
        }

        // ── Helpers ───────────────────────────────────────────────

        public Vector2 UIWorldToScreen(Vector3 worldPos)
        {
            if (rootCanvas == null) return worldPos;
            return rootCanvas.renderMode == RenderMode.ScreenSpaceOverlay
                ? (Vector2)worldPos
                : RectTransformUtility.WorldToScreenPoint(rootCanvas.worldCamera, worldPos);
        }

        Vector2 ResolveSrc(CardView cv, Vector2 fallback)
            => cv != null ? UIWorldToScreen(cv.transform.position) : fallback;

        void DrawMain(Vector2 from, Vector2 to, Color color)
        {
            if (lineImage == null) return;
            ApplyArrow(lineImage.rectTransform, lineImage,
                       arrowheadImage?.rectTransform, arrowheadImage,
                       from, to, color);
        }

        void DrawPool(int slot, Vector2 from, Vector2 to, Color color)
        {
            EnsurePoolSize(slot + 1);
            var (lRT, lImg, hRT, hImg) = _pool[slot];
            ApplyArrow(lRT, lImg, hRT, hImg, from, to, color);
            lRT?.gameObject.SetActive(true);
            hRT?.gameObject.SetActive(true);
        }

        void ApplyArrow(RectTransform lineRT, Image lineImg,
                        RectTransform headRT, Image headImg,
                        Vector2 fromScreen, Vector2 toScreen, Color color)
        {
            if (lineRT == null || rootCanvas == null) return;
            var canvasRT = rootCanvas.GetComponent<RectTransform>();
            Camera cam = rootCanvas.renderMode == RenderMode.ScreenSpaceOverlay ? null : rootCanvas.worldCamera;
            RectTransformUtility.ScreenPointToLocalPointInRectangle(canvasRT, fromScreen, cam, out var from);
            RectTransformUtility.ScreenPointToLocalPointInRectangle(canvasRT, toScreen, cam, out var to);
            Vector2 dir = to - from;
            float dist = dir.magnitude;
            if (dist < 1f) return;
            float angle = Mathf.Atan2(dir.y, dir.x) * Mathf.Rad2Deg;
            if (lineImg) lineImg.color = color;
            lineRT.anchoredPosition = from + dir * 0.5f;
            lineRT.sizeDelta = new Vector2(dist, lineThickness);
            lineRT.localRotation = Quaternion.Euler(0f, 0f, angle);
            if (headRT != null && headImg != null)
            {
                headImg.color = color;
                headRT.anchoredPosition = to;
                headRT.localRotation = Quaternion.Euler(0f, 0f, angle - 90f);
            }
        }

        void EnsurePoolSize(int needed)
        {
            while (_pool.Count < needed)
            {
                if (lineImage == null) { _pool.Add((null, null, null, null)); continue; }
                var lineGO = Instantiate(lineImage.gameObject, lineImage.transform.parent);
                var lineRT = lineGO.GetComponent<RectTransform>();
                var lineImg = lineGO.GetComponent<Image>();
                lineGO.SetActive(false);
                RectTransform headRT = null; Image headImg = null;
                if (arrowheadImage != null)
                {
                    var headGO = Instantiate(arrowheadImage.gameObject, arrowheadImage.transform.parent);
                    headRT = headGO.GetComponent<RectTransform>();
                    headImg = headGO.GetComponent<Image>();
                    headGO.SetActive(false);
                }
                _pool.Add((lineRT, lineImg, headRT, headImg));
            }
        }

        void HidePoolFrom(int startIndex)
        {
            for (int i = startIndex; i < _pool.Count; i++)
            {
                _pool[i].lineRT?.gameObject.SetActive(false);
                _pool[i].headRT?.gameObject.SetActive(false);
            }
        }

        void SetMainVisible(bool v)
        {
            if (lineImage) lineImage.gameObject.SetActive(v);
            if (arrowheadImage) arrowheadImage.gameObject.SetActive(v);
        }

        void ClearSelectingHighlights()
        {
            foreach (var v in _validViews) if (v != null) v.SetSelected(false);
            _validViews.Clear();
        }

        void ClearSelectingState()
        {
            ClearSelectingHighlights();
            ClearPendingArrows();
            SetMainVisible(false);
            _selecting = false;
        }
    }
}