using System.Collections.Generic;
using UnityEngine;

namespace LoRClone.Data
{
    /// <summary>
    /// Cả THÁP — danh sách tầng theo thứ tự. Tầng i mở khi thắng tầng i-1.
    ///
    /// KHO bài Leo Tháp (PvZ) = starterCards + rewardCards của các tầng ĐÃ THẮNG.
    /// Khi vào tầng, người chơi chọn deckPickCount lá từ kho này.
    ///
    /// SETUP: Create → LoRClone → Campaign, kéo các LevelData vào levels theo thứ tự
    /// (phần tử 0 = tầng 1). Gán vào LobbyMenuView.campaign (nút LEO THÁP tự hiện)
    /// và DeckBuilderView.campaign (khóa card quà trong Xưởng Deck).
    ///
    /// File này PHẢI giữ tên CampaignData.cs (trùng tên class).
    /// </summary>
    [CreateAssetMenu(menuName = "LoRClone/Campaign", fileName = "Campaign")]
    public class CampaignData : ScriptableObject
    {
        public string campaignName = "Leo Tháp";

        [Tooltip("Bài khởi đầu — LUÔN có trong kho ngay từ tầng 1 (vd chỉ 1 lá).\n" +
                 "Thắng các tầng sẽ cộng thêm rewardCards vào kho.")]
        public List<CardData> starterCards = new List<CardData>();

        [Tooltip("Tối đa số bản sao 1 lá khi chọn bài vào tầng.")]
        [Min(1)] public int maxCopiesPerCard = 3;

        [Header("Con Đường Anh Hùng — Champion (Mốc 3)")]
        [Tooltip("Danh sách champion để chọn đầu hành trình (bản đồ nhánh).\n" +
                 "CÓ champion → chọn champion → dùng starterDeck cố định cả run.\n" +
                 "TRỐNG → map chạy kiểu chọn bài từng trận như cũ.")]
        public List<ChampionData> champions = new List<ChampionData>();

        [Tooltip("Mốc 4: kho Cổ Vật (Relic) — nhặt sau Elite/Boss. Trống = không có relic.")]
        public List<RelicData> relicPool = new List<RelicData>();

        [Tooltip("Mốc 14: LÁ BÀI hiếm có thể rơi từ RƯƠNG (mở khoá ở Xưởng Deck). Trống = rương chỉ rơi Lõi/Trang bị.")]
        public List<CardData> chestCardPool = new List<CardData>();

        [Tooltip("Giai đoạn 1: CHAMPION POWER (kỹ năng chủ động trong trận). Mỗi asset gán championName trùng tên tướng.\n" +
                 "Tướng không có power → không hiện nút. Trống = không tướng nào có power.")]
        public List<ChampionPowerData> championPowers = new List<ChampionPowerData>();

        [Tooltip("CORE trang bị: THƯ VIỆN TRANG BỊ per-card (CardItem + SkillData). Có gán → RandomOf bốc từ đây\n" +
                 "(kèm SkillData + championEquip cho trang bị champion). KHO DUY NHẤT để thiết kế trang bị.")]
        public CardItemLibrary cardItemLibrary;

        [Tooltip("Sức Mạnh (buff) TỰ THÊM — cộng vào kho có sẵn khi roll lựa chọn lúc thắng.\n" +
                 "Mỗi phần tử: tên, mô tả, +ATK, +HP, +mana, +nexus. Trống = chỉ dùng kho mặc định.")]
        public List<RunPower> powerPool = new List<RunPower>();

        [Tooltip("POWER (Lõi vận hành) — hệ cốt lõi PoC, ĐỔI cách vận hành trận (không buff stat).\n" +
                 "Node Power / Shop bốc từ đây. Trống = không có power để nhặt/mua.")]
        public List<PowerData> powerCores = new List<PowerData>();

        public List<LevelData> levels = new List<LevelData>();

        [Header("M1 — Ải (Adventures). Kéo AdventureData vào đây. Trống = dùng 3 độ khó cũ.")]
        public List<AdventureData> adventures = new List<AdventureData>();

        [Header("★ LUẬT TRẬN / BOSS POWER cho ẢI AUTO (không cần AdventureData) ★")]
        [Tooltip("Boss auto bốc NGẪU NHIÊN (cố định theo ải) 1 EncounterModifier từ đây. Trống = dùng boss power code mặc định.")]
        public List<EncounterModifier> bossModifierPool = new List<EncounterModifier>();
        [Tooltip("MiniBoss auto bốc 1 từ đây. Trống = mặc định.")]
        public List<EncounterModifier> miniBossModifierPool = new List<EncounterModifier>();
        [Tooltip("Elite auto bốc 1 từ đây. Trống = không luật riêng cho Elite (chỉ trang bị baseline).")]
        public List<EncounterModifier> eliteModifierPool = new List<EncounterModifier>();
        [Tooltip("Luật áp MỌI trận trong ải auto (special rule). Bốc 1 từ đây. Trống = không có.")]
        public List<EncounterModifier> adventureSpecialRules = new List<EncounterModifier>();
    }
}