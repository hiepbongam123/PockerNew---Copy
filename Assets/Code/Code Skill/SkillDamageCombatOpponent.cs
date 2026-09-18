using UnityEngine;
using LoRClone.Data;
using LoRClone.Model;
using LoRClone.Controller;

namespace LoRClone.Skills
{
    /// <summary>
    /// Battle-skill kiểu Renekton: gây damage lên unit đang GIAO TRANH với unit đã kích skill.
    ///   • Khi unit TẤN CÔNG → đánh kẻ đang CHẶN nó (blockedBy).
    ///   • Khi unit CHẶN     → đánh kẻ đang TẤN CÔNG nó (blockingTarget).
    ///
    /// 2 CÁCH DÙNG:
    ///
    /// ① BATTLE-SKILL CŨ (spellcraftSpell + UnitSkill + unitSkillOnAttack): source lúc Execute
    ///    là LÁ SKILL trên spell zone → tự tìm unit gốc qua BattleSkillUnitFor. Không đổi gì, vẫn chạy y cũ.
    ///
    /// ② GÁN THẲNG LÊN UNIT (mới): thêm skill này vào CardData.abilities của UNIT, KHÔNG cần
    ///    spellcraftSpell/UnitSkill gì cả. source lúc Execute chính là unit → BattleSkillUnitFor
    ///    trả null → fallback dùng thẳng source.
    ///
    ///    ⚠ CHỌN CONDITION ĐÚNG (rất quan trọng):
    ///      - condition = OnStrike  → ĐÚNG cho cả 2 chiều (tấn công lẫn chặn). Lúc này combat đã
    ///        resolve xong 1 nhịp, blockedBy/blockingTarget đã được gán đầy đủ.
    ///      - condition = OnBlock   → ĐÚNG cho unit CHẶN (blockingTarget đã có ngay khi block declare).
    ///      - condition = OnAttack  → SAI cho mục đích này. OnAttack fire TRƯỚC khi đối thủ khai báo
    ///        block (B6, trước BlockDeclare) → blockedBy luôn null → skill sẽ không tìm thấy ai và
    ///        tự bỏ qua (log "không có kẻ giao tranh"). Muốn hiệu ứng lúc tấn công, dùng OnStrike.
    /// </summary>
    [CreateAssetMenu(menuName = "LoRClone/Skills/Damage Combat Opponent", fileName = "Skill_DamageCombatOpponent")]
    public class SkillDamageCombatOpponent : SkillData
    {
        [Tooltip("Sát thương lên kẻ giao tranh (kẻ chặn nếu ta tấn công / kẻ tấn công nếu ta chặn).")]
        public int damage = 2;

        [Tooltip("BẬT = chỉ kích khi unit đang TẤN CÔNG (bỏ qua khi chặn).")]
        public bool onlyWhenAttacking = false;

        [Tooltip("BẬT = cộng damage vào bộ đếm của unit gốc (để lên cấp theo damage như Renekton Lv2).")]
        public bool countForSource = true;

        public override void Execute(CardModel source, GameContext ctx,
                                     int p1 = -1, int p2 = -1, int p3 = -1)
        {
            var gc = (ctx?.controller as GameController) ?? GameController.Instance;
            if (gc == null) return;
            int dmg = p1 >= 0 ? p1 : damage;
            if (dmg <= 0) return;

            // ① battle-skill cũ: source = LÁ SKILL → tra unit gốc.
            // ② gán thẳng lên unit: BattleSkillUnitFor trả null → fallback dùng chính source (đã là unit).
            var unit = gc.BattleSkillUnitFor(source) ?? source;
            if (unit == null) return;

            if (onlyWhenAttacking && unit.state != CardState.Attacking)
            {
                Debug.Log($"[Skill] {unit.data.cardName}: onlyWhenAttacking — đang không tấn công, bỏ qua.");
                return;
            }

            // Ta tấn công → đánh kẻ CHẶN (blockedBy). Ta chặn → đánh kẻ TẤN CÔNG (blockingTarget).
            var foe = unit.blockedBy ?? unit.blockingTarget;
            if (foe == null || !foe.IsAlive)
            {
                Debug.Log($"[Skill] {unit.data.cardName}: không có kẻ giao tranh (chưa bị chặn / không chặn ai / trigger quá sớm).");
                return;
            }

            // Hòa Hợp (SpellShield): vô hiệu skill kế tiếp — tiêu shield, không gây damage.
            if (foe.TryConsumeSpellShield())
            {
                Debug.Log($"[Hòa Hợp] {foe.data.cardName} chặn skill của {unit.data.cardName}!");
                return;
            }

            int hpBefore = foe.currentHealth;
            foe.TakeDamage(dmg);
            int dealt = hpBefore - foe.currentHealth;
            Debug.Log($"[Skill] {unit.data.cardName} → giao tranh {foe.data.cardName}: -{dmg} | {hpBefore} → {foe.currentHealth}");
            if (countForSource && dealt > 0) gc.RegisterSkillDamage(unit, dealt);
            // ProcessDeaths do closure resolve của spell stack / combat coroutine tự chạy sau khi Execute xong.
        }
    }
}