using UnityEngine;
using System.Collections.Generic;
using LoRClone.Model;

namespace LoRClone.Data
{
    /// <summary>
    /// Buff attack và/hoặc health cho 1 đồng minh ngẫu nhiên trên bench/battlefield.
    /// Không buff chính lá bài kích hoạt skill.
    ///
    /// Inspector params:
    ///   attackBuff — lượng buff attack (0 = không buff attack)
    ///   healthBuff — lượng buff health (0 = không buff health)
    ///   count      — số lượng đồng minh ngẫu nhiên được buff (mặc định 1)
    ///
    /// CardAbility override:
    ///   p1 = attackBuff
    ///   p2 = healthBuff
    ///   p3 = count
    ///
    /// Ví dụ dùng với trigger:
    ///   OnAllyDeath     → "khi đồng minh chết, buff 1 đồng minh ngẫu nhiên +1|+1"
    ///   WhenPlayed      → "khi ra sân, buff đồng minh ngẫu nhiên +2 attack"
    ///   RoundStart      → "đầu mỗi round, buff 1 đồng minh ngẫu nhiên +0|+1"
    /// </summary>
    [CreateAssetMenu(menuName = "LoRClone/Skills/BuffRandomAlly",
                     fileName = "Skill_BuffRandomAlly")]
    public class SkillBuffRandomAlly : SkillData
    {
        [Tooltip("Buff thêm attack cho đồng minh ngẫu nhiên. 0 = không buff attack.")]
        [Min(0)]
        public int attackBuff = 1;

        [Tooltip("Buff thêm health cho đồng minh ngẫu nhiên. 0 = không buff health.")]
        [Min(0)]
        public int healthBuff = 1;

        [Tooltip("Số đồng minh ngẫu nhiên được chọn để buff (có thể buff cùng 1 unit nếu count > số ally).")]
        [Min(1)]
        public int count = 1;

        public override void Execute(CardModel source, GameContext ctx,
                                     int p1 = -1, int p2 = -1, int p3 = -1)
        {
            int atkBuff = p1 >= 0 ? p1 : attackBuff;
            int hpBuff = p2 >= 0 ? p2 : healthBuff;
            int cnt = p3 >= 0 ? p3 : count;

            var candidates = new List<CardModel>();
            foreach (var unit in ctx.owner.bench)
                if (unit != null && unit != source && unit.IsAlive) candidates.Add(unit);
            foreach (var unit in ctx.owner.battlefield)
                if (unit != null && unit != source && unit.IsAlive) candidates.Add(unit);

            if (candidates.Count == 0)
            {
                Debug.Log($"[BuffRandomAlly] Không có đồng minh nào để buff.");
                return;
            }

            bool tmp = ctx.temporaryBuff;
            for (int i = 0; i < cnt; i++)
            {
                var chosen = candidates[Random.Range(0, candidates.Count)];
                if (atkBuff > 0) chosen.BuffAttack(atkBuff, tmp);
                if (hpBuff > 0) chosen.BuffHealth(hpBuff, tmp);
                if (atkBuff != 0 || hpBuff != 0)
                    chosen.RecordSkillBuff(source.data.cardName, source.data.artwork, atkBuff, hpBuff, tmp);
                Debug.Log($"[BuffRandomAlly] {source.data.cardName} → buff {chosen.data.cardName} +{atkBuff}/+{hpBuff} {(tmp ? "(round)" : "(perm)")}.");
            }
        }
    }
}