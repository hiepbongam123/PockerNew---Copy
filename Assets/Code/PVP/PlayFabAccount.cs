// ============================================================================
//  PlayFabAccount — TÀI KHOẢN + CLOUD SAVE + LEADERBOARD qua PlayFab.
//
//  Ý tưởng: GIỮ NGUYÊN save local (ProgressStore JSON + PlayerPrefs deck) làm CACHE.
//    • Login (device id, không cần nhập gì) → KÉO cloud về → ghi đè file local → chơi.
//    • Data đổi (ProgressStore.OnChanged) → ĐẨY lên cloud (debounce 3s + lúc thoát).
//    • Rank: nộp 1 chỉ số MMR → PlayFab tự xếp hạng → GetLeaderboard trả bảng.
//  KHÔNG đổi format save nào — chỉ upload/download nguyên văn JSON đang có.
//
//  ── SETUP (làm 1 lần) ──────────────────────────────────────────────────────
//  1) Tạo tài khoản playfab.com → New Title → lấy TITLE ID (Game Manager ▸ Settings ▸ API).
//  2) Cài SDK (bản CLASSIC v1 — dùng PlayFabClientAPI như file này):
//        • Tải .unitypackage: https://aka.ms/playfabunitysdkdownload
//        • Double-click file đó (hoặc Assets ▸ Import Package ▸ Custom Package…) → Import hết.
//        • KHÔNG dùng "Install package from git URL" — PlayFab classic không phát hành dạng UPM git.
//        • (Bản "v2 Unified" là UPM nhưng ĐỔI API + chỉ Windows/Xbox → đừng dùng cho file này.)
//  3) Đặt Title ID: chọn asset Assets/PlayFabSdk/Shared/Public/Resources/PlayFabSettings, điền ở Inspector.
//  4) Thêm symbol PLAYFAB_SDK vào Player Settings ▸ Scripting Define Symbols để BẬT file này.
//  5) Kéo script lên 1 GameObject sống suốt game (VD cùng object LobbyManager); điền Title Id nếu muốn.
//  6) Bật Leaderboard: Game Manager ▸ Leaderboards ▸ New (Statistic name = "rank_mmr",
//     Aggregation = Maximum hoặc Last). Xong.
//
//  Gọi khi có kết quả rank:  PlayFabAccount.Instance?.SubmitRank(newMmr);
//  Lấy bảng xếp hạng:        PlayFabAccount.Instance?.FetchLeaderboard(list => { ... });
//
//  (Đã BỎ công tắc #if PLAYFAB_SDK vì SDK đã cài — file biên dịch trực tiếp.)
// ============================================================================
using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;
using PlayFab;
using PlayFab.ClientModels;

namespace LoRClone.Net
{
    public class PlayFabAccount : MonoBehaviour
    {
        public static PlayFabAccount Instance { get; private set; }

        [Tooltip("Title ID ở PlayFab (Game Manager ▸ Settings ▸ API). Để trống nếu đã set trong PlayFabSharedSettings.")]
        public string titleId = "";

        public bool IsLoggedIn { get; private set; }
        public string PlayFabId { get; private set; }
        public event Action OnSynced;   // gọi SAU khi kéo cloud về + reload local (UI nghe để refresh)

        const string DeviceKey = "pf_device_id";  // GUID định danh máy (lưu local)
        const string ProgressKey = "progress";    // UserData key ↔ campaign_progress.json
        const string DecksKey = "decks";          // UserData key ↔ deck tự tạo
        const string RankStat = "rank_mmr";       // tên Statistic/Leaderboard

        static string ProgressPath => Path.Combine(Application.persistentDataPath, "campaign_progress.json");
        bool _pushPending;

        void Awake()
        {
            if (Instance != null && Instance != this) { Destroy(gameObject); return; }
            Instance = this;
            DontDestroyOnLoad(gameObject);
        }

        void OnEnable() { LoRClone.Data.ProgressStore.OnChanged += MarkDirty; }
        void OnDisable() { LoRClone.Data.ProgressStore.OnChanged -= MarkDirty; }

        void Start()
        {
            if (!string.IsNullOrEmpty(titleId)) PlayFabSettings.TitleId = titleId;
            Login();
        }

        // ── LOGIN ẩn danh bằng device id (GUID lưu local). Sau có thể nâng cấp lên email. ──
        public void Login()
        {
            string id = PlayerPrefs.GetString(DeviceKey, "");
            if (string.IsNullOrEmpty(id))
            {
                id = Guid.NewGuid().ToString("N");
                PlayerPrefs.SetString(DeviceKey, id); PlayerPrefs.Save();
            }
            PlayFabClientAPI.LoginWithCustomID(new LoginWithCustomIDRequest { CustomId = id, CreateAccount = true },
                ok =>
                {
                    IsLoggedIn = true; PlayFabId = ok.PlayFabId;
                    Debug.Log($"[PlayFab] Login OK — id={PlayFabId}");
                    PullFromCloud();
                },
                err => Debug.LogWarning("[PlayFab] Login lỗi: " + err.GenerateErrorReport()));
        }

        // ── PULL: cloud → file local → reload cache → báo UI ──
        public void PullFromCloud()
        {
            if (!IsLoggedIn) return;
            PlayFabClientAPI.GetUserData(new GetUserDataRequest(), res =>
            {
                var data = res.Data;
                if (data != null && data.TryGetValue(ProgressKey, out var p) && !string.IsNullOrEmpty(p?.Value))
                {
                    try { File.WriteAllText(ProgressPath, p.Value); }
                    catch (Exception e) { Debug.LogWarning("[PlayFab] ghi progress: " + e.Message); }
                    LoRClone.Data.ProgressStore.Reload();   // bỏ cache → đọc data cloud
                }
                if (data != null && data.TryGetValue(DecksKey, out var d) && !string.IsNullOrEmpty(d?.Value))
                    RestoreDecks(d.Value);

                Debug.Log("[PlayFab] Đã đồng bộ cloud → local.");
                OnSynced?.Invoke();
            },
            err => Debug.LogWarning("[PlayFab] GetUserData lỗi: " + err.GenerateErrorReport()));
        }

        // ── PUSH: local → cloud (gộp nhiều thay đổi trong 3s thành 1 lần) ──
        public void MarkDirty()
        {
            if (!IsLoggedIn) return;
            _pushPending = true;
            CancelInvoke(nameof(PushNow));
            Invoke(nameof(PushNow), 3f);
        }

        public void PushNow()
        {
            _pushPending = false;
            if (!IsLoggedIn) return;
            var data = new Dictionary<string, string>();
            try { if (File.Exists(ProgressPath)) data[ProgressKey] = File.ReadAllText(ProgressPath); } catch { }
            data[DecksKey] = SnapshotDecks();
            if (data.Count == 0) return;
            PlayFabClientAPI.UpdateUserData(new UpdateUserDataRequest { Data = data },
                _ => Debug.Log("[PlayFab] Đã đẩy local → cloud."),
                err => Debug.LogWarning("[PlayFab] UpdateUserData lỗi: " + err.GenerateErrorReport()));
        }

        void OnApplicationPause(bool paused) { if (paused && _pushPending) PushNow(); }
        void OnApplicationQuit() { if (_pushPending) PushNow(); }

        // ── LEADERBOARD / RANK ──
        public void SubmitRank(int mmr)
        {
            if (!IsLoggedIn) return;
            PlayFabClientAPI.UpdatePlayerStatistics(new UpdatePlayerStatisticsRequest
            {
                Statistics = new List<StatisticUpdate> { new StatisticUpdate { StatisticName = RankStat, Value = mmr } }
            },
            _ => Debug.Log($"[PlayFab] Đã nộp rank {mmr}."),
            err => Debug.LogWarning("[PlayFab] Nộp rank lỗi: " + err.GenerateErrorReport()));
        }

        public void FetchLeaderboard(Action<List<PlayerLeaderboardEntry>> cb, int top = 50)
        {
            PlayFabClientAPI.GetLeaderboard(new GetLeaderboardRequest
            { StatisticName = RankStat, StartPosition = 0, MaxResultsCount = top },
            res => cb?.Invoke(res.Leaderboard),
            err => { Debug.LogWarning("[PlayFab] Leaderboard lỗi: " + err.GenerateErrorReport()); cb?.Invoke(null); });
        }

        // ── Deck tự tạo (CustomDeckStore) ↔ JSON (JsonUtility) ──
        [Serializable] class DeckBlob { public List<DeckEntry> decks = new List<DeckEntry>(); }
        [Serializable] class DeckEntry { public string name; public List<string> cards; }

        string SnapshotDecks()
        {
            var blob = new DeckBlob();
            foreach (var d in LoRClone.Data.CustomDeckStore.LoadAll())
                if (d != null) blob.decks.Add(new DeckEntry { name = d.name, cards = d.cardNames });
            return JsonUtility.ToJson(blob);
        }

        void RestoreDecks(string json)
        {
            DeckBlob blob;
            try { blob = JsonUtility.FromJson<DeckBlob>(json); } catch { return; }
            if (blob?.decks == null) return;
            foreach (var e in blob.decks)
                if (!string.IsNullOrEmpty(e.name) && e.cards != null)
                    LoRClone.Data.CustomDeckStore.SaveDeck(e.name, e.cards);
        }
    }
}