using System.Collections.Generic;
using UnityEngine;
using LoRClone.Data;
using LoRClone.Model;
using LoRClone.Controller;

namespace LoRClone.Skills
{
    /// <summary>
    /// Gay damage len (cac) target da chon.
    /// Tick 'obliterate' = THU TIEU (xoa han, khong Last Breath, huy trang bi, khong hoi sinh).
    /// Tick 'hitNexusIfNoTarget' = khi KHONG danh trung muc tieu song nao (target rong HOAC target
    ///   da chet) thi danh thang vao Nexus dich. Dung cho Lee Sin: co thach dau → da ke do;
    ///   khong thach dau → target rong → da nexus.
    ///
    /// 2 CÁCH DÙNG (targetMode):
    ///   ① UseTargetSelection (mặc định — giữ nguyên hành vi cũ) — dùng cho SPELL cần
    ///      requiresTarget=true, hoặc Unit WhenPlayed cần người chơi chọn target tay.
    ///   ② Mọi mode khác (Self/BattleOpponent/Strongest.../Random...) — TỰ ĐỘNG chọn target,
    ///      KHÔNG cần targeting UI. Gán thẳng skill này vào CardData.abilities của Unit/Spell/
    ///      Equipment để làm kỹ năng/thiên phú tự kích hoạt.
    ///      VD: condition=OnAttack, targetMode=WeakestEnemyAtk → tự bắn địch yếu nhất khi tấn công.
    ///          condition=OnStrike, targetMode=BattleOpponent  → tự đánh thêm kẻ đang giao tranh.
    /// </summary>
    [CreateAssetMenu(menuName = "LoRClone/Skills/Damage Target", fileName = "Skill_DamageTarget")]
    public class SkillDamageTarget : SkillData
    {
        [Tooltip("Sat thuong len moi target.")]
        public int damage = 1;

        [Tooltip("BAT = THU TIEU muc tieu (xoa han, khong Last Breath, huy trang bi) thay vi gay damage.")]
        public bool obliterate = false;

        [Tooltip("BAT = khong danh trung muc tieu song nao thi danh dmg vao Nexus dich " +
                 "(vd Lee Sin tan cong khong thach dau).")]
        public bool hitNexusIfNoTarget = false;

        [Header("Chế độ chọn Target")]
        [Tooltip("UseTargetSelection = target tay do người chơi/UI chọn (spell/unit targeting cũ).\n" +
                 "Mode khác = TỰ ĐỘNG chọn target — dùng khi gán thẳng skill làm ability/thiên phú.")]
        public AutoTargetMode targetMode = AutoTargetMode.UseTargetSelection;

        [Tooltip("Số target khi targetMode != UseTargetSelection. Bỏ qua nếu dùng UseTargetSelection " +
                 "(lúc đó số lượng do CardData.targetCount của spell quyết định).")]
        [Min(1)]
        public int autoTargetCount = 1;

        [Tooltip("Auto mode có quét cả bench (không chỉ battlefield) khi tìm Strongest/Weakest/Random không.")]
        public bool includeBenchInAuto = true;

        public override void Execute(CardModel source, GameContext ctx,
                                     int p1 = -1, int p2 = -1, int p3 = -1)
        {
            int dmg = p1 >= 0 ? p1 : damage;
            var gc = (ctx?.controller as GameController) ?? GameController.Instance;

            List<CardModel> targets = targetMode == AutoTargetMode.UseTargetSelection
                ? ctx?.targetCards
                : SkillTargetResolver.Resolve(targetMode, source, ctx, autoTargetCount, includeBenchInAuto);

            int liveHits = 0;
            if (targets != null)
            {
                foreach (var target in targets)
                {
                    if (target == null || !target.IsAlive) continue;
                    liveHits++;
                    if (obliterate && gc != null)
                    {
                        gc.ObliterateUnit(target);
                    }
                    else
                    {
                        int hpBefore = target.currentHealth;
                        target.TakeDamage(dmg);
                        Debug.Log($"[Skill] {source?.data?.cardName} -> {target.data.cardName}: -{dmg} | {hpBefore} -> {target.currentHealth}");
                    }
                }
            }

            // Khong trung muc tieu song nao (rong hoac da chet) → tuy chon danh Nexus.
            if (liveHits == 0)
            {
                if (hitNexusIfNoTarget && gc != null && dmg > 0)
                {
                    var dealer = gc.BattleSkillUnitFor(source) ?? source; // battle skill → unit goc
                    gc.DamageEnemyNexusFrom(dealer, dmg);
                    Debug.Log($"[Skill] {source?.data?.cardName}: khong co muc tieu song → {dmg} vao Nexus dich.");
                }
                else
                {
                    Debug.LogWarning($"[Skill] {source?.data?.cardName}: Damage Target — khong co muc tieu.");
                }
            }
        }
    }
}