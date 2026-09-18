using System.Collections.Generic;
using UnityEngine;
using LoRClone.Data;
using LoRClone.Model;
using LoRClone.Controller;

namespace LoRClone.Skills
{
    /// <summary>
    /// Chế độ chọn TARGET dùng CHUNG cho mọi skill "tác động lên 1 (hoặc N) unit"
    /// (DamageTarget, Recall, BuffTarget...). Tách khỏi SkillAttackWithAlly để mọi skill
    /// khác dùng lại được, không phải copy-paste enum + logic tìm target riêng.
    ///
    /// ① UseTargetSelection — hành vi CŨ: target = ctx.targetCards do người chơi/UI chọn.
    ///    Dùng khi skill nằm trên 1 lá SPELL cần targeting (requiresTarget=true), hoặc Unit
    ///    WhenPlayed cần chọn target tay.
    ///
    /// ② Mọi mode còn lại — TỰ ĐỘNG chọn target, KHÔNG cần targeting UI. Dùng khi gán thẳng
    ///    skill vào CardData.abilities của Unit/Spell/Equipment để làm kỹ năng/thiên phú
    ///    "tự kích hoạt" (VD: OnAttack tự bắn địch yếu nhất, OnStrike tự đánh thêm kẻ giao tranh...).
    /// </summary>
    public enum AutoTargetMode
    {
        UseTargetSelection, // target = ctx.targetCards đã chọn (spell targeting / unit WhenPlayed targeting)
        Self,               // target = chính source (unit sở hữu skill)
        BattleOpponent,     // target = kẻ đang giao tranh (blockedBy/blockingTarget) — dùng OnStrike/OnBlock
        StrongestEnemyAtk, WeakestEnemyAtk,
        StrongestEnemyHp, WeakestEnemyHp,
        RandomEnemy,
        StrongestAllyAtk, WeakestAllyAtk,
        RandomAlly,
    }

    public static class SkillTargetResolver
    {
        /// <summary>Trả về danh sách target theo mode (tối đa 'count' phần tử).</summary>
        public static List<CardModel> Resolve(AutoTargetMode mode, CardModel source, GameContext ctx,
                                               int count = 1, bool includeBench = true)
        {
            var result = new List<CardModel>();
            if (source == null) return result;
            var gc = (ctx?.controller as GameController) ?? GameController.Instance;

            switch (mode)
            {
                case AutoTargetMode.UseTargetSelection:
                    if (ctx?.targetCards != null) result.AddRange(ctx.targetCards);
                    break;

                case AutoTargetMode.Self:
                    if (source.IsAlive) result.Add(source);
                    break;

                case AutoTargetMode.BattleOpponent:
                {
                    var unit = (gc != null ? gc.BattleSkillUnitFor(source) : null) ?? source;
                    var foe = unit?.blockedBy ?? unit?.blockingTarget;
                    if (foe != null && foe.IsAlive) result.Add(foe);
                    break;
                }

                case AutoTargetMode.RandomEnemy:
                {
                    var pool = gc?.EnemyUnitsOf(source, battlefieldOnly: !includeBench) ?? new List<CardModel>();
                    AddRandom(pool, count, result);
                    break;
                }

                case AutoTargetMode.RandomAlly:
                {
                    var pool = AllyPool(ctx, includeBench);
                    pool.Remove(source);
                    AddRandom(pool, count, result);
                    break;
                }

                default: // Strongest/Weakest Atk/Hp — Enemy hoặc Ally
                {
                    bool isAlly = mode == AutoTargetMode.StrongestAllyAtk || mode == AutoTargetMode.WeakestAllyAtk;
                    var pool = isAlly
                        ? AllyPool(ctx, includeBench)
                        : gc?.EnemyUnitsOf(source, battlefieldOnly: !includeBench) ?? new List<CardModel>();
                    pool.RemoveAll(c => c == null || !c.IsAlive);
                    pool.Sort((a, b) => CompareFor(mode, a, b));
                    for (int i = 0; i < Mathf.Min(count, pool.Count); i++) result.Add(pool[i]);
                    break;
                }
            }
            return result;
        }

        /// <summary>Tiện dùng khi skill chỉ cần đúng 1 target (VD: AttackWithAlly, DamageCombatOpponent).</summary>
        public static CardModel ResolveSingle(AutoTargetMode mode, CardModel source, GameContext ctx, bool includeBench = true)
        {
            var list = Resolve(mode, source, ctx, 1, includeBench);
            return list.Count > 0 ? list[0] : null;
        }

        static List<CardModel> AllyPool(GameContext ctx, bool includeBench)
        {
            var list = new List<CardModel>();
            if (ctx?.owner == null) return list;
            foreach (var c in ctx.owner.BattlefieldCards()) if (c.IsAlive) list.Add(c);
            if (includeBench) foreach (var c in ctx.owner.BenchCards()) if (c.IsAlive) list.Add(c);
            return list;
        }

        static void AddRandom(List<CardModel> pool, int count, List<CardModel> result)
        {
            if (pool == null) return;
            var copy = new List<CardModel>(pool);
            copy.RemoveAll(c => c == null || !c.IsAlive);
            for (int i = 0; i < count && copy.Count > 0; i++)
            {
                int idx = GameRNG.Next(copy.Count); // GameRNG — bắt buộc, đồng bộ PvP
                result.Add(copy[idx]);
                copy.RemoveAt(idx);
            }
        }

        static int CompareFor(AutoTargetMode mode, CardModel a, CardModel b)
        {
            switch (mode)
            {
                case AutoTargetMode.StrongestEnemyAtk:
                case AutoTargetMode.StrongestAllyAtk:
                    return b.currentAttack.CompareTo(a.currentAttack);
                case AutoTargetMode.WeakestEnemyAtk:
                case AutoTargetMode.WeakestAllyAtk:
                    return a.currentAttack.CompareTo(b.currentAttack);
                case AutoTargetMode.StrongestEnemyHp:
                    return b.currentHealth.CompareTo(a.currentHealth);
                case AutoTargetMode.WeakestEnemyHp:
                    return a.currentHealth.CompareTo(b.currentHealth);
                default:
                    return 0;
            }
        }
    }
}
