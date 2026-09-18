using System.Collections.Generic;
using UnityEngine;

namespace LoRClone.Data
{
    public enum AdventureType { World, ChampionCampaign, Weekly, Monthly }

    /// <summary>
    /// M1 — MỘT "ẢI" kiểu Con Đường Anh Hùng (PoC). Thay mô hình 3 độ khó chung bằng
    /// từng ải có ĐỘ KHÓ SAO riêng + ĐỘ DÀI riêng + điều kiện mở khoá.
    ///
    /// SETUP: Create → LoRClone → Adventure. Kéo các ải vào CampaignData.adventures.
    /// Chưa tạo ải nào → game tự chạy chế độ 3 độ khó cũ (không vỡ).
    /// </summary>
    [CreateAssetMenu(menuName = "LoRClone/Adventure", fileName = "Adventure")]
    public class AdventureData : ScriptableObject
    {
        public string adventureName = "Ải mới";
        [Tooltip("Vùng/chủ đề — dùng cho tên hiển thị (M2: ảnh nền/boss theo vùng).")]
        public string region = "";
        public AdventureType type = AdventureType.World;

        [Header("Độ khó SAO — thay 3 bậc Thường/Khó/Ác Mộng")]
        [Tooltip("Cao = địch mạnh hơn + thưởng nhiều hơn. Ladder: 0★ → 10★ (bước 0.5).")]
        [Range(0f, 10f)] public float starDifficulty = 1.5f;

        [Header("Độ dài & cấu trúc")]
        [Tooltip("Số cột của bản đồ = ĐỘ DÀI ải. Ải ngắn 4–5, chuẩn 6–8, dài 9+.")]
        [Min(3)] public int columns = 6;

        [Header("(M2) Boss riêng theo ải — để trống thì dùng levels của CampaignData")]
        public LevelData miniBoss;    // boss GIỮA
        public LevelData finalBoss;   // boss CUỐI
        public LevelData[] battlePool;

        [Header("Luật trận (Encounter Modifier) — Boss modifier / độ khó kiểu PoC")]
        [Tooltip("A. Luật riêng CẢ ẢI — áp MỌI trận. Trống = không có.")]
        public EncounterModifier specialRule;
        [Tooltip("Luật trận MiniBoss (giữa). Trống = dùng buff boss mặc định.")]
        public EncounterModifier miniBossModifier;
        [Tooltip("Luật trận Boss CUỐI. Trống = dùng buff boss mặc định.")]
        public EncounterModifier finalBossModifier;
        [Tooltip("Kho luật trận ELITE — mỗi Elite bốc ngẫu nhiên 1. Trống = Elite không có luật riêng.")]
        public EncounterModifier[] eliteModifierPool;

        [Header("Mở khoá (như PoC)")]
        [Tooltip("Cần SAO champion tối thiểu để mở ải này.")]
        [Min(1)] public int requiredChampionStar = 1;
        [Tooltip("Phải qua ải này trước (M2 enforce đầy đủ).")]
        public AdventureData prerequisite;

        [Header("Thưởng")]
        [Tooltip("Thưởng runtime = base × starDifficulty × số foe hạ × cái này.")]
        public float rewardMultiplier = 1f;

        // Dùng chữ "Sao" thay glyph ★ (font TMP thiếu ★ → hiện ô vuông). "3.5 Sao".
        public string StarLabel() => $"{starDifficulty:0.#} Sao";
    }

    /// <summary>
    /// ENCOUNTER MODIFIER (Luật trận / Boss modifier) kiểu PoC — mục C/D/F khung độ khó.
    /// Đổi CÁCH VẬN HÀNH 1 trận theo hướng khó hơn (KHÔNG buff toàn quân player).
    /// Gắn ở AdventureData: specialRule (cả ải) · miniBoss/finalBossModifier · eliteModifierPool.
    /// 2 nhóm: ONE-TIME (đầu trận) + PER-ROUND (địch ramp dần). SETUP: Create → LoRClone → Encounter Modifier.
    /// </summary>
    [CreateAssetMenu(menuName = "LoRClone/Encounter Modifier", fileName = "Modifier")]
    public class EncounterModifier : ScriptableObject
    {
        public string modName = "Luật trận";
        [TextArea] public string description = "";

        [Header("MỘT LẦN đầu trận (phía ĐỊCH)")]
        [Tooltip("+máu Nexus địch (hiện trên thanh Nexus — minh bạch).")]
        public int enemyNexusBonus = 0;
        [Tooltip("POWER ĐẶC BIỆT: mỗi đơn vị địch có THÊM N trang bị (NGOÀI trang bị cơ bản theo độ khó).")]
        public int enemyBonusItemSlots = 0;
        [Tooltip("Độ hiếm TỐI THIỂU của trang bị THÊM (0 Thường · 1 Hiếm · 2 Sử Thi).")]
        public LoRClone.Model.ItemRarity enemyBonusItemMinRarity = LoRClone.Model.ItemRarity.Common;
        [Tooltip("Địch rút thêm N lá ngay đầu trận.")]
        public int enemyExtraDrawStart = 0;
        [Tooltip("+N spell mana địch đầu trận.")]
        public int enemyBonusSpellMana = 0;
        [Tooltip("Triệu hồi sẵn cho ĐỊCH các đơn vị này (đặt lên ghế chờ).")]
        public CardData[] enemyStartUnits;

        [Header("MỖI VÒNG (địch ramp dần — cơ chế minh bạch)")]
        [Tooltip("Mỗi vòng địch rút thêm N lá.")]
        public int enemyDrawPerRound = 0;
        [Tooltip("Mỗi vòng địch +N spell mana.")]
        public int enemyManaPerRound = 0;

        [Header("★ BOSS POWER (dấu ấn riêng của boss/ải) ★")]
        [Tooltip("Tên power đặc trưng — hiện to ở preview/pause. Trống = không có boss power.")]
        public string bossPowerName = "";
        [TextArea] public string bossPowerDesc = "";
        [Tooltip("Đầu trận: MỌI quân địch nhận keyword này (vd Hồi Phục/Tri Thức/Hủy Diệt/Bảo Hộ).")]
        public bool bossGrantKeyword = false;
        public KeywordType bossKeyword = KeywordType.Regeneration;
        [Tooltip("Mỗi vòng: hồi N máu Nexus địch.")]
        public int bossHealNexusPerRound = 0;
        [Tooltip("Mỗi vòng: quân địch TRÊN SÂN +ATK/+HP (ramp có tên — minh bạch, KHÁC buff vô cớ).")]
        public int bossBoardAtkPerRound = 0;
        public int bossBoardHpPerRound = 0;
        [Tooltip("Mỗi vòng: triệu hồi 1 token này lên ghế chờ địch (Quân Đoàn).")]
        public CardData bossSummonPerRound;

        public bool HasBossPower =>
            bossGrantKeyword || bossHealNexusPerRound != 0
            || bossBoardAtkPerRound != 0 || bossBoardHpPerRound != 0
            || bossSummonPerRound != null || !string.IsNullOrEmpty(bossPowerName);

        public bool HasOneTime =>
            enemyNexusBonus != 0 || enemyBonusItemSlots != 0
            || enemyExtraDrawStart != 0 || enemyBonusSpellMana != 0
            || (enemyStartUnits != null && enemyStartUnits.Length > 0);
        public bool HasPerRound =>
            enemyDrawPerRound != 0 || enemyManaPerRound != 0
            || bossHealNexusPerRound != 0 || bossBoardAtkPerRound != 0
            || bossBoardHpPerRound != 0 || bossSummonPerRound != null;

        // Mô tả riêng BOSS POWER (in đậm/nổi bật ở preview). Trống nếu không có.
        public string BossPowerText()
        {
            if (!HasBossPower) return "";
            if (!string.IsNullOrEmpty(bossPowerDesc)) return bossPowerDesc;
            var s = new System.Text.StringBuilder();
            void Add(string t) { if (s.Length > 0) s.Append(" · "); s.Append(t); }
            if (bossGrantKeyword) Add($"Toàn quân địch có {KeywordVN(bossKeyword)}");
            if (bossHealNexusPerRound != 0) Add($"Mỗi vòng hồi {bossHealNexusPerRound} Nexus");
            if (bossBoardAtkPerRound != 0 || bossBoardHpPerRound != 0) Add($"Mỗi vòng quân trên sân +{bossBoardAtkPerRound}|+{bossBoardHpPerRound}");
            if (bossSummonPerRound != null) Add($"Mỗi vòng triệu hồi {bossSummonPerRound.cardName}");
            return s.ToString();
        }

        static string KeywordVN(KeywordType k) => k switch
        {
            KeywordType.Regeneration => "Hồi Phục",
            KeywordType.Tough => "Tri Thức",
            KeywordType.Overwhelm => "Hủy Diệt",
            KeywordType.Barrier => "Bảo Hộ",
            KeywordType.QuickAttack => "Săn Bắn",
            KeywordType.Fearsome => "Hư Vô",
            KeywordType.Lifesteal => "Trù Phú",
            _ => k.ToString(),
        };

        public string AutoDescribe()
        {
            if (!string.IsNullOrEmpty(description)) return description;
            var s = new System.Text.StringBuilder();
            void Add(string t) { if (s.Length > 0) s.Append("\n"); s.Append(t); }
            string bp = BossPowerText();
            if (!string.IsNullOrEmpty(bp)) Add($"<color=#F0C24E>★ {(!string.IsNullOrEmpty(bossPowerName) ? bossPowerName : "Boss Power")}: {bp}</color>");
            if (enemyNexusBonus != 0) Add($"Địch +{enemyNexusBonus} Nexus");
            if (enemyBonusItemSlots != 0)
            {
                string rr = enemyBonusItemMinRarity == LoRClone.Model.ItemRarity.Epic ? "Sử Thi+"
                          : enemyBonusItemMinRarity == LoRClone.Model.ItemRarity.Rare ? "Hiếm+" : "";
                Add($"Địch có THÊM {enemyBonusItemSlots} trang bị {rr}".TrimEnd());
            }
            if (enemyExtraDrawStart != 0) Add($"Địch rút thêm {enemyExtraDrawStart} lá đầu trận");
            if (enemyBonusSpellMana != 0) Add($"Địch +{enemyBonusSpellMana} spell mana");
            if (enemyStartUnits != null && enemyStartUnits.Length > 0) Add($"Địch có sẵn {enemyStartUnits.Length} đơn vị");
            if (enemyDrawPerRound != 0) Add($"Mỗi vòng địch rút thêm {enemyDrawPerRound} lá");
            if (enemyManaPerRound != 0) Add($"Mỗi vòng địch +{enemyManaPerRound} spell mana");
            return s.Length > 0 ? s.ToString() : "(chưa cấu hình)";
        }

#if UNITY_EDITOR
        // TIỆN ÍCH EDITOR — tạo sẵn 3 modifier mẫu để test nhanh (menu LoRClone → Create Sample Encounter Modifiers).
        [UnityEditor.MenuItem("LoRClone/Create Sample Encounter Modifiers")]
        static void CreateSamples()
        {
            const string dir = "Assets/_SampleModifiers";
            if (!UnityEditor.AssetDatabase.IsValidFolder(dir))
                UnityEditor.AssetDatabase.CreateFolder("Assets", "_SampleModifiers");

            void Make(string fileName, System.Action<EncounterModifier> setup)
            {
                string path = $"{dir}/{fileName}.asset";
                var existing = UnityEditor.AssetDatabase.LoadAssetAtPath<EncounterModifier>(path);
                bool isNew = existing == null;
                var m = isNew ? CreateInstance<EncounterModifier>() : existing;
                setup(m);
                if (isNew) UnityEditor.AssetDatabase.CreateAsset(m, path);
                else UnityEditor.EditorUtility.SetDirty(m);
            }

            Make("SpecialRule_TrieuTap", m => { m.modName = "Triệu Tập Không Ngừng"; m.enemyManaPerRound = 1; m.enemyDrawPerRound = 1; });
            Make("Elite_TinhNhue", m => { m.modName = "Tinh Nhuệ Vũ Trang"; m.enemyBonusItemSlots = 1; m.enemyBonusItemMinRarity = LoRClone.Model.ItemRarity.Rare; });
            Make("Boss_ConThinhNo", m =>
            {
                m.modName = "Cơn Thịnh Nộ Của Boss";
                m.enemyNexusBonus = 12;
                m.enemyBonusItemSlots = 2; m.enemyBonusItemMinRarity = LoRClone.Model.ItemRarity.Rare;
                m.enemyExtraDrawStart = 1; m.enemyDrawPerRound = 1;
                // ★ Boss power: toàn quân địch Hồi Phục + mỗi vòng hồi Nexus.
                m.bossPowerName = "Bất Diệt"; m.bossGrantKeyword = true; m.bossKeyword = KeywordType.Regeneration;
                m.bossHealNexusPerRound = 3;
                m.bossPowerDesc = "Toàn quân địch có Hồi Phục · mỗi vòng boss hồi 3 Nexus.";
            });

            UnityEditor.AssetDatabase.SaveAssets(); UnityEditor.AssetDatabase.Refresh();
            Debug.Log($"[LoRClone] Đã tạo 3 EncounterModifier mẫu trong {dir}. Kéo vào AdventureData.");
        }
#endif
    }
}