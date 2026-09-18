namespace LoRClone.Data
{
    /// <summary>
    /// BƯỚC 4 (PvP-RPG) — Loadout champion của NGƯỜI CHƠI LOCAL, chờ gửi qua mạng.
    /// Song song với PvpDeckSelection: PvpDeckSelection giữ DECK, cái này giữ LOADOUT (buff champion).
    ///
    /// Lobby SET khi vào "Đấu Trường Anh Hùng" (PvP-RPG): PvpLoadoutSelection.Set(loadout).
    /// NetworkBridge ĐỌC lúc handshake để trao 2 máy.
    ///
    /// RỖNG = PvP thường (deck-only, không loadout) → không ảnh hưởng gì.
    /// Chỉ là dữ liệu (chuỗi JSON) — không mutate trận, không đọc ProgressStore.
    /// </summary>
    public static class PvpLoadoutSelection
    {
        /// <summary>Loadout local dạng JSON (để trao qua mạng). "" = không có (PvP thường).</summary>
        public static string LocalJson { get; private set; } = "";

        public static bool HasSelection => !string.IsNullOrEmpty(LocalJson);

        public static void Set(ChampionLoadout lo) => LocalJson = lo != null ? lo.ToJson() : "";

        public static void Clear() => LocalJson = "";
    }
}
