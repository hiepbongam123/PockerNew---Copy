using System.Collections.Generic;
using UnityEngine;

namespace LoRClone.Data
{
    /// <summary>
    /// MỐC 12 — LADDER ẢI: 21 ải từ 0★ → 10★ (bước 0.5), 1 CHUỖI DUY NHẤT rải LUÂN PHIÊN
    /// qua 9 khu vực. Mở khoá tuần tự: thắng ải sao ngay trước mới mở ải sao kế (prerequisite).
    /// Sao càng cao → map dài hơn + địch mạnh hơn + thưởng nhiều hơn.
    ///
    /// Chỉ chạy khi CampaignData.adventures chưa phải ladder (không có ải 0★).
    /// </summary>
    public static class AdventureFactory
    {
        // 9 vùng Runeterra — thứ tự luân phiên khi rải các mốc sao.
        static readonly string[] Regions =
        {
            "Demacia", "Noxus", "Ionia", "Freljord", "Piltover & Zaun",
            "Bilgewater", "Targon", "Shurima", "Đảo Bóng Đêm"
        };

        public static void EnsureAdventures(CampaignData campaign)
        {
            if (campaign == null) return;
            var list = campaign.adventures;

            // Nhận diện LADDER mới qua sự tồn tại của ải 0★ (chỉ ladder mới có). Không có → dựng lại.
            bool hasLadder = false;
            if (list != null)
                foreach (var a in list)
                    if (a != null && a.starDifficulty <= 0.01f) { hasLadder = true; break; }

            if (list == null || list.Count == 0 || !hasLadder)
            {
                campaign.adventures = BuildLadder();
                Debug.Log($"[AdventureFactory] Sinh ladder {campaign.adventures.Count} ải (0★→10★, luân phiên 9 vùng).");
            }
        }

        static List<AdventureData> BuildLadder()
        {
            var list = new List<AdventureData>();
            AdventureData prev = null;
            // 0, 0.5, 1, ... 10  → 21 mốc.
            for (int i = 0; i <= 20; i++)
            {
                float star = i * 0.5f;
                string region = Regions[i % Regions.Length];

                var a = ScriptableObject.CreateInstance<AdventureData>();
                a.adventureName = $"Ải {star:0.#} Sao";     // duy nhất theo sao
                a.region = region;
                a.type = AdventureType.World;
                a.starDifficulty = star;
                // LUÔN 12 chặng cho mọi ải (0★ hay 10★) — độ khó do SỨC ĐỊCH, không do độ dài.
                a.columns = 12;
                a.requiredChampionStar = 1;                 // KHÔNG gate theo sao champion (ladder-only)
                a.prerequisite = prev;                      // phải qua ải sao ngay trước
                a.rewardMultiplier = 1f + star * 0.15f;     // sao cao thưởng nhiều
                list.Add(a);
                prev = a;
            }
            return list;
        }
    }
}