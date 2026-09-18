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
        // ── Spell ─────────────────────────────────────────────────
        StackEntry MakeSpellStackEntry(PlayerModel owner, CardModel spell)
        {
            return new StackEntry(owner, spell, spell.data.cardName, resolveCtx =>
            {
                // FIX: resolveCtx do SpellStack tạo KHÔNG set controller (null) → mọi skill cần
                // ctx.controller (SummonUnit, SlainOf...) sẽ fail. Dựng ctx chuẩn có controller = this.
                var ctx = new GameContext(model, owner, this);
                ctx.targetCards = resolveCtx.targetCards;   // giữ target SpellStack đã set (nếu có)

                if (_spellTargets.TryGetValue(spell.id, out var storedTargets))
                {
                    ctx.targetCards = storedTargets;
                    _spellTargets.Remove(spell.id);
                    Debug.Log($"[GC] {spell.data.cardName} resolve — {ctx.targetCards.Count} target(s).");
                }
                else
                {
                    Debug.LogWarning($"[GC] {spell.data.cardName} resolve — NO TARGET " +
                                     $"requiresTarget={spell.data.requiresTarget}");
                }

                FilterSpellShield(ctx);

                foreach (var skill in spell.data.skills)
                    if (skill != null) skill.Execute(spell, ctx);
                foreach (var ability in spell.data.abilities)
                    if (ability.effect != null)
                    {
                        ctx.temporaryBuff = ability.temporaryBuff;
                        ctx.grantKeyword = ability.grantKeyword;
                        ctx.grantKeywordType = ability.grantKeywordType;
                        ability.effect.Execute(spell, ctx, ability.p1, ability.p2, ability.p3);
                        ctx.temporaryBuff = false;
                        ctx.grantKeyword = false;
                    }
                owner.KillCard(spell);
                var _prevCause = _deathCause; _deathCause = owner.isPlayer;
                ProcessDeaths();
                _deathCause = _prevCause;
            });
        }

        // ── Burst spell ───────────────────────────────────────────
        void HandleCastSpell(CastSpellAction a)
        {
            var p = P(a.byPlayer);
            if (a.spell.data.cardType != CardType.Spell)
            { Reject("Không phải Spell."); return; }
            if (a.spell.data.spellSpeed != SpellSpeed.Burst)
            { Reject("Không phải Burst — kéo lên Spell Zone để staging."); return; }

            // (TINH HỒN ★1/★3: -1 đã rải sẵn lên lá trong tay → currentManaCost đã giảm.)
            if (!p.CanAffordSpell(a.spell.currentManaCost))
            { Reject($"Thiếu mana. Cần {a.spell.currentManaCost}, còn {p.spellMana + p.mana}."); return; }

            var (spentOk, spellManaUsed) = p.SpendManaForSpell(a.spell.currentManaCost);
            if (!spentOk) { Reject("Lỗi tiêu mana."); return; }
            _spellManaUsed[a.spell.id] = spellManaUsed;
            _manaCostPaid[a.spell.id] = a.spell.currentManaCost;  // ghi lại cost thực tế đã trả

            // SpellManaTrigger check (CantBlock không còn tích charge — chỉ giữ trigger tạo bài)
            UpdateSpellCharges(p, spellManaUsed);

            // Dòng Thức (Flow): mỗi spell được chơi → tất cả unit Flow +1|+0 tạm thời
            ApplyFlowBuff(a.byPlayer);

            // Cộng Hưởng (Augment): nếu spell này là generated → buff tất cả Augment +1|+0
            if (a.spell.data.isGenerated)
                ApplyAugmentBuff(a.byPlayer);

            _cardsPlayedThisRound++; // spell cũng tính vào counter Daybreak/Nightfall

            if (a.byPlayer && !networkMode) CampaignRun.ConstellationNotifyCardPlayed(p);   // TINH HỒN: chốt -1 lá đầu (PvP: off)

            p.CastBurstSpell(a.spell);
            AudioManager.PlayCardEvent(CardAudioData.Ev.SpellCast, a.spell.data?.audioData);   // thoại lúc dùng burst

            var ctx = new GameContext(model, p, this);
            if (_spellTargets.TryGetValue(a.spell.id, out var burstTargets))
            {
                ctx.targetCards = burstTargets;
                _spellTargets.Remove(a.spell.id);
            }

            FilterSpellShield(ctx);

            foreach (var skill in a.spell.data.skills)
                if (skill != null) skill.Execute(a.spell, ctx);
            foreach (var ability in a.spell.data.abilities)
                if (ability.effect != null)
                {
                    ctx.temporaryBuff = ability.temporaryBuff;
                    ctx.grantKeyword = ability.grantKeyword;
                    ctx.grantKeywordType = ability.grantKeywordType;
                    ability.effect.Execute(a.spell, ctx, ability.p1, ability.p2, ability.p3);
                    ctx.temporaryBuff = false;
                    ctx.grantKeyword = false;
                }
            p.KillCard(a.spell);
            var _prevCause = _deathCause; _deathCause = p.isPlayer;
            ProcessDeaths();
            _deathCause = _prevCause;

            Debug.Log($"[GC] Burst {a.spell.data.cardName} resolved immediately.");
            // Bug fix: Notify để view cập nhật tay, mana, effects ngay sau Burst resolve
            Notify();
        }

        // ── Hand → SpellZone (staging Fast/Slow) ──────────────────
        void HandleStageSpell(StageSpellAction a)
        {
            var p = P(a.byPlayer);
            var card = a.spell;

            if (card.data.cardType != CardType.Spell)
            { Reject("Không phải Spell."); return; }
            // Burst có target: cho phép staging để hiện trong SpellZone rồi commit ngay.
            // CommitStagedSpells sẽ resolve không có response window khi toàn bộ là Burst.

            if (card.data.spellSpeed == SpellSpeed.Slow)
            {
                // Slow = lá "mở đầu giao tranh": CHỈ dùng được trong main phase, khi CHƯA có spell
                // trên stack và CHƯA vào combat. KHÔNG response gì (kể cả Slow khác) — đúng luật LoR.
                // (Trước đây cho Slow response một Slow → không chuẩn, đã bỏ.)
                bool inMainPhase = model.phase == GamePhase.PlayerPriority
                                || model.phase == GamePhase.EnemyPriority;
                if (!inMainPhase || !model.spellStack.IsEmpty || model.combatPending)
                { Reject("Slow spell chỉ dùng trong main phase — không được dùng khi đang đấu phép hoặc trong combat."); return; }
            }

            if (AvailableManaForSpell(p) < card.currentManaCost)
            { Reject($"Thiếu mana. Cần {card.currentManaCost}, còn {AvailableManaForSpell(p)}."); return; }

            if (!p.StageSpell(card))
            { Reject("Không thể đặt lên Spell Zone."); return; }

            Debug.Log($"[GC] {Tag(a.byPlayer)} staged {card.data.cardName} ({card.data.spellSpeed}).");

            // Nếu target đã được set TRƯỚC khi stage (AI flow: SetSpellTargets → StageSpellAction),
            // fire OnSpellCommittedWithTargets ngay để GameView hiện locked arrow liền — đồng bộ
            // với player (arrow hiện ngay khi confirm target, không cần đợi Pass/Confirm).
            // An toàn cho cả 2 phía: GameView chỉ set nếu CHƯA có (idempotent), và trong thực tế
            // chỉ AI mới set target TRƯỚC khi stage — player luôn chọn target SAU khi đã stage
            // (xem StartTargetingForSpell/AfterStageNextFrame trong GameView.cs).
            if (_spellTargets.TryGetValue(card.id, out var preStagedTargets) && preStagedTargets?.Count > 0)
                OnSpellCommittedWithTargets?.Invoke(card, preStagedTargets, a.byPlayer);

            Notify();
        }

        // ── SpellZone → Hand (bỏ staging) ─────────────────────────
        void HandleUnstageSpell(UnstageSpellAction a)
        {
            var p = P(a.byPlayer);
            if (a.spell.belongsToPlayer != a.byPlayer)
            { Reject("Không phải spell của bạn."); return; }
            if (!p.UnstageSpell(a.spell))
            { Reject($"{a.spell.data.cardName} không thể bỏ staging."); return; }
            Debug.Log($"[GC] {Tag(a.byPlayer)} unstaged {a.spell.data.cardName}.");
        }

        // ── Spell Stack → Hand (hủy cast) ─────────────────────────
        void HandleUndoCastSpell(UndoCastSpellAction a)
        {
            var p = P(a.byPlayer);
            var card = a.spell;

            if (card.location != CardLocation.OnStack)
            { Reject($"{card.data.cardName} không ở trên stack."); return; }
            if (card.belongsToPlayer != a.byPlayer)
            { Reject("Không phải spell của bạn."); return; }
            if (!model.spellStack.TryRemoveTopIfMatches(card))
            { Reject($"{card.data.cardName} không phải spell trên cùng."); return; }

            if (!p.UndoCastSpell(card))
            {
                model.spellStack.Push(MakeSpellStackEntry(p, card));
                Reject("Hand đầy — không thể hủy cast.");
                return;
            }
            // Đồng bộ sound stack: xóa spell vừa bị undo khỏi top
            if (_spellSoundStack.Count > 0) _spellSoundStack.Pop();

            _spellManaUsed.TryGetValue(card.id, out int usedSpellMana);
            _spellManaUsed.Remove(card.id);
            // Hoàn đúng số mana đã trả lúc cast (không dùng currentManaCost vì có thể đã đổi)
            _manaCostPaid.TryGetValue(card.id, out int paidCost);
            _manaCostPaid.Remove(card.id);
            p.RefundManaForSpell(paidCost, usedSpellMana);
            _spellTargets.Remove(card.id);
            Debug.Log($"[GC] {Tag(a.byPlayer)} hủy cast {card.data.cardName}, hoàn {paidCost} mana ({usedSpellMana} spell mana).");

            if (model.spellStack.IsEmpty)
            {
                model.SetRestrictToSpellsOnly(false);
                model.ReturnToMainPhase();
                if (model.phase == GamePhase.PlayerPriority || model.phase == GamePhase.EnemyPriority)
                    model.GivePriorityTo(model.playerHasAttackToken);
            }
            else
            {
                var top = model.spellStack.PeekTop();
                model.SetSpellRestrictionTarget(!top.owner.isPlayer);
            }
        }

        // ── Pass Priority ─────────────────────────────────────────
        void HandlePassPriority(PassPriorityAction a)
        {
            bool inAttackWindow = model.phase == GamePhase.AttackDeclare
                || (model.phase == GamePhase.SpellStackResolve && model.combatPending);
            bool inBlockWindow = model.phase == GamePhase.BlockDeclare
                || (model.phase == GamePhase.SpellStackResolve && model.combatPending);

            // ── Attacker xác nhận tấn công ────────────────────────
            // Thứ tự đúng theo Gameplay Feel Bible Section 3:
            //   B5: Keyword attack effects (Support, Scout) — tất cả attacker, 1 pass
            //   B6: OnAttack skill triggers (FireTrigger per card) — custom skills fire sau keyword
            //   B7: Summon on Attack — xảy ra inline bên trong B6 (skill Execute gọi SummonUnit)
            //   B8: Level Up check — sau khi tất cả effects đã apply
            //   B9: Chuyển priority sang Defender → BlockDeclare
            // PHẢI chạy TRƯỚC CommitStagedSpells — đảm bảo OnAttack fire đúng timing.
            // Staged spell (nếu có) tự commit khi attacker lấy lại priority sau defender pass.
            if (model.phase == GamePhase.AttackDeclare && a.byPlayer == model.playerHasAttackToken)
            {
                var attackingCards = new List<CardModel>();
                foreach (var card in P(a.byPlayer).BattlefieldCards())
                    if (card.state == CardState.Attacking) attackingCards.Add(card);

                // ── B5: Keyword attack effects (toàn bộ attacker, 1 pass riêng trước skill) ──
                foreach (var card in attackingCards)
                {
                    // Cộng Lực (Support): buff unit bên phải bench.
                    // supportTemporary = true → buff hết round (LoR); false → vĩnh viễn.
                    if (card.HasKeyword(KeywordType.Support))
                    {
                        var supportNeighbor = FindSupportNeighbor(card, a.byPlayer);
                        if (supportNeighbor != null)
                        {
                            if (card.data.supportAttackBuff != 0)
                                supportNeighbor.BuffAttack(card.data.supportAttackBuff, temporary: card.data.supportTemporary);
                            if (card.data.supportHealthBuff != 0)
                                supportNeighbor.BuffHealth(card.data.supportHealthBuff, temporary: card.data.supportTemporary);
                            foreach (var kw in card.data.supportGrantKeywords)
                                supportNeighbor.GrantKeyword(kw);
                            Debug.Log($"[B5-Cộng Lực] {card.data.cardName} → {supportNeighbor.data.cardName}: " +
                                      $"+{card.data.supportAttackBuff}|+{card.data.supportHealthBuff}" +
                                      (card.data.supportGrantKeywords.Count > 0 ? $" + {string.Join(",", card.data.supportGrantKeywords)}" : ""));
                            // Pulse glow trên unit nhận Support buff
                            if (combatAnimator != null)
                            {
                                var supportedView = combatAnimator.GetBenchView(supportNeighbor, a.byPlayer)
                                    ?? combatAnimator.GetView(supportNeighbor, a.byPlayer);
                                if (supportedView != null)
                                    StartCoroutine(combatAnimator.AnimateSupportPulse(supportedView));
                            }
                        }
                    }

                }

                // ── B5b: Tiên Phong (Scout) — chuẩn LoR ──────────────────────────
                // Nếu TẤT CẢ quân đang tấn công đều có Scout (và chưa dùng round này) →
                // "sẵn sàng tấn công": hoàn lại attack token 1 lần trong round. (Không buff đồng minh.)
                if (attackingCards.Count > 0 && !_scoutReadyUsedThisRound
                    && attackingCards.TrueForAll(c => c.HasKeyword(KeywordType.Scout)))
                {
                    _scoutReadyUsedThisRound = true;
                    _scoutGrantedExtraAttack = true;
                    Debug.Log($"[B5-Tiên Phong] Cả {attackingCards.Count} quân tấn công đều Tiên Phong → sẵn sàng tấn công lần nữa.");
                }

                // ── B5c: Linh Thiêng (Hallowed) — chuẩn LoR ──────────────────────
                // Quân ĐẦU TIÊN tấn công mỗi round nhận +N|+0 (N = số Hallowed đã chết cả ván).
                GrantHallowedToFirstAttacker(a.byPlayer, attackingCards);

                // ── B6: OnAttack skill triggers (custom skills, Summon on Attack inline = B7) ──
                foreach (var card in attackingCards)
                {
                    if (!_attackTriggered.Add(card.id)) continue; // đã fire rồi (không duplicate)

                    FireTrigger(card, SkillTrigger.OnAttack, a.byPlayer);
                    ApplyChallengerConfirmBuff(card);      // nội tại Thách Đấu +2|+1 khi attack CONFIRM

                    // Đếm lượt tấn công — dùng cho CardCreationTrigger (counter=AttacksMade).
                    card.AddAttackMade();

                    // Keyword SFX — audio feedback per unit (thuộc AudioManager, fire cùng B6)
                    if (card.HasKeyword(KeywordType.QuickAttack)) AudioManager.Instance?.PlayKwQuickAttack();
                    if (card.HasKeyword(KeywordType.Overwhelm)) AudioManager.Instance?.PlayKwOverwhelmAttack();
                    if (card.HasKeyword(KeywordType.Fearsome)) AudioManager.Instance?.PlayKwFearsome();
                    if (card.HasKeyword(KeywordType.Elusive)) AudioManager.Instance?.PlayKwElusive();
                    if (card.HasKeyword(KeywordType.CantBlock)) AudioManager.Instance?.PlayKwCantBlockAttack();
                }

                // Team aggregate + kiểm tra CardCreationTrigger(counter=AttacksMade) — cả Self lẫn Team.
                if (attackingCards.Count > 0)
                {
                    P(a.byPlayer).AddAttackMadeTeam(attackingCards.Count);
                    CheckAttacksMadeTriggers(P(a.byPlayer));
                }

                // Champion passive (Kayle — OnEmpoweredAllyAttack): sau B6 khi tất cả buffs đã apply
                {
                    int empowered = 0;
                    foreach (var c in P(a.byPlayer).BattlefieldCards())
                        if (c.state == CardState.Attacking && c.currentAttack > c.data.baseAttack)
                            empowered++;
                    if (empowered > 0)
                        TriggerAllCards(SkillTrigger.OnEmpoweredAllyAttack, a.byPlayer,
                                        triggerCard: null, triggerAmount: empowered);
                }

                // "Anywhere" — mọi đồng minh tấn công (không cần cường hóa), buff dù đang ở deck/tay.
                if (attackingCards.Count > 0)
                    TriggerAllCards(SkillTrigger.OnAllyAttackAnywhere, a.byPlayer,
                                    triggerCard: null, triggerAmount: attackingCards.Count);

                // Âm thanh confirm tấn công — 1 lần chung
                AudioManager.Instance?.PlayConfirmAttack();

                // ── B8: Level Up check — sau khi tất cả keyword + skill effects đã apply ──
                EvaluatePassiveLevelUpsAtTrigger(SkillTrigger.OnAttack);

                // ── B8b: Renekton/Lee Sin-style — skill khi TẤN CÔNG (chọn target nếu skill cần) ──
                // Có battle skill → coroutine lo: stage (+ chọn target khi attacker CÒN priority) rồi
                // mới sang BlockDeclare. Skill tự commit ở post-block window → resolve xong vào combat.
                if (AnyPendingBattleSkill(attackingCards, isBlock: false))
                {
                    StartCoroutine(StageBattleSkillsCo(attackingCards, a.byPlayer, isBlock: false));
                    return;
                }

                // ── B9: Chuyển priority sang Defender → BlockDeclare ──
                // Staged spell (nếu có) tự commit khi attacker lấy lại priority sau defender pass.
                model.SetPhase(GamePhase.BlockDeclare);
                model.HandOffPriority(a.byPlayer);
                Debug.Log($"[GC] {Tag(a.byPlayer)} confirmed attackers → BlockDeclare.");
                return;
            }

            // Commit staged spells (BlockDeclare, SpellStackResolve, main phase)
            if (CommitStagedSpells(a.byPlayer))
            {
                Debug.Log($"[GC] {Tag(a.byPlayer)} committed staged spells.");
                return;
            }

            // ── Block C: OnAttack cho attacker MỚI declare trong SpellStackResolve ──
            // (chỉ xảy ra khi Slow spell trên stack cho phép declare attacker thêm)
            // Dùng cùng thứ tự B5→B6 như Block B để nhất quán.
            if (inAttackWindow && a.byPlayer == model.playerHasAttackToken
                && model.phase == GamePhase.SpellStackResolve)
            {
                var newAttackers = new List<CardModel>();
                foreach (var card in P(a.byPlayer).BattlefieldCards())
                    if (card.state == CardState.Attacking && _attackTriggered.Contains(card.id) == false)
                        newAttackers.Add(card);

                // B5: keyword effects
                foreach (var card in newAttackers)
                {
                    if (card.HasKeyword(KeywordType.Support))
                    {
                        var n = FindSupportNeighbor(card, a.byPlayer);
                        if (n != null)
                        {
                            if (card.data.supportAttackBuff != 0) n.BuffAttack(card.data.supportAttackBuff, temporary: card.data.supportTemporary);
                            if (card.data.supportHealthBuff != 0) n.BuffHealth(card.data.supportHealthBuff, temporary: card.data.supportTemporary);
                            foreach (var kw in card.data.supportGrantKeywords) n.GrantKeyword(kw);
                            Debug.Log($"[B5-Cộng Lực/C] {card.data.cardName} → {n.data.cardName}");
                        }
                    }
                }

                // B6: skill triggers
                foreach (var card in newAttackers)
                {
                    if (!_attackTriggered.Add(card.id)) continue;
                    FireTrigger(card, SkillTrigger.OnAttack, a.byPlayer);
                    if (card.HasKeyword(KeywordType.QuickAttack)) AudioManager.Instance?.PlayKwQuickAttack();
                    if (card.HasKeyword(KeywordType.Overwhelm)) AudioManager.Instance?.PlayKwOverwhelmAttack();
                    if (card.HasKeyword(KeywordType.Fearsome)) AudioManager.Instance?.PlayKwFearsome();
                    if (card.HasKeyword(KeywordType.Elusive)) AudioManager.Instance?.PlayKwElusive();
                    if (card.HasKeyword(KeywordType.CantBlock)) AudioManager.Instance?.PlayKwCantBlockAttack();
                }

                // B8: level up check
                EvaluatePassiveLevelUpsAtTrigger(SkillTrigger.OnAttack);
            }

            // ── Defender pass trong BlockDeclare → priority về ATTACKER (không combat ngay) ──
            // LoR gốc: defender pass → attacker vẫn có 1 window nữa để cast spell.
            // Combat chỉ resolve khi CẢ 2 pass liên tiếp → OnBothPassed(BlockDeclare) → OnReadyToResolveCombat.
            // TRÁNH dùng HandOffPriority ở đây — nó reset cờ pass → vòng lặp pass vô hạn.
            // PassPriority tích lũy cờ; khi cả 2 pass → OnBothPassed fires combat.
            if (inBlockWindow && a.byPlayer != model.playerHasAttackToken)
            {
                var newBlockers = new List<CardModel>();
                foreach (var card in P(a.byPlayer).BattlefieldCards())
                    if (card.state == CardState.Blocking && _blockTriggered.Add(card.id))
                    {
                        FireTrigger(card, SkillTrigger.OnBlock, a.byPlayer);
                        newBlockers.Add(card);
                    }

                // ── Renekton/Lee Sin-style: khi CHẶN → skill lên stack (chọn target nếu cần) ──
                // Coroutine commit ngay trong lượt defender (mở response cho attacker); hủy target →
                // hoàn tất pass phòng thủ như thường (FinishDefenderBlockPass).
                if (AnyPendingBattleSkill(newBlockers, isBlock: true))
                {
                    StartCoroutine(StageBattleSkillsCo(newBlockers, a.byPlayer, isBlock: true));
                    return;
                }

                // Khi defender pass trong BlockDeclare → bật restriction cho ATTACKER:
                // attacker chỉ được dùng Fast/Burst spell hoặc pass trong post-block window.
                // KHÔNG làm điều này trong SpellStackResolve — restriction đó do CommitStagedSpells quản lý.
                if (model.phase == GamePhase.BlockDeclare)
                {
                    model.SetRestrictToSpellsOnly(true);
                    model.SetSpellRestrictionTarget(model.playerHasAttackToken); // restrict bên attacker
                    Debug.Log($"[GC] {Tag(a.byPlayer)} confirmed blocks → combat spell window mở cho attacker (Fast/Burst only).");
                }
            }

            model.PassPriority(a.byPlayer);
            Debug.Log($"[GC] {Tag(a.byPlayer)} passed priority.");
        }

        /// <summary>
        /// Hoàn tất "defender pass" trong BlockDeclare (bật combat spell window cho attacker + pass).
        /// Tách riêng để coroutine battle skill gọi khi player HỦY chọn target (không có skill để commit).
        /// Logic y hệt nhánh block window bình thường trong HandlePassPriority.
        /// </summary>
        void FinishDefenderBlockPass(bool byPlayer)
        {
            if (model.phase == GamePhase.BlockDeclare)
            {
                model.SetRestrictToSpellsOnly(true);
                model.SetSpellRestrictionTarget(model.playerHasAttackToken);
                Debug.Log($"[GC] {Tag(byPlayer)} confirmed blocks → combat spell window mở cho attacker (Fast/Burst only).");
            }
            model.PassPriority(byPlayer);
            Debug.Log($"[GC] {Tag(byPlayer)} passed priority.");
        }

        bool CommitStagedSpells(bool byPlayer)
        {
            var p = P(byPlayer);
            if (p.stagedSpells.Count == 0) return false;

            var toCommit = new List<CardModel>(p.stagedSpells);
            bool allBurst = toCommit.TrueForAll(s => s.data?.spellSpeed == SpellSpeed.Burst);

            foreach (var spell in toCommit)
            {
                // (TINH HỒN ★1/★3: -1 đã rải sẵn lên lá trong tay → currentManaCost đã giảm.)
                var (ok, spellManaUsed) = p.SpendManaForSpell(spell.currentManaCost);
                if (!ok) { Reject($"Thiếu mana khi commit {spell.data.cardName}."); return false; }
                _spellManaUsed[spell.id] = spellManaUsed;
                _manaCostPaid[spell.id] = spell.currentManaCost;  // ghi lại cost thực tế đã trả

                // SpellManaTrigger check
                UpdateSpellCharges(p, spellManaUsed);

                // Dòng Thức (Flow): mỗi spell staged → buff Flow units
                ApplyFlowBuff(byPlayer);

                // Cộng Hưởng (Augment): generated spell
                if (spell.data.isGenerated)
                    ApplyAugmentBuff(byPlayer);

                _cardsPlayedThisRound++;

                if (byPlayer && !networkMode) CampaignRun.ConstellationNotifyCardPlayed(p);   // TINH HỒN: chốt -1 lá đầu (idempotent, PvP: off)

                p.CommitStagedSpell(spell);
                model.spellStack.Push(MakeSpellStackEntry(p, spell));
                _spellSoundStack.Push(spell); // track để phát sfxSpellResolve trước ResolveTop
                AudioManager.PlayCardEvent(CardAudioData.Ev.SpellCast, spell.data?.audioData);   // thoại lúc dùng skill/spell

                // Burst resolve ngay — không cần hiện locked arrow
                if (!allBurst && _spellTargets.TryGetValue(spell.id, out var committedTargets) && committedTargets?.Count > 0)
                    OnSpellCommittedWithTargets?.Invoke(spell, committedTargets, byPlayer);

                bool hasTargets = _spellTargets.ContainsKey(spell.id);
                int regularUsed = spell.currentManaCost - spellManaUsed;
                Debug.Log($"[GC] Committed {spell.data.cardName} ({spell.data.spellSpeed}) " +
                          $"cost={spell.currentManaCost} (spellMana={spellManaUsed}, regular={regularUsed}). " +
                          $"targets={hasTargets}, requiresTarget={spell.data.requiresTarget}");
            }

            // ── Burst: resolve trực tiếp từ stack, không qua coroutine ───────────────
            if (allBurst)
            {
                _spellSoundStack.Clear(); // Burst resolve ngay — clear sound stack
                while (!model.spellStack.IsEmpty)
                    model.spellStack.ResolveTop(model);

                model.CheckGameOver();
                if (model.phase != GamePhase.GameOver)
                {
                    if (!model.combatPending)
                        model.ReturnToMainPhase();
                    Notify();
                }
                Debug.Log("[GC] Burst staged spell resolved immediately.");
                return true;
            }

            // ── Fast/Slow: normal stack flow ──────────────────────────────────────────
            model.BeginSpellStackResolve();
            model.SetRestrictToSpellsOnly(true);
            model.SetSpellRestrictionTarget(!byPlayer);
            model.HandOffPriority(byPlayer);
            return true;
        }

        // ── Sequential spell resolution ───────────────────────────
        IEnumerator ResolveSpellsSequentially()
        {
            _isResolvingSpells = true;
            RaiseStateChanged();
            yield return StartCoroutine(ResolveSpellsSequentiallyInner());
            _isResolvingSpells = false;
            RaiseStateChanged();
        }

        IEnumerator ResolveSpellsSequentiallyInner()
        {
            while (!model.spellStack.IsEmpty)
            {
                // Pause trước khi resolve — arrow kịp hiện cho player thấy target
                if (preResolveDelay > 0f)
                    yield return new WaitForSeconds(preResolveDelay);
                else
                    yield return null;

                // Phát sfxSpellResolve của spell (phase 2: bài phát huy tác dụng)
                if (_spellSoundStack.Count > 0)
                {
                    var resolveCard = _spellSoundStack.Pop();
                    AudioManager.PlayCardEvent(CardAudioData.Ev.SpellResolve, resolveCard?.data?.audioData);
                }

                // Game feel: bolt phép bay tới mục tiêu rồi nổ, TRƯỚC khi effect apply.
                if (combatAnimator != null)
                {
                    var topSpell = model.spellStack.PeekTop()?.source;
                    if (topSpell != null
                        && _spellTargets.TryGetValue(topSpell.id, out var vfxTargets)
                        && vfxTargets != null && vfxTargets.Count > 0)
                        yield return StartCoroutine(
                            combatAnimator.AnimateSpellProjectiles(topSpell.belongsToPlayer, vfxTargets));
                }

                model.spellStack.ResolveTop(model);

                model.CheckGameOver();
                Notify();
                if (model.phase == GamePhase.GameOver) yield break;

                if (spellResolveDelay > 0f)
                    yield return new WaitForSeconds(spellResolveDelay);
                else
                    yield return null;
            }

            model.CheckGameOver();
            if (model.phase == GamePhase.GameOver) { Notify(); yield break; }

            if (model.combatPending)
            {
                model.SetPhase(GamePhase.CombatResolve);
                yield return StartCoroutine(ResolveCombatSequentially());
            }
            else
            {
                // FIX (initiative): dùng Fast/Slow là tiêu "action" của lượt → resolve xong thì
                // lượt CHUYỂN SANG ĐỐI THỦ (giống LoR). Trước đây ReturnToMainPhase trả priority về
                // chính người cast → cast xong lại được đi tiếp, sai luật.
                // (Burst KHÔNG đi qua đây — nó resolve inline, giữ lượt cho người cast.)
                bool oppIsPlayer = !model.isPlayerPriority;   // đối thủ của người vừa cast
                model.SetPhase(oppIsPlayer ? GamePhase.PlayerPriority : GamePhase.EnemyPriority);
                model.GivePriorityTo(oppIsPlayer);
                Notify();
            }
        }

        // ── SpellShield / Hòa Hợp ────────────────────────────────
        /// <summary>
        /// Lọc SpellShield khỏi danh sách target trước khi spell/skill execute.
        /// Hòa Hợp vẫn chặn spell targeting như cũ — combat hits xử lý trong TakeDamage.
        /// </summary>
        void FilterSpellShield(GameContext ctx)
        {
            if (ctx.targetCards.Count == 0) return;
            for (int i = ctx.targetCards.Count - 1; i >= 0; i--)
            {
                var t = ctx.targetCards[i];
                if (t != null && t.TryConsumeSpellShield())
                {
                    ctx.targetCards.RemoveAt(i);
                    Debug.Log($"[Hòa Hợp] {t.data.cardName} chặn spell/skill!");
                }
            }
        }
    }
}