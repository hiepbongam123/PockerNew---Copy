using System.Collections.Generic;

namespace LoRClone.Data
{
    /// <summary>
    /// BƯỚC 5 (PvP-RPG) — CỬA VÀO cho "Đấu Trường Anh Hùng" (PvP_Gauntlet).
    /// Gọi từ ChampionArenaView (view riêng), KHÔNG từ lobby deck-picker.
    ///
    /// ★ AN TOÀN: chỉ set static DỮ LIỆU (đã có sẵn). KHÔNG mutate trận, KHÔNG tự chạy.
    /// ★ ĐỐI XỨNG: PvP THƯỜNG phải gọi Leave() để không dính loadout của trận gauntlet trước.
    /// </summary>
    public static class GauntletEntry
    {
        /// <summary>
        /// GIỮ THAM CHIẾU library trang bị từ lúc mở Đấu Trường → asset KHÔNG bị unload → icon (Sprite) còn sống
        /// trên máy này suốt trận. NetworkBridge dùng nguồn này TRƯỚC để SetLibrary (khỏi phụ thuộc gán campaign
        /// hay Resources). Set ở ChampionArenaView khi bấm VÀO ĐẤU TRƯỜNG; xoá ở Leave().
        /// </summary>
        public static LoRClone.Data.CardItemLibrary Library;

        /// <summary>
        /// Chuẩn bị vào gauntlet với 1 champion đã nâng ở PoC.
        /// Đọc ProgressStore 1 lần qua ChampionLoadout.BuildFromProgress (item + mastery/sao/chòm sao + deck run).
        /// Set CẢ loadout (để áp buff) LẪN deck (cardName) TỪ CÙNG 1 NGUỒN → không lệch.
        ///
        /// LƯU Ý: caller (ChampionArenaView) nên set CardItem.SetLibrary trước khi gọi, nếu không
        /// item đã equip đọc ra rỗng.
        /// </summary>
        public static void Enter(ChampionData champ)
        {
            if (champ == null) return;

            var lo = ChampionLoadout.BuildFromProgress(champ);
            PvpLoadoutSelection.Set(lo);   // NetworkBridge đọc PvpLoadoutSelection.LocalJson lúc handshake.

            // Deck run của champion (đã tính trong loadout: ChampionDeckStore đã lưu, hoặc starterDeck).
            var names = new List<string>();
            if (lo.deckCardNames != null) foreach (var n in lo.deckCardNames) if (!string.IsNullOrEmpty(n)) names.Add(n);
            PvpDeckSelection.Set(champ.championName, names);

            MatchContext.mode = MatchMode.PvP_Gauntlet; // handshake sẽ set lại — trùng, an toàn.
        }

        /// <summary>
        /// DỌN SẠCH state gauntlet — GỌI Ở MỌI luồng KHÔNG-gauntlet (PvP thường, Đấu Với Máy, Con Đường Anh Hùng)
        /// để feature này KHÔNG rò rỉ sang mode khác (item hiện nhầm, buff áp nhầm...).
        /// Reset: mode→PvE, loadout/champion null, deck+loadout selection, và HOÀN NGUYÊN thư viện item override
        /// (SetLibrary(null) → CardItem đọc lại theo CampaignContext của PoC như cũ).
        /// </summary>
        public static void Leave()
        {
            PvpLoadoutSelection.Clear();
            PvpDeckSelection.Clear();
            MatchContext.Clear();
            Library = null;                             // thả tham chiếu library gauntlet
            LoRClone.Model.CardItem.SetLibrary(null);   // gỡ override → PoC/PvE resolve item theo campaign của nó
        }
    }
}