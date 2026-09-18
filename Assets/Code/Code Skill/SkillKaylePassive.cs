using UnityEngine;
using LoRClone.Data;
using LoRClone.Model;

namespace LoRClone
{
    /// <summary>
    /// Passive của Kayle — buff dù đang ở hand, deck, bench hay battlefield. Skill này
    /// TRIGGER-AGNOSTIC (chỉ đọc ctx.triggerAmount) nên gắn được với BẤT KỲ trigger "Anywhere"
    /// nào (đều fire qua TriggerAllCards — quét cả deck), không chỉ riêng cường hóa tấn công:
    ///
    ///   • OnEmpoweredAllyAttack → chỉ đồng minh CƯỜNG HÓA (currentAttack > baseAttack) tấn công.
    ///   • OnAllyAttackAnywhere  → BẤT KỲ đồng minh nào tấn công (không cần cường hóa).
    ///   • OnAllyDeathAnywhere   → khi đồng minh chết (triggerAmount=0 → skill tự coi count=1).
    ///
    /// Gắn NHIỀU entry cùng lúc vào Abilities (mỗi entry 1 trigger, cùng trỏ tới asset này,
    /// p1/p2 riêng cho từng loại sự kiện) để cộng dồn nhiều nguồn buff cho Kayle.
    ///
    /// ─── Cách setup trong Inspector ───────────────────────────────
    ///
    /// 1. Tạo asset này: Assets → Create → LoRClone/Skills/KaylePassive.
    ///
    /// 2. Trên CardData của Kayle, thêm entry vào "Abilities" — 1 entry cho mỗi loại sự kiện
    ///    muốn buff (có thể thêm cả 3):
    ///      condition  = OnEmpoweredAllyAttack / OnAllyAttackAnywhere / OnAllyDeathAnywhere
    ///      effect     = [asset KaylePassive vừa tạo]
    ///      p1         = ATK buff mỗi lần trigger (default 1, hoặc để -1 dùng defaultAtkBuff)
    ///      p2         = HP buff mỗi lần trigger (default 0, hoặc để -1 dùng defaultHpBuff)
    ///
    /// 3. Thêm các entry vào "Conditional Keywords" của Kayle để define level-up:
    ///      keyword         = Overwhelm (hoặc keyword khác)
    ///      requiredAttack  = 10  (grant khi ATK ≥ 10)
    ///      requiredHealth  = 0   (bỏ qua)
    ///      mode            = All
    ///
    ///    Khi Kayle đạt đủ ATK, skill tự gọi GrantKeyword → Kayle nhận keyword.
    ///
    /// 4. Thêm các entry vào "Conditional Abilities" của Kayle để define extra ability
    ///    khi đủ ngưỡng (ví dụ: OnAttack → damage tất cả địch 1 điểm):
    ///      condition       = { requiredAttack = 10, mode = All }
    ///      ability.trigger = OnAttack
    ///      ability.effect  = [skill damage]
    ///
    /// ─── Logic ────────────────────────────────────────────────────
    ///
    /// ctx.triggerAmount = số đồng minh được cường hóa đang tấn công.
    /// Kayle nhận +p1 ATK * count và +p2 HP * count.
    /// Sau buff: kiểm tra tất cả conditionalKeywords — grant nếu thỏa.
    /// ConditionalAbility được GameController tự kiểm tra qua ExecuteMatching.
    /// </summary>
    [CreateAssetMenu(menuName = "LoRClone/Skills/KaylePassive", fileName = "SkillKaylePassive")]
    public class SkillKaylePassive : SkillData
    {
        [Header("Buff mỗi đồng minh cường hóa tấn công")]
        [Tooltip("ATK buff cho Kayle mỗi đồng minh cường hóa. Dùng khi p1 = -1 (không override).")]
        [Min(0)]
        public int defaultAtkBuff = 1;

        [Tooltip("HP buff cho Kayle mỗi đồng minh cường hóa. Dùng khi p2 = -1 (không override).")]
        [Min(0)]
        public int defaultHpBuff = 0;

        // ──────────────────────────────────────────────────────────
        public override void Execute(CardModel card, GameContext ctx,
                                     int p1 = -1, int p2 = -1, int p3 = -1)
        {
            int atk = p1 >= 0 ? p1 : defaultAtkBuff;
            int hp = p2 >= 0 ? p2 : defaultHpBuff;
            int count = ctx.triggerAmount > 0 ? ctx.triggerAmount : 1;

            // Buff Kayle dù đang ở bất kỳ đâu
            if (atk > 0) card.BuffAttack(atk * count);
            if (hp > 0) card.BuffHealth(hp * count);

            Debug.Log($"[Kayle] {card.data.cardName} nhận +{atk * count} ATK / " +
                      $"+{hp * count} HP ({count} đồng minh cường hóa) " +
                      $"→ ATK hiện tại: {card.currentAttack}");

            // Grant ConditionalKeywords nếu thỏa ngưỡng sau khi buff
            // ConditionalAbility được GameController xử lý riêng trong ExecuteMatching.
            GrantConditionalKeywords(card);
        }

        // ── Helper ────────────────────────────────────────────────
        /// <summary>
        /// Duyệt toàn bộ conditionalKeywords của card, grant keyword nếu:
        ///   • StatCondition thỏa (dùng card.EvalCondition).
        ///   • Keyword chưa được grant (tránh duplicate GrantKeyword).
        /// </summary>
        static void GrantConditionalKeywords(CardModel card)
        {
            if (card.data.conditionalKeywords == null) return;

            foreach (var ck in card.data.conditionalKeywords)
            {
                if (!card.EvalCondition(ck.condition)) continue;
                if (card.HasKeyword(ck.keyword)) continue; // đã có

                card.GrantKeyword(ck.keyword);
                Debug.Log($"[Kayle] {card.data.cardName} đạt ngưỡng ATK " +
                          $"(req ≥ {ck.condition.requiredAttack}) → grant [{ck.keyword}].");
            }
        }
    }
}