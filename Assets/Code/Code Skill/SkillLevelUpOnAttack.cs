using UnityEngine;
using LoRClone.Data;
using LoRClone.Model;

namespace LoRClone.Skills
{
    [CreateAssetMenu(menuName = "LoRClone/Skills/Level Up On Attack", fileName = "Skill_LevelUpOnAttack")]
    public class SkillLevelUpOnAttack : SkillData
    {
        public int requiredAttacks = 3;

        public override void Execute(CardModel source, GameContext ctx,
                                     int p1 = -1, int p2 = -1, int p3 = -1)
        {
            int req = p1 >= 0 ? p1 : requiredAttacks;

            source.AddLevelUpProgress(1);
            Debug.Log($"[Skill] {source.data.cardName} level up progress: {source.levelUpProgress}/{req}");

            if (source.levelUpProgress >= req)
                source.LevelUp();
        }
    }
}
