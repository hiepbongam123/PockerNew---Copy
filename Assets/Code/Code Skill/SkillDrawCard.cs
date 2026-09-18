using UnityEngine;
using LoRClone.Data;
using LoRClone.Model;

namespace LoRClone.Skills
{
    [CreateAssetMenu(menuName = "LoRClone/Skills/Draw Card", fileName = "Skill_DrawCard")]
    public class SkillDrawCard : SkillData
    {
        public int count = 1;

        public override void Execute(CardModel source, GameContext ctx,
                                     int p1 = -1, int p2 = -1, int p3 = -1)
        {
            int cnt = p1 >= 0 ? p1 : count;

            for (int i = 0; i < cnt; i++)
            {
                var drawn = ctx.owner.DrawCard();
                if (drawn == null) break;
                Debug.Log($"[Skill] {source.data.cardName} rút bài: {drawn.data.cardName}");
            }
        }
    }
}
