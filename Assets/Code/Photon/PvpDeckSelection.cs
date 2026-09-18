using System.Collections.Generic;

namespace LoRClone.Data
{
    /// <summary>
    /// Deck mà NGƯỜI CHƠI LOCAL đã chọn để đấu PvP (và cả PvE nếu muốn dùng chung).
    ///
    /// Lưu dưới dạng DANH SÁCH cardName THEO THỨ TỰ — vì PvP lockstep cần 2 máy dựng lại
    /// DeckData GIỐNG HỆT (cùng thứ tự lá → cùng id/netId → cùng shuffle theo seed).
    /// Trao cardName (không trao asset/tên-file) để máy kia — vốn KHÔNG có deck tự tạo này —
    /// vẫn dựng lại được qua CardLibrary (asset chung, giống nhau ở cả 2 build).
    ///
    /// UI (màn chọn deck) chỉ cần gọi PvpDeckSelection.Set(...) khi người chơi chốt deck.
    /// NetworkBridge đọc cái này lúc handshake.
    /// </summary>
    public static class PvpDeckSelection
    {
        /// <summary>Tên deck (để hiển thị / log). Không dùng để load.</summary>
        public static string LocalDeckName { get; private set; }

        /// <summary>cardName theo đúng thứ tự trong deck. Đây mới là dữ liệu trao qua mạng.</summary>
        public static List<string> LocalDeckCards { get; private set; }

        public static bool HasSelection =>
            LocalDeckCards != null && LocalDeckCards.Count > 0;

        public static void Set(string name, List<string> cardNames)
        {
            LocalDeckName = name;
            LocalDeckCards = cardNames != null ? new List<string>(cardNames) : null;
        }

        /// <summary>Tiện dụng: set từ 1 DeckDTO đã lưu (Xưởng Deck).</summary>
        public static void Set(CustomDeckStore.DeckDTO dto)
        {
            if (dto == null) { Clear(); return; }
            Set(dto.name, dto.cardNames);
        }

        public static void Clear()
        {
            LocalDeckName = null;
            LocalDeckCards = null;
        }
    }
}
