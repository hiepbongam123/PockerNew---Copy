using UnityEngine;
using LoRClone.Model;

namespace LoRClone.Data
{
    /// <summary>
    /// Buff ATK và/hoặc HP cho các lá bài đang trên tay của owner (hoặc đối thủ).
    ///
    /// Dùng như "Grant" trong LoR: buff vĩnh viễn bám theo lá bài đến khi bị Recall hoặc chết.
    ///
    /// Setup trong CardAbility Inspector:
    ///   p1 = attackBuff  (override, -1 = dùng asset default)
    ///   p2 = healthBuff  (override, -1 = dùng asset default)
    ///   p3 = count       (override, -1 = dùng asset default; 0 / âm = tất cả lá)
    ///   temporaryBuff    = false (mặc định — grant vĩnh viễn đúng như LoR)
    ///
    /// Ví dụ dùng:
    ///   • "WhenPlayed: buff ngẫu nhiên 2 lá trên tay thêm +2|+0" → p1=2, p2=0, p3=2
    ///   • "RoundStart: buff tất cả lá trên tay thêm +1|+1"       → p1=1, p2=1, p3=-1
    ///   • "OnAllyDeath: buff tất cả lá trên tay thêm +0|+2"      → p1=0, p2=2, p3=-1
    /// </summary>
    [CreateAssetMenu(menuName = "LoRClone/Skills/BuffCardsInHand", fileName = "SkillBuffCardsInHand")]
    public class SkillBuffCardsInHand : SkillData
    {
        [Header("Buff Amount")]
        [Tooltip("ATK buff cho mỗi lá được chọn. Override bằng p1 trong CardAbility (-1 dùng giá trị này).")]
        public int attackBuff = 1;

        [Tooltip("HP buff cho mỗi lá được chọn. Override bằng p2 trong CardAbility (-1 dùng giá trị này).")]
        public int healthBuff = 1;

        [Header("Target Count")]
        [Tooltip("Số lá ngẫu nhiên trên tay được buff.\n" +
                 "0 hoặc âm = tất cả lá trên tay.\n" +
                 "Override bằng p3 trong CardAbility (-1 dùng giá trị này).")]
        public int count = -1;

        [Header("Target Player")]
        [Tooltip("true = buff tay của owner skill.\nfalse = buff tay của đối thủ (rất hiếm).")]
        public bool buffOwnerHand = true;

        public override void Execute(CardModel source, GameContext ctx,
                                     int p1 = -1, int p2 = -1, int p3 = -1)
        {
            int atkBuff = p1 >= 0 ? p1 : attackBuff;
            int hpBuff = p2 >= 0 ? p2 : healthBuff;
            int buffCount = p3 >= 0 ? p3 : count;

            // Không có gì để buff thì thoát sớm
            if (atkBuff == 0 && hpBuff == 0) return;

            var targetPlayer = buffOwnerHand ? ctx.owner : ctx.opponent;
            var hand = targetPlayer.hand;

            if (hand.Count == 0) return;

            bool buffAll = buffCount <= 0 || buffCount >= hand.Count;

            if (buffAll)
            {
                // Buff tất cả — tránh boxing bằng index loop
                for (int i = 0; i < hand.Count; i++)
                    ApplyBuff(source, hand[i], atkBuff, hpBuff, ctx.temporaryBuff);
            }
            else
            {
                // Chọn N lá ngẫu nhiên không trùng (Fisher-Yates trên index)
                var indices = new int[hand.Count];
                for (int i = 0; i < hand.Count; i++) indices[i] = i;

                for (int i = hand.Count - 1; i > 0; i--)
                {
                    int j = LoRClone.Controller.GameRNG.Range(0, i + 1);
                    (indices[i], indices[j]) = (indices[j], indices[i]);
                }

                int n = Mathf.Min(buffCount, hand.Count);
                for (int i = 0; i < n; i++)
                    ApplyBuff(source, hand[indices[i]], atkBuff, hpBuff, ctx.temporaryBuff);
            }
        }

        static void ApplyBuff(CardModel source, CardModel card, int atkBuff, int hpBuff, bool temporary)
        {
            if (atkBuff != 0) card.BuffAttack(atkBuff, temporary);
            if (hpBuff != 0) card.BuffHealth(hpBuff, temporary);
            if (atkBuff != 0 || hpBuff != 0)
                card.RecordSkillBuff(source.data.cardName, source.data.artwork, atkBuff, hpBuff, temporary);
        }
    }
}