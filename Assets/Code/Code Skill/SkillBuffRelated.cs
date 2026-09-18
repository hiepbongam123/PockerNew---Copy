using UnityEngine;
using System.Collections.Generic;
using LoRClone.Model;

namespace LoRClone.Data
{
    /// <summary>
    /// Buff attack và/hoặc health cho các lá bài liên quan (<see cref="CardData.relatedCards"/>)
    /// đang ở bench hoặc battlefield của owner.
    ///
    /// So sánh dựa vào <see cref="CardModel.originalData"/> — đúng kể cả khi lá đã level up.
    ///
    /// Inspector params:
    ///   attackBuff        — lượng buff attack (0 = không buff attack)
    ///   healthBuff        — lượng buff health (0 = không buff health)
    ///   requiredOnField   — số lá liên quan (phía owner) tối thiểu phải ở sân để kích hoạt (0 = luôn)
    ///
    /// CardAbility override:
    ///   p1 = attackBuff
    ///   p2 = healthBuff
    ///   p3 = requiredOnField
    /// </summary>
    [UnityEngine.CreateAssetMenu(menuName = "LoRClone/Skills/BuffRelated",
                     fileName = "Skill_BuffRelated")]
    public class SkillBuffRelated : SkillData
    {
        [Tooltip("Buff thêm bao nhiêu attack cho các lá liên quan trên sân. 0 = không buff attack.")]
        [Min(0)]
        public int attackBuff = 1;

        [Tooltip("Buff thêm bao nhiêu health cho các lá liên quan trên sân. 0 = không buff health.")]
        [Min(0)]
        public int healthBuff = 0;

        [Tooltip("Số lá liên quan (phía owner) tối thiểu phải đang ở bench/battlefield.\n" +
                 "0 = luôn kích hoạt, không cần điều kiện.")]
        [Min(0)]
        public int requiredOnField = 0;

        public override void Execute(CardModel source, GameContext ctx,
                                     int p1 = -1, int p2 = -1, int p3 = -1)
        {
            int atkBuff = p1 >= 0 ? p1 : attackBuff;
            int hpBuff = p2 >= 0 ? p2 : healthBuff;
            int reqOnField = p3 >= 0 ? p3 : requiredOnField;

            if (source.data.relatedCards == null || source.data.relatedCards.Count == 0)
            {
                Debug.LogWarning($"[SkillBuffRelated] {source.data.cardName} không có relatedCards.");
                return;
            }

            var targets = CollectRelatedOnField(source, ctx);

            if (reqOnField > 0 && targets.Count < reqOnField)
            {
                Debug.Log($"[SkillBuffRelated] Cần {reqOnField} lá liên quan, hiện có {targets.Count}. Bỏ qua.");
                return;
            }

            bool temporary = ctx.temporaryBuff;
            int count = 0;
            foreach (var unit in targets)
            {
                if (atkBuff > 0) unit.BuffAttack(atkBuff, temporary);
                if (hpBuff > 0) unit.BuffHealth(hpBuff, temporary);
                unit.RecordSkillBuff(source.data.cardName, source.data.artwork, atkBuff, hpBuff, temporary);
                count++;
            }

            Debug.Log($"[SkillBuffRelated] Buff +{atkBuff}/+{hpBuff} ({(temporary ? "tạm" : "vĩnh viễn")}) " +
                      $"cho {count} lá liên quan trên sân.");
        }

        // ── Helper: chỉ lấy phía owner ───────────────────────────
        static List<CardModel> CollectRelatedOnField(CardModel source, GameContext ctx)
        {
            var result = new List<CardModel>();
            var owner = source.belongsToPlayer ? ctx.game.player : ctx.game.enemy;

            foreach (var unit in owner.BenchCards())
                if (unit != source && source.data.relatedCards.Contains(unit.originalData))
                    result.Add(unit);
            foreach (var unit in owner.BattlefieldCards())
                if (unit != source && source.data.relatedCards.Contains(unit.originalData))
                    result.Add(unit);
            return result;
        }
    }
}