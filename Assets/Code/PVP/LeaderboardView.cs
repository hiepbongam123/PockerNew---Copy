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
    /// BẢNG XẾP HẠNG — panel đọc UGS Leaderboards. Tự dựng UI bằng code khi Open().
    /// Hiện: hạng của MÌNH (từ FetchMyRank) + danh sách top (FetchLeaderboard), tô sáng dòng của mình.
    /// Có nút LÀM MỚI và NỘP ĐIỂM TEST (đẩy MMR hiện tại lên bảng để thấy ngay, khỏi cần đánh PvP).
    /// </summary>
    public class LeaderboardView : MonoBehaviour
    {
        public int topCount = 50;

        GameObject _overlay;
        RectTransform _listContent;
        TextMeshProUGUI _myLine, _status;

        // ── Mở / đóng ─────────────────────────────────────────────
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
            ClearList();
            SetStatus("Đang tải bảng xếp hạng...");
            if (UgsAccount.Instance == null || !UgsAccount.Instance.IsSignedIn)
            { SetStatus("Chưa đăng nhập UGS — không tải được bảng."); if (_myLine != null) _myLine.text = ""; return; }

            // Hạng của mình
            UgsAccount.Instance.FetchMyRank(e =>
            {
                if (_myLine == null) return;
                if (e != null)
                    _myLine.text = $"Hạng của bạn: <color=#7FD3FF>#{e.Rank + 1}</color>   " +
                                   $"<color=#7FD3FF>MMR {(int)e.Score}</color>   <color=#9AA7B8>{ProgressStore.RankWins}T/{ProgressStore.RankLosses}B</color>";
                else
                    _myLine.text = $"Hạng của bạn: <color=#9AA7B8>chưa có điểm</color>   MMR {ProgressStore.RankMmr} (bấm NỘP ĐIỂM TEST hoặc đánh 1 trận PvP)";
            });

            // Danh sách top
            UgsAccount.Instance.FetchLeaderboard(list =>
            {
                ClearList();
                if (list == null) { SetStatus("Lỗi tải bảng (thử LÀM MỚI)."); return; }
                if (list.Count == 0) { SetStatus("Chưa có ai trên bảng. Bấm NỘP ĐIỂM TEST để lên bảng."); return; }
                SetStatus("");
                string me = UgsAccount.Instance.PlayerId;
                Debug.Log($"[Leaderboard] Nhận {list.Count} dòng.");
                try { for (int i = 0; i < list.Count; i++) MakeRow(list[i], list[i].PlayerId == me); }
                catch (System.Exception ex) { SetStatus("Lỗi hiện dòng: " + ex.Message); }
                LayoutRebuilder.ForceRebuildLayoutImmediate(_listContent);
            }, topCount);
        }

        void SetStatus(string s) { if (_status != null) _status.text = s; }

        // ── Build UI ──────────────────────────────────────────────
        void BuildUI()
        {
            var parent = GetComponent<RectTransform>();
            var overlayRT = MakeRect("LeaderboardOverlay", parent, Vector2.zero, Vector2.one);
            _overlay = overlayRT.gameObject;
            var cv = _overlay.AddComponent<Canvas>();
            cv.overrideSorting = true; cv.sortingOrder = 610;
            _overlay.AddComponent<GraphicRaycaster>();
            SetImg(overlayRT, new Color(0.02f, 0.03f, 0.05f, 0.86f));

            var panel = MakeRect("Panel", overlayRT, new Vector2(0.28f, 0.06f), new Vector2(0.72f, 0.95f));
            SetImg(panel, new Color(0.08f, 0.10f, 0.15f, 1f));

            var tbar = MakeRect("Tbar", panel, new Vector2(0f, 0.93f), new Vector2(1f, 1f));
            SetImg(tbar, new Color(0.12f, 0.16f, 0.24f, 1f));
            Label(tbar, "BẢNG XẾP HẠNG", 22f, new Color(1f, 0.86f, 0.45f), TextAlignmentOptions.Center, true,
                new Vector2(0.05f, 0f), new Vector2(0.85f, 1f));
            Btn(MakeRect("X", tbar, new Vector2(0.9f, 0.15f), new Vector2(0.98f, 0.85f)),
                "X", new Color(0.5f, 0.14f, 0.14f, 1f), Close);

            _myLine = Label(MakeRect("MyLine", panel, new Vector2(0.04f, 0.855f), new Vector2(0.96f, 0.92f)),
                "", 15f, Color.white, TextAlignmentOptions.MidlineLeft, true);

            var hdr = MakeRect("Hdr", panel, new Vector2(0.04f, 0.80f), new Vector2(0.96f, 0.85f));
            Label(hdr, "#", 13f, new Color(0.6f, 0.68f, 0.8f), TextAlignmentOptions.MidlineLeft, true, new Vector2(0f, 0f), new Vector2(0.11f, 1f));
            Label(hdr, "Người chơi", 13f, new Color(0.6f, 0.68f, 0.8f), TextAlignmentOptions.MidlineLeft, true, new Vector2(0.20f, 0f), new Vector2(0.75f, 1f));
            Label(hdr, "MMR", 13f, new Color(0.6f, 0.68f, 0.8f), TextAlignmentOptions.MidlineRight, true, new Vector2(0.75f, 0f), new Vector2(1f, 1f));

            // Scroll list
            var scrollGO = MakeRect("Scroll", panel, new Vector2(0.04f, 0.14f), new Vector2(0.96f, 0.79f));
            SetImg(scrollGO, new Color(0.05f, 0.07f, 0.11f, 1f));
            var sr = scrollGO.gameObject.AddComponent<ScrollRect>();
            sr.horizontal = false; sr.movementType = ScrollRect.MovementType.Clamped;
            var viewport = MakeRect("Viewport", scrollGO, Vector2.zero, Vector2.one);
            viewport.gameObject.AddComponent<RectMask2D>();
            SetImg(viewport, new Color(0, 0, 0, 0));
            _listContent = MakeRect("Content", viewport, new Vector2(0f, 1f), new Vector2(1f, 1f));
            _listContent.pivot = new Vector2(0.5f, 1f);
            var vlg = _listContent.gameObject.AddComponent<VerticalLayoutGroup>();
            vlg.childControlWidth = true; vlg.childControlHeight = true;   // ★ FIX: false → dòng co cao=0 → vô hình
            vlg.childForceExpandWidth = true; vlg.childForceExpandHeight = false;
            vlg.spacing = 3f; vlg.padding = new RectOffset(6, 6, 6, 6);
            var fitter = _listContent.gameObject.AddComponent<ContentSizeFitter>();
            fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;
            sr.viewport = viewport; sr.content = _listContent;

            _status = Label(MakeRect("Status", panel, new Vector2(0.04f, 0.14f), new Vector2(0.96f, 0.79f)),
                "", 14f, new Color(0.8f, 0.86f, 0.95f), TextAlignmentOptions.Center, false);

            // Nút đáy
            Btn(MakeRect("Refresh", panel, new Vector2(0.04f, 0.035f), new Vector2(0.36f, 0.125f)),
                "LÀM MỚI", new Color(0.12f, 0.31f, 0.52f, 1f), Refresh);
            Btn(MakeRect("TestSubmit", panel, new Vector2(0.38f, 0.035f), new Vector2(0.70f, 0.125f)),
                "NỘP ĐIỂM TEST", new Color(0.14f, 0.34f, 0.20f, 1f), () =>
                {
                    UgsAccount.Instance?.SubmitRank(ProgressStore.RankMmr);
                    SetStatus($"Đã nộp ⚔ {ProgressStore.RankMmr}. Đợi 1–2s rồi bấm LÀM MỚI.");
                });
            Btn(MakeRect("CloseBtn", panel, new Vector2(0.72f, 0.035f), new Vector2(0.96f, 0.125f)),
                "ĐÓNG", new Color(0.5f, 0.14f, 0.14f, 1f), Close);

            _overlay.SetActive(false);
        }

        void MakeRow(LeaderboardEntry e, bool isMe)
        {
            var row = MakeRect("Row", _listContent, Vector2.zero, Vector2.one);
            var le = row.gameObject.AddComponent<LayoutElement>(); le.preferredHeight = 34f; le.minHeight = 34f;
            SetImg(row, isMe ? new Color(0.16f, 0.28f, 0.20f, 1f) : new Color(0.09f, 0.12f, 0.18f, 1f));

            string name = string.IsNullOrEmpty(e.PlayerName) ? ("Người chơi " + Short(e.PlayerId)) : e.PlayerName;
            var nameColor = isMe ? new Color(0.7f, 1f, 0.75f) : Color.white;

            Label(row, "#" + (e.Rank + 1), 14f, new Color(1f, 0.86f, 0.45f), TextAlignmentOptions.MidlineLeft, true, new Vector2(0.02f, 0f), new Vector2(0.11f, 1f));

            // Avatar: của mình = ảnh đã chọn; người khác = ảnh tất định theo id (mỗi người 1 ảnh).
            var sp = isMe ? AvatarStore.Selected : AvatarStore.ForPlayerId(e.PlayerId);
            if (sp != null)
            {
                var avRT = MakeRect("Av", row, new Vector2(0.12f, 0.12f), new Vector2(0.185f, 0.88f));
                var im = avRT.gameObject.AddComponent<Image>(); im.sprite = sp; im.color = Color.white; im.raycastTarget = false;
            }

            Label(row, name + (isMe ? "  <size=80%><color=#7FD3FF>(bạn)</color></size>" : ""), 14f, nameColor, TextAlignmentOptions.MidlineLeft, false, new Vector2(0.20f, 0f), new Vector2(0.75f, 1f));
            Label(row, ((int)e.Score).ToString(), 15f, new Color(0.7f, 0.83f, 0.98f), TextAlignmentOptions.MidlineRight, true, new Vector2(0.75f, 0f), new Vector2(0.97f, 1f));
        }

        static string Short(string id) => string.IsNullOrEmpty(id) ? "?" : id.Substring(0, Mathf.Min(5, id.Length));

        void ClearList()
        {
            if (_listContent == null) return;
            for (int i = _listContent.childCount - 1; i >= 0; i--) Destroy(_listContent.GetChild(i).gameObject);
        }

        // ── Helper dựng UI ─────────────────────────────────────────
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
            var t = rt.gameObject.AddComponent<TextMeshProUGUI>();
            t.text = text; t.fontSize = size; t.color = color; t.alignment = align;
            t.fontStyle = bold ? FontStyles.Bold : FontStyles.Normal;
            t.raycastTarget = false; t.textWrappingMode = TextWrappingModes.NoWrap; t.overflowMode = TextOverflowModes.Ellipsis;
            return t;
        }

        static void Btn(RectTransform rt, string label, Color color, UnityEngine.Events.UnityAction onClick)
        {
            var img = SetImg(rt, color);
            var b = rt.gameObject.AddComponent<Button>(); b.targetGraphic = img;
            b.onClick.AddListener(onClick);
            Label(rt, label, 13f, Color.white, TextAlignmentOptions.Center, true);
        }

    }
}