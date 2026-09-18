using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.EventSystems;
using TMPro;
using LoRClone.Data;
using LoRClone.Model;

namespace LoRClone.View
{
    /// <summary>
    /// XƯỞNG DECK — tạo / sửa / xóa deck tự chọn ngay trong game.
    ///
    /// UI tự dựng 100% bằng code khi Open() lần đầu — KHÔNG cần setup prefab/editor.
    /// Toàn bộ kích thước chữ / chiều cao hàng / màu sắc đều chỉnh được trong Inspector
    /// (đổi xong bấm Play lại để UI dựng lại theo giá trị mới).
    ///
    /// Layout:
    ///   Trái  = thư viện bài (click để thêm; sọc màu trái = loại bài; hiện số bản đã có)
    ///   Phải  = tên deck + deck hiện tại (click để bớt 1) + deck đã lưu (Sửa / Xóa)
    ///
    /// Luật: maxCopies bản sao mỗi lá, deck từ minCards đến maxCards lá.
    /// Lưu qua CustomDeckStore (JSON, persistentDataPath) — picker của LobbyManager
    /// tự hiện deck đã lưu (tên kèm dấu *).
    /// </summary>
    public class ChampionDeckBuilderView : MonoBehaviour
    {
        [Header("Data")]
        [Tooltip("Danh mục card. Để trống → tự gom từ fallbackDecks.")]
        public CardLibrary cardLibrary;
        [Tooltip("Fallback: gom card từ các deck này nếu cardLibrary trống.")]
        public DeckData[] fallbackDecks;

        [Tooltip("Gán CampaignData → card quà Leo Tháp bị KHÓA trong thư viện\n" +
                 "cho tới khi thắng tầng tương ứng (LobbyMenuView tự gán nếu có campaign).")]
        public CampaignData campaign;

        [Header("Card Prefab — thư viện hiện CARD THẬT dạng lưới (giống LoR)")]
        [Tooltip("Kéo card prefab (có CardView) vào đây → thư viện hiện lưới card thật.\n" +
                 "Để trống → thư viện hiện danh sách chữ như cũ.")]
        public GameObject cardPrefab;
        [Tooltip("Kích thước mỗi ô card trong lưới thư viện.")]
        public Vector2 cardCellSize = new Vector2(132f, 190f);
        [Tooltip("Scale card khi phóng to xem chi tiết.\n0 = dùng inspectScale của CardView — giống HỆT zoom trong gameplay.")]
        public float inspectCardScale = 0f;
        [Tooltip("Kéo card xa hơn khoảng này (px) rồi thả = thêm vào deck.")]
        public float dragAddThreshold = 55f;

        [Header("Luật deck — CHAMPION: 15 lá / 1 bản sao")]
        [Min(1)] public int maxCopies = 1;
        [Min(1)] public int minCards = 15;
        [Min(1)] public int maxCards = 15;

        [Header("Kích thước — chỉnh thoải mái trong Inspector")]
        public float titleFontSize = 26f;
        public float headerFontSize = 16f;
        public float rowFontSize = 18f;
        public float buttonFontSize = 16f;
        public float libraryRowHeight = 54f;
        public float deckRowHeight = 46f;
        public float savedRowHeight = 54f;

        [Header("Màu nền")]
        public Color overlayColor = new Color(0f, 0f, 0f, 0.92f);
        public Color panelColor = new Color(0.075f, 0.095f, 0.135f);
        public Color headerColor = new Color(0.115f, 0.150f, 0.210f);
        public Color areaColor = new Color(0.055f, 0.070f, 0.100f);

        [Header("Màu hàng")]
        public Color rowColor = new Color(0.125f, 0.165f, 0.230f);
        public Color rowHoverColor = new Color(0.190f, 0.250f, 0.350f);
        public Color manaBadgeColor = new Color(0.130f, 0.270f, 0.430f);

        [Header("Sọc màu theo loại bài (viền trái mỗi hàng)")]
        public Color unitStripe = new Color(0.850f, 0.630f, 0.210f);
        public Color spellStripe = new Color(0.290f, 0.550f, 1.000f);
        public Color equipStripe = new Color(0.230f, 0.710f, 0.560f);

        [Header("Màu chữ / nút")]
        public Color accentColor = new Color(0.940f, 0.780f, 0.350f);
        public Color textDimColor = new Color(0.600f, 0.660f, 0.740f);
        public Color btnGreen = new Color(0.180f, 0.660f, 0.310f);
        public Color btnBlue = new Color(0.230f, 0.460f, 0.880f);
        public Color btnRed = new Color(0.770f, 0.240f, 0.240f);

        // ── Runtime ───────────────────────────────────────────────
        GameObject _overlay;
        public RectTransform uiParent; // RunMapView gán = ChampionSelectCanvas (canvas đang render)
        RectTransform _libContent, _deckContent, _savedContent;
        TMP_InputField _nameInput;
        TextMeshProUGUI _countLabel, _msgLabel, _libHeaderLabel;

        readonly Dictionary<CardData, int> _current = new Dictionary<CardData, int>();
        readonly Dictionary<CardData, TextMeshProUGUI> _libCountLabels = new Dictionary<CardData, TextMeshProUGUI>();
        readonly Dictionary<CardData, int> _lockedRewards = new Dictionary<CardData, int>(); // card → index tầng thưởng
        List<CardData> _libraryCards;

        // Chế độ champion
        List<CardData> _restrictPool;   // kho = bài champion
        string _championName;
        CardData _lockedCard;           // lá champion — luôn có
        System.Action _onSaved;         // refresh màn champion

        // ══════════════════════════════════════════════════════════
        public void Open()
        {
            if (_overlay == null) BuildUI();
            _overlay.SetActive(true);
            CollectLibrary();
            RefreshLibrary();
            RefreshDeck();
            RefreshSaved();
            Msg("");
        }

        public void Close()
        {
            CloseCardInspect();
            EndDragGhost();
            if (_overlay != null) _overlay.SetActive(false);
        }

        /// <summary>Mở Xưởng Deck cho CHAMPION: kho = pool, 15 lá/1 bản sao, lưu ChampionDeckStore.</summary>
        public void OpenForChampion(string champName, List<CardData> pool, List<CardData> current,
                                    CardData lockedCard = null, System.Action onSaved = null)
        {
            _championName = champName;
            _restrictPool = pool;
            _lockedCard = lockedCard;
            _onSaved = onSaved;
            maxCopies = 1; minCards = 15; maxCards = 15;
            if (_overlay == null) BuildUI();
            else if (uiParent != null && _overlay.transform.parent != uiParent)
                _overlay.transform.SetParent(uiParent, false); // canvas champion select có thể đã dựng lại
            _overlay.SetActive(true);
            _overlay.transform.SetAsLastSibling();
            CollectLibrary();
            RefreshLibrary();
            _current.Clear();
            if (current != null)
                foreach (var c in current) if (c != null && !_current.ContainsKey(c)) _current[c] = 1;
            if (_lockedCard != null && !_current.ContainsKey(_lockedCard)) _current[_lockedCard] = 1;
            if (_nameInput != null) _nameInput.text = champName;
            RefreshDeck();
            RefreshSaved();
            Msg($"Deck của {champName} — 15 lá / 1 bản sao.");
        }

        /// <summary>
        /// Mở Xưởng Deck và nạp sẵn 1 deck đã lưu để chỉnh sửa.
        /// Gọi từ DeckManagerView khi bấm "Sửa".
        /// </summary>
        public void OpenForEdit(CustomDeckStore.DeckDTO dto)
        {
            Open();
            if (dto != null) LoadForEdit(dto);
        }

        // ── Deck logic ────────────────────────────────────────────
        int TotalCount()
        {
            int t = 0;
            foreach (var kv in _current) t += kv.Value;
            return t;
        }

        void AddCard(CardData cd)
        {
            if (TotalCount() >= maxCards) { Msg($"Deck đã đạt tối đa {maxCards} lá."); return; }
            _current.TryGetValue(cd, out int n);
            if (n >= maxCopies) { Msg($"Tối đa {maxCopies} bản sao mỗi lá."); return; }
            _current[cd] = n + 1;
            Msg("");
            RefreshDeck();
        }

        void RemoveCard(CardData cd)
        {
            if (_lockedCard != null && cd == _lockedCard) { Msg("Lá champion luôn phải có trong deck."); return; }
            if (!_current.TryGetValue(cd, out int n)) return;
            if (n <= 1) _current.Remove(cd);
            else _current[cd] = n - 1;
            Msg("");
            RefreshDeck();
        }

        void OnNewDeck()
        {
            _current.Clear();
            if (_lockedCard != null) _current[_lockedCard] = 1; // giữ lá champion
            Msg("Deck champion mới — thêm bài từ kho bên trái.");
            RefreshDeck();
        }

        void OnSave()
        {
            int total = TotalCount();
            if (total < minCards) { Msg($"Deck cần đủ {minCards} lá (đang {total})."); return; }
            var cards = new List<CardData>();
            foreach (var kv in _current) cards.Add(kv.Key); // 1 bản sao/lá
            ChampionDeckStore.Save(_championName, cards);
            Msg($"Đã lưu deck cho {_championName} ({total} lá).");
            var cb = _onSaved;
            Close();
            cb?.Invoke();
        }

        void LoadForEdit(CustomDeckStore.DeckDTO dto)
        {
            var resolver = CustomDeckStore.BuildResolver(cardLibrary, fallbackDecks);
            _current.Clear();
            int missing = 0;
            foreach (var n in dto.cardNames)
            {
                if (resolver.TryGetValue(n, out var cd))
                {
                    _current.TryGetValue(cd, out int c);
                    _current[cd] = c + 1;
                }
                else missing++;
            }
            if (_nameInput != null) _nameInput.text = dto.name;
            Msg(missing > 0 ? $"Đã nạp '{dto.name}' — thiếu {missing} lá không còn tồn tại." : $"Đang sửa deck '{dto.name}'.");
            RefreshDeck();
        }

        void CollectLibrary()
        {
            if (_restrictPool != null) // kho = bài của champion
            {
                _libraryCards = new List<CardData>();
                var seenP = new HashSet<string>();
                foreach (var c in _restrictPool)
                    if (c != null && !c.isGenerated && seenP.Add(c.cardName)) _libraryCards.Add(c);
                _libraryCards.Sort((a, b) => a.manaCost != b.manaCost ? a.manaCost.CompareTo(b.manaCost)
                    : string.Compare(a.cardName, b.cardName, System.StringComparison.Ordinal));
                _lockedRewards.Clear();
                return;
            }

            var map = CustomDeckStore.BuildResolver(cardLibrary, fallbackDecks);
            _libraryCards = new List<CardData>();
            foreach (var c in map.Values)
                if (!c.isGenerated) _libraryCards.Add(c); // card generated không được build deck
            _libraryCards.Sort((a, b) => a.manaCost != b.manaCost
                ? a.manaCost.CompareTo(b.manaCost)
                : string.Compare(a.cardName, b.cardName, System.StringComparison.Ordinal));

            // Card quà Leo Tháp chưa unlock → khóa trong thư viện
            _lockedRewards.Clear();
            if (campaign != null && campaign.levels != null)
                for (int i = 0; i < campaign.levels.Count; i++)
                {
                    var lv = campaign.levels[i];
                    if (lv == null || lv.rewardCards == null) continue;
                    foreach (var rc in lv.rewardCards)
                        if (rc != null && !ProgressStore.IsCardUnlocked(rc.cardName))
                            _lockedRewards[rc] = i;
                }
        }

        void Msg(string s) { if (_msgLabel != null) _msgLabel.text = s; }

        // ── Refresh UI ────────────────────────────────────────────
        void RefreshLibrary()
        {
            ClearChildren(_libContent);
            _libCountLabels.Clear();

            if (_libHeaderLabel != null)
                _libHeaderLabel.text = $"THƯ VIỆN BÀI ({(_libraryCards != null ? _libraryCards.Count : 0)} lá)";

            if (_libraryCards == null || _libraryCards.Count == 0)
            {
                SpawnPlaceholder(_libContent, "Thư viện trống.\nGán CardLibrary hoặc availableDecks.");
                return;
            }

            foreach (var cd in _libraryCards)
            {
                var card = cd;
                bool locked = _lockedRewards.TryGetValue(card, out int rewardLevel);
                if (cardPrefab != null) MakeCardCell(card, locked, rewardLevel);
                else MakeTextRow(card, locked, rewardLevel);
            }
            SyncLibCounts();
            LayoutRebuilder.ForceRebuildLayoutImmediate(_libContent);
        }

        // ── Thư viện dạng DANH SÁCH CHỮ (fallback khi chưa gán cardPrefab) ──
        void MakeTextRow(CardData card, bool locked, int rewardLevel)
        {
            var row = MakeRow(_libContent, libraryRowHeight,
                locked ? (UnityEngine.Events.UnityAction)null : () => AddCard(card));
            AddTypeStripe(row, card);
            AddManaBadge(row, card, V2(0.025f, 0.16f), V2(0.095f, 0.84f));

            SpawnLabel(row, card.cardName, rowFontSize, locked ? textDimColor : Color.white,
                TextAlignmentOptions.MidlineLeft, false, V2(0.115f, 0f), V2(0.70f, 1f));
            SpawnLabel(row, locked ? $"Khóa — quà tầng {rewardLevel + 1}" : TypeTag(card),
                rowFontSize * 0.68f, locked ? accentColor : textDimColor,
                TextAlignmentOptions.MidlineLeft, false, V2(0.705f, 0f), V2(0.92f, 1f));
            if (locked) return;

            var cnt = SpawnLabel(row, "", rowFontSize * 0.9f, accentColor,
                TextAlignmentOptions.MidlineRight, true, V2(0.87f, 0f), V2(0.975f, 1f));
            _libCountLabels[card] = cnt;
        }

        // ── Thư viện dạng LƯỚI CARD THẬT (giống LoR) ─────────────
        // KÉO card ra khỏi ô (> dragAddThreshold px) = thêm vào deck.
        // CLICK = phóng to xem mô tả (card khóa vẫn xem được — chỉ không thêm được).
        void MakeCardCell(CardData card, bool locked, int rewardLevel)
        {
            var cell = MakeRect("Card_" + card.cardName, _libContent, Vector2.zero, Vector2.zero);

            SpawnCardVisual(card, cell, cardCellSize.x - 6f, cardCellSize.y - 6f);

            // Badge số bản đang có trong deck — góc trên-phải (SyncLibCounts bật/tắt)
            var badge = MakeRect("CountBadge", cell, V2(0.64f, 0.85f), V2(0.99f, 0.99f));
            SetImage(badge, accentColor).raycastTarget = false;
            var badgeTxt = SpawnLabel(badge, "", rowFontSize * 0.85f, Color.black,
                TextAlignmentOptions.Center, true);
            badge.gameObject.SetActive(false);

            if (locked)
            {
                var dimRT = MakeRect("LockDim", cell, Vector2.zero, Vector2.one);
                SetImage(dimRT, new Color(0f, 0f, 0f, 0.72f)).raycastTarget = false;
                SpawnLabel(dimRT, $"KHÓA\nquà tầng {rewardLevel + 1}", rowFontSize * 0.78f,
                    accentColor, TextAlignmentOptions.Center, true);
            }
            else
            {
                _libCountLabels[card] = badgeTxt;
            }

            // Input phủ toàn cell: click = xem chi tiết, kéo ra = thêm vào deck
            var inputRT = MakeRect("Input", cell, Vector2.zero, Vector2.one);
            var inputImg = SetImage(inputRT, new Color(1f, 1f, 1f, 0f));
            var hoverBtn = inputRT.gameObject.AddComponent<Button>(); // chỉ để tint hover
            hoverBtn.targetGraphic = inputImg;
            var hc = hoverBtn.colors;
            hc.normalColor = new Color(1f, 1f, 1f, 0f);
            hc.highlightedColor = new Color(1f, 1f, 1f, 0.10f);
            hc.pressedColor = new Color(1f, 1f, 1f, 0.20f);
            hc.disabledColor = new Color(1f, 1f, 1f, 0f);
            hoverBtn.colors = hc;

            var input = inputRT.gameObject.AddComponent<LibraryCardInput>();
            input.dragThreshold = dragAddThreshold;
            input.onClick = () => ShowCardInspect(card);
            if (!locked)
            {
                input.onDragAdd = () => AddCard(card);
                input.onDragBegin = e => BeginDragGhost(card, e);
                input.onDragMove = MoveDragGhost;
                input.onDragEnd = _ => EndDragGhost();
            }
        }

        /// <summary>
        /// Instantiate cardPrefab cho ô thư viện: Bind một CardModel preview rồi để CHÍNH
        /// RefreshAll của gameplay dựng artwork + stats + KEYWORD ICON (giống hệt lá trên tay).
        /// Đây là đúng code path đã hiện keyword thành công ở màn zoom → đảm bảo icon hiện.
        /// Card bị tắt tương tác — click/kéo do LibraryCardInput (overlay) xử lý.
        /// </summary>
        GameObject SpawnCardVisual(CardData card, RectTransform parent, float w, float h)
        {
            var go = Instantiate(cardPrefab, parent);
            var cv = go.GetComponent<CardView>();
            if (cv == null) return go;

            // CardView.Awake tự thêm Canvas overrideSorting — canvas con kiểu này
            // THOÁT khỏi Mask của ScrollRect → gỡ cả GraphicRaycaster lẫn Canvas.
            var gr = go.GetComponent<GraphicRaycaster>();
            if (gr != null) Destroy(gr);
            var cvCanvas = go.GetComponent<Canvas>();
            if (cvCanvas != null) Destroy(cvCanvas);

            // Size TRƯỚC Bind để RefreshArtworkUV đọc đúng tỉ lệ (giống ZoneView.SpawnCardInSlot).
            cv.OverrideSize(w, h);

            // Bind CardModel preview → RefreshAll dựng name/mana/stats + keyword icon bằng
            // đúng RefreshKeywords của gameplay. location = InHand để hiện tên/mana/stats.
            var model = new CardModel(card, true);
            model.SetLocation(CardLocation.InHand);
            cv.AllowInteract = false;
            cv.Bind(model);

            cv.ForceArtworkUV(w, h);

            // RefreshKeywordPositions chỉ tự đặt lại keyword khi card ở TRÊN SÂN.
            // Với thumbnail, ép cụm keyword ra giữa card cho chắc chắn nhìn thấy (giống lúc zoom).
            if (cv.keywordsContainer != null)
            {
                var kwRT = cv.keywordsContainer.GetComponent<RectTransform>();
                if (kwRT != null)
                {
                    kwRT.anchorMin = kwRT.anchorMax = kwRT.pivot = new Vector2(0.5f, 0.5f);
                    kwRT.anchoredPosition = Vector2.zero;
                    LayoutRebuilder.ForceRebuildLayoutImmediate(kwRT);
                }
            }

            // Thumbnail không cần chữ mô tả / lớp inspect
            if (cv.skillsText) cv.skillsText.gameObject.SetActive(false);
            if (cv.inspectOverlay) cv.inspectOverlay.SetActive(false);

            cv.enabled = false; // tắt inspect/drag/hover riêng của CardView
            return go;
        }

        // ── Drag ghost — ảnh card bay theo chuột khi kéo ─────────
        GameObject _dragGhost;
        Canvas _builderCanvasCache;

        void BeginDragGhost(CardData card, PointerEventData e)
        {
            EndDragGhost();
            var overlayRT = (RectTransform)_overlay.transform;
            var rt = MakeRect("DragGhost", overlayRT, V2(0.5f, 0.5f), V2(0.5f, 0.5f));
            rt.sizeDelta = cardCellSize * 0.9f;
            _dragGhost = rt.gameObject;
            var raw = _dragGhost.AddComponent<RawImage>();
            raw.raycastTarget = false;
            if (card.artwork != null)
            {
                raw.texture = card.artwork;
                ApplyFocalUV(raw, card, rt.sizeDelta.x, rt.sizeDelta.y);
                raw.color = new Color(1f, 1f, 1f, 0.85f);
            }
            else raw.color = new Color(1f, 1f, 1f, 0.15f);
            MoveDragGhost(e);
        }

        void MoveDragGhost(PointerEventData e)
        {
            if (_dragGhost == null) return;
            var overlayRT = (RectTransform)_overlay.transform;
            var canvas = BuilderRootCanvas();
            Camera cam = canvas != null && canvas.renderMode != RenderMode.ScreenSpaceOverlay
                ? canvas.worldCamera : null;
            if (RectTransformUtility.ScreenPointToLocalPointInRectangle(overlayRT, e.position, cam, out var local))
                ((RectTransform)_dragGhost.transform).anchoredPosition = local;
        }

        void EndDragGhost()
        {
            if (_dragGhost == null) return;
            Destroy(_dragGhost);
            _dragGhost = null;
        }

        Canvas BuilderRootCanvas()
        {
            if (_builderCanvasCache == null && _overlay != null)
            {
                var c = _overlay.GetComponentInParent<Canvas>();
                _builderCanvasCache = c != null ? c.rootCanvas : null;
            }
            return _builderCanvasCache;
        }

        // ── Popup phóng to: TÁI DÙNG inspect thật của CardView (giống hệt gameplay) ──
        GameObject _inspectCardGO;
        CardView _inspectCardView;

        void ShowCardInspect(CardData card)
        {
            CloseCardInspect();
            if (cardPrefab == null) return;

            // Instantiate card prefab ĐẦY ĐỦ (giữ Canvas + GraphicRaycaster,
            // khác SpawnCardVisual vốn gỡ chúng cho ô thư viện).
            var overlayRT = (RectTransform)_overlay.transform;
            _inspectCardGO = Instantiate(cardPrefab, overlayRT);
            var cv = _inspectCardGO.GetComponent<CardView>();
            if (cv == null) { Destroy(_inspectCardGO); _inspectCardGO = null; return; }
            _inspectCardView = cv;

            // CardModel preview độc lập — constructor chỉ cần CardData, không cần trận đấu.
            var model = new CardModel(card, true);
            model.SetLocation(CardLocation.InHand); // để tên/mana/stats hiện như lá trên tay

            cv.inspectSortingBase = 601;   // nổi trên overlay xưởng deck (Canvas order 600)
            cv.AllowInteract = false;      // chặn drag/chơi bài — vẫn cho inspect + click keyword
            cv.Bind(model);                // RefreshAll: artwork, stats, icon keyword (raycast bật)

            cv.OnInspectClosed += HandleInspectClosed;
            cv.OpenInspectExternally();    // = EnterInspect: dim + Xem ảnh + lá liên quan + keyword popup
        }

        void HandleInspectClosed() => CloseCardInspect();

        void CloseCardInspect()
        {
            if (_inspectCardView != null)
            {
                _inspectCardView.OnInspectClosed -= HandleInspectClosed;
                _inspectCardView = null;
            }
            // OnDestroy của CardView tự dọn dim/dải/art viewer nếu còn mở.
            if (_inspectCardGO != null) { Destroy(_inspectCardGO); _inspectCardGO = null; }
        }

        /// <summary>uvRect theo focal point của CardData (copy logic CardView.ApplyFocalUV).</summary>
        static void ApplyFocalUV(RawImage raw, CardData data, float dispW, float dispH)
        {
            if (raw == null || raw.texture == null || data == null) return;
            var tex = raw.texture;
            if (tex.width <= 0 || tex.height <= 0) { raw.uvRect = new Rect(0, 0, 1, 1); return; }
            float texAspect = (float)tex.width / tex.height;
            float dispAspect = Mathf.Max(dispW, 1f) / Mathf.Max(dispH, 1f);
            float zoom = Mathf.Max(0.1f, data.artworkZoom);
            float baseW, baseH;
            if (texAspect > dispAspect) { baseH = 1f; baseW = dispAspect / texAspect; }
            else { baseW = 1f; baseH = texAspect / dispAspect; }
            float uvW = Mathf.Clamp(baseW / zoom, 0.001f, 1f);
            float uvH = Mathf.Clamp(baseH / zoom, 0.001f, 1f);
            float uvX = Mathf.Clamp(data.artworkFocalX - uvW * 0.5f, 0f, 1f - uvW);
            float uvY = Mathf.Clamp(data.artworkFocalY - uvH * 0.5f, 0f, 1f - uvH);
            raw.uvRect = new Rect(uvX, uvY, uvW, uvH);
        }

        void RefreshDeck()
        {
            ClearChildren(_deckContent);

            var sorted = new List<KeyValuePair<CardData, int>>(_current);
            sorted.Sort((a, b) => a.Key.manaCost != b.Key.manaCost
                ? a.Key.manaCost.CompareTo(b.Key.manaCost)
                : string.Compare(a.Key.cardName, b.Key.cardName, System.StringComparison.Ordinal));

            foreach (var kv in sorted)
            {
                var card = kv.Key;
                var row = MakeRow(_deckContent, deckRowHeight, () => RemoveCard(card));
                AddTypeStripe(row, card);
                AddRowArtwork(row, card); // ảnh nhận diện — nửa phải thanh, dưới chữ
                AddManaBadge(row, card, V2(0.025f, 0.14f), V2(0.105f, 0.86f));

                SpawnLabel(row, card.cardName, rowFontSize * 0.94f, Color.white,
                    TextAlignmentOptions.MidlineLeft, false, V2(0.13f, 0f), V2(0.80f, 1f));
                SpawnLabel(row, $"x{kv.Value}", rowFontSize * 0.94f, accentColor,
                    TextAlignmentOptions.MidlineRight, true, V2(0.80f, 0f), V2(0.97f, 1f));
            }

            if (sorted.Count == 0)
                SpawnPlaceholder(_deckContent, "Deck trống.\nClick bài bên trái để thêm — click bài trong deck để bớt.");

            int total = TotalCount();
            if (_countLabel != null)
            {
                _countLabel.text = $"{total}/{maxCards}";
                _countLabel.color = total >= minCards && total <= maxCards
                    ? new Color(0.45f, 0.90f, 0.55f) : accentColor;
            }
            SyncLibCounts();
            LayoutRebuilder.ForceRebuildLayoutImmediate(_deckContent);
        }

        void SyncLibCounts()
        {
            foreach (var kv in _libCountLabels)
            {
                if (kv.Value == null) continue;
                _current.TryGetValue(kv.Key, out int n);
                kv.Value.text = n > 0 ? $"x{n}" : "";
                // Grid mode: badge có nền riêng (parent tên CountBadge) — ẩn khi chưa có bản nào
                var badgeRoot = kv.Value.transform.parent;
                if (badgeRoot != null && badgeRoot.name == "CountBadge")
                    badgeRoot.gameObject.SetActive(n > 0);
            }
        }

        void RefreshSaved()
        {
            ClearChildren(_savedContent);
            // Champion: KHÔNG hiện deck PvP đã lưu (tránh nạp nhầm deck 40 lá).
            if (_championName != null)
            {
                SpawnPlaceholder(_savedContent, "Deck champion — lưu riêng theo tướng.\nBấm LƯU DECK để lưu.");
                return;
            }
            var all = CustomDeckStore.LoadAll();
            if (all.Count == 0)
            {
                SpawnPlaceholder(_savedContent, "Chưa có deck nào được lưu.");
                return;
            }
            foreach (var dto in all)
            {
                var d = dto;
                var row = MakeRow(_savedContent, savedRowHeight, null);

                SpawnLabel(row, d.name, rowFontSize * 0.94f, Color.white,
                    TextAlignmentOptions.MidlineLeft, true, V2(0.03f, 0f), V2(0.50f, 1f));
                SpawnLabel(row, $"{d.cardNames.Count} lá", rowFontSize * 0.78f, textDimColor,
                    TextAlignmentOptions.MidlineLeft, false, V2(0.50f, 0f), V2(0.64f, 1f));

                MakeButton(row, "Sửa", btnBlue, V2(0.66f, 0.14f), V2(0.81f, 0.86f), () => LoadForEdit(d));
                MakeButton(row, "Xóa", btnRed, V2(0.83f, 0.14f), V2(0.98f, 0.86f), () =>
                {
                    CustomDeckStore.DeleteDeck(d.name);
                    Msg($"Đã xóa deck '{d.name}'.");
                    RefreshSaved();
                });
            }
            LayoutRebuilder.ForceRebuildLayoutImmediate(_savedContent);
        }

        // ══════════════════════════════════════════════════════════
        // BUILD UI (1 lần, procedural)
        // ══════════════════════════════════════════════════════════
        void BuildUI()
        {
            // Dựng UI DƯỚI canvas do RunMapView cấp (ChampionSelectCanvas — đang render tốt).
            // KHÔNG tự tạo canvas (nested canvas trên GameObject thường bị CanvasScaler tính sai → vô hình).
            var parentRT = uiParent != null ? uiParent : GetComponent<RectTransform>();
            var overlayRT = MakeRect("ChampionDeckBuilderOverlay", parentRT, Vector2.zero, Vector2.one);
            _overlay = overlayRT.gameObject;
            _overlay.transform.SetAsLastSibling(); // nổi trên panel chọn champion
            SetImage(overlayRT, overlayColor);

            var panel = MakeRect("Panel", overlayRT, V2(0.03f, 0.04f), V2(0.97f, 0.96f));
            SetImage(panel, panelColor);

            // Title bar
            var tbar = MakeRect("TitleBar", panel, V2(0f, 0.92f), V2(1f, 1f));
            SetImage(tbar, headerColor);
            SpawnLabel(tbar, "XƯỞNG DECK CHAMPION — 15 lá / 1 bản sao", titleFontSize, accentColor, TextAlignmentOptions.Center, true);
            MakeButton(MakeRect("CloseHolder", tbar, V2(0.955f, 0.12f), V2(0.995f, 0.88f)),
                "X", btnRed, Vector2.zero, Vector2.one, Close);

            // ── Trái: thư viện ──
            var lib = MakeRect("LibraryArea", panel, V2(0.008f, 0.008f), V2(0.52f, 0.912f));
            SetImage(lib, areaColor);
            var libHdr = MakeRect("Hdr", lib, V2(0f, 0.93f), V2(1f, 1f));
            SetImage(libHdr, headerColor);
            _libHeaderLabel = SpawnLabel(libHdr, "THƯ VIỆN BÀI", headerFontSize, Color.white,
                TextAlignmentOptions.MidlineLeft, true, V2(0.02f, 0f), V2(0.6f, 1f));
            SpawnLabel(libHdr, "click để thêm", headerFontSize * 0.8f, textDimColor,
                TextAlignmentOptions.MidlineRight, false, V2(0.6f, 0f), V2(0.98f, 1f));
            _libContent = BuildScrollArea("LibScroll", lib, V2(0f, 0f), V2(1f, 0.92f),
                grid: cardPrefab != null);

            // ── Phải ──
            var right = MakeRect("RightArea", panel, V2(0.53f, 0.008f), V2(0.992f, 0.912f));

            _nameInput = MakeInput(right, V2(0f, 0.905f), V2(0.58f, 0.99f), "Tên deck...");
            MakeButton(MakeRect("SaveHolder", right, V2(0.60f, 0.905f), V2(0.79f, 0.99f)),
                "LƯU DECK", btnGreen, Vector2.zero, Vector2.one, OnSave);
            MakeButton(MakeRect("NewHolder", right, V2(0.81f, 0.905f), V2(1f, 0.99f)),
                "DECK MỚI", btnBlue, Vector2.zero, Vector2.one, OnNewDeck);

            var deckHdr = MakeRect("DeckHdr", right, V2(0f, 0.84f), V2(1f, 0.898f));
            SetImage(deckHdr, headerColor);
            SpawnLabel(deckHdr, "DECK HIỆN TẠI", headerFontSize, Color.white,
                TextAlignmentOptions.MidlineLeft, true, V2(0.02f, 0f), V2(0.45f, 1f));
            SpawnLabel(deckHdr, "click để bớt", headerFontSize * 0.8f, textDimColor,
                TextAlignmentOptions.MidlineLeft, false, V2(0.45f, 0f), V2(0.72f, 1f));
            _countLabel = SpawnLabel(deckHdr, "0/15", headerFontSize * 1.25f, accentColor,
                TextAlignmentOptions.MidlineRight, true, V2(0.72f, 0f), V2(0.97f, 1f));

            var deckArea = MakeRect("DeckArea", right, V2(0f, 0.375f), V2(1f, 0.84f));
            SetImage(deckArea, areaColor);
            _deckContent = BuildScrollArea("DeckScroll", deckArea, Vector2.zero, Vector2.one);

            _msgLabel = SpawnLabel(right, "", headerFontSize * 0.95f, accentColor,
                TextAlignmentOptions.MidlineLeft, false, V2(0.01f, 0.318f), V2(1f, 0.372f));

            var savedHdr = MakeRect("SavedHdr", right, V2(0f, 0.258f), V2(1f, 0.315f));
            SetImage(savedHdr, headerColor);
            SpawnLabel(savedHdr, "DECK ĐÃ LƯU", headerFontSize, Color.white,
                TextAlignmentOptions.MidlineLeft, true, V2(0.02f, 0f), V2(0.7f, 1f));

            var savedArea = MakeRect("SavedArea", right, V2(0f, 0f), V2(1f, 0.258f));
            SetImage(savedArea, areaColor);
            _savedContent = BuildScrollArea("SavedScroll", savedArea, Vector2.zero, Vector2.one);

            _overlay.SetActive(false);
        }

        // ── Row helpers ───────────────────────────────────────────
        string TypeTag(CardData cd) => cd.cardType switch
        {
            CardType.Unit => "Unit",
            CardType.Spell => $"{cd.spellSpeed}",
            CardType.Equipment => "Trang Bị",
            _ => "",
        };

        Color StripeColor(CardData cd) => cd.cardType switch
        {
            CardType.Unit => unitStripe,
            CardType.Equipment => equipStripe,
            _ => spellStripe,
        };

        /// <summary>Hàng nền tối + hover; onClick null = hàng tĩnh (không phải nút).</summary>
        RectTransform MakeRow(RectTransform parent, float height, UnityEngine.Events.UnityAction onClick)
        {
            var rt = MakeRect("Row", parent, Vector2.zero, Vector2.one);
            rt.gameObject.AddComponent<LayoutElement>().preferredHeight = height;
            // Hàng có click: image trắng + Button tint theo state (chuẩn Unity —
            // normal = rowColor, hover = rowHoverColor). Hàng tĩnh: tô thẳng rowColor.
            var img = SetImage(rt, onClick != null ? Color.white : rowColor);
            if (onClick != null)
            {
                var btn = rt.gameObject.AddComponent<Button>();
                btn.targetGraphic = img;
                var bc = btn.colors;
                bc.normalColor = rowColor;
                bc.highlightedColor = rowHoverColor;
                bc.pressedColor = Color.Lerp(rowColor, Color.black, 0.25f);
                bc.disabledColor = rowColor;
                btn.colors = bc;
                btn.onClick.AddListener(onClick);
            }
            return rt;
        }

        /// <summary>Sọc màu 7px bên trái hàng — nhận diện loại bài nhanh.</summary>
        void AddTypeStripe(RectTransform row, CardData cd)
        {
            var rt = MakeRect("Stripe", row, V2(0f, 0f), V2(0f, 1f));
            rt.offsetMin = Vector2.zero;
            rt.offsetMax = new Vector2(7f, 0f);
            var img = SetImage(rt, StripeColor(cd));
            img.raycastTarget = false;
        }

        /// <summary>
        /// Ảnh artwork nhận diện trên thanh deck — chiếm nửa phải, phủ lớp tối
        /// để tên card + số xN vẫn đọc rõ (labels thêm sau nên render đè lên).
        /// </summary>
        void AddRowArtwork(RectTransform row, CardData card)
        {
            if (card.artwork == null) return;
            var artRT = MakeRect("Art", row, V2(0.40f, 0f), V2(1f, 1f));
            var raw = artRT.gameObject.AddComponent<RawImage>();
            raw.texture = card.artwork;
            raw.raycastTarget = false;
            ApplyFocalUV(raw, card, 220f, deckRowHeight); // uv theo aspect dải ngang
            var shade = MakeRect("ArtShade", row, V2(0.40f, 0f), V2(1f, 1f));
            SetImage(shade, new Color(0f, 0f, 0f, 0.38f)).raycastTarget = false;
        }

        /// <summary>Ô badge mana màu xanh đậm + số trắng.</summary>
        void AddManaBadge(RectTransform row, CardData cd, Vector2 ancMin, Vector2 ancMax)
        {
            var rt = MakeRect("Mana", row, ancMin, ancMax);
            var img = SetImage(rt, manaBadgeColor);
            img.raycastTarget = false;
            SpawnLabel(rt, cd.manaCost.ToString(), rowFontSize * 0.95f, Color.white,
                TextAlignmentOptions.Center, true);
        }

        static void ClearChildren(RectTransform content)
        {
            if (content == null) return;
            for (int i = content.childCount - 1; i >= 0; i--)
                Destroy(content.GetChild(i).gameObject);
        }

        void MakeButton(RectTransform holder, string label, Color color,
            Vector2 ancMin, Vector2 ancMax, UnityEngine.Events.UnityAction onClick)
        {
            var rt = MakeRect("Btn_" + label, holder, ancMin, ancMax);
            var img = SetImage(rt, color);
            var btn = rt.gameObject.AddComponent<Button>();
            btn.targetGraphic = img;
            var bc = btn.colors;
            bc.normalColor = color;
            bc.highlightedColor = Color.Lerp(color, Color.white, 0.22f);
            bc.pressedColor = Color.Lerp(color, Color.black, 0.22f);
            btn.colors = bc;
            btn.onClick.AddListener(onClick);
            SpawnLabel(rt, label, buttonFontSize, Color.white, TextAlignmentOptions.Center, true);
        }

        TMP_InputField MakeInput(RectTransform parent, Vector2 ancMin, Vector2 ancMax, string placeholder)
        {
            var rt = MakeRect("NameInput", parent, ancMin, ancMax);
            var bg = SetImage(rt, areaColor);
            var input = rt.gameObject.AddComponent<TMP_InputField>();
            input.targetGraphic = bg;

            var area = MakeRect("TextArea", rt, Vector2.zero, Vector2.one);
            area.offsetMin = new Vector2(10f, 5f);
            area.offsetMax = new Vector2(-10f, -5f);
            area.gameObject.AddComponent<RectMask2D>();

            var ph = SpawnLabel(area, placeholder, rowFontSize * 0.94f, new Color(1f, 1f, 1f, 0.32f),
                TextAlignmentOptions.MidlineLeft, false);
            var txt = SpawnLabel(area, "", rowFontSize * 0.94f, Color.white,
                TextAlignmentOptions.MidlineLeft, false);

            input.textViewport = area;
            input.textComponent = txt;
            input.placeholder = ph;
            input.characterLimit = 24;
            return input;
        }

        RectTransform BuildScrollArea(string name, RectTransform parent, Vector2 ancMin, Vector2 ancMax,
            bool grid = false)
        {
            var scrollRT = MakeRect(name, parent, ancMin, ancMax);
            scrollRT.offsetMin = new Vector2(5f, 5f);
            scrollRT.offsetMax = new Vector2(-5f, -5f);
            var sr = scrollRT.gameObject.AddComponent<ScrollRect>();
            sr.horizontal = false;
            sr.movementType = ScrollRect.MovementType.Clamped;
            sr.scrollSensitivity = 25f;

            var vp = MakeRect("Viewport", scrollRT, Vector2.zero, Vector2.one);
            vp.offsetMax = new Vector2(-16f, 0f); // chừa 16px bên phải cho thanh cuộn
            vp.gameObject.AddComponent<Image>().color = Color.white;
            var mask = vp.gameObject.AddComponent<Mask>();
            mask.showMaskGraphic = false;

            var content = MakeRect("Content", vp, V2(0f, 1f), V2(1f, 1f));
            content.pivot = V2(0.5f, 1f);
            content.offsetMin = new Vector2(0f, -300f);
            content.offsetMax = Vector2.zero;
            if (grid)
            {
                var glg = content.gameObject.AddComponent<GridLayoutGroup>();
                glg.cellSize = cardCellSize;
                glg.spacing = new Vector2(8f, 8f);
                glg.padding = new RectOffset(8, 8, 8, 8);
                glg.childAlignment = TextAnchor.UpperLeft;
            }
            else
            {
                var vlg = content.gameObject.AddComponent<VerticalLayoutGroup>();
                vlg.spacing = 4f;
                vlg.padding = new RectOffset(4, 4, 4, 4);
                vlg.childForceExpandWidth = true;
                vlg.childForceExpandHeight = false;
            }
            content.gameObject.AddComponent<ContentSizeFitter>().verticalFit =
                ContentSizeFitter.FitMode.PreferredSize;

            // Thanh cuộn dọc KÉO ĐƯỢC (chuột + cảm ứng/điện thoại) — luôn hiện.
            var sbRT = MakeRect("Scrollbar", scrollRT, V2(1f, 0f), V2(1f, 1f));
            sbRT.offsetMin = new Vector2(-16f, 0f);
            sbRT.offsetMax = new Vector2(0f, 0f);
            sbRT.gameObject.AddComponent<Image>().color = new Color(0.08f, 0.10f, 0.16f, 0.9f);
            var scrollbar = sbRT.gameObject.AddComponent<Scrollbar>();
            scrollbar.direction = Scrollbar.Direction.BottomToTop;
            var slideRT = MakeRect("SlidingArea", sbRT, Vector2.zero, Vector2.one);
            slideRT.offsetMin = new Vector2(2f, 2f);
            slideRT.offsetMax = new Vector2(-2f, -2f);
            var handleRT = MakeRect("Handle", slideRT, Vector2.zero, Vector2.one);
            var handleImg = handleRT.gameObject.AddComponent<Image>();
            handleImg.color = new Color(0.45f, 0.52f, 0.66f, 0.98f);
            scrollbar.targetGraphic = handleImg;
            scrollbar.handleRect = handleRT;

            sr.viewport = vp;
            sr.content = content;
            sr.verticalScrollbar = scrollbar;
            sr.verticalScrollbarVisibility = ScrollRect.ScrollbarVisibility.Permanent;
            return content;
        }

        void SpawnPlaceholder(RectTransform parent, string msg)
        {
            var go = new GameObject("Placeholder");
            go.transform.SetParent(parent, false);
            var rt = go.AddComponent<RectTransform>();
            go.AddComponent<LayoutElement>().preferredHeight = 80f;
            SpawnLabel(rt, msg, headerFontSize * 0.9f, textDimColor,
                TextAlignmentOptions.Center, false);
        }

        static TextMeshProUGUI SpawnLabel(RectTransform parent, string text, float size,
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
            tmp.text = text;
            tmp.fontSize = size;
            tmp.color = color;
            tmp.alignment = align;
            tmp.fontStyle = bold ? FontStyles.Bold : FontStyles.Normal;
            tmp.raycastTarget = false;
            return tmp;
        }

        static RectTransform MakeRect(string name, RectTransform parent, Vector2 ancMin, Vector2 ancMax)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            var rt = go.AddComponent<RectTransform>();
            rt.anchorMin = ancMin;
            rt.anchorMax = ancMax;
            rt.offsetMin = rt.offsetMax = Vector2.zero;
            return rt;
        }

        static Image SetImage(RectTransform rt, Color color)
        {
            var img = rt.gameObject.GetComponent<Image>() ?? rt.gameObject.AddComponent<Image>();
            img.color = color;
            return img;
        }

        static Vector2 V2(float x, float y) => new Vector2(x, y);

        // ── Input cho ô card: phân biệt CLICK (inspect) vs DRAG (thêm vào deck) ──
        class LibraryCardInput : MonoBehaviour,
            IPointerClickHandler, IBeginDragHandler, IDragHandler, IEndDragHandler
        {
            public System.Action onClick;
            public System.Action onDragAdd;
            public System.Action<PointerEventData> onDragBegin, onDragMove, onDragEnd;
            public float dragThreshold = 55f;

            Vector2 _start;
            bool _dragging;

            public void OnPointerClick(PointerEventData e)
            {
                // Thứ tự Unity khi thả chuột: PointerUp → PointerClick → EndDrag.
                // _dragging còn true tại đây nghĩa là vừa kéo → không tính là click.
                if (_dragging || e.dragging) return;
                onClick?.Invoke();
            }

            public void OnBeginDrag(PointerEventData e)
            {
                if (onDragAdd == null) return; // card khóa: không kéo được
                _dragging = true;
                _start = e.position;
                onDragBegin?.Invoke(e);
            }

            public void OnDrag(PointerEventData e)
            {
                if (_dragging) onDragMove?.Invoke(e);
            }

            public void OnEndDrag(PointerEventData e)
            {
                if (!_dragging) return;
                _dragging = false;
                onDragEnd?.Invoke(e);
                if (Vector2.Distance(e.position, _start) >= dragThreshold)
                    onDragAdd?.Invoke();
            }
        }
    }
}