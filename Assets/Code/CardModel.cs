using System;
using System.Collections.Generic;
using LoRClone.Data;

namespace LoRClone.Model
{
    public enum CardLocation { InDeck, InHand, OnBench, OnBattlefield, StagingSpell, OnStack, InDiscard, Equipped }
    public enum CardState { Idle, Attacking, Blocking, Dead }

    public partial class CardModel
    {
        // ── Identity ──────────────────────────────────────────────
        public readonly int id;
        public CardData data { get; private set; }
        /// <summary>
        /// CardData gốc lúc khởi tạo — không bao giờ thay đổi kể cả khi level up.
        /// Dùng để match relatedCards sau khi unit đã level up (data trỏ sang levelUpForm).
        /// </summary>
        public readonly CardData originalData;
        public bool belongsToPlayer { get; set; }

        /// <summary>
        /// True nếu CardModel này được tạo lúc DỰNG DECK (lá gốc trong deck).
        /// False cho lá tạo bởi hiệu ứng (summon / token / generated / equipment tự đeo).
        /// LOOP MODE chỉ recycle lá fromDeck — lá summon KHÔNG bao giờ chui vào deck.
        /// </summary>
        public bool fromDeck { get; private set; }

        // ── Stats ─────────────────────────────────────────────────
        public int currentHealth { get; private set; }
        public int currentAttack { get; private set; }
        public int maxHealth { get; private set; }

        // ── Keyword: Barrier / Bảo Hộ ────────────────────────────
        /// <summary>
        /// Giảm 1 damage từ mọi nguồn vĩnh viễn. Khi nhận damage từ unit → +1|+1.
        /// hasBarrier = true khi có keyword hoặc được cấp runtime; không bao giờ bị "tiêu hao".
        /// </summary>
        public bool hasBarrier { get; private set; }

        // ── Keyword: SpellShield / Hòa Hợp ───────────────────────
        /// <summary>
        /// Hấp thụ 1 đòn bất kỳ (unit hoặc spell/skill địch). Tiêu hao sau khi kích hoạt.
        /// Icon mờ khi đã hết. Khi tấn công → +1|+1 cho toàn bộ đồng minh trên bench.
        /// </summary>
        public bool hasSpellShield { get; private set; }

        // ── Keyword: Lifesteal runtime grant / Trù Phú ───────────
        /// <summary>
        /// Được cấp runtime khi một đồng minh có Trù Phú chết.
        /// Khi unit này gây damage, hồi nexus bằng lượng đó.
        /// </summary>
        public bool hasLifesteal { get; private set; }

        // ── Keyword: CantBlock charges / Vui Vẻ ──────────────────
        /// <summary>
        /// Mỗi 3 spell mana tiêu hao → +1 charge tích lũy.
        /// Khi tấn công nexus, gây thêm damage bằng số charges hiện có.
        /// </summary>
        public int spellCharges { get; private set; }
        private int _spellManaTowardCharge;

        // ── Keyword: Fearsome temp debuff / Hư Vô ────────────────
        /// <summary>
        /// Giảm ATK tạm thời trong round này (do Hư Vô của attacker áp lên khi combat).
        /// Reset tự động đầu mỗi round.
        /// </summary>
        public int tempAtkDebuff { get; private set; }

        // ── Temporary buff (trong vòng) ───────────────────────────
        /// <summary>Tổng ATK buff tạm thời đã nhận trong round này. Reset đầu round.</summary>
        private int _tempAtkBuffRound;
        /// <summary>Tổng HP buff tạm thời đã nhận trong round này. Reset đầu round.</summary>
        private int _tempHpBuffRound;

        // ── New keyword state fields ──────────────────────────────
        /// <summary>Vực Thẳm (Deep): true sau khi đã nhận +3|+3 — không áp lại lần 2.</summary>
        public bool deepTriggered { get; private set; }

        /// <summary>Tiên Phong (Scout): true sau khi đã tấn công lần đầu round này — không hoàn token lần 2.</summary>
        public bool scoutFiredThisRound { get; private set; }

        /// <summary>Thiên Cơ (Fated): true sau khi đã nhận buff lần đầu round này — không buff lần 2.</summary>
        public bool fatedTriggeredThisRound { get; private set; }

        /// <summary>Chiến Lợi (Plunder): true sau khi đã nhận buff trong combat này — reset khi combat mới.</summary>
        public bool plunderFiredThisAttack { get; private set; }

        /// <summary>Đếm Ngược (Countdown): số round còn lại trước khi unit tự hủy. -1 nếu không có keyword.</summary>
        public int countdownRemaining { get; private set; } = -1;

        /// <summary>
        /// ATK thực tế sau tempAtkDebuff — dùng trong mọi tính toán combat và block check.
        /// Thần Thép (Formidable): ATK = maxHealth thay vì currentAttack.
        /// </summary>
        public int effectiveAttack
        {
            get
            {
                int baseAtk = HasKeyword(KeywordType.Formidable) ? maxHealth : currentAttack;
                return Math.Max(0, baseAtk - tempAtkDebuff);
            }
        }

        // ── Location & State ──────────────────────────────────────
        public CardLocation location { get; set; } = CardLocation.InDeck;
        public CardState state { get; set; } = CardState.Idle;
        public int slotIndex { get; set; } = -1;

        /// <summary>
        /// Slot bench gốc — được set khi card vào OnBench và KHÔNG bị ghi đè khi card di chuyển lên
        /// OnBattlefield. Dùng bởi Support (Cộng Lực) để tìm đúng neighbor trong bench[].
        /// Lý do cần riêng: MoveToBattlefield() gán slotIndex = FindEmptyBattlefieldSlot() (slot mới),
        /// xóa mất thông tin bench slot gốc.
        /// </summary>
        public int benchSlot { get; private set; } = -1;

        // ── Combat ────────────────────────────────────────────────
        public CardModel blockedBy { get; set; }
        public CardModel blockingTarget { get; set; }

        // ── Mana Cost Reduction ───────────────────────────────────
        /// <summary>Tổng lượng mana cost được giảm runtime (không âm). Dùng bởi SkillReduceRelatedManaCost.</summary>
        public int manaCostReduction { get; private set; }

        /// <summary>
        /// Giảm giá NGOÀI tính ĐỘNG mỗi lần đọc cost (KHÔNG sửa manaCostReduction → không cộng dồn).
        /// Dùng cho Tinh Hồn ★1/★3 "lá đầu vòng -mana". CampaignRun gán hàm này; null = không có.
        /// </summary>
        public static System.Func<CardModel, int> ExternalCostDiscount;

        /// <summary>Chi phí thực tế sau khi tính reduction + giảm giá ngoài (động). Không bao giờ âm.</summary>
        public int currentManaCost =>
            Math.Max(0, data.manaCost - manaCostReduction - (ExternalCostDiscount != null ? ExternalCostDiscount(this) : 0));

        // ── Equipment ─────────────────────────────────────────────
        /// <summary>Equipment đang gắn vào unit này. Null nếu không có.</summary>
        public CardModel equipmentAttached { get; private set; }

        /// <summary>Unit mà equipment này đang được gắn vào. Null nếu không có.</summary>
        public CardModel equippedTo { get; private set; }

        public void AttachEquipment(CardModel equipment) => equipmentAttached = equipment;
        public void DetachEquipment() => equipmentAttached = null;
        public void AttachToUnit(CardModel unit) => equippedTo = unit;
        public void DetachFromUnit() => equippedTo = null;

        // ── Runtime Keywords ──────────────────────────────────────
        /// <summary>Keyword được cấp runtime (không sửa ScriptableObject). Dùng bởi SkillBuffRelatedKeyword.</summary>
        private readonly HashSet<KeywordType> _runtimeKeywords = new HashSet<KeywordType>();

        // ── Bonus Skills (từ TRANG BỊ / CỔ VẬT đã lắp) ────────────
        /// <summary>
        /// Skill được GẮN từ trang bị/cổ vật đã lắp (ItemData.skills / RelicData.skills).
        /// Chạy theo TRIGGER giống hệt skill gốc của card. Gán TƯƠI mỗi trận
        /// (CampaignRun.ApplyChampionKeywordsAndSkills gọi ClearBonusSkills rồi AddBonusSkill).
        /// </summary>
        private readonly List<SkillData> _bonusSkills = new List<SkillData>();
        public IReadOnlyList<SkillData> bonusSkills => _bonusSkills;
        public void AddBonusSkill(SkillData s) { if (s != null && !_bonusSkills.Contains(s)) _bonusSkills.Add(s); }
        public void ClearBonusSkills() => _bonusSkills.Clear();

        // ── Once-per-round ability tracker ───────────────────────
        /// <summary>Index của các ability (trong data.abilities) đã kích hoạt trong round này.</summary>
        private readonly HashSet<int> _abilitiesFiredThisRound = new HashSet<int>();

        public bool HasAbilityFiredThisRound(int abilityIndex)
            => _abilitiesFiredThisRound.Contains(abilityIndex);

        public void MarkAbilityFired(int abilityIndex)
            => _abilitiesFiredThisRound.Add(abilityIndex);

        public void ResetAbilityFiredTracker()
            => _abilitiesFiredThisRound.Clear();

        // ── Buff History (lịch sử nguồn gốc buff) ────────────────
        // Hiện trong panel bên trái khi inspect card.
        // Mỗi entry ghi nhận: nguồn gốc buff + lượng thay đổi stat.
        private readonly List<BuffRecord> _buffHistory = new List<BuffRecord>();

        /// <summary>Danh sách lịch sử buff — read-only cho CardView.</summary>
        public IReadOnlyList<BuffRecord> buffHistory => _buffHistory;

        /// <summary>
        /// Ghi nhận buff từ equipment.
        /// Gọi trong ApplyEquipment() sau khi BuffAttack/BuffHealth.
        /// Nếu cùng equipment đã buff trước (replace) → merge entry.
        /// </summary>
        public void RecordEquipmentBuff(string equipName, UnityEngine.Texture2D artwork,
                                        int atkBuff, int hpBuff)
        {
            if (atkBuff == 0 && hpBuff == 0) return;
            foreach (var r in _buffHistory)
            {
                if (r.sourceType == BuffRecord.SourceType.Equipment
                    && r.sourceName == equipName)
                {
                    r.attackBuff += atkBuff;
                    r.healthBuff += hpBuff;
                    return;
                }
            }
            _buffHistory.Add(BuffRecord.FromEquipment(equipName, artwork, atkBuff, hpBuff));
        }

        /// <summary>
        /// Xóa entry buff của equipment cũ khi detach (unit đổi equipment hoặc equipment bị gỡ).
        /// Gọi trong UnlinkEquipment / trước khi apply equipment mới.
        /// </summary>
        public void RemoveEquipmentBuff(string equipName)
        {
            _buffHistory.RemoveAll(r =>
                r.sourceType == BuffRecord.SourceType.Equipment
                && r.sourceName == equipName);
        }

        /// <summary>
        /// Ghi nhận buff từ keyword (Fury +1|+1, Scout +0|+0 v.v.)
        /// Nếu cùng keyword đã có entry → cộng dồn (Fury có thể stack nhiều lần).
        /// </summary>
        public void RecordKeywordBuff(string keywordDisplayName, int atkBuff, int hpBuff)
        {
            if (atkBuff == 0 && hpBuff == 0) return;
            foreach (var r in _buffHistory)
            {
                if (r.sourceType == BuffRecord.SourceType.Keyword
                    && r.sourceName == keywordDisplayName)
                {
                    r.attackBuff += atkBuff;
                    r.healthBuff += hpBuff;
                    return;
                }
            }
            _buffHistory.Add(BuffRecord.FromKeyword(keywordDisplayName, atkBuff, hpBuff));
        }

        /// <summary>
        /// GHI NHẬN buff CHUNG — điểm vào mở rộng cho MỌI nguồn (item, aura, relic...).
        /// Nguồn mới chỉ cần: BuffRecord.Create(...) rồi gọi RecordBuff(...). Không cần thêm hàm riêng.
        /// Tự gộp entry cùng (sourceType + SourceKey).
        /// </summary>
        public void RecordBuff(BuffRecord r)
        {
            if (r == null) return;
            foreach (var e in _buffHistory)
            {
                if (e.sourceType == r.sourceType && e.SourceKey == r.SourceKey)
                {
                    e.attackBuff += r.attackBuff;
                    e.healthBuff += r.healthBuff;
                    if (string.IsNullOrEmpty(e.description)) e.description = r.description;
                    return;
                }
            }
            _buffHistory.Add(r);
        }

        /// <summary>
        /// Ghi nhận buff từ skill / spell (tên skill, artwork của lá kích hoạt).
        /// Nếu cùng skill đã có entry → cộng dồn.
        /// </summary>
        public void RecordSkillBuff(string skillName, UnityEngine.Texture2D artwork,
                                    int atkBuff, int hpBuff, bool temporary = false)
        {
            if (atkBuff == 0 && hpBuff == 0) return;
            foreach (var r in _buffHistory)
            {
                if (r.sourceType == BuffRecord.SourceType.Skill
                    && r.sourceName == skillName)
                {
                    r.attackBuff += atkBuff;
                    r.healthBuff += hpBuff;
                    r.isTemporary = temporary;
                    return;
                }
            }
            _buffHistory.Add(BuffRecord.FromSkill(skillName, artwork, atkBuff, hpBuff, temporary));
        }

        /// <summary>
        /// Ghi nhận nguồn gốc: lá này được tạo ra bởi card nào.
        /// Hiện dạng "Tạo ra lá bài này" trong inspect panel.
        /// Chỉ set 1 lần — gọi khi SummonUnit tạo ra generated card.
        /// </summary>
        public void RecordOrigin(string creatorName, UnityEngine.Texture2D creatorArtwork)
        {
            _buffHistory.RemoveAll(r => r.IsOrigin);
            _buffHistory.Insert(0, BuffRecord.Origin(creatorName, creatorArtwork));
        }

        /// <summary>Xóa toàn bộ buff history (dùng khi recall / reset stats).</summary>
        public void ClearBuffHistory() => _buffHistory.Clear();

        // ── Damage tracking ───────────────────────────────────────
        /// <summary>Tổng damage thực tế đã nhận từ đầu ván (sau Bảo Hộ / SpellShield). Dùng cho DamageTakenTrigger.</summary>
        public int damageTakenTotal { get; private set; }

        /// <summary>Tổng damage unit này ĐÃ GÂY ra từ đầu ván (combat + skill). Dùng cho level-up damage-based (Mydei) & DamageDealtTrigger.</summary>
        public int damageDealtTotal { get; private set; }

        /// <summary>Cộng dồn damage unit này gây ra. Gọi qua GameController tại các điểm gây damage.</summary>
        public void AddDamageDealt(int amount)
        {
            if (amount > 0) damageDealtTotal += amount;
        }

        /// <summary>Tổng số lần CHÍNH unit này confirm tấn công từ đầu ván. Dùng cho CardCreationTrigger (counter=AttacksMade, scope=Self).</summary>
        public int attacksMadeTotal { get; private set; }

        /// <summary>Cộng dồn số lần tấn công. Gọi bởi GameController tại B6 (confirm attack).</summary>
        public void AddAttackMade(int count = 1)
        {
            if (count > 0) attacksMadeTotal += count;
        }

        /// <summary>Đã bị Thủ Tiêu (Obliterate) — không thể hồi sinh/tương tác. Revive effect nên bỏ qua card này.</summary>
        public bool isObliterated { get; private set; }
        public void MarkObliterated() => isObliterated = true;

        // ── Level Up ──────────────────────────────────────────────
        public int levelUpProgress { get; private set; }
        public bool hasLeveledUp { get; private set; }

        /// <summary>
        /// Điều kiện level-up đã thỏa (progress >= threshold) nhưng champion chưa ở trên sân.
        /// GameController sẽ gọi TriggerLevelUp() ngay khi champion được đặt lên bench/battlefield.
        ///
        /// LoR rule: progress tích lũy từ bất kỳ đâu (tay, deck, sân),
        /// nhưng TRANSFORMATION chỉ xảy ra khi champion đang ở trên sân.
        /// </summary>
        public bool readyToLevelUp { get; private set; }

        // ── Events ────────────────────────────────────────────────
        public event Action<CardModel> OnStatsChanged;
        public event Action<CardModel> OnLocationChanged;
        public event Action<CardModel> OnLeveledUp;

        // ── Sound Events (chỉ dùng bởi CardView / AudioManager) ──
        /// <summary>Fire khi unit nhận damage thực sự (sau Barrier/SpellShield).</summary>
        public event Action<CardModel> OnDamaged;
        /// <summary>Fire khi unit bắt đầu tấn công. Gọi từ GameController.</summary>
        public event Action<CardModel> OnAttacked;
        /// <summary>Fire một lần khi HP về 0 lần đầu.</summary>
        public event Action<CardModel> OnDied;

        private static int _nextId = 1;

        public CardModel(CardData data, bool belongsToPlayer, bool fromDeck = false)
        {
            id = _nextId++;
            this.fromDeck = fromDeck;
            this.data = data;
            originalData = data;   // snapshot gốc — không đổi dù level up
            this.belongsToPlayer = belongsToPlayer;
            currentHealth = data.baseHealth;
            maxHealth = data.baseHealth;
            currentAttack = data.baseAttack;

            // Khởi tạo runtime keyword state từ CardData
            hasBarrier = data.keywords.Contains(KeywordType.Barrier);
            hasSpellShield = data.keywords.Contains(KeywordType.SpellShield);
            hasLifesteal = data.keywords.Contains(KeywordType.Lifesteal);

            // Khởi tạo Countdown
            if (data.keywords.Contains(KeywordType.Countdown))
                countdownRemaining = data.countdownRounds;
        }

        // ── Tag Helper ────────────────────────────────────────────
        /// <summary>Kiểm tra card có tag này không (theo data hiện tại + originalData). Không phân biệt hoa/thường.</summary>
        public bool HasTag(string tag)
            => (data != null && data.HasTag(tag)) || (originalData != null && originalData.HasTag(tag));

        // ── Keyword Helper ────────────────────────────────────────
        /// <summary>Kiểm tra card có keyword này không (data asset + runtime grant + conditional ATK/HP).</summary>
        public bool HasKeyword(KeywordType kw)
        {
            // Barrier là khiên TIÊU HAO → "có Barrier" phải theo cờ sống hasBarrier
            // (không thì đã tiêu khiên mà data.keywords vẫn khiến nó "còn Barrier").
            if (kw == KeywordType.Barrier) return hasBarrier;
            if (data.keywords.Contains(kw) || _runtimeKeywords.Contains(kw)) return true;
            if (data.conditionalKeywords != null)
                foreach (var ck in data.conditionalKeywords)
                    if (ck.keyword == kw && EvalCondition(ck.condition)) return true;
            return false;
        }

        /// <summary>Đánh giá StatCondition dựa trên stats hiện tại (currentAttack / currentHealth).</summary>
        public bool EvalCondition(StatCondition c)
        {
            bool atkOk = c.requiredAttack <= 0 || currentAttack >= c.requiredAttack;
            bool hpOk = c.requiredHealth <= 0 || currentHealth >= c.requiredHealth;
            return c.mode == ConditionalMode.Any ? (atkOk || hpOk) : (atkOk && hpOk);
        }

        /// <summary>
        /// Trả về toàn bộ keyword active: data asset + runtime grant + boolean shortcuts + conditional ATK.
        /// Không có duplicate. Dùng bởi CardView để render icon.
        /// SpellShield: hiện trong list kể cả khi tiêu hao — CardView tự dim icon dựa vào hasSpellShield.
        /// Conditional: keyword chỉ hiện khi currentAttack >= requiredAttack — tự ẩn nếu ATK giảm.
        /// </summary>
        public IEnumerable<KeywordType> GetAllKeywords()
        {
            var seen = new HashSet<KeywordType>();
            // 1. Keyword từ asset — BỎ QUA Barrier: nó là khiên TIÊU HAO, icon phải theo cờ hasBarrier
            //    (yield ở bước 3), không thì đã tiêu khiên rồi icon vẫn hiện → nhìn như "không mất".
            foreach (var kw in data.keywords)
                if (kw != KeywordType.Barrier && seen.Add(kw)) yield return kw;
            // 2. Keyword cấp runtime qua GrantKeyword (Barrier runtime đã bị Remove khi tiêu → không kẹt icon)
            foreach (var kw in _runtimeKeywords)
                if (seen.Add(kw)) yield return kw;
            // 3. Boolean shortcuts — cho trường hợp SetLifesteal/SetBarrier gọi trực tiếp
            if (hasLifesteal && seen.Add(KeywordType.Lifesteal)) yield return KeywordType.Lifesteal;
            if (hasBarrier && seen.Add(KeywordType.Barrier)) yield return KeywordType.Barrier;
            if (hasSpellShield && seen.Add(KeywordType.SpellShield)) yield return KeywordType.SpellShield;
            // 4. Conditional keywords — grant khi thỏa điều kiện ATK/HP
            if (data.conditionalKeywords != null)
                foreach (var ck in data.conditionalKeywords)
                    if (EvalCondition(ck.condition) && seen.Add(ck.keyword))
                        yield return ck.keyword;
        }

        /// <summary>
        /// Cấp keyword runtime cho card này (không sửa ScriptableObject).
        /// Tự động cập nhật hasBarrier / hasSpellShield / hasLifesteal nếu cần.
        /// </summary>
        public void GrantKeyword(KeywordType kw)
        {
            if (_runtimeKeywords.Add(kw))
            {
                // Sync boolean shortcuts để combat logic không bị lệch
                if (kw == KeywordType.Barrier) hasBarrier = true;
                if (kw == KeywordType.SpellShield) hasSpellShield = true;
                if (kw == KeywordType.Lifesteal) hasLifesteal = true;
                OnStatsChanged?.Invoke(this);
            }
        }

        // ── Mana Cost Reduction ───────────────────────────────────
        /// <summary>Giảm mana cost runtime. Stack được (gọi nhiều lần). Không bao giờ âm nhờ currentManaCost.</summary>
        public void ReduceManaCost(int amount)
        {
            if (amount <= 0) return;
            manaCostReduction += amount;
            OnStatsChanged?.Invoke(this);
        }

        /// <summary>Ép CardView đọc lại chỉ số/cost — dùng khi cost đổi do nguồn NGOÀI (Tinh Hồn ★1/★3)
        /// mà không sửa stat, nên không có event nào tự bắn.</summary>
        public void RaiseStatsChanged() => OnStatsChanged?.Invoke(this);

        // ── Stat Modification ─────────────────────────────────────
        /// <summary>
        /// Áp damage lên unit.
        /// fromUnit = true khi nguồn là unit combat (attacker/blocker) — dùng cho Bảo Hộ +1|+1.
        /// fromUnit = false (default) khi nguồn là spell/skill.
        /// Trả về lượng HP thực sự bị mất (trước khi Bảo Hộ BuffHealth bù lại).
        /// GameController.DealDamage dùng giá trị này để kiểm tra kill/damage dealt chính xác.
        /// </summary>
        public int TakeDamage(int amount, bool fromUnit = false)
        {
            if (amount <= 0) return 0;

            // Hòa Hợp (SpellShield): hấp thụ toàn bộ 1 đòn bất kỳ — unit lẫn spell
            if (hasSpellShield)
            {
                hasSpellShield = false;
                OnStatsChanged?.Invoke(this);
                return 0;
            }

            // Bảo Hộ (Barrier) — chuẩn LoR: vô hiệu HOÀN TOÀN 1 đòn kế tiếp rồi tiêu.
            if (hasBarrier)
            {
                hasBarrier = false;
                _runtimeKeywords.Remove(KeywordType.Barrier);
                OnStatsChanged?.Invoke(this);
                return 0;
            }

            int healthBefore = currentHealth;
            int newHealth = Math.Max(0, currentHealth - amount);

            // Bất Diệt khi tấn công (Mydei): CHỈ với sát thương combat từ unit (fromUnit).
            // Đòn combat chí tử khi unit đang tấn công → sống ở 1 máu + nhận 1 Khiên (Barrier).
            // Spell/skill đủ dame kill → CHẾT bình thường (không cứu). Phòng thủ/chặn cũng chết bình thường.
            if (newHealth <= 0
                && fromUnit
                && data != null && data.surviveLethalWhileAttacking
                && state == CardState.Attacking)
            {
                currentHealth = 1;
                int survived = healthBefore - 1;
                if (survived > 0) damageTakenTotal += survived;
                GrantKeyword(KeywordType.Barrier);   // +1 Khiên (cũng set hasBarrier)
                OnStatsChanged?.Invoke(this);
                if (survived > 0 && state != CardState.Dead) OnDamaged?.Invoke(this);
                UnityEngine.Debug.Log($"[Bất Diệt] {data.cardName} đang tấn công — sống ở 1 máu + nhận Khiên.");
                return survived;
            }

            currentHealth = newHealth;
            int actualDamage = healthBefore - currentHealth;
            damageTakenTotal += actualDamage;   // cộng dồn cho DamageTakenTrigger
            OnStatsChanged?.Invoke(this);

            if (actualDamage > 0)
            {
                // Death override: nếu chết thì chỉ fire OnDied, bỏ qua OnDamaged
                if (currentHealth <= 0 && state != CardState.Dead)
                {
                    state = CardState.Dead;
                    OnDied?.Invoke(this);       // sound: chết (fire đúng 1 lần)
                }
                else if (state != CardState.Dead)
                {
                    OnDamaged?.Invoke(this);    // sound: nhận damage nhưng còn sống
                }
            }

            // Trả về actualDamage (HP mất thực sự). DealDamage dùng để đo đúng kill/damage.
            return actualDamage;
        }

        public void Heal(int amount)
        {
            if (amount <= 0) return;
            currentHealth = Math.Min(maxHealth, currentHealth + amount);
            OnStatsChanged?.Invoke(this);
        }

        /// <summary>
        /// Giết unit ngay lập tức không qua damage — dùng cho Cảm Tử (Ephemeral).
        /// Không fire OnDamaged, chỉ fire OnDied (1 lần) và cập nhật state = Dead.
        /// Sau khi gọi ForceKill(), cần gọi ProcessDeaths() để Last Breath và dọn sân.
        /// </summary>
        public void ForceKill()
        {
            if (!IsAlive) return;   // đã chết rồi, bỏ qua
            currentHealth = 0;
            if (state != CardState.Dead)
            {
                state = CardState.Dead;
                OnDied?.Invoke(this);
            }
            OnStatsChanged?.Invoke(this);
        }

        // ── Support indicator ─────────────────────────────────────
        /// <summary>
        /// True khi card này đang là target của Support (đồng minh bên trái có Support đang tấn công).
        /// GameController set/clear — CardView dùng để hiện UI glow.
        /// </summary>
        public bool isSupportTarget { get; private set; }

        public void SetSupportTarget(bool value)
        {
            if (isSupportTarget == value) return;
            isSupportTarget = value;
            OnStatsChanged?.Invoke(this); // CardView.RefreshHighlights() tự cập nhật
        }

        /// <summary>
        /// Set trực tiếp ATK và HP — không qua Barrier/SpellShield.
        /// Dùng bởi SkillSummonCopy để clone chính xác stats của source.
        /// currentHp được clamp vào [1, maxHp].
        /// </summary>
        public void ForceStats(int atk, int maxHp, int currentHp)
        {
            currentAttack = Math.Max(0, atk);
            maxHealth = Math.Max(1, maxHp);
            currentHealth = Math.Clamp(currentHp, 1, maxHealth);
            OnStatsChanged?.Invoke(this);
        }

        /// <param name="temporary">true = chỉ trong vòng này, tự reset đầu round tiếp theo.</param>
        public void BuffAttack(int amount, bool temporary = false)
        {
            currentAttack = Math.Max(0, currentAttack + amount);
            if (temporary) _tempAtkBuffRound += amount;
            OnStatsChanged?.Invoke(this);
        }

        /// <param name="temporary">true = chỉ trong vòng này, tự reset đầu round tiếp theo.</param>
        public void BuffHealth(int amount, bool temporary = false)
        {
            maxHealth += amount;
            currentHealth += amount;
            if (temporary) _tempHpBuffRound += amount;
            OnStatsChanged?.Invoke(this);
        }

        // ── Barrier ───────────────────────────────────────────────
        public void SetBarrier(bool value)
        {
            hasBarrier = value;
            OnStatsChanged?.Invoke(this);
        }

        // ── SpellShield / Hòa Hợp ────────────────────────────────
        public void SetSpellShield(bool value)
        {
            hasSpellShield = value;
            OnStatsChanged?.Invoke(this);
        }

        /// <summary>
        /// Tiêu SpellShield nếu đang có. Trả về true = đã chặn, caller nên bỏ qua hiệu ứng.
        /// Dùng trong FilterSpellShield (spell targeting) — combat dùng TakeDamage trực tiếp.
        /// </summary>
        public bool TryConsumeSpellShield()
        {
            if (!hasSpellShield) return false;
            SetSpellShield(false);
            return true;
        }

        // ── Lifesteal / Trù Phú ───────────────────────────────────
        /// <summary>Cấp/thu hồi Lifesteal runtime (khi đồng minh Trù Phú chết).</summary>
        public void SetLifesteal(bool value)
        {
            hasLifesteal = value;
            OnStatsChanged?.Invoke(this);
        }

        // ── CantBlock charges / Vui Vẻ ───────────────────────────
        /// <summary>
        /// Cộng thêm spell mana vào bộ đếm. Cứ 3 spell mana tích đủ → +1 charge.
        /// Gọi từ GameController mỗi khi PlayerModel tiêu spell mana.
        /// </summary>
        public void AddSpellCharge(int spellManaAmount)
        {
            if (spellManaAmount <= 0) return;
            _spellManaTowardCharge += spellManaAmount;
            int newCharges = _spellManaTowardCharge / 3;
            _spellManaTowardCharge %= 3;
            spellCharges += newCharges;
            if (newCharges > 0) OnStatsChanged?.Invoke(this);
        }

        // ── Fearsome temp debuff / Hư Vô ─────────────────────────
        /// <summary>Giảm ATK tạm thời (reset mỗi round). Áp bởi attacker Hư Vô lên blocker.</summary>
        public void ApplyTempAtkDebuff(int amount)
        {
            if (amount <= 0) return;
            tempAtkDebuff += amount;
            OnStatsChanged?.Invoke(this);
        }

        /// <summary>Reset toàn bộ hiệu ứng tạm thời theo round. Gọi đầu mỗi RoundStart.</summary>
        public void ResetTempEffects()
        {
            bool changed = false;

            if (tempAtkDebuff != 0)
            {
                tempAtkDebuff = 0;
                changed = true;
            }

            if (_tempAtkBuffRound != 0)
            {
                currentAttack = Math.Max(0, currentAttack - _tempAtkBuffRound);
                _tempAtkBuffRound = 0;
                changed = true;
            }

            if (_tempHpBuffRound != 0)
            {
                maxHealth = Math.Max(1, maxHealth - _tempHpBuffRound);
                currentHealth = Math.Min(currentHealth, maxHealth);
                _tempHpBuffRound = 0;
                changed = true;
            }

            // Xoá entry buff "trong vòng này" khỏi lịch sử — panel inspect không còn
            // hiện buff đã hết hạn (khớp với việc currentAttack/currentHealth vừa reset ở trên).
            if (_buffHistory.RemoveAll(r => r.isTemporary) > 0)
                changed = true;

            // Reset per-round new keyword flags
            scoutFiredThisRound = false;
            fatedTriggeredThisRound = false;
            plunderFiredThisAttack = false;

            // Reset tracker oncePerRound đầu mỗi round
            ResetAbilityFiredTracker();

            if (changed) OnStatsChanged?.Invoke(this);
        }

        // ── Recall reset ──────────────────────────────────────────
        /// <summary>
        /// Reset card về "natural state" đúng như LoR khi bị Recall.
        ///
        /// Xóa: mọi buff stats, runtime keywords, mana cost reduction, temp effects,
        ///       spell charges, combat state, per-round flags, ability tracker, buff history.
        ///
        /// Giữ nguyên: data (form hiện tại kể cả đã level-up), hasLeveledUp,
        ///             levelUpProgress, readyToLevelUp.
        ///
        /// Gọi bởi PlayerModel.RecallCard() trước khi đưa card về hand.
        /// </summary>
        public void RecallReset()
        {
            // ── Stats về base của form hiện tại ──────────────────────
            // Nếu champion đã level-up, data đã trỏ sang levelUpForm
            // → baseAttack/baseHealth tự động đúng form đó.
            currentAttack = data.baseAttack;
            maxHealth = data.baseHealth;
            currentHealth = data.baseHealth;

            // ── Runtime keywords ──────────────────────────────────────
            _runtimeKeywords.Clear();
            // Sync boolean shortcuts từ data asset form hiện tại
            hasBarrier = data.keywords.Contains(KeywordType.Barrier);
            hasSpellShield = data.keywords.Contains(KeywordType.SpellShield);
            hasLifesteal = data.keywords.Contains(KeywordType.Lifesteal);

            // ── Mana cost reduction ───────────────────────────────────
            manaCostReduction = 0;

            // ── Temp effects ──────────────────────────────────────────
            // Stats đã được reset về base ở trên nên không cần trừ ngược
            // như ResetTempEffects — chỉ cần xóa tracker.
            _tempAtkBuffRound = 0;
            _tempHpBuffRound = 0;
            tempAtkDebuff = 0;

            // ── Spell charges (Vui Vẻ / CantBlock) ───────────────────
            spellCharges = 0;
            _spellManaTowardCharge = 0;

            // ── Per-round flags ───────────────────────────────────────
            scoutFiredThisRound = false;
            fatedTriggeredThisRound = false;
            plunderFiredThisAttack = false;
            deepTriggered = false;   // Vực Thẳm: có thể trigger lại sau khi re-summon

            // ── Countdown ─────────────────────────────────────────────
            if (data.keywords.Contains(KeywordType.Countdown))
                countdownRemaining = data.countdownRounds;

            // ── Combat state ──────────────────────────────────────────
            blockedBy = null;
            blockingTarget = null;
            state = CardState.Idle;
            isSupportTarget = false;

            // ── Slot (benchSlot reset — SetLocation sẽ gán lại khi vào bench) ──
            benchSlot = -1;

            // ── Ability tracker ───────────────────────────────────────
            _abilitiesFiredThisRound.Clear();

            // ── Damage taken (reset để DamageTakenTrigger không re-fire khi re-summon) ──
            damageTakenTotal = 0;
            damageDealtTotal = 0;
            attacksMadeTotal = 0;

            // ── Buff history (xóa khi recall — card về tay sạch sẽ) ──
            _buffHistory.Clear();

            OnStatsChanged?.Invoke(this);
        }

        // ── Recycle reset (LOOP MODE) ─────────────────────────────
        /// <summary>
        /// LOOP MODE — reset lá để tái sử dụng vô hạn: xoá mọi buff/keyword runtime,
        /// đưa stats về base, gỡ link trang bị. GIỮ NGUYÊN level-up (form đã tiến hoá +
        /// hasLeveledUp + progress) — champion đã lên cấp về deck vẫn ở form đã level.
        /// Gọi bởi PlayerModel.KillCard khi recycleUsedCards = true, TRƯỚC khi shuffle vào deck.
        /// (OnCardDied đã fire trước đó nên trang bị đã được trả về tay.)
        /// </summary>
        public void ResetForRecycle()
        {
            // ── Level-up: GIỮ NGUYÊN (data = form hiện tại, hasLeveledUp, levelUpProgress,
            //    readyToLevelUp đều KHÔNG đụng tới). Stats reset về base của FORM HIỆN TẠI —
            //    nếu champion đã level-up thì data đã trỏ sang levelUpForm nên base tự đúng.

            // ── Stats về base của form hiện tại ───────────────────────
            currentAttack = data.baseAttack;
            maxHealth = data.baseHealth;
            currentHealth = data.baseHealth;

            // ── Runtime keywords (theo form hiện tại) ─────────────────
            _runtimeKeywords.Clear();
            hasBarrier = data.keywords.Contains(KeywordType.Barrier);
            hasSpellShield = data.keywords.Contains(KeywordType.SpellShield);
            hasLifesteal = data.keywords.Contains(KeywordType.Lifesteal);

            // ── Mana cost reduction ───────────────────────────────────
            manaCostReduction = 0;

            // ── Temp effects ──────────────────────────────────────────
            _tempAtkBuffRound = 0;
            _tempHpBuffRound = 0;
            tempAtkDebuff = 0;

            // ── Spell charges (Vui Vẻ / CantBlock) ───────────────────
            spellCharges = 0;
            _spellManaTowardCharge = 0;

            // ── Per-round / keyword flags ─────────────────────────────
            scoutFiredThisRound = false;
            fatedTriggeredThisRound = false;
            plunderFiredThisAttack = false;
            deepTriggered = false;

            // ── Countdown (theo form hiện tại) ───────────────────────
            countdownRemaining = data.keywords.Contains(KeywordType.Countdown)
                ? data.countdownRounds
                : -1;

            // ── Combat / slot state ───────────────────────────────────
            blockedBy = null;
            blockingTarget = null;
            state = CardState.Idle;
            isSupportTarget = false;
            benchSlot = -1;
            slotIndex = -1;

            // ── Equipment link (trang bị đã được trả tay ở OnCardDied) ─
            equipmentAttached = null;
            equippedTo = null;

            // ── Trackers & history ────────────────────────────────────
            _abilitiesFiredThisRound.Clear();
            damageTakenTotal = 0;
            damageDealtTotal = 0;
            attacksMadeTotal = 0;
            _buffHistory.Clear();

            OnStatsChanged?.Invoke(this);
        }

        // ── New keyword helpers ───────────────────────────────────

        /// <summary>Đánh dấu Vực Thẳm (Deep) đã trigger — không áp lại.</summary>
        public void MarkDeepTriggered()
        {
            deepTriggered = true;
        }

        /// <summary>Đánh dấu Scout đã fire round này.</summary>
        public void MarkScoutFired()
        {
            scoutFiredThisRound = true;
        }

        /// <summary>Đánh dấu Fated đã trigger round này.</summary>
        public void MarkFatedTriggered()
        {
            fatedTriggeredThisRound = true;
        }

        /// <summary>Đánh dấu Plunder đã fire trong combat này.</summary>
        public void MarkPlunderFired()
        {
            plunderFiredThisAttack = true;
        }

        /// <summary>
        /// Đếm Ngược (Countdown): giảm 1 round còn lại. Trả về true khi countdown đạt 0 (unit phải bị ForceKill).
        /// Không làm gì nếu không có keyword Countdown.
        /// </summary>
        public bool TickCountdown()
        {
            if (countdownRemaining <= 0) return false;
            countdownRemaining = Math.Max(0, countdownRemaining - 1);
            OnStatsChanged?.Invoke(this);
            return countdownRemaining == 0;
        }

        /// <summary>Thần Thép (Formidable): trigger +0|+1 khi sống sót sau khi bị hit.</summary>
        public void OnFormidableSurvive()
        {
            if (!HasKeyword(KeywordType.Formidable)) return;
            BuffHealth(1);
            UnityEngine.Debug.Log($"[Thần Thép] {data.cardName} sống sót → +0|+1.");
        }

        // ── Location ──────────────────────────────────────────────
        public void SetLocation(CardLocation loc, int slot = -1)
        {
            location = loc;
            slotIndex = slot;
            // Lưu bench slot để Support (Cộng Lực) tìm đúng neighbor sau khi card lên battlefield.
            // MoveToBattlefield() sẽ đổi slotIndex → battlefield slot, nhưng benchSlot KHÔNG bị ghi đè.
            if (loc == CardLocation.OnBench) benchSlot = slot;
            OnLocationChanged?.Invoke(this);
        }

        // ── Level Up ──────────────────────────────────────────────
        public void AddLevelUpProgress(int amount)
        {
            if (!data.canLevelUp || hasLeveledUp) return;
            levelUpProgress += amount;
        }

        /// <summary>
        /// Đánh dấu champion đã thỏa điều kiện nhưng chưa ở trên sân.
        /// Gọi từ SkillLevelUp khi progress đủ mà card đang ở tay/deck.
        /// </summary>
        public void MarkReadyToLevelUp()
        {
            if (!data.canLevelUp || hasLeveledUp || readyToLevelUp) return;
            readyToLevelUp = true;
            OnStatsChanged?.Invoke(this); // CardView có thể hiện indicator "sẵn sàng level up"
        }

        public void LevelUp()
        {
            if (!data.canLevelUp || hasLeveledUp || data.levelUpForm == null) return;

            // Tính delta TRƯỚC khi đổi form để giữ lại buff đã tích lũy.
            //
            // atkDelta = (permanent buff) + (temp buff round hiện tại, nếu có)
            //          = currentAttack - data.baseAttack
            // Cộng lại vào baseAttack của form mới → buff vĩnh viễn tự nhiên được giữ;
            // _tempAtkBuffRound vẫn nguyên → sẽ bị trừ đúng lúc reset đầu round.
            //
            // Ví dụ: base 3 | Fury +2 | temp +1 → atkDelta = 3, hpDelta tương tự.
            // Sau level up (form mới base 4): currentAttack = 4+3 = 7.
            // Đầu round kế: temp bị trừ → currentAttack = 6 (base 4 + Fury 2). Đúng.
            int atkDelta = currentAttack - data.baseAttack;
            int hpDelta = maxHealth - data.baseHealth;
            int damageTaken = maxHealth - currentHealth;

            data = data.levelUpForm;
            hasLeveledUp = true;

            maxHealth = data.baseHealth + hpDelta;
            currentHealth = Math.Max(1, maxHealth - damageTaken);
            currentAttack = data.baseAttack + atkDelta;

            OnLeveledUp?.Invoke(this);
            OnStatsChanged?.Invoke(this);
        }

        /// <summary>
        /// Biến hình unit sang một form BẤT KỲ (không cần levelUpForm), GIỮ chỉ số cộng thêm (delta buff)
        /// và lượng máu đã mất. Dùng cho Darkin transform và các biến hình đặc biệt.
        /// markLeveled = true → đánh dấu hasLeveledUp (coi như đã "hoàn tất tiến hóa").
        /// </summary>
        public void TransformInto(CardData newForm, bool markLeveled = true)
        {
            if (newForm == null) return;

            int atkDelta = currentAttack - data.baseAttack;
            int hpDelta = maxHealth - data.baseHealth;
            int damageTaken = maxHealth - currentHealth;

            data = newForm;
            if (markLeveled) hasLeveledUp = true;

            maxHealth = data.baseHealth + hpDelta;
            currentHealth = Math.Max(1, maxHealth - damageTaken);
            currentAttack = data.baseAttack + atkDelta;

            OnLeveledUp?.Invoke(this);   // tái dùng event → CardView.RefreshAll()
            OnStatsChanged?.Invoke(this);
        }

        // ── Sound helpers ─────────────────────────────────────────
        /// <summary>
        /// Gọi từ GameController ngay khi unit bắt đầu tấn công.
        /// Fire OnAttacked để CardView phát sfxAttack.
        /// </summary>
        public void NotifyAttacked() => OnAttacked?.Invoke(this);

        // ── Combat helpers ────────────────────────────────────────
        public bool IsAlive => currentHealth > 0;

        public void ResetCombatState()
        {
            blockedBy = null;
            blockingTarget = null;
            if (state == CardState.Attacking || state == CardState.Blocking)
                state = CardState.Idle;
        }

        public override string ToString() =>
            $"[{id}]{data.cardName}({currentAttack}/{currentHealth})";
    }
}