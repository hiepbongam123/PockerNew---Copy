using UnityEngine;
using System.Collections.Generic;
using LoRClone.Model;   // KeywordType

namespace LoRClone.Data
{
    /// <summary>
    /// MỐC 4 — CỔ VẬT (Relic) của hành trình.
    /// Áp cho CẢ run: cộng buff toàn quân + KHẢ NĂNG ĐẶC BIỆT (keyword/skill) mỗi trận.
    /// </summary>
    [CreateAssetMenu(menuName = "LoRClone/Relic", fileName = "Relic")]
    public class RelicData : ScriptableObject
    {
        public string relicName = "Cổ Vật";
        [TextArea(2, 4)] public string description = "";
        public Sprite icon;

        [Header("Hiệu ứng chỉ số (áp đầu mỗi trận)")]
        public int alliesAtk;
        public int alliesHp;
        public int bonusMana;
        public int bonusNexus;

        [Header("KHẢ NĂNG ĐẶC BIỆT THẬT — cấp cho CHAMPION qua engine (KHUYÊN DÙNG)")]
        [Tooltip("Keyword cấp cho champion khi vào trận: Bảo Hộ (Barrier), Trù Phú (Lifesteal),\n" +
                 "Huỷ Diệt (Overwhelm), Săn Bắn (QuickAttack)... Chạy Y NHƯ keyword của card thật.")]
        public List<KeywordType> grantKeywords = new List<KeywordType>();
        [Tooltip("Skill (SkillData asset có sẵn) chạy theo TRIGGER của champion — tái dùng đúng hệ skill của game.\n" +
                 "Nên dùng skill KHÔNG cần chọn mục tiêu: BuffSelf, DamageNexus, DrawCard, GainSpellMana...")]
        public List<SkillData> skills = new List<SkillData>();

        [Header("(Cũ) Bảng RunEffects — để TRỐNG nếu đã dùng grantKeywords/skills ở trên")]
        public List<RelicEffect> effects = new List<RelicEffect>();
    }
}