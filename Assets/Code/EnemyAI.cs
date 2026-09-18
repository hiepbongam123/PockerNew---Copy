using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using LoRClone.Controller;
using LoRClone.Model;
using LoRClone.Data;

namespace LoRClone.AI
{
    /// <summary>
    /// AI đơn giản cho enemy. Gắn lên bất kỳ GameObject nào trong scene.
    ///
    /// Logic:
    ///   Mulligan    — giữ tất cả bài, không swap.
    ///   Main        — play unit rẻ nhất → play equipment → cast Burst → stage Fast/Slow → tấn công.
    ///   Attack      — đưa tất cả unit bench ra tấn công.
    ///   Block       — favorable trade → even trade → sacrifice nhỏ nhất.
    ///   Spell target:
    ///     EnemyUnit → unit HP thấp nhất (ưu tiên kill)
    ///     AllyUnit  → unit ATK cao nhất (buff kẻ mạnh)
    ///     AnyUnit   → enemy unit HP thấp nhất (offensive default)
    ///   Equipment target:
    ///     Equip mode  → ally ATK cao nhất (tăng sức mạnh cho unit mạnh nhất)
    ///     Darkin mode → tự động bởi EquipmentModeView (ưu tiên Darkin nếu bench còn chỗ)
    ///   UnitSkill target:
    ///     Dùng lại logic PickSpellTargets — không treo game khi unit có skill cần target.
    ///
    /// ★ FIX (token logic): điều kiện tấn công/chặn trước đây bị ĐẢO NGƯỢC theo attack token
    ///   → AI chỉ ra quân + dùng bài phép, không bao giờ tấn công hay chặn. Đã sửa:
    ///     • MainPhase: chỉ tấn công khi CÓ token  → if (IHaveToken(m) && !attackTokenUsed)
    ///     • Think(Attack/BlockDeclare): chỉ đi CHẶN khi KHÔNG có token → if (!IHaveToken(m))
    ///
    /// ★ FIX (PvP): mọi callback targeting đều CHẶN khi networkMode — trong PvP không được để AI
    ///   can thiệp (2 người thật đánh nhau). Nếu AI tự chọn target/mode → 2 máy lệch → desync.
    ///   (OnStateChanged đã có guard networkMode sẵn; bổ sung cho các callback còn lại.)
    /// </summary>
    public class EnemyAI : MonoBehaviour
    {
        [Header("Timing")]
        [Tooltip("Giây AI 'suy nghĩ' trước khi bắt đầu hành động")]
        public float thinkDelay = 0.6f;
        [Tooltip("Giây giữa mỗi hành động của AI")]
        public float actionDelay = 0.7f;

        GameController _gc;
        bool _isThinking;
        bool _mulliganDone;

        [Header("Phe điều khiển")]
        [Tooltip("false = AI đánh ENEMY (mặc định). true = AI đánh PLAYER (dùng cho auto-play).")]
        public bool controlsPlayer = false;

        // ── Side-aware helpers ──
        LoRClone.Model.PlayerModel Me(GameModel g) => controlsPlayer ? g.player : g.enemy;
        LoRClone.Model.PlayerModel Opp(GameModel g) => controlsPlayer ? g.enemy : g.player;
        bool HasPriority(GameModel g) => g.isPlayerPriority == controlsPlayer;
        bool IHaveToken(GameModel g) => g.playerHasAttackToken == controlsPlayer;
        // Enemy AI luôn bật; Player AI chỉ bật khi playerAutoPlay ON.
        bool AutoActive => !controlsPlayer || (_gc != null && _gc.playerAutoPlay);
        // PvP: AI phải tắt hoàn toàn (2 người thật đánh).
        bool NetOff => _gc != null && _gc.networkMode;

        // ── Lifecycle ────────────────────────────────────────────────
        void Start()
        {
            _gc = GameController.Instance;
            if (_gc == null) { Debug.LogWarning("[EnemyAI] Không tìm thấy GameController!"); return; }

            _gc.OnStateChanged += OnStateChanged;

            // Equipment mode: AI tự chọn Darkin hay Trang Bị dựa trên mana thực tế.
            // Subscribe OnEquipmentNeedsEnemyMode (không phải OnEquipmentNeedsMode) —
            // GameView KHÔNG nhận event này → không hiện panel cho player khi enemy chọn mode.
            _gc.OnEquipmentNeedsEnemyMode += HandleEquipmentModeNeeded;

            // Equipment target: AI tự chọn ally khi mode Trang Bị.
            // Subscribe OnEquipmentNeedsEnemyTarget (không phải OnEquipmentNeedsTarget) —
            // GameView KHÔNG nhận event này → không hiện arrow trên cursor player.
            _gc.OnEquipmentNeedsEnemyTarget += HandleEquipmentTargetNeeded;

            // UnitSkill: nếu không subscribe, _isUnitSkillTargeting = true mãi → game treo.
            _gc.OnUnitSkillNeedsTarget += HandleUnitSkillTargetNeeded;
        }

        void OnDestroy()
        {
            if (_gc == null) return;
            _gc.OnStateChanged -= OnStateChanged;
            _gc.OnEquipmentNeedsEnemyMode -= HandleEquipmentModeNeeded;
            _gc.OnEquipmentNeedsEnemyTarget -= HandleEquipmentTargetNeeded;
            _gc.OnUnitSkillNeedsTarget -= HandleUnitSkillTargetNeeded;
        }

        // ── State listener ───────────────────────────────────────────
        void OnStateChanged(GameModel _)
        {
            if (NetOff) return;                          // PvP: tắt AI, người thật đánh
            if (!AutoActive) return;                     // Player AI chỉ chạy khi AUTO ON
            if (_isThinking) return;
            if (_gc.IsBusy) return;                       // đang combat/spell resolve/summon → đợi RaiseStateChanged kế tiếp
            var m = _gc.model;
            if (m == null || m.phase == GamePhase.GameOver) return;

            bool needAct = (m.phase == GamePhase.Mulligan && !_mulliganDone)
                        || (HasPriority(m) && m.phase != GamePhase.Mulligan);
            if (!needAct) return;

            StartCoroutine(Think());
        }

        // ── Main decision loop ────────────────────────────────────────
        IEnumerator Think()
        {
            _isThinking = true;
            yield return new WaitForSeconds(thinkDelay);

            // Đợi cinematic level-up VÀ mọi coroutine hệ thống khác (combat resolve, spell stack
            // resolve, summon sequence...) kết thúc trước khi act. TRƯỚC ĐÂY chỉ đợi isCinematicPlaying
            // → AI cứ thử PassPriorityAction trong lúc combat đang resolve → bị Reject liên tục → spam
            // toast trên UI (mỗi RaiseStateChanged trong lúc resolve lại kích OnStateChanged → Think() mới).
            while (_gc.isCinematicPlaying || _gc.IsBusy)
                yield return new WaitForSeconds(0.1f);

            var m = _gc.model;
            if (m == null || m.phase == GamePhase.GameOver) { _isThinking = false; yield break; }

            if (m.phase == GamePhase.Mulligan)
            {
                if (!_mulliganDone) DoMulligan();
            }
            else if (HasPriority(m))
            {
                switch (m.phase)
                {
                    case GamePhase.PlayerPriority:
                    case GamePhase.EnemyPriority:
                        yield return StartCoroutine(MainPhase());
                        break;

                    case GamePhase.AttackDeclare:
                    case GamePhase.BlockDeclare:
                        // ★ FIX: bên KHÔNG có token = bên phòng thủ → mới là bên đi CHẶN.
                        if (!IHaveToken(m))
                        {
                            if (m.phase == GamePhase.AttackDeclare)
                            {
                                yield return StartCoroutine(PlayUnits());
                                yield return StartCoroutine(CastSpells());
                            }
                            yield return StartCoroutine(BlockPhase());
                        }
                        else
                        {
                            if (m.phase == GamePhase.AttackDeclare)
                            {
                                yield return StartCoroutine(PlayUnits());
                                yield return StartCoroutine(CastSpells());
                            }
                            Pass();
                        }
                        break;

                    default:
                        Pass();
                        break;
                }
            }

            _isThinking = false;

            var final = _gc.model;
            if (final == null || final.phase == GamePhase.GameOver) yield break;
            bool stillNeedAct = (final.phase == GamePhase.Mulligan && !_mulliganDone)
                             || (HasPriority(final) && final.phase != GamePhase.Mulligan);
            if (stillNeedAct) OnStateChanged(final);
        }

        // ── Mulligan ──────────────────────────────────────────────────
        void DoMulligan()
        {
            _mulliganDone = true;
            _gc.HandleMulliganConfirm(controlsPlayer, new List<CardModel>()); // giữ tất cả
        }

        // ── Main Phase ────────────────────────────────────────────────
        IEnumerator MainPhase()
        {
            bool summonedDirectToAttack = false;

            // Bước 1: play unit (greedy — rẻ nhất trước)
            yield return StartCoroutine(PlayUnits());

            {
                var m = _gc.model;
                if (m.phase == GamePhase.AttackDeclare)
                    summonedDirectToAttack = true;
            }

            // Bước 2: play equipment (chỉ main phase)
            if (!summonedDirectToAttack)
                yield return StartCoroutine(PlayEquipments());

            // Bước 3: cast Burst / stage Fast/Slow
            if (!summonedDirectToAttack)
                yield return StartCoroutine(CastSpells());

            // Bước 4: tấn công hoặc pass
            {
                var m = _gc.model;
                if (m.phase == GamePhase.GameOver || !HasPriority(m)) yield break;

                // ★ FIX: chỉ tấn công khi CÓ attack token (và token chưa dùng).
                if (IHaveToken(m) && !m.attackTokenUsed)
                {
                    if (summonedDirectToAttack)
                    {
                        yield return new WaitForSeconds(actionDelay * 0.5f);
                        Pass();
                    }
                    else
                        yield return StartCoroutine(AttackPhase());
                }
                else
                    Pass();
            }
        }

        // ── Play Units ────────────────────────────────────────────────
        IEnumerator PlayUnits()
        {
            bool played = true;
            while (played)
            {
                played = false;
                var m = _gc.model;
                if (m.phase == GamePhase.GameOver || !HasPriority(m)) yield break;

                bool canPlay = m.phase == GamePhase.PlayerPriority
                            || m.phase == GamePhase.EnemyPriority
                            || m.phase == GamePhase.AttackDeclare;
                if (!canPlay) yield break;
                if (!m.spellStack.IsEmpty) yield break; // FIX: stack có spell → không triệu hồi (tránh lặp reject)
                if (!Me(m).BenchHasSpace) yield break;

                int mana = AvailableMana(m, Me(m));
                CardModel pick = null;
                foreach (var card in new List<CardModel>(Me(m).hand))
                {
                    if (card.data.cardType != CardType.Unit) continue;
                    if (card.currentManaCost > mana) continue;
                    if (pick == null || card.currentManaCost < pick.currentManaCost)
                        pick = card;
                }

                if (pick != null)
                {
                    _gc.HandleAction(new PlayUnitAction(controlsPlayer, pick));
                    played = true;
                    yield return new WaitForSeconds(actionDelay);
                }
            }
        }

        // ── Play Equipments ───────────────────────────────────────────
        /// <summary>
        /// Chơi Equipment card từ tay AI.
        /// Mode được chọn tự động bởi EquipmentModeView (Darkin nếu bench còn chỗ, ngược lại Equip).
        /// Ally target (cho Equip mode) được xử lý bởi HandleEquipmentTargetNeeded.
        ///
        /// Ưu tiên chọn lá Equipment có chi phí hiệu quả thấp nhất:
        ///   • Darkin mode: tính theo darkinUnit.manaCost
        ///   • Equip mode:  tính theo card.currentManaCost
        /// </summary>
        IEnumerator PlayEquipments()
        {
            bool played = true;
            while (played)
            {
                played = false;
                var m = _gc.model;
                if (m.phase == GamePhase.GameOver || !HasPriority(m)) yield break;

                bool canPlay = m.phase == GamePhase.PlayerPriority
                            || m.phase == GamePhase.EnemyPriority;
                if (!canPlay) yield break;
                if (!m.spellStack.IsEmpty) yield break; // FIX: giống PlayUnits — tránh lặp reject

                // Trang Bị (weapon) dùng spell mana trước → tổng spellMana + mana thường.
                // Darkin (unit) CHỈ dùng mana thường — tách riêng biến regularMana cho nó.
                int mana = Me(m).spellMana + Me(m).mana - Me(m).TotalStagedManaCost();
                int regularMana = AvailableMana(m, Me(m));
                bool hasBenchSpace = Me(m).BenchHasSpace;

                // Kiểm tra có ally để trang bị không
                bool hasAlly = false;
                foreach (var c in Me(m).BenchCards()) { hasAlly = true; break; }
                if (!hasAlly)
                    foreach (var c in Me(m).BattlefieldCards()) { hasAlly = true; break; }

                CardModel pick = null;
                int bestCost = int.MaxValue;

                foreach (var card in new List<CardModel>(Me(m).hand))
                {
                    if (card.data.cardType != CardType.Equipment) continue;

                    bool hasDarkin = card.data.darkinUnit != null;
                    // "Đã chơi round này" chỉ khoá lại mode Trang Bị (chống re-equip miễn phí
                    // sau khi unit đeo nó chết) — không khoá Darkin, 2 mode không mâu thuẫn.
                    // Card thuần weapon (không có Darkin) thì đã dùng rồi là hết đường, bỏ qua hẳn.
                    bool alreadyPlayed = _gc.IsEquipmentPlayedThisRound(card.id);
                    if (alreadyPlayed && !hasDarkin) continue;

                    int darkinCost = hasDarkin ? card.data.darkinUnit.manaCost : int.MaxValue;
                    int equipCost = card.currentManaCost;

                    bool canDarkin = hasDarkin && hasBenchSpace && regularMana >= darkinCost;
                    bool canEquip = !alreadyPlayed && hasAlly && mana >= equipCost;

                    if (!canDarkin && !canEquip) continue; // không đủ điều kiện cho cả 2 mode

                    // Chi phí hiệu quả: ưu tiên Darkin nếu bench còn chỗ (giống EquipmentModeView)
                    int effectiveCost = canDarkin ? darkinCost : equipCost;
                    if (effectiveCost < bestCost)
                    {
                        pick = card;
                        bestCost = effectiveCost;
                    }
                }

                if (pick != null)
                {
                    _gc.HandleAction(new PlayEquipmentAction(pick, controlsPlayer));
                    played = true;
                    yield return new WaitForSeconds(actionDelay);
                }
            }
        }

        // ── Equipment: chọn mode (Darkin vs Trang Bị) ───────────────
        /// <summary>
        /// Callback khi EquipmentSequence hỏi AI muốn chơi Darkin hay Trang Bị.
        /// AI tự kiểm tra mana + điều kiện trước khi trả lời.
        /// Subscribe OnEquipmentNeedsEnemyMode — không có bool isPlayer (luôn là enemy).
        ///
        /// Callback int: 0 = Trang Bị, 1 = Darkin, -1 = hủy
        /// </summary>
        void HandleEquipmentModeNeeded(CardModel card, System.Action<int> callback)
        {
            if (NetOff) return; // ★ PvP: AI không chọn mode (người thật + relay lo).
            if (!AutoActive) return; // ★ PlayerAI đang TẮT (playerAutoPlay=false) → không tự quyết hộ người chơi.

            var m = _gc.model;
            // Trang Bị dùng spell mana trước (pool gộp) — Darkin CHỈ dùng mana thường.
            int mana = Me(m).spellMana + Me(m).mana - Me(m).TotalStagedManaCost();
            int regularMana = AvailableMana(m, Me(m));

            bool hasDarkin = card.data.darkinUnit != null;
            int darkinCost = hasDarkin ? card.data.darkinUnit.manaCost : int.MaxValue;
            bool hasBenchSpace = Me(m).BenchHasSpace;
            bool canDarkin = hasDarkin && hasBenchSpace && regularMana >= darkinCost;

            bool hasAlly = false;
            foreach (var c in Me(m).BenchCards()) { hasAlly = true; break; }
            if (!hasAlly)
                foreach (var c in Me(m).BattlefieldCards()) { hasAlly = true; break; }
            bool canEquip = hasAlly && mana >= card.currentManaCost;

            if (canDarkin)
            {
                Debug.Log($"[EnemyAI] Equipment '{card.data.cardName}' → chọn Darkin (cost {darkinCost}, mana {mana}).");
                callback?.Invoke(1);
            }
            else if (canEquip)
            {
                Debug.Log($"[EnemyAI] Equipment '{card.data.cardName}' → chọn Trang Bị (cost {card.currentManaCost}, mana {mana}).");
                callback?.Invoke(0);
            }
            else
            {
                Debug.LogWarning($"[EnemyAI] Equipment '{card.data.cardName}' → không đủ điều kiện, hủy.");
                callback?.Invoke(-1);
            }
        }

        // ── Equipment: chọn ally target (mode Trang Bị) ──────────────
        /// <summary>
        /// Callback khi EquipmentSequence cần chọn ally để đeo equipment.
        /// Subscribe OnEquipmentNeedsEnemyTarget — không có bool isPlayer (luôn là enemy).
        /// GameView không nhận event này → không hiện arrow trên cursor player.
        /// Chiến lược: chọn ally có ATK cao nhất để tối đa hóa buff.
        /// </summary>
        void HandleEquipmentTargetNeeded(CardModel equipment, System.Action<CardModel> callback)
        {
            if (NetOff) return; // ★ PvP: AI không chọn target.
            if (!AutoActive) return; // ★ PlayerAI đang TẮT → nhường người chơi tự chọn.

            var m = _gc.model;
            CardModel best = null;

            // Tìm ally ATK cao nhất (bench + battlefield)
            foreach (var c in Me(m).BenchCards())
                if (c.IsAlive && (best == null || c.effectiveAttack > best.effectiveAttack))
                    best = c;
            foreach (var c in Me(m).BattlefieldCards())
                if (c.IsAlive && (best == null || c.effectiveAttack > best.effectiveAttack))
                    best = c;

            if (best == null)
                Debug.LogWarning("[EnemyAI] Không tìm được ally để trang bị — hủy Equip.");

            callback?.Invoke(best); // null = hủy → card giữ lại trong tay
        }

        // ── UnitSkill: chọn target cho skill của unit vừa ra sân ─────
        /// <summary>
        /// Callback khi unit AI ra sân có UnitSkill cần chọn target.
        /// Chỉ xử lý isPlayer=false; GameView xử lý isPlayer=true.
        /// Dùng lại PickSpellTargets — logic giống targeting spell.
        /// Nếu skill không cần target (requiresTarget=false) → trả về list rỗng ngay.
        ///
        /// ★ PvP: bỏ qua hoàn toàn — GameController.StageUnitSkillSpell đã tự AutoPickTargets khi
        ///   networkMode (giống hệt 2 máy), không đi qua event này.
        /// </summary>
        void HandleUnitSkillTargetNeeded(
            CardModel unit,
            CardModel skillCard,
            bool isPlayer,
            System.Action<List<CardModel>> callback)
        {
            if (NetOff) return; // ★ PvP: AI không can thiệp targeting.
            // ★ FIX (bug lớn): PlayerAI đang TẮT (playerAutoPlay=false) thì KHÔNG được tự chọn target
            //   hộ người chơi. Trước đây thiếu check này → PlayerAI (controlsPlayer=true) vẫn auto-pick
            //   trong khi GameView đã mở mũi tên cho người chơi → chọn khác là lỗi (callback gọi 2 lần).
            //   EnemyAI thật (controlsPlayer=false) có AutoActive luôn = true nên KHÔNG bị ảnh hưởng.
            if (!AutoActive) return;
            if (isPlayer != controlsPlayer) return; // chỉ xử phe MÌNH; phe kia do GameView/AI kia

            // Không cần target (skill tự kích hoạt)
            if (skillCard?.data == null || !skillCard.data.requiresTarget)
            {
                callback?.Invoke(new List<CardModel>());
                return;
            }

            var m = _gc.model;
            var targets = PickSpellTargets(skillCard.data, m);

            if (targets == null)
            {
                Debug.LogWarning($"[EnemyAI] UnitSkill '{skillCard.data.cardName}': " +
                                 "không tìm được target hợp lệ — hủy skill.");
                callback?.Invoke(new List<CardModel>()); // trả empty → GameController xử lý fallback
            }
            else
            {
                callback?.Invoke(targets);
            }
        }

        // ── Cast Spells ───────────────────────────────────────────────
        IEnumerator CastSpells()
        {
            // 1. Burst spells
            bool cast = true;
            while (cast)
            {
                cast = false;
                var m = _gc.model;
                if (m.phase == GamePhase.GameOver || !HasPriority(m)) yield break;

                foreach (var card in new List<CardModel>(Me(m).hand))
                {
                    if (card.data.cardType != CardType.Spell) continue;
                    if (card.data.spellSpeed != SpellSpeed.Burst) continue;
                    if (!Me(m).CanAffordSpell(card.currentManaCost)) continue;

                    if (card.data.requiresTarget)
                    {
                        var targets = PickSpellTargets(card.data, m);
                        if (targets == null) continue;
                        _gc.SetSpellTargets(card, targets);
                    }

                    _gc.HandleAction(new CastSpellAction(controlsPlayer, card));
                    yield return new WaitForSeconds(actionDelay);
                    // Chống spam: CHỈ lặp lại vòng while khi cast THÀNH CÔNG (lá đã rời tay).
                    // Nếu bị Reject mà lá vẫn nằm trong tay → không set cast=true → thoát,
                    // tránh reject lặp vô hạn trên cùng 1 lá.
                    if (!Me(_gc.model).hand.Contains(card)) cast = true;
                    break;
                }
            }

            // 2. Fast / Slow spells: stage RỒI COMMIT NGAY (1 lá / lượt).
            //
            // ★ FIX (bug cơ chế): TRƯỚC ĐÂY chỉ stage rồi để đó, không commit —
            //   lá nằm ở trạng thái StagingSpell (úp, CHƯA trừ mana, CHƯA lên stack) và
            //   chỉ được CommitStagedSpells "vô tình" gọi ở lượt Pass sau. Hậu quả:
            //     • Enemy: lá phép úp + chưa trừ mana → phải confirm thêm 1 vòng mới hiện.
            //     • Slow spell stage ở main phase bị kéo sang bước tấn công/combat rồi mới
            //       commit tại đó → Slow bị cast TRONG combat như Fast (sai luật).
            //   Fix: chốt cast NGAY sau khi stage (Pass → CommitStagedSpells đẩy lên stack,
            //   trừ mana, lật ngửa, mở response window), rồi nhường lượt. Think() chạy lại
            //   khi priority quay về (spell resolve xong) để chơi tiếp / tấn công.
            {
                var m = _gc.model;
                if (m.phase == GamePhase.GameOver || !HasPriority(m)) yield break;

                foreach (var card in new List<CardModel>(Me(m).hand))
                {
                    m = _gc.model;
                    if (m.phase == GamePhase.GameOver || !HasPriority(m)) yield break;

                    if (card.data.cardType != CardType.Spell) continue;
                    if (card.data.spellSpeed == SpellSpeed.Burst) continue;

                    // Slow: CHỈ được cast trong main phase (đúng luật LoR). Fast: main + combat window.
                    bool inMain = m.phase == GamePhase.PlayerPriority
                               || m.phase == GamePhase.EnemyPriority;
                    if (card.data.spellSpeed == SpellSpeed.Slow && !inMain) continue;

                    // Chỉ stage khi lượt Pass sắp tới CHẮC CHẮN đi qua CommitStagedSpells.
                    // Ở AttackDeclare với chính mình cầm token, Pass rơi vào nhánh "xác nhận tấn công"
                    // → KHÔNG commit → lá lại dangling. Bỏ qua để không tái tạo bug.
                    if (m.phase == GamePhase.AttackDeclare && IHaveToken(m)) continue;

                    int available = Me(m).spellMana + Me(m).mana - Me(m).TotalStagedManaCost();
                    if (available < card.currentManaCost) continue;

                    if (card.data.requiresTarget)
                    {
                        var targets = PickSpellTargets(card.data, m);
                        if (targets == null) continue;
                        _gc.SetSpellTargets(card, targets);
                    }

                    _gc.HandleAction(new StageSpellAction(controlsPlayer, card));
                    yield return new WaitForSeconds(actionDelay * 0.5f);

                    // Stage bị từ chối → thử lá khác, không để kẹt.
                    if (!Me(_gc.model).stagedSpells.Contains(card)) continue;

                    // Chốt cast: Pass → CommitStagedSpells (lên stack + trừ mana + lật ngửa).
                    Pass();
                    yield return new WaitForSeconds(actionDelay);

                    // Đã cast 1 spell + hand-off priority sang đối thủ (response window).
                    // Dừng lượt tại đây; Think() sẽ tiếp tục khi priority quay lại.
                    yield break;
                }
            }
        }

        // ── Spell target selection ────────────────────────────────────
        List<CardModel> PickSpellTargets(CardData spell, GameModel m)
        {
            var result = new List<CardModel>();
            for (int step = 0; step < spell.targetCount; step++)
            {
                var type = spell.GetTargetTypeForStep(step);
                var candidates = GetSpellCandidates(type, m, result);
                if (candidates.Count == 0) return null;

                var pick = PickBestSpellTarget(type, candidates);
                if (pick == null) return null;
                result.Add(pick);
            }
            return result;
        }

        List<CardModel> GetSpellCandidates(TargetType type, GameModel m, List<CardModel> exclude)
        {
            var list = new List<CardModel>();
            switch (type)
            {
                case TargetType.EnemyUnit:
                    foreach (var c in Opp(m).BenchCards())
                        if (c.IsAlive && !exclude.Contains(c)) list.Add(c);
                    foreach (var c in Opp(m).BattlefieldCards())
                        if (c.IsAlive && !exclude.Contains(c)) list.Add(c);
                    break;

                case TargetType.AllyUnit:
                    foreach (var c in Me(m).BenchCards())
                        if (c.IsAlive && !exclude.Contains(c)) list.Add(c);
                    foreach (var c in Me(m).BattlefieldCards())
                        if (c.IsAlive && !exclude.Contains(c)) list.Add(c);
                    break;

                case TargetType.AnyUnit:
                    foreach (var c in Opp(m).BenchCards())
                        if (c.IsAlive && !exclude.Contains(c)) list.Add(c);
                    foreach (var c in Opp(m).BattlefieldCards())
                        if (c.IsAlive && !exclude.Contains(c)) list.Add(c);
                    foreach (var c in Me(m).BenchCards())
                        if (c.IsAlive && !exclude.Contains(c)) list.Add(c);
                    foreach (var c in Me(m).BattlefieldCards())
                        if (c.IsAlive && !exclude.Contains(c)) list.Add(c);
                    break;
            }
            return list;
        }

        CardModel PickBestSpellTarget(TargetType type, List<CardModel> candidates)
        {
            if (candidates.Count == 0) return null;
            switch (type)
            {
                case TargetType.EnemyUnit:
                    CardModel lowestHp = null;
                    foreach (var c in candidates)
                        if (lowestHp == null || c.currentHealth < lowestHp.currentHealth)
                            lowestHp = c;
                    return lowestHp;

                case TargetType.AllyUnit:
                    CardModel strongest = null;
                    foreach (var c in candidates)
                        if (strongest == null || c.effectiveAttack > strongest.effectiveAttack)
                            strongest = c;
                    return strongest;

                case TargetType.AnyUnit:
                    CardModel bestEnemy = null;
                    foreach (var c in candidates)
                        if (c.belongsToPlayer &&
                            (bestEnemy == null || c.currentHealth < bestEnemy.currentHealth))
                            bestEnemy = c;
                    return bestEnemy ?? candidates[0];
            }
            return candidates[0];
        }

        // ── Attack Phase ──────────────────────────────────────────────
        IEnumerator AttackPhase()
        {
            var benched = new List<CardModel>(Me(_gc.model).BenchCards());
            foreach (var card in benched)
            {
                var m = _gc.model;
                if (m.phase == GamePhase.GameOver || !HasPriority(m)) yield break;
                if (!Me(m).BattlefieldHasSpace) break;
                if (card.location != CardLocation.OnBench) continue;

                _gc.HandleAction(new DeclareAttackerAction(controlsPlayer, card));
                yield return new WaitForSeconds(actionDelay * 0.5f);
            }

            yield return new WaitForSeconds(actionDelay * 0.3f);
            Pass();
        }

        // ── Block Phase ───────────────────────────────────────────────
        IEnumerator BlockPhase()
        {
            var m = _gc.model;

            var attackers = new List<CardModel>();
            foreach (var c in Opp(m).BattlefieldCards())
                if (c.state == CardState.Attacking && c.blockedBy == null)
                    attackers.Add(c);

            attackers.Sort((a, b) => b.effectiveAttack.CompareTo(a.effectiveAttack));

            var blockers = new List<CardModel>();
            foreach (var c in Me(m).BenchCards()) blockers.Add(c);
            foreach (var c in Me(m).BattlefieldCards())
                if (c.state == CardState.Idle) blockers.Add(c);

            foreach (var attacker in attackers)
            {
                m = _gc.model;
                if (m.phase == GamePhase.GameOver || !HasPriority(m)) yield break;
                if (attacker.blockedBy != null) continue;

                var blocker = PickBlocker(attacker, blockers);
                if (blocker != null)
                {
                    _gc.HandleAction(new DeclareBlockerAction(controlsPlayer, blocker, attacker));
                    blockers.Remove(blocker);
                    yield return new WaitForSeconds(actionDelay * 0.5f);
                }
            }

            yield return new WaitForSeconds(actionDelay * 0.3f);
            Pass();
        }

        // ── Blocker selection ─────────────────────────────────────────
        CardModel PickBlocker(CardModel attacker, List<CardModel> available)
        {
            var valid = new List<CardModel>();
            foreach (var b in available)
            {
                if (b.HasKeyword(KeywordType.CantBlock)) continue;
                if (attacker.HasKeyword(KeywordType.Elusive) && !b.HasKeyword(KeywordType.Elusive)) continue;
                if (attacker.HasKeyword(KeywordType.Fearsome) && b.effectiveAttack < 4) continue;
                valid.Add(b);
            }
            if (valid.Count == 0) return null;

            // 1. Favorable trade
            foreach (var b in valid)
            {
                bool killsAttacker = b.effectiveAttack >= attacker.currentHealth;
                bool survives = attacker.effectiveAttack < b.currentHealth;
                if (killsAttacker && survives) return b;
            }

            // 2. Even trade
            foreach (var b in valid)
                if (b.effectiveAttack >= attacker.currentHealth) return b;

            // 3. Sacrifice nhỏ nhất nếu attacker nguy hiểm
            if (attacker.effectiveAttack >= 4)
            {
                CardModel weakest = null;
                foreach (var b in valid)
                    if (weakest == null || b.currentHealth < weakest.currentHealth)
                        weakest = b;
                return weakest;
            }

            return null;
        }

        // ── Helpers ───────────────────────────────────────────────────
        void Pass()
        {
            if ((!HasPriority(_gc.model))) return;
            _gc.HandleAction(new PassPriorityAction(controlsPlayer));
        }

        int AvailableMana(GameModel m, PlayerModel p)
        {
            int staged = p.TotalStagedManaCost();
            int coveredBySpell = System.Math.Min(p.spellMana, staged);
            return p.mana - (staged - coveredBySpell);
        }
    }
}