using UnityEngine;
using LoRClone.Data;
using LoRClone.Model;

namespace LoRClone.Data
{
    /// <summary>
    /// Tăng mana trực tiếp (regular mana, spell mana, hoặc cả hai).
    ///
    /// Regular mana bị giới hạn bởi maxMana của round hiện tại.
    /// Spell mana bị giới hạn bởi PlayerModel.MaxSpellMana (5).
    ///
    /// Inspector params:
    ///   amount   — lượng mana cộng thêm (mặc định 1)
    ///   manaType — loại mana:
    ///                0 = regular mana (mana xanh thông thường)
    ///                1 = spell mana   (mana tím, tích lũy qua round)
    ///                2 = cả hai       (cộng amount vào cả hai loại)
    ///
    /// CardAbility override:
    ///   p1 = amount
    ///   p2 = manaType  (0/1/2)
    ///
    /// Ví dụ dùng:
    ///   WhenPlayed, p1=1, p2=0  → khi ra sân, +1 regular mana
    ///   OnAllyDeath, p1=1, p2=1 → khi đồng minh chết, +1 spell mana
    ///   RoundStart, p1=2, p2=2  → đầu round, +2 regular mana và +2 spell mana
    /// </summary>
    [CreateAssetMenu(menuName = "LoRClone/Skills/GainMana",
                     fileName = "Skill_GainMana")]
    public class SkillGainMana : SkillData
    {
        [Tooltip("Lượng mana cộng thêm.")]
        [Min(1)]
        public int amount = 1;

        [Tooltip("Loại mana được cộng:\n" +
                 "  0 = Regular mana (mana xanh, giới hạn bởi maxMana)\n" +
                 "  1 = Spell mana   (mana tím, tích lũy, max 5)\n" +
                 "  2 = Cả hai       (cộng amount vào cả regular và spell mana)")]
        [Min(0)]
        public int manaType = 0;

        public override void Execute(CardModel source, GameContext ctx,
                                     int p1 = -1, int p2 = -1, int p3 = -1)
        {
            int gain  = p1 >= 0 ? p1 : amount;
            int mType = p2 >= 0 ? p2 : manaType;

            var owner = source.belongsToPlayer ? ctx.game.player : ctx.game.enemy;

            bool addedRegular = false;
            bool addedSpell   = false;

            if (mType == 0 || mType == 2)
            {
                owner.RefundMana(gain);
                addedRegular = true;
            }
            if (mType == 1 || mType == 2)
            {
                owner.AddSpellMana(gain);
                addedSpell = true;
            }

            string label = (addedRegular && addedSpell) ? "regular + spell"
                         : addedRegular ? "regular"
                         : "spell";

            Debug.Log($"[GainMana] {source.data.cardName} → +{gain} {label} mana " +
                      $"cho {(source.belongsToPlayer ? "player" : "enemy")}.");
        }
    }
}
