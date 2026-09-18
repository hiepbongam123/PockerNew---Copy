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
        // ── Hand → Bench ──────────────────────────────────────────
        void HandlePlayUnit(PlayUnitAction a)
        {
            var p = P(a.byPlayer);

            if (a.card.data.cardType != CardType.Unit)
            { Reject("Không phải Unit."); return; }

            bool inMainPhase = model.phase == GamePhase.PlayerPriority
                            || model.phase == GamePhase.EnemyPriority;
            if (!inMainPhase)
            { Reject($"Không thể đặt Unit trong phase {model.phase}."); return; }

            if (p.stagedSpells.Count > 0)
            { Reject("Đang có spell staging — commit hoặc hủy staging trước."); return; }
            // FIX: không thể triệu hồi unit khi đang có spell/skill chưa resolve trên stack
            // (áp dụng cho cả 2 phía, kể cả khi restrictToSpellsOnly chưa được set).
            if (!model.spellStack.IsEmpty)
            { Reject("Đang có spell/skill trên stack — không thể triệu hồi unit."); return; }
            if (!p.BenchHasSpace)
            { Reject("Bench đầy (tối đa 6)."); return; }
            // (TINH HỒN ★1/★3: -1 đã được rải sẵn lên lá này trong tay → currentManaCost đã giảm, check & trừ tự đúng.)
            if (AvailableRegularMana(p) < a.card.currentManaCost)
            { Reject($"Thiếu mana. Cần {a.card.currentManaCost}, còn {AvailableRegularMana(p)}."); return; }

            if (!p.PlayToBench(a.card))
            { Reject("Không thể đặt bài."); return; }

            // Level-Up: nếu champion đã thỏa điều kiện khi còn trên tay → level up ngay khi vừa ra sân
            if (a.card.readyToLevelUp)
                TriggerLevelUp(a.card);

            // Bình Minh / Hoàng Hôn: kiểm tra trước khi tăng counter
            bool isFirstCard = _cardsPlayedThisRound == 0;
            _cardsPlayedThisRound++;

            // TINH HỒN (player): ★2 unit đầu vòng +1|+1; chốt -1 lá đầu → hoàn -1 cho các lá còn lại trên tay.
            if (a.byPlayer && !networkMode)   // ★ PvP: off campaign (tránh desync)
            {
                CampaignRun.ConstellationOnUnitPlayed(a.card);
                CampaignRun.ConstellationNotifyCardPlayed(p);
            }

            // Section 2 Bible B4-B6: capture world pos của hand card TRƯỚC Notify() — sau đó hand view bị destroy
            Vector3? handWorldPos = null;
            if (combatAnimator != null)
            {
                var handCV = combatAnimator.GetHandView(a.card, a.byPlayer);
                if (handCV != null) handWorldPos = handCV.transform.position;
            }

            // Section 2 Bible B8: card đã có trên bench — Notify ngay để View hiển thị card trước animation
            // B9–B12 (spawn animation + WhenPlayed + HandOff) chạy trong SummonSequence coroutine
            Notify();
            StartCoroutine(SummonSequence(a.card, a.byPlayer, isFirstCard, handWorldPos));
        }

        /// <summary>
        /// Section 2 Gameplay Feel Bible — B9 đến HandOff.
        ///
        /// B9:  AnimateSummon (scale 0→1.12→1.0, ~180ms).
        /// B11: Spawn SFX.
        /// B12: FireTrigger(WhenPlayed) + keyword effects + ally triggers.
        ///      HandOffPriority sau khi tất cả effects xong.
        ///
        /// Tại sao dùng coroutine:
        ///   Bible nói "chỉ sau B12 mới phát OnSummoned (WhenPlayed)".
        ///   Dùng coroutine → animation xong → rồi mới fire skills → rồi HandOff.
        ///   Trong ~180ms animation, priority chưa handoff → đối thủ không thể hành động.
        /// </summary>
        IEnumerator SummonSequence(CardModel card, bool byPlayer, bool isFirstCard, Vector3? handWorldPos = null)
        {
            _isSummoningSequence = true;
            RaiseStateChanged();
            yield return StartCoroutine(SummonSequenceInner(card, byPlayer, isFirstCard, handWorldPos));
            _isSummoningSequence = false;
            RaiseStateChanged();
        }

        IEnumerator SummonSequenceInner(CardModel card, bool byPlayer, bool isFirstCard, Vector3? handWorldPos = null)
        {
            var p = P(byPlayer);

            // B4-B12: Kéo bài xuống bench → PHÓNG TO ra giữa màn hình rồi ĐẬP MẠNH xuống bench slot.
            // AnimatePlayToBench (kiểu ZoomSlam) gộp cả zoom-up + slam + camera shake + squash khi chạm đất,
            // thay cho AnimateCardFromPosition + AnimateSummon (kiểu bay-thẳng + pop-in cũ).
            // Style theo playSummonStyle của CombatAnimator (AutoByOwner: player = ZoomSlam, enemy = FlipReveal).
            // Tinh chỉnh cảm giác đập: chỉnh zoomPeakScale / slamDuration / zoomHoldDuration trong Inspector CombatAnimator.
            if (combatAnimator != null)
            {
                var cv = combatAnimator.GetBenchView(card, byPlayer);
                if (cv != null)
                    yield return StartCoroutine(combatAnimator.AnimatePlayToBench(cv, handWorldPos, byPlayer));
            }

            // B11: Thoại ra sân — bốc ngẫu nhiên từ CardAudioData (kênh Card riêng)
            AudioManager.PlayCardEvent(CardAudioData.Ev.Summon, card.data?.audioData);

            // B12: WhenPlayed fire sau khi animation hoàn tất.
            // (Auto-equip đã chạy qua event OnCardPlayedToBench ngay khi PlayToBench —
            //  xem InitEquipmentDeathHandlers — nên trang bị đã có mặt trước WhenPlayed.)
            FireTrigger(card, SkillTrigger.WhenPlayed, byPlayer);

            // Chú Thuật (Spellcraft): khi chơi từ tay → tạo spell cụ thể lên tay
            if (card.HasKeyword(KeywordType.Spellcraft) && card.data.spellcraftSpell != null)
            {
                p.CreateCard(card.data.spellcraftSpell);
                Debug.Log($"[Chú Thuật] {card.data.cardName} chơi → tạo {card.data.spellcraftSpell.cardName} lên tay.");
            }

            // Kỹ Năng (UnitSkill):
            //
            // VẤN ĐỀ THỨ TỰ STACK (quan trọng nhất):
            //   Nếu chỉ staged → player pass → commit: skill bị commit SAU response spell của enemy
            //   → skill lên TOP stack → resolve TRƯỚC response → SAI LIFO.
            //
            // Fix: CommitStagedSpells NGAY sau khi có target:
            //   skill vào BOTTOM stack → enemy respond đè lên TOP → response resolve trước ✓
            //
            // Arrow hiện ngay:
            //   GameView.StartUnitSkillTargeting set _lockedArrowsBySpell trực tiếp sau khi chọn target
            //   → arrow hiện trước cả CommitStagedSpells, không cần player pass thêm ✓
            //
            // Stack thứ tự đúng:
            //   Unit plays → skill committed (BOTTOM) → enemy response committed (TOP)
            //   → both pass → TOP (response) resolves first → BOTTOM (skill) resolves after ✓
            // Kỹ Năng (UnitSkill): stage skill lên stack khi RA SÂN.
            // Nếu unitSkillOnAttack = true → KHÔNG stage ở đây, để dành kích khi tấn công/chặn
            // (kiểu Renekton — StageAndCommitBattleSkills gọi cùng luồng confirm tấn công/chặn).
            if (card.HasKeyword(KeywordType.UnitSkill) && card.data.spellcraftSpell != null
                && !card.data.unitSkillOnAttack)
                yield return StartCoroutine(StageUnitSkillSpell(card, byPlayer));

            // Cảm Tử (Ephemeral): sound khi triệu hồi từ tay
            if (card.HasKeyword(KeywordType.Ephemeral))
                AudioManager.Instance?.PlayKwEphemeral();

            // Bình Minh (Daybreak): lá đầu tiên round này → +1|+1 ngay
            if (isFirstCard && card.HasKeyword(KeywordType.Daybreak))
            {
                card.BuffAttack(1); card.BuffHealth(1);
                Debug.Log($"[Bình Minh] {card.data.cardName} lá đầu tiên round → +1|+1.");
            }
            // Hoàng Hôn (Nightfall): KHÔNG phải lá đầu tiên → +1|+1 ngay
            else if (!isFirstCard && card.HasKeyword(KeywordType.Nightfall))
            {
                card.BuffAttack(1); card.BuffHealth(1);
                Debug.Log($"[Hoàng Hôn] {card.data.cardName} không phải lá đầu → +1|+1.");
            }

            // Cộng Hưởng (Augment): nếu lá vừa chơi là generated → buff tất cả unit Augment +1|+0
            if (card.data.isGenerated)
                ApplyAugmentBuff(byPlayer);

            // Notify đồng minh: ai đó vừa ra sân
            TriggerAllAllies(SkillTrigger.OnAllyPlayed, byPlayer, card);
            TriggerAllAllies(SkillTrigger.OnRelatedAllyPlayed, byPlayer, card);
            // FIX: đánh giá passive level-up đếm-đồng-minh (AllyCountOnBoard / AllyTagCountOnBoard — Elise)
            // NGAY sau khi 1 unit ra sân. Trước đây chỉ chạy ở OnAttack/RoundStart nên trigger=OnAllyPlayed không bao giờ được kiểm.
            EvaluatePassiveLevelUpsAtTrigger(SkillTrigger.OnAllyPlayed);

            Debug.Log($"[GC] {Tag(byPlayer)} played {card.data.cardName} to bench.");

            // HandOff sau khi tất cả B12 effects xong.
            // Skip nếu CommitStagedSpells (UnitSkill) đã HandOff rồi (phase == SpellStackResolve).
            // Skip nếu WhenPlayed trigger đã gọi SummonUnit toBattlefield → phase == AttackDeclare.
            if (model.phase == GamePhase.AttackDeclare && model.combatPending)
                model.HandOffPriority(!byPlayer);
            else if (model.phase != GamePhase.SpellStackResolve)
                model.HandOffPriority(byPlayer);

            Notify();
        }

        // ── Aatrox-style auto-equip ───────────────────────────────
        /// <summary>
        /// Nếu unit có CardData.autoEquipOnSummon (1 Equipment) → tự tạo equipment CardModel
        /// và gắn lên unit. Được gọi qua event OnCardPlayedToBench / OnCardMovedToBattlefield
        /// (đăng ký trong InitEquipmentDeathHandlers) → áp cho MỌI cách unit xuất hiện trên sân:
        /// chơi từ tay, summon bằng skill, summon từ deck, ĐẶT SẴN (tutorial Leo Tháp), v.v.
        /// Bỏ qua nếu unit đã đeo sẵn trang bị khác (event battlefield fire khi unit bench lên
        /// tấn công cũng sẽ bị skip vì đã đeo).
        /// </summary>
        /// <summary>
        /// Stage spellcraftSpell của unit lên stack (kiểu "play a skill" — Dominus Destruction).
        /// Dùng cho UnitSkill lúc ra sân (unitSkillOnAttack = false).
        /// </summary>
        public IEnumerator StageUnitSkillSpell(CardModel card, bool byPlayer)
        {
            if (card == null || card.data == null || card.data.spellcraftSpell == null) yield break;
            var p = P(byPlayer);
            var skillCard = p.CreateCard(card.data.spellcraftSpell);
            if (skillCard == null) yield break; // hand đầy
            p.StageSpell(skillCard);
            Notify();

            // Battle-skill kiểu Renekton (unitSkillOnAttack): cho lá skill HIỆN trên spell zone 1 nhịp
            // rồi mới resolve. Skill spell để spellSpeed = Burst → CommitStagedSpells resolve INLINE ngay
            // (không mở cửa sổ pass 2 bên) → không có confirm thừa, không kẹt, KHÔNG đổi luồng combat.
            // Delay này chỉ để mắt kịp thấy lá bài (Burst vốn resolve trong 1 frame nên trước đây không thấy).
            if (card.data.unitSkillOnAttack && battleSkillShowDelay > 0f)
                yield return new WaitForSeconds(battleSkillShowDelay);

            if (skillCard.data.requiresTarget && OnUnitSkillNeedsTarget != null)
            {
                List<CardModel> skillTargets = null;
                bool targetingDone = false;
                _isUnitSkillTargeting = true;

                // Gen activation (chỉ dùng cho PvP) — tăng ĐỒNG BỘ trên cả 2 máy (lockstep).
                int netGen = networkMode ? NextUnitSkillGen(card.netId) : 0;

                if (!networkMode && byPlayer && playerAutoPlay)
                {
                    // PvE auto-play: AI tự chọn (không có người) — auto-pick xác định.
                    skillTargets = AutoPickTargets(skillCard.data, byPlayer);
                    targetingDone = true;
                }
                else if (networkMode && byPlayer != NetLocalIsPlayer)
                {
                    // ★ PvP — MÁY KIA (không phải chủ unit): KHÔNG mở UI, CHỜ target do chủ relay tới
                    //   (keyed theo unit + gen). Timeout dài (last-resort) chỉ để chống treo khi mất
                    //   gói/đối thủ AFK — KHÔNG bắn trong lúc chơi bình thường.
                    List<CardModel> netT = null;
                    bool timedOut = false;
                    float t0 = UnityEngine.Time.realtimeSinceStartup;
                    yield return new WaitUntil(() =>
                    {
                        if (TryTakeUnitSkillTargets(card.netId, netGen, out var tt)) { netT = tt; return true; }
                        if (model.phase == GamePhase.GameOver) return true;
                        if (UnityEngine.Time.realtimeSinceStartup - t0 > 300f) { timedOut = true; return true; }
                        return false;
                    });
                    if (timedOut)
                        Debug.LogError($"[Net] Unit-skill target CHỜ QUÁ LÂU (unit netId={card.netId}, gen={netGen}) " +
                                       "— có thể mất gói mạng hoặc đối thủ AFK → hủy skill để không treo.");
                    skillTargets = netT;
                    targetingDone = true;
                }
                else
                {
                    // CHỦ unit (PvE người thật hoặc PvP phe mình): mở UI cho người chọn.
                    OnUnitSkillNeedsTarget.Invoke(card, skillCard, byPlayer, result =>
                    {
                        skillTargets = result;
                        targetingDone = true;
                    });
                }

                yield return new WaitUntil(() => targetingDone);
                _isUnitSkillTargeting = false;

                // ★ PvP — CHỦ unit: phát lựa chọn (kể cả rỗng = hủy) cho máy kia.
                if (networkMode && byPlayer == NetLocalIsPlayer)
                    SubmitLocalUnitSkillTargets(card, netGen, skillTargets);

                if (skillTargets != null && skillTargets.Count > 0)
                {
                    SetSpellTargets(skillCard, skillTargets);
                    CommitStagedSpells(byPlayer);
                }
                else
                {
                    p.UnstageSpell(skillCard); // cancel
                }
            }
            else
            {
                CommitStagedSpells(byPlayer);
            }

            Debug.Log($"[Kỹ Năng] {card.data.cardName} → skill {skillCard.data.cardName} lên stack.");
        }

        /// <summary>Có unit nào (trong list) mang battle skill CHƯA stage round này không?
        /// isBlock = true → bỏ qua unit có unitSkillAttackOnly (chỉ kích khi tấn công).</summary>
        bool AnyPendingBattleSkill(List<CardModel> units, bool isBlock)
        {
            if (units == null) return false;
            foreach (var u in units)
                if (u?.data != null && u.HasKeyword(KeywordType.UnitSkill)
                    && u.data.unitSkillOnAttack && u.data.spellcraftSpell != null
                    && !(isBlock && u.data.unitSkillAttackOnly)
                    && !_battleSkillStaged.Contains(u.id))
                    return true;
            return false;
        }

        /// <summary>
        /// Renekton/Lee Sin-style: stage skill (spellcraftSpell) của các unit TẤN CÔNG/CHẶN lên spell zone.
        /// HỖ TRỢ skill CẦN CHỌN TARGET (requiresTarget) qua OnUnitSkillNeedsTarget — dùng chung UI
        /// targeting với UnitSkill lúc ra sân. Là coroutine vì targeting là async.
        ///
        /// isBlock = false (TẤN CÔNG): stage (+ chọn target khi attacker CÒN priority) rồi tự chuyển
        ///     sang BlockDeclare. Skill KHÔNG commit ở đây — tự commit ở post-block window → resolve
        ///     xong chảy thẳng vào combat (một chuỗi, bài phép trước rồi tới bài quân).
        /// isBlock = true (CHẶN): stage (+ chọn target) rồi commit ngay trong lượt defender (mở response
        ///     cho attacker). Hủy chọn target → hoàn tất pass phòng thủ như thường.
        ///
        /// LƯU Ý: skill spell nên để spellSpeed = Fast, manaCost = 0.
        /// </summary>
        IEnumerator StageBattleSkillsCo(List<CardModel> units, bool byPlayer, bool isBlock)
        {
            var p = P(byPlayer);
            int stagedCount = 0;

            if (units != null)
                foreach (var u in units)
                {
                    if (u == null || u.data == null) continue;
                    if (!u.HasKeyword(KeywordType.UnitSkill)) continue;
                    if (!u.data.unitSkillOnAttack || u.data.spellcraftSpell == null) continue;
                    if (isBlock && u.data.unitSkillAttackOnly) continue; // chỉ kích khi tấn công → bỏ qua khi chặn
                    if (!_battleSkillStaged.Add(u.id)) continue; // đã stage round này

                    var skillCard = p.CreateCard(u.data.spellcraftSpell);
                    if (skillCard == null) { _battleSkillStaged.Remove(u.id); continue; }        // hand đầy
                    if (!p.StageSpell(skillCard)) { p.KillCard(skillCard); _battleSkillStaged.Remove(u.id); continue; }
                    _battleSkillUnit[skillCard.id] = u; // để skill combat-target biết unit gốc là ai
                    stagedCount++;
                    Notify();
                    Debug.Log($"[Kỹ Năng] {u.data.cardName} → skill {skillCard.data.cardName} stage lên spell zone.");

                    // Skill cần chọn mục tiêu → mở targeting (chặn action khác qua _isUnitSkillTargeting)
                    if (skillCard.data.requiresTarget && OnUnitSkillNeedsTarget != null)
                    {
                        List<CardModel> targets = null;
                        bool doneTargeting = false;
                        _isUnitSkillTargeting = true;

                        // Gen activation (PvP) — tăng đồng bộ 2 máy.
                        int netGen = networkMode ? NextUnitSkillGen(u.netId) : 0;

                        if (!networkMode && byPlayer && playerAutoPlay)
                        {
                            // PvE auto-play: AI tự chọn.
                            targets = AutoPickTargets(skillCard.data, byPlayer);
                            doneTargeting = true;
                        }
                        else if (networkMode && byPlayer != NetLocalIsPlayer)
                        {
                            // ★ PvP — MÁY KIA: chờ target chủ unit relay (keyed theo unit u + gen).
                            List<CardModel> netT = null;
                            bool timedOut = false;
                            float t0 = UnityEngine.Time.realtimeSinceStartup;
                            yield return new WaitUntil(() =>
                            {
                                if (TryTakeUnitSkillTargets(u.netId, netGen, out var tt)) { netT = tt; return true; }
                                if (model.phase == GamePhase.GameOver) return true;
                                if (UnityEngine.Time.realtimeSinceStartup - t0 > 300f) { timedOut = true; return true; }
                                return false;
                            });
                            if (timedOut)
                                Debug.LogError($"[Net] Battle-skill target CHỜ QUÁ LÂU (unit netId={u.netId}, gen={netGen}) " +
                                               "— mất gói/đối thủ AFK → hủy skill.");
                            targets = netT;
                            doneTargeting = true;
                        }
                        else
                        {
                            // CHỦ unit: mở UI chọn.
                            OnUnitSkillNeedsTarget.Invoke(u, skillCard, byPlayer, r => { targets = r; doneTargeting = true; });
                        }

                        yield return new WaitUntil(() => doneTargeting);
                        _isUnitSkillTargeting = false;

                        // ★ PvP — CHỦ unit: phát lựa chọn cho máy kia.
                        if (networkMode && byPlayer == NetLocalIsPlayer)
                            SubmitLocalUnitSkillTargets(u, netGen, targets);

                        if (targets != null && targets.Count > 0)
                        {
                            SetSpellTargets(skillCard, targets);
                        }
                        else // hủy chọn → gỡ skill khỏi spell zone
                        {
                            p.UnstageSpell(skillCard);
                            p.KillCard(skillCard);
                            _battleSkillUnit.Remove(skillCard.id);
                            _battleSkillStaged.Remove(u.id);
                            stagedCount--;
                            Notify();
                        }
                    }
                    else if (!skillCard.data.requiresTarget)
                    {
                        // KHÔNG chọn tay: tự nhắm kẻ đang giao tranh (kẻ bị thách đấu / chặn / tấn công).
                        // Set target khóa + báo GameView để HIỆN MŨI TÊN (và SkillDamageTarget đánh đúng kẻ đó).
                        var foe = u.blockedBy ?? u.blockingTarget;
                        if (foe != null && foe.IsAlive)
                        {
                            var tlist = new List<CardModel> { foe };
                            SetSpellTargets(skillCard, tlist);
                            OnSpellCommittedWithTargets?.Invoke(skillCard, tlist, byPlayer); // vẽ mũi tên khóa
                        }
                    }
                }

            if (isBlock)
            {
                // CHẶN: có skill → commit ngay (mở response). Không có (hủy target) → hoàn tất pass phòng thủ.
                if (stagedCount > 0) CommitStagedSpells(byPlayer);
                else FinishDefenderBlockPass(byPlayer);
            }
            else
            {
                // TẤN CÔNG: stage + chọn target xong → chuyển sang BlockDeclare (skill commit ở post-block).
                model.SetPhase(GamePhase.BlockDeclare);
                model.HandOffPriority(byPlayer);
                Debug.Log($"[GC] {Tag(byPlayer)} confirmed attackers → BlockDeclare.");
                Notify();
            }
        }

        void AutoEquipOnSummon(CardModel unit)
        {
            if (unit == null || unit.data == null) return;
            var eqData = unit.data.autoEquipOnSummon;
            if (eqData == null || eqData.cardType != CardType.Equipment) return;
            if (unit.equipmentAttached != null) return; // đã có trang bị → không đè

            bool byPlayer = unit.belongsToPlayer;
            var equipModel = new CardModel(eqData, byPlayer);
            equipModel.SetLocation(CardLocation.Equipped);
            ApplyEquipment(equipModel, unit, byPlayer);
            Debug.Log($"[Trang Bị] {unit.data.cardName} tự đeo '{eqData.cardName}' khi có mặt trên sân.");
        }

        // ── Skill: Summon Unit ────────────────────────────────────
        /// <summary>
        /// Triệu hồi unit thẳng lên bench của forPlayer không qua tay, không tốn mana.
        /// Gọi từ SkillSummonUnit.Execute().
        ///
        /// Guard _isSummoning ngăn đệ quy vô tận:
        ///   VD: A triệu hồi B → WhenPlayed của B triệu hồi A → chặn tại đây.
        /// Nếu muốn chuỗi summon sâu hơn 1 cấp, tắt guard và tự chịu trách nhiệm
        /// thiết kế CardData không tạo vòng lặp.
        /// </summary>
        /// <param name="toBattlefield">
        /// true  → summon thẳng lên battlefield (vị trí tấn công).
        /// false → summon lên bench (mặc định).
        /// </param>
        public CardModel SummonUnit(CardData data, bool forPlayer, bool toBattlefield = false)
        {
            if (_isSummoning)
            {
                Debug.LogWarning($"[SummonUnit] Recursion guard — bỏ qua summon '{data?.cardName}'.");
                return null;
            }

            if (data == null || data.cardType != CardType.Unit)
            {
                Debug.LogWarning("[SummonUnit] data null hoặc không phải Unit.");
                return null;
            }

            var p = P(forPlayer);

            if (toBattlefield && !p.BattlefieldHasSpace)
            {
                Debug.Log($"[SummonUnit] Battlefield của {Tag(forPlayer)} đầy — bỏ qua '{data.cardName}'.");
                return null;
            }
            if (!toBattlefield && !p.BenchHasSpace)
            {
                Debug.Log($"[SummonUnit] Bench của {Tag(forPlayer)} đầy — bỏ qua '{data.cardName}'.");
                return null;
            }

            _isSummoning = true;
            try
            {
                var card = toBattlefield
                    ? p.SummonToBattlefield(data)   // fire OnCardMovedToBattlefield → BattlefieldZoneView
                    : p.SummonToBench(data);         // fire OnCardPlayedToBench     → BenchZoneView
                if (card == null) return null;

                ApplyPostSummonEffects(card, forPlayer, toBattlefield);
                return card;
            }
            finally
            {
                _isSummoning = false;
            }
        }

        // ── Skill: Summon Unit FROM DECK ──────────────────────────
        /// <summary>
        /// Triệu hồi một CardModel CÓ SẴN trong deck của forPlayer lên bench/battlefield.
        ///
        /// Khác SummonUnit(CardData):
        ///   • Di chuyển CardModel thật từ deck → giữ nguyên mọi buff runtime
        ///     (Lurk, Kayle passive... đã buff card khi còn trong deck).
        ///   • Card bị XÓA khỏi deck → deck mỏng đi (ảnh hưởng Deep, draw) — đúng luật LoR.
        ///   • Không tạo bản copy → không có bug nhân đôi lá bài.
        ///
        /// Post-summon effects (WhenPlayed, level-up, Spellcraft, Tough, ally triggers,
        /// auto-join attack nếu toBattlefield) giống hệt SummonUnit.
        /// Trả về null nếu: recursion guard, card không hợp lệ / không còn trong deck,
        /// hoặc vị trí đích đầy.
        /// </summary>
        public CardModel SummonUnitFromDeck(CardModel deckCard, bool forPlayer, bool toBattlefield = false)
        {
            if (_isSummoning)
            {
                Debug.LogWarning($"[SummonUnitFromDeck] Recursion guard — bỏ qua '{deckCard?.data?.cardName}'.");
                return null;
            }
            if (deckCard == null || deckCard.data.cardType != CardType.Unit)
            {
                Debug.LogWarning("[SummonUnitFromDeck] deckCard null hoặc không phải Unit.");
                return null;
            }

            var p = P(forPlayer);
            if (!p.deck.Contains(deckCard))
            {
                Debug.LogWarning($"[SummonUnitFromDeck] '{deckCard.data.cardName}' không còn trong deck.");
                return null;
            }
            if (toBattlefield && !p.BattlefieldHasSpace)
            {
                Debug.Log($"[SummonUnitFromDeck] Battlefield {Tag(forPlayer)} đầy — bỏ qua '{deckCard.data.cardName}'.");
                return null;
            }
            if (!toBattlefield && !p.BenchHasSpace)
            {
                Debug.Log($"[SummonUnitFromDeck] Bench {Tag(forPlayer)} đầy — bỏ qua '{deckCard.data.cardName}'.");
                return null;
            }

            _isSummoning = true;
            try
            {
                int deckIndex = p.deck.IndexOf(deckCard);
                p.deck.Remove(deckCard); // xóa khỏi deck TRƯỚC khi đặt lên sân → không nhân đôi

                var card = toBattlefield
                    ? p.SummonExistingToBattlefield(deckCard)
                    : p.SummonExistingToBench(deckCard);
                if (card == null)
                {
                    p.deck.Insert(deckIndex, deckCard); // rollback — trả về đúng vị trí cũ
                    return null;
                }

                Debug.Log($"[GC] SummonUnitFromDeck: '{card.data.cardName}' từ deck (index {deckIndex}) → sân {Tag(forPlayer)}.");
                // LOOP MODE: lá này bị RÚT KHỎI DECK để ra sân → chèn 1 bản sao bù vào deck
                // (đường summon-from-deck không qua "chơi từ tay" nên không tự seed).
                p.SeedReplacementForDeckSummon(deckCard);
                ApplyPostSummonEffects(card, forPlayer, toBattlefield);
                return card;
            }
            finally
            {
                _isSummoning = false;
            }
        }

        // ── Post-summon effects (dùng chung SummonUnit + SummonUnitFromDeck) ──
        // Code chuyển nguyên vẹn từ SummonUnit cũ — không đổi hành vi.
        void ApplyPostSummonEffects(CardModel card, bool forPlayer, bool toBattlefield)
        {
            var p = P(forPlayer);
            var data = card.data;
            AudioManager.PlayCardEvent(CardAudioData.Ev.Summon, card.data?.audioData);   // thoại ra sân (summon bằng skill)

            // Nếu summon thẳng lên battlefield VÀ bên này có attack token (chưa dùng)
            // → unit tự động tham chiến bất kể phase hiện tại (main phase hay mid-combat).
            if (toBattlefield)
            {
                bool isAttacker = forPlayer == model.playerHasAttackToken;
                bool canAttack = !model.attackTokenUsed;                  // token chưa tiêu

                // Chỉ set Attacking nếu đang ở BlockDeclare trở về trước
                // (không join attack sau khi defender đã confirm block).
                bool tooLate = model.phase == GamePhase.CombatResolve;

                if (isAttacker && canAttack && !tooLate)
                {
                    card.state = CardState.Attacking;
                    if (!model.combatPending) model.combatPending = true;

                    if (model.phase != GamePhase.AttackDeclare
                        && model.phase != GamePhase.BlockDeclare
                        && model.phase != GamePhase.SpellStackResolve)
                    {
                        model.SetPhase(GamePhase.AttackDeclare);
                    }

                    // Bug fix: Fire OnAttack ngay (không đợi Block A của HandlePassPriority)
                    // vì unit này bỏ qua flow declare thông thường.
                    // _attackTriggered.Add() đảm bảo Block A không fire lại lần nữa.
                    if (_attackTriggered.Add(card.id))
                    {
                        FireTrigger(card, SkillTrigger.OnAttack, forPlayer);

                        // Keyword SFX — giống Block B
                        if (card.HasKeyword(KeywordType.QuickAttack)) AudioManager.Instance?.PlayKwQuickAttack();
                        if (card.HasKeyword(KeywordType.Overwhelm)) AudioManager.Instance?.PlayKwOverwhelmAttack();
                        if (card.HasKeyword(KeywordType.Fearsome)) AudioManager.Instance?.PlayKwFearsome();
                        if (card.HasKeyword(KeywordType.Elusive)) AudioManager.Instance?.PlayKwElusive();
                        if (card.HasKeyword(KeywordType.CantBlock)) AudioManager.Instance?.PlayKwCantBlockAttack();

                        // Animation: lean về phía địch (view có thể chưa spawn kịp — StartCoroutine để delay 1 frame)
                        if (combatAnimator != null)
                        {
                            var atkCV = combatAnimator.GetView(card, forPlayer);
                            if (atkCV != null) StartCoroutine(combatAnimator.AnimateAttackLean(atkCV, forPlayer));
                        }
                    }

                    Debug.Log($"[SummonUnit] '{data.cardName}' summon → Attacking + OnAttack fired (phase={model.phase}).");
                }
            }

            var dest = toBattlefield ? "battlefield" : "bench";
            Debug.Log($"[GC] SummonUnit: '{data.cardName}' → {dest} {Tag(forPlayer)}.");

            // Fire WhenPlayed như unit thông thường.
            // (Auto-equip đã chạy qua event OnCardPlayedToBench / OnCardMovedToBattlefield.)
            FireTrigger(card, SkillTrigger.WhenPlayed, forPlayer);

            // Level-Up: nếu champion đã thỏa điều kiện trước khi được triệu hồi → level up ngay
            if (card.readyToLevelUp)
                TriggerLevelUp(card);

            // Chú Thuật (Spellcraft): khi triệu hồi bằng skill → tạo spell lên tay
            if (card.HasKeyword(KeywordType.Spellcraft) && card.data.spellcraftSpell != null)
            {
                p.CreateCard(card.data.spellcraftSpell);
                Debug.Log($"[Chú Thuật] {card.data.cardName} triệu hồi → tạo {card.data.spellcraftSpell.cardName} lên tay.");
            }

            // Cảm Tử (Ephemeral): sound khi triệu hồi bằng skill
            if (card.HasKeyword(KeywordType.Ephemeral))
                AudioManager.Instance?.PlayKwEphemeral();

            // Tri Thức (Tough): khi triệu hồi → +1 spell mana
            if (card.HasKeyword(KeywordType.Tough))
            {
                p.AddSpellMana(1);
                Debug.Log($"[Tri Thức] {card.data.cardName} summoned → +1 spell mana.");
            }

            // Notify đồng minh: ai đó vừa ra sân
            TriggerAllAllies(SkillTrigger.OnAllyPlayed, forPlayer, card);
            TriggerAllAllies(SkillTrigger.OnRelatedAllyPlayed, forPlayer, card);
            EvaluatePassiveLevelUpsAtTrigger(SkillTrigger.OnAllyPlayed);
        }
    }
}