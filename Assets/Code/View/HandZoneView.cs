using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.EventSystems;
using LoRClone.Model;

namespace LoRClone.View
{
    public class HandZoneView : ZoneView
    {
        // ── Collapsed Fan ─────────────────────────────────────────
        [Header("═══ COLLAPSED FAN ═══")]
        public Vector2 collapsedPos = new Vector2(0f, 0f);
        [Tooltip("Player: (0.5,0). Enemy: (0.5,1).")]
        public Vector2 collapsedAnchorMin = new Vector2(0.5f, 0f);
        public Vector2 collapsedAnchorMax = new Vector2(0.5f, 0f);
        public Vector2 collapsedPivot = new Vector2(0.5f, 0f);
        [Tooltip("Kích thước lá khi collapsed")]
        public Vector2 collapsedCardSize = new Vector2(65f, 95f);
        [Tooltip("Tổng góc xòe fan (degrees). 60-80 ≈ LoR")]
        public float fanAngle = 70f;
        [Tooltip("Tổng độ rộng ngang của fan collapsed (px). Đủ để click riêng từng lá. 180-250 đẹp.")]
        public float collapsedTotalSpread = 200f;
        [Tooltip("Dịch ngang tâm quạt (px). 0 = giữa màn, dương = phải. LoR ≈ 0 (giữa).")]
        public float fanXOffset = 0f;
        [Tooltip("Tỉ lệ lá bị ẩn ra ngoài màn hình. 0.6 = ẩn 60%, hiện 40%.")]
        [Range(0f, 0.90f)]
        public float collapsedHideRatio = 0.60f;
        [Tooltip("Bật cho enemy hand — cắt phần trên + flip fan")]
        public bool invertHideDirection = false;

        // ── Expanded ──────────────────────────────────────────────
        [Header("═══ EXPANDED ═══")]
        public Vector2 expandedPos = new Vector2(0f, 15f);
        public Vector2 expandedAnchorMin = new Vector2(0.5f, 0f);
        public Vector2 expandedAnchorMax = new Vector2(0.5f, 0f);
        public Vector2 expandedPivot = new Vector2(0.5f, 0f);
        [Tooltip("Kích thước lá khi expanded (base — sẽ nhân với multiplier bên dưới).")]
        public Vector2 expandedCardSize = new Vector2(90f, 130f);

        [Header("═══ EXPANDED — Adaptive layout (fix cứng) ═══")]
        [Tooltip("Nhân size hand để TO HƠN bench cho dễ phân biệt. 1.25 = to hơn 25%.")]
        public float expandedSizeMultiplier = 1.25f;
        [Tooltip("Bề rộng tối đa của quạt bài = canvasWidth × factor. Tự co theo độ phân giải, "
                 + "KHÔNG hardcode. Nhiều lá sẽ tự chồng mép để không tràn ra ngoài / đè bench.")]
        [Range(0.4f, 1f)] public float expandedMaxWidthFactor = 0.82f;
        [Tooltip("Góc nghiêng mỗi lá (độ). Ít lá gần như phẳng; tổng góc bị cap bởi expandedFanAngle.")]
        public float expandedAnglePerCard = 3f;
        [Tooltip("Độ cong vòng cung: lá giữa cao hơn 2 mép (px). 0 = hàng thẳng.")]
        public float expandedArcHeight = 14f;
        [Tooltip("Spacing giữa lá khi expanded (flat row)")]
        public float expandedSpacing = 6f;
        [Tooltip("Số lá tối đa trước khi expanded cũng dùng fan thay vì hàng ngang. 0 = luôn dùng hàng ngang.")]
        public int expandedFanThreshold = 7;
        [Tooltip("Tổng góc xòe khi expanded fan (degrees). 40-60 ≈ LoR")]
        public float expandedFanAngle = 50f;
        [Tooltip("Tổng độ rộng ngang của fan expanded (px). 500-800 đẹp.")]
        public float expandedTotalSpread = 650f;
        [Tooltip("Tỉ lệ lá bị ẩn khi expanded fan. Nhỏ hơn collapsed.")]
        [Range(0f, 0.85f)]
        public float expandedHideRatio = 0.20f;

        // ── Toggle ────────────────────────────────────────────────
        [Header("Toggle")]
        [Tooltip("true = player (có expand). false = enemy (luôn collapsed).")]
        public bool canExpand = true;

        // ── Runtime ───────────────────────────────────────────────
        bool _expanded = false;
        RectTransform _rt;
        CanvasGroup _canvasGroup;
        readonly List<CardView> _handCards = new List<CardView>();

        // ── Draw fly-in (lá lướt từ deck về slot) ────────────────
        [Header("Draw Animation")]
        [Tooltip("Bật hiệu ứng lá lướt vào tay khi rút. CHẠY NGAY không cần setup gì.")]
        public bool enableDrawAnimation = true;
        [Tooltip("(Tùy chọn) Kéo ảnh deck (HUDView.playerDeckButton / enemyDeckButton) để lá bay TỪ đúng deck. "
                 + "Để TRỐNG cũng chạy — lá tự bay vào từ mép màn hình (dưới cho player, trên cho enemy).")]
        public Transform deckAnchor;
        [Tooltip("Thời gian lá bay từ deck về slot (giây).")]
        public float flyDuration = 0.28f;
        [Tooltip("Delay giữa các lá khi rút nhiều lá cùng lúc (giây).")]
        public float flyStagger = 0.07f;
        [Range(0.1f, 1f)]
        [Tooltip("Scale lá lúc còn ở deck (nhỏ hơn lúc về tay).")]
        public float deckStartScale = 0.6f;

        [Header("Draw Zoom — bài rút phóng to lên giữa màn hình rồi vào tay")]
        [Tooltip("Bật: lá rút của MÌNH sẽ phóng to lên giữa màn hình rồi mới vào tay. "
                 + "Chỉ áp dụng cho hand player (canExpand). Tay địch úp bài → luôn bay thẳng deck→tay.")]
        public bool drawZoomToCenter = true;
        [Tooltip("Scale đỉnh khi lá ở giữa màn hình (so với scale lúc ở tay). 2.2 = to gấp ~2.2 lần.")]
        public float drawCenterScale = 2.2f;
        [Tooltip("Thời gian lá bay từ deck → giữa màn hình + phóng to (giây).")]
        public float drawZoomUpDuration = 0.28f;
        [Tooltip("Giữ ở giữa màn hình cho người chơi thấy rõ mặt bài (giây).")]
        public float drawCenterHold = 0.12f;
        [Tooltip("Thời gian lá bay từ giữa màn hình → slot trong tay + thu nhỏ (giây).")]
        public float drawToHandDuration = 0.24f;

        readonly HashSet<CardView> _flying = new HashSet<CardView>();
        int _flyIndex = 0;
        bool _openingRevealDone = false;

        void Awake()
        {
            _rt = GetComponent<RectTransform>();
            _canvasGroup = GetComponent<CanvasGroup>();
            if (_canvasGroup == null)
                _canvasGroup = gameObject.AddComponent<CanvasGroup>();
        }

        void Update()
        {
            if (!_expanded || !canExpand) return;
            if (!Input.GetMouseButtonDown(0)) return;

            foreach (var cv in _handCards)
                if (cv != null && cv.IsInspecting) return;

            var pointerData = new PointerEventData(EventSystem.current)
            { position = Input.mousePosition };
            var results = new List<RaycastResult>();
            EventSystem.current.RaycastAll(pointerData, results);

            bool hitHand = false;
            foreach (var r in results)
            {
                if (r.gameObject == null) continue;
                Transform t = r.gameObject.transform;
                while (t != null)
                {
                    if (t == this.transform) { hitHand = true; break; }
                    t = t.parent;
                }
                if (hitHand) break;
            }

            if (!hitHand) { _expanded = false; RefreshLayout(); }
        }

        // ── Init ─────────────────────────────────────────────────
        public void Init(PlayerModel playerModel)
        {
            playerModel.OnCardDrawn += (_, c) => AddCard(c, fromDeck: true);
            playerModel.OnCardCreated += (_, c) => AddCard(c, fromDeck: true); // card tạo bằng hiệu ứng
            playerModel.OnCardRecalled += (_, c) => AddCard(c);  // card bị Recall về tay — stats đã reset
            // Mulligan trả bài về deck — xóa view tương ứng
            playerModel.OnCardRemovedFromHand += (_, c) => DropCard(c);
            playerModel.OnCardPlayedToBench += (_, c) => DropCard(c);
            playerModel.OnCardCast += (_, c) => DropCard(c);
            playerModel.OnSpellStaged += (_, c) => DropCard(c);
            playerModel.OnSpellUnstaged += (_, c) => AddCard(c);
            playerModel.OnSpellReturnedToHand += (_, c) => AddCard(c);
            foreach (var c in playerModel.hand) AddCard(c);

            // Ẩn hand trong Mulligan; collapse khi mất priority (giống LoR gốc)
            var gc = Controller.GameController.Instance;
            if (gc != null)
            {
                gc.OnStateChanged += m =>
                {
                    bool handVisibleNow = m.phase != GamePhase.Mulligan;
                    SetHandVisible(handVisibleNow);
                    // Reveal tay mở đầu: lần đầu hand hiện (sau mulligan) → bay cả tay từ deck.
                    if (handVisibleNow && !_openingRevealDone)
                    {
                        _openingRevealDone = true;
                        if (enableDrawAnimation && _handCards.Count > 0)
                            StartCoroutine(RevealOpeningHand());
                    }

                    // Thu hand về fan khi player này mất priority — đúng như LoR:
                    // player có thể kéo nhiều spell liên tiếp miễn còn priority,
                    // hand chỉ thu lại khi pass xong và đến lượt đối thủ phản ứng.
                    if (canExpand && _expanded)
                    {
                        bool thisSideHasPriority = (isPlayer == m.isPlayerPriority);
                        bool actionable = thisSideHasPriority
                                          && m.phase != GamePhase.CombatResolve
                                          && m.phase != GamePhase.GameOver
                                          && m.phase != GamePhase.Mulligan;
                        if (!actionable)
                        {
                            _expanded = false;
                            RefreshLayout();
                        }
                    }
                };
                SetHandVisible(gc.model.phase != GamePhase.Mulligan);
            }

            RefreshLayout();
        }

        // ── Card management ───────────────────────────────────────
        void AddCard(CardModel cardModel, bool fromDeck = false)
        {
            if (cardPrefab == null) { Debug.LogError("[HandZoneView] cardPrefab chưa gán!"); return; }
            var go = Instantiate(cardPrefab, transform);
            var cv = go.GetComponent<CardView>();
            if (cv == null) { Destroy(go); return; }

            cv.Bind(cardModel);
            if (!isPlayer) cv.SetFaceDown(true);   // tay ĐỊCH úp mặt sau
            cv.OnDragIntent += (view, intent) => FireDragIntent(view, intent);
            cv.OnDropped += (view, _) => FireDrop(view);

            cv.ClickShouldExpand = () =>
            {
                if (!canExpand || _expanded) return false;
                _expanded = true;
                RefreshLayout();
                return true;
            };

            _handCards.Add(cv);
            SyncSlotMap();
            RefreshLayout();

            // Lá rút/tạo → xếp vào hàng đợi (2 phe không đè nhau, phe có token trước) rồi zoom vào tay.
            if (fromDeck && enableDrawAnimation && HandVisible && _openingRevealDone)
                EnqueueDraw(cv, allowZoom: true);
        }

        void DropCard(CardModel cardModel)
        {
            var cv = _handCards.Find(c => c != null && c.model == cardModel);
            if (cv == null) return;
            _handCards.Remove(cv);
            Destroy(cv.gameObject);
            SyncSlotMap();
            RefreshLayout();
        }

        void SyncSlotMap()
        {
            _slotMap.Clear();
            for (int i = 0; i < _handCards.Count; i++)
                if (_handCards[i] != null) _slotMap[i] = _handCards[i];
        }

        public new void RemoveCard(CardModel model) => DropCard(model);

        /// <summary>
        /// Collapse hand ngay lập tức để không che bench/battlefield khi player đang chọn target.
        /// Không restore tự động — trạng thái sau targeting do priority logic xử lý.
        /// </summary>
        public void CollapseForTargeting()
        {
            if (!canExpand) return;
            _expanded = false;
            RefreshLayout();
        }

        void SetHandVisible(bool visible)
        {
            if (_canvasGroup == null) return;
            _canvasGroup.alpha = visible ? 1f : 0f;
            _canvasGroup.interactable = visible;
            _canvasGroup.blocksRaycasts = visible;
        }

        bool HandVisible => _canvasGroup == null || _canvasGroup.alpha > 0.5f;

        // ── Draw fly-in ───────────────────────────────────────────
        IEnumerator RevealOpeningHand()
        {
            RefreshLayout(); // đảm bảo target slot đã tính
            var snapshot = new List<CardView>(_handCards);
            foreach (var cv in snapshot)
            {
                if (cv == null) continue;
                EnqueueDraw(cv, allowZoom: false); // reveal cả tay → bay thẳng, xếp hàng đợi theo phe
            }
            yield break;
        }

        // Cache transform ảnh deck (lấy từ HUDView) khi deckAnchor không được gán thủ công.
        Transform _resolvedDeck;

        /// <summary>
        /// Tìm transform ảnh deck để bài bay TỪ ĐÚNG deck.
        /// Ưu tiên deckAnchor (gán tay); nếu trống → tự lấy HUDView.playerDeckButton/enemyDeckButton.
        /// </summary>
        Transform ResolveDeckTransform()
        {
            if (deckAnchor != null) return deckAnchor;
            if (_resolvedDeck != null) return _resolvedDeck;

            var hud = FindObjectOfType<HUDView>();
            if (hud != null)
            {
                var btn = isPlayer ? hud.playerDeckButton : hud.enemyDeckButton;
                if (btn != null) _resolvedDeck = btn.transform;
            }
            return _resolvedDeck;
        }

        /// <summary>Điểm xuất phát khi bay: deck thật (deckAnchor hoặc ảnh deck trong HUDView);
        /// nếu vẫn không tìm thấy → mép màn hình (dưới cho player, trên cho enemy).</summary>
        Vector3 DeckSourcePos(Vector3 targetPos)
        {
            var deck = ResolveDeckTransform();
            if (deck != null) return deck.position;
            float dir = canExpand ? -1f : 1f;          // player (canExpand) bay từ dưới lên; enemy từ trên xuống
            return targetPos + new Vector3(0f, dir * Screen.height * 0.6f, 0f);
        }

        // ── Draw sequencing: 2 phe KHÔNG draw đè nhau ─────────────
        // Hàng đợi TĨNH dùng chung cho cả player + enemy hand.
        // Phe CÓ attack token draw trước; xong hết mới tới phe không có token.
        // Rút nhiều lá liên tục (VD phòng thủ / mulligan) → chạy tuần tự từng lá.
        struct DrawReq { public HandZoneView view; public CardView cv; public bool allowZoom; public int priority; public int order; }
        static readonly List<DrawReq> _drawQueue = new List<DrawReq>();
        static bool _drawRunning = false;
        static int _drawOrderCounter = 0;

        void EnqueueDraw(CardView cv, bool allowZoom)
        {
            int priority = 1; // 0 = phe có attack token (draw trước), 1 = phe kia
            var gc = Controller.GameController.Instance;
            if (gc != null && gc.model != null)
                priority = (isPlayer == gc.model.playerHasAttackToken) ? 0 : 1;

            _drawQueue.Add(new DrawReq { view = this, cv = cv, allowZoom = allowZoom, priority = priority, order = _drawOrderCounter++ });

            if (!_drawRunning)
            {
                _drawRunning = true;
                StartCoroutine(RunDrawQueue());
            }
        }

        IEnumerator RunDrawQueue()
        {
            // FIX rủi ro ordering: KHÔNG chỉ chờ 1 frame. Gom tới khi hàng đợi "ổn định"
            // (không có lá mới nào được thêm trong vài frame liên tiếp) → bắt được cả trường hợp
            // controller rút cho 2 phe rải qua nhiều frame, đảm bảo phe token luôn được xếp trước.
            // Có cap tổng frame để không bao giờ treo.
            const int settleFramesNeeded = 3;
            const int maxGatherFrames = 30;
            int stableFrames = 0;
            int lastCount = -1;
            int gathered = 0;
            while (stableFrames < settleFramesNeeded && gathered < maxGatherFrames)
            {
                if (_drawQueue.Count != lastCount) { lastCount = _drawQueue.Count; stableFrames = 0; }
                else stableFrames++;
                gathered++;
                yield return null;
            }

            while (_drawQueue.Count > 0)
            {
                // Chọn request ưu tiên nhất: priority nhỏ trước (phe có token), cùng priority giữ thứ tự rút.
                int best = 0;
                for (int i = 1; i < _drawQueue.Count; i++)
                {
                    bool better = _drawQueue[i].priority < _drawQueue[best].priority
                        || (_drawQueue[i].priority == _drawQueue[best].priority && _drawQueue[i].order < _drawQueue[best].order);
                    if (better) best = i;
                }
                var req = _drawQueue[best];
                _drawQueue.RemoveAt(best);

                // Chạy TUẦN TỰ: đợi lá này bay xong mới tới lá kế (kể cả khác phe).
                if (req.view != null && req.cv != null)
                    yield return req.view.StartCoroutine(req.view.FlyIn(req.cv, 0, req.allowZoom));
            }

            _drawRunning = false;
        }

        IEnumerator FlyIn(CardView cv, int staggerIndex, bool allowZoom = true)
        {
            if (cv == null || !enableDrawAnimation) yield break;

            // Target = vị trí RefreshLayout vừa đặt cho lá (capture đồng bộ ngay).
            Vector3 targetPos = cv.transform.position;
            Quaternion targetRot = cv.transform.rotation;
            Vector3 targetScale = cv.transform.localScale;

            _flying.Add(cv);
            cv.AllowInteract = false;

            // Đưa lá về điểm xuất phát ngay (tránh 'hiện ở tay rồi nhảy').
            Vector3 startPos = DeckSourcePos(targetPos);
            cv.transform.position = startPos;
            cv.transform.rotation = Quaternion.identity;
            cv.transform.localScale = targetScale * deckStartScale;

            if (flyStagger > 0f && staggerIndex > 0)
                yield return new WaitForSeconds(flyStagger * staggerIndex);

            // Nâng sorting order để lá nổi lên trên khi bay/zoom (RefreshLayout cuối sẽ set lại).
            var cvCanvas = cv.GetComponent<Canvas>();
            int prevOrder = cvCanvas != null ? cvCanvas.sortingOrder : 1;
            if (cvCanvas != null) cvCanvas.sortingOrder = 120;

            // Chỉ tay MÌNH (canExpand) mới phóng to giữa màn hình. Tay địch úp bài → bay thẳng.
            bool zoom = drawZoomToCenter && canExpand && allowZoom;

            if (zoom)
            {
                Vector3 center = new Vector3(Screen.width * 0.5f, Screen.height * 0.5f, 0f);
                Vector3 peakScale = targetScale * drawCenterScale;

                // Pha A: deck → GIỮA màn hình + phóng to
                float e = 0f;
                float upDur = Mathf.Max(0.01f, drawZoomUpDuration);
                while (e < upDur && cv != null)
                {
                    e += Time.deltaTime;
                    float t = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(e / upDur));
                    cv.transform.position = Vector3.Lerp(startPos, center, t);
                    cv.transform.localScale = Vector3.Lerp(targetScale * deckStartScale, peakScale, t);
                    yield return null;
                }
                if (cv == null) yield break;
                cv.transform.position = center;
                cv.transform.rotation = Quaternion.identity;
                cv.transform.localScale = peakScale;

                // Pha B: giữ ở giữa cho thấy rõ mặt bài
                if (drawCenterHold > 0f) yield return new WaitForSeconds(drawCenterHold);

                // Pha C: giữa màn hình → slot trong tay + thu nhỏ về size tay
                e = 0f;
                float toHand = Mathf.Max(0.01f, drawToHandDuration);
                while (e < toHand && cv != null)
                {
                    e += Time.deltaTime;
                    float t = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(e / toHand));
                    cv.transform.position = Vector3.Lerp(center, targetPos, t);
                    cv.transform.rotation = Quaternion.Slerp(Quaternion.identity, targetRot, t);
                    cv.transform.localScale = Vector3.Lerp(peakScale, targetScale, t);
                    yield return null;
                }
            }
            else
            {
                // Bay thẳng deck → slot (tay địch úp bài, hoặc opening reveal, hoặc tắt zoom)
                float e = 0f;
                while (e < flyDuration && cv != null)
                {
                    e += Time.deltaTime;
                    float t = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(e / flyDuration));
                    cv.transform.position = Vector3.Lerp(startPos, targetPos, t);
                    cv.transform.rotation = Quaternion.Slerp(Quaternion.identity, targetRot, t);
                    cv.transform.localScale = Vector3.Lerp(targetScale * deckStartScale, targetScale, t);
                    yield return null;
                }
            }
            if (cv == null) yield break;

            cv.transform.position = targetPos;
            cv.transform.rotation = targetRot;
            cv.transform.localScale = targetScale;
            if (cvCanvas != null) cvCanvas.sortingOrder = prevOrder;

            _flying.Remove(cv);
            cv.AllowInteract = !cv.IsFaceDown;   // enemy face-down giữ khoá; còn lại mở tương tác
            if (_flying.Count == 0) _flyIndex = 0;
            RefreshLayout(); // chỉnh về fan hiện tại (lá khác có thể đã dịch)
        }

        // ── Layout ────────────────────────────────────────────────
        void RefreshLayout()
        {
            if (_rt == null) return;
            if (_expanded && canExpand) RefreshExpanded();
            else RefreshCollapsedFan();
        }

        void RefreshExpanded()
        {
            _rt.anchorMin = expandedAnchorMin;
            _rt.anchorMax = expandedAnchorMax;
            _rt.pivot = expandedPivot;

            int count = _handCards.Count;

            for (int i = 0; i < count; i++)
                if (_handCards[i] != null)
                    _handCards[i].SetHandSortingOrder(i + 1);

            // ── Adaptive layout: ít lá → spacing dễ chịu, căn giữa; nhiều lá → chồng mép, không tràn ──
            float cardW = expandedCardSize.x * expandedSizeMultiplier;
            float cardH = expandedCardSize.y * expandedSizeMultiplier;

            // Bề rộng khả dụng theo canvas (tự co theo độ phân giải, không hardcode)
            float canvasW = Screen.width;
            var rootCanvas = GetComponentInParent<Canvas>();
            if (rootCanvas != null)
            {
                var crt = rootCanvas.transform as RectTransform;
                if (crt != null && crt.rect.width > 1f) canvasW = crt.rect.width;
            }
            float maxWidth = canvasW * expandedMaxWidthFactor;

            // Step: mặc định spacing dễ chịu; nếu vượt maxWidth thì chồng mép cho vừa
            float desiredStep = cardW + expandedSpacing;
            float step = (count > 1 && (count - 1) * desiredStep > maxWidth)
                       ? maxWidth / (count - 1)
                       : desiredStep;
            float startX = -(count - 1) * step * 0.5f;

            // Fan nhẹ: tổng góc cap bởi expandedFanAngle; ít lá gần như phẳng
            float totalAngle = Mathf.Min(expandedFanAngle, (count - 1) * expandedAnglePerCard);
            float startAngle = totalAngle * 0.5f;
            float angleStep = count > 1 ? totalAngle / (count - 1) : 0f;

            float hideSign = invertHideDirection ? 1f : -1f;
            _rt.anchoredPosition = expandedPos + new Vector2(0f, hideSign * cardH * expandedHideRatio);

            for (int i = 0; i < count; i++)
            {
                var cv = _handCards[i];
                if (cv == null || cv.IsInspecting || _flying.Contains(cv)) continue;

                cv.SetHandScale(cardW, cardH);
                var rt = cv.GetComponent<RectTransform>();
                if (rt == null) continue;

                float x = count > 1 ? startX + i * step : 0f;
                float tNorm = count > 1 ? ((float)i / (count - 1) - 0.5f) : 0f; // -0.5..0.5
                float yArc = expandedArcHeight * (1f - 4f * tNorm * tNorm);       // 0 ở mép, max ở giữa
                float rotZ = count > 1 ? startAngle - i * angleStep : 0f;
                if (invertHideDirection) { rotZ = -rotZ; yArc = -yArc; }

                rt.anchorMin = new Vector2(0.5f, 0f);
                rt.anchorMax = new Vector2(0.5f, 0f);
                rt.pivot = new Vector2(0.5f, 0f);
                rt.anchoredPosition = new Vector2(x + fanXOffset, yArc);
                cv.transform.localEulerAngles = new Vector3(0f, 0f, rotZ);
                cv.SaveCurrentPosition();
            }
        }

        void RefreshCollapsedFan()
        {
            _rt.anchorMin = collapsedAnchorMin;
            _rt.anchorMax = collapsedAnchorMax;
            _rt.pivot = collapsedPivot;

            float hideSign = invertHideDirection ? 1f : -1f;
            float hideOffset = hideSign * collapsedCardSize.y * collapsedHideRatio;
            _rt.anchoredPosition = collapsedPos + new Vector2(0f, hideOffset);

            int count = _handCards.Count;

            for (int i = 0; i < count; i++)
                if (_handCards[i] != null)
                    _handCards[i].SetHandSortingOrder(i + 1);

            float angleStep = count > 1 ? fanAngle / (count - 1) : 0f;
            float startAngle = fanAngle * 0.5f;

            float xStep = count > 1 ? collapsedTotalSpread / (count - 1) : 0f;
            float startX = -collapsedTotalSpread * 0.5f;

            for (int i = 0; i < count; i++)
            {
                var cv = _handCards[i];
                if (cv == null || cv.IsInspecting || _flying.Contains(cv)) continue;

                cv.SetHandScale(collapsedCardSize.x, collapsedCardSize.y);
                var rt = cv.GetComponent<RectTransform>();
                if (rt == null) continue;

                float arcX = count > 1 ? startX + i * xStep : 0f;
                float rotZ = count > 1 ? startAngle - i * angleStep : 0f;
                if (invertHideDirection) rotZ = -rotZ;

                rt.anchorMin = new Vector2(0.5f, 0f);
                rt.anchorMax = new Vector2(0.5f, 0f);
                rt.pivot = new Vector2(0.5f, 0f);
                rt.anchoredPosition = new Vector2(arcX + fanXOffset, 0f);
                cv.transform.localEulerAngles = new Vector3(0f, 0f, rotZ);
                cv.SaveCurrentPosition();
            }
        }
    }
}