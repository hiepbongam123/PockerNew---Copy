using System;
using System.Text;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.EventSystems;
using TMPro;
using LoRClone.Model;
using LoRClone.Data;
using LoRClone.Controller;
using LoRClone; // AudioManager

namespace LoRClone.View
{
    /// <summary>
    /// CardView — phần Panels: artwork viewer toàn màn hình, dải thumbnail lá liên quan, buff history panel.
    /// Partial class: tách từ CardView.cs gốc, KHÔNG đổi logic.
    /// Field khai báo ở CardView.cs (file chính) — mọi partial dùng chung.
    /// </summary>
    public partial class CardView
    {
        // ── Artwork Viewer ────────────────────────────────────────
        void ShowViewArtButton()
        {
            if (model?.data?.artwork == null) return;
            if (_viewArtBtn != null) return;
            _viewArtBtn = new GameObject("_ViewArtBtn");
            _viewArtBtn.transform.SetParent(transform, false);
            var rt = _viewArtBtn.AddComponent<RectTransform>();
            rt.anchorMin = new Vector2(1f, 1f); rt.anchorMax = new Vector2(1f, 1f);
            rt.pivot = new Vector2(1f, 1f);
            rt.anchoredPosition = new Vector2(-6f, -6f);
            rt.sizeDelta = new Vector2(52f, 20f);
            var bg = _viewArtBtn.AddComponent<Image>();
            bg.color = new Color(0.05f, 0.05f, 0.05f, 0.82f);
            if (viewArtButtonSprite != null) { bg.sprite = viewArtButtonSprite; bg.type = Image.Type.Sliced; }
            var btn = _viewArtBtn.AddComponent<Button>();
            btn.targetGraphic = bg;
            var cb = btn.colors;
            cb.highlightedColor = new Color(0.3f, 0.3f, 0.3f, 1f);
            cb.pressedColor = new Color(0.15f, 0.15f, 0.15f, 1f);
            btn.colors = cb;
            btn.onClick.AddListener(OpenArtworkViewer);
            var labelGO = new GameObject("Label");
            labelGO.transform.SetParent(_viewArtBtn.transform, false);
            var labelRT = labelGO.AddComponent<RectTransform>();
            labelRT.anchorMin = Vector2.zero; labelRT.anchorMax = Vector2.one;
            labelRT.offsetMin = new Vector2(3f, 1f); labelRT.offsetMax = new Vector2(-3f, -1f);
            var tmp = labelGO.AddComponent<TextMeshProUGUI>();
            tmp.text = string.IsNullOrWhiteSpace(viewArtButtonLabel) ? "Xem anh" : viewArtButtonLabel;
            tmp.fontSize = 7.5f; tmp.alignment = TextAlignmentOptions.Center;
            tmp.color = Color.white; tmp.raycastTarget = false;
            _viewArtBtn.transform.SetAsLastSibling();
        }

        void HideViewArtButton()
        {
            if (_viewArtBtn == null) return;
            Destroy(_viewArtBtn); _viewArtBtn = null;
        }

        void OpenArtworkViewer()
        {
            if (_artViewerOpen) return;
            if (model?.data?.artwork == null) return;
            _artViewerOpen = true;
            var tex = model.data.artwork;
            _artViewerOverlay = new GameObject("_ArtViewer");
            var parent = _rootCanvas != null ? _rootCanvas.transform : transform;
            _artViewerOverlay.transform.SetParent(parent, false);
            _artViewerOverlay.transform.SetAsLastSibling();
            var ovCanvas = _artViewerOverlay.AddComponent<Canvas>();
            ovCanvas.overrideSorting = true; ovCanvas.sortingOrder = inspectSortingBase + 300;
            _artViewerOverlay.AddComponent<GraphicRaycaster>();
            var ovRT = _artViewerOverlay.GetComponent<RectTransform>();
            ovRT.anchorMin = Vector2.zero; ovRT.anchorMax = Vector2.one;
            ovRT.offsetMin = Vector2.zero; ovRT.offsetMax = Vector2.zero;

            var dimGO = new GameObject("Dim");
            dimGO.transform.SetParent(_artViewerOverlay.transform, false);
            var dimRT = dimGO.AddComponent<RectTransform>();
            dimRT.anchorMin = Vector2.zero; dimRT.anchorMax = Vector2.one;
            dimRT.offsetMin = Vector2.zero; dimRT.offsetMax = Vector2.zero;
            var dimImg = dimGO.AddComponent<Image>();
            dimImg.color = artViewerDimColor;
            var dimBtn = dimGO.AddComponent<Button>();
            dimBtn.targetGraphic = dimImg;
            var dimCb = dimBtn.colors;
            dimCb.normalColor = dimCb.highlightedColor = Color.white;
            dimCb.pressedColor = new Color(1f, 1f, 1f, 0.85f);
            dimBtn.colors = dimCb;
            dimBtn.onClick.AddListener(CloseArtworkViewer);

            // Tính available area từ canvas
            float availW, availH;
            if (_rootCanvas != null && _rootCanvas.renderMode != RenderMode.ScreenSpaceOverlay)
            {
                var cvRT = _rootCanvas.GetComponent<RectTransform>();
                availW = cvRT.rect.width - artViewerPadding * 2f;
                availH = cvRT.rect.height - artViewerPadding * 2f;
            }
            else
            {
                float sf = _rootCanvas != null ? _rootCanvas.scaleFactor : 1f;
                availW = Screen.width / sf - artViewerPadding * 2f;
                availH = Screen.height / sf - artViewerPadding * 2f;
            }
            availW = Mathf.Max(availW, 1f);
            availH = Mathf.Max(availH, 1f);

            var artGO = new GameObject("Art");
            artGO.transform.SetParent(_artViewerOverlay.transform, false);
            var artRT = artGO.AddComponent<RectTransform>();
            // Đặt kích thước bằng vùng available, AspectRatioFitter sẽ co lại đúng tỉ lệ
            artRT.anchorMin = new Vector2(0.5f, 0.5f); artRT.anchorMax = new Vector2(0.5f, 0.5f);
            artRT.pivot = new Vector2(0.5f, 0.5f); artRT.anchoredPosition = Vector2.zero;
            artRT.sizeDelta = new Vector2(availW, availH);
            var artRaw = artGO.AddComponent<RawImage>();
            artRaw.texture = tex; artRaw.uvRect = new Rect(0f, 0f, 1f, 1f);
            artRaw.raycastTarget = false;
            // AspectRatioFitter giữ đúng tỉ lệ ảnh gốc, không méo
            var arf = artGO.AddComponent<AspectRatioFitter>();
            arf.aspectMode = AspectRatioFitter.AspectMode.FitInParent;
            arf.aspectRatio = tex.width > 0 && tex.height > 0
                ? (float)tex.width / tex.height
                : 16f / 9f;   // fallback 16:9 nếu texture chưa load

            var closeGO = new GameObject("CloseBtn");
            closeGO.transform.SetParent(_artViewerOverlay.transform, false);
            var closeRT = closeGO.AddComponent<RectTransform>();
            closeRT.anchorMin = new Vector2(1f, 1f); closeRT.anchorMax = new Vector2(1f, 1f);
            closeRT.pivot = new Vector2(1f, 1f); closeRT.anchoredPosition = new Vector2(-18f, -18f);
            closeRT.sizeDelta = new Vector2(44f, 44f);
            var closeBg = closeGO.AddComponent<Image>();
            closeBg.color = new Color(0.08f, 0.08f, 0.08f, 0.88f);
            if (closeArtButtonSprite != null) { closeBg.sprite = closeArtButtonSprite; closeBg.type = Image.Type.Sliced; }
            var closeBtn = closeGO.AddComponent<Button>();
            closeBtn.targetGraphic = closeBg;
            closeBtn.onClick.AddListener(CloseArtworkViewer);
            MakeTMPLabel(closeGO.transform, "X", 22f, Color.white, TextAlignmentOptions.Center);

            var infoGO = new GameObject("Info");
            infoGO.transform.SetParent(_artViewerOverlay.transform, false);
            var infoRT = infoGO.AddComponent<RectTransform>();
            infoRT.anchorMin = new Vector2(0f, 1f); infoRT.anchorMax = new Vector2(0f, 1f);
            infoRT.pivot = new Vector2(0f, 1f); infoRT.anchoredPosition = new Vector2(18f, -18f);
            infoRT.sizeDelta = new Vector2(280f, 40f);
            var infoTMP = infoGO.AddComponent<TextMeshProUGUI>();
            infoTMP.text = $"{model.data.cardName}  <size=70%><color=#aaaaaa>{tex.width}x{tex.height}</color></size>";
            infoTMP.fontSize = 13f; infoTMP.alignment = TextAlignmentOptions.TopLeft;
            infoTMP.color = Color.white; infoTMP.raycastTarget = false;
        }

        void CloseArtworkViewer()
        {
            if (!_artViewerOpen) return;
            _artViewerOpen = false;
            if (_artViewerOverlay != null) { Destroy(_artViewerOverlay); _artViewerOverlay = null; }
        }

        // ── Related Cards — Dải thumbnail ngang phía dưới màn hình ──

        void ShowRelatedSideCards()
        {
            if (model?.data?.relatedCards == null || model.data.relatedCards.Count == 0) return;
            HideRelatedSideCards();

            var parent = _rootCanvas != null ? _rootCanvas.transform : transform;

            float thumbScale = Mathf.Clamp(relatedThumbScale, 0.2f, 0.8f);
            float thumbW = _portraitCardSize.x * thumbScale;
            float thumbH = _portraitCardSize.y * thumbScale;
            float gap = 10f;
            float padV = 8f;   // padding trên/dưới trong strip
            float padH = 12f;  // padding trái/phải trong strip
            float stripH = thumbH + padV * 2f;

            // Vị trí: đáy canvas + margin nhỏ
            float canvasHalfH = 0f;
            if (_rootCanvas != null)
                canvasHalfH = _rootCanvas.GetComponent<RectTransform>().rect.height * 0.5f;
            if (canvasHalfH < 1f)
            {
                float sf = _rootCanvas != null ? Mathf.Max(_rootCanvas.scaleFactor, 0.01f) : 1f;
                canvasHalfH = Screen.height / sf * 0.5f;
            }
            float stripY = -canvasHalfH + stripH * 0.5f + 6f;

            // Đếm lá hợp lệ
            int count = 0;
            foreach (var d in model.data.relatedCards) if (d != null) count++;
            if (count == 0) return;

            float totalThumbW = count * thumbW + (count - 1) * gap;
            float stripW = totalThumbW + padH * 2f;

            // Strip container — Canvas riêng sortingOrder 210 (trên tất cả)
            var stripGO = new GameObject("_RelatedStrip");
            stripGO.transform.SetParent(parent, false);
            stripGO.transform.SetAsLastSibling();

            var stripCV = stripGO.AddComponent<Canvas>();
            stripCV.overrideSorting = true;
            stripCV.sortingOrder = inspectSortingBase + 210;
            stripGO.AddComponent<GraphicRaycaster>();

            var stripRT = stripGO.GetComponent<RectTransform>();
            stripRT.anchorMin = stripRT.anchorMax = stripRT.pivot = new Vector2(0.5f, 0.5f);
            stripRT.sizeDelta = new Vector2(stripW, stripH);
            stripRT.anchoredPosition = new Vector2(0f, stripY);

            var stripBg = stripGO.AddComponent<Image>();
            stripBg.color = new Color(0.04f, 0.04f, 0.08f, 0.88f);
            stripBg.raycastTarget = false;

            _relatedSideCards.Add(stripGO);

            // Tạo từng thumbnail
            float startX = -(totalThumbW * 0.5f) + thumbW * 0.5f;
            int idx = 0;
            foreach (var relData in model.data.relatedCards)
            {
                if (relData == null) continue;
                float x = startX + idx * (thumbW + gap);
                var thumb = CreateThumbCard(relData, stripGO.transform, new Vector2(x, 0f), thumbW, thumbH);
                _relatedSideCards.Add(thumb);
                idx++;
            }
        }

        void HideRelatedSideCards()
        {
            foreach (var go in _relatedSideCards)
                if (go != null) Destroy(go);
            _relatedSideCards.Clear();
        }

        // ── Buff History Panel (đơn giản, tự dựng bằng code — không prefab) ──
        // Liệt kê model.buffHistory bên trái card khi inspect: tên nguồn + "+ATK|+HP",
        // hoặc "tạo ra" cho entry Origin. Panel/dòng dựng bằng MakeTMPLabel có sẵn.
        void ShowBuffHistoryPanel()
        {
            HideBuffHistoryPanel();
            if (model?.data == null || model.data.cardType != CardType.Unit) return;
            var history = model.buffHistory;
            if (history == null || history.Count == 0) return;

            var parent = _rootCanvas != null ? _rootCanvas.transform : transform.parent;
            float rowH = buffPanelRowHeight;
            float w = buffPanelWidth;
            float pad = 8f;
            float h = history.Count * rowH + pad * 2f;

            _buffHistoryPanel = new GameObject("_BuffHistoryPanel");
            _buffHistoryPanel.transform.SetParent(parent, false);
            _buffHistoryPanel.transform.SetAsLastSibling();

            var cv = _buffHistoryPanel.AddComponent<Canvas>();
            cv.overrideSorting = true;
            cv.sortingOrder = inspectSortingBase + 201; // ngang related preview (201), dưới side strip (210)
            _buffHistoryPanel.AddComponent<GraphicRaycaster>();

            var rt = _buffHistoryPanel.GetComponent<RectTransform>();
            float cardHalfW = (_portraitCardSize.x * inspectScale) * 0.5f;
            rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 0.5f);
            rt.pivot = new Vector2(1f, 0.5f); // neo mép phải panel — không cần biết trước vị trí trái
            rt.sizeDelta = new Vector2(w, h);
            rt.anchoredPosition = new Vector2(_inspectCenterPos.x - cardHalfW - 14f, _inspectCenterPos.y);

            var bg = _buffHistoryPanel.AddComponent<Image>();
            bg.color = new Color(0.04f, 0.04f, 0.08f, 0.9f);
            bg.raycastTarget = false;

            for (int i = 0; i < history.Count; i++)
            {
                var e = history[i];
                float y = h * 0.5f - pad - rowH * 0.5f - i * rowH;

                var nameLbl = MakeTMPLabel(_buffHistoryPanel.transform, e.sourceName, buffPanelNameFontSize,
                    Color.white, TextAlignmentOptions.MidlineLeft);
                var nRT = nameLbl.rectTransform;
                nRT.anchorMin = new Vector2(0f, 0.5f); nRT.anchorMax = new Vector2(0f, 0.5f);
                nRT.pivot = new Vector2(0f, 0.5f);
                nRT.anchoredPosition = new Vector2(buffPanelPadding, y);
                nRT.sizeDelta = new Vector2(w - buffPanelPadding * 2f - buffPanelStatColumnWidth - 4f, rowH - 4f);
                // Ép 1 dòng + "..." khi hết chỗ — tên dài không bao giờ làm vỡ layout hàng.
                nameLbl.textWrappingMode = TextWrappingModes.NoWrap;
                nameLbl.overflowMode = TextOverflowModes.Ellipsis;

                int net = e.attackBuff + e.healthBuff;
                string statStr = e.IsOrigin ? "tạo ra" : e.StatDeltaText();
                Color statColor = e.IsOrigin
                    ? new Color(0.7f, 0.7f, 0.75f)
                    : (net > 0 ? statColorBuffed : (net < 0 ? statColorDebuffed : Color.white));

                var statLbl = MakeTMPLabel(_buffHistoryPanel.transform, statStr, buffPanelStatFontSize,
                    statColor, TextAlignmentOptions.MidlineRight, bold: !e.IsOrigin);
                var sRT = statLbl.rectTransform;
                sRT.anchorMin = new Vector2(1f, 0.5f); sRT.anchorMax = new Vector2(1f, 0.5f);
                sRT.pivot = new Vector2(1f, 0.5f);
                sRT.anchoredPosition = new Vector2(-buffPanelPadding, y);
                sRT.sizeDelta = new Vector2(buffPanelStatColumnWidth, rowH - 4f);
                // Ép 1 dòng — tránh vỡ dòng kiểu "+1|" xuống "0" khi cột hẹp.
                // Auto-shrink font nếu số dài (vd +15|+15) không vừa cột, thay vì bị cắt.
                statLbl.textWrappingMode = TextWrappingModes.NoWrap;
                statLbl.overflowMode = TextOverflowModes.Ellipsis;
                statLbl.enableAutoSizing = true;
                statLbl.fontSizeMin = Mathf.Max(8f, buffPanelStatFontSize * 0.6f);
                statLbl.fontSizeMax = buffPanelStatFontSize;
            }
        }

        void HideBuffHistoryPanel()
        {
            if (_buffHistoryPanel != null) Destroy(_buffHistoryPanel);
            _buffHistoryPanel = null;
        }

        // ── PoC ITEM panel — hexagon bên PHẢI card khi inspect ─────
        // Lá CHAMPION: hiện TRANG BỊ ĐÃ LẮP (ItemData, từ hub — ProgressStore.equippedItems).
        // Lá khác: hiện CardItem gắn theo lá trong run (RunItems).
        void ShowItemPanel()
        {
            HideItemPanel();
            if (model?.data == null) return;

            // Gom danh sách ô hiển thị.
            var infos = new System.Collections.Generic.List<ItemSlotInfo>();
            bool isChampCard = false;

            // PvP-GAUNTLET: trang bị lấy từ LOADOUT (MatchContext) — CampaignRun.champion null ở PvP.
            // Champion gauntlet: LUÔN hiện ô (kể cả ô trống, giống PoC) → slotCount theo SAO (đã truyền qua mạng),
            // KHÔNG gọi ProgressStore.ItemSlots/CampaignRun → tránh NRE. Rỗng ở PvE/PvP-thường → nhánh cũ y hệt.
            bool pvpChampion = LoRClone.Data.PvpItemDisplay.IsChampion(model);
            if (pvpChampion)
            {
                isChampCard = true;   // để phần slotCount hiện tối đa số ô (ô trống + ô có món)
                // Chữ (tên/mô tả/độ hiếm) từ LOADOUT → hiện dù máy này KHÔNG có library. Icon từ library nếu sẵn.
                foreach (var v in LoRClone.Data.PvpItemDisplay.ViewsFor(model))
                    infos.Add(new ItemSlotInfo(v.name, v.desc, v.icon, RarityColor(v.rarity)));
            }
            else if (!model.belongsToPlayer)
            {
                // ĐỊCH: trang bị theo model.id (chỉ hiện ô có đồ).
                foreach (var it in RunItems.EnemyItemsOf(model.id))
                    infos.Add(new ItemSlotInfo(it.itemName, it.Describe(), it.icon, RarityColor(it.rarity)));
            }
            else
            {
                isChampCard = CampaignRun.champion != null && CampaignRun.champion.championCard != null
                    && CampaignRun.champion.championCard.cardName == model.data.cardName;
                if (isChampCard)
                {
                    var champ = CampaignRun.champion;
                    int lvl = ProgressStore.GetMasteryLevel(champ.championName);
                    foreach (var it in LoRClone.Model.CardItem.EquippedChampionItems(champ.championName, lvl))
                        infos.Add(new ItemSlotInfo(it.itemName, it.Describe(), it.icon, RarityColor(it.rarity)));
                }
                else
                {
                    var items = RunItems.ItemsOf(model.data.cardName);
                    if (items != null)
                        foreach (var it in items)
                            infos.Add(new ItemSlotInfo(it.itemName, it.Describe(), it.icon, RarityColor(it.rarity)));
                }
            }

            // Lá champion: LUÔN hiện tối đa 3 ô (kể cả ô trống). Lá khác/địch: chỉ hiện ô có item.
            // Champion gauntlet → số ô theo SAO trong loadout (PvpItemDisplay.SlotCount), KHÔNG đụng CampaignRun (null ở PvP).
            int slotCount = pvpChampion
                ? Mathf.Clamp(Mathf.Max(LoRClone.Data.PvpItemDisplay.SlotCount(model), infos.Count), 1, 3)
                : isChampCard
                    ? Mathf.Clamp(Mathf.Max(ProgressStore.ItemSlots(CampaignRun.champion.championName), infos.Count), 1, 3)
                    : infos.Count;
            if (slotCount == 0) return;

            var parent = _rootCanvas != null ? _rootCanvas.transform : transform.parent;
            const float w = 300f, rowH = 72f, pad = 10f;
            float h = slotCount * rowH + pad * 2f;

            _itemPanel = new GameObject("_ItemPanel");
            _itemPanel.transform.SetParent(parent, false);
            _itemPanel.transform.SetAsLastSibling();
            var cv = _itemPanel.AddComponent<Canvas>();
            cv.overrideSorting = true;
            cv.sortingOrder = inspectSortingBase + 201;
            _itemPanel.AddComponent<GraphicRaycaster>();

            var rt = _itemPanel.GetComponent<RectTransform>();
            float cardHalfW = (_portraitCardSize.x * inspectScale) * 0.5f;
            rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 0.5f);
            rt.pivot = new Vector2(0f, 0.5f); // neo mép TRÁI panel → panel nằm bên PHẢI card
            rt.sizeDelta = new Vector2(w, h);
            rt.anchoredPosition = new Vector2(_inspectCenterPos.x + cardHalfW + 30f, _inspectCenterPos.y);

            var bg = _itemPanel.AddComponent<Image>();
            bg.color = new Color(0.04f, 0.04f, 0.08f, 0.9f);
            bg.raycastTarget = false;

            float hex = Mathf.Min(rowH - 20f, 46f);
            float textX = pad + hex + 12f;
            for (int i = 0; i < slotCount; i++)
            {
                bool has = i < infos.Count;
                var info = has ? infos[i] : default;   // ô trống khi has == false
                float y = h * 0.5f - pad - rowH * 0.5f - i * rowH;

                var hexGO = new GameObject("_hex", typeof(RectTransform), typeof(Image));
                hexGO.transform.SetParent(_itemPanel.transform, false);
                var hrt = (RectTransform)hexGO.transform;
                hrt.anchorMin = hrt.anchorMax = new Vector2(0f, 0.5f);
                hrt.pivot = new Vector2(0f, 0.5f);
                hrt.anchoredPosition = new Vector2(pad, y);
                hrt.sizeDelta = new Vector2(hex, hex);
                var himg = hexGO.GetComponent<Image>();
                himg.sprite = HexSprite();
                himg.color = has ? info.color : new Color(0.28f, 0.30f, 0.36f, 0.9f);
                himg.raycastTarget = false;

                if (has && info.icon != null)
                {
                    var icGO = new GameObject("_ic", typeof(RectTransform), typeof(UnityEngine.UI.Image));
                    icGO.transform.SetParent(hexGO.transform, false);
                    var iRT = (RectTransform)icGO.transform;
                    iRT.anchorMin = new Vector2(0.22f, 0.22f); iRT.anchorMax = new Vector2(0.78f, 0.78f);
                    iRT.offsetMin = iRT.offsetMax = Vector2.zero;
                    var iImg = icGO.GetComponent<UnityEngine.UI.Image>();
                    iImg.sprite = info.icon; iImg.preserveAspect = true; iImg.raycastTarget = false;
                }
                else
                {
                    var glyph = MakeTMPLabel(hexGO.transform, has ? "" : "+", hex * 0.42f,
                        new Color(0.08f, 0.09f, 0.12f, 0.92f), TextAlignmentOptions.Center);
                    var gRT = glyph.rectTransform;
                    gRT.anchorMin = Vector2.zero; gRT.anchorMax = Vector2.one; gRT.offsetMin = gRT.offsetMax = Vector2.zero;
                }

                var nameLbl = MakeTMPLabel(_itemPanel.transform, has ? info.name : "Ô trống", 15f,
                    has ? Color.white : new Color(0.60f, 0.63f, 0.70f),
                    TextAlignmentOptions.MidlineLeft, bold: true);
                var nRT = nameLbl.rectTransform;
                nRT.anchorMin = nRT.anchorMax = new Vector2(0f, 0.5f); nRT.pivot = new Vector2(0f, 0.5f);
                nRT.anchoredPosition = new Vector2(textX, y + 20f);
                nRT.sizeDelta = new Vector2(w - textX - pad, 20f);
                nameLbl.textWrappingMode = TextWrappingModes.NoWrap;
                nameLbl.overflowMode = TextOverflowModes.Ellipsis;

                var sub = MakeTMPLabel(_itemPanel.transform,
                    has ? info.desc : "",
                    12f, new Color(0.86f, 0.90f, 0.96f), TextAlignmentOptions.TopLeft);
                var sRT = sub.rectTransform;
                sRT.anchorMin = sRT.anchorMax = new Vector2(0f, 0.5f); sRT.pivot = new Vector2(0f, 0.5f);
                sRT.anchoredPosition = new Vector2(textX, y - 6f);
                sRT.sizeDelta = new Vector2(w - textX - pad, 48f);
                sub.textWrappingMode = TextWrappingModes.Normal;
                sub.overflowMode = TextOverflowModes.Ellipsis;
            }
        }

        // Thông tin 1 ô item để vẽ (thống nhất nguồn ItemData đã lắp / CardItem gắn lá).
        struct ItemSlotInfo
        {
            public string name, desc; public Sprite icon; public Color color;
            public ItemSlotInfo(string name, string desc, Sprite icon, Color color)
            { this.name = name; this.desc = desc; this.icon = icon; this.color = color; }
        }

        void HideItemPanel()
        {
            if (_itemPanel != null) Destroy(_itemPanel);
            _itemPanel = null;
        }

        static Color RarityColor(ItemRarity r) =>
            r == ItemRarity.Epic ? new Color(0.85f, 0.62f, 0.28f) :
            r == ItemRarity.Rare ? new Color(0.36f, 0.60f, 0.90f) :
                                   new Color(0.66f, 0.69f, 0.73f);

        static string RarityHex(ItemRarity r) =>
            r == ItemRarity.Epic ? "D99E47" : r == ItemRarity.Rare ? "5C99E6" : "A9AEB5";

        static string RarityVN(ItemRarity r) =>
            r == ItemRarity.Epic ? "Sử Thi" : r == ItemRarity.Rare ? "Hiếm" : "Thường";

        // Sprite lục giác flat-top (sinh runtime, cache) cho icon item.
        static Sprite _hexSprite;
        static Sprite HexSprite()
        {
            if (_hexSprite != null) return _hexSprite;
            int s = 64; float c = (s - 1) * 0.5f; float ap = c - 2f;
            const float k = 0.8660254f; // sin60
            var tex = new Texture2D(s, s, TextureFormat.RGBA32, false)
            { filterMode = FilterMode.Bilinear, wrapMode = TextureWrapMode.Clamp, name = "HexIcon" };
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
            _hexSprite.name = "HexIconSprite";
            return _hexSprite;
        }

        /// <summary>
        /// Thumbnail đơn giản cho dải related cards phía dưới.
        /// Chỉ gồm: artwork, dim, tên ở cuối, mana badge góc trên-trái.
        /// Click → NavigateToRelated (hiện full preview ở giữa màn hình).
        /// </summary>
        GameObject CreateThumbCard(CardData data, Transform parent, Vector2 pos, float thumbW, float thumbH)
        {
            var go = new GameObject("_Thumb_" + data.cardName);
            go.transform.SetParent(parent, false);

            var rt = go.AddComponent<RectTransform>();
            rt.anchorMin = rt.anchorMax = rt.pivot = new Vector2(0.5f, 0.5f);
            rt.anchoredPosition = pos;
            rt.sizeDelta = new Vector2(thumbW, thumbH);

            // Background (frame sprite nếu có, màu theo loại bài)
            var bgImg = go.AddComponent<Image>();
            if (cardFrame != null && cardFrame.sprite != null)
            { bgImg.sprite = cardFrame.sprite; bgImg.type = Image.Type.Sliced; }
            bgImg.color = data.cardType == CardType.Unit
                ? new Color(0.13f, 0.10f, 0.07f, 1f)
                : new Color(0.07f, 0.08f, 0.18f, 1f);

            // Artwork — fill toàn card
            if (data.artwork != null)
            {
                var artGO = new GameObject("Art");
                artGO.transform.SetParent(go.transform, false);
                var artRT = artGO.AddComponent<RectTransform>();
                artRT.anchorMin = Vector2.zero; artRT.anchorMax = Vector2.one;
                artRT.offsetMin = artRT.offsetMax = Vector2.zero;
                var raw = artGO.AddComponent<RawImage>();
                raw.texture = data.artwork;
                raw.raycastTarget = false;
                ApplyFocalUV(raw, data, thumbW, thumbH);
            }

            // Overlay mờ giúp text dễ đọc
            {
                var ovGO = new GameObject("Dim");
                ovGO.transform.SetParent(go.transform, false);
                var ovRT = ovGO.AddComponent<RectTransform>();
                ovRT.anchorMin = Vector2.zero; ovRT.anchorMax = Vector2.one;
                ovRT.offsetMin = ovRT.offsetMax = Vector2.zero;
                var ovImg = ovGO.AddComponent<Image>();
                ovImg.color = new Color(0f, 0f, 0f, 80f / 255f);
                ovImg.raycastTarget = false;
            }

            // Tên lá — dải đen ở cuối card
            {
                float nameBandH = Mathf.Max(14f, thumbH * 0.22f);
                var nGO = new GameObject("Name");
                nGO.transform.SetParent(go.transform, false);
                var nRT = nGO.AddComponent<RectTransform>();
                nRT.anchorMin = new Vector2(0f, 0f); nRT.anchorMax = new Vector2(1f, 0f);
                nRT.pivot = new Vector2(0.5f, 0f);
                nRT.anchoredPosition = Vector2.zero;
                nRT.sizeDelta = new Vector2(0f, nameBandH);
                var nBg = nGO.AddComponent<Image>();
                nBg.color = new Color(0f, 0f, 0f, 0.78f);
                nBg.raycastTarget = false;
                var nTMP = MakeTMPLabel(nGO.transform, data.cardName,
                    Mathf.Max(6f, nameBandH * 0.48f), Color.white, TextAlignmentOptions.Center, bold: true);
                nTMP.overflowMode = TextOverflowModes.Ellipsis;
            }

            // Mana badge — góc trên-trái
            {
                float badgeSize = Mathf.Max(12f, thumbW * 0.26f);
                var mGO = new GameObject("Mana");
                mGO.transform.SetParent(go.transform, false);
                var mRT = mGO.AddComponent<RectTransform>();
                mRT.anchorMin = new Vector2(0f, 1f); mRT.anchorMax = new Vector2(0f, 1f);
                mRT.pivot = new Vector2(0f, 1f);
                mRT.anchoredPosition = new Vector2(2f, -2f);
                mRT.sizeDelta = new Vector2(badgeSize, badgeSize);
                var mImg = mGO.AddComponent<Image>();
                mImg.color = new Color(0.08f, 0.18f, 0.58f, 0.95f);
                mImg.raycastTarget = false;
                MakeTMPLabel(mGO.transform, data.manaCost.ToString(),
                    Mathf.Max(5f, badgeSize * 0.58f), Color.white, TextAlignmentOptions.Center, bold: true);
            }

            // Button — click toàn thumbnail → navigate đến lá liên quan
            var btn = go.AddComponent<Button>();
            btn.targetGraphic = bgImg;
            var bc = btn.colors;
            bc.highlightedColor = new Color(1.3f, 1.3f, 1.3f, 1f);
            bc.pressedColor = new Color(0.78f, 0.78f, 0.78f, 1f);
            btn.colors = bc;
            btn.onClick.AddListener(() => NavigateToRelated(data));

            return go;
        }
    }
}