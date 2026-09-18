using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using LoRClone.Model;

namespace LoRClone.View
{
    /// <summary>
    /// Xử lý toàn bộ animation combat: dash, clash, return về slot, nexus pulse, death fade.
    /// Thêm Section 2 Gameplay Feel Bible: AnimateSummon (B8-B12).
    ///
    /// Nguyên lý:
    ///   Mỗi card trên battlefield là con của slot transform, anchoredPosition = (0,0) khi đứng yên.
    ///   Dash = animate anchoredPosition từ (0,0) đến offset (tính trong canvas reference units).
    ///   Return = animate anchoredPosition từ offset về (0,0).
    ///
    /// Tại sao dùng InverseTransformPoint:
    ///   transform.position của UI element là screen-pixel coords (world space của Overlay Canvas).
    ///   anchoredPosition là canvas reference units — khác screen pixels khi canvas.scaleFactor != 1.
    ///   parent.InverseTransformPoint(worldPos) tự động chia cho scaleFactor, cho ra đúng offset local.
    ///
    /// Setup:
    ///   1. Tạo Empty GameObject trong scene.
    ///   2. Gắn component này + kéo playerBattlefield, enemyBattlefield vào Inspector.
    ///   3. Kéo GameObject đó vào field `combatAnimator` của GameController.
    ///   (★ Nếu bật autoWire — mặc định — bước 2 và 3 KHÔNG bắt buộc: component tự lấy zone
    ///      từ GameView và tự gán mình vào GameController. Xem AutoWireZones / SelfRegisterToController.)
    ///
    /// Nexus shake:
    ///   Gán playerNexusTransform / enemyNexusTransform → shake nexus đúng chỗ.
    ///   Để trống → không shake nexus.
    ///
    /// Nexus dash direction:
    ///   Gán playerNexusTransform / enemyNexusTransform → dash theo hướng đó (nexusDashFraction%).
    ///   Để trống → dùng nexusDashOffset (canvas units). Chỉnh offset cho đúng layout màn hình:
    ///     Enemy ở trên player → nexusDashOffset = (0, 150, 0)
    ///     Enemy ở bên phải   → nexusDashOffset = (150, 0, 0)
    /// </summary>
    public class CombatAnimator : MonoBehaviour
    {
        // ── Play-to-Bench Style (đưa unit ra sân) ─────────────────
        public enum PlaySummonStyle { AutoByOwner, ZoomSlam, FlipReveal, Classic }
        [Header("Play-to-Bench Style")]
        [Tooltip("AutoByOwner = player: zoom+đập; enemy: úp→lật→đập. ZoomSlam/FlipReveal = ép 1 kiểu. Classic = như cũ.")]
        public PlaySummonStyle playSummonStyle = PlaySummonStyle.AutoByOwner;
        [Tooltip("Thời gian zoom lên giữa màn hình (giây).")]
        public float zoomUpDuration = 0.22f;
        [Tooltip("Giữ ở giữa màn hình trước khi đập (giây).")]
        public float zoomHoldDuration = 0.08f;
        [Tooltip("Thời gian đập xuống bench (ease-in, giây).")]
        public float slamDuration = 0.16f;
        [Tooltip("Scale đỉnh khi zoom giữa màn hình (so với scale bench).")]
        public float zoomPeakScale = 1.45f;
        [Tooltip("Thời gian lật bài (FlipReveal, giây).")]
        public float flipDuration = 0.26f;
        [Tooltip("(Tùy chọn) SFX khi đập xuống bench.")]
        public AudioClip slamSfx;

        [Header("Battlefield Zone refs — kéo từ Scene (cùng BattlefieldZoneView của GameView)")]
        public BattlefieldZoneView playerBattlefield;
        public BattlefieldZoneView enemyBattlefield;

        [Header("Bench Zone refs — dùng cho AnimateSummon (kéo BenchZoneView từ Scene)")]
        [Tooltip("ZoneView của bench player. Gán BenchZoneView của player vào đây.")]
        public ZoneView playerBench;
        [Tooltip("ZoneView của bench enemy. Gán BenchZoneView của enemy vào đây.")]
        public ZoneView enemyBench;

        [Header("Hand Zone refs — dùng để lấy world pos trước khi card rời tay")]
        [Tooltip("ZoneView của hand player (HandZoneView). Gán vào đây để card fly từ tay xuống bench.")]
        public ZoneView playerHand;
        [Tooltip("ZoneView của hand enemy (HandZoneView).")]
        public ZoneView enemyHand;

        [Header("Nexus refs (optional)")]
        [Tooltip("Transform của nexus player. Dùng cho dash direction + shake. Để trống nếu không cần.")]
        public Transform playerNexusTransform;
        [Tooltip("Transform của nexus enemy. Dùng cho dash direction + shake. Để trống nếu không cần.")]
        public Transform enemyNexusTransform;

        [Header("Combat Timing")]
        [Tooltip("Thời gian mỗi lá dash (giây). Tăng lên 0.35–0.45 để dễ thấy hơn.")]
        public float dashDuration = 0.32f;
        [Tooltip("Thời gian mỗi lá trở về battleslot sau clash (giây).")]
        public float returnDuration = 0.22f;
        [Tooltip("Thời gian nexus shake khi bị đánh (giây).")]
        public float nexusPulseDuration = 0.14f;
        [Tooltip("Thời gian card fade out khi chết (giây).")]
        public float dieFadeDuration = 0.28f;

        [Header("Dash Distance")]
        [Tooltip("QuickAttack: attacker tiến bao nhiêu % về phía blocker slot (0.85 = gần chạm).")]
        [Range(0.5f, 1f)]
        public float quickAttackDashFraction = 0.85f;

        [Header("Summon Animation — Section 2 Gameplay Feel Bible")]
        [Tooltip("B9: thời gian card scale từ 0 → peak (spawn pop-in). Bible ~80ms.")]
        public float summonPopInDuration = 0.08f;
        [Tooltip("B12: thời gian bounce từ peak → 1.0 (settle). Bible ~100ms.")]
        public float summonSettleDuration = 0.10f;
        [Tooltip("Scale peak khi card vừa spawn (1.12 = phình nhẹ, tạo cảm giác va đất).")]
        public float summonPeakScale = 1.12f;

        [Header("Play From Hand Animation — Section 2 B4-B6")]
        [Tooltip("B4: thời gian card phóng to nhẹ trong tay trước khi thả (~80ms).")]
        public float handAnticipationDuration = 0.08f;
        [Tooltip("B4: scale peak của card trong tay khi anticipation (~110%).")]
        public float handAnticipationScale = 1.10f;
        [Tooltip("B5: thời gian card lao từ tay xuống bench (~180ms).")]
        public float handToBenchDuration = 0.18f;

        [Header("Camera Shake")]
        [Tooltip("Magnitude rung camera (canvas units). Bible: 1–3px với đòn thường.")]
        public float cameraShakeMagnitude = 2.5f;
        [Tooltip("Thời gian rung camera (giây).")]
        public float cameraShakeDuration = 0.12f;
        [Tooltip("Gán Camera.main transform hoặc Camera wrapper transform. Để trống = không shake.")]
        public Transform cameraTransform;

        [Header("Attack Lean")]
        [Tooltip("Khoảng cách lean forward khi declare attack (canvas units). Hướng về phía địch.")]
        public float attackLeanOffset = 18f;
        [Tooltip("Thời gian lean forward (giây).")]
        public float attackLeanDuration = 0.12f;
        [Tooltip("Thời gian hold ở vị trí lean (giây) — người chơi thấy unit 'đứng tấn'.")]
        public float attackLeanHoldDuration = 0.08f;
        [Tooltip("Thời gian thu về vị trí ban đầu sau lean (giây).")]
        public float attackLeanReturnDuration = 0.10f;

        [Header("Floating Damage Numbers")]
        [Tooltip("Font size của damage number (nếu không có TMPro prefab, dùng Unity built-in text).")]
        public float damageNumberFontSize = 36f;
        [Tooltip("Màu damage number bình thường.")]
        public Color damageNumberColor = new Color(1f, 0.3f, 0.3f, 1f);
        [Tooltip("Màu khi damage là heal (số dương, màu xanh).")]
        public Color healNumberColor = new Color(0.3f, 1f, 0.4f, 1f);
        [Tooltip("Khoảng cách float lên (canvas units).")]
        public float damageNumberFloatDistance = 60f;
        [Tooltip("Thời gian float (giây).")]
        public float damageNumberDuration = 0.7f;
        [Tooltip("Canvas root để spawn damage number vào. Nếu null, tự tìm Canvas đầu tiên trong scene.")]
        public Canvas rootCanvas;

        [Header("HP Flash")]
        [Tooltip("Màu flash của HP text khi nhận damage.")]
        public Color hpFlashColor = new Color(1f, 0.2f, 0.2f, 1f);
        [Tooltip("Thời gian flash HP text (giây).")]
        public float hpFlashDuration = 0.18f;

        [Header("Spell Cast VFX")]
        [Tooltip("Thời gian bolt bay từ nguồn tới mục tiêu (giây).")]
        public float spellBoltDuration = 0.26f;
        [Tooltip("Kích thước bolt (canvas units).")]
        public float spellBoltSize = 30f;
        [Tooltip("Màu bolt phép.")]
        public Color spellBoltColor = new Color(0.5f, 0.85f, 1f, 1f);

        [Header("Hit Squash")]
        [Tooltip("Scale X squash khi impact (<1 = dẹt theo X, >1 Y). 0.85 = squash vừa phải.")]
        public float hitSquashX = 0.85f;
        [Tooltip("Scale Y khi squash (>1 vì volume bảo toàn).")]
        public float hitSquashY = 1.18f;
        [Tooltip("Thời gian squash (giây). ~0.05–0.08.")]
        public float hitSquashDuration = 0.06f;
        [Tooltip("Thời gian phục hồi scale về 1 sau squash (giây).")]
        public float hitRecoverDuration = 0.10f;

        [Header("Nexus Dash Direction (canvas units — KHÔNG phải pixel)")]
        [Tooltip("Hướng và khoảng cách unit lao về phía nexus địch.\n" +
                 "Đây là offset trong canvas reference space (không cần gán nexusTransform để dash).\n\n" +
                 "Cách chỉnh:\n" +
                 "  Enemy ở TRÊN player  → (0, 150, 0)   ← thường đúng với layout dọc\n" +
                 "  Enemy ở DƯỚI player  → (0, -150, 0)\n" +
                 "  Enemy ở BÊN PHẢI     → (150, 0, 0)\n\n" +
                 "QUAN TRỌNG: nexusTransform CHỈ dùng để shake/rung nexus khi bị đánh.\n" +
                 "Không nên gán nexusTransform vào element UI hiển thị HP vì sẽ gây dash sai hướng.")]
        public Vector3 nexusDashOffset = new Vector3(0f, 150f, 0f);

        // ══════════════════════════════════════════════════════════════════════════
        // ★ AUTO-WIRE (rào trước lỗi "build mất animation do ref chưa save scene")
        // ══════════════════════════════════════════════════════════════════════════
        [Header("★ Auto-Wire — rào trước mất ref khi build")]
        [Tooltip("BẬT (khuyên dùng): khi vào scene, tự lấy zone ref (battlefield/bench/hand) từ GameView " +
                 "cho các field còn TRỐNG, và tự gán mình vào GameController.combatAnimator nếu nó null.\n\n" +
                 "Lý do tồn tại: animation trong Editor chạy vì ref sống trong scene-in-memory; khi BUILD, " +
                 "Unity đọc scene TRÊN Ổ ĐĨA — nếu bạn kéo ref mà QUÊN Ctrl+S save scene, ref sẽ null trong " +
                 "build → mọi animation bị 'if (cv==null) yield break' bỏ qua IM LẶNG → build mất sạch animation. " +
                 "AutoWire lấy ref từ GameView (đã gán sẵn) nên build LUÔN = Editor, bất kể đã save hay chưa.\n\n" +
                 "Tắt nếu bạn muốn kiểm soát ref hoàn toàn bằng tay.")]
        public bool autoWire = true;

        // ── Setup validation ──────────────────────────────────────
        // Tự kiểm tra reference khi khởi động. Nếu animation "không hiện", xem Console:
        // warning ở đây cho biết chính xác ref nào đang thiếu (nguyên nhân phổ biến nhất).
        void Awake()
        {
            if (autoWire) AutoWireZones();     // ★ lấy zone ref còn trống từ GameView
            ValidateSetup();
            if (autoWire) StartCoroutine(SelfRegisterToController()); // ★ tự gán vào GameController
        }

        /// <summary>
        /// ★ Lấy zone ref (battlefield/bench/hand) + rootCanvas từ GameView cho các field còn TRỐNG.
        /// GameView chắc chắn đã gán đủ zone (nếu không, GameView tự báo lỗi khi Init) → nguồn ref an toàn.
        /// Chỉ ghi đè field null → nếu bạn đã gán tay thì giữ nguyên giá trị của bạn.
        /// </summary>
        void AutoWireZones()
        {
            var gv = FindObjectOfType<GameView>(true); // true = tìm cả object đang inactive
            if (gv == null)
            {
                Debug.LogWarning("[CombatAnimator] AutoWire: KHÔNG tìm thấy GameView trong scene — " +
                                 "không tự lấy được zone ref. Gán tay trong Inspector.", this);
                return;
            }

            int filled = 0;
            if (playerBattlefield == null && gv.playerBattlefield != null) { playerBattlefield = gv.playerBattlefield; filled++; }
            if (enemyBattlefield == null && gv.enemyBattlefield != null) { enemyBattlefield = gv.enemyBattlefield; filled++; }
            if (playerBench == null && gv.playerBench != null) { playerBench = gv.playerBench; filled++; }
            if (enemyBench == null && gv.enemyBench != null) { enemyBench = gv.enemyBench; filled++; }
            if (playerHand == null && gv.playerHand != null) { playerHand = gv.playerHand; filled++; }
            if (enemyHand == null && gv.enemyHand != null) { enemyHand = gv.enemyHand; filled++; }
            if (rootCanvas == null) { rootCanvas = gv.GetComponentInParent<Canvas>(); if (rootCanvas != null) filled++; }

            Debug.Log($"[CombatAnimator] AutoWireZones: đã điền {filled} ref còn trống từ GameView.", this);
        }

        /// <summary>
        /// ★ Nếu GameController.combatAnimator đang NULL (chưa gán / chưa save scene) → tự gán mình vào.
        /// Chờ GameController.Instance sẵn sàng (tránh phụ thuộc thứ tự Awake). Cap 10s để không treo.
        /// Đây là ref QUYẾT ĐỊNH: nếu nó null thì GameController bỏ qua MỌI animation → build im lặng.
        /// </summary>
        IEnumerator SelfRegisterToController()
        {
            float t = 0f;
            while (LoRClone.Controller.GameController.Instance == null && t < 10f)
            {
                t += Time.unscaledDeltaTime;
                yield return null;
            }

            var gc = LoRClone.Controller.GameController.Instance;
            if (gc == null)
            {
                Debug.LogWarning("[CombatAnimator] SelfRegister: GameController.Instance vẫn null sau 10s — bỏ qua.", this);
                yield break;
            }

            if (gc.combatAnimator == null)
            {
                gc.combatAnimator = this;
                Debug.LogWarning("[CombatAnimator] ★ GameController.combatAnimator đang NULL → ĐÃ TỰ GÁN mình vào. " +
                                 "ĐÂY chính là lý do build mất animation (ref chưa save scene). " +
                                 "Nên save scene lại cho chắc.", this);
            }
            else if (gc.combatAnimator != this)
            {
                Debug.Log("[CombatAnimator] SelfRegister: GameController đã có combatAnimator khác — giữ nguyên, không ghi đè.", this);
            }
        }

        /// <summary>
        /// Log cảnh báo cho từng reference chưa gán trong Inspector.
        /// Gọi tự động ở Awake; có thể gọi lại từ GameController để debug.
        /// </summary>
        public void ValidateSetup()
        {
            if (playerBattlefield == null || enemyBattlefield == null)
                Debug.LogWarning("[CombatAnimator] playerBattlefield/enemyBattlefield CHƯA gán → dash/clash/return combat sẽ KHÔNG chạy.", this);
            if (playerBench == null || enemyBench == null)
                Debug.LogWarning("[CombatAnimator] playerBench/enemyBench CHƯA gán → summon (card ra bench) sẽ KHÔNG animate.", this);
            if (playerHand == null || enemyHand == null)
                Debug.LogWarning("[CombatAnimator] playerHand/enemyHand CHƯA gán → card sẽ không bay từ tay xuống bench (chỉ pop-in tại bench).", this);
            if (cameraTransform == null)
                Debug.Log("[CombatAnimator] cameraTransform để trống → không có camera shake (tùy chọn).", this);
            if (rootCanvas == null)
                Debug.Log("[CombatAnimator] rootCanvas để trống → floating damage number sẽ tự tìm Canvas đầu tiên trong scene.", this);
        }

        // ── Public API ────────────────────────────────────────────

        /// <summary>Lấy CardView của card từ đúng battlefield zone.</summary>
        public CardView GetView(CardModel card, bool cardIsPlayer)
        {
            var zone = cardIsPlayer ? playerBattlefield : enemyBattlefield;
            return zone?.GetView(card);
        }

        /// <summary>
        /// Lấy CardView của card từ bench zone (dùng cho AnimateSummon).
        /// Yêu cầu playerBench / enemyBench được gán trong Inspector (BenchZoneView component).
        /// </summary>
        public CardView GetBenchView(CardModel card, bool cardIsPlayer)
        {
            var zone = cardIsPlayer ? playerBench : enemyBench;
            return zone?.GetView(card);
        }

        /// <summary>Lấy CardView của card từ hand zone (dùng TRƯỚC Notify để capture world pos).</summary>
        public CardView GetHandView(CardModel card, bool cardIsPlayer)
        {
            var zone = cardIsPlayer ? playerHand : enemyHand;
            return zone?.GetView(card);
        }

        /// <summary>Lấy world position của battleslot chứa card.</summary>
        public Vector3 GetSlotWorldPos(CardModel card, bool cardIsPlayer)
        {
            var zone = cardIsPlayer ? playerBattlefield : enemyBattlefield;
            if (zone == null) return Vector3.zero;
            int slot = zone.GetSlotOf(card);
            if (slot < 0 || slot >= zone.slots.Count) return Vector3.zero;
            return zone.slots[slot].position;
        }

        /// <summary>
        /// Section 2 Gameplay Feel Bible — B8-B12: Spawn animation khi card xuất hiện trên bench.
        ///
        /// B8: Card instantiate (đã có trên bench, bắt đầu từ scale = 0).
        /// B9: Scale từ 0 → summonPeakScale (~80ms, ease out) — cảm giác card "bật" vào sân.
        /// B12: Bounce từ peak → 1.0 (~100ms, ease in) — settle vào vị trí.
        ///
        /// Gọi từ GameController.SummonSequence — yield sau coroutine này mới fire WhenPlayed (B12 done).
        /// </summary>
        public IEnumerator AnimateSummon(CardView cv)
        {
            if (cv == null) yield break;

            // B8: card đã có trong scene nhưng bắt đầu từ scale = 0
            cv.transform.localScale = Vector3.zero;

            // B9: pop in — scale 0 → peak
            float elapsed = 0f;
            while (elapsed < summonPopInDuration)
            {
                elapsed += Time.deltaTime;
                float t = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(elapsed / summonPopInDuration));
                cv.transform.localScale = Vector3.Lerp(Vector3.zero, Vector3.one * summonPeakScale, t);
                yield return null;
            }
            cv.transform.localScale = Vector3.one * summonPeakScale;

            // B12: settle — scale peak → 1.0
            elapsed = 0f;
            while (elapsed < summonSettleDuration)
            {
                elapsed += Time.deltaTime;
                float t = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(elapsed / summonSettleDuration));
                cv.transform.localScale = Vector3.Lerp(Vector3.one * summonPeakScale, Vector3.one, t);
                yield return null;
            }
            cv.transform.localScale = Vector3.one;
            // Caller (SummonSequence) fires WhenPlayed sau đây — đúng B12 per Bible.
        }

        /// <summary>
        /// Đưa unit ra sân: zoom lên giữa màn hình → (lật bài nếu FlipReveal) → ĐẬP mạnh xuống bench.
        /// Thay cho AnimateCardFromPosition + AnimateSummon. Style theo playSummonStyle (Auto theo owner).
        /// </summary>
        public IEnumerator AnimatePlayToBench(CardView cv, Vector3? handWorldPos, bool cardIsPlayer)
        {
            if (cv == null) yield break;

            var style = playSummonStyle;
            if (style == PlaySummonStyle.AutoByOwner)
                style = cardIsPlayer ? PlaySummonStyle.ZoomSlam : PlaySummonStyle.FlipReveal;

            if (style == PlaySummonStyle.Classic)
            {
                if (handWorldPos.HasValue) yield return AnimateCardFromPosition(cv, handWorldPos.Value);
                yield return AnimateSummon(cv);
                yield break;
            }

            // Đích tại bench slot (capture ngay)
            Vector3 benchPos = cv.transform.position;
            Quaternion benchRot = cv.transform.rotation;
            Vector3 benchScale = cv.transform.localScale;
            if (benchScale == Vector3.zero) benchScale = Vector3.one;

            var cvCanvas = cv.GetComponent<Canvas>();
            int prevOrder = cvCanvas != null ? cvCanvas.sortingOrder : 1;
            if (cvCanvas != null) cvCanvas.sortingOrder = 60; // nổi trên tất cả

            Vector3 center = new Vector3(Screen.width * 0.5f, Screen.height * 0.5f, 0f);
            Vector3 startPos = handWorldPos ?? benchPos;
            Vector3 peakScale = benchScale * zoomPeakScale;
            bool flip = style == PlaySummonStyle.FlipReveal;

            if (flip) cv.SetFaceDown(true);

            // Phase A: zoom tay → giữa màn hình, phóng to
            cv.transform.position = startPos;
            cv.transform.rotation = Quaternion.identity;
            cv.transform.localScale = benchScale * 0.7f;
            float e = 0f;
            while (e < zoomUpDuration && cv != null)
            {
                e += Time.deltaTime;
                float t = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(e / zoomUpDuration));
                cv.transform.position = Vector3.Lerp(startPos, center, t);
                cv.transform.localScale = Vector3.Lerp(benchScale * 0.7f, peakScale, t);
                yield return null;
            }
            if (cv == null) yield break;
            cv.transform.position = center;
            cv.transform.localScale = peakScale;

            // Phase A2: lật bài (FlipReveal) — co scaleX về 0 (úp) → lật mặt → giãn lại (ngửa)
            if (flip)
            {
                float half = Mathf.Max(0.01f, flipDuration * 0.5f);
                e = 0f;
                while (e < half && cv != null)
                {
                    e += Time.deltaTime;
                    float t = Mathf.Clamp01(e / half);
                    var s = peakScale; s.x = Mathf.Lerp(peakScale.x, 0f, t);
                    cv.transform.localScale = s;
                    yield return null;
                }
                if (cv == null) yield break;
                cv.SetFaceDown(false); // điểm giữa: hiện mặt unit
                e = 0f;
                while (e < half && cv != null)
                {
                    e += Time.deltaTime;
                    float t = Mathf.Clamp01(e / half);
                    var s = peakScale; s.x = Mathf.Lerp(0f, peakScale.x, t);
                    cv.transform.localScale = s;
                    yield return null;
                }
                if (cv == null) yield break;
                cv.transform.localScale = peakScale;
            }

            if (zoomHoldDuration > 0f) yield return new WaitForSeconds(zoomHoldDuration);

            // Phase B: ĐẬP xuống bench (ease-in tăng tốc → cảm giác rơi mạnh)
            e = 0f;
            while (e < slamDuration && cv != null)
            {
                e += Time.deltaTime;
                float raw = Mathf.Clamp01(e / slamDuration);
                float t = raw * raw;
                cv.transform.position = Vector3.Lerp(center, benchPos, t);
                cv.transform.rotation = Quaternion.Slerp(Quaternion.identity, benchRot, t);
                cv.transform.localScale = Vector3.Lerp(peakScale, benchScale, t);
                yield return null;
            }
            if (cv == null) yield break;
            cv.transform.position = benchPos;
            cv.transform.rotation = benchRot;
            cv.transform.localScale = benchScale;

            // Impact: camera shake + squash + SFX (tùy)
            if (cameraTransform != null) StartCoroutine(AnimateCameraShake());
            StartCoroutine(AnimateHitSquash(cv));
            if (slamSfx != null) LoRClone.AudioManager.Play(slamSfx);

            if (cvCanvas != null) cvCanvas.sortingOrder = prevOrder;
        }

        /// <summary>
        /// Cả 2 lá dash đồng thời, gặp nhau ở midpoint giữa 2 slot.
        /// Mỗi lá di chuyển 50% về phía lá kia.
        /// Gọi TRƯỚC khi apply damage — coroutine kết thúc đúng lúc impact.
        /// </summary>
        public IEnumerator AnimateDashBoth(CardView a, CardView b)
        {
            if (a == null || b == null) yield break;

            var aParent = a.transform.parent as RectTransform;
            var bParent = b.transform.parent as RectTransform;
            if (aParent == null || bParent == null) yield break;

            var aRect = a.GetComponent<RectTransform>();
            var bRect = b.GetComponent<RectTransform>();
            if (aRect == null || bRect == null) yield break;

            // Midpoint giữa 2 slot trong world space
            Vector3 midWorld = (aParent.position + bParent.position) * 0.5f;

            // Chuyển midpoint về canvas reference units của parent từng lá.
            // InverseTransformPoint tự xử lý canvas.scaleFactor → không cần chia thủ công.
            // Vì anchoredPosition = localPosition khi anchor=pivot=(0.5,0.5),
            // và card đang ở localPosition=(0,0) tức là slotCenter, nên offset = localPos(midpoint).
            Vector2 aOffset = aParent.InverseTransformPoint(midWorld);
            Vector2 bOffset = bParent.InverseTransformPoint(midWorld);

            // Bump sortingOrder để card dash hiện ra trên các card đứng yên
            var aCanvas = a.GetComponent<Canvas>();
            var bCanvas = b.GetComponent<Canvas>();
            int aOrder = aCanvas != null ? aCanvas.sortingOrder : 1;
            int bOrder = bCanvas != null ? bCanvas.sortingOrder : 1;
            if (aCanvas != null) aCanvas.sortingOrder = 20;
            if (bCanvas != null) bCanvas.sortingOrder = 20;

            float elapsed = 0f;
            while (elapsed < dashDuration)
            {
                elapsed += Time.deltaTime;
                float t = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(elapsed / dashDuration));
                if (aRect != null) aRect.anchoredPosition = Vector2.Lerp(Vector2.zero, aOffset, t);
                if (bRect != null) bRect.anchoredPosition = Vector2.Lerp(Vector2.zero, bOffset, t);
                yield return null;
            }
            if (aRect != null) aRect.anchoredPosition = aOffset;
            if (bRect != null) bRect.anchoredPosition = bOffset;

            // Restore sortingOrder sau khi dash xong (lá vẫn đứng ở clash point nhưng không cần nổi trên)
            if (aCanvas != null) aCanvas.sortingOrder = aOrder;
            if (bCanvas != null) bCanvas.sortingOrder = bOrder;
        }

        /// <summary>
        /// Một lá dash về phía targetWorldPos.
        /// fraction: bao nhiêu % khoảng cách đến target (mặc định = 1.0 = toàn bộ đường).
        /// Dùng cho QuickAttack (attacker → blocker slot), blocker phản đòn (blocker → attacker slot).
        /// </summary>
        public IEnumerator AnimateDashOne(CardView cv, Vector3 targetWorldPos, float fraction = 1f)
        {
            if (cv == null) yield break;
            var rect = cv.GetComponent<RectTransform>();
            if (rect == null) yield break;
            var parentRect = cv.transform.parent as RectTransform;
            if (parentRect == null) yield break;

            // Chuyển target về canvas units của parent (slot) của card
            Vector2 fullOffset = parentRect.InverseTransformPoint(targetWorldPos);
            Vector2 dashTarget = fullOffset * fraction;

            var cvCanvas = cv.GetComponent<Canvas>();
            int prevOrder = cvCanvas != null ? cvCanvas.sortingOrder : 1;
            if (cvCanvas != null) cvCanvas.sortingOrder = 20;

            yield return MoveAnchored(rect, Vector2.zero, dashTarget, dashDuration);

            if (cvCanvas != null) cvCanvas.sortingOrder = prevOrder;
        }

        /// <summary>
        /// Card dash thẳng vào khu battlefield trống của địch — đúng slot tương ứng với attacker.
        ///
        /// Cách tính target:
        ///   1. Lấy slotIndex của attacker (card.slotIndex).
        ///   2. Lấy world position của slot đó trên enemy battlefield zone.
        ///   3. Chuyển về canvas units của parent card (InverseTransformPoint) → dashTarget.
        ///
        /// Khác với cách cũ (nexusDashOffset cố định):
        ///   nexusDashOffset chỉ đi được nửa sân vì là offset tùy chỉnh.
        ///   Dùng world position của enemy zone → card lao đúng vào ô tương ứng bên địch,
        ///   giống behavior của game gốc LoR.
        ///
        /// Fallback về nexusDashOffset nếu playerBattlefield/enemyBattlefield chưa gán
        /// hoặc slots list rỗng.
        /// </summary>
        public IEnumerator AnimateDashToNexus(CardView cv, bool atkIsPlayer, CardModel attacker = null)
        {
            if (cv == null) yield break;
            var rect = cv.GetComponent<RectTransform>();
            if (rect == null) yield break;
            var parentRect = cv.transform.parent as RectTransform;
            if (parentRect == null) yield break;

            var ownZone = atkIsPlayer ? playerBattlefield : enemyBattlefield;
            var targetZone = atkIsPlayer ? enemyBattlefield : playerBattlefield;

            Vector2 dashTarget;

            if (targetZone != null && ownZone != null
                && targetZone.slots != null && targetZone.slots.Count > 0
                && attacker != null)
            {
                // Slot của attacker (đã compact từ trái), clamp cho an toàn
                int slot = Mathf.Clamp(attacker.slotIndex, 0, targetZone.slots.Count - 1);

                // World position của slot tương ứng trên sân địch
                Vector3 targetWorldPos = targetZone.slots[slot].position;

                // Chuyển về canvas units của parent (slot transform bên nhà)
                // InverseTransformPoint tự handle canvas.scaleFactor
                dashTarget = parentRect.InverseTransformPoint(targetWorldPos);
            }
            else
            {
                // Fallback: offset cố định (khi zones chưa gán trong Inspector)
                float sign = atkIsPlayer ? 1f : -1f;
                dashTarget = (Vector2)(nexusDashOffset * sign);
            }

            var cvCanvas = cv.GetComponent<Canvas>();
            int prevOrder = cvCanvas != null ? cvCanvas.sortingOrder : 1;
            if (cvCanvas != null) cvCanvas.sortingOrder = 20;

            yield return MoveAnchored(rect, Vector2.zero, dashTarget, dashDuration);

            if (cvCanvas != null) cvCanvas.sortingOrder = prevOrder;
        }

        /// <summary>Card trở về battleslot (anchoredPosition về (0,0)).</summary>
        public IEnumerator AnimateReturnToSlot(CardView cv)
        {
            if (cv == null) yield break;
            var rect = cv.GetComponent<RectTransform>();
            if (rect == null) yield break;

            Vector2 from = rect.anchoredPosition;
            yield return MoveAnchored(rect, from, Vector2.zero, returnDuration);
        }

        /// <summary>
        /// Cả 2 lá trở về slot của mình đồng thời.
        /// Null-safe: nếu 1 lá null thì chỉ animate lá còn lại.
        /// </summary>
        public IEnumerator AnimateReturnBoth(CardView a, CardView b)
        {
            if (a == null && b == null) yield break;
            if (a == null) { yield return AnimateReturnToSlot(b); yield break; }
            if (b == null) { yield return AnimateReturnToSlot(a); yield break; }

            var aRect = a.GetComponent<RectTransform>();
            var bRect = b.GetComponent<RectTransform>();
            if (aRect == null && bRect == null) yield break;
            if (aRect == null) { yield return AnimateReturnToSlot(b); yield break; }
            if (bRect == null) { yield return AnimateReturnToSlot(a); yield break; }

            Vector2 aFrom = aRect.anchoredPosition;
            Vector2 bFrom = bRect.anchoredPosition;

            float elapsed = 0f;
            while (elapsed < returnDuration)
            {
                elapsed += Time.deltaTime;
                float t = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(elapsed / returnDuration));
                if (aRect != null) aRect.anchoredPosition = Vector2.Lerp(aFrom, Vector2.zero, t);
                if (bRect != null) bRect.anchoredPosition = Vector2.Lerp(bFrom, Vector2.zero, t);
                yield return null;
            }
            if (aRect != null) aRect.anchoredPosition = Vector2.zero;
            if (bRect != null) bRect.anchoredPosition = Vector2.zero;
        }

        /// <summary>
        /// Nexus shake nhẹ khi bị đánh.
        /// Dùng StartCoroutine(AnimateNexusPulse(...)) — fire &amp; forget.
        /// </summary>
        public IEnumerator AnimateNexusPulse(bool playerNexus)
        {
            var t = playerNexus ? playerNexusTransform : enemyNexusTransform;
            if (t == null) yield break;

            var rt = t.GetComponent<RectTransform>();
            if (rt != null)
            {
                Vector2 origin = rt.anchoredPosition;
                float elapsed = 0f;
                float mag = 8f;
                while (elapsed < nexusPulseDuration)
                {
                    elapsed += Time.deltaTime;
                    float shake = Mathf.Sin(elapsed * Mathf.PI * 14f)
                                  * mag * (1f - elapsed / nexusPulseDuration);
                    rt.anchoredPosition = origin + new Vector2(shake, 0f);
                    yield return null;
                }
                rt.anchoredPosition = origin;
            }
            else
            {
                Vector3 origin = t.localPosition;
                float elapsed = 0f;
                float mag = 8f;
                while (elapsed < nexusPulseDuration)
                {
                    elapsed += Time.deltaTime;
                    float shake = Mathf.Sin(elapsed * Mathf.PI * 14f)
                                  * mag * (1f - elapsed / nexusPulseDuration);
                    t.localPosition = origin + new Vector3(shake, 0f, 0f);
                    yield return null;
                }
                t.localPosition = origin;
            }
        }

        /// <summary>
        /// Card chết: fade alpha về 0 và thu nhỏ nhẹ.
        /// Gọi StartCoroutine(AnimateDie(cv)) — fire &amp; forget.
        /// Sau đó yield WaitForSeconds(dieFadeDuration), rồi ProcessDeaths() Destroy gameObject.
        /// </summary>
        public IEnumerator AnimateDie(CardView cv)
        {
            if (cv == null) yield break;

            var cg = cv.GetComponent<CanvasGroup>();
            if (cg == null) cg = cv.gameObject.AddComponent<CanvasGroup>();

            var rect = cv.GetComponent<RectTransform>();
            Vector3 startScale = cv.transform.localScale;
            float elapsed = 0f;

            while (elapsed < dieFadeDuration && cv != null)
            {
                elapsed += Time.deltaTime;
                float t = Mathf.Clamp01(elapsed / dieFadeDuration);
                cg.alpha = 1f - t;
                if (rect != null) cv.transform.localScale = Vector3.Lerp(startScale, startScale * 0.78f, t);
                yield return null;
            }
            // ProcessDeaths() sẽ Destroy — không reset lại
        }


        // ─────────────────────────────────────────────────────────────────────
        // Section 2 B4-B6: Card fly từ vị trí tay → bench slot
        // ─────────────────────────────────────────────────────────────────────

        /// <summary>
        /// Bible Section 2 B4-B6: bench CardView bắt đầu ở vị trí hand rồi bay về bench slot.
        ///
        /// Cách dùng trong GameController (QUAN TRỌNG — thứ tự):
        ///   1. Trước Notify(): handWorldPos = GetHandView(card, byPlayer)?.transform.position
        ///   2. Notify() — view rebuild: hand card destroy, bench card spawn ở slot (anchored = 0,0)
        ///   3. Trong SummonSequence: yield AnimateCardFromPosition(benchCV, handWorldPos)
        ///   4. Sau đó yield AnimateSummon(benchCV) — pop-in bounce bình thường
        ///
        /// cv = CardView vừa spawn trên bench (SAU Notify).
        /// startWorldPos = world position của card khi còn trên tay (TRƯỚC Notify).
        /// </summary>
        public IEnumerator AnimateCardFromPosition(CardView cv, Vector3 startWorldPos)
        {
            if (cv == null) yield break;
            var rect = cv.GetComponent<RectTransform>();
            if (rect == null) yield break;
            var parentRect = cv.transform.parent as RectTransform;
            if (parentRect == null) yield break;

            // Raise sortingOrder để card bay nổi lên trên tất cả
            var cvCanvas = cv.GetComponent<Canvas>();
            int prevOrder = cvCanvas != null ? cvCanvas.sortingOrder : 1;
            if (cvCanvas != null) cvCanvas.sortingOrder = 50;

            // Tính offset từ bench slot đến hand position trong canvas-space của slot parent
            // (parentRect là slot transform — anchoredPosition=0 nghĩa là đứng đúng tại slot)
            Vector2 handOffset = parentRect.InverseTransformPoint(startWorldPos);

            // Teleport card đến vị trí tay để bắt đầu animation
            rect.anchoredPosition = handOffset;

            // B4: Anticipation — phóng to nhẹ (card vừa "được chọn" từ tay)
            float elapsed = 0f;
            Vector3 startScale = cv.transform.localScale;
            Vector3 peakScale = startScale * handAnticipationScale;
            while (elapsed < handAnticipationDuration)
            {
                elapsed += Time.deltaTime;
                float t = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(elapsed / handAnticipationDuration));
                cv.transform.localScale = Vector3.Lerp(startScale, peakScale, t);
                yield return null;
            }
            cv.transform.localScale = peakScale;

            // B5: Lao từ hand position → bench slot (anchoredPosition = 0,0)
            elapsed = 0f;
            while (elapsed < handToBenchDuration)
            {
                elapsed += Time.deltaTime;
                // EaseIn-Out: ném mạnh, phanh nhẹ khi đến nơi
                float t = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(elapsed / handToBenchDuration));
                rect.anchoredPosition = Vector2.Lerp(handOffset, Vector2.zero, t);
                cv.transform.localScale = Vector3.Lerp(peakScale, Vector3.zero, t); // thu nhỏ về 0 để AnimateSummon pop-in tiếp theo
                yield return null;
            }
            rect.anchoredPosition = Vector2.zero;
            cv.transform.localScale = Vector3.zero; // AnimateSummon sẽ scale từ 0 lên

            if (cvCanvas != null) cvCanvas.sortingOrder = prevOrder;
        }

        // ─────────────────────────────────────────────────────────────────────
        // Camera Shake
        // ─────────────────────────────────────────────────────────────────────

        /// <summary>
        /// Rung camera nhẹ khi có impact (Bible Section 8: 1–3px với đòn thường).
        /// Fire-and-forget: StartCoroutine(AnimateCameraShake()).
        /// cameraTransform phải được gán trong Inspector — dùng Camera.main.transform
        /// hoặc một wrapper GameObject rỗng là parent của camera.
        /// </summary>
        public IEnumerator AnimateCameraShake(float magnitudeOverride = -1f)
        {
            if (cameraTransform == null) yield break;
            float mag = magnitudeOverride >= 0f ? magnitudeOverride : cameraShakeMagnitude;

            Vector3 origin = cameraTransform.localPosition;
            float elapsed = 0f;
            while (elapsed < cameraShakeDuration)
            {
                elapsed += Time.deltaTime;
                float decay = 1f - (elapsed / cameraShakeDuration);
                float x = UnityEngine.Random.Range(-1f, 1f) * mag * decay;
                float y = UnityEngine.Random.Range(-1f, 1f) * mag * decay;
                cameraTransform.localPosition = origin + new Vector3(x, y, 0f);
                yield return null;
            }
            cameraTransform.localPosition = origin;
        }

        // ─────────────────────────────────────────────────────────────────────
        // Hit Squash / Stretch
        // ─────────────────────────────────────────────────────────────────────

        /// <summary>
        /// Visual hit-stop: squash card theo X khi nhận impact, recover về 1.
        /// Fire-and-forget: StartCoroutine(AnimateHitSquash(cv)).
        /// Gọi trên card NHẬN đòn (blocker hoặc attacker bị phản đòn).
        /// </summary>
        public IEnumerator AnimateHitSquash(CardView cv)
        {
            if (cv == null) yield break;
            Vector3 origin = cv.transform.localScale;
            Vector3 squashed = new Vector3(
                origin.x * hitSquashX,
                origin.y * hitSquashY,
                origin.z);

            // Squash
            float elapsed = 0f;
            while (elapsed < hitSquashDuration)
            {
                elapsed += Time.deltaTime;
                float t = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(elapsed / hitSquashDuration));
                if (cv != null) cv.transform.localScale = Vector3.Lerp(origin, squashed, t);
                yield return null;
            }
            if (cv != null) cv.transform.localScale = squashed;

            // Recover
            elapsed = 0f;
            while (elapsed < hitRecoverDuration)
            {
                elapsed += Time.deltaTime;
                float t = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(elapsed / hitRecoverDuration));
                if (cv != null) cv.transform.localScale = Vector3.Lerp(squashed, origin, t);
                yield return null;
            }
            if (cv != null) cv.transform.localScale = origin;
        }

        // ─────────────────────────────────────────────────────────────────────
        // Support Buff Pulse
        // ─────────────────────────────────────────────────────────────────────

        /// <summary>
        /// Pulse alpha trên supportedHighlight khi unit vừa nhận Support buff.
        /// Fire-and-forget: StartCoroutine(AnimateSupportPulse(cv)).
        /// CardView.supportedHighlight phải được gán trong Inspector (Image màu vàng/cam nhạt).
        /// </summary>
        public IEnumerator AnimateSupportPulse(CardView cv, int pulseCount = 2)
        {
            if (cv == null || cv.supportedHighlight == null) yield break;

            var img = cv.supportedHighlight;
            img.gameObject.SetActive(true);
            Color baseColor = img.color;

            for (int i = 0; i < pulseCount; i++)
            {
                // Fade in
                float elapsed = 0f;
                float pulseDur = 0.12f;
                while (elapsed < pulseDur)
                {
                    elapsed += Time.deltaTime;
                    float t = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(elapsed / pulseDur));
                    if (img != null) img.color = new Color(baseColor.r, baseColor.g, baseColor.b, Mathf.Lerp(0f, 1f, t));
                    yield return null;
                }
                // Fade out
                elapsed = 0f;
                while (elapsed < pulseDur)
                {
                    elapsed += Time.deltaTime;
                    float t = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(elapsed / pulseDur));
                    if (img != null) img.color = new Color(baseColor.r, baseColor.g, baseColor.b, Mathf.Lerp(1f, 0f, t));
                    yield return null;
                }
            }
            // Giữ highlight nếu support còn active (GameController/CardView tự ẩn sau)
        }


        // ─────────────────────────────────────────────────────────────────────
        // Attack Lean — unit nghiêng về phía địch khi declare attack
        // ─────────────────────────────────────────────────────────────────────

        /// <summary>
        /// Khi unit declare attack: lean nhẹ về phía địch, hold, rồi thu về.
        /// Tạo cảm giác unit "đứng tấn" / chuẩn bị tấn công rõ ràng.
        ///
        /// atkIsPlayer = true → lean lên trên (về phía enemy zone ở trên).
        /// atkIsPlayer = false → lean xuống dưới.
        ///
        /// Fire-and-forget: StartCoroutine(AnimateAttackLean(cv, atkIsPlayer)).
        /// </summary>
        public IEnumerator AnimateAttackLean(CardView cv, bool atkIsPlayer)
        {
            if (cv == null) yield break;
            var rect = cv.GetComponent<RectTransform>();
            if (rect == null) yield break;

            float direction = atkIsPlayer ? 1f : -1f;
            Vector2 leanTarget = new Vector2(0f, direction * attackLeanOffset);
            Vector2 origin = rect.anchoredPosition; // thường là (0,0) khi đứng tại slot

            // Lean forward
            float elapsed = 0f;
            while (elapsed < attackLeanDuration)
            {
                elapsed += Time.deltaTime;
                float t = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(elapsed / attackLeanDuration));
                if (rect != null) rect.anchoredPosition = Vector2.Lerp(origin, leanTarget, t);
                yield return null;
            }
            if (rect != null) rect.anchoredPosition = leanTarget;

            // Hold
            yield return new WaitForSeconds(attackLeanHoldDuration);

            // Return
            elapsed = 0f;
            while (elapsed < attackLeanReturnDuration)
            {
                elapsed += Time.deltaTime;
                float t = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(elapsed / attackLeanReturnDuration));
                if (rect != null) rect.anchoredPosition = Vector2.Lerp(leanTarget, origin, t);
                yield return null;
            }
            if (rect != null) rect.anchoredPosition = origin;
        }

        // ─────────────────────────────────────────────────────────────────────
        // Floating Damage Numbers
        // ─────────────────────────────────────────────────────────────────────

        /// <summary>
        /// Popup số damage/heal nổi lên và fade out tại vị trí card.
        /// Không cần prefab — tự tạo TextMeshProUGUI dynamically.
        ///
        /// amount > 0 = damage (đỏ). amount < 0 = heal (xanh, hiện dấu +).
        /// Fire-and-forget: StartCoroutine(AnimateDamageNumber(cv, damage)).
        /// </summary>
        public IEnumerator AnimateDamageNumber(CardView cv, int amount)
        {
            if (cv == null || amount == 0) yield break;

            // Tìm root canvas nếu chưa gán
            Canvas canvas = rootCanvas;
            if (canvas == null) canvas = Object.FindAnyObjectByType<Canvas>();
            if (canvas == null) yield break;

            // Tạo TextMeshProUGUI động
            var go = new GameObject("DmgNum", typeof(RectTransform));
            go.transform.SetParent(canvas.transform, false);
            go.transform.SetAsLastSibling(); // nổi lên trên tất cả

            TMPro.TextMeshProUGUI tmp = go.AddComponent<TMPro.TextMeshProUGUI>();
            tmp.text = amount > 0 ? $"-{amount}" : $"+{Mathf.Abs(amount)}";
            tmp.fontSize = damageNumberFontSize;
            tmp.color = amount > 0 ? damageNumberColor : healNumberColor;
            tmp.alignment = TMPro.TextAlignmentOptions.Center;
            tmp.fontStyle = TMPro.FontStyles.Bold;
            tmp.raycastTarget = false;

            var rect = go.GetComponent<RectTransform>();
            rect.sizeDelta = new Vector2(120f, 60f);

            // Đặt vị trí tại card trong canvas space
            Vector2 screenPos = RectTransformUtility.WorldToScreenPoint(null, cv.transform.position);
            RectTransformUtility.ScreenPointToLocalPointInRectangle(
                canvas.GetComponent<RectTransform>(), screenPos, canvas.worldCamera, out Vector2 localPos);
            rect.anchoredPosition = localPos + new Vector2(0f, 20f); // offset lên 1 chút

            Vector2 startPos = rect.anchoredPosition;
            Vector2 endPos = startPos + new Vector2(0f, damageNumberFloatDistance);

            float elapsed = 0f;
            while (elapsed < damageNumberDuration && go != null)
            {
                elapsed += Time.deltaTime;
                float t = Mathf.Clamp01(elapsed / damageNumberDuration);
                // Float lên với ease-out
                float floatT = 1f - (1f - t) * (1f - t);
                rect.anchoredPosition = Vector2.Lerp(startPos, endPos, floatT);
                // Fade out ở nửa sau
                float alpha = t < 0.4f ? 1f : Mathf.Lerp(1f, 0f, (t - 0.4f) / 0.6f);
                // Scale to nhỏ lại khi fade
                float scale = t < 0.1f ? Mathf.Lerp(0.5f, 1.2f, t / 0.1f) : Mathf.Lerp(1.2f, 0.9f, (t - 0.1f) / 0.9f);
                go.transform.localScale = Vector3.one * scale;
                if (tmp != null) tmp.color = new Color(tmp.color.r, tmp.color.g, tmp.color.b, alpha);
                yield return null;
            }
            if (go != null) Destroy(go);
        }

        // ─────────────────────────────────────────────────────────────────────
        // HP Text Flash
        // ─────────────────────────────────────────────────────────────────────

        /// <summary>
        /// Flash màu healthText của card khi nhận damage — immediate visual feedback.
        /// Fire-and-forget: StartCoroutine(AnimateHpFlash(cv)).
        /// </summary>
        public IEnumerator AnimateHpFlash(CardView cv)
        {
            if (cv == null || cv.healthText == null) yield break;

            var txt = cv.healthText;
            Color originalColor = txt.color;
            float elapsed = 0f;

            // Flash in
            float flashIn = hpFlashDuration * 0.3f;
            while (elapsed < flashIn)
            {
                elapsed += Time.deltaTime;
                float t = Mathf.Clamp01(elapsed / flashIn);
                if (txt != null) txt.color = Color.Lerp(originalColor, hpFlashColor, t);
                yield return null;
            }
            if (txt != null) txt.color = hpFlashColor;

            // Hold
            yield return new WaitForSeconds(hpFlashDuration * 0.1f);

            // Flash out
            elapsed = 0f;
            float flashOut = hpFlashDuration * 0.6f;
            while (elapsed < flashOut)
            {
                elapsed += Time.deltaTime;
                float t = Mathf.Clamp01(elapsed / flashOut);
                if (txt != null) txt.color = Color.Lerp(hpFlashColor, originalColor, t);
                yield return null;
            }
            if (txt != null) txt.color = originalColor;
        }

        // ─────────────────────────────────────────────────────────────────────
        // Consolidated FX bundles — 1 chỗ điều khiển cho GameController gọi
        // ─────────────────────────────────────────────────────────────────────

        /// <summary>
        /// Gộp toàn bộ hiệu ứng khi 1 unit NHẬN đòn: hit squash + damage number + HP flash + camera shake.
        /// Fire-and-forget — thay cho 4 lời gọi StartCoroutine rải rác trong GameController.
        ///
        /// view: card nhận đòn (null-safe → bỏ qua). dmg: sát thương hiển thị (0 = không hiện số).
        /// shake: có rung camera không (đặt false khi muốn gộp shake 1 lần cho nhiều đòn đồng thời).
        /// </summary>
        public void PlayHitFx(CardView view, int dmg, bool shake = true)
        {
            if (view != null)
            {
                StartCoroutine(AnimateHitSquash(view));
                if (dmg != 0) StartCoroutine(AnimateDamageNumber(view, dmg));
                StartCoroutine(AnimateHpFlash(view));
            }
            if (shake) StartCoroutine(AnimateCameraShake());
        }

        /// <summary>Rung camera 1 lần (fire-and-forget). Dùng khi gộp shake cho combat đồng thời.</summary>
        public void PlayShake() => StartCoroutine(AnimateCameraShake());

        /// <summary>
        /// Fade chết cho 1 card (fire-and-forget). Trả về true nếu có animate.
        /// Caller yield WaitForSeconds(dieFadeDuration) sau đó rồi ProcessDeaths().
        /// </summary>
        public bool PlayDeathFx(CardView view)
        {
            if (view == null) return false;
            StartCoroutine(AnimateDie(view));
            return true;
        }

        // ─────────────────────────────────────────────────────────────────────
        // HUD Animation — Mana sinh động + Attack Token trượt
        // (Implementation gom về ĐÂY; HUDView chỉ gọi combatAnimator.PlayXxx(...).)
        // ─────────────────────────────────────────────────────────────────────
        [Header("HUD — Mana Animation")]
        [Tooltip("Scale đỉnh của gem khi VỪA ĐƯỢC HỒI mana (bung to rồi thu về).")]
        public float manaGainPopScale = 1.6f;
        [Tooltip("Scale đỉnh của gem khi VỪA TIÊU mana (co lại rồi về).")]
        public float manaSpendPopScale = 0.55f;
        [Tooltip("Thời gian mỗi lần pulse gem/chữ mana (giây).")]
        public float manaPulseDuration = 0.28f;

        [Header("HUD — Attack Token Animation")]
        [Tooltip("Thời gian token TRƯỢT từ bên này sang bên kia khi qua round (giây).")]
        public float tokenSlideDuration = 0.45f;
        [Tooltip("Độ phình nhẹ của token khi đang trượt (0 = không phình).")]
        public float tokenSlideBulge = 0.25f;

        // Cache base scale của mỗi Transform để pulse không phá scale gốc.
        readonly Dictionary<Transform, Vector3> _hudBaseScale = new Dictionary<Transform, Vector3>();

        /// <summary>
        /// Pulse các gem mana vừa ĐỔI trạng thái giữa prev→current.
        /// Gem vừa đầy (hồi mana) → bung to; gem vừa rỗng (tiêu mana) → co lại.
        /// prev &lt; 0 = lần đầu (bỏ qua). Gọi từ HUDView.Render.
        /// </summary>
        public void PlayManaChange(Image[] icons, int prev, int current)
        {
            if (icons == null || prev < 0 || prev == current) return;
            bool gained = current > prev;
            float peak = gained ? manaGainPopScale : manaSpendPopScale;
            for (int i = 0; i < icons.Length; i++)
            {
                if (icons[i] == null) continue;
                bool before = i < prev;
                bool now = i < current;
                if (before != now) StartCoroutine(PulseTransform(icons[i].transform, peak, manaPulseDuration));
            }
        }

        /// <summary>Pulse 1 Transform (VD: chữ mana) — bung nhẹ rồi về. Fire-and-forget.</summary>
        public void PlayPulse(Transform t) => StartCoroutine(PulseTransform(t, manaGainPopScale, manaPulseDuration));

        /// <summary>Token trượt từ vị trí <paramref name="from"/> về <paramref name="to"/> (anchoredPosition), có phình nhẹ.</summary>
        public void PlayTokenSlide(RectTransform rt, Vector2 from, Vector2 to)
        {
            if (rt != null) StartCoroutine(SlideRect(rt, from, to));
        }

        IEnumerator PulseTransform(Transform t, float peak, float dur)
        {
            if (t == null) yield break;
            if (!_hudBaseScale.TryGetValue(t, out var baseScale))
            {
                baseScale = t.localScale;
                if (baseScale == Vector3.zero) baseScale = Vector3.one;
                _hudBaseScale[t] = baseScale;
            }
            Vector3 peakScale = baseScale * peak;

            float up = Mathf.Max(0.01f, dur * 0.4f);
            float down = Mathf.Max(0.01f, dur * 0.6f);
            float e = 0f;
            Vector3 start = t.localScale;
            while (e < up && t != null)
            {
                e += Time.deltaTime;
                float k = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(e / up));
                t.localScale = Vector3.Lerp(start, peakScale, k);
                yield return null;
            }
            if (t == null) yield break;
            e = 0f;
            while (e < down && t != null)
            {
                e += Time.deltaTime;
                float k = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(e / down));
                t.localScale = Vector3.Lerp(peakScale, baseScale, k);
                yield return null;
            }
            if (t != null) t.localScale = baseScale;
        }

        IEnumerator SlideRect(RectTransform rt, Vector2 from, Vector2 to)
        {
            if (rt == null) yield break;
            Vector3 baseScale = rt.localScale;
            if (baseScale == Vector3.zero) baseScale = Vector3.one;
            rt.anchoredPosition = from;

            float dur = Mathf.Max(0.01f, tokenSlideDuration);
            float e = 0f;
            while (e < dur && rt != null)
            {
                e += Time.deltaTime;
                float t = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(e / dur));
                rt.anchoredPosition = Vector2.Lerp(from, to, t);
                float bulge = 1f + tokenSlideBulge * Mathf.Sin(t * Mathf.PI);
                rt.localScale = baseScale * bulge;
                yield return null;
            }
            if (rt != null)
            {
                rt.anchoredPosition = to;
                rt.localScale = baseScale;
            }
        }

        // ─────────────────────────────────────────────────────────────────────
        // Spell Cast VFX — bolt bay từ phía caster tới (các) mục tiêu rồi nổ
        // ─────────────────────────────────────────────────────────────────────
        /// <summary>
        /// Bắn bolt phép tới mọi mục tiêu (đồng thời) rồi nổ (squash + HP flash + shake).
        /// Gọi TRƯỚC khi effect apply: xem GameController.ResolveSpellsSequentially().
        /// </summary>
        public IEnumerator AnimateSpellProjectiles(bool casterIsPlayer, List<CardModel> targets)
        {
            if (targets == null || targets.Count == 0) yield break;

            Vector3 src = new Vector3(Screen.width * 0.5f,
                casterIsPlayer ? Screen.height * 0.06f : Screen.height * 0.94f, 0f);

            bool any = false;
            foreach (var t in targets)
            {
                if (t == null) continue;
                var cv = GetView(t, t.belongsToPlayer);
                if (cv == null) continue;
                StartCoroutine(OneSpellBolt(src, cv));
                any = true;
            }
            if (any) yield return new WaitForSeconds(spellBoltDuration + 0.05f);
        }

        IEnumerator OneSpellBolt(Vector3 src, CardView target)
        {
            if (target == null) yield break;
            Canvas canvas = rootCanvas != null ? rootCanvas : Object.FindAnyObjectByType<Canvas>();
            if (canvas == null) yield break;

            var go = new GameObject("SpellBolt", typeof(RectTransform), typeof(Image));
            go.transform.SetParent(canvas.transform, false);
            go.transform.SetAsLastSibling();
            var img = go.GetComponent<Image>();
            img.sprite = UISprites.Circle();
            img.color = spellBoltColor;
            img.raycastTarget = false;
            var rt = go.GetComponent<RectTransform>();
            rt.sizeDelta = new Vector2(spellBoltSize, spellBoltSize);

            float e = 0f, dur = Mathf.Max(0.05f, spellBoltDuration);
            while (e < dur && target != null)
            {
                e += Time.deltaTime;
                float t = e / dur;
                go.transform.position = Vector3.Lerp(src, target.transform.position, t * t); // ease-in
                float s = 1f + 0.4f * Mathf.Sin(t * Mathf.PI);
                go.transform.localScale = Vector3.one * s;
                yield return null;
            }
            Destroy(go);

            if (target != null)
            {
                StartCoroutine(AnimateHitSquash(target));
                StartCoroutine(AnimateHpFlash(target));
                if (cameraTransform != null) StartCoroutine(AnimateCameraShake());
            }
        }

        // ── Private helpers ───────────────────────────────────────

        IEnumerator MoveAnchored(RectTransform rect, Vector2 from, Vector2 to, float duration)
        {
            float elapsed = 0f;
            while (elapsed < duration)
            {
                elapsed += Time.deltaTime;
                float t = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(elapsed / duration));
                if (rect != null) rect.anchoredPosition = Vector2.Lerp(from, to, t);
                yield return null;
            }
            if (rect != null) rect.anchoredPosition = to;
        }
    }
}