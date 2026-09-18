using System;
using System.Collections.Generic;
using LoRClone.Data;

namespace LoRClone.Model
{
    public partial class PlayerModel
    {
        public readonly bool isPlayer;

        // ── Health ────────────────────────────────────────────────
        public int health { get; private set; } = 20;
        public int maxHealth { get; private set; } = 20;

        // ── Mana ──────────────────────────────────────────────────
        public int mana { get; private set; }
        public int maxMana { get; private set; }
        public const int AbsoluteMaxMana = 9;

        // ── Spell Mana ────────────────────────────────────────────
        public int spellMana { get; private set; }
        public const int MaxSpellMana = 5;

        /// <summary>Tổng spell mana đã tiêu hao cộng dồn từ đầu ván (dùng cho SpellManaTrigger).</summary>
        public int spellManaSpentTotal { get; private set; }

        /// <summary>Tổng số lần CẢ ĐỘI confirm tấn công từ đầu ván (dùng cho CardCreationTrigger, counter=AttacksMade, scope=Team).</summary>
        public int attacksMadeTotal { get; private set; }

        /// <summary>Cộng dồn số lượt tấn công của cả đội. Gọi bởi GameController tại B6 (confirm attack).</summary>
        public void AddAttackMadeTeam(int count = 1)
        {
            if (count > 0) attacksMadeTotal += count;
        }

        // ── Zones ─────────────────────────────────────────────────
        public List<CardModel> deck { get; } = new();
        public List<CardModel> hand { get; } = new();
        public CardModel[] bench { get; } = new CardModel[6];
        public CardModel[] battlefield { get; } = new CardModel[6];
        public List<CardModel> discard { get; } = new();
        public List<CardModel> stagedSpells { get; } = new();

        public const int MaxHandSize = 10;
        public const int MaxBenchSize = 6;
        public const int MaxStagedSpells = 10;

        // ── Events ────────────────────────────────────────────────
        public event Action<PlayerModel> OnHealthChanged;
        public event Action<PlayerModel> OnManaChanged;
        public event Action<PlayerModel, CardModel> OnCardDrawn;
        /// <summary>Card được tạo ra bằng hiệu ứng (không phải rút từ deck).</summary>
        public event Action<PlayerModel, CardModel> OnCardCreated;
        public event Action<PlayerModel, CardModel> OnCardRemovedFromHand;
        public event Action<PlayerModel, CardModel> OnCardPlayedToBench;
        public event Action<PlayerModel, CardModel> OnCardMovedToBattlefield;
        public event Action<PlayerModel, CardModel> OnCardReturnedToBench;
        public event Action<PlayerModel, CardModel> OnCardDied;
        public event Action<PlayerModel, CardModel> OnCardCast;
        public event Action<PlayerModel, CardModel> OnSpellReturnedToHand;
        public event Action<PlayerModel, CardModel> OnSpellStaged;
        public event Action<PlayerModel, CardModel> OnSpellUnstaged;
        /// <summary>
        /// Fire sau khi CompactAttackerSlots() dịch chuyển attacker về slot liên tục.
        /// Tham số: danh sách card bị đổi slot (slotIndex đã được cập nhật sang slot mới).
        /// BattlefieldZoneView subscribe để rebuild view tại đúng vị trí.
        /// </summary>
        public event Action<PlayerModel, List<CardModel>> OnBattlefieldCompacted;

        /// <summary>
        /// Fire khi một unit bị Recall về tay (đã qua RecallReset — mọi buff đã bị xóa).
        /// HandZoneView subscribe để spawn CardView mới cho card này.
        ///
        /// Không fire nếu hand đầy (card bị obliterate thay vì về tay).
        /// </summary>
        public event Action<PlayerModel, CardModel> OnCardRecalled;

        // ── Loop Mode (chế độ vòng lặp — set bởi GameController khi bật loopMode) ──
        /// <summary>Bật = lá "dùng xong" (KillCard) reset về base rồi shuffle lại vào deck thay vì discard.</summary>
        public bool recycleUsedCards;

        /// <summary>Fire khi 1 lá được tái sử dụng (recycle) về deck. GameController subscribe để purge tracker theo id.</summary>
        public event Action<PlayerModel, CardModel> OnCardRecycled;

        readonly System.Random _recycleRng = new System.Random();

        public PlayerModel(bool isPlayer) { this.isPlayer = isPlayer; }

        // ── Health ────────────────────────────────────────────────
        public void TakeDamage(int amount)
        {
            if (amount <= 0) return;
            health = Math.Max(0, health - amount);
            OnHealthChanged?.Invoke(this);
        }

        /// <summary>Lifesteal / hồi máu nexus. Không vượt quá maxHealth.</summary>
        public void HealNexus(int amount)
        {
            if (amount <= 0) return;
            health = Math.Min(maxHealth, health + amount);
            OnHealthChanged?.Invoke(this);
        }

        /// <summary>
        /// Set máu Nexus khởi đầu (Leo Tháp — mỗi tầng máu khác nhau).
        /// Gọi ngay sau khi tạo PlayerModel/trước khi trận bắt đầu. hp &lt;= 0 → giữ nguyên.
        /// </summary>
        public void SetStartingHealth(int hp)
        {
            if (hp <= 0) return;
            maxHealth = hp;
            health = hp;
            OnHealthChanged?.Invoke(this);
        }

        /// <summary>
        /// M13 — HP Nexus XUYÊN SUỐT ải: đặt MAX và CURRENT riêng (current mang từ trận trước sang).
        /// </summary>
        public void SetStartingHealth(int max, int current)
        {
            if (max <= 0) return;
            maxHealth = max;
            health = Math.Clamp(current, 1, max);
            OnHealthChanged?.Invoke(this);
        }

        public bool IsAlive => health > 0;

        // ── Mana ──────────────────────────────────────────────────
        public void RefillMana()
        {
            if (maxMana < AbsoluteMaxMana) maxMana++;
            mana = maxMana;
            OnManaChanged?.Invoke(this);
        }

        /// <summary>
        /// Set mana khởi đầu (Leo Tháp/tutorial): maxMana + đầy mana. Cap ở AbsoluteMaxMana (9).
        /// Gọi SAU RefillMana round 1 để không bị đè. maxManaValue &lt;= 0 → giữ nguyên.
        /// </summary>
        public void SetStartingMana(int maxManaValue)
        {
            if (maxManaValue <= 0) return;
            maxMana = Math.Min(AbsoluteMaxMana, maxManaValue);
            mana = maxMana;
            OnManaChanged?.Invoke(this);
        }

        public bool CanSpend(int amount) => mana >= amount;
        public bool SpendMana(int amount)
        {
            if (!CanSpend(amount)) return false;
            mana -= amount;
            OnManaChanged?.Invoke(this);
            return true;
        }

        public void RefundMana(int amount)
        {
            if (amount <= 0) return;
            mana = Math.Min(maxMana, mana + amount);
            OnManaChanged?.Invoke(this);
        }

        // ── Spell Mana Methods ────────────────────────────────────

        /// <summary>
        /// Cuối round: mana thừa → spell mana (tích lũy, max 5).
        /// PHẢI gọi TRƯỚC RefillMana() — sau RefillMana mana đã về max.
        /// </summary>
        public void ConvertExcessToSpellMana()
        {
            if (mana <= 0) return;
            int canAdd = MaxSpellMana - spellMana;
            if (canAdd <= 0) return;
            spellMana += Math.Min(mana, canAdd);
            OnManaChanged?.Invoke(this);
        }

        /// <summary>Spell dùng được spell mana + regular mana cộng lại.</summary>
        public bool CanAffordSpell(int amount) => spellMana + mana >= amount;

        /// <summary>
        /// Tiêu mana cho spell: spell mana trước, regular sau.
        /// Trả về (success, spellManaUsed) để GameController lưu cho refund chính xác.
        /// </summary>
        public (bool success, int spellManaUsed) SpendManaForSpell(int amount)
        {
            if (!CanAffordSpell(amount)) return (false, 0);
            int fromSpell = Math.Min(spellMana, amount);
            spellMana -= fromSpell;
            mana -= (amount - fromSpell);
            spellManaSpentTotal += fromSpell;   // cộng dồn cho SpellManaTrigger
            OnManaChanged?.Invoke(this);
            return (true, fromSpell);
        }

        /// <summary>
        /// Refund khi undo cast spell — hoàn đúng lượng spell mana đã dùng.
        /// </summary>
        public void RefundManaForSpell(int totalAmount, int spellManaUsed)
        {
            int regularUsed = totalAmount - spellManaUsed;
            mana = Math.Min(maxMana, mana + regularUsed);
            spellMana = Math.Min(MaxSpellMana, spellMana + spellManaUsed);
            OnManaChanged?.Invoke(this);
        }

        /// <summary>
        /// Cộng thêm spell mana trực tiếp (từ Tri Thức và các effect khác).
        /// Không vượt quá MaxSpellMana.
        /// </summary>
        public void AddSpellMana(int amount)
        {
            if (amount <= 0) return;
            spellMana = Math.Min(MaxSpellMana, spellMana + amount);
            OnManaChanged?.Invoke(this);
        }

        // ── Mulligan ──────────────────────────────────────────────
        /// <summary>
        /// Trả các lá đã chọn về deck (shuffle lại), rút đúng số lá mới thay thế.
        /// Gọi sau khi player xác nhận mulligan.
        /// </summary>
        public void MulliganSwap(List<CardModel> toSwap)
        {
            if (toSwap == null || toSwap.Count == 0) return;
            int count = toSwap.Count;

            foreach (var card in toSwap)
            {
                hand.Remove(card);
                deck.Add(card);
                OnCardRemovedFromHand?.Invoke(this, card);
            }

            Shuffle();

            for (int i = 0; i < count; i++)
                DrawCard();
        }

        // ── Deck ──────────────────────────────────────────────────
        public void BuildDeck(DeckData deckData)
        {
            deck.Clear();
            foreach (var cd in deckData.cards)
            {
                var m = new CardModel(cd, isPlayer, fromDeck: true);
                if (isPlayer) RunItems.Apply(m, cd);   // PoC item: buff mọi bản sao lá player
                deck.Add(m);
            }
            Shuffle();
        }

        /// <summary>
        /// LOOP MODE: giữ đúng 1 bản sao mỗi CardData trong deck (lọc trùng, giữ lá đầu tiên).
        /// Gọi sau BuildDeck khi bật loopMode. Không đụng deck ở mode thường.
        /// </summary>
        public void DedupeSingleCopy()
        {
            var seen = new HashSet<CardData>();
            var kept = new List<CardModel>();
            foreach (var c in deck)
                if (seen.Add(c.data)) kept.Add(c);
            deck.Clear();
            deck.AddRange(kept);
        }

        // ── LOOP MODE: dùng bài → chèn NGAY 1 bản sao vào deck ────────────────
        /// <summary>
        /// Tạo 1 CardModel MỚI từ originalData của lá vừa được dùng và chèn ngẫu nhiên vào deck.
        /// Gọi ngay lúc lá RỜI TAY để dùng (chơi unit / cast spell / chơi trang bị) — KHÔNG đợi chết.
        /// Bản sao có id mới → không dính tracker theo id của lá cũ.
        /// Bỏ qua: lá không thuộc deck gốc (token/summon) và lá isGenerated.
        /// </summary>
        void SeedCopyToDeck(CardModel src)
        {
            if (!recycleUsedCards || src == null) return;

            // ── CHẶN 1 (chính): lá do KỸ NĂNG TẠO RA lúc chơi ─────────────────────
            // Mọi lá tạo runtime đều qua CreateCard() → new CardModel(...) với fromDeck = false.
            // Gồm: Spellcraft/UnitSkill tạo spell lên tay khi triệu hồi unit, battle-skill (Renekton),
            //      SpellManaTrigger, DamageTakenTrigger, DamageDealtTrigger (finisher), token summon...
            // Những lá này dùng bao nhiêu lần cũng KHÔNG được chèn vào deck (tránh lệch gameplay).
            if (!src.fromDeck) return;

            // ── CHẶN 2 (phụ): CardData tự đánh dấu isGenerated ────────────────────
            if (src.originalData == null || src.originalData.isGenerated) return;

            var copy = new CardModel(src.originalData, isPlayer, fromDeck: true);
            if (isPlayer)
            {
                RunItems.Apply(copy, src.originalData);                       // giữ item buff khi loop-mode reseed
                LoRClone.Data.CampaignRun.ApplyRunBuffsToUnit(copy);         // áp buff ẢI + champion lên bản sao
            }
            deck.Add(copy);                                                  // HÀNG CHỜ CUỐI (đáy deck) — không ngẫu nhiên nữa
            int idx = deck.Count - 1;
            copy.SetLocation(CardLocation.InDeck);

            UnityEngine.Debug.Log($"[LoopMode] Dùng '{src.originalData.cardName}' → xếp 1 bản sao xuống ĐÁY deck " +
                                  $"(deck còn {deck.Count} lá).");
        }

        /// <summary>Hoàn tác SeedCopyToDeck khi người chơi HỦY cast spell (undo) — gỡ 1 bản sao khỏi deck.</summary>
        void UnseedCopyFromDeck(CardModel src)
        {
            if (!recycleUsedCards || src == null || src.originalData == null) return;
            for (int i = deck.Count - 1; i >= 0; i--)
                if (deck[i] != src && deck[i].originalData == src.originalData)
                { deck.RemoveAt(i); return; }
        }

        /// <summary>
        /// LOOP MODE — bù lại deck khi 1 lá RỜI DECK để ra sân bằng skill (SummonUnitFromDeck),
        /// vì đường này không đi qua "chơi từ tay" nên không tự seed. Giữ deck không hụt dần.
        /// Dùng chung bộ chặn của SeedCopyToDeck (fromDeck + isGenerated) nên token vẫn bị loại.
        /// </summary>
        public void SeedReplacementForDeckSummon(CardModel src) => SeedCopyToDeck(src);

        void Shuffle()
        {
            var rng = _netRng ?? new System.Random();
            for (int i = deck.Count - 1; i > 0; i--)
            {
                int j = rng.Next(i + 1);
                (deck[i], deck[j]) = (deck[j], deck[i]);
            }
        }

        // ── Draw ──────────────────────────────────────────────────
        /// <summary>
        /// Rút ĐÚNG 1 card cụ thể ra khỏi deck (bất kỳ vị trí) vào tay — khác DrawCard() (luôn
        /// rút deck[0]). Dùng cho AfterMulligan / các hiệu ứng "rút đúng lá này" tương tự.
        /// Trả về null nếu card không còn trong deck, hoặc tay đã đầy.
        /// </summary>
        public CardModel DrawSpecificCard(CardModel card)
        {
            if (card == null || hand.Count >= MaxHandSize) return null;
            if (!deck.Remove(card)) return null;
            hand.Add(card);
            card.SetLocation(CardLocation.InHand, hand.Count - 1);
            OnCardDrawn?.Invoke(this, card);
            return card;
        }

        public CardModel DrawCard()
        {
            if (deck.Count == 0 || hand.Count >= MaxHandSize) return null;
            var card = deck[0];
            deck.RemoveAt(0);
            hand.Add(card);
            card.SetLocation(CardLocation.InHand, hand.Count - 1);
            OnCardDrawn?.Invoke(this, card);
            return card;
        }

        /// <summary>
        /// Tạo card từ <paramref name="data"/> trực tiếp lên hand (không từ deck).
        /// Dùng cho generated cards / token / spell trigger.
        /// Trả về null nếu hand đầy hoặc data null.
        /// </summary>
        public CardModel CreateCard(CardData data)
        {
            if (data == null || hand.Count >= MaxHandSize) return null;
            var card = new CardModel(data, isPlayer);
            hand.Add(card);
            card.SetLocation(CardLocation.InHand, hand.Count - 1);
            OnCardCreated?.Invoke(this, card);
            return card;
        }

        // ── Hand → Bench (play unit) ──────────────────────────────
        public bool PlayToBench(CardModel card)
        {
            if (!hand.Contains(card)) return false;
            if (!SpendMana(card.currentManaCost)) return false;

            int slot = FindEmptyBenchSlot();
            if (slot < 0) return false;

            hand.Remove(card);
            bench[slot] = card;
            card.SetLocation(CardLocation.OnBench, slot);
            SeedCopyToDeck(card);          // LOOP MODE: dùng bài → copy vào deck ngay
            OnCardPlayedToBench?.Invoke(this, card);
            return true;
        }

        // ── Direct summon to battlefield (skill effect — không từ tay, không tốn mana) ──
        /// <summary>
        /// Tạo CardModel mới và đặt thẳng lên battlefield (không qua bench, không tốn mana).
        /// Dùng cho skill SkillSummonUnit khi summonToBattlefield = true.
        /// Fires OnCardMovedToBattlefield → BattlefieldZoneView nhận và spawn view.
        /// </summary>
        public CardModel SummonToBattlefield(CardData data)
        {
            if (!BattlefieldHasSpace) return null;

            int slot = FindEmptyBattlefieldSlot();
            var card = new CardModel(data, isPlayer);
            battlefield[slot] = card;
            card.SetLocation(CardLocation.OnBattlefield, slot);

            OnCardMovedToBattlefield?.Invoke(this, card);
            return card;
        }

        // ── Direct summon to bench (skill effect — không từ tay, không tốn mana) ──
        /// <summary>
        /// Tạo CardModel mới từ data và đặt thẳng lên bench (không cần có trong tay,
        /// không tốn mana). Dùng cho skill SkillSummonUnit.
        /// Fires OnCardPlayedToBench → view layer (BenchZoneView) tự spawn card.
        /// </summary>
        public CardModel SummonToBench(CardData data)
        {
            if (!BenchHasSpace) return null;

            int slot = FindEmptyBenchSlot();
            var card = new CardModel(data, isPlayer);
            bench[slot] = card;
            card.SetLocation(CardLocation.OnBench, slot);

            // Reuse cùng event mà PlayToBench fire → BenchZoneView nhận và spawn view.
            OnCardPlayedToBench?.Invoke(this, card);
            return card;
        }

        /// <summary>
        /// Đặt unit lên bench ở SLOT cụ thể (tutorial / dàn cảnh Leo Tháp).
        /// slot ngoài [0, MaxBenchSize) hoặc đã có unit → fallback slot trống đầu tiên.
        /// Fires OnCardPlayedToBench → BenchZoneView tự spawn view.
        /// </summary>
        public CardModel SummonToBenchAt(CardData data, int slot)
        {
            if (data == null) return null;
            if (slot < 0 || slot >= MaxBenchSize || bench[slot] != null)
                return SummonToBench(data);

            var card = new CardModel(data, isPlayer);
            bench[slot] = card;
            card.SetLocation(CardLocation.OnBench, slot);
            OnCardPlayedToBench?.Invoke(this, card);
            return card;
        }

        // ── Direct summon CardModel CÓ SẴN (vd: từ deck) — không tạo mới ──
        /// <summary>
        /// Đặt một CardModel đã tồn tại lên bench (dùng cho summon-from-deck).
        /// QUAN TRỌNG: caller phải xóa card khỏi vùng cũ (deck) TRƯỚC khi gọi —
        /// nếu không sẽ bị nhân đôi lá bài.
        /// Giữ nguyên mọi buff runtime trên card (Lurk, Kayle passive, v.v.).
        /// Fires OnCardPlayedToBench → BenchZoneView spawn view.
        /// </summary>
        public CardModel SummonExistingToBench(CardModel card)
        {
            if (card == null || !BenchHasSpace) return null;
            int slot = FindEmptyBenchSlot();
            bench[slot] = card;
            card.SetLocation(CardLocation.OnBench, slot);
            OnCardPlayedToBench?.Invoke(this, card);
            return card;
        }

        /// <summary>Như SummonExistingToBench nhưng đặt thẳng lên battlefield.</summary>
        public CardModel SummonExistingToBattlefield(CardModel card)
        {
            if (card == null || !BattlefieldHasSpace) return null;
            int slot = FindEmptyBattlefieldSlot();
            battlefield[slot] = card;
            card.SetLocation(CardLocation.OnBattlefield, slot);
            OnCardMovedToBattlefield?.Invoke(this, card);
            return card;
        }

        // ── Hand → (resolve ngay) Burst spell ─────────────────────
        public bool CastBurstSpell(CardModel card)
        {
            if (!hand.Contains(card)) return false;

            hand.Remove(card);
            card.SetLocation(CardLocation.OnStack);
            SeedCopyToDeck(card);          // LOOP MODE
            OnCardCast?.Invoke(this, card);
            return true;
        }

        // ── Hand → SpellZone (staging, CHƯA commit) ───────────────
        public bool StageSpell(CardModel card)
        {
            if (!hand.Contains(card)) return false;
            if (stagedSpells.Count >= MaxStagedSpells) return false;

            hand.Remove(card);
            stagedSpells.Add(card);
            card.SetLocation(CardLocation.StagingSpell, stagedSpells.Count - 1);
            OnSpellStaged?.Invoke(this, card);
            return true;
        }

        // ── SpellZone → Hand (bỏ staging) ─────────────────────────
        public bool UnstageSpell(CardModel card)
        {
            if (card.location != CardLocation.StagingSpell) return false;
            if (!stagedSpells.Remove(card)) return false;
            if (hand.Count >= MaxHandSize)
            {
                stagedSpells.Add(card);
                return false;
            }

            hand.Add(card);
            card.SetLocation(CardLocation.InHand, hand.Count - 1);
            OnSpellUnstaged?.Invoke(this, card);
            return true;
        }

        public int TotalStagedManaCost()
        {
            int total = 0;
            foreach (var c in stagedSpells) total += c.currentManaCost;  // tôn trọng runtime reduction
            return total;
        }

        // ── SpellZone (staging) → Spell Stack (commit) ────────────
        public bool CommitStagedSpell(CardModel card)
        {
            if (card.location != CardLocation.StagingSpell) return false;
            if (!stagedSpells.Remove(card)) return false;

            card.SetLocation(CardLocation.OnStack);
            SeedCopyToDeck(card);          // LOOP MODE
            OnCardCast?.Invoke(this, card);
            return true;
        }

        // ── Spell Stack → Hand (hủy cast) ─────────────────────────
        public bool UndoCastSpell(CardModel card)
        {
            if (card.location != CardLocation.OnStack) return false;
            if (hand.Count >= MaxHandSize) return false;

            hand.Add(card);
            card.SetLocation(CardLocation.InHand, hand.Count - 1);
            UnseedCopyFromDeck(card);      // LOOP MODE: hủy cast → gỡ bản sao đã chèn
            OnSpellReturnedToHand?.Invoke(this, card);
            return true;
        }


        // ── Equipment → Hand (khi unit đeo equipment chết) ───────
        /// <summary>
        /// Trả equipment card về tay khi unit đeo nó chết.
        /// Card đã tồn tại — không tạo mới, chỉ đưa vào hand list.
        /// Fire OnCardRecalled để HandZoneView spawn view cho nó.
        /// Trả về false nếu tay đầy (caller nên discard thay thế).
        /// </summary>
        public bool ReturnEquipmentToHand(CardModel equipment)
        {
            if (hand.Count >= MaxHandSize) return false;
            hand.Add(equipment);
            equipment.SetLocation(CardLocation.InHand, hand.Count - 1);
            OnCardRecalled?.Invoke(this, equipment); // HandZoneView.AddCard
            return true;
        }

        /// <summary>
        /// Dùng khi chơi một Equipment card từ tay: xóa khỏi hand list và fire OnCardRemovedFromHand
        /// để HandZoneView gọi DropCard, xóa view cũ đi.
        /// Không gọi p.hand.Remove(card) trực tiếp từ GameController — nó không fire event.
        /// </summary>
        public void RemoveEquipmentFromHand(CardModel card)
        {
            hand.Remove(card);
            SeedCopyToDeck(card);          // LOOP MODE
            OnCardRemovedFromHand?.Invoke(this, card);
        }

        // ── Bench → Battlefield (attacker) ───────────────────────
        public bool MoveToBattlefield(CardModel card)
        {
            int benchSlot = card.slotIndex;
            if (benchSlot < 0 || bench[benchSlot] != card) return false;

            // Attacker: compact từ trái (FindEmpty) — vị trí cụ thể không quan trọng ở đây
            // vì blocker bên kia sẽ match theo attacker.slotIndex (xem MoveBlockerToBattlefield).
            int bfSlot = FindEmptyBattlefieldSlot();
            if (bfSlot < 0) return false;

            bench[benchSlot] = null;
            battlefield[bfSlot] = card;
            card.SetLocation(CardLocation.OnBattlefield, bfSlot);
            OnCardMovedToBattlefield?.Invoke(this, card);
            return true;
        }

        // ── Bench → Battlefield (blocker — phải match slot của attacker đang bị block) ─
        /// <summary>
        /// Di chuyển blocker từ bench lên battlefield ở đúng slot đối diện với attacker.
        /// <paramref name="attackerSlot"/> là slotIndex của attacker bên địch đang bị block.
        ///
        /// Quy tắc slot:
        ///   • Ưu tiên đặt blocker ở <c>attackerSlot</c> — đảm bảo 2 bên cùng cột, không đánh chéo.
        ///   • Nếu slot đó đã có unit khác (unit đứng im từ trước), fallback sang FindEmptyBattlefieldSlot().
        ///
        /// View nhận OnCardMovedToBattlefield → spawn CardView → đặt vào slot container đúng cột.
        /// </summary>
        public bool MoveBlockerToBattlefield(CardModel card, int attackerSlot)
        {
            int benchSlot = card.slotIndex;
            if (benchSlot < 0 || bench[benchSlot] != card) return false;

            // Ưu tiên slot đối diện với attacker
            int bfSlot = -1;
            if (attackerSlot >= 0 && attackerSlot < MaxBenchSize && battlefield[attackerSlot] == null)
                bfSlot = attackerSlot;
            else
                bfSlot = FindEmptyBattlefieldSlot();   // fallback nếu slot đó bị chiếm

            if (bfSlot < 0) return false;

            bench[benchSlot] = null;
            battlefield[bfSlot] = card;
            card.SetLocation(CardLocation.OnBattlefield, bfSlot);
            OnCardMovedToBattlefield?.Invoke(this, card);
            return true;
        }

        // ── Reposition blocker đã trên battlefield về slot đối diện attacker ──────────
        /// <summary>
        /// Unit đã đứng trên battlefield (vd: summon skill) muốn block → cần di chuyển sang
        /// slot đối diện với attacker để không đánh chéo.
        /// Không fire OnCardMovedToBattlefield (đã ở đây rồi) —
        /// fire OnBattlefieldCompacted để view rebuild vị trí card.
        /// </summary>
        public void RepositionToBlockerSlot(CardModel card, int attackerSlot)
        {
            int currentSlot = card.slotIndex;
            if (currentSlot < 0 || battlefield[currentSlot] != card) return;
            if (currentSlot == attackerSlot) return;  // đã đúng vị trí

            // Chỉ di chuyển nếu slot đích trống
            if (attackerSlot >= 0 && attackerSlot < MaxBenchSize && battlefield[attackerSlot] == null)
            {
                battlefield[currentSlot] = null;
                battlefield[attackerSlot] = card;
                card.slotIndex = attackerSlot;
                OnBattlefieldCompacted?.Invoke(this, new List<CardModel> { card });
            }
            // Nếu slot đích bị chiếm → giữ nguyên vị trí (edge case)
        }

        // ── Compact attackers (gọi sau mỗi lần undeclare) ────────
        /// <summary>
        /// Dồn tất cả unit đang tấn công (state == Attacking) về slot liên tục từ trái.
        /// Gọi ngay sau HandleUndeclare để không có khoảng trống giữa các attacker.
        /// Fire OnBattlefieldCompacted với danh sách card đã đổi slot → view rebuild vị trí.
        /// </summary>
        public void CompactAttackerSlots()
        {
            // Thu thập attacker theo thứ tự slot hiện tại (trái → phải)
            var attackers = new List<CardModel>();
            for (int i = 0; i < MaxBenchSize; i++)
                if (battlefield[i] != null && battlefield[i].state == CardState.Attacking)
                    attackers.Add(battlefield[i]);

            if (attackers.Count == 0) return;

            // Xóa attacker khỏi mảng (non-attacker giữ nguyên vị trí)
            foreach (var atk in attackers)
                battlefield[atk.slotIndex] = null;

            // Điền lại compact từ trái, bỏ qua slot có non-attacker
            var moved = new List<CardModel>();
            int fill = 0;
            foreach (var atk in attackers)
            {
                while (fill < MaxBenchSize && battlefield[fill] != null) fill++;
                if (fill >= MaxBenchSize) break;

                int oldSlot = atk.slotIndex;
                battlefield[fill] = atk;
                atk.slotIndex = fill;   // cập nhật trực tiếp, không fire OnLocationChanged

                if (oldSlot != fill)
                    moved.Add(atk);

                fill++;
            }

            if (moved.Count > 0)
                OnBattlefieldCompacted?.Invoke(this, moved);
        }

        // ── Battlefield → Bench (sau combat) ─────────────────────
        public void ReturnToBench(CardModel card)
        {
            int bfSlot = card.slotIndex;
            if (bfSlot < 0 || battlefield[bfSlot] != card)
            {
                card.ResetCombatState();
                return;
            }

            int slot = FindEmptyBenchSlot();
            if (slot < 0)
            {
                UnityEngine.Debug.LogWarning(
                    $"[PlayerModel] Bench đầy — {card.data.cardName} bị đưa vào discard sau combat.");
                battlefield[bfSlot] = null;
                card.ResetCombatState();
                KillCard(card);
                return;
            }

            battlefield[bfSlot] = null;
            bench[slot] = card;
            card.SetLocation(CardLocation.OnBench, slot);
            card.ResetCombatState();
            OnCardReturnedToBench?.Invoke(this, card);
        }

        // ── Recall (Bench hoặc Battlefield → Hand, strip all effects) ────────────────
        /// <summary>
        /// Recall unit về tay đúng như LoR:
        ///   1. Xóa card khỏi bench hoặc battlefield.
        ///   2. Gọi RecallReset() — xóa MỌI buff/debuff, đưa stats về base form hiện tại.
        ///   3. Nếu hand đầy (≥ MaxHandSize) → card bị obliterate (vào discard), không về tay.
        ///   4. Nếu hand còn chỗ → đưa về hand, fire OnCardRecalled.
        ///
        /// Trả về true nếu xử lý được (kể cả khi bị obliterate do hand đầy).
        /// Trả về false nếu card không ở bench/battlefield (caller nên log warning).
        /// </summary>
        public bool RecallCard(CardModel card)
        {
            // Xóa card khỏi vùng hiện tại
            bool removed = false;

            if (card.location == CardLocation.OnBench
                && card.slotIndex >= 0
                && card.slotIndex < MaxBenchSize
                && bench[card.slotIndex] == card)
            {
                bench[card.slotIndex] = null;
                removed = true;
            }
            else if (card.location == CardLocation.OnBattlefield
                     && card.slotIndex >= 0
                     && card.slotIndex < MaxBenchSize
                     && battlefield[card.slotIndex] == card)
            {
                battlefield[card.slotIndex] = null;
                removed = true;
            }

            if (!removed) return false;

            // Strip ALL effects — đúng cơ chế LoR Recall
            card.RecallReset();

            // Hand đầy → obliterate (giống LoR: card biến mất, không về tay)
            if (hand.Count >= MaxHandSize)
            {
                UnityEngine.Debug.Log(
                    $"[Recall] {card.data.cardName} bị obliterate — tay đang đầy ({hand.Count}/{MaxHandSize}).");
                discard.Add(card);
                card.SetLocation(CardLocation.InDiscard);
                // Dùng OnCardDied để View dọn dẹp, không fire OnCardRecalled
                OnCardDied?.Invoke(this, card);
                return true;
            }

            // Đưa về tay
            hand.Add(card);
            card.SetLocation(CardLocation.InHand, hand.Count - 1);
            OnCardRecalled?.Invoke(this, card);
            return true;
        }

        // ── Die ───────────────────────────────────────────────────
        public void KillCard(CardModel card)
        {
            if (card.location == CardLocation.OnBench && card.slotIndex >= 0)
                bench[card.slotIndex] = null;
            if (card.location == CardLocation.OnBattlefield && card.slotIndex >= 0)
                battlefield[card.slotIndex] = null;
            hand.Remove(card);

            // LOOP MODE KHÔNG can thiệp ở đây nữa: bản sao đã được chèn vào deck NGAY LÚC DÙNG BÀI
            // (xem SeedCopyToDeck). Unit chết vẫn vào discard bình thường như luật gốc.
            discard.Add(card);
            card.SetLocation(CardLocation.InDiscard);
            OnCardDied?.Invoke(this, card);
        }

        // ── Helpers ───────────────────────────────────────────────
        public int FindEmptyBenchSlot()
        {
            for (int i = 0; i < MaxBenchSize; i++)
                if (bench[i] == null) return i;
            return -1;
        }

        public int FindEmptyBattlefieldSlot()
        {
            for (int i = 0; i < MaxBenchSize; i++)
                if (battlefield[i] == null) return i;
            return -1;
        }

        public bool BenchHasSpace => FindEmptyBenchSlot() >= 0;
        public bool BattlefieldHasSpace => FindEmptyBattlefieldSlot() >= 0;

        public IEnumerable<CardModel> BenchCards()
        {
            foreach (var c in bench) if (c != null) yield return c;
        }
        public IEnumerable<CardModel> BattlefieldCards()
        {
            foreach (var c in battlefield) if (c != null) yield return c;
        }
    }
}