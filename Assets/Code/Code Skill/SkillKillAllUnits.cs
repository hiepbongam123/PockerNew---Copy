using UnityEngine;
using LoRClone.Data;
using LoRClone.Model;

namespace LoRClone.Skills
{
    /// <summary>
    /// THE RUINATION — giết toàn bộ unit trên sân. Gán vào 1 Spell (Slow) hoặc unit ability.
    ///
    /// ══ SETUP ══
    ///   Create → LoRClone/Skills/Kill All Units.
    ///   The Ruination: scope = All, obliterate = false. Gán vào Spell Slow, abilities: effect = asset.
    ///
    /// scope:
    ///   All        = cả 2 bên (The Ruination).
    ///   EnemyOnly  = chỉ quân địch của người dùng skill.
    ///   AllyOnly   = chỉ quân mình.
    /// obliterate:
    ///   false = giết thường → Last Breath / OnAllyDeath kích hoạt (chuẩn The Ruination).
    ///   true  = Thủ Tiêu → xóa hẳn, KHÔNG Last Breath, hủy trang bị.
    /// </summary>
    [CreateAssetMenu(menuName = "LoRClone/Skills/Kill All Units", fileName = "Skill_KillAllUnits")]
    public class SkillKillAllUnits : SkillData
    {
        public enum KillScope { All, EnemyOnly, AllyOnly }

        [Header("Phạm vi")]
        [Tooltip("All = cả 2 bên (The Ruination). EnemyOnly = chỉ địch. AllyOnly = chỉ quân mình.")]
        public KillScope scope = KillScope.All;

        [Header("Kiểu")]
        [Tooltip("false = giết thường (Last Breath kích hoạt — The Ruination). true = Thủ Tiêu (xóa hẳn).")]
        public bool obliterate = false;

        public override void Execute(CardModel source, GameContext ctx, int p1 = -1, int p2 = -1, int p3 = -1)
        {
            if (ctx?.controller == null)
            {
                Debug.LogWarning("[SkillKillAllUnits] ctx.controller null — skill không thể chạy ngoài GameController.");
                return;
            }
            bool includeAlly  = scope != KillScope.EnemyOnly;
            bool includeEnemy = scope != KillScope.AllyOnly;
            ctx.controller.KillAllUnits(source, includeAlly, includeEnemy, obliterate);
        }
    }
}
