namespace LoRClone.Data
{
    /// <summary>
    /// Chế độ của 1 trận đấu — 1 NGUỒN SỰ THẬT thay cho việc rải cờ (networkMode, CampaignContext.Active,
    /// InputMode.Hotseat...) khắp nơi. Bước gắn (sau) sẽ hợp nhất; hiện chỉ THÊM, không thay.
    /// </summary>
    public enum MatchMode
    {
        PvE_Adventure,  // Con Đường Anh Hùng (PvE) — loadout của MÌNH build từ ProgressStore
        VsAI,           // Đấu với máy thường — KHÔNG loadout
        PvP_Normal,     // PvP thường (deck-only) — KHÔNG loadout (giữ công bằng)
        PvP_Gauntlet,   // "Đấu Trường Anh Hùng" — PvP-RPG: CÓ loadout 2 phe (nhận qua mạng)
        Hotseat,        // 2 người 1 máy
    }

    /// <summary>
    /// BƯỚC 3 (PvP-RPG) — BỐI CẢNH TRẬN cho chế độ mới. Static, sống xuyên scene (giống CampaignContext).
    ///
    /// ★ CHỈ LÀ DỮ LIỆU: không mutate trận, không đọc ProgressStore, không tự chạy.
    ///   Thêm vào project = 0 thay đổi hành vi. Chỉ có tác dụng khi bước 4 (nối mạng) đọc/ghi nó.
    ///
    /// Canonical (khớp GameController.Net): player = HOST, enemy = CLIENT trên cả 2 máy.
    ///   PvE  : chỉ playerLoadout (enemy = null).
    ///   PvP-RPG: cả 2 loadout — mỗi máy tự build phe mình rồi trao qua mạng, áp GIỐNG HỆT 2 máy.
    /// </summary>
    public static class MatchContext
    {
        public static MatchMode mode = MatchMode.PvE_Adventure;

        // Loadout 2 phe (canonical player=host / enemy=client).
        public static ChampionLoadout playerLoadout;
        public static ChampionLoadout enemyLoadout;

        // Lá champion mỗi phe — để ChampionLoadoutApplier biết buff lá nào. null nếu phe đó không dùng champion.
        public static CardData playerChampionCard;
        public static CardData enemyChampionCard;

        /// <summary>Chế độ này có áp loadout champion không? (PvP thường & VsAI = không → công bằng/đơn giản.)</summary>
        public static bool AppliesLoadout =>
            mode == MatchMode.PvE_Adventure || mode == MatchMode.PvP_Gauntlet;

        /// <summary>Trận qua mạng (lockstep) — cần tất định 2 máy.</summary>
        public static bool IsNetwork =>
            mode == MatchMode.PvP_Normal || mode == MatchMode.PvP_Gauntlet;

        // ── Tiện ích lấy đúng loadout / champion theo phe ──
        /// <summary>Loadout của phe: true = player(host), false = enemy(client).</summary>
        public static ChampionLoadout LoadoutFor(bool playerSide)
            => playerSide ? playerLoadout : enemyLoadout;

        public static CardData ChampionFor(bool playerSide)
            => playerSide ? playerChampionCard : enemyChampionCard;

        /// <summary>
        /// Chữ ký NGẮN của 2 loadout (chỉ mode Gauntlet) — nhồi vào StateFingerprint để BẮT
        /// lệch loadout giữa trận. Hash FNV-1a ỔN ĐỊNH cross-machine (KHÔNG dùng string.GetHashCode
        /// — hàm đó randomize mỗi process → 2 máy khác nhau → báo desync GIẢ).
        /// </summary>
        public static string LoadoutSig()
        {
            if (mode != MatchMode.PvP_Gauntlet) return "";
            string a = playerLoadout != null ? playerLoadout.Signature() : "-";
            string b = enemyLoadout != null ? enemyLoadout.Signature() : "-";
            return Fnv(a) + "/" + Fnv(b);
        }

        static string Fnv(string s)
        {
            unchecked
            {
                uint h = 2166136261u;
                if (s != null) foreach (char ch in s) { h ^= ch; h *= 16777619u; }
                return h.ToString("x8");
            }
        }

        public static void Clear()
        {
            mode = MatchMode.PvE_Adventure;
            playerLoadout = null;
            enemyLoadout = null;
            playerChampionCard = null;
            enemyChampionCard = null;
        }
    }
}