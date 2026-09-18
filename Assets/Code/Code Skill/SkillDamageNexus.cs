using UnityEngine;
using LoRClone.Model;

namespace LoRClone.Data
{
    /// <summary>
    /// Gây sát thương trực tiếp vào nexus của đối thủ.
    /// Không xuyên qua SpellShield (nexus không có shield).
    ///
    /// Inspector params:
    ///   damage — lượng sát thương (mặc định 1)
    ///
    /// CardAbility override:
    ///   p1 = damage
    ///
    /// Ví dụ dùng với trigger:
    ///   OnAllyDeath       → "khi đồng minh chết, gây 1 damage lên nexus địch"
    ///   OnAllyDealsDamage → "khi đồng minh gây damage, gây thêm 1 lên nexus"
    ///   WhenPlayed        → "khi ra sân, gây 2 damage lên nexus địch"
    ///   OnKill            → "khi giết địch, gây 1 damage lên nexus địch"
    /// </summary>
    [CreateAssetMenu(menuName = "LoRClone/Skills/DamageNexus",
                     fileName = "Skill_DamageNexus")]
    public class SkillDamageNexus : SkillData
    {
        [Tooltip("Lượng sát thương gây lên nexus đối thủ.")]
        [Min(1)]
        public int damage = 1;

        public override void Execute(CardModel source, GameContext ctx,
                                     int p1 = -1, int p2 = -1, int p3 = -1)
        {
            int dmg = p1 >= 0 ? p1 : damage;

            ctx.opponent.TakeDamage(dmg);

            Debug.Log($"[DamageNexus] {source.data.cardName} → nexus địch -{dmg} HP " +
                      $"(còn {ctx.opponent.health}).");
        }
    }
}