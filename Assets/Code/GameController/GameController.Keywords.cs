using System.Collections.Generic;
using UnityEngine;
using LoRClone;
using LoRClone.Data;
using LoRClone.Model;

namespace LoRClone.Controller
{
    public partial class GameController
    {
        // ── Keyword Helpers ───────────────────────────────────────

        /// <summary>
        /// Hồi Phục (Regeneration): đầu mỗi round hồi TOÀN BỘ HP (chuẩn LoR — không buff thêm).
        /// </summary>
        void ApplyRegeneration(CardModel card)
        {
            card.Heal(card.maxHealth);
            Debug.Log($"[Hồi Phục] {card.data.cardName} hồi toàn bộ HP đầu round.");
        }

        /// <summary>
        /// Tiềm Phục (Lurk): chuẩn LoR — khi bạn tấn công lúc 1 unit Lurk ở ĐẦU DECK (deck[0]),
        /// TẤT CẢ quân Lurk của bên đó (kể cả lá top + trên bench/battlefield) nhận +1|+0 vĩnh viễn.
        /// Lặp lại mỗi lần tấn công. Gọi mỗi khi có attacker được declare.
        /// </summary>
        void CheckLurk(bool byPlayer)
        {
            var p = P(byPlayer);
            if (p.deck.Count == 0) return;
            var top = p.deck[0];
            if (top == null || !top.HasKeyword(KeywordType.Lurk)) return;

            var lurkers = new List<CardModel> { top };
            foreach (var c in p.BenchCards())
                if (c.IsAlive && c.HasKeyword(KeywordType.Lurk)) lurkers.Add(c);
            foreach (var c in p.BattlefieldCards())
                if (c.IsAlive && c.HasKeyword(KeywordType.Lurk)) lurkers.Add(c);
            foreach (var c in lurkers) c.BuffAttack(1);
            Debug.Log($"[Tiềm Phục] Top deck Lurk → {lurkers.Count} quân Lurk nhận +1|+0.");
        }

        /// <summary>
        /// Vực Thẳm (Deep): khi deck ≤ 15 lá → unit Deep nhận +3|+3 vĩnh viễn (1 lần).
        /// Kiểm tra tất cả unit trên bench, battlefield, tay, và deck của bên đó.
        /// </summary>
        void CheckDeep(PlayerModel p)
        {
            if (p.deck.Count > 15) return;
            // Tất cả vùng: hand, bench, battlefield, deck
            var cards = new List<CardModel>(p.hand);
            foreach (var c in p.BenchCards()) cards.Add(c);
            foreach (var c in p.BattlefieldCards()) cards.Add(c);
            foreach (var c in p.deck) cards.Add(c);
            foreach (var c in cards)
            {
                if (c.HasKeyword(KeywordType.Deep) && !c.deepTriggered)
                {
                    c.MarkDeepTriggered();
                    c.BuffAttack(3);
                    c.BuffHealth(3);
                    Debug.Log($"[Vực Thẳm] {c.data.cardName} deck ≤ 15 → +3|+3 vĩnh viễn.");
                }
            }
        }

        /// <summary>
        /// Dòng Thức (Flow): mỗi spell được chơi → tất cả unit Flow +1|+0 tạm thời.
        /// Gọi từ HandleCastSpell và CommitStagedSpells.
        /// </summary>
        void ApplyFlowBuff(bool byPlayer)
        {
            var p = P(byPlayer);
            var flowUnits = new List<CardModel>();
            foreach (var c in p.BenchCards())
                if (c.IsAlive && c.HasKeyword(KeywordType.Flow)) flowUnits.Add(c);
            foreach (var c in p.BattlefieldCards())
                if (c.IsAlive && c.HasKeyword(KeywordType.Flow)) flowUnits.Add(c);
            foreach (var c in flowUnits)
            {
                c.BuffAttack(1, temporary: true);
                Debug.Log($"[Dòng Thức] {c.data.cardName} +1|+0 tạm (spell được chơi).");
            }
        }

        /// <summary>
        /// Cộng Hưởng (Augment): khi lá generated được chơi → tất cả unit Augment +1|+0 vĩnh viễn.
        /// </summary>
        void ApplyAugmentBuff(bool byPlayer)
        {
            var p = P(byPlayer);
            var augUnits = new List<CardModel>();
            foreach (var c in p.BenchCards())
                if (c.IsAlive && c.HasKeyword(KeywordType.Augment)) augUnits.Add(c);
            foreach (var c in p.BattlefieldCards())
                if (c.IsAlive && c.HasKeyword(KeywordType.Augment)) augUnits.Add(c);
            foreach (var c in augUnits)
            {
                c.BuffAttack(1);
                Debug.Log($"[Cộng Hưởng] {c.data.cardName} +1|+0 (lá generated được chơi).");
            }
        }

        /// <summary>
        /// Thiên Cơ (Fated): lần đầu trong round này unit này bị nhắm bởi hiệu ứng đồng minh → +1|+1.
        /// Gọi từ SkillBuffTarget, SkillHealAllAllies hoặc bất kỳ skill nào nhắm vào đồng minh.
        /// </summary>
        public void CheckFated(CardModel target)
        {
            if (target == null) return;
            if (!target.HasKeyword(KeywordType.Fated)) return;
            if (target.fatedTriggeredThisRound) return;
            target.MarkFatedTriggered();
            target.BuffAttack(1);
            target.BuffHealth(1);
            Debug.Log($"[Thiên Cơ] {target.data.cardName} lần đầu bị nhắm bởi đồng minh → +1|+1.");
        }

        /// <summary>
        /// Chỉ Định (Challenger): attacker chọn unit địch nào buộc phải block mình.
        /// Gọi khi player/AI muốn khai báo mục tiêu Challenger.
        /// forcedBlocker: unit địch bị buộc block (chuẩn LoR — không kèm debuff ATK).
        /// </summary>
        public void HandleChallengerTarget(CardModel attacker, CardModel forcedBlocker)
        {
            if (attacker == null || forcedBlocker == null) return;
            if (!attacker.HasKeyword(KeywordType.Challenger)) return;
            if (!forcedBlocker.HasKeyword(KeywordType.Vulnerable))
            {
                // Challenger chỉ buộc được unit có Vulnerable (Phơi Bày), hoặc mọi unit tùy rule
                // Theo thiết kế custom: Challenger buộc bất kỳ unit địch nào (không cần Vulnerable)
                // Vulnerable chỉ bị -2 ATK khi block. Challenger là người chọn, không cần target Vulnerable.
            }
            _challengerTargets[attacker.id] = forcedBlocker;
            Debug.Log($"[Chỉ Định] {attacker.data.cardName} buộc {forcedBlocker.data.cardName} phải block.");
        }

        // ── Thoáng Hiện / Đếm Ngược / Support ────────────────────

        /// <summary>
        /// Thoáng Hiện (Fleeting): cuối round, hủy tất cả lá Fleeting vẫn còn trên tay.
        /// Gọi từ OnRoundChanged, TRƯỚC TriggerRoundSkills(RoundStart).
        /// </summary>
        void DiscardFleetingCards()
        {
            foreach (var side in new[] { model.player, model.enemy })
            {
                var toDiscard = new List<CardModel>();
                foreach (var c in side.hand)
                    if (c.HasKeyword(KeywordType.Fleeting)) toDiscard.Add(c);
                foreach (var c in toDiscard)
                {
                    side.hand.Remove(c);
                    c.SetLocation(CardLocation.InDiscard);
                    Debug.Log($"[Thoáng Hiện] {c.data.cardName} bị hủy cuối round (còn trên tay).");
                }
                if (toDiscard.Count > 0) Notify();
            }
        }

        /// <summary>
        /// Đếm Ngược (Countdown): đầu mỗi round giảm countdown của tất cả unit Countdown trên bench/battlefield.
        /// Unit nào countdown về 0 → ForceKill → ProcessDeaths.
        /// </summary>
        void TickCountdowns()
        {
            bool anyDied = false;
            foreach (var side in new[] { model.player, model.enemy })
            {
                var cards = new List<CardModel>();
                foreach (var c in side.BenchCards()) cards.Add(c);
                foreach (var c in side.BattlefieldCards()) cards.Add(c);
                foreach (var c in cards)
                {
                    if (!c.HasKeyword(KeywordType.Countdown)) continue;
                    if (c.TickCountdown())
                    {
                        c.ForceKill();
                        anyDied = true;
                        Debug.Log($"[Đếm Ngược] {c.data.cardName} countdown về 0 → ForceKill.");
                    }
                }
            }
            if (anyDied) ProcessDeaths();
        }

        /// <summary>
        /// Cộng Lực (Support): tìm unit bên phải bench (bench[benchSlot + 1]) của attacker.
        /// Trả về null nếu attacker không có keyword Support, không có neighbor, hoặc neighbor đã chết.
        /// </summary>
        CardModel FindSupportNeighbor(CardModel attacker, bool atkIsPlayer)
        {
            if (!attacker.HasKeyword(KeywordType.Support)) return null;
            var p = P(atkIsPlayer);

            // Attacker đang ở battlefield → tìm unit ngay bên phải theo slotIndex
            int nextSlot = attacker.slotIndex + 1;
            if (nextSlot < 0 || nextSlot >= p.battlefield.Length) return null;

            var neighbor = p.battlefield[nextSlot];
            if (neighbor != null && neighbor.IsAlive) return neighbor;

            return null;
        }

        /// <summary>
        /// Cập nhật isSupportTarget cho toàn bộ unit bench của một bên.
        /// Gọi sau mỗi declare/undeclare attacker và sau combat kết thúc.
        /// </summary>
        void RefreshSupportIndicators(bool forPlayer)
        {
            var p = P(forPlayer);
            // Clear tất cả
            foreach (var c in p.bench) if (c != null) c.SetSupportTarget(false);
            foreach (var c in p.battlefield) if (c != null) c.SetSupportTarget(false);
            // Set cho unit ngay bên phải (slotIndex+1) của attacker có Support
            foreach (var attacker in p.BattlefieldCards())
            {
                if (attacker.state != CardState.Attacking) continue;
                if (!attacker.HasKeyword(KeywordType.Support)) continue;
                var neighbor = FindSupportNeighbor(attacker, forPlayer);
                if (neighbor != null) neighbor.SetSupportTarget(true);
            }
        }

        /// <summary>Clear support indicators cho cả 2 bên — gọi sau combat kết thúc.</summary>
        void ClearAllSupportIndicators()
        {
            foreach (var c in model.player.bench)
                if (c != null) c.SetSupportTarget(false);
            foreach (var c in model.enemy.bench)
                if (c != null) c.SetSupportTarget(false);
        }

        // ── Linh Thiêng (Hallowed) ────────────────────────────────
        /// <summary>
        /// Linh Thiêng (Hallowed): quân ĐẦU TIÊN tấn công mỗi round nhận +N|+0
        /// (N = số quân Hallowed của bên đó đã chết trong ván — tích lũy cả game).
        /// Chỉ cấp 1 lần mỗi round cho mỗi bên. Gọi ở B5 khi attacker xác nhận tấn công.
        /// </summary>
        void GrantHallowedToFirstAttacker(bool byPlayer, List<CardModel> attackingCards)
        {
            if (attackingCards == null || attackingCards.Count == 0) return;
            int stacks = byPlayer ? _hallowedStacksP : _hallowedStacksE;
            if (stacks <= 0) return;
            bool granted = byPlayer ? _hallowedGrantedThisRoundP : _hallowedGrantedThisRoundE;
            if (granted) return;

            var first = attackingCards[0];
            first.BuffAttack(stacks); // vĩnh viễn
            if (byPlayer) _hallowedGrantedThisRoundP = true;
            else _hallowedGrantedThisRoundE = true;
            Debug.Log($"[Linh Thiêng] {first.data.cardName} là quân đầu tấn công round này → +{stacks}|+0 (stack Hallowed).");
        }
    }
}