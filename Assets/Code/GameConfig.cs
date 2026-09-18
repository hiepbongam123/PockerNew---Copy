using LoRClone.Data;

namespace LoRClone
{
    /// <summary>
    /// Config tĩnh truyền giữa scene Lobby → Game.
    /// LobbyManager gán deck trước khi LoadScene.
    /// GameController đọc trong StartGame() rồi gọi Clear().
    /// </summary>
    public static class GameConfig
    {
        public static DeckData playerDeck;
        public static DeckData enemyDeck;

        /// <summary>
        /// CardLibrary chung — set từ Lobby (LobbyMenuView), sống xuyên scene để NetworkBridge
        /// (scene game) resolve cardName → CardData khi dựng lại deck PvP nhận qua mạng.
        /// KHÔNG bị Clear() xóa (giữ xuyên nhiều trận).
        /// </summary>
        public static CardLibrary cardLibrary;

        public static bool IsReady => playerDeck != null && enemyDeck != null;

        public static void Clear()
        {
            playerDeck = null;
            enemyDeck = null;
            // cardLibrary KHÔNG clear — cần dùng lại cho các trận PvP sau.
        }
    }
}