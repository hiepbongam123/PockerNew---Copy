using System;
using UnityEngine;

namespace LoRClone.Controller
{
    /// <summary>
    /// GameController — ĐẦU HÀNG (concede) + tiện ích kết quả.
    /// Partial mới, không sửa file khác. Đầu hàng = giết nexus phe mình → CheckGameOver kết thúc
    /// trận (bên kia thắng). PvP: relay qua NetworkBridge để 2 máy cùng kết thúc (KHÔNG ngắt Photon).
    /// </summary>
    public partial class GameController
    {
        /// <summary>NetworkBridge subscribe → gửi RPC báo phe nào (canonical) đầu hàng.</summary>
        public event Action<bool> OnLocalConcedeForNetwork;

        /// <summary>Người chơi LOCAL bấm Đầu Hàng.</summary>
        public void ConcedeLocal()
        {
            if (model == null || model.phase == LoRClone.Model.GamePhase.GameOver) return;
            bool localIsPlayerSide = networkMode ? NetLocalIsPlayer : true;
            ApplyConcede(localIsPlayerSide);
            if (networkMode) OnLocalConcedeForNetwork?.Invoke(localIsPlayerSide);
        }

        /// <summary>Máy KIA nhận qua mạng: phe (canonical) nào đã đầu hàng.</summary>
        public void ApplyRemoteConcede(bool concederIsPlayerSide)
        {
            if (model == null || model.phase == LoRClone.Model.GamePhase.GameOver) return;
            ApplyConcede(concederIsPlayerSide);
        }

        void ApplyConcede(bool playerSideConcedes)
        {
            var side = playerSideConcedes ? model.player : model.enemy;
            int dmg = side.health > 0 ? side.health : 1;
            side.TakeDamage(dmg);   // nexus về 0 → thua
            Debug.Log($"[Concede] {(playerSideConcedes ? "Player(host)" : "Enemy(client)")} đầu hàng → -{dmg} máu.");
            model.CheckGameOver();
            RaiseStateChanged();
        }

        // ── Tiện ích cho UI kết quả ──────────────────────────────────
        /// <summary>true = đây là trận PvP (đang nối mạng).</summary>
        public bool IsNetworkMatch => networkMode;

        /// <summary>
        /// Người xem LOCAL có THẮNG không (tính đúng cho cả PvE lẫn PvP).
        /// Phe thua = phe hết máu. PvP: phe local do NetLocalIsPlayer quyết định.
        /// </summary>
        public bool LocalViewerWon()
        {
            if (model == null) return false;
            bool playerSideWon = model.player.IsAlive;          // ai còn sống là thắng (thua = hết máu)
            bool localIsPlayerSide = networkMode ? NetLocalIsPlayer : true;
            return playerSideWon == localIsPlayerSide;
        }

        /// <summary>Đã kết thúc trận chưa.</summary>
        public bool IsGameOver => model != null && model.phase == LoRClone.Model.GamePhase.GameOver;
    }
}
