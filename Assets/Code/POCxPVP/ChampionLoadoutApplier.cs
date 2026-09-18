using System.Collections.Generic;
using UnityEngine;
using LoRClone.Model;

namespace LoRClone.Data
{
    /// <summary>
    /// BƯỚC 2 (PvP-RPG) — ÁP loadout lên 1 phe trong trận. DETERMINISTIC, KHÔNG đọc ProgressStore.
    ///
    /// ĐÂY LÀ "TẦNG DƯỚI VAN": nhận ChampionLoadout (dữ liệu thuần, tên+số) rồi buff lá champion.
    /// Vì chỉ đọc TÊN món trong loadout + resolve qua CardItemLibrary (asset GIỐNG NHAU 2 máy),
    /// nên chạy trên máy A hay B đều ra KẾT QUẢ Y HỆT → không desync.
    ///
    /// ★ ĐIỀU CẤM: tuyệt đối KHÔNG gọi ProgressStore / UnityEngine.Random / thời gian ở đây.
    ///   Mọi thứ cần có SẴN trong loadout. (Đây là tiêu chí giữ tất định cho PvP.)
    /// </summary>
    public static class ChampionLoadoutApplier
    {
        // ── TRẦN CÂN BẰNG PvP (đổi số ở đây để chỉnh balance) — clamp DETERMINISTIC, áp giống hệt 2 máy.
        //    Đặt cao (vd 999) = gần như bỏ cap. Mặc định dưới đây là mức khởi điểm, tinh chỉnh sau khi chơi thử.
        public static int CapChampionAtk = 12;           // trần +ATK cho lá champion (từ trang bị)
        public static int CapChampionHp = 12;            // trần +HP cho lá champion
        public static int CapStartNexus = 15;            // trần máu Nexus khởi đầu (mastery + power + item)
        public static int CapChampionCostReduction = 3;  // trần giảm mana lá champion (item)
        public static int CapAllUnitsCostReduction = 2;  // trần giảm mana TOÀN QUÂN (power)
        public static int CapStartMana = 3;              // trần +mana khởi đầu
        public static int CapStartDraw = 2;              // trần +rút bài đầu trận

        /// <summary>
        /// Đầu trận: áp máu Nexus + buff lá CHAMPION (atk/hp/giảm giá/keyword/skill) từ trang bị trong loadout.
        /// GỌI SAU khi deck đã build. championCard = lá đại diện champion; null → tìm lá theo TÊN (lo.championName).
        /// </summary>
        public static void Apply(PlayerModel side, ChampionLoadout lo, CardData championCard)
        {
            if (side == null || lo == null) return;

            // ── SỐ BUFF ĐÃ CHỐT trong loadout (build lúc máy có save của mình) → áp THẲNG, KHÔNG đọc library ở đây.
            //    Đây là chỗ sửa desync: trước resolve item qua AllFromLibrary → 1 máy có library 1 máy không → lệch.
            //    Cap DETERMINISTIC (giống hệt 2 máy). Đổi trần ở field Cap* trên đầu class.
            int nexus = Mathf.Clamp(lo.startNexusBonus, 0, CapStartNexus);                    // mastery + power + item (đã gộp)
            int allCost = Mathf.Clamp(lo.allUnitsCostReduction, 0, CapAllUnitsCostReduction);  // power → toàn quân
            int atk = Mathf.Clamp(lo.championAtkBonus, 0, CapChampionAtk);
            int hp = Mathf.Clamp(lo.championHpBonus, 0, CapChampionHp);
            int champCost = Mathf.Clamp(lo.championCostReduction, 0, CapChampionCostReduction);

            var kws = new List<KeywordType>();
            if (lo.championKeywords != null) foreach (var k in lo.championKeywords) kws.Add((KeywordType)k);
            var skills = ResolveSkills(lo.championSkillNames);   // skill vẫn qua library (chỉ dùng cho món CÓ skill)

            Debug.Log($"[Gauntlet] Apply: side={(side.isPlayer ? "player(host)" : "enemy(client)")}, champ='{lo.championName}' → " +
                      $"nexus+{nexus}, atk+{atk}, hp+{hp}, cost-{champCost}, toàn quân -{allCost}, kw={kws.Count}, skill={skills.Count}");

            // (1) Máu Nexus khởi đầu + giảm giá toàn quân.
            if (nexus > 0) side.SetStartingHealth(side.health + nexus);
            if (allCost > 0) foreach (var u in AllUnits(side)) u.ReduceManaCost(allCost);

            // (2) Buff lá CHAMPION. championCard null → fallback theo TÊN (lo.championName).
            bool hasChampBuff = atk > 0 || hp > 0 || champCost > 0 || kws.Count > 0 || skills.Count > 0;
            int buffed = 0;
            if (hasChampBuff)
                foreach (var c in ChampionCards(side, championCard, lo.championName))
                {
                    foreach (var k in kws) c.GrantKeyword(k);
                    foreach (var s in skills) c.AddBonusSkill(s);
                    if (atk > 0) c.BuffAttack(atk);
                    if (hp > 0) c.BuffHealth(hp);
                    if (champCost > 0) c.ReduceManaCost(champCost);
                    buffed++;
                }

            if (hasChampBuff && buffed == 0)
                Debug.LogWarning($"[Gauntlet] ✖ KHÔNG thấy lá champion để buff. championName='{lo.championName}'. " +
                                 "→ deck có lá champion không? Tên có khớp championName/championCard không?");
        }

        /// <summary>
        /// +mana khởi đầu & +rút bài — CẦN TIMING: GỌI SAU MULLIGAN vòng 1 (khối post-mulligan trong
        /// GameController.HandleMulliganConfirm) để không bị RefillMana đè và rút bài đúng nhịp.
        /// DETERMINISTIC: số nằm sẵn trong loadout, khối gọi chạy giống hệt 2 máy.
        /// </summary>
        public static void ApplyManaDraw(PlayerModel side, ChampionLoadout lo)
        {
            if (side == null || lo == null) return;
            int mana = Mathf.Clamp(lo.startManaBonus, 0, CapStartMana);   // cap deterministic
            int draw = Mathf.Clamp(lo.startDrawBonus, 0, CapStartDraw);
            if (mana > 0) side.SetStartingMana(side.maxMana + mana);
            for (int i = 0; i < draw; i++) side.DrawCard();
        }

        // ── Helpers ─────────────────────────────────────────────────────────────

        /// <summary>
        /// Tên SkillData → SkillData. Resolve qua REGISTRY tất định (Inspector-independent):
        ///   nguồn 1 = Resources.LoadAll&lt;SkillData&gt; (asset GIỐNG HỆT 2 build, KHÔNG cần gán campaign),
        ///   nguồn 2 = skill trong CardItemLibrary (bổ sung, nếu asset không nằm dưới Resources).
        /// → Dù máy kia CHƯA gán NetworkBridge.campaign, cả 2 vẫn tra ra cùng SkillData → KHÔNG desync.
        ///
        /// ★ ĐK DUY NHẤT: tên SkillData phải DUY NHẤT (như itemName/cardName). Trùng tên → có thể chọn khác asset.
        /// </summary>
        static Dictionary<string, SkillData> _skillRegistry;   // cache: name → SkillData

        static Dictionary<string, SkillData> SkillRegistry()
        {
            if (_skillRegistry != null) return _skillRegistry;
            var reg = new Dictionary<string, SkillData>();

            // Nguồn 1 — quét toàn build qua Resources (tất định, không phụ thuộc gán tay).
            var all = Resources.LoadAll<SkillData>("");
            if (all != null)
                foreach (var s in all)
                    if (s != null && !string.IsNullOrEmpty(s.name) && !reg.ContainsKey(s.name))
                        reg[s.name] = s;

            // Nguồn 2 — bổ sung từ library (phòng khi SkillData KHÔNG nằm dưới Resources).
            foreach (var it in CardItem.AllFromLibrary())
                if (it != null && it.skill != null && !string.IsNullOrEmpty(it.skill.name) && !reg.ContainsKey(it.skill.name))
                    reg[it.skill.name] = it.skill;

            _skillRegistry = reg;
            return reg;
        }

        static List<SkillData> ResolveSkills(string[] names)
        {
            var res = new List<SkillData>();
            if (names == null || names.Length == 0) return res;
            var reg = SkillRegistry();
            foreach (var n in names)
            {
                if (string.IsNullOrEmpty(n)) continue;
                if (reg.TryGetValue(n, out var s) && !res.Contains(s)) res.Add(s);
                else Debug.LogWarning($"[Gauntlet] ✖ Không tra được SkillData '{n}' (thiếu ở Resources & library). " +
                                      "→ nếu 1 máy tra được 1 máy không sẽ DESYNC. Đưa SkillData vào Resources/ hoặc library.");
            }
            return res;
        }

        /// <summary>Mọi CardModel là lá champion (khớp championCard theo data/originalData, hoặc theo TÊN) ở mọi vùng.</summary>
        static IEnumerable<CardModel> ChampionCards(PlayerModel side, CardData championCard, string championName)
        {
            foreach (var c in side.deck) if (IsChampion(c, championCard, championName)) yield return c;
            foreach (var c in side.hand) if (IsChampion(c, championCard, championName)) yield return c;
            foreach (var c in side.BenchCards()) if (IsChampion(c, championCard, championName)) yield return c;
            foreach (var c in side.BattlefieldCards()) if (IsChampion(c, championCard, championName)) yield return c;
        }

        // Mọi ĐƠN VỊ (unit) của 1 phe ở mọi vùng — để áp giảm giá toàn quân (luật PoC).
        static IEnumerable<CardModel> AllUnits(PlayerModel p)
        {
            foreach (var c in p.deck) if (IsUnit(c)) yield return c;
            foreach (var c in p.hand) if (IsUnit(c)) yield return c;
            foreach (var c in p.BenchCards()) if (IsUnit(c)) yield return c;
            foreach (var c in p.BattlefieldCards()) if (IsUnit(c)) yield return c;
        }

        static bool IsUnit(CardModel c) => c != null && c.data != null && c.data.cardType == CardType.Unit;

        static bool IsChampion(CardModel c, CardData championCard, string championName)
        {
            if (c == null) return false;
            if (championCard != null && (c.data == championCard || c.originalData == championCard)) return true;
            // Fallback theo TÊN (khi championCard null vì chưa gán NetworkBridge.campaign).
            if (!string.IsNullOrEmpty(championName))
            {
                if (c.data != null && c.data.cardName == championName) return true;
                if (c.originalData != null && c.originalData.cardName == championName) return true;
            }
            return false;
        }
    }
}