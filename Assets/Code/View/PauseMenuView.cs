using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.SceneManagement;
using TMPro;
using LoRClone.Controller;
using LoRClone.Data;   // CampaignRun (luật trận)

namespace LoRClone.View
{
    /// <summary>
    /// Menu Tạm Dừng / Cài Đặt trong trận — TỰ DỰNG toàn bộ UI bằng code, style theo Honkai: Star Rail.
    /// Chỉ cần tạo 1 GameObject trong scene game + gắn component này. KHÔNG cần dựng UI / kéo ref tay.
    ///
    /// Style HSR: nền tối full màn, rail tab tròn bên trái, hàng thanh sáng chữ xám đậm,
    /// slider cam (thang 0-10), 3 nút viền bo góc dưới, header vàng + tên tab trắng lớn.
    ///
    /// Font: để khớp 100% chữ HSR, thả 1 TMP_FontAsset vào field 'uiFont' (Inspector). Bỏ trống = font mặc định.
    /// Pause: Open()/Close() set Time.timeScale trực tiếp → đóng băng AI (cả 2 phe) + animation.
    /// </summary>
    public class PauseMenuView : MonoBehaviour
    {
        [Header("Scene")]
        [Tooltip("Tên scene sảnh. Để trống = LoadScene(0).")]
        public string lobbySceneName = "";

        [Header("Font (tùy chọn — thả TMP font HSR vào để khớp chữ)")]
        public TMP_FontAsset uiFont;

        [Header("Style (HSR palette)")]
        public Color dimColor = new Color(0.03f, 0.04f, 0.06f, 0.88f); // nền tối full màn
        public Color goldColor = new Color(0.80f, 0.66f, 0.42f, 1f);    // phụ đề + emblem
        public Color titleColor = new Color(0.97f, 0.97f, 0.95f, 1f);    // tên tab lớn
        public Color sectionColor = new Color(0.96f, 0.96f, 0.93f, 1f);    // section header
        public Color dividerColor = new Color(1f, 1f, 1f, 0.16f);
        public Color rowColor = new Color(0.88f, 0.88f, 0.85f, 0.90f); // thanh sáng
        public Color rowText = new Color(0.14f, 0.14f, 0.15f, 1f);    // chữ xám đậm trên thanh
        public Color accent = new Color(0.96f, 0.62f, 0.15f, 1f);    // cam HSR
        public Color trackColor = new Color(0.30f, 0.30f, 0.30f, 0.45f); // rãnh slider (trên thanh sáng)
        public Color iconActive = new Color(1f, 1f, 1f, 0.95f);
        public Color iconIdle = new Color(1f, 1f, 1f, 0.42f);
        public Color circleActive = new Color(1f, 1f, 1f, 0.12f);
        public Color circleIdle = new Color(1f, 1f, 1f, 0.04f);
        public Color railLine = new Color(1f, 1f, 1f, 0.12f);
        public Color btnBorder = new Color(0.86f, 0.86f, 0.82f, 0.55f);
        public Color btnFill = new Color(0.10f, 0.11f, 0.13f, 0.45f);
        public Color btnFillHover = new Color(0.96f, 0.62f, 0.15f, 0.28f);
        public Color btnText = new Color(0.97f, 0.97f, 0.95f, 1f);

        // Tên hiển thị
        readonly string[] _tabBig = { "Thông Tin Trận", "Âm Thanh", "Tùy Chọn Khác" };

        Canvas _canvas;
        GameObject _panel;
        GameObject[] _contents;
        TextMeshProUGUI _tabNameText, _waveText, _infoText, _autoBtnText;
        Image _autoBtnFill;

        // Màn kết quả (thắng/thua)
        GameObject _resultPanel;
        TextMeshProUGUI _resultTitle, _resultSub;
        bool _gameOverHooked;
        Image[] _tabCircles;
        List<Image>[] _tabIcons;
        Slider _sMaster, _sMusic, _sSfx, _sVoice;
        TextMeshProUGUI _vMaster, _vMusic, _vSfx, _vVoice;

        // Sprite tự sinh (bo góc + tròn) — không phụ thuộc resource built-in của Unity
        static Sprite _spRound, _spCircle, _spSpeaker;
        static Sprite RoundSp { get { if (!_spRound) _spRound = UISprites.RoundedRect(); return _spRound; } }
        static Sprite CircleSp { get { if (!_spCircle) _spCircle = UISprites.Circle(); return _spCircle; } }

        GameController GC => GameController.Instance;

        [Header("Ép palette HSR trong code (bỏ qua giá trị Inspector cũ). Tắt nếu muốn tự chỉnh màu.)")]
        public bool forceHsrPalette = true;

        void Start()
        {
            if (forceHsrPalette) ApplyPalette();
            EnsureEventSystem();

            var cgo = new GameObject("PauseMenuCanvas", typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
            _canvas = cgo.GetComponent<Canvas>();
            _canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            _canvas.sortingOrder = 900;
            var scaler = cgo.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920f, 1080f);
            scaler.matchWidthOrHeight = 0.5f;

            BuildOpenButton();
            BuildPanel();
            _panel.SetActive(false);

            BuildResultPanel();
            StartCoroutine(HookGameOver());
        }

        // Chờ GameController.model sẵn sàng rồi lắng nghe OnGameOver để hiện màn kết quả.
        IEnumerator HookGameOver()
        {
            while (GC == null || GC.model == null) yield return null;
            if (!_gameOverHooked)
            {
                GC.model.OnGameOver += OnGameOverMsg;
                _gameOverHooked = true;
            }
        }

        /// <summary>Ép toàn bộ màu về palette HSR đục/tương phản cao (không phụ thuộc Inspector cũ).</summary>
        void ApplyPalette()
        {
            dimColor = new Color(0.035f, 0.045f, 0.065f, 0.985f); // nền gần đặc → che hẳn trận đấu
            goldColor = new Color(0.83f, 0.69f, 0.44f, 1f);
            titleColor = new Color(0.98f, 0.98f, 0.96f, 1f);
            sectionColor = new Color(0.98f, 0.98f, 0.95f, 1f);
            dividerColor = new Color(1f, 1f, 1f, 0.22f);
            rowColor = new Color(0.91f, 0.91f, 0.88f, 1f);       // thanh sáng ĐỤC hoàn toàn
            rowText = new Color(0.09f, 0.09f, 0.10f, 1f);       // chữ gần đen → nổi rõ
            accent = new Color(0.97f, 0.63f, 0.16f, 1f);
            trackColor = new Color(0.45f, 0.45f, 0.43f, 0.75f);    // rãnh đậm hơn để thấy trên thanh sáng
            iconActive = new Color(1f, 1f, 1f, 0.98f);
            iconIdle = new Color(1f, 1f, 1f, 0.5f);
            circleActive = new Color(1f, 1f, 1f, 0.14f);
            circleIdle = new Color(1f, 1f, 1f, 0.05f);
            railLine = new Color(1f, 1f, 1f, 0.14f);
            btnBorder = new Color(0.88f, 0.88f, 0.84f, 0.7f);
            btnFill = new Color(0.12f, 0.13f, 0.16f, 0.9f);     // nút nền đục → chữ trắng rõ
            btnFillHover = new Color(0.97f, 0.63f, 0.16f, 0.85f);
            btnText = new Color(0.98f, 0.98f, 0.96f, 1f);
        }

        static void EnsureEventSystem()
        {
            if (FindFirstObjectByType<UnityEngine.EventSystems.EventSystem>() == null)
                new GameObject("EventSystem",
                    typeof(UnityEngine.EventSystems.EventSystem),
                    typeof(UnityEngine.EventSystems.StandaloneInputModule));
        }

        // ════════════════ NÚT MỞ (☰) ════════════════
        void BuildOpenButton()
        {
            var rt = Rect("PauseOpenBtn", _canvas.transform, new Vector2(0.945f, 0.925f), new Vector2(0.99f, 0.985f));
            var img = rt.gameObject.AddComponent<Image>();
            img.sprite = RoundSp; img.type = Image.Type.Sliced; img.color = new Color(0.10f, 0.11f, 0.13f, 0.55f);
            var btn = rt.gameObject.AddComponent<Button>(); btn.targetGraphic = img;
            Tint(btn, img.color, new Color(0.96f, 0.62f, 0.15f, 0.45f));
            for (int i = 0; i < 3; i++)
            {
                float y = 0.32f + i * 0.18f;
                var bar = Rect($"Bar{i}", rt, new Vector2(0.26f, y - 0.05f), new Vector2(0.74f, y + 0.05f));
                var b = bar.gameObject.AddComponent<Image>(); b.color = goldColor; b.raycastTarget = false;
            }
            btn.onClick.AddListener(Open);
        }

        // ════════════════ PANEL ════════════════
        void BuildPanel()
        {
            var dim = Rect("PausePanel", _canvas.transform, Vector2.zero, Vector2.one);
            var dimImg = dim.gameObject.AddComponent<Image>(); dimImg.color = dimColor; dimImg.raycastTarget = true;
            _panel = dim.gameObject;

            BuildHeader(dim);
            BuildTabRail(dim);

            // Vùng nội dung (phải rail, trên hàng nút)
            var area = Rect("Area", dim, new Vector2(0.135f, 0.14f), new Vector2(0.955f, 0.80f));
            _contents = new GameObject[3];
            _contents[0] = BuildInfoTab(area);
            _contents[1] = BuildAudioTab(area);
            _contents[2] = BuildOtherTab(area);

            BuildBottomButtons(dim);
        }

        void BuildHeader(RectTransform dim)
        {
            // Emblem vàng (diamond ring) góc trái-trên
            var em = Rect("Emblem", dim, new Vector2(0.055f, 0.905f), new Vector2(0.083f, 0.965f));
            var eo = em.gameObject.AddComponent<Image>(); eo.color = goldColor; eo.raycastTarget = false;
            em.localRotation = Quaternion.Euler(0, 0, 45f);
            var ei = Rect("EmblemInner", em, new Vector2(0.22f, 0.22f), new Vector2(0.78f, 0.78f));
            var eii = ei.gameObject.AddComponent<Image>(); eii.color = dimColor; eii.raycastTarget = false;

            // Phụ đề vàng
            var sub = Label(Rect("Sub", dim, new Vector2(0.093f, 0.945f), new Vector2(0.55f, 0.985f)),
                            "TẠM DỪNG CHIẾN ĐẤU", 20f, goldColor, TextAlignmentOptions.Left, false);
            sub.characterSpacing = 6f;

            // Tên tab lớn (trắng)
            _tabNameText = Label(Rect("TabName", dim, new Vector2(0.092f, 0.885f), new Vector2(0.6f, 0.95f)),
                                 _tabBig[1], 42f, titleColor, TextAlignmentOptions.Left, true);

            // "Số đợt x/y" (phải)
            _waveText = Label(Rect("Wave", dim, new Vector2(0.78f, 0.925f), new Vector2(0.925f, 0.98f)),
                              "Số đợt 1/1", 22f, new Color(1, 1, 1, 0.75f), TextAlignmentOptions.Right, false);

            // Nút ✕ (2 vạch chéo)
            var xrt = Rect("Close", dim, new Vector2(0.935f, 0.925f), new Vector2(0.985f, 0.985f));
            var xImg = xrt.gameObject.AddComponent<Image>(); xImg.color = new Color(1, 1, 1, 0.001f);
            var xBtn = xrt.gameObject.AddComponent<Button>(); xBtn.targetGraphic = xImg;
            for (int s = 0; s < 2; s++)
            {
                var bar = Rect($"XBar{s}", xrt, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f));
                bar.sizeDelta = new Vector2(34, 3f);
                var bi = bar.gameObject.AddComponent<Image>(); bi.color = new Color(0.95f, 0.95f, 0.93f, 0.9f); bi.raycastTarget = false;
                bar.localRotation = Quaternion.Euler(0, 0, s == 0 ? 45f : -45f);
            }
            xBtn.onClick.AddListener(Close);
        }

        void BuildTabRail(RectTransform dim)
        {
            // Đường nối dọc mờ
            var line = Rect("RailLine", dim, new Vector2(0.069f, 0.40f), new Vector2(0.069f, 0.78f));
            line.sizeDelta = new Vector2(2f, 0f);
            var li = line.gameObject.AddComponent<Image>(); li.color = railLine; li.raycastTarget = false;

            string[] names = { "Info", "Audio", "Other" };
            _tabCircles = new Image[3];
            _tabIcons = new List<Image>[3];
            for (int i = 0; i < 3; i++)
            {
                float cy = 0.735f - i * 0.115f;
                var c = Rect($"Tab{i}", dim, new Vector2(0.069f, cy), new Vector2(0.069f, cy));
                c.sizeDelta = new Vector2(74f, 74f);
                var ci = c.gameObject.AddComponent<Image>(); ci.sprite = CircleSp; ci.type = Image.Type.Simple; ci.color = circleIdle;
                var btn = c.gameObject.AddComponent<Button>(); btn.targetGraphic = ci;
                Tint(btn, circleIdle, circleActive);
                _tabCircles[i] = ci;

                _tabIcons[i] = new List<Image>();
                if (i == 0) IconDiamond(c, _tabIcons[i]);
                else if (i == 1) IconEqualizer(c, _tabIcons[i]);
                else IconLayers(c, _tabIcons[i]);

                int idx = i;
                btn.onClick.AddListener(() => ShowTab(idx));
            }
        }

        void BuildBottomButtons(RectTransform dim)
        {
            MainButton(dim, new Vector2(0.135f, 0.055f), new Vector2(0.335f, 0.115f), "Khiêu Chiến Lại", Restart);
            BuildConcedeButton(dim, new Vector2(0.345f, 0.055f), new Vector2(0.535f, 0.115f)); // Đầu Hàng (đỏ) — ô giữa
            MainButton(dim, new Vector2(0.545f, 0.055f), new Vector2(0.755f, 0.115f), "Trở Về Sảnh Đường", ReturnLobby);
            MainButton(dim, new Vector2(0.76f, 0.055f), new Vector2(0.955f, 0.115f), "Tiếp Tục Chiến Đấu", Close);
        }

        // Nút ĐẦU HÀNG (đỏ) — kết thúc trận, phe mình thua.
        void BuildConcedeButton(RectTransform parent, Vector2 amin, Vector2 amax)
        {
            var rt = Rect("DauHang", parent, amin, amax);
            var border = rt.gameObject.AddComponent<Image>();
            border.sprite = RoundSp; border.type = Image.Type.Sliced; border.pixelsPerUnitMultiplier = 2f;
            border.color = new Color(0.82f, 0.42f, 0.40f, 0.75f);
            var fillRt = Rect("Fill", rt, Vector2.zero, Vector2.one);
            fillRt.offsetMin = new Vector2(2, 2); fillRt.offsetMax = new Vector2(-2, -2);
            var fill = fillRt.gameObject.AddComponent<Image>();
            fill.sprite = RoundSp; fill.type = Image.Type.Sliced; fill.pixelsPerUnitMultiplier = 2f;
            fill.color = new Color(0.36f, 0.10f, 0.10f, 0.92f);
            var btn = rt.gameObject.AddComponent<Button>(); btn.targetGraphic = fill;
            Tint(btn, fill.color, new Color(0.76f, 0.20f, 0.18f, 0.95f));
            Label(rt, "Đầu Hàng", 24f, new Color(1f, 0.90f, 0.88f, 1f), TextAlignmentOptions.Center, false);
            btn.onClick.AddListener(OnConcede);
        }

        void OnConcede()
        {
            Close();                 // resume timeScale + ẩn pause
            GC?.ConcedeLocal();      // → CheckGameOver → OnGameOver → ShowResult (PvP: relay 2 máy)
        }

        // ════════════════ TAB ICONS (vẽ bằng Image, tránh tofu) ════════════════
        void IconDiamond(RectTransform c, List<Image> store)
        {
            var d = Rect("Ico", c, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f));
            d.sizeDelta = new Vector2(26, 26); d.localRotation = Quaternion.Euler(0, 0, 45f);
            var oi = d.gameObject.AddComponent<Image>(); oi.color = iconIdle; oi.raycastTarget = false; store.Add(oi);
        }

        void IconEqualizer(RectTransform c, List<Image> store)
        {
            float[] h = { 0.26f, 0.42f, 0.32f };
            for (int i = 0; i < 3; i++)
            {
                float x = 0.36f + i * 0.14f;
                var b = Rect($"Eq{i}", c, new Vector2(x - 0.045f, 0.5f - h[i]), new Vector2(x + 0.045f, 0.5f + h[i]));
                var im = b.gameObject.AddComponent<Image>(); im.color = iconIdle; im.raycastTarget = false; store.Add(im);
            }
        }

        void IconLayers(RectTransform c, List<Image> store)
        {
            for (int i = 0; i < 3; i++)
            {
                float y = 0.36f + i * 0.14f;
                var b = Rect($"Ly{i}", c, new Vector2(0.34f, y - 0.035f), new Vector2(0.66f, y + 0.035f));
                var im = b.gameObject.AddComponent<Image>(); im.color = iconIdle; im.raycastTarget = false; store.Add(im);
            }
        }

        // ════════════════ TAB: THÔNG TIN ════════════════
        GameObject BuildInfoTab(RectTransform area)
        {
            var c = Rect("InfoTab", area, Vector2.zero, Vector2.one).gameObject;
            var rt = GetRT(c.transform);
            float cur = 0f;
            SectionHeader(rt, ref cur, "Thông Tin Trận Đấu");
            _infoText = Label(StackTop(rt, ref cur, 340f, 8f, 8f, 8f), "", 24f, rowText, TextAlignmentOptions.TopLeft, false);
            _infoText.raycastTarget = false; _infoText.lineSpacing = 18f;
            return c;
        }

        // ════════════════ TAB: ÂM THANH ════════════════
        GameObject BuildAudioTab(RectTransform area)
        {
            var c = Rect("AudioTab", area, Vector2.zero, Vector2.one).gameObject;
            var rt = GetRT(c.transform);
            float cur = 0f;
            SectionHeader(rt, ref cur, "Thiết Lập Âm Thanh");
            SliderRow(rt, ref cur, "Âm Lượng Chung", AudioListener.volume, v => AudioListener.volume = v, out _sMaster, out _vMaster);
            cur += 6f;
            SectionHeader(rt, ref cur, "Cân Bằng Âm Lượng");
            SliderRow(rt, ref cur, "Âm Lượng Nhạc", Vol(a => a.musicVolume, 0.5f), v => AudioManager.SetMusicVolumeStatic(v), out _sMusic, out _vMusic);
            SliderRow(rt, ref cur, "Âm Lượng Lồng Tiếng", Vol(a => a.cardVolume, 1f), v => AudioManager.SetCardVolumeStatic(v), out _sVoice, out _vVoice);
            SliderRow(rt, ref cur, "Âm Lượng Hiệu Ứng", Vol(a => a.MasterSfxVolume, 1f), v => AudioManager.SetSfxVolumeStatic(v), out _sSfx, out _vSfx);
            return c;
        }

        // ════════════════ TAB: KHÁC ════════════════
        GameObject BuildOtherTab(RectTransform area)
        {
            var c = Rect("OtherTab", area, Vector2.zero, Vector2.one).gameObject;
            var rt = GetRT(c.transform);
            float cur = 0f;
            SectionHeader(rt, ref cur, "Chế Độ Chơi");

            var row = StackTop(rt, ref cur, 82f, 0f, 0f, 8f);
            RowBg(row);
            Label(Rect("L", row, new Vector2(0.03f, 0f), new Vector2(0.6f, 1f)),
                  "Tự Động Chiến Đấu (PlayerAI)", 24f, rowText, TextAlignmentOptions.Left, false);
            var brt = Rect("AutoBtn", row, new Vector2(0.74f, 0.2f), new Vector2(0.97f, 0.8f));
            _autoBtnFill = brt.gameObject.AddComponent<Image>();
            _autoBtnFill.sprite = RoundSp; _autoBtnFill.type = Image.Type.Sliced; _autoBtnFill.color = trackColor;
            var bbtn = brt.gameObject.AddComponent<Button>(); bbtn.targetGraphic = _autoBtnFill;
            _autoBtnText = Label(brt, "TẮT", 22f, rowText, TextAlignmentOptions.Center, true);
            bbtn.onClick.AddListener(() => { GC?.TogglePlayerAutoPlay(); RefreshAuto(); });
            return c;
        }

        // ════════════════ LOGIC ════════════════
        public void Open()
        {
            if (_panel == null) return;
            Time.timeScale = 0f;
            GC?.Pause();
            Debug.Log($"[PauseMenu] OPEN → timeScale={Time.timeScale}");
            _panel.SetActive(true);
            _panel.transform.SetAsLastSibling();
            RefreshInfo(); RefreshAudio(); RefreshAuto();
            ShowTab(1); // mặc định vào Âm Thanh giống ảnh
        }

        public void Close()
        {
            if (_panel) _panel.SetActive(false);
            Time.timeScale = 1f;
            GC?.Resume();
            Debug.Log($"[PauseMenu] CLOSE → timeScale={Time.timeScale}");
        }

        void Restart()
        {
            Close();
            Time.timeScale = 1f;
            SceneManager.LoadScene(SceneManager.GetActiveScene().buildIndex);
        }

        void ReturnLobby()
        {
            Close();
            Time.timeScale = 1f;
            if (!string.IsNullOrEmpty(lobbySceneName)) SceneManager.LoadScene(lobbySceneName);
            else SceneManager.LoadScene(0);
        }

        // ════════════════ MÀN KẾT QUẢ ════════════════
        void OnGameOverMsg(string _)
        {
            bool won = GC != null && GC.LocalViewerWon();
            if (_resultTitle)
            {
                _resultTitle.text = won ? "CHIẾN THẮNG" : "THẤT BẠI";
                _resultTitle.color = won ? new Color(1f, 0.84f, 0.35f) : new Color(0.92f, 0.42f, 0.42f);
            }
            if (_resultSub)
                _resultSub.text = won ? "Bạn đã chiến thắng trận đấu!" : "Bạn đã thua trận này.";

            if (_panel) _panel.SetActive(false);   // ẩn pause nếu đang mở
            Time.timeScale = 1f;                    // để animation kết thúc chạy bình thường
            if (_resultPanel)
            {
                _resultPanel.SetActive(true);
                _resultPanel.transform.SetAsLastSibling();
            }
            Debug.Log($"[PauseMenu] GAME OVER → localWon={won} (networkMatch={GC?.IsNetworkMatch})");
        }

        void BuildResultPanel()
        {
            var dim = Rect("ResultPanel", _canvas.transform, Vector2.zero, Vector2.one);
            var di = dim.gameObject.AddComponent<Image>();
            di.color = new Color(0.02f, 0.03f, 0.05f, 0.93f); di.raycastTarget = true;
            _resultPanel = dim.gameObject;

            var card = Rect("Card", dim, new Vector2(0.32f, 0.30f), new Vector2(0.68f, 0.72f));
            var ci = card.gameObject.AddComponent<Image>();
            ci.sprite = RoundSp; ci.type = Image.Type.Sliced; ci.color = new Color(0.06f, 0.07f, 0.10f, 1f);

            // Viền vàng mảnh trên đầu card
            var top = Rect("TopLine", card, new Vector2(0.08f, 0.86f), new Vector2(0.92f, 0.875f));
            top.gameObject.AddComponent<Image>().color = goldColor;

            _resultTitle = Label(Rect("T", card, new Vector2(0.05f, 0.6f), new Vector2(0.95f, 0.86f)),
                                 "", 56f, goldColor, TextAlignmentOptions.Center, true);
            _resultSub = Label(Rect("S", card, new Vector2(0.05f, 0.46f), new Vector2(0.95f, 0.6f)),
                               "", 24f, new Color(1, 1, 1, 0.8f), TextAlignmentOptions.Center, false);

            MainButton(card, new Vector2(0.10f, 0.12f), new Vector2(0.48f, 0.28f), "Đấu Lại", Restart);
            MainButton(card, new Vector2(0.52f, 0.12f), new Vector2(0.90f, 0.28f), "Về Sảnh Đường", ReturnLobby);

            _resultPanel.SetActive(false);
        }

        void ShowTab(int i)
        {
            for (int k = 0; k < _contents.Length; k++)
                if (_contents[k]) _contents[k].SetActive(k == i);
            for (int k = 0; k < _tabCircles.Length; k++)
            {
                if (_tabCircles[k]) _tabCircles[k].color = (k == i) ? circleActive : circleIdle;
                if (_tabIcons[k] != null)
                    foreach (var g in _tabIcons[k]) if (g) g.color = (k == i) ? iconActive : iconIdle;
            }
            if (_tabNameText) _tabNameText.text = _tabBig[Mathf.Clamp(i, 0, _tabBig.Length - 1)];
        }

        void RefreshInfo()
        {
            if (GC != null && GC.model != null && _waveText)
                _waveText.text = $"Vòng {GC.model.roundNumber}";
            if (_infoText == null || GC == null || GC.model == null) return;
            var m = GC.model;
            string rule = CampaignRun.EncounterModifierBannerText();
            string ruleBlock = string.IsNullOrEmpty(rule) ? "" :
                $"\n\n<color=#C77A4A><b>LUẬT TRẬN</b></color>\n{rule}";
            _infoText.text =
                $"Vòng đấu:  <b>{m.roundNumber}</b>\n\n" +
                $"Máu    —   Bạn <b>{m.player.health}</b>     Địch <b>{m.enemy.health}</b>\n\n" +
                $"Bài trong deck   —   Bạn <b>{m.player.deck.Count}</b>     Địch <b>{m.enemy.deck.Count}</b>\n\n" +
                $"Bài trên tay   —   Bạn <b>{m.player.hand.Count}</b>     Địch <b>{m.enemy.hand.Count}</b>" +
                ruleBlock;
        }

        void RefreshAudio()
        {
            SetSlider(_sMaster, _vMaster, AudioListener.volume);
            SetSlider(_sMusic, _vMusic, Vol(a => a.musicVolume, 0.5f));
            SetSlider(_sSfx, _vSfx, Vol(a => a.MasterSfxVolume, 1f));
            SetSlider(_sVoice, _vVoice, Vol(a => a.cardVolume, 1f));
        }

        void RefreshAuto()
        {
            bool on = GC != null && GC.playerAutoPlay;
            if (_autoBtnText) { _autoBtnText.text = on ? "BẬT" : "TẮT"; _autoBtnText.color = on ? Color.white : rowText; }
            if (_autoBtnFill) _autoBtnFill.color = on ? accent : trackColor;
        }

        static float Vol(Func<AudioManager, float> get, float fallback)
            => AudioManager.Instance != null ? get(AudioManager.Instance) : fallback;

        // ════════════════ BUILDERS ════════════════
        void SectionHeader(RectTransform parent, ref float cur, string text)
        {
            var h = StackTop(parent, ref cur, 52f, 0f, 0f, 10f);
            Label(Rect("H", h, new Vector2(0f, 0f), new Vector2(0.6f, 1f)), text, 26f, sectionColor, TextAlignmentOptions.Left, true);
            var ln = Rect("Line", h, new Vector2(0f, 0f), new Vector2(1f, 0f));
            ln.sizeDelta = new Vector2(0, 2f); ln.anchoredPosition = new Vector2(0, 2f);
            var li = ln.gameObject.AddComponent<Image>(); li.color = dividerColor; li.raycastTarget = false;
        }

        void SliderRow(RectTransform parent, ref float cur, string label, float value01, Action<float> onChange,
                       out Slider slider, out TextMeshProUGUI valText)
        {
            var row = StackTop(parent, ref cur, 82f, 0f, 0f, 8f);
            RowBg(row);
            Label(Rect("L", row, new Vector2(0.03f, 0f), new Vector2(0.5f, 1f)), label, 24f, rowText, TextAlignmentOptions.Left, false);

            // Icon loa
            var sp = Rect("Spk", row, new Vector2(0.60f, 0.30f), new Vector2(0.635f, 0.70f));
            var spi = sp.gameObject.AddComponent<Image>(); spi.sprite = SpeakerSprite(); spi.color = rowText; spi.raycastTarget = false; spi.preserveAspect = true;

            // Slider
            var srt = Rect("Slider", row, new Vector2(0.655f, 0.38f), new Vector2(0.90f, 0.62f));
            slider = srt.gameObject.AddComponent<Slider>();

            var bg = Rect("Background", srt, new Vector2(0f, 0.2f), new Vector2(1f, 0.8f));
            var bgImg = bg.gameObject.AddComponent<Image>(); bgImg.sprite = RoundSp; bgImg.type = Image.Type.Sliced; bgImg.color = trackColor;

            var fillArea = Rect("Fill Area", srt, new Vector2(0f, 0.2f), new Vector2(1f, 0.8f));
            fillArea.offsetMin = new Vector2(4, 0); fillArea.offsetMax = new Vector2(-16, 0);
            var fill = Rect("Fill", fillArea, new Vector2(0f, 0f), new Vector2(1f, 1f));
            fill.sizeDelta = new Vector2(12, 0);
            var fillImg = fill.gameObject.AddComponent<Image>(); fillImg.sprite = RoundSp; fillImg.type = Image.Type.Sliced; fillImg.color = accent;

            var hsa = Rect("Handle Slide Area", srt, new Vector2(0f, 0f), new Vector2(1f, 1f));
            hsa.offsetMin = new Vector2(10, 0); hsa.offsetMax = new Vector2(-10, 0);
            var handle = Rect("Handle", hsa, new Vector2(0f, 0.5f), new Vector2(0f, 0.5f));
            handle.sizeDelta = new Vector2(26, 26);
            var hImg = handle.gameObject.AddComponent<Image>(); hImg.sprite = CircleSp; hImg.color = accent;

            slider.fillRect = fill; slider.handleRect = handle; slider.targetGraphic = hImg;
            slider.direction = Slider.Direction.LeftToRight;
            slider.minValue = 0f; slider.maxValue = 1f;
            slider.SetValueWithoutNotify(Mathf.Clamp01(value01));
            slider.onValueChanged.AddListener(v => onChange(v));

            // Giá trị 0-10 (đúng HSR)
            valText = Label(Rect("V", row, new Vector2(0.92f, 0f), new Vector2(0.99f, 1f)),
                            Mathf.RoundToInt(value01 * 10f).ToString(), 24f, rowText, TextAlignmentOptions.Center, true);
            var vt = valText;
            slider.onValueChanged.AddListener(v => vt.text = Mathf.RoundToInt(v * 10f).ToString());
        }

        void RowBg(RectTransform row)
        {
            var img = row.gameObject.AddComponent<Image>();
            img.sprite = RoundSp; img.type = Image.Type.Sliced; img.pixelsPerUnitMultiplier = 3f; img.color = rowColor;
        }

        void MainButton(RectTransform parent, Vector2 amin, Vector2 amax, string text, Action onClick)
        {
            var rt = Rect(text, parent, amin, amax);
            var border = rt.gameObject.AddComponent<Image>();
            border.sprite = RoundSp; border.type = Image.Type.Sliced; border.pixelsPerUnitMultiplier = 2f; border.color = btnBorder;
            var fillRt = Rect("Fill", rt, Vector2.zero, Vector2.one); fillRt.offsetMin = new Vector2(2, 2); fillRt.offsetMax = new Vector2(-2, -2);
            var fill = fillRt.gameObject.AddComponent<Image>();
            fill.sprite = RoundSp; fill.type = Image.Type.Sliced; fill.pixelsPerUnitMultiplier = 2f; fill.color = btnFill;
            var btn = rt.gameObject.AddComponent<Button>(); btn.targetGraphic = fill;
            Tint(btn, btnFill, btnFillHover);
            Label(rt, text, 24f, btnText, TextAlignmentOptions.Center, false);
            btn.onClick.AddListener(() => onClick());
        }

        // ════════════════ LOW-LEVEL HELPERS ════════════════
        // Xếp 1 hàng full-width neo từ ĐỈNH area xuống. cur = px đã dùng tính từ trên.
        RectTransform StackTop(RectTransform parent, ref float cur, float height, float padL, float padR, float gap)
        {
            var go = new GameObject("Stack");
            go.transform.SetParent(parent, false);
            var rt = go.AddComponent<RectTransform>();
            rt.anchorMin = new Vector2(0f, 1f); rt.anchorMax = new Vector2(1f, 1f);
            rt.offsetMin = new Vector2(padL, -(cur + height));
            rt.offsetMax = new Vector2(-padR, -cur);
            cur += height + gap;
            return rt;
        }

        RectTransform Rect(string name, Transform parent, Vector2 amin, Vector2 amax)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            var rt = go.AddComponent<RectTransform>();
            rt.anchorMin = amin; rt.anchorMax = amax;
            rt.offsetMin = rt.offsetMax = Vector2.zero;
            return rt;
        }

        TextMeshProUGUI Label(RectTransform parent, string text, float size, Color color, TextAlignmentOptions align, bool bold)
        {
            var go = new GameObject("Label");
            go.transform.SetParent(parent, false);
            var rt = go.AddComponent<RectTransform>();
            rt.anchorMin = Vector2.zero; rt.anchorMax = Vector2.one;
            rt.offsetMin = new Vector2(8, 2); rt.offsetMax = new Vector2(-8, -2);
            var tmp = go.AddComponent<TextMeshProUGUI>();
            if (uiFont) tmp.font = uiFont;
            tmp.text = text; tmp.fontSize = size; tmp.color = color;
            tmp.alignment = align; tmp.fontStyle = bold ? FontStyles.Bold : FontStyles.Normal;
            tmp.raycastTarget = false; tmp.textWrappingMode = TextWrappingModes.Normal;
            return tmp;
        }

        void SetSlider(Slider s, TextMeshProUGUI v, float val)
        {
            if (s) s.SetValueWithoutNotify(Mathf.Clamp01(val));
            if (v) v.text = Mathf.RoundToInt(Mathf.Clamp01(val) * 10f).ToString();
        }

        static RectTransform GetRT(Transform t) => t.GetComponent<RectTransform>() ?? t.gameObject.AddComponent<RectTransform>();

        static void Tint(Button btn, Color normal, Color highlight)
        {
            var bc = btn.colors;
            bc.normalColor = normal; bc.highlightedColor = highlight;
            bc.pressedColor = new Color(Mathf.Max(normal.r - 0.05f, 0), Mathf.Max(normal.g - 0.05f, 0), Mathf.Max(normal.b - 0.05f, 0), Mathf.Min(normal.a + 0.1f, 1f));
            bc.selectedColor = normal; bc.fadeDuration = 0.1f;
            btn.colors = bc;
        }

        // Sprite loa (silhouette trắng, tint bằng Image.color)
        static Sprite SpeakerSprite()
        {
            if (_spSpeaker) return _spSpeaker;
            int S = 64; var t = new Texture2D(S, S, TextureFormat.RGBA32, false);
            var clear = new Color(0, 0, 0, 0);
            for (int y = 0; y < S; y++) for (int x = 0; x < S; x++) t.SetPixel(x, y, clear);
            for (int y = 24; y < 40; y++) for (int x = 12; x < 26; x++) t.SetPixel(x, y, Color.white); // thân
            for (int x = 26; x < 46; x++) { int half = (x - 20); for (int y = 32 - half; y < 32 + half; y++) if (y >= 0 && y < S) t.SetPixel(x, y, Color.white); } // loe
            t.Apply();
            _spSpeaker = Sprite.Create(t, new Rect(0, 0, S, S), new Vector2(0.5f, 0.5f), 100f);
            return _spSpeaker;
        }
    }
}