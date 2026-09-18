using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.Events;
using TMPro;

namespace LoRClone.View
{
    /// <summary>
    /// Nút "Cài Đặt" cho SẢNH CHỜ — style HSR giống PauseMenuView (gameplay), có tab rail.
    /// KHÔNG có 4 nút gameplay (Đầu Hàng/Ra Sảnh/Đấu Lại/Tiếp Tục) vì lobby không có trận đang chạy.
    /// KHÔNG có tab "Thông Tin Trận" / toggle "Tự Động Chiến Đấu" — cả 2 chỉ có ý nghĩa trong trận.
    ///
    /// 2 tab: ÂM THANH (sliders, y hệt PauseMenuView) — KHÁC (Thoát Game + Đặt Lại Tiến Trình).
    ///
    /// SETUP: tạo 1 GameObject trống trong scene Lobby, gắn component này. Tự dựng UI bằng code.
    /// Reset tiến trình: kéo hàm thật (VD LobbyManager.ResetProgress) vào field "On Reset Progress".
    /// </summary>
    public class LobbySettingsMenuView : MonoBehaviour
    {
        [Header("Font (tùy chọn)")]
        public TMP_FontAsset uiFont;

        [Header("Nội dung xác nhận reset")]
        [TextArea]
        public string resetConfirmText =
            "Toàn bộ tiến trình (deck, chiến dịch, phần thưởng...) sẽ bị xóa\nvà KHÔNG THỂ khôi phục. Bạn chắc chắn muốn tiếp tục?";

        [Header("Sự kiện — gắn hàm reset tiến trình thật ở đây")]
        public UnityEvent onResetProgress;

        // ── Palette (đồng bộ PauseMenuView) ──
        public Color dimColor = new Color(0.035f, 0.045f, 0.065f, 0.985f);
        public Color goldColor = new Color(0.83f, 0.69f, 0.44f, 1f);
        public Color titleColor = new Color(0.98f, 0.98f, 0.96f, 1f);
        public Color sectionColor = new Color(0.98f, 0.98f, 0.95f, 1f);
        public Color dividerColor = new Color(1f, 1f, 1f, 0.22f);
        public Color rowColor = new Color(0.91f, 0.91f, 0.88f, 1f);
        public Color rowText = new Color(0.09f, 0.09f, 0.10f, 1f);
        public Color accent = new Color(0.97f, 0.63f, 0.16f, 1f);
        public Color trackColor = new Color(0.45f, 0.45f, 0.43f, 0.75f);
        public Color iconActive = new Color(1f, 1f, 1f, 0.98f);
        public Color iconIdle = new Color(1f, 1f, 1f, 0.5f);
        public Color circleActive = new Color(1f, 1f, 1f, 0.14f);
        public Color circleIdle = new Color(1f, 1f, 1f, 0.05f);
        public Color railLine = new Color(1f, 1f, 1f, 0.14f);
        public Color btnBorder = new Color(0.88f, 0.88f, 0.84f, 0.7f);
        public Color btnFill = new Color(0.12f, 0.13f, 0.16f, 0.9f);
        public Color btnFillHover = new Color(0.97f, 0.63f, 0.16f, 0.85f);
        public Color btnText = new Color(0.98f, 0.98f, 0.96f, 1f);
        public Color dangerBorder = new Color(0.82f, 0.42f, 0.40f, 0.75f);
        public Color dangerFill = new Color(0.36f, 0.10f, 0.10f, 0.92f);
        public Color dangerFillHover = new Color(0.76f, 0.20f, 0.18f, 0.95f);

        readonly string[] _tabBig = { "Âm Thanh", "Tiến Trình" };

        Canvas _canvas;
        GameObject _panel, _confirmPanel;
        GameObject[] _contents;
        TextMeshProUGUI _tabNameText;
        Image[] _tabCircles;
        List<Image>[] _tabIcons;
        Slider _sMaster, _sMusic, _sSfx, _sVoice;
        TextMeshProUGUI _vMaster, _vMusic, _vSfx, _vVoice;

        static Sprite _spRound, _spCircle, _spSpeaker;
        static Sprite RoundSp { get { if (!_spRound) _spRound = UISprites.RoundedRect(); return _spRound; } }
        static Sprite CircleSp { get { if (!_spCircle) _spCircle = UISprites.Circle(); return _spCircle; } }

        void Start()
        {
            EnsureEventSystem();

            var cgo = new GameObject("LobbySettingsCanvas", typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
            _canvas = cgo.GetComponent<Canvas>();
            _canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            _canvas.sortingOrder = 900;
            var scaler = cgo.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920f, 1080f);
            scaler.matchWidthOrHeight = 0.5f;

            BuildOpenButton();
            BuildPanel();
            BuildConfirmPanel();
            _panel.SetActive(false);
            _confirmPanel.SetActive(false);
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
            var rt = Rect("SettingsOpenBtn", _canvas.transform, new Vector2(0.945f, 0.925f), new Vector2(0.99f, 0.985f));
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
            var dim = Rect("SettingsPanel", _canvas.transform, Vector2.zero, Vector2.one);
            var dimImg = dim.gameObject.AddComponent<Image>(); dimImg.color = dimColor; dimImg.raycastTarget = true;
            _panel = dim.gameObject;

            BuildHeader(dim);
            BuildTabRail(dim);

            var area = Rect("Area", dim, new Vector2(0.135f, 0.20f), new Vector2(0.955f, 0.80f));
            _contents = new GameObject[2];
            _contents[0] = BuildAudioTab(area);
            _contents[1] = BuildResetTab(area);

            BuildBottomButtons(dim);
        }

        // Hàng nút cố định dưới đáy panel — LUÔN hiện bất kể đang ở tab nào,
        // giống 4 nút bottom-row của PauseMenuView. Thoát Game chỉ cần 1 chạm.
        void BuildBottomButtons(RectTransform dim)
        {
            MainButton(dim, new Vector2(0.135f, 0.055f), new Vector2(0.335f, 0.115f), "Thoát Game", QuitGame,
                       dangerBorder, dangerFill, dangerFillHover, new Color(1f, 0.90f, 0.88f, 1f));
        }

        void BuildHeader(RectTransform dim)
        {
            var em = Rect("Emblem", dim, new Vector2(0.055f, 0.905f), new Vector2(0.083f, 0.965f));
            var eo = em.gameObject.AddComponent<Image>(); eo.color = goldColor; eo.raycastTarget = false;
            em.localRotation = Quaternion.Euler(0, 0, 45f);
            var ei = Rect("EmblemInner", em, new Vector2(0.22f, 0.22f), new Vector2(0.78f, 0.78f));
            var eii = ei.gameObject.AddComponent<Image>(); eii.color = dimColor; eii.raycastTarget = false;

            var sub = Label(Rect("Sub", dim, new Vector2(0.093f, 0.945f), new Vector2(0.55f, 0.985f)),
                            "CÀI ĐẶT SẢNH CHỜ", 20f, goldColor, TextAlignmentOptions.Left, false);
            sub.characterSpacing = 6f;

            _tabNameText = Label(Rect("TabName", dim, new Vector2(0.092f, 0.885f), new Vector2(0.6f, 0.95f)),
                                 _tabBig[0], 42f, titleColor, TextAlignmentOptions.Left, true);

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
            var line = Rect("RailLine", dim, new Vector2(0.069f, 0.40f), new Vector2(0.069f, 0.78f));
            line.sizeDelta = new Vector2(2f, 0f);
            var li = line.gameObject.AddComponent<Image>(); li.color = railLine; li.raycastTarget = false;

            _tabCircles = new Image[2];
            _tabIcons = new List<Image>[2];
            for (int i = 0; i < 2; i++)
            {
                float cy = 0.735f - i * 0.115f;
                var c = Rect($"Tab{i}", dim, new Vector2(0.069f, cy), new Vector2(0.069f, cy));
                c.sizeDelta = new Vector2(74f, 74f);
                var ci = c.gameObject.AddComponent<Image>(); ci.sprite = CircleSp; ci.type = Image.Type.Simple; ci.color = circleIdle;
                var btn = c.gameObject.AddComponent<Button>(); btn.targetGraphic = ci;
                Tint(btn, circleIdle, circleActive);
                _tabCircles[i] = ci;

                _tabIcons[i] = new List<Image>();
                if (i == 0) IconEqualizer(c, _tabIcons[i]);
                else IconLayers(c, _tabIcons[i]);

                int idx = i;
                btn.onClick.AddListener(() => ShowTab(idx));
            }
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

        // ════════════════ TAB: ÂM THANH (y hệt PauseMenuView) ════════════════
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

        static float Vol(Func<AudioManager, float> get, float fallback)
            => AudioManager.Instance != null ? get(AudioManager.Instance) : fallback;

        void RefreshAudio()
        {
            SetSlider(_sMaster, _vMaster, AudioListener.volume);
            SetSlider(_sMusic, _vMusic, Vol(a => a.musicVolume, 0.5f));
            SetSlider(_sSfx, _vSfx, Vol(a => a.MasterSfxVolume, 1f));
            SetSlider(_sVoice, _vVoice, Vol(a => a.cardVolume, 1f));
        }

        void SetSlider(Slider s, TextMeshProUGUI v, float val)
        {
            if (s) s.SetValueWithoutNotify(Mathf.Clamp01(val));
            if (v) v.text = Mathf.RoundToInt(Mathf.Clamp01(val) * 10f).ToString();
        }

        // ════════════════ TAB: TIẾN TRÌNH (chỉ Đặt Lại Tiến Trình) ════════════════
        GameObject BuildResetTab(RectTransform area)
        {
            var c = Rect("ResetTab", area, Vector2.zero, Vector2.one).gameObject;
            var rt = GetRT(c.transform);
            float cur = 0f;
            SectionHeader(rt, ref cur, "Dữ Liệu Đã Lưu");

            var row = StackTop(rt, ref cur, 82f, 0f, 0f, 8f);
            var img = row.gameObject.AddComponent<Image>();
            img.sprite = RoundSp; img.type = Image.Type.Sliced; img.pixelsPerUnitMultiplier = 3f; img.color = rowColor;
            Label(Rect("L", row, new Vector2(0.03f, 0f), new Vector2(0.62f, 1f)),
                  "Xóa toàn bộ tiến trình đã lưu (deck, chiến dịch...)", 22f, rowText, TextAlignmentOptions.Left, false);

            var brt = Rect("Btn", row, new Vector2(0.70f, 0.16f), new Vector2(0.97f, 0.84f));
            var border = brt.gameObject.AddComponent<Image>();
            border.sprite = RoundSp; border.type = Image.Type.Sliced; border.pixelsPerUnitMultiplier = 2f; border.color = dangerBorder;
            var fillRt = Rect("Fill", brt, Vector2.zero, Vector2.one);
            fillRt.offsetMin = new Vector2(2, 2); fillRt.offsetMax = new Vector2(-2, -2);
            var fill = fillRt.gameObject.AddComponent<Image>();
            fill.sprite = RoundSp; fill.type = Image.Type.Sliced; fill.pixelsPerUnitMultiplier = 2f; fill.color = dangerFill;
            var btn = brt.gameObject.AddComponent<Button>(); btn.targetGraphic = fill;
            Tint(btn, fill.color, dangerFillHover);
            Label(brt, "Đặt Lại", 22f, new Color(1f, 0.90f, 0.88f, 1f), TextAlignmentOptions.Center, false);
            btn.onClick.AddListener(OpenConfirmReset);
            return c;
        }

        void QuitGame()
        {
#if UNITY_EDITOR
            UnityEditor.EditorApplication.isPlaying = false;
#else
            Application.Quit();
#endif
        }

        // ════════════════ POPUP XÁC NHẬN RESET ════════════════
        void BuildConfirmPanel()
        {
            var dim = Rect("ResetConfirm", _canvas.transform, Vector2.zero, Vector2.one);
            var di = dim.gameObject.AddComponent<Image>(); di.color = new Color(0.02f, 0.03f, 0.05f, 0.93f); di.raycastTarget = true;
            _confirmPanel = dim.gameObject;

            var card = Rect("Card", dim, new Vector2(0.32f, 0.34f), new Vector2(0.68f, 0.66f));
            var ci = card.gameObject.AddComponent<Image>();
            ci.sprite = RoundSp; ci.type = Image.Type.Sliced; ci.color = new Color(0.06f, 0.07f, 0.10f, 1f);

            var top = Rect("TopLine", card, new Vector2(0.08f, 0.86f), new Vector2(0.92f, 0.875f));
            top.gameObject.AddComponent<Image>().color = dangerBorder;

            Label(Rect("T", card, new Vector2(0.05f, 0.62f), new Vector2(0.95f, 0.86f)),
                  "ĐẶT LẠI TIẾN TRÌNH?", 34f, dangerBorder, TextAlignmentOptions.Center, true);
            Label(Rect("S", card, new Vector2(0.08f, 0.30f), new Vector2(0.92f, 0.62f)),
                  resetConfirmText, 20f, new Color(1, 1, 1, 0.85f), TextAlignmentOptions.Center, false);

            ActionRow2(card, new Vector2(0.10f, 0.12f), new Vector2(0.48f, 0.26f), "Hủy", CloseConfirmReset, false);
            ActionRow2(card, new Vector2(0.52f, 0.12f), new Vector2(0.90f, 0.26f), "Xác Nhận", ConfirmReset, true);

            _confirmPanel.SetActive(false);
        }

        void MainButton(RectTransform parent, Vector2 amin, Vector2 amax, string text, Action onClick,
                        Color border, Color fill, Color fillHover, Color textColor)
        {
            var rt = Rect(text, parent, amin, amax);
            var borderImg = rt.gameObject.AddComponent<Image>();
            borderImg.sprite = RoundSp; borderImg.type = Image.Type.Sliced; borderImg.pixelsPerUnitMultiplier = 2f; borderImg.color = border;
            var fillRt = Rect("Fill", rt, Vector2.zero, Vector2.one);
            fillRt.offsetMin = new Vector2(2, 2); fillRt.offsetMax = new Vector2(-2, -2);
            var fillImg = fillRt.gameObject.AddComponent<Image>();
            fillImg.sprite = RoundSp; fillImg.type = Image.Type.Sliced; fillImg.pixelsPerUnitMultiplier = 2f; fillImg.color = fill;
            var btn = rt.gameObject.AddComponent<Button>(); btn.targetGraphic = fillImg;
            Tint(btn, fill, fillHover);
            Label(rt, text, 24f, textColor, TextAlignmentOptions.Center, false);
            btn.onClick.AddListener(() => onClick());
        }

        void ActionRow2(RectTransform parent, Vector2 amin, Vector2 amax, string text, Action onClick, bool danger)
        {
            var rt = Rect(text, parent, amin, amax);
            var border = rt.gameObject.AddComponent<Image>();
            border.sprite = RoundSp; border.type = Image.Type.Sliced; border.pixelsPerUnitMultiplier = 2f;
            border.color = danger ? dangerBorder : btnBorder;
            var fillRt = Rect("Fill", rt, Vector2.zero, Vector2.one);
            fillRt.offsetMin = new Vector2(2, 2); fillRt.offsetMax = new Vector2(-2, -2);
            var fill = fillRt.gameObject.AddComponent<Image>();
            fill.sprite = RoundSp; fill.type = Image.Type.Sliced; fill.pixelsPerUnitMultiplier = 2f;
            fill.color = danger ? dangerFill : btnFill;
            var btn = rt.gameObject.AddComponent<Button>(); btn.targetGraphic = fill;
            Tint(btn, fill.color, danger ? dangerFillHover : btnFillHover);
            Label(rt, text, 24f, danger ? new Color(1f, 0.90f, 0.88f, 1f) : btnText, TextAlignmentOptions.Center, false);
            btn.onClick.AddListener(() => onClick());
        }

        void OpenConfirmReset()
        {
            _confirmPanel.SetActive(true);
            _confirmPanel.transform.SetAsLastSibling();
        }
        void CloseConfirmReset() => _confirmPanel.SetActive(false);

        void ConfirmReset()
        {
            onResetProgress?.Invoke(); // TODO: gắn logic reset thật (deck/campaign/save) qua Inspector
            CloseConfirmReset();
            Close();
        }

        // ════════════════ MỞ / ĐÓNG / TAB ════════════════
        public void Open()
        {
            _panel.SetActive(true);
            _panel.transform.SetAsLastSibling();
            RefreshAudio();
            ShowTab(0);
        }

        public void Close()
        {
            _panel.SetActive(false);
            _confirmPanel.SetActive(false);
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

        // ════════════════ BUILDERS DÙNG CHUNG ════════════════
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
            var rowImg = row.gameObject.AddComponent<Image>();
            rowImg.sprite = RoundSp; rowImg.type = Image.Type.Sliced; rowImg.pixelsPerUnitMultiplier = 3f; rowImg.color = rowColor;
            Label(Rect("L", row, new Vector2(0.03f, 0f), new Vector2(0.5f, 1f)), label, 24f, rowText, TextAlignmentOptions.Left, false);

            var sp = Rect("Spk", row, new Vector2(0.60f, 0.30f), new Vector2(0.635f, 0.70f));
            var spi = sp.gameObject.AddComponent<Image>(); spi.sprite = SpeakerSprite(); spi.color = rowText; spi.raycastTarget = false; spi.preserveAspect = true;

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

            valText = Label(Rect("V", row, new Vector2(0.92f, 0f), new Vector2(0.99f, 1f)),
                            Mathf.RoundToInt(value01 * 10f).ToString(), 24f, rowText, TextAlignmentOptions.Center, true);
            var vt = valText;
            slider.onValueChanged.AddListener(v => vt.text = Mathf.RoundToInt(v * 10f).ToString());
        }

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

        static RectTransform GetRT(Transform t) => t.GetComponent<RectTransform>() ?? t.gameObject.AddComponent<RectTransform>();

        static void Tint(Button btn, Color normal, Color highlight)
        {
            var bc = btn.colors;
            bc.normalColor = normal; bc.highlightedColor = highlight;
            bc.pressedColor = new Color(Mathf.Max(normal.r - 0.05f, 0), Mathf.Max(normal.g - 0.05f, 0), Mathf.Max(normal.b - 0.05f, 0), Mathf.Min(normal.a + 0.1f, 1f));
            bc.selectedColor = normal; bc.fadeDuration = 0.1f;
            btn.colors = bc;
        }

        static Sprite SpeakerSprite()
        {
            if (_spSpeaker) return _spSpeaker;
            int S = 64; var t = new Texture2D(S, S, TextureFormat.RGBA32, false);
            var clear = new Color(0, 0, 0, 0);
            for (int y = 0; y < S; y++) for (int x = 0; x < S; x++) t.SetPixel(x, y, clear);
            for (int y = 24; y < 40; y++) for (int x = 12; x < 26; x++) t.SetPixel(x, y, Color.white);
            for (int x = 26; x < 46; x++) { int half = (x - 20); for (int y = 32 - half; y < 32 + half; y++) if (y >= 0 && y < S) t.SetPixel(x, y, Color.white); }
            t.Apply();
            _spSpeaker = Sprite.Create(t, new Rect(0, 0, S, S), new Vector2(0.5f, 0.5f), 100f);
            return _spSpeaker;
        }
    }
}