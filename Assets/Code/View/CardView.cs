using System;
using System.Text;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.EventSystems;
using TMPro;
using LoRClone.Model;
using LoRClone.Data;
using LoRClone.Controller;
using LoRClone; // AudioManager

namespace LoRClone.View
{
    public enum DragIntent { None, Forward, Backward }

    /// <summary>
    /// Gắn vào card prefab.
    ///
    /// === Artwork Viewer ===
    /// Khi inspect, nút "Xem ảnh" hiện góc trên-phải card.
    /// Click → overlay toàn màn hình hiện ảnh gốc (uvRect 0,0,1,1).
    /// Nút X góc phải hoặc click nền để đóng.
    ///
    /// === Related Cards Strip ===
    /// Khi inspect, nếu CardData.relatedCards không rỗng:
    /// → Dải ngang xuất hiện phía dưới màn hình hiện thumbnail các lá liên quan.
    /// → Click thumbnail → detail panel hiện phía trên dải.
    /// → Click ra ngoài dải → đóng detail. Click ra ngoài card + dải → đóng inspect.
    /// </summary>
    public partial class CardView : MonoBehaviour,
        IPointerClickHandler,
        IBeginDragHandler, IDragHandler, IEndDragHandler,
        IPointerEnterHandler, IPointerExitHandler
    {
        // ── Inspector refs ────────────────────────────────────────
        [Header("UI cơ bản")]
        public TextMeshProUGUI nameText;
        public Image cardFrame;

        [Header("Inspect Overlay")]
        [Tooltip("GameObject/Image chỉ hiện khi inspect (zoom to đọc thông tin). Ẩn hoàn toàn khi card ở tay hoặc trên sân. Gán Image nền chi tiết vào đây.")]
        public GameObject inspectOverlay;

        [Header("Artwork — phải là RawImage")]
        public RawImage artworkRawImage;

        [Header("Focal Point fallback")]
        [Range(0f, 1f)] public float artworkFocalX = 0.5f;
        [Range(0f, 1f)] public float artworkFocalY = 0.5f;

        [Header("Name — ẩn khi trên sân")]
        public GameObject nameGroup;

        [Header("Mana — ẩn khi trên sân")]
        public GameObject manaGroup;
        public TextMeshProUGUI manaCostText;

        [Header("Stats — chỉ Unit")]
        public GameObject statsGroup;
        public TextMeshProUGUI attackText;
        public TextMeshProUGUI healthText;
        public float statsEdgeOffset = 8f;
        public float keywordsFieldOffset = 0f;

        [Header("Stat Text Colors")]
        [Tooltip("Màu mặc định của số ATK / HP (không buff, không bị thương).")]
        public Color statColorDefault = Color.white;
        [Tooltip("Màu khi ATK hoặc HP được cường hóa (cao hơn chỉ số gốc).")]
        public Color statColorBuffed = new Color(0.55f, 1f, 0.55f);  // xanh lá nhạt
        [Tooltip("Màu khi ATK bị giảm hoặc HP đang bị thương (thấp hơn tối đa).")]
        public Color statColorDebuffed = new Color(1f, 0.45f, 0.45f);  // đỏ nhạt

        [Header("Skill/Description text")]
        public TextMeshProUGUI skillsText;

        [Header("Highlight")]
        public Image attackingHighlight;
        public Image blockingHighlight;
        public Image selectedHighlight;

        [Tooltip("Glow/border hiện khi card này đang được hỗ trợ bởi đồng minh Support bên trái.\n" +
                 "Tạo Image con full-stretch, màu vàng/cam nhạt, gán vào đây.")]
        public Image supportedHighlight;

        [Header("Hover")]
        public float hoverScale = 1.12f;

        [Header("Inspect")]
        public float inspectScale = 2.8f;

        [Header("Spell Zone — Circle Mode")]
        public Vector2 circleSize = new Vector2(80f, 80f);

        [Header("Keyword Icons")]
        public Transform keywordsContainer;
        public GameObject keywordIconPrefab;
        public KeywordIconEntry[] keywordIcons = new KeywordIconEntry[0];

        [Header("Keyword Popup")]
        public GameObject keywordPopup;
        public TextMeshProUGUI keywordPopupText;

        [Header("Artwork Viewer")]
        public Sprite viewArtButtonSprite;
        public Sprite closeArtButtonSprite;
        public string viewArtButtonLabel = "Xem anh";
        public Color artViewerDimColor = new Color(0f, 0f, 0f, 0.88f);
        public float artViewerPadding = 48f;

        [Header("Related Cards — Dải thumbnail phía dưới")]
        [Tooltip("Scale thumbnail lá liên quan so với portrait size (0.35–0.6). Hiện dưới dạng dải ngang ở đáy màn hình khi inspect.")]
        public float relatedThumbScale = 0.45f;

        [Header("Related Card Preview — Prefab")]
        [Tooltip("Gán card prefab vào đây. Khi click thumbnail, preview dùng prefab thật thay vì tạo thủ công. Nếu bỏ trống sẽ fallback sang code manual.")]
        public GameObject cardPrefab;

        [Header("Buff History Panel — Khi Inspect")]
        [Tooltip("Bề rộng panel liệt kê buff bên trái card. Tăng số này để panel to ra.")]
        public float buffPanelWidth = 220f;
        [Tooltip("Chiều cao mỗi dòng buff. Tăng số này để dòng cao/thoáng hơn.")]
        public float buffPanelRowHeight = 32f;
        [Tooltip("Cỡ chữ tên nguồn buff (vd: 'Máy Bắn Tên Darkin').")]
        public float buffPanelNameFontSize = 14f;
        [Tooltip("Cỡ chữ số liệu +ATK|+HP.")]
        public float buffPanelStatFontSize = 13f;
        [Tooltip("Khoảng đệm 2 bên trong panel (spacing trái/phải quanh chữ).")]
        public float buffPanelPadding = 10f;
        [Tooltip("Bề rộng dành cho cột số liệu +ATK|+HP bên phải. Tên nguồn buff tự co lại " +
                 "cho vừa phần còn lại — tên dài sẽ tự rút gọn kèm '...', không tràn ra ngoài.")]
        public float buffPanelStatColumnWidth = 64f;

        [Header("Level Up")]
        [Tooltip("Image viền/glow hiện khi champion đã đủ điều kiện level-up nhưng chưa ra sân.\n" +
                 "Tạo Image con trong prefab card (full-stretch, màu vàng/trắng glow), gán vào đây.\n" +
                 "Tắt bởi default — CardView tự bật/tắt theo model.readyToLevelUp.")]
        public Image levelUpReadyBorder;

        [Tooltip("TextMeshProUGUI hiện tiến độ 'X/N' ngay trên card (luôn visible khi canLevelUp).\n" +
                 "Ví dụ: '3/5'. Đặt góc dưới-phải hoặc dưới tên champion.\n" +
                 "Tự ẩn khi champion chưa có canLevelUp hoặc đã level up xong.")]
        public TextMeshProUGUI levelUpProgressText;

        [Header("Drag Intent")]
        public float dragIntentThreshold = 60f;

        [Header("Targeting highlight colors")]
        public Color targetableColor = new Color(0.2f, 0.6f, 1f, 0.85f);
        public Color confirmedTargetColor = new Color(0.15f, 1f, 0.35f, 0.9f);

        // ── Runtime ───────────────────────────────────────────────
        public CardModel model { get; private set; }
        RectTransform _rect;
        Canvas _rootCanvas;
        Canvas _selfCanvas;
        CanvasGroup _cg;
        Transform _originalParent;
        Vector2 _originalPos;
        int _originalSibling;
        bool _dragging;
        Vector2 _dragStartScreenPos;
        bool _inspecting;
        static CardView _currentInspecting;

        /// <summary>
        /// Dùng flag này thay vì CardView.enabled = false để lock tương tác.
        /// Khi false: chặn drag + ClickShouldExpand, NHƯNG vẫn cho inspect + keyword popup.
        /// Zone view gọi: cardView.AllowInteract = false/true thay vì cardView.enabled = false/true.
        /// CardView.enabled = false sẽ khiến Unity bỏ qua toàn bộ OnPointerClick → keyword không hoạt động.
        /// </summary>
        public bool AllowInteract { get; set; } = true;

        public bool IsInspecting => _inspecting;

        /// <summary>
        /// Offset cộng vào MỌI sortingOrder của lớp inspect (card / dim / preview / dải lá liên quan /
        /// keyword popup / artwork viewer). Mặc định 0 = giữ nguyên hành vi gameplay.
        /// DeckBuilder đặt = 601 để inspect nổi TRÊN overlay xưởng deck (Canvas sortingOrder 600).
        /// </summary>
        [HideInInspector] public int inspectSortingBase = 0;

        /// <summary>Fire ở cuối ExitInspect — DeckBuilder dùng để dọn card preview tạm.</summary>
        public event Action OnInspectClosed;

        /// <summary>
        /// Mở inspect từ code (không qua click chuột). Card phải đã Bind model.
        /// Dùng bởi DeckBuilder để tái dùng y hệt inspect của gameplay.
        /// </summary>
        public void OpenInspectExternally()
        {
            if (_inspecting) return;
            EnterInspect();
        }

        Vector2 _portraitCardSize;
        Vector2 _portraitArtworkSize;
        Vector2 _fieldCardSize;
        Vector2 _fieldArtworkSize;
        bool _hasFieldSize;
        float _sizeScale = 1f;
        int _baseSortingOrder = 1;

        bool _inCircleMode;
        GameObject _circleClipGO;
        Transform _artworkOriginalParent;
        int _artworkOriginalSiblingIdx;
        Vector2 _originalAnchorMin;
        Vector2 _originalAnchorMax;
        Vector2 _originalPivot;
        Vector3 _originalRotation;

        // ── Keyword popup state ───────────────────────────────────
        static CardView _cardWithOpenKeywordPopup;

        // ── Artwork Viewer state ──────────────────────────────────
        bool _artViewerOpen;
        GameObject _artViewerOverlay;
        GameObject _viewArtBtn;

        // ── Related Cards Side state ──────────────────────────────
        System.Collections.Generic.List<GameObject> _relatedSideCards =
            new System.Collections.Generic.List<GameObject>();

        GameObject _buffHistoryPanel;
        GameObject _itemPanel;   // PoC: panel hexagon item bên PHẢI card khi inspect
        GameObject _itemBadge;   // PoC: badge ◆N góc card khi có item gắn
        TMPro.TextMeshProUGUI _itemBadgeText;

        // ── Inspect Navigation (LoR-style) ────────────────────────
        GameObject _inspectDim;          // dark overlay sortOrder 194
        bool _navigatedToRelated;        // đang xem lá liên quan
        GameObject _relatedCardPreview;  // card preview ở trung tâm
        GameObject _backLabel;           // label "QUAY LAI" trên lá trái
        Vector2 _inspectCenterPos;       // vị trí trung tâm canvas khi inspect
        Coroutine _slideCoroutine;

        // ── Keyword descriptions ──────────────────────────────────
        static readonly System.Collections.Generic.Dictionary<KeywordType, (string name, string desc)> _kwDesc =
            new System.Collections.Generic.Dictionary<KeywordType, (string, string)>
            {
                { KeywordType.Barrier,      ("Bao Ho",       "Vo hieu HOAN TOAN don sat thuong ke tiep unit nay nhan, roi tieu hao. Mat hieu luc dau vong sau.") },
                { KeywordType.Lifesteal,    ("Tru Phu",      "Sat thuong quan nay gay ra se hoi cung luong mau cho Nexus cua ban.") },
                { KeywordType.QuickAttack,  ("San Ban",      "Khi tan cong, ra don TRUOC. Neu giet duoc muc tieu, khong nhan don phan.") },
                { KeywordType.Tough,        ("Tri Thuc",     "Nhan it hon 1 sat thuong tu moi nguon.") },
                { KeywordType.Elusive,      ("Than Bi",      "Chi bi chan boi quan co Than Bi.") },
                { KeywordType.Overwhelm,    ("Huy Diet",     "Sat thuong du khi giet blocker tran len Nexus dich.") },
                { KeywordType.Fearsome,     ("Hu Vo",        "Chi co the bi chan boi unit co tan cong tu 3 tro len.") },
                { KeywordType.CantBlock,    ("Vui Ve",       "Khong the chan.") },
                { KeywordType.SpellShield,  ("Hoa Hop",      "Vo hieu hieu ung spell/skill ke tiep cua doi thu nham vao unit nay, roi tieu hao. KHONG chan sat thuong combat.") },
                { KeywordType.Ephemeral,    ("Cam Tu",       "Bi tieu diet sau khi ra don hoac khi ket thuc vong.") },
                // ── New keywords ──────────────────────────────────────────────────────────
                { KeywordType.DoubleAttack, ("Song Kich",    "Khi tan cong, ra don ca TRUOC va DONG THOI voi quan chan.") },
                { KeywordType.Challenger,   ("Chi Dinh",     "Khi tan cong, chon 1 quan dich buoc phai chan ta.") },
                { KeywordType.Regeneration, ("Hoi Phuc",     "Dau moi vong, hoi toan bo HP.") },
                { KeywordType.Scout,        ("Tien Phong",   "Khi tan cong ma chi co cac quan Tien Phong khac cung tan cong, ban duoc san sang tan cong them 1 lan trong vong.") },
                { KeywordType.Fury,         ("Ac Nghiep",    "Moi khi tieu diet 1 quan dich, nhan +1|+1 vinh vien.") },
                { KeywordType.Lurk,         ("Tiem Phuc",    "Khi ban tan cong luc 1 quan Tiem Phuc o dau deck, tat ca quan Tiem Phuc nhan +1|+0. (Lap lai)") },
                { KeywordType.Deep,         ("Vuc Tham",     "Khi deck cua ban co 15 la hoac it hon, nhan +3|+3 vinh vien (1 lan duy nhat).") },
                { KeywordType.Augment,      ("Cong Huong",   "Moi khi ban choi 1 la bai duoc tao ra (generated), nhan +1|+0 vinh vien.") },
                { KeywordType.Formidable,   ("Than Thep",    "Ra don bang chi so HP thay vi ATK.") },
                { KeywordType.Vulnerable,   ("Phoi Bay",     "Ke dich co the dung Chi Dinh buoc quan nay phai chan.") },
                { KeywordType.Fated,        ("Thien Co",     "Lan dau tien trong moi vong duoc dong minh nham muc tieu, nhan +1|+1 vinh vien.") },
                { KeywordType.Daybreak,     ("Binh Minh",    "Neu day la la bai dau tien ban choi trong vong nay, nhan +1|+1 ngay lap tuc.") },
                { KeywordType.Nightfall,    ("Hoang Hon",    "Neu day KHONG phai la la bai dau tien ban choi trong vong nay, nhan +1|+1 ngay lap tuc.") },
                { KeywordType.Plunder,      ("Chien Loi",    "Khi phat dong tan cong vao Nexus dich, neu Nexus do da bi ton thuong trong vong nay: hoan 1 mana va nhan +1|+0 tam thoi.") },
                { KeywordType.Flow,         ("Dong Thuc",    "Moi khi ban choi 1 spell, toan bo quan co Dong Thuc nhan +1|+0 tam thoi.") },
                { KeywordType.Hallowed,     ("Linh Thieng",  "Khi quan nay chet, den het van, quan dau tien tan cong moi vong nhan +1|+0. (Cong don qua nhieu quan)") },
                { KeywordType.Fleeting,     ("Thoang Hien",  "Quan bi huy o cuoi vong neu van con tren tay (chua duoc choi ra san).") },
                { KeywordType.Support,      ("Cong Luc",     "Khi quan nay tan cong, buff dong minh ben phai tren ghe (theo chi so Support cua la).") },
                { KeywordType.LastBreath,   ("Hoi Tho Cuoi", "Kich hoat hieu ung khi quan nay bi giet — truoc khi bi xoa khoi san.") },
                { KeywordType.Countdown,    ("Dem Nguoc",    "Dau moi vong, so dem nguoc giam 1. Khi ve 0, quan bi huy.") },
                { KeywordType.Impact,       ("Xung Kich",    "Khi ra don luc dang tan cong (du bi chan hay khong), gay 1 sat thuong len Nexus dich. Nhieu don cong don.") },
                { KeywordType.Spellcraft,   ("Chu Thuat",    "Khi duoc choi hoac trieu hoi, tao 1 spell cu the len tay ban.") },
                { KeywordType.UnitSkill,    ("Ky Nang",      "Khi ra san, tu dong kich hoat 1 spell vao spell zone (0 mana). Doi thu co the phan ung neu la Fast/Slow.") },
                { KeywordType.Equipped,     ("Trang Bi",     "Dang deo mot trang bi: nhan chi so va keyword tu trang bi do. Khi unit nay chet, trang bi tro ve tay chu so huu.") },
                { KeywordType.Frostbite,    ("Dong Bang",    "Suc tan cong bi giam ve 0 den het round. Van co the duoc buff ATK lai trong round do.") },
                { KeywordType.Stun,         ("Choang",       "Bi loai khoi combat — khong the tan cong hoac chan trong round nay.") },
                { KeywordType.Silence,      ("Cam Lang",     "Bi xoa toan bo keyword, ability va hieu ung dang co. Khong anh huong chi so.") },
                { KeywordType.Sharpsight,   ("Tinh Mat",     "Co the chan ca unit Than Bi (Elusive).") },
                { KeywordType.Immobile,     ("Bat Dong",     "Khong the tan cong lan chan.") },
            };

        public event Action<CardView, Vector2> OnDropped;
        public event Action<CardView, DragIntent> OnDragIntent;
        public Func<bool> ClickShouldExpand;
        Action<CardModel> _onStatsChanged;
        Action<CardModel> _onLocationChanged;
        Action<CardModel> _onLeveledUp;

        // ── Sound delegates (CHỈ TakeDamage/Die — xem Bind) ───────
        Action<CardModel> _onDamaged;
        Action<CardModel> _onDied;

        // ── Focal Point helpers ───────────────────────────────────
        float GetFocalX() => model?.data != null ? model.data.artworkFocalX : artworkFocalX;
        float GetFocalY() => model?.data != null ? model.data.artworkFocalY : artworkFocalY;
        float GetZoom() => model?.data != null ? Mathf.Max(0.1f, model.data.artworkZoom) : 1f;

        // ── Awake ─────────────────────────────────────────────────
        void Awake()
        {
            _rect = GetComponent<RectTransform>();
            _rootCanvas = GetComponentInParent<Canvas>();
            if (_rootCanvas != null)
                while (!_rootCanvas.isRootCanvas && _rootCanvas.transform.parent != null)
                    _rootCanvas = _rootCanvas.transform.parent.GetComponentInParent<Canvas>();
            _cg = GetComponent<CanvasGroup>() ?? gameObject.AddComponent<CanvasGroup>();
            _selfCanvas = GetComponent<Canvas>() ?? gameObject.AddComponent<Canvas>();
            _selfCanvas.overrideSorting = true;
            _selfCanvas.sortingOrder = 1;
            if (GetComponent<GraphicRaycaster>() == null)
                gameObject.AddComponent<GraphicRaycaster>();
            _portraitCardSize = _rect.sizeDelta;
            if (artworkRawImage != null)
                _portraitArtworkSize = artworkRawImage.GetComponent<RectTransform>().sizeDelta;
            if (keywordPopup != null) keywordPopup.SetActive(false);
        }

        void Update()
        {
            // Artwork viewer ưu tiên trước
            if (_artViewerOpen)
            {
                if (Input.GetKeyDown(KeyCode.Escape) || Input.GetMouseButtonDown(1))
                    CloseArtworkViewer();
                return;
            }

            if (!_inspecting) return;

            // Dim overlay xử lý click-outside. Ở đây chỉ cần ESC / right-click.
            if (Input.GetKeyDown(KeyCode.Escape) || Input.GetMouseButtonDown(1))
            {
                if (_navigatedToRelated) ReturnFromRelated();
                else ExitInspect();
            }
        }

        // ── Bind ──────────────────────────────────────────────────
        /// <summary>
        /// Có thể gọi nhiều lần an toàn (idempotent).
        /// Luôn unsubscribe delegate cũ trước khi subscribe mới
        /// để tránh double-subscription khi SpawnCardInSlot đã gọi Bind rồi.
        /// </summary>
        public void Bind(CardModel cardModel)
        {
            // Unsubscribe old delegates trước
            if (model != null)
            {
                if (_onStatsChanged != null) model.OnStatsChanged -= _onStatsChanged;
                if (_onLocationChanged != null) model.OnLocationChanged -= _onLocationChanged;
                if (_onLeveledUp != null) model.OnLeveledUp -= _onLeveledUp;
                if (_onDamaged != null) model.OnDamaged -= _onDamaged;
                if (_onDied != null) model.OnDied -= _onDied;
            }

            model = cardModel;
            _onStatsChanged = _ => RefreshStats();
            _onLocationChanged = _ => RefreshAll();
            _onLeveledUp = _ => RefreshAll();
            // THOẠI LÁ BÀI: TakeDamage/Die phát qua CardAudioData.Play (KÊNH CARD riêng) — bắt MỌI
            // nguồn sát thương/cái chết (combat, spell, hiệu ứng). Các sự kiện flow khác
            // (Attack từng đòn / Summon / SpellCast / SpellResolve / KillEnemy / LevelUp) do
            // GameController phát tập trung → không phát chồng, không lẫn kênh SFX chung.
            _onDamaged = _ => AudioManager.PlayCardEvent(CardAudioData.Ev.TakeDamage, model?.data?.audioData);
            _onDied = _ => AudioManager.PlayCardEvent(CardAudioData.Ev.Die, model?.data?.audioData);

            model.OnStatsChanged += _onStatsChanged;
            model.OnLocationChanged += _onLocationChanged;
            model.OnLeveledUp += _onLeveledUp;
            model.OnDamaged += _onDamaged;
            model.OnDied += _onDied;
            RefreshAll();
        }

        void OnDisable()
        {
            // Chỉ reset flags — KHÔNG gọi SetParent hay ExitInspect().
            // Unity lỗi "Cannot set the parent while activating/deactivating Canvas" nếu reparent ở đây.
            if (_dragging)
            {
                _dragging = false;
                if (_cg != null) _cg.blocksRaycasts = true;
                if (_selfCanvas != null) _selfCanvas.sortingOrder = _baseSortingOrder;
            }
            if (CurrentDragging == this) CurrentDragging = null;
            if (_inspecting)
            {
                _inspecting = false;
                if (_currentInspecting == this) _currentInspecting = null;
                HideKeywordPopup();
                DestroyInspectDim();
                // FIX kẹt panel: các panel nổi parent vào ROOT CANVAS (không phải card) nên
                // phải hủy tay khi card bị tắt giữa lúc inspect. Đều là Destroy (không reparent) → an toàn.
                HideBuffHistoryPanel();
                HideItemPanel();
                HideRelatedSideCards();
                HideViewArtButton();
                DestroyRelatedCardPreview();
                CloseArtworkViewer();
                HideBackLabel();
            }
        }

        void OnDestroy()
        {
            if (CurrentDragging == this) CurrentDragging = null;
            if (_slideCoroutine != null) { StopCoroutine(_slideCoroutine); _slideCoroutine = null; }
            // Đảm bảo blocksRaycasts không để lại false trên đối tượng đang destroy
            if (_cg != null) _cg.blocksRaycasts = true;
            HideKeywordPopup();
            CloseArtworkViewer();
            HideViewArtButton();
            HideBackLabel();
            DestroyRelatedCardPreview();
            DestroyInspectDim();
            HideRelatedSideCards();
            HideBuffHistoryPanel();   // FIX kẹt panel buff khi card bị destroy giữa lúc inspect
            HideItemPanel();
            if (model == null) return;
            model.OnStatsChanged -= _onStatsChanged;
            model.OnLocationChanged -= _onLocationChanged;
            model.OnLeveledUp -= _onLeveledUp;
            model.OnDamaged -= _onDamaged;
            model.OnDied -= _onDied;
        }
    }

    [System.Serializable]
    public struct KeywordIconEntry
    {
        public KeywordType keyword;
        public Sprite icon;
    }
}