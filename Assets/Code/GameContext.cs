using System.Collections.Generic;
using LoRClone.Controller;
using LoRClone.Data;

namespace LoRClone.Model
{
    /// <summary>
    /// Snapshot của game state tại thời điểm một skill/spell được resolve.
    /// Truyền vào SkillData.Execute() thay vì dùng Singleton.
    /// </summary>
    public class GameContext
    {
        public readonly GameModel game;
        public readonly PlayerModel owner;      // người chơi sở hữu skill
        public readonly PlayerModel opponent;   // đối thủ

        /// <summary>
        /// Reference đến GameController — dùng bởi skill như SkillSummonUnit
        /// để gọi các method cấp game (SummonUnit, v.v.).
        /// Null nếu context tạo từ bên ngoài GameController.
        /// </summary>
        public readonly GameController controller;

        /// <summary>
        /// Danh sách target theo thứ tự player đã chọn.
        /// Skill lặp qua list này để xử lý tuần tự (không đồng thời).
        /// </summary>
        public List<CardModel> targetCards = new List<CardModel>();

        /// <summary>
        /// Shortcut backward-compat: trả về targetCards[0], hoặc null nếu list rỗng.
        /// Các skill single-target cũ dùng ctx.targetCard vẫn chạy đúng.
        /// </summary>
        public CardModel targetCard
        {
            get => targetCards.Count > 0 ? targetCards[0] : null;
            set
            {
                targetCards.Clear();
                if (value != null) targetCards.Add(value);
            }
        }

        /// <summary>
        /// Card gây ra trigger — dùng cho ally triggers.
        /// OnAllyDeath       → unit vừa chết
        /// OnAllyPlayed      → unit vừa được triệu hồi
        /// OnRelatedAllyPlayed → unit liên quan vừa được triệu hồi
        /// OnAllyDamaged     → unit vừa nhận damage
        /// OnAllyDealsDamage → unit vừa gây damage
        /// </summary>
        public CardModel triggerCard;

        /// <summary>
        /// Lượng damage liên quan đến trigger (OnAllyDamaged / OnAllyDealsDamage).
        /// 0 cho các trigger không liên quan đến số lượng.
        /// </summary>
        public int triggerAmount;

        /// <summary>
        /// Khi true: buff chỉ kéo dài trong round này, tự reset đầu round tiếp theo.
        /// Được GameController gán từ CardAbility.temporaryBuff trước mỗi Execute().
        /// Skill đọc giá trị này rồi truyền vào CardModel.BuffAttack/BuffHealth.
        /// </summary>
        public bool temporaryBuff;

        /// <summary>Ability yêu cầu cấp 1 keyword (gán từ CardAbility trước Execute). Skill Buff đọc.</summary>
        public bool grantKeyword;
        public KeywordType grantKeywordType;

        public GameContext(GameModel game, PlayerModel owner, GameController controller = null)
        {
            this.game = game;
            this.owner = owner;
            this.opponent = game.GetOpponent(owner);
            this.controller = controller;
        }
    }
}