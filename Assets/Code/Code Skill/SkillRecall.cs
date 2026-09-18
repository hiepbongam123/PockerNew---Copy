using System.Collections.Generic;
using UnityEngine;
using LoRClone.Model;
using LoRClone.Skills;

namespace LoRClone.Data
{
    /// <summary>
    /// Recall một hoặc nhiều unit về tay của chủ nhân chúng, đúng như cơ chế LoR:
    ///   • Xóa MỌI hiệu ứng đã áp dụng (buff stats, runtime keyword, mana reduction…)
    ///   • Giữ nguyên form hiện tại (champion đã level-up KHÔNG mất level)
    ///   • Nếu tay đang đầy (10 lá) → card bị obliterate (vào discard)
    ///
    /// Recall unit địch = queued Skill (Fast-speed) → đối thủ có thể response.
    /// Recall đồng minh  = instant (Burst-speed).
    /// → Behavior này được xử lý bởi GameController (targeting + spell speed), không phải skill.
    ///
    /// 2 CÁCH DÙNG (targetMode):
    ///   ① UseTargetSelection (mặc định — giữ nguyên hành vi cũ):
    ///      Tạo CardAbility với effect = SkillRecall asset này.
    ///      targetType = EnemyUnit / AllyUnit / AnyUnit tùy lá bài. p1, p2, p3 không dùng — để -1.
    ///
    ///   ② Mode khác (Self/BattleOpponent/Strongest.../Random...) — TỰ ĐỘNG chọn unit để recall,
    ///      KHÔNG cần targeting UI. Gán thẳng vào CardData.abilities của Unit/Spell/Equipment.
    ///      VD: condition=OnBlock, targetMode=BattleOpponent → tự recall kẻ đang chặn mình
    ///          (kiểu "Vô Hiệu Hóa" phòng thủ).
    ///          condition=WhenPlayed, targetMode=StrongestEnemyAtk → ra sân tự recall địch mạnh nhất.
    /// </summary>
    [CreateAssetMenu(menuName = "LoRClone/Skills/Recall", fileName = "SkillRecall")]
    public class SkillRecall : SkillData
    {
        [Header("Chế độ chọn Target")]
        [Tooltip("UseTargetSelection = target do người chơi/UI chọn (spell targeting cũ).\n" +
                 "Mode khác = TỰ ĐỘNG chọn unit để recall — dùng khi gán thẳng skill làm ability/thiên phú.")]
        public AutoTargetMode targetMode = AutoTargetMode.UseTargetSelection;

        [Tooltip("Số unit recall khi targetMode != UseTargetSelection.")]
        [Min(1)]
        public int autoTargetCount = 1;

        [Tooltip("Auto mode có quét cả bench khi tìm Strongest/Weakest/Random không.")]
        public bool includeBenchInAuto = true;

        public override void Execute(CardModel source, GameContext ctx,
                                     int p1 = -1, int p2 = -1, int p3 = -1)
        {
            List<CardModel> rawTargets = targetMode == AutoTargetMode.UseTargetSelection
                ? ctx.targetCards
                : SkillTargetResolver.Resolve(targetMode, source, ctx, autoTargetCount, includeBenchInAuto);

            if (rawTargets == null || rawTargets.Count == 0)
            {
                Debug.Log($"[SkillRecall] {source?.data?.cardName}: không có target hợp lệ để recall.");
                return;
            }

            // Iterate trên copy để tránh mutation trong lúc loop
            var targets = new List<CardModel>(rawTargets);

            foreach (var target in targets)
            {
                if (target == null || !target.IsAlive) continue;

                // Xác định PlayerModel nào sở hữu target
                // belongsToPlayer: true = human player, false = AI/enemy
                // isPlayer:        true = human player, false = AI/enemy
                PlayerModel targetOwner = (target.belongsToPlayer == ctx.owner.isPlayer)
                    ? ctx.owner
                    : ctx.opponent;

                bool recalled = targetOwner.RecallCard(target);

                if (!recalled)
                    Debug.LogWarning($"[SkillRecall] Không thể recall {target} — card không ở bench/battlefield.");
            }
        }
    }
}