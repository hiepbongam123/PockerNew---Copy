using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using TMPro;
using LoRClone.Data;

namespace LoRClone.View
{
    /// <summary>
    /// SẢNH — 2 màn: MÀN HÌNH CHÍNH (splash) và CHƠI NGAY (chọn bộ bài).
    /// Rail trái chuyển tab giữa 2 màn. Bấm 1 bộ bài → lớp mờ + panel chi tiết (đóng bằng ❌/nền).
    ///
    /// Wiring giữ nguyên: Chơi Ngay(PvP)→popup→PvpDeckSelection+NetworkLauncher.OpenAndFind;
    /// Đấu Với Máy(AI)→GameConfig+load scene; Con Đường Anh Hùng→RunMapView.Open;
    /// Chỉnh Sửa→DeckBuilderView.OpenForEdit (deck tự tạo).
    /// Ảnh champion/icon phe/logo là placeholder tự vẽ.
    /// </summary>
    [RequireComponent(typeof(LobbyManager))]
    public class LobbyMenuView : MonoBehaviour
    {
        [Header("Refs (tự lấy nếu để trống)")]
        public LobbyManager lobby;
        public DeckBuilderView deckBuilder;
        public DeckManagerView deckManager;

        [Header("Leo Tháp / Con Đường Anh Hùng")]
        public CampaignData campaign;
        public CampaignView campaignView;
        public RunMapView runMapView;

        [Header("Màn chọn bài cũ của LobbyManager — sẽ ẩn")]
        public GameObject[] matchSetupRoots;

        [Header("PvP (Photon)")]
        public LoRClone.Net.NetworkLauncher networkLauncher;

        [Header("Đấu Trường Anh Hùng (PvP-RPG)")]
        public ChampionArenaView championArena;

        [Header("Cửa hàng trang bị (Lõi)")]
        public ItemShopView itemShop;   // auto-add khi có campaign; nút CỬA HÀNG ở rail mở cái này

        // Nhãn tiền tệ ở sảnh (Lõi / Tinh Hồn / rương) — cập nhật realtime từ ProgressStore.
        TextMeshProUGUI _curCores, _curEssence, _curChests;

        [Header("Hồ sơ")]
        public string playerName = "Người chơi";

        [Header("Ảnh MÀN HÌNH CHÍNH (kéo Sprite HOẶC Texture vào — để trống = nền tạm)")]
        [Tooltip("Ảnh nền splash. Ưu tiên Sprite; nếu để trống thì dùng Texture.")]
        public Sprite homeBackgroundSprite;
        public Texture homeBackgroundTexture;
        [Tooltip("Ảnh banner 'Con Đường Anh Hùng' (tùy chọn).")]
        public Sprite promoSprite;
        public Texture promoTexture;

        [Header("Kích thước thẻ bài")]
        public Vector2 cardCellSize = new Vector2(172f, 232f);

        // ── PALETTE ──
        static readonly Color AMBER = C(0xe8, 0xa1, 0x3a);
        static readonly Color AMBERHI = C(0xf6, 0xc6, 0x67);
        static readonly Color AMBERDK = C(0x8a, 0x5a, 0x1e);
        static readonly Color GOLD = C(0xe8, 0xc4, 0x6f);
        static readonly Color GOLDHI = C(0xf6, 0xdd, 0x98);
        static readonly Color BLUE = C(0x3b, 0x7f, 0xd6);
        static readonly Color BLUEHI = C(0x5f, 0xa2, 0xef);
        static readonly Color BLUEDK = C(0x1f, 0x4d, 0x86);
        static readonly Color INK = C(0xf2, 0xea, 0xd9);
        static readonly Color DIM = C(0xb9, 0xa8, 0x88);
        static readonly Color FAINT = C(0x8a, 0x7c, 0x63);
        static readonly Color RAIL_A = C(0x2a, 0x1a, 0x12);
        static readonly Color RAIL_B = C(0x16, 0x0c, 0x09);
        static readonly Color BG_A = C(0x24, 0x17, 0x13);
        static readonly Color BG_B = C(0x0b, 0x07, 0x05);
        static readonly Color RED = C(0xc2, 0x3b, 0x2a);
        static Color C(int r, int g, int b) => new Color(r / 255f, g / 255f, b / 255f, 1f);

        // ── Runtime ──
        GameObject _menu, _home, _play;
        readonly List<GameObject> _setupRoots = new List<GameObject>();
        string _mode = "play";
        DeckData _picked; string _pickedEditName; string _selectedDeckName;
        RectTransform _gridContent;
        GameObject _detailLayer;        // lớp mờ + panel
        RectTransform _selCardHolder;
        TextMeshProUGUI _selName;
        Button _editBtn;
        GameObject _popup;
        readonly List<(Button btn, Image img, TextMeshProUGUI lbl, string id)> _railBtns = new List<(Button, Image, TextMeshProUGUI, string)>();
        readonly List<(Button btn, Image img, TextMeshProUGUI lbl, string mode)> _modeBtns = new List<(Button, Image, TextMeshProUGUI, string)>();
        readonly List<(Image ring, string deck)> _deckCells = new List<(Image, string)>();

        readonly Dictionary<long, Sprite> _vgrad = new Dictionary<long, Sprite>();
        readonly Dictionary<int, Texture2D> _artTex = new Dictionary<int, Texture2D>();
        Sprite _circleSp;

        // Vùng nội dung bên phải rail
        const float RAIL_W = 0.13f;

        // ══════════════════════════════════════════════════════════
        void Start()
        {
            if (!Application.isPlaying) return;
            if (lobby == null) lobby = GetComponent<LobbyManager>();

            if (deckBuilder == null)
                deckBuilder = GetComponent<DeckBuilderView>() ?? gameObject.AddComponent<DeckBuilderView>();
            if (deckBuilder.cardLibrary == null && lobby != null) deckBuilder.cardLibrary = lobby.cardLibrary;
            if ((deckBuilder.fallbackDecks == null || deckBuilder.fallbackDecks.Length == 0) && lobby != null)
                deckBuilder.fallbackDecks = lobby.availableDecks;
            deckBuilder.onClosed = PopulateGrid;

            if (deckManager == null)
                deckManager = GetComponent<DeckManagerView>() ?? gameObject.AddComponent<DeckManagerView>();
            if (deckManager.cardLibrary == null && lobby != null) deckManager.cardLibrary = lobby.cardLibrary;
            if ((deckManager.fallbackDecks == null || deckManager.fallbackDecks.Length == 0) && lobby != null)
                deckManager.fallbackDecks = lobby.availableDecks;
            if (deckManager.deckBuilder == null) deckManager.deckBuilder = deckBuilder;
            if (deckManager.cardPrefab == null) deckManager.cardPrefab = deckBuilder.cardPrefab;
            if (deckBuilder.cardCellSize != Vector2.zero) deckManager.cardCellSize = deckBuilder.cardCellSize;

            if (campaign != null)
            {
                if (campaignView == null)
                    campaignView = GetComponent<CampaignView>() ?? gameObject.AddComponent<CampaignView>();
                campaignView.lobby = lobby; campaignView.campaign = campaign;
                if (campaignView.cardPrefab == null) campaignView.cardPrefab = deckBuilder.cardPrefab;
                if (deckBuilder.cardCellSize != Vector2.zero) campaignView.cardCellSize = deckBuilder.cardCellSize;
                if (deckBuilder.campaign == null) deckBuilder.campaign = campaign;
                if (runMapView == null)
                    runMapView = GetComponent<RunMapView>() ?? gameObject.AddComponent<RunMapView>();
                runMapView.campaign = campaign; runMapView.campaignView = campaignView;
                if (runMapView.cardPrefab == null) runMapView.cardPrefab = deckBuilder.cardPrefab;
            }

            if (networkLauncher == null) networkLauncher = FindObjectOfType<LoRClone.Net.NetworkLauncher>();
            if (networkLauncher != null && lobby != null)
            {
                if (networkLauncher.cardLibrary == null) networkLauncher.cardLibrary = lobby.cardLibrary;
                if (networkLauncher.availableDecks == null || networkLauncher.availableDecks.Length == 0)
                    networkLauncher.availableDecks = lobby.availableDecks;
            }

            // ĐẤU TRƯỜNG ANH HÙNG — view riêng chọn champion + loadout.
            if (championArena == null)
                championArena = GetComponent<ChampionArenaView>() ?? gameObject.AddComponent<ChampionArenaView>();
            championArena.campaign = campaign;
            championArena.networkLauncher = networkLauncher;
            if (championArena.runMapView == null) championArena.runMapView = runMapView;   // nút SỬA DECK/TRANG BỊ mở hub PoC
            if (championArena.cardPrefab == null && deckBuilder != null) championArena.cardPrefab = deckBuilder.cardPrefab;

            // CỬA HÀNG TRANG BỊ (Lõi) — cần campaign để đọc CardItemLibrary.
            if (campaign != null)
            {
                if (itemShop == null) itemShop = GetComponent<ItemShopView>() ?? gameObject.AddComponent<ItemShopView>();
                itemShop.campaign = campaign;
            }

            if (lobby != null && lobby.cardLibrary != null) LoRClone.GameConfig.cardLibrary = lobby.cardLibrary;

            CollectSetupRoots();
            BuildMenu();
            ShowMenu(true);

            if (Data.CampaignContext.reopenCampaign)
            { Data.CampaignContext.reopenCampaign = false; OpenLeoThap(); }
        }

        void CollectSetupRoots()
        {
            _setupRoots.Clear();
            if (matchSetupRoots != null && matchSetupRoots.Length > 0)
            { foreach (var go in matchSetupRoots) if (go != null) _setupRoots.Add(go); return; }
            foreach (Transform c in lobby.transform)
                if (c.name == "PlayerPanel" || c.name == "EnemyPanel" || c.name == "StartButton")
                    _setupRoots.Add(c.gameObject);
        }

        void ShowMenu(bool on)
        {
            if (_menu != null) _menu.SetActive(on);
            foreach (var go in _setupRoots) if (go != null) go.SetActive(false);
            if (networkLauncher != null) networkLauncher.SetVisible(false);
        }

        // VỀ SAU TRẬN (reopenCampaign): tiếp tục NODE MAP của ải đang chơi — GIỮ nguyên vòng lặp PoC.
        void OpenLeoThap()
        {
            LoRClone.Data.GauntletEntry.Leave();   // ★ ISOLATION: PoC → xóa sạch state gauntlet
            if (runMapView != null) runMapView.Open(); else if (campaignView != null) campaignView.Open();
        }

        // TỪ SẢNH (bấm nút Con Đường Anh Hùng): LUÔN vào WORLD MAP (chọn khu vực) — KHÔNG auto nhảy vào ải dở.
        void OpenChampionRoad()
        {
            LoRClone.Data.GauntletEntry.Leave();
            if (runMapView != null) runMapView.OpenFromLobby(); else if (campaignView != null) campaignView.Open();
        }

        void OpenArena()
        { if (championArena != null) championArena.Open(); else Say("Thiếu ChampionArenaView."); }

        void QuitGame()
        {
#if UNITY_EDITOR
            UnityEditor.EditorApplication.isPlaying = false;
#else
            Application.Quit();
#endif
        }

        // ══════════════════════════════════════════════════════════
        void BuildMenu()
        {
            var parentRT = GetComponent<RectTransform>();
            var root = MakeRect("LobbyMenu", parentRT, Vector2.zero, Vector2.one);
            _menu = root.gameObject;
            var cv = _menu.AddComponent<Canvas>();
            cv.overrideSorting = true; cv.sortingOrder = 400;
            _menu.AddComponent<GraphicRaycaster>();

            var bg = MakeRect("BG", root, Vector2.zero, Vector2.one);
            SetVGrad(bg, BG_A, BG_B).raycastTarget = true;

            BuildHome(root);
            BuildPlay(root);
            BuildRail(root);       // rail vẽ trên cùng (luôn hiện)

            ShowScreen("home");
        }

        // ── RAIL (global, chuyển tab) ──
        void BuildRail(RectTransform root)
        {
            var rail = MakeRect("Rail", root, new Vector2(0f, 0f), new Vector2(RAIL_W, 1f));
            SetVGrad(rail, RAIL_A, RAIL_B);
            var edge = MakeRect("Edge", rail, new Vector2(0.975f, 0f), new Vector2(1f, 1f));
            SetVGrad(edge, AMBERHI, AMBERDK).raycastTarget = false;

            var logo = MakeRect("Logo", rail, new Vector2(0.24f, 0.905f), new Vector2(0.76f, 0.975f));
            SetRoundedGrad(logo, AMBERHI, AMBERDK, 12);
            SpawnLabel(logo.transform, "H", 28f, C(0x20, 0x16, 0x0a), TextAlignmentOptions.Center, true);

            _railBtns.Clear();
            float y = 0.875f;
            RailItem(rail, "home", "MÀN HÌNH\nCHÍNH", ref y);
            RailItem(rail, "play", "CHƠI NGAY", ref y);
            RailItem(rail, "coll", "BỘ SƯU TẬP", ref y);
            RailItem(rail, "rewd", "PHẦN THƯỞNG", ref y);
            RailItem(rail, "pass", "BATTLE PASS", ref y);
            // Cửa hàng đáy
            RailItemAt(rail, "shop", "CỬA HÀNG", 0.02f, 0.115f);
        }

        void RailItem(RectTransform rail, string id, string label, ref float y)
        {
            float h = 0.10f, bot = y - h;
            MakeRailBtn(rail, id, label, bot, y);
            y = bot - 0.008f;
        }
        void RailItemAt(RectTransform rail, string id, string label, float bot, float top)
            => MakeRailBtn(rail, id, label, bot, top);

        void MakeRailBtn(RectTransform rail, string id, string label, float bot, float top)
        {
            var rt = MakeRect("R_" + id, rail, new Vector2(0.06f, bot), new Vector2(0.94f, top));
            var img = SetRounded(rt, new Color(0, 0, 0, 0), 10);
            var btn = rt.gameObject.AddComponent<Button>(); btn.targetGraphic = img;
            var bc = btn.colors; bc.normalColor = Color.white; bc.highlightedColor = new Color(1, 1, 1, 1.5f);
            bc.pressedColor = new Color(0.85f, 0.85f, 0.85f, 1f); btn.colors = bc;
            var ic = MakeRect("Ic", rt, new Vector2(0.34f, 0.5f), new Vector2(0.66f, 0.86f));
            var icImg = SetRounded(ic, AMBER, 7); icImg.raycastTarget = false;
            var lbl = SpawnLabel(MakeRect("L", rt, new Vector2(0.02f, 0.04f), new Vector2(0.98f, 0.48f)),
                label, 10.5f, DIM, TextAlignmentOptions.Center, true);
            var _id = id;
            btn.onClick.AddListener(() => OnRail(_id));
            _railBtns.Add((btn, icImg, lbl, id));
        }

        void OnRail(string id)
        {
            RefreshCurrencies();   // mỗi lần bấm rail → cập nhật tiền (VD vừa mua ở shop xong quay ra)
            if (id == "home") ShowScreen("home");
            else if (id == "play") ShowScreen("play");
            else if (id == "coll")                       // BỘ SƯU TẬP = QUẢN LÝ DECK (xem/sửa/xóa + ＋ BỘ BÀI MỚI → Xưởng Deck)
            { if (deckManager != null) deckManager.Open(); else Say("Thiếu DeckManagerView."); }
            else if (id == "shop")                       // CỬA HÀNG = mua trang bị bằng Lõi
            { if (itemShop != null) itemShop.Open(); else Say("Thiếu ItemShopView (cần gán campaign)."); }
            else if (id == "rewd") GrantTestReward();     // PHẦN THƯỞNG = nhận test liên tục
            else Say(RailName(id) + " (chưa gắn).");
            HighlightRail(id == "play" || id == "home" ? id : _lastScreen);
        }
        string RailName(string id) => id == "coll" ? "Bộ sưu tập" : id == "rewd" ? "Phần thưởng" : id == "pass" ? "Battle Pass" : id == "shop" ? "Cửa hàng" : id;

        string _lastScreen = "home";
        void ShowScreen(string s)
        {
            _lastScreen = s;
            if (_home != null) _home.SetActive(s == "home");
            if (_play != null) _play.SetActive(s == "play");
            if (s != "play" && _detailLayer != null) _detailLayer.SetActive(false);
            HighlightRail(s);
        }
        void HighlightRail(string id)
        {
            foreach (var (btn, img, lbl, rid) in _railBtns)
            {
                bool on = rid == id;
                if (img != null) img.color = on ? GOLDHI : AMBER;
                if (lbl != null) lbl.color = on ? GOLDHI : DIM;
                var self = btn != null ? btn.targetGraphic as Image : null;
                if (self != null) self.color = on ? new Color(0.29f, 0.19f, 0.10f, 1f) : new Color(0, 0, 0, 0);
            }
        }

        // ── MÀN HÌNH CHÍNH (splash) ──
        void BuildHome(RectTransform root)
        {
            var home = MakeRect("Home", root, new Vector2(RAIL_W, 0f), new Vector2(1f, 1f));
            _home = home.gameObject;

            // Ảnh nền: ưu tiên Sprite gán ở Inspector → Texture → gradient tạm.
            var splash = MakeRect("Splash", home, Vector2.zero, Vector2.one);
            if (homeBackgroundSprite != null)
            {
                var im = splash.gameObject.AddComponent<Image>();
                im.sprite = homeBackgroundSprite; im.type = Image.Type.Simple; im.color = Color.white; im.preserveAspect = false;
            }
            else if (homeBackgroundTexture != null)
            {
                splash.gameObject.AddComponent<RawImage>().texture = homeBackgroundTexture;
            }
            else
            {
                var sImg = splash.gameObject.AddComponent<Image>();
                sImg.sprite = ArtSprite(200); sImg.type = Image.Type.Simple; sImg.color = new Color(1, 1, 1, 1);
            }
            var veil = MakeRect("Veil", home, Vector2.zero, Vector2.one);
            SetImage(veil, new Color(0.03f, 0.05f, 0.08f, 0.25f)).raycastTarget = false;

            // Top bar: hồ sơ + tiền tệ
            var av = MakeRect("Av", home, new Vector2(0.01f, 0.90f), new Vector2(0.045f, 0.985f));
            var avImg = av.gameObject.AddComponent<Image>(); avImg.sprite = CircleSprite(); avImg.type = Image.Type.Simple;
            avImg.color = C(0x2a, 0x30, 0x50);
            SpawnLabel(av.transform, string.IsNullOrEmpty(playerName) ? "?" : playerName.Substring(0, 1).ToUpper(), 18f, GOLD, TextAlignmentOptions.Center, true);
            // Tiền tệ THẬT của PoC (dùng chung sang PvP): Lõi Cường Hóa + Tinh Hồn + tổng rương.
            _curCores = CurRef(home, 0.06f, 0.93f, 0.145f);
            _curEssence = CurRef(home, 0.15f, 0.93f, 0.235f);
            _curChests = CurRef(home, 0.24f, 0.93f, 0.315f);
            RefreshCurrencies();

            // Promo banner (Con Đường Anh Hùng)
            var promo = MakeRect("Promo", home, new Vector2(0.70f, 0.30f), new Vector2(0.985f, 0.74f));
            SetRoundedGrad(promo, C(0x2a, 0x3a, 0x52), C(0x14, 0x1e, 0x30), 14);
            if (promoSprite != null || promoTexture != null)
            {
                var pi = MakeRect("PImg", promo, Vector2.zero, Vector2.one);
                pi.offsetMin = new Vector2(3, 3); pi.offsetMax = new Vector2(-3, -3);
                if (promoSprite != null)
                { var im = pi.gameObject.AddComponent<Image>(); im.sprite = promoSprite; im.type = Image.Type.Simple; im.raycastTarget = false; }
                else
                { pi.gameObject.AddComponent<RawImage>().raycastTarget = false; pi.gameObject.GetComponent<RawImage>().texture = promoTexture; }
                var pveil = MakeRect("PV", promo, new Vector2(0f, 0f), new Vector2(1f, 0.32f));
                var pv = pveil.gameObject.AddComponent<Image>();
                pv.sprite = VGrad(new Color(0, 0, 0, 0.8f), new Color(0, 0, 0, 0f)); pv.type = Image.Type.Simple; pv.raycastTarget = false;
            }
            SpawnLabel(MakeRect("PT", promo, new Vector2(0.06f, 0.06f), new Vector2(0.94f, 0.2f)),
                "THAM GIA NGAY CON ĐƯỜNG ANH HÙNG!", 14f, GOLDHI, TextAlignmentOptions.Center, true);
            var pbtn = promo.gameObject.AddComponent<Button>(); pbtn.targetGraphic = promo.gameObject.GetComponent<Image>();
            pbtn.onClick.AddListener(OpenChampionRoad);   // nút sảnh → WORLD MAP (không nhảy thẳng vào ải dở)

            // Nút đáy
            HomeBtn(home, "NHẬN THƯỞNG (TEST)", 0.16f, 0.44f, GrantTestReward);
            HomeBtn(home, "BÁU VẬT", 0.46f, 0.68f, () => Say("Báu vật (chưa gắn)."));
            HomeBtn(home, "KHO BÁU TUẦN", 0.70f, 0.98f, () => Say("Kho báu tuần (chưa gắn)."));

            // gợi ý giữa màn
            SpawnLabel(MakeRect("Hint", home, new Vector2(0.2f, 0.5f), new Vector2(0.66f, 0.6f)),
                "Bấm CHƠI NGAY ở thanh trái để chọn bộ bài ra trận.", 15f, new Color(1, 1, 1, 0.75f),
                TextAlignmentOptions.Center, false);
        }

        void Cur(RectTransform home, string txt, float x0, float y0, float x1)
        {
            var rt = MakeRect("Cur", home, new Vector2(x0, y0), new Vector2(x1, y0 + 0.05f));
            SetRounded(rt, new Color(0.03f, 0.05f, 0.08f, 0.7f), 20).raycastTarget = false;
            SpawnLabel(rt.transform, txt, 12f, GOLDHI, TextAlignmentOptions.Center, true);
        }

        // Chip tiền tệ TRẢ VỀ nhãn để cập nhật realtime (RefreshCurrencies).
        TextMeshProUGUI CurRef(RectTransform home, float x0, float y0, float x1)
        {
            var rt = MakeRect("Cur", home, new Vector2(x0, y0), new Vector2(x1, y0 + 0.05f));
            SetRounded(rt, new Color(0.03f, 0.05f, 0.08f, 0.7f), 20).raycastTarget = false;
            return SpawnLabel(rt.transform, "", 12f, GOLDHI, TextAlignmentOptions.Center, true);
        }

        /// <summary>Đọc ProgressStore → cập nhật chip tiền tệ ở sảnh. Gọi khi mở sảnh / sau khi nhận thưởng / mua.</summary>
        void RefreshCurrencies()
        {
            if (_curCores != null) _curCores.text = $"<color=#5FCFD2>⬡</color> {ProgressStore.Cores}";        // Lõi Cường Hóa
            if (_curEssence != null) _curEssence.text = $"<color=#C89BF5>❖</color> {ProgressStore.Essence}";   // Tinh Hồn
            if (_curChests != null)
            {
                int chests = ProgressStore.ChestCount(0) + ProgressStore.ChestCount(1)
                           + ProgressStore.ChestCount(2) + ProgressStore.ChestCount(3);
                _curChests.text = $"<color=#F0C24E>▣</color> {chests}";                                       // tổng rương
            }
        }

        /// <summary>THƯỞNG TEST — bấm là nhận ngay (không khoá theo ngày) để có tiền/rương thử trang bị nhanh.
        /// Dùng đúng hệ đã có: Lõi (mua trang bị ở shop) + Tinh Hồn + rương mỗi bậc (mở ở màn rương).</summary>
        void GrantTestReward()
        {
            ProgressStore.AddCores(500);
            ProgressStore.AddEssence(500);
            for (int t = 0; t <= 3; t++) ProgressStore.AddChest(t);   // +1 rương mỗi bậc (Thường→Huyền Thoại)
            RefreshCurrencies();
            Say("Đã nhận: +500 Lõi, +500 Tinh Hồn, +1 rương mỗi bậc. Bấm tiếp để nhận thêm.");
        }
        void HomeBtn(RectTransform home, string txt, float x0, float x1, UnityEngine.Events.UnityAction act)
        {
            var rt = MakeRect("HB", home, new Vector2(x0, 0.05f), new Vector2(x1, 0.13f));
            var img = SetRoundedGrad(rt, C(0x2a, 0x22, 0x18), C(0x14, 0x10, 0x0b), 11);
            var btn = rt.gameObject.AddComponent<Button>(); btn.targetGraphic = rt.gameObject.GetComponent<Image>();
            ApplyBtnColors(btn, Color.white, new Color(1, 1, 1, 0.9f));
            SpawnLabel(rt.transform, txt, 15f, GOLDHI, TextAlignmentOptions.Center, true);
            if (act != null) btn.onClick.AddListener(act);
        }

        // ── MÀN CHƠI NGAY (chọn bộ bài) ──
        void BuildPlay(RectTransform root)
        {
            var play = MakeRect("Play", root, Vector2.zero, Vector2.one);
            _play = play.gameObject;
            SetImage(MakeRect("PBG", play, new Vector2(RAIL_W, 0f), new Vector2(1f, 1f)), new Color(0, 0, 0, 0.001f)).raycastTarget = true;

            BuildModeColumn(play);
            BuildMainGrid(play);
            BuildDetail(play);
            BuildPopup(root);
        }

        void BuildModeColumn(RectTransform play)
        {
            var col = MakeRect("Modes", play, new Vector2(RAIL_W, 0f), new Vector2(0.30f, 1f));
            var line = MakeRect("Ln", col, new Vector2(0.985f, 0.05f), new Vector2(1f, 0.95f));
            SetImage(line, new Color(1, 1, 1, 0.06f)).raycastTarget = false;
            _modeBtns.Clear();
            float y = 0.9f;
            ModeBtn(col, "hero", "CON ĐƯỜNG\nANH HÙNG", ref y);
            ModeBtn(col, "play", "CHƠI NGAY", ref y);
            ModeBtn(col, "ai", "ĐẤU VỚI MÁY", ref y);
            ModeBtn(col, "gauntlet", "ĐẤU TRƯỜNG\nANH HÙNG", ref y);   // ★ PvP-RPG (bước 5-B)
            ModeBtn(col, "chal", "THỬ THÁCH", ref y);
            ModeBtn(col, "rank", "BẢNG XẾP HẠNG", ref y);
        }
        void ModeBtn(RectTransform col, string mode, string label, ref float y)
        {
            float h = 0.085f, bot = y - h;
            var rt = MakeRect("M_" + mode, col, new Vector2(0.12f, bot), new Vector2(0.92f, y));
            var img = SetRounded(rt, C(0x22, 0x16, 0x0f), 12);
            var btn = rt.gameObject.AddComponent<Button>(); btn.targetGraphic = img;
            ApplyBtnColors(btn, Color.white, new Color(1, 1, 1, 0.92f));
            var lbl = SpawnLabel(rt.transform, label, 15f, C(0xd9, 0xc3, 0x9c), TextAlignmentOptions.Center, true);
            var m = mode;
            btn.onClick.AddListener(() => OnMode(m));
            _modeBtns.Add((btn, img, lbl, mode));
            y = bot - 0.022f;
        }
        void OnMode(string mode)
        {
            if (mode == "hero") { OpenLeoThap(); return; }
            if (mode == "gauntlet") { OpenArena(); return; }   // ★ PvP-RPG: view riêng (ChampionArenaView)
            if (mode == "chal") { Say("Thử thách (chưa gắn)."); }
            if (mode == "rank") { Say("Bảng xếp hạng (chưa gắn)."); }
            SelectContext(mode);
        }
        void SelectContext(string mode)
        {
            _mode = (mode == "ai") ? "ai" : "play";
            foreach (var (btn, img, lbl, m) in _modeBtns)
            {
                bool on = m == mode;
                if (img != null) img.color = on ? BLUEDK : C(0x22, 0x16, 0x0f);
                if (lbl != null) lbl.color = on ? C(0xdf, 0xf0, 0xff) : C(0xd9, 0xc3, 0x9c);
            }
        }

        void BuildMainGrid(RectTransform play)
        {
            var main = MakeRect("Main", play, new Vector2(0.30f, 0f), new Vector2(1f, 1f));
            SpawnLabel(MakeRect("H", main, new Vector2(0.02f, 0.925f), new Vector2(0.6f, 0.985f)),
                "CHỌN BỘ BÀI CỦA BẠN", 24f, INK, TextAlignmentOptions.Left, true);
            MakeSeg(main, "TIÊU CHUẨN", false, new Vector2(0.62f, 0.94f), new Vector2(0.78f, 0.982f));
            MakeSeg(main, "VÔ HẠN", true, new Vector2(0.79f, 0.94f), new Vector2(0.94f, 0.982f));
            var rule = MakeRect("Rule", main, new Vector2(0.02f, 0.912f), new Vector2(0.98f, 0.918f));
            SetVGrad(rule, AMBER, new Color(AMBER.r, AMBER.g, AMBER.b, 0f)).raycastTarget = false;
            _gridContent = BuildGridScroll("Grid", main, new Vector2(0.02f, 0.02f), new Vector2(0.98f, 0.90f));
            PopulateGrid();
        }
        void MakeSeg(RectTransform parent, string text, bool on, Vector2 min, Vector2 max)
        {
            var rt = MakeRect("Seg", parent, min, max);
            var dot = MakeRect("Dot", rt, new Vector2(0f, 0.28f), new Vector2(0.15f, 0.72f));
            var di = dot.gameObject.AddComponent<Image>(); di.sprite = CircleSprite(); di.type = Image.Type.Simple;
            di.color = on ? GOLD : C(0x42, 0x36, 0x24); di.raycastTarget = false;
            SpawnLabel(MakeRect("T", rt, new Vector2(0.17f, 0f), new Vector2(1f, 1f)),
                text, 12f, on ? INK : FAINT, TextAlignmentOptions.Left, true);
        }

        RectTransform BuildGridScroll(string name, RectTransform parent, Vector2 amin, Vector2 amax)
        {
            var scrollRT = MakeRect(name, parent, amin, amax);
            var sr = scrollRT.gameObject.AddComponent<ScrollRect>();
            sr.horizontal = false; sr.movementType = ScrollRect.MovementType.Clamped; sr.scrollSensitivity = 32f;
            var vp = MakeRect("Viewport", scrollRT, Vector2.zero, Vector2.one);
            vp.gameObject.AddComponent<Image>().color = Color.white;
            vp.gameObject.AddComponent<Mask>().showMaskGraphic = false;
            var content = MakeRect("Content", vp, new Vector2(0f, 1f), new Vector2(1f, 1f));
            content.pivot = new Vector2(0.5f, 1f);
            content.offsetMin = new Vector2(0f, -300f); content.offsetMax = Vector2.zero;
            var glg = content.gameObject.AddComponent<GridLayoutGroup>();
            glg.cellSize = cardCellSize; glg.spacing = new Vector2(22f, 28f);
            glg.padding = new RectOffset(16, 16, 14, 22); glg.childAlignment = TextAnchor.UpperLeft;
            content.gameObject.AddComponent<ContentSizeFitter>().verticalFit = ContentSizeFitter.FitMode.PreferredSize;
            sr.viewport = vp; sr.content = content;
            return content;
        }

        // ── LƯỚI THẺ ──
        void PopulateGrid()
        {
            if (_gridContent == null) return;
            for (int i = _gridContent.childCount - 1; i >= 0; i--) Destroy(_gridContent.GetChild(i).gameObject);
            _deckCells.Clear();
            var decks = new List<DeckData>();
            if (lobby != null && lobby.availableDecks != null)
                foreach (var d in lobby.availableDecks) if (d != null) decks.Add(d);
            decks.AddRange(CustomDeckStore.LoadAllAsDecks(lobby != null ? lobby.cardLibrary : null,
                                                          lobby != null ? lobby.availableDecks : null));
            if (decks.Count == 0)
            {
                SpawnLabel(MakeRect("Empty", _gridContent, Vector2.zero, Vector2.one),
                    "Chưa có bộ bài.\nTạo ở XƯỞNG DECK.", 15f, C(0xb0, 0x8a, 0x8a), TextAlignmentOptions.Center, false);
                return;
            }
            foreach (var deck in decks)
            {
                bool isCustom = deck.deckName != null && deck.deckName.EndsWith(" *");
                string editName = isCustom ? deck.deckName.Substring(0, deck.deckName.Length - 2) : null;
                SpawnDeckCard(deck, editName);
            }
            LayoutRebuilder.ForceRebuildLayoutImmediate(_gridContent);
        }

        void SpawnDeckCard(DeckData deck, string editName)
        {
            var cell = MakeRect("Deck_" + deck.deckName, _gridContent, Vector2.zero, Vector2.one);
            var card = MakeRect("Card", cell, new Vector2(0f, 0.15f), new Vector2(1f, 1f));
            var ringRT = MakeRect("Ring", card, new Vector2(-0.02f, -0.015f), new Vector2(1.02f, 1.015f));
            var ring = SetRounded(ringRT, GOLDHI, 14); ring.raycastTarget = false; ringRT.gameObject.SetActive(false);
            BuildCardFace(card, deck);
            SpawnLabel(MakeRect("N", cell, new Vector2(0f, 0f), new Vector2(1f, 0.14f)),
                deck.deckName, 13f, C(0xe6, 0xd8, 0xbd), TextAlignmentOptions.Center, false);
            var input = MakeRect("Input", card, Vector2.zero, Vector2.one);
            var iImg = SetImage(input, new Color(1, 1, 1, 0f));
            var btn = input.gameObject.AddComponent<Button>(); btn.targetGraphic = iImg;
            var hc = btn.colors; hc.normalColor = new Color(1, 1, 1, 0f);
            hc.highlightedColor = new Color(1, 1, 1, 0.08f); hc.pressedColor = new Color(1, 1, 1, 0.16f); btn.colors = hc;
            var dk = deck; var en = editName;
            btn.onClick.AddListener(() => SelectDeck(dk, en));
            _deckCells.Add((ring, deck.deckName));
        }

        void BuildCardFace(RectTransform card, DeckData deck)
        {
            SetRoundedGrad(card, GOLDHI, C(0x7a, 0x53, 0x1f), 12);
            var artHolder = MakeRect("ArtHolder", card, Vector2.zero, Vector2.one);
            artHolder.offsetMin = new Vector2(5f, 5f); artHolder.offsetMax = new Vector2(-5f, -5f);
            SetRounded(artHolder, Color.white, 8).raycastTarget = false;
            artHolder.gameObject.AddComponent<Mask>().showMaskGraphic = false;
            var champ = GetChampion(deck);
            var art = MakeRect("Art", artHolder, Vector2.zero, Vector2.one);
            if (champ != null && champ.artwork != null)
            {
                var raw = art.gameObject.AddComponent<RawImage>(); raw.texture = champ.artwork; raw.raycastTarget = false;
                ApplyFocalUV(raw, champ, cardCellSize.x, cardCellSize.y);
            }
            else
            {
                var img = art.gameObject.AddComponent<Image>();
                img.sprite = ArtSprite(HueOf(deck.deckName)); img.type = Image.Type.Simple; img.raycastTarget = false;
            }
            var scrim = MakeRect("Scrim", artHolder, new Vector2(0f, 0f), new Vector2(1f, 0.5f));
            var sImg = scrim.gameObject.AddComponent<Image>();
            sImg.sprite = VGrad(new Color(0, 0, 0, 0.75f), new Color(0, 0, 0, 0f)); sImg.type = Image.Type.Simple; sImg.raycastTarget = false;
            var ban = MakeRect("Banner", card, new Vector2(0.08f, 0.85f), new Vector2(0.28f, 1.03f));
            SetImage(ban, RED).raycastTarget = false;
            SpawnLabel(ban.transform, "★", 13f, GOLDHI, TextAlignmentOptions.Center, true);
            var facWrap = MakeRect("Fac", card, new Vector2(0.32f, -0.03f), new Vector2(0.68f, 0.15f));
            FacCircle(MakeRect("f1", facWrap, new Vector2(0f, 0f), new Vector2(0.5f, 1f)), HueOf(deck.deckName));
            FacCircle(MakeRect("f2", facWrap, new Vector2(0.5f, 0f), new Vector2(1f, 1f)), HueOf(deck.deckName + "x"));
            if (champ != null)
            {
                var cost = MakeRect("Cost", card, new Vector2(0.72f, 0.85f), new Vector2(0.94f, 1.0f));
                SetRoundedGrad(cost, C(0x2b, 0x5c, 0x9e), C(0x17, 0x3a, 0x6b), 7).raycastTarget = false;
                SpawnLabel(cost.transform, champ.manaCost.ToString(), 12f, Color.white, TextAlignmentOptions.Center, true);
            }
        }
        void FacCircle(RectTransform rt, int hue)
        {
            var ring = rt.gameObject.AddComponent<Image>(); ring.sprite = CircleSprite(); ring.type = Image.Type.Simple;
            ring.color = new Color(0.85f, 0.71f, 0.42f, 1f); ring.raycastTarget = false;
            var inner = MakeRect("in", rt, Vector2.zero, Vector2.one);
            inner.offsetMin = new Vector2(2, 2); inner.offsetMax = new Vector2(-2, -2);
            var img = inner.gameObject.AddComponent<Image>(); img.sprite = CircleSprite(); img.type = Image.Type.Simple;
            img.color = Color.HSVToRGB(hue / 360f, 0.55f, 0.6f); img.raycastTarget = false;
        }

        void SelectDeck(DeckData deck, string editName)
        {
            if (deck == null) return;
            _picked = deck; _pickedEditName = editName; _selectedDeckName = deck.deckName;
            foreach (var (ring, name) in _deckCells) if (ring != null) ring.gameObject.SetActive(name == _selectedDeckName);
            if (_selName != null) _selName.text = deck.deckName;
            if (_selCardHolder != null)
            {
                for (int i = _selCardHolder.childCount - 1; i >= 0; i--) Destroy(_selCardHolder.GetChild(i).gameObject);
                var img = _selCardHolder.GetComponent<Image>(); if (img) img.color = new Color(0, 0, 0, 0);
                BuildCardFace(_selCardHolder, deck);
            }
            if (_editBtn != null) _editBtn.interactable = editName != null;
            if (_detailLayer != null) _detailLayer.SetActive(true);
        }

        // ── LỚP MỜ + PANEL CHI TIẾT ──
        void BuildDetail(RectTransform play)
        {
            var layer = MakeRect("DetailLayer", play, Vector2.zero, Vector2.one);
            _detailLayer = layer.gameObject;
            // nền mờ (bấm để đóng) — phủ modes+grid, chừa rail
            var dim = MakeRect("Dim", layer, new Vector2(RAIL_W, 0f), new Vector2(1f, 1f));
            SetImage(dim, new Color(0.02f, 0.03f, 0.05f, 0.62f));
            var dimBtn = dim.gameObject.AddComponent<Button>(); dimBtn.transition = Selectable.Transition.None;
            dimBtn.onClick.AddListener(CloseDetail);

            // Cosmetics strip
            var cos = MakeRect("Cos", layer, new Vector2(0.70f, 0.06f), new Vector2(0.755f, 0.94f));
            SetImage(cos, C(0x0c, 0x09, 0x06));
            string[] cg = { "◈", "▣", "❈", "✦", "☺", "⚑", "⚙" };
            for (int i = 0; i < cg.Length; i++)
            {
                float top = 0.97f - i * 0.13f;
                var b = MakeRect("c" + i, cos, new Vector2(0.12f, top - 0.11f), new Vector2(0.88f, top));
                SetRounded(b, i == 0 ? new Color(0.07f, 0.19f, 0.32f, 1f) : C(0x1c, 0x14, 0x10), 9);
                SpawnLabel(b.transform, cg[i], 16f, i == 0 ? GOLDHI : C(0xcd, 0xb9, 0x8f), TextAlignmentOptions.Center, false);
            }

            // Panel
            var det = MakeRect("Panel", layer, new Vector2(0.755f, 0f), new Vector2(1f, 1f));
            SetVGrad(det, C(0x16, 0x40, 0x6f), C(0x0a, 0x1a, 0x30));
            var ln = MakeRect("Ln", det, new Vector2(0f, 0f), new Vector2(0.008f, 1f));
            SetImage(ln, BLUEHI).raycastTarget = false;

            var close = MakeRect("X", det, new Vector2(0.86f, 0.9f), new Vector2(0.97f, 0.98f));
            var xImg = SetRounded(close, new Color(0.1f, 0.2f, 0.34f, 0.9f), 10);
            var xBtn = close.gameObject.AddComponent<Button>(); xBtn.targetGraphic = xImg;
            SpawnLabel(close.transform, "✕", 16f, INK, TextAlignmentOptions.Center, true);
            xBtn.onClick.AddListener(CloseDetail);

            var modehdr = MakeRect("ModeHdr", det, new Vector2(0.1f, 0.87f), new Vector2(0.82f, 0.95f));
            SetRoundedGrad(modehdr, BLUE, BLUEDK, 10);
            SpawnLabel(modehdr.transform, "XẾP HẠNG | VÔ HẠN", 14f, C(0xdf, 0xf0, 0xff), TextAlignmentOptions.Center, true);

            var rank = MakeRect("Rank", det, new Vector2(0.38f, 0.72f), new Vector2(0.62f, 0.85f));
            var rimg = rank.gameObject.AddComponent<Image>(); rimg.sprite = CircleSprite(); rimg.type = Image.Type.Simple;
            rimg.color = C(0x9d, 0xb8, 0xd6); rimg.raycastTarget = false;
            SpawnLabel(rank.transform, "III", 22f, C(0x12, 0x31, 0x4f), TextAlignmentOptions.Center, true);
            SpawnLabel(MakeRect("Prog", det, new Vector2(0.1f, 0.66f), new Vector2(0.9f, 0.71f)),
                "ĐNG: <b><color=#f6dd98>0</color></b> / 100", 13f, C(0xcf, 0xe0, 0xf2), TextAlignmentOptions.Center, false);

            // thẻ giữa (tỉ lệ cố định)
            _selCardHolder = MakeRect("SelCard", det, new Vector2(0.5f, 0.40f), new Vector2(0.5f, 0.40f));
            _selCardHolder.sizeDelta = new Vector2(150f, 202f);

            // 2 nút cạnh thẻ
            var fav = MakeRect("Fav", det, new Vector2(0.06f, 0.34f), new Vector2(0.24f, 0.48f));
            var favImg = SetRounded(fav, C(0x12, 0x31, 0x52), 10);
            var favBtn = fav.gameObject.AddComponent<Button>(); favBtn.targetGraphic = favImg;
            SpawnLabel(fav.transform, "★", 20f, GOLDHI, TextAlignmentOptions.Center, true);
            favBtn.onClick.AddListener(() => Say("Đã đánh dấu yêu thích."));
            SpawnLabel(MakeRect("FavL", det, new Vector2(0.02f, 0.29f), new Vector2(0.28f, 0.34f)),
                "YÊU THÍCH", 9.5f, C(0xcf, 0xe0, 0xf2), TextAlignmentOptions.Center, true);

            var edit = MakeRect("Edit", det, new Vector2(0.76f, 0.34f), new Vector2(0.94f, 0.48f));
            var editImg = SetRounded(edit, C(0x12, 0x31, 0x52), 10);
            _editBtn = edit.gameObject.AddComponent<Button>(); _editBtn.targetGraphic = editImg;
            var eb = _editBtn.colors; eb.normalColor = Color.white; eb.disabledColor = new Color(0.4f, 0.4f, 0.4f, 0.4f); _editBtn.colors = eb;
            SpawnLabel(edit.transform, "✎", 20f, GOLDHI, TextAlignmentOptions.Center, true);
            SpawnLabel(MakeRect("EditL", det, new Vector2(0.72f, 0.29f), new Vector2(0.98f, 0.34f)),
                "CHỈNH SỬA", 9.5f, C(0xcf, 0xe0, 0xf2), TextAlignmentOptions.Center, true);
            _editBtn.onClick.AddListener(OnEdit); _editBtn.interactable = false;

            _selName = SpawnLabel(MakeRect("SelName", det, new Vector2(0.05f, 0.23f), new Vector2(0.95f, 0.29f)),
                "", 16f, GOLDHI, TextAlignmentOptions.Center, true);

            var cta = MakeRect("Cta", det, new Vector2(0.08f, 0.05f), new Vector2(0.92f, 0.16f));
            SetRoundedGrad(cta, GOLDHI, C(0xd9, 0xa8, 0x3e), 12);
            var ctaBtn = cta.gameObject.AddComponent<Button>(); ctaBtn.targetGraphic = cta.gameObject.GetComponent<Image>();
            SpawnLabel(cta.transform, "CHƠI NGAY", 19f, C(0x3a, 0x27, 0x08), TextAlignmentOptions.Center, true);
            ctaBtn.onClick.AddListener(OnDetailPlay);

            _detailLayer.SetActive(false);
        }

        void CloseDetail()
        {
            if (_detailLayer != null) _detailLayer.SetActive(false);
            _selectedDeckName = null;
            foreach (var (ring, name) in _deckCells) if (ring != null) ring.gameObject.SetActive(false);
        }

        void OnEdit()
        {
            if (_pickedEditName == null) return;
            var dto = new CustomDeckStore.DeckDTO { name = _pickedEditName, cardNames = new List<string>() };
            if (_picked != null && _picked.cards != null)
                foreach (var c in _picked.cards) if (c != null) dto.cardNames.Add(c.cardName);
            deckBuilder.OpenForEdit(dto);
        }
        void OnDetailPlay()
        {
            if (_picked == null) { Say("Hãy chọn 1 bộ bài."); return; }
            if (_mode == "ai") StartAI();
            else if (_popup != null) _popup.SetActive(true);
        }

        // ── POPUP CHẾ ĐỘ ──
        void BuildPopup(RectTransform root)
        {
            var scrim = MakeRect("Popup", root, Vector2.zero, Vector2.one);
            _popup = scrim.gameObject;
            SetImage(scrim, new Color(0.02f, 0.03f, 0.03f, 0.8f));
            var modal = MakeRect("Modal", scrim, new Vector2(0.30f, 0.28f), new Vector2(0.70f, 0.72f));
            SetRoundedGrad(modal, C(0x1b, 0x4a, 0x7e), C(0x0e, 0x2b, 0x4d), 16);
            var tabs = MakeRect("Tabs", modal, new Vector2(0f, 0f), new Vector2(0.2f, 1f));
            SetImage(tabs, C(0x0c, 0x22, 0x3f));
            PopupTab(tabs, "TIÊU\nCHUẨN", false, 0.62f, 0.86f);
            PopupTab(tabs, "VÔ HẠN", true, 0.36f, 0.60f);
            SpawnLabel(MakeRect("Ti", modal, new Vector2(0.24f, 0.74f), new Vector2(0.95f, 0.9f)),
                "VÔ HẠN", 28f, GOLDHI, TextAlignmentOptions.Left, true);
            SpawnLabel(MakeRect("De", modal, new Vector2(0.24f, 0.55f), new Vector2(0.95f, 0.74f)),
                "Xuất trận với những lá bài vô hạn từ tất cả huyền thoại.", 13f, C(0xcf, 0xe0, 0xf2), TextAlignmentOptions.TopLeft, false);
            SpawnLabel(MakeRect("Ol", modal, new Vector2(0.24f, 0.46f), new Vector2(0.95f, 0.53f)),
                "CHỌN ĐỐI THỦ", 11f, C(0x8f, 0xb0, 0xd4), TextAlignmentOptions.Left, true);
            PopupOption(modal, "XẾP HẠNG", "Đấu với người chơi khác và leo hạng.", new Vector2(0.24f, 0.1f), new Vector2(0.58f, 0.44f));
            PopupOption(modal, "THƯỜNG", "Thách đấu bạn bè hoặc người chơi khác.", new Vector2(0.60f, 0.1f), new Vector2(0.94f, 0.44f));
            var cl = MakeRect("Close", modal, new Vector2(0.92f, 0.88f), new Vector2(1.02f, 1.02f));
            SetRoundedGrad(cl, C(0xf0, 0xb7, 0x56), C(0xb0, 0x6a, 0x2a), 20);
            var clBtn = cl.gameObject.AddComponent<Button>(); clBtn.targetGraphic = cl.gameObject.GetComponent<Image>();
            SpawnLabel(cl.transform, "×", 22f, C(0x3a, 0x27, 0x08), TextAlignmentOptions.Center, true);
            clBtn.onClick.AddListener(() => _popup.SetActive(false));
            var closeBg = scrim.gameObject.AddComponent<Button>(); closeBg.transition = Selectable.Transition.None;
            closeBg.onClick.AddListener(() => _popup.SetActive(false));
            _popup.SetActive(false);
        }
        void PopupTab(RectTransform tabs, string label, bool on, float bot, float top)
        {
            var rt = MakeRect("Tab", tabs, new Vector2(0f, bot), new Vector2(1f, top));
            if (on) SetImage(rt, new Color(0.10f, 0.29f, 0.49f, 0.9f));
            SpawnLabel(rt.transform, label, 11f, on ? GOLDHI : C(0x8f, 0xb0, 0xd4), TextAlignmentOptions.Center, true);
        }
        void PopupOption(RectTransform modal, string title, string desc, Vector2 min, Vector2 max)
        {
            var rt = MakeRect("Op_" + title, modal, min, max);
            var img = SetRounded(rt, C(0x0f, 0x2a, 0x48), 12);
            var btn = rt.gameObject.AddComponent<Button>(); btn.targetGraphic = img;
            ApplyBtnColors(btn, C(0x0f, 0x2a, 0x48), C(0x16, 0x3a, 0x60));
            SpawnLabel(MakeRect("T", rt, new Vector2(0.1f, 0.6f), new Vector2(0.95f, 0.9f)), title, 18f, INK, TextAlignmentOptions.Left, true);
            var u = MakeRect("U", rt, new Vector2(0.1f, 0.54f), new Vector2(0.45f, 0.58f));
            SetVGrad(u, GOLDHI, new Color(GOLDHI.r, GOLDHI.g, GOLDHI.b, 0f)).raycastTarget = false;
            SpawnLabel(MakeRect("D", rt, new Vector2(0.1f, 0.12f), new Vector2(0.92f, 0.5f)), desc, 11.5f, C(0xa9, 0xc0, 0xd8), TextAlignmentOptions.TopLeft, false);
            btn.onClick.AddListener(StartPvP);
        }

        // ── BẮT ĐẦU TRẬN ──
        void StartPvP()
        {
            if (_picked == null || _picked.cards == null) return;
            LoRClone.Data.GauntletEntry.Leave();   // ★ xóa loadout cũ → PvP thường KHÔNG áp buff
            var names = new List<string>();
            foreach (var c in _picked.cards) if (c != null) names.Add(c.cardName);
            PvpDeckSelection.Set(_picked.deckName, names);
            if (_popup != null) _popup.SetActive(false);
            if (_detailLayer != null) _detailLayer.SetActive(false);
            if (networkLauncher != null) networkLauncher.OpenAndFind();
            else Say("Thiếu NetworkLauncher.");
        }
        void StartAI()
        {
            if (_picked == null) return;
            LoRClone.Data.GauntletEntry.Leave();   // ★ ISOLATION: xóa state gauntlet → Đấu Với Máy KHÔNG dính buff/item PvP-RPG
            LoRClone.GameConfig.playerDeck = _picked;
            LoRClone.GameConfig.enemyDeck = RandomEnemyDeck();
            ScreenFade.LoadScene(lobby.gameSceneName);
        }
        DeckData RandomEnemyDeck()
        {
            if (lobby != null && lobby.availableDecks != null)
            {
                var pool = new List<DeckData>();
                foreach (var d in lobby.availableDecks) if (d != null && d.cards != null && d.cards.Count > 0) pool.Add(d);
                if (pool.Count > 0) return pool[Random.Range(0, pool.Count)];
            }
            return _picked;
        }

        // ── helpers dữ liệu ──
        static CardData GetChampion(DeckData deck)
        {
            if (deck == null || deck.cards == null) return null;
            CardData bestUnit = null, bestAny = null;
            foreach (var cd in deck.cards)
            {
                if (cd == null) continue;
                if (bestAny == null || cd.manaCost > bestAny.manaCost) bestAny = cd;
                if (cd.cardType == CardType.Unit && (bestUnit == null || cd.manaCost > bestUnit.manaCost)) bestUnit = cd;
            }
            return bestUnit ?? bestAny;
        }
        static int HueOf(string s)
        {
            if (string.IsNullOrEmpty(s)) return 210;
            int h = 0; foreach (var c in s) h = (h * 31 + c) & 0x7fffffff; return h % 360;
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

        // ── gradient / texture ──
        Sprite VGrad(Color top, Color bot)
        {
            long key = ((long)Col32(top) << 32) ^ (uint)Col32(bot);
            if (_vgrad.TryGetValue(key, out var sp)) return sp;
            var t = new Texture2D(4, 64, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp };
            for (int y = 0; y < 64; y++) { var c = Color.Lerp(bot, top, y / 63f); for (int x = 0; x < 4; x++) t.SetPixel(x, y, c); }
            t.Apply();
            sp = Sprite.Create(t, new Rect(0, 0, 4, 64), new Vector2(0.5f, 0.5f), 100f, 0, SpriteMeshType.FullRect);
            _vgrad[key] = sp; return sp;
        }
        static int Col32(Color c) => ((int)(c.r * 255) << 24) | ((int)(c.g * 255) << 16) | ((int)(c.b * 255) << 8) | (int)(c.a * 255);
        Image SetVGrad(RectTransform rt, Color top, Color bot)
        {
            var img = rt.gameObject.GetComponent<Image>() ?? rt.gameObject.AddComponent<Image>();
            img.sprite = VGrad(top, bot); img.type = Image.Type.Simple; img.color = Color.white; return img;
        }
        Image SetRoundedGrad(RectTransform rt, Color top, Color bot, int radius)
        {
            var baseImg = SetRounded(rt, bot, radius);
            var grad = MakeRect("Grad", rt, Vector2.zero, Vector2.one);
            int pad = Mathf.Max(2, radius / 3);
            grad.offsetMin = new Vector2(pad, pad); grad.offsetMax = new Vector2(-pad, -pad);
            var s = grad.gameObject.AddComponent<Image>();
            s.sprite = VGrad(top, bot); s.type = Image.Type.Simple; s.raycastTarget = false;
            return baseImg;
        }
        Sprite CircleSprite()
        {
            if (_circleSp != null) return _circleSp;
            int n = 64; var t = new Texture2D(n, n, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp };
            var ctr = new Vector2(n / 2f, n / 2f);
            for (int y = 0; y < n; y++) for (int x = 0; x < n; x++)
                { float d = Vector2.Distance(new Vector2(x, y), ctr) / (n / 2f); t.SetPixel(x, y, new Color(1, 1, 1, d <= 1f ? 1f : 0f)); }
            t.Apply();
            _circleSp = Sprite.Create(t, new Rect(0, 0, n, n), new Vector2(0.5f, 0.5f), 100f, 0, SpriteMeshType.FullRect);
            return _circleSp;
        }
        Sprite ArtSprite(int hue)
        {
            if (_artTex.TryGetValue(hue, out var tex))
                return Sprite.Create(tex, new Rect(0, 0, tex.width, tex.height), new Vector2(0.5f, 0.5f), 100f, 0, SpriteMeshType.FullRect);
            int w = 64, h = 88; var t = new Texture2D(w, h, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp };
            float hf = hue / 360f;
            var top = Color.HSVToRGB(hf, 0.55f, 0.42f); var bot = Color.HSVToRGB(hf, 0.5f, 0.12f); var glow = Color.HSVToRGB(hf, 0.45f, 0.55f);
            var focal = new Vector2(0.5f * w, 0.85f * h);
            for (int y = 0; y < h; y++) for (int x = 0; x < w; x++)
                {
                    float vy = y / (float)(h - 1); var bc = Color.Lerp(bot, top, vy);
                    float d = Vector2.Distance(new Vector2(x, y), focal) / (w * 0.7f); float g = Mathf.Clamp01(1f - d); g *= g * 0.6f;
                    t.SetPixel(x, y, Color.Lerp(bc, glow, g));
                }
            t.Apply(); _artTex[hue] = t;
            return Sprite.Create(t, new Rect(0, 0, w, h), new Vector2(0.5f, 0.5f), 100f, 0, SpriteMeshType.FullRect);
        }

        // ── micro helpers ──
        static Image SetRounded(RectTransform rt, Color color, int radius)
        {
            var img = rt.gameObject.GetComponent<Image>() ?? rt.gameObject.AddComponent<Image>();
            img.sprite = UITheme.RoundedSprite(radius); img.type = Image.Type.Sliced; img.color = color; return img;
        }
        static void ApplyBtnColors(Button btn, Color normal, Color hover)
        {
            var bc = btn.colors; bc.normalColor = normal; bc.highlightedColor = hover;
            bc.pressedColor = Color.Lerp(normal, Color.black, 0.22f); bc.disabledColor = normal; btn.colors = bc;
        }
        static RectTransform MakeRect(string name, Transform parent, Vector2 amin, Vector2 amax)
        {
            var go = new GameObject(name); go.transform.SetParent(parent, false);
            var rt = go.AddComponent<RectTransform>();
            rt.anchorMin = amin; rt.anchorMax = amax; rt.offsetMin = rt.offsetMax = Vector2.zero; return rt;
        }
        static Image SetImage(RectTransform rt, Color color)
        {
            var img = rt.gameObject.GetComponent<Image>() ?? rt.gameObject.AddComponent<Image>(); img.color = color; return img;
        }
        static TextMeshProUGUI SpawnLabel(Transform parent, string text, float size, Color color,
            TextAlignmentOptions align, bool bold, Vector2? amin = null, Vector2? amax = null)
        {
            var go = new GameObject("Label"); go.transform.SetParent(parent, false);
            var rt = go.AddComponent<RectTransform>();
            rt.anchorMin = amin ?? Vector2.zero; rt.anchorMax = amax ?? Vector2.one;
            rt.offsetMin = new Vector2(3, 2); rt.offsetMax = new Vector2(-3, -2);
            var tmp = go.AddComponent<TextMeshProUGUI>();
            tmp.text = text; tmp.fontSize = size; tmp.color = color; tmp.alignment = align;
            tmp.fontStyle = bold ? FontStyles.Bold : FontStyles.Normal;
            tmp.raycastTarget = false; tmp.richText = true; tmp.textWrappingMode = TextWrappingModes.Normal;
            return tmp;
        }

        // Toast
        GameObject _toast; TextMeshProUGUI _toastLbl; float _toastT;
        void Say(string msg)
        {
            if (_toast == null)
            {
                var rt = MakeRect("Toast", _menu.transform, new Vector2(0.5f, 0.05f), new Vector2(0.5f, 0.05f));
                rt.sizeDelta = new Vector2(460, 48); rt.anchoredPosition = new Vector2(0, 30);
                SetRounded(rt, C(0x16, 0x0d, 0x0b), 10);
                _toastLbl = SpawnLabel(rt.transform, "", 15f, INK, TextAlignmentOptions.Center, false);
                _toast = rt.gameObject;
            }
            _toast.transform.SetAsLastSibling();
            _toastLbl.text = msg; _toast.SetActive(true); _toastT = 2.4f;
        }
        void Update()
        {
            if (_toast != null && _toast.activeSelf)
            { _toastT -= Time.unscaledDeltaTime; if (_toastT <= 0f) _toast.SetActive(false); }
        }
    }
}