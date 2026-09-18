using System.Collections.Generic;
using UnityEngine;
using LoRClone.Model;

namespace LoRClone.Data
{
    /// <summary>
    /// Sinh sẵn 1 DANH SÁCH CỔ VẬT phong phú (khỏi tạo asset tay) — để phần thưởng sau ải
    /// không quẩn quanh vài buff stat. Dùng đủ 3 nhánh: chỉ số + keyword thật + hiệu ứng BattleStart.
    ///
    /// Gọi EnsureRelics(campaign) TRƯỚC khi mở màn nhặt/relic tab. Chỉ THÊM cổ vật chưa có
    /// (dedupe theo tên) → không đụng cổ vật bạn tự tạo; muốn bỏ 3 cái cũ thì xoá thủ công trong relicPool.
    /// </summary>
    public static class RelicFactory
    {
        public const string Tag = "★"; // đánh dấu cổ vật do factory sinh (đầu tên)

        // ★ TẮT HỆ RELIC (Cổ Vật): false = KHÔNG tự sinh relic nữa → empty relicPool trong Inspector sẽ DÍNH,
        //   và không còn cổ vật nào xuất hiện. Muốn bật lại relic: đổi thành true.
        public static bool Enabled = false;

        public static void EnsureRelics(CampaignData campaign)
        {
            if (!Enabled) return;              // relic đã tắt → không đắp pool
            if (campaign == null) return;
            if (campaign.relicPool == null) campaign.relicPool = new List<RelicData>();

            // Đã có cổ vật factory (nhận diện qua Tag) → khỏi sinh lại.
            foreach (var r in campaign.relicPool)
                if (r != null && !string.IsNullOrEmpty(r.relicName) && r.relicName.StartsWith(Tag))
                    return;

            var made = Build();
            var have = new HashSet<string>();
            foreach (var r in campaign.relicPool) if (r != null) have.Add(r.relicName);
            foreach (var r in made) if (!have.Contains(r.relicName)) campaign.relicPool.Add(r);

            Debug.Log($"[RelicFactory] Bổ sung {made.Count} cổ vật (tổng {campaign.relicPool.Count}).");
        }

        static List<RelicData> Build()
        {
            var list = new List<RelicData>();

            // ── Nhánh CHỈ SỐ (áp đầu mỗi trận) ───────────────────────
            list.Add(R("Lưỡi Rìu Cổ", "Toàn quân +3 Công mỗi trận.", 3, 0, 0, 0));
            list.Add(R("Giáp Thần Thoại", "Toàn quân +4 Máu mỗi trận.", 0, 4, 0, 0));
            list.Add(R("Ngọc Mana", "+1 mana khởi đầu mỗi trận.", 0, 0, 1, 0));
            list.Add(R("Tim Nexus", "+6 máu Nexus mỗi trận.", 0, 0, 0, 6));
            list.Add(R("Cờ Xung Trận", "Toàn quân +2|+1 mỗi trận.", 2, 1, 0, 0));

            // ── Nhánh KEYWORD THẬT (cấp cho champion + toàn quân) ─────
            list.Add(R("Ấn Bảo Hộ", "Champion nhận Bảo Hộ đầu mỗi trận.", 0, 0, 0, 0,
                new[] { KeywordType.Barrier }));
            list.Add(R("Nanh Hút Máu", "Champion nhận Trù Phú (hút máu) đầu mỗi trận.", 0, 0, 0, 0,
                new[] { KeywordType.Lifesteal }));
            list.Add(R("Lư Tái Sinh", "Champion nhận Hồi Phục đầu mỗi trận.", 0, 0, 0, 0,
                new[] { KeywordType.Regeneration }));
            list.Add(R("Khiên Ma Thuật", "Champion nhận Hoà Hợp (chặn 1 đòn) đầu mỗi trận.", 0, 0, 0, 0,
                new[] { KeywordType.SpellShield }));
            list.Add(R("Đá Thép", "Champion nhận Thần Thép (Công = Máu) đầu mỗi trận.", 0, 0, 0, 0,
                new[] { KeywordType.Formidable }));

            // ── Nhánh HIỆU ỨNG BattleStart (RunEffects đã wire) ──────
            list.Add(R("Sách Cổ", "Đầu mỗi trận: rút thêm 1 lá.", 0, 0, 0, 0, null,
                Fx(RelicOp.DrawCards, 1)));
            list.Add(R("Đá Giảm Giá", "Đầu mỗi trận: mọi bài -1 mana.", 0, 0, 0, 0, null,
                Fx(RelicOp.DiscountAll, 1)));
            list.Add(R("Bình Máu Lớn", "Đầu mỗi trận: hồi 5 máu Nexus.", 0, 0, 0, 0, null,
                Fx(RelicOp.HealNexus, 5)));
            list.Add(R("Vương Miện", "Đầu mỗi trận: riêng champion +3|+3.", 0, 0, 0, 0, null,
                Fx(RelicOp.BuffChampion, 3, 3)));
            list.Add(R("Chiến Kỳ Rực Lửa", "Đầu mỗi trận: toàn quân nhận Bảo Hộ.", 0, 0, 0, 0, null,
                FxK(RelicOp.GrantKeywordAll, KeywordType.Barrier)));

            return list;
        }

        // ── Helpers dựng RelicData runtime ───────────────────────────
        static RelicData R(string name, string desc, int atk, int hp, int mana, int nexus,
            KeywordType[] kws = null, RelicEffect[] fx = null)
        {
            var r = ScriptableObject.CreateInstance<RelicData>();
            r.relicName = Tag + " " + name;   // Tag đầu tên để nhận diện + tránh trùng cổ vật user
            r.description = desc;
            r.alliesAtk = atk; r.alliesHp = hp; r.bonusMana = mana; r.bonusNexus = nexus;
            r.grantKeywords = new List<KeywordType>();
            if (kws != null) r.grantKeywords.AddRange(kws);
            r.effects = new List<RelicEffect>();
            if (fx != null) r.effects.AddRange(fx);
            return r;
        }

        static RelicEffect[] Fx(RelicOp op, int a, int b = 0) => new[]
        {
            new RelicEffect { trigger = RelicTrigger.BattleStart, op = op, a = a, b = b, chance = 100 }
        };

        static RelicEffect[] FxK(RelicOp op, KeywordType kw) => new[]
        {
            new RelicEffect { trigger = RelicTrigger.BattleStart, op = op, keyword = kw, chance = 100 }
        };
    }
}