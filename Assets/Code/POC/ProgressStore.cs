using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

namespace LoRClone.Data
{
    /// <summary>
    /// Tiến độ Leo Tháp — lưu JSON tại Application.persistentDataPath/campaign_progress.json.
    ///   highestCompleted — tầng cao nhất đã thắng (-1 = chưa thắng tầng nào)
    ///   unlockedCards    — tên các card quà đã nhận (Xưởng Deck dùng để mở khóa)
    ///
    /// Luật mở tầng: tầng i chơi được khi i <= highestCompleted + 1.
    /// Quà chỉ nhận 1 lần (thắng lại tầng cũ không nhận thêm).
    /// </summary>
    public static class ProgressStore
    {
        [Serializable]
        class ProgressDTO
        {
            public int highestCompleted = -1;
            public List<string> unlockedCards = new List<string>();
            // Mốc 3: mastery champion — 2 list song song (JsonUtility không hỗ trợ Dictionary).
            public List<string> masteryNames = new List<string>();
            public List<int> masteryXp = new List<int>();
            // Mốc 9: Tinh Hồn (essence) toàn cục + các node Tinh Hồn đã mua ("champ|index").
            public int essence = 0;
            // MỐC 11: Lõi Cường Hóa — currency FARM (rơi từ node, nặng ở Boss). Dùng nâng cấp trang bị.
            public int cores = 0;
            public List<string> constellationBought = new List<string>();
            // Mốc 8: item ĐANG ĐEO theo champion ("champ|itemName").
            public List<string> equippedItems = new List<string>();
            // Phần Chest: trang bị ĐÃ SỞ HỮU (nhặt từ rương) — tên item, dùng chung mọi champion.
            public List<string> ownedItems = new List<string>();
            // MỐC 14: số RƯƠNG đang có theo bậc [Thường, Hiếm, Tuyệt Phẩm, Huyền Thoại].
            public List<int> chests = new List<int>();
            // RANK PvP: điểm MMR + thắng/thua (đồng bộ cloud). Mặc định 1000 cho tài khoản cũ (JsonUtility giữ initializer khi thiếu field).
            public int rankMmr = 1000;
            public int rankWins = 0;
            public int rankLosses = 0;
        }

        static ProgressDTO _cache;
        // ★ CHỐT AN TOÀN: lần Load gần nhất có ĐỌC ĐƯỢC không. Đọc LỖI → cấm Save ghi đè (tránh mất save thật).
        static bool _loadOk;

        /// <summary>Bắn MỖI khi tiến độ thay đổi (Save) → lớp cloud (PlayFabAccount) nghe để đẩy lên server.</summary>
        public static event System.Action OnChanged;

        /// <summary>Bỏ cache → lần đọc sau nạp lại từ file. Gọi SAU khi cloud ghi đè file save (PullFromCloud).</summary>
        public static void Reload() { _cache = null; _loadOk = false; }
        static string FilePath => Path.Combine(Application.persistentDataPath, "campaign_progress.json");
        static string BakPath => FilePath + ".bak";
        static string TmpPath => FilePath + ".tmp";

        // Đọc 1 file JSON → DTO. Trả null nếu file KHÔNG tồn tại / RỖNG. NÉM exception nếu JSON hỏng (caller xử lý).
        static ProgressDTO TryReadFile(string path)
        {
            if (!File.Exists(path)) return null;
            string json = File.ReadAllText(path);
            if (string.IsNullOrWhiteSpace(json)) return null;   // file rỗng ≠ save hợp lệ → đừng nhận rỗng
            return JsonUtility.FromJson<ProgressDTO>(json);     // JSON hỏng → ném → nhánh catch
        }

        static ProgressDTO Load()
        {
            if (_cache != null) return _cache;
            try
            {
                var dto = TryReadFile(FilePath);
                if (dto != null) { _cache = dto; _loadOk = true; return _cache; }

                // File chính trống/không có → thử .bak TRƯỚC khi coi là tài khoản mới.
                var bak = TryReadFile(BakPath);
                if (bak != null)
                {
                    Debug.LogWarning("[ProgressStore] File chính trống — KHÔI PHỤC từ .bak.");
                    _cache = bak; _loadOk = true; return _cache;
                }

                // Thật sự chưa có save (tài khoản mới) → rỗng, CHO PHÉP ghi.
                _cache = new ProgressDTO(); _loadOk = true;
            }
            catch (Exception e)
            {
                // ĐỌC LỖI (JSON hỏng / file bị khoá): thử .bak; vẫn hỏng → CẤM Save để KHÔNG ghi đè save thật.
                Debug.LogError($"[ProgressStore] ĐỌC LỖI: {e.Message} — thử .bak & CẤM ghi đè.");
                try
                {
                    var bak = TryReadFile(BakPath);
                    if (bak != null) { _cache = bak; _loadOk = true; Debug.LogWarning("[ProgressStore] Khôi phục từ .bak OK."); return _cache; }
                }
                catch (Exception e2) { Debug.LogWarning($"[ProgressStore] .bak cũng lỗi: {e2.Message}"); }
                _cache = new ProgressDTO(); _loadOk = false;   // ★ chốt: đọc lỗi → cấm Save đè
            }
            return _cache;
        }

        static void Save()
        {
            if (!_loadOk)
            {
                Debug.LogWarning("[ProgressStore] BỎ QUA Save — lần đọc trước LỖI, tránh ghi đè save thật. " +
                                 "Khôi phục/sửa file rồi khởi động lại game.");
                return;
            }
            try
            {
                string json = JsonUtility.ToJson(Load(), true);
                File.WriteAllText(TmpPath, json);                            // 1) ghi file TẠM
                if (File.Exists(FilePath)) File.Copy(FilePath, BakPath, true); // 2) sao lưu bản cũ → .bak
                if (File.Exists(FilePath)) File.Delete(FilePath);
                File.Move(TmpPath, FilePath);                                // 3) đổi tên nguyên tử → không để file dở
            }
            catch (Exception e) { Debug.LogError($"[ProgressStore] Lỗi ghi tiến độ: {e.Message}"); }
            try { OnChanged?.Invoke(); } catch (Exception e) { Debug.LogWarning($"[ProgressStore] OnChanged: {e.Message}"); }
        }

        public static int HighestCompleted => Load().highestCompleted;
        public static bool IsLevelCompleted(int index) => index <= HighestCompleted;
        public static bool IsLevelUnlocked(int index) => index <= HighestCompleted + 1;
        public static bool IsCardUnlocked(string cardName) => Load().unlockedCards.Contains(cardName);
        public static IReadOnlyList<string> UnlockedCards => Load().unlockedCards;

        /// <summary>
        /// Ghi nhận thắng tầng. Trả về true nếu là LẦN ĐẦU thắng tầng này
        /// (lần đầu mới unlock quà).
        /// </summary>
        public static bool CompleteLevel(int index, List<string> rewardCardNames)
        {
            var p = Load();
            bool firstTime = index > p.highestCompleted;
            if (firstTime)
            {
                p.highestCompleted = index;
                if (rewardCardNames != null)
                    foreach (var n in rewardCardNames)
                        if (!string.IsNullOrEmpty(n) && !p.unlockedCards.Contains(n))
                            p.unlockedCards.Add(n);
                Save();
                Debug.Log($"[ProgressStore] Thắng tầng {index + 1} lần đầu — unlock {rewardCardNames?.Count ?? 0} card.");
            }
            return firstTime;
        }

        // ── Mốc 3: Champion Mastery ───────────────────────────────
        // Level = 1 + xp/100 (mỗi 100 XP 1 cấp), tối đa 30. XP tích lũy giữa các run.
        public const int XpPerLevel = 100;
        public const int MaxMasteryLevel = 30;

        public static int GetMasteryXp(string championName)
        {
            var p = Load();
            int i = p.masteryNames.IndexOf(championName ?? "");
            return i >= 0 ? p.masteryXp[i] : 0;
        }

        // Tổng XP tích luỹ để ĐẠT mỗi cấp — theo đúng Path of Champions (index = cấp; 1..30).
        static readonly int[] LevelTotalXp =
        {
            0,      // [0] không dùng
            0, 50, 150, 300, 500, 800, 1250, 1750, 2310, 2980,       // 1-10
            3780, 4710, 5780, 6990, 8350, 9870, 11560, 13420, 15460, 17680, // 11-20
            20090, 22700, 25510, 28530, 31760, 35210, 38880, 42780, 46920, 51290 // 21-30
        };

        /// <summary>Cấp (1..30) từ tổng XP, theo đường cong PoC.</summary>
        public static int LevelFromXp(int xp)
        {
            int lvl = 1;
            for (int L = 1; L < LevelTotalXp.Length; L++)
            {
                if (xp >= LevelTotalXp[L]) lvl = L; else break;
            }
            return Mathf.Clamp(lvl, 1, MaxMasteryLevel);
        }

        /// <summary>Tổng XP cần để ĐẠT 'level' (mốc sàn của cấp đó).</summary>
        public static int XpFloorForLevel(int level)
        {
            level = Mathf.Clamp(level, 1, LevelTotalXp.Length - 1);
            return LevelTotalXp[level];
        }

        public static int GetMasteryLevel(string championName)
            => LevelFromXp(GetMasteryXp(championName));

        // ── Cấp SAO (1→6) suy ra từ mastery. Sao cao = vào run mạnh hơn (M7). ──
        // Công thức đơn giản, dễ chỉnh: mỗi 2 cấp mastery = +1 sao.
        public const int MaxStar = 6;
        public static int GetStar(string championName)
            => Mathf.Clamp(1 + (GetMasteryLevel(championName) - 1) / 2, 1, MaxStar);
        /// <summary>Sao ứng với 1 cấp bất kỳ (dùng cho bảng phần thưởng theo cấp).</summary>
        public static int StarAtLevel(int level) => Mathf.Clamp(1 + (level - 1) / 2, 1, MaxStar);

        /// <summary>Mỗi cấp (không phải cấp lên sao) → +máu Nexus khởi đầu. Cộng dồn theo cấp.</summary>
        public const int NexusPerLevel = 2;

        /// <summary>Mastery level cần để lên sao kế (cho UI). 0 nếu đã max sao.</summary>
        public static int MasteryLevelForNextStar(string championName)
        {
            int star = GetStar(championName);
            if (star >= MaxStar) return 0;
            return 1 + star * 2; // đảo công thức GetStar
        }

        /// <summary>Cộng XP mastery cho champion và lưu. Trả về true nếu vừa LÊN CẤP.</summary>
        public static bool AddMasteryXp(string championName, int amount)
        {
            if (string.IsNullOrEmpty(championName) || amount <= 0) return false;
            var p = Load();
            int i = p.masteryNames.IndexOf(championName);
            if (i < 0) { p.masteryNames.Add(championName); p.masteryXp.Add(0); i = p.masteryNames.Count - 1; }
            int before = Mathf.Clamp(1 + p.masteryXp[i] / XpPerLevel, 1, MaxMasteryLevel);
            p.masteryXp[i] += amount;
            int after = Mathf.Clamp(1 + p.masteryXp[i] / XpPerLevel, 1, MaxMasteryLevel);
            Save();
            Debug.Log($"[ProgressStore] {championName} +{amount} XP (Lv {after}).");
            return after > before;
        }

        // ── Mốc 9: Tinh Hồn + Tinh Hồn ────────────────────────────
        public static int Essence => Load().essence;

        public static void AddEssence(int amount)
        {
            if (amount <= 0) return;
            Load().essence += amount;
            Save();
        }

        // ── MỐC 11: Lõi Cường Hóa (Enhancement Core) — currency FARM ──────
        // Rơi từ node thắng (Boss nặng nhất). Tiêu để NÂNG CẤP trang bị đã lắp.
        // Toàn cục theo account (giống Essence), tích lũy vĩnh viễn qua các run.
        public static int Cores => Load().cores;

        public static void AddCores(int amount)
        {
            if (amount <= 0) return;
            Load().cores += amount;
            Save();
            Debug.Log($"[ProgressStore] +{amount} Lõi Cường Hóa (tổng {Load().cores}).");
        }

        /// <summary>Tiêu Lõi nếu đủ. Trả về true nếu thành công.</summary>
        public static bool SpendCores(int amount)
        {
            if (amount <= 0) return true;
            var p = Load();
            if (p.cores < amount) return false;
            p.cores -= amount;
            Save();
            return true;
        }

        static string ConstKey(string champ, int index) => $"{champ}|{index}";

        public static bool IsConstellationBought(string champ, int index)
            => Load().constellationBought.Contains(ConstKey(champ, index));

        /// <summary>Mua node tinh hồn nếu đủ Tinh Hồn. Trả về true nếu mua thành công.</summary>
        public static bool BuyConstellation(string champ, int index, int cost)
        {
            var p = Load();
            string key = ConstKey(champ, index);
            if (p.constellationBought.Contains(key)) return false;
            if (p.essence < cost) return false;
            p.essence -= cost;
            p.constellationBought.Add(key);
            Save();
            Debug.Log($"[ProgressStore] Mua tinh hồn {key} (-{cost} Tinh Hồn).");
            return true;
        }

        // ── Mốc 8: Trang Bị — slot + đeo/tháo ─────────────────────
        public const int MaxItemSlots = 3;
        /// <summary>Số slot trang bị theo sao: sao càng cao càng nhiều slot (1→3).</summary>
        public static int ItemSlots(string champ) => Mathf.Clamp(1 + (GetStar(champ) - 1) / 2, 1, MaxItemSlots);

        static string EquipKey(string champ, string item) => $"{champ}|{item}";

        public static bool IsItemEquipped(string champ, string item)
            => Load().equippedItems.Contains(EquipKey(champ, item));

        public static int EquippedCount(string champ)
        {
            int n = 0; string pre = champ + "|";
            foreach (var e in Load().equippedItems) if (e.StartsWith(pre)) n++;
            return n;
        }

        /// <summary>Đeo item nếu còn slot. Trả về true nếu thành công.</summary>
        public static bool EquipItem(string champ, string item)
        {
            var p = Load();
            string key = EquipKey(champ, item);
            if (p.equippedItems.Contains(key)) return false;
            if (EquippedCount(champ) >= ItemSlots(champ)) return false;
            p.equippedItems.Add(key);
            Save();
            return true;
        }

        public static void UnequipItem(string champ, string item)
        {
            var p = Load();
            if (p.equippedItems.Remove(EquipKey(champ, item))) Save();
        }

        /// <summary>Gỡ mọi key equip của champ mà item KHÔNG thuộc validItemNames
        /// (key cũ ItemData / item đã xoá khỏi library) → tránh đếm nhầm đầy slot.</summary>
        public static void PruneEquipped(string champ, System.Collections.Generic.ICollection<string> validItemNames)
        {
            var p = Load(); string pre = champ + "|"; bool changed = false;
            for (int i = p.equippedItems.Count - 1; i >= 0; i--)
            {
                string e = p.equippedItems[i];
                if (!e.StartsWith(pre)) continue;
                string name = e.Substring(pre.Length);
                if (validItemNames == null || !validItemNames.Contains(name)) { p.equippedItems.RemoveAt(i); changed = true; }
            }
            if (changed) Save();
        }

        // ── Phần Chest: SỞ HỮU trang bị (nhặt từ rương, thay cửa mở-theo-mastery) ──
        public static bool IsItemOwned(string item)
            => !string.IsNullOrEmpty(item) && Load().ownedItems.Contains(item);

        public static IReadOnlyList<string> OwnedItems => Load().ownedItems;

        /// <summary>Cho sở hữu 1 trang bị (rương rơi). Trả về true nếu LẦN ĐẦU sở hữu.</summary>
        public static bool GrantItem(string item)
        {
            if (string.IsNullOrEmpty(item)) return false;
            var p = Load();
            if (p.ownedItems.Contains(item)) return false;
            p.ownedItems.Add(item);
            Save();
            return true;
        }

        // ── MỐC 14: RƯƠNG BÁU (kho tiêu hao) + mở khoá card ───────
        static void EnsureChests(ProgressDTO p) { while (p.chests.Count < 4) p.chests.Add(0); }

        public static int ChestCount(int tier)
        {
            var p = Load(); EnsureChests(p);
            return (tier >= 0 && tier < 4) ? p.chests[tier] : 0;
        }

        public static void AddChest(int tier)
        {
            if (tier < 0 || tier > 3) return;
            var p = Load(); EnsureChests(p);
            p.chests[tier]++; Save();
            Debug.Log($"[ProgressStore] +1 rương bậc {tier} (còn {p.chests[tier]}).");
        }

        /// <summary>Lấy (tiêu) 1 rương bậc này để mở. Trả về true nếu còn rương.</summary>
        public static bool TakeChest(int tier)
        {
            if (tier < 0 || tier > 3) return false;
            var p = Load(); EnsureChests(p);
            if (p.chests[tier] <= 0) return false;
            p.chests[tier]--; Save();
            return true;
        }

        /// <summary>Mở khoá 1 lá bài (rương rơi) cho Xưởng Deck. True nếu LẦN ĐẦU.</summary>
        public static bool UnlockCard(string cardName)
        {
            if (string.IsNullOrEmpty(cardName)) return false;
            var p = Load();
            if (p.unlockedCards.Contains(cardName)) return false;
            p.unlockedCards.Add(cardName); Save();
            return true;
        }

        // ── RANK PvP (MMR + W/L) — lưu local + đồng bộ cloud qua OnChanged ──
        public const int RankStartMmr = 1000;
        public static int RankMmr => Load().rankMmr;
        public static int RankWins => Load().rankWins;
        public static int RankLosses => Load().rankLosses;

        /// <summary>Áp kết quả 1 trận rank: THẮNG +winDelta, THUA -lossDelta (sàn 'floor'). Trả MMR mới.</summary>
        public static int ApplyRankResult(bool win, int winDelta, int lossDelta, int floor)
        {
            var p = Load();
            if (win) { p.rankMmr += Mathf.Max(0, winDelta); p.rankWins++; }
            else { p.rankMmr = Mathf.Max(floor, p.rankMmr - Mathf.Max(0, lossDelta)); p.rankLosses++; }
            Save();   // → OnChanged → UgsAccount tự đẩy MMR mới lên cloud
            Debug.Log($"[ProgressStore] Rank {(win ? "+" + winDelta : "-" + lossDelta)} → MMR {p.rankMmr} (W{p.rankWins}/L{p.rankLosses}).");
            return p.rankMmr;
        }

        /// <summary>Xóa toàn bộ tiến độ (debug / chơi lại từ đầu).</summary>
        public static void ResetProgress()
        {
            _cache = new ProgressDTO();
            _loadOk = true;   // reset CHỦ Ý → cho phép Save ghi lại (khác với đọc-lỗi)
            try { if (File.Exists(FilePath)) File.Delete(FilePath); }
            catch (Exception e) { Debug.LogWarning($"[ProgressStore] {e.Message}"); }
            Debug.Log("[ProgressStore] Đã reset tiến độ Leo Tháp.");
        }
    }
}