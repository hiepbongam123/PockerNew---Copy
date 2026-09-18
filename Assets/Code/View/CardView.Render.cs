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
    /// CardView — phần Render: kích thước/UV artwork, refresh stats, keywords, visibility, highlights, spell circle mode.
    /// Partial class: tách từ CardView.cs gốc, KHÔNG đổi logic.
    /// Field khai báo ở CardView.cs (file chính) — mọi partial dùng chung.
    /// </summary>
    public partial class CardView
    {
        // ── Size & UV ─────────────────────────────────────────────
        public void OverrideSize(float width, float height)
        {
            if (width <= 0f && height <= 0f) return;
            _fieldCardSize = new Vector2(width, height);
            _fieldArtworkSize = new Vector2(width - 10f, height - 10f);
            _hasFieldSize = true;
            ApplySizeInternal(width, height);
        }

        void ApplySizeInternal(float width, float height)
        {
            _rect.anchorMin = new Vector2(0.5f, 0.5f);
            _rect.anchorMax = new Vector2(0.5f, 0.5f);
            _rect.pivot = new Vector2(0.5f, 0.5f);
            _rect.anchoredPosition = Vector2.zero;
            _rect.sizeDelta = new Vector2(width, height);
            if (artworkRawImage != null)
            {
                var artRT = artworkRawImage.GetComponent<RectTransform>();
                artRT.anchorMin = new Vector2(0.5f, 0.5f);
                artRT.anchorMax = new Vector2(0.5f, 0.5f);
                artRT.pivot = new Vector2(0.5f, 0.5f);
                artRT.anchoredPosition = Vector2.zero;
                artRT.sizeDelta = new Vector2(width - 10f, height - 10f);
            }
        }

        public void ResetToPortraitSize()
        {
            _hasFieldSize = false;
            ApplySizeInternal(_portraitCardSize.x, _portraitCardSize.y);
            RefreshArtworkUV(false);
        }

        void ComputeAndApplyUV(float cardW, float cardH, float focalX, float focalY)
        {
            if (artworkRawImage == null || artworkRawImage.texture == null) return;
            var tex = artworkRawImage.texture;
            if (tex.width <= 0 || tex.height <= 0) { artworkRawImage.uvRect = new Rect(0, 0, 1, 1); return; }
            float texAspect = (float)tex.width / tex.height;
            float cardAspect = Mathf.Max(cardW, 1f) / Mathf.Max(cardH, 1f);
            float zoom = GetZoom();
            float baseUvW, baseUvH;
            if (texAspect > cardAspect) { baseUvH = 1f; baseUvW = cardAspect / texAspect; }
            else { baseUvW = 1f; baseUvH = texAspect / cardAspect; }
            float uvW = Mathf.Clamp(baseUvW / zoom, 0.001f, 1f);
            float uvH = Mathf.Clamp(baseUvH / zoom, 0.001f, 1f);
            float uvX = Mathf.Clamp(focalX - uvW * 0.5f, 0f, 1f - uvW);
            float uvY = Mathf.Clamp(focalY - uvH * 0.5f, 0f, 1f - uvH);
            artworkRawImage.uvRect = new Rect(uvX, uvY, uvW, uvH);
        }

        void RefreshArtworkUV(bool onField)
        {
            float cardW = Mathf.Max(_rect.rect.width, 1f);
            float cardH = Mathf.Max(_rect.rect.height, 1f);
            ComputeAndApplyUV(cardW, cardH, GetFocalX(), GetFocalY());
        }

        public void ForceArtworkUV(float cardW, float cardH)
        {
            ComputeAndApplyUV(cardW, cardH, GetFocalX(), GetFocalY());
        }

        /// <summary>Tính và gán uvRect cho một RawImage bất kỳ theo focal point của CardData.</summary>
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

        public void SetHandSortingOrder(int order)
        {
            _baseSortingOrder = order;
            // FIX: HandZoneView.RefreshLayout gọi hàm này cho MỌI lá — kể cả lá đang
            // inspect (order 200) hoặc đang mở keyword popup (order 150).
            // Nếu hạ order lúc đó: card inspect tụt xuống dưới dim overlay (194),
            // popup bị lá bên cạnh (order cao hơn) đè lên → "popup mở mà không thấy".
            // Chỉ ghi nhận _baseSortingOrder; order thật được restore khi thoát state.
            if (_inspecting || _cardWithOpenKeywordPopup == this) return;
            _selfCanvas.sortingOrder = order;
        }

        public void SetHandScale(float targetW, float targetH)
        {
            if (_portraitCardSize.x > 0f && _portraitCardSize.y > 0f)
            {
                _sizeScale = Mathf.Min(targetW / _portraitCardSize.x, targetH / _portraitCardSize.y);
                transform.localScale = Vector3.one * _sizeScale;
            }
        }

        // ── Render ────────────────────────────────────────────────
        public void RefreshAll()
        {
            HideKeywordPopup();
            if (model == null) return;
            if (_faceDown) { ApplyFaceDownVisuals(); return; }
            if (nameText) nameText.text = model.data.cardName;
            if (manaCostText) manaCostText.text = model.currentManaCost.ToString();
            if (artworkRawImage && model.data.artwork)
                artworkRawImage.texture = model.data.artwork;
            RefreshStats();
            RefreshKeywords();
            RefreshSkillsText();
            RefreshVisibilityByLocation();
            RefreshHighlights();
            RefreshItemBadge();
        }

        // 1 ô icon trang bị để vẽ trên dải (gộp cả trang bị CHAMPION lẫn CardItem per-card).
        struct BadgeIcon { public Sprite icon; public Color color; public bool hasSkill; }

        System.Collections.Generic.List<BadgeIcon> GatherEquipIcons()
        {
            var list = new System.Collections.Generic.List<BadgeIcon>();
            if (model?.data == null) return list;

            // PvP-GAUNTLET: trang bị đọc từ LOADOUT (MatchContext) thay vì CampaignRun (null ở PvP).
            // Rỗng ở PvE/PvP-thường → chạy tiếp nhánh cũ y hệt (byte-identical).
            var pvpBadge = LoRClone.Data.PvpItemDisplay.ViewsFor(model);
            if (pvpBadge.Count > 0)
            {
                foreach (var v in pvpBadge)
                    list.Add(new BadgeIcon
                    {
                        icon = v.icon,
                        color = RarityColor(v.rarity),
                        hasSkill = LoRClone.Model.CardItem.SkillForItem(v.name) != null   // null nếu chưa có library → không marker
                    });
                return list;
            }

            // ĐỊCH: đọc trang bị theo model.id (mỗi con đeo đồ riêng — hiện ở ô trang bị như quân mình).
            if (!model.belongsToPlayer)
            {
                foreach (var it in RunItems.EnemyItemsOf(model.id))
                    list.Add(new BadgeIcon { icon = it.icon, color = RarityColor(it.rarity), hasSkill = it.HasSkill });
                return list;
            }

            bool isChamp = CampaignRun.champion != null && CampaignRun.champion.championCard != null
                && CampaignRun.champion.championCard.cardName == model.data.cardName;

            if (isChamp)
            {
                var champ = CampaignRun.champion;
                int lvl = ProgressStore.GetMasteryLevel(champ.championName);
                foreach (var it in LoRClone.Model.CardItem.EquippedChampionItems(champ.championName, lvl))
                    list.Add(new BadgeIcon { icon = it.icon, color = RarityColor(it.rarity), hasSkill = it.HasSkill });
            }
            else
            {
                var items = RunItems.ItemsOf(model.data.cardName);
                if (items != null)
                    foreach (var it in items)
                        list.Add(new BadgeIcon { icon = it.icon, color = RarityColor(it.rarity), hasSkill = it.HasSkill });
            }
            return list;
        }

        // DẢI ICON trang bị áp SÁT viền PHẢI card (của player) — cả trang bị champion lẫn CardItem.
        // Nằm trong khung card (không tách khoảng → không che/không bị che bởi lá khác). Không dùng ký tự (tránh lỗi font).
        void RefreshItemBadge()
        {
            var icons = GatherEquipIcons();
            int count = icons.Count;
            if (count <= 0)
            {
                if (_itemBadge != null) _itemBadge.SetActive(false);
                return;
            }

            if (_itemBadge == null)
            {
                _itemBadge = new GameObject("_ItemStrip", typeof(RectTransform));
                _itemBadge.transform.SetParent(transform, false);
                var srt = (RectTransform)_itemBadge.transform;
                srt.anchorMin = srt.anchorMax = new Vector2(1f, 0.5f); // mép PHẢI, giữa chiều cao
                srt.pivot = new Vector2(1f, 0.5f);                     // áp sát viền phải, nằm TRONG khung card
                srt.anchoredPosition = new Vector2(-2f, 0f);
            }
            _itemBadge.SetActive(true);
            _itemBadge.transform.SetAsLastSibling();

            var strip = (RectTransform)_itemBadge.transform;
            for (int i = strip.childCount - 1; i >= 0; i--) Destroy(strip.GetChild(i).gameObject);

            const int maxShown = 4;
            const float cell = 24f, gap = 3f;
            int show = Mathf.Min(count, maxShown);
            bool overflow = count > maxShown;
            int rows = show + (overflow ? 1 : 0);
            strip.sizeDelta = new Vector2(cell, rows * cell + (rows - 1) * gap);

            for (int i = 0; i < show; i++)
            {
                var bi = icons[i];
                var g = new GameObject("it" + i, typeof(RectTransform), typeof(Image));
                g.transform.SetParent(strip, false);
                var rt = (RectTransform)g.transform;
                rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 1f); rt.pivot = new Vector2(0.5f, 1f);
                rt.anchoredPosition = new Vector2(0f, -(i * (cell + gap)));
                rt.sizeDelta = new Vector2(cell, cell);
                var img = g.GetComponent<Image>();
                img.raycastTarget = false;
                img.sprite = HexSprite();
                img.color = bi.color;                 // nền hex theo màu độ hiếm/slot

                if (bi.icon != null)                  // có icon riêng → phủ lên nền
                {
                    var icGO = new GameObject("ic", typeof(RectTransform), typeof(Image));
                    icGO.transform.SetParent(g.transform, false);
                    var irt = (RectTransform)icGO.transform;
                    irt.anchorMin = new Vector2(0.18f, 0.18f); irt.anchorMax = new Vector2(0.82f, 0.82f);
                    irt.offsetMin = irt.offsetMax = Vector2.zero;
                    var iimg = icGO.GetComponent<Image>();
                    iimg.sprite = bi.icon; iimg.preserveAspect = true; iimg.raycastTarget = false;
                }
                if (bi.hasSkill)                      // chấm VÀNG góc dưới-phải (dùng sprite, không ký tự)
                {
                    var dot = new GameObject("sk", typeof(RectTransform), typeof(Image));
                    dot.transform.SetParent(g.transform, false);
                    var drt = (RectTransform)dot.transform;
                    drt.anchorMin = new Vector2(0.58f, 0f); drt.anchorMax = new Vector2(1f, 0.42f);
                    drt.offsetMin = drt.offsetMax = Vector2.zero;
                    var dimg = dot.GetComponent<Image>();
                    dimg.sprite = HexSprite(); dimg.color = new Color(1f, 0.84f, 0.32f, 1f); dimg.raycastTarget = false;
                }
            }

            if (overflow)
            {
                var g = new GameObject("more", typeof(RectTransform), typeof(Image));
                g.transform.SetParent(strip, false);
                var rt = (RectTransform)g.transform;
                rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 1f); rt.pivot = new Vector2(0.5f, 1f);
                rt.anchoredPosition = new Vector2(0f, -(show * (cell + gap)));
                rt.sizeDelta = new Vector2(cell, cell);
                var img = g.GetComponent<Image>(); img.sprite = HexSprite();
                img.color = new Color(0.22f, 0.24f, 0.30f, 0.96f); img.raycastTarget = false;
                var gl = MakeTMPLabel(g.transform, "+" + (count - maxShown), cell * 0.42f, Color.white,
                    TextAlignmentOptions.Center);
                var grt = gl.rectTransform;
                grt.anchorMin = Vector2.zero; grt.anchorMax = Vector2.one; grt.offsetMin = grt.offsetMax = Vector2.zero;
            }
        }

        void RefreshStats()
        {
            if (model == null) return;
            if (_faceDown) return;

            if (manaCostText)
            {
                manaCostText.text = model.currentManaCost.ToString();   // ← cập nhật SỐ
                manaCostText.color = model.currentManaCost < model.data.manaCost
                    ? statColorBuffed    // xanh lá
                    : statColorDefault;  // trắng
            }

            if (attackText)
            {
                attackText.text = model.effectiveAttack.ToString();
                attackText.color = GetAttackColor();
            }
            if (healthText)
            {
                healthText.text = model.currentHealth.ToString();
                healthText.color = GetHealthColor();
            }

            // ── Level-Up Indicator ────────────────────────────────
            // Viền sáng: bật khi champion đủ điều kiện nhưng chưa ra sân
            if (levelUpReadyBorder != null)
                levelUpReadyBorder.enabled = model.readyToLevelUp;

            // Thanh tiến độ: hiện khi champion có thể level up và chưa level up.
            // Nội dung thay đổi theo conditionType:
            //   TriggerCount     → "X/N"
            //   SelfAttackReach  → "ATK X/N"
            //   SelfHealthReach  → "HP X/N"
            //   Passive đồng minh → "Sẵn sàng!" hoặc "Ngưỡng: N" (CardView không thấy PlayerModel)
            if (levelUpProgressText != null)
            {
                bool showProgress = model.data.canLevelUp && !model.hasLeveledUp;
                levelUpProgressText.gameObject.SetActive(showProgress);
                if (showProgress)
                {
                    var cfg = model.data.levelUpConfig;
                    if (cfg == null)
                    {
                        levelUpProgressText.text = $"{model.levelUpProgress}/?";
                    }
                    else switch (cfg.conditionType)
                        {
                            case LevelUpConditionType.TriggerCount:
                                levelUpProgressText.text = $"{model.levelUpProgress}/{cfg.threshold}";
                                break;
                            case LevelUpConditionType.SelfAttackReach:
                                levelUpProgressText.text = $"ATK {model.currentAttack}/{cfg.threshold}";
                                break;
                            case LevelUpConditionType.SelfHealthReach:
                                levelUpProgressText.text = $"HP {model.currentHealth}/{cfg.threshold}";
                                break;
                            default:
                                // AllyCountOnBoard / AllySumAttack / AllySumHealth:
                                // CardView không có PlayerModel nên không tính được tổng.
                                // readyToLevelUp (glow viền) đã thể hiện trạng thái — text chỉ cần nhẹ.
                                levelUpProgressText.text = model.readyToLevelUp ? "Sẵn sàng!" : $"/{cfg.threshold}";
                                break;
                        }
                }
            }

            // FIX: keyword có thể thay đổi RUNTIME giữa game (GrantKeyword khi Trang Bị,
            // equipGrantKeywords, SpellShield tiêu hao đổi trạng thái dim...).
            // GrantKeyword fire OnStatsChanged → tới đây. So sánh chữ ký keyword:
            // chỉ rebuild icon khi tập keyword THỰC SỰ đổi — không rebuild mỗi lần
            // HP/ATK thay đổi (giữ nguyên fix chống double-rebuild cũ).
            var sig = ComputeKeywordSignature();
            if (sig != _kwSignature)
            {
                RefreshKeywords(); // tự cập nhật _kwSignature bên trong
                bool onField = model.location == CardLocation.OnBench
                            || model.location == CardLocation.OnBattlefield;
                if (!_inspecting) RefreshKeywordPositions(onField);
            }

            RefreshItemBadge();   // trang bị áp runtime (địch lắp đồ / player gắn item) → cập nhật badge ngay
        }

        // ── Keyword signature (phát hiện keyword đổi runtime) ─────
        string _kwSignature;

        string ComputeKeywordSignature()
        {
            if (model == null) return "";
            var sb = new StringBuilder();
            foreach (var kw in model.GetAllKeywords())
            {
                sb.Append((int)kw);
                sb.Append(',');
            }
            // Trạng thái dim của SpellShield cũng là 1 phần chữ ký (icon mờ/rõ)
            if (model.hasSpellShield) sb.Append('S');
            return sb.ToString();
        }

        /// <summary>
        /// Màu số ATK:
        ///   xanh  — effectiveAttack &gt; baseAttack (được cường hóa)
        ///   đỏ    — effectiveAttack &lt; baseAttack (bị giảm, kể cả tempDebuff)
        ///   trắng — bằng base
        /// </summary>
        Color GetAttackColor()
        {
            int eff = model.effectiveAttack;
            int base_ = model.data.baseAttack;
            if (eff > base_) return statColorBuffed;
            if (eff < base_) return statColorDebuffed;
            return statColorDefault;
        }

        /// <summary>
        /// Màu số HP:
        ///   đỏ    — currentHealth &lt; maxHealth (đang bị thương — ưu tiên cao nhất)
        ///   xanh  — maxHealth &gt; baseHealth nhưng đang đầy (HP được cường hóa và không bị thương)
        ///   trắng — đầy máu và bằng base
        /// </summary>
        Color GetHealthColor()
        {
            if (model.currentHealth < model.maxHealth)
                return statColorDebuffed;                       // bị thương
            if (model.maxHealth > model.data.baseHealth)
                return statColorBuffed;                         // buff HP, đang đầy máu
            return statColorDefault;
        }

        void RefreshKeywords()
        {
            if (keywordsContainer == null || keywordIconPrefab == null || model == null) return;
            // Tách child ra khỏi container NGAY (SetParent null) rồi Destroy deferred.
            // Không dùng DestroyImmediate vì có thể gây conflict với layout rebuild cycle.
            // Không dùng Destroy thẳng vì child vẫn còn trong childCount → layout sắp xếp sai vị trí.
            for (int i = keywordsContainer.childCount - 1; i >= 0; i--)
            {
                var child = keywordsContainer.GetChild(i);
                child.SetParent(null);   // tức thì loại ra khỏi container
                Destroy(child.gameObject);
            }

            // Đảm bảo icon cụm vào giữa, không bị kéo ra 2 đầu
            var hlg = keywordsContainer.GetComponent<HorizontalLayoutGroup>();
            if (hlg == null) hlg = keywordsContainer.gameObject.AddComponent<HorizontalLayoutGroup>();
            hlg.childForceExpandWidth = false;
            hlg.childForceExpandHeight = false;
            hlg.childAlignment = TextAnchor.MiddleCenter;
            hlg.spacing = 4f;

            var csf = keywordsContainer.GetComponent<ContentSizeFitter>();
            if (csf == null) csf = keywordsContainer.gameObject.AddComponent<ContentSizeFitter>();
            csf.horizontalFit = ContentSizeFitter.FitMode.PreferredSize;
            csf.verticalFit = ContentSizeFitter.FitMode.Unconstrained;

            var spriteMap = new System.Collections.Generic.Dictionary<KeywordType, Sprite>();
            foreach (var entry in keywordIcons)
                if (entry.icon != null) spriteMap[entry.keyword] = entry.icon;
            // GetAllKeywords() trả về union của data.keywords + runtime grants + boolean shortcuts
            // SpellShield đã tiêu hao vẫn hiện trong list → dim bởi !model.hasSpellShield
            var toShow = new System.Collections.Generic.List<(KeywordType kw, bool dimmed)>();
            foreach (var kw in model.GetAllKeywords())
            {
                bool dimmed = kw == KeywordType.SpellShield && !model.hasSpellShield;
                toShow.Add((kw, dimmed));
            }
            foreach (var (kw, dimmed) in toShow)
            {
                var go = Instantiate(keywordIconPrefab, keywordsContainer);
                var img = go.GetComponent<Image>();
                if (img != null)
                {
                    if (spriteMap.TryGetValue(kw, out var sprite)) img.sprite = sprite;
                    img.color = dimmed ? new Color(1f, 1f, 1f, 0.35f) : Color.white;
                    img.raycastTarget = true;
                }
                go.name = kw.ToString();
            }

            // Force rebuild layout ngay lập tức sau khi tạo icon — đặc biệt quan trọng
            // khi gọi trong lúc _inspecting (RefreshVisibilityByLocation early-return → không gọi
            // RefreshKeywordPositions → ContentSizeFitter chưa kịp update → TryHandleKeywordClick miss).
            var kwContainerRT = keywordsContainer.GetComponent<RectTransform>();
            if (kwContainerRT != null)
                UnityEngine.UI.LayoutRebuilder.ForceRebuildLayoutImmediate(kwContainerRT);

            // Đồng bộ chữ ký — RefreshStats dùng để phát hiện keyword đổi runtime
            _kwSignature = ComputeKeywordSignature();
        }

        void RefreshSkillsText()
        {
            if (skillsText == null || model == null) return;
            var sb = new StringBuilder();
            var d = model.data;
            if (d.cardType == CardType.Spell) sb.AppendLine($"[{d.spellSpeed} Spell]");
            if (!string.IsNullOrWhiteSpace(d.description)) sb.AppendLine(d.description);
            foreach (var skill in d.skills)
                if (skill != null && !string.IsNullOrWhiteSpace(skill.description))
                    sb.AppendLine("• " + skill.description);

            // Level-Up condition text — hiện khi inspect champion chưa level up
            if (d.canLevelUp && !model.hasLeveledUp && !string.IsNullOrWhiteSpace(d.levelUpCondition))
            {
                if (sb.Length > 0) sb.AppendLine();
                sb.AppendLine($" Level Up: {d.levelUpCondition}");
                var cfg2 = d.levelUpConfig;
                if (cfg2 != null)
                    switch (cfg2.conditionType)
                    {
                        case LevelUpConditionType.TriggerCount:
                            sb.Append($"  Tiến độ: {model.levelUpProgress}/{cfg2.threshold}");
                            break;
                        case LevelUpConditionType.SelfAttackReach:
                            sb.Append($"  ATK hiện tại: {model.currentAttack}/{cfg2.threshold}");
                            break;
                        case LevelUpConditionType.SelfHealthReach:
                            sb.Append($"  HP hiện tại: {model.currentHealth}/{cfg2.threshold}");
                            break;
                        default:
                            sb.Append(model.readyToLevelUp ? "  Đã đủ điều kiện!" : $"  Ngưỡng cần đạt: {cfg2.threshold}");
                            break;
                    }
            }

            skillsText.text = sb.ToString().TrimEnd();
            skillsText.gameObject.SetActive(false);
        }

        void RefreshVisibilityByLocation()
        {
            if (_inspecting || model == null) return;
            if (_faceDown) { ApplyFaceDownVisuals(); return; }
            bool inHand = model.location == CardLocation.InHand
                       || model.location == CardLocation.StagingSpell
                       || model.location == CardLocation.OnStack;
            bool onField = model.location == CardLocation.OnBench
                        || model.location == CardLocation.OnBattlefield;
            if (nameGroup) nameGroup.SetActive(inHand);
            else if (nameText) nameText.gameObject.SetActive(inHand);
            if (manaGroup) manaGroup.SetActive(inHand);
            bool isUnit = model.data.cardType == CardType.Unit;
            if (statsGroup) statsGroup.SetActive(isUnit);
            if (isUnit && onField) RefreshStatPositions();
            RefreshKeywordPositions(onField);
            RefreshArtworkUV(onField);

            // cardFrame: ẩn khi trên sân (chỉ artwork + stats hiện) → tránh nền đen
            if (cardFrame != null) cardFrame.enabled = !onField;
            // inspectOverlay: chỉ hiện khi inspect — luôn ẩn ở đây
            if (inspectOverlay != null) inspectOverlay.SetActive(false);
        }

        void RefreshKeywordPositions(bool onField)
        {
            if (keywordsContainer == null || model == null || !onField) return;
            var rt = keywordsContainer.GetComponent<RectTransform>();
            if (rt == null) return;
            if (model.belongsToPlayer)
            {
                rt.anchorMin = new Vector2(0f, 0f); rt.anchorMax = new Vector2(1f, 0f);
                rt.pivot = new Vector2(0.5f, 0.40f); rt.anchoredPosition = Vector2.zero;
            }
            else
            {
                rt.anchorMin = new Vector2(0f, 1f); rt.anchorMax = new Vector2(1f, 1f);
                rt.pivot = new Vector2(0.5f, 0.60f); rt.anchoredPosition = Vector2.zero;
            }
            UnityEngine.UI.LayoutRebuilder.ForceRebuildLayoutImmediate(rt);
        }

        void RefreshStatPositions()
        {
            if (statsGroup == null) return;
            var rt = statsGroup.GetComponent<RectTransform>();
            if (rt == null) return;
            if (model.belongsToPlayer)
            {
                rt.anchorMin = new Vector2(0f, 1f); rt.anchorMax = new Vector2(1f, 1f);
                rt.pivot = new Vector2(0.5f, 1f);
                rt.anchoredPosition = new Vector2(0f, -statsEdgeOffset);
            }
            else
            {
                rt.anchorMin = new Vector2(0f, 0f); rt.anchorMax = new Vector2(1f, 0f);
                rt.pivot = new Vector2(0.5f, 0f);
                rt.anchoredPosition = new Vector2(0f, statsEdgeOffset);
            }
        }

        void RefreshHighlights()
        {
            if (model == null) return;
            if (attackingHighlight) attackingHighlight.enabled = model.state == CardState.Attacking;
            if (blockingHighlight) blockingHighlight.enabled = model.state == CardState.Blocking;
            if (supportedHighlight) supportedHighlight.enabled = model.isSupportTarget;
        }

        public void SetSelected(bool selected)
        {
            if (selectedHighlight) { selectedHighlight.enabled = selected; if (selected) selectedHighlight.color = targetableColor; }
        }
        public void SetConfirmed(bool confirmed)
        {
            if (selectedHighlight) { selectedHighlight.enabled = confirmed; if (confirmed) selectedHighlight.color = confirmedTargetColor; }
        }

        // ── Spell Circle Mode ─────────────────────────────────────
        public void SetSpellCircleMode(bool on) { _inCircleMode = on; if (on) ApplyCircleVisuals(); else RemoveCircleVisuals(); }

        void ApplyCircleVisuals()
        {
            float side = Mathf.Min(circleSize.x, circleSize.y);
            ApplySizeInternal(side, side); ForceArtworkUV(side, side);
            if (_circleClipGO == null && artworkRawImage != null)
            {
                _artworkOriginalParent = artworkRawImage.transform.parent;
                _artworkOriginalSiblingIdx = artworkRawImage.transform.GetSiblingIndex();
                _circleClipGO = new GameObject("_CircleClip");
                var clipRT = _circleClipGO.AddComponent<RectTransform>();
                _circleClipGO.transform.SetParent(transform, false);
                clipRT.anchorMin = Vector2.zero; clipRT.anchorMax = Vector2.one;
                clipRT.offsetMin = Vector2.zero; clipRT.offsetMax = Vector2.zero;
                var img = _circleClipGO.AddComponent<Image>();
                img.sprite = CreateCircleSprite(); img.raycastTarget = false;
                var mask = _circleClipGO.AddComponent<Mask>();
                mask.showMaskGraphic = false;
                _circleClipGO.transform.SetSiblingIndex(0);
                artworkRawImage.transform.SetParent(_circleClipGO.transform, false);
            }
            if (cardFrame != null) cardFrame.enabled = false;
            if (nameGroup) nameGroup.SetActive(false);
            if (manaGroup) manaGroup.SetActive(false);
            if (statsGroup) statsGroup.SetActive(false);
            if (skillsText) skillsText.gameObject.SetActive(false);
        }

        void RemoveCircleVisuals()
        {
            if (artworkRawImage != null && _artworkOriginalParent != null)
            {
                artworkRawImage.transform.SetParent(_artworkOriginalParent, false);
                artworkRawImage.transform.SetSiblingIndex(_artworkOriginalSiblingIdx);
                _artworkOriginalParent = null;
            }
            if (_circleClipGO != null) { Destroy(_circleClipGO); _circleClipGO = null; }
            if (cardFrame != null) cardFrame.enabled = true;
            if (!_inCircleMode)
            {
                if (_hasFieldSize) { ApplySizeInternal(_fieldCardSize.x, _fieldCardSize.y); ForceArtworkUV(_fieldCardSize.x, _fieldCardSize.y); }
                else ResetToPortraitSize();
                RefreshVisibilityByLocation();
            }
        }

        // Fix: cache circle sprite — trước đây mỗi lần gọi tạo Texture2D mới
        // không bao giờ Destroy → leak GPU memory tích lũy trong session.
        static Sprite _cachedCircleSprite;
        static Sprite CreateCircleSprite(int res = 128)
        {
            if (_cachedCircleSprite != null) return _cachedCircleSprite;
            var tex = new Texture2D(res, res, TextureFormat.RGBA32, false);
            float c = res * 0.5f;
            var pixels = new Color32[res * res];
            for (int y = 0; y < res; y++)
                for (int x = 0; x < res; x++)
                {
                    float dx = x - c + 0.5f, dy = y - c + 0.5f;
                    pixels[y * res + x] = dx * dx + dy * dy <= c * c
                        ? new Color32(255, 255, 255, 255) : new Color32(0, 0, 0, 0);
                }
            tex.SetPixels32(pixels); tex.Apply();
            _cachedCircleSprite = Sprite.Create(tex, new Rect(0, 0, res, res), new Vector2(0.5f, 0.5f));
            return _cachedCircleSprite;
        }
    }
}