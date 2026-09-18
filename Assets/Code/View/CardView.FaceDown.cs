using UnityEngine;
using UnityEngine.UI;
using LoRClone.Model;

namespace LoRClone.View
{
    /// <summary>
    /// CardView — phần Face-Down (úp bài).
    ///
    /// Dùng cho: lá trên tay ĐỊCH, và spell ĐỊCH đang stage (chưa commit) → úp mặt sau,
    /// người chơi không đọc được nội dung. Khi lá được reveal (commit / vào sân) thì gọi
    /// SetFaceDown(false) hoặc đơn giản là spawn một CardView mới mặt-ngửa.
    ///
    /// Overlay mặt sau dùng RawImage + Texture (giống artworkRawImage) → tạo runtime, KHÔNG cần sửa prefab.
    /// Gán 'cardBackTexture' trên prefab CardView để dùng ảnh mặt sau; bỏ trống thì dùng màu đặc 'cardBackColor'.
    /// </summary>
    public partial class CardView
    {
        [Header("Face Down (úp bài)")]
        [Tooltip("Texture mặt sau lá bài (RawImage). Bỏ trống → dùng màu đặc fallback bên dưới.")]
        public Texture cardBackTexture;
        [Tooltip("Màu fallback khi không gán cardBackTexture (RawImage vẫn vẽ màu này khi texture null).")]
        public Color cardBackColor = new Color(0.12f, 0.10f, 0.18f, 1f);

        bool _faceDown;
        RawImage _faceDownOverlay;

        /// <summary>True nếu lá đang bị úp (mặt sau).</summary>
        public bool IsFaceDown => _faceDown;

        /// <summary>Bật/tắt úp bài. Bật = ẩn toàn bộ nội dung, chặn tương tác & inspect.</summary>
        public void SetFaceDown(bool on)
        {
            _faceDown = on;
            if (on)
            {
                AllowInteract = false;          // chặn drag/expand
                ApplyFaceDownVisuals();
            }
            else
            {
                if (_faceDownOverlay != null) _faceDownOverlay.gameObject.SetActive(false);
                // Khôi phục các thành phần đã bị tắt cứng
                if (artworkRawImage) artworkRawImage.enabled = true;
                if (keywordsContainer) keywordsContainer.gameObject.SetActive(true);
                AllowInteract = true;
                RefreshAll();                   // dựng lại mặt trước theo location
            }
        }

        void EnsureFaceDownOverlay()
        {
            if (_faceDownOverlay != null) return;
            var go = new GameObject("_FaceDownOverlay");
            var rt = go.AddComponent<RectTransform>();
            go.transform.SetParent(transform, false);
            rt.anchorMin = Vector2.zero; rt.anchorMax = Vector2.one;
            rt.offsetMin = Vector2.zero; rt.offsetMax = Vector2.zero;
            _faceDownOverlay = go.AddComponent<RawImage>();
            _faceDownOverlay.raycastTarget = true;   // chặn click xuyên qua → không inspect được
        }

        /// <summary>Ẩn mọi thành phần mặt trước và bật overlay mặt sau. Gọi lại được nhiều lần (idempotent).</summary>
        internal void ApplyFaceDownVisuals()
        {
            EnsureFaceDownOverlay();
            _faceDownOverlay.transform.SetAsLastSibling();   // luôn nằm trên cùng
            _faceDownOverlay.gameObject.SetActive(true);

            _faceDownOverlay.texture = cardBackTexture;                     // null → RawImage vẽ màu đặc
            _faceDownOverlay.color = cardBackTexture != null ? Color.white : cardBackColor;
            _faceDownOverlay.uvRect = new Rect(0f, 0f, 1f, 1f);

            // Ẩn toàn bộ mặt trước
            if (artworkRawImage) artworkRawImage.enabled = false;
            if (cardFrame) cardFrame.enabled = false;
            if (nameGroup) nameGroup.SetActive(false);
            if (manaGroup) manaGroup.SetActive(false);
            if (statsGroup) statsGroup.SetActive(false);
            if (skillsText) skillsText.gameObject.SetActive(false);
            if (keywordsContainer) keywordsContainer.gameObject.SetActive(false);
            if (levelUpProgressText) levelUpProgressText.gameObject.SetActive(false);
            if (levelUpReadyBorder) levelUpReadyBorder.enabled = false;
            if (attackingHighlight) attackingHighlight.enabled = false;
            if (blockingHighlight) blockingHighlight.enabled = false;
            if (selectedHighlight) selectedHighlight.enabled = false;
            if (supportedHighlight) supportedHighlight.enabled = false;
        }
    }
}