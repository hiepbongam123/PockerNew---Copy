using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using LoRClone;
using LoRClone.Data;
using LoRClone.Model;

namespace LoRClone.Controller
{
    public partial class GameController
    {
        // ── Champion Level-Up ─────────────────────────────────────

        /// <summary>
        /// Trigger level-up cho champion card. Gọi từ SkillLevelUp hoặc bất kỳ skill nào
        /// cần level-up mà không muốn gọi CardModel.LevelUp() trực tiếp.
        ///
        /// Lý do dùng wrapper này thay vì CardModel.LevelUp() trực tiếp:
        /// - Đảm bảo Notify() được gọi → GameView và HUDView cập nhật ngay sau level-up.
        /// - Có thể thêm global VFX / sound / event log ở đây sau này mà không cần
        ///   sửa tất cả các SkillData.
        /// - CheckGameOver() sau level-up phòng trường hợp skill của form mới thắng ngay.
        /// </summary>
        public void TriggerLevelUp(CardModel card)
        {
            if (card == null || !card.data.canLevelUp || card.hasLeveledUp) return;
            StartCoroutine(TriggerLevelUpCoroutine(card));
        }

        IEnumerator TriggerLevelUpCoroutine(CardModel card)
        {
            _levelUpCinematicPlaying = true;

            // QUAN TRỌNG: bọc try/finally để _levelUpCinematicPlaying LUÔN được reset.
            // Nếu card.LevelUp()/FireTrigger/Notify/PlayAndWait ném exception mà không có finally,
            // flag sẽ kẹt = true vĩnh viễn → mọi WaitUntil(() => !_levelUpCinematicPlaying) trong
            // combat treo mãi + mọi action bị reject "Đang phát cinematic" → game "khóa hết".
            try
            {
                // Capture trước khi LevelUp() đổi card.data sang levelUpForm
                var clip = card.data?.levelUpCinematic;
                var levelUpData = card.data?.levelUpForm;   // CardData của form level-up để hiện trong cinematic
                var cardName = card.data?.cardName ?? "";
                var voiceLines = card.data?.levelUpVoiceLines;

                // Áp dụng TRƯỚC (burst speed — đúng theo LoR: transform tức thời, không ai respond được)
                card.LevelUp();   // fires OnLeveledUp → CardView.RefreshAll() + sfxLevelUp
                AudioManager.PlayCardEvent(CardAudioData.Ev.LevelUp, card.data?.audioData);   // thoại thăng cấp
                model.CheckGameOver();
                Debug.Log($"[LevelUp] {cardName} đã level up!");
                Notify(); // cập nhật view ngay với stats mới

                // Fire OnLevelUp trigger — dùng bởi SkillSummonCopy và các skill level-up khác
                // Chạy SAU LevelUp() để skill đọc đúng stats của form mới
                FireTrigger(card, SkillTrigger.OnLevelUp, card.belongsToPlayer);

                // Cinematic là visual-only — chạy sau khi stats đã áp dụng
                if (levelUpCinematicPlayer != null)
                    yield return StartCoroutine(levelUpCinematicPlayer.PlayAndWait(clip, levelUpData, cardName, voiceLines));
            }
            finally
            {
                _levelUpCinematicPlaying = false;
            }

            // Re-trigger enemy AI: Think() bị block trong cinematic nên _isThinking = false,
            // Notify() → OnStateChanged → EnemyAI.OnStateChanged → StartCoroutine(Think())
            Notify();
        }

        // ── Level-Up Inline System ────────────────────────────────
        //
        // Hai loại điều kiện:
        //   A. TriggerCount (đếm sự kiện) — xử lý trong CheckLevelUpInline,
        //      được gọi từ ExecuteMatching sau mỗi trigger.
        //   B. Passive (ngưỡng thụ động) — xử lý trong CheckPassiveLevelUp,
        //      được gọi từ EvaluatePassiveLevelUps (trước mỗi Notify).
        //
        // Location rule (LoR): progress tích lũy ở bất kỳ đâu (hand, deck, sân).
        // Transformation CHỈ xảy ra khi đang ở bench/battlefield.
        // Nếu đủ điều kiện trong tay/deck → MarkReadyToLevelUp → level up khi ra sân.

        /// <summary>
        /// Xử lý level-up inline cho mọi conditionType — gọi từ ExecuteMatching.
        /// ExecuteMatching chỉ gọi hàm này cho 1 card cụ thể (card đang fire trigger),
        /// nên đây là nơi xử lý các điều kiện CHỈ phụ thuộc vào card đó (Self*).
        ///
        /// Ví dụ setup trong Inspector:
        ///   TriggerCount    + OnAttack     → đếm số lần UNIT NÀY tấn công
        ///   SelfAttackReach + OnAttack     → check ATK của UNIT NÀY khi nó tấn công
        ///   SelfAttackReach + RoundStart   → check ATK đầu mỗi round (mặc định)
        ///   SelfAttackReach + OnKill       → check ATK ngay sau khi UNIT NÀY kill địch
        ///   AllyCountOnBoard + OnAllyPlayed → cần EvaluatePassiveLevelUpsAtTrigger (xem dưới)
        ///
        /// AllyCount/AllySum KHÔNG được xử lý ở đây — chúng phụ thuộc board state toàn cục,
        /// cần EvaluatePassiveLevelUpsAtTrigger() SAU khi toàn bộ effects đã fire.
        /// </summary>
        void CheckLevelUpInline(CardModel card, SkillTrigger trigger, PlayerModel owner)
        {
            if (!card.data.canLevelUp || card.hasLeveledUp) return;
            var cfg = card.data.levelUpConfig;
            if (cfg == null) return;
            if (cfg.trigger != trigger) return;

            switch (cfg.conditionType)
            {
                case LevelUpConditionType.TriggerCount:
                    // Cổng tag (VD Shyvana chỉ tính khi có 'Dragon'): rỗng = không cổng.
                    if (!string.IsNullOrEmpty(cfg.requiredTag) && !OwnerControlsTag(owner, cfg.requiredTag))
                        break;
                    card.AddLevelUpProgress(1);
                    Debug.Log($"[LevelUp] {card.data.cardName} tiến triển: {card.levelUpProgress}/{cfg.threshold}");
                    if (card.levelUpProgress >= cfg.threshold)
                        ApplyLevelUpOrMark(card);
                    break;

                // Self-based passives: chỉ cần check card chính nó, không phụ thuộc card khác.
                // Xử lý ở đây (per-card) thay vì EvaluatePassiveLevelUpsAtTrigger (all-cards)
                // để đảm bảo CHỈ card đang fire trigger (VD: đang tấn công) mới được check.
                case LevelUpConditionType.SelfAttackReach:
                    if (card.currentAttack >= cfg.threshold)
                        ApplyLevelUpOrMark(card);
                    break;

                case LevelUpConditionType.SelfHealthReach:
                    if (card.currentHealth >= cfg.threshold)
                        ApplyLevelUpOrMark(card);
                    break;

                    // AllyCount/AllySum: bỏ qua ở đây, xử lý trong EvaluatePassiveLevelUpsAtTrigger.
            }
        }

        /// <summary>
        /// Helper nội bộ: đánh giá điều kiện passive tại thời điểm gọi.
        /// Gọi từ CheckLevelUpInline (khi trigger khớp), hoặc từ EvaluatePassiveLevelUps (sweep toàn sân).
        /// </summary>
        void CheckPassiveLevelUp(CardModel card, PlayerModel owner)
        {
            if (!card.data.canLevelUp || card.hasLeveledUp) return;
            var cfg = card.data.levelUpConfig;
            if (cfg == null || cfg.conditionType == LevelUpConditionType.TriggerCount) return;

            bool conditionMet = false;
            switch (cfg.conditionType)
            {
                case LevelUpConditionType.SelfAttackReach:
                    conditionMet = card.currentAttack >= cfg.threshold;
                    break;

                case LevelUpConditionType.SelfHealthReach:
                    conditionMet = card.currentHealth >= cfg.threshold;
                    break;

                case LevelUpConditionType.AllyCountOnBoard:
                    {
                        int count = 0;
                        foreach (var c in owner.BenchCards())
                            if (c != card && c.IsAlive) count++;
                        foreach (var c in owner.BattlefieldCards())
                            if (c != card && c.IsAlive) count++;
                        conditionMet = count >= cfg.threshold;
                        break;
                    }

                case LevelUpConditionType.AllyTagCountOnBoard:
                    {
                        int count = 0;
                        foreach (var c in owner.BenchCards())
                            if (c != card && c.IsAlive && c.HasTag(cfg.requiredTag)) count++;
                        foreach (var c in owner.BattlefieldCards())
                            if (c != card && c.IsAlive && c.HasTag(cfg.requiredTag)) count++;
                        conditionMet = count >= cfg.threshold;
                        break;
                    }

                case LevelUpConditionType.AllySumAttack:
                    {
                        int sum = 0;
                        foreach (var c in owner.BenchCards()) if (c.IsAlive) sum += c.currentAttack;
                        foreach (var c in owner.BattlefieldCards()) if (c.IsAlive) sum += c.currentAttack;
                        conditionMet = sum >= cfg.threshold;
                        break;
                    }

                case LevelUpConditionType.AllySumHealth:
                    {
                        int sum = 0;
                        foreach (var c in owner.BenchCards()) if (c.IsAlive) sum += c.currentHealth;
                        foreach (var c in owner.BattlefieldCards()) if (c.IsAlive) sum += c.currentHealth;
                        conditionMet = sum >= cfg.threshold;
                        break;
                    }
            }

            if (conditionMet)
                ApplyLevelUpOrMark(card);
        }

        /// <summary>
        /// Kiểm tra passive level-up SAU KHI toàn bộ effects của <paramref name="trigger"/> đã fire xong.
        /// Chỉ check card có cfg.trigger == trigger → đúng timing, không nhảy điều kiện sai pha.
        ///
        /// Gọi sau vòng lặp attacker (OnAttack), sau TriggerRoundSkills (RoundStart/End),
        /// sau FireTrigger batch khác (OnAllyPlayed, OnAllyDeath...).
        ///
        /// Lý do tách khỏi CheckLevelUpInline: ExecuteMatching chạy per-card → effects của
        /// card khác chưa fire hết → passive check mid-loop cho kết quả sai.
        /// </summary>
        void EvaluatePassiveLevelUpsAtTrigger(SkillTrigger trigger)
        {
            if (_evaluatingPassiveLevelUps) return;
            _evaluatingPassiveLevelUps = true;
            try
            {
                foreach (var side in new[] { model.player, model.enemy })
                {
                    var cards = new List<CardModel>();
                    foreach (var c in side.BenchCards()) cards.Add(c);
                    foreach (var c in side.BattlefieldCards()) cards.Add(c);
                    foreach (var c in side.hand) cards.Add(c);

                    foreach (var card in cards)
                    {
                        if (!card.data.canLevelUp || card.hasLeveledUp) continue;
                        var cfg = card.data.levelUpConfig;
                        if (cfg == null) continue;
                        // TriggerCount: xử lý per-card trong CheckLevelUpInline.
                        if (cfg.conditionType == LevelUpConditionType.TriggerCount) continue;
                        // Self-based passives: xử lý per-card trong CheckLevelUpInline —
                        // để chỉ card đang fire trigger (VD: đang tấn công) mới được check ATK của nó.
                        // Nếu để ở đây, MỌI unit đủ ATK sẽ lên lv khi BẤT KỲ unit nào tấn công.
                        if (cfg.conditionType == LevelUpConditionType.SelfAttackReach) continue;
                        if (cfg.conditionType == LevelUpConditionType.SelfHealthReach) continue;
                        if (cfg.trigger != trigger) continue;
                        CheckPassiveLevelUp(card, side);
                    }
                }
            }
            finally { _evaluatingPassiveLevelUps = false; }
        }

        /// <summary>
        /// Sweep toàn sân và kiểm tra passive level-up cho tất cả champion.
        /// Dùng khi cần force-check không qua trigger (ví dụ: sau khi load game, sau khi play card).
        /// Không cần gọi thường xuyên — EvaluatePassiveLevelUpsAtTrigger xử lý tại trigger point.
        /// Guard _evaluatingPassiveLevelUps tránh recursion: TriggerLevelUp → EvaluatePassive → loop.
        /// </summary>
        void EvaluatePassiveLevelUps()
        {
            if (_evaluatingPassiveLevelUps) return;
            _evaluatingPassiveLevelUps = true;
            try
            {
                foreach (var side in new[] { model.player, model.enemy })
                {
                    // Snapshot để tránh modify-while-iterate (TriggerLevelUp thay đổi data card)
                    var cards = new List<CardModel>();
                    foreach (var c in side.BenchCards()) cards.Add(c);
                    foreach (var c in side.BattlefieldCards()) cards.Add(c);
                    // Hand: không level up nhưng cần set readyToLevelUp nếu đủ điều kiện
                    foreach (var c in side.hand) cards.Add(c);

                    foreach (var card in cards)
                        CheckPassiveLevelUp(card, side);
                }
            }
            finally
            {
                _evaluatingPassiveLevelUps = false;
            }
        }

        /// <summary>
        /// Áp dụng level-up theo location rule:
        ///   Đang ở sân (bench/battlefield) → TriggerLevelUp() ngay.
        ///   Đang ở tay/deck → MarkReadyToLevelUp() (level up khi ra sân).
        /// </summary>
        /// <summary>Level-up dựa trên tổng damage unit đã gây (DamageDealtByThis — Mydei 10/35).</summary>
        void CheckDamageDealtLevelUp(CardModel card, PlayerModel owner)
        {
            if (card == null || !card.data.canLevelUp || card.hasLeveledUp) return;
            var cfg = card.data.levelUpConfig;
            if (cfg == null || cfg.conditionType != LevelUpConditionType.DamageDealtByThis) return;
            if (card.damageDealtTotal >= cfg.threshold)
                ApplyLevelUpOrMark(card);
        }

        void ApplyLevelUpOrMark(CardModel card)
        {
            bool isOnBoard = card.location == CardLocation.OnBench
                          || card.location == CardLocation.OnBattlefield;
            if (isOnBoard)
                TriggerLevelUp(card);
            else
            {
                card.MarkReadyToLevelUp();
                Debug.Log($"[LevelUp] {card.data.cardName} đủ điều kiện nhưng chưa ở sân — sẽ level up khi triệu hồi.");
            }
        }

        /// <summary>True nếu owner có ≥1 đồng minh (bench/battlefield) mang tag này. Dùng cổng level-up.</summary>
        bool OwnerControlsTag(PlayerModel owner, string tag)
        {
            foreach (var c in owner.BenchCards()) if (c.IsAlive && c.HasTag(tag)) return true;
            foreach (var c in owner.BattlefieldCards()) if (c.IsAlive && c.HasTag(tag)) return true;
            return false;
        }
    }
}