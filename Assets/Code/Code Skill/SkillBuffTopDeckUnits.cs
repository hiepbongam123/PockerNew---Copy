using UnityEngine;
using LoRClone.Data;
using LoRClone.Model;

namespace LoRClone.Skills
{
    /// <summary>
    /// Skill: buff ATK/HP cho N unit GẦN ĐỈNH DECK NHẤT của chủ sở hữu
    /// (mặc định 3 — "grant the top 3 units in your deck +X|+Y" như LoR).
    ///
    /// Cách hoạt động:
    ///   Duyệt deck từ lá trên cùng (deck[0]) xuống, buff N unit đầu tiên gặp được
    ///   (bỏ qua spell/equipment xen giữa). Buff bám theo CardModel trong deck —
    ///   khi lá được rút lên tay / summon ra sân, stats mới tự hiển thị
    ///   (CardView đọc currentAttack/currentHealth runtime).
    ///
    /// ══ SETUP TRONG INSPECTOR ══
    ///   1. Create → LoRClone/Skills/Buff Top Deck Units
    ///   2. Đặt attackBuff / healthBuff / count
    ///   3. Gán vào CardData.abilities của card gốc:
    ///        trigger = WhenPlayed (hoặc trigger khác)
    ///        effect  = asset này
    ///        p1 = attackBuff override, p2 = healthBuff override, p3 = count override
    ///        temporaryBuff = false (grant vĩnh viễn — đúng như LoR)
    ///   4. Không cần target → AI enemy dùng được ngay.
    ///
    /// ══ LƯU Ý / RỦI RO ══
    ///   • Deck không bị xáo trộn, thứ tự giữ nguyên — chỉ stats thay đổi.
    ///   • Ít hơn N unit trong deck → buff số unit tìm được, không lỗi.
    ///   • Buff mất nếu unit bị Recall (RecallReset xóa buff) — đúng cơ chế LoR.
    ///   • RecordSkillBuff được gọi → UI hiển thị nguồn buff khi inspect card.
    /// </summary>
    [CreateAssetMenu(menuName = "LoRClone/Skills/Buff Top Deck Units", fileName = "Skill_BuffTopDeckUnits")]
    public class SkillBuffTopDeckUnits : SkillData
    {
        [Header("Buff Amount")]
        [Tooltip("ATK buff cho mỗi unit. Override bằng p1 (-1 = dùng giá trị này).")]
        public int attackBuff = 1;

        [Tooltip("HP buff cho mỗi unit. Override bằng p2 (-1 = dùng giá trị này).")]
        public int healthBuff = 1;

        [Header("Số unit gần đỉnh deck nhất được buff")]
        [Tooltip("Số unit tính từ đỉnh deck xuống. Override bằng p3 (-1 = dùng giá trị này).")]
        [Min(1)]
        public int count = 3;

        public override void Execute(CardModel source, GameContext ctx,
                                     int p1 = -1, int p2 = -1, int p3 = -1)
        {
            int atkBuff = (p1 >= 0) ? p1 : attackBuff;
            int hpBuff = (p2 >= 0) ? p2 : healthBuff;
            int buffCount = (p3 >= 0) ? p3 : count;

            if (atkBuff == 0 && hpBuff == 0) return;

            var deck = ctx.owner.deck;
            if (deck.Count == 0)
            {
                Debug.Log($"[SkillBuffTopDeckUnits] '{source.data.cardName}': deck rỗng — bỏ qua.");
                return;
            }

            // Duyệt từ đỉnh deck (deck[0]) xuống, buff N unit đầu tiên
            int buffed = 0;
            for (int i = 0; i < deck.Count && buffed < buffCount; i++)
            {
                var c = deck[i];
                if (c.data.cardType != CardType.Unit) continue;

                if (atkBuff != 0) c.BuffAttack(atkBuff, ctx.temporaryBuff);
                if (hpBuff != 0) c.BuffHealth(hpBuff, ctx.temporaryBuff);
                c.RecordSkillBuff(source.data.cardName, source.data.artwork, atkBuff, hpBuff, ctx.temporaryBuff);
                buffed++;

                Debug.Log($"[SkillBuffTopDeckUnits] {c.data.cardName} (deck #{i}) được grant +{atkBuff}|+{hpBuff}.");
            }

            if (buffed == 0)
                Debug.Log($"[SkillBuffTopDeckUnits] '{source.data.cardName}': không có unit nào trong deck.");
        }
    }
}
