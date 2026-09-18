using System.Collections.Generic;
using UnityEngine;

namespace LoRClone.Data
{
    [CreateAssetMenu(menuName = "LoRClone/Deck Data", fileName = "New Deck")]
    public class DeckData : ScriptableObject
    {
        public string deckName = "My Deck";
        public List<CardData> cards = new();
    }
}
