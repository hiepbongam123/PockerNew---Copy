using System.Collections.Generic;
using UnityEngine;

namespace LoRClone.Data
{
    /// <summary>
    /// MỐC 14 — RƯƠNG BÁU: thưởng LẦN ĐẦU vượt mỗi ải. 4 bậc (Thường/Hiếm/Tuyệt Phẩm/Huyền Thoại).
    /// Mở rương → 1 trong: LÕI · TRANG BỊ (mở sở hữu) · LÁ BÀI hiếm (mở khoá Xưởng Deck).
    /// TẤT CẢ TỈ LỆ chỉnh ở BẢNG dưới — không cần sửa logic.
    /// </summary>
    public enum ChestTier { Common = 0, Rare = 1, Epic = 2, Legend = 3 }

    public static class ChestSystem
    {
        // ── BẢNG TỈ LỆ BẬC RƯƠNG theo SAO ải (chỉnh thoải mái). Mỗi hàng = trọng số [Thường,Hiếm,Tuyệt,Huyền] ──
        //   tổng không cần = 100, hệ thống tự chuẩn hoá.
        struct Row { public float maxStar; public int[] w; }
        static readonly Row[] TierTable =
        {
            new Row { maxStar = 2f,  w = new[] { 70, 26,  4,  0 } },  // 0–2★
            new Row { maxStar = 4f,  w = new[] { 45, 38, 15,  2 } },  // 2.5–4★
            new Row { maxStar = 7f,  w = new[] { 25, 40, 28,  7 } },  // 4.5–7★
            new Row { maxStar = 99f, w = new[] { 10, 33, 40, 17 } },  // 7.5★+
        };

        // ── BẢNG NỘI DUNG mỗi bậc: trọng số [Lõi, Trang bị, Lá bài] + khoảng LÕI (min,max) ──
        struct Loot { public int[] kindW; public int coreMin, coreMax; }
        static readonly Loot[] LootTable =
        {
            new Loot { kindW = new[] { 70, 25,  5 }, coreMin = 30,  coreMax = 60  }, // Thường
            new Loot { kindW = new[] { 55, 33, 12 }, coreMin = 60,  coreMax = 120 }, // Hiếm
            new Loot { kindW = new[] { 40, 40, 20 }, coreMin = 120, coreMax = 220 }, // Tuyệt Phẩm
            new Loot { kindW = new[] { 25, 40, 35 }, coreMin = 250, coreMax = 400 }, // Huyền Thoại
        };

        public static string TierName(ChestTier t) => t switch
        {
            ChestTier.Common => "Thường",
            ChestTier.Rare => "Hiếm",
            ChestTier.Epic => "Tuyệt Phẩm",
            ChestTier.Legend => "Huyền Thoại",
            _ => "Rương",
        };

        // Màu theo bậc (hex, cho UI).
        public static string TierHex(ChestTier t) => t switch
        {
            ChestTier.Common => "9FB0C4",
            ChestTier.Rare => "5AC8E0",
            ChestTier.Epic => "B080E0",
            ChestTier.Legend => "F0C24E",
            _ => "9FB0C4",
        };

        static int WeightedPick(int[] w)
        {
            int total = 0;
            foreach (int x in w) total += Mathf.Max(0, x);
            if (total <= 0) return 0;
            int r = Random.Range(0, total);
            for (int i = 0; i < w.Length; i++) { r -= Mathf.Max(0, w[i]); if (r < 0) return i; }
            return w.Length - 1;
        }

        /// <summary>Bậc rương ngẫu nhiên theo sao ải.</summary>
        public static ChestTier RollTier(float star)
        {
            foreach (var row in TierTable)
                if (star <= row.maxStar) return (ChestTier)WeightedPick(row.w);
            return (ChestTier)WeightedPick(TierTable[TierTable.Length - 1].w);
        }

        public enum RewardKind { Cores, Item, Card }
        public class ChestReward
        {
            public RewardKind kind;
            public int cores;
            public CardItemDef item;   // trang bị champion (CardItemLibrary, championEquip)
            public CardData card;
        }

        /// <summary>
        /// MỞ rương: roll nội dung theo bậc rồi ÁP luôn (cộng Lõi / cho sở hữu item / mở khoá card).
        /// Trả về reward để UI hiện. Item/Card hết nguồn → quay về Lõi.
        /// </summary>
        public static ChestReward Open(ChestTier tier, CampaignData campaign)
        {
            var loot = LootTable[(int)tier];
            int kind = WeightedPick(loot.kindW); // 0 Lõi · 1 Item · 2 Card
            var rw = new ChestReward();

            if (kind == 1) // TRANG BỊ CHAMPION: chọn món championEquip CHƯA sở hữu trong CardItemLibrary
            {
                var pool = new List<CardItemDef>();
                var lib = campaign != null ? campaign.cardItemLibrary : null;
                if (lib != null && lib.items != null)
                    foreach (var it in lib.items)
                        if (it != null && it.championEquip && !ProgressStore.IsItemOwned(it.itemName)) pool.Add(it);
                if (pool.Count > 0)
                {
                    var it = pool[Random.Range(0, pool.Count)];
                    ProgressStore.GrantItem(it.itemName);
                    rw.kind = RewardKind.Item; rw.item = it; return rw;
                }
                kind = 0; // hết item → Lõi
            }
            else if (kind == 2) // LÁ BÀI: chọn card CHƯA mở khoá trong chestCardPool
            {
                var pool = new List<CardData>();
                if (campaign != null && campaign.chestCardPool != null)
                    foreach (var cd in campaign.chestCardPool)
                        if (cd != null && !ProgressStore.IsCardUnlocked(cd.cardName)) pool.Add(cd);
                if (pool.Count > 0)
                {
                    var cd = pool[Random.Range(0, pool.Count)];
                    ProgressStore.UnlockCard(cd.cardName);
                    rw.kind = RewardKind.Card; rw.card = cd; return rw;
                }
                kind = 0; // hết card → Lõi
            }

            // LÕI (mặc định / fallback)
            int cores = Random.Range(loot.coreMin, loot.coreMax + 1);
            ProgressStore.AddCores(cores);
            rw.kind = RewardKind.Cores; rw.cores = cores;
            return rw;
        }
    }
}