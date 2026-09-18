using UnityEngine;

namespace LoRClone.Data
{
    /// <summary>
    /// MỐC 3 — CHAMPION (kiểu Con Đường Anh Hùng LoR).
    ///
    /// Mỗi champion định nghĩa 1 HÀNH TRÌNH:
    ///   • championCard   — lá champion đại diện (chân dung) + LUÔN có trong deck.
    ///   • starterDeck    — DECK CỐ ĐỊNH của run (đã gồm championCard + bài chủ đề).
    ///   • signaturePower — Sức Mạnh khởi đầu, áp ngay khi bắt đầu hành trình.
    ///
    /// SETUP: Create → LoRClone → Champion. Gán championCard + starterDeck, điền signaturePower.
    /// Kéo các ChampionData vào CampaignData.champions.
    /// </summary>
    [CreateAssetMenu(menuName = "LoRClone/Champion", fileName = "Champion")]
    public class ChampionData : ScriptableObject
    {
        [Header("Thông tin")]
        public string championName = "Champion";
        [TextArea(2, 4)] public string description = "";

        [Tooltip("Lá champion — chân dung + luôn nằm trong deck run.")]
        public CardData championCard;

        [Tooltip("Deck CỐ ĐỊNH của hành trình (gồm championCard + bài chủ đề). "
                 + "Đây là deck dùng cho MỌI trận trong run (không chọn bài từng trận như map không-champion).")]
        public DeckData starterDeck;

        [Header("Sức Mạnh chiêu bài (áp ngay đầu run)")]
        public RunPower signaturePower = new RunPower();

        [Header("Perk theo SAO (M7) — mở dần khi champion lên sao")]
        [Tooltip("Danh sách perk mở CỘNG DỒN theo sao: phần tử 0 mở ở Sao 2, phần tử 1 ở Sao 3, ...\n" +
                 "Vào run được tặng sẵn các perk ứng với sao hiện tại.\n" +
                 "Để TRỐNG → tự tặng (sao-1) Sức Mạnh ngẫu nhiên từ kho.")]
        public System.Collections.Generic.List<RunPower> starPerks = new System.Collections.Generic.List<RunPower>();

        [Header("Chòm Sao (M9) — nâng cấp VĨNH VIỄN mua bằng Tinh Hồn")]
        [Tooltip("Mỗi node: hiệu ứng (RunPower) + giá Tinh Hồn. Mua rồi → tự áp mỗi trận, giữ vĩnh viễn qua các run.")]
        public System.Collections.Generic.List<ConstellationNode> constellation = new System.Collections.Generic.List<ConstellationNode>();

        [Header("M4 — Nâng bài theo CẤP champion (PoC: lên cấp đổi lá yếu → lá mạnh)")]
        [Tooltip("Mỗi mục: đạt CẤP 'level' thì lá 'from' trong deck được thay bằng lá 'to'. Để trống = không nâng.")]
        public System.Collections.Generic.List<DeckUpgrade> deckUpgrades = new System.Collections.Generic.List<DeckUpgrade>();

        // Trang Bị KHÔNG gán ở đây (M8) — item là KHO CHUNG ở CampaignData.itemPool,
        // dùng chung mọi champion, mở khoá theo cấp mastery.
    }

    /// <summary>M4: đạt CẤP 'level' → thay lá 'from' bằng lá 'to' trong run deck (mở "khả năng mới").</summary>
    [System.Serializable]
    public class DeckUpgrade
    {
        [Min(1)] public int level = 5;
        public CardData from;
        public CardData to;
    }

    /// <summary>1 node trên Chòm Sao (cây có nhánh): hiệu ứng vĩnh viễn mua bằng Tinh Hồn (M9).</summary>
    [System.Serializable]
    public class ConstellationNode
    {
        public string nodeName = "Chòm Sao";
        [Min(0)] public int cost = 100;      // giá Tinh Hồn
        public RunPower effect = new RunPower();

        [Header("Cây nhánh")]
        [Tooltip("Index node BẮT BUỘC mua trước (trong danh sách constellation). -1 = node gốc, mua được ngay.")]
        public int requires = -1;
        [Tooltip("Cột (độ sâu) để vẽ cây, trái→phải.")]
        public int tier = 0;
        [Tooltip("Hàng (dọc) để vẽ cây. 0 = trên.")]
        public int row = 0;
    }
}