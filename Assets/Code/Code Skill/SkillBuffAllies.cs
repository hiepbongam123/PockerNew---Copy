using System.Collections.Generic;
using UnityEngine;
using LoRClone.Data;
using LoRClone.Model;

namespace LoRClone.Skills
{
    /// <summary>
    /// Buff ATK/HP (+ grant keyword) cho 1 hoặc nhiều đồng minh. GỘP 3 skill cũ
    /// (SkillBuffAlly, SkillBuffNAllies, SkillBuffRandomAlly) thành 1 — cùng pattern
    /// "Pick" như SkillDamageWeakestOrStrongest cho nhất quán codebase.
    ///
    /// KHÔNG còn buff ngẫu nhiên — LoR gốc không có cơ chế này, đa số bài buff
    /// đồng minh yếu/mạnh nhất hoặc vị trí cố định (Support/Cộng Lực). Thay bằng
    /// pick = LowestAttack/HighestAttack/LowestHealth/HighestHealth.
    ///
    /// ══ 2 CÁCH DÙNG ══
    ///
    /// ① SPELL — target chọn tay (CardData: requiresTarget=true, targetTypes=[AllyUnit,...]):
    ///      pick = UseTargetSelection   ← mặc định, giữ nguyên hành vi target-tay cũ.
    ///
    /// ② GÁN THẲNG LÊN UNIT (ability, không cần targeting UI) — đổi pick sang 1 trong:
    ///      RightNeighbor   → bench slot bên phải source (Support/Cộng Lực — CHỈ đúng khi
    ///                        source đang ở bench, tương đương SkillBuffAlly cũ).
    ///      Self            → buff chính source.
    ///      LowestAttack / HighestAttack / LowestHealth / HighestHealth → tự chọn N đồng minh
    ///                        yếu/mạnh nhất theo tiêu chí (count = số lượng, mặc định 1).
    ///      AllAllies       → buff toàn bộ đồng minh còn sống (scope quyết định phạm vi quét).
    ///
    /// ══ MIGRATE TỪ 3 SKILL CŨ ══
    ///   SkillBuffAlly (Right Neighbor)     → pick = RightNeighbor
    ///   SkillBuffNAllies (N ngẫu nhiên)    → pick = LowestAttack/HighestAttack (thay vì random),
    ///                                        count = N cũ
    ///   SkillBuffRandomAlly (1 ngẫu nhiên) → pick = LowestAttack/HighestAttack, count = 1
    ///
    /// ══ statOp — CÁCH TÍNH BUFF (mới) ══
    ///   Add      (mặc định) → cộng thẳng attackBuff/healthBuff, y hệt hành vi cũ.
    ///   Double   → NHÂN ĐÔI chỉ số hiện tại (currentAttack/currentHealth × 2). Buff spell.
    ///   Halve    → GIẢM CÒN 1 NỬA (làm tròn xuống). Dùng cho spell DEBUFF — gán skill này lên
    ///              spell địch, pick = UseTargetSelection, targetTypes = [EnemyUnit].
    ///   SetZero  → SET VỀ 0 — tương đương Frostbite/"Đóng Băng" của LoR (ATK về 0 tạm thời/
    ///              vĩnh viễn tùy temporaryBuff). Mặc định chỉ áp ATK (affectHealth = false),
    ///              vì set HP = 0 = giết chết unit — chỉ bật affectHealth nếu CỐ Ý muốn vậy.
    ///   3 mode Double/Halve/SetZero dùng affectAttack/affectHealth để chọn áp lên stat nào,
    ///   KHÔNG dùng attackBuff/healthBuff (2 field đó chỉ có nghĩa khi statOp = Add).
    /// </summary>
    [CreateAssetMenu(menuName = "LoRClone/Skills/Buff Allies", fileName = "Skill_BuffAllies")]
    public class SkillBuffAllies : SkillData
    {
        public enum PickMode
        {
            UseTargetSelection, // targetCards đã chọn qua spell targeting (spell mode)
            RightNeighbor,      // bench slot bên phải source (Support/Cộng Lực — unit mode)
            Self,                // buff chính source
            LowestAttack,
            HighestAttack,
            LowestHealth,
            HighestHealth,
            AllAllies,
        }

        public enum Scope { AllUnits, BattlefieldOnly, BenchOnly }

        public enum StatOp
        {
            Add,       // cộng thẳng attackBuff/healthBuff (hành vi cũ)
            Double,    // × 2 chỉ số hiện tại
            Halve,     // × 0.5 chỉ số hiện tại (làm tròn xuống) — debuff
            SetZero,   // = 0 — Frostbite/Đóng Băng
        }

        [Header("Chọn mục tiêu")]
        public PickMode pick = PickMode.UseTargetSelection;

        [Tooltip("Phạm vi quét — chỉ áp dụng cho Lowest/Highest*/AllAllies.")]
        public Scope scope = Scope.AllUnits;

        [Tooltip("Số đồng minh được buff theo tiêu chí — chỉ áp dụng cho Lowest/Highest*.")]
        [Min(1)]
        public int count = 1;

        [Header("Cách tính buff")]
        public StatOp statOp = StatOp.Add;

        [Header("Buff Stats — chỉ dùng khi statOp = Add")]
        public int attackBuff = 1;
        public int healthBuff = 0;

        [Header("Áp dụng stat nào — chỉ dùng khi statOp = Double/Halve/SetZero")]
        public bool affectAttack = true;

        [Tooltip("⚠ Với statOp = SetZero, bật cái này = set HP về 0 = GIẾT unit ngay. " +
                 "Chỉ bật nếu cố ý muốn vậy (VD: skill xử tử).")]
        public bool affectHealth = false;

        [Header("Grant Keywords")]
        [Tooltip("Keyword cấp thêm cho mục tiêu. Để trống nếu chỉ buff chỉ số.")]
        public KeywordType[] grantKeywords = new KeywordType[0];

        [Tooltip("BẬT = loại chính source khỏi danh sách ứng viên (Lowest/Highest*/AllAllies).")]
        public bool excludeSelf = true;

        public override void Execute(CardModel source, GameContext ctx,
                                     int p1 = -1, int p2 = -1, int p3 = -1)
        {
            int atk = p1 >= 0 ? p1 : attackBuff;
            int hp = p2 >= 0 ? p2 : healthBuff;
            int cnt = p3 >= 0 ? p3 : count;
            bool tmp = ctx != null && ctx.temporaryBuff;

            var targets = ResolveTargets(source, ctx, cnt);
            if (targets.Count == 0)
            {
                Debug.Log($"[Skill] {source.data.cardName}: BuffAllies — không tìm được mục tiêu ({pick}).");
                return;
            }

            foreach (var ally in targets)
            {
                if (ally == null || !ally.IsAlive) continue;

                var (atkDelta, hpDelta) = ComputeDeltas(ally, atk, hp);

                if (atkDelta != 0) ally.BuffAttack(atkDelta, tmp);
                if (hpDelta != 0) ally.BuffHealth(hpDelta, tmp);
                if (atkDelta != 0 || hpDelta != 0)
                    ally.RecordSkillBuff(source.data.cardName, source.data.artwork, atkDelta, hpDelta, tmp);
                foreach (var kw in grantKeywords) ally.GrantKeyword(kw);

                Debug.Log($"[Skill] {source.data.cardName} → {ally.data.cardName} ({pick}/{statOp}): " +
                          $"{(atkDelta >= 0 ? "+" : "")}{atkDelta} ATK / {(hpDelta >= 0 ? "+" : "")}{hpDelta} HP" +
                          (grantKeywords.Length > 0 ? $" +{string.Join(",", grantKeywords)}" : "") +
                          $" {(tmp ? "(round)" : "(perm)")}");
            }
        }

        // ── Helpers ──────────────────────────────────────────────

        /// <summary>Tính delta ATK/HP cần cộng (âm = trừ) theo statOp. atk/hp chỉ dùng khi statOp=Add.</summary>
        (int atkDelta, int hpDelta) ComputeDeltas(CardModel ally, int atk, int hp)
        {
            switch (statOp)
            {
                case StatOp.Add:
                    return (atk, hp);

                case StatOp.Double:
                    return (affectAttack ? ally.currentAttack : 0,
                            affectHealth ? ally.currentHealth : 0);

                case StatOp.Halve:
                    return (affectAttack ? (ally.currentAttack / 2) - ally.currentAttack : 0,
                            affectHealth ? HpDeltaClamped(ally, ally.currentHealth / 2) : 0);

                case StatOp.SetZero:
                    return (affectAttack ? -ally.currentAttack : 0,
                            affectHealth ? HpDeltaClamped(ally, 0) : 0);

                default:
                    return (0, 0);
            }
        }

        /// <summary>Delta HP tới targetValue, nhưng không để maxHealth tụt dưới 1 (BuffHealth cộng thẳng vào maxHealth).</summary>
        static int HpDeltaClamped(CardModel ally, int targetCurrentHealth)
        {
            int delta = targetCurrentHealth - ally.currentHealth;
            if (ally.maxHealth + delta < 1) delta = 1 - ally.maxHealth;
            return delta;
        }

        List<CardModel> ResolveTargets(CardModel source, GameContext ctx, int cnt)
        {
            var result = new List<CardModel>();
            switch (pick)
            {
                case PickMode.UseTargetSelection:
                    if (ctx?.targetCards != null) result.AddRange(ctx.targetCards);
                    break;

                case PickMode.RightNeighbor:
                    {
                        var bench = ctx?.owner?.bench;
                        int nextSlot = source.benchSlot + 1;
                        if (bench != null && nextSlot > 0 && nextSlot < bench.Length && bench[nextSlot] != null)
                            result.Add(bench[nextSlot]);
                        break;
                    }

                case PickMode.Self:
                    result.Add(source);
                    break;

                case PickMode.AllAllies:
                    result.AddRange(CollectAllies(source, ctx));
                    break;

                default: // Lowest/HighestAttack/Health — sort rồi lấy cnt đầu
                    var pool = CollectAllies(source, ctx);
                    pool.Sort(Compare);
                    int toTake = Mathf.Min(cnt, pool.Count);
                    for (int i = 0; i < toTake; i++) result.Add(pool[i]);
                    break;
            }
            return result;
        }

        List<CardModel> CollectAllies(CardModel source, GameContext ctx)
        {
            var list = new List<CardModel>();
            if (ctx?.owner == null) return list;
            if (scope != Scope.BenchOnly)
                foreach (var c in ctx.owner.BattlefieldCards())
                    if (c.IsAlive && (!excludeSelf || c != source)) list.Add(c);
            if (scope != Scope.BattlefieldOnly)
                foreach (var c in ctx.owner.BenchCards())
                    if (c.IsAlive && (!excludeSelf || c != source)) list.Add(c);
            return list;
        }

        // Sắp xếp để mục tiêu ƯU TIÊN nằm ở đầu danh sách (index 0..).
        int Compare(CardModel a, CardModel b)
        {
            switch (pick)
            {
                case PickMode.LowestAttack: return a.currentAttack.CompareTo(b.currentAttack);
                case PickMode.HighestAttack: return b.currentAttack.CompareTo(a.currentAttack);
                case PickMode.LowestHealth: return a.currentHealth.CompareTo(b.currentHealth);
                case PickMode.HighestHealth: return b.currentHealth.CompareTo(a.currentHealth);
                default: return 0;
            }
        }
    }
}