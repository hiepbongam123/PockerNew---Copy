using UnityEngine;
using LoRClone.Data;
using LoRClone.Model;
using LoRClone.Controller;

namespace LoRClone.Skills
{
    /// <summary>
    /// [LEGACY FALLBACK] SkillLevelUp — chỉ dùng khi điều kiện level-up không thể diễn đạt
    /// bằng "đếm N lần trigger X" đơn giản, hoặc cần kết hợp với ConditionalAbility.
    ///
    /// ══ CÁCH THÔNG THƯỜNG (khuyến dùng) ══
    /// Điền trực tiếp vào CardData.levelUpConfig:
    ///   trigger   = OnAttack   (hoặc OnKill, OnBlock, OnAllyPlayed, ...)
    ///   threshold = 5
    /// GameController tự đếm và level up — không cần tạo asset này.
    ///
    /// ══ KHI NÀO DÙNG ASSET NÀY ══
    /// Chỉ khi cần logic đặc biệt không có trong LevelUpConfig, ví dụ:
    ///   • Level up dựa trên ConditionalAbility (chỉ đếm khi ATK ≥ X)
    ///   • Level up với nhiều trigger khác nhau (vừa OnAttack vừa OnKill)
    ///   • Tích hợp với skill chain phức tạp
    ///
    /// Lưu ý: nếu vừa có levelUpConfig vừa có CardAbility SkillLevelUp → progress bị cộng đôi.
    /// Chỉ dùng một trong hai.
    /// </summary>
    [CreateAssetMenu(menuName = "LoRClone/Skills/Level Up", fileName = "Skill_LevelUp")]
    public class SkillLevelUp : SkillData
    {
        [Tooltip("Số lần trigger cần thiết để level up.\n" +
                 "CardAbility.p1 ghi đè giá trị này nếu p1 > 0.")]
        [Min(1)]
        public int requiredProgress = 1;

        public override void Execute(CardModel source, GameContext ctx,
                                     int p1 = -1, int p2 = -1, int p3 = -1)
            => ExecuteInternal(source, ctx, p1 > 0 ? p1 : requiredProgress);

        void ExecuteInternal(CardModel source, GameContext ctx, int threshold)
        {
            if (source == null || !source.data.canLevelUp || source.hasLeveledUp) return;

            source.AddLevelUpProgress(1);
            Debug.Log($"[LevelUp] {source.data.cardName} tiến triển: {source.levelUpProgress}/{threshold}");

            if (source.levelUpProgress >= threshold)
            {
                // LoR rule: transformation chỉ xảy ra khi champion đang ở trên sân.
                // Nếu đang ở tay hoặc deck → đánh dấu readyToLevelUp,
                // GameController sẽ TriggerLevelUp() ngay khi champion được đặt lên bench.
                bool isOnBoard = source.location == CardLocation.OnBench
                              || source.location == CardLocation.OnBattlefield;

                if (isOnBoard)
                {
                    var gc = ctx?.controller as GameController;
                    if (gc != null)
                        gc.TriggerLevelUp(source);
                    else
                        source.LevelUp();   // fallback nếu không có controller ref
                }
                else
                {
                    source.MarkReadyToLevelUp();
                    Debug.Log($"[LevelUp] {source.data.cardName} đủ điều kiện nhưng chưa ở sân — sẽ level up khi triệu hồi.");
                }
            }
        }
    }
}