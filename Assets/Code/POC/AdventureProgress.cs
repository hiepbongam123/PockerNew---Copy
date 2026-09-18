using System.Collections.Generic;
using UnityEngine;

namespace LoRClone.Data
{
    /// <summary>
    /// M2 — Lưu ẢI ĐÃ HOÀN THÀNH (hạ boss cuối) để enforce ladder "phải qua ải sao thấp trước".
    /// Persist bằng PlayerPrefs (giữ vĩnh viễn giữa các run).
    ///
    /// MỐC 12: tiến độ tính THEO TỪNG CHAMPION → key = "champion|adventureName".
    /// Mỗi tướng leo ladder riêng (giống Path of Champions).
    /// </summary>
    public static class AdventureProgress
    {
        const string Key = "adv_completed";
        static HashSet<string> _set;

        static HashSet<string> Load()
        {
            if (_set != null) return _set;
            _set = new HashSet<string>();
            var raw = PlayerPrefs.GetString(Key, "");
            if (!string.IsNullOrEmpty(raw))
                foreach (var s in raw.Split('|'))
                    if (!string.IsNullOrEmpty(s)) _set.Add(s);
            return _set;
        }

        // Key gộp: "champ␟adv" (dùng ␟ = ký tự hiếm để không đụng tên có '|').
        static string K(string champ, string adventureName) => (champ ?? "") + "␟" + adventureName;

        public static bool IsCompleted(string champ, string adventureName)
            => !string.IsNullOrEmpty(adventureName) && Load().Contains(K(champ, adventureName));

        /// <summary>Đánh dấu đã qua ải. Trả về TRUE nếu đây là LẦN ĐẦU (dùng để thưởng rương).</summary>
        public static bool MarkCompleted(string champ, string adventureName)
        {
            if (string.IsNullOrEmpty(adventureName)) return false;
            if (Load().Add(K(champ, adventureName)))
            {
                PlayerPrefs.SetString(Key, string.Join("|", _set));
                PlayerPrefs.Save();
                return true;
            }
            return false;
        }

        public static void Reset()
        {
            _set = new HashSet<string>();
            PlayerPrefs.DeleteKey(Key);
            PlayerPrefs.Save();
        }
    }
}