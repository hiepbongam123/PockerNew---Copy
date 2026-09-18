using UnityEngine;
using LoRClone.Model;

namespace LoRClone.Data
{
    /// <summary>
    /// Skill KHÔNG làm gì cả — chỉ dùng làm "marker" khi bạn cần gắn 1 CardAbility với
    /// condition nào đó (VD AfterMulligan) NHƯNG không cần hiệu ứng phụ, vì hành vi chính
    /// (rút bài, v.v.) đã được GameController tự làm khi thấy CÓ ability khớp condition đó —
    /// bất kể effect là skill gì.
    ///
    /// VD: CardData của 1 lá bình thường → Abilities → condition = AfterMulligan,
    ///     effect = Skill_NoEffect asset này → lá tự rút vào tay sau mulligan, không kèm buff/damage.
    /// Muốn "rút + có thêm hiệu ứng" thì dùng thẳng skill khác (SkillBuffSelf, SkillDamageNexus...)
    /// với cùng condition = AfterMulligan thay vì dùng skill này.
    /// </summary>
    [CreateAssetMenu(menuName = "LoRClone/Skills/No Effect (Marker)", fileName = "Skill_NoEffect")]
    public class SkillNoEffect : SkillData
    {
        public override void Execute(CardModel source, GameContext ctx,
                                     int p1 = -1, int p2 = -1, int p3 = -1)
        {
            // Cố ý để trống — chỉ dùng làm marker cho condition.
        }
    }
}
