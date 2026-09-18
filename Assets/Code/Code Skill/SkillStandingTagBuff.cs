using UnityEngine;
using LoRClone.Data;
using LoRClone.Model;

namespace LoRClone.Skills
{
    /// <summary>
    /// SƯƠNG ĐÊM (Viego) — đăng ký 1 buff AURA persistent theo tag. Buff áp cho:
    ///   • unit có tag đang trên sân NGAY khi skill chạy;
    ///   • MỌI unit có tag ra sân sau này — kể cả bản HỒI SINH (Taarosh/Harrowing) → "dù đã chết".
    /// Unit mang tag của chính champion (VD Viego có tag Mist) cũng tự hưởng ké.
    ///
    /// SETUP: Create → LoRClone/Skills/Standing Tag Buff. Gán vào ability (VD WhenPlayed,
    /// hoặc OnAllyDeath để mỗi Mist chết lại +1/+1 vĩnh viễn cho cả bầy).
    /// </summary>
    [CreateAssetMenu(menuName = "LoRClone/Skills/Standing Tag Buff", fileName = "Skill_StandingTagBuff")]
    public class SkillStandingTagBuff : SkillData
    {
        [Header("Tag & lượng buff (persistent)")]
        public string tag = "Mist";
        [Min(0)] public int attackBuff = 1;
        [Min(0)] public int healthBuff = 1;

        [Header("Keyword kèm theo (tùy chọn)")]
        public bool grantKeyword = false;
        public KeywordType keyword = KeywordType.Barrier;

        [Header("Phe")]
        [Tooltip("true = áp cho phe địch (hiếm). false = phe người dùng skill.")]
        public bool forOpponent = false;

        public override void Execute(CardModel source, GameContext ctx, int p1 = -1, int p2 = -1, int p3 = -1)
        {
            if (ctx?.controller == null)
            {
                Debug.LogWarning("[SkillStandingTagBuff] ctx.controller null — skill không thể chạy ngoài GameController.");
                return;
            }
            int atk = p1 >= 0 ? p1 : attackBuff;
            int hp  = p2 >= 0 ? p2 : healthBuff;
            bool forPlayer = forOpponent ? !source.belongsToPlayer : source.belongsToPlayer;
            ctx.controller.AddStandingTagBuff(forPlayer, tag, atk, hp, grantKeyword ? keyword : (KeywordType?)null);
        }
    }
}
