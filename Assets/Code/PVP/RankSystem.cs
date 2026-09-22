using UnityEngine;
using LoRClone.Controller;
using LoRClone.Data;

namespace LoRClone.Net
{
    /// <summary>
    /// TÍNH ĐIỂM RANK cuối trận PvP (PvP thường + Đấu Trường Anh Hùng).
    ///   • Thắng +WinDelta, thua -LossDelta (sàn FloorMmr).
    ///   • Lưu MMR vào ProgressStore (local + tự đồng bộ cloud) và ĐẨY lên leaderboard UGS.
    ///
    /// CHỈ tính cho trận MẠNG (gc.networkMode). PvE / Đấu Với Máy KHÔNG tính → chơi solo test
    /// sẽ KHÔNG đổi điểm (đó là đúng; muốn thử điểm dùng nút NỘP ĐIỂM TEST trong Bảng Xếp Hạng).
    /// MỖI trận chỉ tính 1 lần (CheckGameOver có thể bắn OnGameOver nhiều lần → cờ _reported chặn).
    ///
    /// ★ TỰ TÚC: KHÔNG gọi gc.IsNetworkMatch / gc.LocalViewerWon (2 hàm này không chắc tồn tại).
    ///   Thắng/thua suy TRỰC TIẾP từ máu nexus + phe local (GameController.NetLocalIsPlayer).
    /// </summary>
    public static class RankSystem
    {
        public static int WinDelta = 25;
        public static int LossDelta = 20;
        public static int FloorMmr = 0;     // MMR không xuống dưới mức này

        /// <summary>PvP THƯỜNG có tính điểm không (lobby set theo toggle Thường/Xếp hạng). Đấu Trường luôn tính.</summary>
        public static bool RankedEnabled = false;

        /// <summary>Kết quả lần tính gần nhất — RankReporter đọc để hiện banner trên màn kết thúc.</summary>
        public struct Result { public bool counted; public bool won; public int oldMmr, newMmr, delta; }
        public static Result Last;

        static bool _reported;

        /// <summary>Gọi khi BẮT ĐẦU trận → cho phép tính 1 lần cho trận mới.</summary>
        public static void BeginMatch() { _reported = false; Last = default; }

        /// <summary>Gọi khi trận kết thúc (model.OnGameOver). Tự lọc: chỉ trận MẠNG mới tính.</summary>
        public static void ReportMatch(GameController gc)
        {
            if (_reported || gc == null) return;
            if (!gc.networkMode) { Last = new Result { counted = false }; return; }  // solo/PvE → không tính

            // Chỉ tính khi: PvP thường + toggle XẾP HẠNG bật, HOẶC Đấu Trường Anh Hùng (luôn tính).
            bool ranked = RankedEnabled || MatchContext.mode == MatchMode.PvP_Gauntlet;
            if (!ranked) { Last = new Result { counted = false }; return; }
            _reported = true;

            int oldMmr = ProgressStore.RankMmr;
            bool won = LocalWon(gc);
            int newMmr = ProgressStore.ApplyRankResult(won, WinDelta, LossDelta, FloorMmr);
            Last = new Result { counted = true, won = won, oldMmr = oldMmr, newMmr = newMmr, delta = newMmr - oldMmr };

            Debug.Log($"[Rank] Kết thúc PvP: {(won ? "THẮNG +" + WinDelta : "THUA -" + LossDelta)} → MMR {oldMmr}→{newMmr}");
            UgsAccount.Instance?.SubmitRank(newMmr);   // đẩy lên leaderboard 'rank_mmr' (Update = Keep Latest)
        }

        /// <summary>Suy thắng/thua của MÁY NÀY từ máu nexus. localIsPlayer = phe mình điều khiển.</summary>
        static bool LocalWon(GameController gc)
        {
            var m = gc.model;
            if (m == null) return false;
            bool localIsPlayer = GameController.NetLocalIsPlayer;
            var me = localIsPlayer ? m.player : m.enemy;
            var opp = localIsPlayer ? m.enemy : m.player;
            if (me.health <= 0) return false;   // nexus mình chết → thua
            if (opp.health <= 0) return true;    // nexus địch chết → thắng
            return me.health > opp.health;       // kết thúc khác (deckout…) → so máu
        }
    }
}