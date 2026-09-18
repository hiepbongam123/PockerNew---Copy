using System.Collections.Generic;
using UnityEngine;
using LoRClone.Data;
using LoRClone.Model;
using LoRClone.Controller;

namespace LoRClone.Skills
{
    /// <summary>
    /// Gây damage lên kẻ địch YẾU NHẤT hoặc MẠNH NHẤT theo tiêu chí chọn (ATK hoặc HP).
    /// Dùng được cả làm spell thường lẫn battle-skill (Renekton). Nếu là battle-skill,
    /// bộ đếm damage được cộng cho UNIT gốc (để lên cấp), không phải lá skill.
    /// </summary>
    [CreateAssetMenu(menuName = "LoRClone/Skills/Damage Weakest Or Strongest", fileName = "Skill_DamageWeakestOrStrongest")]
    public class SkillDamageWeakestOrStrongest : SkillData
    {
        public enum Pick { LowestHealth, HighestHealth, LowestAttack, HighestAttack }
        public enum Scope { AllUnits, BattlefieldOnly }

        [Tooltip("Tiêu chí chọn mục tiêu: HP thấp/cao nhất, ATK thấp/cao nhất.")]
        public Pick pick = Pick.HighestAttack;

        [Tooltip("Phạm vi quét: mọi quân địch (bench + battleslot) hay chỉ quân đang ở battleslot.")]
        public Scope scope = Scope.AllUnits;

        [Tooltip("Sát thương mỗi mục tiêu.")]
        public int damage = 2;

        [Tooltip("Số mục tiêu chọn theo tiêu chí (1 = chỉ 1 kẻ yếu/mạnh nhất).")]
        [Min(1)]
        public int count = 1;

        [Tooltip("BẬT = cộng damage vào bộ đếm của source (lên cấp theo damage).")]
        public bool countForSource = false;

        public override void Execute(CardModel source, GameContext ctx,
                                     int p1 = -1, int p2 = -1, int p3 = -1)
        {
            var gc = (ctx?.controller as GameController) ?? GameController.Instance;
            if (gc == null) return;
            int dmg = p1 >= 0 ? p1 : damage;
            int cnt = p2 >= 0 ? p2 : count;
            if (dmg <= 0 || cnt <= 0) return;

            // Battle-skill → dùng unit gốc (đúng phe + đếm damage cho unit). Spell thường → chính source.
            var dealer = gc.BattleSkillUnitFor(source) ?? source;
            if (dealer == null) return;

            var enemies = gc.EnemyUnitsOf(dealer, scope == Scope.BattlefieldOnly);
            if (enemies.Count == 0) return;

            enemies.Sort(Compare); // phần tử ƯU TIÊN đứng đầu

            int toHit = Mathf.Min(cnt, enemies.Count);
            int total = 0;
            for (int i = 0; i < toHit; i++)
            {
                // Hòa Hợp (SpellShield): vô hiệu skill kế tiếp — tiêu shield, không gây damage.
                if (enemies[i].TryConsumeSpellShield())
                {
                    Debug.Log($"[Hòa Hợp] {enemies[i].data.cardName} chặn skill của {dealer.data.cardName}!");
                    continue;
                }
                int before = enemies[i].currentHealth;
                enemies[i].TakeDamage(dmg);
                total += before - enemies[i].currentHealth;
                Debug.Log($"[Skill] {dealer.data.cardName} → {enemies[i].data.cardName} ({pick}): -{dmg}");
            }
            if (countForSource && total > 0) gc.RegisterSkillDamage(dealer, total);
            // ProcessDeaths do closure resolve của spell stack / HandleCastSpell tự chạy sau Execute.
        }

        // Sắp xếp để mục tiêu ƯU TIÊN nằm ở đầu danh sách (index 0..).
        int Compare(CardModel a, CardModel b)
        {
            switch (pick)
            {
                case Pick.LowestHealth:  return a.currentHealth.CompareTo(b.currentHealth);
                case Pick.HighestHealth: return b.currentHealth.CompareTo(a.currentHealth);
                case Pick.LowestAttack:  return a.currentAttack.CompareTo(b.currentAttack);
                default:                 return b.currentAttack.CompareTo(a.currentAttack); // HighestAttack
            }
        }
    }
}
