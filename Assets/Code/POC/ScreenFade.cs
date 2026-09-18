using System.Collections;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace LoRClone.View
{
    /// <summary>
    /// Game feel — chuyển cảnh MƯỢT bằng fade đen. Tự tạo (DontDestroyOnLoad), không cần gán gì.
    /// Dùng: thay SceneManager.LoadScene(x) bằng ScreenFade.LoadScene(x).
    ///   • Ra đen → load scene → fade vào (tự động khi scene mới lên).
    /// </summary>
    public class ScreenFade : MonoBehaviour
    {
        public float fadeDuration = 0.35f;

        static ScreenFade _instance;
        CanvasGroup _cg;

        static ScreenFade Instance
        {
            get { if (_instance == null) Create(); return _instance; }
        }

        static void Create()
        {
            var go = new GameObject("~ScreenFade");
            DontDestroyOnLoad(go);
            _instance = go.AddComponent<ScreenFade>();
            _instance.Build();
        }

        void Build()
        {
            var canvas = gameObject.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 32000; // trên mọi UI khác
            gameObject.AddComponent<GraphicRaycaster>();
            _cg = gameObject.AddComponent<CanvasGroup>();

            var img = new GameObject("Black", typeof(RectTransform), typeof(Image));
            img.transform.SetParent(transform, false);
            var rt = (RectTransform)img.transform;
            rt.anchorMin = Vector2.zero; rt.anchorMax = Vector2.one; rt.offsetMin = rt.offsetMax = Vector2.zero;
            img.GetComponent<Image>().color = Color.black;

            _cg.alpha = 0f;
            _cg.blocksRaycasts = false;
            SceneManager.sceneLoaded += OnSceneLoaded;
        }

        void OnSceneLoaded(Scene s, LoadSceneMode m) => StartCoroutine(Fade(0f)); // fade VÀO scene mới

        // ── API ───────────────────────────────────────────────────
        public static void LoadScene(string sceneName) => Instance.StartCoroutine(Instance.DoLoad(sceneName, -1));
        public static void LoadScene(int buildIndex) => Instance.StartCoroutine(Instance.DoLoad(null, buildIndex));

        IEnumerator DoLoad(string sceneName, int buildIndex)
        {
            yield return Fade(1f); // ra đen
            if (!string.IsNullOrEmpty(sceneName)) SceneManager.LoadScene(sceneName);
            else SceneManager.LoadScene(buildIndex);
            // OnSceneLoaded sẽ fade vào.
        }

        IEnumerator Fade(float target)
        {
            _cg.blocksRaycasts = target > 0.01f;
            float start = _cg.alpha, e = 0f, dur = Mathf.Max(0.01f, fadeDuration);
            while (e < dur)
            {
                e += Time.unscaledDeltaTime; // hoạt động cả khi timeScale = 0
                _cg.alpha = Mathf.Lerp(start, target, e / dur);
                yield return null;
            }
            _cg.alpha = target;
            _cg.blocksRaycasts = target > 0.01f;
        }
    }
}
