using System.Collections.Generic;
using UnityEngine;

namespace LoRClone.Data
{
    /// <summary>Unit đặt SẴN trên sân khi bắt đầu tầng (tutorial / dàn cảnh).</summary>
    [System.Serializable]
    public class PrePlacedUnit
    {
        [Tooltip("Unit sẽ đứng sẵn trên bench khi vào tầng.")]
        public CardData unit;
        [Tooltip("Bật = đặt bên sân ĐỊCH. Tắt = sân người chơi.")]
        public bool onEnemySide = false;
        [Tooltip("Slot bench muốn đặt (0-5). -1 = slot trống đầu tiên.")]
        public int benchSlot = -1;
    }

    /// <summary>
    /// 1 TẦNG của tháp (kiểu PvZ + Path of Champions).
    ///
    /// SETUP: Create → LoRClone → Campaign Level, điền:
    ///   levelName / description — hiện trong màn Leo Tháp
    ///   bossCard               — card đại diện boss (để trống → Unit cost cao nhất trong enemyDeck)
    ///   enemyDeck              — deck AI dùng ở tầng này
    ///   deckPickCount          — số lá người chơi phải chọn để mang vào (vd tầng 3 = 6 lá)
    ///   playerHealth/enemyHealth — máu Nexus 2 bên (0 = mặc định 20)
    ///   playerStartMana/enemyStartMana — mana khởi đầu (0 = mặc định theo round; hữu ích cho
    ///                            tutorial để chơi ngay unit cost cao)
    ///   startUnits             — unit đặt SẴN trên sân khi vào tầng (dàn cảnh hướng dẫn)
    ///   rewardCards            — card thưởng khi thắng LẦN ĐẦU → cộng vào KHO bài Leo Tháp
    ///
    /// File này PHẢI giữ tên LevelData.cs (trùng tên class).
    /// </summary>
    [CreateAssetMenu(menuName = "LoRClone/Campaign Level", fileName = "Level_01")]
    public class LevelData : ScriptableObject
    {
        [Header("Thông tin")]
        public string levelName = "Tầng 1";

        [TextArea(2, 4)]
        public string description = "";

        [Tooltip("Card đại diện boss (ảnh tầng). Để trống → tự lấy Unit cost cao nhất trong enemyDeck.")]
        public CardData bossCard;

        [Header("Đối thủ")]
        [Tooltip("Deck AI dùng ở tầng này.")]
        public DeckData enemyDeck;

        [Header("Máu Nexus (0 = mặc định 20)")]
        [Min(0)] public int playerHealth = 0;
        [Min(0)] public int enemyHealth = 0;

        [Header("Mana khởi đầu (0 = mặc định theo round)")]
        [Tooltip("Mana khởi đầu của người chơi ở tầng này (max mana + đầy). 0 = mặc định.\n" +
                 "Hữu ích cho tutorial: để cao để chơi ngay unit cost lớn.")]
        [Min(0)] public int playerStartMana = 0;
        [Tooltip("Mana khởi đầu của địch. 0 = mặc định.")]
        [Min(0)] public int enemyStartMana = 0;

        [Header("Chọn bài (PvZ) — số lá phải mang vào tầng này")]
        [Min(1)] public int deckPickCount = 1;

        [Header("Unit đặt sẵn trên sân (tutorial / dàn cảnh)")]
        [Tooltip("Các unit đứng sẵn trên bench khi vào tầng. Vd tầng 1 để 1 unit hướng dẫn.")]
        public List<PrePlacedUnit> startUnits = new List<PrePlacedUnit>();

        [Header("Phần thưởng")]
        [Tooltip("Card thưởng khi thắng LẦN ĐẦU — cộng vào KHO bài Leo Tháp (+ mở khóa Xưởng Deck).")]
        public List<CardData> rewardCards = new List<CardData>();
    }
}
