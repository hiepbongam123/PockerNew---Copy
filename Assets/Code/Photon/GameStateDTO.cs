using System;
using System.Collections.Generic;

namespace LoRClone.Net
{
    /// <summary>
    /// FILE 2/5 — Gói tin snapshot state. Host xuất ra (đã LỌC thông tin ẩn) rồi gửi client.
    /// Client nhận → dựng lại để render. Đặt ở: Assets/Code/Net/GameStateDTO.cs
    /// </summary>
    [Serializable]
    public class CardDTO
    {
        public int id;
        public string dataId;   // khóa tra CardData (art/stats gốc)
        public int attack, health, cost;
        public int slot;
        public int location;    // (int)CardLocation
        public bool faceDown;   // true = client chỉ thấy mặt sau
    }

    [Serializable]
    public class SideDTO
    {
        public int health, mana, spellMana;
        public int deckCount, handCount;               // đối thủ: chỉ gửi SỐ LƯỢNG
        public List<CardDTO> hand = new List<CardDTO>();         // của mình: đầy đủ; đối thủ: rỗng
        public List<CardDTO> bench = new List<CardDTO>();
        public List<CardDTO> battlefield = new List<CardDTO>();
    }

    [Serializable]
    public class GameStateDTO
    {
        public int round;
        public int phase;               // (int)GamePhase
        public bool viewerHasPriority;  // người xem có đang được đi không
        public bool viewerHasToken;     // người xem có attack token không
        public SideDTO me = new SideDTO();
        public SideDTO opp = new SideDTO();
        public List<CardDTO> stack = new List<CardDTO>();
    }
}
