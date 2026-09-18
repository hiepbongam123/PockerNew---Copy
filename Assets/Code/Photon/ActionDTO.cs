using System;

namespace LoRClone.Net
{
    /// <summary>
    /// FILE 1/5 — Gói tin "tôi muốn làm action này". Client build cái này rồi gửi cho host.
    /// Không chứa object CardModel (không gửi được qua mạng) → chỉ chứa id + số.
    /// Đặt ở: Assets/Code/Net/ActionDTO.cs
    /// </summary>
    [Serializable]
    public class ActionDTO
    {
        public int seq;             // số thứ tự — host bỏ tin trùng/cũ
        public bool byPlayer;       // phe gửi (theo góc nhìn host). Bridge tự set.
        public string type;         // "PlayUnit","Cast","Stage","Attack","Block","Undeclare","Pass","Unstage","UndoCast"
        public int cardId = -1;     // lá chính (unit/spell) = CardModel.id
        public int slotIndex = -1;  // ô bench khi chơi unit
        public int mode = -1;       // mode equipment (nếu có)
        public int[] targetIds;     // id các mục tiêu (nếu action cần)
    }
}
