using System.Collections.Generic;
using UnityEngine;
using LoRClone.Data;
using LoRClone.Model;

namespace LoRClone.Data
{
    /// <summary>
    /// Giảm mana cost của các lá đang ở tay theo tiêu chí linh hoạt.
    /// Không cần relatedCards — có thể áp dụng cho tất cả lá, chỉ Unit, chỉ Spell,
    /// hoặc chỉ n lá rẻ nhất/đắt nhất trong tay.
    ///
    /// Inspector params:
    ///   amount     — lượng giảm mana cost (mặc định 1)
    ///   targetType — loại lá được giảm:
    ///                  0 = tất cả lá trong tay
    ///                  1 = chỉ Unit
    ///                  2 = chỉ Spell
    ///   maxCount   — số lá tối đa được áp dụng (0 = không giới hạn)
    ///
    /// CardAbility override:
    ///   p1 = amount
    ///   p2 = targetType  (0/1/2)
    ///   p3 = maxCount
    ///
    /// Ví dụ dùng:
    ///   WhenPlayed, p1=1, p2=0, p3=0  → khi ra sân, giảm 1 mana tất cả lá trong tay
    ///   RoundStart, p1=2, p2=1, p3=3  → đầu round, giảm 2 mana cho 3 Unit trong tay
    ///   OnAllyDeath, p1=1, p2=2, p3=0 → khi đồng minh chết, giảm 1 mana tất cả Spell trong tay
    /// </summary>
    [CreateAssetMenu(menuName = "LoRClone/Skills/ReduceManaCost",
                     fileName = "Skill_ReduceManaCost")]
    public class SkillReduceManaCost : SkillData
    {
        [Tooltip("Lượng giảm mana cost cho mỗi lá hợp lệ.")]
        [Min(1)]
        public int amount = 1;

        [Tooltip("Loại lá được giảm mana cost:\n" +
                 "  0 = tất cả lá trong tay\n" +
                 "  1 = chỉ Unit\n" +
                 "  2 = chỉ Spell")]
        [Min(0)]
        public int targetType = 0;

        [Tooltip("Số lá tối đa được áp dụng. 0 = không giới hạn.")]
        [Min(0)]
        public int maxCount = 0;

        public override void Execute(CardModel source, GameContext ctx,
                                     int p1 = -1, int p2 = -1, int p3 = -1)
        {
            int reduceAmt  = p1 >= 0 ? p1 : amount;
            int tType      = p2 >= 0 ? p2 : targetType;
            int limit      = p3 >= 0 ? p3 : maxCount;

            var owner = source.belongsToPlayer ? ctx.game.player : ctx.game.enemy;

            int count = 0;
            foreach (var card in new List<CardModel>(owner.hand))
            {
                if (limit > 0 && count >= limit) break;

                bool matches = tType switch
                {
                    1 => card.data.cardType == CardType.Unit,
                    2 => card.data.cardType == CardType.Spell,
                    _ => true   // 0 = tất cả
                };

                if (!matches) continue;

                card.ReduceManaCost(reduceAmt);
                count++;
            }

            string typeLabel = tType == 1 ? "Unit" : tType == 2 ? "Spell" : "tất cả";
            Debug.Log($"[ReduceManaCost] {source.data.cardName} → giảm {reduceAmt} mana cost " +
                      $"cho {count} lá ({typeLabel}) trong tay.");
        }
    }
}
