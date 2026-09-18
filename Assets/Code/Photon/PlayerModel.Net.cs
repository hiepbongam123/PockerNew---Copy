namespace LoRClone.Model
{
    /// <summary>
    /// Phần network của PlayerModel — RNG seed để 2 máy xáo bài giống hệt (PvP lockstep).
    /// Đặt ở: Assets/Code/Model/PlayerModel.Net.cs
    ///
    /// CẦN 2 SỬA NHỎ trong PlayerModel.cs:
    ///   ① Khai báo class: 'public class PlayerModel'  →  'public partial class PlayerModel'
    ///   ② Trong Shuffle(): 'var rng = new System.Random();'  →  'var rng = _netRng ?? new System.Random();'
    /// </summary>
    public partial class PlayerModel
    {
        /// <summary>null = random (PvE mặc định). Set = deterministic (PvP).</summary>
        System.Random _netRng;

        /// <summary>PvP: gọi TRƯỚC BuildDeck để 2 máy xáo bài giống hệt nhau.</summary>
        public void SetDeterministicSeed(int seed) => _netRng = new System.Random(seed);
    }
}
