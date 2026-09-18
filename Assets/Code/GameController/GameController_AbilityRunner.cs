using LoRClone.Data;
using LoRClone.Model;

namespace LoRClone.Controller
{
    /// <summary>
    /// GameController — CHỖ DUY NHẤT chứa logic "chạy skills+abilities khớp 1 trigger trên 1 card".
    /// Partial mới, không sửa file khác.
    ///
    /// LÝ DO TỒN TẠI: đoạn "loop qua card.data.skills (legacy) + loop qua card.data.abilities,
    /// set ctx.temporaryBuff/grantKeyword, gọi Execute()" đã bị chép tay 3 lần trong project
    /// (ExecuteMatching cho trigger trên sân, FireEquipOnPlayedEffect cho lúc trang bị,
    /// DrawAfterMulliganCards cho lúc rút bài sau mulligan) → mỗi lần thêm 1 trigger/context mới
    /// lại phải nhớ chép lại y hệt, dễ quên field, dễ lệch hành vi.
    ///
    /// TỪ NAY: mọi chỗ cần "chạy trigger X trên card Y" (dù card đang ở sân, trong deck, vừa
    /// trang bị, hay bất kỳ context nào KHÔNG cần zone-gate/oncePerRound/conditionalAbilities
    /// phức tạp như ExecuteMatching) → gọi RunAbilities() ở đây.
    ///
    /// ExecuteMatching (GameController_Triggers.cs) — dùng cho trigger TRÊN SÂN — CỐ TÌNH giữ
    /// nguyên riêng, không gộp vào đây, vì nó có thêm zone-gate/oncePerRound/conditionalAbilities/
    /// equipment-cascade mà các context "đặc biệt" (mulligan, on-equip...) không cần tới và gộp
    /// vào sẽ rủi ro hơn là lợi.
    /// </summary>
    public partial class GameController
    {
        /// <summary>
        /// Chạy MỌI legacy skill (skill.trigger == trigger) + MỌI ability (ability.condition ==
        /// trigger) trên 'card', dùng chung 'ctx'. KHÔNG zone-gate, KHÔNG oncePerRound, KHÔNG
        /// conditionalAbilities — dùng cho context "đặc biệt" nằm ngoài vòng đời sân đấu bình
        /// thường (mulligan, on-equip, hoặc bất kỳ trigger mới bạn nghĩ ra sau này).
        /// Trả về true nếu có ít nhất 1 skill/ability đã chạy (tiện log/kiểm tra).
        /// </summary>
        bool RunAbilities(CardModel card, SkillTrigger trigger, GameContext ctx)
        {
            if (card?.data == null || ctx == null) return false;
            bool ranAny = false;

            if (card.data.skills != null)
                foreach (var skill in card.data.skills)
                {
                    if (skill == null || skill.trigger != trigger) continue;
                    skill.Execute(card, ctx);
                    ranAny = true;
                }

            if (card.data.abilities != null)
                foreach (var ability in card.data.abilities)
                {
                    if (ability?.effect == null || ability.condition != trigger) continue;
                    ctx.temporaryBuff = ability.temporaryBuff;
                    ctx.grantKeyword = ability.grantKeyword;
                    ctx.grantKeywordType = ability.grantKeywordType;
                    ability.effect.Execute(card, ctx, ability.p1, ability.p2, ability.p3);
                    ctx.temporaryBuff = false;
                    ctx.grantKeyword = false;
                    ranAny = true;
                }

            return ranAny;
        }

        /// <summary>Tiện dùng khi 'source' chạy skill (VD unit đeo trang bị) khác 'card' chứa data
        /// (VD chính lá trang bị) — như FireEquipOnPlayedEffect đang cần.</summary>
        bool RunAbilitiesFromData(CardData data, CardModel source, SkillTrigger trigger, GameContext ctx)
        {
            if (data == null || source == null || ctx == null) return false;
            bool ranAny = false;

            if (data.skills != null)
                foreach (var skill in data.skills)
                {
                    if (skill == null || skill.trigger != trigger) continue;
                    skill.Execute(source, ctx);
                    ranAny = true;
                }

            if (data.abilities != null)
                foreach (var ability in data.abilities)
                {
                    if (ability?.effect == null || ability.condition != trigger) continue;
                    ctx.temporaryBuff = ability.temporaryBuff;
                    ctx.grantKeyword = ability.grantKeyword;
                    ctx.grantKeywordType = ability.grantKeywordType;
                    ability.effect.Execute(source, ctx, ability.p1, ability.p2, ability.p3);
                    ctx.temporaryBuff = false;
                    ctx.grantKeyword = false;
                    ranAny = true;
                }

            return ranAny;
        }
    }
}
