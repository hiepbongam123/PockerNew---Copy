using UnityEngine;
using LoRClone.Data;
using LoRClone.Model;

namespace LoRClone.Skills
{
    [CreateAssetMenu(menuName = "LoRClone/Skills/Buff Target", fileName = "Skill_BuffTarget")]
    public class SkillBuffTarget : SkillData
    {
        public int attackBuff = 1;
        public int healthBuff = 0;

        [Header("Tuy chon: cap them 1 keyword cho (cac) target")]
        public bool alsoGrantKeyword = false;
        public KeywordType keyword = KeywordType.QuickAttack;

        public override void Execute(CardModel source, GameContext ctx,
                                     int p1 = -1, int p2 = -1, int p3 = -1)
        {
            int atk = p1 >= 0 ? p1 : attackBuff;
            int hp = p2 >= 0 ? p2 : healthBuff;

            if (ctx.targetCards == null || ctx.targetCards.Count == 0)
            {
                Debug.LogWarning($"[Skill] {source?.data?.cardName}: Buff Target — khong co target.");
                return;
            }
            bool tmp = ctx.temporaryBuff;
            bool grantKw = ctx.grantKeyword || alsoGrantKeyword;
            KeywordType kw = ctx.grantKeyword ? ctx.grantKeywordType : keyword;
            foreach (var target in ctx.targetCards)
            {
                if (target == null || !target.IsAlive) continue;
                if (atk != 0) target.BuffAttack(atk, tmp);
                if (hp != 0) target.BuffHealth(hp, tmp);
                if (atk != 0 || hp != 0)
                    target.RecordSkillBuff(source.data.cardName, source.data.artwork, atk, hp, tmp);
                if (grantKw) target.GrantKeyword(kw);
                Debug.Log($"[Skill] {source?.data?.cardName} -> {target.data.cardName}: +{atk}/{hp}" +
                          (alsoGrantKeyword ? $" +{keyword}" : "") + (tmp ? " (round)" : " (perm)"));
            }
        }
    }
}
