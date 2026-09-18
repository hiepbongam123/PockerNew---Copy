using UnityEngine;
using LoRClone.Model;

namespace LoRClone.Data
{
    /// <summary>
    /// Buff ATK và/hoặc HP cho 1 lá bài trên tay do PLAYER tự chọn.
    ///
    /// Khác với SkillBuffCardsInHand (buff ngẫu nhiên / buff tất cả),
    /// skill này đọc target từ ctx.targetCards[0] — lá mà player đã click chọn.
    ///
    /// ── Setup trên CardData của spell / unit ─────────────────────────────────
    ///   requiresTarget = true
    ///   targetType     = AllyInHand          ← TargetType mới (xem hướng dẫn bên dưới)
    ///   targetCount    = 1
    ///   CardAbility:
    ///     trigger        = WhenPlayed
    ///     effect         = asset SkillBuffTargetInHand
    ///     p1             = attackBuff  (-1 = dùng asset default)
    ///     p2             = healthBuff  (-1 = dùng asset default)
    ///     temporaryBuff  = false       (grant vĩnh viễn — đúng như LoR)
    ///
    /// ── Cần làm thêm trong GameController ───────────────────────────────────
    ///   Trong phần lọc target hợp lệ (GetValidTargets hoặc HighlightTargets),
    ///   thêm case AllyInHand:
    ///     return owner.hand (tất cả lá trên tay owner)
    ///   → GameController sẽ highlight hand cards thay vì board units.
    /// </summary>
    [CreateAssetMenu(menuName = "LoRClone/Skills/BuffTargetInHand", fileName = "SkillBuffTargetInHand")]
    public class SkillBuffTargetInHand : SkillData
    {
        [Header("Buff Amount")]
        [Tooltip("ATK buff cho lá được chọn. Override bằng p1 trong CardAbility (-1 = dùng giá trị này).")]
        public int attackBuff = 2;

        [Tooltip("HP buff cho lá được chọn. Override bằng p2 trong CardAbility (-1 = dùng giá trị này).")]
        public int healthBuff = 2;

        /// <summary>Tự báo cho GameView biết skill này cần chọn 1 lá trên tay — không cần set Inspector.</summary>
        public override TargetType RequiredTargetType => TargetType.AllyInHand;

        public override void Execute(CardModel source, GameContext ctx,
                                     int p1 = -1, int p2 = -1, int p3 = -1)
        {
            int atkBuff = p1 >= 0 ? p1 : attackBuff;
            int hpBuff = p2 >= 0 ? p2 : healthBuff;

            if (atkBuff == 0 && hpBuff == 0) return;

            // Lấy target đầu tiên player đã chọn
            var target = ctx.targetCard;

            if (target == null)
            {
                Debug.LogWarning("[SkillBuffTargetInHand] Không có target — skill bị bỏ qua.");
                return;
            }

            // Đảm bảo target đang ở trong tay (bảo vệ khỏi edge case targeting sai)
            if (target.location != CardLocation.InHand)
            {
                Debug.LogWarning($"[SkillBuffTargetInHand] Target {target} không ở InHand (location={target.location}) — bỏ qua.");
                return;
            }

            if (atkBuff != 0) target.BuffAttack(atkBuff, ctx.temporaryBuff);
            if (hpBuff != 0) target.BuffHealth(hpBuff, ctx.temporaryBuff);
            target.RecordSkillBuff(source.data.cardName, source.data.artwork, atkBuff, hpBuff, ctx.temporaryBuff);

            Debug.Log($"[SkillBuffTargetInHand] {target.data.cardName} được grant +{atkBuff}|+{hpBuff}.");
        }
    }
}