using System.Collections.Generic;
using UnityEngine;
using LoRClone;
using LoRClone.Data;
using LoRClone.Model;

namespace LoRClone.Controller
{
    public partial class GameController
    {
        // ── Trigger Helpers ───────────────────────────────────────
        void FireTrigger(CardModel card, SkillTrigger trigger, bool ownerIsPlayer)
        {
            var ctx = new GameContext(model, P(ownerIsPlayer), this);
            // WhenPlayed với targeting: đọc pre-selected targets (set bởi GameView trước PlayUnitAction)
            if (trigger == SkillTrigger.WhenPlayed
                && _spellTargets.TryGetValue(card.id, out var unitTargets))
            {
                ctx.targetCards = new System.Collections.Generic.List<CardModel>(unitTargets);
                _spellTargets.Remove(card.id);
            }
            ExecuteMatching(card, trigger, ctx);
        }

        /// <summary>
        /// Fire trigger trên toàn bộ unit sống phía isPlayer (bench + battlefield).
        /// triggerCard  = unit gây ra sự kiện (ai chết / ai ra sân / ai nhận damage...).
        /// triggerAmount = lượng damage (chỉ dùng cho OnAllyDamaged / OnAllyDealsDamage).
        /// </summary>
        void TriggerAllAllies(SkillTrigger trigger, bool isPlayer,
                              CardModel triggerCard, int triggerAmount = 0)
        {
            var owner = P(isPlayer);
            var ctx = new GameContext(model, owner, this)
            {
                triggerCard = triggerCard,
                triggerAmount = triggerAmount,
            };

            // Snapshot để tránh modify-while-iterate nếu skill thêm/xóa unit
            // Bao gồm cả hand — nhiều trigger (OnAllyDeath, RoundStart, v.v.) cũng fire
            // cho card đang ở tay (VD: spell giảm mana khi đồng minh chết).
            var cards = new List<CardModel>();
            foreach (var c in owner.hand) cards.Add(c);
            foreach (var c in owner.BenchCards()) if (c.IsAlive) cards.Add(c);
            foreach (var c in owner.BattlefieldCards()) if (c.IsAlive) cards.Add(c);

            foreach (var c in cards)
                ExecuteMatching(c, trigger, ctx);
        }

        /// <summary>
        /// Fire trigger trên TOÀN BỘ card của isPlayer:
        ///   hand → bench → battlefield → deck (chưa rút).
        ///
        /// Dùng cho các hiệu ứng "buff dù đang ở đâu" như Kayle passive (OnEmpoweredAllyAttack).
        /// Cards trong deck không có CardView nên không cần lo UI — model vẫn được cập nhật,
        /// và khi rút lên hand CardView sẽ đọc currentAttack mới nhất.
        /// </summary>
        void TriggerAllCards(SkillTrigger trigger, bool isPlayer,
                             CardModel triggerCard = null, int triggerAmount = 0)
        {
            var owner = P(isPlayer);
            var ctx = new GameContext(model, owner, this)
            {
                triggerCard = triggerCard,
                triggerAmount = triggerAmount,
            };

            // Snapshot toàn bộ vùng — tránh modify-while-iterate
            var cards = new List<CardModel>();
            foreach (var c in owner.hand) cards.Add(c);
            foreach (var c in owner.BenchCards()) if (c.IsAlive) cards.Add(c);
            foreach (var c in owner.BattlefieldCards()) if (c.IsAlive) cards.Add(c);
            foreach (var c in owner.deck) cards.Add(c);

            foreach (var c in cards)
                ExecuteMatching(c, trigger, ctx);
        }

        /// <summary>True nếu candidate.originalData nằm trong relatedCards của source.</summary>
        static bool IsRelatedTo(CardModel source, CardModel candidate)
        {
            if (candidate == null) return false;
            var related = source.data.relatedCards;
            return related != null && related.Contains(candidate.originalData);
        }

        /// <summary>
        /// True nếu card đang ở vùng cho phép ability kích hoạt.
        /// StagingSpell / OnStack / Equipped / InDiscard: luôn false —
        /// spell đang cast thực thi qua đường riêng (HandleCastSpell / MakeSpellStackEntry),
        /// không đi qua ExecuteMatching nên không bị chặn nhầm.
        /// </summary>
        static bool AbilityActiveAt(CardLocation loc, AbilityActiveZone zone)
        {
            bool onBoard = loc == CardLocation.OnBench || loc == CardLocation.OnBattlefield;
            bool inHand = loc == CardLocation.InHand;
            switch (zone)
            {
                case AbilityActiveZone.OnBoard: return onBoard;
                case AbilityActiveZone.InHand: return inHand;
                case AbilityActiveZone.HandAndBoard: return onBoard || inHand;
                case AbilityActiveZone.Anywhere: return true;
            }
            return onBoard;
        }

        void ExecuteMatching(CardModel card, SkillTrigger trigger, GameContext ctx)
        {
            // OnRelatedAllyPlayed: chỉ fire nếu triggerCard nằm trong relatedCards của card này
            bool checkRelated = trigger == SkillTrigger.OnRelatedAllyPlayed;
            if (checkRelated && !IsRelatedTo(card, ctx.triggerCard)) return;

            // FIX: legacy 'skills' TRƯỚC ĐÂY không có cổng vùng, trong khi TriggerAllAllies quét cả
            // HAND và TriggerAllCards quét cả DECK → skill chạy dù lá còn trên tay/trong deck.
            // Nay áp cùng mặc định với abilities: chỉ chạy khi lá ĐANG Ở SÂN (bench/battlefield).
            // Spell đang cast KHÔNG bị ảnh hưởng (resolve qua MakeSpellStackEntry/HandleCastSpell, không qua đây).
            bool _skillOnBoard = card.location == CardLocation.OnBench
                              || card.location == CardLocation.OnBattlefield;
            foreach (var skill in card.data.skills)
                if (skill != null && skill.trigger == trigger && _skillOnBoard)
                    skill.Execute(card, ctx);

            for (int _i = 0; _i < card.data.abilities.Count; _i++)
            {
                var ability = card.data.abilities[_i];
                if (ability.effect == null || ability.condition != trigger) continue;
                // Zone gate: ability chỉ kích hoạt khi card đang ở đúng vùng cấu hình
                // (chuẩn hóa hand vs board — TriggerAllAllies/TriggerAllCards fire cả
                // hand/deck nhưng ability mặc định OnBoard sẽ tự lọc).
                if (!AbilityActiveAt(card.location, ability.activeZone)) continue;
                // oncePerRound: bỏ qua nếu ability này đã fire trong round hiện tại
                if (ability.oncePerRound && card.HasAbilityFiredThisRound(_i)) continue;
                ctx.temporaryBuff = ability.temporaryBuff;
                ctx.grantKeyword = ability.grantKeyword;
                ctx.grantKeywordType = ability.grantKeywordType;
                ability.effect.Execute(card, ctx, ability.p1, ability.p2, ability.p3);
                ctx.temporaryBuff = false;
                ctx.grantKeyword = false;
                if (ability.oncePerRound) card.MarkAbilityFired(_i);
            }

            // ConditionalAbility: cùng hệ abilities nhưng thêm điều kiện StatCondition
            // (requiredAttack / requiredHealth). Chỉ fire khi cả trigger lẫn stat thỏa.
            // Index offset = abilities.Count để tránh collision với _abilitiesFiredThisRound
            int _condOffset = card.data.abilities.Count;
            for (int _i = 0; _i < card.data.conditionalAbilities.Count; _i++)
            {
                var ca = card.data.conditionalAbilities[_i];
                if (ca.ability.effect == null) continue;
                if (ca.ability.condition != trigger) continue;
                // Zone gate — giống abilities thường
                if (!AbilityActiveAt(card.location, ca.ability.activeZone)) continue;
                if (!card.EvalCondition(ca.condition)) continue;
                int _idx = _condOffset + _i;
                if (ca.ability.oncePerRound && card.HasAbilityFiredThisRound(_idx)) continue;
                ctx.temporaryBuff = ca.ability.temporaryBuff;
                ctx.grantKeyword = ca.ability.grantKeyword;
                ctx.grantKeywordType = ca.ability.grantKeywordType;
                ca.ability.effect.Execute(card, ctx, ca.ability.p1, ca.ability.p2, ca.ability.p3);
                ctx.temporaryBuff = false;
                ctx.grantKeyword = false;
                if (ca.ability.oncePerRound) card.MarkAbilityFired(_idx);
            }

            // ── TRANG BỊ (Equipment) abilities ────────────────────────────────────
            // Unit đang đeo trang bị → chạy CẢ skill/ability của trang bị, coi UNIT là source
            // (giống Aatrox: hiệu ứng vũ khí kích hoạt theo trigger của unit đeo).
            // Trước đây ExecuteMatching chỉ đọc card.data → skill trang bị không bao giờ fire.
            ExecuteEquipmentAbilities(card, trigger, ctx);

            // ── BONUS SKILLS từ TRANG BỊ / CỔ VẬT đã lắp (meta hub) ───────────────
            // Skill gắn qua ItemData.skills / RelicData.skills → chạy Y NHƯ skill gốc,
            // chỉ khi lá ĐANG Ở SÂN (bench/battlefield). Source = chính card này.
            if (card.bonusSkills != null && card.bonusSkills.Count > 0)
            {
                bool _bonusOnBoard = card.location == CardLocation.OnBench
                                  || card.location == CardLocation.OnBattlefield;
                if (_bonusOnBoard)
                    foreach (var bs in card.bonusSkills)
                        if (bs != null && bs.trigger == trigger)
                            bs.Execute(card, ctx);
            }

            // Level-up inline: xử lý cả TriggerCount và passive (SelfAttackReach, v.v.).
            // Chạy SAU tất cả abilities để skill của form hiện tại không bị trigger thêm lần nữa.
            CheckLevelUpInline(card, trigger, ctx.owner);
        }

        /// <summary>
        /// Chạy skills / abilities / conditionalAbilities của TRANG BỊ mà unit đang đeo,
        /// theo cùng trigger — source = unit (hiệu ứng tác động lên unit đeo).
        /// Index oncePerRound dùng offset lớn để không đụng với ability của chính unit.
        /// </summary>
        void ExecuteEquipmentAbilities(CardModel unit, SkillTrigger trigger, GameContext ctx)
        {
            var equip = unit.equipmentAttached;
            if (equip == null || equip.data == null) return;
            var d = equip.data;

            // 1) Skills legacy (trigger baked vào SkillData)
            if (d.skills != null)
                foreach (var skill in d.skills)
                    if (skill != null && skill.trigger == trigger)
                        skill.Execute(unit, ctx);

            // 2) Abilities (điều kiện + effect)
            if (d.abilities != null)
                for (int _e = 0; _e < d.abilities.Count; _e++)
                {
                    var ab = d.abilities[_e];
                    if (ab == null || ab.effect == null || ab.condition != trigger) continue;
                    if (!AbilityActiveAt(unit.location, ab.activeZone)) continue;
                    int _idx = 5000 + _e; // offset tránh trùng index oncePerRound của unit
                    if (ab.oncePerRound && unit.HasAbilityFiredThisRound(_idx)) continue;
                    ctx.temporaryBuff = ab.temporaryBuff;
                    ab.effect.Execute(unit, ctx, ab.p1, ab.p2, ab.p3);
                    ctx.temporaryBuff = false;
                    if (ab.oncePerRound) unit.MarkAbilityFired(_idx);
                }

            // 3) Conditional abilities (thêm điều kiện ATK/HP của unit đeo)
            if (d.conditionalAbilities != null)
                for (int _e = 0; _e < d.conditionalAbilities.Count; _e++)
                {
                    var ca = d.conditionalAbilities[_e];
                    if (ca == null || ca.ability == null || ca.ability.effect == null) continue;
                    if (ca.ability.condition != trigger) continue;
                    if (!AbilityActiveAt(unit.location, ca.ability.activeZone)) continue;
                    if (!unit.EvalCondition(ca.condition)) continue;
                    int _idx = 6000 + _e;
                    if (ca.ability.oncePerRound && unit.HasAbilityFiredThisRound(_idx)) continue;
                    ctx.temporaryBuff = ca.ability.temporaryBuff;
                    ca.ability.effect.Execute(unit, ctx, ca.ability.p1, ca.ability.p2, ca.ability.p3);
                    ctx.temporaryBuff = false;
                    if (ca.ability.oncePerRound) unit.MarkAbilityFired(_idx);
                }
        }

        void TriggerRoundSkills(SkillTrigger trigger)
        {
            foreach (var side in new[] { model.player, model.enemy })
            {
                var ctx = new GameContext(model, side, this);

                // Vực Thẳm (Deep): check deck ≤ 15 đầu round (tất cả unit ở mọi vùng)
                if (trigger == SkillTrigger.RoundStart)
                    CheckDeep(side);

                foreach (var card in side.BenchCards())
                {
                    // Reset hiệu ứng tạm thời (Fearsome debuff) đầu mỗi round
                    if (trigger == SkillTrigger.RoundStart)
                        card.ResetTempEffects();

                    ExecuteMatching(card, trigger, ctx);

                    // Hồi Phục (Regeneration): đầu round → hồi toàn bộ HP; +0|+1 nếu HP ≤ 50% trước hồi
                    if (trigger == SkillTrigger.RoundStart && card.HasKeyword(KeywordType.Regeneration))
                        ApplyRegeneration(card);
                }

                foreach (var card in side.BattlefieldCards())
                {
                    if (trigger == SkillTrigger.RoundStart)
                        card.ResetTempEffects();

                    ExecuteMatching(card, trigger, ctx);

                    if (trigger == SkillTrigger.RoundStart && card.HasKeyword(KeywordType.Regeneration))
                        ApplyRegeneration(card);
                }
            }

            // Sau khi TẤT CẢ skills của trigger này đã fire → check passive level-up.
            // Ví dụ: RoundStart buff ATK → SelfAttackReach champion đủ điều kiện lên cấp.
            EvaluatePassiveLevelUpsAtTrigger(trigger);
        }

        // ── Card Creation Triggers (GENERIC — thay 3 hệ thống cũ) ──
        /// <summary>
        /// Lấy giá trị counter hiện tại theo scope. SpellManaSpent luôn team (owner.spellManaSpentTotal).
        /// DamageDealt/DamageTaken hiện CHỈ hỗ trợ Self (card.damageDealtTotal/damageTakenTotal) —
        /// chưa có aggregate team cho 2 loại này (cần track delta ở nhiều điểm gây/nhận damage,
        /// để dành cho lần sau nếu cần). AttacksMade hỗ trợ ĐẦY ĐỦ cả Self lẫn Team.
        /// </summary>
        int GetCardCounterValue(CardModel card, PlayerModel owner, CardCreationTrigger t)
        {
            switch (t.counter)
            {
                case CardCounterType.SpellManaSpent:
                    return owner.spellManaSpentTotal;
                case CardCounterType.DamageDealt:
                    return card.damageDealtTotal; // Self only hiện tại
                case CardCounterType.DamageTaken:
                    return card.damageTakenTotal; // Self only hiện tại
                case CardCounterType.AttacksMade:
                    return t.scope == CounterScope.Team ? owner.attacksMadeTotal : card.attacksMadeTotal;
                default:
                    return 0;
            }
        }

        /// <summary>
        /// Quét TOÀN BỘ lá của owner (hand + bench + battlefield) tìm CardCreationTrigger khớp
        /// 'counter', so threshold, tạo bài nếu thỏa. Gọi sau MỌI sự kiện làm thay đổi counter đó
        /// (tiêu spell mana / gây damage / nhận damage / confirm tấn công).
        /// </summary>
        void CheckCardCreationTriggers(PlayerModel owner, CardCounterType counter)
        {
            var cards = new List<CardModel>(owner.hand);
            foreach (var c in owner.BenchCards()) cards.Add(c);
            foreach (var c in owner.BattlefieldCards()) cards.Add(c);

            foreach (var card in cards)
            {
                if (card.data.cardCreationTriggers == null) continue;
                for (int i = 0; i < card.data.cardCreationTriggers.Count; i++)
                {
                    var t = card.data.cardCreationTriggers[i];
                    if (t == null || t.counter != counter || t.cardToCreate == null || t.threshold <= 0) continue;

                    int value = GetCardCounterValue(card, owner, t);
                    int firstFire = t.firstAt > 0 ? t.firstAt : t.threshold;
                    int shouldFire;
                    if (value < firstFire) shouldFire = 0;
                    else if (!t.repeatEvery) shouldFire = 1;
                    else shouldFire = 1 + (value - firstFire) / t.threshold;

                    var key = (card.id, i);
                    int fired = _cardCreationFiredCount.TryGetValue(key, out var f) ? f : 0;
                    while (fired < shouldFire)
                    {
                        var created = owner.CreateCard(t.cardToCreate);
                        fired++;
                        if (created != null)
                            Debug.Log($"[CardCreation] {card.data.cardName}: {counter}({t.scope})={value} " +
                                      $"→ tạo '{t.cardToCreate.cardName}' lên hand (lần {fired}).");
                        else
                            Debug.Log($"[CardCreation] {card.data.cardName}: trigger thỏa nhưng hand đầy — bỏ.");
                    }
                    if (fired != f) _cardCreationFiredCount[key] = fired;
                }
            }
        }

        // ── Spell Mana Triggers ───────────────────────────────────
        /// <summary>Gọi sau mỗi lần tiêu spell mana. Kiểm tra CardCreationTrigger(counter=SpellManaSpent).</summary>
        void UpdateSpellCharges(PlayerModel p, int spellManaSpent)
        {
            if (spellManaSpent <= 0) return;
            // CantBlock giờ chỉ là "không thể chặn" (LoR) — bỏ cơ chế tích charge.
            CheckCardCreationTriggers(p, CardCounterType.SpellManaSpent);
        }

        // ── Damage Taken Triggers ─────────────────────────────────
        /// <summary>Kiểm tra CardCreationTrigger(counter=DamageTaken) sau mỗi lần nhận damage trong combat.</summary>
        void CheckDamageTakenTriggers(PlayerModel owner, CardModel card)
        {
            CheckCardCreationTriggers(owner, CardCounterType.DamageTaken);
        }

        // ── Damage Dealt Triggers (finisher Mydei) ────────────────
        /// <summary>Sau khi 1 unit gây damage xong: kiểm tra level-up damage-based + CardCreationTrigger(counter=DamageDealt).</summary>
        void CheckDamageDealtOutcome(PlayerModel owner, CardModel card)
        {
            if (card == null) return;
            CheckDamageDealtLevelUp(card, owner);   // transform trước (có thể lên form Ult)
            CheckCardCreationTriggers(owner, CardCounterType.DamageDealt); // rồi trigger của form mới thấy đúng tổng damage
        }

        // ── Attacks Made Triggers ──────────────────────────────────
        /// <summary>Gọi tại B6 sau khi confirm tấn công. Kiểm tra CardCreationTrigger(counter=AttacksMade).</summary>
        void CheckAttacksMadeTriggers(PlayerModel owner)
        {
            CheckCardCreationTriggers(owner, CardCounterType.AttacksMade);
        }

        /// <summary>Public: skill gây damage gọi để ghi nhận (AoE Mydei Lv2, finisher...).</summary>
        public void RegisterSkillDamage(CardModel dealer, int amount)
        {
            if (dealer == null || amount <= 0) return;
            dealer.AddDamageDealt(amount);
            CheckDamageDealtOutcome(P(dealer.belongsToPlayer), dealer);
        }
    }
}