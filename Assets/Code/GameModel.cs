using System;
using LoRClone.Data;

namespace LoRClone.Model
{
    public enum GamePhase
    {
        NotStarted,
        Mulligan,           // chọn lại bài đầu game
        RoundStart,         // đầu round: refill mana, draw bài
        PlayerPriority,     // người chơi có quyền hành động
        EnemyPriority,      // enemy có quyền hành động
        AttackDeclare,      // bên có attack token chọn attacker
        BlockDeclare,       // bên không có attack token chọn blocker
        SpellStackResolve,  // đang giải quyết spell stack
        CombatResolve,      // tính damage combat
        RoundEnd,           // cuối round
        GameOver,
    }

    public class GameModel
    {
        // ── Players ───────────────────────────────────────────────
        public readonly PlayerModel player;
        public readonly PlayerModel enemy;

        // ── Phase & Turn ──────────────────────────────────────────
        public GamePhase phase { get; private set; } = GamePhase.NotStarted;
        public int roundNumber { get; private set; } = 1;

        /// <summary>
        /// true = player đang có attack token (được tấn công trước).
        /// Đổi mỗi round. Có thể thay đổi mid-round sau combat qua TransferAttackToken().
        /// </summary>
        public bool playerHasAttackToken { get; private set; } = true;
        public bool combatPending { get; set; } = false;
        /// <summary>true sau khi combat resolve trong round này — HUD dùng để đổi màu attack token.</summary>
        public bool attackTokenUsed { get; set; } = false;

        // Track ai có token khi bắt đầu round.
        // EndRound() dùng để flip đúng — tránh double-flip nếu token đã transfer mid-round.
        private bool _roundStartTokenHolder = true;

        /// <summary>
        /// Ai đang có priority (được hành động).
        /// </summary>
        public bool isPlayerPriority { get; private set; } = true;

        // ── Spell Stack ───────────────────────────────────────────
        public readonly SpellStack spellStack = new();

        /// <summary>
        /// true khi có spell đang chờ resolve trên stack — bên RESTRICTION TARGET
        /// CHỈ được cast Fast/Burst spell hoặc pass để response, không được
        /// play unit / declare attacker/blocker mới, KHÔNG được cast Slow spell.
        ///
        /// Áp dụng cho CẢ Fast lẫn Slow spell trên stack — trong LoR, bất kể tốc độ
        /// nào của spell đang trên stack, response window luôn chỉ cho phép Fast/Burst.
        /// </summary>
        public bool restrictToSpellsOnly { get; private set; } = false;

        /// <summary>
        /// true = player, false = enemy. Xác định AI BÊN nào bị giới hạn bởi
        /// restrictToSpellsOnly. Người commit spell (byPlayer) KHÔNG bị giới hạn,
        /// chỉ đối thủ của họ mới bị — kể cả khi priority sau đó quay lại byPlayer.
        /// </summary>
        public bool restrictionTargetIsPlayer { get; private set; } = false;

        /// <summary>
        /// Bật/tắt giới hạn response window (Fast/Burst only).
        /// </summary>
        public void SetRestrictToSpellsOnly(bool value) => restrictToSpellsOnly = value;

        /// <summary>Đặt bên bị giới hạn bởi restrictToSpellsOnly (true = player, false = enemy).</summary>
        public void SetSpellRestrictionTarget(bool isPlayer) => restrictionTargetIsPlayer = isPlayer;

        // ── Mulligan tracking ─────────────────────────────────────
        private bool _playerMulliganDone = false;
        private bool _enemyMulliganDone = false;

        // ── Pass tracking ─────────────────────────────────────────
        // Cả hai pass liên tiếp → kết thúc giai đoạn hiện tại
        private bool _playerPassed = false;
        private bool _enemyPassed = false;


        // Phase trước khi vào SpellStackResolve — dùng để quay lại đúng phase sau khi spell resolve.
        // Quan trọng khi spell được cast trong AttackDeclare hoặc BlockDeclare.
        private GamePhase _priorPhase = GamePhase.PlayerPriority;

        // ── Win/Lose ──────────────────────────────────────────────
        public string gameOverMessage { get; private set; }

        // ── Events ────────────────────────────────────────────────
        public event Action<GamePhase> OnPhaseChanged;
        public event Action<int> OnRoundChanged;
        public event Action<bool> OnPriorityChanged;   // true = player priority
        public event Action<string> OnGameOver;
        public event Action OnReadyToResolveCombat;
        /// <summary>
        /// Fired khi cả 2 pass trong SpellStackResolve — GameController chạy coroutine
        /// resolve từng spell theo LIFO rồi trigger combat nếu combatPending.
        /// </summary>
        public event Action OnReadyToResolveSpells;

        /// <summary>Fired khi CẢ 2 bên pass ở main phase → vòng kết thúc.
        /// UI dùng để flash banner PASS đúng nghĩa "kết thúc vòng" (không hiện lúc chờ thường).</summary>
        public event Action OnRoundEndedByPass;

        // ── Constructor ───────────────────────────────────────────
        public GameModel()
        {
            player = new PlayerModel(isPlayer: true);
            enemy = new PlayerModel(isPlayer: false);
        }

        // ── Setup ─────────────────────────────────────────────────

        public void SetupDecks(DeckData playerDeck, DeckData enemyDeck)
        {
            player.BuildDeck(playerDeck);
            enemy.BuildDeck(enemyDeck);
        }

        // ── Phase Transitions ─────────────────────────────────────

        public void SetPhase(GamePhase newPhase)
        {
            phase = newPhase;
            OnPhaseChanged?.Invoke(phase);
        }

        /// <summary>
        /// Lưu phase hiện tại rồi vào SpellStackResolve.
        /// Dùng thay cho SetPhase(SpellStackResolve) trong GameController.CommitStagedSpells
        /// để ReturnToMainPhase sau đó biết cần quay lại AttackDeclare/BlockDeclare hay main phase.
        /// </summary>
        public void BeginSpellStackResolve()
        {
            // Chỉ lưu _priorPhase khi lần đầu vào SpellStackResolve.
            // Nếu đã ở trong SpellStackResolve (đối thủ respond bằng spell),
            // KHÔNG ghi đè — giữ nguyên phase gốc (AttackDeclare/BlockDeclare)
            // để ReturnToMainPhase sau này quay về đúng combat phase.
            if (phase != GamePhase.SpellStackResolve)
                _priorPhase = phase;
            SetPhase(GamePhase.SpellStackResolve);
        }

        // ── Mulligan ──────────────────────────────────────────────

        /// <summary>
        /// Bắt đầu phase mulligan — gọi sau khi draw 4 lá đầu game.
        /// </summary>
        public void StartMulligan()
        {
            _playerMulliganDone = false;
            _enemyMulliganDone = false;
            SetPhase(GamePhase.Mulligan);
        }

        /// <summary>
        /// Xác nhận mulligan cho một bên. Khi cả 2 xong → bắt đầu round 1.
        /// </summary>
        public void ConfirmMulligan(bool byPlayer)
        {
            if (byPlayer) _playerMulliganDone = true;
            else _enemyMulliganDone = true;

            if (_playerMulliganDone && _enemyMulliganDone)
                StartRound();
        }

        public void StartRound()
        {
            // Lưu lại ai có token khi bắt đầu round — EndRound sẽ dùng để flip đúng.
            // Quan trọng: phải lưu TRƯỚC khi thay đổi bất kỳ state nào khác trong round.
            _roundStartTokenHolder = playerHasAttackToken;

            _playerPassed = false;
            _enemyPassed = false;

            // Convert mana thừa → spell mana TRƯỚC khi refill (quan trọng: đúng thứ tự)
            player.ConvertExcessToSpellMana();
            enemy.ConvertExcessToSpellMana();
            player.RefillMana();
            enemy.RefillMana();

            // Đầu game không draw thêm; từ round 2 trở đi draw 1
            if (roundNumber > 1)
            {
                player.DrawCard();
                enemy.DrawCard();
            }

            attackTokenUsed = false;   // reset đầu round mới
            OnRoundChanged?.Invoke(roundNumber);
            SetPhase(GamePhase.PlayerPriority);
            // Bên có attack token được priority trước mỗi round (giống Runeterra)
            GivePriorityTo(playerHasAttackToken);
        }

        // ── Priority ──────────────────────────────────────────────

        public void GivePriorityTo(bool isPlayer)
        {
            isPlayerPriority = isPlayer;
            OnPriorityChanged?.Invoke(isPlayerPriority);
        }

        /// <summary>
        /// Dùng khi một hành động (đặt bài, commit spell, v.v.) chuyển priority sang đối thủ,
        /// nhưng KHÔNG tính là "pass" để kết thúc phase/round.
        /// Reset cả 2 cờ pass — vì có state mới, pass cũ của bên kia không còn giá trị.
        /// </summary>
        public void HandOffPriority(bool byPlayer)
        {
            _playerPassed = false;
            _enemyPassed = false;
            GivePriorityTo(!byPlayer);
        }

        /// <summary>
        /// Pass priority sang đối thủ (explicit — bấm End Turn / Pass, không làm gì cả).
        /// Nếu cả hai đều pass liên tiếp (không ai hành động ở giữa) → xử lý cuối phase.
        /// </summary>
        public void PassPriority(bool byPlayer)
        {
            if (byPlayer) _playerPassed = true;
            else _enemyPassed = true;

            if (_playerPassed && _enemyPassed)
            {
                _playerPassed = false;
                _enemyPassed = false;
                OnBothPassed();
            }
            else
            {
                // Chuyển priority sang đối thủ
                GivePriorityTo(!byPlayer);
            }
        }

        private void OnBothPassed()
        {
            switch (phase)
            {
                case GamePhase.PlayerPriority:
                case GamePhase.EnemyPriority:
                    // Không ai làm gì → kết thúc round. Báo UI để flash banner PASS.
                    OnRoundEndedByPass?.Invoke();
                    EndRound();
                    break;

                case GamePhase.AttackDeclare:
                    SetPhase(GamePhase.BlockDeclare);
                    // Priority chuyển sang bên không có attack token
                    GivePriorityTo(!playerHasAttackToken);
                    break;

                case GamePhase.BlockDeclare:
                    SetPhase(GamePhase.CombatResolve);
                    OnReadyToResolveCombat?.Invoke();
                    break;

                case GamePhase.SpellStackResolve:
                    restrictToSpellsOnly = false;
                    // Delegate sang GameController coroutine để resolve tuần tự:
                    //   1. ResolveTop từng spell (LIFO) → Notify → delay → lặp đến hết stack
                    //   2. Nếu combatPending → SetPhase(CombatResolve) → sequential combat
                    //      Nếu không → ReturnToMainPhase()
                    OnReadyToResolveSpells?.Invoke();
                    break;
            }
        }

        /// <summary>
        /// Resolve entry trên cùng của stack, sau đó:
        /// - Nếu stack rỗng: tắt restriction, quay về main phase.
        /// - Nếu còn entry: reset pass flags, cập nhật restriction target theo
        ///   spell mới trên top, mở response window mới (priority về bên attack token).
        ///
        /// Đúng với LoR: mỗi spell resolve → response window mới → cả 2 pass → spell tiếp theo.
        /// </summary>
        private void ResolveOneSpellAndContinue()
        {
            spellStack.ResolveTop(this);

            if (spellStack.IsEmpty)
            {
                // Stack rỗng — xóa restriction, về main phase
                restrictToSpellsOnly = false;
                ReturnToMainPhase();
            }
            else
            {
                // Còn spell trên stack — mở response window mới
                _playerPassed = false;
                _enemyPassed = false;

                // Restriction target = đối thủ của owner spell đang ở trên cùng.
                // Ví dụ: stack còn spell của Player → restrict Enemy (chỉ Fast/Burst).
                var top = spellStack.PeekTop();
                SetRestrictToSpellsOnly(true);
                SetSpellRestrictionTarget(!top.owner.isPlayer);

                // Priority về bên có attack token để bắt đầu response window mới.
                // (Bên bị restrict vẫn có thể cast Fast/Burst nếu họ nhận được priority.)
                GivePriorityTo(playerHasAttackToken);
            }
        }

        /// <summary>
        /// Chuyển attack token sang bên kia (defender nhận token sau khi combat xong).
        /// Gọi từ GameController.ResolveCombatSequentially sau mỗi lần combat resolve.
        /// Đây là mid-round transfer — EndRound() sẽ dùng _roundStartTokenHolder để flip đúng.
        /// </summary>
        public void TransferAttackToken()
        {
            playerHasAttackToken = !playerHasAttackToken;

        }

        public void EndRound()
        {
            SetPhase(GamePhase.RoundEnd);
            // Dùng _roundStartTokenHolder (ai có token đầu round) để flip — KHÔNG dùng
            // playerHasAttackToken hiện tại vì có thể đã bị thay đổi bởi TransferAttackToken().
            // Đảm bảo round tiếp theo luôn bắt đầu đúng thứ tự luân phiên (Player → Enemy → Player ...).
            playerHasAttackToken = !_roundStartTokenHolder;
            roundNumber++;
            StartRound();
        }

        /// <summary>
        /// Sau khi spell stack resolve xong, quay lại đúng phase:
        /// - Nếu spell được cast trong AttackDeclare/BlockDeclare (combatPending = true):
        ///   → quay lại phase combat đó, mở lại response window với priority đúng.
        /// - Nếu không (main phase): → về PlayerPriority/EnemyPriority như bình thường.
        ///
        /// Public để GameController.HandleUndoCastSpell cũng có thể gọi khi stack về rỗng.
        /// </summary>
        public void ReturnToMainPhase()
        {
            bool wasCombat = _priorPhase == GamePhase.AttackDeclare
                          || _priorPhase == GamePhase.BlockDeclare;

            if (wasCombat && combatPending)
            {
                // Quay lại phase combat trước khi spell được cast.
                // Luôn trả priority về bên PHÒNG THỦ (!playerHasAttackToken) để họ block ngay.
                //
                // Lý do: Theo flow LoR gốc, sau khi attacker lock attack, defender nhận window
                // để vừa block vừa respond spell. Khi spell resolve, defender cần block ngay
                // không cần attacker phải pass thêm một lần nữa.
                //
                // Không cần phân biệt AttackDeclare vs BlockDeclare: cả hai đều trả về defender.
                // BlockDeclare: defender tiếp tục declare blocker hoặc pass.
                // AttackDeclare: defender có thể block ngay (không cần chờ attacker pass).
                SetPhase(_priorPhase);
                GivePriorityTo(!playerHasAttackToken);  // luôn về bên phòng thủ
            }
            else
            {
                // Main phase thường — dùng isPlayerPriority (ai vừa commit spell).
                SetPhase(isPlayerPriority ? GamePhase.PlayerPriority : GamePhase.EnemyPriority);
            }
        }

        // ── Game Over ─────────────────────────────────────────────

        public void CheckGameOver()
        {
            if (!player.IsAlive)
            {
                gameOverMessage = "Bạn đã thua!";
                SetPhase(GamePhase.GameOver);
                OnGameOver?.Invoke(gameOverMessage);
            }
            else if (!enemy.IsAlive)
            {
                gameOverMessage = "Bạn đã thắng!";
                SetPhase(GamePhase.GameOver);
                OnGameOver?.Invoke(gameOverMessage);
            }
        }

        // ── Helpers ───────────────────────────────────────────────

        public PlayerModel GetOpponent(PlayerModel p) =>
            p.isPlayer ? enemy : player;

        public PlayerModel GetActiveAttacker() =>
            playerHasAttackToken ? player : enemy;

        public PlayerModel GetActiveDefender() =>
            playerHasAttackToken ? enemy : player;
    }
}