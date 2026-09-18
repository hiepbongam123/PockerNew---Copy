using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.EventSystems;
using TMPro;
using LoRClone.Data;

namespace LoRClone.View
{
    /// <summary>
    /// Reusable card picker — hiện danh sách lá bài dưới dạng CardView prefab thật.
    ///
    /// Tạo Canvas ScreenSpaceOverlay riêng (sortingOrder 999) thay vì nest vào canvas game
    /// → đảm bảo luôn ở đúng giữa màn hình bất kể canvas game có scale/renderMode gì.
    /// Mặc định KHÔNG có panel nền (cards nổi thẳng trên dim). Bật showPanel = true để có
    /// khối nền obsidian bo góc + viền vàng (dùng cho màn thưởng — che nền bận phía sau).
    ///
    /// Dùng cho: Equipment mode, WhenPlayed, Predict, bất kỳ "chọn 1 lá" nào.
    ///
    /// Setup: Gán cardPrefab (có CardView) vào Inspector. Không cần setup thêm.
    /// </summary>
    public class CardPickerView : MonoBehaviour
    {
        // ── Inspector ─────────────────────────────────────────────

        [Header("Card Prefab (có CardView component)")]
        public GameObject cardPrefab;

        [Header("Card Size (screen pixels, không phụ thuộc canvas scale)")]
        public float cardDisplayWidth = 155f;
        public float cardDisplayHeight = 225f;

        [Header("Layout")]
        public float cardSpacing = 36f;
        public float cardOffsetY = 10f;   // cards dịch lên/xuống so với tâm màn hình (dương = lên)

        [Header("Colors")]
        public Color dimColor = new Color(0f, 0f, 0f, 0.60f);
        public Color cancelBgColor = new Color(0.30f, 0.10f, 0.10f, 1f);

        [Header("Panel nền (khi showPanel = true)")]
        public Color panelColor = new Color(0.04f, 0.055f, 0.10f, 0.99f);
        public Color panelBorderColor = new Color(0.82f, 0.66f, 0.32f, 0.95f);

        // ── Singleton ─────────────────────────────────────────────
        static CardPickerView _instance;
        public static CardPickerView Instance
        {
            get
            {
                if (_instance == null) _instance = FindFirstObjectByType<CardPickerView>();
                return _instance;
            }
        }

        // ── State ─────────────────────────────────────────────────
        Action<int> _pendingCallback;
        GameObject _rootGO;   // Canvas ScreenSpaceOverlay tạo mới
        readonly List<GameObject> _slots = new List<GameObject>();
        string[] _labels;   // labels dưới mỗi card (optional)
        bool _showPanel;    // có vẽ panel nền không
        string _extraLabel; // nút phụ (không đóng picker) — vd "XEM BUILD"
        Action _extraAction;

        // ─────────────────────────────────────────────────────────

        void Awake() => _instance = this;
        void OnDestroy() { if (_instance == this) _instance = null; }

        void Update()
        {
            if (_pendingCallback == null) return;
            if (Input.GetKeyDown(KeyCode.Escape) || Input.GetMouseButtonDown(1))
                Respond(-1);
        }

        // ── Public API ────────────────────────────────────────────

        /// <summary>
        /// Hiện picker. onChosen(index): ≥ 0 = chọn lá đó; -1 = cancel.
        /// showPanel = true → có khối nền obsidian bo góc + viền vàng (che nền bận, hợp màn thưởng).
        /// </summary>
        public void Show(List<CardData> choices, string title, Action<int> onChosen,
                         bool showCancel = true, bool showPanel = false,
                         string extraLabel = null, Action extraAction = null)
        {
            if (_pendingCallback != null)
            {
                Debug.LogWarning("[CardPickerView] Already showing.");
                return;
            }
            _pendingCallback = onChosen;
            _showPanel = showPanel;
            _extraLabel = extraLabel;
            _extraAction = extraAction;
            BuildUI(choices, title, showCancel);
        }

        /// <summary>
        /// Giống Show() nhưng kèm label mô tả dưới mỗi card.
        /// extraLabel/extraAction: nút phụ (vd "XEM BUILD") — bấm gọi extraAction, KHÔNG đóng picker.
        /// </summary>
        public void ShowWithLabels(List<CardData> choices, string title, string[] labels,
                                   Action<int> onChosen, bool showCancel = true, bool showPanel = false,
                                   string extraLabel = null, Action extraAction = null)
        {
            _labels = labels;
            Show(choices, title, idx => { _labels = null; onChosen(idx); },
                 showCancel, showPanel, extraLabel, extraAction);
        }

        public void ForceHide()
        {
            DestroyUI();
            _pendingCallback = null;
            _labels = null;
            _extraLabel = null;
            _extraAction = null;
        }

        // ── Core ──────────────────────────────────────────────────

        void Respond(int index)
        {
            var cb = _pendingCallback;
            _labels = null;
            _extraLabel = null;
            _extraAction = null;
            DestroyUI();
            _pendingCallback = null;
            cb?.Invoke(index);
        }

        void DestroyUI()
        {
            _slots.Clear(); // slots are children of _rootGO, destroyed with it
            if (_rootGO != null) { Destroy(_rootGO); _rootGO = null; }
        }

        // ── Build ─────────────────────────────────────────────────
        //
        // Hierarchy:
        //   _rootGO  [Canvas ScreenSpaceOverlay sortingOrder=999]
        //     ├── _dim      [Image fullscreen, semi-transparent black]
        //     ├── _Panel    [tùy chọn: khối nền bo góc]
        //     ├── Title     [TMP, centered above cards]
        //     ├── Slot_0    [transparent Button, anchor center]
        //     │     └── card visual [prefab, Canvas disabled → renders in root at 999]
        //     ├── Slot_1 …
        //     ├── Label_0   [TMP below Slot_0]
        //     ├── Label_1 …
        //     └── CancelBtn [Button]

        void BuildUI(List<CardData> choices, string title, bool showCancel)
        {
            // ── Fresh ScreenSpaceOverlay Canvas ───────────────────
            // Lý do tạo mới: game canvas có thể dùng renderMode Camera hoặc
            // ScaleWithScreenSize → tọa độ (0,0) không phải tâm màn hình.
            // Canvas ScreenSpaceOverlay mới đảm bảo (0,0) luôn là đúng tâm màn hình.
            _rootGO = new GameObject("_CardPickerRoot");
            var rootCV = _rootGO.AddComponent<Canvas>();
            rootCV.renderMode = RenderMode.ScreenSpaceOverlay;
            rootCV.sortingOrder = 999;
            _rootGO.AddComponent<GraphicRaycaster>();
            // RootGO RT: Unity tự set fullscreen cho ScreenSpaceOverlay root Canvas
            // → không cần set anchorMin/Max

            // ── Dim (fullscreen, semi-transparent) ────────────────
            var dimGO = new GameObject("_Dim");
            dimGO.transform.SetParent(_rootGO.transform, false);
            var dimRT = dimGO.AddComponent<RectTransform>();
            dimRT.anchorMin = Vector2.zero; dimRT.anchorMax = Vector2.one;
            dimRT.offsetMin = dimRT.offsetMax = Vector2.zero;
            var dimImg = dimGO.AddComponent<Image>();
            dimImg.color = dimColor;
            dimImg.raycastTarget = true; // chặn click vào game phía dưới

            // ── Layout constants ──────────────────────────────────
            float totalW = choices.Count * cardDisplayWidth + (choices.Count - 1) * cardSpacing;
            float startX = -(totalW * 0.5f) + cardDisplayWidth * 0.5f;

            const float titleH = 36f;
            const float labelH = 28f;
            const float labelGap = 10f;
            const float btnH = 38f;
            const float btnW = 130f;
            const float btnGap = 20f;

            float cardY = cardOffsetY;                           // center of cards
            float titleY = cardY + cardDisplayHeight * 0.5f + 14f + titleH * 0.5f;
            float labelY = cardY - cardDisplayHeight * 0.5f - labelGap - labelH * 0.5f;
            float cancelY = labelY - labelH * 0.5f - btnGap - btnH * 0.5f;

            // ── Panel nền (tùy chọn) ──────────────────────────────
            // Khối obsidian bo góc + viền vàng, bao trọn tiêu đề → cards → nút Hủy.
            // Thêm SAU dim, TRƯỚC title/cards → nằm dưới nội dung, trên dim.
            if (_showPanel)
            {
                var rounded = RoundedSprite();
                Color obsid = new Color(panelColor.r, panelColor.g, panelColor.b, 1f); // đặc hoàn toàn

                // 1) Nền obsidian PHỦ TOÀN màn hình → che sạch nền game phía sau.
                MakeStretch("_PanelFull", Vector2.zero, Vector2.zero, obsid, null, true);

                // 2) Viền vàng bo góc, thụt vào từ mép màn hình.
                const float inset = 16f;
                MakeStretch("_PanelBorder",
                    new Vector2(inset, inset), new Vector2(-inset, -inset),
                    panelBorderColor, rounded, false);
                // 3) Nền trong (thụt thêm 2px) để viền chỉ là khung mỏng.
                MakeStretch("_PanelInner",
                    new Vector2(inset + 2f, inset + 2f), new Vector2(-(inset + 2f), -(inset + 2f)),
                    obsid, rounded, false);

                // 4) Dải accent vàng ngay dưới tiêu đề.
                var accGO = new GameObject("_TitleAccent");
                accGO.transform.SetParent(_rootGO.transform, false);
                var aRT = accGO.AddComponent<RectTransform>();
                aRT.anchorMin = aRT.anchorMax = aRT.pivot = new Vector2(0.5f, 0.5f);
                aRT.anchoredPosition = new Vector2(0f, titleY - titleH * 0.5f - 2f);
                aRT.sizeDelta = new Vector2(Mathf.Max(totalW, 260f), 2f);
                var aImg = accGO.AddComponent<Image>();
                aImg.color = new Color(panelBorderColor.r, panelBorderColor.g, panelBorderColor.b, 0.5f);
                aImg.raycastTarget = false;
            }

            // ── Title ─────────────────────────────────────────────
            {
                var go = new GameObject("_Title");
                go.transform.SetParent(_rootGO.transform, false);
                var rt = go.AddComponent<RectTransform>();
                rt.anchorMin = rt.anchorMax = rt.pivot = new Vector2(0.5f, 0.5f);
                rt.anchoredPosition = new Vector2(0f, titleY);
                rt.sizeDelta = new Vector2(totalW + 40f, titleH);
                var tmp = go.AddComponent<TextMeshProUGUI>();
                tmp.text = title;
                tmp.fontSize = 18f; tmp.fontStyle = FontStyles.Bold;
                tmp.alignment = TextAlignmentOptions.Center;
                tmp.color = Color.white;
                tmp.raycastTarget = false;
                tmp.textWrappingMode = TextWrappingModes.NoWrap;
                tmp.overflowMode = TextOverflowModes.Ellipsis;
                // Shadow để đọc được trên mọi nền
                var sh = go.AddComponent<Shadow>();
                sh.effectColor = new Color(0f, 0f, 0f, 0.9f);
                sh.effectDistance = new Vector2(1f, -1f);
            }

            // ── Card Slots ────────────────────────────────────────
            _slots.Clear();
            for (int i = 0; i < choices.Count; i++)
            {
                int idx = i;
                float x = startX + i * (cardDisplayWidth + cardSpacing);
                var slot = BuildSlot(choices[i], new Vector2(x, cardY), () => Respond(idx));
                _slots.Add(slot);
            }

            // ── Labels dưới mỗi card ─────────────────────────────
            if (_labels != null)
            {
                for (int i = 0; i < choices.Count && i < _labels.Length; i++)
                {
                    if (string.IsNullOrEmpty(_labels[i])) continue;
                    float x = startX + i * (cardDisplayWidth + cardSpacing);
                    var go = new GameObject("_Label" + i);
                    go.transform.SetParent(_rootGO.transform, false);
                    var rt = go.AddComponent<RectTransform>();
                    rt.anchorMin = rt.anchorMax = rt.pivot = new Vector2(0.5f, 0.5f);
                    rt.anchoredPosition = new Vector2(x, labelY);
                    rt.sizeDelta = new Vector2(cardDisplayWidth + 16f, labelH);
                    var tmp = go.AddComponent<TextMeshProUGUI>();
                    tmp.text = _labels[i];
                    tmp.fontSize = 12f; tmp.fontStyle = FontStyles.Bold;
                    tmp.alignment = TextAlignmentOptions.Center;
                    tmp.color = new Color(1f, 0.88f, 0.45f);
                    tmp.raycastTarget = false;
                    var sh = go.AddComponent<Shadow>();
                    sh.effectColor = new Color(0f, 0f, 0f, 0.9f);
                    sh.effectDistance = new Vector2(1f, -1f);
                }
            }

            // ── Cancel Button ─────────────────────────────────────
            if (showCancel)
            {
                var go = new GameObject("_CancelBtn");
                go.transform.SetParent(_rootGO.transform, false);
                var rt = go.AddComponent<RectTransform>();
                rt.anchorMin = rt.anchorMax = rt.pivot = new Vector2(0.5f, 0.5f);
                rt.anchoredPosition = new Vector2(0f, cancelY);
                rt.sizeDelta = new Vector2(btnW, btnH);
                var img = go.AddComponent<Image>();
                img.color = cancelBgColor;
                var btn = go.AddComponent<Button>();
                btn.targetGraphic = img;
                var bc = btn.colors;
                bc.highlightedColor = new Color(0.52f, 0.20f, 0.20f);
                bc.pressedColor = new Color(0.16f, 0.06f, 0.06f);
                btn.colors = bc;
                btn.onClick.AddListener(() => Respond(-1));
                MakeTMP(go.transform, "Hủy", 15f, Color.white, TextAlignmentOptions.Center);
            }

            // ── Nút phụ (không đóng picker) — vd "XEM BUILD" ──────
            if (!string.IsNullOrEmpty(_extraLabel) && _extraAction != null)
            {
                float extraY = cancelY + btnH + 12f; // ngay trên nút Hủy
                var go = new GameObject("_ExtraBtn");
                go.transform.SetParent(_rootGO.transform, false);
                var rt = go.AddComponent<RectTransform>();
                rt.anchorMin = rt.anchorMax = rt.pivot = new Vector2(0.5f, 0.5f);
                rt.anchoredPosition = new Vector2(0f, extraY);
                rt.sizeDelta = new Vector2(190f, btnH);
                var img = go.AddComponent<Image>();
                var rounded = RoundedSprite();
                if (rounded != null) { img.sprite = rounded; img.type = Image.Type.Sliced; }
                img.color = new Color(0.22f, 0.34f, 0.50f, 1f);
                var btn = go.AddComponent<Button>();
                btn.targetGraphic = img;
                var bc = btn.colors;
                bc.highlightedColor = new Color(0.34f, 0.48f, 0.66f);
                bc.pressedColor = new Color(0.16f, 0.24f, 0.36f);
                btn.colors = bc;
                var cap = _extraAction;
                btn.onClick.AddListener(() => cap?.Invoke());
                MakeTMP(go.transform, _extraLabel, 14f, Color.white, TextAlignmentOptions.Center, bold: true);
            }
        }

        // Tạo 1 khối nền STRETCH toàn màn hình theo offset mép (offMin từ góc dưới-trái, offMax từ góc trên-phải).
        void MakeStretch(string name, Vector2 offMin, Vector2 offMax, Color color, Sprite rounded, bool block)
        {
            var go = new GameObject(name);
            go.transform.SetParent(_rootGO.transform, false);
            var rt = go.AddComponent<RectTransform>();
            rt.anchorMin = Vector2.zero; rt.anchorMax = Vector2.one;
            rt.offsetMin = offMin; rt.offsetMax = offMax;
            var img = go.AddComponent<Image>();
            if (rounded != null) { img.sprite = rounded; img.type = Image.Type.Sliced; }
            img.color = color;
            img.raycastTarget = block; // full nền chặn click; viền/nền trong không cần
        }

        static Sprite _roundedPanelSprite;
        static Sprite RoundedSprite()
        {
            if (_roundedPanelSprite == null) _roundedPanelSprite = UISprites.RoundedRect();
            return _roundedPanelSprite;
        }

        // ── Slot ─────────────────────────────────────────────────

        GameObject BuildSlot(CardData data, Vector2 anchoredPos, Action onClick)
        {
            // Slot = transparent click-area (Button) + card visual child
            var slot = new GameObject("Slot_" + data.cardName);
            slot.transform.SetParent(_rootGO.transform, false);
            var slotRT = slot.AddComponent<RectTransform>();
            slotRT.anchorMin = slotRT.anchorMax = slotRT.pivot = new Vector2(0.5f, 0.5f);
            slotRT.anchoredPosition = anchoredPos;
            slotRT.sizeDelta = new Vector2(cardDisplayWidth, cardDisplayHeight);

            var slotImg = slot.AddComponent<Image>();
            slotImg.color = Color.clear;
            slotImg.raycastTarget = true;

            var btn = slot.AddComponent<Button>();
            btn.targetGraphic = slotImg;
            // Disable default tween — hover handled via EventTrigger scale
            var bc = btn.colors;
            bc.normalColor = bc.highlightedColor = bc.pressedColor = bc.selectedColor = Color.white;
            btn.colors = bc;
            btn.onClick.AddListener(() => onClick?.Invoke());

            // Card visual
            var visual = BuildVisual(data, slot.transform);

            // Hover: scale slot 1.08×; bật selectedHighlight nếu có
            var et = slot.AddComponent<EventTrigger>();
            AddTrigger(et, EventTriggerType.PointerEnter, _ =>
            {
                slot.transform.localScale = Vector3.one * 1.08f;
                visual?.GetComponent<CardView>()?.SetSelected(true);
            });
            AddTrigger(et, EventTriggerType.PointerExit, _ =>
            {
                slot.transform.localScale = Vector3.one;
                visual?.GetComponent<CardView>()?.SetSelected(false);
            });

            return slot;
        }

        // ── Card Visual ───────────────────────────────────────────

        GameObject BuildVisual(CardData data, Transform parent)
        {
            float W = cardDisplayWidth, H = cardDisplayHeight;

            // ══ PATH A: dùng cardPrefab ═══════════════════════════
            if (cardPrefab != null)
            {
                var go = Instantiate(cardPrefab, parent, false);
                go.name = "Visual_" + data.cardName;

                var rt = go.GetComponent<RectTransform>();
                rt.anchorMin = rt.anchorMax = rt.pivot = new Vector2(0.5f, 0.5f);
                rt.anchoredPosition = Vector2.zero;
                rt.sizeDelta = new Vector2(W, H);

                // CardView.Awake() tự thêm Canvas + GraphicRaycaster.
                // KHÔNG disable Canvas — disabled Canvas ẩn toàn bộ children!
                // Giữ Canvas bật ở sortingOrder 1000 → render trên dim (rootGO=999).
                // Chỉ disable GraphicRaycaster → slot Button nhận click thay vì card.
                var selfCV = go.GetComponent<Canvas>();
                if (selfCV != null)
                {
                    selfCV.overrideSorting = true;
                    selfCV.sortingOrder = 1000;
                }
                var rc = go.GetComponent<GraphicRaycaster>();
                if (rc != null) rc.enabled = false;

                var cvw = go.GetComponent<CardView>();
                if (cvw != null)
                {
                    // Text
                    if (cvw.nameText) cvw.nameText.text = data.cardName;
                    if (cvw.manaCostText) cvw.manaCostText.text = data.manaCost.ToString();
                    if (cvw.attackText) cvw.attackText.text = data.baseAttack.ToString();
                    if (cvw.healthText) cvw.healthText.text = data.baseHealth.ToString();

                    // Artwork
                    if (cvw.artworkRawImage != null && data.artwork != null)
                    {
                        var aRT = cvw.artworkRawImage.GetComponent<RectTransform>();
                        aRT.anchorMin = aRT.anchorMax = aRT.pivot = new Vector2(0.5f, 0.5f);
                        aRT.anchoredPosition = Vector2.zero;
                        aRT.sizeDelta = new Vector2(W - 6f, H - 6f);
                        cvw.artworkRawImage.texture = data.artwork;
                        ApplyFocalUV(cvw.artworkRawImage, data, W, H);
                    }

                    // Skills text
                    if (cvw.skillsText != null)
                    {
                        var sb = new System.Text.StringBuilder();
                        if (data.cardType == CardType.Spell)
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

                    // Visibility (inspect-mode: show all elements)
                    if (cvw.nameGroup) cvw.nameGroup.SetActive(true);
                    else if (cvw.nameText) cvw.nameText.gameObject.SetActive(true);
                    if (cvw.manaGroup) cvw.manaGroup.SetActive(true);
                    if (cvw.statsGroup) cvw.statsGroup.SetActive(data.cardType == CardType.Unit);
                    if (cvw.inspectOverlay != null) cvw.inspectOverlay.SetActive(false);
                    if (cvw.cardFrame != null) cvw.cardFrame.enabled = true;

                    // Keywords
                    if (cvw.keywordsContainer != null)
                    {
                        for (int k = cvw.keywordsContainer.childCount - 1; k >= 0; k--)
                            Destroy(cvw.keywordsContainer.GetChild(k).gameObject);
                        if (data.keywords != null && cvw.keywordIconPrefab != null)
                        {
                            var smap = new Dictionary<KeywordType, Sprite>();
                            foreach (var e in cvw.keywordIcons)
                                if (e.icon != null) smap[e.keyword] = e.icon;
                            foreach (var kw in data.keywords)
                            {
                                var iGO = Instantiate(cvw.keywordIconPrefab, cvw.keywordsContainer);
                                iGO.name = kw.ToString();
                                var img = iGO.GetComponent<Image>();
                                if (img != null && smap.TryGetValue(kw, out var sp)) img.sprite = sp;
                            }
                        }
                    }

                    // Disable CardView interaction (inspect, drag, hover-scale)
                    cvw.AllowInteract = false;
                    cvw.enabled = false;
                }
                return go;
            }

            // ══ PATH B: fallback code-built card ══════════════════
            var fb = new GameObject("Visual_" + data.cardName);
            fb.transform.SetParent(parent, false);
            var fbRT = fb.AddComponent<RectTransform>();
            fbRT.anchorMin = fbRT.anchorMax = fbRT.pivot = new Vector2(0.5f, 0.5f);
            fbRT.anchoredPosition = Vector2.zero;
            fbRT.sizeDelta = new Vector2(W, H);

            // Background
            var bgImg = fb.AddComponent<Image>();
            bgImg.color = data.cardType == CardType.Unit
                ? new Color(0.13f, 0.10f, 0.07f) : new Color(0.07f, 0.08f, 0.18f);
            bgImg.raycastTarget = false;

            // Artwork
            if (data.artwork != null)
            {
                var aGO = new GameObject("Art"); aGO.transform.SetParent(fb.transform, false);
                var aRT = aGO.AddComponent<RectTransform>();
                aRT.anchorMin = Vector2.zero; aRT.anchorMax = Vector2.one;
                aRT.offsetMin = aRT.offsetMax = Vector2.zero;
                var raw = aGO.AddComponent<RawImage>();
                raw.texture = data.artwork; raw.raycastTarget = false;
                ApplyFocalUV(raw, data, W, H);
            }

            // Dim
            {
                var dGO = new GameObject("Dim"); dGO.transform.SetParent(fb.transform, false);
                var dRT = dGO.AddComponent<RectTransform>();
                dRT.anchorMin = Vector2.zero; dRT.anchorMax = Vector2.one;
                dRT.offsetMin = dRT.offsetMax = Vector2.zero;
                dGO.AddComponent<Image>().color = new Color(0, 0, 0, 0.38f);
                dGO.GetComponent<Image>().raycastTarget = false;
            }

            // Name band (bottom)
            {
                float bH = H * 0.19f;
                var nGO = new GameObject("Name"); nGO.transform.SetParent(fb.transform, false);
                var nRT = nGO.AddComponent<RectTransform>();
                nRT.anchorMin = new Vector2(0, 0); nRT.anchorMax = new Vector2(1, 0);
                nRT.pivot = new Vector2(0.5f, 0); nRT.anchoredPosition = Vector2.zero;
                nRT.sizeDelta = new Vector2(0, bH);
                nGO.AddComponent<Image>().color = new Color(0, 0, 0, 0.82f);
                nGO.GetComponent<Image>().raycastTarget = false;
                MakeTMP(nGO.transform, data.cardName,
                    Mathf.Max(7f, bH * 0.44f), Color.white, TextAlignmentOptions.Center, bold: true)
                    .overflowMode = TextOverflowModes.Ellipsis;
            }

            // Mana badge (top-left)
            {
                float bS = W * 0.22f;
                var mGO = new GameObject("Mana"); mGO.transform.SetParent(fb.transform, false);
                var mRT = mGO.AddComponent<RectTransform>();
                mRT.anchorMin = new Vector2(0, 1); mRT.anchorMax = new Vector2(0, 1);
                mRT.pivot = new Vector2(0, 1); mRT.anchoredPosition = new Vector2(4, -4);
                mRT.sizeDelta = new Vector2(bS, bS);
                mGO.AddComponent<Image>().color = new Color(0.08f, 0.18f, 0.58f, 0.95f);
                mGO.GetComponent<Image>().raycastTarget = false;
                MakeTMP(mGO.transform, data.manaCost.ToString(),
                    Mathf.Max(6f, bS * 0.55f), Color.white, TextAlignmentOptions.Center, bold: true);
            }

            // Stats strip (unit only, top)
            if (data.cardType == CardType.Unit)
            {
                float sH = H * 0.14f;
                var sGO = new GameObject("Stats"); sGO.transform.SetParent(fb.transform, false);
                var sRT = sGO.AddComponent<RectTransform>();
                sRT.anchorMin = new Vector2(0, 1); sRT.anchorMax = new Vector2(1, 1);
                sRT.pivot = new Vector2(0.5f, 1); sRT.anchoredPosition = Vector2.zero;
                sRT.sizeDelta = new Vector2(0, sH);
                sGO.AddComponent<Image>().color = new Color(0, 0, 0, 0.70f);
                sGO.GetComponent<Image>().raycastTarget = false;
                float fSz = Mathf.Max(8f, sH * 0.55f);
                MakeTMP(sGO.transform, data.baseAttack.ToString(), fSz,
                    new Color(1f, 0.8f, 0.1f), TextAlignmentOptions.Left, bold: true,
                    offMin: new Vector2(5, 0));
                MakeTMP(sGO.transform, data.baseHealth.ToString(), fSz,
                    new Color(0.25f, 1f, 0.35f), TextAlignmentOptions.Right, bold: true,
                    offMax: new Vector2(-5, 0));
            }

            return fb;
        }

        // ── Static Helpers ────────────────────────────────────────

        static TextMeshProUGUI MakeTMP(Transform parent, string text, float fs,
            Color color, TextAlignmentOptions align, bool bold = false,
            Vector2? offMin = null, Vector2? offMax = null)
        {
            var go = new GameObject("_t");
            go.transform.SetParent(parent, false);
            var rt = go.AddComponent<RectTransform>();
            rt.anchorMin = Vector2.zero; rt.anchorMax = Vector2.one;
            rt.offsetMin = offMin ?? Vector2.zero;
            rt.offsetMax = offMax ?? Vector2.zero;
            var tmp = go.AddComponent<TextMeshProUGUI>();
            tmp.text = text; tmp.fontSize = fs; tmp.alignment = align; tmp.color = color;
            tmp.fontStyle = bold ? FontStyles.Bold : FontStyles.Normal;
            tmp.raycastTarget = false;
            return tmp;
        }

        static void ApplyFocalUV(RawImage raw, CardData data, float W, float H)
        {
            if (raw == null || raw.texture == null || data == null) return;
            var tex = raw.texture;
            if (tex.width <= 0 || tex.height <= 0) { raw.uvRect = new Rect(0, 0, 1, 1); return; }
            float tA = (float)tex.width / tex.height;
            float dA = Mathf.Max(W, 1f) / Mathf.Max(H, 1f);
            float z = Mathf.Max(0.1f, data.artworkZoom);
            float bW, bH;
            if (tA > dA) { bH = 1f; bW = dA / tA; } else { bW = 1f; bH = tA / dA; }
            float uvW = Mathf.Clamp(bW / z, 0.001f, 1f);
            float uvH = Mathf.Clamp(bH / z, 0.001f, 1f);
            raw.uvRect = new Rect(
                Mathf.Clamp(data.artworkFocalX - uvW * 0.5f, 0f, 1f - uvW),
                Mathf.Clamp(data.artworkFocalY - uvH * 0.5f, 0f, 1f - uvH),
                uvW, uvH);
        }

        static void AddTrigger(EventTrigger et, EventTriggerType type,
            UnityEngine.Events.UnityAction<BaseEventData> cb)
        {
            var e = new EventTrigger.Entry { eventID = type };
            e.callback.AddListener(cb);
            et.triggers.Add(e);
        }
    }
}