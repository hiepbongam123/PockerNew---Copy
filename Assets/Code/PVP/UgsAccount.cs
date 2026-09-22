// ============================================================================
//  UgsAccount — TÀI KHOẢN + CLOUD SAVE + LEADERBOARD qua Unity Gaming Services (UGS).
//  Thay cho PlayFab. Đăng nhập ẨN DANH của UGS KHÔNG có rào "tạo player bị khóa".
//
//  Giữ nguyên save local (ProgressStore JSON + deck) làm CACHE:
//    • Vào game → init + sign-in ẩn danh → KÉO cloud về → chơi.
//    • Data đổi (ProgressStore.OnChanged) → ĐẨY lên cloud (debounce 3s + lúc thoát).
//    • Rank: nộp điểm → UGS Leaderboards tự xếp hạng.
//
//  ── SETUP (làm 1 lần) ──────────────────────────────────────────────────────
//  1) LIÊN KẾT PROJECT: Edit ▸ Project Settings ▸ Services → đăng nhập tài khoản Unity →
//     tạo/liên kết 1 Unity Cloud project (nút "Link"/"Create"). Đây là bước bắt buộc để UGS chạy.
//  2) CÀI GÓI (Package Manager ▸ Unity Registry, hoặc "Add package by name"):
//        com.unity.services.authentication
//        com.unity.services.cloudsave
//        com.unity.services.leaderboards
//     (com.unity.services.core tự kéo theo.) CÀI 3 GÓI NÀY TRƯỚC KHI thêm script này vào project.
//  3) DASHBOARD (dashboard.unity.com ▸ project của bạn):
//        • Authentication: "Anonymous" đã bật sẵn — không cần làm gì.
//        • Cloud Save: không cần cấu hình.
//        • Leaderboards: tạo 1 leaderboard, ID = "rank_mmr", sort = Descending (điểm cao đứng đầu).
//  4) Kéo script này lên 1 GameObject sống suốt game (VD object có LobbyManager).
//  5) Xoá/gỡ PlayFabAccount (component + có thể xoá cả thư mục PlayFabSDK) để khỏi lẫn.
//  6) Play → Console thấy "[UGS] Đăng nhập ẩn danh OK — playerId=..." là chạy.
//
//  Nộp rank:        UgsAccount.Instance?.SubmitRank(mmr);
//  Lấy bảng:        UgsAccount.Instance?.FetchLeaderboard(list => { ... });
// ============================================================================
using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using UnityEngine;
using Unity.Services.Core;
using Unity.Services.Authentication;
using Unity.Services.CloudSave;
using Unity.Services.Leaderboards;
using Unity.Services.Leaderboards.Models;

namespace LoRClone.Net
{
    public class UgsAccount : MonoBehaviour
    {
        public static UgsAccount Instance { get; private set; }

        [Tooltip("Leaderboard ID tạo ở Unity Cloud Dashboard (Leaderboards). Phải KHỚP tên bên dashboard.")]
        public string leaderboardId = "rank_mmr";

        public bool IsSignedIn { get; private set; }
        public event Action OnSynced;   // gọi SAU khi kéo cloud về + reload local

        /// <summary>PlayerId của mình (để tô sáng dòng của mình trong bảng xếp hạng). null nếu chưa login.</summary>
        public string PlayerId =>
            (AuthenticationService.Instance != null && AuthenticationService.Instance.IsSignedIn)
                ? AuthenticationService.Instance.PlayerId : null;

        /// <summary>Tên hiển thị hiện tại của UGS (dạng "Ten#1234"). null nếu chưa login/chưa đặt.</summary>
        public string PlayerName =>
            (AuthenticationService.Instance != null && AuthenticationService.Instance.IsSignedIn)
                ? AuthenticationService.Instance.PlayerName : null;

        /// <summary>Phần TÊN không kèm mã "#1234" — dùng để prefill ô đổi tên.</summary>
        public string PlayerNameBase
        {
            get { var n = PlayerName; if (string.IsNullOrEmpty(n)) return ""; int i = n.IndexOf('#'); return i >= 0 ? n.Substring(0, i) : n; }
        }

        const string NameKey = "player_display_name";   // PlayerPrefs — tên người chơi chọn

        const string ProgressKey = "progress";   // Cloud Save key ↔ campaign_progress.json
        const string DecksKey = "decks";          // Cloud Save key ↔ deck tự tạo
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

        async void Start() { await InitAndSignIn(); }

        // ── Init UGS + đăng nhập ẨN DANH (không cần nhập gì, không rào tạo player) ──
        async Task InitAndSignIn()
        {
            try
            {
                if (UnityServices.State != ServicesInitializationState.Initialized)
                    await UnityServices.InitializeAsync();

                if (!AuthenticationService.Instance.IsSignedIn)
                    await AuthenticationService.Instance.SignInAnonymouslyAsync();

                IsSignedIn = true;
                Debug.Log($"[UGS] Đăng nhập ẩn danh OK — playerId={AuthenticationService.Instance.PlayerId}");
                await EnsurePlayerName();   // đảm bảo có TÊN hiển thị (bảng xếp hạng cần tên, không chỉ id)
                await PullFromCloud();
            }
            catch (Exception e)
            {
                Debug.LogWarning("[UGS] Init/SignIn lỗi: " + e.Message +
                                 " (đã Link project ở Project Settings ▸ Services chưa?)");
            }
        }

        // ── PULL: cloud → file local → reload cache → báo UI ──
        public async Task PullFromCloud()
        {
            if (!IsSignedIn) return;
            try
            {
                var data = await CloudSaveService.Instance.Data.Player.LoadAsync(
                    new HashSet<string> { ProgressKey, DecksKey });

                if (data.TryGetValue(ProgressKey, out var p))
                {
                    string json = p.Value.GetAs<string>();
                    if (!string.IsNullOrEmpty(json))
                    {
                        try { File.WriteAllText(ProgressPath, json); }
                        catch (Exception e) { Debug.LogWarning("[UGS] ghi progress: " + e.Message); }
                        LoRClone.Data.ProgressStore.Reload();   // bỏ cache → đọc data cloud
                    }
                }
                if (data.TryGetValue(DecksKey, out var d))
                {
                    string dj = d.Value.GetAs<string>();
                    if (!string.IsNullOrEmpty(dj)) RestoreDecks(dj);
                }
                Debug.Log("[UGS] Đã đồng bộ cloud → local.");
                OnSynced?.Invoke();
            }
            catch (Exception e) { Debug.LogWarning("[UGS] Load lỗi: " + e.Message); }
        }

        // ── PUSH: local → cloud (gộp nhiều thay đổi trong 3s thành 1 lần) ──
        public void MarkDirty()
        {
            if (!IsSignedIn) return;
            _pushPending = true;
            CancelInvoke(nameof(PushNowInvoke));
            Invoke(nameof(PushNowInvoke), 3f);
        }
        void PushNowInvoke() { _ = PushNow(); }   // Invoke không gọi async Task trực tiếp được

        public async Task PushNow()
        {
            _pushPending = false;
            if (!IsSignedIn) return;
            try
            {
                var data = new Dictionary<string, object>();
                try { if (File.Exists(ProgressPath)) data[ProgressKey] = File.ReadAllText(ProgressPath); } catch { }
                data[DecksKey] = SnapshotDecks();
                if (data.Count == 0) return;
                await CloudSaveService.Instance.Data.Player.SaveAsync(data);
                Debug.Log("[UGS] Đã đẩy local → cloud.");
            }
            catch (Exception e) { Debug.LogWarning("[UGS] Save lỗi: " + e.Message); }
        }

        void OnApplicationPause(bool paused) { if (paused && _pushPending) _ = PushNow(); }
        void OnApplicationQuit() { if (_pushPending) _ = PushNow(); }

        // ── TÊN NGƯỜI CHƠI ──────────────────────────────────────────
        // UGS chỉ nhận tên [A-Za-z0-9._-], 1–50 ký tự, KHÔNG dấu, KHÔNG khoảng trắng, KHÔNG '#'
        // (UGS tự thêm mã "#1234" phía sau). Space → '_'; ký tự có dấu/khác → bỏ.
        static string Sanitize(string s)
        {
            var sb = new System.Text.StringBuilder();
            foreach (char c in (s ?? "").Trim())
            {
                if ((c >= 'A' && c <= 'Z') || (c >= 'a' && c <= 'z') || (c >= '0' && c <= '9')
                    || c == '.' || c == '_' || c == '-') sb.Append(c);
                else if (c == ' ') sb.Append('_');
            }
            string r = sb.ToString();
            if (r.Length > 20) r = r.Substring(0, 20);
            if (r.Length < 1) r = "Player" + (AuthenticationService.Instance?.PlayerId ?? "0000")
                                    .Substring(0, 4);
            return r;
        }

        /// <summary>Login xong → nếu đã lưu tên thì áp lại; nếu chưa từng đặt và UGS cũng chưa có tên → tạo tên mặc định.</summary>
        async Task EnsurePlayerName()
        {
            try
            {
                string want = PlayerPrefs.GetString(NameKey, "");
                string curBase = PlayerNameBase;
                if (string.IsNullOrEmpty(want))
                {
                    if (!string.IsNullOrEmpty(curBase)) return;               // UGS đã có tên → thôi
                    want = "Player" + AuthenticationService.Instance.PlayerId.Substring(0, 4);
                }
                string clean = Sanitize(want);
                if (clean != curBase)
                    await AuthenticationService.Instance.UpdatePlayerNameAsync(clean);
                Debug.Log("[UGS] Tên hiển thị: " + AuthenticationService.Instance.PlayerName);
            }
            catch (Exception e) { Debug.LogWarning("[UGS] Đặt tên lỗi: " + e.Message); }
        }

        /// <summary>Người chơi đổi tên trong Bảng Xếp Hạng. Lưu local + đẩy lại điểm để bảng hiện tên mới ngay.</summary>
        public async void SetPlayerName(string raw)
        {
            if (!IsSignedIn || string.IsNullOrWhiteSpace(raw)) return;
            string clean = Sanitize(raw);
            try
            {
                await AuthenticationService.Instance.UpdatePlayerNameAsync(clean);
                PlayerPrefs.SetString(NameKey, clean); PlayerPrefs.Save();
                // Đẩy lại điểm (Keep Latest) để entry trên bảng cập nhật tên mới.
                await LeaderboardsService.Instance.AddPlayerScoreAsync(leaderboardId, LoRClone.Data.ProgressStore.RankMmr);
                Debug.Log("[UGS] Đổi tên → " + AuthenticationService.Instance.PlayerName);
            }
            catch (Exception e) { Debug.LogWarning("[UGS] Đổi tên lỗi: " + e.Message); }
        }

        // ── LEADERBOARD / RANK ──
        public async void SubmitRank(int mmr)
        {
            if (!IsSignedIn) return;
            try
            {
                await LeaderboardsService.Instance.AddPlayerScoreAsync(leaderboardId, mmr);
                Debug.Log($"[UGS] Đã nộp rank {mmr}.");
            }
            catch (Exception e) { Debug.LogWarning("[UGS] Nộp rank lỗi: " + e.Message); }
        }

        /// <summary>Lấy top bảng xếp hạng. Trả về danh sách LeaderboardEntry (PlayerId/PlayerName/Rank/Score).</summary>
        public async void FetchLeaderboard(Action<List<LeaderboardEntry>> cb, int top = 50)
        {
            try
            {
                var page = await LeaderboardsService.Instance.GetScoresAsync(
                    leaderboardId, new GetScoresOptions { Offset = 0, Limit = top });
                cb?.Invoke(page.Results);
            }
            catch (Exception e) { Debug.LogWarning("[UGS] Leaderboard lỗi: " + e.Message); cb?.Invoke(null); }
        }

        /// <summary>Lấy HẠNG của chính mình (Rank/Score). null nếu chưa từng nộp điểm.</summary>
        public async void FetchMyRank(Action<LeaderboardEntry> cb)
        {
            if (!IsSignedIn) { cb?.Invoke(null); return; }
            try { cb?.Invoke(await LeaderboardsService.Instance.GetPlayerScoreAsync(leaderboardId)); }
            catch { cb?.Invoke(null); }   // chưa có điểm trên bảng
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