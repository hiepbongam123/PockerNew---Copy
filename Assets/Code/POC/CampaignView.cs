using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.SceneManagement;
using TMPro;
using LoRClone.Data;
using LoRClone.Model;

namespace LoRClone.View
{
    /// <summary>
    /// MÀN LEO THÁP (PvE kiểu PvZ + Path of Champions).
    ///
    /// Trái  = LƯỚI tầng, mỗi tầng = 1 CARD BOSS đại diện (✔ đã thắng / ▶ đang mở / 🔒 khóa).
    /// Phải  = chọn tầng xong → CHỌN BÀI: kho đã mở khóa (starterCards + quà tầng đã thắng),
    ///         chọn đúng deckPickCount lá (được nhiều bản 1 lá) → VÀO TRẬN.
    ///
    /// Máu Nexus mỗi tầng do LevelData.playerHealth/enemyHealth quyết định
    /// (CampaignResultWatcher áp trong game scene).
    ///
    /// UI tự dựng runtime. LobbyMenuView tự thêm component + gán campaign/cardPrefab.
    /// </summary>
    public class CampaignView : MonoBehaviour
    {
        [Header("Refs")]
        public LobbyManager lobby;
        public CampaignData campaign;

        [Header("Card Prefab — LobbyMenuView tự gán từ DeckBuilder")]
        public GameObject cardPrefab;
        public Vector2 cardCellSize = new Vector2(132f, 190f);

        [Header("Kích thước")]
        public float titleFontSize = 26f;
        public float headerFontSize = 16f;
        public float rowFontSize = 18f;

        [Header("Màu — đồng bộ theme Xưởng Deck")]
        public Color overlayColor = new Color(0f, 0f, 0f, 0.92f);
        public Color panelColor = new Color(0.075f, 0.095f, 0.135f);
        public Color headerColor = new Color(0.115f, 0.150f, 0.210f);
        public Color areaColor = new Color(0.055f, 0.070f, 0.100f);
        public Color rowColor = new Color(0.125f, 0.165f, 0.230f);
        public Color rowSelectedColor = new Color(0.940f, 0.780f, 0.350f);
        public Color accentColor = new Color(0.940f, 0.780f, 0.350f);
        public Color textDimColor = new Color(0.600f, 0.660f, 0.740f);
        public Color doneColor = new Color(0.350f, 0.850f, 0.480f);
        public Color manaBadgeColor = new Color(0.130f, 0.270f, 0.430f);
        public Color btnGreen = new Color(0.180f, 0.660f, 0.310f);
        public Color btnRed = new Color(0.770f, 0.240f, 0.240f);

        const float LeftCellExtra = 34f; // dải trạng thái + tên tầng dưới boss card

        // ── Runtime ───────────────────────────────────────────────
        GameObject _overlay;
        RectTransform _levelContent, _poolContent;
        TextMeshProUGUI _detailTitle, _detailBody, _counterLabel, _msgLabel, _poolHint;
        Button _startBtn;
        TextMeshProUGUI _startLabel;

        int _selectedLevel = -1;
        int _pickTarget = 1;
        int _maxCopies = 3;
        readonly List<CardData> _pool = new List<CardData>();
        readonly Dictionary<CardData, int> _picks = new Dictionary<CardData, int>();
        readonly List<RectTransform> _levelTiles = new List<RectTransform>();
        readonly Dictionary<RectTransform, int> _tileLevel = new Dictionary<RectTransform, int>();
        readonly Dictionary<RectTransform, Image> _tileHighlight = new Dictionary<RectTransform, Image>();

        // ══════════════════════════════════════════════════════════
        public void Open()
        {
            if (campaign == null || campaign.levels == null || campaign.levels.Count == 0)
            {
                Debug.LogWarning("[CampaignView] Chưa gán CampaignData / campaign trống.");
                return;
            }
            if (_overlay == null) BuildUI();
            _overlay.SetActive(true);

            _maxCopies = Mathf.Max(1, campaign.maxCopiesPerCard);
            BuildPool();

            // Auto chọn tầng đang mở cao nhất
            int auto = Mathf.Clamp(ProgressStore.HighestCompleted + 1, 0, campaign.levels.Count - 1);
            RefreshLevels();
            SelectLevel(auto);
        }

        public void Close() { if (_overlay != null) _overlay.SetActive(false); }

        /// <summary>
        /// Mốc 2 (bản đồ nhánh): mở thẳng màn CHỌN BÀI cho 1 tầng do node bản đồ chỉ định,
        /// bỏ qua khóa ProgressStore (map tự quản đường đi). Nhớ nodeId để watcher đánh dấu done khi thắng.
        /// </summary>
        public void OpenAtLevel(int levelIndex, int nodeId)
        {
            if (campaign == null || campaign.levels == null || campaign.levels.Count == 0) return;
            CampaignContext.currentNodeId = nodeId;
            if (_overlay == null) BuildUI();
            _overlay.SetActive(true);
            _maxCopies = Mathf.Max(1, campaign.maxCopiesPerCard);
            BuildPool();
            RefreshLevels();
            SelectLevel(Mathf.Clamp(levelIndex, 0, campaign.levels.Count - 1), ignoreLock: true);
        }

        void Msg(string s) { if (_msgLabel != null) _msgLabel.text = s; }

        // ── KHO bài đã mở khóa ────────────────────────────────────
        void BuildPool()
        {
            _pool.Clear();
            var seen = new HashSet<CardData>();
            void Add(CardData c)
            {
                if (c == null || c.isGenerated) return;
                if (seen.Add(c)) _pool.Add(c);
            }

            if (campaign.starterCards != null)
                foreach (var c in campaign.starterCards) Add(c);

            for (int i = 0; i < campaign.levels.Count; i++)
            {
                if (!ProgressStore.IsLevelCompleted(i)) continue;
                var lv = campaign.levels[i];
                if (lv != null && lv.rewardCards != null)
                    foreach (var c in lv.rewardCards) Add(c);
            }

            _pool.Sort((a, b) => a.manaCost != b.manaCost
                ? a.manaCost.CompareTo(b.manaCost)
                : string.Compare(a.cardName, b.cardName, System.StringComparison.Ordinal));
        }

        // ── Lưới tầng (trái) ──────────────────────────────────────
        void RefreshLevels()
        {
            ClearChildren(_levelContent);
            _levelTiles.Clear();
            _tileLevel.Clear();
            _tileHighlight.Clear();

            for (int i = 0; i < campaign.levels.Count; i++)
            {
                if (campaign.levels[i] == null) continue;
                if (cardPrefab != null) MakeLevelCard(i);
                else MakeLevelRow(i);
            }
            LayoutRebuilder.ForceRebuildLayoutImmediate(_levelContent);
            HighlightSelectedLevel();
        }

        void MakeLevelCard(int idx)
        {
            var lv = campaign.levels[idx];
            bool done = ProgressStore.IsLevelCompleted(idx);
            bool unlocked = ProgressStore.IsLevelUnlocked(idx);

            var cell = MakeRect("Lv_" + idx, _levelContent, Vector2.zero, Vector2.one);
            var bg = SetImage(cell, rowColor);
            _levelTiles.Add(cell);
            _tileLevel[cell] = idx;
            _tileHighlight[cell] = bg;

            float nameFrac = LeftCellExtra / (cardCellSize.y + LeftCellExtra);

            var boss = GetBoss(lv);
            var holder = MakeRect("Card", cell, V2(0.035f, nameFrac + 0.01f), V2(0.965f, 0.985f));
            if (boss != null)
                SpawnCardVisual(boss, holder, cardCellSize.x - 16f, cardCellSize.y - 16f);
            else
                SpawnLabel(holder, lv.enemyDeck != null ? lv.enemyDeck.deckName : "(chưa gán boss)",
                    rowFontSize * 0.8f, textDimColor, TextAlignmentOptions.Center, false);

            // Khóa → dim + ổ khóa
            if (!unlocked)
            {
                var dim = MakeRect("Lock", holder, Vector2.zero, Vector2.one);
                SetImage(dim, new Color(0f, 0f, 0f, 0.74f)).raycastTarget = false;
                SpawnLabel(dim, "🔒", rowFontSize * 1.6f, Color.white, TextAlignmentOptions.Center, true);
            }

            // Dải trạng thái + tên tầng
            var band = MakeRect("Band", cell, V2(0f, 0f), V2(1f, nameFrac));
            SetImage(band, new Color(0f, 0f, 0f, 0.6f)).raycastTarget = false;
            string status = done ? "<color=#59D97A>✔</color>" : unlocked ? "<color=#F0C75A>▶</color>" : "<color=#8090A0>🔒</color>";
            SpawnLabel(band, $"{status} T{idx + 1} — {lv.levelName}", rowFontSize * 0.72f, Color.white,
                TextAlignmentOptions.MidlineLeft, true, V2(0.05f, 0f), V2(0.98f, 1f));

            if (unlocked)
            {
                var input = MakeRect("Input", cell, Vector2.zero, Vector2.one);
                var inputImg = SetImage(input, new Color(1f, 1f, 1f, 0f));
                var btn = input.gameObject.AddComponent<Button>();
                btn.targetGraphic = inputImg;
                var hc = btn.colors;
                hc.normalColor = new Color(1f, 1f, 1f, 0f);
                hc.highlightedColor = new Color(1f, 1f, 1f, 0.10f);
                hc.pressedColor = new Color(1f, 1f, 1f, 0.18f);
                btn.colors = hc;
                int captured = idx;
                btn.onClick.AddListener(() => SelectLevel(captured));
            }
        }

        void MakeLevelRow(int idx)
        {
            var lv = campaign.levels[idx];
            bool done = ProgressStore.IsLevelCompleted(idx);
            bool unlocked = ProgressStore.IsLevelUnlocked(idx);

            var row = MakeRect("Lv_" + idx, _levelContent, Vector2.zero, Vector2.one);
            row.gameObject.AddComponent<LayoutElement>().preferredHeight = 56f;
            var img = SetImage(row, rowColor);
            _levelTiles.Add(row); _tileLevel[row] = idx; _tileHighlight[row] = img;
            if (unlocked)
            {
                var btn = row.gameObject.AddComponent<Button>();
                btn.targetGraphic = img;
                int captured = idx;
                btn.onClick.AddListener(() => SelectLevel(captured));
            }
            string status = done ? "<color=#59D97A>✔</color>" : unlocked ? "<color=#F0C75A>▶</color>" : "<color=#8090A0>🔒</color>";
            SpawnLabel(row, status, rowFontSize, Color.white, TextAlignmentOptions.Center, true, V2(0.02f, 0f), V2(0.12f, 1f));
            SpawnLabel(row, $"Tầng {idx + 1} — {lv.levelName}", rowFontSize,
                unlocked ? Color.white : textDimColor, TextAlignmentOptions.MidlineLeft, true,
                V2(0.14f, 0f), V2(0.98f, 1f));
        }

        CardData GetBoss(LevelData lv)
        {
            if (lv.bossCard != null) return lv.bossCard;
            if (lv.enemyDeck == null || lv.enemyDeck.cards == null) return null;
            CardData bestUnit = null, bestAny = null;
            foreach (var c in lv.enemyDeck.cards)
            {
                if (c == null) continue;
                if (bestAny == null || c.manaCost > bestAny.manaCost) bestAny = c;
                if (c.cardType == CardType.Unit && (bestUnit == null || c.manaCost > bestUnit.manaCost))
                    bestUnit = c;
            }
            return bestUnit ?? bestAny;
        }

        void HighlightSelectedLevel()
        {
            foreach (var tile in _levelTiles)
            {
                if (tile == null) continue;
                bool sel = _tileLevel.TryGetValue(tile, out var i) && i == _selectedLevel;
                if (_tileHighlight.TryGetValue(tile, out var hi) && hi != null)
                    hi.color = sel ? rowSelectedColor : rowColor;
            }
        }

        // ── Chọn tầng → mở phần chọn bài ──────────────────────────
        void SelectLevel(int idx, bool ignoreLock = false)
        {
            if (idx < 0 || idx >= campaign.levels.Count) return;
            if (!ignoreLock && !ProgressStore.IsLevelUnlocked(idx)) return;
            _selectedLevel = idx;
            _pickTarget = Mathf.Max(1, campaign.levels[idx].deckPickCount);
            _picks.Clear();
            Msg("");
            HighlightSelectedLevel();
            RefreshDetail();
            RefreshPool();
        }

        void RefreshDetail()
        {
            var lv = campaign.levels[_selectedLevel];
            bool done = ProgressStore.IsLevelCompleted(_selectedLevel);
            _detailTitle.text = $"Tầng {_selectedLevel + 1} — {lv.levelName}" + (done ? "  <color=#59D97A>(đã thắng)</color>" : "");

            var sb = new System.Text.StringBuilder();
            if (!string.IsNullOrWhiteSpace(lv.description)) sb.AppendLine(lv.description);
            sb.Append($"<color=#9AA7B8>Địch:</color> {(lv.enemyDeck != null ? lv.enemyDeck.deckName : "<color=#C43D3D>CHƯA GÁN!</color>")}");
            int pHp = lv.playerHealth > 0 ? lv.playerHealth : 20;
            int eHp = lv.enemyHealth > 0 ? lv.enemyHealth : 20;
            sb.Append($"    <color=#9AA7B8>Máu:</color> Bạn {pHp} / Địch {eHp}");
            _detailBody.text = sb.ToString();
        }

        // ── Lưới KHO bài — chọn deckPickCount lá ──────────────────
        void RefreshPool()
        {
            ClearChildren(_poolContent);

            if (_pool.Count == 0)
            {
                if (_poolHint != null) { _poolHint.gameObject.SetActive(true); _poolHint.text = "Kho trống — thắng tầng để nhận bài."; }
                RefreshCounter();
                return;
            }
            if (_poolHint != null) _poolHint.gameObject.SetActive(false);

            foreach (var card in _pool)
            {
                if (cardPrefab != null) MakePoolCell(card);
                else MakePoolRow(card);
            }
            LayoutRebuilder.ForceRebuildLayoutImmediate(_poolContent);
            RefreshCounter();
        }

        void MakePoolCell(CardData card)
        {
            _picks.TryGetValue(card, out int cnt);
            var cell = MakeRect("Pool_" + card.cardName, _poolContent, Vector2.zero, Vector2.zero);
            SpawnCardVisual(card, cell, cardCellSize.x - 6f, cardCellSize.y - 6f);

            // Overlay + click = thêm 1 bản
            var add = MakeRect("Add", cell, Vector2.zero, Vector2.one);
            var addImg = SetImage(add, new Color(1f, 1f, 1f, 0f));
            var addBtn = add.gameObject.AddComponent<Button>();
            addBtn.targetGraphic = addImg;
            var hc = addBtn.colors;
            hc.normalColor = new Color(1f, 1f, 1f, 0f);
            hc.highlightedColor = new Color(1f, 1f, 1f, 0.10f);
            hc.pressedColor = new Color(1f, 1f, 1f, 0.18f);
            addBtn.colors = hc;
            var c1 = card;
            addBtn.onClick.AddListener(() => AddPick(c1));

            if (cnt > 0)
            {
                // Badge số bản
                var badge = MakeRect("Cnt", cell, V2(0.60f, 0.85f), V2(0.99f, 0.99f));
                SetImage(badge, accentColor).raycastTarget = false;
                SpawnLabel(badge, $"x{cnt}", rowFontSize * 0.82f, Color.black, TextAlignmentOptions.Center, true);

                // Dải "-" ở đáy (thêm SAU add → nằm trên add ở vùng đáy) = bớt 1 bản
                var minus = MakeRect("Minus", cell, V2(0.02f, 0.02f), V2(0.98f, 0.16f));
                var mImg = SetImage(minus, new Color(0.5f, 0.12f, 0.12f, 0.9f));
                var mBtn = minus.gameObject.AddComponent<Button>();
                mBtn.targetGraphic = mImg;
                SpawnLabel(minus, "−  bớt", rowFontSize * 0.62f, Color.white, TextAlignmentOptions.Center, true);
                var c2 = card;
                mBtn.onClick.AddListener(() => RemovePick(c2));
            }
        }

        void MakePoolRow(CardData card)
        {
            _picks.TryGetValue(card, out int cnt);
            var row = MakeRect("PoolRow_" + card.cardName, _poolContent, Vector2.zero, Vector2.one);
            row.gameObject.AddComponent<LayoutElement>().preferredHeight = 44f;
            var img = SetImage(row, rowColor);
            var btn = row.gameObject.AddComponent<Button>();
            btn.targetGraphic = img;
            var c1 = card;
            btn.onClick.AddListener(() => AddPick(c1));
            SpawnLabel(row, $"{card.manaCost}", rowFontSize * 0.9f, accentColor,
                TextAlignmentOptions.Center, true, V2(0.02f, 0f), V2(0.12f, 1f));
            SpawnLabel(row, card.cardName, rowFontSize * 0.9f, Color.white,
                TextAlignmentOptions.MidlineLeft, false, V2(0.14f, 0f), V2(0.75f, 1f));
            if (cnt > 0)
            {
                SpawnLabel(row, $"x{cnt}", rowFontSize * 0.9f, accentColor,
                    TextAlignmentOptions.MidlineRight, true, V2(0.75f, 0f), V2(0.88f, 1f));
                var minus = MakeRect("Minus", row, V2(0.88f, 0.12f), V2(0.99f, 0.88f));
                var mImg = SetImage(minus, new Color(0.5f, 0.12f, 0.12f, 0.9f));
                var mBtn = minus.gameObject.AddComponent<Button>();
                mBtn.targetGraphic = mImg;
                SpawnLabel(minus, "−", rowFontSize, Color.white, TextAlignmentOptions.Center, true);
                var c2 = card;
                mBtn.onClick.AddListener(() => RemovePick(c2));
            }
        }

        int TotalPicked()
        {
            int t = 0;
            foreach (var kv in _picks) t += kv.Value;
            return t;
        }

        void AddPick(CardData card)
        {
            if (TotalPicked() >= _pickTarget) { Msg($"Đã đủ {_pickTarget} lá."); return; }
            _picks.TryGetValue(card, out int n);
            if (n >= _maxCopies) { Msg($"Tối đa {_maxCopies} bản mỗi lá."); return; }
            _picks[card] = n + 1;
            Msg("");
            RefreshPool();
        }

        void RemovePick(CardData card)
        {
            if (!_picks.TryGetValue(card, out int n)) return;
            if (n <= 1) _picks.Remove(card);
            else _picks[card] = n - 1;
            Msg("");
            RefreshPool();
        }

        void RefreshCounter()
        {
            int total = TotalPicked();
            if (_counterLabel != null)
            {
                _counterLabel.text = $"Đã chọn: {total}/{_pickTarget}";
                _counterLabel.color = total == _pickTarget ? doneColor : accentColor;
            }
            bool ready = total == _pickTarget && total > 0
                      && _selectedLevel >= 0 && campaign.levels[_selectedLevel].enemyDeck != null;
            if (_startBtn != null) _startBtn.interactable = ready;
            if (_startLabel != null)
                _startLabel.text = ready ? "VÀO TRẬN"
                    : (campaign.levels[_selectedLevel].enemyDeck == null ? "Địch chưa gán deck" : $"Chọn đủ {_pickTarget} lá");
        }

        void StartBattle()
        {
            if (_selectedLevel < 0) return;
            var lv = campaign.levels[_selectedLevel];
            if (lv == null || lv.enemyDeck == null) return;
            if (TotalPicked() != _pickTarget) return;

            var deck = ScriptableObject.CreateInstance<DeckData>();
            deck.deckName = $"Leo Tháp T{_selectedLevel + 1}";
            deck.cards = new List<CardData>();
            foreach (var kv in _picks)
                for (int i = 0; i < kv.Value; i++) deck.cards.Add(kv.Key);

            GameConfig.playerDeck = deck;
            GameConfig.enemyDeck = lv.enemyDeck;

            CampaignContext.campaign = campaign;
            CampaignContext.levelIndex = _selectedLevel;
            CampaignContext.returnSceneName = SceneManager.GetActiveScene().name;

            // Con Đường Anh Hùng: vào tầng 1 = bắt đầu HÀNH TRÌNH MỚI → xóa Sức Mạnh đã tích.
            // CHỈ áp cho luồng tầng tuyến tính cũ. Ở map mode (Mốc 2) RunMapView tự reset khi tạo map
            // → KHÔNG reset ở đây, tránh node đầu (tầng 1) xóa mất Sức Mạnh của hành trình.
            if (_selectedLevel == 0 && CampaignContext.map == null) CampaignRun.ResetRun();

            ScreenFade.LoadScene(lobby != null ? lobby.gameSceneName : "SampleScene");
        }

        // ══════════════════════════════════════════════════════════
        // BUILD UI
        // ══════════════════════════════════════════════════════════
        void BuildUI()
        {
            var parentRT = GetComponent<RectTransform>();
            var overlayRT = MakeRect("CampaignOverlay", parentRT, Vector2.zero, Vector2.one);
            _overlay = overlayRT.gameObject;
            var cv = _overlay.AddComponent<Canvas>();
            cv.overrideSorting = true;
            cv.sortingOrder = 500; // trên menu (400), dưới Xưởng Deck (600)
            _overlay.AddComponent<GraphicRaycaster>();
            SetImage(overlayRT, overlayColor);

            var panel = MakeRect("Panel", overlayRT, V2(0.03f, 0.04f), V2(0.97f, 0.96f));
            SetImage(panel, panelColor);

            var tbar = MakeRect("TitleBar", panel, V2(0f, 0.92f), V2(1f, 1f));
            SetImage(tbar, headerColor);
            SpawnLabel(tbar, campaign != null ? campaign.campaignName.ToUpper() : "LEO THÁP",
                titleFontSize, accentColor, TextAlignmentOptions.Center, true);
            MakeButton(MakeRect("CloseHolder", tbar, V2(0.955f, 0.12f), V2(0.995f, 0.88f)),
                "X", btnRed, Vector2.zero, Vector2.one, Close);

            // ── Trái: lưới tầng (boss card) ──
            var left = MakeRect("TowerArea", panel, V2(0.008f, 0.008f), V2(0.44f, 0.912f));
            SetImage(left, areaColor);
            var tHdr = MakeRect("Hdr", left, V2(0f, 0.94f), V2(1f, 1f));
            SetImage(tHdr, headerColor);
            SpawnLabel(tHdr, "THÁP", headerFontSize, Color.white,
                TextAlignmentOptions.MidlineLeft, true, V2(0.02f, 0f), V2(0.98f, 1f));
            _levelContent = BuildScrollArea("TowerScroll", left, V2(0f, 0f), V2(1f, 0.93f),
                grid: cardPrefab != null,
                cellSize: new Vector2(cardCellSize.x, cardCellSize.y + LeftCellExtra));

            // ── Phải: chi tiết tầng + CHỌN BÀI ──
            var right = MakeRect("RightArea", panel, V2(0.45f, 0.008f), V2(0.992f, 0.912f));
            SetImage(right, areaColor);

            _detailTitle = SpawnLabel(right, "", rowFontSize * 1.05f, accentColor,
                TextAlignmentOptions.MidlineLeft, true, V2(0.02f, 0.935f), V2(0.98f, 0.995f));
            _detailBody = SpawnLabel(right, "", rowFontSize * 0.78f, Color.white,
                TextAlignmentOptions.TopLeft, false, V2(0.02f, 0.86f), V2(0.98f, 0.935f));
            _detailBody.textWrappingMode = TextWrappingModes.Normal;

            var pHdr = MakeRect("PoolHdr", right, V2(0f, 0.80f), V2(1f, 0.855f));
            SetImage(pHdr, headerColor);
            SpawnLabel(pHdr, "CHỌN BÀI (kho đã mở khóa)", headerFontSize * 0.9f, Color.white,
                TextAlignmentOptions.MidlineLeft, true, V2(0.02f, 0f), V2(0.62f, 1f));
            _counterLabel = SpawnLabel(pHdr, "Đã chọn: 0/1", headerFontSize * 0.95f, accentColor,
                TextAlignmentOptions.MidlineRight, true, V2(0.62f, 0f), V2(0.98f, 1f));

            _poolContent = BuildScrollArea("PoolScroll", right, V2(0f, 0.115f), V2(1f, 0.80f),
                grid: cardPrefab != null, cellSize: cardCellSize);
            _poolHint = SpawnLabel(right, "Chọn tầng để bắt đầu.", headerFontSize * 0.95f, textDimColor,
                TextAlignmentOptions.Center, false, V2(0.05f, 0.4f), V2(0.95f, 0.6f));

            _msgLabel = SpawnLabel(right, "", headerFontSize * 0.85f, accentColor,
                TextAlignmentOptions.MidlineLeft, false, V2(0.02f, 0.11f), V2(0.60f, 0.115f + 0.03f));

            var startHolder = MakeRect("StartHolder", right, V2(0.15f, 0.01f), V2(0.85f, 0.10f));
            var sImg = SetImage(startHolder, btnGreen);
            _startBtn = startHolder.gameObject.AddComponent<Button>();
            _startBtn.targetGraphic = sImg;
            var bc = _startBtn.colors;
            bc.normalColor = btnGreen;
            bc.highlightedColor = Color.Lerp(btnGreen, Color.white, 0.22f);
            bc.pressedColor = Color.Lerp(btnGreen, Color.black, 0.22f);
            bc.disabledColor = new Color(0.25f, 0.30f, 0.35f, 0.7f);
            _startBtn.colors = bc;
            _startBtn.onClick.AddListener(StartBattle);
            _startLabel = SpawnLabel(startHolder, "VÀO TRẬN", rowFontSize * 1.05f, Color.white,
                TextAlignmentOptions.Center, true);

            _overlay.SetActive(false);
        }

        // ── Card visual (model-bound, khung như Xưởng Deck) ───────
        public GameObject SpawnCardVisual(CardData card, RectTransform parent, float w, float h)
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
            SpawnLabel(rt, label, rowFontSize * 0.85f, Color.white, TextAlignmentOptions.Center, true);
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
                vlg.spacing = 4f;
                vlg.padding = new RectOffset(4, 4, 4, 4);
                vlg.childForceExpandWidth = true;
                vlg.childForceExpandHeight = false;
            }
            content.gameObject.AddComponent<ContentSizeFitter>().verticalFit =
                ContentSizeFitter.FitMode.PreferredSize;

            sr.viewport = vp;
            sr.content = content;
            return content;
        }

        static void ClearChildren(RectTransform content)
        {
            if (content == null) return;
            for (int i = content.childCount - 1; i >= 0; i--)
                Destroy(content.GetChild(i).gameObject);
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