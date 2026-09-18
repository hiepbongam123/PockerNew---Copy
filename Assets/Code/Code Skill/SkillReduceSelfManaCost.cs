using UnityEngine;
using LoRClone.Data;
using LoRClone.Model;

namespace LoRClone.Data
{
    /// <summary>
    /// Giảm mana cost của chính lá bài gắn skill này.
    /// Dùng để tạo cơ chế "lá này rẻ hơn khi điều kiện X được đáp ứng".
    ///
    /// Thường dùng kết hợp với:
    ///   - RoundStart trigger → giảm dần qua từng round
    ///   - ConditionalAbility → chỉ giảm khi có đủ quân trên sân, v.v.
    ///
    /// Inspector params:
    ///   amount  — lượng giảm mana cost (mặc định 1)
    ///   minCost — giới hạn dưới của currentManaCost sau khi giảm (mặc định 0)
    ///
    /// CardAbility override:
    ///   p1 = amount
    ///   p2 = minCost
    ///
    /// Ví dụ dùng:
    ///   RoundStart, p1=1, p2=0           → đầu mỗi round, lá này rẻ hơn 1 (tối thiểu 0)
    ///   RoundStart, p1=2, p2=3           → đầu mỗi round, giảm 2 nhưng không dưới 3 mana
    ///   ConditionalAbility (có 3 quân trên sân), p1=1 → chỉ giảm khi điều kiện thỏa
    /// </summary>
    [CreateAssetMenu(menuName = "LoRClone/Skills/ReduceSelfManaCost",
                     fileName = "Skill_ReduceSelfManaCost")]
    public class SkillReduceSelfManaCost : SkillData
    {
        [Tooltip("Lượng giảm mana cost của lá bài này.")]
        [Min(1)]
        public int amount = 1;

        [Tooltip("Mana cost tối thiểu sau khi giảm.\n" +
                 "0 = có thể giảm xuống miễn phí.")]
        [Min(0)]
        public int minCost = 0;

        public override void Execute(CardModel source, GameContext ctx,
                                     int p1 = -1, int p2 = -1, int p3 = -1)
        {
            int reduceAmt = p1 >= 0 ? p1 : amount;
            int floor     = p2 >= 0 ? p2 : minCost;

            int before = source.currentManaCost;
            int target = System.Math.Max(floor, before - reduceAmt);
            int actual = before - target;

            if (actual <= 0)
            {
                Debug.Log($"[ReduceSelfManaCost] {source.data.cardName} đã đạt mức tối thiểu " +
                          $"({floor}), không giảm thêm.");
                return;
            }

            source.ReduceManaCost(actual);
            Debug.Log($"[ReduceSelfManaCost] {source.data.cardName} mana cost " +
                      $"{before} → {source.currentManaCost}.");
        }
    }
}
