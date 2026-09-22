using System.Collections.Generic;
using LoRClone.Data;
using LoRClone.Model;

namespace LoRClone.Controller
{
    /// <summary>
    /// FILE 4/5 — Network-mode (CANONICAL lockstep).
    /// Đặt ở: Assets/Code/Controller/GameController.Net.cs  (CHỈ để 1 bản duy nhất!)
    ///
    /// Canonical: TRÊN CẢ 2 MÁY, model.player = HOST, model.enemy = CLIENT (giống hệt nhau).
    /// → token/priority gán 1 phe duy nhất, id/deck khớp, không lệch.
    /// Client lật giao diện để thấy phe mình (model.enemy) ở dưới.
    /// </summary>
    public partial class GameController
    {
        // Set bởi NetworkLauncher TRƯỚC khi load scene game → Start() không auto StartGame.
        public static bool PendingNetworkMode;

        // Máy này điều khiển phe player? host=true (model.player), client=false (model.enemy).
        // Dùng bởi GameView/MulliganView để lật view + xác định phe local.
        public static bool NetLocalIsPlayer = true;

        public bool networkMode { get; private set; }

        int _seedPlayer, _seedEnemy;
        bool _netSeedsPending;

        /// <summary>Bắt đầu ván canonical. GỌI GIỐNG HỆT trên 2 máy: player=deck host, enemy=deck client.</summary>
        public void StartNetworkGame(DeckData hostDeck, DeckData clientDeck, int seedPlayer, int seedEnemy)
        {
            networkMode = true;

            // ★ CÔ LẬP PvP ↔ PoC — XOÁ trang bị RUN của ải PoC (RunItems static) TRƯỚC khi dựng deck.
            //   VÌ SAO: máy đã chơi PoC còn RunItems._byCard/_byEnemyUnit. StartGame() → PlayerModel.SeedCopyToDeck
            //   gọi RunItems.Apply(isPlayer) → buff thêm lá host CHỈ trên máy đó → máy chưa chơi PoC không có → DESYNC
            //   (VD champion atk 5 vs 2). Trang bị META gauntlet đến qua ChampionLoadoutApplier (SỐ trong loadout),
            //   KHÔNG qua RunItems → xoá ở đây KHÔNG mất buff hợp lệ. ClearRun() gồm cả _byEnemyUnit → 2 máy đều rỗng.
            LoRClone.Model.RunItems.ClearRun();

            playerAutoPlay = false;       // ★ FIX: PvP KHÔNG dùng auto. Nếu cờ này bị tick sẵn trong
                                          //   Inspector, GameView guard (|| playerAutoPlay) sẽ chặn mở
                                          //   mũi tên targeting unit-skill → 2 máy kẹt. Ép tắt tại đây.
            loopMode = false;             // PvP v1: tắt LoopMode (lá copy runtime netId=-1 + RNG chưa seed → lệch)
            playerDeckData = hostDeck;    // player = HOST trên mọi máy
            enemyDeckData = clientDeck;  // enemy  = CLIENT trên mọi máy
            _seedPlayer = seedPlayer;
            _seedEnemy = seedEnemy;
            _netSeedsPending = true;
            GameRNG.Seed(unchecked(seedPlayer * 31 + seedEnemy)); // random skill đồng bộ 2 máy
            StartGame();
            AssignNetIds();

            // ★ PvP-RPG (Đấu Trường Anh Hùng): áp loadout 2 phe — DETERMINISTIC, giống hệt 2 máy.
            //   CHỈ chạy ở đúng mode này. PvP thường / mọi mode khác → KHÔNG áp → hành vi cũ giữ nguyên.
            //   ChampionLoadoutApplier.Apply() tự no-op nếu loadout null → an toàn kể cả khi handshake
            //   chưa gửi loadout (không crash).
            if (MatchContext.mode == MatchMode.PvP_Gauntlet)
            {
                ChampionLoadoutApplier.Apply(
                    model.player, MatchContext.playerLoadout, MatchContext.playerChampionCard);
                ChampionLoadoutApplier.Apply(
                    model.enemy, MatchContext.enemyLoadout, MatchContext.enemyChampionCard);
            }

            // ★ RANK: cuối trận PvP (thường + gauntlet) → tính điểm MMR + đẩy leaderboard. Mỗi trận 1 lần.
            LoRClone.Net.RankSystem.BeginMatch();
            if (model != null) model.OnGameOver += _ => LoRClone.Net.RankSystem.ReportMatch(this);
        }

        /// <summary>HOOK — gọi trong StartGame NGAY SAU 'model = new GameModel();'. Set seed trước khi shuffle.</summary>
        public void ApplyNetworkSeedsIfPending()
        {
            if (!_netSeedsPending || model == null) return;
            model.player.SetDeterministicSeed(_seedPlayer);
            model.enemy.SetDeterministicSeed(_seedEnemy);
            _netSeedsPending = false;
        }

        /// <summary>
        /// Gán netId theo THỨ TỰ TẠO LÁ (sort theo CardModel.id) — bền vững, không phụ thuộc
        /// xáo bài / mulligan / rút bài. Vì BuildDeck tạo lá theo deckData.cards giống hệt 2 máy,
        /// thứ tự tương đối của id là như nhau → netId trỏ đúng cùng 1 lá logic trên cả 2 máy.
        /// </summary>
        void AssignNetIds()
        {
            int n = 0;
            n = AssignOwnerByCreation(model.player, n);
            n = AssignOwnerByCreation(model.enemy, n);
        }

        int AssignOwnerByCreation(PlayerModel p, int start)
        {
            var all = new List<CardModel>();
            all.AddRange(p.hand);
            all.AddRange(p.deck);
            all.AddRange(p.BenchCards());
            all.AddRange(p.BattlefieldCards());
            all.Sort((a, b) => a.id.CompareTo(b.id)); // thứ tự tạo (id tăng dần) — giống nhau 2 máy
            foreach (var c in all) c.netId = start++;
            return start;
        }

        // ── Relay action ────────────────────────────────────────────
        public event System.Action<PlayerAction> OnLocalActionForNetwork;

        /// <summary>GameView gọi cái này THAY cho HandleAction. PvE: y hệt. PvP: áp + phát cho máy kia.</summary>
        public void SubmitLocal(PlayerAction action)
        {
            HandleAction(action);
            if (networkMode) OnLocalActionForNetwork?.Invoke(action);
        }

        // ── Relay mulligan ──────────────────────────────────────────
        public event System.Action<List<CardModel>> OnLocalMulliganForNetwork;

        /// <summary>MulliganView gọi cái này. byPlayer = phe local (NetLocalIsPlayer).</summary>
        public void SubmitLocalMulligan(List<CardModel> swap, bool byPlayer)
        {
            HandleMulliganConfirm(byPlayer, swap);
            if (networkMode) OnLocalMulliganForNetwork?.Invoke(swap);
        }

        // ── Relay target chọn cho spell/unit-skill ──────────────────
        public event System.Action<CardModel, List<CardModel>> OnLocalSpellTargetsForNetwork;

        /// <summary>GameView gọi THAY cho SetSpellTargets. PvE: y hệt. PvP: set + gửi target cho máy kia.</summary>
        public void SubmitLocalSpellTargets(CardModel source, List<CardModel> targets)
        {
            SetSpellTargets(source, targets);
            if (networkMode) OnLocalSpellTargetsForNetwork?.Invoke(source, targets);
        }

        // ── Relay target cho UNIT-SKILL (keyed theo netId của UNIT) ─────────────────
        // Vì lá skill sinh runtime có netId=-1, KHÔNG relay theo lá skill được.
        // Ta relay theo netId của chính UNIT (ổn định, gán lúc đầu ván): "unit X chọn các target Y".
        // Máy kia (không phải chủ unit) sẽ CHỜ gói này rồi mới commit skill — xem StageUnitSkillSpell.
        public event System.Action<CardModel, int, List<CardModel>> OnLocalUnitSkillTargetsForNetwork;

        // ── B-FIX: activation "generation" theo từng UNIT ──────────────────────────
        // Mỗi lần 1 unit kích skill cần target, gen của unit đó +1. StageUnitSkillSpell/
        // StageBattleSkillsCo chạy LOCKSTEP trên cả 2 máy (đều từ PlayUnit/Attack relay) nên
        // gen tăng ĐỒNG BỘ → cùng activation có cùng gen ở 2 máy.
        // Key hàng chờ = (unitNetId, gen): gói target CŨ (gen thấp) đến muộn sẽ KHÔNG bị lần
        // activation sau (gen cao) nhặt nhầm → hết bug "stale target". An toàn cả khi gói tới sớm
        // (lưu đúng key gen hiện tại, không cần xoá trước khi chờ).
        readonly Dictionary<int, int> _unitSkillGenByUnit = new Dictionary<int, int>();
        readonly Dictionary<(int unitNetId, int gen), List<CardModel>> _pendingUnitSkillTargets
            = new Dictionary<(int, int), List<CardModel>>();

        /// <summary>+1 gen cho unit này rồi trả về. GỌI TRÊN CẢ 2 MÁY (lockstep) mỗi activation.</summary>
        public int NextUnitSkillGen(int unitNetId)
        {
            _unitSkillGenByUnit.TryGetValue(unitNetId, out var g);
            g++;
            _unitSkillGenByUnit[unitNetId] = g;
            return g;
        }

        /// <summary>CHỦ unit gọi (trong PvP) để phát lựa chọn target của mình cho máy kia.</summary>
        public void SubmitLocalUnitSkillTargets(CardModel unit, int gen, List<CardModel> targets)
        {
            if (networkMode && unit != null)
                OnLocalUnitSkillTargetsForNetwork?.Invoke(unit, gen, targets);
        }

        /// <summary>NetworkBridge gọi khi nhận RPC target unit-skill từ chủ unit (máy kia).</summary>
        public void ReceiveUnitSkillTargets(int unitNetId, int gen, List<CardModel> targets)
        {
            // Dọn gói cũ hơn của cùng unit (nếu có) — tránh rò rỉ + loại stale triệt để.
            var stale = new List<(int, int)>();
            foreach (var k in _pendingUnitSkillTargets.Keys)
                if (k.unitNetId == unitNetId && k.gen < gen) stale.Add(k);
            foreach (var k in stale) _pendingUnitSkillTargets.Remove(k);

            _pendingUnitSkillTargets[(unitNetId, gen)] = targets ?? new List<CardModel>();
        }

        /// <summary>Coroutine lấy target đã relay (theo unit + gen). Trả false nếu chưa tới.</summary>
        public bool TryTakeUnitSkillTargets(int unitNetId, int gen, out List<CardModel> targets)
        {
            if (_pendingUnitSkillTargets.TryGetValue((unitNetId, gen), out targets))
            {
                _pendingUnitSkillTargets.Remove((unitNetId, gen));
                return true;
            }
            targets = null;
            return false;
        }

        // ── Desync detector: "vân tay" state chi tiết (canonical → giống nhau khi đồng bộ) ──
        public string StateFingerprint()
        {
            var sb = new System.Text.StringBuilder();
            // KHÔNG so ph (phase) và pri (priority): chúng "lắng" chậm khác nhau do animation → báo giả.
            // Chỉ so state cứng: round, token, và toàn bộ bài/máu/mana/deck.
            sb.Append($"r{model.roundNumber}|tok{(model.playerHasAttackToken ? 1 : 0)}");
            sb.Append(" |P:"); AppendSide(sb, model.player);
            sb.Append(" |E:"); AppendSide(sb, model.enemy);
            // Gộp chữ ký loadout (chỉ Gauntlet) — bắt lệch loadout 2 máy. Đặt CUỐI để token vòng "rN"
            // vẫn ở ĐẦU chuỗi (round-guard của RpcHash cắt phần trước '|' đầu — không được phá).
            string loSig = MatchContext.LoadoutSig();
            if (!string.IsNullOrEmpty(loSig)) sb.Append(" |LO:").Append(loSig);
            return sb.ToString();
        }

        static void AppendSide(System.Text.StringBuilder sb, PlayerModel p)
        {
            sb.Append($"hp{p.health},mana{p.mana}/{p.maxMana},sm{p.spellMana},hand{p.hand.Count},deck{p.deck.Count},bench[");
            foreach (var c in p.BenchCards()) AppendCard(sb, c);
            sb.Append("],bf[");
            foreach (var c in p.BattlefieldCards()) AppendCard(sb, c);
            sb.Append("],handIds[");
            foreach (var c in p.hand) sb.Append($"{c.netId},");
            sb.Append("],deckOrder[");
            foreach (var c in p.deck) sb.Append($"{c.netId},");
            sb.Append("]");
        }

        // 1 lá trên sân: netId=atk/hp + cost hiện tại + số keyword → bắt lệch giảm-giá / cấp-keyword (loadout).
        static void AppendCard(System.Text.StringBuilder sb, CardModel c)
        {
            if (c == null) return;
            int kw = 0; foreach (var _ in c.GetAllKeywords()) kw++;
            sb.Append($"{c.netId}={c.effectiveAttack}/{c.currentHealth}c{c.currentManaCost}k{kw};");
        }
    }
}