using UnityEngine;
using LoRClone.Model;

namespace LoRClone.Data
{
    /// <summary>
    /// Giảm mana cost của các lá bài liên quan (<see cref="CardData.relatedCards"/>) đang ở tay.
    ///
    /// So sánh dựa vào <see cref="CardModel.originalData"/> — đúng kể cả khi lá đã level up.
    /// Reduction là vĩnh viễn và có thể stack nếu skill fire nhiều lần.
    /// currentManaCost không bao giờ âm (Math.Max(0, ...)).
    ///
    /// Inspector params:
    ///   amount            — lượng giảm (mặc định 1)
    ///   requiredOnField   — số lá liên quan (phía owner) phải đang ở bench/battlefield
    ///                       để hiệu ứng kích hoạt (0 = luôn kích hoạt)
    ///
    /// CardAbility override:
    ///   p1 = amount
    ///   p2 = requiredOnField
    /// </summary>
    [CreateAssetMenu(menuName = "LoRClone/Skills/ReduceRelatedManaCost",
                     fileName = "Skill_ReduceRelatedManaCost")]
    public class SkillReduceRelatedManaCost : SkillData
    {
        [Tooltip("Giảm mana cost bao nhiêu cho mỗi lá liên quan trong tay.")]
        [Min(1)]
        public int amount = 1;

        [Tooltip("Số lá liên quan (phía owner) tối thiểu phải đang ở bench/battlefield.\n" +
                 "0 = luôn kích hoạt, không cần điều kiện.")]
        [Min(0)]
        public int requiredOnField = 0;

        public override void Execute(CardModel source, GameContext ctx,
                                     int p1 = -1, int p2 = -1, int p3 = -1)
        {
            int reduceAmt  = p1 >= 0 ? p1 : amount;
            int reqOnField = p2 >= 0 ? p2 : requiredOnField;

            if (source.data.relatedCards == null || source.data.relatedCards.Count == 0)
            {
                Debug.LogWarning($"[ReduceRelatedManaCost] {source.data.cardName} không có relatedCards.");
                return;
            }

            // Kiểm tra điều kiện: đủ lá liên quan ở sân phía owner chưa?
            if (reqOnField > 0)
            {
                int onField = CountRelatedOnField(source, ctx);
                if (onField < reqOnField)
                {
                    Debug.Log($"[ReduceRelatedManaCost] Cần {reqOnField} lá liên quan trên sân, " +
                              $"hiện có {onField}. Bỏ qua.");
                    return;
                }
            }

            var owner = source.belongsToPlayer ? ctx.game.player : ctx.game.enemy;

            int count = 0;
            foreach (var card in owner.hand)
            {
                // Dùng originalData để match đúng dù unit liên quan đã level up
                if (source.data.relatedCards.Contains(card.originalData))
                {
                    card.ReduceManaCost(reduceAmt);
                    count++;
                }
            }

            Debug.Log($"[ReduceRelatedManaCost] Giảm {reduceAmt} mana cost cho {count} lá liên quan trong tay.");
        }

        // ── Helper: chỉ đếm phía owner ───────────────────────────
        // Dùng BenchCards()/BattlefieldCards() thay vì bench[]/battlefield[] trực tiếp
        // vì bench/battlefield là mảng cố định CardModel[6] — các slot trống chứa null.
        // Truy cập null.originalData sẽ throw NullReferenceException.
        static int CountRelatedOnField(CardModel source, GameContext ctx)
        {
            var owner = source.belongsToPlayer ? ctx.game.player : ctx.game.enemy;
            int count = 0;
            foreach (var unit in owner.BenchCards())
                if (source.data.relatedCards.Contains(unit.originalData)) count++;
            foreach (var unit in owner.BattlefieldCards())
                if (source.data.relatedCards.Contains(unit.originalData)) count++;
            return count;
        }
    }
}
