using System.Collections.Generic;
using UnityEngine;
using LoRClone.Data;
using LoRClone.Model;

namespace LoRClone.Skills
{
    /// <summary>
    /// Giết 1 hoặc NHIỀU unit ĐƯỢC CHỌN (đọc ctx.targetCards). "Kill" thật (ForceKill) —
    /// bỏ qua HP/Barrier/Tough, khác gây damage. Hòa Hợp (SpellShield) tự miễn nhiễm
    /// (đã bị lọc khỏi targetCards ở FilterSpellShield trước khi skill chạy).
    ///
    /// ══ SETUP TRÊN CardData (Spell) ══
    ///   requiresTarget = true
    ///   targetType     = EnemyUnit / AnyUnit / AllyUnit (hoặc dùng targetTypes cho từng bước)
    ///   targetCount    = số mục tiêu phải chọn (1 = 1 con, 2+ = nhiều con)
    ///   abilities → effect = asset này
    ///   (Đã có UI targeting sẵn từ SkillDamageTarget — dùng chung, không cần thêm.)
    /// </summary>
    [CreateAssetMenu(menuName = "LoRClone/Skills/Kill Target", fileName = "Skill_KillTarget")]
    public class SkillKillTarget : SkillData
    {
        [Header("Kiểu")]
        [Tooltip("false = giết thường (Last Breath kích hoạt). true = Thủ Tiêu (xóa hẳn, không Last Breath).")]
        public bool obliterate = false;

        public override void Execute(CardModel source, GameContext ctx, int p1 = -1, int p2 = -1, int p3 = -1)
        {
            if (ctx?.controller == null)
            {
                Debug.LogWarning("[SkillKillTarget] ctx.controller null — skill không thể chạy ngoài GameController.");
                return;
            }
            if (ctx.targetCards == null || ctx.targetCards.Count == 0)
            {
                Debug.Log($"[SkillKillTarget] '{source?.data?.cardName}': không có mục tiêu (SpellShield chặn hết?).");
                return;
            }

            // Snapshot vì ProcessDeaths có thể sửa collection nguồn.
            var targets = new List<CardModel>(ctx.targetCards);
            ctx.controller.KillTargets(source, targets, obliterate);
        }
    }
}
