using UnityEngine;

namespace LoRClone.Data
{
    /// <summary>
    /// Base class cho mọi skill. Tạo skill mới = tạo class kế thừa SkillData.
    /// Không bao giờ đặt logic game vào đây — chỉ đặt data và Execute().
    ///
    /// p1/p2/p3: override từ CardAbility. Dùng -1 = không override (dùng default của asset).
    /// </summary>
    public abstract class SkillData : ScriptableObject
    {
        [Header("Trigger")]
        public SkillTrigger trigger;

        [TextArea]
        public string description;

        /// <summary>
        /// Được gọi bởi GameController khi trigger khớp.
        /// p1/p2/p3: override params từ CardAbility (-1 = dùng default của asset).
        /// </summary>
        public abstract void Execute(Model.CardModel source, Model.GameContext ctx,
                                     int p1 = -1, int p2 = -1, int p3 = -1);

        /// <summary>
        /// TargetType mà skill này cần khi dùng cho unit WhenPlayed.
        /// Override trong subclass nếu skill cần player chọn target.
        /// GameView đọc property này để tự động bật targeting flow — không cần set thủ công trong Inspector.
        /// </summary>
        public virtual TargetType RequiredTargetType => TargetType.None;
    }
}