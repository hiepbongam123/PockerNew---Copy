namespace LoRClone.Model
{
    /// <summary>
    /// Phần network của CardModel — netId đồng bộ giữa 2 máy (PvP lockstep).
    /// Đặt ở: Assets/Code/Model/CardModel.Net.cs
    ///
    /// CẦN 1 SỬA NHỎ trong CardModel.cs:
    ///   Dòng khai báo class 'public class CardModel'  →  'public partial class CardModel'
    ///
    /// Vì sao cần: CardModel.id cấp bằng static _nextId++ → KHÁC nhau mỗi máy.
    /// netId gán theo thứ tự chính tắc (deck + seed đã đồng bộ) nên GIỐNG nhau trên 2 máy.
    /// </summary>
    public partial class CardModel
    {
        /// <summary>ID đồng bộ mạng. -1 = chưa gán (PvE không dùng).</summary>
        public int netId = -1;
    }
}
