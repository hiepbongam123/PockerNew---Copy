using System.Collections.Generic;
using UnityEngine;
using LoRClone.Data;
using LoRClone.Model;
using LoRClone.Controller;

namespace LoRClone.Skills
{
    /// <summary>
    /// Triệu hồi một unit (summonTemplate) và copy stats + keyword từ source sang nó.
    ///
    /// ══ DÙNG CHO ══
    ///   • Zed level-up  → triệu hồi "Zed's Shadow" với ATK/HP/keyword của Zed hiện tại.
    ///   • Phân thân     → bất kỳ unit nào clone chính mình.
    ///   • Copy keyword  → dùng source khác bằng cách gán trigger khác (WhenPlayed, OnKill...).
    ///
    /// ══ SETUP TRONG INSPECTOR ══
    ///   1. Create Asset → LoRClone/Skills/Summon Copy
    ///   2. Gán vào CardAbility của champion level-up form:
    ///        trigger   = OnLevelUp
    ///        effect    = asset này
    ///        oncePerRound = false (chỉ xảy ra 1 lần vì OnLevelUp chỉ fire 1 lần)
    ///   3. Gán summonTemplate = CardData của unit bóng (VD: "Zed's Shadow")
    ///      Unit bóng nên có base stats thấp (0/1) — stats thực sẽ bị overwrite bởi skill.
    ///
    /// ══ CÁC TRƯỜNG HỢP MỞ RỘNG ══
    ///   • copyCurrentHealth = false → clone summon đầy máu (dùng khi source bị thương nhưng clone nên đầy máu)
    ///   • copyKeywords = false      → clone không nhận keyword
    ///   • toBattlefield = false     → clone vào bench (chưa tấn công ngay)
    ///   • summonForOpponent = true  → triệu hồi bên địch (tạo unit cho đối thủ, dùng cho trap/spell địch)
    /// </summary>
    [CreateAssetMenu(menuName = "LoRClone/Skills/Summon Copy", fileName = "Skill_SummonCopy")]
    public class SkillSummonCopy : SkillData
    {
        [Header("Nguồn template")]
        [Tooltip("true  = clone TẤT CẢ unit trong source.data.summonUnits (nhiều bóng).\n" +
                 "false = dùng field summonTemplate bên dưới (1 bóng, hành vi cũ).")]
        public bool useSourceCardData = false;

        [Header("Template — chỉ dùng khi useSourceCardData = false")]
        [Tooltip("CardData của unit được triệu hồi. Stats sẽ bị overwrite bởi skill.\n" +
                 "Đặt base stats thấp (vd 0/1) để tránh nhầm lẫn.")]
        public CardData summonTemplate;

        [Header("Vị trí summon")]
        [Tooltip("true = summon thẳng lên battlefield (có thể tấn công ngay nếu combat đang diễn ra).\n" +
                 "false = summon lên bench (thông thường hơn).")]
        public bool toBattlefield = false;

        [Tooltip("true = triệu hồi bên đối thủ (hiếm). false = triệu hồi bên sở hữu skill.")]
        public bool summonForOpponent = false;

        [Header("Stat Copy")]
        [Tooltip("Copy currentAttack từ source (bao gồm buff/debuff runtime).")]
        public bool copyAttack = true;

        [Tooltip("Copy maxHealth từ source.\n" +
                 "Nếu true + copyCurrentHealth true → clone có đúng HP (kể cả vết thương).\n" +
                 "Nếu true + copyCurrentHealth false → clone đầy máu với maxHP của source.")]
        public bool copyMaxHealth = true;

        [Tooltip("Copy currentHealth từ source (clone bị thương nếu source đang bị thương).\n" +
                 "Chỉ có hiệu lực khi copyMaxHealth = true.")]
        public bool copyCurrentHealth = false;

        [Header("Keyword Copy")]
        [Tooltip("Copy toàn bộ keyword (base + runtime grant) từ source sang clone.")]
        public bool copyKeywords = true;

        // ─────────────────────────────────────────────────────────────
        public override void Execute(CardModel source, GameContext ctx,
                                     int p1 = -1, int p2 = -1, int p3 = -1)
        {
            if (ctx?.controller == null)
            {
                Debug.LogWarning("[SkillSummonCopy] Không có GameController reference trong ctx.");
                return;
            }
            var gc = ctx.controller as GameController;
            if (gc == null) return;

            // ── Danh sách template ───────────────────────────────────
            // useSourceCardData = true  → toàn bộ source.data.summonUnits (clone NHIỀU bóng).
            // useSourceCardData = false → chỉ field summonTemplate (1 bóng, hành vi cũ).
            var templates = new List<CardData>();
            if (useSourceCardData)
            {
                var list = source?.data?.summonUnits;
                if (list != null)
                    foreach (var t in list) if (t != null) templates.Add(t);
                if (templates.Count == 0)
                {
                    Debug.LogWarning($"[SkillSummonCopy] useSourceCardData bật nhưng summonUnits rỗng trên {name}.");
                    return;
                }
            }
            else
            {
                if (summonTemplate == null)
                {
                    Debug.LogWarning($"[SkillSummonCopy] summonTemplate chưa gán trên {name}.");
                    return;
                }
                templates.Add(summonTemplate);
            }

            bool forPlayer = summonForOpponent ? !source.belongsToPlayer : source.belongsToPlayer;

            // ── Summon + copy stats/keywords cho TỪNG template ───────
            foreach (var tpl in templates)
            {
                var clone = gc.SummonUnit(tpl, forPlayer, toBattlefield);
                if (clone == null)
                {
                    Debug.LogWarning($"[SkillSummonCopy] SummonUnit '{tpl?.cardName}' trả về null — bench/battlefield đầy? Dừng.");
                    break;   // đầy chỗ → dừng, các bóng còn lại bỏ qua (không lỗi)
                }

                // ── Copy stats ───────────────────────────────────────
                int targetAtk     = copyAttack      ? source.currentAttack : clone.data.baseAttack;
                int targetMaxHp   = copyMaxHealth   ? source.maxHealth     : clone.data.baseHealth;
                int targetCurrHp  = (copyMaxHealth && copyCurrentHealth)
                                        ? source.currentHealth
                                        : targetMaxHp; // đầy máu nếu không copy currentHealth

                clone.ForceStats(targetAtk, targetMaxHp, targetCurrHp);

                // ── Copy keywords ────────────────────────────────────
                if (copyKeywords)
                {
                    foreach (var kw in source.GetAllKeywords())
                        clone.GrantKeyword(kw);
                }

                Debug.Log($"[SkillSummonCopy] Triệu hồi '{clone.data.cardName}' " +
                          $"với ATK={clone.currentAttack} HP={clone.currentHealth}/{clone.maxHealth}" +
                          (copyKeywords ? " + keywords copied" : "") + ".");
            }
        }
    }
}
