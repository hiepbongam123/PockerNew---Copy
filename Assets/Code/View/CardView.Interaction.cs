using System;
using System.Text;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.EventSystems;
using TMPro;
using LoRClone.Model;
using LoRClone.Data;
using LoRClone.Controller;
using LoRClone; // AudioManager

namespace LoRClone.View
{
    /// <summary>
    /// CardView — phần Interaction: click, keyword popup, hover, drag & drop, snap/return helpers.
    /// Partial class: tách từ CardView.cs gốc, KHÔNG đổi logic.
    /// Field khai báo ở CardView.cs (file chính) — mọi partial dùng chung.
    /// </summary>
    public partial class CardView
    {
        // ── Keyword Popup ─────────────────────────────────────────
        void ShowKeywordPopup(KeywordType kw)
        {
            if (_cardWithOpenKeywordPopup != null && _cardWithOpenKeywordPopup != this)
                _cardWithOpenKeywordPopup.HideKeywordPopup();
            _cardWithOpenKeywordPopup = this;

            string popupText = _kwDesc.TryGetValue(kw, out var info)
                ? $"<b>{info.name}</b>\n{info.desc}"
                : kw.ToString();

            // ── Path A: popup prefab đã gán trong Inspector ──────────
            if (keywordPopup != null)
            {
                if (keywordPopupText != null) keywordPopupText.text = popupText;
                else Debug.LogWarning("[CardView] keywordPopupText chưa gán — popup hiện nhưng KHÔNG có chữ.");
                keywordPopup.SetActive(true);

                // Nâng sorting order khi popup mở — card trên sân/tay có canvas order thấp,
                // popup con của card có thể bị card BÊN CẠNH (cùng order, sibling sau) đè lên.
                if (!_inspecting && _selfCanvas != null)
                    _selfCanvas.sortingOrder = 150;
                return;
            }

            // ── Path B: keywordPopup CHƯA gán → TỰ DỰNG popup bằng code ──
            // Cùng cơ chế với nút "Xem ảnh" (ShowViewArtButton): không phụ thuộc prefab setup,
            // canvas riêng sortingOrder cao → không bao giờ bị card khác che.
            CreateRuntimeKeywordPopup(popupText);
        }

        // ── Runtime Keyword Popup (fallback khi prefab chưa gán) ──
        GameObject _runtimeKeywordPopup;

        void CreateRuntimeKeywordPopup(string text)
        {
            DestroyRuntimeKeywordPopup();

            var parent = _rootCanvas != null ? _rootCanvas.transform : transform;
            _runtimeKeywordPopup = new GameObject("_KeywordPopupRuntime");
            _runtimeKeywordPopup.transform.SetParent(parent, false);
            _runtimeKeywordPopup.transform.SetAsLastSibling();

            var cv = _runtimeKeywordPopup.AddComponent<Canvas>();
            cv.overrideSorting = true;
            cv.sortingOrder = 230; // trên related strip (210), dưới art viewer (300)

            var rt = _runtimeKeywordPopup.GetComponent<RectTransform>();
            rt.anchorMin = rt.anchorMax = rt.pivot = new Vector2(0.5f, 0.5f);
            rt.sizeDelta = new Vector2(300f, 110f);

            // Vị trí: phía trên card (card ở nửa dưới màn hình) hoặc phía dưới (card enemy nửa trên)
            Camera cam = _rootCanvas != null && _rootCanvas.renderMode != RenderMode.ScreenSpaceOverlay
                ? _rootCanvas.worldCamera : null;
            Vector2 screenPos = RectTransformUtility.WorldToScreenPoint(cam, transform.position);
            Vector2 local = Vector2.zero;
            var canvasRT = _rootCanvas != null ? _rootCanvas.GetComponent<RectTransform>() : null;
            if (canvasRT != null)
                RectTransformUtility.ScreenPointToLocalPointInRectangle(canvasRT, screenPos, cam, out local);
            float cardHalfH = _rect.rect.height * Mathf.Abs(transform.localScale.y) * 0.5f;
            float dir = local.y <= 0f ? 1f : -1f;
            rt.anchoredPosition = local + new Vector2(0f, dir * (cardHalfH + rt.sizeDelta.y * 0.5f + 10f));

            var bg = _runtimeKeywordPopup.AddComponent<Image>();
            bg.color = new Color(0.05f, 0.05f, 0.10f, 0.95f);
            bg.raycastTarget = false; // không chặn raycast — click card lần nữa để đóng

            var tmp = MakeTMPLabel(_runtimeKeywordPopup.transform, text, 14f, Color.white,
                TextAlignmentOptions.TopLeft, bold: false,
                offsetMin: new Vector2(10f, 8f), offsetMax: new Vector2(-10f, -8f));
            tmp.textWrappingMode = TextWrappingModes.Normal;
            tmp.overflowMode = TextOverflowModes.Ellipsis;
        }

        void DestroyRuntimeKeywordPopup()
        {
            if (_runtimeKeywordPopup == null) return;
            Destroy(_runtimeKeywordPopup);
            _runtimeKeywordPopup = null;
        }

        public void HideKeywordPopup()
        {
            if (keywordPopup != null) keywordPopup.SetActive(false);
            DestroyRuntimeKeywordPopup();
            if (_cardWithOpenKeywordPopup == this) _cardWithOpenKeywordPopup = null;
            // Trả sorting order về mặc định (trừ khi đang inspect/drag — các state đó tự quản lý order)
            if (!_inspecting && !_dragging && _selfCanvas != null)
                _selfCanvas.sortingOrder = _baseSortingOrder;
        }

        // ── Debug ─────────────────────────────────────────────────
        /// <summary>
        /// Bật log chẩn đoán keyword popup. Đặt false khi đã fix xong.
        /// Đọc Console để biết click chết ở giai đoạn nào:
        ///   không có log OnPointerClick → click không tới card (bị UI khác chặn raycast)
        ///   có OnPointerClick, không có "trúng icon" → hit-test trượt icon
        ///   có warning "keywordPopup CHƯA gán" → thiếu reference trong prefab
        /// </summary>
        public static bool DebugKeywordClicks = true;

        // ── Click ─────────────────────────────────────────────────
        public void OnPointerClick(PointerEventData eventData)
        {
            if (DebugKeywordClicks)
                Debug.Log($"[CardView] OnPointerClick: {model?.data?.cardName} " +
                          $"(loc={model?.location}, inspecting={_inspecting}, allowInteract={AllowInteract})");

            // Safety: OnPointerClick chỉ fire khi mouse đã release → _dragging không thể true.
            // Nếu true = stale state (OnEndDrag bị miss) → reset để card hoạt động trở lại.
            if (_dragging)
            {
                _dragging = false;
                if (_cg != null) _cg.blocksRaycasts = true;
                if (CurrentDragging == this) CurrentDragging = null;
                return;
            }
            if (_artViewerOpen) return;
            // Đang xem lá liên quan — click vào lá gốc (bên trái) để quay lại
            if (_navigatedToRelated) { ReturnFromRelated(); return; }
            bool popupOpen = (keywordPopup != null && keywordPopup.activeSelf) || _runtimeKeywordPopup != null;
            if (popupOpen) { HideKeywordPopup(); return; }

            // Fix: thử keyword click TRƯỚC khi kiểm tra _inspecting.
            // Trước đây TryHandleKeywordClick chỉ gọi khi _inspecting = true,
            // nên click keyword khi card trên sân (chưa inspect) không bao giờ
            // hiện popup — thay vào đó nó đi thẳng vào EnterInspect().
            if (TryHandleKeywordClick(eventData.position)) return;

            if (_inspecting) return; // không có action nào khác khi đang inspect

            // Inspect + keyword luôn hoạt động dù AllowInteract = false.
            // AllowInteract chỉ chặn drag / ClickShouldExpand (chơi bài / tấn công).
            if (TargetingArrow.Instance != null && TargetingArrow.Instance.IsTargeting)
            {
                if (model != null && TargetingArrow.Instance.IsValidTarget(model))
                    TargetingArrow.Instance.ConfirmTarget(model);
                return;
            }
            if (ClickShouldExpand != null && ClickShouldExpand()) return;
            EnterInspect();
        }

        bool TryHandleKeywordClick(Vector2 screenPos)
        {
            if (keywordsContainer == null) return false;
            Camera cam = _rootCanvas != null && _rootCanvas.renderMode != RenderMode.ScreenSpaceOverlay
                ? _rootCanvas.worldCamera : null;
            foreach (Transform child in keywordsContainer)
            {
                var rt = child.GetComponent<RectTransform>();
                if (rt == null) continue;
                if (!RectTransformUtility.RectangleContainsScreenPoint(rt, screenPos, cam)) continue;
                if (System.Enum.TryParse<KeywordType>(child.name, out var kw))
                {
                    if (DebugKeywordClicks)
                        Debug.Log($"[CardView] Trúng icon keyword: {kw} trên {model?.data?.cardName}");
                    ShowKeywordPopup(kw);
                    return true;
                }
            }
            if (DebugKeywordClicks && keywordsContainer.childCount > 0)
                Debug.Log($"[CardView] Click KHÔNG trúng icon nào " +
                          $"({keywordsContainer.childCount} icon) trên {model?.data?.cardName}.");
            return false;
        }

        // ── Hover ─────────────────────────────────────────────────
        public void OnPointerEnter(PointerEventData _)
        {
            if (!AllowInteract) return;
            if (!_dragging && !_inspecting)
            { _selfCanvas.sortingOrder = 50; transform.localScale = Vector3.one * (_sizeScale * hoverScale); }
        }
        public void OnPointerExit(PointerEventData _)
        {
            if (!AllowInteract) return;
            if (!_dragging && !_inspecting)
            { _selfCanvas.sortingOrder = _baseSortingOrder; transform.localScale = Vector3.one * _sizeScale; }
        }

        // ── Drag ──────────────────────────────────────────────────
        /// <summary>
        /// Card đang được kéo (null nếu không có). GameView đọc mỗi frame để làm
        /// sweep-declare: giữ 1 unit bench và quét qua các unit khác → declare attack hàng loạt.
        /// </summary>
        public static CardView CurrentDragging { get; private set; }

        public void OnBeginDrag(PointerEventData eventData)
        {
            if (model == null) return;
            bool challengerPull = GameController.Instance != null
                                  && GameController.Instance.CanChallengerPull(model);
            if (!AllowInteract && !challengerPull) return;  // lock drag; trừ khi kéo quân địch bằng Challenger
            // NET: CHỈ được kéo lá của CHÍNH MÌNH (theo NetLocalIsPlayer), trừ Challenger kéo quân địch.
            // FIX: guard cũ chỉ theo LƯỢT (isPlayerPriority) → khi phe địch tới lượt, máy này kéo được quân địch.
            //      PvE/hotseat (networkMode=false) BỎ QUA guard này → không đổi hành vi cũ.
            if (GameController.Instance.networkMode
                && model.belongsToPlayer != GameController.NetLocalIsPlayer && !challengerPull) return;
            if (model.belongsToPlayer != GameController.Instance.model.isPlayerPriority && !challengerPull) return;
            HideKeywordPopup();
            if (_inspecting) ExitInspect();
            _dragging = true;
            CurrentDragging = this;
            _originalParent = transform.parent; _originalPos = _rect.anchoredPosition;
            _originalSibling = transform.GetSiblingIndex();
            _dragStartScreenPos = eventData.position;
            if (_rootCanvas != null) transform.SetParent(_rootCanvas.transform, worldPositionStays: true);
            transform.SetAsLastSibling(); _selfCanvas.sortingOrder = 100;
            _cg.blocksRaycasts = false;
        }
        public void OnDrag(PointerEventData eventData)
        {
            if (!_dragging) return;
            float scale = _rootCanvas != null ? _rootCanvas.scaleFactor : 1f;
            _rect.anchoredPosition += eventData.delta / scale;
        }
        public void OnEndDrag(PointerEventData eventData)
        {
            if (!_dragging) return;
            _dragging = false; _cg.blocksRaycasts = true;
            if (CurrentDragging == this) CurrentDragging = null;
            // Fix: dùng _baseSortingOrder thay vì hardcode 1 — tránh z-fighting
            // khi card ở tay có sorting order khác 1 (gán bởi SetHandSortingOrder).
            _selfCanvas.sortingOrder = _baseSortingOrder; transform.localScale = Vector3.one * _sizeScale;
            transform.SetParent(_originalParent, worldPositionStays: true);
            transform.SetSiblingIndex(_originalSibling);
            OnDragIntent?.Invoke(this, ComputeDragIntent(eventData.position));
            OnDropped?.Invoke(this, eventData.position);
        }
        DragIntent ComputeDragIntent(Vector2 endScreenPos)
        {
            float deltaY = endScreenPos.y - _dragStartScreenPos.y;
            if (Mathf.Abs(deltaY) < dragIntentThreshold) return DragIntent.None;
            bool draggedUp = deltaY > 0f;
            bool mine = model != null && model.belongsToPlayer == LoRClone.Controller.GameController.NetLocalIsPlayer;
            bool isForward = mine ? draggedUp : !draggedUp;
            return isForward ? DragIntent.Forward : DragIntent.Backward;
        }

        // ── Helpers ───────────────────────────────────────────────
        public void ReturnToPlace()
        {
            if (_inspecting) ExitInspect();
            _rect.anchoredPosition = _originalPos;
            transform.localEulerAngles = _originalRotation;
            transform.localScale = Vector3.one * _sizeScale;
        }
        public void SnapToSlot(Transform slotTransform) => SnapTo(slotTransform);
        public void SnapTo(Transform slotTransform)
        {
            transform.SetParent(slotTransform, worldPositionStays: false);
            _rect.anchoredPosition = Vector2.zero; transform.localScale = Vector3.one;
            _originalPos = Vector2.zero; _originalParent = slotTransform;
        }
        public void SaveCurrentPosition()
        {
            _originalPos = _rect.anchoredPosition; _originalParent = transform.parent;
            _originalSibling = transform.GetSiblingIndex(); _originalRotation = transform.localEulerAngles;
        }
    }
}