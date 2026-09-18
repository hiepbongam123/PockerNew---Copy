using System.Collections.Generic;
using UnityEngine;
using LoRClone.Model;

namespace LoRClone.Data
{
    /// <summary>1 ô trang bị để VẼ — chữ (tên/mô tả/độ hiếm) từ LOADOUT (không cần library); icon từ library nếu có.</summary>
    public struct PvpItemView
    {
        public string name, desc;
        public Sprite icon;
        public ItemRarity rarity;
    }

    /// <summary>
    /// Helper HIỂN THỊ trang bị champion trong PvP-Gauntlet (icon dải phải + panel inspect).
    ///
    /// Vì sao cần: CardView vốn đọc trang bị qua CampaignRun.champion / RunItems (chỉ có trong PoC).
    /// Ở PvP CampaignRun.champion = null → không hiện gì. Helper này lấy trang bị từ LOADOUT
    /// (MatchContext) — dữ liệu đã trao qua mạng — để CardView hiện GIỐNG PoC.
    ///
    /// ★ ĐỘC LẬP LIBRARY: ô trang bị dựng THẲNG từ tên món trong loadout (equippedItemNames).
    ///   Có library (đã SetLibrary) → thêm icon + mô tả. KHÔNG có library → vẫn hiện ô + TÊN món
    ///   (icon fallback). Nhờ vậy panel LUÔN hiện dù máy kia quên gán campaign — trước đây library
    ///   null làm cả panel biến mất.
    ///
    /// ★ CHỈ trả dữ liệu khi mode == PvP_Gauntlet. PvE/PvP-thường → rỗng → CardView chạy nhánh cũ y hệt.
    /// </summary>
    public static class PvpItemDisplay
    {
        /// <summary>Lá này CÓ PHẢI champion (của loadout phe nó) trong trận gauntlet không?
        /// Khớp theo THAM CHIẾU lá champion trước (giống ChampionLoadoutApplier) — chắc ăn khi cardName
        /// KHÁC championName (VD lá tên "Anglea The ..." còn championName = "Anglea"); rồi mới fallback TÊN.</summary>
        public static bool IsChampion(CardModel card)
        {
            if (card == null || card.data == null) return false;
            if (MatchContext.mode != MatchMode.PvP_Gauntlet) return false;
            var lo = MatchContext.LoadoutFor(card.belongsToPlayer);
            if (lo == null) return false;

            // 1) Khớp tham chiếu lá champion (nguồn chính applier dùng để buff → buff & panel luôn ĐỒNG BỘ).
            var champCard = MatchContext.ChampionFor(card.belongsToPlayer);
            if (champCard != null && (card.data == champCard || card.originalData == champCard)) return true;

            // 2) Fallback theo TÊN (khi championCard chưa resolve được).
            if (!string.IsNullOrEmpty(lo.championName))
            {
                if (card.data.cardName == lo.championName) return true;
                if (card.originalData != null && card.originalData.cardName == lo.championName) return true;
            }
            return false;
        }

        /// <summary>Số ô trang bị hiện cho champion — suy từ SAO trong loadout (giống ProgressStore.ItemSlots).
        /// Dùng SAO đã TRUYỀN QUA MẠNG → 2 máy hiện cùng số ô cho cùng 1 champion (kể cả champion địch).</summary>
        public static int SlotCount(CardModel card)
        {
            var lo = (card != null && MatchContext.mode == MatchMode.PvP_Gauntlet)
                ? MatchContext.LoadoutFor(card.belongsToPlayer) : null;
            int star = lo != null ? lo.star : 1;
            int slots = 1 + (star - 1) / 2;                 // 1→3 theo sao
            return slots < 1 ? 1 : (slots > 3 ? 3 : slots);
        }

        static readonly List<PvpItemView> EmptyViews = new List<PvpItemView>();

        /// <summary>Danh sách ô trang bị để VẼ cho 1 lá champion gauntlet.
        /// TÊN/MÔ TẢ/ĐỘ HIẾM lấy TỪ LOADOUT (đã truyền qua mạng, chốt lúc build nơi CÓ library) → hiện được
        /// dù máy xem KHÔNG có library. Icon tra thêm từ library nếu sẵn (Sprite không serialize được).</summary>
        public static List<PvpItemView> ViewsFor(CardModel card)
        {
            if (!IsChampion(card)) return EmptyViews;
            var lo = MatchContext.LoadoutFor(card.belongsToPlayer);
            if (lo == null || lo.equippedItemNames == null || lo.equippedItemNames.Length == 0) return EmptyViews;

            var res = new List<PvpItemView>();
            for (int i = 0; i < lo.equippedItemNames.Length; i++)
            {
                string n = lo.equippedItemNames[i];
                if (string.IsNullOrEmpty(n)) continue;
                string desc = (lo.equippedItemDescs != null && i < lo.equippedItemDescs.Length) ? lo.equippedItemDescs[i] : "";
                int rar = (lo.equippedItemRarities != null && i < lo.equippedItemRarities.Length) ? lo.equippedItemRarities[i] : 0;
                res.Add(new PvpItemView
                {
                    name = n,
                    desc = desc,
                    rarity = (ItemRarity)rar,
                    icon = CardItem.IconForItem(n)   // null nếu library chưa sẵn → panel dùng glyph
                });
            }
            return res;
        }
    }
}