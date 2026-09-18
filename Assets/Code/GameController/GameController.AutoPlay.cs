using System.Collections.Generic;
using UnityEngine;
using LoRClone.Data;
using LoRClone.Model;

namespace LoRClone.Controller
{
    /// <summary>
    /// GameController — phần Auto-Play (player do AI điều khiển).
    /// Partial mới, chỉ thêm 1 cờ + toggle. Không sửa file GameController khác.
    ///
    /// Bật cờ → RaiseStateChanged() → OnStateChanged fire → PlayerAI (EnemyAI với
    /// controlsPlayer=true) thấy AutoActive=true và bắt đầu đánh hộ.
    ///
    /// ⚠ PHASE 2 (chưa làm): để auto-play chạy KHÔNG xung đột, cần gate ở GameView/MulliganView
    /// (bỏ qua handler phe player + khoá input người khi playerAutoPlay=true). Xem ghi chú bàn giao.
    /// </summary>
    public partial class GameController
    {
        [Header("Auto-Play")]
        [Tooltip("true = phe PLAYER do AI điều khiển (cần PlayerAI: 1 GameObject EnemyAI với controlsPlayer=true).")]
        public bool playerAutoPlay = false;

        /// <summary>Bật/tắt auto-play cho player. Kích OnStateChanged để AI vào cuộc ngay.</summary>
        public void SetPlayerAutoPlay(bool on)
        {
            playerAutoPlay = on;
            RaiseStateChanged();
        }

        public void TogglePlayerAutoPlay() => SetPlayerAutoPlay(!playerAutoPlay);

        // ── PAUSE ─────────────────────────────────────────────────
        /// <summary>Cờ tạm dừng: AI (cả enemy lẫn player) ngừng bắt đầu lượt mới.</summary>
        public bool isPaused { get; private set; }

        /// <summary>Tạm dừng: đóng băng AI + animation qua timeScale=0, chặn AI vào lượt mới.</summary>
        public void Pause() { isPaused = true; Time.timeScale = 0f; Debug.Log("[Pause] PAUSED (timeScale=0)"); }

        /// <summary>Tiếp tục: khôi phục timeScale, AI chạy lại.</summary>
        public void Resume() { isPaused = false; Time.timeScale = 1f; Debug.Log("[Pause] RESUMED (timeScale=1)"); RaiseStateChanged(); }

        /// <summary>Tự chọn target cho skill/spell khi autoplay (ưu tiên ATK cao nhất theo target type).</summary>
        internal List<CardModel> AutoPickTargets(CardData data, bool byPlayer)
        {
            var result = new List<CardModel>();
            if (data == null) return result;
            var me = P(byPlayer); var opp = P(!byPlayer);
            for (int step = 0; step < data.targetCount; step++)
            {
                var type = data.GetTargetTypeForStep(step);
                CardModel pick = null;
                void scan(PlayerModel pl)
                {
                    foreach (var c in pl.BattlefieldCards())
                        if (c.IsAlive && !result.Contains(c) && (pick == null || c.effectiveAttack > pick.effectiveAttack)) pick = c;
                    foreach (var c in pl.BenchCards())
                        if (c.IsAlive && !result.Contains(c) && (pick == null || c.effectiveAttack > pick.effectiveAttack)) pick = c;
                }
                if (type == TargetType.EnemyUnit) scan(opp);
                else if (type == TargetType.AllyUnit) scan(me);
                else if (type == TargetType.AnyUnit) { scan(opp); if (pick == null) scan(me); }
                else scan(opp);
                if (pick == null) break;
                result.Add(pick);
            }
            return result;
        }
    }
}