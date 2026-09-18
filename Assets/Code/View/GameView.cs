using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using LoRClone.Model;
using LoRClone.Data;
using LoRClone.Controller;

namespace LoRClone.View
{
    public class GameView : MonoBehaviour
    {
        [Header("Player zones")]
        public HandZoneView playerHand;
        public BenchZoneView playerBench;
        public BattlefieldZoneView playerBattlefield;

        [Header("Enemy zones")]
        public HandZoneView enemyHand;
        public BenchZoneView enemyBench;
        public BattlefieldZoneView enemyBattlefield;

        [Header("Shared zones")]
        public SpellZoneView spellZone;

        [Header("HUD")]
        public HUDView hudView;

        [Header("Targeting")]
        public TargetingArrow targetingArrow;

        // ── Locked arrow state ────────────────────────────────────
        readonly Dictionary<CardModel, List<CardModel>> _lockedArrowsBySpell = new();

        System.Action<GameModel> _onStateChangedHandler;

        // ── Init ──────────────────────────────────────────────────
        void Start() => StartCoroutine(InitNextFrame());

        IEnumerator InitNextFrame()
        {
            yield return null;

            var gc = GameController.Instance;
            while (gc == null) { yield return null; gc = GameController.Instance; }
            while (gc.model == null) yield return null;   // PvP: doi StartNetworkGame tao model

            if (!playerHand || !playerBench || !playerBattlefield ||
                !enemyHand || !enemyBench || !enemyBattlefield ||
                !spellZone || !hudView)
            { Debug.LogError("[GameView] Mot hoac nhieu zone chua gan!"); yield break; }

            if (targetingArrow == null)
                targetingArrow = TargetingArrow.Instance;

            if (targetingArrow == null)
                Debug.LogWarning("[GameView] TargetingArrow.Instance null — arrow se khong hien.");

            // CANONICAL PvP: client (NetLocalIsPlayer=false) LẬT view — phe mình (model.enemy) xuống dưới.
            // PvE / host: NetLocalIsPlayer=true → như cũ (model.player ở dưới).
            var meSide = GameController.NetLocalIsPlayer ? gc.model.player : gc.model.enemy;
            var oppSide = GameController.NetLocalIsPlayer ? gc.model.enemy : gc.model.player;

            playerHand.Init(meSide);
            playerBench.Init(meSide);
            playerBattlefield.Init(meSide);

            enemyHand.Init(oppSide);
            enemyBench.Init(oppSide);
            enemyBattlefield.Init(oppSide);

            spellZone.Init(meSide, oppSide, gc.model.spellStack);
            hudView.Init();

            _onStateChangedHandler = m =>
            {
                hudView.Render(m);
                RefreshAllLockedArrows();
            };
            gc.OnStateChanged += _onStateChangedHandler;
            gc.OnActionRejected += msg => Debug.LogWarning($"[GameView] Rejected: {msg}");

            // Hiện locked arrow cho enemy spells (player spells đã được track qua targeting flow)
            gc.OnSpellCommittedWithTargets += (spell, targets, byPlayer) =>
            {
                // Chỉ thêm nếu chưa có (tránh overwrite arrow player đã set thủ công)
                if (!_lockedArrowsBySpell.ContainsKey(spell))
                {
                    _lockedArrowsBySpell[spell] = new System.Collections.Generic.List<CardModel>(targets);
                    RefreshAllLockedArrows();
                }
            };

            gc.OnUnitSkillNeedsTarget += (unit, skill, isPlayer, cb) =>
            {
                // FIX: EnemyAI cung subscribe event nay va tu chon target khi isPlayer=false.
                // Neu GameView khong loc isPlayer, ca 2 handler cung chay -> EnemyAI da goi
                // callback xong nhung GameView van mo targeting arrow that, de du 1 mui ten
                // tren chuot cho nguoi choi tu chon ho enemy.
                if (isPlayer != GameController.NetLocalIsPlayer || GameController.Instance.playerAutoPlay) return; // chặn skill đối thủ + nhường AI khi AUTO
                StartCoroutine(StartUnitSkillTargeting(unit, skill, isPlayer, cb));
            };

            gc.OnEquipmentNeedsTarget += (equipment, isPlayer, cb) =>
                StartCoroutine(StartEquipmentTargeting(equipment, isPlayer, cb));

            // isPlayer suy từ CHỦ lá (belongsToPlayer) — canonical, đúng trên cả 2 máy dù view đã lật.
            System.Func<CardView, bool> owner = cv => cv.model != null && cv.model.belongsToPlayer;

            playerHand.OnCardDragIntent += (_, cv, i) => HandleDragIntent(cv, i, owner(cv));
            playerBench.OnCardDragIntent += (_, cv, i) => HandleDragIntent(cv, i, owner(cv));
            playerBattlefield.OnCardDragIntent += (_, cv, i) => HandleDragIntent(cv, i, owner(cv));

            enemyHand.OnCardDragIntent += (_, cv, i) => HandleDragIntent(cv, i, owner(cv));
            enemyBench.OnCardDragIntent += (_, cv, i) => HandleDragIntent(cv, i, owner(cv));
            enemyBattlefield.OnCardDragIntent += (_, cv, i) => HandleDragIntent(cv, i, owner(cv));

            spellZone.OnCardDragIntent += (_, cv, i) => HandleDragIntent(cv, i, owner(cv));

            hudView.Render(gc.model);
        }

        // ── Drag Intent Router ────────────────────────────────────
        void HandleDragIntent(CardView cv, DragIntent intent, bool isPlayer)
        {
            var card = cv.model;
            if (card == null) { cv.ReturnToPlace(); return; }

            // Sweep-gather: nếu đây là lá nguồn đang giữ bộ lá đã gom → xử lý CẢ BỘ,
            // bỏ qua flow declare đơn lẻ bên dưới.
            if (cv == _sweepSource && _sweepStack.Count > 0)
            {
                HandleSweepRelease(cv, intent);
                return;
            }

            if (intent == DragIntent.None)
            {
                bool isBlockerOnField = card.location == CardLocation.OnBattlefield
                                     && card.state == CardState.Blocking;
                if (!isBlockerOnField) { cv.ReturnToPlace(); return; }
            }

            var locationBefore = card.location;

            switch (card.location)
            {
                case CardLocation.InHand:
                    if (intent == DragIntent.Forward) HandlePlayFromHand(cv, isPlayer);
                    else cv.ReturnToPlace();
                    break;

                case CardLocation.OnBench:
                    if (intent == DragIntent.Forward) HandleBattlefieldDeclare(cv, isPlayer);
                    else cv.ReturnToPlace();
                    break;

                case CardLocation.OnBattlefield:
                    if (intent == DragIntent.Backward)
                        HandleUndeclare(cv, isPlayer);
                    else if (cv.model?.state == CardState.Blocking)
                        HandleReassignBlocker(cv, isPlayer);
                    else
                        cv.ReturnToPlace();
                    break;

                case CardLocation.OnStack:
                    if (intent == DragIntent.Backward) HandleUndoCastSpell(cv, isPlayer);
                    else cv.ReturnToPlace();
                    break;

                case CardLocation.StagingSpell:
                    if (intent == DragIntent.Backward) HandleUnstageSpell(cv, isPlayer);
                    else cv.ReturnToPlace();
                    break;

                default:
                    cv.ReturnToPlace();
                    break;
            }

            // FIX: Khong goi ReturnToPlace neu targeting vua bat dau.
            // Burst spell co target: card van o InHand sau StartTargetingForSpell,
            // neu khong check IsTargeting thi ReturnToPlace duoc goi va co the
            // rebuild zone, huy targeting context.
            if (card.location == locationBefore && cv != null
                && !(targetingArrow != null && targetingArrow.IsTargeting))
                cv.ReturnToPlace();
        }

        // ── Hand -> Bench / Spell Zone ─────────────────────────────
        void HandlePlayFromHand(CardView cv, bool isPlayer)
        {
            if (cv.model.location != CardLocation.InHand) return;

            switch (cv.model.data.cardType)
            {
                case CardType.Unit:
                    if (UnitNeedsWhenPlayedTarget(cv.model, isPlayer))
                        StartTargetingForUnit(cv, isPlayer);
                    else
                        GameController.Instance.SubmitLocal(new PlayUnitAction(isPlayer, cv.model));
                    break;

                case CardType.Spell:
                    if (cv.model.data.requiresTarget)
                        StartTargetingForSpell(cv, isPlayer);
                    else
                        CommitSpell(cv.model, isPlayer);
                    break;

                case CardType.Equipment:
                    GameController.Instance.SubmitLocal(new PlayEquipmentAction(cv.model, isPlayer));
                    break;
            }
        }

        // ── Targeting ─────────────────────────────────────────────

        // ── Unit WhenPlayed Targeting ──────────────────────────────

        // Ưu tiên targetType explicit trên CardAbility; fallback về RequiredTargetType của skill.
        // → Không cần set Inspector, skill tự báo nó cần target loại nào.
        TargetType GetAbilityTargetType(CardAbility ab)
        {
            if (ab.targetType != TargetType.None) return ab.targetType;
            return ab.effect != null ? ab.effect.RequiredTargetType : TargetType.None;
        }

        bool UnitNeedsWhenPlayedTarget(CardModel unit, bool isPlayer)
        {
            foreach (var ab in unit.data.abilities)
            {
                var tt = GetAbilityTargetType(ab);
                if (tt == TargetType.None) continue;
                var views = GetValidTargetViews(tt, isPlayer);
                views.RemoveAll(v => v != null && v.model == unit);
                if (views.Count > 0) return true;
            }
            return false;
        }

        void StartTargetingForUnit(CardView cv, bool isPlayer)
        {
            if (targetingArrow == null)
            {
                GameController.Instance.SubmitLocal(new PlayUnitAction(isPlayer, cv.model));
                return;
            }

            var unit = cv.model;
            TargetType targetType = TargetType.None;
            foreach (var ab in unit.data.abilities)
            {
                var tt = GetAbilityTargetType(ab);
                if (tt != TargetType.None) { targetType = tt; break; }
            }

            var validViews = GetValidTargetViews(targetType, isPlayer);
            validViews.RemoveAll(v => v != null && v.model == unit); // loại chính nó

            if (validViews.Count == 0)
            {
                GameController.Instance.SubmitLocal(new PlayUnitAction(isPlayer, unit));
                return;
            }

            // ── Đưa unit card vào khu vực spell zone (như spell đang stage) ──
            // Chỉ di chuyển visual, KHÔNG thêm vào spell stack.
            // Giúp tay không collapse → spell cards trong tay vẫn clickable.
            var originalParent = cv.transform.parent;
            cv.transform.SetParent(spellZone.transform, worldPositionStays: true);
            cv.transform.position = spellZone.transform.position;
            cv.SetSpellCircleMode(true); // hiện như spell trong zone

            Vector2 src = targetingArrow.UIWorldToScreen(cv.transform.position);

            targetingArrow.StartTargeting(cv, src, validViews,
                target =>
                {
                    // Confirm: restore rồi play unit
                    cv.SetSpellCircleMode(false);
                    targetingArrow?.ClearPendingArrows();
                    GameController.Instance.SubmitLocalSpellTargets(unit, new System.Collections.Generic.List<CardModel> { target });
                    GameController.Instance.SubmitLocal(new PlayUnitAction(isPlayer, unit));
                    // cv sẽ bị destroy bởi HandZoneView khi card rời tay
                },
                () =>
                {
                    // Cancel: trả card về vị trí ban đầu trong tay
                    cv.SetSpellCircleMode(false);
                    cv.transform.SetParent(originalParent, worldPositionStays: true);
                    cv.ReturnToPlace();
                }
            );
        }

        System.Collections.IEnumerator StartUnitSkillTargeting(
            CardModel unit, CardModel skill, bool isPlayer,
            System.Action<List<CardModel>> onComplete)
        {
            yield return null; // đợi bench CV spawn
            if (targetingArrow == null) { onComplete(null); yield break; }
            var validViews = GetValidTargetViews(skill.data.GetTargetTypeForStep(0), isPlayer);
            if (validViews.Count == 0) { onComplete(null); yield break; }

            var bench = (isPlayer == GameController.NetLocalIsPlayer) ? playerBench : enemyBench; // ★ PvP: zone theo local
            var unitCV = bench.GetView(unit);

            // Đưa unit vào spellzone tạm thời (giống StartTargetingForUnit)
            // → arrow gốc từ spellzone; cho phép kéo về để hủy
            Transform originalParent = null;
            if (unitCV != null)
            {
                originalParent = unitCV.transform.parent;
                unitCV.transform.SetParent(spellZone.transform, worldPositionStays: true);
                unitCV.transform.position = spellZone.transform.position;
                unitCV.SetSpellCircleMode(true);
            }

            Vector2 src = unitCV != null
                ? targetingArrow.UIWorldToScreen(unitCV.transform.position)
                : new Vector2(Screen.width * 0.5f, Screen.height * 0.3f);

            bool done = false;
            targetingArrow.StartTargeting(unitCV, src, validViews,
                t =>
                {
                    targetingArrow?.ClearPendingArrows();
                    if (unitCV != null)
                    {
                        unitCV.SetSpellCircleMode(false);
                        if (originalParent != null)
                            unitCV.transform.SetParent(originalParent, worldPositionStays: true);
                    }
                    // Unit về lại bench slot
                    unitCV?.ReturnToPlace();
                    // Hiện locked arrow ngay (giống BeginTargetingNextFrame cho Fast spell)
                    _lockedArrowsBySpell[skill] = new List<CardModel> { t };
                    RefreshAllLockedArrows();
                    onComplete(new List<CardModel> { t });
                    done = true;
                },
                () =>
                {
                    if (unitCV != null)
                    {
                        unitCV.SetSpellCircleMode(false);
                        if (originalParent != null)
                            unitCV.transform.SetParent(originalParent, worldPositionStays: true);
                        unitCV.ReturnToPlace();
                    }
                    onComplete(null);
                    done = true;
                });
            while (!done) yield return null;
        }

        IEnumerator StartEquipmentTargeting(
            CardModel equipment, bool isPlayer,
            System.Action<CardModel> onComplete)
        {
            yield return null;
            if (targetingArrow == null) { onComplete(null); yield break; }

            var validViews = GetValidTargetViews(TargetType.AllyUnit, isPlayer);
            if (validViews.Count == 0) { onComplete(null); yield break; }

            var handZone = (isPlayer == GameController.NetLocalIsPlayer) ? playerHand : enemyHand; // ★ PvP: zone theo local
            CardView equipCV = null;
            foreach (var cv in handZone.AllViews())
                if (cv.model == equipment) { equipCV = cv; break; }

            Vector2 src = equipCV != null
                ? targetingArrow.UIWorldToScreen(equipCV.transform.position)
                : new Vector2(Screen.width * 0.5f, Screen.height * 0.35f);

            bool done = false;
            targetingArrow.StartTargeting(equipCV, src, validViews,
                target =>
                {
                    targetingArrow?.ClearPendingArrows();
                    onComplete(target);
                    done = true;
                },
                () =>
                {
                    onComplete(null);
                    done = true;
                }
            );
            while (!done) yield return null;
        }

        // ── SPELL CAST FLOW ──────────────────────────────────────────
        // 1. Player chọn Spell từ tay                      → GameView (nơi gọi hàm này)
        // 5-6-7. Lock chỗ + Move Hand → Spell Zone + animation đặt lá
        //        → StageSpellAction chạy TRƯỚC — card vào Spell Zone NGAY khi kéo ra (đúng ý UX:
        //          "lá lúc thao tác cần ở Spell Zone", không lửng lơ trên tay).
        // 2-3. Vào Target Selection, hiện valid targets + arrow đang chọn
        //      → arrow xuất phát TỪ VỊ TRÍ SPELL ZONE (spellZoneCV), không phải từ tay.
        // 4. Player confirm target                          → callback CollectTargets bên dưới
        // 8-9. Face-down / reveal cho đối thủ                → SpellZoneView.Refresh() tự xử lý theo belongsToPlayer
        // 10. Arrow PERSISTENT từ Spell Zone → target        → set ngay khi confirm (không cần đợi Pass/Confirm)
        // 11-14. Resolve/VFX/remove/priority tiếp theo       → GameController (không đổi)
        void StartTargetingForSpell(CardView cv, bool isPlayer)
        {
            if (targetingArrow == null)
            {
                Debug.LogWarning("[GameView] TargetingArrow chua gan — cast ngay khong can target.");
                CommitSpell(cv.model, isPlayer);
                return;
            }

            var spellModel = cv.model;

            // Early-out: neu step 0 khong co target nao thi khong bat dau targeting.
            var step0Views = GetValidTargetViews(spellModel.data.GetTargetTypeForStep(0), isPlayer);
            if (step0Views.Count == 0)
            {
                Debug.LogWarning($"[GameView] Khong co target hop le cho step 0 " +
                                 $"(type={spellModel.data.GetTargetTypeForStep(0)}). Huy.");
                cv.ReturnToPlace();
                return;
            }

            bool isBurst = spellModel.data.spellSpeed == SpellSpeed.Burst;

            // ── Bước 5-6-7: Stage NGAY — lá rời tay, vào Spell Zone, animation đặt lá tự chạy
            //    qua SpellZoneView (subscribe OnSpellStaged).
            GameController.Instance.SubmitLocal(new StageSpellAction(isPlayer, spellModel));

            // Khong collapse tay khi spell can chon target tren tay (AllyInHand)
            if (isPlayer && !SpellNeedsHandTarget(spellModel.data))
                playerHand.CollapseForTargeting();

            // SubmitLocal khong xu ly dong bo tuc thi (can >=1 frame de model cap nhat location)
            // → toan bo phan con lai (check Stage thanh cong + target selection) doi 1 frame.
            StartCoroutine(AfterStageNextFrame(cv, spellModel, isPlayer, isBurst));
        }

        /// <summary>Chạy sau khi StageSpellAction đã có ≥1 frame để model xử lý xong.</summary>
        IEnumerator AfterStageNextFrame(CardView cv, CardModel spellModel, bool isPlayer, bool isBurst)
        {
            yield return null; // đợi model xử lý StageSpellAction + SpellZoneView spawn CardView xong

            if (spellModel.location != CardLocation.StagingSpell)
            {
                Debug.LogWarning("[GameView] Stage that bai.");
                cv.ReturnToPlace();
                yield break;
            }

            // ── Bước 2-3: BẮT ĐẦU chọn target — arrow xuất phát TỪ VỊ TRÍ SPELL ZONE.
            var spellZoneCV = spellZone.GetView(spellModel);
            Vector2 srcFallback = spellZoneCV != null
                ? targetingArrow.UIWorldToScreen(spellZoneCV.transform.position)
                : targetingArrow.UIWorldToScreen(cv.transform.position);

            CollectTargets(
                spellZoneCV, srcFallback,
                spellModel.data, isPlayer,
                new List<CardModel>(),
                targets =>
                {
                    // ── Bước 4: Player đã confirm target ─────────────
                    targetingArrow?.ClearPendingArrows();
                    GameController.Instance.SubmitLocalSpellTargets(spellModel, targets);

                    if (isBurst)
                    {
                        // Burst: khong response window — commit ngay.
                        GameController.Instance.SubmitLocal(new PassPriorityAction(isPlayer));
                    }
                    else
                    {
                        // ── Bước 10: khoá arrow PERSISTENT từ Spell Zone → target NGAY,
                        //    không cần đợi Pass/Confirm.
                        _lockedArrowsBySpell[spellModel] = new List<CardModel>(targets);
                        RefreshAllLockedArrows();
                    }
                },
                () => GameController.Instance.SubmitLocal(new UnstageSpellAction(isPlayer, spellModel))
            );
        }

        // [DEAD CODE - kept for reference]
        void CollectTargets(
            CardView srcCV,
            Vector2 srcScreen,
            List<CardView> remaining,
            List<CardModel> collected,
            int totalNeeded,
            System.Action<List<CardModel>> onAllDone,
            System.Action onCancel)
        {
            CollectTargetsInternal(
                srcCV, srcScreen, remaining, collected,
                new List<CardView>(),
                totalNeeded, onAllDone, onCancel);
        }

        void CollectTargetsInternal(
            CardView srcCV,
            Vector2 srcScreen,
            List<CardView> remaining,
            List<CardModel> collected,
            List<CardView> confirmedCVs,
            int totalNeeded,
            System.Action<List<CardModel>> onAllDone,
            System.Action onCancel)
        {
            foreach (var cv in confirmedCVs) cv?.SetConfirmed(true);

            if (collected.Count >= totalNeeded)
            {
                foreach (var cv in confirmedCVs) cv?.SetConfirmed(false);
                onAllDone?.Invoke(collected);
                return;
            }

            if (remaining.Count == 0)
            {
                foreach (var cv in confirmedCVs) cv?.SetConfirmed(false);
                if (collected.Count > 0)
                {
                    Debug.Log($"[Targeting] Khong con target: dung {collected.Count}/{totalNeeded} da chon.");
                    onAllDone?.Invoke(collected);
                }
                else
                {
                    Debug.LogWarning("[Targeting] Khong co target nao kha dung. Huy.");
                    targetingArrow?.ClearPendingArrows();
                    onCancel?.Invoke();
                }
                return;
            }

            Debug.Log($"[Targeting] Chon target {collected.Count + 1}/{totalNeeded}...");

            targetingArrow.StartTargeting(
                srcCV, srcScreen, remaining,
                target =>
                {
                    var newCV = remaining.Find(v => v != null && v.model == target);
                    collected.Add(target);
                    remaining.RemoveAll(v => v != null && v.model == target);

                    if (newCV != null)
                    {
                        confirmedCVs.Add(newCV);
                        targetingArrow?.AddPendingArrow(srcCV, srcScreen, newCV);
                    }

                    CollectTargetsInternal(
                        srcCV, srcScreen, remaining, collected, confirmedCVs,
                        totalNeeded, onAllDone, onCancel);
                },
                () =>
                {
                    foreach (var cv in confirmedCVs) cv?.SetConfirmed(false);
                    onCancel?.Invoke();
                }
            );
        }

        // ── Per-step targeting overloads ──────────────────────────

        void CollectTargets(
            CardView srcCV,
            Vector2 srcScreen,
            CardData spellData,
            bool isPlayer,
            List<CardModel> collected,
            System.Action<List<CardModel>> onAllDone,
            System.Action onCancel)
        {
            int totalNeeded = Mathf.Max(1, spellData.targetCount);
            CollectTargetsInternal(
                srcCV, srcScreen, spellData, isPlayer,
                0, collected, new List<CardView>(),
                totalNeeded, onAllDone, onCancel);
        }

        void CollectTargetsInternal(
            CardView srcCV,
            Vector2 srcScreen,
            CardData spellData,
            bool isPlayer,
            int step,
            List<CardModel> collected,
            List<CardView> confirmedCVs,
            int totalNeeded,
            System.Action<List<CardModel>> onAllDone,
            System.Action onCancel)
        {
            foreach (var cv in confirmedCVs) cv?.SetConfirmed(true);

            if (collected.Count >= totalNeeded)
            {
                foreach (var cv in confirmedCVs) cv?.SetConfirmed(false);
                onAllDone?.Invoke(collected);
                return;
            }

            var stepViews = GetValidTargetViews(spellData.GetTargetTypeForStep(step), isPlayer);
            stepViews.RemoveAll(v => v != null && collected.Contains(v.model));

            if (stepViews.Count == 0)
            {
                foreach (var cv in confirmedCVs) cv?.SetConfirmed(false);
                if (collected.Count > 0)
                {
                    Debug.Log($"[Targeting] Khong con target: dung {collected.Count}/{totalNeeded} da chon.");
                    onAllDone?.Invoke(collected);
                }
                else
                {
                    Debug.LogWarning("[Targeting] Khong co target nao kha dung. Huy.");
                    targetingArrow?.ClearPendingArrows();
                    onCancel?.Invoke();
                }
                return;
            }

            Debug.Log($"[Targeting] Chon target {collected.Count + 1}/{totalNeeded}... " +
                      $"(step {step}, type={spellData.GetTargetTypeForStep(step)}, " +
                      $"candidates={stepViews.Count})");

            targetingArrow.StartTargeting(
                srcCV, srcScreen, stepViews,
                target =>
                {
                    var newCV = stepViews.Find(v => v != null && v.model == target);
                    collected.Add(target);

                    if (newCV != null)
                    {
                        confirmedCVs.Add(newCV);
                        targetingArrow?.AddPendingArrow(srcCV, srcScreen, newCV);
                    }

                    var nextSrc = newCV ?? srcCV;
                    var nextScreen = newCV != null
                        ? targetingArrow.UIWorldToScreen(newCV.transform.position)
                        : srcScreen;

                    CollectTargetsInternal(
                        nextSrc, nextScreen, spellData, isPlayer,
                        step + 1, collected, confirmedCVs,
                        totalNeeded, onAllDone, onCancel);
                },
                () =>
                {
                    foreach (var cv in confirmedCVs) cv?.SetConfirmed(false);
                    onCancel?.Invoke();
                }
            );
        }

        // ── Locked arrows ─────────────────────────────────────────

        void RefreshAllLockedArrows()
        {
            if (targetingArrow == null) return;

            var toRemove = new List<CardModel>();
            foreach (var spell in _lockedArrowsBySpell.Keys)
                if (spell.location != CardLocation.StagingSpell && spell.location != CardLocation.OnStack)
                    toRemove.Add(spell);
            foreach (var s in toRemove) _lockedArrowsBySpell.Remove(s);

            targetingArrow.ClearAllLockedArrows();
            foreach (var (spell, targetModels) in _lockedArrowsBySpell)
            {
                var spellCV = spellZone.GetView(spell);
                if (spellCV == null) continue;
                var targetCVs = new List<CardView>();
                foreach (var m in targetModels)
                {
                    var tcv = FindCardView(m);
                    // FIX: bỏ qua target view null / đã destroy — SetLockedArrows vẽ tới
                    // transform chết sẽ ra tọa độ rác → vệt kẻ ngang loạn trên màn hình.
                    if (tcv != null) targetCVs.Add(tcv);
                }
                if (targetCVs.Count == 0) continue;
                targetingArrow.SetLockedArrows(spellCV, targetCVs);
            }
        }

        void ClearLockedArrowForSpell(CardModel spell)
        {
            _lockedArrowsBySpell.Remove(spell);
            RefreshAllLockedArrows();
        }

        CardView FindCardView(CardModel model)
        {
            if (model == null) return null;
            CardView cv;
            cv = playerBench.GetView(model); if (cv != null) return cv;
            cv = enemyBench.GetView(model); if (cv != null) return cv;
            cv = playerBattlefield.GetView(model); if (cv != null) return cv;
            cv = enemyBattlefield.GetView(model); if (cv != null) return cv;
            return null;
        }

        List<CardView> GetValidTargetViews(TargetType targetType, bool isPlayer)
        {
            var views = new List<CardView>();
            // ★ PvP FIX: playerBench/enemyBench la ZONE VIEW theo goc nhin local (da lat o client).
            //   isPlayer la phe CANONICAL -> quy doi: local = (isPlayer == NetLocalIsPlayer).
            //   Truoc day dung isPlayer tho -> o client tro nham zone -> khong chon duoc target / NRE.
            bool local = (isPlayer == GameController.NetLocalIsPlayer);
            var myBench = local ? playerBench : enemyBench;
            var theirBench = local ? enemyBench : playerBench;
            var myField = local ? playerBattlefield : enemyBattlefield;
            var theirField = local ? enemyBattlefield : playerBattlefield;

            switch (targetType)
            {
                case TargetType.EnemyUnit:
                    views.AddRange(theirBench.AllViews());
                    views.AddRange(theirField.AllViews());
                    break;
                case TargetType.AllyUnit:
                    views.AddRange(myBench.AllViews());
                    views.AddRange(myField.AllViews());
                    break;
                case TargetType.AnyUnit:
                    views.AddRange(myBench.AllViews());
                    views.AddRange(myField.AllViews());
                    views.AddRange(theirBench.AllViews());
                    views.AddRange(theirField.AllViews());
                    break;
                case TargetType.AllyInHand:
                    // Highlight tay cua owner — player click vao 1 la tren tay de buff
                    var myHand = local ? playerHand : enemyHand;
                    views.AddRange(myHand.AllViews());
                    break;
                default:
                    Debug.LogWarning($"[GameView] GetValidTargetViews: TargetType.{targetType} khong co handler.");
                    break;
            }
            return views;
        }

        // Kiem tra xem co buoc nao cua spell can chon target tren tay khong
        bool SpellNeedsHandTarget(CardData data)
        {
            int steps = Mathf.Max(1, data.targetCount);
            for (int i = 0; i < steps; i++)
                if (data.GetTargetTypeForStep(i) == TargetType.AllyInHand)
                    return true;
            return false;
        }

        // ── Commit non-targeted spell ─────────────────────────────
        void CommitSpell(CardModel spell, bool isPlayer)
        {
            if (spell.data.spellSpeed == SpellSpeed.Burst)
                GameController.Instance.SubmitLocal(new CastSpellAction(isPlayer, spell));
            else
                GameController.Instance.SubmitLocal(new StageSpellAction(isPlayer, spell));
        }

        // ── UI Camera helper ──────────────────────────────────────
        static Camera GetUICamera(Component uiComponent)
        {
            var c = uiComponent.GetComponentInParent<Canvas>();
            while (c != null && !c.isRootCanvas && c.transform.parent != null)
                c = c.transform.parent.GetComponentInParent<Canvas>();
            return (c != null && c.renderMode == RenderMode.ScreenSpaceCamera)
                   ? c.worldCamera : null;
        }

        // ── Bench -> Battlefield ───────────────────────────────────
        void HandleBattlefieldDeclare(CardView cv, bool isPlayer)
        {
            if (cv.model.location != CardLocation.OnBench) return;
            var m = GameController.Instance.model;

            // Chỉ Định: người chơi kéo quân địch xuống → nó chặn attacker Challenger.
            if (GameController.Instance.CanChallengerPull(cv.model))
            {
                GameController.Instance.HandleChallengerPull(cv.model);
                return;
            }

            if (isPlayer == m.playerHasAttackToken)
            {
                GameController.Instance.SubmitLocal(new DeclareAttackerAction(isPlayer, cv.model));
            }
            else
            {
                var attackerField = (m.playerHasAttackToken == GameController.NetLocalIsPlayer) ? playerBattlefield : enemyBattlefield;
                var defenderField = (isPlayer == GameController.NetLocalIsPlayer) ? playerBattlefield : enemyBattlefield;
                float dropX = Input.mousePosition.x;
                Camera uiCam = GetUICamera(cv);

                CardView bestAtkCV = null;
                CardModel bestAttacker = null;
                float bestXDist = float.MaxValue;
                foreach (var atkCV in attackerField.AllViews())
                {
                    if (atkCV.model.state != CardState.Attacking ||
                        atkCV.model.blockedBy != null) continue;
                    float sx = RectTransformUtility.WorldToScreenPoint(uiCam, atkCV.transform.position).x;
                    float d = Mathf.Abs(dropX - sx);
                    if (d < bestXDist) { bestXDist = d; bestAttacker = atkCV.model; bestAtkCV = atkCV; }
                }
                if (bestAttacker == null) { cv.ReturnToPlace(); return; }

                float atkX = RectTransformUtility.WorldToScreenPoint(uiCam, bestAtkCV.transform.position).x;
                int faceSlot = -1;
                float bestSD = float.MaxValue;
                for (int i = 0; i < defenderField.slots.Count; i++)
                {
                    float sx = RectTransformUtility.WorldToScreenPoint(uiCam, defenderField.slots[i].position).x;
                    float d = Mathf.Abs(atkX - sx);
                    if (d < bestSD) { bestSD = d; faceSlot = i; }
                }

                if (faceSlot >= 0) defenderField.PrepareBlockerSlot(faceSlot);
                GameController.Instance.SubmitLocal(new DeclareBlockerAction(isPlayer, cv.model, bestAttacker));
            }
        }

        // ── Reassign blocker ──────────────────────────────────────
        void HandleReassignBlocker(CardView cv, bool isPlayer)
        {
            var m = GameController.Instance.model;
            bool canReassign = m.phase == GamePhase.BlockDeclare
                || (m.phase == GamePhase.SpellStackResolve && m.combatPending);
            if (!canReassign) { cv.ReturnToPlace(); return; }

            var card = cv.model;
            var attackerField = (m.playerHasAttackToken == GameController.NetLocalIsPlayer) ? playerBattlefield : enemyBattlefield;
            var defenderField = (isPlayer == GameController.NetLocalIsPlayer) ? playerBattlefield : enemyBattlefield;
            Camera uiCam = GetUICamera(cv);

            float dropX = Input.mousePosition.x;
            int cardSlot = defenderField.GetSlotOf(card);
            float cardStartX = cardSlot >= 0
                ? RectTransformUtility.WorldToScreenPoint(uiCam, defenderField.slots[cardSlot].position).x
                : dropX;

            float deltaX = dropX - cardStartX;
            if (Mathf.Abs(deltaX) < 15f) { cv.ReturnToPlace(); return; }

            bool draggingRight = deltaX > 0f;

            var curAtkCV = attackerField.GetView(card.blockingTarget);
            float curAtkX = curAtkCV != null
                ? RectTransformUtility.WorldToScreenPoint(uiCam, curAtkCV.transform.position).x
                : cardStartX;

            CardView bestAtkCV = null;
            CardModel bestAttacker = null;
            float bestXDist = float.MaxValue;
            foreach (var atkCV in attackerField.AllViews())
            {
                if (atkCV.model.state != CardState.Attacking) continue;
                if (atkCV.model.blockedBy != null && atkCV.model.blockedBy != card) continue;
                if (atkCV.model == card.blockingTarget) continue;

                float sx = RectTransformUtility.WorldToScreenPoint(uiCam, atkCV.transform.position).x;
                bool isToRight = sx > curAtkX;
                if (isToRight != draggingRight) continue;

                float d = Mathf.Abs(sx - curAtkX);
                if (d < bestXDist) { bestXDist = d; bestAttacker = atkCV.model; bestAtkCV = atkCV; }
            }

            if (bestAttacker == null) { cv.ReturnToPlace(); return; }

            GameController.Instance.SubmitLocal(new UndeclareAction(isPlayer, card));
            if (card.location != CardLocation.OnBench) return;

            float atkX = RectTransformUtility.WorldToScreenPoint(uiCam, bestAtkCV.transform.position).x;
            int faceSlot = -1;
            float bestSD = float.MaxValue;
            for (int i = 0; i < defenderField.slots.Count; i++)
            {
                float sx = RectTransformUtility.WorldToScreenPoint(uiCam, defenderField.slots[i].position).x;
                float d = Mathf.Abs(atkX - sx);
                if (d < bestSD) { bestSD = d; faceSlot = i; }
            }

            if (faceSlot >= 0) defenderField.PrepareBlockerSlot(faceSlot);
            GameController.Instance.SubmitLocal(new DeclareBlockerAction(isPlayer, card, bestAttacker));
        }

        // ── Undeclare ─────────────────────────────────────────────
        void HandleUndeclare(CardView cv, bool isPlayer)
        {
            var m = GameController.Instance.model;
            var card = cv.model;
            bool canUndeclare =
                (m.phase == GamePhase.AttackDeclare || m.phase == GamePhase.BlockDeclare
                 || m.phase == GamePhase.SpellStackResolve)
                && m.combatPending
                && (card.state == CardState.Attacking || card.state == CardState.Blocking)
                && card.belongsToPlayer == isPlayer;
            if (!canUndeclare) return;
            GameController.Instance.SubmitLocal(new UndeclareAction(isPlayer, card));
        }

        // ── Undo cast ─────────────────────────────────────────────
        void HandleUndoCastSpell(CardView cv, bool isPlayer)
        {
            if (cv.model.belongsToPlayer != isPlayer) return;
            GameController.Instance.SubmitLocal(new UndoCastSpellAction(isPlayer, cv.model));
        }

        // ── Unstage ───────────────────────────────────────────────
        void HandleUnstageSpell(CardView cv, bool isPlayer)
        {
            if (cv.model.belongsToPlayer != isPlayer) return;
            ClearLockedArrowForSpell(cv.model);
            targetingArrow?.Cancel();
            GameController.Instance.SubmitLocal(new UnstageSpellAction(isPlayer, cv.model));
        }

        // ── Sweep-Gather Attackers (LoR-style swipe) ──────────────
        // Giữ 1 unit từ bench (lá này sẽ vào battlefield slot ĐẦU TIÊN) rồi quét
        // qua các unit khác: lá bị quét được GOM vào bộ, xếp chồng bám theo con trỏ
        // (chưa declare — dễ lựa chọn). Thả ra → declare CẢ BỘ theo thứ tự đã chọn.
        // Kéo xuống (Backward) khi thả → hủy, cả bộ bay về slot cũ.
        // Lá quét nhầm sau khi đã declare vẫn undeclare được như cũ.

        readonly HashSet<int> _sweptThisDrag = new HashSet<int>();
        readonly List<(CardView cv, Transform slot)> _sweepStack = new List<(CardView, Transform)>();
        CardView _sweepSource;
        Canvas _sweepCanvasCache;

        void Update()
        {
            SweepGatherUpdate();
        }

        void SweepGatherUpdate()
        {
            var dragging = CardView.CurrentDragging;

            // Không có drag → nếu còn stack chưa xử lý (drag bị hủy bất thường,
            // HandleSweepRelease không được gọi) thì trả các lá đã gom về slot cũ.
            if (dragging == null || dragging.model == null)
            {
                if (_sweepSource != null)
                {
                    foreach (var (cv, slot) in _sweepStack) RestoreCollected(cv, slot);
                    _sweepStack.Clear();
                    _sweepSource = null;
                    _sweptThisDrag.Clear();
                }
                return;
            }

            // Chỉ gom khi kéo unit của PLAYER từ bench
            if (!dragging.model.belongsToPlayer
                || dragging.model.location != CardLocation.OnBench) return;

            var gc = GameController.Instance;
            var m = gc != null ? gc.model : null;
            if (m == null) return;

            // Điều kiện declare attacker: player giữ attack token chưa dùng + phase hợp lệ
            bool canDeclare = m.playerHasAttackToken && !m.attackTokenUsed
                && (m.phase == GamePhase.PlayerPriority
                 || m.phase == GamePhase.EnemyPriority
                 || m.phase == GamePhase.AttackDeclare);
            if (!canDeclare) return;

            // Drag mới → reset bộ gom; lá đang cầm không tự gom (nó là lá nguồn)
            if (_sweepSource != dragging)
            {
                _sweepSource = dragging;
                _sweepStack.Clear();
                _sweptThisDrag.Clear();
                _sweptThisDrag.Add(dragging.model.id);
            }

            // Hit-test pointer với các unit khác trên bench → GOM vào bộ (chưa declare)
            Vector2 pointer = Input.mousePosition;
            var cam = SweepCamera();
            foreach (var cv in new List<CardView>(playerBench.AllViews()))
            {
                if (cv == null || cv.model == null) continue;
                if (cv.model.location != CardLocation.OnBench) continue;
                if (_sweptThisDrag.Contains(cv.model.id)) continue;

                var rt = cv.GetComponent<RectTransform>();
                if (rt == null) continue;
                if (!RectTransformUtility.RectangleContainsScreenPoint(rt, pointer, cam)) continue;

                _sweptThisDrag.Add(cv.model.id);
                CollectIntoStack(cv);
            }

            // Bộ lá đã gom bám theo lá đang cầm — xếp chồng lệch dần để thấy thứ tự
            for (int i = 0; i < _sweepStack.Count; i++)
            {
                var (scv, _) = _sweepStack[i];
                if (scv == null) continue;
                scv.transform.position = dragging.transform.position;
                scv.transform.localPosition += new Vector3(22f * (i + 1), 16f * (i + 1), 0f);
            }
        }

        void CollectIntoStack(CardView cv)
        {
            var slot = cv.transform.parent;
            _sweepStack.Add((cv, slot));

            // Reparent lên root canvas để bay tự do theo con trỏ
            var canvas = _sweepCanvasCache != null ? _sweepCanvasCache.rootCanvas : null;
            if (canvas != null)
                cv.transform.SetParent(canvas.transform, worldPositionStays: true);

            // Khóa tương tác + không chặn raycast (để quét tiếp các lá bench còn lại)
            cv.AllowInteract = false;
            var cg = cv.GetComponent<CanvasGroup>();
            if (cg != null) cg.blocksRaycasts = false;
            cv.SetHandSortingOrder(120 + _sweepStack.Count); // nổi trên bench, dưới drag (order drag = 100? — trên luôn cũng ổn)
        }

        /// <summary>
        /// Gọi từ HandleDragIntent khi lá nguồn được thả.
        /// Backward = hủy cả bộ về slot cũ. None/Forward = declare cả bộ:
        /// lá nguồn TRƯỚC (chiếm slot đầu tiên) → các lá theo thứ tự đã quét.
        /// </summary>
        void HandleSweepRelease(CardView source, DragIntent intent)
        {
            var collected = new List<(CardView cv, Transform slot)>(_sweepStack);
            _sweepStack.Clear();
            _sweepSource = null;
            _sweptThisDrag.Clear();

            var srcModel = source.model;

            if (intent == DragIntent.Backward)
            {
                foreach (var (cv, slot) in collected) RestoreCollected(cv, slot);
                source.ReturnToPlace();
                return;
            }

            var gc = GameController.Instance;

            // Lá giữ declare TRƯỚC → luôn chiếm battlefield slot đầu tiên
            gc.SubmitLocal(new DeclareAttackerAction(true, srcModel));
            foreach (var (cv, _) in collected)
                if (cv != null && cv.model != null)
                    gc.SubmitLocal(new DeclareAttackerAction(true, cv.model));

            // Lá bị reject (battlefield đầy, restriction...) vẫn còn OnBench → trả về slot
            if (source != null && srcModel != null && srcModel.location == CardLocation.OnBench)
                source.ReturnToPlace();
            foreach (var (cv, slot) in collected)
                if (cv != null && cv.model != null && cv.model.location == CardLocation.OnBench)
                    RestoreCollected(cv, slot);
        }

        void RestoreCollected(CardView cv, Transform slot)
        {
            if (cv == null) return;
            cv.AllowInteract = true;
            var cg = cv.GetComponent<CanvasGroup>();
            if (cg != null) cg.blocksRaycasts = true;
            cv.SetHandSortingOrder(1);
            if (slot != null) cv.SnapTo(slot); // reparent về slot + snap vị trí chuẩn
        }

        Camera SweepCamera()
        {
            if (_sweepCanvasCache == null && playerBench != null)
                _sweepCanvasCache = playerBench.GetComponentInParent<Canvas>();
            if (_sweepCanvasCache == null) return null;
            return _sweepCanvasCache.renderMode == RenderMode.ScreenSpaceOverlay
                ? null : _sweepCanvasCache.worldCamera;
        }

        void OnDestroy()
        {
            var gc = GameController.Instance;
            if (gc != null && _onStateChangedHandler != null)
                gc.OnStateChanged -= _onStateChangedHandler;
        }
    }
}