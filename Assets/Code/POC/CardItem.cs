using System.Collections.Generic;
using UnityEngine;
using LoRClone.Data;

namespace LoRClone.Model
{
    public enum ItemRarity { Common, Rare, Epic }

    /// <summary>
    /// ITEM kiểu PoC (khác Relic): gắn vào MỘT loại card (theo cardName), buff MỌI bản sao lá đó
    /// trong suốt run — không giới hạn số item/lá. Nhặt giữa run ở node Item Shop / Item Chest.
    ///
    /// Relic = buff run/champion; Item = buff card. Đừng nhầm 2 cái.
    /// </summary>
    [System.Serializable]
    public class CardItem
    {
        public string itemName;
        public ItemRarity rarity;
        public int atkBonus;              // +ATK cho lá
        public int hpBonus;               // +HP cho lá
        public int costReduction;         // giảm mana (>=0)
        public KeywordType? grantKeyword; // null = không cấp keyword
        public Sprite icon;               // art thật (từ ItemData.icon); null = dùng fallback ◆
        public LoRClone.Data.SkillData skill; // kỹ năng kích hoạt (từ library); null = không có
        public int goldCost;              // giá VÀNG ở cửa hàng trong-run (0 = tự tính theo độ hiếm)
        public int coreCost;              // giá LÕI (Core) ở shop meta champion (0 = không bán bằng Lõi)
        public bool championEquip;        // true = TRANG BỊ CHAMPION meta (Lõi + mastery + equip vĩnh viễn)
        public int bonusMana;             // +mana khởi đầu (chỉ champion equip)
        public int bonusNexus;            // +máu Nexus khởi đầu (chỉ champion equip)

        public CardItem(string name, ItemRarity rarity, int atk = 0, int hp = 0,
                        int costReduc = 0, KeywordType? kw = null, Sprite icon = null,
                        LoRClone.Data.SkillData skill = null, int goldCost = 0, int coreCost = 0,
                        bool championEquip = false, int bonusMana = 0, int bonusNexus = 0)
        {
            itemName = name; this.rarity = rarity;
            atkBonus = atk; hpBonus = hp; costReduction = costReduc; grantKeyword = kw;
            this.icon = icon; this.skill = skill;
            this.goldCost = goldCost; this.coreCost = coreCost;
            this.championEquip = championEquip; this.bonusMana = bonusMana; this.bonusNexus = bonusNexus;
        }

        public string Describe()
        {
            var parts = new List<string>();
            if (atkBonus != 0) parts.Add($"+{atkBonus} ATK");
            if (hpBonus != 0) parts.Add($"+{hpBonus} HP");
            if (costReduction > 0) parts.Add($"-{costReduction} mana");
            if (grantKeyword.HasValue) parts.Add(KeywordVN(grantKeyword.Value));
            if (skill != null) parts.Add("<color=#F5D98A>" + SkillText() + "</color>"); // màu vàng thay icon (tránh lỗi font)
            return string.Join(", ", parts);
        }

        /// <summary>Mô tả kỹ năng: ưu tiên description; trống → tên asset làm đẹp (bỏ tiền tố Skill_).</summary>
        public string SkillText()
        {
            if (skill == null) return "";
            if (!string.IsNullOrEmpty(skill.description)) return skill.description;
            string n = skill.name ?? "Kỹ năng";
            if (n.StartsWith("Skill_")) n = n.Substring(6);
            else if (n.StartsWith("Skill")) n = n.Substring(5);
            return n.Replace('_', ' ').Trim();
        }

        public bool HasSkill => skill != null;

        // Tên keyword tiếng Việt cho mô tả (khớp tên trong game — theo bảng _kwDesc của CardView).
        static string KeywordVN(KeywordType k)
        {
            switch (k)
            {
                case KeywordType.Barrier: return "Bảo Hộ";
                case KeywordType.SpellShield: return "Hoà Hợp";
                case KeywordType.Lifesteal: return "Trù Phú";
                case KeywordType.Regeneration: return "Hồi Phục";
                case KeywordType.Formidable: return "Thần Thép";
                case KeywordType.QuickAttack: return "Săn Bắn";
                case KeywordType.Elusive: return "Thần Bí";
                case KeywordType.Overwhelm: return "Huỷ Diệt";
                case KeywordType.Fury: return "Ác Nghiệp";
                case KeywordType.Impact: return "Xung Kích";
                case KeywordType.Challenger: return "Chỉ Định";
                case KeywordType.Scout: return "Tiên Phong";
                case KeywordType.Fated: return "Thiên Cơ";
                case KeywordType.Tough: return "Trí Thức";
                case KeywordType.Fearsome: return "Hư Vô";
                case KeywordType.Deep: return "Vực Thẳm";
                case KeywordType.DoubleAttack: return "Song Kích";
                case KeywordType.Hallowed: return "Linh Thiêng";
                default: return k.ToString();
            }
        }

        // ── KHO TRANG BỊ: CHỈ dùng CardItemLibrary (gán ở CampaignData.cardItemLibrary). ──
        // Đã BỎ Pool hardcoded — không tạo item ngẫu nhiên nữa. Library trống = không có item để bốc.

        // ── Library (CardItemLibrary) — nếu campaign gán thì ưu tiên (item có SkillData) ──
        static LoRClone.Data.CardItemLibrary _libraryOverride;
        public static void SetLibrary(LoRClone.Data.CardItemLibrary lib) => _libraryOverride = lib;

        static LoRClone.Data.CardItemLibrary ActiveLibrary()
        {
            if (_libraryOverride != null) return _libraryOverride;
            var camp = CampaignContext.campaign;
            return camp != null ? camp.cardItemLibrary : null;
        }

        // KHO DÙNG CHUNG: in-run (Shop/Vật Phẩm/Thưởng, gắn vào lá) bốc TOÀN BỘ item trong library,
        // kể cả item championEquip (khác nhau ở CÁCH DÙNG, không chia pool). Trống → RỖNG.
        static List<CardItem> ActivePool()
        {
            var list = new List<CardItem>();
            var lib = ActiveLibrary();
            if (lib != null && lib.items != null)
                foreach (var d in lib.items) if (d != null) list.Add(d.ToRuntime());
            return list;
        }

        /// <summary>Tra SkillData theo TÊN món trong library (dùng khôi phục skill sau save).</summary>
        public static LoRClone.Data.SkillData SkillForItem(string name)
        {
            if (string.IsNullOrEmpty(name)) return null;
            var lib = ActiveLibrary();
            if (lib != null && lib.items != null)
                foreach (var d in lib.items)
                    if (d != null && d.itemName == name && d.skill != null) return d.skill;
            return null;
        }

        /// <summary>Tra ICON theo TÊN món trong library (Sprite không serialize → khôi phục sau save).</summary>
        public static Sprite IconForItem(string name)
        {
            if (string.IsNullOrEmpty(name)) return null;
            var lib = ActiveLibrary();
            if (lib != null && lib.items != null)
                foreach (var d in lib.items)
                    if (d != null && d.itemName == name && d.icon != null) return d.icon;
            return null;
        }

        /// <summary>Bốc 1 item độ hiếm cho trước từ LIBRARY. Trả null nếu library trống (caller phải xử lý null).</summary>
        public static CardItem RandomOf(ItemRarity rarity)
        {
            var pool = ActivePool();
            if (pool.Count == 0) return null;   // chưa cấu hình library → không có gì để bốc
            var pick = pool.FindAll(i => i.rarity == rarity);
            if (pick.Count == 0) pick = pool;
            return pick[Random.Range(0, pick.Count)];
        }

        /// <summary>Toàn bộ item trong library (cho shop hiện tất cả). Rỗng nếu chưa cấu hình.</summary>
        public static List<CardItem> AllFromLibrary() => ActivePool();

        /// <summary>Định nghĩa gốc (CardItemDef) trong library — cho UI shop/equip đọc coreCost/mastery/championEquip.</summary>
        public static IReadOnlyList<LoRClone.Data.CardItemDef> AllDefs()
        {
            var lib = ActiveLibrary();
            return lib != null && lib.items != null ? (IReadOnlyList<LoRClone.Data.CardItemDef>)lib.items
                                                    : System.Array.Empty<LoRClone.Data.CardItemDef>();
        }

        /// <summary>Trang bị CHAMPION đang ĐEO cho championName (đã sở hữu/mở khoá + equip). Áp mỗi trận.</summary>
        public static List<CardItem> EquippedChampionItems(string championName, int masteryLevel)
        {
            var res = new List<CardItem>();
            var lib = ActiveLibrary();
            if (lib == null || lib.items == null || string.IsNullOrEmpty(championName)) return res;
            foreach (var d in lib.items)
            {
                if (d == null || !d.championEquip) continue;
                bool owned = ProgressStore.IsItemOwned(d.itemName);
                bool unlocked = owned || (d.coreCost <= 0 && masteryLevel >= d.unlockMasteryLevel); // mua Lõi HOẶC mở theo mastery
                if (!unlocked) continue;
                if (!ProgressStore.IsItemEquipped(championName, d.itemName)) continue;
                res.Add(d.ToRuntime());
            }
            return res;
        }
    }

    /// <summary>
    /// Kho item của RUN hiện tại: cardName → danh sách item đã gắn. Static, sống suốt phiên chơi.
    /// Gọi ClearRun() khi bắt đầu run mới (vd trong CampaignRun.StartRun).
    /// </summary>
    public static class RunItems
    {
        static readonly Dictionary<string, List<CardItem>> _byCard = new Dictionary<string, List<CardItem>>();

        // ── Trang bị ĐỊCH: key theo CardModel.id (mỗi con địch đeo đồ riêng, không đụng RunItems player). ──
        static readonly Dictionary<int, List<CardItem>> _byEnemyUnit = new Dictionary<int, List<CardItem>>();
        public static void AttachEnemy(int unitId, CardItem it)
        {
            if (it == null) return;
            if (!_byEnemyUnit.TryGetValue(unitId, out var l)) _byEnemyUnit[unitId] = l = new List<CardItem>();
            l.Add(it);
        }
        public static IReadOnlyList<CardItem> EnemyItemsOf(int unitId)
            => _byEnemyUnit.TryGetValue(unitId, out var l) ? l : System.Array.Empty<CardItem>();
        public static void ClearEnemy() => _byEnemyUnit.Clear();

        public static void Attach(string cardName, CardItem it)
        {
            if (string.IsNullOrEmpty(cardName) || it == null) return;
            if (!_byCard.TryGetValue(cardName, out var l)) _byCard[cardName] = l = new List<CardItem>();
            l.Add(it);
        }

        public static IReadOnlyList<CardItem> ItemsOf(string cardName)
            => _byCard.TryGetValue(cardName, out var l) ? l : System.Array.Empty<CardItem>();

        public static int CountOf(string cardName)
            => _byCard.TryGetValue(cardName, out var l) ? l.Count : 0;

        public static void ClearRun() { _byCard.Clear(); _byEnemyUnit.Clear(); }

        /// <summary>Xóa item của 1 lá (để re-derive equipment không bị nhân đôi khi refresh).</summary>
        public static void ClearForCard(string cardName)
        {
            if (!string.IsNullOrEmpty(cardName)) _byCard.Remove(cardName);
        }

        // ── Persist item nhặt qua save (icon không serialize; equipment champion tự re-derive lại icon) ──
        [System.Serializable]
        public struct SaveEntry
        {
            public string card, name;
            public int rarity, atk, hp, cost, keyword; // keyword: -1 = không có
        }

        public static List<SaveEntry> Snapshot()
        {
            var list = new List<SaveEntry>();
            foreach (var kv in _byCard)
                foreach (var it in kv.Value)
                    list.Add(new SaveEntry
                    {
                        card = kv.Key,
                        name = it.itemName,
                        rarity = (int)it.rarity,
                        atk = it.atkBonus,
                        hp = it.hpBonus,
                        cost = it.costReduction,
                        keyword = it.grantKeyword.HasValue ? (int)it.grantKeyword.Value : -1
                    });
            return list;
        }

        public static void Restore(List<SaveEntry> list)
        {
            _byCard.Clear();
            if (list == null) return;
            foreach (var e in list)
            {
                KeywordType? kw = e.keyword >= 0 ? (KeywordType?)(KeywordType)e.keyword : null;
                var it = new CardItem(e.name, (ItemRarity)e.rarity, e.atk, e.hp, e.cost, kw);
                it.skill = CardItem.SkillForItem(e.name); // khôi phục SkillData theo tên (nếu library có)
                it.icon = CardItem.IconForItem(e.name);   // khôi phục icon (Sprite không serialize)
                Attach(e.card, it);
            }
        }

        /// <summary>Áp mọi item của lá lên 1 CardModel vừa tạo (chỉ deck player). Gọi trong BuildDeck.</summary>
        public static void Apply(CardModel m, CardData cd)
        {
            if (m == null || cd == null) return;
            if (!_byCard.TryGetValue(cd.cardName, out var items)) return;
            foreach (var it in items)
            {
                if (it.atkBonus != 0) m.BuffAttack(it.atkBonus);
                if (it.hpBonus != 0) m.BuffHealth(it.hpBonus);
                if (it.costReduction > 0) m.ReduceManaCost(it.costReduction);
                if (it.grantKeyword.HasValue) m.GrantKeyword(it.grantKeyword.Value);
                // Kỹ năng kích hoạt: tự resolve từ library nếu chưa có (item khôi phục từ save), rồi gắn.
                if (it.skill == null) it.skill = CardItem.SkillForItem(it.itemName);
                if (it.skill != null) m.AddBonusSkill(it.skill);
                // Ghi nguồn để panel inspect biết buff này đến từ item nào.
                m.RecordBuff(BuffRecord.FromItem(it.itemName, it.atkBonus, it.hpBonus, it.Describe()));
            }
        }

        /// <summary>
        /// CHỈ gắn lại kỹ năng của item lên 1 lá (không đụng chỉ số). Dùng cho lá CHAMPION sau khi
        /// ApplyChampionKeywordsAndSkills gọi ClearBonusSkills — để item-skill trên lá champion không bị mất.
        /// AddBonusSkill dedup nên gọi lại an toàn.
        /// </summary>
        public static void ApplySkillsOnly(CardModel m, string cardName)
        {
            if (m == null || string.IsNullOrEmpty(cardName)) return;
            if (!_byCard.TryGetValue(cardName, out var items)) return;
            foreach (var it in items)
            {
                if (it.skill == null) it.skill = CardItem.SkillForItem(it.itemName);
                if (it.skill != null) m.AddBonusSkill(it.skill);
            }
        }
    }
}