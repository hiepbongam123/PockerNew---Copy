using System.Collections;
using UnityEngine;
using UnityEngine.Video;
using UnityEngine.UI;
using TMPro;
using LoRClone.Data;

namespace LoRClone.View
{
    /// <summary>
    /// Quản lý cinematic khi champion level up.
    ///
    /// ══ SETUP TRONG SCENE ══
    /// 1. Tạo GameObject "LevelUpCinematic" gán component này vào.
    /// 2. Tạo Canvas con (overlayCanvas) — Sort Order cao (vd 100).
    /// 3. Bên trong overlayCanvas:
    ///      - RawImage (videoDisplay)       → hiện video MP4
    ///      - RawImage (cardArtFallback)    → hiện art tĩnh nếu không có video
    ///      - TextMeshProUGUI (cardNameText)
    ///      - TextMeshProUGUI (levelUpLabel)
    /// 4. Tạo RenderTexture → gán vào renderTexture.
    /// 5. Gán VideoPlayer component.
    /// 6. Gán card prefab (cardPrefab) — prefab có CardView component.
    ///    Card này sẽ được instantiate và hiện ở giữa màn hình SAU khi video kết thúc.
    ///
    /// ══ FLOW ══
    /// GameController.TriggerLevelUpCoroutine → PlayAndWait(clip, levelUpData, name, voices)
    ///   → fade in → play video → video xong → card xuất hiện → hold → fade out
    /// </summary>
    public class LevelUpCinematicPlayer : MonoBehaviour
    {
        // ── UI References ──────────────────────────────────────────
        [Header("UI References")]
        public Canvas overlayCanvas;
        public CanvasGroup canvasGroup;

        [Tooltip("RawImage hiển thị RenderTexture output của VideoPlayer.")]
        public RawImage videoDisplay;

        [Tooltip("RawImage hiển thị artwork tĩnh khi không có video clip.")]
        public RawImage cardArtFallback;

        public TextMeshProUGUI cardNameText;
        public TextMeshProUGUI levelUpLabel;

        // ── Card Prefab ────────────────────────────────────────────
        [Header("Card Prefab")]
        [Tooltip("Prefab card có CardView. Sẽ được instantiate ở giữa màn hình sau video.\n" +
                 "Để trống → không hiện card (chỉ video + fade).")]
        public GameObject cardPrefab;

        [Tooltip("Chiều cao card hiện trong cinematic so với canvas height (0–1). LoR gốc ≈ 0.45.")]
        [Range(0.1f, 0.9f)]
        public float cardHeightRatio = 0.45f;

        // ── Video ──────────────────────────────────────────────────
        [Header("Video")]
        public VideoPlayer videoPlayer;
        public RenderTexture renderTexture;

        // ── Audio ──────────────────────────────────────────────────
        [Header("Audio")]
        public AudioSource voiceSource;

        // ── Timing ────────────────────────────────────────────────
        [Header("Timing")]
        public float fadeInDuration = 0.35f;
        public float holdAfterVideo = 0.8f;
        public float fadeOutDuration = 0.5f;
        public float fallbackDuration = 1.8f;
        public string levelUpText = "THĂNG CẤP!";

        // ── State ─────────────────────────────────────────────────
        bool _isPlaying;
        GameObject _spawnedCard;

        // ── Awake ─────────────────────────────────────────────────
        void Awake()
        {
            if (overlayCanvas != null)
            {
                if (overlayCanvas.GetComponent<GraphicRaycaster>() == null)
                    overlayCanvas.gameObject.AddComponent<GraphicRaycaster>();
                overlayCanvas.gameObject.SetActive(false);
            }
            CenterAnchor(videoDisplay);
            CenterAnchor(cardArtFallback);
        }

        // ── Public API ────────────────────────────────────────────

        /// <summary>
        /// Gọi từ GameController sau khi card.LevelUp() đã chạy.
        /// levelUpData = card.data.levelUpForm (capture TRƯỚC khi LevelUp() đổi data).
        /// </summary>
        public IEnumerator PlayAndWait(VideoClip clip, CardData levelUpData, string cardName,
                                       AudioClip[] voiceLines = null)
        {
            while (_isPlaying)
                yield return null;

            _isPlaying = true;
            yield return StartCoroutine(PlayCinematic(clip, levelUpData, cardName, voiceLines));
            _isPlaying = false;
        }

        // ── Core coroutine ────────────────────────────────────────
        IEnumerator PlayCinematic(VideoClip clip, CardData levelUpData, string cardName,
                                  AudioClip[] voiceLines)
        {
            bool hasVideo = clip != null && videoPlayer != null && renderTexture != null;
            var artwork = levelUpData?.artwork; // Texture2D cho fallback

            // ── Setup UI ────────────────────────────────────────
            overlayCanvas.gameObject.SetActive(true);
            canvasGroup.alpha = 0f;

            if (cardNameText != null) cardNameText.text = cardName;
            if (levelUpLabel != null) levelUpLabel.text = levelUpText;

            if (videoDisplay != null) videoDisplay.gameObject.SetActive(hasVideo);
            if (cardArtFallback != null) cardArtFallback.gameObject.SetActive(!hasVideo);

            // Yield 1 frame để Unity recalculate canvas rect sau SetActive(true)
            yield return null;

            // ── Chuẩn bị video ─────────────────────────────────
            if (hasVideo)
            {
                videoPlayer.clip = clip;
                videoPlayer.renderMode = VideoRenderMode.RenderTexture;

                videoPlayer.Prepare();
                float prepareTimeout = 5f;
                while (!videoPlayer.isPrepared && prepareTimeout > 0f)
                {
                    prepareTimeout -= Time.unscaledDeltaTime;
                    yield return null;
                }

                if (!videoPlayer.isPrepared)
                {
                    Debug.LogWarning("[Cinematic] VideoPlayer không prepare được — dùng fallback.");
                    hasVideo = false;
                    if (videoDisplay != null) videoDisplay.gameObject.SetActive(false);
                    if (cardArtFallback != null) cardArtFallback.gameObject.SetActive(true);
                }
                else
                {
                    // Resize RenderTexture khớp resolution thực của video
                    int vw = (int)videoPlayer.width;
                    int vh = (int)videoPlayer.height;
                    if (vw > 0 && vh > 0)
                    {
                        renderTexture.Release();
                        renderTexture.width = vw;
                        renderTexture.height = vh;
                        renderTexture.Create();
                    }
                    videoPlayer.targetTexture = renderTexture;
                    videoDisplay.texture = renderTexture;

                    if (videoPlayer.height > 0)
                        AspectCover(videoDisplay, (float)videoPlayer.width / videoPlayer.height);
                }
            }

            // Fallback: hiện artwork tĩnh
            if (!hasVideo && cardArtFallback != null && artwork != null)
            {
                cardArtFallback.texture = artwork;
                if (artwork.height > 0)
                    AspectCover(cardArtFallback, (float)artwork.width / artwork.height);
            }

            // ── Fade IN ─────────────────────────────────────────
            yield return StartCoroutine(Fade(0f, 1f, fadeInDuration));

            // ── Phát voice line ──────────────────────────────────
            if (voiceSource != null && voiceLines != null && voiceLines.Length > 0)
            {
                var pick = voiceLines[Random.Range(0, voiceLines.Length)];
                if (pick != null) { voiceSource.Stop(); voiceSource.clip = pick; voiceSource.Play(); }
            }

            // ── Phát video / fallback ────────────────────────────
            if (hasVideo)
            {
                videoPlayer.Play();
                while (videoPlayer.isPlaying)
                {
                    if (AnyInputThisFrame()) break;
                    yield return null;
                }
                videoPlayer.Stop();
            }
            else
            {
                float elapsed = 0f;
                while (elapsed < fallbackDuration)
                {
                    if (AnyInputThisFrame()) break;
                    elapsed += Time.unscaledDeltaTime;
                    yield return null;
                }
            }

            // ── Hiện card level-up ở giữa màn hình ──────────────
            if (cardPrefab != null && levelUpData != null)
            {
                _spawnedCard = SpawnCinematicCard(levelUpData);
                if (_spawnedCard != null)
                    yield return StartCoroutine(ScaleIn(_spawnedCard.transform, 0.85f, 1f, 0.25f));
            }

            // ── Hold ─────────────────────────────────────────────
            if (holdAfterVideo > 0f)
                yield return new WaitForSecondsRealtime(holdAfterVideo);

            // ── Fade OUT ─────────────────────────────────────────
            if (voiceSource != null && voiceSource.isPlaying) voiceSource.Stop();
            yield return StartCoroutine(Fade(1f, 0f, fadeOutDuration));

            // ── Cleanup ──────────────────────────────────────────
            if (_spawnedCard != null) { Destroy(_spawnedCard); _spawnedCard = null; }
            overlayCanvas.gameObject.SetActive(false);
        }

        // ── Spawn card prefab ─────────────────────────────────────
        GameObject SpawnCinematicCard(CardData data)
        {
            var go = Instantiate(cardPrefab, overlayCanvas.transform);
            go.name = "_CinematicCard";
            go.transform.SetAsLastSibling(); // render đè lên video

            // Canvas sorting — đè lên overlayCanvas
            var selfCanvas = go.GetComponent<Canvas>();
            if (selfCanvas == null) selfCanvas = go.AddComponent<Canvas>();
            selfCanvas.overrideSorting = true;
            selfCanvas.sortingOrder = overlayCanvas.sortingOrder + 1;
            if (go.GetComponent<GraphicRaycaster>() == null)
                go.AddComponent<GraphicRaycaster>();

            // Lấy natural size của prefab trước khi override
            var rt = go.GetComponent<RectTransform>();
            float naturalH = rt.sizeDelta.y;

            // Center + scale để chiều cao = cardHeightRatio * canvasHeight
            rt.anchorMin = rt.anchorMax = rt.pivot = new Vector2(0.5f, 0.5f);
            rt.anchoredPosition = Vector2.zero;

            float canvasH = overlayCanvas.GetComponent<RectTransform>().rect.height;
            float targetH = canvasH * cardHeightRatio;
            float scale = naturalH > 0f ? targetH / naturalH : 1f;
            go.transform.localScale = Vector3.one * (scale * 0.85f); // sẽ được ScaleIn về scale

            // ── Populate data (giống CreateCardPreview PATH A trong CardView) ──
            var cvw = go.GetComponent<CardView>();
            if (cvw != null)
            {
                if (cvw.nameText) cvw.nameText.text = data.cardName;
                if (cvw.manaCostText) cvw.manaCostText.text = data.manaCost.ToString();
                if (cvw.attackText) cvw.attackText.text = data.baseAttack.ToString();
                if (cvw.healthText) cvw.healthText.text = data.baseHealth.ToString();

                if (cvw.artworkRawImage != null && data.artwork != null)
                {
                    cvw.artworkRawImage.texture = data.artwork;
                    ApplyFocalUV(cvw.artworkRawImage, data,
                        cvw.artworkRawImage.rectTransform.rect.width,
                        cvw.artworkRawImage.rectTransform.rect.height);
                }

                // Visibility — portrait mode (giống inspect)
                if (cvw.nameGroup) cvw.nameGroup.SetActive(true);
                else if (cvw.nameText) cvw.nameText.gameObject.SetActive(true);
                if (cvw.manaGroup) cvw.manaGroup.SetActive(true);
                if (cvw.statsGroup) cvw.statsGroup.SetActive(data.cardType == CardType.Unit);

                // Ẩn description — cinematic ngắn, không cần đọc text
                if (cvw.skillsText) cvw.skillsText.gameObject.SetActive(false);

                // Disable script: ngăn inspect + drag trong cinematic
                cvw.enabled = false;
                cvw.AllowInteract = false;
            }

            // Bắt đầu nhỏ — ScaleIn sẽ animate lên targetScale
            // Lưu targetScale để ScaleIn dùng
            go.transform.localScale = Vector3.one * (scale * 0.85f);

            // Gắn targetScale vào component tạm để ScaleIn biết đích
            var scaler = go.AddComponent<_CinematicScaleTarget>();
            scaler.targetScale = scale;

            return go;
        }

        // ── Helpers ───────────────────────────────────────────────
        static void CenterAnchor(RawImage img)
        {
            if (img == null) return;
            var rt = img.rectTransform;
            rt.anchorMin = rt.anchorMax = rt.pivot = new Vector2(0.5f, 0.5f);
            rt.anchoredPosition = Vector2.zero;
        }

        void AspectCover(RawImage img, float aspect)
        {
            if (img == null || overlayCanvas == null) return;
            var canvasRt = overlayCanvas.GetComponent<RectTransform>();
            float cw = canvasRt.rect.width, ch = canvasRt.rect.height;
            float ca = cw / ch;
            float w, h;
            if (aspect >= ca) { h = ch; w = h * aspect; }
            else { w = cw; h = w / aspect; }
            img.rectTransform.sizeDelta = new Vector2(w, h);
        }

        /// <summary>Tính uvRect theo focal point của CardData — dùng khi set artwork cho card preview.</summary>
        static void ApplyFocalUV(RawImage raw, CardData data, float dispW, float dispH)
        {
            if (raw == null || raw.texture == null || data == null) return;
            var tex = raw.texture;
            if (tex.width <= 0 || tex.height <= 0) { raw.uvRect = new Rect(0, 0, 1, 1); return; }
            float texAspect = (float)tex.width / tex.height;
            float dispAspect = Mathf.Max(dispW, 1f) / Mathf.Max(dispH, 1f);
            float zoom = Mathf.Max(0.1f, data.artworkZoom);
            float baseW, baseH;
            if (texAspect > dispAspect) { baseH = 1f; baseW = dispAspect / texAspect; }
            else { baseW = 1f; baseH = texAspect / dispAspect; }
            float uvW = Mathf.Clamp(baseW / zoom, 0.001f, 1f);
            float uvH = Mathf.Clamp(baseH / zoom, 0.001f, 1f);
            float uvX = Mathf.Clamp(data.artworkFocalX - uvW * 0.5f, 0f, 1f - uvW);
            float uvY = Mathf.Clamp(data.artworkFocalY - uvH * 0.5f, 0f, 1f - uvH);
            raw.uvRect = new Rect(uvX, uvY, uvW, uvH);
        }

        IEnumerator Fade(float from, float to, float duration)
        {
            if (duration <= 0f) { canvasGroup.alpha = to; yield break; }
            float elapsed = 0f;
            while (elapsed < duration)
            {
                elapsed += Time.unscaledDeltaTime;
                canvasGroup.alpha = Mathf.Lerp(from, to, elapsed / duration);
                yield return null;
            }
            canvasGroup.alpha = to;
        }

        /// <summary>Scale Transform từ from → targetScale (đọc từ _CinematicScaleTarget) trong duration giây.</summary>
        static IEnumerator ScaleIn(Transform target, float fromRatio, float toRatio, float duration)
        {
            if (target == null) yield break;

            // Đọc targetScale từ component helper
            var scaler = target.GetComponent<_CinematicScaleTarget>();
            float finalScale = scaler != null ? scaler.targetScale : 1f;
            float from = finalScale * fromRatio;
            float to = finalScale * toRatio;

            if (duration <= 0f) { target.localScale = Vector3.one * to; yield break; }
            float elapsed = 0f;
            while (elapsed < duration)
            {
                elapsed += Time.unscaledDeltaTime;
                target.localScale = Vector3.one * Mathf.Lerp(from, to, elapsed / duration);
                yield return null;
            }
            target.localScale = Vector3.one * to;
        }

        static bool AnyInputThisFrame()
            => Input.anyKeyDown || Input.GetMouseButtonDown(0);
    }

    /// <summary>Component tạm để truyền targetScale vào ScaleIn coroutine.</summary>
    internal class _CinematicScaleTarget : MonoBehaviour
    {
        public float targetScale = 1f;
    }
}