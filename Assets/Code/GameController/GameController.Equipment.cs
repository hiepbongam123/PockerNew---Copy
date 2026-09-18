using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using LoRClone.Data;
using LoRClone.Model;

namespace LoRClone.Controller
{
    // ─────────────────────────────────────────────────────────────────────────
    // PlayerAction cho Equipment (thêm vào file chứa PlayUnitAction)
    // Nếu bạn có file riêng PlayerActions.cs thì chuyển class này sang đó.
    // ─────────────────────────────────────────────────────────────────────────
    public class PlayEquipmentAction : PlayerAction
    {
        public CardModel card;
        public PlayEquipmentAction(CardModel card, bool byPlayer) : base(byPlayer)
        {
            this.card = card;
        }
    }

    // ─────────────────────────────────────────────────────────────────────────
    // GameController — Equipment (Trang Bị) logic
    // ─────────────────────────────────────────────────────────────────────────
    public partial class GameController
    {
        // Guard chặn mọi input khi đang chờ player chọn mode / chọn target cho Equipment
        bool _isEquipmentSelecting = false;

        /// <summary>
        /// ID của các Equipment card đã được chơi round này (cả equip lẫn darkin mode).
        /// Reset mỗi đầu round mới trong OnRoundChanged.
        /// Mục đích: ngăn spam re-equip 0-mana sau khi unit đeo chết → equipment về tay.
        /// </summary>
        readonly HashSet<int> _equipmentsPlayedThisRound = new HashSet<int>();

        /// <summary>
        /// Trả về true nếu equipment card này đã được chơi trong round hiện tại.
        /// EnemyAI dùng để tự skip card đã hết lượt thay vì cứ retry → vòng lặp vô tận.
        /// </summary>
        public bool IsEquipmentPlayedThisRound(int cardId) =>
            _equipmentsPlayedThisRound.Contains(cardId);

        // ── Player-facing events (GameView subscribes, shows UI) ──────────────

        /// <summary>
        /// Fired khi PLAYER chơi Equipment card có darkinUnit.
        /// GameView / EquipmentModeView subscribe để hiện panel 2 lựa chọn.
        /// KHÔNG fire cho enemy — dùng OnEquipmentNeedsEnemyMode thay thế.
        ///
        /// Callback int:
        ///   0 = Trang Bị (equip ally)
        ///   1 = Triệu Hồi Darkin (summon unit, consume card)
        ///  -1 = Hủy
        /// </summary>
        public event System.Action<CardModel, bool, System.Action<int>> OnEquipmentNeedsMode;

        /// <summary>
        /// Fired khi PLAYER chọn mode Trang Bị → cần chọn ally target.
        /// GameView subscribe để bật ally targeting UI (arrow).
        /// KHÔNG fire cho enemy — dùng OnEquipmentNeedsEnemyTarget thay thế.
        ///
        /// Callback CardModel: unit được chọn, null nếu hủy.
        /// </summary>
        public event System.Action<CardModel, bool, System.Action<CardModel>> OnEquipmentNeedsTarget;

        // ── Enemy-only events (EnemyAI subscribes, NO UI shown) ──────────────

        /// <summary>
        /// Enemy-side counterpart của OnEquipmentNeedsMode.
        /// EnemyAI subscribe — GameView KHÔNG nhận event này, không hiện panel nào.
        /// Signature khác: không có bool byPlayer (luôn là enemy).
        /// </summary>
        public event System.Action<CardModel, System.Action<int>> OnEquipmentNeedsEnemyMode;

        /// <summary>
        /// Enemy-side counterpart của OnEquipmentNeedsTarget.
        /// EnemyAI subscribe — GameView KHÔNG nhận event này, không hiện targeting arrow.
        /// Signature khác: không có bool byPlayer (luôn là enemy).
        /// </summary>
        public event System.Action<CardModel, System.Action<CardModel>> OnEquipmentNeedsEnemyTarget;

        // ── HandlePlayEquipment ───────────────────────────────────
        void HandlePlayEquipment(PlayEquipmentAction a)
        {
            var p = P(a.byPlayer);
            var card = a.card;

            if (card.data.cardType != CardType.Equipment)
            { Reject("Không phải Equipment."); return; }

            bool inMainPhase = model.phase == GamePhase.PlayerPriority
                            || model.phase == GamePhase.EnemyPriority;
            if (!inMainPhase)
            { Reject($"Không thể chơi Equipment trong phase {model.phase}."); return; }

            if (!p.hand.Contains(card))
            { Reject("Lá bài không còn trong tay."); return; }

            // Chỉ chặn thẳng ở đây nếu card KHÔNG có lựa chọn Darkin (thuần weapon) —
            // lúc đó Trang Bị là lựa chọn duy nhất và đã dùng rồi, không còn đường nào khác.
            // Nếu card CÓ Darkin, vẫn cho tiếp tục: Darkin không xung đột với việc equipment
            // đã được Trang Bị rồi bị trả về tay (do unit đeo chết) — chặn riêng nhánh Equip
            // mode bên trong EquipmentSequence, không chặn cả card ở đây.
            if (card.data.darkinUnit == null && _equipmentsPlayedThisRound.Contains(card.id))
            { Reject($"'{card.data.cardName}' đã được chơi round này — chờ round mới."); return; }

            if (p.stagedSpells.Count > 0)
            { Reject("Đang có spell staging — commit hoặc hủy trước."); return; }

            // Kiểm tra đủ mana cho ít nhất 1 mode — MỖI mode dùng loại mana riêng:
            // Equip mode  (weapon) = card.currentManaCost, trả bằng spell mana trước rồi mana thường
            // Darkin mode (unit)   = darkinUnit.manaCost,  CHỈ trả bằng mana thường (không spell mana)
            int darkinManaCost = (card.data.darkinUnit != null) ? card.data.darkinUnit.manaCost : int.MaxValue;
            bool canAffordEquip = AvailableManaForSpell(p) >= card.currentManaCost;
            bool canAffordDarkin = card.data.darkinUnit != null && AvailableRegularMana(p) >= darkinManaCost;
            if (!canAffordEquip && !canAffordDarkin)
            {
                Reject($"Thiếu mana. Trang Bị cần {card.currentManaCost} (spell+thường: còn {AvailableManaForSpell(p)}), " +
                       $"Triệu Hồi cần {darkinManaCost} (mana thường: còn {AvailableRegularMana(p)}).");
                return;
            }

            // Kiểm tra có ít nhất 1 ally trên sân (cần cho mode Trang Bị)
            // (nếu không có ally thì vẫn cho chơi nếu có Darkin mode)
            bool hasAlly = false;
            foreach (var c in p.BenchCards()) { hasAlly = true; break; }
            if (!hasAlly)
                foreach (var c in p.BattlefieldCards()) { hasAlly = true; break; }

            bool hasDarkin = card.data.darkinUnit != null;
            if (!hasAlly && !hasDarkin)
            { Reject("Không có đồng minh nào trên sân để trang bị."); return; }

            StartCoroutine(EquipmentSequence(card, a.byPlayer));
        }

        // ── EquipmentSequence ─────────────────────────────────────
        /// <summary>
        /// Coroutine xử lý Equipment play:
        ///   1. Chọn mode (Trang Bị / Triệu Hồi Darkin) nếu có Darkin.
        ///      → byPlayer=true  : fire OnEquipmentNeedsMode     (GameView hiện panel)
        ///      → byPlayer=false : fire OnEquipmentNeedsEnemyMode (EnemyAI tự chọn, không UI)
        ///   2a. Mode Triệu Hồi Darkin → SummonUnit + consume card.
        ///   2b. Mode Trang Bị → chọn ally → ApplyEquipment.
        ///      → byPlayer=true  : fire OnEquipmentNeedsTarget     (GameView hiện arrow)
        ///      → byPlayer=false : fire OnEquipmentNeedsEnemyTarget (EnemyAI tự chọn, không arrow)
        /// </summary>
        IEnumerator EquipmentSequence(CardModel card, bool byPlayer)
        {
            var p = P(byPlayer);
            bool hasDarkin = card.data.darkinUnit != null;
            int mode = 0; // 0 = equip, 1 = darkin

            // ── Bước 1: Chọn mode (nếu có Darkin) ────────────────
            if (hasDarkin)
            {
                bool modeDone = false;
                _isEquipmentSelecting = true;

                if (byPlayer && playerAutoPlay)
                {
                    bool hasAlly0 = false;
                    foreach (var c in p.BenchCards()) { hasAlly0 = true; break; }
                    if (!hasAlly0) foreach (var c in p.BattlefieldCards()) { hasAlly0 = true; break; }
                    bool canDarkin0 = hasDarkin && p.BenchHasSpace && p.mana >= card.data.darkinUnit.manaCost;
                    bool canEquip0 = hasAlly0 && (p.spellMana + p.mana) >= card.currentManaCost;
                    mode = canDarkin0 ? 1 : (canEquip0 ? 0 : -1);
                    modeDone = true;
                }
                else if (byPlayer && OnEquipmentNeedsMode != null)
                {
                    // Player → GameView hiện panel chọn mode
                    OnEquipmentNeedsMode.Invoke(card, byPlayer, result =>
                    {
                        mode = result;
                        modeDone = true;
                    });
                }
                else if (!byPlayer && OnEquipmentNeedsEnemyMode != null)
                {
                    // Enemy → EnemyAI tự chọn, GameView không nhận event này
                    OnEquipmentNeedsEnemyMode.Invoke(card, result =>
                    {
                        mode = result;
                        modeDone = true;
                    });
                }
                else
                {
                    // Không có handler → mặc định equip mode (0)
                    modeDone = true;
                }

                yield return new WaitUntil(() => modeDone);
                _isEquipmentSelecting = false;

                if (mode == -1)
                {
                    // Hủy → card vẫn trong tay
                    Notify();
                    yield break;
                }
            }

            // ── Bước 2a: Triệu Hồi Darkin ────────────────────────
            if (mode == 1)
            {
                // Darkin mode tính theo manaCost của UNIT, không phải weapon
                int cost = card.data.darkinUnit != null
                    ? card.data.darkinUnit.manaCost
                    : card.currentManaCost;

                // Darkin (triệu hồi unit) CHỈ được trả bằng mana thường — không đụng spell mana,
                // khác với mode Trang Bị (weapon) vốn trả bằng spell mana trước như spell thật.
                bool okDarkin = p.SpendMana(cost);
                if (!okDarkin)
                {
                    Debug.LogWarning($"[Equipment] Không đủ mana thường cho Darkin ({cost}). Card giữ lại trong tay.");
                    Notify();
                    yield break; // card vẫn trong tay, không tốn gì
                }

                // KHÔNG đánh dấu _equipmentsPlayedThisRound ở đây — Darkin tiêu card thẳng
                // vào discard, không bao giờ quay lại tay nên không cần chặn round này.
                // (Cờ này chỉ có ý nghĩa cho nhánh Trang Bị, nơi card có thể về tay giữa round
                // nếu unit đeo nó chết.)

                // Fire OnCardRemovedFromHand → HandZoneView.DropCard xóa view cũ
                p.RemoveEquipmentFromHand(card);
                p.discard.Add(card);
                card.SetLocation(CardLocation.InDiscard);
                _cardsPlayedThisRound++;
                // (Darkin dùng darkinUnit.manaCost — không phải card.currentManaCost — nên KHÔNG áp giảm giá ★1/★3 ở đây.)

                // Summon Darkin lên bench
                var darkin = SummonUnit(card.data.darkinUnit, byPlayer);
                if (darkin == null)
                    Debug.LogWarning($"[Equipment] Bench đầy hoặc darkinUnit null — '{card.data.darkinUnit?.cardName}' không được triệu hồi.");

                Debug.Log($"[Equipment] {card.data.cardName} → Triệu Hồi Darkin '{card.data.darkinUnit?.cardName}'.");

                if (model.phase != GamePhase.SpellStackResolve)
                    model.HandOffPriority(byPlayer);
                Notify();
                yield break;
            }

            // ── Bước 2b: Trang Bị → chọn ally ───────────────────
            bool targetDone = false;
            CardModel targetUnit = null;

            if (byPlayer && playerAutoPlay)
            {
                foreach (var c in p.BattlefieldCards())
                    if (c.IsAlive && (targetUnit == null || c.effectiveAttack > targetUnit.effectiveAttack)) targetUnit = c;
                foreach (var c in p.BenchCards())
                    if (c.IsAlive && (targetUnit == null || c.effectiveAttack > targetUnit.effectiveAttack)) targetUnit = c;
                targetDone = true;
            }
            else if (byPlayer && OnEquipmentNeedsTarget != null)
            {
                // Player → GameView hiện targeting arrow, chờ player click
                _isEquipmentSelecting = true;
                OnEquipmentNeedsTarget.Invoke(card, byPlayer, result =>
                {
                    targetUnit = result;
                    targetDone = true;
                });
                yield return new WaitUntil(() => targetDone);
                _isEquipmentSelecting = false;
            }
            else if (!byPlayer && OnEquipmentNeedsEnemyTarget != null)
            {
                // Enemy → EnemyAI tự chọn ally, GameView KHÔNG nhận event này
                // → không có arrow nào xuất hiện trên cursor player
                _isEquipmentSelecting = true;
                OnEquipmentNeedsEnemyTarget.Invoke(card, result =>
                {
                    targetUnit = result;
                    targetDone = true;
                });
                yield return new WaitUntil(() => targetDone);
                _isEquipmentSelecting = false;
            }
            else
            {
                // Không có handler → auto-equip lên unit đầu tiên trên bench
                foreach (var c in p.BenchCards()) { targetUnit = c; break; }
            }

            if (targetUnit == null)
            {
                // Hủy targeting → card vẫn trong tay, không tốn mana
                Notify();
                yield break;
            }

            // Mode Trang Bị: nếu card này đã được Trang Bị trong round này rồi (unit đeo nó
            // chết → card về tay) thì chặn re-equip miễn phí ở đây. Đặt check tại đây (không
            // phải ở đầu HandlePlayEquipment) để không ảnh hưởng tới lựa chọn Darkin của
            // cùng card đó — 2 mode không mâu thuẫn nhau.
            if (_equipmentsPlayedThisRound.Contains(card.id))
            {
                Debug.LogWarning($"[Equipment] '{card.data.cardName}' đã Trang Bị round này — không thể Trang Bị lại. Card giữ lại trong tay.");
                Notify();
                yield break;
            }

            // (TINH HỒN ★1/★3: -1 đã rải sẵn lên lá trang bị trong tay → currentManaCost đã giảm.)
            // Equipment dùng spell mana trước, sau đó mana thường (giống spell)
            var (okEquip, _) = p.SpendManaForSpell(card.currentManaCost);
            if (!okEquip)
            { Debug.LogWarning("[Equipment] SpendManaForSpell thất bại."); Notify(); yield break; }

            // Đánh dấu đã chơi round này — không cho tái sử dụng nếu về tay
            _equipmentsPlayedThisRound.Add(card.id);

            // Fire OnCardRemovedFromHand → HandZoneView.DropCard xóa view cũ
            p.RemoveEquipmentFromHand(card);
            card.SetLocation(CardLocation.Equipped);
            _cardsPlayedThisRound++;
            if (byPlayer && !networkMode) CampaignRun.ConstellationNotifyCardPlayed(p);   // TINH HỒN: chốt -1 lá đầu (PvP: off)

            // Apply equipment lên target
            ApplyEquipment(card, targetUnit, byPlayer);

            Debug.Log($"[Equipment] {card.data.cardName} → Trang Bị cho {targetUnit.data.cardName}.");

            if (model.phase != GamePhase.SpellStackResolve)
                model.HandOffPriority(byPlayer);
            Notify();
        }

        // ── ApplyEquipment ────────────────────────────────────────
        /// <summary>
        /// Gắn equipment lên unit: áp buffs, grant keywords, link cả 2 phía.
        /// Nếu unit đang có equipment khác → detach cái cũ + trả về tay trước.
        /// </summary>
        public void ApplyEquipment(CardModel equipment, CardModel unit, bool byPlayer = true)
        {
            // Detach equipment cũ nếu có
            if (unit.equipmentAttached != null)
            {
                var old = unit.equipmentAttached;
                UnlinkEquipment(old, unit);

                // Reverse buffs (keywords không thu hồi được)
                if (old.data.equipAttackBuff != 0) unit.BuffAttack(-old.data.equipAttackBuff);
                if (old.data.equipHealthBuff != 0) unit.BuffHealth(-old.data.equipHealthBuff);

                // Xóa buff history entry của equipment cũ
                unit.RemoveEquipmentBuff(old.data.cardName);

                // Trả equipment cũ về tay owner của unit
                var oldOwner = P(unit.belongsToPlayer);
                bool returned = oldOwner.ReturnEquipmentToHand(old);
                if (!returned)
                {
                    // Tay đầy → discard
                    oldOwner.discard.Add(old);
                    old.SetLocation(CardLocation.InDiscard);
                    Debug.Log($"[Equipment] Tay đầy → {old.data.cardName} bị hủy.");
                }
                else
                {
                    Debug.Log($"[Equipment] Detach {old.data.cardName} khỏi {unit.data.cardName} → về tay.");
                }
            }

            // Link equipment ↔ unit
            LinkEquipment(equipment, unit);

            // Keyword Trang Bị (UI): icon hiện trên unit để thấy rõ nó đang đeo equipment.
            // Không cần gỡ keyword: unit chỉ mất equipment khi chết (card bị xóa)
            // hoặc khi thay equipment mới (vẫn đang đeo → keyword vẫn đúng).
            unit.GrantKeyword(KeywordType.Equipped);

            // Apply buffs vĩnh viễn lên unit
            if (equipment.data.equipAttackBuff != 0)
                unit.BuffAttack(equipment.data.equipAttackBuff);
            if (equipment.data.equipHealthBuff != 0)
                unit.BuffHealth(equipment.data.equipHealthBuff);

            // Ghi nhận vào buff history (hiện trong inspect panel)
            unit.RecordEquipmentBuff(
                equipment.data.cardName,
                equipment.data.artwork,
                equipment.data.equipAttackBuff,
                equipment.data.equipHealthBuff);

            // Grant keywords
            if (equipment.data.equipGrantKeywords != null)
                foreach (var kw in equipment.data.equipGrantKeywords)
                    unit.GrantKeyword(kw);

            Debug.Log($"[Equipment] {equipment.data.cardName} gắn vào {unit.data.cardName} " +
                      $"(+{equipment.data.equipAttackBuff}|+{equipment.data.equipHealthBuff}).");
        }

        // ── CheckReturnEquipmentOnDeath ───────────────────────────
        /// <summary>
        /// Gọi TRƯỚC KHI p.KillCard(unit) trong ProcessDeaths.
        /// Nếu unit đang đeo equipment → detach + trả về tay.
        /// </summary>
        public void CheckReturnEquipmentOnDeath(CardModel unit)
        {
            if (unit.equipmentAttached == null) return;

            var equipment = unit.equipmentAttached;
            UnlinkEquipment(equipment, unit);

            var owner = P(unit.belongsToPlayer);
            bool returned = owner.ReturnEquipmentToHand(equipment);
            if (!returned)
            {
                owner.discard.Add(equipment);
                equipment.SetLocation(CardLocation.InDiscard);
                Debug.Log($"[Equipment] {unit.data.cardName} chết, tay đầy → {equipment.data.cardName} bị hủy.");
            }
            else
            {
                Debug.Log($"[Equipment] {unit.data.cardName} chết → {equipment.data.cardName} về tay.");
            }
            Notify();
        }

        // ── InitEquipmentDeathHandlers ────────────────────────────
        /// <summary>
        /// Gọi 1 lần trong StartGame() sau khi model được khởi tạo.
        /// 1) Subscribe OnCardDied của cả 2 player → tự trả equipment về tay khi unit đeo chết.
        /// 2) Subscribe OnCardPlayedToBench / OnCardMovedToBattlefield → AUTO-EQUIP:
        ///    unit có autoEquipOnSummon sẽ tự đeo trang bị NGAY khi có mặt trên sân,
        ///    bất kể cách xuất hiện (chơi từ tay, summon skill/deck, đặt sẵn tutorial...).
        ///
        /// Gọi trong StartGame():
        ///   model = new GameModel();
        ///   InitEquipmentDeathHandlers();   ← thêm dòng này
        ///   ...
        /// </summary>
        public void InitEquipmentDeathHandlers()
        {
            model.player.OnCardDied += (_, unit) =>
            {
                if (unit.equipmentAttached != null)
                    CheckReturnEquipmentOnDeath(unit);
            };
            model.enemy.OnCardDied += (_, unit) =>
            {
                if (unit.equipmentAttached != null)
                    CheckReturnEquipmentOnDeath(unit);
            };

            // AUTO-EQUIP (Aatrox-style) — mọi cách unit lên sân đều fire các event này.
            model.player.OnCardPlayedToBench += (_, unit) => AutoEquipOnSummon(unit);
            model.enemy.OnCardPlayedToBench += (_, unit) => AutoEquipOnSummon(unit);
            model.player.OnCardMovedToBattlefield += (_, unit) => AutoEquipOnSummon(unit);
            model.enemy.OnCardMovedToBattlefield += (_, unit) => AutoEquipOnSummon(unit);
        }

        // ── Link / Unlink helpers ─────────────────────────────────
        static void LinkEquipment(CardModel equipment, CardModel unit)
        {
            equipment.AttachToUnit(unit);
            unit.AttachEquipment(equipment);
        }

        static void UnlinkEquipment(CardModel equipment, CardModel unit)
        {
            equipment.DetachFromUnit();
            unit.DetachEquipment();
        }
    }
}