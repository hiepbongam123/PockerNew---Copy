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
    /// CardView — phần Inspect: zoom xem card, dim overlay, điều hướng lá liên quan, card preview, slide/scale coroutines, TMP label helper.
    /// Partial class: tách từ CardView.cs gốc, KHÔNG đổi logic.
    /// Field khai báo ở CardView.cs (file chính) — mọi partial dùng chung.
    /// </summary>
    public partial class CardView
    {
        // ── Inspect ───────────────────────────────────────────────
        void EnterInspect()
        {
            if (_faceDown) return;   // không cho zoom xem lá địch đang úp
            if (_currentInspecting != null && _currentInspecting != this)
                _currentInspecting.ExitInspect();
            _currentInspecting = this;
            _inspecting = true;
            if (_inCircleMode) RemoveCircleVisuals();
            _originalPos = _rect.anchoredPosition;
            _originalParent = transform.parent;
            _originalSibling = transform.GetSiblingIndex();
            _originalAnchorMin = _rect.anchorMin;
            _originalAnchorMax = _rect.anchorMax;
            _originalPivot = _rect.pivot;
            _originalRotation = transform.localEulerAngles;
            if (_rootCanvas != null)
                transform.SetParent(_rootCanvas.transform, worldPositionStays: true);
            transform.SetAsLastSibling();
            _selfCanvas.sortingOrder = inspectSortingBase + 200;

            _rect.anchorMin = new Vector2(0.5f, 0.5f);
            _rect.anchorMax = new Vector2(0.5f, 0.5f);
            _rect.pivot = new Vector2(0.5f, 0.5f);
            _rect.sizeDelta = _portraitCardSize;
            if (artworkRawImage != null)
            {
                var artRT = artworkRawImage.GetComponent<RectTransform>();
                artRT.anchorMin = new Vector2(0.5f, 0.5f);
                artRT.anchorMax = new Vector2(0.5f, 0.5f);
                artRT.pivot = new Vector2(0.5f, 0.5f);
                artRT.anchoredPosition = Vector2.zero;
                artRT.sizeDelta = _portraitArtworkSize;
            }
            bool onField = model != null
                && (model.location == CardLocation.OnBench || model.location == CardLocation.OnBattlefield);
            RefreshArtworkUV(onField);
            _rect.anchoredPosition = CanvasCenter();
            transform.localScale = Vector3.one * inspectScale;
            transform.localEulerAngles = Vector3.zero;

            if (nameGroup) nameGroup.SetActive(true);
            else if (nameText) nameText.gameObject.SetActive(true);
            if (manaGroup) manaGroup.SetActive(true);
            if (statsGroup) statsGroup.SetActive(model.data.cardType == CardType.Unit);
            if (skillsText)
            {
                skillsText.gameObject.SetActive(true);
                skillsText.overflowMode = TextOverflowModes.Ellipsis;
                skillsText.textWrappingMode = TextWrappingModes.Normal;
            }
            if (keywordsContainer != null)
            {
                var kwRT = keywordsContainer.GetComponent<RectTransform>();
                if (kwRT != null)
                {
                    kwRT.anchorMin = new Vector2(0.5f, 0.5f);
                    kwRT.anchorMax = new Vector2(0.5f, 0.5f);
                    kwRT.pivot = new Vector2(0.5f, 0.5f);
                    kwRT.anchoredPosition = Vector2.zero;
                    // Fix: rebuild ngay để ContentSizeFitter cập nhật bounds
                    // trước khi TryHandleKeywordClick dùng RectTransformUtility hit-test.
                    UnityEngine.UI.LayoutRebuilder.ForceRebuildLayoutImmediate(kwRT);
                }
            }
            if (statsGroup != null)
            {
                var rt = statsGroup.GetComponent<RectTransform>();
                if (rt != null)
                {
                    rt.anchorMin = new Vector2(0f, 0f); rt.anchorMax = new Vector2(1f, 0f);
                    rt.pivot = new Vector2(0.5f, 0f);
                    rt.anchoredPosition = new Vector2(0f, statsEdgeOffset);
                }
            }

            _inspectCenterPos = _rect.anchoredPosition;

            // Bật cardFrame + inspectOverlay khi inspect
            if (cardFrame != null) cardFrame.enabled = true;
            if (inspectOverlay != null) inspectOverlay.SetActive(true);

            CreateInspectDim();
            ShowViewArtButton();
            ShowRelatedSideCards();
            ShowBuffHistoryPanel();
            ShowItemPanel();
        }

        void ExitInspect()
        {
            if (_slideCoroutine != null) { StopCoroutine(_slideCoroutine); _slideCoroutine = null; }
            _navigatedToRelated = false;
            HideBackLabel();
            DestroyRelatedCardPreview();
            DestroyInspectDim();
            CloseArtworkViewer();
            HideViewArtButton();
            HideRelatedSideCards();
            HideBuffHistoryPanel();
            HideItemPanel();
            HideKeywordPopup();
            _inspecting = false;
            if (_currentInspecting == this) _currentInspecting = null;

            // Guard: Unity lỗi "Cannot set the parent while activating/deactivating Canvas"
            // nếu ta gọi SetParent trong lúc canvas đang bật/tắt (activeInHierarchy = false).
            // Trường hợp đó, OnEnable sẽ restore transform sau khi canvas bật lại.
            // Fix: kiểm tra thêm _originalParent.gameObject — slot có thể bị destroy
            // giữa lúc EnterInspect và ExitInspect, gây MissingReferenceException.
            if (gameObject.activeInHierarchy && _originalParent != null
                && _originalParent.gameObject != null)
            {
                transform.SetParent(_originalParent, worldPositionStays: true);
                transform.SetSiblingIndex(_originalSibling);
                _rect.anchorMin = _originalAnchorMin;
                _rect.anchorMax = _originalAnchorMax;
                _rect.pivot = _originalPivot;
                _rect.anchoredPosition = _originalPos;
                transform.localEulerAngles = _originalRotation;
                transform.localScale = Vector3.one * _sizeScale;
                _selfCanvas.sortingOrder = 1;
                if (skillsText) skillsText.gameObject.SetActive(false);
                if (inspectOverlay != null) inspectOverlay.SetActive(false);
                RefreshVisibilityByLocation();
                if (_inCircleMode) ApplyCircleVisuals();
                else if (_hasFieldSize)
                {
                    ApplySizeInternal(_fieldCardSize.x, _fieldCardSize.y);
                    ForceArtworkUV(_fieldArtworkSize.x, _fieldArtworkSize.y);
                }
            }
            OnInspectClosed?.Invoke();
        }

        Vector2 CanvasCenter()
        {
            if (_rootCanvas == null) return Vector2.zero;
            var canvasRect = _rootCanvas.GetComponent<RectTransform>();
            var screenCenter = new Vector2(Screen.width * 0.5f, Screen.height * 0.5f);
            Camera cam = _rootCanvas.renderMode == RenderMode.ScreenSpaceOverlay ? null : _rootCanvas.worldCamera;
            RectTransformUtility.ScreenPointToLocalPointInRectangle(canvasRect, screenCenter, cam, out var localPoint);
            return localPoint;
        }

        float GetCanvasHalfW() => _rootCanvas != null
            ? _rootCanvas.GetComponent<RectTransform>().rect.width * 0.5f : 400f;

        // ── Inspect Dim (LoR-style dark overlay) ──────────────────
        void CreateInspectDim()
        {
            if (_inspectDim != null) return;
            var parent = _rootCanvas != null ? _rootCanvas.transform : transform.parent;
            _inspectDim = new GameObject("_InspectDim");
            _inspectDim.transform.SetParent(parent, false);
            // Dim phải nằm TRƯỚC card trong hierarchy để render dưới card
            // Card được SetAsLastSibling() trong EnterInspect nên dim phải được thêm sau đó và đặt dưới.
            // Dùng sortingOrder thay vì sibling index để đảm bảo thứ tự.
            var c = _inspectDim.AddComponent<Canvas>();
            c.overrideSorting = true;
            c.sortingOrder = inspectSortingBase + 194; // dưới card (200) và preview (201)
            _inspectDim.AddComponent<GraphicRaycaster>();
            var rt = _inspectDim.GetComponent<RectTransform>();
            rt.anchorMin = Vector2.zero; rt.anchorMax = Vector2.one;
            rt.offsetMin = rt.offsetMax = Vector2.zero;
            var img = _inspectDim.AddComponent<Image>();
            img.color = new Color(0f, 0f, 0f, 0.80f);
            var btn = _inspectDim.AddComponent<Button>();
            btn.targetGraphic = img;
            var bc = btn.colors;
            bc.normalColor = bc.highlightedColor = Color.white;
            bc.pressedColor = new Color(1f, 1f, 1f, 0.88f);
            btn.colors = bc;
            btn.onClick.AddListener(() =>
            {
                if (_navigatedToRelated) ReturnFromRelated();
                else ExitInspect();
            });
        }

        void DestroyInspectDim()
        {
            if (_inspectDim == null) return;
            Destroy(_inspectDim); _inspectDim = null;
        }

        // ── Navigate to Related Card ───────────────────────────────
        void NavigateToRelated(CardData data)
        {
            if (data == null) return;
            HideBackLabel();
            DestroyRelatedCardPreview();
            if (_slideCoroutine != null) { StopCoroutine(_slideCoroutine); _slideCoroutine = null; }

            bool alreadyNavigated = _navigatedToRelated;
            _navigatedToRelated = true;

            // Ẩn side cards để tránh hiện cùng lá 2 lần
            HideRelatedSideCards();
            HideBuffHistoryPanel();
            HideItemPanel();

            float halfW = GetCanvasHalfW();
            // Lá gốc slide sang trái, nhường chỗ cho preview ở giữa
            Vector2 leftPos = new Vector2(-halfW * 0.55f, 0f);
            float leftScale = inspectScale * 0.40f;

            if (!alreadyNavigated)
                _slideCoroutine = StartCoroutine(DoSlide(_rect, leftPos, leftScale, 0.22f));
            // Nếu đã navigate, card giữ nguyên trái, chỉ thay preview

            _relatedCardPreview = CreateCardPreview(data);
            var previewRT = _relatedCardPreview.GetComponent<RectTransform>();
            previewRT.anchoredPosition = Vector2.zero;

            _relatedCardPreview.transform.localScale = Vector3.zero;
            StartCoroutine(DoScaleIn(_relatedCardPreview.transform, inspectScale, 0.18f));

            ShowBackLabel();
        }

        void ReturnFromRelated()
        {
            if (!_navigatedToRelated) return;
            _navigatedToRelated = false;
            HideBackLabel();
            DestroyRelatedCardPreview();
            if (_slideCoroutine != null) { StopCoroutine(_slideCoroutine); _slideCoroutine = null; }
            // Hiện lại side cards
            ShowRelatedSideCards();
            ShowBuffHistoryPanel();
            ShowItemPanel();
            // Slide card về trung tâm
            _slideCoroutine = StartCoroutine(DoSlide(_rect, _inspectCenterPos, inspectScale, 0.20f));
        }

        void DestroyRelatedCardPreview()
        {
            if (_relatedCardPreview == null) return;
            Destroy(_relatedCardPreview); _relatedCardPreview = null;
        }

        // ── Card Preview (full visual) ─────────────────────────────
        /// <summary>
        /// Nếu cardPrefab đã gán trong Inspector:
        ///   → Instantiate prefab thật, set text/artwork trực tiếp. Layout y hệt card trong game.
        /// Nếu cardPrefab == null (fallback):
        ///   → Tạo thủ công từng element (background, art, text…).
        /// </summary>
        GameObject CreateCardPreview(CardData data)
        {
            var parent = _rootCanvas != null ? _rootCanvas.transform : transform.parent;

            // ══ PATH A: dùng card prefab thật ════════════════════════
            if (cardPrefab != null)
            {
                var go = Instantiate(cardPrefab, parent, false);
                go.name = "_RelatedCardPreview";
                go.transform.SetAsLastSibling();

                // Override canvas sorting (prefab Awake đã tạo Canvas, ghi đè sortingOrder)
                var selfCv = go.GetComponent<Canvas>();
                if (selfCv == null) selfCv = go.AddComponent<Canvas>();
                selfCv.overrideSorting = true;
                selfCv.sortingOrder = inspectSortingBase + 201;
                if (go.GetComponent<GraphicRaycaster>() == null)
                    go.AddComponent<GraphicRaycaster>();

                // RT: center canvas, giữ nguyên sizeDelta của prefab
                var rt = go.GetComponent<RectTransform>();
                rt.anchorMin = rt.anchorMax = rt.pivot = new Vector2(0.5f, 0.5f);
                rt.anchoredPosition = Vector2.zero;

                // Set visuals trực tiếp (không Bind CardModel để tránh event subscriptions)
                var cvw = go.GetComponent<CardView>();
                if (cvw != null)
                {
                    // Text cơ bản
                    if (cvw.nameText) cvw.nameText.text = data.cardName;
                    if (cvw.manaCostText) cvw.manaCostText.text = data.manaCost.ToString();
                    if (cvw.attackText) cvw.attackText.text = data.baseAttack.ToString();
                    if (cvw.healthText) cvw.healthText.text = data.baseHealth.ToString();

                    // Artwork + focal UV
                    if (cvw.artworkRawImage != null && data.artwork != null)
                    {
                        cvw.artworkRawImage.texture = data.artwork;
                        // ApplyFocalUV đọc đúng focalX, focalY VÀ artworkZoom từ CardData.
                        // ForceArtworkUV không dùng được vì GetZoom() trả 1f khi model == null.
                        ApplyFocalUV(cvw.artworkRawImage, data,
                            _portraitArtworkSize.x, _portraitArtworkSize.y);
                    }

                    // Description + skills text
                    if (cvw.skillsText != null)
                    {
                        var sb = new StringBuilder();
                        if (data.cardType == CardType.Spell && data.spellSpeed != default)
                            sb.AppendLine($"[{data.spellSpeed} Spell]");
                        if (!string.IsNullOrWhiteSpace(data.description))
                            sb.AppendLine(data.description);
                        if (data.skills != null)
                            foreach (var s in data.skills)
                                if (s != null && !string.IsNullOrWhiteSpace(s.description))
                                    sb.AppendLine("• " + s.description);
                        cvw.skillsText.text = sb.ToString().TrimEnd();
                        cvw.skillsText.overflowMode = TextOverflowModes.Ellipsis;
                        cvw.skillsText.textWrappingMode = TextWrappingModes.Normal;
                        cvw.skillsText.gameObject.SetActive(true);
                    }

                    // Visibility — giống inspect mode
                    if (cvw.nameGroup) cvw.nameGroup.SetActive(true);
                    else if (cvw.nameText) cvw.nameText.gameObject.SetActive(true);
                    if (cvw.manaGroup) cvw.manaGroup.SetActive(true);
                    if (cvw.statsGroup) cvw.statsGroup.SetActive(data.cardType == CardType.Unit);

                    // Keywords — xóa sạch container rồi populate từ data (không dùng model).
                    // Cần làm thủ công vì cvw.enabled = false nên RefreshKeywords() sẽ không chạy.
                    // Nếu bỏ qua bước này, prefab có thể giữ lại icon cũ → spell hiện keyword của lá khác.
                    if (cvw.keywordsContainer != null)
                    {
                        // Xóa toàn bộ icon con (kể cả icon còn sót từ lần test/save prefab)
                        for (int i = cvw.keywordsContainer.childCount - 1; i >= 0; i--)
                            Destroy(cvw.keywordsContainer.GetChild(i).gameObject);

                        var kwList = data.keywords;
                        bool hasKeywords = kwList != null && kwList.Count > 0
                                          && cvw.keywordIconPrefab != null;

                        cvw.keywordsContainer.gameObject.SetActive(hasKeywords);

                        if (hasKeywords)
                        {
                            // Build sprite map từ cvw.keywordIcons (giống RefreshKeywords)
                            var spriteMap = new System.Collections.Generic.Dictionary<KeywordType, Sprite>();
                            foreach (var entry in cvw.keywordIcons)
                                if (entry.icon != null) spriteMap[entry.keyword] = entry.icon;

                            foreach (var kw in kwList)
                            {
                                var iconGO = Instantiate(cvw.keywordIconPrefab, cvw.keywordsContainer);
                                iconGO.name = kw.ToString();
                                var img = iconGO.GetComponent<Image>();
                                if (img != null && spriteMap.TryGetValue(kw, out var sp))
                                    img.sprite = sp;
                            }
                        }
                    }

                    // Disable MonoBehaviour: ngăn OnPointerEnter/Exit đổi sortingOrder (gây mất artwork)
                    // và ngăn OnPointerClick mở inspect. Close button là child Button riêng, không bị ảnh hưởng.
                    cvw.enabled = false;
                }

                // Nút đóng X
                AddCloseButton(go.transform);

                go.transform.localScale = Vector3.zero; // DoScaleIn sẽ tween lên
                return go;
            }

            // ══ PATH B: fallback tạo thủ công ════════════════════════
            {
                var go = new GameObject("_RelatedCardPreview");
                go.transform.SetParent(parent, false);
                go.transform.SetAsLastSibling();

                var cv = go.AddComponent<Canvas>();
                cv.overrideSorting = true;
                cv.sortingOrder = inspectSortingBase + 201;
                go.AddComponent<GraphicRaycaster>();

                var rt = go.GetComponent<RectTransform>();
                rt.anchorMin = rt.anchorMax = rt.pivot = new Vector2(0.5f, 0.5f);
                rt.anchoredPosition = Vector2.zero;
                rt.sizeDelta = _portraitCardSize;

                // Background
                var bgImg = go.AddComponent<Image>();
                if (cardFrame != null && cardFrame.sprite != null)
                { bgImg.sprite = cardFrame.sprite; bgImg.type = Image.Type.Sliced; }
                bgImg.color = data.cardType == CardType.Unit
                    ? new Color(0.13f, 0.10f, 0.07f, 1f)
                    : new Color(0.07f, 0.08f, 0.18f, 1f);

                // Artwork
                if (data.artwork != null)
                {
                    var artGO = new GameObject("Art");
                    artGO.transform.SetParent(go.transform, false);
                    var artRT = artGO.AddComponent<RectTransform>();
                    if (artworkRawImage != null)
                    {
                        var refRT = artworkRawImage.GetComponent<RectTransform>();
                        artRT.anchorMin = refRT.anchorMin; artRT.anchorMax = refRT.anchorMax;
                        artRT.pivot = refRT.pivot; artRT.anchoredPosition = refRT.anchoredPosition;
                        artRT.sizeDelta = refRT.sizeDelta;
                    }
                    else
                    {
                        artRT.anchorMin = artRT.anchorMax = artRT.pivot = new Vector2(0.5f, 0.5f);
                        artRT.sizeDelta = _portraitArtworkSize;
                    }
                    var raw = artGO.AddComponent<RawImage>();
                    raw.texture = data.artwork; raw.raycastTarget = false;
                    ApplyFocalUV(raw, data, _portraitArtworkSize.x, _portraitArtworkSize.y);
                }

                // Overlay
                {
                    var ovGO = new GameObject("Overlay"); ovGO.transform.SetParent(go.transform, false);
                    var ovRT = ovGO.AddComponent<RectTransform>();
                    ovRT.anchorMin = Vector2.zero; ovRT.anchorMax = Vector2.one;
                    ovRT.offsetMin = ovRT.offsetMax = Vector2.zero;
                    var ovImg = ovGO.AddComponent<Image>();
                    ovImg.color = new Color(0f, 0f, 0f, 150f / 255f); ovImg.raycastTarget = false;
                }

                // Name
                if (nameGroup != null)
                {
                    var refRT = nameGroup.GetComponent<RectTransform>();
                    if (refRT != null)
                    {
                        var nGO = new GameObject("Name"); nGO.transform.SetParent(go.transform, false);
                        var nRT = nGO.AddComponent<RectTransform>();
                        nRT.anchorMin = refRT.anchorMin; nRT.anchorMax = refRT.anchorMax;
                        nRT.pivot = refRT.pivot; nRT.anchoredPosition = refRT.anchoredPosition;
                        nRT.sizeDelta = refRT.sizeDelta;
                        var refImg = nameGroup.GetComponent<Image>();
                        if (refImg != null) { var nImg = nGO.AddComponent<Image>(); nImg.color = refImg.color; nImg.raycastTarget = false; }
                        MakeTMPLabel(nGO.transform, data.cardName,
                            nameText != null ? nameText.fontSize : 12f, Color.white, TextAlignmentOptions.Center, bold: true);
                    }
                }

                // Stats
                if (data.cardType == CardType.Unit)
                {
                    float sFontSize = attackText != null ? attackText.fontSize : 14f;
                    var sGO = new GameObject("Stats"); sGO.transform.SetParent(go.transform, false);
                    var sRT = sGO.AddComponent<RectTransform>();
                    sRT.anchorMin = new Vector2(0f, 0f); sRT.anchorMax = new Vector2(1f, 0f);
                    sRT.pivot = new Vector2(0.5f, 0f);
                    sRT.anchoredPosition = new Vector2(0f, statsEdgeOffset);
                    sRT.sizeDelta = new Vector2(0f, sFontSize + 8f);
                    MakeTMPLabel(sGO.transform, data.baseAttack.ToString(), sFontSize,
                        new Color(1f, 0.8f, 0.1f, 1f), TextAlignmentOptions.Left, bold: true, offsetMin: new Vector2(4f, 0f));
                    MakeTMPLabel(sGO.transform, data.baseHealth.ToString(), sFontSize,
                        new Color(0.2f, 1f, 0.3f, 1f), TextAlignmentOptions.Right, bold: true, offsetMax: new Vector2(-4f, 0f));
                }

                // Keywords (text)
                if (data.keywords != null && data.keywords.Count > 0 && keywordsContainer != null)
                {
                    var kwRefRT = keywordsContainer.GetComponent<RectTransform>();
                    var sb = new StringBuilder();
                    foreach (var kw in data.keywords)
                        if (_kwDesc.TryGetValue(kw, out var inf)) sb.Append($"<b>{inf.name}</b>  ");
                    var kwGO = new GameObject("KW"); kwGO.transform.SetParent(go.transform, false);
                    var kwRT = kwGO.AddComponent<RectTransform>();
                    kwRT.anchorMin = kwRT.anchorMax = kwRT.pivot = new Vector2(0.5f, 0.5f);
                    kwRT.anchoredPosition = kwRefRT != null ? kwRefRT.anchoredPosition : Vector2.zero;
                    kwRT.sizeDelta = new Vector2(_portraitCardSize.x - 10f, 18f);
                    var kwTMP = MakeTMPLabel(kwGO.transform, sb.ToString().TrimEnd(), 8f,
                        new Color(1f, 0.85f, 0.3f, 1f), TextAlignmentOptions.Center);
                    kwTMP.overflowMode = TextOverflowModes.Ellipsis;
                }

                // Description
                if (!string.IsNullOrWhiteSpace(data.description) && skillsText != null)
                {
                    var refRT = skillsText.GetComponent<RectTransform>();
                    if (refRT != null)
                    {
                        var dGO = new GameObject("Desc"); dGO.transform.SetParent(go.transform, false);
                        var dRT = dGO.AddComponent<RectTransform>();
                        dRT.anchorMin = refRT.anchorMin; dRT.anchorMax = refRT.anchorMax;
                        dRT.pivot = refRT.pivot; dRT.anchoredPosition = refRT.anchoredPosition;
                        dRT.sizeDelta = refRT.sizeDelta;
                        var tmp = MakeTMPLabel(dGO.transform, data.description,
                            skillsText.fontSize, skillsText.color, skillsText.alignment);
                        tmp.textWrappingMode = TMPro.TextWrappingModes.Normal;
                        tmp.overflowMode = TextOverflowModes.Ellipsis;
                    }
                }

                AddCloseButton(go.transform);
                return go;
            }
        }

        void AddCloseButton(Transform parent)
        {
            var cGO = new GameObject("Close");
            cGO.transform.SetParent(parent, false);
            cGO.transform.SetAsLastSibling();
            var cRT = cGO.AddComponent<RectTransform>();
            cRT.anchorMin = new Vector2(1f, 1f); cRT.anchorMax = new Vector2(1f, 1f);
            cRT.pivot = new Vector2(1f, 1f); cRT.anchoredPosition = new Vector2(-3f, -3f);
            cRT.sizeDelta = new Vector2(20f, 20f);
            var cImg = cGO.AddComponent<Image>();
            cImg.color = new Color(0.2f, 0.06f, 0.06f, 0.92f);
            var cBtn = cGO.AddComponent<Button>();
            cBtn.targetGraphic = cImg;
            cBtn.onClick.AddListener(ReturnFromRelated);
            MakeTMPLabel(cGO.transform, "X", 10f, Color.white, TextAlignmentOptions.Center, bold: true);
        }

        // ── Back Label (trên lá bên trái khi navigate) ────────────
        void ShowBackLabel()
        {
            if (_backLabel != null) return;
            _backLabel = new GameObject("_BackLabel");
            _backLabel.transform.SetParent(transform, false);
            var rt = _backLabel.AddComponent<RectTransform>();
            rt.anchorMin = Vector2.zero; rt.anchorMax = Vector2.one;
            rt.offsetMin = rt.offsetMax = Vector2.zero;
            var overlay = _backLabel.AddComponent<Image>();
            overlay.color = new Color(0f, 0f, 0f, 0.46f);
            overlay.raycastTarget = false;
            MakeTMPLabel(_backLabel.transform, "CLICK\nQUAY LAI", 10f,
                new Color(1f, 0.88f, 0.4f, 0.95f), TextAlignmentOptions.Center, bold: true);
            _backLabel.transform.SetAsLastSibling();
        }

        void HideBackLabel()
        {
            if (_backLabel == null) return;
            Destroy(_backLabel); _backLabel = null;
        }

        // ── Slide / ScaleIn Coroutines ─────────────────────────────
        System.Collections.IEnumerator DoSlide(RectTransform rt, Vector2 toPos, float toScale, float duration)
        {
            Vector2 startPos = rt.anchoredPosition;
            float startScale = rt.transform.localScale.x;
            float elapsed = 0f;
            while (elapsed < duration)
            {
                elapsed += Time.deltaTime;
                float t = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(elapsed / duration));
                if (rt == null) yield break;
                rt.anchoredPosition = Vector2.Lerp(startPos, toPos, t);
                rt.transform.localScale = Vector3.one * Mathf.Lerp(startScale, toScale, t);
                yield return null;
            }
            if (rt != null) { rt.anchoredPosition = toPos; rt.transform.localScale = Vector3.one * toScale; }
            _slideCoroutine = null;
        }

        System.Collections.IEnumerator DoScaleIn(Transform target, float targetScale, float duration)
        {
            float elapsed = 0f;
            while (elapsed < duration)
            {
                elapsed += Time.deltaTime;
                float t = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(elapsed / duration));
                if (target == null) yield break;
                target.localScale = Vector3.one * Mathf.Lerp(0f, targetScale, t);
                yield return null;
            }
            if (target != null) target.localScale = Vector3.one * targetScale;
        }

        // ── TMP Label Helper ──────────────────────────────────────

        static TextMeshProUGUI MakeTMPLabel(Transform parent, string text, float fontSize,
            Color color, TextAlignmentOptions alignment,
            bool bold = false,
            Vector2? offsetMin = null, Vector2? offsetMax = null)
        {
            var go = new GameObject("_lbl");
            go.transform.SetParent(parent, false);
            var rt = go.AddComponent<RectTransform>();
            rt.anchorMin = Vector2.zero; rt.anchorMax = Vector2.one;
            rt.offsetMin = offsetMin ?? Vector2.zero;
            rt.offsetMax = offsetMax ?? Vector2.zero;
            var tmp = go.AddComponent<TextMeshProUGUI>();
            tmp.text = text;
            tmp.fontSize = fontSize;
            tmp.alignment = alignment;
            tmp.color = color;
            tmp.fontStyle = bold ? FontStyles.Bold : FontStyles.Normal;
            tmp.raycastTarget = false;
            return tmp;
        }
    }
}