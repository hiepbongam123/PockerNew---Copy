using UnityEngine;
using LoRClone.Data;
using LoRClone.Model;
using LoRClone.Controller;

namespace LoRClone.Skills
{
    /// <summary>
    /// Gay damage len nhieu ke dich. Chon pham vi bang dropdown 'scope':
    ///   AllUnits         = moi quan dich (bench + battlefield)
    ///   BattlefieldOnly  = chi quan dang o battleslot
    ///   AllUnitsAndNexus = moi quan dich + Nexus
    /// 'countForSource' = cong damage vao bo dem cua source (champion len cap theo damage).
    /// </summary>
    [CreateAssetMenu(menuName = "LoRClone/Skills/Damage All Enemies", fileName = "Skill_DamageAllEnemies")]
    public class SkillDamageAllEnemies : SkillData
    {
        public enum Scope { AllUnits, BattlefieldOnly, AllUnitsAndNexus }

        [Tooltip("Sat thuong moi muc tieu.")]
        public int damage = 1;

        [Tooltip("Pham vi: AllUnits / BattlefieldOnly / AllUnitsAndNexus.")]
        public Scope scope = Scope.AllUnits;

        [Tooltip("BAT = cong damage vao bo dem cua source (champion len cap theo damage, vd Mydei).")]
        public bool countForSource = false;

        public override void Execute(CardModel source, GameContext ctx,
                                     int p1 = -1, int p2 = -1, int p3 = -1)
        {
            var gc = (ctx?.controller as GameController) ?? GameController.Instance;
            if (source == null || gc == null) return;
            int dmg = p1 >= 0 ? p1 : damage;
            if (dmg <= 0) return;

            switch (scope)
            {
                case Scope.BattlefieldOnly:
                    gc.DamageBattlefieldEnemiesFrom(source, dmg, countForSource);
                    break;
                case Scope.AllUnitsAndNexus:
                    gc.DamageAllEnemiesInclNexusFrom(source, dmg, countForSource);
                    break;
                default:
                    gc.DamageAllEnemyUnitsFrom(source, dmg, countForSource);
                    break;
            }
        }
    }
}
