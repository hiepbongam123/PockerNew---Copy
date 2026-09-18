using System.Collections.Generic;
using UnityEngine;

namespace LoRClone.Data
{
    /// <summary>
    /// Danh mục toàn bộ CardData dùng được trong Xưởng Deck (DeckBuilderView).
    ///
    /// SETUP:
    ///   1. Create → LoRClone → Card Library
    ///   2. Kéo tất cả CardData muốn cho phép build deck vào allCards
    ///   3. Gán asset này vào LobbyManager.cardLibrary (và DeckBuilderView nếu tách riêng)
    ///
    /// Để trống / không gán → Xưởng Deck tự gom card từ các deck có sẵn (availableDecks).
    /// Card có isGenerated = true tự động bị loại khỏi thư viện build deck.
    /// </summary>
    [CreateAssetMenu(menuName = "LoRClone/Card Library", fileName = "CardLibrary")]
    public class CardLibrary : ScriptableObject
    {
        public List<CardData> allCards = new List<CardData>();
    }
}
