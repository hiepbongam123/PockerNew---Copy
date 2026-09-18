using System.Collections.Generic;
using UnityEngine;
using LoRClone.Data;
using LoRClone.Model;
using LoRClone.Controller;

namespace LoRClone.Skills
{
    /// <summary>
    /// Dùng ATK của 1 đồng minh để gây damage lên 1 unit kẻ thù.
    /// Damage = ally.currentAttack (hoặc effectiveAttack nếu useEffectiveAttack) + bonusDamage.
    /// p1 override bonusDamage.
    ///
    /// 2 CÁCH DÙNG:
    ///
    /// ① SPELL (giữ nguyên hành vi cũ) — CardData (spell):
    ///      requiresTarget = true, targetCount = 2, targetTypes = [AllyUnit, EnemyUnit]
    ///    allySource = UseTargetSelection, enemyTarget = UseTargetSelection (mặc định).
    ///    targetCards[0] = ally, targetCards[1] = enemy — y hệt trước giờ.
    ///
    /// ② GÁN THẲNG LÊN UNIT (mới) — thêm skill này vào CardData.abilities của UNIT,
    ///    KHÔNG cần targeting UI. Đổi allySource/enemyTarget sang chế độ auto:
    ///
    ///      allySource=Self, enemyTarget=BattleOpponent, condition=OnStrike
    ///        → unit tự "chém thêm" bằng ATK của mình lên đối thủ đang giao tranh khi ra đòn combat.
    ///          (Dùng OnStrike, KHÔNG dùng OnAttack — xem lưu ý timing bên dưới.)
    ///
    ///      allySource=Self, enemyTarget=RandomEnemy, condition=OnAttack
    ///        → unit tự bắn 1 địch ngẫu nhiên bằng ATK của mình ngay khi khai báo tấn công
    ///          (không phụ thuộc ai chặn, nên OnAttack dùng được bình thường ở mode này).
    ///
    ///      allySource=HighestAttackAlly, enemyTarget=StrongestEnemy, condition=WhenPlayed
    ///        → lá spell/unit "tự chọn" đồng minh ATK cao nhất bắn địch ATK cao nhất khi ra sân,
    ///          không cần người chơi target tay.
    ///
    ///    ⚠ TIMING enemyTarget=BattleOpponent: unit.blockedBy chỉ có giá trị SAU khi block được
    ///    khai báo. Nếu condition=OnAttack (fire ở B6, trước BlockDeclare) thì blockedBy luôn null
    ///    → skill tự bỏ qua. Muốn "đánh kẻ đang chặn mình" lúc tấn công → dùng condition=OnStrike.
    /// </summary>
    [CreateAssetMenu(menuName = "LoRClone/Skills/Attack With Ally", fileName = "Skill_AttackWithAlly")]
    public class SkillAttackWithAlly : SkillData
    {
        public enum AllySourceMode
        {
            UseTargetSelection, // ally = targetCards đã chọn qua spell targeting (hành vi cũ)
            Self,               // ally = chính unit sở hữu ability (gán thẳng lên unit)
            HighestAttackAlly,  // ally = đồng minh (kể cả self) ATK cao nhất trên sân (bench+battlefield)
        }

        public enum EnemyTargetMode
        {
            UseTargetSelection, // enemy = targetCards đã chọn qua spell targeting (hành vi cũ)
            BattleOpponent,     // enemy = kẻ đang giao tranh (blockedBy/blockingTarget) — xem lưu ý timing
            StrongestEnemy,     // enemy = địch ATK cao nhất trên sân
            WeakestEnemy,       // enemy = địch ATK thấp nhất trên sân
            RandomEnemy,        // enemy = địch ngẫu nhiên (GameRNG — đồng bộ PvP)
        }

        [Header("Nguồn ATK (đồng minh)")]
        public AllySourceMode allySource = AllySourceMode.UseTargetSelection;

        [Header("Mục tiêu (kẻ địch)")]
        public EnemyTargetMode enemyTarget = EnemyTargetMode.UseTargetSelection;

        [Tooltip("Damage cộng thêm ngoài ATK của đồng minh. Thường để 0.")]
        public int bonusDamage = 0;

        [Tooltip("BẬT = dùng effectiveAttack (Thần Thép/Formidable...) thay vì currentAttack thô.")]
        public bool useEffectiveAttack = false;

        [Tooltip("Chỉ áp dụng khi enemyTarget != UseTargetSelection/BattleOpponent. " +
                 "BẬT = quét cả bench địch, TẮT = chỉ quét battlefield địch.")]
        public bool includeBenchEnemies = false;

        public override void Execute(CardModel source, GameContext ctx,
                                     int p1 = -1, int p2 = -1, int p3 = -1)
        {
            int bonus = p1 >= 0 ? p1 : bonusDamage;
            var gc = (ctx?.controller as GameController) ?? GameController.Instance;

            // Con trỏ đọc targetCards theo đúng thứ tự resolve (ally trước, enemy sau).
            // Cho phép mix: VD allySource=Self (không tốn target) + enemyTarget=UseTargetSelection
            // (targetCount=1, targetTypes=[EnemyUnit]) → vẫn đọc đúng targetCards[0] cho enemy.
            int targetPtr = 0;
            CardModel NextSelectedTarget() =>
                ctx != null && ctx.targetCards != null && targetPtr < ctx.targetCards.Count
                    ? ctx.targetCards[targetPtr++] : null;

            // ── Resolve ally ─────────────────────────────────────
            CardModel ally;
            switch (allySource)
            {
                case AllySourceMode.Self: ally = source; break;
                case AllySourceMode.HighestAttackAlly: ally = FindExtremeAlly(ctx, highest: true); break;
                default: ally = NextSelectedTarget(); break; // UseTargetSelection
            }

            if (ally == null || !ally.IsAlive)
            {
                Debug.LogWarning($"[Skill] {source.data.cardName}: AttackWithAlly — không tìm được ally hợp lệ ({allySource}).");
                return;
            }

            // ── Resolve enemy ────────────────────────────────────
            CardModel enemy;
            switch (enemyTarget)
            {
                case EnemyTargetMode.BattleOpponent: enemy = ResolveBattleOpponent(source, gc); break;
                case EnemyTargetMode.StrongestEnemy: enemy = FindExtremeEnemy(source, gc, highest: true); break;
                case EnemyTargetMode.WeakestEnemy: enemy = FindExtremeEnemy(source, gc, highest: false); break;
                case EnemyTargetMode.RandomEnemy: enemy = FindRandomEnemy(source, gc); break;
                default: enemy = NextSelectedTarget(); break; // UseTargetSelection
            }

            if (enemy == null || !enemy.IsAlive)
            {
                Debug.Log($"[Skill] {source.data.cardName}: AttackWithAlly — không tìm được enemy hợp lệ ({enemyTarget}), bỏ qua.");
                return;
            }

            // Hòa Hợp (SpellShield): vô hiệu skill kế tiếp — tiêu shield, không gây damage.
            if (enemy.TryConsumeSpellShield())
            {
                Debug.Log($"[Hòa Hợp] {enemy.data.cardName} chặn skill AttackWithAlly của {source.data.cardName}!");
                return;
            }

            int atk = useEffectiveAttack ? ally.effectiveAttack : ally.currentAttack;
            int dmg = atk + bonus;
            enemy.TakeDamage(dmg);

            Debug.Log($"[Skill] {source.data.cardName}: {ally} dùng phép tấn công {enemy} — -{dmg} HP " +
                      $"(ATK {atk}" + (bonus != 0 ? $" + bonus {bonus}" : "") + ")");
        }

        // ── Helpers ──────────────────────────────────────────────

        /// <summary>Kẻ đang giao tranh với unit gốc — hỗ trợ cả battle-skill cũ lẫn gán thẳng lên unit.</summary>
        static CardModel ResolveBattleOpponent(CardModel source, GameController gc)
        {
            var unit = (gc != null ? gc.BattleSkillUnitFor(source) : null) ?? source;
            return unit?.blockedBy ?? unit?.blockingTarget;
        }

        List<CardModel> CollectEnemies(CardModel source, GameController gc)
        {
            var list = new List<CardModel>();
            if (gc != null)
                list.AddRange(gc.EnemyUnitsOf(source, battlefieldOnly: !includeBenchEnemies));
            return list;
        }

        CardModel FindExtremeEnemy(CardModel source, GameController gc, bool highest)
        {
            CardModel best = null;
            foreach (var c in CollectEnemies(source, gc))
            {
                if (!c.IsAlive) continue;
                if (best == null || (highest ? c.currentAttack > best.currentAttack : c.currentAttack < best.currentAttack))
                    best = c;
            }
            return best;
        }

        static CardModel FindRandomEnemy(CardModel source, GameController gc)
        {
            var enemies = gc?.EnemyUnitsOf(source, battlefieldOnly: false);
            if (enemies == null) return null;
            enemies.RemoveAll(c => !c.IsAlive);
            if (enemies.Count == 0) return null;
            return enemies[GameRNG.Next(enemies.Count)]; // GameRNG — bắt buộc, đồng bộ PvP
        }

        static CardModel FindExtremeAlly(GameContext ctx, bool highest)
        {
            if (ctx?.owner == null) return null;
            CardModel best = null;
            void Scan(IEnumerable<CardModel> cards)
            {
                foreach (var c in cards)
                {
                    if (!c.IsAlive) continue;
                    if (best == null || (highest ? c.currentAttack > best.currentAttack : c.currentAttack < best.currentAttack))
                        best = c;
                }
            }
            Scan(ctx.owner.BattlefieldCards());
            Scan(ctx.owner.BenchCards());
            return best;
        }
    }
}