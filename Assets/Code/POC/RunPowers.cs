using System;
using System.Collections.Generic;
using UnityEngine;
using LoRClone.Model;

namespace LoRClone.Data
{
    /// <summary>
    /// 1 SỨC MẠNH (Power) kiểu Con Đường Anh Hùng (Path of Champions).
    /// Áp cho CẢ HÀNH TRÌNH: cộng dồn buff cho toàn bộ đơn vị / nexus / mana mỗi trận.
    ///
    /// Giá trị:
    ///   alliesAtk / alliesHp — buff +ATK|+HP cho MỌI đơn vị của bạn (áp lại mỗi trận).
    ///   bonusMana            — +mana khởi đầu mỗi trận.
    ///   bonusNexus           — +máu Nexus mỗi trận.
    /// </summary>
    [Serializable]
    public class RunPower
    {
        public string name;
        public string desc;
        public int alliesAtk;
        public int alliesHp;
        public int bonusMana;
        public int bonusNexus;

        [Header("Nâng cao (0/false = tắt)")]
        [Tooltip("Giảm giá MỌI bài đi bấy nhiêu mana.")]
        public int costReduction;
        [Tooltip("Rút thêm bấy nhiêu lá ở đầu mỗi trận.")]
        public int bonusDraw;
        [Tooltip("Buff RIÊNG cho lá champion (chỉ có tác dụng ở chế độ champion).")]
        public int championAtk;
        public int championHp;
        [Tooltip("Tặng 1 keyword cho MỌI đơn vị (vd Trù Phú/Bảo Hộ).")]
        public bool grantsKeyword;
        public KeywordType keywordToGrant;

        public RunPower() { }   // cần cho Unity serialize + author signaturePower trong Inspector

        public RunPower(string name, string desc, int atk, int hp, int mana, int nexus)
        {
            this.name = name; this.desc = desc;
            alliesAtk = atk; alliesHp = hp; bonusMana = mana; bonusNexus = nexus;
        }

        public bool IsMeaningful =>
            alliesAtk != 0 || alliesHp != 0 || bonusMana != 0 || bonusNexus != 0
            || costReduction != 0 || bonusDraw != 0 || championAtk != 0 || championHp != 0
            || grantsKeyword;
    }

    /// <summary>
    /// 1 ô phần thưởng sau trận — HOẶC là Sức Mạnh (RunPower) HOẶC là Cổ Vật (RelicData).
    /// Dùng cho màn "CHỌN 1 SỨC MẠNH" nay trộn cả cổ vật.
    /// </summary>
    public class RunReward
    {
        public RunPower power;
        public RelicData relic;
        public bool IsRelic => relic != null;
        public string Name => IsRelic ? relic.relicName : (power != null ? power.name : "");
        public string Desc => IsRelic ? relic.description : (power != null ? power.desc : "");
    }

    /// <summary>
    /// Trạng thái HÀNH TRÌNH (run) hiện tại — cộng dồn các Sức Mạnh đã chọn khi leo tháp.
    /// Static → sống xuyên các lần load scene trong 1 phiên chơi (giống CampaignContext).
    ///
    /// Luồng:
    ///   • Vào tầng 1 (floor 0)     → ResetRun() (bắt đầu hành trình mới).
    ///   • Đầu mỗi trận             → ApplyNexusAndAllies() + ApplyMana() cho player.
    ///   • Thắng tầng               → Roll(3) → người chơi chọn 1 → Add().
    /// </summary>
    public static class CampaignRun
    {
        public static readonly List<RunPower> Collected = new List<RunPower>();

        // ── Mốc 3: Champion + deck cố định của hành trình ─────────
        /// <summary>Champion đang chơi (null = map không champion, chọn bài từng trận).</summary>
        public static ChampionData champion;
        /// <summary>Cấp sao của champion trong run này (1→6, suy từ mastery lúc bắt đầu). M7.</summary>
        public static int championStar = 1;

        // ── M1/M2: Ải (World Map) đang chơi + độ khó theo SAO của ải ──
        /// <summary>Ải đang chơi (null = chơi theo 3 độ khó thường).</summary>
        public static AdventureData adventure;
        /// <summary>Độ khó theo SAO của ải (0.5–5). Dùng để tính thưởng. 1 = mặc định.</summary>
        public static float starDifficulty = 1f;

        // ── M10: Vàng (in-run, cho Shop) + Độ khó ─────────────────
        public static int gold = 0;
        public static int difficulty = 1; // 1 Thường · 2 Khó · 3 Ác Mộng

        // M13: HP NEXUS XUYÊN SUỐT — máu Nexus mang qua các trận trong 1 ải (không hồi mỗi trận).
        public static int nexusMax;
        public static int nexusCurrent;
        public static bool nexusInit;

        // M14: bậc RƯƠNG vừa nhận khi vượt ải lần đầu (-1 = không có) — tổng kết đọc để hiện "mở ngay".
        public static int pendingChestTier = -1;

        // Chụp lại lúc BẮT ĐẦU run để tổng kết tính "kiếm được trong run này".
        public static int runStartXp = 0;
        public static int runStartEssence = 0;
        // MỐC 11: Lõi Cường Hóa lúc bắt đầu run → tổng kết tính "farm được trong run này".
        public static int runStartCores = 0;
        public static void AddGold(int amount) { if (amount > 0) gold += amount; }
        /// <summary>Rơi Lõi Cường Hóa (currency FARM, toàn cục). Gọi khi thắng node.</summary>
        public static void AddCores(int amount) => ProgressStore.AddCores(amount);
        public static bool SpendGold(int amount)
        {
            if (amount <= 0) return true;
            if (gold < amount) return false;
            gold -= amount; return true;
        }
        /// <summary>Deck CỐ ĐỊNH của run (dùng cho mọi trận). Reward node có thể thêm card.</summary>
        public static readonly List<CardData> RunDeck = new List<CardData>();

        // ── Sức Mạnh của THÁP (địch) — 2 buff bạn KHÔNG chọn sẽ về đây ──
        // Càng chơi, tháp càng nhận nhiều buff → khó dần.
        public static readonly List<RunPower> EnemyPowers = new List<RunPower>();
        public static void AddEnemyPower(RunPower p) { if (p != null && p.IsMeaningful) EnemyPowers.Add(p); }
        public static string EnemySummary()
        {
            if (EnemyPowers.Count == 0) return "";
            var names = new List<string>();
            foreach (var p in EnemyPowers) names.Add(p.name);
            return string.Join(", ", names);
        }

        /// <summary>
        /// Chọn 1 Sức Mạnh trong danh sách được mời: lá được chọn về BẠN,
        /// các lá còn lại thuộc về THÁP (địch mạnh dần).
        /// </summary>
        public static void PickPower(List<RunPower> offered, int chosenIndex)
        {
            if (offered == null) return;
            for (int i = 0; i < offered.Count; i++)
            {
                if (i == chosenIndex) Add(offered[i]);
                else AddEnemyPower(offered[i]);
            }
        }

        /// <summary>
        /// Roll 'count' ô phần thưởng TRỘN Sức Mạnh + Cổ Vật cho màn sau trận.
        /// relicChance = xác suất mỗi ô là cổ vật (0..1). Cổ vật đã sở hữu KHÔNG lặp lại.
        /// </summary>
        public static List<RunReward> RollRewards(int count, float relicChance = 0.34f)
        {
            var powers = Roll(count); // các Sức Mạnh khác nhau

            // Kho cổ vật khả dụng = relicPool (đã bổ sung bởi RelicFactory) trừ cái đã sở hữu.
            var avail = new List<RelicData>();
            var camp = CampaignContext.campaign;
            if (camp != null)
            {
                RelicFactory.EnsureRelics(camp);
                if (camp.relicPool != null)
                    foreach (var r in camp.relicPool)
                        if (r != null && !Relics.Contains(r)) avail.Add(r);
            }

            var rng = new System.Random();
            int thr = Mathf.Clamp(Mathf.RoundToInt(relicChance * 100f), 0, 100);
            var res = new List<RunReward>();
            for (int i = 0; i < count; i++)
            {
                bool asRelic = avail.Count > 0 && rng.Next(100) < thr;
                if (asRelic)
                {
                    int k = rng.Next(avail.Count);
                    res.Add(new RunReward { relic = avail[k] });
                    avail.RemoveAt(k);
                }
                else if (i < powers.Count)
                {
                    res.Add(new RunReward { power = powers[i] });
                }
                else if (avail.Count > 0) // hết Sức Mạnh → lấp bằng cổ vật
                {
                    int k = rng.Next(avail.Count);
                    res.Add(new RunReward { relic = avail[k] });
                    avail.RemoveAt(k);
                }
            }
            return res;
        }

        /// <summary>
        /// Chọn 1 ô phần thưởng. Ô chọn: cổ vật → AddRelic, sức mạnh → Add (vào Collected).
        /// Ô KHÔNG chọn: chỉ SỨC MẠNH mới về tay THÁP (cổ vật bỏ qua, không tăng sức địch).
        /// </summary>
        public static void PickReward(List<RunReward> offered, int chosenIndex)
        {
            if (offered == null) return;
            for (int i = 0; i < offered.Count; i++)
            {
                var rw = offered[i];
                if (rw == null) continue;
                if (i == chosenIndex)
                {
                    if (rw.IsRelic) AddRelic(rw.relic);
                    else Add(rw.power);
                }
                else if (!rw.IsRelic && rw.power != null)
                {
                    AddEnemyPower(rw.power);
                }
            }
        }

        // ── Mốc 4: Relic (cổ vật) ─────────────────────────────────
        public static readonly List<RelicData> Relics = new List<RelicData>();

        // Khả năng đặc biệt gom từ TRANG BỊ đã lắp (RunEffects đọc list này). Rebuild TƯƠI mỗi trận.
        public static readonly List<RelicEffect> ExtraEffects = new List<RelicEffect>();
        public static void AddRelic(RelicData r) { if (r != null) Relics.Add(r); }
        public static string RelicSummary()
        {
            if (Relics.Count == 0) return "";
            var names = new List<string>();
            foreach (var r in Relics) names.Add(r.relicName);
            return string.Join(", ", names);
        }

        // ── POWER (Lõi vận hành) — hệ cốt lõi PoC, per-run. Áp qua các hàm Constellation* (dùng chung hook). ──
        public static readonly List<PowerData> Powers = new List<PowerData>();
        public static bool PendingPowerReward;   // MiniBoss thắng → map hiện chọn thêm 1 Lõi
        public static void AddPower(PowerData p) { if (p != null && !Powers.Contains(p)) Powers.Add(p); }
        public static bool HasPower(PowerData p) => p != null && Powers.Contains(p);
        public static string PowerSummary()
        {
            if (Powers.Count == 0) return "";
            var names = new List<string>();
            foreach (var p in Powers) if (p != null) names.Add(p.powerName);
            return string.Join(", ", names);
        }
        // Tổng hiệu ứng power (cộng dồn) — dùng trong các hàm Cons* bên dưới.
        static int PowFirstCardDiscount() { int s = 0; foreach (var p in Powers) if (p != null) s += p.firstCardManaDiscount; return s; }
        static int PowBonusStartMana() { int s = 0; foreach (var p in Powers) if (p != null) s += p.bonusStartMana; return s; }
        static int PowBonusNexus() { int s = 0; foreach (var p in Powers) if (p != null) s += p.bonusNexus; return s; }
        static int PowFirstUnitAtk() { int s = 0; foreach (var p in Powers) if (p != null) s += p.firstUnitBuffAtk; return s; }
        static int PowFirstUnitHp() { int s = 0; foreach (var p in Powers) if (p != null) s += p.firstUnitBuffHp; return s; }
        static bool PowRefillSpell() { foreach (var p in Powers) if (p != null && p.refillSpellManaEachRound) return true; return false; }
        static bool PowExtraReroll() { foreach (var p in Powers) if (p != null && p.extraReroll) return true; return false; }
        static int PowDrawPerRound() { int s = 0; foreach (var p in Powers) if (p != null) s += p.drawPerRound; return s; }
        static int PowSpellManaPerRound() { int s = 0; foreach (var p in Powers) if (p != null) s += p.spellManaPerRound; return s; }
        static int PowUnitCostDiscount() { int s = 0; foreach (var p in Powers) if (p != null) s += p.unitCostDiscount; return s; }
        static int PowSpellCostDiscount() { int s = 0; foreach (var p in Powers) if (p != null) s += p.spellCostDiscount; return s; }

        /// <summary>Bắt đầu hành trình: nạp deck + chiêu bài + perk sao + tinh hồn/item + HANDICAP theo độ khó.</summary>
        public static void StartRun(ChampionData champ, int difficultyLevel = 1)
        {
            ResetRun();
            difficulty = Mathf.Clamp(difficultyLevel, 1, 3);
            champion = champ;
            // Chụp XP/Tinh Hồn trước khi đánh → tổng kết tính chênh lệch.
            runStartXp = champ != null ? ProgressStore.GetMasteryXp(champ.championName) : 0;
            runStartEssence = ProgressStore.Essence;
            runStartCores = ProgressStore.Cores;
            RunDeck.Clear();
            BuildRunDeck(champ);
            if (champ != null && champ.signaturePower != null && champ.signaturePower.IsMeaningful)
                Add(champ.signaturePower);

            // M7: cấp sao (từ mastery) → tặng sẵn perk. Sao cao = vào run mạnh hơn.
            championStar = champ != null ? ProgressStore.GetStar(champ.championName) : 1;
            GrantStarPerks(champ, championStar);

            // M9 Tinh Hồn (đã mua) + M8 Trang Bị (đã mở theo mastery) → áp vĩnh viễn mỗi run.
            GrantConstellationAndItems(champ);

            // BỎ handicap RunPower cũ (buff stat/keyword vô cớ lên quân địch).
            // Độ khó địch giờ = Encounter Modifier (TRANG BỊ + Nexus + cơ chế), áp ở CampaignResultWatcher.
        }

        /// <summary>M1: Bắt đầu hành trình theo ẢI (độ khó suy từ SAO của ải). Giữ nguyên setup của StartRun thường.</summary>
        public static void StartRun(ChampionData champ, AdventureData adv)
        {
            float sd = adv != null ? adv.starDifficulty : 1f;
            // Sao ải → nhãn độ khó thô (chỉ để hiển thị): 0–2★ Thường · 2.5–4★ Khó · 4.5+ Ác Mộng.
            int diff = Mathf.Clamp(Mathf.CeilToInt(sd / 2f), 1, 3);
            StartRun(champ, diff);   // ResetRun + deck + perk + item + handicap cơ bản (diff)
            adventure = adv;         // đặt SAU (StartRun→ResetRun đã xoá)
            starDifficulty = sd;
            // BỎ buff quái RunPower theo sao — độ khó địch giờ do Encounter Modifier (trang bị) lo theo sao.
        }

        /// <summary>Dựng lại RunDeck từ deck champion đã lưu (RunSaveStore gọi khi khôi phục save).</summary>
        public static void RebuildRunDeck(ChampionData champ)
        {
            RunDeck.Clear();
            BuildRunDeck(champ);
        }

        /// <summary>
        /// Làm mới item TRANG BỊ của champion cho run hiện tại.
        /// Stat item đã áp qua GrantConstellationAndItems lúc StartRun; hàm này an toàn để gọi lại
        /// khi khôi phục save (không cộng dồn). Lớp icon RunItems giữ nguyên do RunItems.Restore đã nạp.
        /// </summary>
        public static void RefreshChampionItems()
        {
            // No-op có chủ đích: tránh cộng dồn buff khi resume. Stat item nằm trong Collected (đã serialize).
        }

        /// <summary>Nạp RunDeck: ưu tiên DECK CHAMPION đã lưu (15 lá, 1 bản sao) → không thì starterDeck.</summary>
        static void BuildRunDeck(ChampionData champ)
        {
            if (champ == null) return;
            if (!string.IsNullOrEmpty(champ.championName)
                && ChampionDeckStore.TryGetResolved(champ.championName, out var saved)
                && saved != null && saved.Count > 0)
            {
                foreach (var c in saved) if (c != null && !RunDeck.Contains(c)) RunDeck.Add(c);
                if (champ.championCard != null && !RunDeck.Contains(champ.championCard))
                    RunDeck.Insert(0, champ.championCard);
                return;
            }
            if (champ.starterDeck != null && champ.starterDeck.cards != null)
                RunDeck.AddRange(champ.starterDeck.cards);
        }

        /// <summary>Luật riêng của BOSS: tháp mạnh thêm (đơn vị +2|+2, nexus +8). Gọi trong trận boss.</summary>
        // Buff boss MẶC ĐỊNH cũ — fallback khi Boss/MiniBoss không cấu hình EncounterModifier (không vỡ ải cũ).
        public static void ApplyBossModifier(PlayerModel enemy)
        {
            if (enemy == null) return;
            enemy.SetStartingHealth(enemy.health + 8);
            foreach (var c in AllUnits(enemy)) { c.BuffAttack(2); c.BuffHealth(2); }
        }

        // ── ENCOUNTER MODIFIER (Luật trận / Boss modifier) ──
        static readonly List<EncounterModifier> _activeMods = new List<EncounterModifier>();
        static EncounterModifier _runtimeMod;   // modifier MẶC ĐỊNH sinh bằng code (không phải asset) → tự Destroy.

        /// <summary>Đầu trận: gom modifier theo ải + loại node rồi áp ONE-TIME (phía địch). Lưu để per-round dùng.
        /// Ải AUTO-SINH (không setup AdventureData) → tự sinh modifier MẶC ĐỊNH theo loại node + SAO.</summary>
        public static void ApplyEncounterStart(PlayerModel enemy, MapNodeType nodeType)
        {
            _activeMods.Clear();
            if (_runtimeMod != null) { UnityEngine.Object.Destroy(_runtimeMod); _runtimeMod = null; }

            GatherMods(_activeMods, nodeType, out _runtimeMod);

            if (enemy == null) return;
            LoRClone.Model.RunItems.ClearEnemy();   // xoá đồ địch trận cũ (id khác nhau mỗi trận)

            // (1) TRANG BỊ CƠ BẢN — LUÔN áp theo node + sao (im lặng, KHÔNG phải luật/power).
            var (bSlots, bRar) = BaselineEnemyEquip(nodeType, Mathf.RoundToInt(starDifficulty));
            if (bSlots > 0) EquipEnemyUnits(enemy, bSlots, bRar);

            // (2) LUẬT/POWER ĐẶC BIỆT (modifier) — nexus, THÊM trang bị, rút lá, mana, quân sẵn.
            foreach (var mod in _activeMods)
            {
                if (mod == null) continue;
                if (mod.enemyNexusBonus != 0) enemy.SetStartingHealth(enemy.health + mod.enemyNexusBonus);
                if (mod.enemyBonusItemSlots > 0) EquipEnemyUnits(enemy, mod.enemyBonusItemSlots, mod.enemyBonusItemMinRarity);
                for (int i = 0; i < mod.enemyExtraDrawStart; i++) enemy.DrawCard();
                if (mod.enemyBonusSpellMana > 0) enemy.AddSpellMana(mod.enemyBonusSpellMana);
                if (mod.enemyStartUnits != null)
                    for (int i = 0; i < mod.enemyStartUnits.Length; i++)
                        if (mod.enemyStartUnits[i] != null) enemy.SummonToBenchAt(mod.enemyStartUnits[i], i);

                // ★ BOSS POWER — đầu trận: cấp keyword cho MỌI quân địch (áp cả deck/tay → ra sân là có sẵn).
                if (mod.bossGrantKeyword)
                    foreach (var c in AllUnits(enemy)) c.GrantKeyword(mod.bossKeyword);
            }
        }

        // Gom modifier cho 1 node: (1) AdventureData explicit → (2) POOL trong CampaignData (ải auto) → (3) code mặc định.
        // runtimeTemp = modifier code tạo (cần Destroy); asset từ pool/adventure KHÔNG destroy.
        static void GatherMods(List<EncounterModifier> outList, MapNodeType nodeType, out EncounterModifier runtimeTemp)
        {
            runtimeTemp = null;
            var camp = CampaignContext.campaign;
            int seed = BossPowerSeed();

            // Special rule (mọi trận): adventure trước, không thì pool campaign.
            var special = (adventure != null ? adventure.specialRule : null)
                          ?? PickSeeded(camp != null ? camp.adventureSpecialRules : null, seed);
            if (special != null) outList.Add(special);

            // Modifier theo loại node.
            EncounterModifier nodeMod = null;
            if (nodeType == MapNodeType.Boss)
                nodeMod = (adventure != null ? adventure.finalBossModifier : null)
                          ?? PickSeeded(camp != null ? camp.bossModifierPool : null, seed);
            else if (nodeType == MapNodeType.MiniBoss)
                nodeMod = (adventure != null ? adventure.miniBossModifier : null)
                          ?? PickSeeded(camp != null ? camp.miniBossModifierPool : null, seed);
            else if (nodeType == MapNodeType.Elite)
                nodeMod = PickSeeded(adventure != null ? adventure.eliteModifierPool : null, seed)
                          ?? PickSeeded(camp != null ? camp.eliteModifierPool : null, seed);
            if (nodeMod != null) outList.Add(nodeMod);

            // Không cấu hình gì → code mặc định (Boss/MiniBoss có power; Elite/Battle null).
            if (outList.Count == 0)
            {
                runtimeTemp = BuildDefaultModifier(nodeType, starDifficulty);
                if (runtimeTemp != null) outList.Add(runtimeTemp);
            }
        }

        // Bốc 1 modifier từ pool theo SEED (cố định trong session → preview khớp trận). seed<=0 → ngẫu nhiên.
        static EncounterModifier PickSeeded(System.Collections.Generic.IReadOnlyList<EncounterModifier> pool, int seed)
        {
            if (pool == null) return null;
            int n = 0; foreach (var x in pool) if (x != null) n++;
            if (n == 0) return null;
            int target = (seed > 0 ? seed % n : UnityEngine.Random.Range(0, n));
            int i = 0;
            foreach (var x in pool) { if (x == null) continue; if (i == target) return x; i++; }
            return null;
        }

        // TRANG BỊ CƠ BẢN của địch theo khung level design (KHÔNG phải power). Boss/độ khó cao = nhiều & xịn hơn.
        // Trả (số trang bị mỗi đơn vị, độ hiếm tối thiểu).
        public static (int slots, LoRClone.Model.ItemRarity rarity) BaselineEnemyEquip(MapNodeType t, int star)
        {
            int s = Mathf.Max(1, star);
            int bonus = t == MapNodeType.Boss ? 3 : t == MapNodeType.MiniBoss ? 2 : t == MapNodeType.Elite ? 1 : 0;
            int slots = Mathf.Clamp(s / 3 + bonus, 0, 6);   // Battle sao thấp 0-1 · Boss sao cao 5-6
            var rar = s >= 7 ? LoRClone.Model.ItemRarity.Epic
                    : s >= 3 ? LoRClone.Model.ItemRarity.Rare
                    : LoRClone.Model.ItemRarity.Common;
            return (slots, rar);
        }

        /// <summary>Mô tả trang bị CƠ BẢN của đối thủ (cho tab preview — KHÔNG phải luật trận). Trống nếu 0.</summary>
        public static string EnemyEquipPreviewText(MapNodeType nodeType)
        {
            var (slots, rar) = BaselineEnemyEquip(nodeType, Mathf.RoundToInt(starDifficulty));
            if (slots <= 0) return "";
            string rr = rar == LoRClone.Model.ItemRarity.Epic ? "Sử Thi" : rar == LoRClone.Model.ItemRarity.Rare ? "Hiếm" : "Thường";
            return $"Quân địch trang bị ~{slots} món (từ {rr})";
        }

        // Lắp N trang bị từ CardItemLibrary cho MỌI đơn vị địch (sân+tay+deck) → stat/keyword/skill HIỆN ở ô trang bị.
        static void EquipEnemyUnits(PlayerModel enemy, int slots, LoRClone.Model.ItemRarity minRarity)
        {
            if (enemy == null || slots <= 0) return;
            int libCount = LoRClone.Model.CardItem.AllFromLibrary().Count;
            int units = 0, itemsApplied = 0;
            if (libCount == 0)
                Debug.LogWarning("[EncounterModifier] Kho CardItemLibrary TRỐNG → không lắp được trang bị cho địch.");
            foreach (var c in AllUnits(enemy))
            {
                units++;
                for (int s = 0; s < slots; s++)
                {
                    var it = RollEnemyItem(minRarity);
                    if (it == null) break;   // library trống
                    LoRClone.Model.RunItems.AttachEnemy(c.id, it);   // ĐĂNG KÝ TRƯỚC → badge refresh thấy ngay khi buff fire
                    if (it.skill == null) it.skill = LoRClone.Model.CardItem.SkillForItem(it.itemName);
                    if (it.grantKeyword.HasValue) c.GrantKeyword(it.grantKeyword.Value);
                    if (it.skill != null) c.AddBonusSkill(it.skill);
                    if (it.atkBonus != 0) c.BuffAttack(it.atkBonus);
                    if (it.hpBonus != 0) c.BuffHealth(it.hpBonus);
                    c.RecordBuff(BuffRecord.FromItem(it.itemName, it.atkBonus, it.hpBonus, it.Describe()));
                    itemsApplied++;
                }
            }
            Debug.Log($"[EncounterModifier] Lắp trang bị địch: {itemsApplied} món lên {units} đơn vị (slots={slots}, minRarity={minRarity}, library={libCount}).");
        }

        static LoRClone.Model.CardItem RollEnemyItem(LoRClone.Model.ItemRarity minRarity)
        {
            int diff = Mathf.Max(1, difficulty);
            float r = UnityEngine.Random.value;
            var rar = r < 0.12f + 0.04f * diff ? LoRClone.Model.ItemRarity.Epic
                    : r < 0.50f ? LoRClone.Model.ItemRarity.Rare
                    : LoRClone.Model.ItemRarity.Common;
            if (rar < minRarity) rar = minRarity;
            return LoRClone.Model.CardItem.RandomOf(rar);
        }

        // Sinh POWER/LUẬT ĐẶC BIỆT mặc định (runtime) — CHỈ Boss/MiniBoss có (trang bị cơ bản đã lo độ khó chung).
        // Elite/Battle → null (không luật riêng; địch vẫn có trang bị baseline im lặng).
        static EncounterModifier BuildDefaultModifier(MapNodeType t, float starF)
        {
            int s = Mathf.Max(1, Mathf.RoundToInt(starF));
            var m = ScriptableObject.CreateInstance<EncounterModifier>();
            switch (t)
            {
                case MapNodeType.Boss:
                    m.modName = $"Uy Áp Của Boss · Sao {s}";
                    m.enemyNexusBonus = 8 + s;
                    m.enemyBonusItemSlots = 2;   // "Địch có THÊM 2 trang bị"
                    m.enemyBonusItemMinRarity = s >= 6 ? LoRClone.Model.ItemRarity.Epic : LoRClone.Model.ItemRarity.Rare;
                    m.enemyExtraDrawStart = 1; m.enemyDrawPerRound = 1;
                    AssignRandomBossPower(m, s, BossPowerSeed());   // ★ dấu ấn riêng, CỐ ĐỊNH theo ải (preview khớp trận)
                    break;
                case MapNodeType.MiniBoss:
                    m.modName = $"Uy Áp Mini-Boss · Sao {s}";
                    m.enemyNexusBonus = 4 + s;
                    m.enemyBonusItemSlots = 1;   // "Địch có THÊM 1 trang bị"
                    m.enemyBonusItemMinRarity = s >= 5 ? LoRClone.Model.ItemRarity.Rare : LoRClone.Model.ItemRarity.Common;
                    if (s >= 4) m.enemyManaPerRound = 1;
                    break;
                default: // Battle & Elite: không power riêng — chỉ trang bị baseline (im lặng).
                    ScriptableObject.Destroy(m); return null;
            }
            if (!m.HasOneTime && !m.HasPerRound && !m.HasBossPower) { ScriptableObject.Destroy(m); return null; }
            return m;
        }

        // Seed cố định theo ải → boss power KHÔNG đổi giữa preview và trận thật (cùng session).
        static int BossPowerSeed()
        {
            string key = adventure != null && !string.IsNullOrEmpty(adventure.adventureName) ? adventure.adventureName : "boss";
            return key.GetHashCode() & 0x7fffffff;
        }

        // ★ Gán 1 BOSS POWER (dấu ấn riêng) — CỐ ĐỊNH theo seed ải, scale nhẹ theo sao.
        static void AssignRandomBossPower(EncounterModifier m, int s, int seed)
        {
            int pick = seed % 6;
            switch (pick)
            {
                case 0:
                    m.bossPowerName = "Bất Diệt"; m.bossGrantKeyword = true; m.bossKeyword = KeywordType.Regeneration;
                    m.bossPowerDesc = "Toàn quân địch có Hồi Phục — đầu vòng hồi đầy HP."; break;
                case 1:
                    m.bossPowerName = "Cường Giáp"; m.bossGrantKeyword = true; m.bossKeyword = KeywordType.Tough;
                    m.bossPowerDesc = "Toàn quân địch có Tri Thức — nhận ít hơn 1 sát thương mỗi nguồn."; break;
                case 2:
                    m.bossPowerName = "Cuồng Nộ Tăng Tiến"; m.bossBoardAtkPerRound = 1; m.bossBoardHpPerRound = 1;
                    m.bossPowerDesc = "Mỗi vòng, quân địch trên sân +1|+1 (mạnh dần)."; break;
                case 3:
                    m.bossPowerName = "Suối Nguồn"; m.bossHealNexusPerRound = 2 + s / 2;
                    m.bossPowerDesc = $"Mỗi vòng boss hồi {2 + s / 2} máu Nexus."; break;
                case 4:
                    m.bossPowerName = "Áp Đảo"; m.bossGrantKeyword = true; m.bossKeyword = KeywordType.Overwhelm;
                    m.bossPowerDesc = "Toàn quân địch có Hủy Diệt — sát thương dư tràn lên Nexus bạn."; break;
                default:
                    m.bossPowerName = "Sát Thủ"; m.bossGrantKeyword = true; m.bossKeyword = KeywordType.QuickAttack;
                    m.bossPowerDesc = "Toàn quân địch có Săn Bắn — ra đòn trước, giết là không bị phản."; break;
            }
        }

        /// <summary>Mỗi vòng: địch ramp theo modifier (rút thêm lá / +mana / buff bảng). Gọi từ hook round-start.</summary>
        public static void EncounterModifierRoundStart(PlayerModel enemy)
        {
            if (enemy == null || _activeMods.Count == 0) return;
            foreach (var mod in _activeMods)
            {
                if (mod == null || !mod.HasPerRound) continue;
                for (int i = 0; i < mod.enemyDrawPerRound; i++) enemy.DrawCard();
                if (mod.enemyManaPerRound > 0) enemy.AddSpellMana(mod.enemyManaPerRound);

                // ★ BOSS POWER — mỗi vòng: hồi Nexus / buff quân trên sân / triệu hồi token.
                if (mod.bossHealNexusPerRound > 0) enemy.HealNexus(mod.bossHealNexusPerRound);
                if (mod.bossBoardAtkPerRound != 0 || mod.bossBoardHpPerRound != 0)
                {
                    foreach (var c in enemy.BenchCards()) if (IsUnit(c)) { if (mod.bossBoardAtkPerRound != 0) c.BuffAttack(mod.bossBoardAtkPerRound); if (mod.bossBoardHpPerRound != 0) c.BuffHealth(mod.bossBoardHpPerRound); }
                    foreach (var c in enemy.BattlefieldCards()) if (IsUnit(c)) { if (mod.bossBoardAtkPerRound != 0) c.BuffAttack(mod.bossBoardAtkPerRound); if (mod.bossBoardHpPerRound != 0) c.BuffHealth(mod.bossBoardHpPerRound); }
                }
                if (mod.bossSummonPerRound != null && enemy.BenchHasSpace)
                {
                    var tok = enemy.SummonToBench(mod.bossSummonPerRound);
                    if (tok != null && mod.bossGrantKeyword) tok.GrantKeyword(mod.bossKeyword);   // token cũng hưởng power
                }
            }
        }

        /// <summary>Tên các luật trận đang áp (cho UI hiện). Trống = không có.</summary>
        public static string EncounterModifierSummary()
        {
            if (_activeMods.Count == 0) return "";
            var names = new List<string>();
            foreach (var m in _activeMods) if (m != null) names.Add(m.modName);
            return string.Join(", ", names);
        }

        /// <summary>XEM TRƯỚC luật trận của 1 node (KHÔNG áp) — cho tab preview trên map. Trống = không có.</summary>
        public static string EncounterPreviewText(MapNodeType nodeType)
        {
            var mods = new List<EncounterModifier>();
            GatherMods(mods, nodeType, out var temp);   // cùng nguồn với trận thật → preview khớp

            var lines = new List<string>();
            foreach (var m in mods)
                if (m != null) lines.Add($"<b>{m.modName}</b>\n<size=88%><color=#CFC3A0>{m.AutoDescribe()}</color></size>");
            if (temp != null) UnityEngine.Object.Destroy(temp);
            return lines.Count > 0 ? string.Join("\n\n", lines) : "";
        }

        /// <summary>Chi tiết luật trận cho BANNER: mỗi mod 1 dòng tên đậm + mô tả. Trống = không có.</summary>
        public static string EncounterModifierBannerText()
        {
            if (_activeMods.Count == 0) return "";
            var s = new System.Text.StringBuilder();
            foreach (var m in _activeMods)
            {
                if (m == null) continue;
                if (s.Length > 0) s.Append("\n");
                s.Append($"<b>{m.modName}</b>  <size=80%><color=#D9C9A0>{m.AutoDescribe().Replace("\n", " · ")}</color></size>");
            }
            return s.ToString();
        }

        // Tinh Hồn MẶC ĐỊNH — 6 TINH HỒN kiểu HSR: mở LẦN LƯỢT 1→6 (mỗi cái cần cái trước).
        // MECHANIC-ONLY: effect chỉ mang mô tả (fields = 0, KHÔNG Add vào Collected).
        // Hiệu lực thật do các hàm Constellation* bên dưới lo (theo số sao đã mua).
        static readonly ConstellationNode[] DefaultConstellation =
        {
            new ConstellationNode { nodeName = "Tinh Hồn I",   cost = 100, requires = -1, effect = new RunPower("Tinh Hồn I",   "Giảm 1 mana lá ĐẦU TIÊN chơi mỗi vòng.", 0, 0, 0, 0) },
            new ConstellationNode { nodeName = "Tinh Hồn II",  cost = 160, requires =  0, effect = new RunPower("Tinh Hồn II",  "+1 mana khởi đầu. Lá UNIT đầu tiên mỗi vòng +1|+1.", 0, 0, 0, 0) },
            new ConstellationNode { nodeName = "Tinh Hồn III", cost = 240, requires =  1, effect = new RunPower("Tinh Hồn III", "Giảm 2 mana lá đầu tiên (dồn ★1 = giảm 3).", 0, 0, 0, 0) },
            new ConstellationNode { nodeName = "Tinh Hồn IV",  cost = 340, requires =  2, effect = new RunPower("Tinh Hồn IV",  "+10 máu Nexus. +1 lượt ĐỔI ở màn chọn card/trang bị.", 0, 0, 0, 0) },
            new ConstellationNode { nodeName = "Tinh Hồn V",   cost = 460, requires =  3, effect = new RunPower("Tinh Hồn V",   "Hồi ĐẦY spell mana mỗi vòng.", 0, 0, 0, 0) },
            new ConstellationNode { nodeName = "Tinh Hồn VI",  cost = 600, requires =  4, effect = new RunPower("Tinh Hồn VI",  "+1 mana khởi đầu (dồn ★2 = +2).", 0, 0, 0, 0) },
        };

        /// <summary>Tinh Hồn hiệu lực cho champion: tự cấu hình nếu có, không thì dùng bộ MẶC ĐỊNH.</summary>
        public static List<ConstellationNode> ConstellationFor(ChampionData champ)
        {
            if (champ != null && champ.constellation != null && champ.constellation.Count > 0)
                return champ.constellation;
            return new List<ConstellationNode>(DefaultConstellation);
        }

        // ══════════════════════════════════════════════════════════════════════
        // TINH HỒN (TINH HỒN) — perk "đổi vận hành" theo SỐ SAO đã mua (mở tuần tự 1→6).
        // Đây là 1 phần của CampaignRun (không phải hệ thống rời). Chỉ áp cho PLAYER.
        //   ★1 Giảm 1 mana lá đầu vòng · ★2 +1 mana khởi đầu + unit đầu +1|+1
        //   ★3 Giảm 2 mana lá đầu (dồn ★1 = 3) · ★4 +10 Nexus + 1 lượt ĐỔI ở màn chọn
        //   ★5 Hồi đầy spell mana mỗi vòng · ★6 +1 mana khởi đầu (dồn ★2 = +2)
        // ══════════════════════════════════════════════════════════════════════
        static bool _consFirstUnitBuffed;
        static bool _consFirstCardConsumed;   // vòng này đã chơi lá đầu (được giảm) chưa

        /// <summary>Số sao đang sở hữu (đếm node đã mua tuần tự từ đầu, 0..6).</summary>
        public static int ConstellationStars()
        {
            if (champion == null || string.IsNullOrEmpty(champion.championName)) return 0;
            int n = 0;
            for (int i = 0; i < 6; i++)
            {
                if (ProgressStore.IsConstellationBought(champion.championName, i)) n++;
                else break;   // mở tuần tự → gặp node chưa mua là dừng
            }
            return n;
        }
        static bool ConsHas(int star) => ConstellationStars() >= star;

        // Đầu trận (gọi trong ApplyNexusAndAllies / ApplyMana):
        public static int ConsBonusStartMana() { int m = 0; if (ConsHas(2)) m++; if (ConsHas(6)) m++; return m + PowBonusStartMana(); }
        public static int ConsBonusNexus() => (ConsHas(4) ? 10 : 0) + PowBonusNexus();
        public static bool ConsCanReroll() => ConsHas(4) || PowExtraReroll();   // ★4 hoặc power reroll

        static int ConsFirstCardDiscount() { int d = 0; if (ConsHas(1)) d += 1; if (ConsHas(3)) d += 2; return d + PowFirstCardDiscount(); }

        /// <summary>
        /// ★1/★3 — Giảm giá ĐỘNG cho lá bài (gán vào CardModel.ExternalCostDiscount). KHÔNG sửa data lá.
        /// Trả về mức giảm nếu: lá của PLAYER, đang trong hành trình, CHƯA chơi lá đầu vòng, và lá đang ở
        /// tay / spell zone (nơi sắp trả mana). Chơi lá đầu xong → _consFirstCardConsumed=true → mọi lá trả 0.
        /// Vì tính lại mỗi lần đọc cost nên KHÔNG BAO GIỜ cộng dồn.
        /// </summary>
        static int _consDiscountAmount;   // cache mức giảm ★1/★3 (tính 1 lần/trận, sao không đổi giữa trận)
        static int _powUnitDisc, _powSpellDisc;   // cache giảm giá theo loại (power, tính 1 lần/trận)

        static int ConstellationCostDiscountFor(CardModel card)
        {
            if (card == null || !card.belongsToPlayer) return 0;
            if (CampaignContext.map == null) return 0;   // chỉ trong hành trình Con Đường Anh Hùng
            var loc = card.location;
            if (loc != CardLocation.InHand && loc != CardLocation.StagingSpell && loc != CardLocation.OnStack) return 0;

            int total = 0;
            // Giảm giá theo LOẠI (power, bền suốt trận — dùng cache, không loop mỗi lần đọc cost).
            if (_powUnitDisc > 0 || _powSpellDisc > 0)
            {
                var ct = card.data != null ? card.data.cardType : CardType.Unit;
                if (ct == CardType.Unit) total += _powUnitDisc;
                else if (ct == CardType.Spell) total += _powSpellDisc;
            }
            // Giảm LÁ ĐẦU vòng (Tinh Hồn ★1/★3 + power firstCardManaDiscount) — chỉ tới khi chưa chơi lá đầu.
            if (_consDiscountAmount > 0 && !_consFirstCardConsumed) total += _consDiscountAmount;
            return total;
        }

        /// <summary>Đầu MỖI vòng: reset cờ "lá đầu / unit đầu" + ★5 hồi đầy spell mana. Giảm giá tự hiện lại qua cost động.</summary>
        public static void ConstellationRoundStart(PlayerModel player)
        {
            _consFirstUnitBuffed = false;
            _consFirstCardConsumed = false;
            if (player == null) return;
            if (ConsHas(5) || PowRefillSpell()) player.AddSpellMana(PlayerModel.MaxSpellMana);   // ★5 / power
            int addSpell = PowSpellManaPerRound(); if (addSpell > 0) player.AddSpellMana(addSpell);   // power: +N spell mana
            int draw = PowDrawPerRound(); for (int i = 0; i < draw; i++) player.DrawCard();  // power: rút thêm N lá
            ConsRefreshHand(player);   // ép CardView đọc lại → hiện -1 cho lá đang trên tay
        }

        /// <summary>Bắn OnStatsChanged cho mọi lá trên tay → CardView đọc lại currentManaCost (cost động vừa đổi).</summary>
        static void ConsRefreshHand(PlayerModel player)
        {
            if (player == null) return;
            foreach (var c in player.hand) if (c != null) c.RaiseStatsChanged();
        }

        /// <summary>Gọi SAU khi player chơi thành công lá ĐẦU trong vòng: đánh dấu đã dùng →
        /// MỌI lá còn lại lập tức về giá gốc (cost động trả 0). No-op từ lần 2 trở đi trong vòng.</summary>
        public static void ConstellationNotifyCardPlayed(PlayerModel player)
        {
            if (_consFirstCardConsumed) return;
            _consFirstCardConsumed = true;
            ConsRefreshHand(player);   // đã dùng lá đầu → ép CardView đọc lại → MỌI lá còn lại về giá gốc
        }

        /// <summary>Lá UNIT đầu tiên PLAYER chơi mỗi vòng +ATK|+HP (★2 = +1|+1, cộng dồn power).</summary>
        public static void ConstellationOnUnitPlayed(CardModel unit)
        {
            if (_consFirstUnitBuffed || unit == null) return;
            int atk = (ConsHas(2) ? 1 : 0) + PowFirstUnitAtk();
            int hp = (ConsHas(2) ? 1 : 0) + PowFirstUnitHp();
            if (atk == 0 && hp == 0) return;
            _consFirstUnitBuffed = true;
            if (atk != 0) unit.BuffAttack(atk);
            if (hp != 0) unit.BuffHealth(hp);
        }

        /// <summary>Reset state tinh hồn + gắn hàm giảm giá động vào CardModel (gọi mỗi khi bắt đầu ván).</summary>
        public static void ConstellationResetBattle()
        {
            _consFirstUnitBuffed = false;
            _consFirstCardConsumed = false;
            _consDiscountAmount = ConsFirstCardDiscount();   // cache 1 lần/trận (tránh đọc ProgressStore mỗi frame)
            _powUnitDisc = PowUnitCostDiscount();             // cache giảm giá theo loại (power cố định trong trận)
            _powSpellDisc = PowSpellCostDiscount();
            CardModel.ExternalCostDiscount = ConstellationCostDiscountFor;   // wire cost động (idempotent)
        }

        static void GrantConstellationAndItems(ChampionData champ)
        {
            if (champ == null) return;

            // Tinh Hồn: KHÔNG còn Add() vào Collected (tránh buff stat/mana/nexus trùng lặp).
            // Perk tinh hồn giờ là "đổi vận hành" → do các hàm Constellation* trong CHÍNH class này lo.

            // Trang Bị: KHÔNG bake ở đây nữa — áp TƯƠI mỗi trận (ApplyNexusAndAllies/ApplyMana).
            // Sửa lỗi: lắp đồ sau khi StartRun hoặc resume run cũ vẫn có tác dụng, không bị trống.
        }

        /// <summary>Buff từ TRANG BỊ đã lắp của champion — tính TƯƠI mỗi trận từ danh sách đeo hiện tại.</summary>
        static void ApplyChampionItems(PlayerModel p)
        {
            if (p == null || champion == null) return;
            ExtraEffects.Clear();
            int lvl = ProgressStore.GetMasteryLevel(champion.championName);
            int nexus = 0;
            foreach (var it in LoRClone.Model.CardItem.EquippedChampionItems(champion.championName, lvl))
                nexus += it.bonusNexus;   // TRANG BỊ CHAMPION (CardItemLibrary): +Nexus khởi đầu
            if (nexus > 0) p.SetStartingHealth(p.health + nexus);
        }

        /// <summary>Mana khởi đầu từ trang bị champion đã lắp — tính tươi mỗi trận.</summary>
        static void ApplyChampionItemsMana(PlayerModel p)
        {
            if (p == null || champion == null) return;
            int lvl = ProgressStore.GetMasteryLevel(champion.championName);
            int mana = 0;
            foreach (var it in LoRClone.Model.CardItem.EquippedChampionItems(champion.championName, lvl))
                mana += it.bonusMana;
            if (mana > 0) p.SetStartingMana(p.maxMana + mana);
        }

        /// <summary>Tặng perk theo sao khi bắt đầu run (perk cộng dồn: sao 2 → perk[0], sao 3 → perk[0,1]...).</summary>
        static void GrantStarPerks(ChampionData champ, int star)
        {
            int free = Mathf.Max(0, star - 1);
            if (free == 0) return;

            // CHỈ cấp perk khi champion CẤU HÌNH SẴN starPerks (chủ đích).
            // BỎ nhánh random cũ: trước đây champion không có starPerks sẽ bị tặng đại 'star-1'
            // Sức Mạnh ngẫu nhiên (gồm 'Trù Phú Toàn Quân'/'Khiên Thần'...) → buff bẩn không ai chọn.
            if (champ != null && champ.starPerks != null && champ.starPerks.Count > 0)
            {
                for (int i = 0; i < free && i < champ.starPerks.Count; i++)
                    if (champ.starPerks[i] != null && champ.starPerks[i].IsMeaningful) Add(champ.starPerks[i]);
            }
        }

        /// <summary>Đặt lại cấp sao khi khôi phục save (RunSaveStore gọi).</summary>
        public static void SetChampionStar(int star) => championStar = Mathf.Clamp(star, 1, 6);

        /// <summary>Thêm 1 card vào deck của hành trình (reward node).</summary>
        public static void AddCardToDeck(CardData c) { if (c != null) RunDeck.Add(c); }

        // Kho Sức Mạnh (RunPower: name, desc, +ATK, +HP, +mana, +nexus).
        // Chia theo hướng chiến thuật: Tấn công / Phòng thủ / Cân bằng / Nexus / Tempo / Hỗn hợp.
        // Thêm buff của riêng bạn qua CampaignData.powerPool (không cần sửa code).
        static readonly RunPower[] Pool =
        {
            // ── Tấn công (ATK) ──
            new RunPower("Sát Khí",        "Mọi đơn vị +1|+0.",                     1, 0, 0, 0),
            new RunPower("Lưỡi Kiếm Sắc",  "Mọi đơn vị +2|+0.",                     2, 0, 0, 0),
            new RunPower("Cuồng Nộ",       "Mọi đơn vị +3|+0 (đánh nhanh, mỏng manh).", 3, 0, 0, 0),
            // ── Phòng thủ (HP) ──
            new RunPower("Da Thép",        "Mọi đơn vị +0|+2.",                     0, 2, 0, 0),
            new RunPower("Giáp Trụ Bền",   "Mọi đơn vị +0|+3.",                     0, 3, 0, 0),
            new RunPower("Bất Khuất",      "Mọi đơn vị +0|+4 (lì đòn).",            0, 4, 0, 0),
            // ── Cân bằng ──
            new RunPower("Sức Mạnh Đồng Đội","Mọi đơn vị +1|+1.",                   1, 1, 0, 0),
            new RunPower("Rèn Luyện",      "Mọi đơn vị +1|+2.",                     1, 2, 0, 0),
            new RunPower("Huấn Luyện",     "Mọi đơn vị +2|+1.",                     2, 1, 0, 0),
            new RunPower("Chiến Binh",     "Mọi đơn vị +2|+2 (mạnh toàn diện).",    2, 2, 0, 0),
            // ── Nexus (trụ) ──
            new RunPower("Lá Chắn",        "Nexus +4 máu, đơn vị +0|+1.",           0, 1, 0, 4),
            new RunPower("Thành Trì",      "Nexus +6 máu mỗi trận.",                0, 0, 0, 6),
            new RunPower("Tường Thành",    "Nexus +10 máu (thủ chắc).",             0, 0, 0, 10),
            // ── Tempo (mana) ──
            new RunPower("Dòng Chảy Mana", "Bắt đầu mỗi trận +1 mana.",             0, 0, 1, 0),
            new RunPower("Thủy Triều",     "Bắt đầu mỗi trận +2 mana (bùng nổ sớm).",0, 0, 2, 0),
            new RunPower("Xung Phong",     "Đơn vị +2|+0 và +1 mana đầu trận.",     2, 0, 1, 0),
            new RunPower("Khởi Đầu Vững",  "Đơn vị +1|+1 và +1 mana.",              1, 1, 1, 0),
            // ── Hỗn hợp / mạnh ──
            new RunPower("Đại Quân",       "Mọi đơn vị +1|+1 và Nexus +3 máu.",     1, 1, 0, 3),
            new RunPower("Vệ Binh",        "Đơn vị +0|+2 và Nexus +3 máu.",         0, 2, 0, 3),
            new RunPower("Cân Bằng Hoàn Hảo","Đơn vị +1|+1 và +1 mana.",            1, 1, 1, 0),
            new RunPower("Bạo Chúa",       "Đơn vị +2|+2 và Nexus +4 (rất mạnh — cẩn thận nếu để tháp nhặt!).", 2, 2, 0, 4),

            // ── NÂNG CAO (hiệu ứng đặc biệt) ──
            new RunPower("Vũ Khí Nhẹ",     "Mọi bài −1 mana (ra bài sớm hơn).", 0, 0, 0, 0) { costReduction = 1 },
            new RunPower("Rèn Đúc",        "Mọi bài −2 mana (tempo cực mạnh).", 0, 0, 0, 0) { costReduction = 2 },
            new RunPower("Tiếp Viện",      "Rút thêm 1 lá ở đầu mỗi trận.",     0, 0, 0, 0) { bonusDraw = 1 },
            new RunPower("Quân Lương",     "Rút thêm 2 lá đầu trận (nhiều lựa chọn).", 0, 0, 0, 0) { bonusDraw = 2 },
            new RunPower("Trù Phú Toàn Quân","Mọi đơn vị có Trù Phú (hút máu khi gây damage).", 0, 0, 0, 0) { grantsKeyword = true, keywordToGrant = KeywordType.Lifesteal },
            new RunPower("Khiên Thần",     "Mọi đơn vị có Bảo Hộ (khiên chặn 1 đòn).", 0, 0, 0, 0) { grantsKeyword = true, keywordToGrant = KeywordType.Barrier },
            new RunPower("Anh Hùng Trỗi Dậy","Champion +3|+3 (chỉ chế độ champion).", 0, 0, 0, 0) { championAtk = 3, championHp = 3 },
            new RunPower("Huyền Thoại",    "Champion +5|+5 (dồn sức vào tướng).", 0, 0, 0, 0) { championAtk = 5, championHp = 5 },
        };

        public static int Count => Collected.Count;

        public static void ResetRun()
        {
            Collected.Clear();
            champion = null;
            championStar = 1;
            adventure = null;
            starDifficulty = 1f;
            gold = 0;
            difficulty = 1;
            runStartXp = 0;
            runStartEssence = 0;
            runStartCores = 0;
            nexusMax = 0;
            nexusCurrent = 0;
            nexusInit = false;
            RunDeck.Clear();
            Relics.Clear();
            Powers.Clear();
            PendingPowerReward = false;
            _activeMods.Clear();
            if (_runtimeMod != null) { UnityEngine.Object.Destroy(_runtimeMod); _runtimeMod = null; }
            ExtraEffects.Clear();
            EnemyPowers.Clear();
            LoRClone.Model.RunItems.ClearRun();   // FIX: run MỚI phải xoá trang bị đã gắn của run trước
                                                  // (resume: Restore() gọi ResetRun TRƯỚC rồi RunItems.Restore SAU → an toàn)
            ConstellationResetBattle();           // xoá state per-vòng của Tinh Hồn (giảm giá lá đầu, cờ ★2)
        }

        public static void Add(RunPower p) { if (p != null) Collected.Add(p); }

        /// <summary>Random 'count' Sức Mạnh KHÁC nhau từ kho (built-in + CampaignData.powerPool).</summary>
        public static List<RunPower> Roll(int count = 3)
        {
            var bag = new List<RunPower>(Pool);
            // Buff tự thêm trong Inspector (CampaignData.powerPool) — không cần sửa code.
            var extra = CampaignContext.campaign != null ? CampaignContext.campaign.powerPool : null;
            if (extra != null)
                foreach (var p in extra) if (p != null && p.IsMeaningful) bag.Add(p);

            var res = new List<RunPower>();
            var rng = new System.Random();
            for (int i = 0; i < count && bag.Count > 0; i++)
            {
                int k = rng.Next(bag.Count);
                res.Add(bag[k]);
                bag.RemoveAt(k);
            }
            return res;
        }

        /// <summary>Dòng tóm tắt các Sức Mạnh đang có (cho UI).</summary>
        public static string Summary()
        {
            if (Collected.Count == 0) return "Chưa có sức mạnh nào.";
            var names = new List<string>();
            foreach (var p in Collected) names.Add(p.name);
            return string.Join(", ", names);
        }

        // ── Áp vào 1 trận (gọi từ CampaignResultWatcher) ──────────────
        // Player: dùng Collected + Relics + champion (buff riêng champion).
        // THÁP (enemy): dùng EnemyPowers (không champion).

        /// <summary>Nexus + buff đơn vị + giảm giá + tặng keyword + buff champion. Gọi SAU build deck + đặt unit sẵn.</summary>
        public static void ApplyNexusAndAllies(PlayerModel player)
        {
            ApplyBoardEffects(player, Collected, Relics, champion, applyUnitStats: false);
            ApplyChampionItems(player);   // trang bị đã lắp → áp tươi mỗi trận + gom khả năng đặc biệt
            int consNexus = ConsBonusNexus();   // ★4: +10 máu Nexus
            if (consNexus > 0) player.SetStartingHealth(player.health + consNexus);
            ApplyChampionKeywordsAndSkills(player);  // KHẢ NĂNG ĐẶC BIỆT THẬT: keyword + skill → engine
            RunEffects.Raise(RelicTrigger.BattleStart, player);  // (cũ) bảng RunEffects — để trống nếu đã dùng ở trên
        }

        /// <summary>
        /// KHẢ NĂNG ĐẶC BIỆT THẬT — gom keyword + skill từ TRANG BỊ đã lắp (mastery-gated)
        /// và CỔ VẬT, rồi gắn vào lá CHAMPION qua chính engine (GrantKeyword + AddBonusSkill).
        /// Chạy TƯƠI mỗi trận: xoá bonus skill cũ trước để không cộng dồn khi resume/đánh lại.
        /// Keyword chạy y như keyword card thật; skill chạy theo trigger trong ExecuteMatching.
        /// </summary>
        static void ApplyChampionKeywordsAndSkills(PlayerModel p)
        {
            if (p == null || champion == null || champion.championCard == null) return;

            // 1) Gom keyword + skill + atk/hp/cost từ TRANG BỊ CHAMPION đã đeo (CardItemLibrary)
            var kws = new List<KeywordType>();
            var skills = new List<SkillData>();
            int atk = 0, hp = 0, cost = 0;

            int lvl = ProgressStore.GetMasteryLevel(champion.championName);
            foreach (var it in LoRClone.Model.CardItem.EquippedChampionItems(champion.championName, lvl))
            {
                if (it.grantKeyword.HasValue) kws.Add(it.grantKeyword.Value);
                if (it.skill != null && !skills.Contains(it.skill)) skills.Add(it.skill);
                atk += it.atkBonus; hp += it.hpBonus; cost += it.costReduction;
            }

            // Cổ vật của run
            foreach (var r in Relics)
            {
                if (r == null) continue;
                if (r.grantKeywords != null) foreach (var k in r.grantKeywords) kws.Add(k);
                if (r.skills != null) foreach (var s in r.skills) if (s != null && !skills.Contains(s)) skills.Add(s);
            }

            // 2) Gắn vào MỌI lá champion ở mọi vùng (deck/hand/bench/battlefield).
            //    Luôn ClearBonusSkills trước để áp tươi, không cộng dồn skill giữa các trận.
            //    (atk/hp/cost áp 1 lần/trận — lá champion là CardModel MỚI mỗi trận nên không tích luỹ.)
            foreach (var c in AllChampionCards(p))
            {
                c.ClearBonusSkills();
                foreach (var k in kws) c.GrantKeyword(k);
                foreach (var s in skills) c.AddBonusSkill(s);
                if (atk != 0) c.BuffAttack(atk);
                if (hp != 0) c.BuffHealth(hp);
                if (cost > 0) c.ReduceManaCost(cost);
                // Giữ lại kỹ năng của TRANG BỊ per-card (CardItem) trên lá champion — ClearBonusSkills vừa xoá.
                LoRClone.Model.RunItems.ApplySkillsOnly(c, champion.championCard.cardName);
            }
        }

        /// <summary>Mọi CardModel là lá champion (khớp championCard theo data/originalData) ở mọi vùng.</summary>
        static IEnumerable<CardModel> AllChampionCards(PlayerModel player)
        {
            var target = champion != null ? champion.championCard : null;
            if (target == null) yield break;
            foreach (var c in player.deck) if (IsChampionCard(c, target)) yield return c;
            foreach (var c in player.hand) if (IsChampionCard(c, target)) yield return c;
            foreach (var c in player.BenchCards()) if (IsChampionCard(c, target)) yield return c;
            foreach (var c in player.BattlefieldCards()) if (IsChampionCard(c, target)) yield return c;
        }

        static bool IsChampionCard(CardModel c, CardData target)
            => c != null && (c.data == target || c.originalData == target);

        /// <summary>
        /// Áp buff ẢI + CHAMPION lên 1 UNIT MỚI (bản sao loop-mode tạo giữa trận) — cùng công thức
        /// với lúc đầu trận: atk/hp/giảm-giá + keyword toàn quân; riêng lá champion thêm championAtk/Hp
        /// + keyword/skill từ trang bị & cổ vật. Chỉ gọi cho unit của NGƯỜI CHƠI.
        /// </summary>
        public static void ApplyRunBuffsToUnit(CardModel c)
        {
            if (c == null || c.data == null || c.data.cardType != CardType.Unit) return;

            // PLAYER: KHÔNG cộng stat atk/hp lẫn keyword toàn quân (core = trang bị per-card).
            // Chỉ giữ mechanic giảm giá. Keyword của player chỉ đến từ TRANG BỊ đã gắn.
            int cost = 0;
            foreach (var pw in Collected) cost += pw.costReduction;

            // TRANG BỊ CHAMPION (CardItemLibrary) — keyword/skill + atk/hp/cost cho bản sao lá champion.
            var champKws = new List<KeywordType>();
            var champSkills = new List<SkillData>();
            int champAtk = 0, champHp = 0, champCost = 0;
            if (champion != null)
            {
                int lvl = ProgressStore.GetMasteryLevel(champion.championName);
                foreach (var it in LoRClone.Model.CardItem.EquippedChampionItems(champion.championName, lvl))
                {
                    if (it.grantKeyword.HasValue) champKws.Add(it.grantKeyword.Value);
                    if (it.skill != null && !champSkills.Contains(it.skill)) champSkills.Add(it.skill);
                    champAtk += it.atkBonus; champHp += it.hpBonus; champCost += it.costReduction;
                }
            }
            foreach (var r in Relics)
            {
                if (r == null) continue;
                if (r.grantKeywords != null) foreach (var k in r.grantKeywords) champKws.Add(k);
                if (r.skills != null) foreach (var s in r.skills) if (s != null && !champSkills.Contains(s)) champSkills.Add(s);
            }

            if (cost > 0) c.ReduceManaCost(cost);

            bool isChamp = champion != null && champion.championCard != null
                           && (c.data == champion.championCard || c.originalData == champion.championCard);
            if (isChamp)
            {
                foreach (var k in champKws) c.GrantKeyword(k);
                foreach (var s in champSkills) c.AddBonusSkill(s);
                if (champAtk != 0) c.BuffAttack(champAtk);
                if (champHp != 0) c.BuffHealth(champHp);
                if (champCost > 0) c.ReduceManaCost(champCost);
            }
            // Lưu ý: stat per-card (CardItem) đã do RunItems.Apply áp ở PlayerModel.SeedCopyToDeck trước khi gọi hàm này.
        }

        /// <summary>
        /// M13 — HP NEXUS XUYÊN SUỐT: gọi SAU ApplyNexusAndAllies (đã tính máu FULL trận này).
        /// Lần đầu → chốt nexusMax = full, current = full. Trận sau → đặt (max, current mang sang).
        /// Nếu giữa ải nhận thêm buff +Nexus (full tăng) → tăng cả max lẫn current (được hồi phần tăng).
        /// </summary>
        public static void ApplyPersistentNexus(PlayerModel p)
        {
            if (p == null || CampaignContext.map == null) return; // chỉ trong hành trình (run map)
            int fresh = p.maxHealth;                              // máu FULL vừa tính xong
            if (fresh <= 0) return;
            if (!nexusInit) { nexusMax = fresh; nexusCurrent = fresh; nexusInit = true; }
            else if (fresh > nexusMax) { int delta = fresh - nexusMax; nexusMax = fresh; nexusCurrent += delta; }
            nexusCurrent = Mathf.Clamp(nexusCurrent, 1, nexusMax);
            p.SetStartingHealth(nexusMax, nexusCurrent);
        }

        /// <summary>Hồi ĐẦY Nexus (qua MID-BOSS).</summary>
        public static void HealNexusFull() { if (nexusInit) nexusCurrent = nexusMax; }

        /// <summary>Cộng mana khởi đầu (sau round-1 refill).</summary>
        public static void ApplyMana(PlayerModel player)
        {
            ApplyManaCore(player, Collected, Relics);
            ApplyChampionItemsMana(player);
            int consMana = ConsBonusStartMana();   // ★2 +1, ★6 +1 (dồn +2)
            if (consMana > 0) player.SetStartingMana(player.maxMana + consMana);
        }

        /// <summary>Rút thêm lá đầu trận (gọi cùng bước mana, khi hand đã có).</summary>
        public static void ApplyDraw(PlayerModel player)
            => ApplyDrawCore(player, Collected);

        // applyUnitStats:false → KHÔNG buff stat/keyword lên quân địch từ RunPower (bỏ "buff vô cớ").
        // Địch mạnh nhờ TRANG BỊ (Encounter Modifier) + Nexus, hiện rõ ở ô trang bị.
        public static void ApplyEnemyNexusAndAllies(PlayerModel enemy)
            => ApplyBoardEffects(enemy, EnemyPowers, null, null, applyUnitStats: false);

        public static void ApplyEnemyMana(PlayerModel enemy)
            => ApplyManaCore(enemy, EnemyPowers, null);

        public static void ApplyEnemyDraw(PlayerModel enemy)
            => ApplyDrawCore(enemy, EnemyPowers);

        // ── Core dùng chung cho cả player lẫn tháp ────────────────
        // applyUnitStats: TRUE cho THÁP (giữ scaling khó) — FALSE cho PLAYER
        // (core = trang bị per-card, KHÔNG buff stat toàn quân vô tội vạ).
        static void ApplyBoardEffects(PlayerModel p, List<RunPower> powers, List<RelicData> relics, ChampionData champ, bool applyUnitStats)
        {
            if (p == null || powers == null) return;

            int atk = 0, hp = 0, nexus = 0, cost = 0;
            foreach (var pw in powers) { atk += pw.alliesAtk; hp += pw.alliesHp; nexus += pw.bonusNexus; cost += pw.costReduction; }
            if (relics != null)
                foreach (var r in relics) { atk += r.alliesAtk; hp += r.alliesHp; nexus += r.bonusNexus; }

            if (nexus > 0) p.SetStartingHealth(p.health + nexus);

            // Keyword tặng cho mọi đơn vị (Trù Phú/Bảo Hộ...). CHỈ dành cho tháp (enemy).
            var kws = new List<KeywordType>();
            if (applyUnitStats)
                foreach (var pw in powers) if (pw.grantsKeyword) kws.Add(pw.keywordToGrant);

            // PLAYER: chặn MỌI buff toàn quân (atk/hp + keyword). Keyword/stat của player chỉ đến từ TRANG BỊ per-card.
            // Giữ nexus (máu trụ) + cost (tempo) — không phải "buff bẩn lên unit".
            if (!applyUnitStats) { atk = 0; hp = 0; }

            if (atk > 0 || hp > 0 || cost > 0 || kws.Count > 0)
                foreach (var c in AllUnits(p))
                {
                    if (atk > 0) c.BuffAttack(atk);
                    if (hp > 0) c.BuffHealth(hp);
                    if (cost > 0) c.ReduceManaCost(cost);
                    foreach (var kw in kws) c.GrantKeyword(kw);
                }

            // Máu Nexus theo CẤP champion (mỗi cấp +NexusPerLevel; chỉ player có champion).
            if (champ != null)
            {
                int lvlHp = (ProgressStore.GetMasteryLevel(champ.championName) - 1) * ProgressStore.NexusPerLevel;
                if (lvlHp > 0) p.SetStartingHealth(p.health + lvlHp);
            }

            // Buff RIÊNG lá champion (chỉ player có champion) — cũng chặn khi player (core = trang bị)
            if (applyUnitStats && champ != null && champ.championCard != null)
            {
                int cAtk = 0, cHp = 0;
                foreach (var pw in powers) { cAtk += pw.championAtk; cHp += pw.championHp; }
                if (cAtk > 0 || cHp > 0)
                    foreach (var c in AllUnits(p))
                        if (c.data == champ.championCard || c.originalData == champ.championCard)
                        {
                            if (cAtk > 0) c.BuffAttack(cAtk);
                            if (cHp > 0) c.BuffHealth(cHp);
                        }
            }
        }

        static void ApplyManaCore(PlayerModel p, List<RunPower> powers, List<RelicData> relics)
        {
            if (p == null || powers == null) return;
            int mana = 0;
            foreach (var pw in powers) mana += pw.bonusMana;
            if (relics != null) foreach (var r in relics) mana += r.bonusMana;
            if (mana > 0) p.SetStartingMana(p.maxMana + mana);
        }

        static void ApplyDrawCore(PlayerModel p, List<RunPower> powers)
        {
            if (p == null || powers == null) return;
            int draw = 0;
            foreach (var pw in powers) draw += pw.bonusDraw;
            for (int i = 0; i < draw; i++) p.DrawCard();
        }

        static IEnumerable<CardModel> AllUnits(PlayerModel player)
        {
            foreach (var c in player.deck) if (IsUnit(c)) yield return c;
            foreach (var c in player.hand) if (IsUnit(c)) yield return c;
            foreach (var c in player.BenchCards()) if (IsUnit(c)) yield return c;
            foreach (var c in player.BattlefieldCards()) if (IsUnit(c)) yield return c;
        }

        static bool IsUnit(CardModel c) => c != null && c.data != null && c.data.cardType == CardType.Unit;
    }
}