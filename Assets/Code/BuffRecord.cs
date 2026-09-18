using UnityEngine;

namespace LoRClone.Model
{
    /// <summary>
    /// Một entry trong lịch sử buff/debuff của CardModel — hiện panel inspect (giống LoR).
    ///
    /// THIẾT KẾ MỞ RỘNG: mọi nguồn buff mới chỉ cần gọi BuffRecord.Create(...) rồi
    /// CardModel.RecordBuff(...). KHÔNG phải thêm hàm factory riêng hay sửa panel:
    /// panel đọc các field chung (sourceType→icon/màu, sourceName, description, StatDeltaText).
    /// Thêm 1 loại nguồn mới = (tùy chọn) thêm 1 case trong Icon(); mặc định vẫn có icon fallback.
    /// </summary>
    public class BuffRecord
    {
        public enum SourceType
        {
            Equipment,  // trang bị gắn lên unit (equipment card)
            Skill,      // kỹ năng / spell
            Keyword,    // keyword tự buff (Fury/Scout...)
            Origin,     // "được tạo ra bởi lá này" — không có stat delta
            Item,       // PoC item: buff gắn theo loại card (RunItems)
        }

        public SourceType sourceType;

        /// <summary>Tên nguồn (tên card / keyword / item).</summary>
        public string sourceName;

        /// <summary>Mô tả ngắn "làm gì" (tùy chọn) — hiện dưới tên để biết vì sao có buff.</summary>
        public string description;

        /// <summary>Khóa gộp/loại bỏ. Trống → dùng sourceName. Cho phép 2 entry cùng tên khác khóa.</summary>
        public string sourceKey;

        /// <summary>Buff "trong vòng này" — tự xoá khi ResetTempEffects() đầu round kế.</summary>
        public bool isTemporary;

        /// <summary>Buff vĩnh viễn (không mất theo round). = !isTemporary khi tạo qua Create.</summary>
        public bool permanent = true;

        /// <summary>Artwork thumbnail nguồn (nếu có). null → placeholder.</summary>
        public Texture2D sourceArtwork;

        /// <summary>ATK buff (+) / debuff (–). 0 = không ảnh hưởng ATK.</summary>
        public int attackBuff;

        /// <summary>HP buff (+) / debuff (–). 0 = không ảnh hưởng HP.</summary>
        public int healthBuff;

        /// <summary>Khóa dùng để gộp entry cùng nguồn (fallback = sourceName).</summary>
        public string SourceKey => string.IsNullOrEmpty(sourceKey) ? sourceName : sourceKey;

        // ── Factory CHUNG (khuyến nghị dùng cho mọi nguồn mới) ────────────────
        public static BuffRecord Create(SourceType type, string name, int atk = 0, int hp = 0,
            string description = null, Texture2D artwork = null, bool temporary = false, string key = null)
            => new BuffRecord
            {
                sourceType = type,
                sourceName = name,
                attackBuff = atk,
                healthBuff = hp,
                description = description,
                sourceArtwork = artwork,
                isTemporary = temporary,
                permanent = !temporary,
                sourceKey = key,
            };

        // ── Factory tiện dụng (giữ tương thích code cũ) ───────────────────────
        public static BuffRecord FromEquipment(string name, Texture2D artwork, int atkBuff, int hpBuff)
            => Create(SourceType.Equipment, name, atkBuff, hpBuff, artwork: artwork, key: "equip:" + name);

        public static BuffRecord FromSkill(string name, Texture2D artwork, int atkBuff, int hpBuff, bool temporary = false)
            => Create(SourceType.Skill, name, atkBuff, hpBuff, artwork: artwork, temporary: temporary);

        public static BuffRecord FromKeyword(string keywordDisplayName, int atkBuff, int hpBuff)
            => Create(SourceType.Keyword, keywordDisplayName, atkBuff, hpBuff);

        public static BuffRecord FromItem(string itemName, int atkBuff, int hpBuff, string description)
            => Create(SourceType.Item, itemName, atkBuff, hpBuff, description: description, key: "item:" + itemName);

        public static BuffRecord Origin(string creatorName, Texture2D artwork)
            => Create(SourceType.Origin, creatorName, 0, 0, artwork: artwork);

        // ── Hiển thị ──────────────────────────────────────────────────────────
        public bool IsOrigin => sourceType == SourceType.Origin;

        /// <summary>Icon theo loại nguồn — data-driven, có fallback nên thêm loại mới không vỡ.</summary>
        public string Icon()
        {
            switch (sourceType)
            {
                case SourceType.Equipment: return "🔨";
                case SourceType.Item: return "◆";
                case SourceType.Skill: return "✦";
                case SourceType.Keyword: return "⚔";
                case SourceType.Origin: return "✧";
                default: return "•";
            }
        }

        /// <summary>Text stat delta "+2|+0". Trống nếu IsOrigin.</summary>
        public string StatDeltaText()
        {
            if (IsOrigin) return "";
            string atk = (attackBuff >= 0 ? "+" : "") + attackBuff;
            string hp = (healthBuff >= 0 ? "+" : "") + healthBuff;
            return $"{atk}|{hp}";
        }
    }
}