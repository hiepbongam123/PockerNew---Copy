using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using LoRClone;
using LoRClone.Data;
using LoRClone.Model;
using LoRClone.View; // CombatAnimator, CardView

namespace LoRClone.Controller
{
    public partial class GameController : MonoBehaviour
    {
        [Header("Setup")]
        public DeckData playerDeckData;
        public DeckData enemyDeckData;
        public int openingHandSize = 4;

        [Tooltip("Số lá CẢ 2 BÊN rút thêm NGAY sau khi mulligan xong (round 1), " +
                 "ngoài openingHandSize + số lá mulligan swap. Mặc định 1.")]
        public int extraDrawAfterMulligan = 1;

        [Header("Loop Mode — 1 bản sao mỗi lá + tái sử dụng vô hạn")]
        [Tooltip("Bật = chế độ vòng lặp: deck chỉ giữ 1 bản sao mỗi lá; lá 'dùng xong' " +
                 "(unit chết / spell resolve) reset về base rồi shuffle lại vào deck.\n" +
                 "Tắt = luật gốc (discard bình thường).")]
        public bool loopMode = false;

        [Header("Animation")]
        [Tooltip("Component xử lý animation combat (dash, clash, return, death fade).\n" +
                 "Để trống nếu chưa cần animation — game vẫn chạy bình thường.")]
        public CombatAnimator combatAnimator;

        [Tooltip("Cinematic player khi champion level up. Kéo GameObject LevelUpCinematic vào đây.\n" +
                 "Để trống nếu không dùng cinematic.")]
        public LevelUpCinematicPlayer levelUpCinematicPlayer;

        [Header("Timing — sequential resolution")]
        [Tooltip("Thời gian dừng TRƯỚC khi resolve spell (giây) — để arrow kịp hiện. 0 = tức thì.")]
        public float preResolveDelay = 0.5f;
        [Tooltip("Thời gian dừng SAU khi spell resolve (giây). 0 = tức thì.")]
        public float spellResolveDelay = 0.4f;
        [Tooltip("Thời gian dừng SAU KHI clash sound phát và TRƯỚC KHI damage apply (giây).\n" +
                 "Tăng để clash sound cảm giác 'nặng' hơn trước khi HP trừ. Gợi ý: 0.1–0.25.")]
        public float combatPreDelay = 0.15f;
        [Tooltip("Thời gian dừng SAU KHI damage apply + unit về bench, trước cặp tiếp theo (giây).\n" +
                 "Tăng để có thời gian nhìn kết quả mỗi cặp. Gợi ý: 0.4–0.7.")]
        public float combatPostDelay = 0.5f;
        [Tooltip("Delay giữa damage và lifesteal/overwhelm/kill effects trong 1 cặp combat (giây).\n" +
                 "Giúp Barrier, OnStrike sound có thời gian nghe rõ trước khi sound tiếp theo phát. Gợi ý: 0.1–0.2.")]
        public float keywordEffectDelay = 0.12f;
        [Tooltip("Delay đặc biệt cho Săn Bắn (QuickAttack) — giữa đòn attacker và đòn phản của blocker (giây).\n" +
                 "Tạo cảm giác 'đánh trước rõ ràng'. Gợi ý: 0.2–0.35.")]
        public float quickAttackSplitDelay = 0.25f;
        [Tooltip("Delay trước khi ProcessDeaths chạy sau combat (giây).\n" +
                 "Giúp Overwhelm, Lifesteal sound resolve xong trước khi unit biến mất. Gợi ý: 0.1–0.2.")]
        public float preDeathDelay = 0.15f;
        [Tooltip("Battle-skill kiểu Renekton (unitSkillOnAttack): thời gian lá skill HIỆN trên spell zone\n" +
                 "trước khi resolve inline (giây). Skill spell phải để spellSpeed = Burst. 0 = resolve tức thì\n" +
                 "(không kịp thấy lá). Gợi ý: 0.4–0.7.")]
        public float battleSkillShowDelay = 0.5f;
        [Tooltip("Section 5 Gameplay Feel Bible — Hit-stop: khoảng dừng tại điểm va chạm giữa dash và damage (giây).\n" +
                 "Tạo cảm giác impact 'nặng'. Áp dụng cho blocked combat, QuickAttack, và nexus attack.\n" +
                 "Gợi ý: 0.05–0.10. Để 0 để tắt.")]
        public float hitStopDuration = 0.07f;

        public GameModel model { get; private set; }
        public static GameController Instance { get; private set; }
        public bool isCinematicPlaying => _levelUpCinematicPlaying;

        readonly Dictionary<int, List<CardModel>> _spellTargets = new Dictionary<int, List<CardModel>>();

        // Track spell card theo thứ tự LIFO để phát sfxSpellResolve trước mỗi ResolveTop.
        // Push khi commit, Pop khi resolve hoặc undo.
        readonly Stack<CardModel> _spellSoundStack = new Stack<CardModel>();

        // Track card đã trigger OnAttack/OnBlock trong round này → chặn duplicate khi undeclare + re-declare
        readonly HashSet<int> _attackTriggered = new HashSet<int>();
        readonly HashSet<int> _blockTriggered = new HashSet<int>();

        // Renekton-style: unit.id đã stage skill (spellcraftSpell) round này → chặn double-stage.
        readonly HashSet<int> _battleSkillStaged = new HashSet<int>();
        // Renekton-style: skillCard.id → unit gốc đã tạo battle skill → skill combat-target biết đánh ai.
        readonly Dictionary<int, CardModel> _battleSkillUnit = new Dictionary<int, CardModel>();

        // Lưu spell mana đã dùng cho từng spell (key=spell.id) → dùng khi refund undo cast
        readonly Dictionary<int, int> _spellManaUsed = new Dictionary<int, int>();
        // Lưu tổng mana thực tế đã trả (sau reduction) → dùng để hoàn đúng số khi undo
        readonly Dictionary<int, int> _manaCostPaid = new Dictionary<int, int>();

        // Theo dõi CardCreationTrigger đã fire bao nhiêu lần — (cardId, triggerIndex) → count.
        // Dùng chung cho MỌI counter (SpellManaSpent/DamageDealt/DamageTaken/AttacksMade).
        readonly Dictionary<(int cardId, int trigIdx), int> _cardCreationFiredCount =
            new Dictionary<(int, int), int>();

        // Guard chống recursion cho SummonUnit
        // (VD: WhenPlayed của unit vừa summon gọi summon thêm unit khác)
        bool _isSummoning = false;

        // Guard chống recursion cho EvaluatePassiveLevelUps
        // (TriggerLevelUp → Notify → EvaluatePassiveLevelUps → TriggerLevelUp → vòng lặp)
        bool _evaluatingPassiveLevelUps = false;

        // Guard chặn input trong khi cinematic level-up đang phát
        bool _levelUpCinematicPlaying = false;

        // Guard chặn mọi action trong khi UnitSkill đang chờ player chọn mục tiêu
        // (coroutine SummonSequence yield WaitUntil → HandleAction vẫn chạy nếu không có guard)
        bool _isUnitSkillTargeting = false;

        // Guard: chặn HandleAction trong khi ResolveCombatSequentially đang chạy (animation/delay
        // nhiều giây). TRƯỚC ĐÂY không có guard này → bấm Pass/Confirm liên tục trong lúc combat
        // đang resolve vẫn lọt qua HandleAction → xử lý action giữa lúc state chưa ổn định → bug combat.
        bool _isResolvingCombat = false;

        // Guard tương tự cho ResolveSpellsSequentially (spell stack resolve tuần tự, có delay).
        bool _isResolvingSpells = false;

        // Guard: chặn HandleAction trong SUỐT thời gian SummonSequence chạy (animation + WhenPlayed +
        // spellcraft + stage skill), KHÔNG chỉ riêng đoạn chờ chọn target (_isUnitSkillTargeting).
        // TRƯỚC ĐÂY hở đoạn đầu coroutine → chơi tiếp 1 unit khác trong lúc unit trước chưa xong
        // → 2 SummonSequence chạy chồng nhau → slot bench bị ghi đè sai thứ tự.
        bool _isSummoningSequence = false;

        /// <summary>Đang bận animation/coroutine hệ thống (combat, spell stack, summon...) — dùng cho
        /// UI (VD HUDView) để tự vô hiệu hóa nút bấm thay vì để HandleAction tự Reject.</summary>
        public bool IsBusy => _levelUpCinematicPlaying || _isUnitSkillTargeting || _isEquipmentSelecting
                            || _isResolvingCombat || _isResolvingSpells || _isSummoningSequence;

        // Guard: chặn khối rút bài sau-mulligan (extraDrawAfterMulligan + AfterMulligan skill)
        // chạy nhiều lần nếu HandleMulliganConfirm lỡ bị gọi lặp sau khi phase đã rời Mulligan.
        bool _mulliganBonusApplied = false;

        // Guard chặn mọi action trong khi Equipment đang chờ player chọn mode / chọn ally target
        // (khai báo ở đây, logic nằm trong GameController.Equipment.cs)
        // bool _isEquipmentSelecting = false;  ← khai báo trong GameController.Equipment.cs, không cần ở đây

        // ── New keyword round/combat state ────────────────────────
        // Chiến Lợi (Plunder): nexus địch của ai đó đã bị damage chưa?
        bool _nexusDamagedByPlayerThisRound; // player đang tấn công → enemy nexus bị hurt
        bool _nexusDamagedByEnemyThisRound;  // enemy đang tấn công → player nexus bị hurt
        // Bình Minh / Hoàng Hôn: đếm lá bài đã chơi round này
        int _cardsPlayedThisRound;
        // Tiên Phong (Scout): có "sẵn sàng tấn công" đang chờ hoàn attack token
        bool _scoutGrantedExtraAttack;
        // Tiên Phong (Scout): đã dùng "sẵn sàng tấn công" round này chưa (chỉ 1 lần/round)
        bool _scoutReadyUsedThisRound;
        // Linh Thiêng (Hallowed): stack tích lũy CẢ VÁN (không reset mỗi round)
        int _hallowedStacksP, _hallowedStacksE;
        // Linh Thiêng: quân đầu tiên tấn công mỗi round đã được cấp buff chưa (reset mỗi round)
        bool _hallowedGrantedThisRoundP, _hallowedGrantedThisRoundE;
        // Chỉ Định (Challenger): attacker.id → forced blocker (unit địch phải block attacker đó)
        readonly Dictionary<int, CardModel> _challengerTargets = new Dictionary<int, CardModel>();
        readonly HashSet<int> _challengerBuffedThisRound = new HashSet<int>(); // nội tại +2|+1 đã áp (1 lần/attacker/round)

        // ── SUMMON-SLAIN: sổ "quân do mình hạ" (spell/skill/obliterate/hiến tế) ──
        // _deathCause = bên gây ra cái chết đang xử lý (set scope quanh ProcessDeaths của hành động
        // chủ động). null = combat/tự nhiên → KHÔNG ghi sổ.
        bool? _deathCause;
        readonly List<(CardData data, bool ownerIsPlayer)> _slainByPlayer = new List<(CardData, bool)>();
        readonly List<(CardData data, bool ownerIsPlayer)> _slainByEnemy = new List<(CardData, bool)>();

        // ── FALLEN: sổ "đồng minh đã tử trận" (MỌI cái chết, keyed theo CHỦ) — The Harrowing ──
        readonly List<(CardData data, bool ownerIsPlayer)> _fallenByPlayer = new List<(CardData, bool)>();
        readonly List<(CardData data, bool ownerIsPlayer)> _fallenByEnemy = new List<(CardData, bool)>();

        // ── STANDING TAG-BUFF (aura persistent cả ván — Sương đêm Viego) ──
        class StandingBuff { public int id; public string tag; public int atk; public int hp; public KeywordType? keyword; }
        readonly List<StandingBuff> _standingBuffsPlayer = new List<StandingBuff>();
        readonly List<StandingBuff> _standingBuffsEnemy = new List<StandingBuff>();
        int _nextStandingBuffId = 1;
        // Guard (unitId, buffId): tránh áp trùng khi bench→battlefield fire 2 event.
        readonly HashSet<(int unitId, int buffId)> _standingBuffApplied = new HashSet<(int, int)>();

        public void SetSpellTargets(CardModel spell, List<CardModel> targets)
        {
            if (targets != null && targets.Count > 0)
                _spellTargets[spell.id] = targets;
            else
                _spellTargets.Remove(spell.id);
        }

        public void SetSpellTarget(CardModel spell, CardModel target)
        {
            if (target != null)
                _spellTargets[spell.id] = new List<CardModel> { target };
            else
                _spellTargets.Remove(spell.id);
        }

        public event System.Action<GameModel> OnStateChanged;
        public event System.Action<string> OnActionRejected;
        public event System.Action OnCombatResolved;

        /// <summary>
        /// Fired khi một spell (player hoặc enemy) được push lên stack kèm targets.
        /// GameView subscribe để hiện locked arrow cho enemy spells.
        /// Args: (spell, targets, byPlayer)
        /// </summary>
        public event System.Action<CardModel, List<CardModel>, bool> OnSpellCommittedWithTargets;

        /// <summary>
        /// Fired khi unit có UnitSkill ra sân và spellcraftSpell của nó cần chọn target.
        /// GameView subscribe để bật targeting UI → gọi callback khi player chọn xong (hoặc null nếu cancel).
        /// Args: (unit, skillCard, isPlayer, onComplete(targets))
        /// </summary>
        public event System.Action<CardModel, CardModel, bool, System.Action<List<CardModel>>> OnUnitSkillNeedsTarget;

        // Equipment events — khai báo cũng ở GameController.Equipment.cs (partial).
        // Được kéo lên đây để GameView/EquipmentModeView dễ subscribe từ Inspector.
        // (Xem chi tiết tại GameController.Equipment.cs)

        void Awake()
        {
            if (Instance != null && Instance != this) { Destroy(gameObject); return; }
            Instance = this;
        }

        void Start() { bool net = PendingNetworkMode; PendingNetworkMode = false; if (!net) StartGame(); } // tiêu thụ cờ → không kẹt sang PvE

        // ── Setup ─────────────────────────────────────────────────
        public void StartGame()
        {
            Debug.Log($"[GC] StartGame chạy. PendingNetworkMode={PendingNetworkMode}, networkMode={networkMode}");
            // Đọc deck từ LobbyManager nếu có (ưu tiên hơn Inspector)
            if (GameConfig.IsReady)
            {
                playerDeckData = GameConfig.playerDeck;
                enemyDeckData = GameConfig.enemyDeck;
                GameConfig.Clear();
            }

            model = new GameModel();
            if (!networkMode)                        // ★ PvP: KHÔNG gắn hook giảm giá Chòm Sao (đọc save local → lệch cost 2 máy)
                CampaignRun.ConstellationResetBattle();  // TINH HỒN: reset cờ + gắn hàm giảm giá động vào CardModel
            ApplyNetworkSeedsIfPending(); // PvP: set seed truoc khi shuffle
            InitEquipmentDeathHandlers(); // Equipment: auto-return về tay khi unit đeo chết
            InitFallenTracking();         // The Harrowing: ghi sổ đồng minh tử trận
            InitStandingBuffHooks();      // Aura theo tag: áp cho unit khi ra sân (Sương đêm Viego)
            model.OnReadyToResolveCombat += () => StartCoroutine(ResolveCombatSequentially());
            model.OnReadyToResolveSpells += () => StartCoroutine(ResolveSpellsSequentially());
            model.OnRoundChanged += _ =>
            {
                _attackTriggered.Clear();
                _blockTriggered.Clear();
                _battleSkillStaged.Clear();
                _battleSkillUnit.Clear();
                // Reset keyword round state
                _nexusDamagedByPlayerThisRound = false;
                _nexusDamagedByEnemyThisRound = false;
                _cardsPlayedThisRound = 0;
                _scoutGrantedExtraAttack = false;
                _scoutReadyUsedThisRound = false;
                _hallowedGrantedThisRoundP = false;
                _hallowedGrantedThisRoundE = false;
                _challengerTargets.Clear();
                _challengerBuffedThisRound.Clear();
                _equipmentsPlayedThisRound.Clear(); // Equipment: reset giới hạn 1 lần/round
                KillAllEphemerals();    // Cảm Tử chết cuối round (nếu chưa chết khi ra đòn)
                DiscardFleetingCards(); // Thoáng Hiện: hủy lá Fleeting còn trên tay cuối round
                TickCountdowns();       // Đếm Ngược: tick countdown, ForceKill unit về 0
                // CHÒM SAO: ★5 hồi spell mana + rải giảm giá "lá đầu vòng" (★1/★3) + reset cờ ★2.
                // ★ PvP: TẮT hoàn toàn campaign — Chòm Sao/Luật Trận đọc save LOCAL (mỗi máy khác nhau)
                //   → 2 sim lệch ngay (hp/mana/unit tự mọc) → DESYNC. PvP phải sạch, giống hệt 2 máy.
                if (!networkMode)
                {
                    CampaignRun.ConstellationRoundStart(model.player);
                    CampaignRun.EncounterModifierRoundStart(model.enemy);   // LUẬT TRẬN: địch ramp mỗi vòng
                }
                TriggerRoundSkills(SkillTrigger.RoundStart);
            };

            if (playerDeckData && enemyDeckData)
            {
                model.SetupDecks(playerDeckData, enemyDeckData);
                if (loopMode) EnableLoopMode();
            }
            else
                Debug.LogWarning("[GC] DeckData chưa gán!");

            // Champion / lá bắt buộc: đưa lên ĐẦU deck trước khi rút tay mở đầu
            // → chắc chắn nằm trong tay ngay từ đầu (nhiều lá → ưu tiên theo thứ tự).
            EnsureGuaranteedOpeningHand(model.player);
            EnsureGuaranteedOpeningHand(model.enemy);

            for (int i = 0; i < openingHandSize; i++)
            {
                model.player.DrawCard();
                model.enemy.DrawCard();
            }

            _mulliganBonusApplied = false;
            model.StartMulligan();
            Notify();
        }

        // ── Mulligan ──────────────────────────────────────────────
        public void HandleMulliganConfirm(bool byPlayer, List<CardModel> selectedToSwap)
        {
            if (model.phase != GamePhase.Mulligan)
            {
                Debug.LogWarning("[GC] HandleMulliganConfirm gọi sai phase.");
                return;
            }

            var p = byPlayer ? model.player : model.enemy;
            p.MulliganSwap(selectedToSwap);
            model.ConfirmMulligan(byPlayer);

            // CHÒM SAO: vòng 1 bắt đầu sau mulligan (OnRoundChanged có thể không fire cho vòng 1)
            // → rải giảm giá "lá đầu vòng" lên tay SAU khi mulligan đã đổi bài.
            // Guard: đảm bảo khối rút bài dưới đây CHỈ chạy đúng 1 LẦN cho cả ván, kể cả khi
            // HandleMulliganConfirm lỡ bị gọi thêm lần nữa sau khi phase đã rời Mulligan
            // (VD AI tự confirm gọi trễ / UI gọi lặp) — tránh rút dư bài.
            if (model.phase != GamePhase.Mulligan && !_mulliganBonusApplied)
            {
                _mulliganBonusApplied = true;
                if (!networkMode)   // ★ PvP: KHÔNG áp campaign (tránh desync — xem chú thích OnRoundChanged)
                {
                    CampaignRun.ConstellationRoundStart(model.player);
                    CampaignRun.EncounterModifierRoundStart(model.enemy);   // LUẬT TRẬN: vòng 1
                }

                // Vòng 1: cả 2 bên rút thêm 'extraDrawAfterMulligan' lá THƯỜNG (top deck) ngoài
                // 4 lá mulligan — áp dụng chung cho toàn bộ deck, không cần gắn gì lên CardData.
                for (int i = 0; i < extraDrawAfterMulligan; i++)
                {
                    model.player.DrawCard();
                    model.enemy.DrawCard();
                }

                // AfterMulligan: quét deck, tự động rút các lá có skill/ability
                // condition=AfterMulligan vào tay (bổ sung, KHÔNG tính vào 4 lá mulligan/openingHandSize).
                DrawAfterMulliganCards(model.player);
                DrawAfterMulliganCards(model.enemy);
            }

            Notify();
        }

        /// <summary>
        /// Quét toàn bộ deck của 1 bên, tìm lá có skill (legacy) hoặc ability (mới) với
        /// trigger/condition = SkillTrigger.AfterMulligan → rút NGAY vào tay (DrawSpecificCard,
        /// không phải deck[0]) rồi chạy Execute() của skill đó trên chính lá vừa rút (qua
        /// RunAbilities — xem GameController_AbilityRunner.cs).
        /// Dừng lại (không rút thêm) nếu tay đã đầy (MaxHandSize) — không lỗi, chỉ log cảnh báo.
        /// </summary>
        void DrawAfterMulliganCards(PlayerModel p)
        {
            if (p == null) return;

            var toDraw = new List<CardModel>();
            foreach (var c in p.deck)
            {
                if (c?.data == null) continue;
                bool hasTrigger =
                    (c.data.skills != null && c.data.skills.Exists(s => s != null && s.trigger == SkillTrigger.AfterMulligan)) ||
                    (c.data.abilities != null && c.data.abilities.Exists(a => a?.effect != null && a.condition == SkillTrigger.AfterMulligan));
                if (hasTrigger) toDraw.Add(c);
            }
            if (toDraw.Count == 0) return;

            foreach (var c in toDraw)
            {
                if (p.hand.Count >= PlayerModel.MaxHandSize)
                {
                    Debug.LogWarning($"[AfterMulligan] Tay {(p.isPlayer ? "player" : "enemy")} đã đầy — {c.data.cardName} không rút được, vẫn nằm trong deck.");
                    continue;
                }

                var drawn = p.DrawSpecificCard(c);
                if (drawn == null) continue;

                var ctx = new GameContext(model, p, this);
                RunAbilities(drawn, SkillTrigger.AfterMulligan, ctx);

                Debug.Log($"[AfterMulligan] {c.data.cardName} tự động rút vào tay ({(p.isPlayer ? "player" : "enemy")}) sau khi mulligan xong.");
            }

            model?.CheckGameOver();
        }

        // ── Action Entry Point ────────────────────────────────────
        public void HandleAction(PlayerAction action)
        {
            if (_levelUpCinematicPlaying) { Reject("Đang phát cinematic level-up."); return; }
            if (_isUnitSkillTargeting) { Reject("Đang chọn mục tiêu skill — chờ hoàn tất."); return; }
            if (_isEquipmentSelecting) { Reject("Đang chọn mode / mục tiêu Equipment — chờ hoàn tất."); return; }
            if (_isResolvingCombat) { Reject("Combat đang resolve — chờ hoàn tất."); return; }
            if (_isResolvingSpells) { Reject("Spell stack đang resolve — chờ hoàn tất."); return; }
            if (_isSummoningSequence) { Reject("Unit đang ra sân — chờ hoàn tất."); return; }
            if (model.phase == GamePhase.GameOver) { Reject("Game over."); return; }
            if (!HasPriority(action.byPlayer)) { Reject("Không phải lượt của bạn."); return; }

            if (model.restrictToSpellsOnly && action.byPlayer == model.restrictionTargetIsPlayer)
            {
                // Kiểm tra tốc độ spell trên đỉnh stack — quyết định response window rộng hay hẹp.
                // Fast trên stack: chỉ Fast/Burst được phép (LoR rule).
                // Slow trên stack: Fast/Burst/Slow + chơi unit + declare attacker đều được phép.
                bool topIsSlow = model.spellStack.PeekTop()?.source?.data?.spellSpeed == SpellSpeed.Slow;

                bool allowed = false;
                if (action is CastSpellAction) allowed = true;           // Burst resolve ngay
                if (action is PassPriorityAction) allowed = true;
                if (action is UnstageSpellAction) allowed = true;
                if (action is UndoCastSpellAction) allowed = true;
                if (action is StageSpellAction sa)
                {
                    // Response window: CHỈ Fast (Burst đi lối CastSpellAction ở trên).
                    // Slow KHÔNG bao giờ được stage khi có spell trên stack — kể cả top là Slow
                    // (đúng luật LoR; HandleStageSpell cũng chặn lần 2). Đã bỏ allowance cũ cho Slow.
                    if (sa.spell.data.spellSpeed == SpellSpeed.Fast) allowed = true;
                }
                // Slow spell response window: attacker declare vẫn cho phép.
                // FIX: bỏ PlayUnitAction — có spell/skill trên stack thì KHÔNG được
                // triệu hồi unit (luật LoR). Đây là lý do enemy/player vẫn thả unit
                // trong lúc skill chưa resolve.
                if (topIsSlow && action is DeclareAttackerAction) allowed = true;
                // Block/undeclare luôn được phép nếu có combat pending
                if (action is DeclareBlockerAction && model.combatPending) allowed = true;
                if (action is UndeclareAction && model.combatPending) allowed = true;

                if (!allowed)
                {
                    string msg;
                    if (model.phase == GamePhase.BlockDeclare && model.spellStack.IsEmpty)
                        msg = "Đang trong combat window — chỉ được dùng Fast/Burst spell hoặc pass.";
                    else if (topIsSlow)
                        msg = "Đang có Slow spell trên stack — được phép: Slow/Fast/Burst spell, chơi unit, declare attacker, hoặc pass.";
                    else
                        msg = "Đang có spell trên stack — chỉ được response bằng Fast/Burst spell hoặc pass.";
                    Reject(msg);
                    return;
                }
            }

            switch (action)
            {
                case PlayUnitAction a: HandlePlayUnit(a); break;
                case PlayEquipmentAction a: HandlePlayEquipment(a); break;
                case DeclareAttackerAction a: HandleDeclareAttacker(a); break;
                case DeclareBlockerAction a: HandleDeclareBlocker(a); break;
                case UndeclareAction a: HandleUndeclare(a); break;
                case CastSpellAction a: HandleCastSpell(a); break;
                case UndoCastSpellAction a: HandleUndoCastSpell(a); break;
                case StageSpellAction a: HandleStageSpell(a); break;
                case UnstageSpellAction a: HandleUnstageSpell(a); break;
                case PassPriorityAction a: HandlePassPriority(a); break;
            }

            model.CheckGameOver();
            Notify();
        }

        // ── Helpers ───────────────────────────────────────────────
        bool HasPriority(bool byPlayer) => byPlayer == model.isPlayerPriority;
        PlayerModel P(bool isPlayer) => isPlayer ? model.player : model.enemy;
        string Tag(bool isPlayer) => isPlayer ? "Player" : "Enemy";
        void Reject(string reason)
        {
            Debug.LogWarning($"[GC] Rejected: {reason}");
            OnActionRejected?.Invoke(reason);
        }

        void Notify()
        {
            OnStateChanged?.Invoke(model);
        }

        /// <summary>
        /// Ép phát lại OnStateChanged để View đồng bộ NGAY khi state bị sửa NGOÀI luồng action
        /// (vd: set mana/máu khởi đầu Leo Tháp qua CampaignResultWatcher). Không đổi state, chỉ thông báo.
        /// </summary>
        public void RaiseStateChanged() => OnStateChanged?.Invoke(model);

        /// <summary>Unit gốc đã tạo battle-skill spell này (Renekton...). null nếu không phải battle skill.</summary>
        public CardModel BattleSkillUnitFor(CardModel skillCard) =>
            skillCard != null && _battleSkillUnit.TryGetValue(skillCard.id, out var u) ? u : null;

        /// <summary>Danh sách unit ĐỊCH còn sống của bên sở hữu source (dùng cho skill quét mục tiêu).</summary>
        public List<CardModel> EnemyUnitsOf(CardModel source, bool battlefieldOnly = false)
        {
            var list = new List<CardModel>();
            if (source == null) return list;
            var enemy = P(!source.belongsToPlayer);
            if (!battlefieldOnly)
                foreach (var c in enemy.BenchCards()) if (c.IsAlive) list.Add(c);
            foreach (var c in enemy.BattlefieldCards()) if (c.IsAlive) list.Add(c);
            return list;
        }

        /// <summary>Đánh dmg thẳng vào Nexus địch của bên sở hữu source (skill không có target → nexus).</summary>
        public void DamageEnemyNexusFrom(CardModel source, int dmg, bool count = true)
        {
            if (source == null || dmg <= 0) return;
            var enemyPlayer = P(!source.belongsToPlayer);
            enemyPlayer.TakeDamage(dmg);
            if (source.belongsToPlayer) _nexusDamagedByPlayerThisRound = true;
            else _nexusDamagedByEnemyThisRound = true;
            if (count) RegisterSkillDamage(source, dmg);
            model.CheckGameOver();
            Notify();
        }

        /// <summary>
        /// Đưa các lá có CardData.guaranteedInOpeningHand lên ĐẦU deck (giữ thứ tự) trước khi rút
        /// tay mở đầu → champion/lá bắt buộc luôn nằm trong tay. Nhiều lá → ưu tiên lần lượt
        /// (nếu nhiều hơn số lá tay mở đầu thì chỉ đủ chỗ mới vào tay).
        /// </summary>
        void EnsureGuaranteedOpeningHand(PlayerModel p)
        {
            if (p == null) return;
            var guaranteed = p.deck.FindAll(c => c.data != null && c.data.guaranteedInOpeningHand);
            if (guaranteed.Count == 0) return;
            foreach (var g in guaranteed) p.deck.Remove(g);
            p.deck.InsertRange(0, guaranteed);
        }

        // ── Mana Helpers ──────────────────────────────────────────
        int AvailableRegularMana(PlayerModel p)
        {
            int staged = p.TotalStagedManaCost();
            int stagedCoveredBySpell = System.Math.Min(p.spellMana, staged);
            int stagedFromRegular = staged - stagedCoveredBySpell;
            return p.mana - stagedFromRegular;
        }

        int AvailableManaForSpell(PlayerModel p) =>
            p.spellMana + p.mana - p.TotalStagedManaCost();

        // ── LOOP MODE ─────────────────────────────────────────────
        /// <summary>
        /// Bật chế độ vòng lặp cho CẢ 2 người chơi: giữ 1 bản sao mỗi lá + cho phép recycle
        /// về deck khi lá dùng xong. Gọi 1 lần trong StartGame sau SetupDecks nếu loopMode = true.
        /// loopMode = false → không gọi → hành vi gốc giữ nguyên 100%.
        /// </summary>
        void EnableLoopMode()
        {
            foreach (var p in new[] { model.player, model.enemy })
            {
                p.DedupeSingleCopy();                    // giữ 1 bản sao mỗi lá
                p.recycleUsedCards = true;               // KillCard → recycle về deck
                p.OnCardRecycled -= HandleCardRecycled;  // tránh double-subscribe
                p.OnCardRecycled += HandleCardRecycled;
            }
            Debug.Log("[LoopMode] Bật — deck 1 bản sao mỗi lá, tái sử dụng vô hạn.");
        }

        /// <summary>
        /// LOOP MODE: khi 1 lá recycle về deck, xóa mọi tracker key theo card.id (id KHÔNG đổi khi
        /// tái sử dụng CardModel) để lá quay lại "sạch" — trigger once / progress fire lại đúng như lá mới.
        /// </summary>
        void HandleCardRecycled(PlayerModel owner, CardModel card)
        {
            int id = card.id;

            // Trackers kéo dài CẢ VÁN (không auto-clear mỗi round) — BẮT BUỘC purge:
            var creationKeys = new List<(int cardId, int trigIdx)>();
            foreach (var key in _cardCreationFiredCount.Keys)
                if (key.cardId == id) creationKeys.Add(key);
            foreach (var key in creationKeys) _cardCreationFiredCount.Remove(key);

            // Trackers per-round (đã auto-clear ở OnRoundChanged) — xóa thêm cho chắc:
            _attackTriggered.Remove(id);
            _blockTriggered.Remove(id);
            _battleSkillStaged.Remove(id);
            _battleSkillUnit.Remove(id);
            _challengerTargets.Remove(id);
            _challengerBuffedThisRound.Remove(id);
            _equipmentsPlayedThisRound.Remove(id);
            _spellManaUsed.Remove(id);
            _manaCostPaid.Remove(id);
            _spellTargets.Remove(id);
            _standingBuffApplied.RemoveWhere(k => k.unitId == id);   // aura áp lại khi lá tái sinh
        }

        // ── SUMMON-SLAIN methods ──────────────────────────────────
        /// <summary>Ghi 1 unit vào sổ "quân do causeIsPlayer hạ". Bỏ qua non-unit và token isGenerated.</summary>
        public void RecordSlain(CardModel unit, bool causeIsPlayer)
        {
            if (unit == null || unit.originalData == null) return;
            if (unit.originalData.cardType != CardType.Unit) return;
            if (unit.originalData.isGenerated) return;
            var log = causeIsPlayer ? _slainByPlayer : _slainByEnemy;
            log.Add((unit.originalData, unit.belongsToPlayer));
        }

        /// <summary>Sổ "quân do causeIsPlayer hạ" (read-only). Dùng bởi SkillSummonSlain.</summary>
        public IReadOnlyList<(CardData data, bool ownerIsPlayer)> SlainOf(bool causeIsPlayer)
            => causeIsPlayer ? _slainByPlayer : _slainByEnemy;

        /// <summary>Tiêu (xóa) 1 entry khỏi sổ sau khi đã summon lại thành công.</summary>
        public bool TryConsumeSlain(bool causeIsPlayer, (CardData data, bool ownerIsPlayer) entry)
            => (causeIsPlayer ? _slainByPlayer : _slainByEnemy).Remove(entry);

        // ── FALLEN (The Harrowing) ────────────────────────────────
        /// <summary>Sổ "đồng minh của isPlayer đã tử trận cả ván" (read-only).</summary>
        public IReadOnlyList<(CardData data, bool ownerIsPlayer)> FallenOf(bool isPlayer)
            => isPlayer ? _fallenByPlayer : _fallenByEnemy;

        /// <summary>Ghi 1 unit vừa chết vào sổ tử trận của CHỦ nó. Subscribe OnCardDied của cả 2 player.
        /// Bỏ qua non-unit (spell/equipment) và token isGenerated.</summary>
        void RecordFallen(PlayerModel owner, CardModel card)
        {
            if (card == null || card.originalData == null) return;
            if (card.originalData.cardType != CardType.Unit) return;
            if (card.originalData.isGenerated) return;
            var log = owner.isPlayer ? _fallenByPlayer : _fallenByEnemy;
            log.Add((card.originalData, owner.isPlayer));
        }

        /// <summary>Đăng ký ghi sổ tử trận. Gọi 1 lần trong StartGame sau khi model tạo xong.</summary>
        void InitFallenTracking()
        {
            model.player.OnCardDied += RecordFallen;
            model.enemy.OnCardDied += RecordFallen;
        }

        // ── STANDING TAG-BUFF (aura) ──────────────────────────────
        /// <summary>Đăng ký buff aura theo tag cho forPlayer: áp cho unit đang trên sân có tag, và
        /// MỌI unit có tag ra sân sau này (kể cả bản hồi sinh). Persistent cả ván. Viego mang tag cũng hưởng.</summary>
        public void AddStandingTagBuff(bool forPlayer, string tag, int atk, int hp, KeywordType? kw = null)
        {
            if (string.IsNullOrEmpty(tag) || (atk == 0 && hp == 0 && kw == null)) return;
            var b = new StandingBuff { id = _nextStandingBuffId++, tag = tag, atk = atk, hp = hp, keyword = kw };
            (forPlayer ? _standingBuffsPlayer : _standingBuffsEnemy).Add(b);

            var p = P(forPlayer);
            foreach (var c in p.BenchCards()) ApplyStandingBuffTo(c, b);
            foreach (var c in p.BattlefieldCards()) ApplyStandingBuffTo(c, b);
            Debug.Log($"[Aura] +{atk}/+{hp}{(kw != null ? " +" + kw : "")} cho mọi '{tag}' của {Tag(forPlayer)} (persistent).");
            Notify();
        }

        void ApplyStandingBuffsOnEntry(PlayerModel owner, CardModel unit)
        {
            var list = owner.isPlayer ? _standingBuffsPlayer : _standingBuffsEnemy;
            for (int i = 0; i < list.Count; i++) ApplyStandingBuffTo(unit, list[i]);
        }

        void ApplyStandingBuffTo(CardModel unit, StandingBuff b)
        {
            if (unit == null || !unit.HasTag(b.tag)) return;
            if (!_standingBuffApplied.Add((unit.id, b.id))) return;   // đã áp → bỏ qua
            if (b.atk != 0) unit.BuffAttack(b.atk);
            if (b.hp != 0) unit.BuffHealth(b.hp);
            if (b.keyword.HasValue) unit.GrantKeyword(b.keyword.Value);
            unit.RecordSkillBuff("Aura: " + b.tag, null, b.atk, b.hp);
        }

        void InitStandingBuffHooks()
        {
            model.player.OnCardPlayedToBench += ApplyStandingBuffsOnEntry;
            model.enemy.OnCardPlayedToBench += ApplyStandingBuffsOnEntry;
            model.player.OnCardMovedToBattlefield += ApplyStandingBuffsOnEntry;
            model.enemy.OnCardMovedToBattlefield += ApplyStandingBuffsOnEntry;
        }
    }
}