using UnityEngine;
using UnityEngine.SceneManagement;

namespace LoRClone
{
    /// <summary>
    /// Tự áp UITheme cho các scene MENU — KHÔNG cần gọi tay, KHÔNG cần setup.
    ///
    /// Cách hoạt động:
    ///   • [RuntimeInitializeOnLoadMethod] tạo GameObject ẩn DontDestroyOnLoad, sống xuyên scene.
    ///   • Mỗi 'interval' giây, restyle scene hiện tại (UITheme.ThemeUnder) → bắt được cả UI dựng runtime.
    ///   • menusOnly = true: BỎ QUA scene gameplay (tên chứa game/battle/... ) để không đụng card art.
    ///
    /// Tắt/bật:  UIThemeAuto.enabled = false;   UIThemeAuto.menusOnly = false; (theme cả gameplay — cẩn thận)
    /// Ép Kind 1 nút: gắn UIThemeKind.  Bỏ qua 1 vùng: gắn UIThemeIgnore.
    /// </summary>
    public class UIThemeAuto : MonoBehaviour
    {
        public static bool enabled = false;    // MẶC ĐỊNH TẮT — bật khi bạn muốn: UIThemeAuto.enabled = true;
        public static bool menusOnly = true;
        public static bool aggressive = false; // true = ép đổi cả nút thường (vẫn giữ nút có sprite art riêng)
        public static float interval = 0.5f;

        static readonly string[] GameHints = { "game", "battle", "play", "match", "combat", "board" };

        float _t;
        bool _isMenu = true;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        static void Boot()
        {
            var go = new GameObject("~UITheme");
            Object.DontDestroyOnLoad(go);
            go.hideFlags = HideFlags.HideAndDontSave;
            go.AddComponent<UIThemeAuto>();
        }

        void Awake()
        {
            SceneManager.sceneLoaded += OnSceneLoaded;
            _isMenu = IsMenu(SceneManager.GetActiveScene().name);
            _t = interval; // theme ngay frame đầu
        }

        void OnDestroy() => SceneManager.sceneLoaded -= OnSceneLoaded;

        void OnSceneLoaded(Scene s, LoadSceneMode m) { _isMenu = IsMenu(s.name); _t = interval; }

        void Update()
        {
            if (!enabled) return;
            if (menusOnly && !_isMenu) return;
            _t += Time.unscaledDeltaTime;
            if (_t < interval) return;
            _t = 0f;

            var scene = SceneManager.GetActiveScene();
            if (!scene.IsValid()) return;
            foreach (var root in scene.GetRootGameObjects())
                UITheme.ThemeUnder(root.transform, aggressive);
        }

        static bool IsMenu(string name)
        {
            string n = (name ?? "").ToLowerInvariant();
            foreach (var g in GameHints) if (n.Contains(g)) return false;
            return true;
        }
    }
}
