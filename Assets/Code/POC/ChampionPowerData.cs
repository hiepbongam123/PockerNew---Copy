using System.Collections.Generic;
using UnityEngine;
using LoRClone.Model;

namespace LoRClone.Data
{
    /// <summary>
    /// GIAI ĐOẠN 1 — CHAMPION POWER (kỹ năng CHỦ ĐỘNG dùng trong trận).
    /// Khác skill của lá bài (chạy theo trigger): power do NGƯỜI CHƠI bấm nút để dùng,
    /// tốn mana + giới hạn số lần mỗi trận. Đây là dấu ấn riêng của từng tướng (kiểu PoC).
    ///
    /// SETUP:
    ///   1. Create → LoRClone → Champion Power → điền championName TRÙNG với ChampionData.championName.
    ///   2. Kéo asset vào CampaignData.championPowers.
    ///   3. Vào trận với tướng đó → nút "SỨC MẠNH TƯỚNG" tự hiện bên trái màn.
    ///
    /// KHÔNG cấu hình power cho tướng nào → tướng đó không có nút (không ảnh hưởng gì).
    /// Dùng lại đúng op an toàn của RunEffects → chạy được ngay, khỏi sửa battle code.
    /// </summary>
    [CreateAssetMenu(menuName = "LoRClone/Champion Power", fileName = "ChampionPower")]
    public class ChampionPowerData : ScriptableObject
    {
        [Tooltip("PHẢI trùng ChampionData.championName để hệ thống ghép đúng tướng.")]
        public string championName = "";

        public string powerName = "Sức Mạnh";
        [TextArea(2, 4)] public string description = "";
        public Sprite icon;

        [Header("CHỦ ĐỘNG — nút bấm trong trận (hero power)")]
        [Min(0)]
        [Tooltip("Mana tiêu mỗi lần dùng.")]
        public int manaCost = 3;

        [Min(1)]
        [Tooltip("Số lần được dùng trong MỘT trận. Reset mỗi trận mới.")]
        public int chargesPerBattle = 1;

        [Tooltip("Hiệu ứng khi BẤM nút (áp NGAY lên phe mình). Nhiều dòng = làm nhiều thứ.\n" +
                 "ĐỂ TRỐNG → tướng không có nút chủ động (chỉ dùng Star Power bị động bên dưới).")]
        public List<PowerAction> actions = new List<PowerAction>();

        [Header("BỊ ĐỘNG — Star Power (kiểu Constellation PoC)")]
        [Tooltip("Buff BỊ ĐỘNG áp TỰ ĐỘNG đầu mỗi trận — KHÔNG cần bấm. Mở dần theo Sao tướng.\n" +
                 "Giống cây Sao PoC: sao càng cao mở càng nhiều tầng buff. Trống = không có bị động.")]
        public List<StarPower> starPowers = new List<StarPower>();

        /// <summary>True nếu tướng này có nút CHỦ ĐỘNG (có ít nhất 1 action).</summary>
        public bool HasActive => actions != null && actions.Count > 0;
    }

    /// <summary>1 tầng Star Power bị động — mở khi Sao tướng đạt ngưỡng (giống Constellation PoC).</summary>
    [System.Serializable]
    public class StarPower
    {
        [Tooltip("Tên tầng sao (hiện trong lịch sử buff của lá).")]
        public string label = "Sao";

        [Min(0)]
        [Tooltip("championStar >= giá trị này thì tầng buff kích hoạt. 0 = luôn có.")]
        public int unlockStar = 0;

        [Tooltip("Hiệu ứng áp đầu trận khi tầng này mở.")]
        public List<PowerAction> actions = new List<PowerAction>();
    }

    /// <summary>1 hành động của power. Dùng lại RelicOp (đã map sẵn API) → an toàn.</summary>
    [System.Serializable]
    public class PowerAction
    {
        [Tooltip("Ghi chú cho dễ nhìn (không ảnh hưởng game).")]
        public string label = "";

        public RelicOp op = RelicOp.BuffAllies;

        [Tooltip("Tham số chính: atk / số lá / mana / máu / mức giảm giá.")]
        public int a = 1;
        [Tooltip("Tham số phụ: hp (cho BuffAllies/BuffChampion).")]
        public int b = 0;

        [Tooltip("Keyword tặng (chỉ dùng cho GrantKeywordAll).")]
        public KeywordType keyword = KeywordType.Barrier;
    }

    /// <summary>
    /// Thực thi power lên player — DÙNG ĐÚNG API công khai như RunEffects (đã chứng minh chạy).
    /// Tách riêng để controller gọi mà không đụng RunEffects.
    /// </summary>
    public static class ChampionPowerRuntime
    {
        /// <summary>Áp 1 danh sách action bất kỳ (dùng chung cho power tướng + nút Cộng Hưởng).</summary>
        public static void ApplyActions(IEnumerable<PowerAction> actions, PlayerModel me, string src)
        {
            if (actions == null || me == null) return;
            if (string.IsNullOrEmpty(src)) src = "Sức Mạnh";
            foreach (var act in actions)
                if (act != null) ExecuteOne(act, me, src);
        }

        /// <summary>CHỦ ĐỘNG: chạy khi người chơi BẤM nút power tướng.</summary>
        public static void Apply(ChampionPowerData power, PlayerModel me)
        {
            if (power == null) return;
            string src = string.IsNullOrEmpty(power.powerName) ? "Sức Mạnh Tướng" : power.powerName;
            ApplyActions(power.actions, me, src);
        }

        /// <summary>
        /// BỊ ĐỘNG: áp Star Power đầu trận theo Sao tướng (kiểu Constellation PoC).
        /// Gọi 1 lần lúc bắt đầu trận, SAU ApplyNexusAndAllies.
        /// </summary>
        public static void ApplyPassive(ChampionPowerData power, PlayerModel me, int championStar)
        {
            if (power == null || me == null || power.starPowers == null) return;
            foreach (var sp in power.starPowers)
            {
                if (sp == null || sp.actions == null) continue;
                if (championStar < sp.unlockStar) continue;   // chưa đủ sao → tầng này chưa mở
                string src = string.IsNullOrEmpty(sp.label) ? "Star Power" : sp.label;
                foreach (var act in sp.actions)
                    if (act != null) ExecuteOne(act, me, src);
            }
        }

        static void ExecuteOne(PowerAction e, PlayerModel me, string src)
        {
            switch (e.op)
            {
                case RelicOp.GainMana:
                    if (e.a != 0) me.SetStartingMana(me.maxMana + e.a);
                    break;

                case RelicOp.HealNexus:
                    if (e.a != 0) me.SetStartingHealth(me.health + e.a);
                    break;

                case RelicOp.DrawCards:
                    for (int i = 0; i < e.a; i++) me.DrawCard();
                    break;

                case RelicOp.BuffAllies:
                    foreach (var c in Units(me))
                    {
                        if (e.a != 0) c.BuffAttack(e.a);
                        if (e.b != 0) c.BuffHealth(e.b);
                        // Ghi nguồn buff → panel inspect hiện "tên power +X/+Y" (khỏi băn khoăn buff từ đâu).
                        c.RecordSkillBuff(src, null, e.a, e.b, false);
                    }
                    break;

                case RelicOp.DiscountAll:
                    if (e.a > 0) foreach (var c in Units(me)) c.ReduceManaCost(e.a);
                    break;

                case RelicOp.GrantKeywordAll:
                    foreach (var c in Units(me)) c.GrantKeyword(e.keyword);
                    break;

                case RelicOp.BuffChampion:
                    var champCard = CampaignRun.champion != null ? CampaignRun.champion.championCard : null;
                    if (champCard != null)
                        foreach (var c in Units(me))
                            if (c.data == champCard || c.originalData == champCard)
                            {
                                if (e.a != 0) c.BuffAttack(e.a);
                                if (e.b != 0) c.BuffHealth(e.b);
                                c.RecordSkillBuff(src, null, e.a, e.b, false);
                            }
                    break;
            }
        }

        static IEnumerable<CardModel> Units(PlayerModel p)
        {
            foreach (var c in p.BattlefieldCards()) if (IsUnit(c)) yield return c;
            foreach (var c in p.BenchCards()) if (IsUnit(c)) yield return c;
            foreach (var c in p.hand) if (IsUnit(c)) yield return c;
            foreach (var c in p.deck) if (IsUnit(c)) yield return c;
        }

        static bool IsUnit(CardModel c) => c != null && c.data != null && c.data.cardType == CardType.Unit;
    }
}