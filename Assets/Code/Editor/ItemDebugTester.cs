using System.Collections.Generic;
using UnityEngine;
using LoRClone.Model;

namespace LoRClone
{
    /// <summary>
    /// CHỈ ĐỂ TEST — bỏ component này lên bất kỳ GameObject nào trong scene.
    /// Bấm F9 khi đang trong 1 run Leo Tháp (đã StartRun, RunDeck có bài):
    ///   → gắn 1 item Epic vào MỌI lá trong deck.
    /// Sau đó vào trận, ZOOM (inspect) 1 lá của mình → panel hexagon hiện bên PHẢI card.
    /// Xoá file này khi test xong.
    ///
    /// Lưu ý: dùng legacy Input. Nếu project chỉ bật Input System (New), đổi Input.GetKeyDown
    /// sang Keyboard.current.f9Key.wasPressedThisFrame.
    /// </summary>
    public class ItemDebugTester : MonoBehaviour
    {
        void Update()
        {
            if (!Input.GetKeyDown(KeyCode.F9)) return;

            var deck = LoRClone.Data.CampaignRun.RunDeck;
            if (deck == null || deck.Count == 0)
            {
                Debug.LogWarning("[ItemDebugTester] RunDeck rỗng — vào 1 run Leo Tháp trước (chọn champion).");
                return;
            }

            int n = 0;
            var seen = new HashSet<string>();
            foreach (var cd in deck)
            {
                if (cd == null || !seen.Add(cd.cardName)) continue;
                RunItems.Attach(cd.cardName, CardItem.RandomOf(ItemRarity.Epic));
                n++;
            }
            Debug.Log($"[ItemDebugTester] Đã gắn item Epic cho {n} loại lá. Vào trận, ZOOM lá để xem panel hexagon.");
        }
    }
}
