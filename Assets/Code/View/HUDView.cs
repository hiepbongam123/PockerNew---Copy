using System.Collections;
using UnityEngine;
using UnityEngine.UI;
using TMPro;
using LoRClone.Model;
using LoRClone.Controller;

namespace LoRClone.View
{
    public class HUDView : MonoBehaviour
    {
        [Header("Player stats")]
        public TextMeshProUGUI playerHealthText;
        public TextMeshProUGUI playerManaText;

        [Header("Enemy stats")]
        public TextMeshProUGUI enemyHealthText;
        public TextMeshProUGUI enemyManaText;

        [Header("Mana Icons — Regular (max 10)")]
        [Tooltip("Kéo 10 Image gem mana của player vào đây.")]
        public Image[] playerManaIcons = new Image[9];
        [Tooltip("Kéo 9 Image gem mana của enemy vào đây.")]
        public Image[] enemyManaIcons = new Image[9];
        [Tooltip("Gem còn mana — xanh dương.")]
        public Color manaFilled = new Color(0.20f, 0.55f, 0.95f, 1f);
        [Tooltip("Gem đã dùng trong round — tối/outline.")]
        public Color manaEmpty = new Color(1f, 1f, 1f, 0.20f);
        [Tooltip("Gem chưa unlock — màu khóa.")]
        public Color manaLocked = new Color(1f, 1f, 1f, 0.05f);

        [Header("Spell Mana Icons (max 3)")]
        public TextMeshProUGUI playerSpellManaText;
        public TextMeshProUGUI enemySpellManaText;
        [Tooltip("Kéo 3 Image ngôi sao của player vào đây (index 0=trái → 2=phải).")]
        public Image[] playerSpellManaIcons = new Image[5];
        [Tooltip("Kéo 5 Image ngôi sao của enemy vào đây (index 0=trái → 4=phải).")]
        public Image[] enemySpellManaIcons = new Image[5];
        [Tooltip("Màu khi ô spell mana có giá trị — cyan như LoR gốc.")]
        public Color spellManaFilled = new Color(0.20f, 0.90f, 0.80f, 1f);
        [Tooltip("Màu khi ô spell mana trống.")]
        public Color spellManaEmpty = new Color(1f, 1f, 1f, 0.25f);

        [Header("Game info")]
        public TextMeshProUGUI roundText;
        public TextMeshProUGUI phaseText;       // optional debug
        public TextMeshProUGUI priorityText;    // "Player's Turn" / "Enemy's Turn"

        [Header("Single Action Button")]
        public Button actionButton;
        public Image actionButtonImage;         // background Image (nếu dùng)
        public RawImage actionButtonRawImage;   // RawImage ngoài (mới thêm)
        public TextMeshProUGUI actionButtonText;

        [Header("Button Colors")]
        public Color colorPass = new Color(0.45f, 0.45f, 0.45f);   // xám  — PASS / OK
        public Color colorAttack = new Color(0.95f, 0.50f, 0.10f);   // cam  — ATTACK!
        public Color colorConfirm = new Color(0.20f, 0.55f, 0.95f);   // xanh — CONFIRM / RESPOND
        public Color colorBlock = new Color(0.20f, 0.75f, 0.35f);   // lá   — CONFIRM BLOCKS
        public Color colorWait = new Color(0.28f, 0.28f, 0.28f, 0.75f); // xám mờ — WAITING (lượt đối thủ)

        [Header("Attack Token — ai được tấn công round này")]
        [Tooltip("Hiện khi player có attack token round này.")]
        public GameObject playerAttackToken;
        [Tooltip("Hiện khi enemy có attack token round này.")]
        public GameObject enemyAttackToken;
        [Tooltip("Màu khi còn lượt tấn công.")]
        public Color colorAttackTokenReady = Color.white;
        [Tooltip("Màu khi đã tấn công xong round này.")]
        public Color colorAttackTokenUsed = new Color(0.35f, 0.35f, 0.35f);

        [Header("Priority Indicator — ai đang có quyền hành động")]
        [Tooltip("Hiện khi player đang có priority.")]
        public GameObject playerPriorityIndicator;
        [Tooltip("Hiện khi enemy đang có priority.")]
        public GameObject enemyPriorityIndicator;

        // ── Deck Display ──────────────────────────────────────────
        // Setup trong Inspector:
        //   1. Tạo Image GO làm ảnh deck, thêm Button component, bật Raycast Target.
        //   2. Tạo Panel con bên dưới HUD (mặc định inactive).
        //   3. Trong Panel thêm TextMeshProUGUI để hiện số liệu.
        //   4. Kéo đúng slot vào đây.
        [Header("Deck Display — Player")]
        [Tooltip("Button component trên ảnh deck player.")]
        public Button playerDeckButton;
        [Tooltip("Panel popup hiện khi click — mặc định inactive trong Inspector.")]
        public GameObject playerDeckPopup;
        [Tooltip("TextMeshPro bên trong popup — nội dung tự cập nhật sau mỗi action.")]
        public TextMeshProUGUI playerDeckPopupText;

        [Header("Deck Display — Enemy")]
        [Tooltip("Button component trên ảnh deck enemy.")]
        public Button enemyDeckButton;
        [Tooltip("Panel popup hiện khi click deck enemy.")]
        public GameObject enemyDeckPopup;
        [Tooltip("TextMeshPro bên trong popup enemy.")]
        public TextMeshProUGUI enemyDeckPopupText;

        [Header("Game Over")]
        public GameObject gameOverPanel;
        public TextMeshProUGUI gameOverText;
        public Button restartButton;

        public void Init()
        {
            actionButton?.onClick.AddListener(() =>
            {
                var gc = GameController.Instance;
                if (gc.networkMode)
                {
                    bool meIsP = GameController.NetLocalIsPlayer;
                    bool meHasPriority = meIsP ? gc.model.isPlayerPriority : !gc.model.isPlayerPriority;
                    if (!meHasPriority) return;                       // chỉ pass khi tới lượt MÌNH
                    gc.SubmitLocal(new PassPriorityAction(meIsP));    // byPlayer = phe local
                }
                else gc.SubmitLocal(new PassPriorityAction(gc.model.isPlayerPriority));
            });

            restartButton?.onClick.AddListener(() => ScreenFade.LoadScene(0));

            if (gameOverPanel) gameOverPanel.SetActive(false);

            // ── Deck popups — ẩn mặc định ─────────────────────────
            if (playerDeckPopup) playerDeckPopup.SetActive(false);
            if (enemyDeckPopup) enemyDeckPopup.SetActive(false);

            // Click deck player: toggle popup player, đóng popup enemy
            playerDeckButton?.onClick.AddListener(() =>
            {
                bool willShow = playerDeckPopup != null && !playerDeckPopup.activeSelf;
                if (enemyDeckPopup) enemyDeckPopup.SetActive(false);
                if (playerDeckPopup) playerDeckPopup.SetActive(willShow);
            });

            // Click deck enemy: toggle popup enemy, đóng popup player
            enemyDeckButton?.onClick.AddListener(() =>
            {
                bool willShow = enemyDeckPopup != null && !enemyDeckPopup.activeSelf;
                if (playerDeckPopup) playerDeckPopup.SetActive(false);
                if (enemyDeckPopup) enemyDeckPopup.SetActive(willShow);
            });

            // ── PASS banner: ẩn mặc định, chỉ flash khi hết vòng ──
            if (playerPriorityIndicator) playerPriorityIndicator.SetActive(false);
            if (enemyPriorityIndicator) enemyPriorityIndicator.SetActive(false);

            var gcRef = GameController.Instance;
            if (gcRef != null && gcRef.model != null)
            {
                gcRef.model.OnRoundEndedByPass -= FlashPassBanner; // tránh double-subscribe
                gcRef.model.OnRoundEndedByPass += FlashPassBanner;
            }
        }

        [Header("Pass Banner")]
        [Tooltip("Số giây banner PASS hiện khi hết vòng (cả 2 pass). 0 = tắt hẳn banner.")]
        public float passBannerSeconds = 1.5f;

        Coroutine _passBannerCo;

        /// <summary>Flash banner PASS 2 phe khi CẢ 2 pass → hết vòng, rồi tự ẩn. Không hiện lúc chờ thường.</summary>
        void FlashPassBanner()
        {
            if (passBannerSeconds <= 0f) return;
            if (_passBannerCo != null) StopCoroutine(_passBannerCo);
            _passBannerCo = StartCoroutine(PassBannerCo());
        }

        System.Collections.IEnumerator PassBannerCo()
        {
            if (playerPriorityIndicator) playerPriorityIndicator.SetActive(true);
            if (enemyPriorityIndicator) enemyPriorityIndicator.SetActive(true);
            yield return new WaitForSecondsRealtime(passBannerSeconds);
            if (playerPriorityIndicator) playerPriorityIndicator.SetActive(false);
            if (enemyPriorityIndicator) enemyPriorityIndicator.SetActive(false);
            _passBannerCo = null;
        }

        void OnDestroy()
        {
            var gcRef = GameController.Instance;
            if (gcRef != null && gcRef.model != null)
                gcRef.model.OnRoundEndedByPass -= FlashPassBanner;
        }

        public void Render(GameModel m)
        {
            // ── CANONICAL PvP: remap phe theo góc nhìn máy local ──
            bool meIsP = GameController.NetLocalIsPlayer;
            var meP = meIsP ? m.player : m.enemy;
            var oppP = meIsP ? m.enemy : m.player;
            bool meHasPriority = meIsP ? m.isPlayerPriority : !m.isPlayerPriority;
            bool meHasToken = meIsP ? m.playerHasAttackToken : !m.playerHasAttackToken;

            // ── Stats ──────────────────────────────────────────────
            if (playerHealthText) playerHealthText.text = $"{meP.health}";
            if (enemyHealthText) enemyHealthText.text = $"{oppP.health}";
            // FIX: hiển thị mana KHẢ DỤNG (đã trừ phần đặt cọc cho spell đang staging).
            // → thanh mana tụt NGAY khi kéo spell vào Spell Zone (chưa cần bấm Confirm),
            //   đúng cảm giác LoR gốc, cho cả player lẫn enemy.
            int meReg = AvailableRegularMana(meP), meSpell = AvailableSpellMana(meP);
            int oppReg = AvailableRegularMana(oppP), oppSpell = AvailableSpellMana(oppP);
            if (playerManaText) playerManaText.text = $"{meReg}/";
            if (enemyManaText) enemyManaText.text = $"{oppReg}/";
            RefreshManaIcons(playerManaIcons, meReg, meP.maxMana);
            RefreshManaIcons(enemyManaIcons, oppReg, oppP.maxMana);
            if (playerSpellManaText) playerSpellManaText.text = $"{meSpell}/";
            if (enemySpellManaText) enemySpellManaText.text = $"{oppSpell}/";
            RefreshSpellManaIcons(playerSpellManaIcons, meSpell);
            RefreshSpellManaIcons(enemySpellManaIcons, oppSpell);
            if (roundText) roundText.text = $"Round {m.roundNumber}";
            if (phaseText) phaseText.text = m.phase.ToString();

            // ── Deck popup text — cập nhật dù popup đang ẩn ───────
            // Dữ liệu luôn fresh vì Render() gọi mỗi OnStateChanged.
            if (playerDeckPopupText)
                playerDeckPopupText.text =
                    $"BÀI CÒN LẠI: {meP.deck.Count}\n" +
                    $"TRÊN TAY: {meP.hand.Count}/{PlayerModel.MaxHandSize}";

            if (enemyDeckPopupText)
                enemyDeckPopupText.text =
                    $"BÀI CÒN LẠI: {oppP.deck.Count}\n" +
                    $"TRÊN TAY: {oppP.hand.Count}/{PlayerModel.MaxHandSize}";

            // ── Priority label: ĐÃ GỠ ─────────────────────────────
            // Trước đây set "Player Turn / Enemy Turn" liên tục → gây rối.
            // Nút hành động (WAITING/ATTACK/BLOCK/CONFIRM/PASS) đã là chỉ báo lượt chính.

            // ── Attack Token: chỉ hiện bên đang có token, đổi màu khi đã dùng ──
            if (playerAttackToken) playerAttackToken.SetActive(meHasToken);
            if (enemyAttackToken) enemyAttackToken.SetActive(!meHasToken);

            Color tokenColor = m.attackTokenUsed ? colorAttackTokenUsed : colorAttackTokenReady;
            var activeToken = meHasToken ? playerAttackToken : enemyAttackToken;
            if (activeToken != null)
            {
                var img = activeToken.GetComponent<Image>();
                var raw = activeToken.GetComponent<RawImage>();
                if (img) img.color = tokenColor;
                if (raw) raw.color = tokenColor;
            }

            // ── PASS banner: KHÔNG điều khiển ở Render nữa ─────────
            // Banner chỉ flash lúc CẢ 2 pass → hết vòng, do coroutine FlashPassBanner() xử lý
            // (kích qua event GameModel.OnRoundEndedByPass). Render để yên, tránh hiện liên tục.

            // ── Game Over ──────────────────────────────────────────
            if (m.phase == GamePhase.GameOver)
            {
                // Đóng popup deck khi game kết thúc
                if (playerDeckPopup) playerDeckPopup.SetActive(false);
                if (enemyDeckPopup) enemyDeckPopup.SetActive(false);
                ShowGameOver(m.gameOverMessage);
                if (actionButton) actionButton.gameObject.SetActive(false);
                return;
            }

            // ── Single Action Button ───────────────────────────────
            if (actionButton) actionButton.gameObject.SetActive(true);
            // PvP: chỉ bấm được khi tới lượt MÌNH (PvE luôn bật như cũ).
            // + Vô hiệu hóa khi GameController đang bận animation/coroutine hệ thống (combat resolve,
            //   spell stack resolve, summon sequence...) — tránh spam-click gây bug thứ tự/combat.
            if (actionButton)
                actionButton.interactable = (!GameController.Instance.networkMode || meHasPriority)
                                          && !GameController.Instance.IsBusy;

            // Nút = "thao tác game" theo góc nhìn NGƯỜI CHƠI LOCAL.
            // Chưa tới lượt mình (hoặc đang resolve animation) → WAITING (mờ, không bấm được).
            bool myTurn = meHasPriority && !GameController.Instance.IsBusy;
            var (label, color) = myTurn
                ? GetButtonState(m, meP)
                : ("WAITING…", colorWait);

            if (actionButtonText) actionButtonText.text = label;
            if (actionButtonImage) actionButtonImage.color = color;
            if (actionButtonRawImage) actionButtonRawImage.color = color;
        }

        // Trả về nhãn + màu cho nút, GIẢ ĐỊNH đang là lượt của player local (meP = phe mình).
        // Ưu tiên: có spell đang staging → CONFIRM (commit) ở mọi phase. Còn lại theo phase.
        (string label, Color color) GetButtonState(GameModel m, PlayerModel meP)
        {
            // Có spell đang staging → nút luôn là CONFIRM (bấm để commit lá lên stack).
            if (meP.stagedSpells.Count > 0)
                return ("CONFIRM", colorConfirm);

            switch (m.phase)
            {
                // ── Attack Declare: mình là bên tấn công ───────────
                case GamePhase.AttackDeclare:
                    {
                        bool hasAttackers = false;
                        foreach (var c in meP.BattlefieldCards())
                            if (c.state == CardState.Attacking) { hasAttackers = true; break; }
                        return hasAttackers
                            ? ("ATTACK", colorAttack)   // đã chọn attacker → xác nhận tấn công
                            : ("PASS", colorPass);      // chưa chọn ai → bỏ lượt tấn công
                    }

                // ── Block Declare: mình là bên phòng thủ ───────────
                case GamePhase.BlockDeclare:
                    {
                        bool hasBlockers = false;
                        foreach (var c in meP.BattlefieldCards())
                            if (c.state == CardState.Blocking) { hasBlockers = true; break; }
                        return hasBlockers
                            ? ("CONFIRM BLOCK", colorBlock) // đã chọn chặn → xác nhận
                            : ("PASS", colorPass);          // không chặn → cho combat diễn ra
                    }

                // ── Đấu phép: pass để nhường/kết thúc response window ──
                case GamePhase.SpellStackResolve:
                    return ("PASS", colorPass);

                // ── Main phase & còn lại: pass lượt ────────────────
                case GamePhase.PlayerPriority:
                case GamePhase.EnemyPriority:
                default:
                    return ("PASS", colorPass);
            }
        }

        // ── Mana khả dụng (trừ đặt cọc spell đang staging) ────────
        // Thứ tự tiêu khớp SpendManaForSpell/AvailableRegularMana bên GameController:
        // spell mana bị tiêu TRƯỚC, phần dư mới lấy từ mana thường.
        static int AvailableRegularMana(PlayerModel p)
        {
            int staged = p.TotalStagedManaCost();
            int fromSpell = Mathf.Min(p.spellMana, staged);
            int fromRegular = staged - fromSpell;
            return Mathf.Max(0, p.mana - fromRegular);
        }

        static int AvailableSpellMana(PlayerModel p)
        {
            int staged = p.TotalStagedManaCost();
            int fromSpell = Mathf.Min(p.spellMana, staged);
            return Mathf.Max(0, p.spellMana - fromSpell);
        }

        void RefreshManaIcons(Image[] icons, int current, int max)
        {
            if (icons == null) return;
            for (int i = 0; i < icons.Length; i++)
            {
                if (icons[i] == null) continue;
                if (i < current) icons[i].color = manaFilled;   // còn mana
                else if (i < max) icons[i].color = manaEmpty;    // đã dùng
                else icons[i].color = manaLocked;   // chưa unlock
            }
        }

        void RefreshSpellManaIcons(Image[] icons, int spellMana)
        {
            if (icons == null) return;
            for (int i = 0; i < icons.Length; i++)
                if (icons[i]) icons[i].color = i < spellMana ? spellManaFilled : spellManaEmpty;
        }

        void ShowGameOver(string msg)
        {
            if (gameOverPanel) gameOverPanel.SetActive(true);
            if (gameOverText) gameOverText.text = msg;
        }
    }
}