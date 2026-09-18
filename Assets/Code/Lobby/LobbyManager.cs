using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.SceneManagement;
using TMPro;
using LoRClone.Data;

namespace LoRClone.View
{
    /// <summary>
    /// Lobby screen controller.
    ///
    /// SETUP: Bấm nút ">> Setup Lobby UI <<" trong Inspector (cần file LobbyManagerEditor.cs).
    /// Sau đó đổi màu/size trong Inspector → thấy ngay trong Scene (nhờ ExecuteAlways).
    /// </summary>
    [ExecuteAlways]
    public class LobbyManager : MonoBehaviour
    {
        // ── Data ──────────────────────────────────────────────────
        [Header("Data")]
        public DeckData[] availableDecks;
        public string gameSceneName = "SampleScene";

        [Tooltip("Danh mục card cho Xưởng Deck + resolve deck tự tạo (JSON).\n" +
                 "Để trống = tự gom card từ availableDecks.")]
        public CardLibrary cardLibrary;

        // ── Style — đổi ở đây, thấy ngay trong Scene ─────────────
        [Header("Style / Màu nền")]
        [SerializeField] Color bgColor = new Color(0.08f, 0.10f, 0.14f);
        [SerializeField] Color panelColor = new Color(0.18f, 0.22f, 0.30f);
        [SerializeField] Color panelHeaderColor = new Color(0.12f, 0.15f, 0.21f);
        [SerializeField] Color accentColor = new Color(0.96f, 0.84f, 0.36f);
        [SerializeField] Color dimColor = new Color(0.78f, 0.80f, 0.85f);

        [Header("Style / Nút")]
        [SerializeField] Color btnBlueColor = new Color(0.22f, 0.46f, 0.88f);
        [SerializeField] Color btnGreenColor = new Color(0.13f, 0.62f, 0.28f);

        [Header("Style / Picker")]
        [SerializeField] Color pickerBgColor = new Color(0.16f, 0.20f, 0.28f);
        [SerializeField] Color deckListBgColor = new Color(0.10f, 0.13f, 0.18f);
        [SerializeField] Color deckListHdrColor = new Color(0.22f, 0.28f, 0.40f);
        [SerializeField] Color previewBgColor = new Color(0.10f, 0.13f, 0.18f);

        [Header("Style / Card rows")]
        [SerializeField] Color rowNormal = new Color(0.22f, 0.28f, 0.40f);
        [SerializeField] Color rowHover = new Color(0.30f, 0.38f, 0.55f);
        [SerializeField] Color unitRowColor = new Color(0.28f, 0.20f, 0.10f);
        [SerializeField] Color spellRowColor = new Color(0.12f, 0.17f, 0.32f);

        // ── UI refs — tự assign khi Setup, KHÔNG chỉnh tay ──────
        [Header("UI Refs (auto)")]
        [SerializeField] TextMeshProUGUI playerDeckName;
        [SerializeField] TextMeshProUGUI playerCardCount;
        [SerializeField] Button playerChooseBtn;
        [SerializeField] TextMeshProUGUI enemyDeckName;
        [SerializeField] TextMeshProUGUI enemyCardCount;
        [SerializeField] Button enemyChooseBtn;
        [SerializeField] Button startBtn;
        [SerializeField] TextMeshProUGUI startBtnLabel;
        [SerializeField] GameObject pickerOverlay;
        [SerializeField] TextMeshProUGUI previewTitle;
        [SerializeField] Button closePickerBtn;

        // ── Deck List Scroll ──────────────────────────────────────
        [Header("Deck List Scroll")]
        [Tooltip("ScrollRect chứa danh sách deck — kéo resize cái này")]
        [SerializeField] RectTransform deckListScrollRT;
        [Tooltip("Viewport bên trong scroll (có Mask)")]
        [SerializeField] RectTransform deckListViewport;
        [Tooltip("Content bên trong viewport (có VLG + CSF) — script dùng để spawn rows")]
        [SerializeField] RectTransform deckListContent;
        [Tooltip("Template row — chỉnh màu/size thoải mái, script sẽ clone lúc runtime")]
        [SerializeField] GameObject deckRowTemplate;

        // ── Preview Scroll (Card List) ────────────────────────────
        [Header("Card List Scroll (Preview)")]
        [Tooltip("ScrollRect chứa danh sách card — kéo resize cái này")]
        [SerializeField] RectTransform cardListScrollRT;
        [Tooltip("Viewport bên trong scroll (có Mask)")]
        [SerializeField] RectTransform cardListViewport;
        [Tooltip("Content bên trong viewport (có VLG + CSF) — script dùng để spawn rows")]
        [SerializeField] RectTransform previewContent;
        [Tooltip("Template row card — chỉnh thoải mái")]
        [SerializeField] GameObject cardRowTemplate;

        // Style targets — lưu để OnValidate update ngay
        [HideInInspector][SerializeField] Image _bgImg;
        [HideInInspector][SerializeField] Image _playerPanelImg;
        [HideInInspector][SerializeField] Image _playerPanelHdrImg;
        [HideInInspector][SerializeField] Image _enemyPanelImg;
        [HideInInspector][SerializeField] Image _enemyPanelHdrImg;
        [HideInInspector][SerializeField] Image _startBtnImg;
        [HideInInspector][SerializeField] Image _headerImg;
        [HideInInspector][SerializeField] TextMeshProUGUI _headerTitle;
        [HideInInspector][SerializeField] Image _pickerPanelImg;
        [HideInInspector][SerializeField] Image _deckListBgImg;
        [HideInInspector][SerializeField] Image _deckListHdrImg;
        [HideInInspector][SerializeField] Image _previewBgImg;
        [HideInInspector][SerializeField] Image _playerChooseBtnImg;
        [HideInInspector][SerializeField] Image _enemyChooseBtnImg;

        // ── State ─────────────────────────────────────────────────
        DeckData _playerDeck, _enemyDeck;
        bool _pickingForPlayer;

        // ══════════════════════════════════════════════════════════
        // LIVE PREVIEW — chạy cả trong Edit mode
        // ══════════════════════════════════════════════════════════
        void OnValidate()
        {
            ApplyStyle();
        }

        void ApplyStyle()
        {
            Apply(_bgImg, bgColor);
            Apply(_headerImg, panelHeaderColor);
            Apply(_playerPanelImg, panelColor);
            Apply(_playerPanelHdrImg, panelHeaderColor);
            Apply(_enemyPanelImg, panelColor);
            Apply(_enemyPanelHdrImg, panelHeaderColor);
            Apply(_startBtnImg, btnGreenColor);
            Apply(_pickerPanelImg, pickerBgColor);
            Apply(_deckListBgImg, deckListBgColor);
            Apply(_deckListHdrImg, deckListHdrColor);
            Apply(_previewBgImg, previewBgColor);
            Apply(_playerChooseBtnImg, btnBlueColor);
            Apply(_enemyChooseBtnImg, btnBlueColor);

            // Accent text
            if (_headerTitle) _headerTitle.color = accentColor;
        }

        static void Apply(Image img, Color c) { if (img) img.color = c; }

        // ══════════════════════════════════════════════════════════
        // RUNTIME
        // ══════════════════════════════════════════════════════════
        void Start()
        {
            if (!Application.isPlaying) return;
            if (playerChooseBtn) playerChooseBtn.onClick.AddListener(() => OpenPicker(true));
            if (enemyChooseBtn) enemyChooseBtn.onClick.AddListener(() => OpenPicker(false));
            if (startBtn) startBtn.onClick.AddListener(OnStartClicked);
            if (closePickerBtn) closePickerBtn.onClick.AddListener(ClosePicker);
            if (pickerOverlay) pickerOverlay.SetActive(false);
            RefreshStartButton();
        }

        void OpenPicker(bool forPlayer)
        {
            _pickingForPlayer = forPlayer;
            if (pickerOverlay) pickerOverlay.SetActive(true);
            PopulateDeckList();
        }

        void ClosePicker() { if (pickerOverlay) pickerOverlay.SetActive(false); }

        void PopulateDeckList()
        {
            // Xóa clone cũ, giữ lại template (inactive)
            foreach (Transform c in deckListContent)
                if (c.gameObject != deckRowTemplate) Destroy(c.gameObject);
            foreach (Transform c in previewContent)
                if (c.gameObject != cardRowTemplate) Destroy(c.gameObject);
            if (previewTitle) previewTitle.text = "Chon deck de xem noi dung";

            // Deck tự tạo từ Xưởng Deck (JSON, persistentDataPath) — tên có hậu tố *
            var customDecks = CustomDeckStore.LoadAllAsDecks(cardLibrary, availableDecks);

            bool hasPreset = availableDecks != null && availableDecks.Length > 0;
            if (!hasPreset && customDecks.Count == 0)
            {
                SpawnPlaceholder(deckListContent,
                    "Khong co deck.\nGan DeckData vao Available Decks\nhoac tao deck trong Xuong Deck.");
                return;
            }

            if (hasPreset)
                foreach (var deck in availableDecks)
                {
                    if (deck == null) continue;
                    SpawnDeckRow(deck);
                }
            foreach (var deck in customDecks)
                SpawnDeckRow(deck);
            LayoutRebuilder.ForceRebuildLayoutImmediate(deckListContent);
        }

        void SpawnDeckRow(DeckData deck)
        {
            GameObject row;
            if (deckRowTemplate != null)
            {
                row = Instantiate(deckRowTemplate, deckListContent);
                row.SetActive(true);
                row.name = "Row_" + deck.deckName;

                var nameLabel = row.transform.Find("DeckName")?.GetComponent<TextMeshProUGUI>();
                var countLabel = row.transform.Find("CardCount")?.GetComponent<TextMeshProUGUI>();
                var selBtn = row.transform.Find("SelectBtn")?.GetComponent<Button>();
                var previewBtn = row.GetComponent<Button>();

                if (nameLabel) nameLabel.text = deck.deckName;
                if (countLabel) countLabel.text = deck.cards != null ? $"{deck.cards.Count} la" : "";
                if (selBtn) selBtn.onClick.AddListener(() => SelectDeck(deck));
                if (previewBtn) previewBtn.onClick.AddListener(() => ShowPreview(deck));
            }
            else
            {
                // Fallback: tạo procedural nếu chưa có template
                var rowRT = MakeRect("Row_" + deck.deckName, deckListContent, Vector2.zero, Vector2.one);
                rowRT.gameObject.AddComponent<LayoutElement>().preferredHeight = 64f;
                var img = rowRT.gameObject.AddComponent<Image>(); img.color = rowNormal;
                var btn = rowRT.gameObject.AddComponent<Button>(); btn.targetGraphic = img;
                ApplyBtnColors(btn, rowNormal, rowHover);
                btn.onClick.AddListener(() => ShowPreview(deck));
                SpawnLabel(rowRT, deck.deckName, 15f, Color.white,
                    TextAlignmentOptions.MidlineLeft, true, V2(0.02f, 0.5f), V2(0.68f, 1f));
                string cnt = deck.cards != null ? $"{deck.cards.Count} la" : "";
                SpawnLabel(rowRT, cnt, 12f, dimColor,
                    TextAlignmentOptions.MidlineLeft, false, V2(0.02f, 0f), V2(0.68f, 0.5f));
                var selRT = MakeRect("Sel", rowRT, V2(0.70f, 0.10f), V2(0.98f, 0.90f));
                var sImg = selRT.gameObject.AddComponent<Image>(); sImg.color = btnBlueColor;
                var sBtn = selRT.gameObject.AddComponent<Button>(); sBtn.targetGraphic = sImg;
                ApplyBtnColors(sBtn, btnBlueColor, new Color(0.30f, 0.55f, 1f));
                SpawnLabel(selRT, "Chon", 14f, Color.white, TextAlignmentOptions.Center, true);
                sBtn.onClick.AddListener(() => SelectDeck(deck));
                row = rowRT.gameObject;
            }
        }

        void ShowPreview(DeckData deck)
        {
            foreach (Transform c in previewContent)
                if (c.gameObject != cardRowTemplate) Destroy(c.gameObject);
            if (previewTitle) previewTitle.text = deck.deckName;

            if (deck.cards == null || deck.cards.Count == 0)
            { SpawnPlaceholder(previewContent, "Deck trong."); return; }

            var groups = new Dictionary<string, (CardData cd, int cnt)>();
            foreach (var card in deck.cards)
            {
                if (card == null) continue;
                groups.TryGetValue(card.cardName, out var g);
                groups[card.cardName] = (card, g.cnt + 1);
            }
            foreach (var kv in groups)
            {
                var cd = kv.Value.cd; var n = kv.Value.cnt;

                if (cardRowTemplate != null)
                {
                    var row = Instantiate(cardRowTemplate, previewContent);
                    row.SetActive(true);
                    row.name = "R_" + cd.cardName;

                    var nameLabel = row.transform.Find("CardName")?.GetComponent<TextMeshProUGUI>();
                    var manaLabel = row.transform.Find("ManaCost")?.GetComponent<TextMeshProUGUI>();
                    var cntLabel = row.transform.Find("Count")?.GetComponent<TextMeshProUGUI>();

                    if (nameLabel) nameLabel.text = cd.cardName;
                    if (manaLabel) manaLabel.text = cd.manaCost.ToString();
                    if (cntLabel) { cntLabel.text = $"x{n}"; cntLabel.gameObject.SetActive(n > 1); }
                }
                else
                {
                    // Fallback procedural
                    var rowRT = MakeRect("R_" + cd.cardName, previewContent, Vector2.zero, Vector2.one);
                    rowRT.gameObject.AddComponent<LayoutElement>().preferredHeight = 36f;
                    rowRT.gameObject.AddComponent<Image>().color =
                        cd.cardType == CardType.Unit ? unitRowColor : spellRowColor;
                    SpawnLabel(rowRT, $"<color=#aaaaaa>{cd.manaCost}</color>  {cd.cardName}",
                        14f, Color.white, TextAlignmentOptions.MidlineLeft, false,
                        V2(0.02f, 0f), V2(0.78f, 1f));
                    if (n > 1)
                        SpawnLabel(rowRT, $"x{n}", 14f, accentColor,
                            TextAlignmentOptions.MidlineRight, false, V2(0.78f, 0f), Vector2.one);
                }
            }
            LayoutRebuilder.ForceRebuildLayoutImmediate(previewContent);
        }

        void SelectDeck(DeckData deck)
        {
            string cnt = deck.cards != null ? $"{deck.cards.Count} la bai" : "";
            if (_pickingForPlayer)
            {
                _playerDeck = deck;
                if (playerDeckName) playerDeckName.text = deck.deckName;
                if (playerCardCount) playerCardCount.text = cnt;
            }
            else
            {
                _enemyDeck = deck;
                if (enemyDeckName) enemyDeckName.text = deck.deckName;
                if (enemyCardCount) enemyCardCount.text = cnt;
            }
            ClosePicker(); RefreshStartButton();
        }

        void RefreshStartButton()
        {
            bool ready = _playerDeck != null && _enemyDeck != null;
            if (startBtn) startBtn.interactable = ready;
            if (startBtnLabel)
                startBtnLabel.text = ready ? "BAT DAU TRAN DAU" : "Chon deck de bat dau";
        }

        void OnStartClicked()
        {
            if (_playerDeck == null || _enemyDeck == null) return;
            GameConfig.playerDeck = _playerDeck;
            GameConfig.enemyDeck = _enemyDeck;
            ScreenFade.LoadScene(gameSceneName);
        }

        // ══════════════════════════════════════════════════════════
        // AUTO SETUP
        // ══════════════════════════════════════════════════════════
        public void SetupUI()
        {
            for (int i = transform.childCount - 1; i >= 0; i--)
                DestroyImmediate(transform.GetChild(i).gameObject);

            var self = GetRT(transform);
            Stretch(self);

            // BG
            var bg = MakeRect("BG", self, V2(0, 0), V2(1, 1));
            _bgImg = SetImage(bg, bgColor);

            // Header
            var hdr = MakeRect("Header", self, V2(0, 0.88f), V2(1, 1));
            _headerImg = SetImage(hdr, panelHeaderColor);
            _headerTitle = SpawnLabel(hdr.transform, "LoR Clone", 38f,
                accentColor, TextAlignmentOptions.Center, true);

            // Player panel
            (_playerPanelImg, _playerPanelHdrImg) = BuildSidePanel(
                "PlayerPanel", self, V2(0.03f, 0.20f), V2(0.45f, 0.82f), "NGUOI CHOI",
                out playerDeckName, out playerCardCount,
                out playerChooseBtn, out _playerChooseBtnImg);

            // VS
            SpawnLabel(self.transform, "VS", 36f, new Color(0.7f, 0.7f, 0.75f, 0.9f),
                TextAlignmentOptions.Center, true, V2(0.43f, 0.40f), V2(0.57f, 0.60f));

            // Enemy panel
            (_enemyPanelImg, _enemyPanelHdrImg) = BuildSidePanel(
                "EnemyPanel", self, V2(0.55f, 0.20f), V2(0.97f, 0.82f), "DOI THU",
                out enemyDeckName, out enemyCardCount,
                out enemyChooseBtn, out _enemyChooseBtnImg);

            // Start button
            var sbRT = MakeRect("StartButton", self, V2(0.28f, 0.06f), V2(0.72f, 0.16f));
            _startBtnImg = SetImage(sbRT, btnGreenColor);
            startBtn = sbRT.gameObject.AddComponent<Button>();
            startBtn.targetGraphic = _startBtnImg;
            ApplyBtnColors(startBtn, btnGreenColor, new Color(0.18f, 0.78f, 0.36f));
            startBtnLabel = SpawnLabel(sbRT.transform, "Chon deck de bat dau",
                22f, Color.white, TextAlignmentOptions.Center, true);

            // Picker overlay
            BuildPickerOverlay(self);

            Debug.Log("[LobbyManager] Setup done!");
        }

        // ── Side panel ────────────────────────────────────────────
        (Image panelImg, Image hdrImg) BuildSidePanel(
            string name, RectTransform parent, Vector2 ancMin, Vector2 ancMax, string title,
            out TextMeshProUGUI deckNameOut, out TextMeshProUGUI cardCountOut,
            out Button chooseBtnOut, out Image chooseBtnImg)
        {
            var panel = MakeRect(name, parent, ancMin, ancMax);
            var pImg = SetImage(panel, panelColor);

            var hdr = MakeRect("TitleBar", panel, V2(0, 0.82f), V2(1, 1));
            var hImg = SetImage(hdr, panelHeaderColor);
            SpawnLabel(hdr.transform, title, 18f, accentColor, TextAlignmentOptions.Center, true);

            deckNameOut = SpawnLabel(panel.transform, "-- Chua chon --",
                20f, Color.white, TextAlignmentOptions.Center, false,
                V2(0.05f, 0.52f), V2(0.95f, 0.78f));

            cardCountOut = SpawnLabel(panel.transform, "",
                14f, dimColor, TextAlignmentOptions.Center, false,
                V2(0.05f, 0.38f), V2(0.95f, 0.52f));

            var btnRT = MakeRect("ChooseBtn", panel, V2(0.10f, 0.08f), V2(0.90f, 0.32f));
            chooseBtnImg = SetImage(btnRT, btnBlueColor);
            chooseBtnOut = btnRT.gameObject.AddComponent<Button>();
            chooseBtnOut.targetGraphic = chooseBtnImg;
            ApplyBtnColors(chooseBtnOut, btnBlueColor, new Color(0.30f, 0.55f, 1f));
            SpawnLabel(btnRT.transform, "Chon Deck", 16f, Color.white,
                TextAlignmentOptions.Center, true);

            return (pImg, hImg);
        }

        // ── Picker overlay ────────────────────────────────────────
        void BuildPickerOverlay(RectTransform parent)
        {
            var overlayRT = MakeRect("PickerOverlay", parent, V2(0, 0), V2(1, 1));
            SetImage(overlayRT, new Color(0, 0, 0, 0.82f));
            pickerOverlay = overlayRT.gameObject;

            var panel = MakeRect("Panel", overlayRT, V2(0.04f, 0.06f), V2(0.96f, 0.94f));
            _pickerPanelImg = SetImage(panel, pickerBgColor);

            var tbar = MakeRect("TitleBar", panel, V2(0, 0.91f), V2(1, 1));
            SetImage(tbar, panelHeaderColor);
            SpawnLabel(tbar.transform, "Chon Deck", 22f, accentColor,
                TextAlignmentOptions.Center, true);

            var closeRT = MakeRect("CloseBtn", panel, V2(0.93f, 0.92f), V2(0.995f, 0.995f));
            SetImage(closeRT, new Color(0.55f, 0.12f, 0.12f));
            closePickerBtn = closeRT.gameObject.AddComponent<Button>();
            closePickerBtn.targetGraphic = closeRT.GetComponent<Image>();
            ApplyBtnColors(closePickerBtn, new Color(0.55f, 0.12f, 0.12f), new Color(0.8f, 0.2f, 0.2f));
            SpawnLabel(closeRT.transform, "X", 14f, Color.white, TextAlignmentOptions.Center, true);

            // Deck list (left)
            var dlBG = MakeRect("DeckListArea", panel, V2(0, 0), V2(0.38f, 0.90f));
            _deckListBgImg = SetImage(dlBG, deckListBgColor);
            var dlHdr = MakeRect("DeckListHdr", dlBG, V2(0, 0.92f), V2(1, 1));
            _deckListHdrImg = SetImage(dlHdr, deckListHdrColor);
            SpawnLabel(dlHdr.transform, "DECK CO SAN", 15f, Color.white,
                TextAlignmentOptions.Center, true);
            deckListContent = BuildScrollArea("DeckListScroll", dlBG, V2(0, 0), V2(1, 0.91f),
                out deckListScrollRT, out deckListViewport);
            deckRowTemplate = BuildDeckRowTemplate(deckListContent);

            // Divider
            var div = MakeRect("Divider", panel, V2(0.385f, 0), V2(0.390f, 0.90f));
            SetImage(div, new Color(0.3f, 0.3f, 0.3f, 0.5f));

            // Preview (right)
            var pvBG = MakeRect("PreviewArea", panel, V2(0.40f, 0), V2(1, 0.90f));
            _previewBgImg = SetImage(pvBG, previewBgColor);
            previewTitle = SpawnLabel(pvBG.transform, "Chon deck de xem noi dung",
                16f, Color.white, TextAlignmentOptions.Center, false,
                V2(0, 0.91f), V2(1, 1));
            previewContent = BuildScrollArea("PreviewScroll", pvBG, V2(0, 0), V2(1, 0.90f),
                out cardListScrollRT, out cardListViewport);
            cardRowTemplate = BuildCardRowTemplate(previewContent);
        }

        // ── Templates ─────────────────────────────────────────────
        GameObject BuildDeckRowTemplate(RectTransform parent)
        {
            var rt = MakeRect("DeckRowTemplate", parent, Vector2.zero, Vector2.one);
            rt.gameObject.AddComponent<LayoutElement>().preferredHeight = 64f;
            SetImage(rt, rowNormal);
            rt.gameObject.AddComponent<Button>().targetGraphic = rt.GetComponent<Image>();

            // DeckName — đặt tên đúng để script tìm được
            SpawnLabel(rt, "Ten Deck", 15f, Color.white,
                TextAlignmentOptions.MidlineLeft, true,
                V2(0.02f, 0.5f), V2(0.68f, 1f))
                .gameObject.name = "DeckName";

            // CardCount
            SpawnLabel(rt, "0 la", 12f, dimColor,
                TextAlignmentOptions.MidlineLeft, false,
                V2(0.02f, 0f), V2(0.68f, 0.5f))
                .gameObject.name = "CardCount";

            // SelectBtn
            var selRT = MakeRect("SelectBtn", rt, V2(0.70f, 0.10f), V2(0.98f, 0.90f));
            SetImage(selRT, btnBlueColor);
            selRT.gameObject.AddComponent<Button>().targetGraphic = selRT.GetComponent<Image>();
            SpawnLabel(selRT, "Chon", 14f, Color.white, TextAlignmentOptions.Center, true);

            rt.gameObject.SetActive(false);  // ẩn template, chỉ clone lúc runtime
            return rt.gameObject;
        }

        GameObject BuildCardRowTemplate(RectTransform parent)
        {
            var rt = MakeRect("CardRowTemplate", parent, Vector2.zero, Vector2.one);
            rt.gameObject.AddComponent<LayoutElement>().preferredHeight = 36f;
            SetImage(rt, unitRowColor);

            // ManaCost
            SpawnLabel(rt, "0", 14f, new Color(0.67f, 0.67f, 0.67f),
                TextAlignmentOptions.MidlineLeft, false,
                V2(0.02f, 0f), V2(0.12f, 1f))
                .gameObject.name = "ManaCost";

            // CardName
            SpawnLabel(rt, "Ten La Bai", 14f, Color.white,
                TextAlignmentOptions.MidlineLeft, false,
                V2(0.14f, 0f), V2(0.78f, 1f))
                .gameObject.name = "CardName";

            // Count (x2, x3...)
            SpawnLabel(rt, "x1", 14f, accentColor,
                TextAlignmentOptions.MidlineRight, false,
                V2(0.78f, 0f), Vector2.one)
                .gameObject.name = "Count";

            rt.gameObject.SetActive(false);
            return rt.gameObject;
        }

        // ── Scroll area ───────────────────────────────────────────
        /// <summary>
        /// Tạo ScrollRect hoàn chỉnh: ScrollRect → Viewport (Mask) → Content (VLG + CSF).
        /// scrollRTOut  = GameObject ScrollRect  ← kéo resize cái này trong Editor
        /// viewportRTOut = Viewport (có Mask)
        /// return value  = Content              ← script dùng để Instantiate rows
        /// </summary>
        RectTransform BuildScrollArea(string scrollName, RectTransform parent,
            Vector2 ancMin, Vector2 ancMax,
            out RectTransform scrollRTOut, out RectTransform viewportRTOut)
        {
            var scrollRT = MakeRect(scrollName, parent, ancMin, ancMax);
            scrollRT.offsetMin = new Vector2(4, 4);
            scrollRT.offsetMax = new Vector2(-4, 0);
            var sr = scrollRT.gameObject.AddComponent<ScrollRect>();
            sr.horizontal = false;

            var vpRT = MakeRect("Viewport", scrollRT, Vector2.zero, Vector2.one);
            var vpImg = vpRT.gameObject.AddComponent<Image>(); vpImg.color = Color.white;
            var mask = vpRT.gameObject.AddComponent<Mask>(); mask.showMaskGraphic = false;

            var contentRT = MakeRect("Content", vpRT, V2(0, 1), V2(1, 1));
            contentRT.pivot = V2(0.5f, 1);
            contentRT.offsetMin = new Vector2(0, -300);
            contentRT.offsetMax = Vector2.zero;
            var vlg = contentRT.gameObject.AddComponent<VerticalLayoutGroup>();
            vlg.spacing = 3f; vlg.padding = new RectOffset(3, 3, 3, 3);
            vlg.childForceExpandWidth = true; vlg.childForceExpandHeight = false;
            var csf = contentRT.gameObject.AddComponent<ContentSizeFitter>();
            csf.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

            sr.viewport = vpRT; sr.content = contentRT;
            sr.movementType = ScrollRect.MovementType.Clamped;

            scrollRTOut = scrollRT;
            viewportRTOut = vpRT;
            return contentRT;
        }

        // ── Micro helpers ─────────────────────────────────────────
        static TextMeshProUGUI SpawnLabel(Transform parent, string text, float size,
            Color color, TextAlignmentOptions align, bool bold,
            Vector2? ancMin = null, Vector2? ancMax = null)
        {
            var go = new GameObject("Label");
            go.transform.SetParent(parent, false);
            var rt = go.AddComponent<RectTransform>();
            rt.anchorMin = ancMin ?? Vector2.zero;
            rt.anchorMax = ancMax ?? Vector2.one;
            rt.offsetMin = rt.offsetMax = Vector2.zero;
            var tmp = go.AddComponent<TextMeshProUGUI>();
            tmp.text = text; tmp.fontSize = size; tmp.color = color;
            tmp.alignment = align;
            tmp.fontStyle = bold ? FontStyles.Bold : FontStyles.Normal;
            tmp.raycastTarget = false;
            return tmp;
        }

        static void SpawnPlaceholder(RectTransform parent, string msg)
        {
            var go = new GameObject("Placeholder");
            go.transform.SetParent(parent, false);
            go.AddComponent<RectTransform>();
            go.AddComponent<LayoutElement>().preferredHeight = 60f;
            SpawnLabel(go.transform, msg, 12f, new Color(0.9f, 0.5f, 0.5f),
                TextAlignmentOptions.Center, false);
        }

        static RectTransform MakeRect(string name, RectTransform parent,
            Vector2 ancMin, Vector2 ancMax)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            var rt = go.AddComponent<RectTransform>();
            rt.anchorMin = ancMin; rt.anchorMax = ancMax;
            rt.offsetMin = rt.offsetMax = Vector2.zero;
            return rt;
        }

        static Image SetImage(RectTransform rt, Color color)
        {
            var img = rt.gameObject.GetComponent<Image>() ?? rt.gameObject.AddComponent<Image>();
            img.color = color;
            return img;
        }

        static void ApplyBtnColors(Button btn, Color normal, Color highlight)
        {
            var bc = btn.colors;
            bc.normalColor = normal;
            bc.highlightedColor = highlight;
            bc.pressedColor = new Color(
                Mathf.Max(normal.r - 0.1f, 0), Mathf.Max(normal.g - 0.1f, 0),
                Mathf.Max(normal.b - 0.1f, 0));
            bc.disabledColor = new Color(0.3f, 0.3f, 0.3f, 0.5f);
            btn.colors = bc;
        }

        static void Stretch(RectTransform rt)
        {
            rt.anchorMin = Vector2.zero; rt.anchorMax = Vector2.one;
            rt.offsetMin = rt.offsetMax = Vector2.zero;
        }

        static RectTransform GetRT(Transform t)
            => t.GetComponent<RectTransform>() ?? t.gameObject.AddComponent<RectTransform>();

        static Vector2 V2(float x, float y) => new Vector2(x, y);
    }
}