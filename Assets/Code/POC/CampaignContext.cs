using UnityEngine;

namespace LoRClone.Data
{
    /// <summary>
    /// Context tĩnh truyền qua scene: đang chơi tầng nào của campaign nào.
    /// CampaignView set trước khi LoadScene; CampaignResultWatcher đọc khi game over.
    /// Không đụng GameConfig — trận thường (ĐẤU TRẬN) không bị ảnh hưởng.
    /// </summary>
    public static class CampaignContext
    {
        public static CampaignData campaign;
        public static int levelIndex = -1;
        public static string returnSceneName;

        /// <summary>Khi về sảnh từ 1 trận Leo Tháp → LobbyMenuView tự mở lại màn Leo Tháp
        /// (thay vì đứng ở menu chính). Không reset trong Clear() để sống qua scene load.</summary>
        public static bool reopenCampaign;

        // ── Mốc 2: Bản đồ nhánh (Con Đường Anh Hùng) ──────────────
        /// <summary>Bản đồ hành trình hiện tại (null = chưa bắt đầu run map).</summary>
        public static RunMap map;
        /// <summary>Id node đang đánh — CampaignResultWatcher đánh dấu done khi thắng.</summary>
        public static int currentNodeId = -1;

        /// <summary>Mốc 4: vừa thắng Elite → RunMapView hiện màn nhặt Relic khi về map.</summary>
        public static bool pendingRelic;

        /// <summary>M10: vừa hạ BOSS (hết hành trình) → RunMapView hiện màn TỔNG KẾT.</summary>
        public static bool pendingRunSummary;

        public static bool Active => campaign != null && levelIndex >= 0;

        public static LevelData CurrentLevel =>
            Active && campaign.levels != null && levelIndex < campaign.levels.Count
                ? campaign.levels[levelIndex] : null;

        public static void Clear()
        {
            campaign = null;
            levelIndex = -1;
        }
    }
}