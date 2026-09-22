using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using TMPro;
using Unity.Services.Leaderboards.Models;
using LoRClone.Net;
using LoRClone.Data;

namespace LoRClone.View
{
    /// <summary>
    /// HỒ SƠ — mở từ logo H ở sảnh. Đặt/đổi TÊN + chọn AVATAR (ảnh mặc định), xem RANK hiện tại,
    /// và chỗ cho LỊCH SỬ ĐẤU (sắp có). Tự dựng UI bằng code khi Open().
    /// </summary>
    public class AccountView : MonoBehaviour
    {
        GameObject _overlay;
        TMP_InputField _nameInput;
        TextMeshProUGUI _rankLine, _idLine, _status;
        Image _bigAvatar;
        readonly List<(Image ring, int idx)> _avCells = new List<(Image, int)>();

        public void Open()
        {
            if (_overlay == null) BuildUI();
            _overlay.SetActive(true);
            _overlay.transform.SetAsLastSibling();
            Refresh();
        }
        public void Close() { if (_overlay != null) _overlay.SetActive(false); }

        void Refresh()
        {
            RefreshAvatar();
            var v = UgsAccount.Instance;
            if (v == null || !v.IsSignedIn)
            {
                SetStatus("Chưa đăng nhập UGS.");
                if (_rankLine != null) _rankLine.text = "";
                if (_idLine != null) _idLine.text = "";
                return;
            }
            SetStatus("");
            if (_nameInput != null && string.IsNullOrEmpty(_nameInput.text))
                _nameInput.text = v.PlayerNameBase;
            if (_idLine != null) _idLine.text = "ID: " + (v.PlayerId ?? "?");

            if (_rankLine != null)
                _rankLine.text = RankText("đang tải…");
            v.FetchMyRank(e =>
            {
                if (_rankLine == null) return;
                _rankLine.text = RankText(e != null ? ("#" + (e.Rank + 1)) : "chưa có");
            });
        }
        string RankText(string hang) =>
            $"MMR <color=#7FD3FF>{ProgressStore.RankMmr}</color>   " +
            $"<color=#9AA7B8>{ProgressStore.RankWins}T / {ProgressStore.RankLosses}B</color>   " +
            $"Hạng <color=#F6DD98>{hang}</color>";

        void SetStatus(string s) { if (_status != null) _status.text = s; }

        void RefreshAvatar()
        {
            if (_bigAvatar != null)
            {
                var s = AvatarStore.Selected;
                if (s != null) { _bigAvatar.sprite = s; _bigAvatar.color = Color.white; }
            }
            foreach (var (ring, idx) in _avCells)
                if (ring != null) ring.color = (idx == AvatarStore.SelectedIndex)
                    ? new Color(1f, 0.86f, 0.45f, 1f) : new Color(1, 1, 1, 0.12f);
        }

        void BuildUI()
        {
            var parent = GetComponent<RectTransform>();
            var overlayRT = MakeRect("AccountOverlay", parent, Vector2.zero, Vector2.one);
            _overlay = overlayRT.gameObject;
            var cv = _overlay.AddComponent<Canvas>();
            cv.overrideSorting = true; cv.sortingOrder = 620;
            _overlay.AddComponent<GraphicRaycaster>();
            SetImg(overlayRT, new Color(0.02f, 0.03f, 0.05f, 0.86f));
            var bg = _overlay.AddComponent<Button>(); bg.transition = Selectable.Transition.None;
            bg.onClick.AddListener(Close);

            var panel = MakeRect("Panel", overlayRT, new Vector2(0.30f, 0.12f), new Vector2(0.70f, 0.88f));
            SetImg(panel, new Color(0.08f, 0.10f, 0.15f, 1f));
            var block = panel.gameObject.AddComponent<Button>(); block.transition = Selectable.Transition.None;

            var tbar = MakeRect("Tbar", panel, new Vector2(0f, 0.92f), new Vector2(1f, 1f));
            SetImg(tbar, new Color(0.12f, 0.16f, 0.24f, 1f));
            Label(tbar, "HỒ SƠ", 22f, new Color(1f, 0.86f, 0.45f), TextAlignmentOptions.Center, true,
                new Vector2(0.05f, 0f), new Vector2(0.85f, 1f));
            Btn(MakeRect("X", tbar, new Vector2(0.9f, 0.2f), new Vector2(0.98f, 0.8f)),
                "X", new Color(0.5f, 0.14f, 0.14f, 1f), Close);

            // Avatar lớn (trái) + Tên (phải)
            var avBox = MakeRect("BigAv", panel, new Vector2(0.05f, 0.78f), new Vector2(0.19f, 0.90f));
            _bigAvatar = SetImg(avBox, new Color(0.16f, 0.19f, 0.28f, 1f));
            var s0 = AvatarStore.Selected; if (s0 != null) { _bigAvatar.sprite = s0; _bigAvatar.color = Color.white; }

            Label(MakeRect("NameL", panel, new Vector2(0.22f, 0.855f), new Vector2(0.95f, 0.90f)),
                "Tên hiển thị:", 14f, new Color(0.7f, 0.78f, 0.9f), TextAlignmentOptions.MidlineLeft, true);
            _nameInput = MakeInput(MakeRect("NameIn", panel, new Vector2(0.22f, 0.795f), new Vector2(0.72f, 0.85f)),
                "tên (không dấu, không cách)");
            Btn(MakeRect("Save", panel, new Vector2(0.74f, 0.795f), new Vector2(0.95f, 0.85f)),
                "LƯU TÊN", new Color(0.14f, 0.34f, 0.20f, 1f), OnSaveName);

            // Hàng chọn avatar
            Label(MakeRect("AvL", panel, new Vector2(0.05f, 0.715f), new Vector2(0.95f, 0.76f)),
                "Ảnh đại diện:", 14f, new Color(0.7f, 0.78f, 0.9f), TextAlignmentOptions.MidlineLeft, true);
            BuildAvatarPicker(panel, new Vector2(0.05f, 0.60f), new Vector2(0.95f, 0.71f));

            Label(MakeRect("NameHint", panel, new Vector2(0.05f, 0.555f), new Vector2(0.95f, 0.595f)),
                "UGS chỉ nhận chữ/số/._- , tự thêm mã #1234 để phân biệt trùng tên.",
                11.5f, new Color(0.55f, 0.62f, 0.74f), TextAlignmentOptions.MidlineLeft, false);

            // Rank
            var rankBox = MakeRect("RankBox", panel, new Vector2(0.05f, 0.46f), new Vector2(0.95f, 0.55f));
            SetImg(rankBox, new Color(0.06f, 0.09f, 0.14f, 1f));
            _rankLine = Label(rankBox, "", 16f, Color.white, TextAlignmentOptions.Center, true);

            // Lịch sử (placeholder)
            var histBox = MakeRect("HistBox", panel, new Vector2(0.05f, 0.16f), new Vector2(0.95f, 0.43f));
            SetImg(histBox, new Color(0.05f, 0.07f, 0.11f, 1f));
            Label(MakeRect("HistT", histBox, new Vector2(0.04f, 0.82f), new Vector2(0.96f, 0.98f)),
                "LỊCH SỬ ĐẤU", 14f, new Color(0.6f, 0.68f, 0.8f), TextAlignmentOptions.MidlineLeft, true);
            Label(MakeRect("HistP", histBox, new Vector2(0.04f, 0.1f), new Vector2(0.96f, 0.78f)),
                "(sắp có — lưu lại các trận PvP gần đây)", 13f, new Color(0.5f, 0.57f, 0.68f),
                TextAlignmentOptions.Center, false);

            _idLine = Label(MakeRect("IdLine", panel, new Vector2(0.05f, 0.105f), new Vector2(0.95f, 0.15f)),
                "", 11f, new Color(0.45f, 0.5f, 0.6f), TextAlignmentOptions.MidlineLeft, false);
            _status = Label(MakeRect("Status", panel, new Vector2(0.05f, 0.03f), new Vector2(0.95f, 0.10f)),
                "", 13f, new Color(0.8f, 0.86f, 0.95f), TextAlignmentOptions.Center, false);

            _overlay.SetActive(false);
        }

        void BuildAvatarPicker(RectTransform panel, Vector2 min, Vector2 max)
        {
            _avCells.Clear();
            var strip = MakeRect("AvStrip", panel, min, max);
            var choices = AvatarStore.Choices;
            if (choices == null || choices.Length == 0)
            {
                Label(strip, "(chưa gán ảnh — kéo Sprite vào mục 'avatars' của LobbyMenuView)",
                    12f, new Color(0.8f, 0.6f, 0.5f), TextAlignmentOptions.MidlineLeft, false);
                return;
            }
            int n = Mathf.Min(choices.Length, 10);   // hiển thị tối đa 10 ô cho gọn
            float gap = 0.012f;
            float w = (1f - gap * (n - 1)) / n;
            for (int i = 0; i < n; i++)
            {
                int idx = i;
                float x0 = i * (w + gap);
                var cell = MakeRect("Av" + i, strip, new Vector2(x0, 0f), new Vector2(x0 + w, 1f));
                var ring = SetImg(cell, new Color(1, 1, 1, 0.12f));   // viền/nền chọn
                var inner = MakeRect("in", cell, Vector2.zero, Vector2.one);
                inner.offsetMin = new Vector2(3, 3); inner.offsetMax = new Vector2(-3, -3);
                var img = inner.gameObject.AddComponent<Image>(); img.sprite = choices[i]; img.color = Color.white;
                img.raycastTarget = false;
                var btn = cell.gameObject.AddComponent<Button>(); btn.targetGraphic = ring;
                btn.onClick.AddListener(() => { AvatarStore.SelectedIndex = idx; RefreshAvatar(); });
                _avCells.Add((ring, idx));
            }
        }

        void OnSaveName()
        {
            var v = UgsAccount.Instance;
            if (v == null || !v.IsSignedIn) { SetStatus("Chưa đăng nhập UGS."); return; }
            if (_nameInput == null || string.IsNullOrWhiteSpace(_nameInput.text)) { SetStatus("Nhập tên đã."); return; }
            v.SetPlayerName(_nameInput.text);
            SetStatus("Đã lưu tên. Mở lại Bảng Xếp Hạng để thấy tên mới.");
        }

        // ── Helper dựng UI ──
        static RectTransform MakeRect(string name, Transform parent, Vector2 anchMin, Vector2 anchMax)
        {
            var go = new GameObject(name, typeof(RectTransform));
            var rt = (RectTransform)go.transform;
            rt.SetParent(parent, false);
            rt.anchorMin = anchMin; rt.anchorMax = anchMax;
            rt.offsetMin = Vector2.zero; rt.offsetMax = Vector2.zero;
            return rt;
        }
        static Image SetImg(RectTransform rt, Color c) { var img = rt.gameObject.AddComponent<Image>(); img.color = c; return img; }

        static TextMeshProUGUI Label(RectTransform parent, string text, float size, Color color,
            TextAlignmentOptions align, bool bold, Vector2? min = null, Vector2? max = null)
        {
            var rt = MakeRect("Lbl", parent, min ?? Vector2.zero, max ?? Vector2.one);
            rt.offsetMin = new Vector2(6, 2); rt.offsetMax = new Vector2(-6, -2);
            var t = rt.gameObject.AddComponent<TextMeshProUGUI>();
            t.text = text; t.fontSize = size; t.color = color; t.alignment = align;
            t.fontStyle = bold ? FontStyles.Bold : FontStyles.Normal;
            t.raycastTarget = false; t.textWrappingMode = TextWrappingModes.Normal; t.overflowMode = TextOverflowModes.Ellipsis;
            return t;
        }

        static void Btn(RectTransform rt, string label, Color color, UnityEngine.Events.UnityAction onClick)
        {
            var img = SetImg(rt, color);
            var b = rt.gameObject.AddComponent<Button>(); b.targetGraphic = img;
            b.onClick.AddListener(onClick);
            Label(rt, label, 13f, Color.white, TextAlignmentOptions.Center, true);
        }

        static TMP_InputField MakeInput(RectTransform rt, string placeholder)
        {
            SetImg(rt, new Color(0.12f, 0.15f, 0.22f, 1f));
            var field = rt.gameObject.AddComponent<TMP_InputField>();
            var textRT = MakeRect("Text", rt, Vector2.zero, Vector2.one);
            textRT.offsetMin = new Vector2(8, 2); textRT.offsetMax = new Vector2(-8, -2);
            var txt = textRT.gameObject.AddComponent<TextMeshProUGUI>();
            txt.fontSize = 15f; txt.color = Color.white; txt.alignment = TextAlignmentOptions.MidlineLeft;
            txt.richText = false; txt.textWrappingMode = TextWrappingModes.NoWrap;
            var phRT = MakeRect("Placeholder", rt, Vector2.zero, Vector2.one);
            phRT.offsetMin = new Vector2(8, 2); phRT.offsetMax = new Vector2(-8, -2);
            var ph = phRT.gameObject.AddComponent<TextMeshProUGUI>();
            ph.fontSize = 14f; ph.color = new Color(1, 1, 1, 0.4f);
            ph.alignment = TextAlignmentOptions.MidlineLeft; ph.text = placeholder;
            ph.textWrappingMode = TextWrappingModes.NoWrap;
            field.textViewport = rt; field.textComponent = txt; field.placeholder = ph;
            field.targetGraphic = rt.GetComponent<Image>();
            field.characterLimit = 20; field.lineType = TMP_InputField.LineType.SingleLine;
            return field;
        }
    }
}