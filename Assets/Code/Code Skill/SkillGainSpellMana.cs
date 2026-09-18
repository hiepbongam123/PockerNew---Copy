using UnityEngine;
using LoRClone.Model;

namespace LoRClone.Data
{
    /// <summary>
    /// Hồi spell mana cho owner.
    ///
    /// Inspector params:
    ///   amount — lượng spell mana hồi (mặc định 1)
    ///
    /// CardAbility override:
    ///   p1 = amount
    ///
    /// Ví dụ dùng với trigger:
    ///   OnAllyDeath     → "khi đồng minh chết, hồi 1 spell mana"
    ///   OnAllyPlayed    → "khi triệu hồi đồng minh, hồi 1 spell mana"
    ///   RoundStart      → "đầu mỗi round hồi 1 spell mana"
    /// </summary>
    [CreateAssetMenu(menuName = "LoRClone/Skills/GainSpellMana",
                     fileName = "Skill_GainSpellMana")]
    public class SkillGainSpellMana : SkillData
    {
        [Tooltip("Lượng spell mana hồi cho owner.")]
        [Min(1)]
        public int amount = 1;

        public override void Execute(CardModel source, GameContext ctx,
                                     int p1 = -1, int p2 = -1, int p3 = -1)
        {
            int amt = p1 >= 0 ? p1 : amount;
            ctx.owner.AddSpellMana(amt);
            Debug.Log($"[GainSpellMana] {source.data.cardName} → owner +{amt} spell mana.");
        }
    }
}
