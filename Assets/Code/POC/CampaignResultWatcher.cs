using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.SceneManagement;
using TMPro;
using LoRClone.Data;
using LoRClone.Model;
using LoRClone.Controller;

namespace LoRClone.View
{
    /// <summary>
    /// Đặt trong GAME SCENE (scene đánh bài) — 1 GameObject trống + component này.
    ///
    /// Khi trận là tầng Leo Tháp (CampaignContext.Active):
    ///   • Áp máu Nexus 2 bên theo LevelData.playerHealth / enemyHealth (0 = mặc định).
    ///   • Đặt SẴN unit lên sân theo LevelData.startUnits (tutorial / dàn cảnh).
    ///   • Áp mana khởi đầu theo LevelData.playerStartMana / enemyStartMana (SAU refill round 1).
    ///   • Game over → chấm thắng/thua; thắng lần đầu → cộng card quà vào kho + mở tầng tiếp.
    ///   • Hiện popup kết quả + nút VỀ SẢNH.
    ///
    /// Trận thường (ĐẤU TRẬN từ lobby) → component này không làm gì cả.
    /// </summary>
    public class CampaignResultWatcher : MonoBehaviour
    {
        [Header("Style")]
        public Color panelColor = new Color(0.075f, 0.095f, 0.135f);
        public Color accentColor = new Color(0.940f, 0.780f, 0.350f);
        public Color winColor = new Color(0.350f, 0.850f, 0.480f);
        public Color loseColor = new Color(0.900f, 0.350f, 0.350f);
        public Color btnColor = new Color(0.230f, 0.460f, 0.880f);

        bool _handled;
        bool _manaApplied;
        // Loại node vừa thắng (bản đồ) — quyết định có hiện màn CHỌN SỨC MẠNH hay không.
        MapNodeType _wonNodeType;
        bool _wonNodeKnown;

        IEnumerator Start()
        {
            if (!CampaignContext.Active) yield break; // trận thường — không can thiệp

            // Chờ GameController + model sẵn sàng
            while (GameController.Instance == null || GameController.Instance.model == null)
                yield return null;

            // Máu Nexus (không bị refill theo round → set 1 lần là đủ)
            ApplyLevelHealth();

            // Chờ vài frame để các ZoneView (bench) kịp Init + subscribe event, rồi đặt unit sẵn.
            yield return null; yield return null; yield return null;
            PlacePreUnits();

            // Con Đường Anh Hùng: áp Sức Mạnh hành trình (buff nexus + toàn bộ đơn vị) SAU khi
            // deck đã build + unit sẵn đã đặt → mọi đơn vị (kể cả pre-placed) đều được buff.
            CampaignRun.ApplyNexusAndAllies(GameController.Instance.model.player);
            // M13: HP Nexus XUYÊN SUỐT — đặt máu theo lượng mang từ trận trước (không hồi mỗi trận).
            CampaignRun.ApplyPersistentNexus(GameController.Instance.model.player);
            // THÁP mạnh dần: áp các Sức Mạnh mà người chơi ĐÃ BỎ khi chọn buff cho địch.
            CampaignRun.ApplyEnemyNexusAndAllies(GameController.Instance.model.enemy);
            // M10: luật riêng BOSS — tháp mạnh thêm ở trận boss.
            var curNode = (CampaignContext.map != null && CampaignContext.currentNodeId >= 0)
                ? CampaignContext.map.Get(CampaignContext.currentNodeId) : null;
            // Luật trận (Encounter Modifier): áp cho MỌI node combat theo ải + loại node
            // (specialRule cả ải, miniBoss/boss/elite riêng). Boss/MiniBoss không cấu hình → fallback buff cũ.
            {
                var nt = curNode != null ? curNode.type : MapNodeType.Battle;
                CampaignRun.ApplyEncounterStart(GameController.Instance.model.enemy, nt);
                // KHÔNG hiện banner khi vào trận nữa — luật trận xem ở TAB PREVIEW (map) + Pause Menu.
            }

            // BỊ ĐỘNG: Star Power của tướng (kiểu PoC) — áp buff đầu trận theo Sao, không cần bấm.
            var champPower = FindChampionPower();
            if (champPower != null)
                ChampionPowerRuntime.ApplyPassive(champPower, GameController.Instance.model.player,
                                                  CampaignRun.championStar);

            // CHỦ ĐỘNG (1 nút gộp): CỘNG HƯỞNG VẬN MỆNH theo buff đã gom trong ải.
            SpawnResonanceButton();

            GameController.Instance.OnStateChanged += OnState;
        }

        // 1 nút gộp: hệ Cộng Hưởng mạnh nhất đang mở → nút chủ động scale theo cấp. Chưa mở → không nút.
        void SpawnResonanceButton()
        {
            if (!ResonanceSystem.Dominant(out var type, out int level, out int count)) return;
            var actions = ResonanceSystem.BuildActions(type, level);
            string title = $"Cộng Hưởng\n{ResonanceSystem.TypeName(type)} Lv{level}";
            string sub = ResonanceSystem.EffectDesc(type, level);
            string flash = $"{ResonanceSystem.TypeName(type)} Lv{level}";
            int cost = ResonanceSystem.ManaCost(level);
            string hex = ResonanceSystem.TypeHex(type);
            ChampionPowerController.Spawn(title, sub, cost, 1, hex, flash,
                me => ChampionPowerRuntime.ApplyActions(actions, me, "Cộng Hưởng " + ResonanceSystem.TypeName(type)));
        }

        // Star Power bị động: tìm ChampionPowerData khớp tên tướng đang chơi trong CampaignData.championPowers.
        ChampionPowerData FindChampionPower()
        {
            var champ = CampaignRun.champion;
            var camp = CampaignContext.campaign;
            if (champ == null || camp == null || camp.championPowers == null) return null;
            string name = champ.championName;
            if (string.IsNullOrEmpty(name)) return null;
            foreach (var pw in camp.championPowers)
                if (pw != null && pw.championName == name)
                    return pw; // 1 power/tướng
            return null;
        }

        void ApplyLevelHealth()
        {
            var lv = CampaignContext.CurrentLevel;
            if (lv == null) return;
            var m = GameController.Instance.model;
            if (m == null) return;
            if (lv.playerHealth > 0 && m.player != null) m.player.SetStartingHealth(lv.playerHealth);
            if (lv.enemyHealth > 0 && m.enemy != null) m.enemy.SetStartingHealth(lv.enemyHealth);
        }

        void PlacePreUnits()
        {
            var lv = CampaignContext.CurrentLevel;
            if (lv == null || lv.startUnits == null) return;
            var m = GameController.Instance.model;
            if (m == null) return;
            foreach (var su in lv.startUnits)
            {
                if (su == null || su.unit == null) continue;
                var pm = su.onEnemySide ? m.enemy : m.player;
                if (pm == null) continue;
                pm.SummonToBenchAt(su.unit, su.benchSlot);
            }
        }

        void ApplyLevelMana()
        {
            var lv = CampaignContext.CurrentLevel;
            if (lv == null) return;
            var m = GameController.Instance.model;
            if (m == null) return;
            if (lv.playerStartMana > 0 && m.player != null) m.player.SetStartingMana(lv.playerStartMana);
            if (lv.enemyStartMana > 0 && m.enemy != null) m.enemy.SetStartingMana(lv.enemyStartMana);
        }

        void OnDestroy()
        {
            if (GameController.Instance != null)
                GameController.Instance.OnStateChanged -= OnState;
        }

        void OnState(GameModel m)
        {
            if (m == null) return;

            // Áp mana khởi đầu 1 lần — SAU khi rời Mulligan (round 1 đã RefillMana xong).
            if (!_manaApplied && m.phase != GamePhase.Mulligan && m.phase != GamePhase.GameOver)
            {
                _manaApplied = true; // set TRƯỚC để RaiseStateChanged tái nhập không lặp
                ApplyLevelMana();
                CampaignRun.ApplyMana(m.player);       // Sức Mạnh của bạn: +mana
                CampaignRun.ApplyEnemyMana(m.enemy);   // Sức Mạnh của THÁP: +mana cho địch
                CampaignRun.ApplyDraw(m.player);       // Nâng cao: rút thêm lá (bạn)
                CampaignRun.ApplyEnemyDraw(m.enemy);   // Nâng cao: rút thêm lá (tháp)
                // Ép View vẽ lại ngay (view mana nghe OnStateChanged, không phải OnManaChanged)
                GameController.Instance?.RaiseStateChanged();
            }

            if (_handled || m.phase != GamePhase.GameOver) return;
            _handled = true;

            bool playerWon = m.player.IsAlive && !m.enemy.IsAlive;
            var level = CampaignContext.CurrentLevel;

            // Mốc 2: thắng node bản đồ → đánh dấu done + tiến cột (map tự mở node kế).
            // Mốc 3: cộng mastery XP cho champion theo loại node (Boss > Elite > Battle).
            if (playerWon && CampaignContext.map != null && CampaignContext.currentNodeId >= 0)
            {
                var node = CampaignContext.map.Get(CampaignContext.currentNodeId);
                _wonNodeKnown = node != null;
                if (node != null) _wonNodeType = node.type; // nhớ loại node để quyết định chọn sức mạnh
                CampaignContext.map.CompleteNode(CampaignContext.currentNodeId);
                CampaignContext.currentNodeId = -1;

                // M2: thưởng theo SAO của ải (× từng foe hạ). MiniBoss ≈ giữa Elite và Boss.
                float star = Mathf.Max(1f, CampaignRun.starDifficulty);
                float rm = CampaignRun.adventure != null ? CampaignRun.adventure.rewardMultiplier : 1f;
                int mul = Mathf.Max(1, Mathf.RoundToInt(star * rm));
                if (node != null)
                {
                    int gBase = node.type == MapNodeType.Boss ? 30
                              : node.type == MapNodeType.MiniBoss ? 22
                              : node.type == MapNodeType.Elite ? 15 : 8;
                    CampaignRun.AddGold(gBase * mul);
                    // MỐC 12: KHÔNG rơi Lõi lẻ mỗi node nữa. Vàng kiếm trong ải sẽ QUY ĐỔI thành Lõi
                    // khi TỔNG KẾT (thắng/thua/kết thúc sớm) — 1 đồng tiền chung, đỡ lằng nhằng.
                }
                if (CampaignRun.champion != null && node != null)
                {
                    int xBase = node.type == MapNodeType.Boss ? 60
                              : node.type == MapNodeType.MiniBoss ? 45
                              : node.type == MapNodeType.Elite ? 35 : 20;
                    int xp = Mathf.RoundToInt(xBase * (0.6f + 0.4f * mul)); // XP tăng theo sao (vừa phải)
                    ProgressStore.AddMasteryXp(CampaignRun.champion.championName, xp);
                    int eBase = node.type == MapNodeType.Boss ? 40
                              : node.type == MapNodeType.MiniBoss ? 28
                              : node.type == MapNodeType.Elite ? 20 : 10;
                    ProgressStore.AddEssence(eBase * mul);
                }

                // Mốc 4: thắng ELITE → cho nhặt Relic. Mốc 10: hạ BOSS → hiện TỔNG KẾT (hết run).
                bool hasRelicPool = CampaignContext.campaign != null
                    && CampaignContext.campaign.relicPool != null
                    && CampaignContext.campaign.relicPool.Count > 0;
                if (node != null && node.type == MapNodeType.Elite && hasRelicPool) CampaignContext.pendingRelic = true;
                if (node != null && node.type == MapNodeType.Boss)
                {
                    CampaignContext.pendingRunSummary = true;
                    if (CampaignRun.adventure != null)
                    {
                        bool firstClear = AdventureProgress.MarkCompleted(             // M12: tiến độ theo champion
                            CampaignRun.champion != null ? CampaignRun.champion.championName : "",
                            CampaignRun.adventure.adventureName);                      // qua ải → mở ải sao kế
                        // M14: LẦN ĐẦU vượt ải → thưởng 1 RƯƠNG (bậc ngẫu nhiên theo sao) vào túi.
                        if (firstClear)
                        {
                            var tier = ChestSystem.RollTier(CampaignRun.starDifficulty);
                            ProgressStore.AddChest((int)tier);
                            CampaignRun.pendingChestTier = (int)tier;                  // để tổng kết cho MỞ NGAY
                        }
                    }
                }

                // M13: lưu máu Nexus còn lại (mang sang trận kế); qua MID-BOSS → hồi ĐẦY.
                CampaignRun.nexusCurrent = Mathf.Clamp(m.player.health, 1, Mathf.Max(1, CampaignRun.nexusMax));
                if (node != null && node.type == MapNodeType.MiniBoss)
                {
                    CampaignRun.HealNexusFull();
                    bool hasPower = CampaignContext.campaign != null
                        && CampaignContext.campaign.powerCores != null
                        && CampaignContext.campaign.powerCores.Count > 0;
                    if (hasPower) CampaignRun.PendingPowerReward = true;   // map sẽ hiện chọn thêm 1 Lõi
                }

                RunSaveStore.Save(); // Mốc 6: lưu tiến độ node ngay sau khi thắng
            }

            // M12: THUA trong ải → TỔNG KẾT & kết thúc run (không cho spam lại 1 ải gây nhàm).
            if (!playerWon && CampaignContext.map != null)
            {
                CampaignContext.pendingRunSummary = true;
                RunSaveStore.Save();
            }

            bool firstTime = false;
            var rewardNames = new List<string>();
            if (playerWon && level != null)
            {
                if (level.rewardCards != null)
                    foreach (var c in level.rewardCards)
                        if (c != null) rewardNames.Add(c.cardName);
                firstTime = ProgressStore.CompleteLevel(CampaignContext.levelIndex, rewardNames);
            }

            ShowResult(playerWon, level, rewardNames, firstTime);
        }

        // ── Popup kết quả ─────────────────────────────────────────
        void ShowResult(bool won, LevelData level, List<string> rewards, bool firstTime)
        {
            var canvasGO = new GameObject("CampaignResultCanvas");
            var canvas = canvasGO.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 999;
            canvasGO.AddComponent<GraphicRaycaster>();

            var dim = MakeRect("Dim", canvasGO.GetComponent<RectTransform>(), Vector2.zero, Vector2.one);
            SetImage(dim, new Color(0.02f, 0.03f, 0.05f, 0.88f));

            // BẢN ĐỒ: node cuối = Boss. Mọi node CHIẾN ĐẤU khác (Battle/Elite/MiniBoss) đều cho chọn Sức Mạnh
            // → thống nhất giống HSR: thắng trận nào (trừ Boss cuối) cũng được chọn.
            bool isFinal;
            if (CampaignContext.map != null && _wonNodeKnown)
                isFinal = _wonNodeType == MapNodeType.Boss;
            else // fallback hệ tầng cũ (không có bản đồ)
                isFinal = CampaignContext.campaign == null || CampaignContext.campaign.levels == null
                          || CampaignContext.levelIndex >= CampaignContext.campaign.levels.Count - 1;
            bool offerPower = won && !isFinal; // thắng + không phải Boss cuối → chọn Sức Mạnh

            // Panel to hơn khi có màn chọn Sức Mạnh
            Vector2 pMin = offerPower ? new Vector2(0.26f, 0.12f) : new Vector2(0.32f, 0.30f);
            Vector2 pMax = offerPower ? new Vector2(0.74f, 0.88f) : new Vector2(0.68f, 0.70f);
            var panel = MakeRect("Panel", dim, pMin, pMax);
            RoundImg(panel, new Color(0.10f, 0.12f, 0.17f, 0.995f));

            // Dải màu trạng thái trên đỉnh (xanh thắng / đỏ thua)
            var topbar = MakeRect("TopBar", panel, new Vector2(0f, 0.93f), new Vector2(1f, 1f));
            RoundImg(topbar, won ? new Color(0.18f, 0.42f, 0.28f) : new Color(0.42f, 0.18f, 0.20f));

            string levelTag = level != null
                ? $"Tầng {CampaignContext.levelIndex + 1} — {level.levelName}" : "";

            SpawnLabel(panel, won ? "CHIẾN THẮNG" : "THẤT BẠI", 30f,
                won ? winColor : loseColor, TextAlignmentOptions.Center, true,
                new Vector2(0.05f, 0.90f), new Vector2(0.95f, 0.995f));
            SpawnLabel(panel, levelTag, 14f, new Color(0.72f, 0.78f, 0.9f, 0.8f),
                TextAlignmentOptions.Center, false,
                new Vector2(0.05f, 0.855f), new Vector2(0.95f, 0.905f));

            // Điều hướng về sảnh (dùng chung cho các nút) — mở lại màn Leo Tháp, không đứng ở menu chính.
            void GoLobby()
            {
                string scene = CampaignContext.returnSceneName;
                CampaignContext.reopenCampaign = true; // LobbyMenuView đọc cờ này để tự mở Leo Tháp
                CampaignContext.Clear();
                ScreenFade.LoadScene(string.IsNullOrEmpty(scene) ? "Lobby" : scene);
            }

            if (offerPower)
            {
                Destroy(canvasGO);
                var picker = CardPickerView.Instance;
                var cards = ThreeRandomDeckCards();
                var item = RollRunItem();
                if (item == null) { GoLobby(); return; }   // library trống → không có trang bị để thưởng
                if (picker != null && cards.Count > 0)
                {
                    // 3 LÁ CHỌN hiện to, rõ (card prefab thật). Nút "XEM BUILD DECK" mở list bổ sung.
                    string title = $"CHIẾN THẮNG!  Gắn <b>{item.itemName}</b> " +
                                   $"({RarityVN(item.rarity)}) — {item.Describe()}";
                    picker.ShowWithLabels(cards, title, null, idx =>
                    {
                        if (idx >= 0 && idx < cards.Count)
                        {
                            RunItems.Attach(cards[idx].cardName, item);
                            RunSaveStore.Save();
                        }
                        GoLobby();
                    }, showCancel: true, showPanel: true,
                       extraLabel: "XEM BUILD DECK", extraAction: BuildDeckListOverlay);
                    return;
                }
                // Fallback (scene chưa có CardPickerView): dùng màn list có nút GẮN.
                ShowEquipRewardScreen(GoLobby);
                return;
            }
            else
            {
                string body;
                if (!won)
                    body = "Thử deck khác hoặc luyện thêm rồi quay lại nhé.";
                else if (isFinal)
                    body = "<color=#F0C75A>HOÀN THÀNH HÀNH TRÌNH!</color>\nBạn đã chinh phục toàn bộ tháp.";
                else if (rewards.Count == 0)
                    body = "Đã mở tầng tiếp theo!";
                else if (firstTime)
                    body = "<color=#F0C75A>QUÀ NHẬN ĐƯỢC:</color>\n" + string.Join(", ", rewards)
                         + "\n<size=70%><color=#9AA7B8>(đã cộng vào kho Leo Tháp + mở khóa Xưởng Deck)</color></size>";
                else
                    body = "Tầng này đã thắng trước đó — quà chỉ nhận 1 lần.";

                var bodyLbl = SpawnLabel(panel, body, 17f, Color.white, TextAlignmentOptions.Center, false,
                    new Vector2(0.06f, 0.30f), new Vector2(0.94f, 0.80f));
                bodyLbl.textWrappingMode = TextWrappingModes.Normal;

                MakeBtn(MakeRect("BackBtn", panel, new Vector2(0.30f, 0.08f), new Vector2(0.70f, 0.22f)),
                    new Color(0.22f, 0.42f, 0.76f), "VỀ SẢNH", 18f, Color.white, GoLobby);
            }
        }

        // ── MÀN THƯỞNG TRANG BỊ (LIST kiểu build deck) ─────────────────────────
        // Hiện TOÀN BỘ lá trong deck (chỉ số hiện tại + trang bị đang gắn = "build của mình"),
        // 3 lá viền/nút vàng là được chọn để gắn trang bị lần này. Vừa thấy build, vừa chọn.
        void ShowEquipRewardScreen(System.Action goLobby)
        {
            var item = RollRunItem();
            var distinct = DistinctDeckCards();
            if (distinct.Count == 0 || item == null) { goLobby(); return; }   // library trống → bỏ qua thưởng
            var candSet = new HashSet<string>();
            foreach (var c in ThreeRandomDeckCards()) candSet.Add(c.cardName);

            var canvasGO = new GameObject("EquipRewardCanvas");
            var canvas = canvasGO.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 800;
            canvasGO.AddComponent<GraphicRaycaster>();
            var root = canvasGO.GetComponent<RectTransform>();

            // Nền phủ full + viền vàng bo góc
            SetImage(MakeRect("BG", root, Vector2.zero, Vector2.one), new Color(0.03f, 0.045f, 0.09f, 1f));
            var border = MakeRect("Border", root, Vector2.zero, Vector2.one);
            border.offsetMin = new Vector2(14f, 14f); border.offsetMax = new Vector2(-14f, -14f);
            RoundImg(border, new Color(0.82f, 0.66f, 0.32f, 0.95f)).raycastTarget = false;
            var inner = MakeRect("Inner", root, Vector2.zero, Vector2.one);
            inner.offsetMin = new Vector2(16f, 16f); inner.offsetMax = new Vector2(-16f, -16f);
            RoundImg(inner, new Color(0.05f, 0.065f, 0.11f, 1f)).raycastTarget = false;

            // Tiêu đề
            SpawnLabel(root, "CHIẾN THẮNG — GẮN TRANG BỊ", 24f, new Color(0.96f, 0.82f, 0.45f),
                TextAlignmentOptions.Center, true, new Vector2(0.05f, 0.905f), new Vector2(0.95f, 0.965f));

            // Banner trang bị nhận được
            var banner = MakeRect("Banner", root, new Vector2(0.14f, 0.80f), new Vector2(0.86f, 0.885f));
            RoundImg(banner, RarityTint(item.rarity));
            SpawnLabel(banner, $"<b>{item.itemName}</b>  <size=80%>({RarityVN(item.rarity)})</size>" +
                $"    <color=#EAF0FF>{item.Describe()}</color>",
                16f, Color.white, TextAlignmentOptions.Center, true, new Vector2(0.03f, 0f), new Vector2(0.97f, 1f));

            SpawnLabel(root, "Deck của bạn — chọn 1 trong 3 lá (nút GẮN vàng) để gắn:", 13f,
                new Color(0.82f, 0.86f, 0.94f), TextAlignmentOptions.Center, false,
                new Vector2(0.05f, 0.755f), new Vector2(0.95f, 0.795f));

            // LIST lá deck
            float aTop = 0.745f, aBot = 0.12f, aL = 0.06f, aR = 0.94f;
            int nShown = Mathf.Min(distinct.Count, 13);
            float rowH = (aTop - aBot) / nShown;
            for (int i = 0; i < nShown; i++)
            {
                var cd = distinct[i];
                bool cand = candSet.Contains(cd.cardName);
                float top = aTop - i * rowH;
                float bot = top - rowH + 0.008f;
                var row = MakeRect("Row_" + i, root, new Vector2(aL, bot), new Vector2(aR, top));
                RoundImg(row, cand ? new Color(0.20f, 0.17f, 0.07f, 0.98f) : new Color(0.09f, 0.11f, 0.16f, 0.95f))
                    .raycastTarget = false;

                // Dải trái đánh dấu candidate
                var stripe = MakeRect("Stripe", row, new Vector2(0f, 0f), new Vector2(0.012f, 1f));
                RoundImg(stripe, cand ? new Color(0.95f, 0.78f, 0.35f, 1f) : new Color(0.30f, 0.34f, 0.42f, 0.6f))
                    .raycastTarget = false;

                // Thumbnail artwork
                var thumb = MakeRect("Thumb", row, new Vector2(0.02f, 0.12f), new Vector2(0.085f, 0.88f));
                if (cd.artwork != null)
                { var raw = thumb.gameObject.AddComponent<RawImage>(); raw.texture = cd.artwork; raw.raycastTarget = false; }
                else RoundImg(thumb, new Color(0.16f, 0.19f, 0.27f, 0.9f)).raycastTarget = false;

                int cnt = CountInDeck(cd.cardName);
                CardStats(cd, out int atk, out int hp, out int itemCount, out string itemsStr);

                SpawnLabel(row, cnt > 1
                        ? $"<b>{cd.cardName}</b> <size=78%><color=#9FB0D0>×{cnt}</color></size>"
                        : $"<b>{cd.cardName}</b>",
                    15f, Color.white, TextAlignmentOptions.Left, true,
                    new Vector2(0.10f, 0f), new Vector2(0.44f, 1f));

                string stats = cand
                    ? $"<color=#EAF0FF>{atk}/{hp}</color> <color=#7FE3A0>→ {atk + item.atkBonus}/{hp + item.hpBonus}</color>"
                    : $"<color=#EAF0FF>{atk}/{hp}</color>";
                SpawnLabel(row, stats, 14f, Color.white, TextAlignmentOptions.Center, true,
                    new Vector2(0.44f, 0f), new Vector2(0.62f, 1f));

                SpawnLabel(row, itemCount > 0 ? $"<color=#BFE9E0>◆{itemCount}</color> {itemsStr}"
                                              : "<color=#6d7ca0>— chưa gắn</color>",
                    11.5f, Color.white, TextAlignmentOptions.Left, false,
                    new Vector2(0.63f, 0f), new Vector2(0.86f, 1f)).textWrappingMode = TextWrappingModes.NoWrap;

                if (cand)
                {
                    var gan = MakeRect("Gan", row, new Vector2(0.87f, 0.18f), new Vector2(0.985f, 0.82f));
                    var capName = cd.cardName;
                    MakeBtn(gan, new Color(0.62f, 0.44f, 0.16f, 0.98f), "GẮN", 14f, Color.white, () =>
                    {
                        RunItems.Attach(capName, item);
                        RunSaveStore.Save();
                        Destroy(canvasGO);
                        goLobby();
                    });
                }
            }
            if (distinct.Count > nShown)
                SpawnLabel(root, $"… và {distinct.Count - nShown} lá khác trong deck", 11f,
                    new Color(0.6f, 0.65f, 0.75f), TextAlignmentOptions.Center, false,
                    new Vector2(0.05f, 0.10f), new Vector2(0.95f, 0.12f));

            MakeBtn(MakeRect("Skip", root, new Vector2(0.40f, 0.03f), new Vector2(0.60f, 0.093f)),
                new Color(0.30f, 0.34f, 0.42f, 0.98f), "BỎ QUA", 14f, Color.white,
                () => { Destroy(canvasGO); goLobby(); });
        }

        // Overlay chỉ-đọc: xem toàn bộ BUILD DECK (lá + chỉ số hiện tại + trang bị gắn). Nổi TRÊN picker.
        void BuildDeckListOverlay()
        {
            var distinct = DistinctDeckCards();

            var go = new GameObject("BuildListOverlay");
            var canvas = go.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 1100; // trên picker (999/1000)
            go.AddComponent<GraphicRaycaster>();
            var root = go.GetComponent<RectTransform>();

            SetImage(MakeRect("BG", root, Vector2.zero, Vector2.one), new Color(0.03f, 0.045f, 0.09f, 0.99f));
            var border = MakeRect("Border", root, Vector2.zero, Vector2.one);
            border.offsetMin = new Vector2(14f, 14f); border.offsetMax = new Vector2(-14f, -14f);
            RoundImg(border, new Color(0.82f, 0.66f, 0.32f, 0.95f)).raycastTarget = false;
            var inner = MakeRect("Inner", root, Vector2.zero, Vector2.one);
            inner.offsetMin = new Vector2(16f, 16f); inner.offsetMax = new Vector2(-16f, -16f);
            RoundImg(inner, new Color(0.05f, 0.065f, 0.11f, 1f)).raycastTarget = false;

            SpawnLabel(root, "BUILD DECK CỦA BẠN", 22f, new Color(0.96f, 0.82f, 0.45f),
                TextAlignmentOptions.Center, true, new Vector2(0.05f, 0.905f), new Vector2(0.95f, 0.965f));

            if (distinct.Count == 0)
                SpawnLabel(root, "Deck trống.", 15f, Color.white, TextAlignmentOptions.Center, false,
                    new Vector2(0.1f, 0.5f), new Vector2(0.9f, 0.6f));

            float aTop = 0.87f, aBot = 0.11f, aL = 0.06f, aR = 0.94f;
            int nShown = Mathf.Min(distinct.Count, 14);
            float rowH = nShown > 0 ? (aTop - aBot) / nShown : 0.05f;
            for (int i = 0; i < nShown; i++)
            {
                var cd = distinct[i];
                float top = aTop - i * rowH;
                float bot = top - rowH + 0.006f;
                var row = MakeRect("R_" + i, root, new Vector2(aL, bot), new Vector2(aR, top));
                CardStats(cd, out int atk, out int hp, out int itemCount, out string itemsStr);
                RoundImg(row, itemCount > 0 ? new Color(0.14f, 0.14f, 0.09f, 0.96f)
                                            : new Color(0.09f, 0.11f, 0.16f, 0.9f)).raycastTarget = false;

                var thumb = MakeRect("T", row, new Vector2(0.015f, 0.12f), new Vector2(0.075f, 0.88f));
                if (cd.artwork != null)
                { var raw = thumb.gameObject.AddComponent<RawImage>(); raw.texture = cd.artwork; raw.raycastTarget = false; }
                else RoundImg(thumb, new Color(0.16f, 0.19f, 0.27f, 0.9f)).raycastTarget = false;

                int cnt = CountInDeck(cd.cardName);
                SpawnLabel(row, cnt > 1
                        ? $"<b>{cd.cardName}</b> <size=78%><color=#9FB0D0>×{cnt}</color></size>"
                        : $"<b>{cd.cardName}</b>",
                    14f, Color.white, TextAlignmentOptions.Left, true, new Vector2(0.09f, 0f), new Vector2(0.44f, 1f));
                SpawnLabel(row, $"<color=#EAF0FF>{atk}/{hp}</color>", 14f, Color.white,
                    TextAlignmentOptions.Center, true, new Vector2(0.44f, 0f), new Vector2(0.60f, 1f));
                SpawnLabel(row, itemCount > 0 ? $"<color=#BFE9E0>◆{itemCount}</color> {itemsStr}"
                                              : "<color=#6d7ca0>— chưa gắn</color>",
                    11.5f, Color.white, TextAlignmentOptions.Left, false,
                    new Vector2(0.61f, 0f), new Vector2(0.98f, 1f)).textWrappingMode = TextWrappingModes.NoWrap;
            }
            if (distinct.Count > nShown)
                SpawnLabel(root, $"… và {distinct.Count - nShown} lá khác", 11f, new Color(0.6f, 0.65f, 0.75f),
                    TextAlignmentOptions.Center, false, new Vector2(0.05f, 0.09f), new Vector2(0.95f, 0.11f));

            MakeBtn(MakeRect("Close", root, new Vector2(0.40f, 0.03f), new Vector2(0.60f, 0.088f)),
                new Color(0.30f, 0.34f, 0.42f, 0.98f), "ĐÓNG", 14f, Color.white, () => Destroy(go));
        }

        static List<CardData> DistinctDeckCards()
        {
            var seen = new HashSet<string>();
            var list = new List<CardData>();
            if (CampaignRun.RunDeck != null)
                foreach (var c in CampaignRun.RunDeck)
                    if (c != null && seen.Add(c.cardName)) list.Add(c);
            return list;
        }

        static int CountInDeck(string name)
        {
            int n = 0;
            if (CampaignRun.RunDeck != null)
                foreach (var c in CampaignRun.RunDeck) if (c != null && c.cardName == name) n++;
            return n;
        }

        // Chỉ số HIỆN TẠI của lá (base + tổng trang bị đang gắn) + danh sách item.
        static void CardStats(CardData cd, out int atk, out int hp, out int itemCount, out string itemsStr)
        {
            atk = cd.baseAttack; hp = cd.baseHealth; itemCount = 0;
            var names = new List<string>();
            var items = RunItems.ItemsOf(cd.cardName);
            if (items != null)
                foreach (var it in items)
                { atk += it.atkBonus; hp += it.hpBonus; itemCount++; names.Add(it.itemName); }
            itemsStr = string.Join(", ", names);
        }

        // ── TRANG BỊ (fallback ô đơn giản, không dùng khi có màn LIST ở trên) ──
        void BuildEquipReward(RectTransform panel, System.Action goLobby)
        {
            SpawnLabel(panel, "GẮN TRANG BỊ", 20f, accentColor, TextAlignmentOptions.Center, true,
                new Vector2(0.05f, 0.80f), new Vector2(0.95f, 0.86f));

            var item = RollRunItem();
            // Deck rỗng bất thường HOẶC library trống (item null) → cho nút tiếp tục, khỏi kẹt.
            var cards = ThreeRandomDeckCards();
            if (cards.Count == 0 || item == null)
            {
                SpawnLabel(panel, item == null ? "Chưa cấu hình kho trang bị (CardItemLibrary)." : "Deck trống — không có lá để gắn.", 14f, new Color(0.85f, 0.88f, 0.94f),
                    TextAlignmentOptions.Center, false, new Vector2(0.06f, 0.45f), new Vector2(0.94f, 0.60f));
                MakeBtn(MakeRect("Go", panel, new Vector2(0.32f, 0.06f), new Vector2(0.68f, 0.17f)),
                    new Color(0.24f, 0.62f, 0.38f), "TIẾP TỤC", 15f, Color.white, () => goLobby());
                return;
            }

            SpawnLabel(panel, $"<b>{item.itemName}</b>  <size=78%>({RarityVN(item.rarity)})</size>", 17f, Color.white,
                TextAlignmentOptions.Center, true, new Vector2(0.05f, 0.735f), new Vector2(0.95f, 0.80f));
            SpawnLabel(panel, item.Describe(), 13f, new Color(0.82f, 0.94f, 0.88f),
                TextAlignmentOptions.Center, false, new Vector2(0.06f, 0.66f), new Vector2(0.94f, 0.735f))
                .textWrappingMode = TextWrappingModes.Normal;
            SpawnLabel(panel, "Chọn 1 lá để gắn — buff MỌI bản sao lá đó suốt hành trình:", 12.5f,
                new Color(0.85f, 0.88f, 0.94f), TextAlignmentOptions.Center, false,
                new Vector2(0.05f, 0.60f), new Vector2(0.95f, 0.655f));

            var tint = RarityTint(item.rarity);
            int n = Mathf.Max(1, cards.Count);
            float total = 0.88f, gap = 0.02f;
            float cw = (total - gap * (n - 1)) / n;
            for (int i = 0; i < cards.Count; i++)
            {
                var cd = cards[i];
                float x0 = 0.06f + i * (cw + gap);
                var cell = MakeRect("Card_" + i, panel, new Vector2(x0, 0.22f), new Vector2(x0 + cw, 0.575f));
                var ci = RoundImg(cell, tint);
                var btn = cell.gameObject.AddComponent<Button>();
                btn.targetGraphic = ci;
                var cb = btn.colors; cb.highlightedColor = new Color(1f, 0.98f, 0.88f);
                cb.pressedColor = new Color(0.8f, 0.8f, 0.85f); btn.colors = cb;

                int have = RunItems.CountOf(cd.cardName);
                string tag = have > 0 ? $"\n<size=72%><color=#BFE9E0>◆{have} đang gắn</color></size>" : "";
                SpawnLabel(cell, $"<b>{cd.cardName}</b>{tag}", 14f, Color.white,
                    TextAlignmentOptions.Center, true, new Vector2(0.05f, 0.05f), new Vector2(0.95f, 0.95f))
                    .textWrappingMode = TextWrappingModes.Normal;

                var capItem = item; var capName = cd.cardName;
                btn.onClick.AddListener(() =>
                {
                    RunItems.Attach(capName, capItem);
                    RunSaveStore.Save();
                    goLobby();
                });
            }

            MakeBtn(MakeRect("Skip", panel, new Vector2(0.36f, 0.05f), new Vector2(0.64f, 0.15f)),
                new Color(0.30f, 0.34f, 0.42f), "BỎ QUA", 13f, Color.white, () => goLobby());
        }

        // Roll 1 CardItem theo độ khó (giống ải Vật Phẩm).
        static CardItem RollRunItem()
        {
            int diff = Mathf.Max(1, CampaignRun.difficulty);
            float r = UnityEngine.Random.value;
            float epicP = 0.06f + 0.05f * (diff - 1);
            float rareP = 0.30f + 0.05f * (diff - 1);
            ItemRarity rar = r < epicP ? ItemRarity.Epic
                           : r < epicP + rareP ? ItemRarity.Rare
                           : ItemRarity.Common;
            return CardItem.RandomOf(rar);
        }

        // 3 lá ngẫu nhiên KHÁC nhau trong deck run.
        static List<CardData> ThreeRandomDeckCards()
        {
            var distinct = new List<CardData>();
            var seen = new HashSet<string>();
            foreach (var c in CampaignRun.RunDeck)
                if (c != null && seen.Add(c.cardName)) distinct.Add(c);
            var rng = new System.Random();
            for (int i = distinct.Count - 1; i > 0; i--)
            {
                int j = rng.Next(i + 1);
                var tmp = distinct[i]; distinct[i] = distinct[j]; distinct[j] = tmp;
            }
            if (distinct.Count > 3) distinct = distinct.GetRange(0, 3);
            return distinct;
        }

        static string RarityVN(ItemRarity r) =>
            r == ItemRarity.Epic ? "Sử Thi" : r == ItemRarity.Rare ? "Hiếm" : "Thường";

        static Color RarityTint(ItemRarity r) =>
            r == ItemRarity.Epic ? new Color(0.42f, 0.30f, 0.60f) :
            r == ItemRarity.Rare ? new Color(0.22f, 0.40f, 0.60f) :
                                   new Color(0.26f, 0.42f, 0.40f);

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

        static Sprite _round;
        static Sprite Round => _round != null ? _round : (_round = UISprites.RoundedRect());

        // Màu thẻ power theo hiệu ứng chính (đủ tối để chữ trắng đọc rõ).
        static Color PowerTint(RunPower p)
        {
            if (p == null) return new Color(0.22f, 0.34f, 0.56f);
            if (p.bonusNexus > 0 && p.alliesAtk == 0 && p.alliesHp == 0) return new Color(0.20f, 0.44f, 0.36f); // nexus
            if (p.bonusMana > 0) return new Color(0.24f, 0.38f, 0.62f);                                          // mana
            if (p.alliesAtk > 0 && p.alliesHp == 0) return new Color(0.56f, 0.30f, 0.28f);                       // atk
            if (p.alliesHp > 0 && p.alliesAtk == 0) return new Color(0.26f, 0.40f, 0.55f);                       // hp
            return new Color(0.38f, 0.32f, 0.56f);                                                               // hỗn hợp
        }

        static Image RoundImg(RectTransform rt, Color color)
        {
            var img = rt.gameObject.GetComponent<Image>() ?? rt.gameObject.AddComponent<Image>();
            img.sprite = Round;
            img.type = Image.Type.Sliced;
            img.color = color;
            return img;
        }

        // Nút bo góc + hover.
        static Button MakeBtn(RectTransform rt, Color color, string label, float fontSize,
            Color labelColor, UnityEngine.Events.UnityAction onClick)
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
                SpawnLabel(rt, label, fontSize, labelColor, TextAlignmentOptions.Center, true);
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