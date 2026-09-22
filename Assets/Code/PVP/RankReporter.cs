using UnityEngine;
using UnityEngine.UI;
using TMPro;
using LoRClone.Controller;
using LoRClone.Model;

namespace LoRClone.Net
{
    /// <summary>
    /// GHI ĐIỂM RANK cuối trận + HIỆN BANNER điểm ngay trên màn kết thúc — ĐỘC LẬP, không cần sửa GameController.
    /// Tự theo dõi GameController.Instance.model; mỗi trận mới → BeginMatch, subscribe OnGameOver →
    /// RankSystem.ReportMatch (chỉ tính trận MẠNG). Nếu trận có tính hạng → dựng 1 banner "+25 → MMR 1025".
    ///
    /// CÀI: kéo component này lên 1 GameObject sống suốt game (VD cùng object UgsAccount / LobbyManager).
    /// DontDestroyOnLoad → tự bắt mọi trận. Trận PvE/solo KHÔNG hiện banner (không tính hạng).
    /// </summary>
    public class RankReporter : MonoBehaviour
    {
        static RankReporter _instance;
        GameModel _lastModel;
        bool _hooked, _bannerShown;

        GameObject _banner;
        TextMeshProUGUI _bannerLbl;
        float _bannerT;   // đếm ngược tự ẩn banner (unscaled — kể cả khi timeScale=0)

        void Awake()
        {
            if (_instance != null && _instance != this) { Destroy(gameObject); return; }
            _instance = this;
            DontDestroyOnLoad(gameObject);
        }

        void Update()
        {
            var gc = GameController.Instance;
            if (gc == null) return;

            var m = gc.model;
            if (!ReferenceEquals(m, _lastModel))   // trận MỚI (model tạo lại mỗi StartGame)
            {
                _lastModel = m;
                _hooked = false;
                _bannerShown = false;
                HideBanner();
                if (m != null) RankSystem.BeginMatch();
            }
            if (m != null && !_hooked)
            {
                m.OnGameOver += OnGameOver;
                _hooked = true;
            }

            // Tự ẩn banner sau vài giây (tránh nằm lì đè lên sảnh/bảng xếp hạng).
            if (_banner != null && _banner.activeSelf)
            {
                _bannerT -= Time.unscaledDeltaTime;
                if (_bannerT <= 0f) _banner.SetActive(false);
            }
        }

        void OnGameOver(string _)
        {
            RankSystem.ReportMatch(GameController.Instance);
            if (RankSystem.Last.counted && !_bannerShown)
            {
                _bannerShown = true;
                ShowBanner(RankSystem.Last);
            }
        }

        // ── Banner điểm (Canvas riêng, nổi trên màn kết thúc) ──
        void ShowBanner(RankSystem.Result r)
        {
            if (_banner == null) BuildBanner();
            string sign = r.delta >= 0 ? "+" : "";
            var col = r.won ? new Color(0.55f, 1f, 0.6f) : new Color(1f, 0.5f, 0.45f);
            _bannerLbl.text = $"XẾP HẠNG   <color=#{ColorUtility.ToHtmlStringRGB(col)}>{sign}{r.delta}</color>" +
                              $"   →   MMR {r.newMmr}   <size=70%><color=#9AA7B8>({r.oldMmr})</color></size>";
            _banner.SetActive(true);
            _banner.transform.SetAsLastSibling();
            _bannerT = 4.5f;   // tự ẩn sau 4.5s
        }
        void HideBanner() { if (_banner != null) _banner.SetActive(false); }

        void BuildBanner()
        {
            var go = new GameObject("RankBanner");
            DontDestroyOnLoad(go);
            var cv = go.AddComponent<Canvas>();
            cv.renderMode = RenderMode.ScreenSpaceOverlay;
            cv.sortingOrder = 900;   // trên mọi UI kể cả màn kết thúc
            go.AddComponent<GraphicRaycaster>();

            var panelGO = new GameObject("Panel", typeof(RectTransform));
            var prt = (RectTransform)panelGO.transform;
            prt.SetParent(go.transform, false);
            prt.anchorMin = new Vector2(0.5f, 0.86f); prt.anchorMax = new Vector2(0.5f, 0.86f);
            prt.pivot = new Vector2(0.5f, 0.5f);
            prt.sizeDelta = new Vector2(520f, 60f);
            var img = panelGO.AddComponent<Image>();
            img.color = new Color(0.05f, 0.07f, 0.11f, 0.94f);

            var lblGO = new GameObject("Lbl", typeof(RectTransform));
            var lrt = (RectTransform)lblGO.transform;
            lrt.SetParent(prt, false);
            lrt.anchorMin = Vector2.zero; lrt.anchorMax = Vector2.one;
            lrt.offsetMin = new Vector2(10, 6); lrt.offsetMax = new Vector2(-10, -6);
            _bannerLbl = lblGO.AddComponent<TextMeshProUGUI>();
            _bannerLbl.fontSize = 24f; _bannerLbl.color = Color.white;
            _bannerLbl.alignment = TextAlignmentOptions.Center;
            _bannerLbl.fontStyle = FontStyles.Bold; _bannerLbl.raycastTarget = false;
            _bannerLbl.textWrappingMode = TextWrappingModes.NoWrap;

            _banner = go;
            _banner.SetActive(false);
        }
    }
}