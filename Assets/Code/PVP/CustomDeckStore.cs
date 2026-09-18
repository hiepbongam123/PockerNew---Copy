using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

namespace LoRClone.Data
{
    /// <summary>
    /// Lưu / đọc deck TỰ TẠO (từ Xưởng Deck) dưới dạng JSON tại:
    ///   Application.persistentDataPath/custom_decks.json
    ///
    /// Deck lưu theo TÊN CARD (cardName) — không tham chiếu asset trực tiếp,
    /// nên build ra máy khác vẫn đọc được miễn CardData cùng tên tồn tại.
    /// Resolve tên → CardData qua CardLibrary (fallback: gom từ các deck có sẵn).
    /// Card bị đổi tên/xóa → bỏ qua kèm warning, không crash.
    /// </summary>
    public static class CustomDeckStore
    {
        [Serializable]
        public class DeckDTO
        {
            public string name;
            public List<string> cardNames = new List<string>();
        }

        [Serializable]
        class DeckFile { public List<DeckDTO> decks = new List<DeckDTO>(); }

        static string FilePath => Path.Combine(Application.persistentDataPath, "custom_decks.json");

        public static List<DeckDTO> LoadAll()
        {
            try
            {
                if (!File.Exists(FilePath)) return new List<DeckDTO>();
                var file = JsonUtility.FromJson<DeckFile>(File.ReadAllText(FilePath));
                return file?.decks ?? new List<DeckDTO>();
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[CustomDeckStore] Lỗi đọc file deck: {e.Message}");
                return new List<DeckDTO>();
            }
        }

        /// <summary>Lưu deck — trùng tên thì ghi đè.</summary>
        public static void SaveDeck(string name, List<string> cardNames)
        {
            var all = LoadAll();
            all.RemoveAll(d => d.name == name);
            all.Add(new DeckDTO { name = name, cardNames = new List<string>(cardNames) });
            WriteAll(all);
            Debug.Log($"[CustomDeckStore] Đã lưu deck '{name}' ({cardNames.Count} lá) → {FilePath}");
        }

        public static void DeleteDeck(string name)
        {
            var all = LoadAll();
            all.RemoveAll(d => d.name == name);
            WriteAll(all);
            Debug.Log($"[CustomDeckStore] Đã xóa deck '{name}'.");
        }

        static void WriteAll(List<DeckDTO> decks)
        {
            try
            {
                File.WriteAllText(FilePath, JsonUtility.ToJson(new DeckFile { decks = decks }, true));
            }
            catch (Exception e)
            {
                Debug.LogError($"[CustomDeckStore] Lỗi ghi file deck: {e.Message}");
            }
        }

        /// <summary>
        /// Map cardName → CardData: ưu tiên CardLibrary, fallback gom từ các deck có sẵn.
        /// Dùng chung bởi Xưởng Deck (thư viện bài) và resolve deck đã lưu.
        /// </summary>
        public static Dictionary<string, CardData> BuildResolver(CardLibrary library, DeckData[] fallbackDecks)
        {
            var map = new Dictionary<string, CardData>();
            if (library != null && library.allCards != null)
                foreach (var c in library.allCards)
                    if (c != null && !map.ContainsKey(c.cardName)) map[c.cardName] = c;
            if (fallbackDecks != null)
                foreach (var d in fallbackDecks)
                {
                    if (d == null || d.cards == null) continue;
                    foreach (var c in d.cards)
                        if (c != null && !map.ContainsKey(c.cardName)) map[c.cardName] = c;
                }
            return map;
        }

        /// <summary>
        /// Convert toàn bộ deck đã lưu thành DeckData runtime (CreateInstance, KHÔNG phải asset)
        /// — dùng trực tiếp được cho GameConfig / model.SetupDecks.
        /// Tên deck có hậu tố " *" để phân biệt deck tự tạo trong picker.
        /// </summary>
        public static List<DeckData> LoadAllAsDecks(CardLibrary library, DeckData[] fallbackDecks)
        {
            var resolver = BuildResolver(library, fallbackDecks);
            var result = new List<DeckData>();
            foreach (var dto in LoadAll())
            {
                var deck = ScriptableObject.CreateInstance<DeckData>();
                deck.deckName = dto.name + " *";
                deck.cards = new List<CardData>();
                foreach (var n in dto.cardNames)
                {
                    if (resolver.TryGetValue(n, out var cd)) deck.cards.Add(cd);
                    else Debug.LogWarning($"[CustomDeckStore] Deck '{dto.name}': không tìm thấy card '{n}' — bỏ qua.");
                }
                result.Add(deck);
            }
            return result;
        }
    }
}
