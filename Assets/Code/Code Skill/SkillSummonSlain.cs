using System.Collections.Generic;
using UnityEngine;
using LoRClone.Data;
using LoRClone.Model;

namespace LoRClone.Skills
{
    /// <summary>
    /// Hồi sinh unit đã chết cả ván thành BẢN SAO (tùy chọn Ephemeral). Gộp 2 lá LoR:
    ///
    ///   • TAAROSH        → sourceType = SlainByMe, followersOnly = true, grantEphemeral = true,
    ///                      fillAvailableSlots = true, summonToBattlefield = true, pickMode = Strongest.
    ///                      Gán OnAttack + oncePerRound trên CardAbility ("lần đầu tấn công mỗi vòng").
    ///
    ///   • THE HARROWING  → sourceType = FallenAllies, followersOnly = false, grantEphemeral = true,
    ///                      fillAvailableSlots = false, count = 6, summonToBattlefield = false,
    ///                      pickMode = Strongest. Gán vào 1 Spell (Slow).
    ///
    /// Nguồn dữ liệu:
    ///   SlainByMe    = GameController.SlainOf(causer)  — unit bên này ĐÃ HẠ (combat/spell/skill/obliterate).
    ///   FallenAllies = GameController.FallenOf(causer) — unit của bên này ĐÃ TỬ TRẬN (mọi nguyên nhân).
    ///
    /// KHÔNG tiêu sổ: sổ là lịch sử vĩnh viễn (đúng LoR) → mỗi lần kích hoạt đọc lại snapshot
    /// đã sort, hồi cùng nhóm mạnh nhất. Trong 1 lần kích: đi hết list (không lặp lại cùng entry).
    /// </summary>
    [CreateAssetMenu(menuName = "LoRClone/Skills/Summon Slain", fileName = "Skill_SummonSlain")]
    public class SkillSummonSlain : SkillData
    {
        public enum SlainSource { SlainByMe, FallenAllies }
        public enum SlainPick { Strongest, Random }

        [Header("Nguồn")]
        [Tooltip("SlainByMe = unit BẠN đã hạ (Taarosh). FallenAllies = đồng minh BẠN đã tử trận (Harrowing).")]
        public SlainSource sourceType = SlainSource.SlainByMe;

        [Header("Cách chọn")]
        [Tooltip("Strongest = theo Power (baseAttack) cao nhất. Random = ngẫu nhiên.")]
        public SlainPick pickMode = SlainPick.Strongest;

        [Header("Lọc")]
        [Tooltip("true = chỉ follower (bỏ champion — canLevelUp). Taarosh = true, Harrowing = false.")]
        public bool followersOnly = false;

        [Header("Bản sao")]
        [Tooltip("true = cấp Ephemeral cho bản sao (cả 2 lá đều bật).")]
        public bool grantEphemeral = true;

        [Header("Số lượng")]
        [Tooltip("true = lấp HẾT ô trống nơi triệu hồi (Taarosh). false = dùng count/p1 (Harrowing = 6).")]
        public bool fillAvailableSlots = false;

        [Tooltip("Số bản tối đa khi fillAvailableSlots = false. Harrowing = 6. Override bằng p1.")]
        [Min(1)]
        public int count = 6;

        [Header("Vị trí")]
        [Tooltip("true = battlefield/tấn công (Taarosh). false = bench (Harrowing).")]
        public bool summonToBattlefield = false;

        public override void Execute(CardModel source, GameContext ctx, int p1 = -1, int p2 = -1, int p3 = -1)
        {
            if (ctx?.controller == null)
            {
                Debug.LogWarning("[SkillSummonSlain] ctx.controller null — skill không thể chạy ngoài GameController.");
                return;
            }

            bool causer = source.belongsToPlayer;
            var pool = (sourceType == SlainSource.SlainByMe)
                ? ctx.controller.SlainOf(causer)
                : ctx.controller.FallenOf(causer);

            if (pool == null || pool.Count == 0)
            {
                Debug.Log($"[SkillSummonSlain] '{source?.data?.cardName}': sổ ({sourceType}) rỗng.");
                return;
            }

            // Snapshot các entry hợp lệ (KHÔNG chạm sổ gốc).
            var eligible = new List<(CardData data, bool ownerIsPlayer)>();
            foreach (var e in pool)
            {
                if (e.data == null) continue;
                if (followersOnly && e.data.canLevelUp) continue;   // bỏ champion
                eligible.Add(e);
            }
            if (eligible.Count == 0) return;

            if (pickMode == SlainPick.Strongest)
                eligible.Sort((a, b) => b.data.baseAttack.CompareTo(a.data.baseAttack)); // Power giảm dần
            else
                Shuffle(eligible);

            int cap = fillAvailableSlots ? int.MaxValue : (p1 >= 0 ? p1 : count);
            int made = 0;
            foreach (var e in eligible)
            {
                if (made >= cap) break;
                var copy = ctx.controller.SummonUnit(e.data, causer, summonToBattlefield);
                if (copy == null) break;   // hết ô → dừng
                if (grantEphemeral) copy.GrantKeyword(KeywordType.Ephemeral);
                made++;
            }

            Debug.Log($"[SkillSummonSlain] '{source?.data?.cardName}': hồi {made} unit ({sourceType}, {pickMode}).");
        }

        static void Shuffle(List<(CardData data, bool ownerIsPlayer)> list)
        {
            // PvP: PHẢI dùng GameRNG (seed chung 2 máy) — KHÔNG dùng UnityEngine.Random (mỗi máy khác → DESYNC).
            for (int i = list.Count - 1; i > 0; i--)
            {
                int j = LoRClone.Controller.GameRNG.Range(0, i + 1);
                (list[i], list[j]) = (list[j], list[i]);
            }
        }
    }
}