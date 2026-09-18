using System.Collections.Generic;
using UnityEngine;
using LoRClone.Data;
using LoRClone.Model;
using LoRClone.Controller;

namespace LoRClone.Skills
{
    /// <summary>
    /// Effect cho SPELL: khi cast spell này → champion LÊN CẤP NGAY (kích hoạt điều kiện level-up).
    ///
    /// CÁCH DÙNG:
    ///   1. Create → LoRClone/Skills/Level Up Target → tạo asset.
    ///   2. Gán asset vào spell (CardData.skills, HOẶC abilities[].effect).
    ///      • Muốn lên cấp 1 champion CỤ THỂ: bật spell.requiresTarget = true,
    ///        targetType = AllyUnit → người chơi chọn champion khi cast.
    ///      • Muốn lên cấp TẤT CẢ champion đồng minh: bật allAllyChampions bên dưới
    ///        (hoặc để spell không cần target).
    ///
    /// Chỉ tác động lên champion có canLevelUp = true và chưa level up.
    /// Nếu champion đang ở tay/deck → đánh dấu readyToLevelUp (sẽ lên cấp khi ra sân).
    /// </summary>
    [CreateAssetMenu(menuName = "LoRClone/Skills/Level Up Target", fileName = "Skill_LevelUpTarget")]
    public class SkillLevelUpTarget : SkillData
    {
        [Tooltip("Bật = lên cấp TẤT CẢ champion đồng minh trên sân (bỏ qua target).\n" +
                 "Tắt = lên cấp champion mà spell nhắm target (nếu spell requiresTarget);\n" +
                 "nếu spell không có target thì fallback = tất cả champion đồng minh.")]
        public bool allAllyChampions = false;

        public override void Execute(CardModel source, GameContext ctx, int p1 = -1, int p2 = -1, int p3 = -1)
        {
            if (ctx == null) return;
            var gc = ctx.controller as GameController;

            // Chọn danh sách mục tiêu
            var list = new List<CardModel>();
            if (!allAllyChampions && ctx.targetCards != null && ctx.targetCards.Count > 0)
            {
                list.AddRange(ctx.targetCards);
            }
            else if (ctx.owner != null)
            {
                foreach (var c in ctx.owner.BenchCards()) list.Add(c);
                foreach (var c in ctx.owner.BattlefieldCards()) list.Add(c);
            }

            foreach (var t in list)
            {
                if (t == null || t.data == null) continue;
                if (!t.data.canLevelUp || t.hasLeveledUp) continue;

                bool onBoard = t.location == CardLocation.OnBench
                            || t.location == CardLocation.OnBattlefield;

                if (onBoard && gc != null)
                    gc.TriggerLevelUp(t);         // đang trên sân → transform ngay
                else
                    t.MarkReadyToLevelUp();        // ở tay/deck → lên cấp khi ra sân

                Debug.Log($"[LevelUp] {t.data.cardName} lên cấp do spell '{source?.data?.cardName}'.");
            }
        }
    }
}
