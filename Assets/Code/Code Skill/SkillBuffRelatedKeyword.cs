using UnityEngine;
using System.Collections.Generic;
using LoRClone.Model;

namespace LoRClone.Data
{
    /// <summary>
    /// Cấp keyword cho các lá bài liên quan (<see cref="CardData.relatedCards"/>)
    /// đang ở bench hoặc battlefield của owner.
    ///
    /// So sánh dựa vào <see cref="CardModel.originalData"/> — đúng kể cả khi lá đã level up.
    /// Keyword lưu trong CardModel._runtimeKeywords (không sửa ScriptableObject).
    /// CardView.RefreshKeywords() đọc qua GetAllKeywords() nên icon hiện đúng.
    ///
    /// Inspector params:
    ///   keyword           — keyword muốn cấp
    ///   requiredOnField   — số lá liên quan (phía owner) tối thiểu phải ở sân để kích hoạt (0 = luôn)
    ///
    /// CardAbility override:
    ///   p1 = (int)KeywordType
    ///   p2 = requiredOnField
    /// </summary>
    [CreateAssetMenu(menuName = "LoRClone/Skills/BuffRelatedKeyword",
                     fileName = "Skill_BuffRelatedKeyword")]
    public class SkillBuffRelatedKeyword : SkillData
    {
        [Tooltip("Keyword được cấp cho các lá liên quan trên sân.")]
        public KeywordType keyword = KeywordType.Barrier;

        [Tooltip("Số lá liên quan (phía owner) tối thiểu phải đang ở bench/battlefield.\n" +
                 "0 = luôn kích hoạt, không cần điều kiện.")]
        [Min(0)]
        public int requiredOnField = 0;

        public override void Execute(CardModel source, GameContext ctx,
                                     int p1 = -1, int p2 = -1, int p3 = -1)
        {
            var kw         = p1 >= 0 ? (KeywordType)p1 : keyword;
            int reqOnField = p2 >= 0 ? p2 : requiredOnField;

            if (source.data.relatedCards == null || source.data.relatedCards.Count == 0)
            {
                Debug.LogWarning($"[SkillBuffRelatedKeyword] {source.data.cardName} không có relatedCards.");
                return;
            }

            var targets = CollectRelatedOnField(source, ctx);

            if (reqOnField > 0 && targets.Count < reqOnField)
            {
                Debug.Log($"[SkillBuffRelatedKeyword] Cần {reqOnField} lá liên quan, " +
                          $"hiện có {targets.Count}. Bỏ qua.");
                return;
            }

            int count = 0;
            foreach (var unit in targets)
            {
                unit.GrantKeyword(kw);
                count++;
            }

            Debug.Log($"[SkillBuffRelatedKeyword] Cấp {kw} cho {count} lá liên quan.");
        }

        // ── Helper: chỉ lấy phía owner ───────────────────────────
        // Dùng BenchCards()/BattlefieldCards() thay vì bench[]/battlefield[] trực tiếp
        // vì bench/battlefield là mảng cố định CardModel[6] — các slot trống chứa null.
        // Truy cập null.originalData sẽ throw NullReferenceException.
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
