using System.Collections.Generic;
using UnityEngine;
using LoRClone.Model;   // KeywordType, ItemRarity, CardItem

namespace LoRClone.Data
{
    /// <summary>
    /// 1 định nghĩa trang bị per-card do bạn author trong Inspector — GỒM cả SkillData thật.
    /// Đây là điểm khác biệt với Pool code: ở đây bạn KÉO THẢ được asset SkillData vào từng món.
    /// </summary>
    [System.Serializable]
    public class CardItemDef
    {
        public string itemName = "Trang Bị";
        public ItemRarity rarity = ItemRarity.Common;

        [Header("Chỉ số")]
        public int atkBonus;
        public int hpBonus;
        [Min(0)] public int costReduction;

        [Header("Keyword (tuỳ chọn)")]
        public bool grantsKeyword = false;
        public KeywordType keyword = KeywordType.Barrier;

        [Header("Kỹ năng kích hoạt (tuỳ chọn)")]
        [Tooltip("Kéo 1 asset SkillData vào đây. Khi lá đeo món này lên sân, skill chạy theo TRIGGER của nó.\n" +
                 "Trống = món chỉ có chỉ số/keyword.")]
        public SkillData skill;

        public Sprite icon;

        [Header("TRANG BỊ CHAMPION (meta — Lõi + mastery + equip vĩnh viễn)")]
        [Tooltip("BẬT = đây là trang bị CHAMPION (mua bằng Lõi / mở theo mastery, đeo vĩnh viễn vào lá champion).\n" +
                 "TẮT = trang bị per-card thường (nhặt trong run bằng Vàng, reset mỗi run).")]
        public bool championEquip = false;
        [Tooltip("(Chỉ champion equip) +mana khởi đầu mỗi trận.")]
        public int bonusMana = 0;
        [Tooltip("(Chỉ champion equip) +máu Nexus khởi đầu mỗi trận.")]
        public int bonusNexus = 0;

        [Header("CỬA HÀNG")]
        [Tooltip("(Per-card) Có được bày bán ở CỬA HÀNG trong-run không. Tắt = chỉ rơi ở node thưởng/vật phẩm.")]
        public bool sellInShop = true;
        [Tooltip("(Per-card) Giá VÀNG ở cửa hàng trong hành trình. 0 = tự tính theo độ hiếm (Thường 40 / Hiếm 70 / Sử Thi 110).")]
        [Min(0)] public int goldCost = 0;
        [Tooltip("(Champion equip) Giá LÕI ở shop meta. 0 = không bán bằng Lõi (chỉ mở theo mastery).")]
        [Min(0)] public int coreCost = 0;
        [Tooltip("(Champion equip) Cấp mastery cần đạt để mở khoá (1 = có ngay). Món bán bằng Lõi nên đặt cao (vd 99).")]
        [Min(1)] public int unlockMasteryLevel = 1;

        /// <summary>Dựng CardItem runtime từ định nghĩa (kèm skill + giá + champion-equip).</summary>
        public CardItem ToRuntime()
        {
            KeywordType? kw = grantsKeyword ? keyword : (KeywordType?)null;
            return new CardItem(itemName, rarity, atkBonus, hpBonus, costReduction, kw, icon, skill,
                                goldCost, coreCost, championEquip, bonusMana, bonusNexus);
        }
    }

    /// <summary>
    /// THƯ VIỆN TRANG BỊ per-card (CardItem + SkillData). Gán vào CampaignData.cardItemLibrary.
    /// THƯ VIỆN DUY NHẤT để thiết kế trang bị: RandomOf/AllFromLibrary bốc từ đây. Trống = không có item nào.
    ///
    /// SETUP: Create → LoRClone → Card Item Library → thêm item, kéo SkillData asset vào ô 'skill'.
    /// Lưu/khôi phục theo TÊN món → tự tra ngược library để lấy lại SkillData, không cần registry riêng.
    /// </summary>
    [CreateAssetMenu(menuName = "LoRClone/Card Item Library", fileName = "CardItemLibrary")]
    public class CardItemLibrary : ScriptableObject
    {
        public List<CardItemDef> items = new List<CardItemDef>();
    }
}