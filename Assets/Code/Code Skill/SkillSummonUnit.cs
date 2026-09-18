using System.Collections.Generic;
using UnityEngine;
using LoRClone.Data;
using LoRClone.Model;

namespace LoRClone.Skills
{
    /// <summary>
    /// Skill: triệu hồi một unit chỉ định lên bench hoặc battlefield của chủ sở hữu skill.
    ///
    /// Cách dùng:
    ///   1. Tạo asset: Create → LoRClone/Skills/Summon Unit
    ///   2. Gán unitToSummon = CardData của unit muốn triệu hồi
    ///   3. Tick summonToBattlefield nếu muốn unit ra thẳng vị trí tấn công
    ///   4. Chọn trigger (VD: WhenPlayed để summon khi card ra sân)
    ///   5. Gán skill này vào CardData.abilities của card gốc
    ///
    /// p1/p2/p3 override:
    ///   p1 = số lượng unit triệu hồi (override quantity của asset, -1 = dùng default)
    ///   p2/p3 = chưa dùng, để -1
    ///
    /// Ví dụ:
    ///   asset quantity=1, p1=-1 → triệu hồi 1 con
    ///   asset quantity=1, p1=2  → triệu hồi 2 con (card này summon nhiều hơn default)
    ///
    /// Lưu ý:
    ///   - summonToBattlefield = false → bench (mặc định, unit phải declare attacker sau)
    ///   - summonToBattlefield = true  → battlefield (unit tham chiến ngay vòng này)
    ///   - Đệ quy summon bị chặn bởi _isSummoning guard trong GameController.
    ///     Mỗi lần SummonUnit trong vòng lặp đều set/clear guard riêng → summon nhiều con
    ///     cùng loại vẫn hoạt động, chỉ chặn WhenPlayed của con vừa summon gọi thêm summon.
    ///   - Nếu vị trí đầy giữa chừng → dừng loop, không báo lỗi.
    ///   - Unit được summon không tốn mana, không đến từ tay.
    ///   - WhenPlayed, OnAllyPlayed, OnRelatedAllyPlayed fire cho từng con.
    /// </summary>
    [CreateAssetMenu(menuName = "LoRClone/Skills/Summon Unit", fileName = "Skill_SummonUnit")]
    public class SkillSummonUnit : SkillData
    {
        [Header("Nguồn unit triệu hồi")]
        [Tooltip("true  = đọc danh sách unit từ CardData của LÁ GỐC (source.data.summonUnits) —\n" +
                 "        1 asset skill dùng chung, mỗi card khai báo unit riêng.\n" +
                 "false = dùng field unitToSummon bên dưới (hành vi cũ, tương thích ngược).")]
        public bool useSourceCardData = false;

        [Header("Unit sẽ được triệu hồi — chỉ dùng khi useSourceCardData = false")]
        [Tooltip("CardData của unit sẽ xuất hiện. Phải là cardType = Unit.")]
        public CardData unitToSummon;

        [Header("Số lượng")]
        [Tooltip("Số unit triệu hồi mỗi lần skill kích hoạt. Override bằng p1 trên CardAbility.")]
        [Min(1)]
        public int quantity = 1;

        [Header("Vị trí triệu hồi")]
        [Tooltip("false = bench (chờ tấn công).\ntrue  = battlefield (tấn công ngay vòng này).")]
        public bool summonToBattlefield = false;

        public override void Execute(CardModel source, GameContext ctx, int p1 = -1, int p2 = -1, int p3 = -1)
        {
            if (ctx?.controller == null)
            {
                Debug.LogWarning($"[SkillSummonUnit] ctx.controller null — skill không thể chạy ngoài GameController.");
                return;
            }

            // ── Xác định danh sách unit cần triệu hồi ─────────────────
            // useSourceCardData = true  → lấy từ CardData của LÁ GỐC (source.data.summonUnits).
            // useSourceCardData = false → dùng field unitToSummon (hành vi cũ).
            var toSummon = new List<CardData>();
            if (useSourceCardData)
            {
                var list = source?.data?.summonUnits;
                if (list != null)
                    foreach (var u in list) if (u != null) toSummon.Add(u);
                if (toSummon.Count == 0)
                {
                    Debug.LogWarning($"[SkillSummonUnit] '{source?.data?.cardName}': " +
                                     "useSourceCardData bật nhưng summonUnits rỗng.");
                    return;
                }
            }
            else
            {
                if (unitToSummon == null)
                {
                    Debug.LogWarning($"[SkillSummonUnit] '{source?.data?.cardName}': unitToSummon chưa được gán.");
                    return;
                }
                toSummon.Add(unitToSummon);
            }

            // count = số LẦN lặp toàn bộ danh sách (quantity/p1). Mặc định 1 → summon list 1 lượt.
            int count = (p1 >= 0) ? p1 : quantity;

            for (int r = 0; r < count; r++)
                foreach (var unitData in toSummon)
                {
                    var summoned = ctx.controller.SummonUnit(unitData, source.belongsToPlayer, summonToBattlefield);
                    if (summoned == null) return;   // bench/battlefield đầy → dừng hẳn
                }
        }
    }
}