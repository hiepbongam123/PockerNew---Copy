using UnityEngine;
using LoRClone.Data;
using LoRClone.Model;

namespace LoRClone.Skills
{
    /// <summary>
    /// Spell effect: tăng attack và health của toàn bộ unit đồng minh đang trên sân
    /// (bench + battlefield). Gán vào CardData của spell, trigger = WhenPlayed.
    /// </summary>
    [CreateAssetMenu(menuName = "LoRClone/Skills/Buff All On Field", fileName = "Skill_BuffAllOnField")]
    public class SkillBuffAllOnField : SkillData
    {
        [Header("Buff amount")]
        public int attackBuff = 1;
        public int healthBuff = 1;

        public override void Execute(CardModel source, GameContext ctx,
                                     int p1 = -1, int p2 = -1, int p3 = -1)
        {
            int atk = p1 >= 0 ? p1 : attackBuff;
            int hp = p2 >= 0 ? p2 : healthBuff;
            bool tmp = ctx.temporaryBuff;
            int count = 0;

            foreach (var ally in ctx.owner.BenchCards())
            {
                if (atk > 0) ally.BuffAttack(atk, tmp);
                if (hp > 0) ally.BuffHealth(hp, tmp);
                ally.RecordSkillBuff(source.data.cardName, source.data.artwork, atk, hp, tmp);
                count++;
            }

            foreach (var ally in ctx.owner.BattlefieldCards())
            {
                if (atk > 0) ally.BuffAttack(atk, tmp);
                if (hp > 0) ally.BuffHealth(hp, tmp);
                ally.RecordSkillBuff(source.data.cardName, source.data.artwork, atk, hp, tmp);
                count++;
            }

            Debug.Log($"[Skill] {source.data.cardName} buff +{atk} ATK / +{hp} HP cho {count} unit.");
        }
    }
}