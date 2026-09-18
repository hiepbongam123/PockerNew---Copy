using UnityEngine;

namespace LoRClone.Data
{
    /// <summary>
    /// POWER (Lõi vận hành) — 1 trong 2 hệ cốt lõi PoC (Power + Item). KHÁC buff stat:
    /// Power ĐỔI CÁCH VẬN HÀNH trận (giảm mana lá đầu, +mana khởi đầu, hồi spell mana, +Nexus, reroll...).
    /// Cùng cơ chế Tinh Hồn — được ÁP qua các hàm Constellation* trong RunPowers (dùng chung hook GameController).
    ///
    /// Nhặt/mua giữa run (node Power / Shop) → CampaignRun.Powers (per-run, reset mỗi run).
    /// Nhiều Power CỘNG DỒN (vd 2 power -1 mana lá đầu = -2). Set các field = hiệu ứng muốn, để 0 = không có.
    ///
    /// SETUP: Create → LoRClone → Power. Kéo vào Campaign.powerCores để node/shop bốc.
    /// </summary>
    [CreateAssetMenu(menuName = "LoRClone/Power", fileName = "Power")]
    public class PowerData : ScriptableObject
    {
        public string powerName = "Power";
        [TextArea] public string description = "";
        public Sprite icon;
        [Tooltip("0 Thường · 1 Hiếm · 2 Sử Thi — chỉ để tô màu/định giá.")]
        public LoRClone.Model.ItemRarity rarity = LoRClone.Model.ItemRarity.Common;
        [Tooltip("Giá VÀNG khi bán ở Shop (0 = tự tính theo độ hiếm).")]
        public int goldCost = 0;

        [Header("HIỆU ỨNG VẬN HÀNH (đổi luật trận, KHÔNG buff stat toàn quân)")]
        [Tooltip("Giảm mana LÁ ĐẦU TIÊN chơi mỗi vòng. Cộng dồn nhiều power.")]
        public int firstCardManaDiscount = 0;
        [Tooltip("+mana KHỞI ĐẦU mỗi vòng (thêm mana ô).")]
        public int bonusStartMana = 0;
        [Tooltip("+máu Nexus khi vào trận.")]
        public int bonusNexus = 0;
        [Tooltip("Hồi ĐẦY spell mana mỗi đầu vòng.")]
        public bool refillSpellManaEachRound = false;
        [Tooltip("Lá UNIT ĐẦU TIÊN mỗi vòng +ATK/+HP (cộng dồn).")]
        public int firstUnitBuffAtk = 0;
        public int firstUnitBuffHp = 0;
        [Tooltip("Thêm 1 lượt ĐỔI (reroll) ở màn chọn card/trang bị.")]
        public bool extraReroll = false;
        [Tooltip("Rút thêm N lá vào ĐẦU mỗi vòng.")]
        public int drawPerRound = 0;
        [Tooltip("+N spell mana mỗi đầu vòng (cộng thêm, khác 'hồi đầy').")]
        public int spellManaPerRound = 0;
        [Tooltip("MỌI lá UNIT giảm N mana (bền suốt trận, cộng dồn).")]
        public int unitCostDiscount = 0;
        [Tooltip("MỌI lá PHÉP (Spell) giảm N mana (bền suốt trận, cộng dồn).")]
        public int spellCostDiscount = 0;

        public bool IsMeaningful =>
            firstCardManaDiscount != 0 || bonusStartMana != 0 || bonusNexus != 0
            || refillSpellManaEachRound || firstUnitBuffAtk != 0 || firstUnitBuffHp != 0 || extraReroll
            || drawPerRound != 0 || spellManaPerRound != 0 || unitCostDiscount != 0 || spellCostDiscount != 0;

        /// <summary>Mô tả tự sinh nếu description trống — cho UI khỏi để trắng.</summary>
        public string AutoDescribe()
        {
            if (!string.IsNullOrEmpty(description)) return description;
            var s = new System.Text.StringBuilder();
            void Add(string t) { if (s.Length > 0) s.Append("\n"); s.Append(t); }
            if (firstCardManaDiscount != 0) Add($"Giảm {firstCardManaDiscount} mana lá đầu mỗi vòng");
            if (bonusStartMana != 0) Add($"+{bonusStartMana} mana khởi đầu");
            if (bonusNexus != 0) Add($"+{bonusNexus} máu Nexus");
            if (refillSpellManaEachRound) Add("Hồi đầy spell mana mỗi vòng");
            if (firstUnitBuffAtk != 0 || firstUnitBuffHp != 0) Add($"Lá unit đầu mỗi vòng +{firstUnitBuffAtk}|+{firstUnitBuffHp}");
            if (extraReroll) Add("+1 lượt đổi ở màn chọn");
            if (drawPerRound != 0) Add($"Rút thêm {drawPerRound} lá mỗi vòng");
            if (spellManaPerRound != 0) Add($"+{spellManaPerRound} spell mana mỗi vòng");
            if (unitCostDiscount != 0) Add($"Mọi lá Unit -{unitCostDiscount} mana");
            if (spellCostDiscount != 0) Add($"Mọi lá Phép -{spellCostDiscount} mana");
            return s.Length > 0 ? s.ToString() : "(chưa cấu hình hiệu ứng)";
        }
    }
}