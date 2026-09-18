using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using TMPro;
using LoRClone.Data;
using LoRClone.Model;   // CardItem

namespace LoRClone.View
{
    /// <summary>
    /// ĐẤU TRƯỜNG ANH HÙNG (PvP-RPG) — VIEW RIÊNG (KHÔNG dùng lại lobby deck-picker).
    ///
    /// Khác PvP thường: KHÔNG chọn deck rời — chọn 1 CHAMPION đã nâng ở PoC, xem LOADOUT
    /// (mastery / sao / chòm sao / TRANG BỊ đang đeo / deck run) rồi mang GIỐNG HỆT sang PvP.
    ///
    /// Luồng bấm:
    ///   Trái  = danh sách champion (campaign.champions).
    ///   Phải  = loadout của champion đang chọn (đọc ProgressStore qua ChampionLoadout.BuildFromProgress).
    ///   Nút   = VÀO ĐẤU TRƯỜNG → GauntletEntry.Enter(champ) → networkLauncher.OpenAndFind().
    ///
    /// ★ QUAN TRỌNG: Open() set CardItem.SetLibrary(campaign.cardItemLibrary) TRƯỚC khi build loadout,
    ///   nếu không EquippedChampionItems() trả rỗng ở lobby (CampaignContext chưa set) → không thấy item.
    ///
    /// UI tự dựng 100% bằng code (giống các view khác). LobbyMenuView tự gán campaign + networkLauncher.
    /// </summary>
    public class ChampionArenaView : MonoBehaviour
    {
        [Header("Refs — LobbyMenuView tự gán")]
        public CampaignData campaign;
        public LoRClone.Net.NetworkLauncher networkLauncher;
        [Tooltip("Hub Con Đường Anh Hùng — nút 'SỬA DECK / TRANG BỊ' mở nó để quản lý champion (sync PoC).")]
        public RunMapView runMapView;
        [Tooltip("Card prefab (có CardView) — render champion + lá deck bằng CARD THẬT như PoC.")]
        public GameObject cardPrefab;
        [Tooltip("Kích thước card chuẩn (giống ChampionDeckBuilderView.cardCellSize).")]
        public Vector2 cardCellSize = new Vector2(132f, 190f);
        [Tooltip("Tỉ lệ thu nhỏ lá bài trong lưới 'Bộ bài đem theo' (deck 15 lá).")]
        public float deckCardScale = 0.62f;

        // ── Palette (navy + gold) ──
        static readonly Color OVERLAY = new Color(0.02f, 0.03f, 0.05f, 0.94f);
        static readonly Color PANEL = new Color(0.075f, 0.095f, 0.135f, 0.995f);
        static readonly Color HEADER = new Color(0.115f, 0.150f, 0.210f);
        static readonly Color AREA = new Color(0.055f, 0.070f, 0.100f);
        static readonly Color ROW = new Color(0.125f, 0.165f, 0.230f);
        static readonly Color ROWSEL = new Color(0.230f, 0.460f, 0.880f);
        static readonly Color GOLD = new Color(0.960f, 0.800f, 0.420f);
        static readonly Color DIM = new Color(0.600f, 0.660f, 0.740f);
        static readonly Color GREEN = new Color(0.180f, 0.660f, 0.310f);
        static readonly Color RED = new Color(0.770f, 0.240f, 0.240f);
        static readonly Color TEAL = new Color(0.300f, 0.660f, 0.600f);

        GameObject _overlay;
        RectTransform _listContent, _detail;
        TextMeshProUGUI _status;
        readonly List<(Image bg, int idx)> _rows = new List<(Image, int)>();
        int _focus = -1;
        ChampionData _focusChamp;
        ChampionLoadout _focusLo;

        // ══════════════════════════════════════════════════════════
        public void Open()
        {
            if (campaign == null || campaign.champions == null || campaign.champions.Count == 0)
            { Debug.LogWarning("[ChampionArena] Chưa gán CampaignData / không có champion."); return; }

            // ★ Áp thư viện trang bị để BuildFromProgress/preview đọc được item đã equip (local).
            //   CHỈ set override CardItem — KHÔNG đụng CampaignContext.campaign (tránh rò rỉ sang PoC).
            //   Override được HOÀN NGUYÊN khi Close() / khi vào mode khác (GauntletEntry.Leave).
            CardItem.SetLibrary(campaign.cardItemLibrary);

            if (_overlay == null) BuildUI();
            _overlay.SetActive(true);
            _overlay.transform.SetAsLastSibling();
            PopulateList();

            // Focus champion đầu tiên hợp lệ.
            int first = 0;
            for (int i = 0; i < campaign.champions.Count; i++) if (campaign.champions[i] != null) { first = i; break; }
            Focus(first);
        }

        public void Close()
        {
            if (_overlay != null) _overlay.SetActive(false);
            // HOÀN NGUYÊN override thư viện item (đặt lúc Open cho preview) → không rò rỉ sang lobby/PoC.
            // Nếu đang vào trận gauntlet: NetworkBridge.SetupMatchContext sẽ set lại trước khi áp buff.
            CardItem.SetLibrary(null);
        }

        // ── Danh sách champion (trái) ─────────────────────────────
        void PopulateList()
        {
            ClearChildren(_listContent);
            _rows.Clear();
            for (int i = 0; i < campaign.champions.Count; i++)
            {
                var champ = campaign.champions[i];
                if (champ == null) continue;
                int idx = i;

                var row = MakeRect("Champ_" + champ.championName, _listContent, Vector2.zero, Vector2.one);
                row.gameObject.AddComponent<LayoutElement>().preferredHeight = 62f;
                var bg = SetImage(row, ROW);
                bg.sprite = UISprites.RoundedRect(); bg.type = Image.Type.Sliced;
                var btn = row.gameObject.AddComponent<Button>();
                btn.targetGraphic = bg;
                btn.onClick.AddListener(() => Focus(idx));

                // Ảnh champion nhỏ (nếu có championCard.artwork)
                if (champ.championCard != null && champ.championCard.artwork != null)
                {
                    var art = MakeRect("Art", row, V2(0.02f, 0.12f), V2(0.24f, 0.88f));
                    var raw = art.gameObject.AddComponent<RawImage>();
                    raw.texture = champ.championCard.artwork; raw.raycastTarget = false;
                    ApplyFocalUV(raw, champ.championCard, 60f, 44f);
                }

                int star = ProgressStore.GetStar(champ.championName);
                int mastery = ProgressStore.GetMasteryLevel(champ.championName);
                SpawnLabel(row, champ.championName, 17f, Color.white,
                    TextAlignmentOptions.BottomLeft, true, V2(0.27f, 0.46f), V2(0.98f, 0.9f));
                SpawnLabel(row, $"<color=#E7B24B>{star} Sao</color> · Mastery {mastery}", 12.5f, DIM,
                    TextAlignmentOptions.TopLeft, false, V2(0.27f, 0.12f), V2(0.98f, 0.46f));

                _rows.Add((bg, i));
            }
            LayoutRebuilder.ForceRebuildLayoutImmediate(_listContent);
        }

        // ── Chọn 1 champion → build loadout + render chi tiết ─────
        void Focus(int idx)
        {
            _focus = idx;
            _focusChamp = (idx >= 0 && idx < campaign.champions.Count) ? campaign.champions[idx] : null;
            foreach (var (bg, i) in _rows) if (bg != null) bg.color = (i == idx) ? ROWSEL : ROW;

            if (_focusChamp == null) { ClearChildren(_detail); return; }

            // ★ Đọc loadout LOCAL đúng 1 lần (đọc item PoC đã equip).
            _focusLo = ChampionLoadout.BuildFromProgress(_focusChamp);
            RenderDetail(_focusChamp, _focusLo);
            Debug.Log("[ChampionArena] " + _focusLo.Describe());
        }

        void RenderDetail(ChampionData champ, ChampionLoadout lo)
        {
            ClearChildren(_detail);

            var items = CardItem.EquippedChampionItems(champ.championName, lo.masteryLevel);

            // Tổng buff META từ trang bị (hiển thị ở dòng tóm tắt).
            int addAtk = 0, addHp = 0, redCost = 0, nexus = 0, mana = 0;
            if (items != null)
                foreach (var it in items)
                {
                    if (it == null) continue;
                    addAtk += it.atkBonus; addHp += it.hpBonus; redCost += it.costReduction;
                    nexus += it.bonusNexus; mana += it.bonusMana;
                }

            // ══ HÀNG TRÊN: CardView champion THẬT (spawn từ cardPrefab — giống HỆT PoC) + thông tin ══
            var portraitArea = MakeRect("PortraitArea", _detail, V2(0.04f, 0.60f), V2(0.30f, 0.965f));
            SpawnChampionCard(champ, portraitArea);

            // Tên + pill chỉ số (KHÔNG dùng ký tự ★ — font TMP thiếu glyph → ra ô vuông).
            SpawnLabel(_detail, champ.championName, 30f, GOLD,
                TextAlignmentOptions.BottomLeft, true, V2(0.33f, 0.885f), V2(0.98f, 0.965f));
            Pill(_detail, V2(0.33f, 0.795f), V2(0.50f, 0.86f), $"<color=#E7B24B>{lo.star} Sao</color>");
            Pill(_detail, V2(0.512f, 0.795f), V2(0.69f, 0.86f), $"<color=#8AA4BC>Mastery {lo.masteryLevel}</color>");
            Pill(_detail, V2(0.702f, 0.795f), V2(0.92f, 0.86f), $"<color=#C89BF5>Chòm Sao {lo.constellationStars}</color>");
            // Nexus áp trong trận = nexus từ TRANG BỊ + core (Mastery + power). Giảm giá toàn quân = core.
            int nexusTotal = nexus + lo.startNexusBonus;
            int coreCost = lo.allUnitsCostReduction;
            bool hasBuff = addAtk > 0 || addHp > 0 || redCost > 0 || nexusTotal > 0 || mana > 0 || coreCost > 0;
            string buffTxt = hasBuff
                ? "Buff META: " +
                  $"<color=#E7B24B>+{addAtk} ATK · +{addHp} HP · trang bị -{redCost} mana</color>" +
                  (nexusTotal > 0 ? $" · <color=#7ED47E>+{nexusTotal} Nexus</color>" : "") +
                  (coreCost > 0 ? $" · <color=#C9A6F0>toàn quân -{coreCost} mana</color>" : "") +
                  (mana > 0 ? $" · <color=#7EC8FF>+{mana} Mana</color>" : "")
                : "Chưa có buff META — vào Con Đường Anh Hùng equip trang bị Lõi / lên Sao / Chòm Sao.";
            SpawnLabel(_detail, buffTxt, 13.5f, DIM, TextAlignmentOptions.TopLeft, false,
                V2(0.33f, 0.685f), V2(0.98f, 0.785f));

            // Tên các Sức Mạnh đang có (signaturePower + starPerks theo Sao + node Chòm Sao đã mua).
            string powerNames = ActivePowerNames(champ);
            if (!string.IsNullOrEmpty(powerNames))
                SpawnLabel(_detail, "Sức mạnh: <color=#C89BF5>" + powerNames + "</color>", 12f, DIM,
                    TextAlignmentOptions.TopLeft, false, V2(0.33f, 0.60f), V2(0.98f, 0.685f));

            // ══ CỘT TRÁI: TRANG BỊ ══
            SpawnLabel(_detail, "TRANG BỊ ĐEM VÀO PVP", 14f, TEAL,
                TextAlignmentOptions.BottomLeft, true, V2(0.045f, 0.545f), V2(0.49f, 0.595f));
            var itemBox = MakeRect("ItemBox", _detail, V2(0.045f, 0.175f), V2(0.49f, 0.545f));
            SetImage(itemBox, AREA);
            var itemContent = BuildScroll("ItemScroll", itemBox, Vector2.zero, Vector2.one);
            if (items == null || items.Count == 0)
            {
                var e = MakeRect("Empty", itemContent, Vector2.zero, Vector2.one);
                e.gameObject.AddComponent<LayoutElement>().preferredHeight = 64f;
                SpawnLabel(e, "Chưa đeo trang bị nào.\nVào Con Đường Anh Hùng → equip trang bị Lõi.",
                    12.5f, DIM, TextAlignmentOptions.Center, false);
            }
            else foreach (var it in items) if (it != null) BuildItemCard(itemContent, it);

            // ══ CỘT PHẢI: BỘ BÀI ══
            var names = lo.deckCardNames ?? new string[0];
            SpawnLabel(_detail, $"BỘ BÀI ĐEM THEO  ({names.Length} lá)", 14f, TEAL,
                TextAlignmentOptions.BottomLeft, true, V2(0.51f, 0.545f), V2(0.965f, 0.595f));
            var deckBox = MakeRect("DeckBox", _detail, V2(0.51f, 0.175f), V2(0.965f, 0.545f));
            SetImage(deckBox, AREA);
            var deckCell = cardCellSize * deckCardScale;
            var deckContent = BuildScrollGrid("DeckScroll", deckBox, Vector2.zero, Vector2.one, deckCell);
            BuildDeckCards(deckContent, champ, names, deckCell);

            // ══ NÚT: QUẢN LÝ (mở hub PoC) + VÀO TRẬN + status ══
            // Quản lý deck/trang bị/sao/chòm sao ở CON ĐƯỜNG ANH HÙNG (1 nguồn dữ liệu — sync tuyệt đối).
            var mgRT = MakeRect("ManageBtn", _detail, V2(0.055f, 0.05f), V2(0.45f, 0.145f));
            MakeBtn(mgRT, TEAL, "SỬA DECK / TRANG BỊ\n(Con Đường Anh Hùng)", 13f, OnManageChampion);

            var goRT = MakeRect("EnterBtn", _detail, V2(0.50f, 0.05f), V2(0.945f, 0.145f));
            MakeBtn(goRT, GREEN, "VÀO ĐẤU TRƯỜNG", 20f, OnEnterArena);

            _status = SpawnLabel(_detail, "", 12.5f, GOLD, TextAlignmentOptions.Center, false,
                V2(0.055f, 0.004f), V2(0.945f, 0.045f));
        }

        // Tên các Sức Mạnh ĐANG hiệu lực: signaturePower + starPerks (theo Sao) + node Chòm Sao đã mua.
        string ActivePowerNames(ChampionData champ)
        {
            if (champ == null) return "";
            var names = new List<string>();
            void Add(RunPower p) { if (p != null && p.IsMeaningful && !string.IsNullOrEmpty(p.name)) names.Add(p.name); }

            Add(champ.signaturePower);
            int star = ProgressStore.GetStar(champ.championName);
            if (champ.starPerks != null)
                for (int st = 2; st <= star; st++)
                {
                    int i = st - 2;
                    if (i >= 0 && i < champ.starPerks.Count) Add(champ.starPerks[i]);
                }
            if (champ.constellation != null)
                for (int i = 0; i < champ.constellation.Count; i++)
                    if (ProgressStore.IsConstellationBought(champ.championName, i) && champ.constellation[i] != null)
                        Add(champ.constellation[i].effect);

            return names.Count > 0 ? string.Join(" · ", names) : "";
        }

        // Mở THẲNG màn CHỌN/CHỈNH TƯỚNG cho champion đang chọn (KHÔNG world map, KHÔNG node map).
        // Dùng RunMapView.OpenChampionEditor(name) — luồng chuyên biệt để sửa deck / thay trang bị.
        // Sửa xong quay lại Đấu Trường: Open()→Focus() build lại loadout từ ProgressStore/ChampionDeckStore mới.
        void OnManageChampion()
        {
            if (_focusChamp == null) { Say("Hãy chọn 1 champion."); return; }
            if (runMapView == null) { Say("Thiếu RunMapView (LobbyMenuView chưa gán)."); return; }
            Close();
            runMapView.OpenChampionEditor(_focusChamp.championName);
        }

        // ── 1 thẻ trang bị: hex icon (màu độ hiếm) + tên + tag hiếm + mô tả ──
        void BuildItemCard(RectTransform parent, CardItem it)
        {
            var row = MakeRect("Item_" + it.itemName, parent, Vector2.zero, Vector2.one);
            row.gameObject.AddComponent<LayoutElement>().preferredHeight = 56f;
            var bg = SetImage(row, ROW); bg.sprite = UISprites.RoundedRect(); bg.type = Image.Type.Sliced; bg.raycastTarget = false;

            var hex = MakeRect("Hex", row, V2(0f, 0.5f), V2(0f, 0.5f));
            hex.pivot = V2(0f, 0.5f); hex.anchoredPosition = new Vector2(9f, 0f); hex.sizeDelta = new Vector2(42f, 42f);
            var himg = SetImage(hex, RarityColor(it.rarity)); himg.sprite = HexSprite(); himg.raycastTarget = false;
            if (it.icon != null)
            {
                var ic = MakeRect("ic", hex, V2(0.22f, 0.22f), V2(0.78f, 0.78f));
                var iimg = ic.gameObject.AddComponent<Image>();
                iimg.sprite = it.icon; iimg.preserveAspect = true; iimg.raycastTarget = false;
            }
            else SpawnLabel(hex, "◆", 18f, new Color(0.10f, 0.11f, 0.14f), TextAlignmentOptions.Center, true);

            SpawnLabel(row, $"<b>{it.itemName}</b>", 14.5f, Color.white,
                TextAlignmentOptions.BottomLeft, true, V2(0.155f, 0.48f), V2(0.78f, 0.98f));
            SpawnLabel(row, RarityVN(it.rarity), 11f, RarityColor(it.rarity),
                TextAlignmentOptions.BottomRight, true, V2(0.70f, 0.48f), V2(0.985f, 0.98f));
            SpawnLabel(row, it.Describe(), 12.5f, GOLD,
                TextAlignmentOptions.TopLeft, false, V2(0.155f, 0.04f), V2(0.985f, 0.5f));
        }

        // ── Chip lá bài: artwork + badge mana + tên + đếm số bản (xN) ──
        void BuildDeckCards(RectTransform parent, ChampionData champ, string[] names, Vector2 cell)
        {
            var map = BuildCardMap(champ);
            var order = new List<string>();
            var cnt = new Dictionary<string, int>();
            foreach (var n in names)
            {
                if (string.IsNullOrEmpty(n)) continue;
                if (!cnt.ContainsKey(n)) { cnt[n] = 0; order.Add(n); }
                cnt[n]++;
            }
            if (order.Count == 0)
            {
                var e = MakeRect("Empty", parent, Vector2.zero, Vector2.one);
                SpawnLabel(e, "—", 12f, DIM, TextAlignmentOptions.Center, false);
                return;
            }
            foreach (var n in order) { map.TryGetValue(n, out var cd); BuildDeckCell(parent, n, cd, cnt[n], cell); }
        }

        // 1 ô deck = CardView THẬT (mini) + badge số bản (xN). Chưa resolve được → chip chữ.
        void BuildDeckCell(RectTransform parent, string cardName, CardData cd, int copies, Vector2 cell)
        {
            var cellRT = MakeRect("Cell_" + cardName, parent, Vector2.zero, Vector2.zero); // grid tự set size

            if (cd != null && cardPrefab != null)
                SpawnCardView(cd, cellRT, cell.x - 4f, cell.y - 4f);
            else
            {
                var bg = SetImage(cellRT, new Color(0.10f, 0.12f, 0.18f, 1f));
                bg.sprite = UISprites.RoundedRect(); bg.type = Image.Type.Sliced;
                var nl = SpawnLabel(cellRT, cardName, 8.5f, Color.white, TextAlignmentOptions.Center, true);
                nl.textWrappingMode = TextWrappingModes.Normal; nl.overflowMode = TextOverflowModes.Ellipsis;
            }

            if (copies > 1)
            {
                var q = MakeRect("Qty", cellRT, V2(0.60f, 0.82f), V2(0.99f, 0.99f));
                var qimg = SetImage(q, new Color(0.85f, 0.62f, 0.28f, 0.98f)); qimg.raycastTarget = false;
                qimg.sprite = UISprites.RoundedRect(); qimg.type = Image.Type.Sliced;
                SpawnLabel(q, "x" + copies, 10f, new Color(0.10f, 0.09f, 0.06f), TextAlignmentOptions.Center, true);
            }
        }

        // Gom mọi CardData khả dụng → map theo tên (để resolve deckCardNames ra artwork/mana).
        Dictionary<string, CardData> BuildCardMap(ChampionData champ)
        {
            var m = new Dictionary<string, CardData>();
            void Add(CardData c) { if (c != null && !string.IsNullOrEmpty(c.cardName) && !m.ContainsKey(c.cardName)) m[c.cardName] = c; }

            // Nguồn CHÍNH: catalog toàn cục (Lobby đẩy vào — cùng nguồn NetworkBridge dùng) → resolve mọi lá.
            var lib = LoRClone.GameConfig.cardLibrary;
            if (lib != null && lib.allCards != null) foreach (var c in lib.allCards) Add(c);

            if (champ != null)
            {
                Add(champ.championCard);
                if (champ.starterDeck != null && champ.starterDeck.cards != null)
                    foreach (var c in champ.starterDeck.cards) Add(c);
            }
            if (campaign != null)
            {
                if (campaign.starterCards != null) foreach (var c in campaign.starterCards) Add(c);
                if (campaign.chestCardPool != null) foreach (var c in campaign.chestCardPool) Add(c);
                if (campaign.champions != null)
                    foreach (var ch in campaign.champions)
                    {
                        if (ch == null) continue;
                        Add(ch.championCard);
                        if (ch.starterDeck != null && ch.starterDeck.cards != null)
                            foreach (var c in ch.starterDeck.cards) Add(c);
                    }
            }
            return m;
        }

        // ── CardView THẬT từ cardPrefab — DÙNG CHUNG (champion + lá deck) ──
        // MẤU CHỐT: RENDER ở size THIẾT KẾ (cardCellSize, nơi CardView bố trí đẹp như PoC),
        // rồi localScale ĐỒNG ĐỀU để vừa khung (boxW×boxH). KHÔNG OverrideSize xuống size nhỏ
        // — font/anchor của CardView là px tuyệt đối, thu nhỏ rect sẽ tràn chữ (nát như cũ).
        GameObject SpawnCardView(CardData card, RectTransform parent, float boxW, float boxH)
        {
            var go = Instantiate(cardPrefab, parent);
            var cv = go.GetComponent<CardView>();
            if (cv == null) return go;

            var gr = go.GetComponent<GraphicRaycaster>(); if (gr != null) Destroy(gr);
            var cvCanvas = go.GetComponent<Canvas>(); if (cvCanvas != null) Destroy(cvCanvas);

            cv.OverrideSize(cardCellSize.x, cardCellSize.y);   // render ở size CHUẨN
            var model = new CardModel(card, true);
            model.SetLocation(CardLocation.InHand);            // hiện tên/mana/atk/hp như lá trên tay
            cv.AllowInteract = false;
            cv.Bind(model);                                    // RefreshAll: artwork + hex + keyword
            cv.ForceArtworkUV(cardCellSize.x, cardCellSize.y);

            if (cv.skillsText) cv.skillsText.gameObject.SetActive(false);
            if (cv.inspectOverlay) cv.inspectOverlay.SetActive(false);
            cv.enabled = false;

            // Scale đồng đều để vừa khung — giữ nguyên tỉ lệ + độ nét như card gameplay.
            float s = Mathf.Min(boxW / cardCellSize.x, boxH / cardCellSize.y);
            var rt = (RectTransform)go.transform;
            rt.anchorMin = rt.anchorMax = rt.pivot = V2(0.5f, 0.5f);
            rt.anchoredPosition = Vector2.zero;
            rt.localScale = Vector3.one * s;
            return go;
        }

        // Card champion (to) — canh giữa khung, giữ tỉ lệ card.
        void SpawnChampionCard(ChampionData champ, RectTransform area)
        {
            if (champ == null || champ.championCard == null) return;

            LayoutRebuilder.ForceRebuildLayoutImmediate(area);
            float availW = Mathf.Max(area.rect.width, 1f);
            float availH = Mathf.Max(area.rect.height, 1f);
            float aspect = cardCellSize.x / Mathf.Max(cardCellSize.y, 1f); // w/h chuẩn card (132:190)
            float h = availH * 0.98f, w = h * aspect;
            if (w > availW * 0.98f) { w = availW * 0.98f; h = w / aspect; }

            if (cardPrefab != null) { SpawnCardView(champ.championCard, area, w, h); return; }

            // Fallback khi chưa gán prefab: artwork đơn giản.
            var p = MakeRect("PortraitFallback", area, V2(0.5f, 0.5f), V2(0.5f, 0.5f));
            p.sizeDelta = new Vector2(w, h);
            var oimg = SetImage(p, GOLD); oimg.sprite = UISprites.RoundedRect(); oimg.type = Image.Type.Sliced;
            var inner = MakeRect("Inner", p, Vector2.zero, Vector2.one);
            inner.offsetMin = new Vector2(3f, 3f); inner.offsetMax = new Vector2(-3f, -3f);
            SetImage(inner, new Color(0.03f, 0.04f, 0.06f, 1f));
            if (champ.championCard.artwork != null)
            {
                var raw = MakeRect("Art", inner, Vector2.zero, Vector2.one).gameObject.AddComponent<RawImage>();
                raw.texture = champ.championCard.artwork; raw.raycastTarget = false;
                ApplyFocalUV(raw, champ.championCard, w, h);
            }
        }

        // Pill bo góc + label rich-text căn giữa.
        void Pill(RectTransform parent, Vector2 aMin, Vector2 aMax, string richText)
        {
            var p = MakeRect("Pill", parent, aMin, aMax);
            var img = SetImage(p, new Color(0.16f, 0.22f, 0.32f, 1f));
            img.sprite = UISprites.RoundedRect(); img.type = Image.Type.Sliced; img.raycastTarget = false;
            SpawnLabel(p, richText, 12.5f, Color.white, TextAlignmentOptions.Center, true);
        }

        static Color RarityColor(ItemRarity r) =>
            r == ItemRarity.Epic ? new Color(0.85f, 0.62f, 0.28f) :
            r == ItemRarity.Rare ? new Color(0.36f, 0.60f, 0.90f) :
                                   new Color(0.66f, 0.69f, 0.73f);

        static string RarityVN(ItemRarity r) =>
            r == ItemRarity.Epic ? "Sử Thi" : r == ItemRarity.Rare ? "Hiếm" : "Thường";

        static Sprite _hexSprite;
        static Sprite HexSprite()
        {
            if (_hexSprite != null) return _hexSprite;
            int s = 64; float c = (s - 1) * 0.5f; float ap = c - 2f;
            const float k = 0.8660254f;
            var tex = new Texture2D(s, s, TextureFormat.RGBA32, false)
            { filterMode = FilterMode.Bilinear, wrapMode = TextureWrapMode.Clamp, name = "ArenaHex" };
            var px = new Color32[s * s];
            for (int yy = 0; yy < s; yy++)
                for (int xx = 0; xx < s; xx++)
                {
                    float dx = xx - c, dy = yy - c;
                    float p1 = Mathf.Abs(dy);
                    float p2 = Mathf.Abs(dx * k + dy * 0.5f);
                    float p3 = Mathf.Abs(-dx * k + dy * 0.5f);
                    float margin = ap - Mathf.Max(p1, Mathf.Max(p2, p3));
                    px[yy * s + xx] = new Color32(255, 255, 255, (byte)(Mathf.Clamp01(margin + 0.5f) * 255f));
                }
            tex.SetPixels32(px); tex.Apply();
            _hexSprite = Sprite.Create(tex, new Rect(0, 0, s, s), new Vector2(0.5f, 0.5f), 100f);
            return _hexSprite;
        }

        // Scroll dạng LƯỚI (grid) — cho dải lá bài.
        RectTransform BuildScrollGrid(string name, RectTransform parent, Vector2 aMin, Vector2 aMax, Vector2 cell)
        {
            var scrollRT = MakeRect(name, parent, aMin, aMax);
            scrollRT.offsetMin = new Vector2(5f, 5f); scrollRT.offsetMax = new Vector2(-5f, -5f);
            var sr = scrollRT.gameObject.AddComponent<ScrollRect>();
            sr.horizontal = false; sr.movementType = ScrollRect.MovementType.Clamped; sr.scrollSensitivity = 25f;

            var vp = MakeRect("Viewport", scrollRT, Vector2.zero, Vector2.one);
            vp.gameObject.AddComponent<Image>().color = Color.white;
            vp.gameObject.AddComponent<Mask>().showMaskGraphic = false;

            var content = MakeRect("Content", vp, V2(0f, 1f), V2(1f, 1f));
            content.pivot = V2(0.5f, 1f);
            content.offsetMin = new Vector2(0f, -300f); content.offsetMax = Vector2.zero;
            var grid = content.gameObject.AddComponent<GridLayoutGroup>();
            grid.cellSize = cell; grid.spacing = new Vector2(6f, 6f);
            grid.padding = new RectOffset(6, 6, 6, 6); grid.childAlignment = TextAnchor.UpperCenter;
            content.gameObject.AddComponent<ContentSizeFitter>().verticalFit = ContentSizeFitter.FitMode.PreferredSize;

            sr.viewport = vp; sr.content = content;
            return content;
        }

        void OnEnterArena()
        {
            if (_focusChamp == null) { Say("Hãy chọn 1 champion."); return; }
            if (_focusLo == null || _focusLo.deckCardNames == null || _focusLo.deckCardNames.Length == 0)
            { Say("Champion này chưa có deck run (gán starterDeck hoặc lưu deck champion trước)."); return; }

            // GIỮ library sống xuyên trận (cho icon trang bị ở máy này) — trước khi Close() thả override.
            GauntletEntry.Library = campaign.cardItemLibrary;

            // Set loadout + deck (từ CHÍNH loadout — 1 nguồn), đánh dấu mode gauntlet.
            GauntletEntry.Enter(_focusChamp);

            Close();
            if (networkLauncher != null) networkLauncher.OpenAndFind();
            else Say("Thiếu NetworkLauncher.");
        }

        void Say(string s) { if (_status != null) _status.text = s; Debug.Log("[ChampionArena] " + s); }

        // ══════════════════════════════════════════════════════════
        // BUILD UI (1 lần)
        // ══════════════════════════════════════════════════════════
        void BuildUI()
        {
            var go = new GameObject("ChampionArenaCanvas");
            var canvas = go.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 620; // trên lobby menu (400)
            go.AddComponent<GraphicRaycaster>();
            EnsureEventSystem();
            _overlay = go;

            var root = go.GetComponent<RectTransform>();
            var dim = MakeRect("Dim", root, Vector2.zero, Vector2.one);
            SetImage(dim, OVERLAY);

            var panel = MakeRect("Panel", dim, V2(0.06f, 0.06f), V2(0.94f, 0.94f));
            SetImage(panel, PANEL);

            var tbar = MakeRect("TitleBar", panel, V2(0f, 0.92f), V2(1f, 1f));
            SetImage(tbar, HEADER);
            SpawnLabel(tbar, "ĐẤU TRƯỜNG ANH HÙNG", 24f, GOLD, TextAlignmentOptions.Center, true);
            MakeBtn(MakeRect("Close", tbar, V2(0.955f, 0.12f), V2(0.995f, 0.88f)), RED, "X", 16f, Close);

            // Trái: danh sách champion
            var left = MakeRect("ListArea", panel, V2(0.008f, 0.008f), V2(0.34f, 0.912f));
            SetImage(left, AREA);
            var lHdr = MakeRect("Hdr", left, V2(0f, 0.94f), V2(1f, 1f));
            SetImage(lHdr, HEADER);
            SpawnLabel(lHdr, "CHỌN TƯỚNG", 15f, Color.white, TextAlignmentOptions.MidlineLeft, true,
                V2(0.04f, 0f), V2(0.98f, 1f));
            _listContent = BuildScroll("ListScroll", left, V2(0f, 0f), V2(1f, 0.93f));

            // Phải: chi tiết loadout
            var right = MakeRect("DetailArea", panel, V2(0.35f, 0.008f), V2(0.992f, 0.912f));
            SetImage(right, PANEL);
            _detail = right;

            _overlay.SetActive(false);
        }

        RectTransform BuildScroll(string name, RectTransform parent, Vector2 aMin, Vector2 aMax)
        {
            var scrollRT = MakeRect(name, parent, aMin, aMax);
            scrollRT.offsetMin = new Vector2(5f, 5f); scrollRT.offsetMax = new Vector2(-5f, -5f);
            var sr = scrollRT.gameObject.AddComponent<ScrollRect>();
            sr.horizontal = false; sr.movementType = ScrollRect.MovementType.Clamped; sr.scrollSensitivity = 25f;

            var vp = MakeRect("Viewport", scrollRT, Vector2.zero, Vector2.one);
            vp.gameObject.AddComponent<Image>().color = Color.white;
            vp.gameObject.AddComponent<Mask>().showMaskGraphic = false;

            var content = MakeRect("Content", vp, V2(0f, 1f), V2(1f, 1f));
            content.pivot = V2(0.5f, 1f);
            content.offsetMin = new Vector2(0f, -300f); content.offsetMax = Vector2.zero;
            var vlg = content.gameObject.AddComponent<VerticalLayoutGroup>();
            vlg.spacing = 4f; vlg.padding = new RectOffset(4, 4, 4, 4);
            vlg.childForceExpandWidth = true; vlg.childForceExpandHeight = false;
            content.gameObject.AddComponent<ContentSizeFitter>().verticalFit = ContentSizeFitter.FitMode.PreferredSize;

            sr.viewport = vp; sr.content = content;
            return content;
        }

        // ── Helpers ────────────────────────────────────────────────
        static void EnsureEventSystem()
        {
            if (FindFirstObjectByType<UnityEngine.EventSystems.EventSystem>() == null)
                new GameObject("EventSystem",
                    typeof(UnityEngine.EventSystems.EventSystem),
                    typeof(UnityEngine.EventSystems.StandaloneInputModule));
        }

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

        void MakeBtn(RectTransform rt, Color color, string label, float fontSize, UnityEngine.Events.UnityAction onClick)
        {
            var img = SetImage(rt, color);
            img.sprite = UISprites.RoundedRect(); img.type = Image.Type.Sliced;
            var btn = rt.gameObject.AddComponent<Button>();
            btn.targetGraphic = img;
            var bc = btn.colors;
            bc.normalColor = color;
            bc.highlightedColor = Color.Lerp(color, Color.white, 0.22f);
            bc.pressedColor = Color.Lerp(color, Color.black, 0.22f);
            btn.colors = bc;
            btn.onClick.AddListener(onClick);
            SpawnLabel(rt, label, fontSize, Color.white, TextAlignmentOptions.Center, true);
        }

        static void ClearChildren(RectTransform content)
        {
            if (content == null) return;
            for (int i = content.childCount - 1; i >= 0; i--) Destroy(content.GetChild(i).gameObject);
        }

        static TextMeshProUGUI SpawnLabel(RectTransform parent, string text, float size,
            Color color, TextAlignmentOptions align, bool bold,
            Vector2? aMin = null, Vector2? aMax = null)
        {
            var go = new GameObject("Label");
            go.transform.SetParent(parent, false);
            var rt = go.AddComponent<RectTransform>();
            rt.anchorMin = aMin ?? Vector2.zero;
            rt.anchorMax = aMax ?? Vector2.one;
            rt.offsetMin = new Vector2(3, 2); rt.offsetMax = new Vector2(-3, -2);
            var tmp = go.AddComponent<TextMeshProUGUI>();
            tmp.text = text; tmp.fontSize = size; tmp.color = color; tmp.alignment = align;
            tmp.fontStyle = bold ? FontStyles.Bold : FontStyles.Normal;
            tmp.raycastTarget = false; tmp.richText = true; tmp.textWrappingMode = TextWrappingModes.Normal;
            return tmp;
        }

        static RectTransform MakeRect(string name, RectTransform parent, Vector2 aMin, Vector2 aMax)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            var rt = go.AddComponent<RectTransform>();
            rt.anchorMin = aMin; rt.anchorMax = aMax; rt.offsetMin = rt.offsetMax = Vector2.zero;
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