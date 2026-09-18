using System.Collections.Generic;
using Photon.Pun;
using Photon.Realtime;
using UnityEngine;
using UnityEngine.UI;
using TMPro;
using LoRClone.View; // dùng UISprites
using LoRClone.Data; // CardLibrary, CustomDeckStore, PvpDeckSelection, DeckData
using Hashtable = ExitGames.Client.Photon.Hashtable; // custom room properties (tách matchmaking gauntlet)

namespace LoRClone.Net
{
    /// <summary>
    /// PHASE 3 — UI phòng chờ PvP, TỰ DỰNG bằng code. Chỉ cần tạo 1 GameObject + gắn component này
    /// vào SCENE SẢNH (menu). CHỈ THÊM SAU KHI Photon (PUN2) import xong.
    /// Đặt ở: Assets/Code/Net/NetworkLauncher.cs
    ///
    /// Luồng: bấm "TÌM TRẬN" → nối Photon → tự ghép 2 người vào 1 phòng →
    /// đủ 2 người, HOST load scene game (client tự theo nhờ AutomaticallySyncScene).
    ///
    /// ⚠ Đổi 'gameSceneName' cho khớp tên scene game của bạn (đúng chính tả, đã add vào Build Settings).
    /// </summary>
    public class NetworkLauncher : MonoBehaviourPunCallbacks
    {
        [Header("Tên scene game (phải add vào File > Build Settings)")]
        public string gameSceneName = "GameScene";

        [Header("Hiển thị")]
        [Tooltip("Ẩn menu tìm trận lúc mới vào lobby (chỉ hiện khi bấm nút PvP). "
                 + "Tắt = luôn hiện đè giữa màn hình như cũ.")]
        public bool startHidden = true;

        [Header("Font (tùy chọn)")]
        public TMP_FontAsset uiFont;

        [Header("Chọn deck để đấu (LobbyMenuView tự gán nếu để trống)")]
        [Tooltip("CardLibrary để resolve deck tự tạo (JSON) → CardData.")]
        public CardLibrary cardLibrary;
        [Tooltip("Deck preset. Gộp với deck tự tạo (Xưởng Deck) làm danh sách chọn.")]
        public DeckData[] availableDecks;
        [Tooltip("Kích thước mỗi ô deck (ảnh champion). Cao > rộng cho dáng card dọc kiểu HSR.")]
        public Vector2 cardCellSize = new Vector2(160f, 224f);

        Canvas _canvas;
        Button _findBtn;
        TextMeshProUGUI _status, _findLabel, _selectedLabel;
        RectTransform _deckListContent;
        RectTransform _gridScrollRoot; // gốc ScrollRect lưới deck — ẩn khi lobby đã chọn deck (OpenAndFind)
        readonly List<(Image frame, string deck)> _deckCells = new List<(Image, string)>();
        string _selectedDeckName;
        bool _searching;
        bool _cancelling;      // để OnDisconnected biết là do hủy chủ động, không báo "mất kết nối"
        bool _externalDriven;  // true = lobby đã chọn deck → ẩn lưới, vào thẳng tìm trận

        void Start()
        {
            EnsureEventSystem();
            BuildUI();
            PhotonNetwork.AutomaticallySyncScene = true; // host load scene → client tự theo
            SetStatus("Chưa kết nối");
            if (startHidden) SetVisible(false); // ẩn mặc định → không đè menu lobby; mở qua Open()
        }

        // ══════════ HIỂN THỊ (LobbyMenuView gọi) ══════════
        /// <summary>Hiện menu tìm trận PvP kèm lưới chọn deck (luồng cũ, tự chọn tại đây).</summary>
        public void Open() { _externalDriven = false; SetVisible(true); }
        /// <summary>Ẩn menu tìm trận PvP (không ngắt kết nối Photon nếu đang tìm).</summary>
        public void Close() => SetVisible(false);

        /// <summary>
        /// Lobby (LobbyMenuView) đã chọn deck qua PvpDeckSelection → mở overlay tìm trận GỌN
        /// (ẩn lưới deck) và VÀO THẲNG tìm trận, khỏi bắt chọn deck lần 2.
        /// </summary>
        public void OpenAndFind()
        {
            _externalDriven = true;
            SetVisible(true);
            if (_gridScrollRoot != null) _gridScrollRoot.gameObject.SetActive(false); // ẩn lưới (đã chọn ở lobby)
            if (PvpDeckSelection.HasSelection && _selectedLabel != null)
                _selectedLabel.text = $"Deck: {PvpDeckSelection.LocalDeckName}";
            if (!_searching) OnFindClicked(); // bắt đầu tìm ngay
        }

        /// <summary>Bật/tắt canvas menu tìm trận. Khi HIỆN → nạp lại danh sách deck.</summary>
        public void SetVisible(bool on)
        {
            if (_canvas != null) _canvas.gameObject.SetActive(on);
            if (!on) return;
            // Mở kiểu tự-chọn: hiện lại lưới. Mở kiểu OpenAndFind: giữ ẩn (đã xử lý trong OpenAndFind).
            if (!_externalDriven)
            {
                if (_gridScrollRoot != null) _gridScrollRoot.gameObject.SetActive(true);
                PopulateDeckList();
            }
            RefreshFindGate();
        }

        // ══════════ NÚT ══════════
        void OnFindClicked()
        {
            if (_searching) { CancelSearch(); return; } // đang tìm → bấm lần nữa = HỦY

            // ★ BẮT BUỘC chọn deck trước khi tìm trận (PvpDeckSelection → NetworkBridge dùng).
            if (!PvpDeckSelection.HasSelection)
            { SetStatus("Hãy chọn 1 deck ở trên trước khi tìm trận."); return; }

            _searching = true;
            _cancelling = false;
            // ★ Chốt CỜ GAUNTLET cho phiên tìm này: có loadout champion = Đấu Trường Anh Hùng.
            //   Dùng để CHỈ ghép gauntlet↔gauntlet (không lẫn PvP thường → không ai mất buff/bất công).
            _gauntlet = !string.IsNullOrEmpty(LoRClone.Data.PvpLoadoutSelection.LocalJson);
            // Bật cờ PvP NGAY từ đây → khi scene game load, GameController.Start() KHÔNG auto-start.
            LoRClone.Controller.GameController.PendingNetworkMode = true;
            if (_findLabel) _findLabel.text = "HỦY TÌM";
            SetStatus(_gauntlet ? "Đang tìm trận Đấu Trường..." : "Đang tìm trận...");
            if (PhotonNetwork.IsConnectedAndReady) JoinRandomFiltered();
            else PhotonNetwork.ConnectUsingSettings(); // nối xong sẽ tự tìm ở OnConnectedToMaster
        }

        // Cờ + hằng cho tách matchmaking theo mode (gauntlet vs PvP thường).
        bool _gauntlet;
        const string RoomModeKey = "g"; // 1 = gauntlet, 0 = PvP thường

        Hashtable ModeProps() => new Hashtable { { RoomModeKey, _gauntlet ? 1 : 0 } };

        // Tìm phòng CÙNG mode: chỉ join phòng có "g" khớp cờ _gauntlet.
        void JoinRandomFiltered() => PhotonNetwork.JoinRandomRoom(ModeProps(), 2);

        /// <summary>Hủy tìm trận: rời phòng / ngắt kết nối và reset về trạng thái ban đầu.</summary>
        void CancelSearch()
        {
            _searching = false;
            _cancelling = true;
            LoRClone.Controller.GameController.PendingNetworkMode = false; // tắt cờ PvP
            if (_findLabel) _findLabel.text = "TÌM TRẬN";
            SetStatus("Đã hủy tìm trận");

            if (PhotonNetwork.InRoom) PhotonNetwork.LeaveRoom();
            else if (PhotonNetwork.IsConnected) PhotonNetwork.Disconnect();
            else _cancelling = false; // chưa kết nối gì → không có callback, khỏi giữ cờ
        }

        // ══════════ PHOTON CALLBACKS ══════════
        public override void OnConnectedToMaster()
        {
            SetStatus("Đã kết nối máy chủ");
            if (_searching) JoinRandomFiltered();
        }

        public override void OnJoinRandomFailed(short code, string msg)
        {
            if (!_searching) return; // đã hủy giữa chừng → không tạo phòng
            // chưa có phòng CÙNG mode → tự tạo phòng CÓ GẮN CỜ mode (phòng gauntlet chỉ nhận người gauntlet).
            SetStatus("Tạo phòng mới, đang chờ đối thủ...");
            PhotonNetwork.CreateRoom(null, new RoomOptions
            {
                MaxPlayers = 2,
                CustomRoomProperties = ModeProps(),
                CustomRoomPropertiesForLobby = new[] { RoomModeKey }   // phải public-for-lobby thì JoinRandom mới lọc được
            });
        }

        public override void OnJoinedRoom()
        {
            int n = PhotonNetwork.CurrentRoom.PlayerCount;
            SetStatus($"Đã vào phòng ({n}/2)");
            if (n == 2) TryStart();
        }

        public override void OnPlayerEnteredRoom(Player newPlayer)
        {
            int n = PhotonNetwork.CurrentRoom.PlayerCount;
            SetStatus($"Đối thủ đã vào ({n}/2)");
            if (n == 2) TryStart();
        }

        public override void OnDisconnected(DisconnectCause cause)
        {
            _searching = false;
            if (_findLabel) _findLabel.text = "TÌM TRẬN";
            if (_cancelling) { _cancelling = false; SetStatus("Đã hủy tìm trận"); }
            else SetStatus("Mất kết nối: " + cause);
        }

        // Rời phòng do hủy (LeaveRoom) → ngắt hẳn kết nối cho gọn; OnDisconnected sẽ báo "Đã hủy".
        public override void OnLeftRoom()
        {
            if (_cancelling && PhotonNetwork.IsConnected) PhotonNetwork.Disconnect();
        }

        void TryStart()
        {
            // Chỉ HOST (master) load scene; client tự theo nhờ AutomaticallySyncScene.
            if (PhotonNetwork.IsMasterClient)
            {
                SetStatus("Đủ 2 người — vào trận!");
                PhotonNetwork.LoadLevel(gameSceneName);
            }
            else SetStatus("Đủ 2 người — chờ host vào trận...");
        }

        void SetStatus(string s) { if (_status) _status.text = s; Debug.Log("[PvP] " + s); }

        // ══════════ UI TỰ DỰNG (phong cách HSR) ══════════
        void BuildUI()
        {
            var cgo = new GameObject("PvPLauncherCanvas", typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
            _canvas = cgo.GetComponent<Canvas>();
            _canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            _canvas.sortingOrder = 700;
            var scaler = cgo.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920f, 1080f);
            scaler.matchWidthOrHeight = 0.5f;

            // Nền tối phủ gần hết màn — cảm giác "Kho Nhân Vật" HSR.
            var panel = Rect("Panel", _canvas.transform, new Vector2(0.06f, 0.05f), new Vector2(0.94f, 0.95f));
            var pi = panel.gameObject.AddComponent<Image>();
            pi.sprite = UISprites.RoundedRect(); pi.type = Image.Type.Sliced;
            pi.color = new Color(0.035f, 0.045f, 0.085f, 0.985f);

            // Thanh tiêu đề trên
            var topbar = Rect("TopBar", panel, new Vector2(0f, 0.90f), new Vector2(1f, 1f));
            var tb = topbar.gameObject.AddComponent<Image>();
            tb.sprite = UISprites.RoundedRect(); tb.type = Image.Type.Sliced;
            tb.color = new Color(0.07f, 0.09f, 0.15f, 1f);
            Label(Rect("T", topbar, new Vector2(0.2f, 0f), new Vector2(0.8f, 1f)),
                  "ĐẤU MẠNG (PvP)", 34f, new Color(0.95f, 0.83f, 0.5f), TextAlignmentOptions.Center, true);

            Label(Rect("Sub", panel, new Vector2(0.03f, 0.855f), new Vector2(0.97f, 0.895f)),
                  "CHỌN BỘ BÀI RA TRẬN", 15f, new Color(0.62f, 0.70f, 0.84f), TextAlignmentOptions.Center, false);

            // Nút ĐÓNG (← MENU) — góc trên trái
            var crt = Rect("CloseBtn", topbar, new Vector2(0.012f, 0.2f), new Vector2(0.10f, 0.8f));
            var ci = crt.gameObject.AddComponent<Image>();
            ci.sprite = UISprites.RoundedRect(); ci.type = Image.Type.Sliced; ci.color = new Color(0.5f, 0.14f, 0.14f, 0.95f);
            var cbtn = crt.gameObject.AddComponent<Button>(); cbtn.targetGraphic = ci;
            Label(crt, "← MENU", 15f, Color.white, TextAlignmentOptions.Center, true);
            cbtn.onClick.AddListener(Close);

            // Lưới deck — mỗi deck 1 card có khung + ảnh champion (unit cost cao nhất).
            _deckListContent = BuildGridScroll("DeckGrid", panel, new Vector2(0.02f, 0.20f), new Vector2(0.98f, 0.85f));

            // Footer: nhãn deck đang chọn + TÌM TRẬN + status
            _selectedLabel = Label(Rect("Sel", panel, new Vector2(0.03f, 0.155f), new Vector2(0.97f, 0.20f)),
                            "Chưa chọn deck", 18f, new Color(0.95f, 0.83f, 0.5f), TextAlignmentOptions.Center, true);

            var brt = Rect("FindBtn", panel, new Vector2(0.33f, 0.055f), new Vector2(0.67f, 0.145f));
            var bi = brt.gameObject.AddComponent<Image>();
            bi.sprite = UISprites.RoundedRect(); bi.type = Image.Type.Sliced; bi.color = new Color(0.95f, 0.72f, 0.22f, 0.98f);
            _findBtn = brt.gameObject.AddComponent<Button>(); _findBtn.targetGraphic = bi;
            var fc = _findBtn.colors;
            fc.normalColor = Color.white; fc.highlightedColor = new Color(1f, 1f, 1f, 0.92f);
            fc.pressedColor = new Color(0.9f, 0.9f, 0.9f, 1f);
            fc.disabledColor = new Color(0.5f, 0.5f, 0.5f, 0.55f);
            _findBtn.colors = fc;
            _findLabel = Label(brt, "TÌM TRẬN", 26f, new Color(0.12f, 0.09f, 0.03f), TextAlignmentOptions.Center, true);
            _findBtn.onClick.AddListener(OnFindClicked);

            _status = Label(Rect("S", panel, new Vector2(0.03f, 0.01f), new Vector2(0.97f, 0.05f)),
                            "", 18f, new Color(0.8f, 0.82f, 0.88f), TextAlignmentOptions.Center, false);
        }

        // ══════════ LƯỚI DECK (bìa = ảnh champion) ══════════
        void PopulateDeckList()
        {
            if (_deckListContent == null) return;
            for (int i = _deckListContent.childCount - 1; i >= 0; i--)
                Destroy(_deckListContent.GetChild(i).gameObject);
            _deckCells.Clear();

            var decks = new List<DeckData>();
            if (availableDecks != null)
                foreach (var d in availableDecks) if (d != null) decks.Add(d);
            // Deck tự tạo (Xưởng Deck, JSON) — dựng runtime, tên có hậu tố " *".
            decks.AddRange(CustomDeckStore.LoadAllAsDecks(cardLibrary, availableDecks));

            if (decks.Count == 0)
            {
                Label(Rect("Empty", _deckListContent, Vector2.zero, Vector2.one),
                      "Chưa có deck.\nTạo deck ở XƯỞNG DECK hoặc gán Available Decks.",
                      16f, new Color(0.8f, 0.55f, 0.55f), TextAlignmentOptions.Center, false);
                return;
            }

            foreach (var deck in decks) SpawnDeckCard(deck);
            LayoutRebuilder.ForceRebuildLayoutImmediate(_deckListContent);
        }

        // 1 deck = 1 card dọc: ảnh champion + badge cost + dải tên đáy. Chọn → khung vàng.
        void SpawnDeckCard(DeckData deck)
        {
            var cell = Rect("Deck_" + deck.deckName, _deckListContent, Vector2.zero, Vector2.one);
            // Khung (viền): ảnh inset để khung lộ ra như đường viền; đổi VÀNG khi chọn.
            var frame = cell.gameObject.AddComponent<Image>();
            frame.sprite = UISprites.RoundedRect(); frame.type = Image.Type.Sliced;
            frame.color = new Color(0.11f, 0.13f, 0.20f, 1f);

            // Ảnh champion (unit cost cao nhất) — focal UV giống thumbnail Xưởng Deck.
            var champ = GetChampion(deck);
            var artRT = Rect("Art", cell, new Vector2(0.05f, 0.16f), new Vector2(0.95f, 0.95f));
            if (champ != null && champ.artwork != null)
            {
                var raw = artRT.gameObject.AddComponent<RawImage>();
                raw.texture = champ.artwork; raw.raycastTarget = false;
                ApplyFocalUV(raw, champ, cardCellSize.x, cardCellSize.y * 0.8f);
            }
            else
            {
                var ph = artRT.gameObject.AddComponent<Image>();
                ph.color = new Color(0.06f, 0.07f, 0.11f, 1f); ph.raycastTarget = false;
                Label(artRT, "?", 40f, new Color(0.3f, 0.34f, 0.42f), TextAlignmentOptions.Center, true);
            }

            // Badge cost champion (góc trên-trái)
            if (champ != null)
            {
                var badge = Rect("Cost", cell, new Vector2(0.06f, 0.80f), new Vector2(0.30f, 0.955f));
                var bImg = badge.gameObject.AddComponent<Image>();
                bImg.sprite = UISprites.RoundedRect(); bImg.type = Image.Type.Sliced;
                bImg.color = new Color(0.13f, 0.27f, 0.45f, 0.96f); bImg.raycastTarget = false;
                Label(badge, champ.manaCost.ToString(), 18f, Color.white, TextAlignmentOptions.Center, true);
            }

            // Dải tên deck + số lá (đáy)
            var band = Rect("Band", cell, new Vector2(0.03f, 0.02f), new Vector2(0.97f, 0.155f));
            var bandImg = band.gameObject.AddComponent<Image>();
            bandImg.color = new Color(0f, 0f, 0f, 0.62f); bandImg.raycastTarget = false;
            int count = deck.cards != null ? deck.cards.Count : 0;
            Label(Rect("N", band, new Vector2(0.06f, 0.42f), new Vector2(0.95f, 1f)),
                  deck.deckName, 15f, Color.white, TextAlignmentOptions.Left, true);
            Label(Rect("C", band, new Vector2(0.06f, 0f), new Vector2(0.95f, 0.45f)),
                  $"{count} lá", 12f, new Color(0.72f, 0.78f, 0.88f), TextAlignmentOptions.Left, false);

            // Overlay click phủ toàn ô (đặt cuối → nằm trên cùng)
            var input = Rect("Input", cell, Vector2.zero, Vector2.one);
            var iImg = input.gameObject.AddComponent<Image>(); iImg.color = new Color(1f, 1f, 1f, 0f);
            var btn = input.gameObject.AddComponent<Button>(); btn.targetGraphic = iImg;
            var hc = btn.colors;
            hc.normalColor = new Color(1f, 1f, 1f, 0f);
            hc.highlightedColor = new Color(1f, 1f, 1f, 0.08f);
            hc.pressedColor = new Color(1f, 1f, 1f, 0.16f);
            btn.colors = hc;
            var d = deck;
            btn.onClick.AddListener(() => SelectDeck(d));

            _deckCells.Add((frame, deck.deckName));
        }

        void SelectDeck(DeckData deck)
        {
            if (deck == null || deck.cards == null) return;
            var names = new List<string>();
            foreach (var c in deck.cards) if (c != null) names.Add(c.cardName);
            PvpDeckSelection.Set(deck.deckName, names);
            _selectedDeckName = deck.deckName;
            if (_selectedLabel) _selectedLabel.text = $"Đã chọn: {deck.deckName} ({names.Count} lá)";

            // Tô khung VÀNG ô đang chọn, còn lại về tối.
            foreach (var (frame, name) in _deckCells)
                if (frame != null)
                    frame.color = (name == _selectedDeckName)
                        ? new Color(0.95f, 0.79f, 0.35f, 1f)
                        : new Color(0.11f, 0.13f, 0.20f, 1f);

            RefreshFindGate();
        }

        void RefreshFindGate()
        {
            // TÌM TRẬN chỉ bấm được khi ĐÃ chọn deck (hoặc đang tìm → cho bấm để HỦY).
            if (_findBtn != null)
                _findBtn.interactable = _searching || PvpDeckSelection.HasSelection;
        }

        // ── Unit cost cao nhất làm bìa deck (fallback: card cost cao nhất bất kỳ) ──
        static CardData GetChampion(DeckData deck)
        {
            if (deck == null || deck.cards == null) return null;
            CardData bestUnit = null, bestAny = null;
            foreach (var cd in deck.cards)
            {
                if (cd == null) continue;
                if (bestAny == null || cd.manaCost > bestAny.manaCost) bestAny = cd;
                if (cd.cardType == CardType.Unit && (bestUnit == null || cd.manaCost > bestUnit.manaCost))
                    bestUnit = cd;
            }
            return bestUnit ?? bestAny;
        }

        // ── uvRect theo focal point (copy logic CardView.ApplyFocalUV) ──
        static void ApplyFocalUV(RawImage raw, CardData data, float dispW, float dispH)
        {
            if (raw == null || raw.texture == null || data == null) return;
            var tex = raw.texture;
            if (tex.width <= 0 || tex.height <= 0) { raw.uvRect = new UnityEngine.Rect(0, 0, 1, 1); return; }
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
            raw.uvRect = new UnityEngine.Rect(uvX, uvY, uvW, uvH);
        }

        // ScrollRect + Viewport(Mask) + Content(GridLayoutGroup + CSF). Trả về Content để spawn ô.
        RectTransform BuildGridScroll(string name, RectTransform parent, Vector2 ancMin, Vector2 ancMax)
        {
            var scrollRT = Rect(name, parent, ancMin, ancMax);
            _gridScrollRoot = scrollRT; // lưu để OpenAndFind ẩn lưới
            var sr = scrollRT.gameObject.AddComponent<ScrollRect>();
            sr.horizontal = false; sr.movementType = ScrollRect.MovementType.Clamped; sr.scrollSensitivity = 30f;

            var vp = Rect("Viewport", scrollRT, Vector2.zero, Vector2.one);
            vp.gameObject.AddComponent<Image>().color = Color.white;
            vp.gameObject.AddComponent<Mask>().showMaskGraphic = false;

            var content = Rect("Content", vp, new Vector2(0f, 1f), new Vector2(1f, 1f));
            content.pivot = new Vector2(0.5f, 1f);
            content.offsetMin = new Vector2(0f, -300f); content.offsetMax = Vector2.zero;
            var glg = content.gameObject.AddComponent<GridLayoutGroup>();
            glg.cellSize = cardCellSize;
            glg.spacing = new Vector2(14f, 14f);
            glg.padding = new RectOffset(16, 16, 14, 14);
            glg.childAlignment = TextAnchor.UpperCenter;
            content.gameObject.AddComponent<ContentSizeFitter>().verticalFit = ContentSizeFitter.FitMode.PreferredSize;

            sr.viewport = vp; sr.content = content;
            return content;
        }

        static void EnsureEventSystem()
        {
            if (FindFirstObjectByType<UnityEngine.EventSystems.EventSystem>() == null)
                new GameObject("EventSystem",
                    typeof(UnityEngine.EventSystems.EventSystem),
                    typeof(UnityEngine.EventSystems.StandaloneInputModule));
        }

        RectTransform Rect(string name, Transform parent, Vector2 amin, Vector2 amax)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            var rt = go.AddComponent<RectTransform>();
            rt.anchorMin = amin; rt.anchorMax = amax; rt.offsetMin = rt.offsetMax = Vector2.zero;
            return rt;
        }

        TextMeshProUGUI Label(RectTransform parent, string text, float size, Color color, TextAlignmentOptions align, bool bold)
        {
            var go = new GameObject("Label"); go.transform.SetParent(parent, false);
            var rt = go.AddComponent<RectTransform>();
            rt.anchorMin = Vector2.zero; rt.anchorMax = Vector2.one; rt.offsetMin = new Vector2(6, 4); rt.offsetMax = new Vector2(-6, -4);
            var tmp = go.AddComponent<TextMeshProUGUI>();
            if (uiFont) tmp.font = uiFont;
            tmp.text = text; tmp.fontSize = size; tmp.color = color; tmp.alignment = align;
            tmp.fontStyle = bold ? FontStyles.Bold : FontStyles.Normal; tmp.raycastTarget = false; tmp.textWrappingMode = TextWrappingModes.Normal;
            return tmp;
        }
    }
}