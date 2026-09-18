using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using LoRClone;
using LoRClone.Data;
using LoRClone.Model;
using LoRClone.View;

namespace LoRClone.Controller
{
    public partial class GameController
    {
        // ── Bench → Battlefield (attacker) ───────────────────────
        void HandleDeclareAttacker(DeclareAttackerAction a)
        {
            if (a.byPlayer != model.playerHasAttackToken)
            { Reject("Bạn không có attack token."); return; }
            if (model.attackTokenUsed)
            { Reject("Attack token đã được dùng rồi."); return; }

            bool canDeclare = model.phase == GamePhase.PlayerPriority
                           || model.phase == GamePhase.EnemyPriority
                           || model.phase == GamePhase.AttackDeclare;
            if (!canDeclare)
            { Reject($"Không thể declare attacker trong phase {model.phase}."); return; }

            // FIX (spell speed — TÁCH FAST vs SLOW):
            //   • SLOW đang staging → KHÔNG cho tấn công. Slow là lá "mở đầu giao tranh", phải resolve
            //     xong trong main phase rồi mới đánh; nếu để nó kéo sang combat sẽ commit sai thời điểm.
            //   • FAST đang staging → VẪN CHO tấn công. Fast được phép đi kèm giao tranh (commit ở
            //     combat window là hợp lệ). Đây là điểm khác biệt then chốt giữa Fast và Slow.
            foreach (var st in P(a.byPlayer).stagedSpells)
                if (st.data != null && st.data.spellSpeed == SpellSpeed.Slow)
                { Reject("Đang có Slow spell staging — Slow phải resolve trước khi tấn công (Fast thì được phép đi kèm)."); return; }

            var p = P(a.byPlayer);
            if (a.card.location != CardLocation.OnBench)
            { Reject($"{a.card.data.cardName} không ở bench."); return; }
            if (!p.BattlefieldHasSpace)
            { Reject("Battlefield đầy."); return; }

            if (!p.MoveToBattlefield(a.card))
            { Reject("Không thể di chuyển."); return; }

            a.card.state = CardState.Attacking;
            if (model.phase != GamePhase.AttackDeclare)
                model.SetPhase(GamePhase.AttackDeclare);
            model.combatPending = true;

            // Attack Lean: unit lean về phía địch để báo hiệu tấn công
            if (combatAnimator != null)
            {
                var atkCV = combatAnimator.GetView(a.card, a.byPlayer)
                         ?? combatAnimator.GetBenchView(a.card, a.byPlayer);
                if (atkCV != null) StartCoroutine(combatAnimator.AnimateAttackLean(atkCV, a.byPlayer));
            }

            // Tiềm Phục (Lurk): kiểm tra ngay khi có unit tấn công — unit Lurk ở đầu deck → +1|+0
            CheckLurk(a.byPlayer);

            // Cộng Lực (Support): cập nhật UI indicator cho neighbor bên phải
            RefreshSupportIndicators(a.byPlayer);

            Debug.Log($"[GC] {a.card.data.cardName} declared as attacker.");
        }

        // ── Bench → Battlefield (blocker) ────────────────────────
        void HandleDeclareBlocker(DeclareBlockerAction a)
        {
            if (a.byPlayer == model.playerHasAttackToken)
            { Reject("Bên tấn công không thể block."); return; }

            bool canBlock = model.phase == GamePhase.AttackDeclare
                         || model.phase == GamePhase.BlockDeclare
                         || (model.phase == GamePhase.SpellStackResolve && model.combatPending);
            if (!canBlock)
            { Reject($"Không thể declare blocker trong phase {model.phase}."); return; }

            if (a.target.state != CardState.Attacking)
            { Reject($"{a.target.data.cardName} không đang tấn công."); return; }
            if (a.target.blockedBy != null)
            { Reject($"{a.target.data.cardName} đã bị block rồi."); return; }

            // ── Keyword checks ────────────────────────────────────
            if (a.blocker.HasKeyword(KeywordType.CantBlock))
            { Reject($"{a.blocker.data.cardName} có CantBlock — không thể block."); return; }

            if (a.target.HasKeyword(KeywordType.Elusive) && !a.blocker.HasKeyword(KeywordType.Elusive))
            { Reject($"{a.target.data.cardName} là Elusive — chỉ bị block bởi unit Elusive."); return; }

            // Hư Vô (Fearsome): chỉ bị block bởi unit ATK ≥ 3 (chuẩn LoR — dùng effectiveAttack)
            if (a.target.HasKeyword(KeywordType.Fearsome) && a.blocker.effectiveAttack < 3)
            { Reject($"{a.target.data.cardName} là Hư Vô — chỉ bị block bởi unit có ATK ≥ 3."); return; }

            // Chỉ Định (Challenger): nếu attacker này đã chọn forced blocker, chỉ blocker đó mới được block
            if (_challengerTargets.TryGetValue(a.target.id, out var forcedBlocker) && forcedBlocker != a.blocker)
            { Reject($"{a.target.data.cardName} có Chỉ Định — buộc {forcedBlocker.data.cardName} phải block."); return; }
            // ─────────────────────────────────────────────────────

            var p = P(a.byPlayer);

            // Cho phép blocker đến từ bench HOẶC đã ở battlefield với state Idle
            // (VD: unit được summon thẳng lên battlefield bằng skill trong lúc phòng thủ)
            bool blockerOnBench = a.blocker.location == CardLocation.OnBench;
            bool blockerIdleOnField = a.blocker.location == CardLocation.OnBattlefield
                                   && a.blocker.state == CardState.Idle;
            if (!blockerOnBench && !blockerIdleOnField)
            { Reject($"{a.blocker.data.cardName} không ở bench hoặc chưa declare."); return; }

            // Dùng MoveBlockerToBattlefield thay vì MoveToBattlefield:
            // blocker PHẢI đứng cùng cột với attacker nó chặn (a.target.slotIndex)
            // để animation không đánh chéo slot.
            if (blockerOnBench && !p.MoveBlockerToBattlefield(a.blocker, a.target.slotIndex))
            { Reject("Không thể di chuyển blocker."); return; }

            // Nếu blocker đã ở battlefield (idle), reposition về slot đối diện attacker
            if (blockerIdleOnField)
                p.RepositionToBlockerSlot(a.blocker, a.target.slotIndex);

            a.blocker.state = CardState.Blocking;
            a.target.blockedBy = a.blocker;
            a.blocker.blockingTarget = a.target;

            // Phơi Bày (Vulnerable): chuẩn LoR chỉ cho phép bị Chỉ Định (Challenger) buộc block — KHÔNG debuff ATK.

            if (model.phase != GamePhase.BlockDeclare && model.phase != GamePhase.SpellStackResolve)
                model.SetPhase(GamePhase.BlockDeclare);

            Debug.Log($"[GC] {a.blocker.data.cardName} blocks {a.target.data.cardName}.");
        }

        // ── Battlefield → Bench (hủy declare) ────────────────────
        void HandleUndeclare(UndeclareAction a)
        {
            var p = P(a.byPlayer);
            var card = a.card;

            if (card.location != CardLocation.OnBattlefield)
            { Reject($"{card.data.cardName} không ở battlefield."); return; }
            if (card.state != CardState.Attacking && card.state != CardState.Blocking)
            { Reject($"{card.data.cardName} không thể hủy declare."); return; }

            bool wasAttacker = card.state == CardState.Attacking;

            // Chỉ Định: hủy declare challenger đang kéo quân → trả quân đó về bench
            if (card.state == CardState.Attacking && _challengerTargets.ContainsKey(card.id))
            {
                _challengerTargets.Remove(card.id);
                ReleaseForcedBlocker(card, P(!card.belongsToPlayer));
            }

            if (card.state == CardState.Blocking && card.blockingTarget != null)
                card.blockingTarget.blockedBy = null;
            if (card.state == CardState.Attacking && card.blockedBy != null)
                card.blockedBy.blockingTarget = null;

            p.ReturnToBench(card);
            Debug.Log($"[GC] {Tag(a.byPlayer)} hủy declare {card.data.cardName}.");

            if (wasAttacker)
            {
                // Cập nhật Support indicator sau khi undeclare
                RefreshSupportIndicators(a.byPlayer);
                // Compact ngay: attacker vừa rời slot → dồn các attacker còn lại về trái.
                // Tránh khoảng trống giữa các attacker khiến blocker bên kia đánh chéo.
                p.CompactAttackerSlots();

                bool stillHasAttacker = false;
                foreach (var c in model.player.BattlefieldCards())
                    if (c.state == CardState.Attacking) stillHasAttacker = true;
                foreach (var c in model.enemy.BattlefieldCards())
                    if (c.state == CardState.Attacking) stillHasAttacker = true;

                if (!stillHasAttacker)
                {
                    model.combatPending = false;
                    // Xóa combat spell window restriction nếu đang được bật
                    // (tránh main phase bị kẹt ở chế độ "chỉ spell" sau khi hủy tấn công)
                    model.SetRestrictToSpellsOnly(false);
                    model.SetPhase(model.playerHasAttackToken ? GamePhase.PlayerPriority : GamePhase.EnemyPriority);
                }
            }
        }

        // ── Sequential combat resolution ──────────────────────────
        IEnumerator ResolveCombatSequentially()
        {
            if (!model.combatPending) yield break;

            _isResolvingCombat = true;
            RaiseStateChanged(); // disable nút HUD ngay, không đợi Notify() tiếp theo
            yield return StartCoroutine(ResolveCombatSequentiallyInner());
            _isResolvingCombat = false;
            RaiseStateChanged(); // bật lại nút HUD ngay khi combat resolve xong
        }

        IEnumerator ResolveCombatSequentiallyInner()
        {            // Xóa combat spell window restriction trước khi combat bắt đầu.
            // Cần thiết khi đến từ OnBothPassed(BlockDeclare) — restriction vẫn đang bật cho attacker.
            // Không gây hại khi đến từ ResolveSpellsSequentially — ở đó restriction đã được xóa trước.
            model.SetRestrictToSpellsOnly(false);

            var attackerPlayer = model.GetActiveAttacker();
            var defenderPlayer = model.GetActiveDefender();
            bool atkIsPlayer = model.playerHasAttackToken;

            var attackers = new List<CardModel>();
            foreach (var c in attackerPlayer.BattlefieldCards())
                if (c.state == CardState.Attacking) attackers.Add(c);
            attackers.Sort((a, b) => a.slotIndex.CompareTo(b.slotIndex));

            // Chỉ Định (Challenger): kéo quân địch bị thách đấu vào thế chặn TRƯỚC khi resolve.
            ForceChallengerBlocks(attackers, defenderPlayer);

            foreach (var attacker in attackers)
            {
                if (!attacker.IsAlive || attacker.location != CardLocation.OnBattlefield)
                    continue;

                var blocker = attacker.blockedBy;
                bool wasBlocked = blocker != null;
                bool atkQuick = attacker.HasKeyword(KeywordType.QuickAttack);

                // Clash sound — phát TRƯỚC khi dash (cảm giác bắt đầu combat)
                AudioManager.Instance?.PlayCombatClash();

                if (wasBlocked && blocker.IsAlive && blocker.location == CardLocation.OnBattlefield)
                {
                    // ── Pre-combat animation (non-QuickAttack): cả 2 dash vào nhau ──────
                    // QuickAttack tự xử lý animation bên trong ResolveCombatPair
                    if (!atkQuick && combatAnimator != null)
                    {
                        var atkV = combatAnimator.GetView(attacker, atkIsPlayer);
                        var blkV = combatAnimator.GetView(blocker, !atkIsPlayer);
                        if (atkV != null && blkV != null)
                            yield return StartCoroutine(combatAnimator.AnimateDashBoth(atkV, blkV));
                        else if (combatPreDelay > 0f)
                            yield return new WaitForSeconds(combatPreDelay);
                    }
                    else if (!atkQuick && combatPreDelay > 0f)
                        yield return new WaitForSeconds(combatPreDelay);
                    // QuickAttack: không pre-delay ở đây, coroutine tự animate từng bước

                    // Section 5 Bible: hit-stop tại điểm va chạm trước khi damage apply
                    if (hitStopDuration > 0f) yield return new WaitForSeconds(hitStopDuration);

                    // ResolveCombatPair — damage, sounds, keyword effects, và return animation bên trong
                    yield return StartCoroutine(ResolveCombatPair(attacker, blocker, atkIsPlayer, attackerPlayer, defenderPlayer));

                    // ── Death animation: lá chết fade out trước ProcessDeaths ──────────
                    if (combatAnimator != null)
                    {
                        bool anyDying = false;
                        var atkDieView = !attacker.IsAlive ? combatAnimator.GetView(attacker, atkIsPlayer) : null;
                        var blkDieView = !blocker.IsAlive ? combatAnimator.GetView(blocker, !atkIsPlayer) : null;
                        if (atkDieView != null)
                        {
                            StartCoroutine(combatAnimator.AnimateDie(atkDieView));
                            anyDying = true;
                        }
                        if (blkDieView != null)
                        {
                            StartCoroutine(combatAnimator.AnimateDie(blkDieView));
                            anyDying = true;
                        }
                        if (anyDying) yield return new WaitForSeconds(combatAnimator.dieFadeDuration);
                    }

                    // preDeathDelay: chờ keyword sounds (Overwhelm, Lifesteal) xong trước khi unit biến mất
                    if (preDeathDelay > 0f) yield return new WaitForSeconds(preDeathDelay);
                    ProcessDeaths();
                    if (_levelUpCinematicPlaying) yield return new WaitUntil(() => !_levelUpCinematicPlaying);

                    // Cảm Tử (Ephemeral): chết ngay sau khi ra đòn
                    bool ephDied = false;
                    if (attacker.IsAlive && attacker.HasKeyword(KeywordType.Ephemeral)) { attacker.ForceKill(); ephDied = true; }
                    if (blocker.IsAlive && blocker.HasKeyword(KeywordType.Ephemeral)) { blocker.ForceKill(); ephDied = true; }
                    if (ephDied)
                    {
                        if (combatAnimator != null)
                        {
                            bool anyEph = false;
                            var aEph = attacker.HasKeyword(KeywordType.Ephemeral) ? combatAnimator.GetView(attacker, atkIsPlayer) : null;
                            var bEph = blocker.HasKeyword(KeywordType.Ephemeral) ? combatAnimator.GetView(blocker, !atkIsPlayer) : null;
                            if (aEph != null) { StartCoroutine(combatAnimator.AnimateDie(aEph)); anyEph = true; }
                            if (bEph != null) { StartCoroutine(combatAnimator.AnimateDie(bEph)); anyEph = true; }
                            if (anyEph) yield return new WaitForSeconds(combatAnimator.dieFadeDuration);
                        }
                        ProcessDeaths();
                        if (_levelUpCinematicPlaying) yield return new WaitUntil(() => !_levelUpCinematicPlaying);
                    }

                    // Không ReturnToBench ở đây — lá sống ở lại battleslot cho đến hết toàn bộ combat
                    // ReturnSurvivors() cuối hàm sẽ gửi tất cả survivors về bench cùng lúc
                }
                else if (wasBlocked && !attacker.HasKeyword(KeywordType.Overwhelm))
                {
                    // Luật "once blocked" (LoR gốc): unit THƯỜNG bị block thì dù blocker chết
                    // trước combat (VD bị lá phép giết), đòn tấn công vẫn bị vô hiệu — KHÔNG
                    // redirect sang nexus.
                    Debug.Log($"[Combat] {attacker} bị block nhưng blocker đã chết trước — không đánh nexus (once blocked).");
                    ProcessDeaths();
                    if (_levelUpCinematicPlaying) yield return new WaitUntil(() => !_levelUpCinematicPlaying);
                    // Không ReturnToBench ở đây — xử lý cuối combat
                }
                else
                {
                    // ── Tấn công nexus (không có blocker) ────────────────────────────────
                    CardView atkViewNx = combatAnimator?.GetView(attacker, atkIsPlayer);

                    // ── Beat 0: Attacker dash về phía nexus địch ─────
                    if (combatAnimator != null && atkViewNx != null)
                    {
                        yield return StartCoroutine(combatAnimator.AnimateDashToNexus(atkViewNx, atkIsPlayer, attacker));
                    }
                    else if (combatPreDelay > 0f)
                        yield return new WaitForSeconds(combatPreDelay);

                    // Section 5 Bible: hit-stop tại nexus trước khi damage apply
                    if (hitStopDuration > 0f) yield return new WaitForSeconds(hitStopDuration);

                    // ── Beat 1: Nexus nhận damage ─────────────────────
                    // Chiến Lợi (Plunder): capture trạng thái nexus TRƯỚC bất kỳ damage nào của lần tấn công này
                    // (kể cả Impact) — Plunder chỉ fire nếu nexus đã hurt bởi AI KHÁC trước đó.
                    bool nexusAlreadyHurt = atkIsPlayer
                        ? _nexusDamagedByPlayerThisRound
                        : _nexusDamagedByEnemyThisRound;

                    // Xung Kích (Impact): gây 1 dmg nexus địch TRƯỚC combat chính (khi không bị block)
                    if (attacker.HasKeyword(KeywordType.Impact))
                    {
                        defenderPlayer.TakeDamage(1);
                        if (atkIsPlayer) _nexusDamagedByPlayerThisRound = true;
                        else _nexusDamagedByEnemyThisRound = true;
                        Debug.Log($"[Xung Kích] {attacker.data.cardName} → 1 dmg nexus trước combat.");
                        Notify();
                    }

                    // Cộng Lực (Support): chuẩn LoR — buff neighbor đã xử lý qua supportAttackBuff/supportHealthBuff.
                    // KHÔNG cộng ATK của neighbor vào damage nexus.
                    int dmg = attacker.effectiveAttack;

                    defenderPlayer.TakeDamage(dmg);
                    attacker.AddDamageDealt(dmg);
                    CheckDamageDealtOutcome(attackerPlayer, attacker);
                    // Cập nhật flag nexus sau khi damage apply
                    if (atkIsPlayer) _nexusDamagedByPlayerThisRound = true;
                    else _nexusDamagedByEnemyThisRound = true;
                    // Chiến Lợi: nếu nexus đã bị hurt TRƯỚC lần này → hoàn 1 mana + +1|+0 tạm
                    if (attacker.HasKeyword(KeywordType.Plunder) && nexusAlreadyHurt && !attacker.plunderFiredThisAttack)
                    {
                        attacker.MarkPlunderFired();
                        attackerPlayer.RefundMana(1);
                        attacker.BuffAttack(1, temporary: true);
                        Debug.Log($"[Chiến Lợi] {attacker.data.cardName} tấn công nexus đã hurt → +1 mana, +1|+0 tạm.");
                    }
                    Notify();

                    // Nexus pulse (fire & forget) + attacker trở về slot
                    if (combatAnimator != null)
                    {
                        StartCoroutine(combatAnimator.AnimateNexusPulse(!atkIsPlayer));
                        if (atkViewNx != null)
                            yield return StartCoroutine(combatAnimator.AnimateReturnToSlot(atkViewNx));
                    }

                    // Delay: HP nexus update kịp hiện trước khi sound keyword phát
                    if (keywordEffectDelay > 0f) yield return new WaitForSeconds(keywordEffectDelay);

                    // ── Beat 2: Lifesteal + Triggers ───────────────────
                    ApplyLifesteal(attacker, dmg, attackerPlayer);
                    // Vui Vẻ (CantBlock): chuẩn LoR chỉ là "không thể chặn" — KHÔNG có bonus nexus damage.

                    // OnStrike + OnNexusStrike
                    if (dmg > 0) FireTrigger(attacker, SkillTrigger.OnStrike, atkIsPlayer);
                    if (dmg > 0) FireTrigger(attacker, SkillTrigger.OnNexusStrike, atkIsPlayer);
                    if (_levelUpCinematicPlaying) yield return new WaitUntil(() => !_levelUpCinematicPlaying);

                    Debug.Log($"[Combat] {attacker} → nexus -{dmg}");

                    // ── Beat 3: Deaths ────────────────────────────────
                    if (combatAnimator != null && !attacker.IsAlive && atkViewNx != null)
                    {
                        StartCoroutine(combatAnimator.AnimateDie(atkViewNx));
                        yield return new WaitForSeconds(combatAnimator.dieFadeDuration);
                    }
                    else if (preDeathDelay > 0f)
                        yield return new WaitForSeconds(preDeathDelay);

                    ProcessDeaths();
                    if (_levelUpCinematicPlaying) yield return new WaitUntil(() => !_levelUpCinematicPlaying);

                    // Cảm Tử (Ephemeral): chết ngay sau khi ra đòn vào nexus
                    if (attacker.IsAlive && attacker.HasKeyword(KeywordType.Ephemeral))
                    {
                        attacker.ForceKill();
                        if (combatAnimator != null)
                        {
                            var ephV = combatAnimator.GetView(attacker, atkIsPlayer);
                            if (ephV != null)
                            {
                                StartCoroutine(combatAnimator.AnimateDie(ephV));
                                yield return new WaitForSeconds(combatAnimator.dieFadeDuration);
                            }
                        }
                        ProcessDeaths();
                        if (_levelUpCinematicPlaying) yield return new WaitUntil(() => !_levelUpCinematicPlaying);
                    }

                    // Không ReturnToBench ở đây — ReturnSurvivors() cuối hàm xử lý
                }

                Notify();

                if (!model.player.IsAlive || !model.enemy.IsAlive)
                    break;

                if (combatPostDelay > 0f)
                    yield return new WaitForSeconds(combatPostDelay);
                else
                    yield return null;
            }

            ReturnSurvivors(attackerPlayer);
            ReturnSurvivors(defenderPlayer);

            model.combatPending = false;
            model.attackTokenUsed = true;
            ClearAllSupportIndicators(); // Xoá highlight Support sau khi combat kết thúc

            // Tiên Phong (Scout): hoàn attack token sau combat đầu tiên round này
            if (_scoutGrantedExtraAttack)
            {
                _scoutGrantedExtraAttack = false;
                model.attackTokenUsed = false;
                Debug.Log("[Tiên Phong] Attack token được hoàn — có thể tấn công thêm lần nữa.");
            }

            OnCombatResolved?.Invoke();
            model.CheckGameOver();

            if (model.phase != GamePhase.GameOver)
            {
                // Priority chuyển sang DEFENDER sau combat — họ có lượt bình thường (play/cast/pass).
                // Attack token KHÔNG thay đổi — defender không thể phản công trong round này.
                // (Token chỉ đổi ở cuối round qua EndRound, khi cả 2 pass mà không làm gì.)
                bool defenderIsPlayer = !model.playerHasAttackToken;
                model.SetPhase(defenderIsPlayer ? GamePhase.PlayerPriority : GamePhase.EnemyPriority);
                model.GivePriorityTo(defenderIsPlayer);
            }

            Notify();
        }

        /// <summary>
        /// Xử lý 1 cặp attacker vs blocker — nay là IEnumerator để có thể yield giữa các beat.
        ///
        /// Beat structure:
        ///   [1] Damage → Barrier sound, HP bars drop, OnStrike fire → Notify()
        ///   ⏱ keywordEffectDelay — Barrier/OnStrike sound thở
        ///   [2] Lifesteal (heal sound) → ApplyKillEffects (Overwhelm sound, OnKill)
        ///   ⏱ quickAttackSplitDelay (chỉ Săn Bắn, giữa đòn atk và đòn blk retaliate)
        ///   [3] (Săn Bắn) Blocker phản đòn → OnStrike fire → Notify()
        ///   ⏱ keywordEffectDelay
        ///   [4] (Săn Bắn) Lifesteal blocker → ApplyKillEffects
        ///
        /// ProcessDeaths và preDeathDelay được xử lý bởi ResolveCombatSequentially SAU khi coroutine này kết thúc.
        /// </summary>
        IEnumerator ResolveCombatPair(CardModel attacker, CardModel blocker,
                               bool atkIsPlayer, PlayerModel attackerPlayer, PlayerModel defenderPlayer)
        {
            // ── Views cho animation (null nếu combatAnimator chưa gán) ──────────
            CardView atkView = combatAnimator?.GetView(attacker, atkIsPlayer);
            CardView blkView = combatAnimator?.GetView(blocker, !atkIsPlayer);
            // Dùng parent transform thay vì GetSlotWorldPos(card.slotIndex) để tránh đánh chéo.
            // Lý do: blocker.slotIndex = model slot (FindEmpty = 0,1,2...) nhưng view slot của blocker
            // được set bởi PrepareBlockerSlot (= view slot của attacker). Hai slot này KHÁC nhau.
            // GetSlotWorldPos dùng model slot → sai vị trí → QA dash chéo.
            // transform.parent.position = vị trí thực của slot container trong world → luôn đúng.
            Vector3 atkSlotPos = atkView?.transform.parent?.position ?? Vector3.zero;
            Vector3 blkSlotPos = blkView?.transform.parent?.position ?? Vector3.zero;

            // Hư Vô (Fearsome): chuẩn LoR chỉ hạn chế ai được chặn (ATK ≥ 3) — KHÔNG debuff ATK blocker.

            bool atkQuick = attacker.HasKeyword(KeywordType.QuickAttack);
            // Săn Bắn chỉ kích hoạt khi tấn công — blocker đánh đồng thời bình thường

            // Capture ATK trước khi deal damage
            int capturedBlkAtk = blocker.currentAttack;
            int capturedAtkAtk = attacker.currentAttack;

            // Dùng effectiveAttack cho tính toán damage
            int atkPow = attacker.effectiveAttack;
            int blkPow = blocker.effectiveAttack;

            // Cộng Lực (Support): chuẩn LoR — buff neighbor xử lý qua supportAttackBuff/supportHealthBuff.
            // KHÔNG cộng ATK của neighbor vào damage combat.

            // Track damage để fire ally triggers sau khi resolve xong
            int totalAtkDealt = 0;
            int totalBlkDealt = 0;

            if (atkQuick)
            {
                // ── Anim: Attacker dash về phía blocker slot ──────────
                if (combatAnimator != null && atkView != null)
                    yield return StartCoroutine(combatAnimator.AnimateDashOne(atkView, blkSlotPos, combatAnimator.quickAttackDashFraction));
                else if (combatPreDelay > 0f)
                    yield return new WaitForSeconds(combatPreDelay);

                // Section 5 Bible: hit-stop tại điểm va chạm (atk → blk)
                if (hitStopDuration > 0f) yield return new WaitForSeconds(hitStopDuration);

                // ── Beat 1: Attacker đánh trước ──────────────────────
                // sfxAttack: âm thanh đánh của attacker
                AudioManager.PlayCardEvent(CardAudioData.Ev.Attack, attacker.data?.audioData);
                int blkHpBefore = blocker.currentHealth;
                int atkDealt = DealDamage(blocker, atkPow, fromUnit: true);
                totalAtkDealt = atkDealt;
                attacker.AddDamageDealt(atkDealt);
                // sfxTakeDamage + hit squash + damage number + HP flash + camera shake
                if (atkDealt > 0)
                {
                    combatAnimator?.PlayHitFx(blkView, atkDealt);
                }

                // OnStrike: fire SAU damage, TRƯỚC kill check (LoR timing)
                if (atkDealt > 0) FireTrigger(attacker, SkillTrigger.OnStrike, atkIsPlayer);
                FireImpact(attacker, defenderPlayer, atkIsPlayer); // Xung Kích: ra đòn (bị chặn) → 1 dmg nexus
                Notify(); // HP bar blocker drop ngay

                // ⏱ Delay: Barrier + OnStrike sound thở trước khi Lifesteal/Kill fire
                if (_levelUpCinematicPlaying) yield return new WaitUntil(() => !_levelUpCinematicPlaying);
                if (keywordEffectDelay > 0f) yield return new WaitForSeconds(keywordEffectDelay);

                // ── Beat 2: Lifesteal + Kill effects ─────────────────
                ApplyLifesteal(attacker, atkDealt, attackerPlayer);

                if (!blocker.IsAlive)
                {
                    ApplyKillEffects(attacker, capturedBlkAtk, atkPow, blkHpBefore,
                                     atkIsPlayer, defenderPlayer);
                    if (_levelUpCinematicPlaying) yield return new WaitUntil(() => !_levelUpCinematicPlaying);
                    // Attacker trở về slot
                    if (combatAnimator != null && atkView != null)
                        yield return StartCoroutine(combatAnimator.AnimateReturnToSlot(atkView));
                    Debug.Log($"[Combat][Săn Bắn] {attacker} kills {blocker} (no retaliation)");
                }
                else
                {
                    // Attacker trở về slot trước khi blocker phản đòn
                    if (combatAnimator != null && atkView != null)
                        yield return StartCoroutine(combatAnimator.AnimateReturnToSlot(atkView));

                    // ⏱ QuickAttack split: pause rõ ràng trước khi blocker phản đòn
                    if (quickAttackSplitDelay > 0f) yield return new WaitForSeconds(quickAttackSplitDelay);

                    // ── Anim: Blocker dash về phía attacker slot ──────
                    if (combatAnimator != null && blkView != null)
                        yield return StartCoroutine(combatAnimator.AnimateDashOne(blkView, atkSlotPos, combatAnimator.quickAttackDashFraction));
                    else if (combatPreDelay > 0f)
                        yield return new WaitForSeconds(combatPreDelay);

                    // Section 5 Bible: hit-stop tại điểm va chạm (blk → atk)
                    if (hitStopDuration > 0f) yield return new WaitForSeconds(hitStopDuration);

                    // ── Beat 3: Blocker phản đòn ──────────────────────
                    // sfxAttack: âm thanh đánh của blocker
                    AudioManager.PlayCardEvent(CardAudioData.Ev.Attack, blocker.data?.audioData);
                    int atkHpBefore = attacker.currentHealth;
                    int blkDealt = DealDamage(attacker, blkPow, fromUnit: true);
                    totalBlkDealt = blkDealt;
                    blocker.AddDamageDealt(blkDealt);
                    // sfxTakeDamage + hit squash + damage number + HP flash + camera shake
                    if (blkDealt > 0)
                    {
                        combatAnimator?.PlayHitFx(atkView, blkDealt);
                    }

                    if (blkDealt > 0) FireTrigger(blocker, SkillTrigger.OnStrike, !atkIsPlayer);
                    Notify(); // HP bar attacker drop ngay

                    // ⏱ Delay: Barrier + OnStrike sound thở
                    if (_levelUpCinematicPlaying) yield return new WaitUntil(() => !_levelUpCinematicPlaying);
                    if (keywordEffectDelay > 0f) yield return new WaitForSeconds(keywordEffectDelay);

                    // ── Beat 4: Lifesteal + Kill effects ─────────────
                    ApplyLifesteal(blocker, blkDealt, defenderPlayer);

                    if (!attacker.IsAlive)
                        ApplyKillEffects(blocker, capturedAtkAtk, blkPow, atkHpBefore,
                                         !atkIsPlayer, attackerPlayer);

                    if (_levelUpCinematicPlaying) yield return new WaitUntil(() => !_levelUpCinematicPlaying);
                    // Blocker trở về slot (nếu còn sống)
                    if (blocker.IsAlive && combatAnimator != null && blkView != null)
                        yield return StartCoroutine(combatAnimator.AnimateReturnToSlot(blkView));

                    if (attacker.IsAlive && blkDealt > 0)
                        FireTrigger(attacker, SkillTrigger.WhenDamaged, atkIsPlayer);
                    if (blocker.IsAlive && atkDealt > 0)
                        FireTrigger(blocker, SkillTrigger.WhenDamaged, !atkIsPlayer);

                    Debug.Log($"[Combat][Săn Bắn] {attacker} vs {blocker}");
                }
            }
            else
            {
                // ── Beat 1: Cả hai đánh đồng thời ───────────────────
                // (Dash animation đã chạy trước khi ResolveCombatPair được gọi)
                int blkHpBefore = blocker.currentHealth;
                int atkHpBefore = attacker.currentHealth;

                // sfxAttack: chỉ play attacker (blocker là bên bị tấn công — chỉ sfxTakeDamage)
                // Tránh play 2 sfxAttack cùng lúc nếu dùng cùng clip → nghe như echo/lặp
                AudioManager.PlayCardEvent(CardAudioData.Ev.Attack, attacker.data?.audioData);

                int atkDealt = DealDamage(blocker, atkPow, fromUnit: true);
                int blkDealt = DealDamage(attacker, blkPow, fromUnit: true);
                totalAtkDealt = atkDealt;
                totalBlkDealt = blkDealt;
                attacker.AddDamageDealt(atkDealt);
                blocker.AddDamageDealt(blkDealt);

                // sfxTakeDamage + hit squash + camera shake — cả 2 nhận đòn
                bool anyHit = atkDealt > 0 || blkDealt > 0;
                if (atkDealt > 0)
                {
                    combatAnimator?.PlayHitFx(blkView, atkDealt, shake: false);
                }
                if (blkDealt > 0)
                {
                    combatAnimator?.PlayHitFx(atkView, blkDealt, shake: false);
                }
                if (anyHit) combatAnimator?.PlayShake();

                if (atkDealt > 0) FireTrigger(attacker, SkillTrigger.OnStrike, atkIsPlayer);
                if (blkDealt > 0) FireTrigger(blocker, SkillTrigger.OnStrike, !atkIsPlayer);
                FireImpact(attacker, defenderPlayer, atkIsPlayer); // Xung Kích: ra đòn (bị chặn) → 1 dmg nexus
                Notify(); // HP bar cả 2 drop ngay

                // ⏱ Delay: Barrier + OnStrike sound thở trước khi Lifesteal/Kill fire
                if (_levelUpCinematicPlaying) yield return new WaitUntil(() => !_levelUpCinematicPlaying);
                if (keywordEffectDelay > 0f) yield return new WaitForSeconds(keywordEffectDelay);

                // ── Beat 2: Lifesteal + Kill effects ─────────────────
                ApplyLifesteal(attacker, atkDealt, attackerPlayer);
                ApplyLifesteal(blocker, blkDealt, defenderPlayer);

                if (!blocker.IsAlive)
                    ApplyKillEffects(attacker, capturedBlkAtk, atkPow, blkHpBefore,
                                     atkIsPlayer, defenderPlayer);
                if (!attacker.IsAlive)
                    ApplyKillEffects(blocker, capturedAtkAtk, blkPow, atkHpBefore,
                                     !atkIsPlayer, attackerPlayer);

                if (_levelUpCinematicPlaying) yield return new WaitUntil(() => !_levelUpCinematicPlaying);
                // ── Anim: Survivors trở về battleslot ────────────────
                if (combatAnimator != null)
                {
                    CardView atkRet = attacker.IsAlive ? atkView : null;
                    CardView blkRet = blocker.IsAlive ? blkView : null;
                    if (atkRet != null || blkRet != null)
                        yield return StartCoroutine(combatAnimator.AnimateReturnBoth(atkRet, blkRet));
                }

                // ── Song Kích (DoubleAttack): attacker đánh lần 2 sau khi cả 2 đã ra đòn ──
                if (attacker.HasKeyword(KeywordType.DoubleAttack) && attacker.IsAlive && blocker.IsAlive)
                {
                    if (combatAnimator != null && atkView != null)
                        yield return StartCoroutine(combatAnimator.AnimateDashOne(atkView, blkSlotPos, combatAnimator.quickAttackDashFraction));

                    int blkHpBefore2 = blocker.currentHealth;
                    int atkDealt2 = DealDamage(blocker, atkPow, fromUnit: true);
                    totalAtkDealt += atkDealt2;
                    attacker.AddDamageDealt(atkDealt2);
                    if (atkDealt2 > 0) FireTrigger(attacker, SkillTrigger.OnStrike, atkIsPlayer);
                    FireImpact(attacker, defenderPlayer, atkIsPlayer); // Xung Kích: đòn Song Kích thứ 2 → +1 dmg nexus
                    Notify();
                    if (_levelUpCinematicPlaying) yield return new WaitUntil(() => !_levelUpCinematicPlaying);
                    if (keywordEffectDelay > 0f) yield return new WaitForSeconds(keywordEffectDelay);
                    ApplyLifesteal(attacker, atkDealt2, attackerPlayer);
                    if (!blocker.IsAlive)
                        ApplyKillEffects(attacker, capturedBlkAtk, atkPow, blkHpBefore2, atkIsPlayer, defenderPlayer);
                    if (_levelUpCinematicPlaying) yield return new WaitUntil(() => !_levelUpCinematicPlaying);
                    // Chuẩn LoR: Song Kích chỉ ra đòn 2 lần, KHÔNG có buff +0|+1.
                    if (combatAnimator != null && atkView != null)
                        yield return StartCoroutine(combatAnimator.AnimateReturnToSlot(atkView));
                }

                // Thần Thép (Formidable): chuẩn LoR — ra đòn bằng HP (xử lý ở effectiveAttack),
                // KHÔNG có buff +0|+1 khi sống sót.

                if (attacker.IsAlive && blkDealt > 0)
                    FireTrigger(attacker, SkillTrigger.WhenDamaged, atkIsPlayer);
                if (blocker.IsAlive && atkDealt > 0)
                    FireTrigger(blocker, SkillTrigger.WhenDamaged, !atkIsPlayer);

                Debug.Log($"[Combat] {attacker} vs {blocker}");
            }

            // ── SUMMON-SLAIN: ghi sổ combat kill — killer = ĐỐI PHƯƠNG của quân chết.
            //   blocker chết  → attacker (atkIsPlayer) đã hạ nó.
            //   attacker chết → blocker  (!atkIsPlayer) đã hạ nó.
            // Ghi ở đây (cuối ResolveCombatPair) → phủ mọi nhánh: QuickAttack, đồng thời, Song Kích.
            // Unit chết vẫn còn trên sân (ProcessDeaths chạy SAU) nên originalData còn nguyên.
            if (!blocker.IsAlive) RecordSlain(blocker, atkIsPlayer);
            if (!attacker.IsAlive) RecordSlain(attacker, !atkIsPlayer);

            // ── Ally triggers sau combat ──────────────────────────
            if (totalAtkDealt > 0)
                TriggerAllAllies(SkillTrigger.OnAllyDealsDamage, atkIsPlayer, attacker, totalAtkDealt);
            if (totalBlkDealt > 0)
                TriggerAllAllies(SkillTrigger.OnAllyDealsDamage, !atkIsPlayer, blocker, totalBlkDealt);
            if (attacker.IsAlive && totalBlkDealt > 0)
                TriggerAllAllies(SkillTrigger.OnAllyDamaged, atkIsPlayer, attacker, totalBlkDealt);
            if (blocker.IsAlive && totalAtkDealt > 0)
                TriggerAllAllies(SkillTrigger.OnAllyDamaged, !atkIsPlayer, blocker, totalAtkDealt);

            if (attacker.IsAlive) CheckDamageTakenTriggers(attackerPlayer, attacker);
            if (blocker.IsAlive) CheckDamageTakenTriggers(defenderPlayer, blocker);
            CheckDamageDealtOutcome(attackerPlayer, attacker);
            CheckDamageDealtOutcome(defenderPlayer, blocker);
        }

        // ── Kill Effects Helper ───────────────────────────────────
        /// <summary>
        /// Áp dụng tất cả hiệu ứng khi killer giết target:
        /// OnKill trigger, Overwhelm overflow (tràn nexus), Fury +1|+1.
        /// capturedTargetAtk: ATK của target trước khi chết (giữ cho tương thích chữ ký).
        /// </summary>
        void ApplyKillEffects(CardModel killer, int capturedTargetAtk,
                              int rawPower, int targetHpBefore,
                              bool killerIsPlayer, PlayerModel defenderNexus)
        {
            FireTrigger(killer, SkillTrigger.OnKill, killerIsPlayer);
            AudioManager.PlayCardEvent(CardAudioData.Ev.KillEnemy, killer.data?.audioData);   // thoại giết địch
            ApplyOverwhelm(killer, rawPower, targetHpBefore, defenderNexus);

            // Săn Bắn (QuickAttack): chuẩn LoR chỉ "ra đòn trước, giết thì không bị phản" — KHÔNG buff khi kill.

            // Ác Nghiệp (Fury): +1|+1 vĩnh viễn mỗi khi kill địch
            if (killer.HasKeyword(KeywordType.Fury))
            {
                killer.BuffAttack(1);
                killer.BuffHealth(1);
                Debug.Log($"[Ác Nghiệp] {killer.data.cardName} kill → +1|+1.");
            }

            // Hủy Diệt (Overwhelm): chuẩn LoR chỉ tràn damage dư lên nexus (ApplyOverwhelm) — KHÔNG steal ATK.
        }

        // ── Combat Keyword Helpers ────────────────────────────────

        /// <summary>
        /// Gây damage và trả về HP thực sự bị mất.
        /// fromUnit = true khi nguồn là unit combat (cho Bảo Hộ trigger).
        /// </summary>
        static int DealDamage(CardModel target, int amount, bool fromUnit = true)
        {
            bool hadBarrier = target.HasKeyword(KeywordType.Barrier);
            int before = target.currentHealth;
            target.TakeDamage(amount, fromUnit);
            // Bảo Hộ: phát sound khi barrier vừa bị tiêu thụ
            if (hadBarrier && !target.HasKeyword(KeywordType.Barrier))
                AudioManager.Instance?.PlayKwBarrier();
            return before - target.currentHealth;
        }

        /// <summary>Nếu unit có Trù Phú (Lifesteal hoặc runtime-granted), hồi HP nexus.</summary>
        void ApplyLifesteal(CardModel unit, int dealtAmount, PlayerModel owner)
        {
            bool hasLS = unit.HasKeyword(KeywordType.Lifesteal) || unit.hasLifesteal;
            if (hasLS && dealtAmount > 0)
            {
                owner.HealNexus(dealtAmount);
                AudioManager.Instance?.PlayKwLifestealHeal();
                Debug.Log($"[Trù Phú] {unit.data.cardName} hồi {dealtAmount} HP nexus.");
            }
        }

        /// <summary>
        /// Nếu unit có Hủy Diệt (Overwhelm) và đã giết target, damage dư tràn lên nexus đối thủ.
        /// </summary>
        void ApplyOverwhelm(CardModel unit, int rawPower, int targetHpBefore, PlayerModel defNexus)
        {
            if (!unit.HasKeyword(KeywordType.Overwhelm)) return;
            // Hủy Diệt chỉ tràn nexus khi đang tấn công, không áp dụng khi block
            if (unit.state != CardState.Attacking) return;
            int excess = Mathf.Max(0, rawPower - targetHpBefore);
            if (excess > 0)
            {
                defNexus.TakeDamage(excess);
                unit.AddDamageDealt(excess);
                AudioManager.Instance?.PlayKwOverwhelmHit();
                Debug.Log($"[Hủy Diệt] {unit.data.cardName} tràn {excess} damage lên nexus.");
            }
        }

        /// <summary>
        /// Xung Kích (Impact): khi attacker (đang tấn công) ra đòn — DÙ bị chặn hay không —
        /// gây 1 damage lên nexus đối thủ. Song Kích ra đòn 2 lần → cộng dồn 2 lần.
        /// </summary>
        void FireImpact(CardModel attacker, PlayerModel defenderPlayer, bool atkIsPlayer)
        {
            if (attacker == null || !attacker.IsAlive) return;
            if (!attacker.HasKeyword(KeywordType.Impact)) return;
            if (attacker.state != CardState.Attacking) return;
            defenderPlayer.TakeDamage(1);
            attacker.AddDamageDealt(1);
            if (atkIsPlayer) _nexusDamagedByPlayerThisRound = true;
            else _nexusDamagedByEnemyThisRound = true;
            Debug.Log($"[Xung Kích] {attacker.data.cardName} ra đòn → 1 dmg nexus (Impact).");
            Notify();
        }

        // ── Chỉ Định (Challenger) — kéo quân bị thách đấu vào chặn ──
        /// <summary>
        /// Trước khi giải combat: với mỗi attacker có mục tiêu thách đấu (_challengerTargets),
        /// KÉO quân địch đó ra đứng chặn attacker (forced block, chuẩn LoR).
        /// Nguồn đặt _challengerTargets: HandleChallengerTarget (phép Thách Đấu / UI tương lai).
        /// </summary>
        void ForceChallengerBlocks(List<CardModel> attackers, PlayerModel defenderPlayer)
        {
            foreach (var attacker in attackers)
            {
                if (attacker == null || !attacker.IsAlive) continue;

                if (!_challengerTargets.TryGetValue(attacker.id, out var forced) || forced == null)
                    continue;

                // Nếu CHƯA bị chặn (challenge set qua spell, chưa kéo tay) → kéo forced vào chặn ngay.
                if (attacker.blockedBy == null && forced.IsAlive
                    && forced.belongsToPlayer != attacker.belongsToPlayer)
                {
                    bool onBench = forced.location == CardLocation.OnBench;
                    bool idleOnField = forced.location == CardLocation.OnBattlefield
                                       && forced.state == CardState.Idle;
                    if (onBench)
                    {
                        if (!defenderPlayer.MoveBlockerToBattlefield(forced, attacker.slotIndex)) continue;
                    }
                    else if (idleOnField)
                    {
                        defenderPlayer.RepositionToBlockerSlot(forced, attacker.slotIndex);
                    }
                    else continue;

                    forced.state = CardState.Blocking;
                    attacker.blockedBy = forced;
                    forced.blockingTarget = attacker;
                }

                // Fallback: challenge set qua Slow spell resolve SAU confirm → buff ở đây (guard 1 lần).
                if (attacker.blockedBy != null)
                    ApplyChallengerConfirmBuff(attacker);

                _challengerTargets.Remove(attacker.id); // dùng 1 lần cho combat này
            }
            Notify();
        }

        /// <summary>
        /// Nội tại Thách Đấu: áp +challengeSelfAtkBuff|+challengeSelfHpBuff (tạm thời, trong round)
        /// đúng 1 LẦN cho attacker khi attack đã CONFIRM. Guard bằng _challengerBuffedThisRound
        /// nên hủy/đổi mục tiêu declare không cộng dồn.
        /// </summary>
        void ApplyChallengerConfirmBuff(CardModel attacker)
        {
            if (attacker == null || !attacker.IsAlive) return;
            if (!_challengerTargets.ContainsKey(attacker.id)) return;   // không thách đấu ai → không buff
            if (!_challengerBuffedThisRound.Add(attacker.id)) return;   // đã buff round này → bỏ qua
            if (attacker.data.challengeSelfAtkBuff == 0 && attacker.data.challengeSelfHpBuff == 0) return;

            if (attacker.data.challengeSelfAtkBuff != 0)
                attacker.BuffAttack(attacker.data.challengeSelfAtkBuff, temporary: true);
            if (attacker.data.challengeSelfHpBuff != 0)
                attacker.BuffHealth(attacker.data.challengeSelfHpBuff, temporary: true);
            Debug.Log($"[Chỉ Định] {attacker.data.cardName} thách đấu → +{attacker.data.challengeSelfAtkBuff}|+{attacker.data.challengeSelfHpBuff} (confirm).");
            Notify();
        }

        // ── Obliterate + AoE (finisher Mydei — public cho skill gọi qua ctx.controller) ──
        /// <summary>
        /// Thủ Tiêu (Obliterate): xóa HẲN 1 unit khỏi ván — KHÔNG fire Last Breath (không qua ProcessDeaths),
        /// hủy luôn trang bị đang đeo (không trả về tay). Đánh dấu isObliterated để không hồi sinh.
        /// </summary>
        public void ObliterateUnit(CardModel unit, bool? causeIsPlayer = null)
        {
            if (unit == null) return;
            var owner = P(unit.belongsToPlayer);

            // SUMMON-SLAIN: ghi sổ "quân do mình hạ" TRƯỚC khi xóa (obliterate = hành động chủ động).
            // causeIsPlayer truyền vào; null → lấy theo _deathCause đang scope (nếu có).
            var _cause = causeIsPlayer ?? _deathCause;
            if (_cause.HasValue) RecordSlain(unit, _cause.Value);

            // Hủy trang bị đang đeo (KillCard equipment → vào discard, KHÔNG trả tay)
            var equip = unit.equipmentAttached;
            if (equip != null)
            {
                unit.DetachEquipment();
                P(equip.belongsToPlayer).KillCard(equip);
            }

            unit.MarkObliterated();
            owner.KillCard(unit);   // → discard + OnCardDied (view despawn). KHÔNG ProcessDeaths ⇒ KHÔNG Last Breath.
            Debug.Log($"[Thủ Tiêu] {unit.data.cardName} bị xóa hẳn — không Last Breath, trang bị bị hủy.");
            model.CheckGameOver();
            Notify();
        }

        /// <summary>Gây dmg lên MỌI unit địch (bench + battlefield, KHÔNG Nexus).
        /// count = true → cộng vào bộ đếm damage của source (champion lên cấp). ProcessDeaths + Notify sau.</summary>
        public void DamageAllEnemyUnitsFrom(CardModel source, int dmg, bool count = true)
        {
            if (source == null || dmg <= 0) return;
            var enemy = P(!source.belongsToPlayer);
            var targets = new List<CardModel>();
            foreach (var c in enemy.BenchCards()) targets.Add(c);
            foreach (var c in enemy.BattlefieldCards()) targets.Add(c);
            int total = 0;
            foreach (var c in targets)
            {
                if (!c.IsAlive) continue;
                total += c.TakeDamage(dmg, fromUnit: false);
            }
            if (count && total > 0) RegisterSkillDamage(source, total);
            var _prevCause = _deathCause; _deathCause = source.belongsToPlayer;
            ProcessDeaths();
            _deathCause = _prevCause;
            Notify();
        }

        /// <summary>Gây dmg CHỈ lên quân địch đang ở battleslot (đang tấn công/chặn).</summary>
        public void DamageBattlefieldEnemiesFrom(CardModel source, int dmg, bool count = true)
        {
            if (source == null || dmg <= 0) return;
            var enemy = P(!source.belongsToPlayer);
            int total = 0;
            foreach (var c in enemy.BattlefieldCards())
                if (c.IsAlive) total += c.TakeDamage(dmg, fromUnit: false);
            if (count && total > 0) RegisterSkillDamage(source, total);
            var _prevCause = _deathCause; _deathCause = source.belongsToPlayer;
            ProcessDeaths();
            _deathCause = _prevCause;
            Notify();
        }

        /// <summary>Gây dmg lên mọi unit địch + Nexus địch.</summary>
        public void DamageAllEnemiesInclNexusFrom(CardModel source, int dmg, bool count = true)
        {
            if (source == null || dmg <= 0) return;
            DamageAllEnemyUnitsFrom(source, dmg, count);
            var enemyPlayer = P(!source.belongsToPlayer);
            enemyPlayer.TakeDamage(dmg);
            if (source.belongsToPlayer) _nexusDamagedByPlayerThisRound = true;
            else _nexusDamagedByEnemyThisRound = true;
            if (count) RegisterSkillDamage(source, dmg);
            model.CheckGameOver();
            Notify();
        }

        /// <summary>Tạo 1 lá lên tay của bên sở hữu source.</summary>
        public CardModel CreateCardToHandFor(CardModel source, CardData card)
        {
            if (source == null || card == null) return null;
            var created = P(source.belongsToPlayer).CreateCard(card);
            if (created != null) Notify();
            return created;
        }

        // ── Kill All Units (The Ruination) ────────────────────────
        /// <summary>
        /// Giết TẤT CẢ unit trên sân (bench + battlefield) theo phạm vi.
        /// includeAlly/includeEnemy tính TƯƠNG ĐỐI với source.belongsToPlayer (null source = coi như player).
        /// obliterate = false → giết thường (Last Breath kích hoạt — The Ruination).
        ///            = true  → Thủ Tiêu (xóa hẳn, không Last Breath, hủy trang bị).
        /// Kill được quy kết cho source → nuôi sổ slain (Taarosh). Chết cũng vào sổ fallen (Harrowing).
        /// </summary>
        public void KillAllUnits(CardModel source, bool includeAlly, bool includeEnemy, bool obliterate = false)
        {
            bool srcIsPlayer = source == null || source.belongsToPlayer;

            var victims = new List<CardModel>();
            foreach (var side in new[] { (p: model.player, isP: true), (p: model.enemy, isP: false) })
            {
                bool isAlly = side.isP == srcIsPlayer;
                if (isAlly && !includeAlly) continue;
                if (!isAlly && !includeEnemy) continue;
                foreach (var c in side.p.BenchCards()) if (c.IsAlive) victims.Add(c);
                foreach (var c in side.p.BattlefieldCards()) if (c.IsAlive) victims.Add(c);
            }
            if (victims.Count == 0) { Notify(); return; }

            if (obliterate)
            {
                foreach (var c in victims) ObliterateUnit(c, source?.belongsToPlayer);
            }
            else
            {
                foreach (var c in victims) c.ForceKill();
                var _prev = _deathCause;
                _deathCause = source != null ? source.belongsToPlayer : (bool?)null;
                ProcessDeaths();
                _deathCause = _prev;
            }

            model.CheckGameOver();
            Notify();
            Debug.Log($"[KillAllUnits] Giết {victims.Count} unit " +
                      $"(ally={includeAlly}, enemy={includeEnemy}, obliterate={obliterate}).");
        }

        // ── Kill Target(s) — giết unit được chọn (đọc ctx.targetCards) ──
        /// <summary>
        /// Giết các unit ĐƯỢC CHỌN. SpellShield đã được lọc khỏi targetCards ở FilterSpellShield trước
        /// khi skill chạy → unit có Hòa Hợp tự động miễn nhiễm. Đây là "kill" thật (ForceKill) —
        /// bỏ qua HP/Barrier/Tough, khác với gây damage.
        /// obliterate = false → Last Breath kích hoạt; true → Thủ Tiêu (xóa hẳn).
        /// Kill được quy kết cho source → nuôi sổ slain (Taarosh).
        /// </summary>
        public void KillTargets(CardModel source, List<CardModel> targets, bool obliterate = false)
        {
            if (targets == null || targets.Count == 0) return;

            if (obliterate)
            {
                foreach (var t in targets)
                    if (t != null && t.IsAlive) ObliterateUnit(t, source?.belongsToPlayer);
            }
            else
            {
                int killed = 0;
                foreach (var t in targets)
                    if (t != null && t.IsAlive) { t.ForceKill(); killed++; }

                if (killed > 0)
                {
                    var _prev = _deathCause;
                    _deathCause = source != null ? source.belongsToPlayer : (bool?)null;
                    ProcessDeaths();
                    _deathCause = _prev;
                }
            }

            model.CheckGameOver();
            Notify();
        }

        // ── Chỉ Định (Challenger) — kéo drag: người chơi kéo quân địch xuống chặn ──
        /// <summary>Người chơi (đang có attack token) có được phép kéo unit ĐỊCH này xuống chặn không?</summary>
        public bool CanChallengerPull(CardModel enemyUnit)
        {
            if (enemyUnit == null || !enemyUnit.IsAlive) return false;
            bool puller = model.isPlayerPriority;
            if (enemyUnit.belongsToPlayer == puller) return false;       // phải là quân địch của người kéo
            if (puller != model.playerHasAttackToken) return false;      // người kéo phải đang tấn công
            bool pullable = enemyUnit.location == CardLocation.OnBench
                || (enemyUnit.location == CardLocation.OnBattlefield && enemyUnit.state == CardState.Idle);
            if (!pullable) return false;
            return FindChallengerAttacker() != null;                     // phải có 1 attacker Challenger
        }

        CardModel FindChallengerAttacker()
        {
            var atk = P(model.playerHasAttackToken);
            CardModel any = null;
            foreach (var c in atk.BattlefieldCards())
            {
                if (c.state != CardState.Attacking || !c.HasKeyword(KeywordType.Challenger)) continue;
                if (c.blockedBy == null) return c;   // ưu tiên challenger chưa kéo ai
                any = any ?? c;
            }
            return any;
        }

        /// <summary>Người chơi kéo enemyUnit xuống → nó bị buộc chặn attacker Challenger (đặt ngay).</summary>
        public void HandleChallengerPull(CardModel enemyUnit)
        {
            var challenger = FindChallengerAttacker();
            if (challenger == null || enemyUnit == null) return;
            ForceOneChallengerBlock(challenger, enemyUnit, P(!challenger.belongsToPlayer));
            Notify();
        }

        /// <summary>Đặt forced block. Đổi mục tiêu → trả quân cũ về bench (không lỗi).</summary>
        void ForceOneChallengerBlock(CardModel challenger, CardModel enemyUnit, PlayerModel defender)
        {
            if (challenger == null || enemyUnit == null) return;
            if (enemyUnit.belongsToPlayer == challenger.belongsToPlayer) return;

            if (challenger.blockedBy != null && challenger.blockedBy != enemyUnit)
                ReleaseForcedBlocker(challenger, defender);          // retarget: trả quân cũ về
            if (enemyUnit.blockingTarget != null && enemyUnit.blockingTarget != challenger)
                enemyUnit.blockingTarget.blockedBy = null;

            if (enemyUnit.location == CardLocation.OnBench)
            {
                if (!defender.MoveBlockerToBattlefield(enemyUnit, challenger.slotIndex)) return;
            }
            else if (enemyUnit.location == CardLocation.OnBattlefield)
            {
                defender.RepositionToBlockerSlot(enemyUnit, challenger.slotIndex);
            }

            enemyUnit.state = CardState.Blocking;
            challenger.blockedBy = enemyUnit;
            enemyUnit.blockingTarget = challenger;
            _challengerTargets[challenger.id] = enemyUnit;
            // KHÔNG buff ở đây — nội tại +2|+1 áp tại combat resolve (ForceChallengerBlocks), 1 lần khi attack confirm.
            Debug.Log($"[Chỉ Định] {challenger.data.cardName} kéo {enemyUnit.data.cardName} xuống (chưa confirm).");
        }

        /// <summary>Trả quân địch đang bị kéo về bench (khi đổi mục tiêu / hủy declare challenger).</summary>
        void ReleaseForcedBlocker(CardModel challenger, PlayerModel defender)
        {
            var blocker = challenger.blockedBy;
            if (blocker == null) return;
            challenger.blockedBy = null;
            blocker.blockingTarget = null;
            blocker.state = CardState.Idle;
            defender.ReturnToBench(blocker);
        }

        // ── Death Processing ──────────────────────────────────────
        void ProcessDeaths()
        {
            int pass = 0;
            while (true)
            {
                var dying = new List<(PlayerModel owner, CardModel card)>();
                foreach (var p in new[] { model.player, model.enemy })
                {
                    foreach (var c in p.BenchCards())
                        if (!c.IsAlive) dying.Add((p, c));
                    foreach (var c in p.BattlefieldCards())
                        if (!c.IsAlive) dying.Add((p, c));
                }
                if (dying.Count == 0) break;

                pass++;
                Debug.Log($"[Death] Pass {pass}: {dying.Count} unit chết.");

                // Phase 1: Last Breath — unit vẫn còn trên sân
                foreach (var (owner, card) in dying)
                {
                    var ctx = new GameContext(model, owner, this);
                    ExecuteMatching(card, SkillTrigger.OnDeath, ctx);
                    Debug.Log($"[Death] {card} chết — Last Breath triggered.");

                    // Trù Phú (Lifesteal): chuẩn LoR — chỉ hồi máu nexus khi gây damage, KHÔNG truyền cho đồng minh khi chết.
                }

                // Phase 1.5: OnAllyDeath + Hallowed — notify đồng minh sống của mỗi unit sắp chết
                // Chạy sau Last Breath nhưng trước khi xóa khỏi sân, để allies còn thấy nhau
                foreach (var (owner, card) in dying)
                {
                    bool isPlayer = owner == model.player;
                    TriggerAllAllies(SkillTrigger.OnAllyDeath, isPlayer, card);
                    // "Anywhere" — kiểu Kayle passive: buff dù đang ở deck/tay, chưa lên sân.
                    TriggerAllCards(SkillTrigger.OnAllyDeathAnywhere, isPlayer, card);

                    // Linh Thiêng (Hallowed): chuẩn LoR — khi quân Hallowed chết, đến HẾT VÁN,
                    // quân ĐẦU TIÊN tấn công mỗi round nhận thêm +1|+0 (cộng dồn stack).
                    // Việc cấp buff xử lý ở GrantHallowedToFirstAttacker (gọi khi tấn công).
                    if (card.HasKeyword(KeywordType.Hallowed))
                    {
                        if (isPlayer) _hallowedStacksP++;
                        else _hallowedStacksE++;
                        Debug.Log($"[Linh Thiêng] {card.data.cardName} chết → stack Hallowed = {(isPlayer ? _hallowedStacksP : _hallowedStacksE)}.");
                    }
                }

                // Phase 2: Xóa unit khỏi sân
                foreach (var (owner, card) in dying)
                {
                    // SUMMON-SLAIN: ghi sổ nếu cái chết này có nguồn gây ra (spell/skill/obliterate).
                    // Combat/tự nhiên → _deathCause null → bỏ qua.
                    if (_deathCause.HasValue) RecordSlain(card, _deathCause.Value);
                    owner.KillCard(card);
                }
            }
        }

        /// <summary>
        /// Kill tất cả unit có Cảm Tử (Ephemeral) còn sống trên bench và battlefield.
        /// Gọi cuối mỗi round (trước RoundStart) để đảm bảo Ephemeral không tồn tại sang round mới.
        /// </summary>
        void KillAllEphemerals()
        {
            bool any = false;
            foreach (var p in new[] { model.player, model.enemy })
            {
                foreach (var c in new System.Collections.Generic.List<CardModel>(p.BenchCards()))
                    if (c.IsAlive && c.HasKeyword(KeywordType.Ephemeral)) { c.ForceKill(); any = true; }
                foreach (var c in new System.Collections.Generic.List<CardModel>(p.BattlefieldCards()))
                    if (c.IsAlive && c.HasKeyword(KeywordType.Ephemeral)) { c.ForceKill(); any = true; }
            }
            if (any)
            {
                Debug.Log("[Cảm Tử] Cuối round — xóa tất cả unit Ephemeral còn sống.");
                ProcessDeaths();
            }
        }

        void ReturnSurvivors(PlayerModel p)
        {
            var survivors = new List<CardModel>();
            foreach (var c in p.BattlefieldCards()) if (c.IsAlive) survivors.Add(c);
            foreach (var c in survivors) p.ReturnToBench(c);
        }
    }
}