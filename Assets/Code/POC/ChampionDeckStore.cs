using System.Collections.Generic;
using UnityEngine;

namespace LoRClone.Data
{
    /// <summary>
    /// Lưu DECK TÙY CHỈNH theo TỪNG CHAMPION (Con Đường Anh Hùng: chỉ sửa deck trong champion đã chọn).
    /// Lưu tên lá (PlayerPrefs); resolve → CardData qua kho bài champion + CardLibrary.
    /// RebuildRunDeck ưu tiên deck đã lưu (resolved) thay cho starterDeck.
    /// </summary>
    public static class ChampionDeckStore
    {
        static string Key(string champ) => "champdeck_" + champ;
        static readonly Dictionary<string, List<CardData>> _resolved = new Dictionary<string, List<CardData>>();

        public static bool HasSaved(string champ)
            => !string.IsNullOrEmpty(champ) && !string.IsNullOrEmpty(PlayerPrefs.GetString(Key(champ), ""));

        public static List<string> GetNames(string champ)
        {
            var list = new List<string>();
            var raw = PlayerPrefs.GetString(Key(champ), "");
            if (!string.IsNullOrEmpty(raw))
                foreach (var s in raw.Split('|')) if (!string.IsNullOrEmpty(s)) list.Add(s);
            return list;
        }

        public static void Save(string champ, List<CardData> cards)
        {
            if (string.IsNullOrEmpty(champ) || cards == null) return;
            var names = new List<string>();
            foreach (var c in cards) if (c != null) names.Add(c.cardName);
            PlayerPrefs.SetString(Key(champ), string.Join("|", names));
            PlayerPrefs.Save();
            _resolved[champ] = new List<CardData>(cards);
        }

        // Cache resolved (đặt lúc mở champion detail) → RebuildRunDeck đọc được dù đã restart app.
        public static void SetResolved(string champ, List<CardData> cards)
        {
            if (!string.IsNullOrEmpty(champ) && cards != null) _resolved[champ] = new List<CardData>(cards);
        }

        public static bool TryGetResolved(string champ, out List<CardData> cards)
            => _resolved.TryGetValue(champ ?? "", out cards);

        public static void Clear(string champ)
        {
            if (string.IsNullOrEmpty(champ)) return;
            PlayerPrefs.DeleteKey(Key(champ));
            _resolved.Remove(champ);
            PlayerPrefs.Save();
        }
    }
}
