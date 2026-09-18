using System.Collections.Generic;
using UnityEngine;
using LoRClone.Data;
using LoRClone.Model;

namespace LoRClone.Skills
{
    /// <summary>
    /// BUFF THEO TAG — đa dụng, chọn vùng áp dụng (sân / tay / deck, tick tuỳ ý).
    /// Thay thế hoàn toàn Skill_BuffTag cũ.
    ///
    /// ══ DÙNG CHO ══
    ///   • Buff thường trên sân      → includeBoard ✔, hand/deck ✘, excludeSelf ✘
    ///   • Buff bài trên tay         → includeHand ✔ (VD "mọi Rồng trên tay +2/+0")
    ///   • MÀN SƯƠNG ĐEN (Viego)     → cả 3 vùng ✔, excludeSelf ✔, condition = WhenPlayed
    ///     "Each Encroaching Mist grants all allied Viegos and other Mists everywhere +1/+1."
    ///     KHÔNG tick oncePerRound → nhiều Sương trong 1 round vẫn tính đủ từng con.
    ///     Con ra sau KHÔNG nhận buff của con ra trước (đây là buff 1 lần lúc triệu hồi,
    ///     KHÔNG phải aura) → Viego cộng +1 mỗi con = "theo số đếm", không cấp số cộng.
    ///
    /// ══ GHI CHÚ BUFF TẠM ══
    ///   ResetTempEffects chỉ chạy cho lá TRÊN SÂN. Nên buff tạm chỉ áp cho lá trên sân;
    ///   lá ở tay/deck luôn nhận buff VĨNH VIỄN (nếu không sẽ kẹt buff không bao giờ reset).
    ///
    /// attackBuff/healthBuff cho phép SỐ ÂM → dùng làm debuff.
    /// Override: p1 = attackBuff, p2 = healthBuff.
    /// </summary>
    [CreateAssetMenu(menuName = "LoRClone/Skills/Buff Tag (All Zones)", fileName = "Skill_BuffTag")]
    public class SkillBuffTagEverywhere : SkillData
    {
        [Header("Tag & lượng buff (cho phép số âm = debuff)")]
        public string tag = "Mist";
        public int attackBuff = 1;
        public int healthBuff = 1;

        [Header("Vùng áp dụng — tick tuỳ ý")]
        [Tooltip("Bench + Battlefield.")]
        public bool includeBoard = true;
        [Tooltip("Bài trên tay của chủ skill.")]
        public bool includeHand = false;
        [Tooltip("Bài còn trong deck (chưa rút) — rút lên đã có sẵn buff.")]
        public bool includeDeck = false;

        [Header("Keyword kèm theo (tuỳ chọn)")]
        public bool grantKeyword = false;
        public KeywordType keyword = KeywordType.Barrier;

        [Header("Khác")]
        [Tooltip("true = KHÔNG buff chính lá gây hiệu ứng (bắt buộc cho Màn Sương Đen — 'other Mists').")]
        public bool excludeSelf = false;

        [Tooltip("true = tôn trọng cờ temporaryBuff của CardAbility (chỉ áp cho lá TRÊN SÂN).\n" +
                 "false = luôn buff vĩnh viễn.")]
        public bool allowTemporary = true;

        public override void Execute(CardModel source, GameContext ctx, int p1 = -1, int p2 = -1, int p3 = -1)
        {
            int atk = p1 >= 0 ? p1 : attackBuff;
            int hp  = p2 >= 0 ? p2 : healthBuff;
            if (atk == 0 && hp == 0 && !grantKeyword) return;

            var owner = ctx.owner;
            var targets = new List<CardModel>();
            if (includeBoard)
            {
                foreach (var c in owner.BenchCards()) targets.Add(c);
                foreach (var c in owner.BattlefieldCards()) targets.Add(c);
            }
            if (includeHand) foreach (var c in owner.hand) targets.Add(c);
            if (includeDeck) foreach (var c in owner.deck) targets.Add(c);

            bool tmpRequested = allowTemporary && ctx.temporaryBuff;
            int n = 0;

            foreach (var c in targets)
            {
                if (c == null) continue;
                if (excludeSelf && c == source) continue;
                if (!c.HasTag(tag)) continue;

                // Buff tạm CHỈ cho lá trên sân — lá ở tay/deck không bao giờ được ResetTempEffects.
                bool onBoard = c.location == CardLocation.OnBench
                            || c.location == CardLocation.OnBattlefield;
                bool tmp = tmpRequested && onBoard;

                if (atk != 0) c.BuffAttack(atk, tmp);
                if (hp  != 0) c.BuffHealth(hp, tmp);
                if (grantKeyword) c.GrantKeyword(keyword);
                c.RecordSkillBuff(source.data.cardName, source.data.artwork, atk, hp, tmp);
                n++;
            }

            Debug.Log($"[BuffTag] {source.data.cardName} → {atk:+#;-#;0}/{hp:+#;-#;0} cho {n} lá tag '{tag}' " +
                      $"(board={includeBoard}, hand={includeHand}, deck={includeDeck}, " +
                      $"trừ chính nó={excludeSelf}, tạm={tmpRequested}).");
        }
    }
}
