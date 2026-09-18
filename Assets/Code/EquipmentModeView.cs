using System.Collections.Generic;
using UnityEngine;
using LoRClone.Data;
using LoRClone.Model;
using LoRClone.Controller;

namespace LoRClone.View
{
    /// <summary>
    /// Hiện CardPickerView khi player chơi một Equipment card có Darkin unit.
    ///
    /// Cách hoạt động:
    ///   Khi GameController.OnEquipmentNeedsMode fire:
    ///     • Nếu là AI → tự động chọn (Darkin nếu bench còn chỗ, ngược lại Trang Bị).
    ///     • Nếu là player → hiện CardPickerView với 2 lá:
    ///         index 0 = bản thân Equipment card  → mode "Trang Bị"
    ///         index 1 = Darkin unit card          → mode "Triệu Hồi Darkin"
    ///       Player click lá bài để chọn mode, hoặc Hủy / ESC để cancel.
    ///
    /// Setup trong Unity Editor:
    ///   1. Gán EquipmentModeView lên bất kỳ GameObject nào trong scene.
    ///   2. Đảm bảo có một GameObject riêng với CardPickerView component trong scene
    ///      (có gán cardPrefab).
    ///   Không cần gán gì thêm vào Inspector của component này.
    ///
    /// Mở rộng:
    ///   Để hỗ trợ các mechanic khác (Predict, WhenPlayed…) chỉ cần gọi
    ///   CardPickerView.Instance.Show(choices, title, onChosen) tương tự.
    /// </summary>
    public class EquipmentModeView : MonoBehaviour
    {
        // ── Lifecycle ─────────────────────────────────────────────

        void Start()
        {
            var gc = GameController.Instance;
            if (gc == null)
            {
                Debug.LogWarning("[EquipmentModeView] Không tìm thấy GameController.");
                return;
            }
            gc.OnEquipmentNeedsMode += HandleNeedsMode;
        }

        void OnDestroy()
        {
            if (GameController.Instance != null)
                GameController.Instance.OnEquipmentNeedsMode -= HandleNeedsMode;
        }

        // ── Handler ───────────────────────────────────────────────

        void HandleNeedsMode(CardModel equipment, bool isPlayer, System.Action<int> callback)
        {
            // ── PvE Enemy AI: tự chọn ─────────────────────────────
            // ★ PvP: máy KHÁCH không bao giờ gọi event này (EquipmentSequence đi nhánh chờ),
            //   nên khi event fire ở PvP là CHỦ người thật → luôn hiện picker (bỏ auto).
            if (!GameController.Instance.networkMode && !isPlayer)
            {
                bool benchHasSpace = GameController.Instance.model.enemy.BenchHasSpace;
                callback?.Invoke(benchHasSpace ? 1 : 0);
                return;
            }

            // ── Player: hiện CardPickerView ───────────────────────
            var picker = CardPickerView.Instance;
            if (picker == null)
            {
                Debug.LogWarning(
                    "[EquipmentModeView] Không tìm thấy CardPickerView trong scene. " +
                    "Hãy thêm GameObject có CardPickerView component vào scene.");
                callback?.Invoke(0); // fallback: Trang Bị
                return;
            }

            var choices = new List<CardData>
            {
                equipment.data,           // index 0 = Trang Bị (hiện equipment card)
                equipment.data.darkinUnit // index 1 = Triệu Hồi Darkin (hiện darkin unit card)
            };

            // Tiêu đề: tên card + hướng dẫn
            string title = $"{equipment.data.cardName}\nChọn chế độ:";

            var labels = new[]
            {
                $"Trang Bị\n+{equipment.data.equipAttackBuff}|+{equipment.data.equipHealthBuff}",
                $"Triệu Hồi Darkin\n{equipment.data.darkinUnit.baseAttack}/{equipment.data.darkinUnit.baseHealth}"
            };

            picker.ShowWithLabels(choices, title, labels, idx =>
            {
                // idx: 0 = Trang Bị, 1 = Triệu Hồi Darkin, -1 = Hủy
                callback?.Invoke(idx);
            }, showCancel: true);
        }
    }
}