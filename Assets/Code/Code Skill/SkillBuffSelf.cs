using UnityEngine;
using LoRClone.Data;
using LoRClone.Model;

namespace LoRClone.Skills
{
    [CreateAssetMenu(menuName = "LoRClone/Skills/Buff Self", fileName = "Skill_BuffSelf")]
    public class SkillBuffSelf : SkillData
    {
        public int attackBuff = 0;
        public int healthBuff = 0;

        [Header("Tuy chon: cap them 1 keyword khi buff")]
        public bool alsoGrantKeyword = false;
        public KeywordType keyword = KeywordType.QuickAttack;

        public override void Execute(CardModel source, GameContext ctx,
                                     int p1 = -1, int p2 = -1, int p3 = -1)
        {
            if (source == null) return;
            int atk = p1 >= 0 ? p1 : attackBuff;
            int hp = p2 >= 0 ? p2 : healthBuff;

            bool tmp = ctx.temporaryBuff;
            if (atk != 0) source.BuffAttack(atk, tmp);
            if (hp != 0) source.BuffHealth(hp, tmp);
            if (atk != 0 || hp != 0)
                source.RecordSkillBuff(source.data.cardName, source.data.artwork, atk, hp, tmp);

            if (alsoGrantKeyword) source.GrantKeyword(keyword);

            Debug.Log($"[Skill] {source.data.cardName} tu buff +{atk}/{hp}" +
                      (alsoGrantKeyword ? $" +{keyword}" : "") + (tmp ? " (round)" : " (perm)"));
        }
    }
}
