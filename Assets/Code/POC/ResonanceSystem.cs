using System.Collections.Generic;
using LoRClone.Model;

namespace LoRClone.Data
{
    public enum ResonanceType { CuongCong = 0, KienCuong = 1, PhapThuat = 2 }

    /// <summary>
    /// CỘNG HƯỞNG VẬN MỆNH — kiểu Path Resonance (Vũ Trụ Mô Phỏng HSR).
    ///
    /// Ý tưởng: gom Sức Mạnh (RunPower) trong ải → phân về 3 HỆ theo stat mà buff cộng.
    ///   • Cường Công (ATK)      : alliesAtk / championAtk
    ///   • Kiên Cường (HP/Nexus) : alliesHp / championHp / bonusNexus
    ///   • Pháp Thuật (Mana/Rút) : bonusMana / bonusDraw / costReduction
    /// Đủ NGƯỠNG buff cùng hệ → MỞ nút Cộng Hưởng (cấp 1). Gom thêm cùng hệ → nút LÊN CẤP (mạnh dần).
    ///
    /// Tính TƯƠI từ CampaignRun.Collected mỗi lần cần → tự reset theo ải (ResetRun xoá Collected).
    /// 1 buff "đa hệ" (vd +atk|+hp) tính cho MỌI hệ nó đóng góp → thưởng chọn đa dạng.
    /// </summary>
    public static class ResonanceSystem
    {
        public const int UnlockCount = 3; // đủ 3 buff cùng hệ = mở (cấp 1)
        public const int PerLevel = 2;    // mỗi +2 buff cùng hệ = +1 cấp

        public static string TypeName(ResonanceType t) => t switch
        {
            ResonanceType.CuongCong => "Cường Công",
            ResonanceType.KienCuong => "Kiên Cường",
            ResonanceType.PhapThuat => "Pháp Thuật",
            _ => "Cộng Hưởng",
        };

        public static string TypeHex(ResonanceType t) => t switch
        {
            ResonanceType.CuongCong => "E86B6B",
            ResonanceType.KienCuong => "5AC8E0",
            ResonanceType.PhapThuat => "B080E0",
            _ => "E8C06A",
        };

        public static bool Contributes(RunPower p, ResonanceType t)
        {
            if (p == null) return false;
            switch (t)
            {
                case ResonanceType.CuongCong: return p.alliesAtk > 0 || p.championAtk > 0;
                case ResonanceType.KienCuong: return p.alliesHp > 0 || p.championHp > 0 || p.bonusNexus > 0;
                case ResonanceType.PhapThuat: return p.bonusMana > 0 || p.bonusDraw > 0 || p.costReduction > 0;
            }
            return false;
        }

        public static int Count(ResonanceType t)
        {
            int n = 0;
            if (CampaignRun.Collected != null)
                foreach (var p in CampaignRun.Collected) if (Contributes(p, t)) n++;
            return n;
        }

        /// <summary>Cấp của 1 hệ theo số buff đã gom. &lt; ngưỡng = 0 (chưa mở).</summary>
        public static int Level(int count) => count < UnlockCount ? 0 : 1 + (count - UnlockCount) / PerLevel;

        /// <summary>Số buff còn thiếu để lên cấp kế (dùng cho UI). 0 nếu đã đủ vừa lên.</summary>
        public static int ToNext(int count)
        {
            if (count < UnlockCount) return UnlockCount - count;
            int into = (count - UnlockCount) % PerLevel;
            return PerLevel - into;
        }

        /// <summary>Hệ cộng hưởng MẠNH NHẤT đang mở. Trả về false nếu chưa hệ nào đạt ngưỡng.</summary>
        public static bool Dominant(out ResonanceType type, out int level, out int count)
        {
            type = ResonanceType.CuongCong; level = 0; count = 0;
            for (int i = 0; i < 3; i++)
            {
                var t = (ResonanceType)i;
                int c = Count(t);
                int lv = Level(c);
                if (lv > level || (lv == level && lv > 0 && c > count))
                {
                    type = t; level = lv; count = c;
                }
            }
            return level > 0;
        }

        public static int ManaCost(int level) => 3;

        /// <summary>Hiệu ứng nút theo hệ + cấp — số nhân theo cấp (dùng lại PowerAction/RelicOp).</summary>
        public static List<PowerAction> BuildActions(ResonanceType t, int level)
        {
            int L = level < 1 ? 1 : level;
            var acts = new List<PowerAction>();
            switch (t)
            {
                case ResonanceType.CuongCong: // toàn quân +2×cấp ATK
                    acts.Add(new PowerAction { op = RelicOp.BuffAllies, a = 2 * L, b = 0 });
                    break;
                case ResonanceType.KienCuong: // hồi Nexus 3×cấp + toàn quân +cấp HP
                    acts.Add(new PowerAction { op = RelicOp.HealNexus, a = 3 * L });
                    acts.Add(new PowerAction { op = RelicOp.BuffAllies, a = 0, b = L });
                    break;
                case ResonanceType.PhapThuat: // rút bài theo cấp (tối đa 3) + cấp ≥2 thêm 1 mana
                    int draw = L; if (draw > 3) draw = 3; if (draw < 1) draw = 1;
                    acts.Add(new PowerAction { op = RelicOp.DrawCards, a = draw });
                    if (L >= 2) acts.Add(new PowerAction { op = RelicOp.GainMana, a = 1 });
                    break;
            }
            return acts;
        }

        /// <summary>Mô tả ngắn hiệu ứng nút (cho UI).</summary>
        public static string EffectDesc(ResonanceType t, int level)
        {
            int L = level < 1 ? 1 : level;
            switch (t)
            {
                case ResonanceType.CuongCong: return $"Toàn quân +{2 * L} Công";
                case ResonanceType.KienCuong: return $"Nexus +{3 * L} máu · quân +{L} Máu";
                case ResonanceType.PhapThuat:
                    int draw = L > 3 ? 3 : L;
                    return L >= 2 ? $"Rút {draw} lá +1 mana" : $"Rút {draw} lá";
            }
            return "";
        }
    }
}
