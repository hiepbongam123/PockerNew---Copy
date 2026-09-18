using System.Collections.Generic;
using LoRClone.Model;

namespace LoRClone.Data
{
    /// <summary>
    /// Mốc M-Effects — Relic/Item KÍCH HOẠT (triggered effect) thay cho "cục stat" thuần.
    /// Đây là thứ tạo BUILD kiểu Path of Champions: relic không chỉ +x/+y mà LÀM điều gì đó
    /// ở mốc trận (đầu trận / mỗi N vòng / khi đơn vị chết...).
    ///
    /// Cách dùng: RelicData thêm 1 field  →  public List<RelicEffect> effects;
    /// Rồi cấu hình effect trong Inspector cho từng relic asset (không cần code cho relic mới).
    ///
    /// Battle code gọi RunEffects.Raise(...) ở các mốc:
    ///   • BattleStart : đã wire sẵn trong CampaignRun.ApplyNexusAndAllies (chạy ngay, không cần sửa battle).
    ///   • RoundStart / OnAllyDie / OnPlayUnit... : thêm 1 dòng ở battle/round manager của bạn.
    /// </summary>
    public enum RelicTrigger
    {
        RunStart,        // bắt đầu hành trình
        BattleStart,     // đầu mỗi trận (đã wire)
        RoundStart,      // đầu mỗi vòng
        RoundEnd,        // cuối mỗi vòng
        OnPlayUnit,      // khi bạn chơi 1 đơn vị
        OnPlaySpell,     // khi bạn chơi 1 phép
        OnAllyDie,       // khi 1 đơn vị của bạn chết
        OnEnemyDie,      // khi 1 đơn vị địch chết
        OnNexusDamaged   // khi Nexus bạn ăn damage
    }

    /// <summary>Hành động của 1 effect. Chỉ gồm op đã map vào API sẵn có → chạy được ngay.</summary>
    public enum RelicOp
    {
        BuffAllies,       // mọi đơn vị +a|+b
        BuffChampion,     // riêng lá champion +a|+b (cộng dồn nếu trigger lặp → "champion lớn dần")
        GrantKeywordAll,  // mọi đơn vị nhận keyword
        DrawCards,        // rút a lá
        GainMana,         // +a mana
        HealNexus,        // Nexus +a máu
        DiscountAll       // mọi bài -a mana
    }

    [System.Serializable]
    public class RelicEffect
    {
        [UnityEngine.Tooltip("Ghi chú cho dễ nhìn trong Inspector (không ảnh hưởng game).")]
        public string label = "";

        public RelicTrigger trigger = RelicTrigger.BattleStart;
        public RelicOp op = RelicOp.BuffAllies;

        [UnityEngine.Tooltip("Tham số chính: atk / số lá / mana / máu / mức giảm giá.")]
        public int a = 1;
        [UnityEngine.Tooltip("Tham số phụ: hp (cho BuffAllies/BuffChampion).")]
        public int b = 0;

        [UnityEngine.Tooltip("Keyword tặng (chỉ dùng cho GrantKeywordAll).")]
        public KeywordType keyword = KeywordType.Barrier;

        [UnityEngine.Range(0, 100)]
        [UnityEngine.Tooltip("% cơ hội kích hoạt. 100 = luôn luôn.")]
        public int chance = 100;

        [UnityEngine.Min(0)]
        [UnityEngine.Tooltip("Chỉ kích hoạt ở vòng chia hết cho số này (dùng với RoundStart/RoundEnd). 0 = mỗi lần.")]
        public int everyNRounds = 0;
    }

    /// <summary>Dispatcher tĩnh: battle code raise sự kiện, effect của mọi relic đang có tự chạy.</summary>
    public static class RunEffects
    {
        static readonly System.Random _rng = new System.Random();

        /// <param name="me">Player của trận hiện tại.</param>
        /// <param name="round">Vòng hiện tại (dùng cho everyNRounds). Trigger không theo vòng thì để 1.</param>
        public static void Raise(RelicTrigger trigger, PlayerModel me, int round = 1)
        {
            if (me == null) return;

            // 1) Effect từ RELIC (cổ vật nhặt trong run)
            if (CampaignRun.Relics != null)
                foreach (var r in CampaignRun.Relics)
                    if (r != null && r.effects != null)
                        foreach (var e in r.effects)
                            TryRun(e, trigger, me, round);

            // 2) Effect từ TRANG BỊ đã đeo (phần 2 — gom sẵn vào CampaignRun.ExtraEffects lúc StartRun)
            if (CampaignRun.ExtraEffects != null)
                foreach (var e in CampaignRun.ExtraEffects)
                    TryRun(e, trigger, me, round);
        }

        static void TryRun(RelicEffect e, RelicTrigger trigger, PlayerModel me, int round)
        {
            if (e == null || e.trigger != trigger) return;
            if (e.everyNRounds > 0 && (round <= 0 || round % e.everyNRounds != 0)) return;
            if (e.chance < 100 && _rng.Next(100) >= e.chance) return;
            Execute(e, me);
        }

        static void Execute(RelicEffect e, PlayerModel me)
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
                            }
                    break;
            }
        }

        // Duyệt mọi đơn vị của player ở mọi khu (giống CampaignRun.AllUnits) — dùng đúng API public sẵn có.
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