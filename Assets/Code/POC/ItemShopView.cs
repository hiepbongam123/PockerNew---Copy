using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using TMPro;
using LoRClone.Data;
using LoRClone.Model;   // CardItemDef helpers cần ItemRarity

namespace LoRClone.View
{
    /// <summary>
    /// MỐC 11 — CỬA HÀNG TRANG BỊ CHAMPION: mua trang bị champion (CardItemLibrary, championEquip) bằng LÕI.
    ///
    /// Mua = MỞ KHOÁ SỞ HỮU (ProgressStore.GrantItem, toàn account). KHÔNG tự lắp —
    /// mua xong vào hub champion bấm + để LẮP như thường (đúng cơ chế "phải lắp").
    /// Item cũng vẫn tự mở theo mastery như cũ (shop là đường mua THÊM/sớm).
    ///
    /// UI tự dựng 100% bằng code khi Open() — không cần setup prefab.
    /// LobbyMenuView tự thêm component + gán campaign + thêm nút "CỬA HÀNG (Lõi)".
    /// </summary>
    public class ItemShopView : MonoBehaviour
    {
        [Header("Data — LobbyMenuView tự gán")]
        public CampaignData campaign;

        [Header("Kích thước")]
        public float titleFontSize = 24f;
        public float headerFontSize = 16f;
        public float rowFontSize = 16f;
        public float rowHeight = 92f;

        [Header("Màu")]
        public Color overlayColor = new Color(0f, 0f, 0f, 0.92f);
        public Color panelColor = new Color(0.075f, 0.095f, 0.135f);
        public Color headerColor = new Color(0.115f, 0.150f, 0.210f);
        public Color areaColor = new Color(0.055f, 0.070f, 0.100f);
        public Color rowColor = new Color(0.125f, 0.165f, 0.230f);
        public Color accentColor = new Color(0.940f, 0.780f, 0.350f);
        public Color coreColor = new Color(0.37f, 0.81f, 0.82f);       // xanh ngọc = Lõi
        public Color textDimColor = new Color(0.600f, 0.660f, 0.740f);
        public Color btnBuy = new Color(0.180f, 0.560f, 0.520f);        // teal mua
        public Color btnDisabled = new Color(0.28f, 0.30f, 0.36f);
        public Color btnRed = new Color(0.770f, 0.240f, 0.240f);
        public Color ownedColor = new Color(0.35f, 0.78f, 0.48f);

        GameObject _overlay;
        RectTransform _listContent;
        TextMeshProUGUI _coresLabel, _msgLabel;

        // ══════════════════════════════════════════════════════════
        public void Open()
        {
            if (_overlay == null) BuildUI();
            _overlay.SetActive(true);
            _overlay.transform.SetAsLastSibling();
            Refresh();
            Msg("");
        }

        public void Close() { if (_overlay != null) _overlay.SetActive(false); }

        void Msg(string s) { if (_msgLabel != null) _msgLabel.text = s; }

        void SyncCores()
        {
            if (_coresLabel != null)
                _coresLabel.text = $"<color=#5FCFD2>⬡ {ProgressStore.Cores}</color>  Lõi Cường Hóa";
        }

        // ── Refresh danh sách ─────────────────────────────────────
        void Refresh()
        {
            SyncCores();
            ClearChildren(_listContent);

            var lib = campaign != null ? campaign.cardItemLibrary : null;
            var items = lib != null ? lib.items : null;
            if (items == null || items.Count == 0)
            {
                SpawnPlaceholder(_listContent, "Chưa có trang bị champion.\nTạo trong CardItemLibrary, bật 'championEquip'.");
                return;
            }

            int shown = 0;
            foreach (var it in items)
            {
                if (it == null || !it.championEquip) continue;   // shop meta chỉ bán trang bị CHAMPION
                MakeItemRow(it);
                shown++;
            }
            if (shown == 0)
                SpawnPlaceholder(_listContent, "Chưa có trang bị champion nào (bật 'championEquip' trong library).");
            LayoutRebuilder.ForceRebuildLayoutImmediate(_listContent);
        }

        void MakeItemRow(CardItemDef it)
        {
            bool owned = ProgressStore.IsItemOwned(it.itemName);
            bool forSale = it.coreCost > 0;
            bool canAfford = ProgressStore.Cores >= it.coreCost;

            var row = MakeRect("Row_" + it.itemName, _listContent, Vector2.zero, Vector2.one);
            row.gameObject.AddComponent<LayoutElement>().preferredHeight = rowHeight;
            SetImage(row, rowColor);

            // Sọc màu slot bên trái
            var stripe = MakeRect("Stripe", row, V2(0f, 0f), V2(0f, 1f));
            stripe.offsetMin = Vector2.zero; stripe.offsetMax = new Vector2(7f, 0f);
            SetImage(stripe, RarityColor(it.rarity)).raycastTarget = false;

            // Icon
            var iconBox = MakeRect("Icon", row, V2(0.015f, 0.16f), V2(0.10f, 0.84f));
            SetImage(iconBox, new Color(0.05f, 0.07f, 0.11f, 1f)).raycastTarget = false;
            if (it.icon != null)
            {
                var ic = MakeRect("Ic", iconBox, V2(0.12f, 0.12f), V2(0.88f, 0.88f));
                var iimg = ic.gameObject.AddComponent<Image>();
                iimg.sprite = it.icon; iimg.preserveAspect = true; iimg.raycastTarget = false;
            }

            // Tên + slot
            SpawnLabel(row, $"<b>{it.itemName}</b>  <size=72%><color=#7FE3E5>{RarityVN(it.rarity)}</color></size>",
                rowFontSize, Color.white, TextAlignmentOptions.TopLeft, false,
                V2(0.115f, 0.55f), V2(0.72f, 0.93f));

            // Chỉ số
            SpawnLabel(row, StatLine(it),
                rowFontSize * 0.82f, new Color(0.86f, 0.90f, 0.98f), TextAlignmentOptions.TopLeft, false,
                V2(0.115f, 0.10f), V2(0.72f, 0.56f)).textWrappingMode = TextWrappingModes.Normal;

            // Chú thích tự-mở-theo-mastery
            if (it.unlockMasteryLevel > 1)
                SpawnLabel(row, $"<size=90%><color=#7C869A>Tự mở ở Cấp {it.unlockMasteryLevel}</color></size>",
                    rowFontSize * 0.72f, textDimColor, TextAlignmentOptions.BottomLeft, false,
                    V2(0.115f, 0.02f), V2(0.72f, 0.14f));

            // Cụm phải: trạng thái mua
            if (owned)
            {
                var tag = MakeRect("Owned", row, V2(0.74f, 0.30f), V2(0.985f, 0.70f));
                SetImage(tag, new Color(0.10f, 0.24f, 0.16f, 0.95f)).raycastTarget = false;
                SpawnLabel(tag, "✔ ĐÃ SỞ HỮU", rowFontSize * 0.9f, ownedColor, TextAlignmentOptions.Center, true);
            }
            else if (!forSale)
            {
                SpawnLabel(row, "<color=#7C869A>Không bán (mở theo mastery)</color>", rowFontSize * 0.82f,
                    textDimColor, TextAlignmentOptions.Center, false, V2(0.74f, 0.30f), V2(0.985f, 0.70f));
            }
            else
            {
                var cap = it;
                var buy = MakeRect("Buy", row, V2(0.74f, 0.24f), V2(0.985f, 0.76f));
                var bimg = SetImage(buy, canAfford ? btnBuy : btnDisabled);
                SpawnLabel(buy, $"MUA\n<color=#BEF2F3>⬡ {it.coreCost} Lõi</color>", rowFontSize * 0.86f,
                    Color.white, TextAlignmentOptions.Center, true);
                if (canAfford)
                {
                    var btn = buy.gameObject.AddComponent<Button>();
                    btn.targetGraphic = bimg;
                    var bc = btn.colors;
                    bc.normalColor = btnBuy;
                    bc.highlightedColor = Color.Lerp(btnBuy, Color.white, 0.2f);
                    bc.pressedColor = Color.Lerp(btnBuy, Color.black, 0.2f);
                    btn.colors = bc;
                    btn.onClick.AddListener(() => TryBuy(cap));
                }
            }
        }

        void TryBuy(CardItemDef it)
        {
            if (it == null || it.coreCost <= 0) return;
            if (ProgressStore.IsItemOwned(it.itemName)) { Msg("Đã sở hữu rồi."); return; }
            if (!ProgressStore.SpendCores(it.coreCost))
            {
                Msg($"Không đủ Lõi — cần {it.coreCost}, đang có {ProgressStore.Cores}.");
                return;
            }
            ProgressStore.GrantItem(it.itemName);
            Msg($"<color=#6FE0A0>Đã mua {it.itemName}!</color> Vào hub champion bấm + để LẮP.");
            Refresh();
        }

        string StatLine(CardItemDef it)
        {
            var parts = new List<string>();
            if (it.atkBonus != 0) parts.Add($"<color=#E08C6E>+{it.atkBonus} ATK</color>");
            if (it.hpBonus != 0) parts.Add($"<color=#6EA8E0>+{it.hpBonus} HP</color>");
            if (it.costReduction > 0) parts.Add($"<color=#C9A6F0>-{it.costReduction} mana</color>");
            if (it.bonusMana != 0) parts.Add($"<color=#6E9CE0>+{it.bonusMana} mana đầu</color>");
            if (it.bonusNexus != 0) parts.Add($"<color=#6EE0A8>+{it.bonusNexus} Nexus</color>");
            if (it.grantsKeyword) parts.Add($"<color=#F0C75A>keyword</color>");
            if (it.skill != null) parts.Add($"<color=#F0C75A>skill</color>");
            return parts.Count > 0 ? string.Join("  ", parts) : "<color=#7C869A>(không chỉ số)</color>";
        }

        // ══════════════════════════════════════════════════════════
        void BuildUI()
        {
            // Canvas RIÊNG (ScreenSpaceOverlay) — mở được từ BẤT KỲ đâu (Bản Đồ Ải, lobby...).
            // sortingOrder cao hơn World Map (32000) để nổi lên trên.
            var canvasGO = new GameObject("ItemShopCanvas");
            var canvas = canvasGO.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 32100;
            canvasGO.AddComponent<GraphicRaycaster>();

            var root = canvasGO.GetComponent<RectTransform>();
            var overlayRT = MakeRect("ItemShopOverlay", root, Vector2.zero, Vector2.one);
            _overlay = overlayRT.gameObject;
            SetImage(overlayRT, overlayColor);

            var panel = MakeRect("Panel", overlayRT, V2(0.10f, 0.06f), V2(0.90f, 0.94f));
            SetImage(panel, panelColor);

            var tbar = MakeRect("TitleBar", panel, V2(0f, 0.90f), V2(1f, 1f));
            SetImage(tbar, headerColor);
            SpawnLabel(tbar, "CỬA HÀNG TRANG BỊ", titleFontSize, accentColor,
                TextAlignmentOptions.Left, true, V2(0.02f, 0f), V2(0.6f, 1f));
            _coresLabel = SpawnLabel(tbar, "", headerFontSize, Color.white,
                TextAlignmentOptions.Right, true, V2(0.55f, 0f), V2(0.94f, 1f));
            MakeButton(MakeRect("CloseHolder", tbar, V2(0.955f, 0.18f), V2(0.995f, 0.82f)),
                "X", btnRed, Vector2.zero, Vector2.one, Close);

            // Dòng hướng dẫn + thông báo
            SpawnLabel(panel, "Mua = mở khoá SỞ HỮU (dùng mọi champion). Mua xong vào hub champion bấm + để LẮP.",
                headerFontSize * 0.82f, textDimColor, TextAlignmentOptions.Center, false,
                V2(0.02f, 0.855f), V2(0.98f, 0.90f));
            _msgLabel = SpawnLabel(panel, "", headerFontSize * 0.9f, accentColor,
                TextAlignmentOptions.Center, false, V2(0.02f, 0.815f), V2(0.98f, 0.855f));

            var area = MakeRect("Area", panel, V2(0.012f, 0.012f), V2(0.988f, 0.81f));
            SetImage(area, areaColor);
            _listContent = BuildScrollArea("ShopScroll", area, Vector2.zero, Vector2.one);

            _overlay.SetActive(false);
        }

        // ── Helpers (self-contained) ──────────────────────────────
        static Color RarityColor(ItemRarity r) =>
            r == ItemRarity.Epic ? new Color(0.62f, 0.42f, 0.80f) :
            r == ItemRarity.Rare ? new Color(0.36f, 0.55f, 0.85f) :
                                   new Color(0.55f, 0.60f, 0.66f);

        static string RarityVN(ItemRarity r) =>
            r == ItemRarity.Epic ? "Sử Thi" : r == ItemRarity.Rare ? "Hiếm" : "Thường";

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
            SpawnLabel(rt, label, rowFontSize, Color.white, TextAlignmentOptions.Center, true);
        }

        RectTransform BuildScrollArea(string name, RectTransform parent, Vector2 ancMin, Vector2 ancMax)
        {
            var scrollRT = MakeRect(name, parent, ancMin, ancMax);
            scrollRT.offsetMin = new Vector2(5f, 5f);
            scrollRT.offsetMax = new Vector2(-5f, -5f);
            var sr = scrollRT.gameObject.AddComponent<ScrollRect>();
            sr.horizontal = false;
            sr.movementType = ScrollRect.MovementType.Clamped;
            sr.scrollSensitivity = 25f;

            var vp = MakeRect("Viewport", scrollRT, Vector2.zero, Vector2.one);
            vp.offsetMax = new Vector2(-16f, 0f);
            vp.gameObject.AddComponent<Image>().color = Color.white;
            var mask = vp.gameObject.AddComponent<Mask>();
            mask.showMaskGraphic = false;

            var content = MakeRect("Content", vp, V2(0f, 1f), V2(1f, 1f));
            content.pivot = V2(0.5f, 1f);
            content.offsetMin = new Vector2(0f, -300f);
            content.offsetMax = Vector2.zero;
            var vlg = content.gameObject.AddComponent<VerticalLayoutGroup>();
            vlg.spacing = 6f;
            vlg.padding = new RectOffset(6, 6, 6, 6);
            vlg.childForceExpandWidth = true;
            vlg.childForceExpandHeight = false;
            content.gameObject.AddComponent<ContentSizeFitter>().verticalFit =
                ContentSizeFitter.FitMode.PreferredSize;

            // Thanh cuộn
            var sbRT = MakeRect("Scrollbar", scrollRT, V2(1f, 0f), V2(1f, 1f));
            sbRT.offsetMin = new Vector2(-16f, 0f);
            sbRT.offsetMax = new Vector2(0f, 0f);
            sbRT.gameObject.AddComponent<Image>().color = new Color(0.08f, 0.10f, 0.16f, 0.9f);
            var scrollbar = sbRT.gameObject.AddComponent<Scrollbar>();
            scrollbar.direction = Scrollbar.Direction.BottomToTop;
            var slideRT = MakeRect("SlidingArea", sbRT, Vector2.zero, Vector2.one);
            slideRT.offsetMin = new Vector2(2f, 2f);
            slideRT.offsetMax = new Vector2(-2f, -2f);
            var handleRT = MakeRect("Handle", slideRT, Vector2.zero, Vector2.one);
            var handleImg = handleRT.gameObject.AddComponent<Image>();
            handleImg.color = new Color(0.45f, 0.52f, 0.66f, 0.98f);
            scrollbar.targetGraphic = handleImg;
            scrollbar.handleRect = handleRT;

            sr.viewport = vp;
            sr.content = content;
            sr.verticalScrollbar = scrollbar;
            sr.verticalScrollbarVisibility = ScrollRect.ScrollbarVisibility.Permanent;
            return content;
        }

        void SpawnPlaceholder(RectTransform parent, string msg)
        {
            var go = new GameObject("Placeholder");
            go.transform.SetParent(parent, false);
            var rt = go.AddComponent<RectTransform>();
            go.AddComponent<LayoutElement>().preferredHeight = 100f;
            SpawnLabel(rt, msg, headerFontSize * 0.9f, textDimColor, TextAlignmentOptions.Center, false);
        }

        static void ClearChildren(RectTransform content)
        {
            if (content == null) return;
            for (int i = content.childCount - 1; i >= 0; i--)
                Object.Destroy(content.GetChild(i).gameObject);
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