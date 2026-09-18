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
    /// MỐC 2 — BẢN ĐỒ HÀNH TRÌNH (Con Đường Anh Hùng).
    /// Overlay tự dựng: vẽ các cột node có nhánh; node ĐANG MỞ (Available) mới bấm được.
    ///   • Battle / Elite / Boss → mở màn chọn bài của CampaignView cho tầng tương ứng.
    ///   • Reward               → chọn 1 Sức Mạnh (CampaignRun), đánh dấu xong ngay tại map.
    ///
    /// LobbyMenuView tự thêm component + gán campaign / campaignView.
    /// Nút LEO THÁP và lúc về từ trận đều mở view này.
    /// </summary>
    public class RunMapView : MonoBehaviour
    {
        [Header("Refs — LobbyMenuView tự gán")]
        public CampaignData campaign;
        public CampaignView campaignView;
        [Tooltip("Prefab CardView để render CARD champion làm ảnh (giống PoC). LobbyMenuView tự gán.")]
        public GameObject cardPrefab;

        [Header("Bản đồ")]
        [Tooltip("Số cột (độ dài hành trình). 5–9 hợp lý.")]
        public int mapColumns = 7;

        [Header("Màu (theme navy + gold)")]
        public Color overlayColor = new Color(0.02f, 0.03f, 0.05f, 0.94f);
        public Color panelColor = new Color(0.10f, 0.12f, 0.17f, 0.99f);
        public Color headerColor = new Color(0.14f, 0.17f, 0.24f);
        public Color accentColor = new Color(0.96f, 0.80f, 0.42f);   // gold
        public Color battleColor = new Color(0.22f, 0.44f, 0.80f);   // steel blue
        public Color eliteColor = new Color(0.88f, 0.54f, 0.22f);   // amber
        public Color rewardColor = new Color(0.56f, 0.40f, 0.80f);   // violet
        public Color bossColor = new Color(0.82f, 0.28f, 0.32f);   // crimson
        public Color relicColor = new Color(0.28f, 0.70f, 0.72f);   // teal
        public Color shopColor = new Color(0.86f, 0.70f, 0.26f);   // vàng (cửa hàng)
        public Color eventColor = new Color(0.42f, 0.62f, 0.42f);   // lục nhạt (sự kiện)
        public Color itemColor = new Color(0.30f, 0.66f, 0.60f);   // xanh ngọc (vật phẩm)
        public Color doneColor = new Color(0.22f, 0.48f, 0.34f);   // green
        public Color lockColor = new Color(0.17f, 0.20f, 0.27f);

        GameObject _overlay;
        RectTransform _mapArea;
        TextMeshProUGUI _infoLabel;

        // Champion select (PoC-style) state
        GameObject _champSel;
        RectTransform _champDetail;
        int _focusIdx;
        int _champTab; // 0 Tổng quan · 1 Bộ bài · 2 Cấp độ · 3 Cổ vật · 4 Trang bị
        int _difficulty = 1; // M10: 1 Thường · 2 Khó · 3 Ác Mộng (fallback khi chưa có ải)
        AdventureData _selectedAdventure; // M1: ải đang chọn (null = dùng 3 độ khó)
        CardItemDef _selItem;             // M8: trang bị champion đang xem ở tab TRANG BỊ (CardItemLibrary)
        bool _endArmed;                   // M12: xác nhận 2 lần cho nút Kết Thúc Sớm
        TMPro.TextMeshProUGUI _endLbl;
        RectTransform _statusRoot;        // M13: thanh trạng thái (Nexus/Vàng/Lõi) — dựng lại mỗi Render

        [Header("World Map (M3) — gán ảnh Runeterra + vị trí region (0-1)")]
        public Sprite worldBackground; // ảnh bản đồ; để trống = nền tối
        [System.Serializable] public class RegionPos { public string region; [Range(0f, 1f)] public float x = 0.5f, y = 0.5f; }
        public List<RegionPos> regionLayout = new List<RegionPos>();

        // Editor deck champion (tab BỘ BÀI)
        List<CardData> _editDeck;
        string _editDeckChamp;

        static Sprite _round;
        static Sprite Round => _round != null ? _round : (_round = UISprites.RoundedRect());

        // ══════════════════════════════════════════════════════════
        public void Open()
        {
            if (campaign == null || campaign.levels == null || campaign.levels.Count == 0)
            {
                Debug.LogWarning("[RunMapView] Chưa gán CampaignData / campaign trống.");
                return;
            }
            ApplyTheme(); // M13: áp palette Honkai cho toàn bộ view
            AdventureFactory.EnsureAdventures(campaign); // M2: tự sinh ải nếu chưa có (khỏi setup tay)

            // ĐANG GIỮA HÀNH TRÌNH (vừa đánh xong 1 node → game gọi lại Open()) → tiếp tục
            // CON ĐƯỜNG ANH HÙNG (run map), KHÔNG về hub nhân vật.
            if (CampaignContext.map != null) { OpenMap(); return; }

            // Không có run trong bộ nhớ: có save dở trên đĩa → khôi phục & tiếp tục run map.
            if (RunSaveStore.TryRestore(campaign, LibraryRef())) { OpenMap(); return; }

            // Hoàn toàn chưa có run → mới vào từ lobby: VÀO THẲNG BẢN ĐỒ ẢI của champion hiện tại
            // (hub chọn tướng dựng làm nền — bấm icon champion trên map để vào khu setup).
            if (campaign.champions != null && campaign.champions.Count > 0)
                OpenWorldMapLanding();
            else { StartNewRunNoChampion(); OpenMap(); }
        }

        // ── PvP×PoC (Ask 1): mở THẲNG màn CHỌN/CHỈNH TƯỚNG cho 1 champion.
        //    KHÔNG world map, KHÔNG node map — chỉ hub chỉnh deck/trang bị. Nút "SỬA DECK / TRANG BỊ" ở Đấu Trường gọi.
        public void OpenChampionEditor(string championName)
        {
            if (campaign == null || campaign.champions == null || campaign.champions.Count == 0)
            { Debug.LogWarning("[RunMapView] OpenChampionEditor: chưa gán campaign / không có champion."); return; }
            ApplyTheme();
            AdventureFactory.EnsureAdventures(campaign);
            int idx = 0;
            for (int i = 0; i < campaign.champions.Count; i++)
                if (campaign.champions[i] != null && campaign.champions[i].championName == championName) { idx = i; break; }
            _focusIdx = idx; _champTab = 0;
            ShowChampionSelect();   // hub chỉnh tướng (danh sách + tab Bộ bài/Trang bị...) — không nhảy vào ải
        }

        // ── PoC (Ask 2a): vào Con Đường Anh Hùng TỪ SẢNH → LUÔN mở WORLD MAP (chọn khu vực).
        //    KHÔNG auto nhảy vào node map của ải dở. KHÁC Open(): Open() dành cho lúc VỀ SAU TRẬN (tiếp tục node map).
        public void OpenFromLobby()
        {
            if (campaign == null || campaign.levels == null || campaign.levels.Count == 0)
            { Debug.LogWarning("[RunMapView] OpenFromLobby: chưa gán CampaignData."); return; }
            ApplyTheme();
            AdventureFactory.EnsureAdventures(campaign);
            // Nạp save đĩa để BIẾT có run dở (populate CampaignContext.map) — nhưng KHÔNG nhảy vào node map.
            if (CampaignContext.map == null) RunSaveStore.TryRestore(campaign, LibraryRef());
            if (campaign.champions != null && campaign.champions.Count > 0)
                OpenWorldMapLanding();      // world map (ảnh 2)
            else { StartNewRunNoChampion(); OpenMap(); }
        }

        const string LastChampKey = "poc_last_champ";

        // MỐC 13: gom toàn bộ màu về palette "Tối Lam Băng (Honkai)". Áp 1 lần khi mở
        // → mọi node/panel/label (đọc các field màu bên dưới) tự đổi theo, kể cả instance scene cũ.
        bool _themed;
        void ApplyTheme()
        {
            if (_themed) return; _themed = true;
            overlayColor = PoCTheme.A(PoCTheme.Bg0, 0.97f);
            panelColor = PoCTheme.Bg1;
            headerColor = PoCTheme.Surface;
            accentColor = PoCTheme.Cyan;                 // tiêu đề/nhấn chính
            battleColor = PoCTheme.Cyan;
            eliteColor = PoCTheme.Hex("D98A3A");
            rewardColor = PoCTheme.Hex("6FA8DC");
            bossColor = PoCTheme.Danger;
            relicColor = PoCTheme.Hex("46C0B0");
            shopColor = PoCTheme.Gold;
            eventColor = PoCTheme.Success;
            itemColor = PoCTheme.Hex("3FB0C0");
            doneColor = PoCTheme.Success;
            lockColor = PoCTheme.Locked;
        }

        // Đặt 1 icon SAO vẽ thật + số bên trái (thay "{n}★" hay bị ô vuông). Trả về vùng đã dùng.
        void StarNum(RectTransform parent, float value, Vector2 aMin, Vector2 aMax, Color numCol, float numSize)
        {
            // số chiếm ~70% trái, sao ~30% phải
            float split = Mathf.Lerp(aMin.x, aMax.x, 0.62f);
            SpawnLabel(parent, $"{value:0.#}", numSize, numCol, TextAlignmentOptions.MidlineRight, true,
                new Vector2(aMin.x, aMin.y), new Vector2(split, aMax.y));
            PoCTheme.Put(parent, "star", new Vector2(split + 0.005f, aMin.y), new Vector2(aMax.x, aMax.y),
                PoCTheme.Star(), PoCTheme.Gold);   // preserveAspect → sao vuông, tự căn giữa
        }

        /// <summary>Vào từ lobby: mở thẳng BẢN ĐỒ ẢI của champion đang dùng (hub làm nền).</summary>
        void OpenWorldMapLanding()
        {
            int idx = CurrentChampIdx();
            var champ = campaign.champions[idx];
            int star = ProgressStore.GetStar(champ.championName);
            _focusIdx = idx; _champTab = 0;
            ShowChampionSelect();       // hub setup làm NỀN (bấm badge champion để lộ ra)
            ShowWorldMap(idx, star);    // MÀN CHÍNH khi vào từ lobby
        }

        /// <summary>Index champion "đang dùng" — nhớ tướng chơi gần nhất (PlayerPrefs), fallback tướng đầu tiên.</summary>
        int CurrentChampIdx()
        {
            var champs = campaign.champions;
            string last = PlayerPrefs.GetString(LastChampKey, "");
            if (!string.IsNullOrEmpty(last))
                for (int i = 0; i < champs.Count; i++)
                    if (champs[i] != null && champs[i].championName == last) return i;
            for (int i = 0; i < champs.Count; i++) if (champs[i] != null) return i;
            return 0;
        }

        public void Close() { if (_overlay != null) _overlay.SetActive(false); }

        CardLibrary LibraryRef() =>
            (campaignView != null && campaignView.lobby != null) ? campaignView.lobby.cardLibrary : null;

        void OpenMap()
        {
            if (_overlay == null) BuildOverlay();
            _overlay.SetActive(true);
            Render();
            MaybeShowRelicPick();   // Mốc 4: vừa thắng Elite → nhặt cổ vật
            MaybeShowPowerReward(); // vừa hạ MiniBoss → chọn thêm 1 Lõi Sức Mạnh
            MaybeShowRunSummary();  // Mốc 10: vừa hạ Boss → tổng kết hành trình
        }

        void MaybeShowPowerReward()
        {
            if (!CampaignRun.PendingPowerReward) return;
            CampaignRun.PendingPowerReward = false;
            bool hasPower = campaign.powerCores != null && campaign.powerCores.Count > 0;
            if (hasPower) ShowPowerPick(-1);   // -1 = thưởng, không complete node
        }

        void MaybeShowRunSummary()
        {
            if (!CampaignContext.pendingRunSummary) return;
            CampaignContext.pendingRunSummary = false;
            ShowRunSummary();
        }

        // ── M10: TỔNG KẾT HÀNH TRÌNH — trọng tâm KINH NGHIỆM lên cấp champion ──
        void ShowRunSummary()
        {
            var picker = MakeRect("RunSummary", _overlay.GetComponent<RectTransform>(), Vector2.zero, Vector2.one);
            SetImage(picker, new Color(0f, 0f, 0f, 0.92f));
            var panel = MakeRect("P", picker, new Vector2(0.24f, 0.14f), new Vector2(0.76f, 0.88f));
            RoundImg(panel, panelColor);
            // M12: quy đổi VÀNG kiếm trong ải → LÕI (tiền mua trang bị). Cộng 1 lần.
            int deposited = CampaignRun.gold;
            if (deposited > 0) { ProgressStore.AddCores(deposited); CampaignRun.gold = 0; }

            bool cleared = CampaignContext.map != null && CampaignContext.map.IsFinished();
            var bar = MakeRect("Bar", panel, new Vector2(0f, 0.9f), new Vector2(1f, 1f));
            RoundImg(bar, cleared ? new Color(0.18f, 0.42f, 0.28f) : new Color(0.40f, 0.22f, 0.24f));
            SpawnLabel(panel, cleared ? "CHINH PHỤC THÀNH CÔNG!" : "KẾT THÚC HÀNH TRÌNH", 24f,
                cleared ? new Color(0.42f, 0.92f, 0.56f) : new Color(0.96f, 0.72f, 0.52f),
                TextAlignmentOptions.Center, true, new Vector2(0.05f, 0.905f), new Vector2(0.95f, 0.995f));

            string[] dn = { "", "Thường", "Khó", "Ác Mộng" };
            string champName = CampaignRun.champion != null ? CampaignRun.champion.championName : null;

            SpawnLabel(panel,
                (champName != null ? $"<b>{champName}</b>  <color=#E7B24B>{CampaignRun.championStar} Sao</color>" : "<b>Hành trình</b>")
                + (CampaignRun.adventure != null
                    ? $"    <color=#C89BF5>{CampaignRun.adventure.adventureName} — <color=#F0C75A>{CampaignRun.adventure.StarLabel()}</color></color>"
                    : $"    <color=#C89BF5>Độ khó: {dn[Mathf.Clamp(CampaignRun.difficulty, 1, 3)]}</color>"),
                17f, Color.white, TextAlignmentOptions.Center, false, new Vector2(0.05f, 0.80f), new Vector2(0.95f, 0.885f));

            int startXp = CampaignRun.runStartXp;
            int endXp = champName != null ? ProgressStore.GetMasteryXp(champName) : 0;
            int gainedXp = Mathf.Max(0, endXp - startXp);
            int gainedEss = Mathf.Max(0, ProgressStore.Essence - CampaignRun.runStartEssence);
            int gainedCores = Mathf.Max(0, ProgressStore.Cores - CampaignRun.runStartCores);

            // Dòng "KINH NGHIỆM +N"
            SpawnLabel(panel, $"<b><color=#F0C75A>KINH NGHIỆM</color></b>   <size=130%><color=#7FE3A0>+{gainedXp} XP</color></size>",
                20f, Color.white, TextAlignmentOptions.Center, true, new Vector2(0.05f, 0.66f), new Vector2(0.95f, 0.76f));

            // Thanh XP (nền + fill chạy) + nhãn cấp / xp
            var lvlLbl = SpawnLabel(panel, "", 16f, new Color(0.95f, 0.82f, 0.4f), TextAlignmentOptions.Left, true,
                new Vector2(0.10f, 0.585f), new Vector2(0.5f, 0.645f));
            var xpLbl = SpawnLabel(panel, "", 14f, new Color(0.8f, 0.84f, 0.92f), TextAlignmentOptions.Right, false,
                new Vector2(0.5f, 0.585f), new Vector2(0.90f, 0.645f));
            var track = MakeRect("XpTrack", panel, new Vector2(0.10f, 0.52f), new Vector2(0.90f, 0.575f));
            RoundImg(track, new Color(0.10f, 0.12f, 0.18f, 0.98f));
            var fill = MakeRect("XpFill", track, new Vector2(0f, 0f), new Vector2(0f, 1f));
            fill.pivot = new Vector2(0f, 0.5f);
            RoundImg(fill, new Color(0.36f, 0.80f, 0.5f, 1f)).raycastTarget = false;
            // nhãn reveal "LÊN SAO" hiện khi thanh XP vượt mốc sao
            var starLbl = SpawnLabel(panel, "", 17f, new Color(1f, 0.86f, 0.4f), TextAlignmentOptions.Center, true,
                new Vector2(0.1f, 0.49f), new Vector2(0.9f, 0.525f));
            StartCoroutine(AnimateXp(fill, lvlLbl, xpLbl, starLbl, startXp, endXp));

            // Thưởng khác + build
            string body =
                $"<color=#4FC5E0>Tinh Hồn +{gainedEss}</color>    <color=#E7B24B>Vàng → Lõi +{gainedCores}</color>\n\n" +
                $"<b><color=#F0C75A>Sức mạnh:</color></b> {CampaignRun.Summary()}\n" +
                (CampaignRun.Relics.Count > 0 ? $"<b><color=#5FCFD2>Cổ vật:</color></b> {CampaignRun.RelicSummary()}" : "");
            SpawnLabel(panel, body, 14f, Color.white, TextAlignmentOptions.Top, false,
                new Vector2(0.06f, 0.19f), new Vector2(0.94f, 0.5f)).textWrappingMode = TextWrappingModes.Normal;

            var nr = MakeRect("ToMap", panel, new Vector2(0.08f, 0.04f), new Vector2(0.48f, 0.15f));
            MakeBtn(nr, new Color(0.24f, 0.62f, 0.38f, 0.98f), "▶ VỀ BẢN ĐỒ ẢI", 14f,
                () => { Destroy(picker.gameObject); GoToWorldMap(true); });   // xong ải → World Map (ải kế mở khoá)
            var mn = MakeRect("Menu", panel, new Vector2(0.52f, 0.04f), new Vector2(0.92f, 0.15f));
            MakeBtn(mn, new Color(0.22f, 0.42f, 0.72f, 0.98f), "← MENU", 14f,
                () => { Destroy(picker.gameObject); CampaignContext.map = null; RunSaveStore.Clear(); Close(); });

            // M14: LẦN ĐẦU vượt ải → nhận rương → banner "MỞ NGAY".
            if (CampaignRun.pendingChestTier >= 0)
            {
                var tier = (ChestTier)CampaignRun.pendingChestTier;
                CampaignRun.pendingChestTier = -1;   // tiêu cờ (rương đã vào túi qua AddChest)
                ShowChestEarned(tier);
            }
        }

        // ── M14: RƯƠNG BÁU ─────────────────────────────────────────────
        // Banner mừng nhận rương lần đầu vượt ải: mở ngay hoặc để dành trong túi.
        void ShowChestEarned(ChestTier tier)
        {
            var root = MakeRootCanvas("ChestEarned", 32250, out var go);
            var dim = SetImage(root, new Color(0f, 0f, 0f, 0.82f));
            dim.raycastTarget = true;
            var col = HexColor(ChestSystem.TierHex(tier));

            var panel = MakeRect("P", root, new Vector2(0.33f, 0.30f), new Vector2(0.67f, 0.72f));
            RoundImg(panel, PoCTheme.Obsid);
            var glow = MakeRect("Glow", panel, new Vector2(0.02f, 0.02f), new Vector2(0.98f, 0.98f));
            var glowImg = RoundImg(glow, new Color(col.r, col.g, col.b, 0.14f));
            glowImg.raycastTarget = false;

            SpawnLabel(panel, "RƯƠNG BÁU!", 15f, PoCTheme.AmGoldHi, TextAlignmentOptions.Center, true,
                new Vector2(0.05f, 0.82f), new Vector2(0.95f, 0.94f)).raycastTarget = false;

            // Biểu tượng rương (dùng Diamond tô màu bậc)
            var chest = MakeRect("Icon", panel, new Vector2(0.34f, 0.42f), new Vector2(0.66f, 0.78f));
            var ci = chest.gameObject.AddComponent<Image>();
            ci.sprite = ChestSprite(); ci.color = col; ci.raycastTarget = false;

            // JUICE: panel bật vào, rương nhún nhẹ, viền sáng thở.
            StartCoroutine(PopIn(panel));
            StartCoroutine(Bob(chest));
            StartCoroutine(PulseAlpha(glowImg, 0.14f, 0.10f));

            SpawnLabel(panel, $"<b>{ChestSystem.TierName(tier)}</b>", 22f, col, TextAlignmentOptions.Center, true,
                new Vector2(0.05f, 0.30f), new Vector2(0.95f, 0.42f)).raycastTarget = false;

            var openBtn = MakeRect("Open", panel, new Vector2(0.08f, 0.08f), new Vector2(0.53f, 0.22f));
            MakeBtn(openBtn, new Color(0.62f, 0.44f, 0.16f, 0.98f), "MỞ NGAY", 14f, () =>
            {
                if (ProgressStore.TakeChest((int)tier))
                {
                    var rw = ChestSystem.Open(tier, campaign);
                    Destroy(go);
                    ShowChestReward(tier, rw);
                }
                else Destroy(go);
            });
            var later = MakeRect("Later", panel, new Vector2(0.57f, 0.08f), new Vector2(0.92f, 0.22f));
            MakeBtn(later, new Color(0.30f, 0.34f, 0.42f, 0.98f), "ĐỂ SAU", 14f, () => Destroy(go));
        }

        // Túi rương: liệt kê 4 bậc + số lượng, mở từng cái.
        public void ShowChestInventory()
        {
            var root = MakeRootCanvas("ChestBag", 32200, out var go);
            var dim = SetImage(root, new Color(0f, 0f, 0f, 0.86f));
            dim.raycastTarget = true;

            var panel = MakeRect("P", root, new Vector2(0.28f, 0.12f), new Vector2(0.72f, 0.90f));
            RoundImg(panel, PoCTheme.Obsid);
            var bar = MakeRect("Bar", panel, new Vector2(0f, 0.90f), new Vector2(1f, 1f));
            RoundImg(bar, PoCTheme.PlaqNavy);
            SpawnLabel(panel, "TÚI RƯƠNG BÁU", 22f, PoCTheme.AmGoldHi, TextAlignmentOptions.Center, true,
                new Vector2(0.05f, 0.905f), new Vector2(0.95f, 0.99f)).raycastTarget = false;

            RebuildChestRows(panel);

            var close = MakeRect("Close", panel, new Vector2(0.30f, 0.03f), new Vector2(0.70f, 0.11f));
            MakeBtn(close, new Color(0.30f, 0.34f, 0.42f, 0.98f), "ĐÓNG", 14f, () => Destroy(go));
            StartCoroutine(PopIn(panel)); // JUICE: túi bật vào
        }

        // Vẽ 4 hàng bậc rương trong panel túi (gọi lại khi mở xong để cập nhật số).
        void RebuildChestRows(RectTransform panel)
        {
            var old = panel.Find("Rows");
            if (old != null) Destroy(old.gameObject);
            var rows = MakeRect("Rows", panel, new Vector2(0.06f, 0.14f), new Vector2(0.94f, 0.88f));

            for (int i = 0; i < 4; i++)
            {
                var tier = (ChestTier)i;
                var col = HexColor(ChestSystem.TierHex(tier));
                float top = 1f - i * 0.25f;
                var row = MakeRect("R" + i, rows, new Vector2(0f, top - 0.22f), new Vector2(1f, top - 0.02f));
                RoundImg(row, new Color(col.r * 0.22f, col.g * 0.22f, col.b * 0.22f, 0.95f)).raycastTarget = false;

                var ic = MakeRect("Ic", row, new Vector2(0.02f, 0.16f), new Vector2(0.16f, 0.86f));
                var ici = ic.gameObject.AddComponent<Image>();
                ici.sprite = ChestSprite(); ici.color = col; ici.raycastTarget = false;

                int cnt = ProgressStore.ChestCount(i);
                SpawnLabel(row, $"<b>{ChestSystem.TierName(tier)}</b>", 17f, col, TextAlignmentOptions.Left, true,
                    new Vector2(0.19f, 0.46f), new Vector2(0.70f, 0.94f)).raycastTarget = false;
                SpawnLabel(row, $"Đang có: <b>{cnt}</b>", 13f, PoCTheme.AmCream, TextAlignmentOptions.Left, false,
                    new Vector2(0.19f, 0.08f), new Vector2(0.70f, 0.5f)).raycastTarget = false;

                var open = MakeRect("Open", row, new Vector2(0.72f, 0.20f), new Vector2(0.97f, 0.80f));
                if (cnt > 0)
                {
                    MakeBtn(open, new Color(0.62f, 0.44f, 0.16f, 0.98f), "MỞ", 15f, () =>
                    {
                        // dùng (int)tier — KHÔNG dùng i (biến vòng for bị closure về 4).
                        if (ProgressStore.TakeChest((int)tier))
                        {
                            var rw = ChestSystem.Open(tier, campaign);
                            RebuildChestRows(panel);      // cập nhật số còn lại
                            ShowChestReward(tier, rw);
                        }
                    });
                }
                else
                {
                    RoundImg(open, new Color(0.18f, 0.20f, 0.26f, 0.9f)).raycastTarget = false;
                    SpawnLabel(open, "—", 15f, PoCTheme.AmDim, TextAlignmentOptions.Center, true).raycastTarget = false;
                }
            }
        }

        // Popup phần thưởng sau khi mở rương (Lõi / Trang bị / Lá bài).
        void ShowChestReward(ChestTier tier, ChestSystem.ChestReward rw)
        {
            var root = MakeRootCanvas("ChestReward", 32300, out var go);
            var dim = SetImage(root, new Color(0f, 0f, 0f, 0.86f));
            dim.raycastTarget = true;
            var col = HexColor(ChestSystem.TierHex(tier));

            var panel = MakeRect("P", root, new Vector2(0.34f, 0.26f), new Vector2(0.66f, 0.74f));
            RoundImg(panel, PoCTheme.Obsid);
            var bar = MakeRect("Bar", panel, new Vector2(0f, 0.88f), new Vector2(1f, 1f));
            RoundImg(bar, new Color(col.r * 0.4f, col.g * 0.4f, col.b * 0.4f, 0.98f)).raycastTarget = false;
            SpawnLabel(panel, $"<b>{ChestSystem.TierName(tier)}</b>", 16f, col, TextAlignmentOptions.Center, true,
                new Vector2(0.05f, 0.88f), new Vector2(0.95f, 0.99f)).raycastTarget = false;

            // Khung ảnh phần thưởng
            var frame = MakeRect("Frame", panel, new Vector2(0.30f, 0.40f), new Vector2(0.70f, 0.82f));
            RoundImg(frame, new Color(0.09f, 0.11f, 0.16f, 0.98f)).raycastTarget = false;

            string title, sub; Color tCol = PoCTheme.AmCream;
            RectTransform iconRt = null;
            if (rw != null && rw.kind == ChestSystem.RewardKind.Item && rw.item != null)
            {
                var art = MakeRect("Art", frame, new Vector2(0.14f, 0.14f), new Vector2(0.86f, 0.86f));
                if (rw.item.icon != null) SetImage(art, Color.white).sprite = rw.item.icon;
                else RoundImg(art, new Color(0.5f, 0.7f, 0.4f, 0.9f)).raycastTarget = false;
                iconRt = art;
                title = rw.item.itemName; sub = "TRANG BỊ MỚI"; tCol = PoCTheme.AmGreen;
            }
            else if (rw != null && rw.kind == ChestSystem.RewardKind.Card && rw.card != null)
            {
                var art = MakeRect("Art", frame, new Vector2(0.10f, 0.10f), new Vector2(0.90f, 0.90f));
                if (rw.card.artwork != null) { var raw = art.gameObject.AddComponent<RawImage>(); raw.texture = rw.card.artwork; raw.raycastTarget = false; }
                else RoundImg(art, new Color(0.4f, 0.5f, 0.75f, 0.9f)).raycastTarget = false;
                iconRt = art;
                title = rw.card.cardName; sub = "LÁ BÀI MỞ KHOÁ"; tCol = HexColor("C89BF5");
            }
            else
            {
                int c = rw != null ? rw.cores : 0;
                var coin = MakeRect("Coin", frame, new Vector2(0.2f, 0.2f), new Vector2(0.8f, 0.8f));
                var coi = coin.gameObject.AddComponent<Image>();
                coi.sprite = Round; coi.color = PoCTheme.AmGold; coi.raycastTarget = false;
                iconRt = coin;
                title = $"+{c} Lõi"; sub = "TÀI NGUYÊN"; tCol = PoCTheme.AmGold;
            }

            // JUICE: panel bật vào; sáng bùng sau khung; icon phần thưởng pop trễ nhịp.
            StartCoroutine(PopIn(panel));
            StartCoroutine(Burst(panel, col));
            if (iconRt != null) StartCoroutine(PopIn(iconRt, 0.30f, 0.18f));

            SpawnLabel(panel, sub, 12f, PoCTheme.AmDim, TextAlignmentOptions.Center, true,
                new Vector2(0.05f, 0.30f), new Vector2(0.95f, 0.38f)).raycastTarget = false;
            SpawnLabel(panel, $"<b>{title}</b>", 20f, tCol, TextAlignmentOptions.Center, true,
                new Vector2(0.05f, 0.20f), new Vector2(0.95f, 0.31f)).raycastTarget = false;

            var ok = MakeRect("OK", panel, new Vector2(0.28f, 0.05f), new Vector2(0.72f, 0.16f));
            MakeBtn(ok, new Color(0.24f, 0.62f, 0.38f, 0.98f), "TUYỆT!", 14f, () => Destroy(go));
        }

        // Màu từ hex "RRGGBB".
        static Color HexColor(string hex)
        {
            if (ColorUtility.TryParseHtmlString("#" + hex, out var c)) return c;
            return Color.white;
        }

        // Sprite rương — tái dùng Diamond của PoCTheme (khỏi thêm asset).
        static Sprite ChestSprite() => PoCTheme.Diamond();

        // ── JUICE coroutines (an toàn: tự thoát khi object bị Destroy) ──────────
        // Bật vào kiểu ease-out-back (vọt quá 1 rồi về): dùng cho panel/icon.
        System.Collections.IEnumerator PopIn(RectTransform rt, float dur = 0.28f, float delay = 0f)
        {
            if (rt == null) yield break;
            rt.localScale = Vector3.zero;
            float d = 0f;
            while (d < delay) { d += Time.unscaledDeltaTime; if (rt == null) yield break; yield return null; }
            float t = 0f;
            const float s = 1.70158f;
            while (t < dur)
            {
                t += Time.unscaledDeltaTime;
                if (rt == null) yield break;
                float k = Mathf.Clamp01(t / dur) - 1f;
                float e = 1f + (s + 1f) * k * k * k + s * k * k; // ease-out-back
                rt.localScale = Vector3.one * e;
                yield return null;
            }
            if (rt != null) rt.localScale = Vector3.one;
        }

        // Nhún lên xuống nhẹ vô hạn (chest icon).
        System.Collections.IEnumerator Bob(RectTransform rt, float amp = 0.018f, float speed = 2.2f)
        {
            if (rt == null) yield break;
            Vector2 min0 = rt.anchorMin, max0 = rt.anchorMax;
            float t = 0f;
            while (rt != null)
            {
                t += Time.unscaledDeltaTime * speed;
                float dy = Mathf.Sin(t) * amp;
                rt.anchorMin = new Vector2(min0.x, min0.y + dy);
                rt.anchorMax = new Vector2(max0.x, max0.y + dy);
                yield return null;
            }
        }

        // Viền sáng "thở" (alpha dao động quanh baseA).
        System.Collections.IEnumerator PulseAlpha(Image img, float baseA, float amp, float speed = 2.4f)
        {
            if (img == null) yield break;
            Color c = img.color;
            float t = 0f;
            while (img != null)
            {
                t += Time.unscaledDeltaTime * speed;
                float a = baseA + (Mathf.Sin(t) * 0.5f + 0.5f) * amp;
                img.color = new Color(c.r, c.g, c.b, a);
                yield return null;
            }
        }

        // Vòng sáng bung ra sau khung phần thưởng rồi tan.
        System.Collections.IEnumerator Burst(RectTransform parent, Color color)
        {
            if (parent == null) yield break;
            var b = MakeRect("Burst", parent, new Vector2(0.30f, 0.40f), new Vector2(0.70f, 0.82f));
            var bi = RoundImg(b, new Color(color.r, color.g, color.b, 0.55f));
            bi.raycastTarget = false;
            b.SetAsFirstSibling();
            float t = 0f; const float dur = 0.5f;
            while (t < dur)
            {
                t += Time.unscaledDeltaTime;
                if (b == null) yield break;
                float k = t / dur;
                b.localScale = Vector3.one * Mathf.Lerp(0.5f, 1.9f, k);
                bi.color = new Color(color.r, color.g, color.b, 0.55f * (1f - k));
                yield return null;
            }
            if (b != null) Destroy(b.gameObject);
        }

        // Thanh XP chạy từ startXp → endXp theo ĐƯỜNG CONG PoC; nhảy cấp + reveal LÊN SAO dọc đường.
        System.Collections.IEnumerator AnimateXp(RectTransform fill, TextMeshProUGUI lvlLbl, TextMeshProUGUI xpLbl,
            TextMeshProUGUI starLbl, int startXp, int endXp)
        {
            int StarOf(int lvl) => Mathf.Clamp(1 + (lvl - 1) / 2, 1, ProgressStore.MaxStar);
            void Apply(int xp)
            {
                int lvl = ProgressStore.LevelFromXp(xp);
                int floor = ProgressStore.XpFloorForLevel(lvl);
                int next = ProgressStore.XpFloorForLevel(lvl + 1);
                float frac = next > floor ? (xp - floor) / (float)(next - floor) : 1f;
                if (fill != null) fill.anchorMax = new Vector2(Mathf.Clamp01(frac), 1f);
                if (lvlLbl != null) lvlLbl.text = $"Cấp {lvl}";
                if (xpLbl != null) xpLbl.text = next > floor ? $"{xp - floor}/{next - floor} XP" : "TỐI ĐA";
            }

            int shownStar = StarOf(ProgressStore.LevelFromXp(startXp));
            float dur = startXp == endXp ? 0.01f : 1.4f;
            float e = 0f;
            while (e < dur)
            {
                e += Time.deltaTime;
                float t = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(e / dur));
                int xp = Mathf.RoundToInt(Mathf.Lerp(startXp, endXp, t));
                Apply(xp);
                int star = StarOf(ProgressStore.LevelFromXp(xp));
                if (star > shownStar) { shownStar = star; if (starLbl != null) starLbl.text = $"LÊN SAO {star}! (mở thêm ô trang bị / sức mạnh)"; }
                yield return null;
            }
            Apply(endXp);
        }

        /// <summary>Bắt đầu hành trình mới: có champion → màn chọn champion; không → tạo map luôn.</summary>
        // Về WORLD MAP (bản đồ ải) của tướng đang chơi.
        //   finished=true  → vừa hạ Boss: xoá run (không còn resume) → ải kế mở khoá.
        //   finished=false → rời giữa chừng: GIỮ run, có thể TIẾP TỤC qua hub.
        void GoToWorldMap(bool finished)
        {
            var champ = CampaignRun.champion;
            int idx = (champ != null && campaign.champions != null) ? campaign.champions.IndexOf(champ) : -1;
            int star = champ != null ? ProgressStore.GetStar(champ.championName) : 1;

            if (finished)
            {
                CampaignContext.map = null;
                CampaignContext.currentNodeId = -1;
                RunSaveStore.Clear();
            }
            Close();                       // ẩn run map
            if (idx < 0) return;           // no-champion mode → chỉ về lobby
            _focusIdx = idx; _champTab = 0;
            ShowChampionSelect();          // dựng hub làm nền
            ShowWorldMap(idx, star);       // mở thẳng bản đồ ải
        }

        void BeginNewRun()
        {
            if (campaign.champions != null && campaign.champions.Count > 0)
                ShowChampionSelect();
            else
            {
                StartNewRunNoChampion();
                OpenMap();
            }
        }

        void StartNewRunNoChampion()
        {
            CampaignContext.campaign = campaign; // để powerPool/itemPool sẵn sàng
            CampaignRun.ResetRun();
            CampaignContext.pendingRelic = false;
            CampaignContext.map = GenerateMapForRun();
            CampaignContext.currentNodeId = -1;
            RunSaveStore.Save(); // Mốc 6
        }

        // M1: độ dài/mini-boss theo ải đang chọn; chưa có ải → dùng mapColumns cũ.
        RunMap GenerateMapForRun()
        {
            var adv = CampaignRun.adventure;
            int cols = adv != null ? adv.columns : mapColumns;
            bool mini = adv != null;
            // Ải Power mở đầu chỉ khi có ≥1 power trong kho (không thì bỏ, tránh node rỗng).
            bool startPower = adv != null && campaign.powerCores != null && campaign.powerCores.Count > 0;
            return RunMap.Generate(System.Environment.TickCount, cols, campaign.levels.Count, mini, startPower);
        }

        void StartRunWith(ChampionData champ)
        {
            CampaignContext.campaign = campaign; // để StartRun đọc itemPool/powerPool
            if (_selectedAdventure != null)
                CampaignRun.StartRun(champ, _selectedAdventure); // M1: chơi ải (độ khó theo sao)
            else
                CampaignRun.StartRun(champ, _difficulty);        // fallback 3 độ khó
            CampaignContext.pendingRelic = false;
            CampaignContext.map = GenerateMapForRun();
            CampaignContext.currentNodeId = -1;
            RunSaveStore.Save(); // Mốc 6
            OpenMap();
        }

        // ── Overlay khung ─────────────────────────────────────────
        void BuildOverlay()
        {
            var go = new GameObject("RunMapCanvas");
            var canvas = go.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 610; // trên lobby menu (400), dưới Xưởng Deck (600)? đặt cao để nổi
            go.AddComponent<GraphicRaycaster>();
            _overlay = go;

            var root = go.GetComponent<RectTransform>();
            var dim = MakeRect("Dim", root, Vector2.zero, Vector2.one);
            SetImage(dim, overlayColor);

            // AMPHOREUS — nền TRỜI ĐÊM (obsidian + quầng vàng) + sao.
            var panel = MakeRect("Panel", dim, new Vector2(0.05f, 0.07f), new Vector2(0.95f, 0.93f));
            var frame = panel.gameObject.AddComponent<Image>();
            frame.color = PoCTheme.AmGold; frame.raycastTarget = true;   // viền vàng mảnh
            var skyRT = MakeRect("Sky", panel, Vector2.zero, Vector2.one);
            skyRT.offsetMin = new Vector2(2f, 2f); skyRT.offsetMax = new Vector2(-2f, -2f);
            var sky = skyRT.gameObject.AddComponent<Image>();
            sky.sprite = PoCTheme.NightSky(); sky.type = Image.Type.Simple; sky.color = Color.white; sky.raycastTarget = false;
            ScatterStars(skyRT, 64);

            // Đầu đề: chữ VÀNG lớn + phụ đề AMPHOREUS + gạch vàng (KHÔNG viền hoa văn).
            SpawnLabel(panel, "CON ĐƯỜNG ANH HÙNG", 26f, PoCTheme.AmGoldHi, TextAlignmentOptions.Center, true,
                new Vector2(0.15f, 0.925f), new Vector2(0.85f, 0.99f));
            SpawnLabel(panel, "✦  AMPHOREUS  ✦", 11f, PoCTheme.AmDim, TextAlignmentOptions.Center, true,
                new Vector2(0.30f, 0.902f), new Vector2(0.70f, 0.93f));
            var rule = MakeRect("Rule", panel, new Vector2(0.37f, 0.898f), new Vector2(0.63f, 0.902f));
            SetImage(rule, PoCTheme.A(PoCTheme.AmGold, 0.7f)).raycastTarget = false;

            // THANH TRẠNG THÁI to (Nexus + Vàng + Lõi) — dựng lại mỗi Render.
            _statusRoot = MakeRect("Status", panel, new Vector2(0.04f, 0.822f), new Vector2(0.96f, 0.888f));
            // Dòng phụ (champion / ải / sức mạnh) nhỏ, mờ.
            _infoLabel = SpawnLabel(MakeRect("Info", panel, new Vector2(0.05f, 0.784f), new Vector2(0.95f, 0.818f)),
                "", 12f, PoCTheme.A(PoCTheme.AmCream, 0.82f), TextAlignmentOptions.Center, false);

            // Vùng vẽ map
            _mapArea = MakeRect("MapArea", panel, new Vector2(0.03f, 0.115f), new Vector2(0.97f, 0.775f));

            // ← MENU: GÓC TRÊN-PHẢI (đồng nhất mọi màn). Về lobby.
            MakeBtn(MakeRect("BackBtn", panel, new Vector2(0.855f, 0.928f), new Vector2(0.985f, 0.988f)),
                PoCTheme.NodeNavy, "← MENU", 13f, Close);
            // ← BẢN ĐỒ ẢI (lên world map) — nav riêng, để đáy-trái.
            MakeBtn(MakeRect("ToMapBtn", panel, new Vector2(0.03f, 0.02f), new Vector2(0.26f, 0.095f)),
                PoCTheme.Lerp(PoCTheme.AmGold, PoCTheme.Obsid, 0.35f), "← BẢN ĐỒ ẢI", 14f, () => GoToWorldMap(false));

            // M12: KẾT THÚC SỚM — dừng ải khó, tổng kết (nhận Vàng→Lõi + XP đã kiếm) rồi về phát triển,
            // đánh lại thay vì kẹt. Xác nhận 2 lần tránh bấm nhầm.
            var endRT = MakeRect("EndBtn", panel, new Vector2(0.375f, 0.02f), new Vector2(0.625f, 0.095f));
            var endImg = RoundImg(endRT, PoCTheme.A(PoCTheme.AmRed, 0.85f));
            var endBtn = endRT.gameObject.AddComponent<Button>(); endBtn.targetGraphic = endImg;
            _endLbl = SpawnLabel(endRT, "KẾT THÚC SỚM", 13f, Color.white, TextAlignmentOptions.Center, true);
            endBtn.onClick.AddListener(() =>
            {
                if (!_endArmed) { _endArmed = true; _endLbl.text = "BẤM LẦN NỮA ĐỂ KẾT THÚC"; return; }
                _endArmed = false; ShowRunSummary();
            });
        }

        // AMPHOREUS — rải sao nền (deterministic để không nhấp nháy mỗi lần mở).
        void ScatterStars(RectTransform parent, int count)
        {
            var rng = new System.Random(20250101);
            for (int i = 0; i < count; i++)
            {
                float x = (float)rng.NextDouble(), y = (float)rng.NextDouble();
                float s = 1.5f + (float)rng.NextDouble() * 2.6f;
                float a = 0.22f + (float)rng.NextDouble() * 0.62f;
                var go = new GameObject("star");
                var rt = go.AddComponent<RectTransform>();
                rt.SetParent(parent, false);
                rt.anchorMin = rt.anchorMax = new Vector2(x, y);
                rt.pivot = new Vector2(0.5f, 0.5f);
                rt.sizeDelta = new Vector2(s, s);
                var img = go.AddComponent<Image>();
                img.sprite = PoCTheme.Disc();
                img.color = PoCTheme.A(i % 6 == 0 ? PoCTheme.AmGold : PoCTheme.AmCream, a);
                img.raycastTarget = false;
            }
        }

        // M13 — THANH TRẠNG THÁI to: bar máu Nexus (đỏ) + số lớn · chip Vàng · chip Lõi · champion.
        void BuildStatusChips()
        {
            if (_statusRoot == null) return;
            for (int i = _statusRoot.childCount - 1; i >= 0; i--) Destroy(_statusRoot.GetChild(i).gameObject);
            var root = _statusRoot;

            // NEXUS — nền chip + nhãn + bar + số lớn.
            PoCTheme.Panel(root, "nx", new Vector2(0f, 0.04f), new Vector2(0.46f, 0.96f),
                PoCTheme.A(PoCTheme.PlaqNavy, 0.94f), PoCTheme.A(PoCTheme.AmRed, 0.55f));
            SpawnLabel(root, "NEXUS", 11f, PoCTheme.A(PoCTheme.AmCream, 0.8f), TextAlignmentOptions.MidlineLeft, true,
                new Vector2(0.025f, 0.5f), new Vector2(0.18f, 0.95f));
            if (CampaignRun.nexusInit && CampaignRun.nexusMax > 0)
            {
                var tr = MakeRect("nxTrack", root, new Vector2(0.025f, 0.13f), new Vector2(0.30f, 0.47f));
                RoundImg(tr, PoCTheme.Hex("2A1220")).raycastTarget = false;
                float frac = Mathf.Clamp01((float)CampaignRun.nexusCurrent / CampaignRun.nexusMax);
                if (frac > 0.001f)
                {
                    var fl = MakeRect("nxFill", tr, Vector2.zero, new Vector2(frac, 1f));
                    RoundImg(fl, frac > 0.33f ? PoCTheme.Hex("E24A5E") : PoCTheme.Hex("E0A030")).raycastTarget = false;
                }
                SpawnLabel(root, $"<b>{CampaignRun.nexusCurrent}/{CampaignRun.nexusMax}</b>", 19f, PoCTheme.Hex("FFD3D9"),
                    TextAlignmentOptions.MidlineRight, true, new Vector2(0.31f, 0.4f), new Vector2(0.45f, 0.99f));
            }
            else SpawnLabel(root, "—", 16f, PoCTheme.AmDim, TextAlignmentOptions.MidlineLeft, true,
                new Vector2(0.20f, 0.1f), new Vector2(0.45f, 0.9f));

            // VÀNG / LÕI
            StatusChip(root, 0.485f, 0.65f, PoCTheme.AmGold, "Vàng", CampaignRun.gold.ToString());
            StatusChip(root, 0.665f, 0.83f, PoCTheme.Cyan, "Lõi", ProgressStore.Cores.ToString());

            // Champion + sao (phải)
            if (CampaignRun.champion != null)
                SpawnLabel(root, $"<b><color=#F5D98A>{CampaignRun.champion.championName}</color></b>  <color=#E7B24B>{CampaignRun.championStar} Sao</color>",
                    12f, Color.white, TextAlignmentOptions.MidlineRight, true, new Vector2(0.84f, 0.1f), new Vector2(1f, 0.9f));
        }

        void StatusChip(RectTransform root, float x0, float x1, Color iconCol, string label, string value)
        {
            PoCTheme.Panel(root, "chip_" + label, new Vector2(x0, 0.04f), new Vector2(x1, 0.96f),
                PoCTheme.A(PoCTheme.PlaqNavy, 0.94f), PoCTheme.A(iconCol, 0.5f));
            PoCTheme.Put(root, "d_" + label, new Vector2(x0 + 0.012f, 0.34f), new Vector2(x0 + 0.055f, 0.76f), PoCTheme.Diamond(), iconCol);
            SpawnLabel(root, $"<b>{value}</b>  <size=58%><color=#9FB0D0>{label}</color></size>", 18f, iconCol,
                TextAlignmentOptions.MidlineLeft, true, new Vector2(x0 + 0.062f, 0.06f), new Vector2(x1 - 0.01f, 0.94f));
        }

        // ── Vẽ map ────────────────────────────────────────────────
        void Render()
        {
            if (_mapArea == null) return;
            // BẢO ĐẢM kho trang bị luôn tới được khi map hiện (CampaignContext có thể null nếu vào map
            // qua luồng không set context) → gán context + library override từ field campaign trực tiếp.
            if (campaign != null)
            {
                if (CampaignContext.campaign == null) CampaignContext.campaign = campaign;
                CardItem.SetLibrary(campaign.cardItemLibrary);
            }
            if (_endLbl != null) { _endArmed = false; _endLbl.text = "KẾT THÚC SỚM"; } // reset xác nhận
            for (int i = _mapArea.childCount - 1; i >= 0; i--)
                Destroy(_mapArea.GetChild(i).gameObject);

            var map = CampaignContext.map;
            if (map == null) return;

            bool finished = map.IsFinished();
            string champInfo = CampaignRun.champion != null
                ? $"<color=#E7B24B>{CampaignRun.champion.championName}</color> <color=#E7B24B>{CampaignRun.championStar} Sao</color> <size=88%><color=#8AA4BC>(Mastery {ProgressStore.GetMasteryLevel(CampaignRun.champion.championName)})</color></size>   "
                : "";
            string relicInfo = CampaignRun.Relics.Count > 0
                ? "    <color=#5FCFD2>Cổ vật:</color> " + CampaignRun.RelicSummary() : "";
            string towerInfo = CampaignRun.EnemyPowers.Count > 0
                ? "    <color=#E8836B>Tháp:</color> " + CampaignRun.EnemySummary() : "";
            string[] diffName = { "", "Thường", "Khó", "Ác Mộng" };
            string diffTxt = CampaignRun.adventure != null
                ? $"{CampaignRun.adventure.adventureName} — {CampaignRun.adventure.StarLabel()}"
                : $"Độ khó: {diffName[Mathf.Clamp(CampaignRun.difficulty, 1, 3)]}";
            // Dòng phụ: champion + ải + sức mạnh (Vàng/Lõi/Nexus chuyển sang THANH TRẠNG THÁI to bên dưới).
            _infoLabel.text = champInfo + (finished ? "<color=#59D97A>HOÀN THÀNH!</color>  " : "")
                + $"<color=#C89BF5>{diffTxt}</color>    <color=#F0C75A>Sức mạnh:</color> " + CampaignRun.Summary() + relicInfo + towerInfo;
            BuildStatusChips();

            var availIds = new HashSet<int>();
            foreach (var a in map.Available()) availIds.Add(a.id);

            int cols = Mathf.Max(1, map.columns);

            // Vẽ ĐƯỜNG NỐI trước (nằm dưới node) → nhìn ra "con đường" có nhánh.
            DrawConnectors(map, cols);

            foreach (var n in map.nodes)
            {
                bool avail = availIds.Contains(n.id);
                MakeNode(n, NodeNX(n, cols), NodeNY(n), avail);
            }
        }

        // Toạ độ chuẩn hoá (0..1) của node trong MapArea
        static float NodeNX(MapNode n, int cols) => 0.04f + (n.col + 0.5f) / cols * 0.92f;
        static float NodeNY(MapNode n) => n.rowsInCol <= 1 ? 0.5f : 0.85f - (float)n.row / (n.rowsInCol - 1) * 0.70f;

        void DrawConnectors(RunMap map, int cols)
        {
            Canvas.ForceUpdateCanvases(); // đảm bảo _mapArea.rect đã có kích thước thật
            float W = _mapArea.rect.width, H = _mapArea.rect.height;
            if (W < 1f || H < 1f) return;

            foreach (var n in map.nodes)
            {
                Vector2 a = new Vector2((NodeNX(n, cols) - 0.5f) * W, (NodeNY(n) - 0.5f) * H);
                foreach (var e in n.next)
                {
                    var m = map.Get(e);
                    if (m == null) continue;
                    Vector2 b = new Vector2((NodeNX(m, cols) - 0.5f) * W, (NodeNY(m) - 0.5f) * H);
                    MakeLine(a, b, n.done);
                }
            }
        }

        // V3 — đường NÉT CHẤM MỰC: rải chấm tròn dọc đoạn. Đã qua = chấm vàng đậm.
        void MakeLine(Vector2 a, Vector2 b, bool done)
        {
            Vector2 dir = b - a;
            float len = dir.magnitude;
            if (len < 1f) return;
            int dots = Mathf.Max(2, Mathf.RoundToInt(len / 15f));
            Color col = done ? PoCTheme.AmGoldHi : PoCTheme.A(PoCTheme.AmGold, 0.32f); // tia sao vàng
            float sz = done ? 5.5f : 3.5f;
            for (int i = 0; i <= dots; i++)
            {
                Vector2 p = a + dir * (i / (float)dots);
                var go = new GameObject("dot");
                var rt = go.AddComponent<RectTransform>();
                rt.SetParent(_mapArea, false);
                rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 0.5f);
                rt.pivot = new Vector2(0.5f, 0.5f);
                rt.anchoredPosition = p;
                rt.sizeDelta = new Vector2(sz, sz);
                var img = go.AddComponent<Image>();
                img.sprite = PoCTheme.Disc(); img.color = col; img.raycastTarget = false;
                rt.SetAsFirstSibling(); // chấm nằm DƯỚI node
            }
        }

        Color TypeColor(MapNodeType t) =>
            t == MapNodeType.Boss ? bossColor :
            t == MapNodeType.Elite ? eliteColor :
            t == MapNodeType.Reward ? rewardColor :
            t == MapNodeType.Shop ? shopColor :
            t == MapNodeType.Event ? eventColor :
            t == MapNodeType.Item ? itemColor :
            t == MapNodeType.Power ? new Color(0.78f, 0.42f, 0.92f) :
            t == MapNodeType.MiniBoss ? new Color(0.86f, 0.42f, 0.58f) : battleColor;

        static string NodeTitle(MapNodeType t) =>
            t == MapNodeType.Boss ? "BOSS" :
            t == MapNodeType.Elite ? "ELITE" :
            t == MapNodeType.Reward ? "THƯỞNG" :
            t == MapNodeType.Shop ? "CỬA HÀNG" :
            t == MapNodeType.Power ? "SỨC MẠNH" :
            t == MapNodeType.Event ? "SỰ KIỆN" :
            t == MapNodeType.Item ? "VẬT PHẨM" :
            t == MapNodeType.MiniBoss ? "MINI-BOSS" : "TRẬN";

        // AMPHOREUS — node = MEDALLION (viền vàng + mặt obsidian + sao) + biển tên đọc rõ.
        void MakeNode(MapNode n, float nx, float ny, bool avail)
        {
            var rt = new GameObject("Node_" + n.id).AddComponent<RectTransform>();
            rt.SetParent(_mapArea, false);
            rt.anchorMin = rt.anchorMax = new Vector2(nx, ny);
            rt.pivot = new Vector2(0.5f, 0.5f);
            rt.sizeDelta = new Vector2(98f, 98f);

            bool reward = n.type == MapNodeType.Reward;
            bool boss = n.type == MapNodeType.Boss || n.type == MapNodeType.MiniBoss;
            bool cur = avail && !n.done;                   // ải hiện tại
            bool dim = !avail && !n.done;                  // xa/khoá → mờ

            // Viền vàng chuẩn Amphoreus; loại quan trọng đổi màu viền (Boss đỏ).
            Color rim = n.done ? PoCTheme.AmGreen
                      : boss ? PoCTheme.AmRed
                      : (n.type == MapNodeType.Elite || reward) ? PoCTheme.AmGoldHi
                      : n.type == MapNodeType.Item ? PoCTheme.Hex("58C2B0")
                      : n.type == MapNodeType.Shop ? PoCTheme.Hex("E0A64E")
                      : n.type == MapNodeType.Event ? PoCTheme.Hex("7FC06A")
                      : n.type == MapNodeType.Power ? PoCTheme.Hex("C76BEB")
                      : PoCTheme.AmGold;                    // Trận
            Color fill = PoCTheme.NodeNavy;                 // mặt medallion tối
            Color starCol = PoCTheme.A(PoCTheme.AmGoldHi, 0.9f);
            if (cur) { rim = PoCTheme.Hex("FFE49A"); fill = PoCTheme.AmGold; starCol = PoCTheme.Hex("4A380E"); }
            if (dim) { rim = PoCTheme.A(rim, 0.5f); fill = PoCTheme.A(PoCTheme.NodeNavy, 0.7f); starCol = PoCTheme.A(PoCTheme.AmDim, 0.6f); }

            Sprite shape = reward ? PoCTheme.Diamond() : PoCTheme.Disc();
            float y0 = 0.44f, y1 = 1.00f;

            // Quầng sáng vàng cho ải hiện tại.
            if (cur)
                PoCTheme.Put(rt, "glow", new Vector2(0.06f, y0 - 0.06f), new Vector2(0.94f, y1 + 0.04f),
                    PoCTheme.Disc(), PoCTheme.A(PoCTheme.AmGoldHi, 0.28f));

            // Medallion: viền (to) + mặt (nhỏ hơn) + ngôi sao/coreflame giữa.
            PoCTheme.Put(rt, "rim", new Vector2(0.18f, y0), new Vector2(0.82f, y1), shape, rim);
            PoCTheme.Put(rt, "fill", new Vector2(0.235f, y0 + 0.05f), new Vector2(0.765f, y1 - 0.06f), shape, fill);
            // dấu giữa: đã qua = chấm xanh; còn lại = ngôi sao celestial.
            if (n.done)
                PoCTheme.Put(rt, "done", new Vector2(0.42f, 0.62f), new Vector2(0.58f, 0.80f), PoCTheme.Disc(), PoCTheme.AmGreen);
            else
                PoCTheme.Put(rt, "star", new Vector2(0.36f, 0.55f), new Vector2(0.64f, 0.87f), PoCTheme.Star(), starCol);

            // BIỂN TÊN nền obsidian + viền vàng mảnh → chữ kem LUÔN đọc rõ.
            var plq = MakeRect("plaque", rt, new Vector2(-0.14f, 0.03f), new Vector2(1.14f, 0.42f));
            RoundImg(plq, PoCTheme.A(PoCTheme.PlaqNavy, dim ? 0.6f : 0.94f)).raycastTarget = false;
            Color txt = dim ? PoCTheme.A(PoCTheme.AmCream, 0.6f) : PoCTheme.AmCream;
            SpawnLabel(plq, $"<b>{NodeTitle(n.type)}</b>", 12f, cur ? PoCTheme.AmGoldHi : txt,
                TextAlignmentOptions.Center, true, new Vector2(0.03f, 0.44f), new Vector2(0.97f, 0.98f));
            string sub = n.done ? "đã qua"
                       : n.type == MapNodeType.MiniBoss ? "hồi Nexus"
                       : n.type == MapNodeType.Reward ? "sức mạnh"
                       : n.type == MapNodeType.Shop ? "mua đồ"
                       : n.type == MapNodeType.Event ? "lựa chọn"
                       : n.type == MapNodeType.Item ? "gắn đồ"
                       : n.type == MapNodeType.Power ? "chọn lõi"
                       : (n.levelIndex >= 0 ? "Tầng " + (n.levelIndex + 1) : "");
            SpawnLabel(plq, sub, 9.5f, PoCTheme.A(txt, 0.8f), TextAlignmentOptions.Center, false,
                new Vector2(0.03f, 0.04f), new Vector2(0.97f, 0.46f));

            // Mũi tên + "ĐÁNH ĐÂY" trên ải hiện tại.
            if (cur)
            {
                PoCTheme.Put(rt, "arr", new Vector2(0.42f, y1 + 0.02f), new Vector2(0.58f, y1 + 0.18f),
                    PoCTheme.Triangle(), PoCTheme.Hex("FFD24A"), 180f);
                SpawnLabel(rt, "<b>ĐÁNH ĐÂY</b>", 11f, PoCTheme.Hex("FFD24A"), TextAlignmentOptions.Center, true,
                    new Vector2(-0.4f, y1 + 0.17f), new Vector2(1.4f, y1 + 0.40f));
            }

            if (cur)
            {
                var hit = SetImage(rt, new Color(1f, 1f, 1f, 0f)); // raycast trong suốt để bấm
                var btn = rt.gameObject.AddComponent<Button>();
                btn.targetGraphic = hit;
                int nodeId = n.id; var t = n.type; int lvl = n.levelIndex;
                btn.onClick.AddListener(() => OnNodeClicked(nodeId, t, lvl));
            }
        }

        void OnNodeClicked(int nodeId, MapNodeType type, int levelIndex)
        {
            var map = CampaignContext.map;
            if (map == null || !map.IsAvailable(nodeId)) return;

            if (type == MapNodeType.Reward) { ShowRewardPick(nodeId); return; }
            if (type == MapNodeType.Shop) { ShowShop(nodeId); return; }
            if (type == MapNodeType.Event) { ShowEvent(nodeId); return; }
            if (type == MapNodeType.Item) { ShowItemPick(nodeId); return; }
            if (type == MapNodeType.Power) { ShowPowerPick(nodeId); return; }

            // NODE CHIẾN ĐẤU → tab preview (chủ lực + luật trận) trước khi vào.
            System.Action enter = () =>
            {
                if (CampaignRun.champion != null && CampaignRun.RunDeck.Count > 0)
                    StartBattleDirect(nodeId, levelIndex);                 // champion: deck cố định
                else if (campaignView != null) { Close(); campaignView.OpenAtLevel(levelIndex, nodeId); }  // chọn bài
                else Debug.LogWarning("[RunMapView] campaignView chưa gán — không mở được màn chọn bài.");
            };
            ShowPreBattle(type, levelIndex, enter);
        }

        // ── TAB PREVIEW TRƯỚC TRẬN: chủ lực ĐỊCH (card mạnh nhất trong enemyDeck) + luật trận + nút vào ──
        void ShowPreBattle(MapNodeType type, int levelIndex, System.Action enter)
        {
            var picker = MakeRect("PreBattle", _overlay.GetComponent<RectTransform>(), Vector2.zero, Vector2.one);
            SetImage(picker, new Color(0f, 0f, 0f, 0.82f));
            var panel = MakeRect("P", picker, new Vector2(0.28f, 0.12f), new Vector2(0.72f, 0.90f));
            RoundImg(panel, panelColor);

            Color rc = TypeColor(type);
            SpawnLabel(panel, $"<b>{NodeTitle(type)}</b>", 22f, rc, TextAlignmentOptions.Center, true,
                new Vector2(0.05f, 0.905f), new Vector2(0.95f, 0.985f));
            SpawnLabel(panel, "CHỦ LỰC CỦA ĐỊCH", 12f, PoCTheme.Hex("E8836B"), TextAlignmentOptions.Center, false,
                new Vector2(0.05f, 0.86f), new Vector2(0.95f, 0.9f));

            // Card chủ lực ĐỊCH (mạnh nhất trong enemyDeck của tầng này) — art thật.
            var hero = EnemyHeroCard(levelIndex);
            if (hero != null)
            {
                var holder = MakeRect("Hero", panel, new Vector2(0.32f, 0.44f), new Vector2(0.68f, 0.855f));
                BuildCardBig(hero, holder);
            }
            else
                SpawnLabel(panel, "<color=#7f8fb0>(chưa gán enemyDeck cho tầng này)</color>", 12f, Color.white,
                    TextAlignmentOptions.Center, false, new Vector2(0.1f, 0.6f), new Vector2(0.9f, 0.7f));

            // Trang bị CƠ BẢN của đối thủ (thông tin, KHÔNG phải luật).
            string equip = CampaignRun.EnemyEquipPreviewText(type);
            if (!string.IsNullOrEmpty(equip))
                SpawnLabel(panel, $"<color=#7FE3C0>◆</color> {equip}", 12f, new Color(0.85f, 0.9f, 0.95f),
                    TextAlignmentOptions.Center, false, new Vector2(0.06f, 0.425f), new Vector2(0.94f, 0.47f))
                    .textWrappingMode = TextWrappingModes.Normal;

            // Luật/POWER ĐẶC BIỆT (preview, không áp) — chỉ hiện khi ải/boss có.
            string rule = CampaignRun.EncounterPreviewText(type);
            var box = MakeRect("Rule", panel, new Vector2(0.06f, 0.17f), new Vector2(0.94f, 0.41f));
            RoundImg(box, PoCTheme.A(PoCTheme.PlaqNavy, 0.85f)).raycastTarget = false;
            if (!string.IsNullOrEmpty(rule))
            {
                SpawnLabel(box, "<b>POWER ẢI / BOSS</b>", 12f, PoCTheme.Hex("E88C6E"), TextAlignmentOptions.Center, true,
                    new Vector2(0.04f, 0.82f), new Vector2(0.96f, 0.98f));
                SpawnLabel(box, rule, 12.5f, new Color(0.92f, 0.93f, 0.97f), TextAlignmentOptions.Center, false,
                    new Vector2(0.05f, 0.06f), new Vector2(0.95f, 0.8f)).textWrappingMode = TextWrappingModes.Normal;
            }
            else
                SpawnLabel(box, "Trận thường — không có power đặc biệt.", 12.5f, PoCTheme.AmDim,
                    TextAlignmentOptions.Center, false).textWrappingMode = TextWrappingModes.Normal;

            // Nút vào / đóng.
            var go = MakeRect("Go", panel, new Vector2(0.14f, 0.05f), new Vector2(0.55f, 0.14f));
            var gimg = RoundImg(go, PoCTheme.AmGold);
            SpawnLabel(go, "VÀO TRẬN", 15f, PoCTheme.Hex("1A1206"), TextAlignmentOptions.Center, true);
            var gb = go.gameObject.AddComponent<Button>(); gb.targetGraphic = gimg;
            gb.onClick.AddListener(() => { Destroy(picker.gameObject); enter(); });

            var cl = MakeRect("Close", panel, new Vector2(0.58f, 0.05f), new Vector2(0.86f, 0.14f));
            MakeBtn(cl, new Color(0.30f, 0.34f, 0.42f), "ĐÓNG", 14f, () => Destroy(picker.gameObject));
        }

        // Card CHỦ LỰC ĐỊCH: lá điểm cao nhất (base ATK+HP) trong enemyDeck của tầng này.
        CardData EnemyHeroCard(int levelIndex)
        {
            if (campaign.levels == null || campaign.levels.Count == 0) return null;
            int idx = Mathf.Clamp(levelIndex, 0, campaign.levels.Count - 1);
            var lv = campaign.levels[idx];
            var deck = lv != null ? lv.enemyDeck : null;
            if (deck == null || deck.cards == null) return null;
            CardData best = null; int bestScore = int.MinValue;
            foreach (var cd in deck.cards)
            {
                if (cd == null) continue;
                int score = cd.baseAttack + cd.baseHealth;
                if (cd.cardType == CardType.Unit) score += 2;   // ưu tiên đơn vị (chủ lực thường là quân)
                if (score > bestScore) { bestScore = score; best = cd; }
            }
            return best;
        }

        /// <summary>Vào trận bằng deck cố định của hành trình (champion mode) — không qua chọn bài.</summary>
        void StartBattleDirect(int nodeId, int levelIndex)
        {
            int idx = Mathf.Clamp(levelIndex, 0, campaign.levels.Count - 1);
            var lv = campaign.levels[idx];
            if (lv == null || lv.enemyDeck == null)
            {
                Debug.LogWarning("[RunMapView] Tầng/enemyDeck chưa gán — không vào trận được.");
                return;
            }

            var deck = ScriptableObject.CreateInstance<DeckData>();
            deck.deckName = CampaignRun.champion != null ? "Hành trình — " + CampaignRun.champion.championName : "Hành trình";
            deck.cards = new List<CardData>(CampaignRun.RunDeck);

            GameConfig.playerDeck = deck;
            GameConfig.enemyDeck = lv.enemyDeck;

            CampaignContext.campaign = campaign;
            CampaignContext.levelIndex = idx;
            CampaignContext.returnSceneName = SceneManager.GetActiveScene().name;
            CampaignContext.currentNodeId = nodeId;

            string scene = (campaignView != null && campaignView.lobby != null)
                ? campaignView.lobby.gameSceneName : "SampleScene";
            ScreenFade.LoadScene(scene); // chuyển cảnh mượt

        }

        // ── Màn CHỌN ANH HÙNG (kiểu Path of Champions) ────────────
        // Trái: danh sách anh hùng. Phải: chi tiết + tab (Tổng quan / Bộ bài / Cấp độ / Cổ vật / Trang bị) + nút Tham gia.
        void ShowChampionSelect()
        {
            if (_overlay != null) _overlay.SetActive(false);
            if (_champSel != null) Destroy(_champSel); // tránh chồng canvas khi đổi champion/tab

            _champSel = new GameObject("ChampionSelectCanvas");
            var canvas = _champSel.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 620;
            _champSel.AddComponent<GraphicRaycaster>();
            var root = _champSel.GetComponent<RectTransform>();

            SetImage(MakeRect("Dim", root, Vector2.zero, Vector2.one), overlayColor);
            var panel = MakeRect("Panel", root, new Vector2(0.05f, 0.05f), new Vector2(0.95f, 0.95f));
            RoundImg(panel, panelColor);

            var hdr = MakeRect("Header", panel, new Vector2(0.01f, 0.92f), new Vector2(0.99f, 0.985f));
            RoundImg(hdr, headerColor);
            SpawnLabel(hdr, "CON ĐƯỜNG ANH HÙNG", 24f, accentColor, TextAlignmentOptions.Center, true);

            // ← MENU ở GÓC TRÊN-PHẢI (về lobby).
            var menuRT = MakeRect("MenuTop", hdr, new Vector2(0.865f, 0.16f), new Vector2(0.99f, 0.84f));
            MakeBtn(menuRT, new Color(0.40f, 0.40f, 0.45f, 0.95f), "← MENU", 13f,
                () => { Destroy(_champSel); Close(); });

            // ── DANH SÁCH anh hùng (trái) — chừa đáy 0.075 cho thanh điều khiển ──
            var listBg = MakeRect("ListBg", panel, new Vector2(0.015f, 0.075f), new Vector2(0.24f, 0.905f));
            RoundImg(listBg, new Color(0.06f, 0.08f, 0.12f, 0.95f));

            var champs = campaign.champions;
            int count = 0; foreach (var c in champs) if (c != null) count++;
            float rowH = Mathf.Min(0.11f, 0.86f / Mathf.Max(1, count));
            int shown = 0;
            for (int i = 0; i < champs.Count; i++)
            {
                var champ = champs[i]; if (champ == null) continue;
                int idx = i;
                float y1 = 0.965f - shown * (rowH + 0.008f);
                var row = MakeRect("C_" + i, listBg, new Vector2(0.04f, y1 - rowH), new Vector2(0.96f, y1));
                bool focused = i == _focusIdx;
                var rimg = RoundImg(row, focused ? new Color(0.20f, 0.28f, 0.44f, 0.98f) : new Color(0.12f, 0.15f, 0.22f, 0.95f));
                var rbtn = row.gameObject.AddComponent<Button>(); rbtn.targetGraphic = rimg;
                int star = ProgressStore.GetStar(champ.championName);
                SpawnLabel(row, $"<b>{champ.championName}</b>\n<size=72%><color=#F0C75A>{StarStr(star)}</color> <color=#9AA7B8>Lv {ProgressStore.GetMasteryLevel(champ.championName)}</color></size>",
                    15f, Color.white, TextAlignmentOptions.Left, false, new Vector2(0.08f, 0.05f), new Vector2(0.96f, 0.95f));
                rbtn.onClick.AddListener(() => { _focusIdx = idx; _champTab = 0; ShowChampionSelect(); });
                shown++;
            }

            // ── CHI TIẾT (phải) — chừa đáy 0.075 ──
            _champDetail = MakeRect("Detail", panel, new Vector2(0.25f, 0.075f), new Vector2(0.985f, 0.905f));
            _focusIdx = Mathf.Clamp(_focusIdx, 0, Mathf.Max(0, champs.Count - 1));
            RenderChampDetail(_focusIdx);

            // ── THANH ĐÁY: chỉ khi đang có ải dở → cảnh báo + KẾT THÚC ẢI (trái/giữa) + TIẾP TỤC (phải) ──
            if (CampaignContext.map != null)
            {
                string advName = CampaignRun.adventure != null ? CampaignRun.adventure.adventureName
                    : (CampaignRun.champion != null ? CampaignRun.champion.championName : "hành trình dở");

                var warnRT = MakeRect("EditWarn", panel, new Vector2(0.015f, 0.012f), new Vector2(0.40f, 0.062f));
                RoundImg(warnRT, new Color(0.42f, 0.20f, 0.10f, 0.96f));
                SpawnLabel(warnRT, "⚠ Sửa deck/trang bị = KẾT THÚC ải", 11f, new Color(1f, 0.86f, 0.6f),
                    TextAlignmentOptions.Center, false, new Vector2(0.03f, 0f), new Vector2(0.97f, 1f));

                var endE = MakeRect("EndForEdit", panel, new Vector2(0.41f, 0.012f), new Vector2(0.575f, 0.062f));
                var endEImg = RoundImg(endE, new Color(0.72f, 0.24f, 0.24f, 0.98f));
                var endEBtn = endE.gameObject.AddComponent<Button>(); endEBtn.targetGraphic = endEImg;
                var endELbl = SpawnLabel(endE, "KẾT THÚC ẢI", 12f, Color.white, TextAlignmentOptions.Center, true);
                bool endArmedEdit = false;
                endEBtn.onClick.AddListener(() =>
                {
                    if (!endArmedEdit) { endArmedEdit = true; endELbl.text = "BẤM LẦN NỮA"; return; }
                    CampaignContext.map = null; RunSaveStore.Clear();   // bỏ run (giữ nhân vật + tiến độ đã bank)
                    ShowChampionSelect();                               // vẽ lại: hết cảnh báo, chỉnh tự do
                });

                SpawnLabel(panel, $"<size=80%><color=#9AD9AE>Đang dở: {advName}</color></size>", 11f, Color.white,
                    TextAlignmentOptions.MidlineRight, false, new Vector2(0.59f, 0.02f), new Vector2(0.795f, 0.055f));
                var resRT = MakeRect("Resume", panel, new Vector2(0.80f, 0.012f), new Vector2(0.985f, 0.062f));
                MakeBtn(resRT, new Color(0.24f, 0.62f, 0.38f, 0.98f), "▶ TIẾP TỤC ẢI", 13f,
                    () => { Destroy(_champSel); OpenMap(); });
            }
        }

        static string StarStr(int star)
        {
            star = Mathf.Clamp(star, 0, ProgressStore.MaxStar);
            return new string('★', star) + new string('☆', ProgressStore.MaxStar - star);
        }

        void RenderChampDetail(int idx)
        {
            if (_champDetail == null) return;
            for (int k = _champDetail.childCount - 1; k >= 0; k--) Destroy(_champDetail.GetChild(k).gameObject);
            var champs = campaign.champions;
            if (idx < 0 || idx >= champs.Count || champs[idx] == null) return;
            var champ = champs[idx];
            ResolveChampionDeck(champ); // nạp deck tùy chỉnh đã lưu vào cache (cho RebuildRunDeck)

            int star = ProgressStore.GetStar(champ.championName);
            int mastery = ProgressStore.GetMasteryLevel(champ.championName);

            // Header chi tiết: tên + sao + level
            SpawnLabel(_champDetail, $"<b>{champ.championName}</b>", 26f, Color.white, TextAlignmentOptions.Left, true,
                new Vector2(0.02f, 0.90f), new Vector2(0.7f, 0.99f));
            SpawnLabel(_champDetail, $"<color=#F0C75A><size=120%>{StarStr(star)}</size></color>   <color=#9AA7B8>Cấp anh hùng {mastery}</color>",
                16f, Color.white, TextAlignmentOptions.Left, false, new Vector2(0.02f, 0.84f), new Vector2(0.7f, 0.90f));

            // Thẻ champion = card thật, phóng to lấp khung phải
            BuildChampCard(champ, new Vector2(0.71f, 0.26f), new Vector2(0.995f, 0.90f));

            // Tabs (thứ tự giống PoC)
            string[] tabs = { "TỔNG QUAN", "TINH HỒN", "CẤP ĐỘ", "BỘ BÀI", "TRANG BỊ" };
            float tw = 0.66f / tabs.Length;
            for (int t = 0; t < tabs.Length; t++)
            {
                int tt = t;
                float x0 = 0.02f + t * tw;
                var tab = MakeRect("Tab_" + t, _champDetail, new Vector2(x0, 0.74f), new Vector2(x0 + tw - 0.006f, 0.80f));
                bool on = t == _champTab;
                var timg = RoundImg(tab, on ? accentColor : new Color(0.16f, 0.20f, 0.28f, 0.95f));
                var tbtn = tab.gameObject.AddComponent<Button>(); tbtn.targetGraphic = timg;
                SpawnLabel(tab, tabs[t], 12.5f, on ? new Color(0.1f, 0.1f, 0.12f) : Color.white, TextAlignmentOptions.Center, true);
                tbtn.onClick.AddListener(() => { _champTab = tt; RenderChampDetail(idx); });
            }

            // Nội dung tab (khung giữa)
            var content = MakeRect("Content", _champDetail, new Vector2(0.02f, 0.10f), new Vector2(0.70f, 0.72f));
            RoundImg(content, new Color(0.07f, 0.09f, 0.13f, 0.9f));
            // Cô lập: tab lỗi thì log & bỏ qua, KHÔNG làm hỏng phần còn lại (nút THAM GIA vẫn dựng).
            try { RenderTab(content, idx, champ, star, mastery); }
            catch (System.Exception ex) { Debug.LogError($"[RunMapView] Lỗi dựng tab {_champTab}: {ex}"); }

            // M1: có ẢI → chọn ải (độ khó theo sao). Chưa có ải → 3 độ khó cũ. (bọc try để không rớt nút START)
            try
            {
                if (campaign.adventures != null && campaign.adventures.Count > 0)
                {
                    if (_selectedAdventure == null || !campaign.adventures.Contains(_selectedAdventure))
                        _selectedAdventure = TargetAdventure(champ.championName);
                    SpawnLabel(_champDetail,
                        $"<b>ẢI:</b> {_selectedAdventure.adventureName}   <color=#F0C75A>{_selectedAdventure.StarLabel()}</color>",
                        11.5f, Color.white, TextAlignmentOptions.Center, true, new Vector2(0.72f, 0.305f), new Vector2(0.99f, 0.36f));
                    var chg = MakeRect("ChgAdv", _champDetail, new Vector2(0.72f, 0.245f), new Vector2(0.99f, 0.30f));
                    MakeBtn(chg, new Color(0.24f, 0.36f, 0.52f, 0.98f), "▸ BẢN ĐỒ ẢI", 12f, () => ShowWorldMap(idx, star));
                }
                else
                {
                    SpawnLabel(_champDetail, "ĐỘ KHÓ", 12f, new Color(0.8f, 0.84f, 0.92f), TextAlignmentOptions.Center, true,
                        new Vector2(0.72f, 0.30f), new Vector2(0.99f, 0.35f));
                    string[] dn = { "", "Thường", "Khó", "Ác Mộng" };
                    float dw = 0.27f / 3f;
                    for (int d = 1; d <= 3; d++)
                    {
                        int dd = d;
                        float x0 = 0.72f + (d - 1) * dw;
                        var drt = MakeRect("Diff_" + d, _champDetail, new Vector2(x0, 0.24f), new Vector2(x0 + dw - 0.006f, 0.295f));
                        bool on = _difficulty == d;
                        var dimg = RoundImg(drt, on ? accentColor : new Color(0.16f, 0.20f, 0.28f, 0.95f));
                        var dbtn = drt.gameObject.AddComponent<Button>(); dbtn.targetGraphic = dimg;
                        SpawnLabel(drt, dn[d], 11.5f, on ? new Color(0.1f, 0.1f, 0.12f) : Color.white, TextAlignmentOptions.Center, true);
                        dbtn.onClick.AddListener(() => { _difficulty = dd; RenderChampDetail(idx); });
                    }
                }
            }
            catch (System.Exception ex) { Debug.LogError($"[RunMapView] Lỗi dựng chọn ải/độ khó: {ex}"); }

            // Nút THAM GIA — LUÔN dựng (dù phần trên lỗi). Có ải thì mở BẢN ĐỒ; chưa có thì vào thẳng.
            var start = MakeRect("Start", _champDetail, new Vector2(0.72f, 0.10f), new Vector2(0.99f, 0.22f));
            if (campaign.adventures != null && campaign.adventures.Count > 0)
                MakeBtn(start, new Color(0.62f, 0.44f, 0.16f, 0.98f), "PHIÊU LƯU ▸", 16f, () => ShowWorldMap(idx, star));
            else
                MakeBtn(start, new Color(0.62f, 0.44f, 0.16f, 0.98f), "THAM GIA PHIÊU LƯU", 16f,
                    () => { var picked = champ; Destroy(_champSel); StartRunWith(picked); });
        }

        void RenderTab(RectTransform box, int idx, ChampionData champ, int star, int mastery)
        {
            switch (_champTab)
            {
                case 1: RenderConstellationTab(box, idx, champ); break;
                case 2: RenderLevelTab(box, champ, star, mastery); break;
                case 3: RenderDeckTab(box, champ); break;
                case 4: RenderItemsTab(box, idx, champ, mastery); break; // gộp Cổ Vật + Trang Bị
                default: RenderOverviewTab(box, champ); break;
            }
        }

        void RenderOverviewTab(RectTransform box, ChampionData champ)
        {
            string sig = (champ.signaturePower != null && champ.signaturePower.IsMeaningful)
                ? $"\n\n<b><color=#F0C75A>Sức mạnh chiêu bài:</color></b> {champ.signaturePower.name}\n<size=85%>{champ.signaturePower.desc}</size>" : "";
            SpawnLabel(box, (string.IsNullOrWhiteSpace(champ.description) ? "Chưa có mô tả." : champ.description) + sig,
                14f, Color.white, TextAlignmentOptions.TopLeft, false,
                new Vector2(0.05f, 0.05f), new Vector2(0.95f, 0.95f)).textWrappingMode = TextWrappingModes.Normal;
        }

        // Kho bài của champion: lá champion + starterDeck + lá đã mở khóa (CardLibrary). KHÔNG tự do như PvP.
        List<CardData> ChampionCardPool(ChampionData champ)
        {
            var pool = new List<CardData>();
            var seen = new HashSet<string>();
            void Add(CardData c) { if (c != null && seen.Add(c.cardName)) pool.Add(c); }
            Add(champ.championCard);
            if (champ.starterDeck != null && champ.starterDeck.cards != null)
                foreach (var c in champ.starterDeck.cards) Add(c);
            // Toàn bộ thư viện (giống Xưởng PvP): mọi lá không phải generated. KHÔNG lọc unlock
            // để có kho đầy đủ mà build. (Vẫn tách riêng: lưu deck theo tướng, 15 lá/1 bản sao.)
            var lib = LibraryRef();
            if (lib != null && lib.allCards != null)
                foreach (var c in lib.allCards)
                    if (c != null && !c.isGenerated) Add(c);
            return pool;
        }

        // Nạp deck đã lưu (names → CardData) vào cache cho RebuildRunDeck.
        void ResolveChampionDeck(ChampionData champ)
        {
            if (champ == null || !ChampionDeckStore.HasSaved(champ.championName)) return;
            var lookup = new Dictionary<string, CardData>();
            foreach (var c in ChampionCardPool(champ)) if (!lookup.ContainsKey(c.cardName)) lookup[c.cardName] = c;
            var resolved = new List<CardData>();
            foreach (var nm in ChampionDeckStore.GetNames(champ.championName))
                if (lookup.TryGetValue(nm, out var cd)) resolved.Add(cd);
            if (resolved.Count > 0) ChampionDeckStore.SetResolved(champ.championName, resolved);
        }

        void EnsureEditDeck(ChampionData champ)
        {
            if (_editDeck != null && _editDeckChamp == champ.championName) return;
            _editDeckChamp = champ.championName;
            _editDeck = new List<CardData>();
            var seen = new HashSet<string>();
            List<CardData> src = null;
            if (ChampionDeckStore.TryGetResolved(champ.championName, out var custom) && custom != null && custom.Count > 0) src = custom;
            else if (champ.starterDeck != null) src = champ.starterDeck.cards;
            if (src != null)
                foreach (var c in src)
                    if (c != null && seen.Add(c.cardName) && _editDeck.Count < 15) _editDeck.Add(c);

            // Lá champion LUÔN có trong deck.
            if (champ.championCard != null && !_editDeck.Exists(x => x != null && x.cardName == champ.championCard.cardName))
            {
                if (_editDeck.Count >= 15) _editDeck.RemoveAt(_editDeck.Count - 1);
                _editDeck.Insert(0, champ.championCard);
            }
        }

        void RenderDeckTab(RectTransform box, ChampionData champ)
        {
            EnsureEditDeck(champ);
            int ci = campaign.champions.IndexOf(champ);

            SpawnLabel(box, $"<b>BỘ BÀI CHAMPION</b>   <color=#F0C75A>{_editDeck.Count}/15</color> lá · 1 bản sao",
                14.5f, Color.white, TextAlignmentOptions.Left, true,
                new Vector2(0.03f, 0.925f), new Vector2(0.58f, 0.99f));

            var open = MakeRect("OpenEditor", box, new Vector2(0.59f, 0.925f), new Vector2(0.985f, 0.99f));
            MakeBtn(open, new Color(0.86f, 0.62f, 0.24f, 0.98f), "SỬA DECK Ở XƯỞNG", 12.5f, () =>
            {
                _editDeckChamp = null; // nạp lại deck khi quay lại tab
                DeckBuilder().OpenForChampion(champ.championName, ChampionCardPool(champ), _editDeck,
                    champ.championCard, () => RenderChampDetail(ci));
            });

            // Lưới CARD THẬT (prefab CardView — đủ thiết kế + keyword). Bấm 1 lá = ZOOM.
            RenderRealCardGrid(box, _editDeck, 0.02f, 0.99f, 0.90f, 0.02f, 5, cd => ShowCardZoom(cd));
        }

        // Lưới card THẬT dùng prefab CardView (giống thư viện Xưởng). Bấm 1 lá → onClick(card).
        void RenderRealCardGrid(RectTransform box, List<CardData> cards,
            float xL, float xR, float yTop, float yBot, int cols, System.Action<CardData> onClick)
        {
            if (cards == null || cards.Count == 0) return;
            float cw = (xR - xL) / cols;
            int rows = Mathf.Max(1, Mathf.CeilToInt(cards.Count / (float)cols));
            float ch = Mathf.Min(0.32f, (yTop - yBot) / rows);
            for (int i = 0; i < cards.Count; i++)
            {
                var cd = cards[i]; if (cd == null) continue;
                int col = i % cols, row = i / cols;
                float x0 = xL + col * cw + 0.004f, x1 = xL + (col + 1) * cw - 0.004f;
                float y1 = yTop - row * ch - 0.004f, y0 = y1 - ch + 0.008f;
                if (y0 < yBot) break;
                var holder = MakeRect("Card_" + i, box, new Vector2(x0, y0), new Vector2(x1, y1));
                BuildCardBig(cd, holder); // card thật + keyword
                if (onClick != null)
                {
                    var hit = MakeRect("Hit", holder, Vector2.zero, Vector2.one);
                    var himg = SetImage(hit, new Color(0f, 0f, 0f, 0f)); himg.raycastTarget = true;
                    var b = hit.gameObject.AddComponent<Button>(); b.targetGraphic = himg;
                    var cap = cd; b.onClick.AddListener(() => onClick(cap));
                }
            }
        }

        // XƯỞNG DECK CHAMPION = copy đầy đủ UI/tính năng của DeckBuilderView PvP (thư viện cuộn,
        // card có keyword, lưu deck). Dựng UI DƯỚI ChampionSelectCanvas (uiParent) nên render chắc chắn.
        // MỐC 11: Cửa Hàng Trang Bị (mua bằng Lõi) — thuộc hệ Con Đường Anh Hùng (mở từ Bản Đồ Ải).
        ItemShopView _itemShop;
        ItemShopView ItemShop()
        {
            if (_itemShop == null)
                _itemShop = new GameObject("ItemShop").AddComponent<ItemShopView>();
            _itemShop.campaign = campaign;
            return _itemShop;
        }

        ChampionDeckBuilderView _champBuilder;
        ChampionDeckBuilderView DeckBuilder()
        {
            if (_champBuilder == null)
            {
                var go = new GameObject("ChampionDeckBuilder");
                _champBuilder = go.AddComponent<ChampionDeckBuilderView>();
            }
            _champBuilder.uiParent = _champSel != null ? (RectTransform)_champSel.transform : null;
            _champBuilder.cardLibrary = LibraryRef();
            _champBuilder.campaign = campaign;
            if (_champBuilder.cardPrefab == null)
                _champBuilder.cardPrefab = cardPrefab != null ? cardPrefab
                    : (campaignView != null ? campaignView.cardPrefab : null);
            return _champBuilder;
        }

        // Zoom 1 lá — CARD THẬT (prefab CardView → hiện keyword). Bấm nền để đóng.
        void ShowCardZoom(CardData cd)
        {
            if (cd == null || _champSel == null) return;
            var z = MakeRect("CardZoom", (RectTransform)_champSel.transform, Vector2.zero, Vector2.one);
            z.SetAsLastSibling();
            var dim = SetImage(z, new Color(0f, 0f, 0f, 0.86f));
            var dbtn = z.gameObject.AddComponent<Button>(); dbtn.targetGraphic = dim;
            dbtn.onClick.AddListener(() => Destroy(z.gameObject));

            var holder = MakeRect("Card", z, new Vector2(0.39f, 0.12f), new Vector2(0.61f, 0.92f));
            BuildCardBig(cd, holder);
            SpawnLabel(z, "Bấm nền để đóng", 13f, new Color(0.85f, 0.88f, 0.95f), TextAlignmentOptions.Center, false,
                new Vector2(0.3f, 0.04f), new Vector2(0.7f, 0.09f));
        }

        // Render 1 CARD THẬT lớn (dùng prefab CardView → keyword hiện đầy đủ). Fallback: artwork + stats.
        void BuildCardBig(CardData cd, RectTransform holder)
        {
            if (cd == null) { RoundImg(holder, new Color(0.16f, 0.19f, 0.27f, 0.95f)); return; }
            var prefab = cardPrefab != null ? cardPrefab : (campaignView != null ? campaignView.cardPrefab : null);
            if (prefab != null)
            {
                var go = Instantiate(prefab, holder);
                var cv = go.GetComponent<CardView>();
                if (cv != null)
                {
                    var gr = go.GetComponent<GraphicRaycaster>(); if (gr != null) Destroy(gr);
                    var cvCanvas = go.GetComponent<Canvas>(); if (cvCanvas != null) Destroy(cvCanvas);
                    var model = new CardModel(cd, true);
                    model.SetLocation(CardLocation.InHand);
                    cv.AllowInteract = false;
                    cv.Bind(model);
                    cv.ResetToPortraitSize();
                    cv.enabled = false;
                    var rt = go.GetComponent<RectTransform>();
                    rt.anchorMin = rt.anchorMax = rt.pivot = new Vector2(0.5f, 0.5f);
                    rt.anchoredPosition = Vector2.zero;
                    StartCoroutine(FitCard(rt, holder));
                    return;
                }
                Destroy(go);
            }
            RoundImg(holder, new Color(0.09f, 0.11f, 0.16f, 0.98f));
            var art = MakeRect("Art", holder, new Vector2(0.06f, 0.30f), new Vector2(0.94f, 0.92f));
            if (cd.artwork != null) { var raw = art.gameObject.AddComponent<RawImage>(); raw.texture = cd.artwork; raw.raycastTarget = false; }
            else RoundImg(art, new Color(0.16f, 0.19f, 0.27f, 0.95f)).raycastTarget = false;
            var cost = MakeRect("Cost", holder, new Vector2(0.04f, 0.82f), new Vector2(0.24f, 0.98f));
            RoundImg(cost, new Color(0.15f, 0.35f, 0.7f, 0.98f)).raycastTarget = false;
            SpawnLabel(cost, cd.manaCost.ToString(), 18f, Color.white, TextAlignmentOptions.Center, true);
            SpawnLabel(holder, $"<b>{cd.cardName}</b>", 14f, Color.white, TextAlignmentOptions.Center, true,
                new Vector2(0.03f, 0.17f), new Vector2(0.97f, 0.30f));
            var cm = new CardModel(cd, true);
            var a = MakeRect("Atk", holder, new Vector2(0.05f, 0.02f), new Vector2(0.30f, 0.16f));
            RoundImg(a, new Color(0.2f, 0.4f, 0.75f, 0.98f)).raycastTarget = false;
            SpawnLabel(a, cm.currentAttack.ToString(), 16f, Color.white, TextAlignmentOptions.Center, true);
            var h = MakeRect("Hp", holder, new Vector2(0.70f, 0.02f), new Vector2(0.95f, 0.16f));
            RoundImg(h, new Color(0.75f, 0.24f, 0.24f, 0.98f)).raycastTarget = false;
            SpawnLabel(h, cm.currentHealth.ToString(), 16f, Color.white, TextAlignmentOptions.Center, true);
        }

        // Lưới tile lá (cost + tên). inDeck=true → bấm bỏ; false → bấm thêm. Lá champion khóa (không bỏ).
        void GridCards(RectTransform box, List<CardData> cards, float xL, float xR, float yTop, float yBot, int cols,
            ChampionData champ, bool inDeck, int champIdx, bool readOnly = false,
            System.Action onChanged = null, System.Action<CardData> onZoom = null)
        {
            if (cards == null || cards.Count == 0) return;
            float cw = (xR - xL) / cols;
            int rows = Mathf.Max(1, Mathf.CeilToInt(cards.Count / (float)cols));
            float ch = Mathf.Min(0.26f, (yTop - yBot) / rows);
            for (int i = 0; i < cards.Count; i++)
            {
                var cd = cards[i]; if (cd == null) continue;
                int col = i % cols, row = i / cols;
                float x0 = xL + col * cw + 0.005f, x1 = xL + (col + 1) * cw - 0.005f;
                float y1 = yTop - row * ch - 0.006f, y0 = y1 - ch + 0.012f;
                if (y0 < yBot) break;
                var tile = MakeRect("Tile_" + inDeck + "_" + i, box, new Vector2(x0, y0), new Vector2(x1, y1));
                bool isChampCard = champ.championCard != null && cd.cardName == champ.championCard.cardName;

                // Nền = ẢNH BÀI (RawImage). Không có art → màu.
                UnityEngine.UI.Graphic timg;
                if (cd.artwork != null)
                {
                    var raw = tile.gameObject.AddComponent<RawImage>();
                    raw.texture = cd.artwork;
                    timg = raw;
                }
                else timg = RoundImg(tile, new Color(0.15f, 0.19f, 0.27f, 0.97f));

                // Cost badge (góc trên-trái)
                var costRT = MakeRect("Cost", tile, new Vector2(0.04f, 0.68f), new Vector2(0.34f, 0.97f));
                var cimg = costRT.gameObject.AddComponent<Image>();
                cimg.sprite = UISprites.Circle(); cimg.preserveAspect = true;
                cimg.color = isChampCard ? new Color(0.20f, 0.62f, 0.58f, 0.98f) : new Color(0.16f, 0.42f, 0.72f, 0.98f);
                cimg.raycastTarget = false;
                SpawnLabel(costRT, cd.manaCost.ToString(), 12f, Color.white, TextAlignmentOptions.Center, true);

                // Tên ở đáy trên dải tối
                var nsRT = MakeRect("Ns", tile, new Vector2(0f, 0f), new Vector2(1f, 0.24f));
                var nsi = nsRT.gameObject.AddComponent<Image>();
                nsi.color = isChampCard ? new Color(0.42f, 0.34f, 0.10f, 0.9f) : new Color(0f, 0f, 0f, 0.66f);
                nsi.raycastTarget = false;
                var nl = SpawnLabel(nsRT, (isChampCard ? "★" : "") + cd.cardName, 9f, Color.white,
                    TextAlignmentOptions.Center, true, new Vector2(0.27f, 0f), new Vector2(0.73f, 1f));
                nl.textWrappingMode = TextWrappingModes.NoWrap; nl.overflowMode = TextOverflowModes.Ellipsis;

                // ATK (góc dưới-trái) + HP (góc dưới-phải) — giống card thật
                var atkRT = MakeRect("Atk", tile, new Vector2(0.02f, 0.01f), new Vector2(0.26f, 0.25f));
                var aimg = atkRT.gameObject.AddComponent<Image>(); aimg.sprite = UISprites.Circle(); aimg.preserveAspect = true;
                aimg.color = new Color(0.80f, 0.45f, 0.18f, 0.98f); aimg.raycastTarget = false;
                SpawnLabel(atkRT, cd.baseAttack.ToString(), 11f, Color.white, TextAlignmentOptions.Center, true);
                var hpRT = MakeRect("Hp", tile, new Vector2(0.74f, 0.01f), new Vector2(0.98f, 0.25f));
                var himg = hpRT.gameObject.AddComponent<Image>(); himg.sprite = UISprites.Circle(); himg.preserveAspect = true;
                himg.color = new Color(0.78f, 0.22f, 0.24f, 0.98f); himg.raycastTarget = false;
                SpawnLabel(hpRT, cd.baseHealth.ToString(), 11f, Color.white, TextAlignmentOptions.Center, true);

                if (readOnly)
                {
                    // Chỉ xem: bấm cả tile → ZOOM (keyword đầy đủ).
                    if (onZoom != null)
                    {
                        var vbtn = tile.gameObject.AddComponent<Button>(); vbtn.targetGraphic = timg;
                        var capr = cd; vbtn.onClick.AddListener(() => onZoom(capr));
                    }
                    continue;
                }

                // Nút ZOOM nhỏ (góc trên-phải) — bấm để xem keyword mà KHÔNG thêm/bỏ.
                if (onZoom != null)
                {
                    var zRT = MakeRect("Zoom", tile, new Vector2(0.70f, 0.70f), new Vector2(0.985f, 0.985f));
                    var zimg = RoundImg(zRT, new Color(0.08f, 0.10f, 0.16f, 0.92f));
                    var zbtn = zRT.gameObject.AddComponent<Button>(); zbtn.targetGraphic = zimg;
                    SpawnLabel(zRT, "🔍", 11f, Color.white, TextAlignmentOptions.Center, true);
                    var capz = cd; zbtn.onClick.AddListener(() => onZoom(capz));
                }

                if (inDeck && isChampCard) continue; // lá champion không được bỏ (và không cần nút thêm)
                var btn = tile.gameObject.AddComponent<Button>(); btn.targetGraphic = timg;
                var cap = cd;
                btn.onClick.AddListener(() =>
                {
                    if (inDeck) _editDeck.RemoveAll(x => x != null && x.cardName == cap.cardName);
                    else if (_editDeck.Count < 15 && !_editDeck.Exists(x => x != null && x.cardName == cap.cardName))
                        _editDeck.Add(cap); // 1 bản sao
                    if (onChanged != null) onChanged(); else RenderChampDetail(champIdx);
                });
            }
        }

        void RenderLevelTab(RectTransform box, ChampionData champ, int star, int mastery)
        {
            int xp = ProgressStore.GetMasteryXp(champ.championName);
            int floorXp = ProgressStore.XpFloorForLevel(mastery);
            int nextXp = ProgressStore.XpFloorForLevel(mastery + 1);
            float frac = nextXp > floorXp ? (xp - floorXp) / (float)(nextXp - floorXp) : 1f;

            // Header: cấp + sao + tinh hồn
            SpawnLabel(box, $"<b>Cấp anh hùng {mastery}</b>    <color=#F0C75A>{StarStr(star)}</color>    <color=#C89BF5>Tinh Hồn {ProgressStore.Essence}</color>",
                15f, Color.white, TextAlignmentOptions.Left, true, new Vector2(0.04f, 0.90f), new Vector2(0.96f, 0.985f));

            // Thanh XP đến cấp kế
            var track = MakeRect("Lt", box, new Vector2(0.04f, 0.845f), new Vector2(0.96f, 0.885f));
            RoundImg(track, new Color(0.10f, 0.12f, 0.18f, 0.98f));
            var fillRT = MakeRect("Lf", track, new Vector2(0f, 0f), new Vector2(Mathf.Clamp01(frac), 1f));
            RoundImg(fillRT, new Color(0.36f, 0.8f, 0.5f, 1f)).raycastTarget = false;
            SpawnLabel(box, nextXp > floorXp ? $"{xp - floorXp} / {nextXp - floorXp} XP → Cấp {mastery + 1}" : "ĐÃ TỐI ĐA",
                11f, new Color(0.8f, 0.84f, 0.92f), TextAlignmentOptions.Right, false, new Vector2(0.04f, 0.80f), new Vector2(0.96f, 0.845f));

            // Danh sách theo CẤP CHAMPION (giống bảng Champion Levels của PoC) — cửa sổ quanh cấp hiện tại.
            SpawnLabel(box, "PHẦN THƯỞNG THEO CẤP", 12.5f, accentColor, TextAlignmentOptions.Left, true,
                new Vector2(0.04f, 0.725f), new Vector2(0.96f, 0.79f));

            int maxLvl = ProgressStore.MaxMasteryLevel;
            const int rows = 8;
            int startL = Mathf.Clamp(mastery - 2, 1, Mathf.Max(1, maxLvl - rows + 1));
            float top = 0.70f, h = 0.072f, gap = 0.008f;
            for (int r = 0; r < rows; r++)
            {
                int L = startL + r;
                if (L > maxLvl) break;
                int st = ProgressStore.StarAtLevel(L);
                int stPrev = ProgressStore.StarAtLevel(L - 1);

                string reward;
                if (L == 1)
                    reward = "Mở ô trang bị đầu tiên (×1)";
                else if (st > stPrev)
                {
                    int slots = Mathf.Clamp(1 + (st - 1) / 2, 1, ProgressStore.MaxItemSlots);
                    string perk;
                    if (champ.starPerks != null && (st - 2) < champ.starPerks.Count
                        && champ.starPerks[st - 2] != null && champ.starPerks[st - 2].IsMeaningful)
                        perk = " — " + champ.starPerks[st - 2].name;
                    else perk = " — sức mạnh ngẫu nhiên";
                    reward = $"<color=#F0C75A>★ Lên Sao {st}</color>: ô trang bị ×{slots}, +1 sức mạnh{perk}";
                }
                else
                    reward = $"+{ProgressStore.NexusPerLevel} máu Nexus khởi đầu";

                // M4: nâng bài theo cấp — đổi lá yếu → lá mạnh (nếu ChampionData có cấu hình).
                if (champ.deckUpgrades != null)
                    foreach (var up in champ.deckUpgrades)
                        if (up != null && up.level == L && up.from != null && up.to != null)
                            reward += $"\n<color=#7FD8FF>◆ Nâng bài: {up.from.cardName} → {up.to.cardName}</color>";

                bool done = L < mastery;
                bool cur = L == mastery;
                float y1 = top - r * (h + gap);
                var row = MakeRect("Lv_" + L, box, new Vector2(0.04f, y1 - h), new Vector2(0.96f, y1));
                RoundImg(row, cur ? new Color(0.26f, 0.34f, 0.5f, 0.98f)
                                  : done ? new Color(0.16f, 0.24f, 0.20f, 0.94f)
                                         : new Color(0.13f, 0.15f, 0.20f, 0.92f));
                string tag = cur ? "<color=#F0C75A>▶</color>" : done ? "<color=#59D97A>✔</color>" : "<color=#7A8090>🔒</color>";
                SpawnLabel(row, $"{tag} <b>Cấp {L}</b>", 12.5f, Color.white, TextAlignmentOptions.Left, false,
                    new Vector2(0.03f, 0f), new Vector2(0.24f, 1f));
                SpawnLabel(row, reward, 11.5f, (done || cur) ? new Color(0.9f, 0.94f, 0.92f) : new Color(0.72f, 0.74f, 0.8f),
                    TextAlignmentOptions.Left, false, new Vector2(0.25f, 0f), new Vector2(0.98f, 1f))
                    .textWrappingMode = TextWrappingModes.Normal;
            }
        }

        void RenderRelicTab(RectTransform box)
        {
            RelicFactory.EnsureRelics(campaign); // bổ sung cổ vật phong phú để hiện đủ danh sách
            var pool = campaign.relicPool;
            if (pool == null || pool.Count == 0)
            { SpawnLabel(box, "Chưa có Cổ Vật trong chiến dịch này.\n(Nhặt sau Elite/Boss khi có.)", 14f, new Color(0.7f, 0.75f, 0.85f), TextAlignmentOptions.Center, false); return; }
            var sb = "<b>Cổ Vật có thể nhặt trong hành trình:</b>\n\n";
            foreach (var r in pool) if (r != null) sb += $"<color=#5FCFD2>• {r.relicName}</color> — <size=85%>{r.description}</size>\n";
            SpawnLabel(box, sb, 14f, Color.white, TextAlignmentOptions.TopLeft, false,
                new Vector2(0.05f, 0.05f), new Vector2(0.95f, 0.95f)).textWrappingMode = TextWrappingModes.Normal;
        }

        // ── TINH HỒN (M9): 6 TINH HỒN kiểu HSR — mở lần lượt, hiện rõ buff ──
        void RenderConstellationTab(RectTransform box, int idx, ChampionData champ)
        {
            SpawnLabel(box, $"<b>Tinh Hồn:</b> <color=#C89BF5>{ProgressStore.Essence}</color>   <size=78%><color=#9AA7B8>Mở lần lượt Tinh Hồn 1 → 6.</color></size>",
                14f, Color.white, TextAlignmentOptions.Left, true, new Vector2(0.04f, 0.90f), new Vector2(0.96f, 0.99f));

            var nodes = CampaignRun.ConstellationFor(champ);
            if (nodes == null || nodes.Count == 0)
            { SpawnLabel(box, "Chưa có Tinh Hồn.", 13f, new Color(0.75f, 0.78f, 0.86f), TextAlignmentOptions.Center, false, new Vector2(0.05f, 0.1f), new Vector2(0.95f, 0.85f)); return; }

            float rh = Mathf.Min(0.135f, 0.86f / Mathf.Max(1, nodes.Count));
            for (int i = 0; i < nodes.Count; i++)
            {
                var node = nodes[i]; if (node == null) continue;
                int ni = i;
                bool bought = ProgressStore.IsConstellationBought(champ.championName, i);
                bool prereqMet = node.requires < 0 || ProgressStore.IsConstellationBought(champ.championName, node.requires);
                bool canBuy = !bought && prereqMet && ProgressStore.Essence >= node.cost;

                float y1 = 0.88f - i * (rh + 0.008f);
                var row = MakeRect("K_" + i, box, new Vector2(0.03f, y1 - rh), new Vector2(0.97f, y1));
                RoundImg(row, bought ? new Color(0.18f, 0.34f, 0.28f, 0.96f)
                                     : prereqMet ? new Color(0.20f, 0.17f, 0.30f, 0.96f)
                                                 : new Color(0.14f, 0.15f, 0.20f, 0.92f));

                // Badge số Tinh Hồn (1..6)
                var num = MakeRect("N", row, new Vector2(0.015f, 0.16f), new Vector2(0.095f, 0.84f));
                RoundImg(num, bought ? new Color(0.3f, 0.6f, 0.42f, 0.98f) : new Color(0.42f, 0.32f, 0.62f, 0.98f)).raycastTarget = false;
                SpawnLabel(num, (i + 1).ToString(), 16f, Color.white, TextAlignmentOptions.Center, true);

                // Tên + MÔ TẢ BUFF
                SpawnLabel(row, $"<b>{node.nodeName}</b>\n<size=80%>{node.effect?.desc}</size>", 13f,
                    (bought || prereqMet) ? Color.white : new Color(0.72f, 0.74f, 0.8f),
                    TextAlignmentOptions.Left, false, new Vector2(0.11f, 0.05f), new Vector2(0.73f, 0.95f))
                    .textWrappingMode = TextWrappingModes.Normal;

                // Trạng thái / nút mua
                var buyRT = MakeRect("Buy", row, new Vector2(0.75f, 0.2f), new Vector2(0.97f, 0.8f));
                if (bought)
                {
                    RoundImg(buyRT, new Color(0.2f, 0.5f, 0.34f, 0.95f)).raycastTarget = false;
                    SpawnLabel(buyRT, "ĐÃ MỞ", 13f, Color.white, TextAlignmentOptions.Center, true);
                }
                else if (!prereqMet)
                {
                    RoundImg(buyRT, new Color(0.25f, 0.25f, 0.3f, 0.85f)).raycastTarget = false;
                    SpawnLabel(buyRT, $"🔒 cần Tinh Hồn {node.requires + 1}", 10f, new Color(0.85f, 0.85f, 0.9f), TextAlignmentOptions.Center, true);
                }
                else
                {
                    var col = canBuy ? new Color(0.5f, 0.34f, 0.72f, 0.98f) : new Color(0.3f, 0.3f, 0.34f, 0.9f);
                    var bimg = RoundImg(buyRT, col);
                    SpawnLabel(buyRT, $"{node.cost} ✦", 13f, Color.white, TextAlignmentOptions.Center, true);
                    if (canBuy)
                    {
                        var btn = buyRT.gameObject.AddComponent<Button>(); btn.targetGraphic = bimg;
                        btn.onClick.AddListener(() =>
                        {
                            if (ProgressStore.BuyConstellation(champ.championName, ni, node.cost)) RenderChampDetail(idx);
                        });
                    }
                }
            }
        }

        // ── TRANG BỊ (M8): kho chung + ĐEO/THÁO theo SLOT (chỉ item đeo mới có tác dụng) ──
        // TRANG BỊ (gộp Cổ Vật): item TREO DÍNH mép phải card — hex sát card + info + nút, mỗi SLOT 1 ô.
        // TRÁI: TRANG BỊ ĐÃ CÓ (cuộn, chỉ item đã sở hữu) · PHẢI: ô ĐANG LẮP + Tháo · DƯỚI: chi tiết + GÁN.
        void RenderItemsTab(RectTransform box, int idx, ChampionData champ, int mastery)
        {
            var bimg = box.GetComponent<Image>();
            if (bimg != null) bimg.color = PoCTheme.A(PoCTheme.PlaqNavy, 0.5f);

            string champName = champ.championName;
            int slots = ProgressStore.ItemSlots(champName);

            // CHỈ hiện trang bị ĐÃ MUA (IsItemOwned — mua bằng Lõi ở Cửa Hàng, coreCost>0). KHÔNG hiện cả kho.
            var owned = new List<CardItemDef>();
            foreach (var it in ChampionItemDefs())
                if (it != null && ProgressStore.IsItemOwned(it.itemName)) owned.Add(it);

            // Gỡ key equip cũ (ItemData) / item không còn sở hữu → tránh EquippedCount đếm nhầm = đầy slot.
            var validNames = new HashSet<string>();
            foreach (var it in owned) validNames.Add(it.itemName);
            ProgressStore.PruneEquipped(champName, validNames);

            var equipped = new List<CardItemDef>();
            foreach (var it in owned)
                if (ProgressStore.IsItemEquipped(champName, it.itemName)) equipped.Add(it);

            // ===== TRÁI: TRANG BỊ ĐÃ CÓ (danh sách CUỘN — row cố định, 100 item vẫn ổn) =====
            SpawnLabel(box, "<b>TRANG BỊ ĐÃ CÓ</b>", 13f, PoCTheme.AmGoldHi, TextAlignmentOptions.Left, true,
                new Vector2(0.04f, 0.915f), new Vector2(0.52f, 0.99f));
            if (owned.Count == 0)
                SpawnLabel(box, "Chưa sở hữu trang bị champion.\nMua ở Cửa Hàng (Lõi) hoặc lên cấp mastery.", 12f,
                    PoCTheme.AmDim, TextAlignmentOptions.Center, false, new Vector2(0.04f, 0.50f), new Vector2(0.52f, 0.88f));
            else
                BuildOwnedItemList(box, owned, champName, idx);

            // ===== PHẢI: ĐANG LẮP (ô + Tháo) =====
            SpawnLabel(box, $"<b>ĐANG LẮP</b>  <color=#F5D98A>{equipped.Count}/{slots}</color>", 13f, PoCTheme.AmGoldHi,
                TextAlignmentOptions.Left, true, new Vector2(0.55f, 0.915f), new Vector2(0.98f, 0.99f));
            float sTop = 0.905f, shh = 0.14f, sgap = 0.02f;
            for (int s = 0; s < slots; s++)
            {
                var eqIt = s < equipped.Count ? equipped[s] : null;
                float y1 = sTop - s * (shh + sgap), y0 = y1 - shh;
                var row = MakeRect("sl_" + s, box, new Vector2(0.55f, y0), new Vector2(0.98f, y1));
                RoundImg(row, eqIt != null ? PoCTheme.A(PoCTheme.NodeNavy, 0.95f) : PoCTheme.A(PoCTheme.PlaqNavy, 0.7f)).raycastTarget = false;
                var hx = MakeRect("h", row, new Vector2(0.03f, 0.14f), new Vector2(0.25f, 0.86f));
                var himg = hx.gameObject.AddComponent<Image>(); himg.sprite = HexSprite(); himg.preserveAspect = true;
                himg.color = eqIt != null ? RarityTint(eqIt.rarity) : PoCTheme.A(PoCTheme.NodeNavy, 0.55f); himg.raycastTarget = false;
                if (eqIt != null && eqIt.icon != null)
                {
                    var ic = MakeRect("ic", hx, new Vector2(0.26f, 0.26f), new Vector2(0.74f, 0.74f));
                    var ii = ic.gameObject.AddComponent<Image>(); ii.sprite = eqIt.icon; ii.preserveAspect = true; ii.raycastTarget = false;
                }
                if (eqIt == null) SpawnLabel(hx, "+", 22f, PoCTheme.AmDim, TextAlignmentOptions.Center, true);

                if (eqIt != null)
                {
                    SpawnLabel(row, $"<b>{eqIt.itemName}</b>\n<size=74%><color=#9FB0D0>{RarityVN(eqIt.rarity)}</color></size>", 11f,
                        PoCTheme.AmCream, TextAlignmentOptions.Left, false, new Vector2(0.27f, 0.05f), new Vector2(0.72f, 0.95f))
                        .textWrappingMode = TextWrappingModes.Normal;
                    var rm = MakeRect("rm", row, new Vector2(0.73f, 0.24f), new Vector2(0.97f, 0.76f));
                    var rimg = RoundImg(rm, PoCTheme.A(PoCTheme.AmRed, 0.9f));
                    SpawnLabel(rm, "Tháo", 11f, Color.white, TextAlignmentOptions.Center, true);
                    var rb = rm.gameObject.AddComponent<Button>(); rb.targetGraphic = rimg;
                    var capE = eqIt; rb.onClick.AddListener(() =>
                    { ProgressStore.UnequipItem(champName, capE.itemName); RenderChampDetail(idx); });
                }
                else
                {
                    SpawnLabel(row, "<color=#8FE0B0>+ Bấm để lắp</color>", 12f, Color.white, TextAlignmentOptions.Center, false,
                        new Vector2(0.27f, 0.05f), new Vector2(0.97f, 0.95f));
                    // Cả ô trống bấm được → mở picker chọn trang bị đã có.
                    var hit = MakeRect("hit", row, Vector2.zero, Vector2.one);
                    var hitImg = hit.gameObject.AddComponent<Image>(); hitImg.color = new Color(1f, 1f, 1f, 0.001f);
                    var eb = hit.gameObject.AddComponent<Button>(); eb.targetGraphic = hitImg;
                    eb.onClick.AddListener(() => ShowItemEquipPicker(idx, champName));
                }
            }

            // ===== DƯỚI: CHI TIẾT + GÁN =====
            if (_selItem == null || !owned.Contains(_selItem)) _selItem = owned.Count > 0 ? owned[0] : null;
            RenderItemDetailPanel(box, _selItem, champName, mastery, slots, idx);
        }

        // Danh sách CUỘN trang bị đã có bên trái — row cao cố định (không co nhỏ khi nhiều item).
        void BuildOwnedItemList(RectTransform box, List<CardItemDef> owned, string champName, int idx)
        {
            var area = MakeRect("ItemListArea", box, new Vector2(0.035f, 0.43f), new Vector2(0.53f, 0.895f));
            var sr = area.gameObject.AddComponent<UnityEngine.UI.ScrollRect>();
            sr.horizontal = false; sr.movementType = UnityEngine.UI.ScrollRect.MovementType.Clamped; sr.scrollSensitivity = 22f;

            var vp = MakeRect("VP", area, Vector2.zero, Vector2.one);
            var vpImg = vp.gameObject.AddComponent<Image>(); vpImg.color = new Color(1f, 1f, 1f, 0.008f);
            var mask = vp.gameObject.AddComponent<UnityEngine.UI.Mask>(); mask.showMaskGraphic = false;

            var content = MakeRect("Content", vp, new Vector2(0f, 1f), new Vector2(1f, 1f));
            content.pivot = new Vector2(0.5f, 1f); content.offsetMin = content.offsetMax = Vector2.zero;
            var vlg = content.gameObject.AddComponent<UnityEngine.UI.VerticalLayoutGroup>();
            vlg.spacing = 4f; vlg.padding = new RectOffset(3, 3, 3, 3);
            vlg.childForceExpandWidth = true; vlg.childForceExpandHeight = false;
            vlg.childControlWidth = true; vlg.childControlHeight = true;
            content.gameObject.AddComponent<UnityEngine.UI.ContentSizeFitter>().verticalFit =
                UnityEngine.UI.ContentSizeFitter.FitMode.PreferredSize;
            sr.viewport = vp; sr.content = content;

            foreach (var it in owned) BuildOwnedItemRow(content, it, champName, idx);
        }

        void BuildOwnedItemRow(RectTransform content, CardItemDef it, string champName, int idx)
        {
            bool eq = ProgressStore.IsItemEquipped(champName, it.itemName);
            bool sel = it == _selItem;

            var row = MakeRect("row_" + it.itemName, content, Vector2.zero, Vector2.one);
            row.gameObject.AddComponent<UnityEngine.UI.LayoutElement>().preferredHeight = 46f;
            var rimg = RoundImg(row, sel ? PoCTheme.A(PoCTheme.AmGold, 0.30f) : PoCTheme.A(PoCTheme.NodeNavy, 0.9f));

            var hx = MakeRect("h", row, new Vector2(0.02f, 0.13f), new Vector2(0.17f, 0.87f));
            var himg = hx.gameObject.AddComponent<Image>(); himg.sprite = HexSprite(); himg.preserveAspect = true;
            himg.color = RarityTint(it.rarity); himg.raycastTarget = false;
            if (it.icon != null)
            {
                var ic = MakeRect("ic", hx, new Vector2(0.26f, 0.26f), new Vector2(0.74f, 0.74f));
                var ii = ic.gameObject.AddComponent<Image>(); ii.sprite = it.icon; ii.preserveAspect = true; ii.raycastTarget = false;
            }
            string tag = eq ? "<color=#8FE0B0>đã lắp</color>" : $"<color=#9FB0D0>{RarityVN(it.rarity)}</color>";
            SpawnLabel(row, $"<b>{it.itemName}</b>  <size=76%>{tag}</size>", 11.5f,
                PoCTheme.AmCream, TextAlignmentOptions.Left, false,
                new Vector2(0.19f, 0.05f), new Vector2(0.98f, 0.95f)).textWrappingMode = TextWrappingModes.Normal;
            var b = row.gameObject.AddComponent<Button>(); b.targetGraphic = rimg;
            var cap = it; b.onClick.AddListener(() => { _selItem = cap; RenderChampDetail(idx); });
        }

        // Khung chi tiết trang bị đang chọn (icon + nội dung + chỉ số) + nút GÁN/Tháo ngay đó.
        void RenderItemDetailPanel(RectTransform box, CardItemDef it, string champName, int mastery, int slots, int idx)
        {
            var d = MakeRect("detail", box, new Vector2(0.04f, 0.03f), new Vector2(0.98f, 0.40f));
            RoundImg(d, PoCTheme.A(PoCTheme.PlaqNavy, 0.92f)).raycastTarget = false;
            if (it == null)
            {
                SpawnLabel(d, "Chọn 1 trang bị ở kho bên trái để xem chi tiết.", 12f, PoCTheme.AmDim,
                    TextAlignmentOptions.Center, false); return;
            }

            bool owned = ProgressStore.IsItemOwned(it.itemName);
            bool unlocked = DefUnlocked(it, mastery, owned);
            bool eq = ProgressStore.IsItemEquipped(champName, it.itemName);
            int equippedCount = ProgressStore.EquippedCount(champName);

            // icon hex lớn
            var hx = MakeRect("h", d, new Vector2(0.02f, 0.14f), new Vector2(0.20f, 0.92f));
            var himg = hx.gameObject.AddComponent<Image>(); himg.sprite = HexSprite(); himg.preserveAspect = true;
            himg.color = unlocked ? RarityTint(it.rarity) : PoCTheme.A(PoCTheme.NodeNavy, 1f); himg.raycastTarget = false;
            if (it.icon != null)
            {
                var ic = MakeRect("ic", hx, new Vector2(0.26f, 0.26f), new Vector2(0.74f, 0.74f));
                var ii = ic.gameObject.AddComponent<Image>(); ii.sprite = it.icon; ii.preserveAspect = true; ii.raycastTarget = false;
            }
            // tên + loại
            SpawnLabel(d, $"<b>{it.itemName}</b>   <size=76%><color=#7FE3E5>{RarityVN(it.rarity)}</color></size>", 15f,
                PoCTheme.AmGoldHi, TextAlignmentOptions.Left, true, new Vector2(0.22f, 0.66f), new Vector2(0.78f, 0.95f));
            // chỉ số
            SpawnLabel(d, ItemStatLine(it), 12f, PoCTheme.AmCream, TextAlignmentOptions.BottomLeft, true,
                new Vector2(0.22f, 0.05f), new Vector2(0.78f, 0.30f)).textWrappingMode = TextWrappingModes.Normal;

            // nút hành động
            var act = MakeRect("act", d, new Vector2(0.80f, 0.30f), new Vector2(0.985f, 0.68f));
            if (eq)
            {
                var aimg = RoundImg(act, PoCTheme.A(PoCTheme.AmRed, 0.92f));
                SpawnLabel(act, "THÁO", 14f, Color.white, TextAlignmentOptions.Center, true);
                var ab = act.gameObject.AddComponent<Button>(); ab.targetGraphic = aimg;
                ab.onClick.AddListener(() => { ProgressStore.UnequipItem(champName, it.itemName); RenderChampDetail(idx); });
            }
            else if (!unlocked)
            {
                RoundImg(act, PoCTheme.A(PoCTheme.NodeNavy, 0.9f)).raycastTarget = false;
                SpawnLabel(act, it.coreCost > 0 ? "Mua ở\nCửa Hàng" : $"Cần\nCấp {it.unlockMasteryLevel}", 11f,
                    PoCTheme.AmDim, TextAlignmentOptions.Center, true);
            }
            else if (equippedCount >= slots)
            {
                RoundImg(act, PoCTheme.A(PoCTheme.NodeNavy, 0.9f)).raycastTarget = false;
                SpawnLabel(act, "Hết slot\ntháo bớt", 11f, PoCTheme.AmDim, TextAlignmentOptions.Center, true);
            }
            else
            {
                var aimg = RoundImg(act, PoCTheme.AmGold);
                SpawnLabel(act, "GÁN", 15f, PoCTheme.Hex("1A1206"), TextAlignmentOptions.Center, true);
                var ab = act.gameObject.AddComponent<Button>(); ab.targetGraphic = aimg;
                ab.onClick.AddListener(() =>
                { if (ProgressStore.EquipItem(champName, it.itemName)) RenderChampDetail(idx); });
            }
        }

        // Dòng chỉ số gọn cho 1 item (ATK/HP/mana/nexus + keyword/skill).
        string ItemStatLine(CardItemDef it)
        {
            var parts = new List<string>();
            if (it.atkBonus != 0) parts.Add($"<color=#E88C6E>+{it.atkBonus} ATK</color>");
            if (it.hpBonus != 0) parts.Add($"<color=#6EA8E0>+{it.hpBonus} HP</color>");
            if (it.costReduction > 0) parts.Add($"<color=#C9A6F0>-{it.costReduction} mana</color>");
            if (it.bonusMana != 0) parts.Add($"<color=#6E9CE0>+{it.bonusMana} mana đầu</color>");
            if (it.bonusNexus != 0) parts.Add($"<color=#6EE0A8>+{it.bonusNexus} Nexus</color>");
            if (it.grantsKeyword) parts.Add($"<color=#F5D98A>keyword</color>");
            if (it.skill != null) parts.Add($"<color=#F5D98A>skill</color>");
            return parts.Count > 0 ? string.Join("   ", parts) : "<color=#7f8fb0>(không chỉ số)</color>";
        }

        // 1 ô trang bị PoC (toạ độ _champDetail): [THÁO] [info] [hex dính mép trái card].
        void BuildEquipStrip(RectTransform host, float y0, float y1, CardItemDef eq, string champName, int idx)
        {
            // Info bar — kéo sang trái, xa card
            var info = MakeRect("Info", host, new Vector2(0.305f, y0 + 0.008f), new Vector2(0.648f, y1 - 0.008f));
            RoundImg(info, eq != null ? new Color(0.14f, 0.24f, 0.32f, 0.96f) : new Color(0.12f, 0.13f, 0.17f, 0.9f));
            if (eq != null)
                SpawnLabel(info, $"<b>{eq.itemName}</b>  <size=74%><color=#7FE3E5>{RarityVN(eq.rarity)}</color></size>\n<size=80%><color=#C9CFDA>{ItemStatLine(eq)}</color></size>",
                    13f, Color.white, TextAlignmentOptions.Left, false, new Vector2(0.05f, 0.08f), new Vector2(0.90f, 0.92f))
                    .textWrappingMode = TextWrappingModes.Normal;
            else
                SpawnLabel(info, "<color=#8A93A5>Ô trống — bấm + để gắn</color>", 12.5f, Color.white,
                    TextAlignmentOptions.Center, false, new Vector2(0.05f, 0.1f), new Vector2(0.95f, 0.9f));

            // Hexagon DÍNH mép trái card (card bắt đầu ~0.71)
            var hexBox = MakeRect("HexBox", host, new Vector2(0.645f, y0 - 0.006f), new Vector2(0.716f, y1 + 0.006f));
            if (eq != null)
            {
                var ring = MakeRect("Ring", hexBox, new Vector2(-0.10f, -0.10f), new Vector2(1.10f, 1.10f));
                var rimg = ring.gameObject.AddComponent<Image>();
                rimg.sprite = HexSprite(); rimg.preserveAspect = true;
                rimg.color = new Color(0.96f, 0.80f, 0.35f); rimg.raycastTarget = false;
            }
            var core = MakeRect("Core", hexBox, Vector2.zero, Vector2.one);
            var himg = core.gameObject.AddComponent<Image>();
            himg.sprite = HexSprite(); himg.preserveAspect = true;
            himg.color = eq != null ? RarityTint(eq.rarity) : new Color(0.28f, 0.30f, 0.36f);
            himg.raycastTarget = false;
            if (eq != null && eq.icon != null)
            {
                var ic = MakeRect("Ic", core, new Vector2(0.24f, 0.24f), new Vector2(0.76f, 0.76f));
                var iimg = ic.gameObject.AddComponent<Image>();
                iimg.sprite = eq.icon; iimg.preserveAspect = true; iimg.raycastTarget = false;
            }

            // Nút đổi/tháo — ngoài cùng bên trái (xa card)
            float cy = (y0 + y1) * 0.5f;
            var brt = MakeRect("Btn", host, new Vector2(0.235f, cy - 0.045f), new Vector2(0.298f, cy + 0.045f));
            if (eq != null)
            {
                var bimg = RoundImg(brt, new Color(0.60f, 0.30f, 0.30f, 0.98f));
                var btn = brt.gameObject.AddComponent<Button>(); btn.targetGraphic = bimg;
                SpawnLabel(brt, "THÁO", 11f, Color.white, TextAlignmentOptions.Center, true);
                var cap = eq; btn.onClick.AddListener(() => { ProgressStore.UnequipItem(champName, cap.itemName); RenderChampDetail(idx); });
            }
            else
            {
                var bimg = RoundImg(brt, new Color(0.24f, 0.50f, 0.34f, 0.98f));
                var btn = brt.gameObject.AddComponent<Button>(); btn.targetGraphic = bimg;
                SpawnLabel(brt, "+", 20f, Color.white, TextAlignmentOptions.Center, true);
                btn.onClick.AddListener(() => ShowItemEquipPicker(idx, champName));
            }
        }

        // M12: ải mở khoá = đã qua ải sao ngay TRƯỚC (ladder theo champion). Bỏ gate sao champion.
        bool IsAdventureUnlocked(AdventureData adv, string champName)
            => adv != null
               && (adv.prerequisite == null
                   || AdventureProgress.IsCompleted(champName, adv.prerequisite.adventureName));

        // Ải theo thứ tự SAO tăng dần (bản sao — không đụng campaign.adventures).
        List<AdventureData> LadderSorted()
        {
            var l = new List<AdventureData>();
            if (campaign.adventures != null)
                foreach (var a in campaign.adventures) if (a != null) l.Add(a);
            l.Sort((x, y) => x.starDifficulty.CompareTo(y.starDifficulty));
            return l;
        }

        // Ải MỤC TIÊU: sao thấp nhất ĐÃ MỞ nhưng CHƯA qua (của champion này). Hết → ải sao cao nhất.
        AdventureData TargetAdventure(string champName)
        {
            AdventureData last = null;
            foreach (var a in LadderSorted())
            {
                last = a;
                if (IsAdventureUnlocked(a, champName) && !AdventureProgress.IsCompleted(champName, a.adventureName))
                    return a;
            }
            return last;
        }

        // Sao cao nhất champion đã CLEAR (-1 = chưa qua ải nào).
        float HighestClearedStar(string champName)
        {
            float hi = -1f;
            if (campaign.adventures != null)
                foreach (var a in campaign.adventures)
                    if (a != null && AdventureProgress.IsCompleted(champName, a.adventureName) && a.starDifficulty > hi)
                        hi = a.starDifficulty;
            return hi;
        }

        // M12 — THANH TARGET 0★→10★: tô tới sao cao nhất đã qua + mũi tên chỉ ải mục tiêu kế.
        void BuildLadderBar(RectTransform root, string champName)
        {
            float hi = HighestClearedStar(champName);
            var tgt = TargetAdventure(champName);
            float tgtStar = tgt != null ? tgt.starDifficulty : 0f;

            string hiTxt = hi < 0f ? "<color=#8AA4BC>Chưa qua ải nào</color>"
                                   : $"Đã qua: <color=#E7B24B>{hi:0.#} Sao</color>";
            string tgtTxt = tgt != null ? $"        Mục tiêu: <color=#4FC5E0>{tgtStar:0.#} Sao</color> — {tgt.region}" : "";
            SpawnLabel(root, hiTxt + tgtTxt, 12.5f, PoCTheme.Cream, TextAlignmentOptions.Center, true,
                new Vector2(0.14f, 0.868f), new Vector2(0.86f, 0.902f));

            const float xL = 0.26f, xR = 0.74f;
            // rãnh nền + phần đã qua (phẳng, bo góc)
            PoCTheme.Panel(root, "LadderTrack", new Vector2(xL, 0.842f), new Vector2(xR, 0.856f), PoCTheme.Surface);
            float fillFrac = Mathf.Clamp01((hi < 0f ? 0f : hi) / 10f);
            if (fillFrac > 0.001f)
                PoCTheme.Panel(root, "LadderFill", new Vector2(xL, 0.842f), new Vector2(Mathf.Lerp(xL, xR, fillFrac), 0.856f), PoCTheme.Cyan);

            // 11 nấc SAO vẽ thật (0..10): vàng nếu đã đạt, mờ nếu chưa.
            float hiV = hi < 0f ? -1f : hi;
            for (int v = 0; v <= 10; v++)
            {
                float x = Mathf.Lerp(xL, xR, v / 10f);
                PoCTheme.Put(root, "tick" + v, new Vector2(x - 0.010f, 0.860f), new Vector2(x + 0.010f, 0.882f),
                    PoCTheme.Star(), v <= hiV ? PoCTheme.Gold : PoCTheme.A(PoCTheme.Cream, 0.28f));
            }

            // Mũi tên MỤC TIÊU (tam giác chỉ xuống) ngay dưới rãnh.
            float tx = Mathf.Lerp(xL, xR, Mathf.Clamp01(tgtStar / 10f));
            PoCTheme.Put(root, "LadderMark", new Vector2(tx - 0.014f, 0.818f), new Vector2(tx + 0.014f, 0.842f),
                PoCTheme.Triangle(), PoCTheme.Gold, 180f);
        }

        // Màu node theo sao độ khó 0★→10★ — dùng palette Honkai (xanh băng → cyan → đỏ).
        static Color DiffColor(float s) => PoCTheme.DiffColor(s);

        RectTransform MakeRootCanvas(string name, int sort, out GameObject go)
        {
            go = new GameObject(name);
            var cv = go.AddComponent<Canvas>();
            cv.renderMode = RenderMode.ScreenSpaceOverlay;
            cv.sortingOrder = sort;
            go.AddComponent<GraphicRaycaster>();
            var scl = go.AddComponent<UnityEngine.UI.CanvasScaler>();
            scl.uiScaleMode = UnityEngine.UI.CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scl.referenceResolution = new Vector2(1920, 1080);
            return (RectTransform)go.transform;
        }

        // Vị trí mặc định kiểu bản đồ Runeterra (khỏi setup tay). regionLayout override nếu muốn.
        static readonly Dictionary<string, Vector2> DefaultRegionPos = new Dictionary<string, Vector2>
        {
            { "Freljord",        new Vector2(0.36f, 0.74f) },
            { "Ionia",           new Vector2(0.72f, 0.74f) },
            { "Demacia",         new Vector2(0.27f, 0.55f) },
            { "Noxus",           new Vector2(0.50f, 0.56f) },
            { "Piltover & Zaun", new Vector2(0.57f, 0.42f) },
            { "Bilgewater",      new Vector2(0.74f, 0.40f) },
            { "Targon",          new Vector2(0.33f, 0.24f) },
            { "Shurima",         new Vector2(0.54f, 0.22f) },
            { "Đảo Bóng Đêm",    new Vector2(0.79f, 0.22f) },
        };

        // Vị trí region (0-1): regionLayout → bản đồ Runeterra mặc định → lưới tự xếp.
        Vector2 RegionAnchor(string region, int i, int n)
        {
            if (regionLayout != null)
                foreach (var rp in regionLayout)
                    if (rp != null && rp.region == region) return new Vector2(rp.x, rp.y);
            if (DefaultRegionPos.TryGetValue(region, out var dp)) return dp;
            int cols = Mathf.Min(3, Mathf.Max(1, n));
            int col = i % cols, row = i / cols;
            int rows = Mathf.Max(1, Mathf.CeilToInt(n / (float)cols));
            float x = Mathf.Lerp(0.26f, 0.80f, cols == 1 ? 0.5f : col / (float)(cols - 1));
            float y = Mathf.Lerp(0.66f, 0.30f, rows == 1 ? 0.5f : row / (float)(rows - 1));
            return new Vector2(x, y);
        }

        void ToastOnCanvas(RectTransform root, string msg)
        {
            SpawnLabel(root, msg, 15f, new Color(1f, 0.95f, 0.8f), TextAlignmentOptions.Center, true,
                new Vector2(0.30f, 0.11f), new Vector2(0.70f, 0.17f));
        }

        // WORLD MAP (PoC): các KHU VỰC trên bản đồ; bấm region → panel ải trong region → vào.
        void ShowWorldMap(int idx, int star)
        {
            try
            {
                AdventureFactory.EnsureAdventures(campaign);
                if (campaign.adventures == null || campaign.adventures.Count == 0 || _champSel == null) return;
                var champ = (idx >= 0 && idx < campaign.champions.Count) ? campaign.champions[idx] : null;
                string champName = champ != null ? champ.championName : "";
                if (champ != null) PlayerPrefs.SetString(LastChampKey, champName); // nhớ "tướng đang dùng"

                var root = MakeRootCanvas("WorldMapCanvas", 32000, out var canvasGO);
                var bg = root.gameObject.AddComponent<Image>();
                if (worldBackground != null) { bg.sprite = worldBackground; bg.color = Color.white; }
                else bg.color = PoCTheme.Bg0;

                SpawnLabel(root, "CHỌN KHU VỰC PHIÊU LƯU", 22f, accentColor, TextAlignmentOptions.Center, true,
                    new Vector2(0.2f, 0.905f), new Vector2(0.8f, 0.985f));

                BuildLadderBar(root, champName);   // M12: thanh target 0★→10★

                var regions = new List<string>();
                var byRegion = new Dictionary<string, List<AdventureData>>();
                foreach (var a in campaign.adventures)
                {
                    if (a == null) continue;
                    string rg = string.IsNullOrEmpty(a.region) ? "Khác" : a.region;
                    if (!byRegion.TryGetValue(rg, out var l)) { byRegion[rg] = l = new List<AdventureData>(); regions.Add(rg); }
                    l.Add(a);
                }

                Debug.Log($"[WorldMap] adventures={campaign.adventures.Count}, regions={regions.Count}: {string.Join(", ", regions)}");
                if (regions.Count == 0)
                    SpawnLabel(root, "Không có ải nào (campaign.adventures rỗng)", 16f, new Color(1f, 0.6f, 0.6f),
                        TextAlignmentOptions.Center, true, new Vector2(0.2f, 0.45f), new Vector2(0.8f, 0.55f));

                var target = TargetAdventure(champName);   // ải MỤC TIÊU kế → tô vàng + mũi tên khu vực chứa nó
                for (int i = 0; i < regions.Count; i++)
                {
                    string rg = regions[i];
                    var advs = byRegion[rg];
                    int done = 0; foreach (var a in advs) if (AdventureProgress.IsCompleted(champName, a.adventureName)) done++;
                    var pos = RegionAnchor(rg, i, regions.Count);
                    float rr = 0.05f;
                    bool isTarget = target != null && advs.Contains(target);
                    // ★ Đang đánh DỞ ải ở khu vực đích → chính vòng tròn này = nút TIẾP TỤC (xanh), click = vào lại node map.
                    bool resuming = isTarget && CampaignContext.map != null;
                    Color tgtCol = resuming ? new Color(0.24f, 0.72f, 0.42f, 1f) : PoCTheme.Gold; // xanh=tiếp tục · vàng=cày mới

                    // KHU VỰC ĐÍCH: vòng sáng phía sau (cue chính — luôn thấy dù ở đâu).
                    if (isTarget)
                    {
                        var glow = MakeRect("TargetGlow_" + i, root,
                            new Vector2(pos.x - rr - 0.024f, pos.y - rr - 0.03f - 0.024f),
                            new Vector2(pos.x + rr + 0.024f, pos.y + rr + 0.03f + 0.024f));
                        var gimg = glow.gameObject.AddComponent<Image>();
                        gimg.sprite = UISprites.Circle(); gimg.preserveAspect = true;
                        gimg.color = resuming ? new Color(0.30f, 0.88f, 0.52f, 0.95f) : new Color(0.98f, 0.82f, 0.30f, 0.95f);
                        gimg.raycastTarget = false;
                    }

                    var node = MakeRect("Region_" + i, root, new Vector2(pos.x - rr, pos.y - rr - 0.03f), new Vector2(pos.x + rr, pos.y + rr + 0.03f));
                    var nimg = node.gameObject.AddComponent<Image>();
                    nimg.sprite = UISprites.Circle(); nimg.preserveAspect = true;
                    nimg.color = isTarget ? tgtCol
                               : done >= advs.Count ? PoCTheme.Success
                               : PoCTheme.Surface2;
                    var nbtn = node.gameObject.AddComponent<Button>(); nbtn.targetGraphic = nimg;
                    var capRg = rg; var capAdvs = advs;
                    if (resuming)   // click vòng đích khi đang dở → TIẾP TỤC ải (vào node map)
                        nbtn.onClick.AddListener(() => { if (canvasGO != null) Destroy(canvasGO); if (_champSel != null) Destroy(_champSel); OpenMap(); });
                    else
                        nbtn.onClick.AddListener(() => ShowRegionPanel(capRg, capAdvs, champ, star, canvasGO));

                    SpawnLabel(root, $"<color=#{(isTarget ? "081521" : "EAF2F8")}><b>{done}/{advs.Count}</b></color>", 13f, Color.white, TextAlignmentOptions.Center, true,
                        new Vector2(pos.x - 0.05f, pos.y - 0.026f), new Vector2(pos.x + 0.05f, pos.y + 0.026f));
                    SpawnLabel(root, $"<b>{rg}</b>", 14f, isTarget ? tgtCol : PoCTheme.Cream, TextAlignmentOptions.Center, true,
                        new Vector2(pos.x - 0.13f, pos.y - 0.105f), new Vector2(pos.x + 0.13f, pos.y - 0.06f));

                    // Mũi tên + nhãn nổi ngay TRÊN khu vực đích. Đang dở → "▶ TIẾP TỤC" (xanh); chưa vào → "CÀY TIẾP {sao}".
                    if (isTarget)
                    {
                        float lblTop = Mathf.Min(pos.y + rr + 0.155f, 0.955f);
                        PoCTheme.Put(root, "TgtArrow", new Vector2(pos.x - 0.022f, pos.y + rr + 0.04f),
                            new Vector2(pos.x + 0.022f, pos.y + rr + 0.10f), PoCTheme.Triangle(), tgtCol, 180f);
                        SpawnLabel(root, resuming
                                ? "<b>▶ TIẾP TỤC ẢI</b>"
                                : $"<b>CÀY TIẾP</b>   <color=#FFF0B0>{target.starDifficulty:0.#} Sao</color>", 13f,
                            tgtCol, TextAlignmentOptions.Center, true,
                            new Vector2(pos.x - 0.15f, lblTop - 0.05f), new Vector2(pos.x + 0.15f, lblTop));
                    }
                }

                // Side UI (stub) + champion + tài nguyên
                var monthly = MakeRect("Monthly", root, new Vector2(0.02f, 0.62f), new Vector2(0.18f, 0.73f));
                MakeBtn(monthly, PoCTheme.Surface2, "THỬ THÁCH THÁNG", 11f, () => ToastOnCanvas(root, "Sắp có: Thử Thách Tháng"));
                var shop = MakeRect("Shop", root, new Vector2(0.02f, 0.49f), new Vector2(0.18f, 0.60f));
                MakeBtn(shop, PoCTheme.Lerp(PoCTheme.Gold, PoCTheme.Bg0, 0.35f), "CỬA HÀNG", 12f, () => ItemShop().Open());
                // M14: TÚI RƯƠNG — mở rương tích luỹ bất cứ lúc nào.
                var bag = MakeRect("Bag", root, new Vector2(0.02f, 0.36f), new Vector2(0.18f, 0.47f));
                int totalChests = ProgressStore.ChestCount(0) + ProgressStore.ChestCount(1)
                                + ProgressStore.ChestCount(2) + ProgressStore.ChestCount(3);
                MakeBtn(bag, PoCTheme.Lerp(PoCTheme.AmGold, PoCTheme.Bg0, 0.30f),
                    totalChests > 0 ? $"TÚI RƯƠNG ({totalChests})" : "TÚI RƯƠNG", 12f, () => ShowChestInventory());

                // ICON CHAMPION (mặt card + sao + cấp) — bấm để vào KHU SETUP CHAMPION (hub bên dưới).
                if (champ != null)
                {
                    int mastery = ProgressStore.GetMasteryLevel(champ.championName);
                    var badge = MakeRect("ChampBadge", root, new Vector2(0.015f, 0.02f), new Vector2(0.245f, 0.16f));
                    var bimg = RoundImg(badge, PoCTheme.Surface);   // giữ raycast → badge bấm được
                    var bbtn = badge.gameObject.AddComponent<Button>(); bbtn.targetGraphic = bimg;
                    var bc = bbtn.colors;
                    bc.normalColor = Color.white;
                    bc.highlightedColor = new Color(0.86f, 0.92f, 1f);
                    bc.pressedColor = new Color(0.70f, 0.70f, 0.76f);
                    bbtn.colors = bc;
                    bbtn.onClick.AddListener(() => Destroy(canvasGO)); // vào khu setup champion

                    // Mặt card champion
                    var face = MakeRect("Face", badge, new Vector2(0.04f, 0.14f), new Vector2(0.33f, 0.88f));
                    if (champ.championCard != null && champ.championCard.artwork != null)
                    {
                        var raw = face.gameObject.AddComponent<RawImage>();
                        raw.texture = champ.championCard.artwork; raw.raycastTarget = false;
                    }
                    else RoundImg(face, new Color(0.16f, 0.19f, 0.27f, 0.95f)).raycastTarget = false;

                    SpawnLabel(badge, $"<b>{champ.championName}</b>", 13.5f, PoCTheme.Cream, TextAlignmentOptions.Left, true,
                        new Vector2(0.36f, 0.56f), new Vector2(0.985f, 0.92f));
                    // Hàng SAO mastery vẽ THẬT (thay ★☆ hay bị ô vuông).
                    PoCTheme.StarRating(badge, star, ProgressStore.MaxStar, new Vector2(0.36f, 0.36f), new Vector2(0.80f, 0.52f));
                    SpawnLabel(badge, $"<color=#8AA4BC>Cấp {mastery}</color>", 10f, PoCTheme.Cream, TextAlignmentOptions.Left, false,
                        new Vector2(0.36f, 0.10f), new Vector2(0.72f, 0.30f));
                    SpawnLabel(badge, "<size=92%><color=#4FC5E0>Chỉnh sửa</color></size>", 10f, PoCTheme.Cyan, TextAlignmentOptions.Right, false,
                        new Vector2(0.72f, 0.10f), new Vector2(0.94f, 0.30f));
                    PoCTheme.Put(badge, "edit", new Vector2(0.945f, 0.13f), new Vector2(0.985f, 0.27f), PoCTheme.Triangle(), PoCTheme.Cyan, -90f);
                }
                // Tài nguyên: dời sang TRÁI (trên-trái) để chừa GÓC TRÊN-PHẢI cho nút QUAY LẠI (đồng nhất mọi màn).
                PoCTheme.Put(root, "EssDia", new Vector2(0.020f, 0.918f), new Vector2(0.040f, 0.952f), PoCTheme.Diamond(), PoCTheme.Cyan);
                SpawnLabel(root, $"Tinh Hồn {ProgressStore.Essence}", 13f, PoCTheme.Cream, TextAlignmentOptions.MidlineLeft, true,
                    new Vector2(0.047f, 0.905f), new Vector2(0.20f, 0.965f));
                PoCTheme.Put(root, "CoreDia", new Vector2(0.210f, 0.918f), new Vector2(0.230f, 0.952f), PoCTheme.Diamond(), PoCTheme.Gold);
                SpawnLabel(root, $"Lõi {ProgressStore.Cores}", 13f, PoCTheme.Cream, TextAlignmentOptions.MidlineLeft, true,
                    new Vector2(0.237f, 0.905f), new Vector2(0.37f, 0.965f));

                // ← QUAY LẠI = THOÁT về lobby. GÓC TRÊN-PHẢI (đồng nhất với ← MENU của node map / hub).
                var back = MakeRect("Back", root, new Vector2(0.865f, 0.905f), new Vector2(0.985f, 0.965f));
                MakeBtn(back, new Color(0.30f, 0.34f, 0.42f), "← QUAY LẠI", 12f, () =>
                {
                    Destroy(canvasGO);                       // đóng bản đồ ải
                    if (_champSel != null) Destroy(_champSel); // đóng luôn hub nền
                    Close();                                  // ẩn run map → về lobby
                });
            }
            catch (System.Exception ex) { Debug.LogError($"[RunMapView] Lỗi World Map: {ex}"); }
        }

        // Panel ải trong 1 KHU VỰC: node theo đường tăng độ khó — bấm mở khoá = VÀO.
        void ShowRegionPanel(string region, List<AdventureData> list, ChampionData champ, int star, GameObject worldCanvas)
        {
            try
            {
                var root = MakeRootCanvas("RegionPanel", 32100, out var canvasGO);
                string champName = champ != null ? champ.championName : "";
                var target = TargetAdventure(champName);   // ải cần đánh kế → tô vàng + "ĐÁNH ĐÂY"
                SetImage(root, PoCTheme.Bg0);              // nền ĐỤC HẲN (hết lộ nút phía sau)
                PoCTheme.Panel(root, "Hdr", new Vector2(0.30f, 0.865f), new Vector2(0.70f, 0.945f), PoCTheme.Surface, PoCTheme.Line);
                SpawnLabel(root, $"KHU VỰC — {region}", 22f, PoCTheme.Cyan, TextAlignmentOptions.Center, true,
                    new Vector2(0.1f, 0.865f), new Vector2(0.9f, 0.945f));

                int n = list.Count;
                float cy = 0.52f, r = 0.05f;
                for (int i = 0; i < n - 1; i++)
                {
                    float xa = Mathf.Lerp(0.14f, 0.86f, n == 1 ? 0.5f : i / (float)(n - 1));
                    float xb = Mathf.Lerp(0.14f, 0.86f, (i + 1) / (float)(n - 1));
                    bool p = list[i] != null && AdventureProgress.IsCompleted(champName, list[i].adventureName);
                    var ln = MakeRect("C_" + i, root, new Vector2(xa + r, cy - 0.008f), new Vector2(xb - r, cy + 0.008f));
                    RoundImg(ln, p ? PoCTheme.Success : PoCTheme.Line).raycastTarget = false;
                }
                for (int i = 0; i < n; i++)
                {
                    var adv = list[i]; if (adv == null) continue;
                    bool unlocked = IsAdventureUnlocked(adv, champName);
                    bool passed = AdventureProgress.IsCompleted(champName, adv.adventureName);
                    bool isTgt = adv == target;
                    float cx = Mathf.Lerp(0.14f, 0.86f, n == 1 ? 0.5f : i / (float)(n - 1));

                    if (passed || isTgt || adv == _selectedAdventure)
                    {
                        // Ải đích: vòng vàng TO hơn cho nổi bật.
                        float rp = isTgt ? 0.020f : 0.012f;
                        var ring = MakeRect("R_" + i, root, new Vector2(cx - r - rp, cy - 0.08f - rp), new Vector2(cx + r + rp, cy + 0.08f + rp));
                        var g = ring.gameObject.AddComponent<Image>(); g.sprite = UISprites.Circle(); g.preserveAspect = true;
                        g.color = isTgt ? PoCTheme.Gold : PoCTheme.A(PoCTheme.Gold, 0.55f); g.raycastTarget = false;
                    }

                    // Mũi tên (tam giác) + "ĐÁNH ĐÂY" nổi trên ải cần đánh kế.
                    if (isTgt && unlocked)
                    {
                        PoCTheme.Put(root, "HereArrow", new Vector2(cx - 0.022f, cy + 0.10f), new Vector2(cx + 0.022f, cy + 0.16f),
                            PoCTheme.Triangle(), PoCTheme.Gold, 180f);
                        SpawnLabel(root, "<b>ĐÁNH ĐÂY</b>", 14f, PoCTheme.Gold, TextAlignmentOptions.Center, true,
                            new Vector2(cx - 0.14f, cy + 0.155f), new Vector2(cx + 0.14f, cy + 0.205f));
                    }
                    var badge = MakeRect("N_" + i, root, new Vector2(cx - r, cy - 0.08f), new Vector2(cx + r, cy + 0.08f));
                    var bimg = badge.gameObject.AddComponent<Image>(); bimg.sprite = UISprites.Circle(); bimg.preserveAspect = true;
                    bimg.color = unlocked ? DiffColor(adv.starDifficulty) : PoCTheme.Locked;
                    // Số sao + icon SAO vẽ thật trong node (thay "{n}★").
                    if (unlocked) StarNum(badge, adv.starDifficulty, new Vector2(0.14f, 0.30f), new Vector2(0.86f, 0.70f), PoCTheme.Ink, 19f);
                    SpawnLabel(root, adv.adventureName, 13f, unlocked ? PoCTheme.Cream : PoCTheme.TextDim,
                        TextAlignmentOptions.Center, true, new Vector2(cx - 0.10f, cy - 0.165f), new Vector2(cx + 0.10f, cy - 0.09f));

                    if (unlocked && champ != null)
                    {
                        var b = badge.gameObject.AddComponent<Button>(); b.targetGraphic = bimg;
                        var cap = adv;
                        b.onClick.AddListener(() =>
                        {
                            _selectedAdventure = cap;
                            Destroy(canvasGO);
                            if (worldCanvas != null) Destroy(worldCanvas);
                            Destroy(_champSel);
                            StartRunWith(champ);
                        });
                    }
                    else if (!unlocked)
                    {
                        string reason = adv.prerequisite != null
                            ? $"cần qua {adv.prerequisite.starDifficulty:0.#} Sao trước"
                            : "khoá";
                        SpawnLabel(root, reason, 10.5f, PoCTheme.Danger, TextAlignmentOptions.Center, false,
                            new Vector2(cx - 0.12f, cy - 0.215f), new Vector2(cx + 0.12f, cy - 0.165f));
                    }
                }
                var back = MakeRect("Back", root, new Vector2(0.40f, 0.06f), new Vector2(0.60f, 0.115f));
                MakeBtn(back, PoCTheme.Surface2, "← KHU VỰC", 13f, () => Destroy(canvasGO));
            }
            catch (System.Exception ex) { Debug.LogError($"[RunMapView] Lỗi Region Panel: {ex}"); }
        }

        // Overlay chọn item để gắn vào ô trống (chỉ item chưa đeo).
        void ShowItemEquipPicker(int idx, string champName)
        {
            var items = ChampionItemDefs();
            if (items == null || _champSel == null) return;
            int mastery = ProgressStore.GetMasteryLevel(champName);

            var picker = MakeRect("ItemEquipPicker", _champSel.GetComponent<RectTransform>(), Vector2.zero, Vector2.one);
            var pcv = picker.gameObject.AddComponent<Canvas>();
            pcv.overrideSorting = true; pcv.sortingOrder = 5000;
            picker.gameObject.AddComponent<GraphicRaycaster>();
            SetImage(picker, new Color(0f, 0f, 0f, 0.88f));
            var panel = MakeRect("P", picker, new Vector2(0.30f, 0.14f), new Vector2(0.70f, 0.88f));
            RoundImg(panel, panelColor);
            SpawnLabel(panel, "GẮN TRANG BỊ", 20f, accentColor, TextAlignmentOptions.Center, true,
                new Vector2(0.05f, 0.90f), new Vector2(0.95f, 0.99f));

            var pool = new List<CardItemDef>();
            foreach (var it in items)
                if (it != null && !ProgressStore.IsItemEquipped(champName, it.itemName)
                    && ProgressStore.IsItemOwned(it.itemName)) pool.Add(it);   // chỉ item ĐÃ MUA

            float top = 0.86f, h = 0.135f, gap = 0.015f;
            for (int i = 0; i < pool.Count; i++)
            {
                var it = pool[i];
                bool owned = ProgressStore.IsItemOwned(it.itemName);
                bool unlocked = mastery >= it.unlockMasteryLevel || owned; // mua bằng Lõi cũng mở khoá lắp
                float y1 = top - i * (h + gap);
                var row = MakeRect("R_" + i, panel, new Vector2(0.05f, y1 - h), new Vector2(0.95f, y1));
                var rimg = RoundImg(row, unlocked ? new Color(0.15f, 0.19f, 0.27f, 0.96f) : new Color(0.11f, 0.12f, 0.16f, 0.9f));

                var hb = MakeRect("H", row, new Vector2(0.02f, 0.15f), new Vector2(0.16f, 0.85f));
                var himg = hb.gameObject.AddComponent<Image>(); himg.sprite = HexSprite(); himg.preserveAspect = true;
                himg.color = unlocked ? RarityTint(it.rarity) : new Color(0.30f, 0.32f, 0.38f); himg.raycastTarget = false;
                if (it.icon != null)
                {
                    var ic = MakeRect("Ic", hb, new Vector2(0.24f, 0.24f), new Vector2(0.76f, 0.76f));
                    var iimg = ic.gameObject.AddComponent<Image>(); iimg.sprite = it.icon; iimg.preserveAspect = true; iimg.raycastTarget = false;
                    if (!unlocked) iimg.color = new Color(1f, 1f, 1f, 0.4f);
                }
                string tag = unlocked
                    ? $"<color=#59D97A>{RarityVN(it.rarity)}</color>" + (owned && mastery < it.unlockMasteryLevel ? " <color=#5FCFD2>(đã mua)</color>" : "")
                    : (it.coreCost > 0 ? $"<color=#5FCFD2>Mua {it.coreCost} Lõi ở Cửa Hàng</color>" : $"<color=#9AA7B8>Khoá Cấp {it.unlockMasteryLevel}</color>");
                SpawnLabel(row, $"<b>{it.itemName}</b>  <size=74%>{tag}</size>\n<size=78%><color=#C9CFDA>{ItemStatLine(it)}</color></size>",
                    12.5f, Color.white, TextAlignmentOptions.Left, false, new Vector2(0.18f, 0.06f), new Vector2(0.97f, 0.94f))
                    .textWrappingMode = TextWrappingModes.Normal;

                if (unlocked)
                {
                    var btn = row.gameObject.AddComponent<Button>(); btn.targetGraphic = rimg;
                    var cap = it;
                    btn.onClick.AddListener(() =>
                    {
                        if (ProgressStore.EquipItem(champName, cap.itemName))
                        { Destroy(picker.gameObject); RenderChampDetail(idx); }
                    });
                }
            }

            var leave = MakeRect("X", panel, new Vector2(0.36f, 0.02f), new Vector2(0.64f, 0.08f));
            MakeBtn(leave, new Color(0.30f, 0.34f, 0.42f), "ĐÓNG", 13f, () => Destroy(picker.gameObject));
        }

        // ── Trang bị CHAMPION (CardItemLibrary, championEquip) ──
        List<CardItemDef> ChampionItemDefs()
        {
            var res = new List<CardItemDef>();
            var lib = campaign != null ? campaign.cardItemLibrary : null;
            if (lib != null && lib.items != null)
                foreach (var d in lib.items) if (d != null && d.championEquip) res.Add(d);
            return res;
        }

        // Mở khoá: đã mua (Lõi) HOẶC không bán bằng Lõi & đủ cấp mastery.
        static bool DefUnlocked(CardItemDef d, int mastery, bool owned)
            => owned || (d != null && d.coreCost <= 0 && mastery >= d.unlockMasteryLevel);

        static Sprite _hexSpriteMap;
        static Sprite HexSprite()
        {
            if (_hexSpriteMap != null) return _hexSpriteMap;
            int s = 64; float c = (s - 1) * 0.5f; float ap = c - 2f;
            const float k = 0.8660254f;
            var tex = new Texture2D(s, s, TextureFormat.RGBA32, false)
            { filterMode = FilterMode.Bilinear, wrapMode = TextureWrapMode.Clamp, name = "HexMap" };
            var px = new Color32[s * s];
            for (int yy = 0; yy < s; yy++)
                for (int xx = 0; xx < s; xx++)
                {
                    float dx = xx - c, dy = yy - c;
                    float m = ap - Mathf.Max(Mathf.Abs(dy),
                        Mathf.Max(Mathf.Abs(dx * k + dy * 0.5f), Mathf.Abs(-dx * k + dy * 0.5f)));
                    px[yy * s + xx] = new Color32(255, 255, 255, (byte)(Mathf.Clamp01(m + 0.5f) * 255f));
                }
            tex.SetPixels32(px); tex.Apply();
            _hexSpriteMap = Sprite.Create(tex, new Rect(0, 0, s, s), new Vector2(0.5f, 0.5f), 100f);
            return _hexSpriteMap;
        }

        // Chờ 1 frame cho layout xong rồi phóng to cả card (uniform localScale) cho vừa ô chứa.
        System.Collections.IEnumerator FitCard(RectTransform rt, RectTransform holder)
        {
            yield return null;
            Canvas.ForceUpdateCanvases();
            if (rt == null || holder == null) yield break;
            float nw = rt.rect.width, nh = rt.rect.height;
            float hw = holder.rect.width, hh = holder.rect.height;
            if (nw > 1f && nh > 1f && hw > 1f && hh > 1f)
            {
                float s = Mathf.Min(hw / nw, hh / nh) * 0.98f;
                rt.localScale = new Vector3(s, s, 1f);
            }
        }

        // Ảnh champion = CARD THẬT (giống PoC). Ưu tiên render qua cardPrefab; fallback artwork + stats.
        void BuildChampCard(ChampionData champ, Vector2 ancMin, Vector2 ancMax)
        {
            var holder = MakeRect("ChampCard", _champDetail, ancMin, ancMax);
            var cd = champ.championCard;
            if (cd == null) { RoundImg(holder, new Color(0.16f, 0.19f, 0.27f, 0.95f)); return; }

            // 1) Card THẬT — giữ NGUYÊN thiết kế gốc (viền, vị trí...) ở size chuẩn, rồi PHÓNG TO cả card
            //    bằng transform.localScale cho vừa khung. KHÔNG dùng OverrideSize (nó đổi sizeDelta → bể layout bên trong).
            var prefab = cardPrefab != null ? cardPrefab : (campaignView != null ? campaignView.cardPrefab : null);
            if (prefab != null)
            {
                var go = Instantiate(prefab, holder);
                var cv = go.GetComponent<CardView>();
                if (cv != null)
                {
                    var gr = go.GetComponent<GraphicRaycaster>(); if (gr != null) Destroy(gr);
                    var cvCanvas = go.GetComponent<Canvas>(); if (cvCanvas != null) Destroy(cvCanvas);

                    var model = new CardModel(cd, true);
                    model.SetLocation(CardLocation.InHand);
                    cv.AllowInteract = false;
                    cv.Bind(model);
                    cv.ResetToPortraitSize();   // size gốc, đầy đủ thiết kế (viền, badge...)
                    cv.enabled = false;

                    var rt = go.GetComponent<RectTransform>();
                    rt.anchorMin = rt.anchorMax = rt.pivot = new Vector2(0.5f, 0.5f);
                    rt.anchoredPosition = Vector2.zero;
                    StartCoroutine(FitCard(rt, holder)); // zoom CẢ card cho vừa khung
                    return;
                }
                Destroy(go);
            }

            // 2) Fallback: khung + artwork + cost + stats
            RoundImg(holder, new Color(0.09f, 0.11f, 0.16f, 0.98f));
            var art = MakeRect("Art", holder, new Vector2(0.06f, 0.30f), new Vector2(0.94f, 0.92f));
            if (cd.artwork != null)
            {
                var raw = art.gameObject.AddComponent<RawImage>();
                raw.texture = cd.artwork; raw.raycastTarget = false;
            }
            else RoundImg(art, new Color(0.16f, 0.19f, 0.27f, 0.95f)).raycastTarget = false;

            var cost = MakeRect("Cost", holder, new Vector2(0.04f, 0.82f), new Vector2(0.24f, 0.98f));
            RoundImg(cost, new Color(0.15f, 0.35f, 0.7f, 0.98f)).raycastTarget = false;
            SpawnLabel(cost, cd.manaCost.ToString(), 18f, Color.white, TextAlignmentOptions.Center, true);
            SpawnLabel(holder, $"<b>{cd.cardName}</b>", 14f, Color.white, TextAlignmentOptions.Center, true,
                new Vector2(0.03f, 0.17f), new Vector2(0.97f, 0.30f));

            var cm = new CardModel(cd, true);
            var a = MakeRect("Atk", holder, new Vector2(0.05f, 0.02f), new Vector2(0.30f, 0.16f));
            RoundImg(a, new Color(0.2f, 0.4f, 0.75f, 0.98f)).raycastTarget = false;
            SpawnLabel(a, cm.currentAttack.ToString(), 16f, Color.white, TextAlignmentOptions.Center, true);
            var h = MakeRect("Hp", holder, new Vector2(0.70f, 0.02f), new Vector2(0.95f, 0.16f));
            RoundImg(h, new Color(0.75f, 0.24f, 0.24f, 0.98f)).raycastTarget = false;
            SpawnLabel(h, cm.currentHealth.ToString(), 16f, Color.white, TextAlignmentOptions.Center, true);
        }

        // ── PoC: CARD làm chủ đạo — mỗi lá deck kèm 1 TRANG BỊ, chọn/mua ngay trên lá (1 màn, không tách bước) ──
        void ShowRewardPick(int nodeId, bool rerollUsed = false) => ShowCardEquipPick(nodeId, "PHẦN THƯỞNG — CHỌN LÁ ĐỂ NHẬN TRANG BỊ", false, rerollUsed);
        void ShowItemPick(int nodeId, bool rerollUsed = false) => ShowCardEquipPick(nodeId, "VẬT PHẨM — CHỌN LÁ ĐỂ NHẬN TRANG BỊ", false, rerollUsed);
        // ── CỬA HÀNG: hub 3 tab (Trang bị / Sức mạnh / Hồi máu) + BỎ QUA ──
        int _shopTab = 0;
        void ShowShop(int nodeId)
        {
            void Complete() { if (nodeId >= 0 && CampaignContext.map != null) CampaignContext.map.CompleteNode(nodeId); RunSaveStore.Save(); Render(); }

            var picker = MakeRect("Shop", _overlay.GetComponent<RectTransform>(), Vector2.zero, Vector2.one);
            SetImage(picker, new Color(0f, 0f, 0f, 0.86f));
            var panel = MakeRect("P", picker, new Vector2(0.07f, 0.08f), new Vector2(0.93f, 0.93f));
            RoundImg(panel, panelColor);

            var content = MakeRect("Content", panel, new Vector2(0.02f, 0.10f), new Vector2(0.98f, 0.82f));

            void Rebuild()
            {
                // Tiêu đề + vàng
                for (int i = panel.childCount - 1; i >= 0; i--)
                {
                    var ch = panel.GetChild(i);
                    if (ch.name == "Content") continue;
                    Destroy(ch.gameObject);
                }
                SpawnLabel(panel, $"<b>CỬA HÀNG</b>    <color=#F5D98A>Vàng: {CampaignRun.gold}</color>", 20f,
                    PoCTheme.Hex("E0A64E"), TextAlignmentOptions.Center, true, new Vector2(0.03f, 0.92f), new Vector2(0.97f, 0.99f));

                // Tab bar
                string[] tabs = { "TRANG BỊ", "SỨC MẠNH", "HỒI MÁU" };
                float tw = 0.28f, tgap = 0.02f, tx0 = 0.05f;
                for (int t = 0; t < 3; t++)
                {
                    int ti = t; float x0 = tx0 + t * (tw + tgap);
                    var tab = MakeRect("T_" + t, panel, new Vector2(x0, 0.835f), new Vector2(x0 + tw, 0.905f));
                    bool on = _shopTab == t;
                    var timg = RoundImg(tab, on ? PoCTheme.AmGold : new Color(0.18f, 0.21f, 0.28f, 0.95f));
                    SpawnLabel(tab, tabs[t], 13f, on ? PoCTheme.Hex("1A1206") : Color.white, TextAlignmentOptions.Center, true);
                    var tb = tab.gameObject.AddComponent<Button>(); tb.targetGraphic = timg;
                    tb.onClick.AddListener(() => { _shopTab = ti; Rebuild(); });
                }

                // BỎ QUA (rời cửa hàng)
                var skip = MakeRect("Skip", panel, new Vector2(0.40f, 0.015f), new Vector2(0.60f, 0.085f));
                MakeBtn(skip, new Color(0.30f, 0.34f, 0.42f), "BỎ QUA", 13f, () => { Destroy(picker.gameObject); Complete(); });

                // Nội dung tab
                for (int i = content.childCount - 1; i >= 0; i--) Destroy(content.GetChild(i).gameObject);
                if (_shopTab == 0) BuildEquipScroll(content, true, Rebuild);
                else if (_shopTab == 1) BuildPowerShop(content, Rebuild);
                else BuildHealShop(content, Rebuild);
            }
            _shopTab = 0;
            Rebuild();
        }

        // TAB SỨC MẠNH: mua Lõi Power bằng Vàng (3 lựa chọn, chưa sở hữu).
        void BuildPowerShop(RectTransform host, System.Action refresh)
        {
            var all = new List<PowerData>();
            if (campaign.powerCores != null)
                foreach (var p in campaign.powerCores)
                    if (p != null && p.IsMeaningful && !CampaignRun.HasPower(p)) all.Add(p);
            if (all.Count == 0)
            {
                SpawnLabel(host, "Hết Lõi Sức Mạnh để mua (đã sở hữu tất cả).", 14f, PoCTheme.AmDim,
                    TextAlignmentOptions.Center, false); return;
            }
            for (int i = all.Count - 1; i > 0; i--) { int j = UnityEngine.Random.Range(0, i + 1); (all[i], all[j]) = (all[j], all[i]); }
            int n = Mathf.Min(3, all.Count);
            float gap = 0.02f, cw = (0.9f - gap * (n - 1)) / n, x0 = 0.05f;
            for (int i = 0; i < n; i++)
            {
                var p = all[i]; int cost = PowerPrice(p);
                float cx0 = x0 + i * (cw + gap);
                var cell = MakeRect("PW_" + i, host, new Vector2(cx0, 0.08f), new Vector2(cx0 + cw, 0.95f));
                RoundImg(cell, PoCTheme.A(RarityTint(p.rarity), 0.32f));
                var hx = MakeRect("h", cell, new Vector2(0.36f, 0.78f), new Vector2(0.64f, 0.96f));
                var himg = hx.gameObject.AddComponent<Image>(); himg.sprite = HexSprite(); himg.preserveAspect = true;
                himg.color = RarityTint(p.rarity); himg.raycastTarget = false;
                if (p.icon != null)
                {
                    var ic = MakeRect("ic", hx, new Vector2(0.24f, 0.24f), new Vector2(0.76f, 0.76f));
                    var ii = ic.gameObject.AddComponent<Image>(); ii.sprite = p.icon; ii.preserveAspect = true; ii.raycastTarget = false;
                }
                SpawnLabel(cell, $"<b>{p.powerName}</b>", 14f, PoCTheme.AmGoldHi, TextAlignmentOptions.Center, true,
                    new Vector2(0.05f, 0.66f), new Vector2(0.95f, 0.77f));
                SpawnLabel(cell, p.AutoDescribe(), 11.5f, new Color(0.9f, 0.92f, 0.97f), TextAlignmentOptions.Center, false,
                    new Vector2(0.07f, 0.24f), new Vector2(0.93f, 0.64f)).textWrappingMode = TextWrappingModes.Normal;

                bool afford = CampaignRun.gold >= cost;
                var buy = MakeRect("buy", cell, new Vector2(0.2f, 0.06f), new Vector2(0.8f, 0.19f));
                var bimg = RoundImg(buy, afford ? PoCTheme.AmGold : new Color(0.3f, 0.32f, 0.38f, 0.9f));
                SpawnLabel(buy, $"{cost} Vàng", 12f, afford ? PoCTheme.Hex("1A1206") : PoCTheme.AmDim, TextAlignmentOptions.Center, true);
                if (afford)
                {
                    var bb = buy.gameObject.AddComponent<Button>(); bb.targetGraphic = bimg;
                    var cap = p; int c2 = cost;
                    bb.onClick.AddListener(() => { CampaignRun.SpendGold(c2); CampaignRun.AddPower(cap); RunSaveStore.Save(); refresh(); });
                }
            }
        }

        // TAB HỒI MÁU: mua hồi Nexus bằng Vàng (mức cố định + hồi đầy).
        void BuildHealShop(RectTransform host, System.Action refresh)
        {
            int cur = CampaignRun.nexusCurrent, max = Mathf.Max(1, CampaignRun.nexusMax);
            SpawnLabel(host, $"Nexus: <color=#8FE0B0>{cur}</color> / {max}", 18f, Color.white,
                TextAlignmentOptions.Center, true, new Vector2(0.1f, 0.8f), new Vector2(0.9f, 0.96f));
            if (cur >= max)
            {
                SpawnLabel(host, "Nexus đã đầy.", 14f, PoCTheme.AmDim, TextAlignmentOptions.Center, false,
                    new Vector2(0.1f, 0.4f), new Vector2(0.9f, 0.7f)); return;
            }
            // 2 gói: +5 (30 vàng) và HỒI ĐẦY (100 vàng)
            void Offer(float x0, float x1, string label, int heal, int cost, bool full)
            {
                var cell = MakeRect("H_" + label, host, new Vector2(x0, 0.3f), new Vector2(x1, 0.75f));
                RoundImg(cell, new Color(0.16f, 0.30f, 0.24f, 0.95f));
                SpawnLabel(cell, label, 15f, PoCTheme.Hex("8FE0B0"), TextAlignmentOptions.Center, true,
                    new Vector2(0.05f, 0.5f), new Vector2(0.95f, 0.92f));
                bool afford = CampaignRun.gold >= cost;
                var buy = MakeRect("buy", cell, new Vector2(0.2f, 0.12f), new Vector2(0.8f, 0.4f));
                var bimg = RoundImg(buy, afford ? PoCTheme.AmGold : new Color(0.3f, 0.32f, 0.38f, 0.9f));
                SpawnLabel(buy, $"{cost} Vàng", 12f, afford ? PoCTheme.Hex("1A1206") : PoCTheme.AmDim, TextAlignmentOptions.Center, true);
                if (afford)
                {
                    var bb = buy.gameObject.AddComponent<Button>(); bb.targetGraphic = bimg;
                    bb.onClick.AddListener(() =>
                    {
                        CampaignRun.SpendGold(cost);
                        if (full) CampaignRun.HealNexusFull();
                        else CampaignRun.nexusCurrent = Mathf.Min(max, CampaignRun.nexusCurrent + heal);
                        RunSaveStore.Save(); refresh();
                    });
                }
            }
            Offer(0.08f, 0.48f, "+5 Nexus", 5, 30, false);
            Offer(0.52f, 0.92f, "HỒI ĐẦY", 0, 100, true);
        }

        static int PowerPrice(PowerData p) => p == null ? 0 : (p.goldCost > 0 ? p.goldCost
            : p.rarity == LoRClone.Model.ItemRarity.Epic ? 140
            : p.rarity == LoRClone.Model.ItemRarity.Rare ? 90 : 55);

        // ── ẢI SỨC MẠNH: chọn 1 trong 3 LÕI POWER vào hành trình (kiểu chọn ngọc/core LoL/PoC) ──
        void ShowPowerPick(int nodeId)
        {
            // nodeId >= 0 → ải Power trên map (complete node). nodeId < 0 → thưởng sau MiniBoss (không có node).
            void Complete() { if (nodeId >= 0 && CampaignContext.map != null) CampaignContext.map.CompleteNode(nodeId); RunSaveStore.Save(); Render(); }

            // Bốc 3 power KHÁC nhau từ kho, ưu tiên power chưa sở hữu.
            var all = new List<PowerData>();
            if (campaign.powerCores != null)
                foreach (var p in campaign.powerCores)
                    if (p != null && p.IsMeaningful && !CampaignRun.HasPower(p)) all.Add(p);
            if (all.Count == 0) { Complete(); return; }   // hết power → bỏ qua node
            for (int i = all.Count - 1; i > 0; i--) { int j = UnityEngine.Random.Range(0, i + 1); (all[i], all[j]) = (all[j], all[i]); }
            int n = Mathf.Min(3, all.Count);

            var picker = MakeRect("PowerPick", _overlay.GetComponent<RectTransform>(), Vector2.zero, Vector2.one);
            SetImage(picker, new Color(0f, 0f, 0f, 0.86f));
            var panel = MakeRect("P", picker, new Vector2(0.10f, 0.14f), new Vector2(0.90f, 0.90f));
            RoundImg(panel, panelColor);
            SpawnLabel(panel, "CHỌN LÕI SỨC MẠNH", 22f, PoCTheme.Hex("D69BF2"), TextAlignmentOptions.Center, true,
                new Vector2(0.03f, 0.9f), new Vector2(0.97f, 0.99f));
            SpawnLabel(panel, "Lõi đi theo suốt hành trình — đổi cách vận hành trận, không chồng chất chỉ số.",
                12.5f, new Color(0.82f, 0.86f, 0.94f), TextAlignmentOptions.Center, false,
                new Vector2(0.05f, 0.83f), new Vector2(0.95f, 0.9f));

            float gap = 0.02f, cw = (0.94f - gap * (n - 1)) / n, x0 = 0.03f;
            for (int i = 0; i < n; i++)
            {
                var p = all[i];
                float cx0 = x0 + i * (cw + gap);
                var cell = MakeRect("PC_" + i, panel, new Vector2(cx0, 0.16f), new Vector2(cx0 + cw, 0.8f));
                var cimg = RoundImg(cell, PoCTheme.A(RarityTint(p.rarity), 0.35f));

                // icon hex to
                var hx = MakeRect("h", cell, new Vector2(0.34f, 0.72f), new Vector2(0.66f, 0.95f));
                var himg = hx.gameObject.AddComponent<Image>(); himg.sprite = HexSprite(); himg.preserveAspect = true;
                himg.color = RarityTint(p.rarity); himg.raycastTarget = false;
                if (p.icon != null)
                {
                    var ic = MakeRect("ic", hx, new Vector2(0.24f, 0.24f), new Vector2(0.76f, 0.76f));
                    var ii = ic.gameObject.AddComponent<Image>(); ii.sprite = p.icon; ii.preserveAspect = true; ii.raycastTarget = false;
                }
                SpawnLabel(cell, $"<b>{p.powerName}</b>", 15f, PoCTheme.AmGoldHi, TextAlignmentOptions.Center, true,
                    new Vector2(0.05f, 0.6f), new Vector2(0.95f, 0.71f));
                SpawnLabel(cell, $"<color=#C79BF0>{RarityVN(p.rarity)}</color>", 11f, Color.white, TextAlignmentOptions.Center, false,
                    new Vector2(0.05f, 0.53f), new Vector2(0.95f, 0.6f));
                SpawnLabel(cell, p.AutoDescribe(), 12.5f, new Color(0.90f, 0.92f, 0.97f), TextAlignmentOptions.Center, false,
                    new Vector2(0.07f, 0.14f), new Vector2(0.93f, 0.52f)).textWrappingMode = TextWrappingModes.Normal;

                var hit = MakeRect("hit", cell, Vector2.zero, Vector2.one);
                var hi = SetImage(hit, new Color(0f, 0f, 0f, 0f)); hi.raycastTarget = true;
                var btn = hit.gameObject.AddComponent<Button>(); btn.targetGraphic = hi;
                var cb = btn.colors; cb.highlightedColor = new Color(1f, 1f, 1f, 0.12f); btn.colors = cb;
                var cap = p;
                btn.onClick.AddListener(() => { CampaignRun.AddPower(cap); Destroy(picker.gameObject); Complete(); });
            }

            var skip = MakeRect("Skip", panel, new Vector2(0.4f, 0.03f), new Vector2(0.6f, 0.11f));
            MakeBtn(skip, new Color(0.30f, 0.34f, 0.42f), "BỎ QUA", 13f, () => { Destroy(picker.gameObject); Complete(); });
        }

        /// <summary>
        /// Màn chọn CARD-chủ-đạo: hiện các lá trong deck (prefab thật), MỖI lá kèm 1 trang bị đề xuất ngay dưới.
        /// Bấm 1 lá = nhận/mua trang bị của lá đó. Không có bước "chọn item" riêng.
        /// Shop: bấm = trừ vàng + gắn rồi mở lại (mua tiếp); rời bằng nút. ★4: nút ĐỔI MỚI roll lại (1 lần/màn).
        /// </summary>
        // THƯỞNG / VẬT PHẨM: chọn 1 lá để GẮN trang bị (không phải shop). Dùng chung grid BuildEquipScroll.
        void ShowCardEquipPick(int nodeId, string title, bool isShop, bool rerollUsed)
        {
            void Complete() { if (nodeId >= 0 && CampaignContext.map != null) CampaignContext.map.CompleteNode(nodeId); RunSaveStore.Save(); Render(); }
            if (DistinctDeck().Count == 0) { Complete(); return; }

            var picker = MakeRect("CardEquipPick", _overlay.GetComponent<RectTransform>(), Vector2.zero, Vector2.one);
            SetImage(picker, new Color(0f, 0f, 0f, 0.85f));
            var panel = MakeRect("P", picker, new Vector2(0.08f, 0.09f), new Vector2(0.92f, 0.93f));
            RoundImg(panel, panelColor);
            SpawnLabel(panel, title, 19f, itemColor, TextAlignmentOptions.Center, true,
                new Vector2(0.03f, 0.92f), new Vector2(0.97f, 0.99f));

            var content = MakeRect("Content", panel, new Vector2(0.02f, 0.10f), new Vector2(0.98f, 0.90f));
            System.Action fill = null;
            fill = () =>
            {
                for (int i = content.childCount - 1; i >= 0; i--) Destroy(content.GetChild(i).gameObject);
                BuildEquipScroll(content, false, () => { Destroy(picker.gameObject); Complete(); });
            };
            fill();

            var leave = MakeRect("Skip", panel, new Vector2(0.33f, 0.015f), new Vector2(0.57f, 0.08f));
            MakeBtn(leave, new Color(0.30f, 0.34f, 0.42f), "BỎ QUA", 13f, () => { Destroy(picker.gameObject); Complete(); });
            if (CampaignRun.ConsCanReroll() && !rerollUsed)
            {
                var rr = MakeRect("Reroll", panel, new Vector2(0.60f, 0.015f), new Vector2(0.80f, 0.08f));
                MakeBtn(rr, new Color(0.42f, 0.32f, 0.62f), "ĐỔI MỚI", 13f, () => fill());
            }
        }

        // GRID CUỘN card+trang bị — GridLayoutGroup ô ĐỀU NHAU (fix lệch size). Mỗi ô: card trên + panel item dưới.
        // isShop=true: mua bằng Vàng, ở lại (onAction = rebuild). isShop=false: gắn 1 lá rồi onAction (đóng + complete).
        void BuildEquipScroll(RectTransform host, bool isShop, System.Action onAction)
        {
            var cards = DistinctDeck();
            var items = new List<CardItem>();
            for (int i = 0; i < cards.Count; i++) items.Add(RollItemByDifficulty());

            if (CardItem.AllFromLibrary().Count == 0)
            {
                var lib = CampaignContext.campaign != null ? CampaignContext.campaign.cardItemLibrary : null;
                string why = CampaignContext.campaign == null ? "CampaignContext.campaign = null"
                           : lib == null ? "campaign.cardItemLibrary CHƯA gán (null)"
                           : "cardItemLibrary.items rỗng (chưa thêm item)";
                Debug.LogWarning($"[RunMapView] Kho trang bị rỗng → {why}.");
                SpawnLabel(host, $"<color=#E88C6E>Kho trang bị trống.</color>\n<size=80%>{why}</size>", 15f,
                    Color.white, TextAlignmentOptions.Center, false).textWrappingMode = TextWrappingModes.Normal;
                return;
            }

            var area = MakeRect("Area", host, Vector2.zero, Vector2.one);
            var sr = area.gameObject.AddComponent<UnityEngine.UI.ScrollRect>();
            sr.horizontal = false; sr.movementType = UnityEngine.UI.ScrollRect.MovementType.Clamped; sr.scrollSensitivity = 28f;
            var vp = MakeRect("VP", area, Vector2.zero, Vector2.one);
            var vpImg = vp.gameObject.AddComponent<Image>(); vpImg.color = new Color(1f, 1f, 1f, 0.006f);
            var mask = vp.gameObject.AddComponent<UnityEngine.UI.Mask>(); mask.showMaskGraphic = false;
            var contentR = MakeRect("C", vp, new Vector2(0f, 1f), new Vector2(1f, 1f));
            contentR.pivot = new Vector2(0.5f, 1f); contentR.offsetMin = contentR.offsetMax = Vector2.zero;
            var grid = contentR.gameObject.AddComponent<UnityEngine.UI.GridLayoutGroup>();
            grid.cellSize = new Vector2(196f, 292f);
            grid.spacing = new Vector2(14f, 14f);
            grid.padding = new RectOffset(12, 12, 8, 12);
            grid.constraint = UnityEngine.UI.GridLayoutGroup.Constraint.FixedColumnCount;
            grid.constraintCount = 4;
            grid.childAlignment = TextAnchor.UpperCenter;
            contentR.gameObject.AddComponent<UnityEngine.UI.ContentSizeFitter>().verticalFit =
                UnityEngine.UI.ContentSizeFitter.FitMode.PreferredSize;
            sr.viewport = vp; sr.content = contentR;

            for (int i = 0; i < cards.Count; i++)
            {
                var cd = cards[i]; var it = items[i];
                if (it == null) continue;
                var cell = MakeRect("Cell_" + i, contentR, Vector2.zero, Vector2.one);
                RoundImg(cell, PoCTheme.A(PoCTheme.NodeNavy, 0.55f)).raycastTarget = false;

                // CARD (trên) — FitCard giữ tỉ lệ, căn giữa trong khung cố định.
                var cardHolder = MakeRect("Card", cell, new Vector2(0.06f, 0.33f), new Vector2(0.94f, 0.985f));
                BuildCardBig(cd, cardHolder);

                // PANEL TRANG BỊ (dưới) — nền theo độ hiếm, icon trái, tên+mô tả+giá.
                int have = RunItems.CountOf(cd.cardName);
                var lbl = MakeRect("Item", cell, new Vector2(0.04f, 0.02f), new Vector2(0.96f, 0.31f));
                RoundImg(lbl, RarityTint(it.rarity)).raycastTarget = false;
                float txtL = 0.06f;
                if (it.icon != null)
                {
                    var ic = MakeRect("Ic", lbl, new Vector2(0.04f, 0.30f), new Vector2(0.24f, 0.92f));
                    var im = SetImage(ic, Color.white); im.sprite = it.icon; im.preserveAspect = true; im.raycastTarget = false;
                    txtL = 0.27f;
                }
                string price = isShop ? $"  <color=#F5D98A>{ItemPrice(it)}v</color>" : "";
                string owned = have > 0 ? $"  <color=#BFE9E0>x{have}</color>" : "";
                SpawnLabel(lbl, $"<b>{it.itemName}</b> <size=68%>({RarityVN(it.rarity)})</size>{price}{owned}", 11.5f,
                    Color.white, TextAlignmentOptions.TopLeft, true, new Vector2(txtL, 0.5f), new Vector2(0.97f, 0.95f))
                    .textWrappingMode = TextWrappingModes.Normal;
                SpawnLabel(lbl, $"<size=90%>{it.Describe()}</size>", 10.5f, new Color(0.92f, 0.94f, 0.98f),
                    TextAlignmentOptions.TopLeft, false, new Vector2(0.06f, 0.06f), new Vector2(0.97f, 0.48f))
                    .textWrappingMode = TextWrappingModes.Normal;

                // Nút phủ cả ô.
                var hit = MakeRect("Hit", cell, Vector2.zero, Vector2.one);
                var himg = SetImage(hit, new Color(0f, 0f, 0f, 0f)); himg.raycastTarget = true;
                var btn = hit.gameObject.AddComponent<Button>(); btn.targetGraphic = himg;
                var cb = btn.colors; cb.highlightedColor = new Color(1f, 0.98f, 0.88f, 0.12f); btn.colors = cb;
                var capCd = cd; var capIt = it;
                btn.onClick.AddListener(() =>
                {
                    if (isShop)
                    {
                        int cost = ItemPrice(capIt);
                        if (CampaignRun.gold < cost) return;   // thiếu vàng → không làm gì
                        CampaignRun.SpendGold(cost);
                        RunItems.Attach(capCd.cardName, capIt);
                        RunSaveStore.Save();
                        onAction();   // shop: rebuild (restock + cập nhật vàng)
                    }
                    else
                    {
                        RunItems.Attach(capCd.cardName, capIt);
                        RunSaveStore.Save();
                        onAction();   // reward/item: đóng + complete
                    }
                });
            }
        }

        // Roll n trang bị KHÁC tên (theo độ khó). ItemGoldCost: giá theo độ hiếm.
        List<CardItem> RollDistinctItems(int n)
        {
            var res = new List<CardItem>();
            var seen = new System.Collections.Generic.HashSet<string>();
            int guard = 0;
            while (res.Count < n && guard++ < 60)
            {
                var it = RollItemByDifficulty();
                if (it != null && seen.Add(it.itemName)) res.Add(it);
            }
            return res;
        }

        // Giá VÀNG của 1 món: ưu tiên goldCost tự đặt trong library; 0 → tính theo độ hiếm.
        static int ItemPrice(CardItem it) => it == null ? 0 : (it.goldCost > 0 ? it.goldCost : ItemGoldCost(it.rarity));

        static int ItemGoldCost(ItemRarity r) =>
            r == ItemRarity.Epic ? 110 : r == ItemRarity.Rare ? 70 : 40;

        CardItem RollItemByDifficulty()
        {
            int diff = Mathf.Max(1, CampaignRun.difficulty);
            float r = UnityEngine.Random.value;
            float epicP = 0.06f + 0.05f * (diff - 1);   // Thường 6% → Ác Mộng 16%
            float rareP = 0.30f + 0.05f * (diff - 1);
            ItemRarity rar = r < epicP ? ItemRarity.Epic
                           : r < epicP + rareP ? ItemRarity.Rare
                           : ItemRarity.Common;
            return CardItem.RandomOf(rar);
        }

        static List<CardData> DistinctDeck()
        {
            var seen = new HashSet<string>();
            var list = new List<CardData>();
            foreach (var c in CampaignRun.RunDeck)
                if (c != null && seen.Add(c.cardName)) list.Add(c);
            return list;
        }

        static string RarityVN(ItemRarity r) =>
            r == ItemRarity.Epic ? "Sử Thi" : r == ItemRarity.Rare ? "Hiếm" : "Thường";

        static Color RarityTint(ItemRarity r) =>
            r == ItemRarity.Epic ? new Color(0.42f, 0.30f, 0.60f) :
            r == ItemRarity.Rare ? new Color(0.22f, 0.40f, 0.60f) :
                                   new Color(0.26f, 0.42f, 0.40f);

        // ── M10: SỰ KIỆN (lựa chọn) ───────────────────────────────
        void ShowEvent(int nodeId)
        {
            var picker = MakeRect("EventPicker", _overlay.GetComponent<RectTransform>(), Vector2.zero, Vector2.one);
            SetImage(picker, new Color(0f, 0f, 0f, 0.82f));
            var panel = MakeRect("P", picker, new Vector2(0.28f, 0.24f), new Vector2(0.72f, 0.82f));
            RoundImg(panel, panelColor);
            SpawnLabel(panel, "SỰ KIỆN", 22f, eventColor, TextAlignmentOptions.Center, true,
                new Vector2(0.05f, 0.87f), new Vector2(0.95f, 0.97f));
            SpawnLabel(panel, "Trên đường đi bạn gặp một ngã rẽ...", 13f, new Color(0.85f, 0.88f, 0.94f),
                TextAlignmentOptions.Center, false, new Vector2(0.05f, 0.79f), new Vector2(0.95f, 0.86f));

            int diff = Mathf.Max(1, CampaignRun.difficulty);
            float top = 0.73f, h = 0.16f, gap = 0.03f;
            int slot = 0;
            // autoComplete=false: act() tự lo hoàn thành node (vd mở overlay chọn trang bị rồi mới complete).
            void AddChoice(string label, System.Action act, bool autoComplete = true)
            {
                float y1 = top - slot * (h + gap); slot++;
                var brt = MakeRect("E_" + slot, panel, new Vector2(0.06f, y1 - h), new Vector2(0.94f, y1));
                var bimg = RoundImg(brt, new Color(0.24f, 0.34f, 0.5f, 0.98f));
                var btn = brt.gameObject.AddComponent<Button>(); btn.targetGraphic = bimg;
                SpawnLabel(brt, label, 14f, Color.white, TextAlignmentOptions.Center, false,
                    new Vector2(0.04f, 0.05f), new Vector2(0.96f, 0.95f)).textWrappingMode = TextWrappingModes.Normal;
                btn.onClick.AddListener(() =>
                {
                    if (autoComplete)
                    {
                        act();
                        CampaignContext.map.CompleteNode(nodeId);
                        RunSaveStore.Save();
                        Destroy(picker.gameObject);
                        Render();
                    }
                    else act();
                });
            }

            // Khổ luyện: nhận 1 TRANG BỊ (core) — mở màn chọn trang bị + gắn lá, tự complete khi xong.
            AddChoice("Khổ luyện — nhận 1 TRANG BỊ", () => { Destroy(picker.gameObject); ShowItemPick(nodeId); }, autoComplete: false);
            AddChoice($"Cướp bóc — +{40 * diff} vàng", () => CampaignRun.AddGold(40 * diff));
            AddChoice($"Cầu nguyện — 50% +{80 * diff} vàng, 50% không gì", () => { if (UnityEngine.Random.value < 0.5f) CampaignRun.AddGold(80 * diff); });
        }

        // ── Mốc 4: NHẶT CỔ VẬT (Relic) ────────────────────────────
        void MaybeShowRelicPick()
        {
            // ĐÃ TẮT: cổ vật là nguồn buff stat toàn quân → bỏ để tránh lạm phát chỉ số.
            // Elite thắng giờ thưởng bằng TRANG BỊ (core) + vàng ở luồng ShowRewardPick/ShowItemPick.
            if (!CampaignContext.pendingRelic) return;
            CampaignContext.pendingRelic = false;
            RunSaveStore.Save();
        }

        void ShowRelicPick()
        {
            var bag = new List<RelicData>();
            foreach (var r in campaign.relicPool) if (r != null) bag.Add(r);
            if (bag.Count == 0) { CampaignContext.pendingRelic = false; return; }

            var rng = new System.Random();
            var picks = new List<RelicData>();
            for (int i = 0; i < 3 && bag.Count > 0; i++)
            {
                int k = rng.Next(bag.Count);
                picks.Add(bag[k]); bag.RemoveAt(k);
            }

            var picker = MakeRect("RelicPicker", _overlay.GetComponent<RectTransform>(), Vector2.zero, Vector2.one);
            SetImage(picker, new Color(0f, 0f, 0f, 0.82f));
            var panel = MakeRect("P", picker, new Vector2(0.28f, 0.24f), new Vector2(0.72f, 0.82f));
            RoundImg(panel, panelColor);
            SpawnLabel(panel, "NHẶT CỔ VẬT", 22f, relicColor, TextAlignmentOptions.Center, true,
                new Vector2(0.05f, 0.87f), new Vector2(0.95f, 0.98f));

            float top = 0.82f, h = 0.225f, gap = 0.03f;
            for (int i = 0; i < picks.Count; i++)
            {
                var chosen = picks[i];
                float y1 = top - i * (h + gap);
                var brt = MakeRect("Rl_" + i, panel, new Vector2(0.06f, y1 - h), new Vector2(0.94f, y1));
                var bimg = RoundImg(brt, new Color(relicColor.r * 0.42f, relicColor.g * 0.42f, relicColor.b * 0.42f, 0.98f));
                var btn = brt.gameObject.AddComponent<Button>(); btn.targetGraphic = bimg;
                var cb = btn.colors; cb.normalColor = Color.white; cb.highlightedColor = new Color(0.8f, 1f, 1f); btn.colors = cb;

                if (chosen.icon != null)
                {
                    var ic = MakeRect("Icon", brt, new Vector2(0.02f, 0.16f), new Vector2(0.17f, 0.84f));
                    var iimg = ic.gameObject.AddComponent<Image>();
                    iimg.sprite = chosen.icon; iimg.preserveAspect = true; iimg.raycastTarget = false;
                }

                var effs = new List<string>();
                if (chosen.alliesAtk != 0) effs.Add($"+{chosen.alliesAtk} ATK");
                if (chosen.alliesHp != 0) effs.Add($"+{chosen.alliesHp} HP");
                if (chosen.bonusMana != 0) effs.Add($"+{chosen.bonusMana} mana");
                if (chosen.bonusNexus != 0) effs.Add($"+{chosen.bonusNexus} nexus");

                var cap = SpawnLabel(brt,
                    $"<b>{chosen.relicName}</b>\n<size=72%>{chosen.description}\n<color=#7FE3E5>{string.Join("   ", effs)}</color></size>",
                    15f, Color.white, TextAlignmentOptions.Left, false,
                    new Vector2(chosen.icon != null ? 0.19f : 0.05f, 0.06f), new Vector2(0.97f, 0.94f));
                cap.textWrappingMode = TextWrappingModes.Normal;

                btn.onClick.AddListener(() =>
                {
                    CampaignRun.AddRelic(chosen);
                    CampaignContext.pendingRelic = false;
                    RunSaveStore.Save(); // Mốc 6
                    Destroy(picker.gameObject);
                    Render();
                });
            }
        }

        // ── Micro helpers ─────────────────────────────────────────
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

        // Ảnh BO GÓC (dùng UISprites.RoundedRect) — panel/nút/node nhìn mềm hơn.
        static Image RoundImg(RectTransform rt, Color color)
        {
            var img = rt.gameObject.GetComponent<Image>() ?? rt.gameObject.AddComponent<Image>();
            img.sprite = Round;
            img.type = Image.Type.Sliced;
            img.color = color;
            return img;
        }

        // Nút bo góc + hiệu ứng hover.
        static Button MakeBtn(RectTransform rt, Color color, string label, float fontSize,
            UnityEngine.Events.UnityAction onClick)
        {
            var img = RoundImg(rt, color);
            var btn = rt.gameObject.AddComponent<Button>();
            btn.targetGraphic = img;
            var cb = btn.colors;
            cb.normalColor = Color.white;
            cb.highlightedColor = new Color(0.86f, 0.92f, 1f);
            cb.pressedColor = new Color(0.72f, 0.72f, 0.78f);
            cb.fadeDuration = 0.08f;
            btn.colors = cb;
            if (!string.IsNullOrEmpty(label))
                SpawnLabel(rt, label, fontSize, Color.white, TextAlignmentOptions.Center, true);
            if (onClick != null) btn.onClick.AddListener(onClick);
            return btn;
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
    }
}