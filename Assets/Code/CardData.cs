using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Video;

namespace LoRClone.Data
{
    /// <summary>Hằng số tag chuẩn — dùng để tránh gõ sai chuỗi. Thêm tag mới tùy ý.</summary>
    public static class Tags
    {
        public const string Spider = "Spider";   // Nhện  — Elise
        public const string Dragon = "Dragon";   // Rồng  — Shyvana
        public const string Mist = "Mist";     // Sương đêm — Viego
        public const string Elite = "Elite";
        public const string Poro = "Poro";
    }

    public enum CardType { Unit, Spell, Equipment }
    public enum SpellSpeed { Burst, Fast, Slow }
    public enum SkillTrigger
    {
        // ── Self triggers (fire trên chính lá bài đó) ────────────
        WhenPlayed,
        OnAttack,
        OnBlock,
        WhenDamaged,
        OnKill,
        RoundStart,
        RoundEnd,
        /// <summary>
        /// "Last Breath" — kích hoạt ngay trước khi unit bị xóa khỏi sân vì HP ≤ 0.
        /// Tất cả Last Breath fire đồng thời trước khi bất kỳ unit nào thực sự bị xóa.
        /// </summary>
        OnDeath,

        // ── Game-flow trigger (fire theo PHA GAME, lá vẫn còn nằm TRONG DECK) ──────
        /// <summary>
        /// Fire ĐÚNG 1 LẦN, ngay sau khi CẢ HAI bên xác nhận xong Mulligan (phase rời Mulligan).
        /// Khác guaranteedInOpeningHand (ép vào tay mở đầu, hiện luôn ở MulliganView, có thể bị
        /// đổi đi): AfterMulligan KHÔNG hiện ở MulliganView — lá vẫn nằm trong deck lúc mulligan,
        /// rồi tự động RÚT vào tay như 1 lá bổ sung ngay khi mulligan xong (không tính vào 4 lá mở đầu).
        /// Chỉ cần gắn 1 skill/ability với condition = AfterMulligan vào CardData — không cần
        /// lá đang ở sân/tay để trigger fire (khác mọi trigger khác trong enum này).
        /// </summary>
        AfterMulligan,

        // ── Ally triggers (fire trên các đồng minh còn sống khi sự kiện xảy ra) ────
        /// <summary>
        /// Khi bất kỳ đồng minh nào chết (Last Breath đã fire xong, unit sắp bị xóa).
        /// ctx.triggerCard = unit vừa chết.
        /// </summary>
        OnAllyDeath,

        /// <summary>
        /// Khi bất kỳ đồng minh Unit nào được triệu hồi ra bench.
        /// ctx.triggerCard = unit vừa được triệu hồi.
        /// </summary>
        OnAllyPlayed,

        /// <summary>
        /// Khi 1 đồng minh trong danh sách relatedCards của lá này được triệu hồi.
        /// ctx.triggerCard = unit vừa được triệu hồi.
        /// Chỉ fire nếu ctx.triggerCard.originalData có trong source.data.relatedCards.
        /// </summary>
        OnRelatedAllyPlayed,

        /// <summary>
        /// Khi bất kỳ đồng minh nào nhận sát thương trong combat.
        /// ctx.triggerCard = unit nhận damage. ctx.triggerAmount = lượng damage thực nhận.
        /// </summary>
        OnAllyDamaged,

        /// <summary>
        /// Khi bất kỳ đồng minh nào gây sát thương trong combat.
        /// ctx.triggerCard = unit gây damage. ctx.triggerAmount = lượng damage đã gây.
        /// </summary>
        OnAllyDealsDamage,

        // ── Champion passive triggers ──────────────────────────────
        /// <summary>
        /// Khi ít nhất 1 đồng minh có currentAttack &gt; data.baseAttack confirm tấn công
        /// trong lượt (đồng minh "được cường hóa tấn công").
        ///
        /// ctx.triggerAmount = số đồng minh được cường hóa đang tấn công.
        ///
        /// Trigger fire trên TOÀN BỘ card của owner — hand, bench, battlefield, deck
        /// (để champion như Kayle nhận buff dù chưa được rút hay vẫn trên tay).
        ///
        /// Setup: gán CardAbility với condition = OnEmpoweredAllyAttack và effect = SkillKaylePassive.
        /// </summary>
        OnEmpoweredAllyAttack,

        /// <summary>
        /// Fire khi unit này deal damage combat lên 1 enemy unit (attacker hoặc blocker).
        /// Timing: SAU khi damage được apply, TRƯỚC khi kill/Last Breath effects.
        /// Chỉ fire nếu actual damage > 0 (SpellShield / miss = không fire).
        /// </summary>
        OnStrike,

        /// <summary>
        /// Fire khi unit này (không bị block) deal damage thẳng lên enemy nexus.
        /// Timing: SAU khi nexus nhận damage, TRƯỚC ProcessDeaths.
        /// </summary>
        OnNexusStrike,

        /// <summary>
        /// Fire ngay sau khi champion level up (transform đã xong, stats mới đã áp dụng).
        /// source = champion vừa level up.
        /// Dùng cho skill "triệu hồi bóng với chỉ số bản thân" (Zed, phân thân...).
        /// </summary>
        OnLevelUp,

        // ── "Anywhere" triggers — fire trên TOÀN BỘ card của owner (hand/bench/battlefield/DECK) ──
        // Cùng nhóm với OnEmpoweredAllyAttack. Dùng cho passive kiểu Kayle: buff dù chưa lên sân.

        /// <summary>
        /// Khi bất kỳ đồng minh nào chết — fire kể cả khi source đang ở deck/tay (khác OnAllyDeath
        /// thường, chỉ fire cho hand+bench+battlefield). ctx.triggerCard = unit vừa chết.
        /// </summary>
        OnAllyDeathAnywhere,

        /// <summary>
        /// Khi có ít nhất 1 đồng minh confirm tấn công (KHÔNG cần cường hóa — khác
        /// OnEmpoweredAllyAttack). Fire kể cả khi source đang ở deck/tay.
        /// ctx.triggerAmount = tổng số đồng minh đang tấn công lượt này.
        /// </summary>
        OnAllyAttackAnywhere,
    }

    /// <summary>
    /// Keyword cố định của lá bài — hiện icon ngay trên card.
    /// Thêm keyword mới ở đây, gán sprite tương ứng trong CardView Inspector.
    /// </summary>
    public enum KeywordType
    {
        // ── Existing (đã chỉnh về đúng LoR) ───────────────────────
        Barrier,        // Bảo Hộ:  vô hiệu HOÀN TOÀN đòn kế tiếp, rồi tiêu
        Lifesteal,      // Trù Phú:  gây damage = hồi máu Nexus
        QuickAttack,    // Săn Bắn:  đánh trước; giết được thì không bị phản
        Tough,          // Tri Thức: nhận ít hơn 1 damage từ mọi nguồn
        Elusive,        // Thần Bí:  chỉ bị block bởi Thần Bí
        Overwhelm,      // Hủy Diệt: damage dư tràn nexus
        Fearsome,       // Hư Vô:    chỉ bị block bởi unit ATK ≥ 3
        CantBlock,      // Vui Vẻ:   không thể block
        SpellShield,    // Hòa Hợp:  vô hiệu spell/skill kế tiếp
        Ephemeral,      // Cảm Tử:   chết sau khi ra đòn hoặc cuối vòng

        // ── New ───────────────────────────────────────────────────
        DoubleAttack,   // Song Kích:   ra đòn cả trước VÀ đồng thời với quân chặn (không buff)
        Challenger,     // Chỉ Định:    chọn quân địch nào buộc phải chặn nó (không debuff)
        Regeneration,   // Hồi Phục:    đầu mỗi round hồi toàn bộ HP
        Scout,          // Tiên Phong:  tấn công chỉ toàn quân Tiên Phong → sẵn sàng tấn công lần nữa (1 lần/vòng)
        Fury,           // Ác Nghiệp:   +1|+1 vĩnh viễn mỗi khi kill địch
        Lurk,           // Tiềm Phục:   tấn công khi 1 Lurk ở đầu deck → mọi quân Lurk +1|+0 (lặp lại)
        Deep,           // Vực Thẳm:    khi deck ≤ 15 lá → +3|+3 vĩnh viễn (1 lần)
        Augment,        // Cộng Hưởng:  khi bạn chơi lá được tạo ra (generated) → +1|+0 vĩnh viễn
        Formidable,     // Thần Thép:   ra đòn bằng chỉ số HP thay vì ATK
        Vulnerable,     // Phơi Bày:    kẻ địch có thể Chỉ Định buộc quân này phải chặn
        Fated,          // Thiên Cơ:    lần đầu được đồng minh nhắm mục tiêu mỗi round → +1|+1
        Daybreak,       // Bình Minh:   nếu là lá đầu tiên được chơi round này → +1|+1 ngay
        Nightfall,      // Hoàng Hôn:   nếu KHÔNG phải lá đầu tiên được chơi round này → +1|+1 ngay
        Plunder,        // Chiến Lợi:   khi tấn công, nếu nexus địch đã bị damage round này → hoàn 1 mana + +1|+0 tạm thời
        Flow,           // Dòng Thức:   mỗi spell được chơi → tất cả unit Flow +1|+0 tạm thời
        Hallowed,       // Linh Thiêng: khi chết → đến hết ván, quân đầu tấn công mỗi vòng +1|+0 (cộng dồn)

        // ── Final batch ───────────────────────────────────────────
        Fleeting,       // Thoáng Hiện: bị hủy cuối round nếu vẫn trên tay (không được chơi)
        Support,        // Cộng Lực:    khi tấn công → buff unit bên phải bench (supportAttackBuff...)
        LastBreath,     // Hơi Thở Cuối: icon keyword — logic xử lý qua SkillTrigger.OnDeath
        Countdown,      // Đếm Ngược:   tự hủy sau N round (N = CardData.countdownRounds)
        Impact,         // Xung Kích:   ra đòn lúc tấn công (kể cả bị chặn) → 1 dmg nexus (cộng dồn qua Song Kích)
        Spellcraft,     // Chú Thuật:   khi được chơi/triệu hồi → tạo 1 spell cụ thể lên tay (CardData.spellcraftSpell)
        UnitSkill,      // Kỹ Năng:     khi ra sân → tự stage spellcraftSpell vào spell zone (0 mana, Fast/Slow)

        // ── UI-only ───────────────────────────────────────────────
        Equipped,       // Trang Bị:    unit đang đeo equipment — GameController.ApplyEquipment tự grant.
                        //              KHÔNG gán thủ công vào CardData.keywords.

        // ── Bổ sung theo LoR gốc (đối chiếu wiki LoR) ─────────────
        // ⚠ 5 keyword dưới đây mới có ICON + MÔ TẢ POPUP — LOGIC COMBAT CHƯA NỐI
        //   (cần GameController.Combat.cs để implement). Gán vào card chỉ hiện icon.
        Frostbite,      // Đóng Băng:  ATK về 0 đến hết round (do spell/effect gán)
        Stun,           // Choáng:     bị loại khỏi combat — không attack/block round này
        Silence,        // Câm Lặng:   xóa mọi keyword + ability + effect trên unit
        Sharpsight,     // Tinh Mắt:   block được unit Thần Bí (Elusive)
        Immobile,       // Bất Động:   không thể attack lẫn block (vĩnh viễn — khác Stun)
    }

    public enum TargetType
    {
        None,
        EnemyUnit,
        AllyUnit,
        AnyUnit,
        /// <summary>
        /// Lá bài trên tay của owner (không phải board units).
        /// GameController phải highlight owner.hand thay vì bench/battlefield.
        /// Dùng cho SkillBuffTargetInHand và các skill tương tự.
        /// </summary>
        AllyInHand,
    }

    /// <summary>
    /// Vùng lá bài phải đang ở để CardAbility được kích hoạt — chuẩn hóa hand vs board.
    ///
    /// OnBoard = 0 là MẶC ĐỊNH: mọi ability hiện có (kể cả asset cũ chưa set field này)
    /// tự động thành "chỉ kích hoạt trên sân" — đúng chuẩn LoR.
    /// Card nào cần trigger từ tay/deck → chỉnh activeZone trong Inspector:
    ///   • Spell "giảm mana khi đồng minh chết" (nằm trên tay)  → InHand
    ///   • Passive kiểu Kayle (buff dù ở tay/deck)              → Anywhere
    ///
    /// LƯU Ý: chỉ áp dụng cho trigger qua ExecuteMatching (unit abilities).
    /// Spell đang cast (OnStack) thực thi qua đường riêng — không bị ảnh hưởng.
    /// </summary>
    public enum AbilityActiveZone
    {
        OnBoard,       // chỉ khi ở bench / battlefield (mặc định — chuẩn LoR)
        InHand,        // chỉ khi trên tay
        HandAndBoard,  // trên tay HOẶC trên sân
        Anywhere,      // mọi nơi: tay + sân + deck (passive kiểu Kayle)
    }

    // ─────────────────────────────────────────────────────────────────────────
    // LEVEL UP CONFIG — nguồn sự thật duy nhất cho điều kiện level-up
    // ─────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Loại điều kiện level-up. Quyết định GameController dùng logic nào để đánh giá.
    ///
    /// ── Nhóm Đếm Sự Kiện (TriggerCount) ──────────────────────────────────────
    ///   Mỗi lần trigger khớp → progress +1. Đạt threshold → level up.
    ///   Dùng field <see cref="LevelUpConfig.trigger"/> để chỉ định sự kiện cần đếm.
    ///
    /// ── Nhóm Ngưỡng Thụ Động (Passive) ───────────────────────────────────────
    ///   Không đếm — kiểm tra điều kiện liên tục sau mỗi thay đổi game state.
    ///   Không dùng field trigger. Level up ngay khi điều kiện thỏa (nếu đang ở sân).
    /// </summary>
    public enum LevelUpConditionType
    {
        // ── Đếm sự kiện ───────────────────────────────────────────────────────

        /// <summary>
        /// Đếm số lần trigger xảy ra trên hoặc liên quan đến lá này.
        ///   Self trigger  (OnAttack, OnKill, OnBlock, OnStrike, WhenDamaged…) → lá này là chủ thể.
        ///   Ally trigger  (OnAllyDeath, OnAllyPlayed, OnAllyDamaged…) → lá này "chứng kiến" sự kiện.
        /// progress tăng từng bước, đạt threshold → level up.
        /// </summary>
        TriggerCount,

        // ── Ngưỡng chỉ số bản thân (passive) ──────────────────────────────────

        /// <summary>
        /// currentAttack của chính lá này >= threshold.
        /// Kiểm tra sau mỗi lần stats thay đổi.
        /// Ví dụ: threshold = 10 → level up khi ATK đạt 10 (do buff bản thân hoặc đồng minh).
        /// </summary>
        SelfAttackReach,

        /// <summary>
        /// currentHealth của chính lá này >= threshold.
        /// Kiểm tra sau mỗi lần stats thay đổi.
        /// </summary>
        SelfHealthReach,

        // ── Ngưỡng tổng hợp đồng minh (passive) ──────────────────────────────

        /// <summary>
        /// Số lượng đồng minh còn sống đang ở sân (bench + battlefield) >= threshold.
        /// Không tính chính lá này. Kiểm tra sau mỗi khi unit ra sân hoặc chết.
        /// Ví dụ: threshold = 6 → level up khi có 6+ đồng minh đứng cùng.
        /// </summary>
        AllyCountOnBoard,

        /// <summary>
        /// Tổng currentAttack của TẤT CẢ đồng minh trên sân >= threshold.
        /// Bao gồm cả chính lá này (nếu đang ở sân).
        /// Kiểm tra sau mỗi lần stats thay đổi.
        /// </summary>
        AllySumAttack,

        /// <summary>
        /// Tổng currentHealth của TẤT CẢ đồng minh trên sân >= threshold.
        /// Bao gồm cả chính lá này (nếu đang ở sân).
        /// Kiểm tra sau mỗi lần stats thay đổi.
        /// </summary>
        AllySumHealth,

        /// <summary>
        /// Tổng damage unit này ĐÃ GÂY ra (combat + skill) >= threshold.
        /// Dùng cho champion đánh-để-lên-cấp (Mydei 10/35). Kiểm tra sau mỗi lần gây damage.
        /// </summary>
        DamageDealtByThis,

        /// <summary>
        /// Số đồng minh trên sân (bench+battlefield, KHÔNG tính chính lá này) có TAG = requiredTag
        /// đạt >= threshold. Passive. Dùng cho Elise (đếm "Nhện").
        /// </summary>
        AllyTagCountOnBoard,
    }

    /// <summary>
    /// Cấu hình level-up tập trung — nguồn sự thật duy nhất.
    /// GameController đọc trực tiếp, không cần CardAbility hay SkillLevelUp asset riêng.
    /// </summary>
    [System.Serializable]
    public class LevelUpConfig
    {
        [Tooltip("Loại điều kiện level-up.\n" +
                 "TriggerCount     = đếm N lần sự kiện (dùng field trigger bên dưới).\n" +
                 "SelfAttackReach  = ATK lá này đạt ngưỡng (passive).\n" +
                 "SelfHealthReach  = HP lá này đạt ngưỡng (passive).\n" +
                 "AllyCountOnBoard = số đồng minh trên sân đạt ngưỡng (passive).\n" +
                 "AllySumAttack    = tổng ATK đồng minh đạt ngưỡng (passive).\n" +
                 "AllySumHealth    = tổng HP đồng minh đạt ngưỡng (passive).")]
        public LevelUpConditionType conditionType = LevelUpConditionType.TriggerCount;

        [Tooltip("Sự kiện cần đếm — CHỈ dùng khi conditionType = TriggerCount.\n" +
                 "Self: OnAttack / OnKill / OnBlock / WhenDamaged / OnStrike / OnNexusStrike…\n" +
                 "Ally (chứng kiến): OnAllyDeath / OnAllyPlayed / OnAllyDamaged…")]
        public SkillTrigger trigger = SkillTrigger.OnAttack;

        [Tooltip("Ngưỡng cần đạt.\n" +
                 "TriggerCount     → số lần trigger.\n" +
                 "SelfAttackReach  → giá trị ATK tối thiểu.\n" +
                 "SelfHealthReach  → giá trị HP tối thiểu.\n" +
                 "AllyCountOnBoard → số đồng minh tối thiểu trên sân.\n" +
                 "AllySumAttack    → tổng ATK đồng minh tối thiểu.\n" +
                 "AllySumHealth    → tổng HP đồng minh tối thiểu.")]
        [Min(1)]
        public int threshold = 1;

        [Tooltip("AllyTagCountOnBoard: tag CẦN ĐẾM (VD 'Spider').\n" +
                 "Các loại khác: nếu đặt, làm ĐIỀU KIỆN CỔNG — chỉ tiến triển/đạt khi owner đang có\n" +
                 "ít nhất 1 đồng minh (kể cả chính nó) mang tag này (VD Shyvana + 'Dragon'). Rỗng = bỏ qua.")]
        public string requiredTag = "";
    }

    // ─────────────────────────────────────────────────────────────────────────
    // CARD CREATION TRIGGER — hệ thống GENERIC duy nhất, thay thế 3 hệ thống cũ
    // (SpellManaTrigger / DamageTakenTrigger / DamageDealtTrigger).
    // "Tạo bài" (Create) — KHÁC draw card: lá được tạo mới thẳng lên hand, không rút từ deck.
    // ─────────────────────────────────────────────────────────────────────────

    /// <summary>Loại chỉ số cộng dồn dùng để so threshold tạo bài.</summary>
    public enum CardCounterType
    {
        /// <summary>Tổng spell mana cả đội đã tiêu (PlayerModel.spellManaSpentTotal).
        /// LUÔN tính theo cả đội — trường <see cref="CardCreationTrigger.scope"/> bị bỏ qua.</summary>
        SpellManaSpent,

        /// <summary>Tổng damage đã GÂY. Self = riêng lá này (card.damageDealtTotal).
        /// Team = hiện CHƯA hỗ trợ (đang tính như Self) — xem ghi chú ở CheckCardCreationTriggers.</summary>
        DamageDealt,

        /// <summary>Tổng damage đã NHẬN. Self = riêng lá này (card.damageTakenTotal).
        /// Team = hiện CHƯA hỗ trợ (đang tính như Self) — xem ghi chú ở CheckCardCreationTriggers.</summary>
        DamageTaken,

        /// <summary>Tổng số lần confirm tấn công. Self = card.attacksMadeTotal (riêng unit này).
        /// Team = owner.attacksMadeTotal (cộng dồn CẢ ĐỘI) — hỗ trợ đầy đủ cả 2 scope.</summary>
        AttacksMade,
    }

    /// <summary>Phạm vi đếm — Self (riêng lá này) hay Team (cộng dồn cả đội).</summary>
    public enum CounterScope { Self, Team }

    /// <summary>
    /// Trigger tạo bài GENERIC — gán vào bất kỳ CardData nào (unit/spell), khi counter cộng dồn
    /// đạt threshold → tạo <see cref="cardToCreate"/> thẳng lên hand của owner (KHÁC draw card).
    ///
    /// Thay thế hoàn toàn SpellManaTrigger/DamageTakenTrigger/DamageDealtTrigger cũ. Migrate:
    ///   SpellManaTrigger { threshold, once=true }
    ///     → counter=SpellManaSpent, threshold, repeatEvery=false
    ///   DamageTakenTrigger { threshold, once=true }
    ///     → counter=DamageTaken, scope=Self, threshold, repeatEvery=false
    ///   DamageDealtTrigger { firstAt, threshold, repeatEvery } (Mydei mỗi 15 dmg)
    ///     → counter=DamageDealt, scope=Self, firstAt, threshold, repeatEvery (giữ nguyên)
    ///   (once=false cũ — footgun fire lại MỌI lần check — KHÔNG còn hỗ trợ trực tiếp;
    ///    dùng repeatEvery=true với threshold tương ứng thay thế, an toàn & rõ ràng hơn.)
    /// </summary>
    [System.Serializable]
    public class CardCreationTrigger
    {
        [Tooltip("Chỉ số cộng dồn dùng để so threshold.")]
        public CardCounterType counter = CardCounterType.AttacksMade;

        [Tooltip("Self = đếm riêng lá này. Team = đếm cộng dồn CẢ ĐỘI.\n" +
                 "Bỏ qua nếu counter = SpellManaSpent (vốn luôn tính cả đội).\n" +
                 "Team hiện chỉ hỗ trợ đầy đủ cho AttacksMade.")]
        public CounterScope scope = CounterScope.Self;

        [Tooltip("Mốc đạt lần ĐẦU để tạo bài. 0 = dùng threshold làm mốc đầu.")]
        [Min(0)]
        public int firstAt = 0;

        [Tooltip("Ngưỡng cộng dồn cần đạt (và bước lặp nếu repeatEvery).")]
        [Min(1)]
        public int threshold = 1;

        [Tooltip("Bật = lặp lại mỗi 'threshold' đơn vị kể từ firstAt (VD finisher Mydei: mỗi 15 damage).\n" +
                 "Tắt = chỉ tạo bài 1 LẦN DUY NHẤT khi vượt threshold (mặc định, thay 'once=true' cũ).")]
        public bool repeatEvery = false;

        [Tooltip("Lá bài được tạo lên hand khi thỏa. Thường isGenerated = true (không cho vào deck).")]
        public CardData cardToCreate;
    }

    // ─────────────────────────────────────────────────────────────────────────
    // STAT CONDITION — dùng chung cho ConditionalKeyword và ConditionalAbility
    // ─────────────────────────────────────────────────────────────────────────

    public enum ConditionalMode { All, Any }

    /// <summary>
    /// Điều kiện chỉ số: thỏa khi ATK và/hoặc HP đạt ngưỡng.
    /// Dùng chung bởi ConditionalKeyword và ConditionalAbility.
    /// requiredAttack / requiredHealth = 0 → bỏ qua điều kiện đó.
    /// mode = All → phải thỏa cả hai; Any → thỏa một trong hai là đủ.
    /// </summary>
    [System.Serializable]
    public struct StatCondition
    {
        [Tooltip("Thỏa khi currentAttack >= giá trị này. 0 = bỏ qua.")]
        [Min(0)] public int requiredAttack;

        [Tooltip("Thỏa khi currentHealth >= giá trị này. 0 = bỏ qua.")]
        [Min(0)] public int requiredHealth;

        [Tooltip("All = cả ATK lẫn HP phải thỏa (nếu > 0).\nAny = thỏa 1 trong 2 là đủ.")]
        public ConditionalMode mode;
    }

    // ─────────────────────────────────────────────────────────────────────────
    // CONDITIONAL KEYWORD
    // ─────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Tự ban keyword khi StatCondition thỏa. Icon tự hiện/ẩn khi stats thay đổi.
    /// Lưu ý: Barrier/SpellShield/Lifesteal — combat dùng boolean riêng,
    /// cần gọi GrantKeyword() trong skill nếu muốn combat cũng nhận.
    /// </summary>
    [System.Serializable]
    public class ConditionalKeyword
    {
        [Tooltip("Keyword được ban khi condition thỏa.")]
        public KeywordType keyword;

        public StatCondition condition;
    }

    // ─────────────────────────────────────────────────────────────────────────
    // CONDITIONAL ABILITY
    // ─────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Gán thêm ability (trigger + effect) khi StatCondition thỏa.
    /// GameController kiểm tra list này cùng lúc với abilities thường — chỉ fire khi condition met.
    /// Dùng y hệt CardAbility: gán trigger, effect (SkillData), override p1/p2/p3 nếu cần.
    /// </summary>
    [System.Serializable]
    public class ConditionalAbility
    {
        public StatCondition condition;

        [Tooltip("Ability được kích hoạt khi condition thỏa — cấu hình y hệt abilities thường.")]
        public CardAbility ability;
    }

    /// <summary>
    /// Một "khả năng" của unit: ghép điều kiện (condition) + kỹ năng (effect) trực tiếp
    /// trong Inspector — không cần tạo class C# mới cho từng combo.
    /// </summary>
    [System.Serializable]
    public class CardAbility
    {
        [Tooltip("Điều kiện kích hoạt — KHI NÀO skill chạy.")]
        public SkillTrigger condition;

        [Tooltip("Kỹ năng được kích hoạt — LÀM GÌ khi điều kiện thỏa.")]
        public SkillData effect;

        // ── Override params — ghi đè chỉ số bên trong SkillData asset ──
        // Để -1 = dùng giá trị mặc định của skill asset.
        // Ý nghĩa theo loại skill:
        //   DamageTarget / DamageAllEnemies / DamageNEnemies → p1=damage, p2=count(N)
        //   BuffSelf / BuffTarget / BuffAllOnField / BuffNAllies → p1=attackBuff, p2=healthBuff, p3=count(N)
        //   BuffAllyAttack → p1=attackBuff
        //   DrawCard → p1=count
        //   HealAllAllies → p1=healAmount
        //   AttackWithAlly → p1=bonusDamage
        //   LevelUpOnAttack → p1=requiredAttacks
        //   BuffCardsInHand / BuffTargetInHand → p1=attackBuff, p2=healthBuff, p3=count(N)
        [Tooltip("Override p1 (damage / attackBuff / count / healAmount / bonusDamage / requiredAttacks).\n-1 = dùng default của skill asset.")]
        public int p1 = -1;
        [Tooltip("Override p2 (healthBuff / count tùy skill).\n-1 = dùng default.")]
        public int p2 = -1;
        [Tooltip("Override p3 (count — chỉ dùng cho BuffNAllies / BuffCardsInHand).\n-1 = dùng default.")]
        public int p3 = -1;

        [Tooltip("Buff chỉ kéo dài trong round này — tự reset đầu round tiếp theo.\n" +
                 "Tắt = buff vĩnh viễn (mặc định).")]
        public bool temporaryBuff = false;

        [Tooltip("Cấp 1 keyword khi ability chạy (dùng với Skill Buff Self/Target). Bật rồi chọn keyword.")]
        public bool grantKeyword = false;
        [Tooltip("Keyword được cấp khi grantKeyword bật.")]
        public KeywordType grantKeywordType = KeywordType.QuickAttack;

        [Tooltip("Chỉ kích hoạt 1 lần mỗi round — các lần trigger tiếp theo trong cùng round sẽ bị bỏ qua.\n" +
                 "Dùng khi muốn buff theo điều kiện nhưng không stack nếu điều kiện xảy ra nhiều lần.")]
        public bool oncePerRound = false;

        [Tooltip("Vùng lá bài phải đang ở để ability kích hoạt:\n" +
                 "OnBoard = phải trên sân (mặc định — chuẩn LoR).\n" +
                 "InHand = chỉ khi trên tay (VD: giảm mana khi đồng minh chết).\n" +
                 "HandAndBoard = tay hoặc sân.\n" +
                 "Anywhere = mọi nơi kể cả deck (passive kiểu Kayle).")]
        public AbilityActiveZone activeZone = AbilityActiveZone.OnBoard;

        [Tooltip("Target cần chọn khi WhenPlayed — None = không cần chọn target.\n " +
            "Dùng để unit ability yêu cầu player chỉ định 1 lá cụ thể trước khi ra sân.")]
        public TargetType targetType = TargetType.None;

        [TextArea(1, 2)]
        [Tooltip("Mô tả hiển thị trên card (không ảnh hưởng logic).")]
        public string description;
    }

    [CreateAssetMenu(menuName = "LoRClone/Card Data", fileName = "New Card")]
    public class CardData : ScriptableObject
    {
        [Header("Basic Info")]
        public string cardName = "Unnamed";
        public CardType cardType = CardType.Unit;

        [Tooltip("Bật = lá này LUÔN có sẵn trong tay mở đầu (như champion), hiện ở mulligan.\n" +
                 "Vẫn có thể mulligan đổi đi nếu muốn — tùy chiến thuật.\n" +
                 "Nhiều lá cùng bật → deck ưu tiên đưa hết lên tay trước (theo số lá tay mở đầu).")]
        public bool guaranteedInOpeningHand = false;

        [Header("Mô tả (hiện khi inspect)")]
        [TextArea(2, 5)]
        public string description = "";

        [Header("Stats — chỉ dùng khi là Unit")]
        public int baseHealth = 1;
        public int baseAttack = 0;

        [Header("Spell — chỉ dùng khi là Spell")]
        public SpellSpeed spellSpeed = SpellSpeed.Slow;

        [Header("Targeting — chỉ dùng khi là Spell")]
        [Tooltip("Bật = spell yêu cầu player chọn target trước khi cast.")]
        public bool requiresTarget = false;
        [Tooltip("Nhóm lá được highlight làm target hợp lệ.")]
        public TargetType targetType = TargetType.EnemyUnit;
        [Tooltip("Số target player phải chọn lần lượt.")]
        [Min(1)]
        public int targetCount = 1;

        [Tooltip("TargetType riêng cho từng bước chọn target.\n" +
                 "Ví dụ: [AllyUnit, EnemyUnit] → bước 1 highlight đồng minh, bước 2 highlight kẻ thù.\n" +
                 "Để rỗng = tất cả bước dùng chung targetType bên trên (behavior cũ).")]
        public List<TargetType> targetTypes = new List<TargetType>();

        /// <summary>Trả về TargetType cho bước chọn thứ step (0-indexed).
        /// Dùng targetTypes[step] nếu có, fallback về targetType nếu không.</summary>
        public TargetType GetTargetTypeForStep(int step) =>
            targetTypes != null && step < targetTypes.Count
                ? targetTypes[step]
                : targetType;

        [Header("Cost")]
        public int manaCost = 0;

        // ─────────────────────────────────────────────────────────────
        // ARTWORK
        // ─────────────────────────────────────────────────────────────
        [Header("Visual — dùng Texture2D (khuyến dùng ảnh landscape, vd: 1920x1280)")]
        [Tooltip("Texture gốc. Nhiều CardData có thể dùng chung 1 texture.")]
        public Texture2D artwork;

        [Header("Artwork Focal Point — tọa độ nhân vật trong ảnh (0–1)")]
        [Range(0f, 1f)]
        public float artworkFocalX = 0.5f;
        [Range(0f, 1f)]
        public float artworkFocalY = 0.5f;
        [Range(0.1f, 5f)]
        public float artworkZoom = 1f;

        [Header("Keywords — icon hiện trực tiếp trên card")]
        public List<KeywordType> keywords = new List<KeywordType>();

        [Header("Support — chỉ dùng khi có keyword Support")]
        [Tooltip("ATK buff cho unit bên phải khi tấn công. Kato = 3, đa số = 0.")]
        public int supportAttackBuff = 0;
        [Tooltip("HP buff cho unit bên phải khi tấn công. Thường = 0.")]
        public int supportHealthBuff = 0;
        [Tooltip("Keyword cấp cho unit bên phải khi tấn công. Shen = Barrier, Kato = Overwhelm.")]
        public List<KeywordType> supportGrantKeywords = new List<KeywordType>();
        [Tooltip("Buff Support chỉ trong round này (bật — giống LoR) hay vĩnh viễn (tắt).")]
        public bool supportTemporary = true;

        [Header("Trang bị tự động khi triệu hồi (kiểu Aatrox) — chỉ dùng khi là Unit")]
        [Tooltip("Gán 1 CardData Equipment → unit tự đeo ngay khi được triệu hồi\n" +
                 "(áp buff + keyword + skill của trang bị lên unit).")]
        public CardData autoEquipOnSummon;

        [Header("Bất Diệt khi tấn công (Mydei) — chỉ dùng khi là Unit")]
        [Tooltip("Bật = khi unit ĐANG TẤN CÔNG nhận đòn COMBAT chí tử từ unit → sống ở 1 máu + nhận 1 Khiên (Barrier).\n" +
                 "CHỈ áp với sát thương combat từ unit — spell/skill đủ dame vẫn giết bình thường.\n" +
                 "Khi phòng thủ/chặn cũng chết bình thường (điểm yếu 'hở sống lưng').")]
        public bool surviveLethalWhileAttacking = false;

        [Header("Nội tại khi Thách Đấu (Challenger) — buff bản thân khi kéo địch chặn")]
        [Tooltip("ATK buff cho CHÍNH unit này khi nó thách đấu (kéo địch vào chặn). Mydei = 2. Chỉ trong vòng đó.")]
        public int challengeSelfAtkBuff = 0;
        [Tooltip("HP buff cho chính unit này khi thách đấu. Mydei = 1. Chỉ trong vòng đó.")]
        public int challengeSelfHpBuff = 0;

        [Header("Abilities — Điều kiện + Kỹ năng (khuyến dùng)")]
        public List<CardAbility> abilities = new List<CardAbility>();

        [Header("Skills (legacy — trigger baked vào SkillData)")]
        public List<SkillData> skills = new List<SkillData>();

        [Header("Level Up")]
        public bool canLevelUp = false;
        public CardData levelUpForm;

        [Tooltip("Config level-up — trigger và threshold tập trung 1 chỗ duy nhất.\n" +
                 "GameController đọc trực tiếp, không cần CardAbility hay SkillLevelUp asset riêng.\n" +
                 "Ví dụ: trigger = OnAttack, threshold = 5 → level up sau 5 lần tấn công.")]
        public LevelUpConfig levelUpConfig = new LevelUpConfig();

        [Tooltip("Văn bản điều kiện hiện khi inspect card (ví dụ: 'Đã tấn công 5 lần').\n" +
                 "Chỉ dùng để hiển thị — không ảnh hưởng logic.")]
        public string levelUpCondition = "";

        [Tooltip("File MP4 phát khi champion level up. Để trống → fallback hiện artwork tĩnh.")]
        public VideoClip levelUpCinematic;

        [Tooltip("Các voice line khi level up. Mỗi lần sẽ chọn ngẫu nhiên 1 clip.")]
        public AudioClip[] levelUpVoiceLines;

        // ─────────────────────────────────────────────────────────────
        // GENERATED CARD
        // ─────────────────────────────────────────────────────────────
        [Header("Generated Card — không thể cho vào deck")]
        [Tooltip("Bật = lá này chỉ được tạo ra bằng hiệu ứng, KHÔNG thể có trong deck.\n" +
                 "Deck builder nên lọc bỏ các lá có isGenerated = true.")]
        public bool isGenerated = false;

        // ─────────────────────────────────────────────────────────────
        // CARD CREATION TRIGGERS — tạo bài mới lên hand (KHÁC draw card)
        // ─────────────────────────────────────────────────────────────
        [Header("Card Creation Triggers — tích lũy chỉ số → tạo bài lên hand")]
        [Tooltip("Danh sách trigger tạo bài theo counter cộng dồn (mana đã tiêu / damage gây-nhận /\n" +
                 "số lượt tấn công...). Lá này phải đang ở hand/bench/battlefield để được kiểm tra\n" +
                 "(riêng SpellManaSpent luôn tính theo cả đội).")]
        public List<CardCreationTrigger> cardCreationTriggers = new List<CardCreationTrigger>();

        // ─────────────────────────────────────────────────────────────
        // CONDITIONAL KEYWORDS — ban keyword khi đủ ATK/HP
        // ─────────────────────────────────────────────────────────────
        [Header("Conditional Keywords — ban keyword khi đủ ATK/HP")]
        [Tooltip("Keyword tự hiện/ẩn icon theo stats. Barrier/SpellShield/Lifesteal cần logic thêm nếu muốn combat xử lý.")]
        public List<ConditionalKeyword> conditionalKeywords = new List<ConditionalKeyword>();

        // ─────────────────────────────────────────────────────────────
        // CONDITIONAL ABILITIES — gán thêm ability khi đủ ATK/HP
        // ─────────────────────────────────────────────────────────────
        [Header("Conditional Abilities — gán ability khi đủ ATK/HP")]
        [Tooltip("Ability chỉ fire khi StatCondition thỏa tại thời điểm trigger.\n" +
                 "Cấu hình giống abilities thường (trigger + effect + p1/p2/p3).")]
        public List<ConditionalAbility> conditionalAbilities = new List<ConditionalAbility>();

        // ─────────────────────────────────────────────────────────────
        // COUNTDOWN — chỉ dùng khi card có keyword Countdown
        // ─────────────────────────────────────────────────────────────
        [Header("Countdown — chỉ dùng khi có keyword Countdown")]
        [Tooltip("Số round cho đến khi unit tự hủy. Round đầu tiên nó được chơi không tính (chỉ countdown từ round tiếp theo).")]
        [Min(1)]
        public int countdownRounds = 3;

        // ─────────────────────────────────────────────────────────────
        // SPELLCRAFT — chỉ dùng khi card có keyword Spellcraft
        // ─────────────────────────────────────────────────────────────
        [Header("Spellcraft — chỉ dùng khi có keyword Spellcraft")]
        [Tooltip("Spell được tạo lên tay khi card này được chơi/triệu hồi. Phải là CardData có cardType = Spell.")]
        public CardData spellcraftSpell;

        [Tooltip("Chỉ dùng với keyword UnitSkill.\n" +
                 "Bật = stage spellcraftSpell lên stack khi unit TẤN CÔNG/CHẶN (kiểu Renekton), thay vì khi ra sân.")]
        public bool unitSkillOnAttack = false;

        [Tooltip("Chỉ dùng với UnitSkill + unitSkillOnAttack.\n" +
                 "Bật = CHỈ kích khi TẤN CÔNG (không kích khi chặn).")]
        public bool unitSkillAttackOnly = false;

        // ─────────────────────────────────────────────────────────────
        // EQUIPMENT — chỉ dùng khi cardType = Equipment
        // ─────────────────────────────────────────────────────────────
        [Header("Equipment — chỉ dùng khi cardType = Equipment")]
        [Tooltip("ATK buff cấp cho unit được trang bị. 0 = không buff ATK.")]
        public int equipAttackBuff = 0;

        [Tooltip("HP buff cấp cho unit được trang bị. 0 = không buff HP.")]
        public int equipHealthBuff = 0;

        [Tooltip("Keyword cấp vĩnh viễn cho unit được trang bị.")]
        public List<KeywordType> equipGrantKeywords = new List<KeywordType>();

        [Tooltip("Unit Darkin triệu hồi khi player chọn mode Triệu Hồi Darkin.")]
        public CardData darkinUnit;

        // ─────────────────────────────────────────────────────────────
        // AUDIO
        // ─────────────────────────────────────────────────────────────
        [Header("Audio — để trống = không có sound")]
        [Tooltip("Bộ SFX riêng của lá bài này.\n" +
                 "Để trống nếu muốn dùng sound mặc định do AudioManager quản lý.\n" +
                 "Nhiều card có thể share cùng 1 CardAudioData asset.")]
        public CardAudioData audioData;

        // ─────────────────────────────────────────────────────────────
        // SUMMON — unit lá này tự triệu hồi (nguồn "chính chủ")
        // ─────────────────────────────────────────────────────────────
        [Header("Summon — unit lá này triệu hồi")]
        [Tooltip("Danh sách unit mà LÁ NÀY triệu hồi. Dùng bởi SkillSummonUnit / SkillSummonCopy\n" +
                 "khi bật 'useSourceCardData' → 1 asset skill dùng chung cho mọi card, mỗi card\n" +
                 "khai báo unit riêng tại đây thay vì nhồi vào SkillData. Phải là cardType = Unit.\n" +
                 "SkillSummonUnit dùng cả list; SkillSummonCopy dùng phần tử [0].")]
        public List<CardData> summonUnits = new List<CardData>();

        [Header("Tags — nhãn nhận diện (Spider/Dragon/Mist...) cho level-up, buff, đếm theo loại")]
        [Tooltip("Danh sách tag của lá. Dùng LoRClone.Data.Tags.* làm hằng số để tránh gõ sai.\n" +
                 "VD: Spider (Elise lên cấp), Dragon (Shyvana), Mist (Viego aura).")]
        public List<string> tags = new List<string>();

        /// <summary>True nếu CardData có tag này (không phân biệt hoa/thường).</summary>
        public bool HasTag(string tag)
        {
            if (string.IsNullOrEmpty(tag) || tags == null) return false;
            for (int i = 0; i < tags.Count; i++)
                if (string.Equals(tags[i], tag, System.StringComparison.OrdinalIgnoreCase)) return true;
            return false;
        }

        [Header("Lá bài liên quan — hiện khi inspect")]
        [Tooltip("Tiêu đề dải lá liên quan. Để trống thì dải vẫn hiện nhưng không có label.\n" +
                 "Ví dụ: 'LÁ BÀI LIÊN QUAN CỦA AATROX'")]
        public string relatedCardsLabel = "";

        [Tooltip("Danh sách lá bài liên quan. Hiện thành thumbnail ngang phía dưới khi inspect.\n" +
                 "Click thumbnail → xem thông tin chi tiết lá đó.")]
        public List<CardData> relatedCards = new List<CardData>();
    }
}