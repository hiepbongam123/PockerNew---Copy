namespace LoRClone.Controller
{
    /// <summary>
    /// Bộ random DÙNG CHUNG cho MỌI hiệu ứng ảnh hưởng GAME STATE (skill random, summon random,
    /// xáo bài trong skill...). Đặt ở: Assets/Code/Controller/GameRNG.cs
    ///
    /// PvP: seed giống nhau trên 2 máy (StartNetworkGame gọi Seed) → mọi skill random tự đồng bộ.
    /// PvE: mặc định random theo thời gian như cũ (không gọi Seed).
    ///
    /// QUY TẮC: mọi skill/hiệu ứng ngẫu nhiên tác động state PHẢI dùng GameRNG thay cho UnityEngine.Random.
    /// (Âm thanh / animation cứ dùng UnityEngine.Random — không ảnh hưởng state, không cần đồng bộ.)
    /// </summary>
    public static class GameRNG
    {
        static System.Random _rng = new System.Random();

        /// <summary>PvP: gọi 1 lần khi bắt đầu ván (cùng seed trên 2 máy).</summary>
        public static void Seed(int seed) => _rng = new System.Random(seed);

        /// <summary>Giống UnityEngine.Random.Range(int,int): [minInclusive, maxExclusive).</summary>
        public static int Range(int minInclusive, int maxExclusive)
            => maxExclusive <= minInclusive ? minInclusive : _rng.Next(minInclusive, maxExclusive);

        /// <summary>Giống UnityEngine.Random.Range(float,float): [min, max].</summary>
        public static float Range(float min, float max)
            => min + (float)_rng.NextDouble() * (max - min);

        /// <summary>[0, maxExclusive).</summary>
        public static int Next(int maxExclusive) => _rng.Next(maxExclusive);

        /// <summary>[0.0, 1.0).</summary>
        public static double NextDouble() => _rng.NextDouble();
    }
}
