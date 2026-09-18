using System.Collections.Generic;
using UnityEngine;
using LoRClone.Data;
using LoRClone.Model;

namespace LoRClone.Skills
{
    [CreateAssetMenu(menuName = "LoRClone/Skills/Damage N Enemies", fileName = "Skill_DamageNEnemies")]
    public class SkillDamageNEnemies : SkillData
    {
        public int damage = 1;
        public int count = 1;

        public override void Execute(CardModel source, GameContext ctx,
                                     int p1 = -1, int p2 = -1, int p3 = -1)
        {
            int dmg   = p1 >= 0 ? p1 : damage;
            int cnt   = p2 >= 0 ? p2 : count;

            var enemies = new List<CardModel>(ctx.opponent.BenchCards());
            SkillUtils.Shuffle(enemies);

            int toHit = Mathf.Min(cnt, enemies.Count);
            for (int i = 0; i < toHit; i++)
            {
                // Hòa Hợp (SpellShield): vô hiệu skill kế tiếp — tiêu shield, không gây damage.
                if (enemies[i].TryConsumeSpellShield())
                {
                    Debug.Log($"[Hòa Hợp] {enemies[i].data.cardName} chặn skill của {source.data.cardName}!");
                    continue;
                }
                enemies[i].TakeDamage(dmg);
                Debug.Log($"[Skill] {source.data.cardName} gây {dmg} damage cho {enemies[i].data.cardName}");
            }
        }
    }
}
