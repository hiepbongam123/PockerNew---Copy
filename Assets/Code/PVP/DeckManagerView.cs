using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using TMPro;
using LoRClone.Data;
using LoRClone.Model;

namespace LoRClone.View
{
    /// <summary>
    /// QUẢN LÝ DECK — màn RIÊNG để xem / sửa / xóa các deck đã tạo trong Xưởng Deck.
    ///
    /// UI tự dựng 100% bằng code khi Open() lần đầu — KHÔNG cần setup prefab/editor.
    ///
    /// Layout:
    ///   Trái  = LƯỚI các deck. Mỗi deck = 1 CARD có khung (Unit cost cao nhất, đại diện) + dải tên
    ///           deck + số lá ở đáy. Click để chọn (viền sáng lên).
    ///   Phải  = deck đang chọn: tên deck + LƯỚI CARD có khung (nội dung deck) + nút Sửa / Xóa.
    ///
    /// Lưới card dùng cardPrefab (CardView) — LobbyMenuView tự gán từ DeckBuilderView.
    /// Nếu cardPrefab để trống → fallback list/tile chữ đơn giản.
    /// </summary>
    public class DeckManagerView : MonoBehaviour
    {
        [Header("Data — LobbyMenuView tự gán nếu để trống")]
        public CardLibrary cardLibrary;
        public DeckData[] fallbackDecks;
        [Tooltip("Tham chiếu Xưởng Deck để bấm 'Sửa' mở đúng deck.")]
        public DeckBuilderView deckBuilder;

        [Header("Card Prefab — hiện card có khung (giống thư viện Xưởng Deck)")]
        [Tooltip("Kéo card prefab (có CardView) — LobbyMenuView tự gán từ DeckBuilder.\n" +
                 "Để trống → fallback tile/list chữ.")]
        public GameObject cardPrefab;
        public Vector2 cardCellSize = new Vector2(132f, 190f);

        [Header("Kích thước")]
        public float titleFontSize = 26f;
        public float headerFontSize = 16f;
        public float rowFontSize = 18f;
        public float buttonFontSize = 16f;
        public float deckTileHeight = 76f;
        public float detailRowHeight = 44f;

        [Header("Màu nền")]
        public Color overlayColor = new Color(0f, 0f, 0f, 0.92f);
        public Color panelColor = new Color(0.075f, 0.095f, 0.135f);
        public Color headerColor = new Color(0.115f, 0.150f, 0.210f);
        public Color areaColor = new Color(0.055f, 0.070f, 0.100f);

        [Header("Màu hàng")]
        public Color rowColor = new Color(0.125f, 0.165f, 0.230f);
        public Color rowHoverColor = new Color(0.190f, 0.250f, 0.350f);
        public Color rowSelectedColor = new Color(0.940f, 0.780f, 0.350f); // viền chọn (vàng)
        public Color manaBadgeColor = new Color(0.130f, 0.270f, 0.430f);

        [Header("Màu chữ / nút")]
        public Color accentColor = new Color(0.940f, 0.780f, 0.350f);
        public Color textDimColor = new Color(0.600f, 0.660f, 0.740f);
        public Color unitStripe = new Color(0.850f, 0.630f, 0.210f);
        public Color spellStripe = new Color(0.290f, 0.550f, 1.000f);
        public Color equipStripe = new Color(0.230f, 0.710f, 0.560f);
        public Color btnBlue = new Color(0.230f, 0.460f, 0.880f);
        public Color btnRed = new Color(0.770f, 0.240f, 0.240f);

        const float LeftCellExtra = 30f; // chiều cao thêm cho dải tên deck dưới card

        // ── Runtime ───────────────────────────────────────────────
        GameObject _overlay;
        RectTransform _listContent, _detailContent;
        TextMeshProUGUI _listHeader, _detailTitle, _msgLabel, _detailHint;
        GameObject _actionRow;

        Dictionary<string, CardData> _resolver;
        string _selectedName;
        readonly List<RectTransform> _tiles = new List<RectTransform>();
        readonly Dictionary<RectTransform, string> _tileDeck = new Dictionary<RectTransform, string>();
        readonly Dictionary<RectTransform, Image> _tileHighlight = new Dictionary<RectTransform, Image>();

        // ══════════════════════════════════════════════════════════
        public void Open()
        {
            if (_overlay == null) BuildUI();
            _overlay.SetActive(true);
            _resolver = CustomDeckStore.BuildResolver(cardLibrary, fallbackDecks);
            _selectedName = null;
            RefreshList();
            ClearDetail("Chọn 1 deck bên trái để xem.");
            Msg("");
        }

        public void Close()
        {
            if (_overlay != null) _overlay.SetActive(false);
        }

        void Msg(string s) { if (_msgLabel != null) _msgLabel.text = s; }

        // ── Danh sách deck (trái) ─────────────────────────────────
        void RefreshList()
        {
            ClearChildren(_listContent);
            _tiles.Clear();
            _tileDeck.Clear();
            _tileHighlight.Clear();

            var all = CustomDeckStore.LoadAll();
            if (_listHeader != null)
                _listHeader.text = $"DECK ĐÃ TẠO ({all.Count})";

            if (all.Count == 0)
            {
                SpawnPlaceholder(_listContent, "Chưa có deck nào.\nVào XƯỞNG DECK để tạo deck mới.");
                return;
            }

            foreach (var dto in all)
            {
                if (cardPrefab != null) MakeDeckCard(dto);
                else MakeDeckTile(dto);
            }

            LayoutRebuilder.ForceRebuildLayoutImmediate(_listContent);
            HighlightSelected();
        }

        // ── Deck = 1 CARD có khung (khi có cardPrefab) ────────────
        void MakeDeckCard(CustomDeckStore.DeckDTO dto)
        {
            var cell = MakeRect("Deck_" + dto.name, _listContent, Vector2.zero, Vector2.one);
            var bg = SetImage(cell, rowColor); // nền = viền quanh card + nền dải tên
            _tiles.Add(cell);
            _tileDeck[cell] = dto.name;
            _tileHighlight[cell] = bg;

            float nameFrac = LeftCellExtra / (cardCellSize.y + LeftCellExtra);

            // Card đại diện (Unit cost cao nhất)
            var champ = GetChampion(dto);
            var holder = MakeRect("Card", cell, V2(0.035f, nameFrac + 0.01f), V2(0.965f, 0.985f));
            if (champ != null)
                SpawnCardVisual(champ, holder, cardCellSize.x - 16f, cardCellSize.y - 16f);
            else
                SpawnLabel(holder, "(deck trống)", rowFontSize * 0.8f, textDimColor,
                    TextAlignmentOptions.Center, false);

            // Dải tên deck + số lá (đáy)
            var band = MakeRect("Band", cell, V2(0f, 0f), V2(1f, nameFrac));
            SetImage(band, new Color(0f, 0f, 0f, 0.55f)).raycastTarget = false;
            SpawnLabel(band, dto.name, rowFontSize * 0.78f, Color.white,
                TextAlignmentOptions.MidlineLeft, true, V2(0.06f, 0f), V2(0.72f, 1f));
            SpawnLabel(band, $"{dto.cardNames.Count} lá", rowFontSize * 0.66f, textDimColor,
                TextAlignmentOptions.MidlineRight, false, V2(0.72f, 0f), V2(0.94f, 1f));

            // Overlay click phủ toàn ô (card đã tắt raycast) → chọn deck
            var input = MakeRect("Input", cell, Vector2.zero, Vector2.one);
            var inputImg = SetImage(input, new Color(1f, 1f, 1f, 0f));
            var btn = input.gameObject.AddComponent<Button>();
            btn.targetGraphic = inputImg;
            var hc = btn.colors;
            hc.normalColor = new Color(1f, 1f, 1f, 0f);
            hc.highlightedColor = new Color(1f, 1f, 1f, 0.10f);
            hc.pressedColor = new Color(1f, 1f, 1f, 0.18f);
            btn.colors = hc;
            var name = dto.name;
            btn.onClick.AddListener(() => SelectDeck(name));
        }

        // ── Fallback tile chữ (khi chưa gán cardPrefab) ───────────
        void MakeDeckTile(CustomDeckStore.DeckDTO dto)
        {
            var tile = MakeRect("Tile_" + dto.name, _listContent, Vector2.zero, Vector2.one);
            tile.gameObject.AddComponent<LayoutElement>().preferredHeight = deckTileHeight;
            var img = SetImage(tile, rowColor);
            var btn = tile.gameObject.AddComponent<Button>();
            btn.targetGraphic = img;
            var bc = btn.colors;
            bc.normalColor = rowColor;
            bc.highlightedColor = rowHoverColor;
            bc.pressedColor = Color.Lerp(rowColor, Color.black, 0.2f);
            btn.colors = bc;
            var name = dto.name;
            btn.onClick.AddListener(() => SelectDeck(name));

            _tiles.Add(tile);
            _tileDeck[tile] = dto.name;
            _tileHighlight[tile] = img;

            var champ = GetChampion(dto);
            var cell = MakeRect("Champ", tile, V2(0.015f, 0.12f), V2(0.20f, 0.88f));
            SetImage(cell, new Color(0.03f, 0.04f, 0.06f, 1f)).raycastTarget = false;
            if (champ != null && champ.artwork != null)
            {
                var artRT = MakeRect("Art", cell, Vector2.zero, Vector2.one);
                var raw = artRT.gameObject.AddComponent<RawImage>();
                raw.texture = champ.artwork; raw.raycastTarget = false;
                ApplyFocalUV(raw, champ, 64f, deckTileHeight * 0.76f);
            }
            SpawnLabel(tile, dto.name, rowFontSize * 0.98f, Color.white,
                TextAlignmentOptions.BottomLeft, true, V2(0.23f, 0.46f), V2(0.97f, 0.86f));
            SpawnLabel(tile, $"{dto.cardNames.Count} lá", rowFontSize * 0.76f, textDimColor,
                TextAlignmentOptions.TopLeft, false, V2(0.23f, 0.14f), V2(0.97f, 0.46f));
        }

        /// <summary>Unit cost cao nhất làm "đại diện" deck (fallback: card cost cao nhất bất kỳ).</summary>
        CardData GetChampion(CustomDeckStore.DeckDTO dto)
        {
            CardData bestUnit = null, bestAny = null;
            foreach (var n in dto.cardNames)
            {
                if (!_resolver.TryGetValue(n, out var cd) || cd == null) continue;
                if (bestAny == null || cd.manaCost > bestAny.manaCost) bestAny = cd;
                if (cd.cardType == CardType.Unit && (bestUnit == null || cd.manaCost > bestUnit.manaCost))
                    bestUnit = cd;
            }
            return bestUnit ?? bestAny;
        }

        void SelectDeck(string deckName)
        {
            _selectedName = deckName;
            HighlightSelected();
            var dto = FindDeck(deckName);
            if (dto == null) { ClearDetail("Deck không tồn tại."); return; }
            RefreshDetail(dto);
        }

        void HighlightSelected()
        {
            foreach (var tile in _tiles)
            {
                if (tile == null) continue;
                bool sel = _tileDeck.TryGetValue(tile, out var n) && n == _selectedName;
                if (_tileHighlight.TryGetValue(tile, out var hi) && hi != null)
                    hi.color = sel ? rowSelectedColor : rowColor;
                var btn = tile.GetComponent<Button>();
                if (btn != null)
                {
                    var bc = btn.colors;
                    bc.normalColor = sel ? rowSelectedColor : rowColor;
                    btn.colors = bc;
                }
            }
        }

        CustomDeckStore.DeckDTO FindDeck(string name)
        {
            foreach (var d in CustomDeckStore.LoadAll())
                if (d.name == name) return d;
            return null;
        }

        void OnEditClicked()
        {
            var dto = FindDeck(_selectedName);
            if (dto == null) return;
            Close();
            if (deckBuilder != null) deckBuilder.OpenForEdit(dto);
        }

        void OnDeleteClicked()
        {
            if (string.IsNullOrEmpty(_selectedName)) return;
            string name = _selectedName;
            CustomDeckStore.DeleteDeck(name);
            _selectedName = null;
            Msg($"Đã xóa deck '{name}'.");
            RefreshList();
            ClearDetail("Chọn 1 deck bên trái để xem.");
        }

        // ── Panel phải (chi tiết deck đang chọn) ──────────────────
        void ClearDetail(string placeholder)
        {
            ClearChildren(_detailContent);
            if (_detailTitle != null) _detailTitle.text = "NỘI DUNG DECK";
            if (_detailHint != null)
            {
                _detailHint.gameObject.SetActive(true);
                _detailHint.text = placeholder;
            }
            if (_actionRow != null) _actionRow.SetActive(false);
        }

        void RefreshDetail(CustomDeckStore.DeckDTO dto)
        {
            if (_detailTitle != null) _detailTitle.text = $"{dto.name}  ({dto.cardNames.Count} lá)";
            if (_detailHint != null) _detailHint.gameObject.SetActive(false);
            if (_actionRow != null) _actionRow.SetActive(true);

            ClearChildren(_detailContent);

            var groups = new Dictionary<string, (CardData cd, int cnt)>();
            var order = new List<string>();
            foreach (var n in dto.cardNames)
            {
                if (!_resolver.TryGetValue(n, out var cd) || cd == null) continue;
                if (!groups.ContainsKey(n)) order.Add(n);
                groups.TryGetValue(n, out var g);
                groups[n] = (cd, g.cnt + 1);
            }

            if (groups.Count == 0)
            {
                SpawnPlaceholder(_detailContent, "Deck rỗng hoặc bài không còn tồn tại.");
                return;
            }

            var list = new List<(CardData cd, int cnt)>();
            foreach (var n in order) list.Add(groups[n]);
            list.Sort((a, b) => a.cd.manaCost != b.cd.manaCost
                ? a.cd.manaCost.CompareTo(b.cd.manaCost)
                : string.Compare(a.cd.cardName, b.cd.cardName, System.StringComparison.Ordinal));

            foreach (var (cd, cnt) in list)
            {
                if (cardPrefab != null) MakeCardCell(cd, cnt);
                else MakeDetailRow(cd, cnt);
            }

            LayoutRebuilder.ForceRebuildLayoutImmediate(_detailContent);
        }

        // ── LƯỚI CARD có khung ────────────────────────────────────
        void MakeCardCell(CardData card, int count)
        {
            var cell = MakeRect("Card_" + card.cardName, _detailContent, Vector2.zero, Vector2.zero);
            SpawnCardVisual(card, cell, cardCellSize.x - 6f, cardCellSize.y - 6f);
            if (count > 1)
            {
                var badge = MakeRect("CountBadge", cell, V2(0.62f, 0.84f), V2(0.99f, 0.99f));
                SetImage(badge, accentColor).raycastTarget = false;
                SpawnLabel(badge, $"x{count}", rowFontSize * 0.85f, Color.black,
                    TextAlignmentOptions.Center, true);
            }
        }

        /// <summary>
        /// Instantiate cardPrefab, Bind CardModel preview → RefreshAll dựng artwork + stats + keyword
        /// (giống hệt thumbnail thư viện Xưởng Deck). Card bị tắt tương tác.
        /// </summary>
        GameObject SpawnCardVisual(CardData card, RectTransform parent, float w, float h)
        {
            var go = Instantiate(cardPrefab, parent);
            var cv = go.GetComponent<CardView>();
            if (cv == null) return go;

            var gr = go.GetComponent<GraphicRaycaster>();
            if (gr != null) Destroy(gr);
            var cvCanvas = go.GetComponent<Canvas>();
            if (cvCanvas != null) Destroy(cvCanvas);

            cv.OverrideSize(w, h);

            var model = new CardModel(card, true);
            model.SetLocation(CardLocation.InHand);
            cv.AllowInteract = false;
            cv.Bind(model);
            cv.ForceArtworkUV(w, h);

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

            if (cv.skillsText) cv.skillsText.gameObject.SetActive(false);
            if (cv.inspectOverlay) cv.inspectOverlay.SetActive(false);

            cv.enabled = false;
            return go;
        }

        // ── Fallback list chữ (khi chưa gán cardPrefab) ───────────
        void MakeDetailRow(CardData card, int count)
        {
            var row = MakeRect("Row_" + card.cardName, _detailContent, Vector2.zero, Vector2.one);
            row.gameObject.AddComponent<LayoutElement>().preferredHeight = detailRowHeight;
            SetImage(row, rowColor);

            var stripe = MakeRect("Stripe", row, V2(0f, 0f), V2(0f, 1f));
            stripe.offsetMin = Vector2.zero; stripe.offsetMax = new Vector2(7f, 0f);
            SetImage(stripe, StripeColor(card)).raycastTarget = false;

            var mana = MakeRect("Mana", row, V2(0.025f, 0.15f), V2(0.11f, 0.85f));
            SetImage(mana, manaBadgeColor).raycastTarget = false;
            SpawnLabel(mana, card.manaCost.ToString(), rowFontSize * 0.9f, Color.white,
                TextAlignmentOptions.Center, true);

            SpawnLabel(row, card.cardName, rowFontSize * 0.9f, Color.white,
                TextAlignmentOptions.MidlineLeft, false, V2(0.135f, 0f), V2(0.82f, 1f));
            SpawnLabel(row, $"x{count}", rowFontSize * 0.9f, accentColor,
                TextAlignmentOptions.MidlineRight, true, V2(0.82f, 0f), V2(0.975f, 1f));
        }

        Color StripeColor(CardData cd) => cd.cardType switch
        {
            CardType.Unit => unitStripe,
            CardType.Equipment => equipStripe,
            _ => spellStripe,
        };

        // ══════════════════════════════════════════════════════════
        // BUILD UI (1 lần)
        // ══════════════════════════════════════════════════════════
        void BuildUI()
        {
            var parentRT = GetComponent<RectTransform>();
            var overlayRT = MakeRect("DeckManagerOverlay", parentRT, Vector2.zero, Vector2.one);
            _overlay = overlayRT.gameObject;
            var cv = _overlay.AddComponent<Canvas>();
            cv.overrideSorting = true;
            cv.sortingOrder = 590; // dưới Xưởng Deck (600) để "Sửa" mở đè lên trên
            _overlay.AddComponent<GraphicRaycaster>();
            SetImage(overlayRT, overlayColor);

            var panel = MakeRect("Panel", overlayRT, V2(0.03f, 0.04f), V2(0.97f, 0.96f));
            SetImage(panel, panelColor);

            var tbar = MakeRect("TitleBar", panel, V2(0f, 0.92f), V2(1f, 1f));
            SetImage(tbar, headerColor);
            SpawnLabel(tbar, "QUẢN LÝ DECK", titleFontSize, accentColor, TextAlignmentOptions.Center, true);
            MakeButton(MakeRect("CloseHolder", tbar, V2(0.955f, 0.12f), V2(0.995f, 0.88f)),
                "X", btnRed, Vector2.zero, Vector2.one, Close);

            // ── Trái: LƯỚI deck (mỗi deck = 1 card có khung) ──
            var left = MakeRect("ListArea", panel, V2(0.008f, 0.008f), V2(0.50f, 0.912f));
            SetImage(left, areaColor);
            var lHdr = MakeRect("Hdr", left, V2(0f, 0.94f), V2(1f, 1f));
            SetImage(lHdr, headerColor);
            _listHeader = SpawnLabel(lHdr, "DECK ĐÃ TẠO", headerFontSize, Color.white,
                TextAlignmentOptions.MidlineLeft, true, V2(0.02f, 0f), V2(0.98f, 1f));
            _listContent = BuildScrollArea("ListScroll", left, V2(0f, 0f), V2(1f, 0.93f),
                grid: cardPrefab != null,
                cellSize: new Vector2(cardCellSize.x, cardCellSize.y + LeftCellExtra));

            // ── Phải: chi tiết deck đang chọn ──
            var right = MakeRect("DetailArea", panel, V2(0.51f, 0.008f), V2(0.992f, 0.912f));
            SetImage(right, areaColor);
            var rHdr = MakeRect("Hdr", right, V2(0f, 0.94f), V2(1f, 1f));
            SetImage(rHdr, headerColor);
            _detailTitle = SpawnLabel(rHdr, "NỘI DUNG DECK", headerFontSize, Color.white,
                TextAlignmentOptions.MidlineLeft, true, V2(0.02f, 0f), V2(0.98f, 1f));

            _detailContent = BuildScrollArea("DetailScroll", right, V2(0f, 0.135f), V2(1f, 0.93f),
                grid: cardPrefab != null, cellSize: cardCellSize);

            _detailHint = SpawnLabel(right, "Chọn 1 deck bên trái để xem.",
                headerFontSize * 0.95f, textDimColor, TextAlignmentOptions.Center, false,
                V2(0.05f, 0.4f), V2(0.95f, 0.62f));

            var actRT = MakeRect("ActionRow", right, V2(0.02f, 0.02f), V2(0.98f, 0.12f));
            _actionRow = actRT.gameObject;
            MakeButton(actRT, "SỬA", btnBlue, V2(0f, 0f), V2(0.485f, 1f), OnEditClicked);
            MakeButton(actRT, "XÓA", btnRed, V2(0.515f, 0f), V2(1f, 1f), OnDeleteClicked);
            _actionRow.SetActive(false);

            _msgLabel = SpawnLabel(right, "", headerFontSize * 0.85f, accentColor,
                TextAlignmentOptions.Center, false, V2(0.02f, 0.125f), V2(0.98f, 0.135f));

            _overlay.SetActive(false);
        }

        // ── Helpers ───────────────────────────────────────────────
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

        RectTransform BuildScrollArea(string name, RectTransform parent, Vector2 ancMin, Vector2 ancMax,
            bool grid, Vector2 cellSize)
        {
            var scrollRT = MakeRect(name, parent, ancMin, ancMax);
            scrollRT.offsetMin = new Vector2(5f, 5f);
            scrollRT.offsetMax = new Vector2(-5f, -5f);
            var sr = scrollRT.gameObject.AddComponent<ScrollRect>();
            sr.horizontal = false;
            sr.movementType = ScrollRect.MovementType.Clamped;
            sr.scrollSensitivity = 25f;

            var vp = MakeRect("Viewport", scrollRT, Vector2.zero, Vector2.one);
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
                glg.cellSize = cellSize;
                glg.spacing = new Vector2(8f, 8f);
                glg.padding = new RectOffset(8, 8, 8, 8);
                glg.childAlignment = TextAnchor.UpperLeft;
            }
            else
            {
                var vlg = content.gameObject.AddComponent<VerticalLayoutGroup>();
                vlg.spacing = 5f;
                vlg.padding = new RectOffset(5, 5, 5, 5);
                vlg.childForceExpandWidth = true;
                vlg.childForceExpandHeight = false;
            }
            content.gameObject.AddComponent<ContentSizeFitter>().verticalFit =
                ContentSizeFitter.FitMode.PreferredSize;

            sr.viewport = vp;
            sr.content = content;
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

        static void ClearChildren(RectTransform content)
        {
            if (content == null) return;
            for (int i = content.childCount - 1; i >= 0; i--)
                Destroy(content.GetChild(i).gameObject);
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
    }
}