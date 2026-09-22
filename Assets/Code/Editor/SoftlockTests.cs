// ─────────────────────────────────────────────────────────────────────────────
// SoftlockTests.cs — UNIT TEST (EditMode) chống TREO GAME ở luồng targeting + spell-stack.
//
// PHẠM VI (chạy được, KHÔNG cần scene/GameController):
//   T. TargetingArrow — bất biến "mọi phiên chọn target đều fire đúng 1 callback"
//        → nếu vi phạm, coroutine while(!done) / battle-skill coroutine chờ mãi → treo game.
//        Bao gồm case re-entrancy (2 phiên targeting chồng nhau) vừa được vá.
//   S. SpellStack — stack luôn DRAIN hết, kể cả khi target "chết" giữa chừng (resolve no-op).
//
// Chạy: Window → General → Test Runner → EditMode → Run All.
// ─────────────────────────────────────────────────────────────────────────────
using System;
using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using LoRClone.Model;
using LoRClone.View;

namespace LoRClone.Tests
{
    // ══════════════════════════════════════════════════════════════════
    // T. TargetingArrow — callback luôn fire (chống treo coroutine)
    // ══════════════════════════════════════════════════════════════════
    public class TargetingArrowSoftlockTests
    {
        readonly List<GameObject> _spawned = new List<GameObject>();

        // Tạo TargetingArrow trên GameObject INACTIVE → Awake/Start KHÔNG chạy
        // (không set Instance, không đụng canvas) → cô lập giữa các test.
        TargetingArrow NewArrow()
        {
            var go = new GameObject("TargetingArrow_Test");
            go.SetActive(false);
            _spawned.Add(go);
            return go.AddComponent<TargetingArrow>();
        }

        static List<CardView> NoTargets() => new List<CardView>();

        [TearDown]
        public void TearDown()
        {
            foreach (var go in _spawned) if (go != null) UnityEngine.Object.DestroyImmediate(go);
            _spawned.Clear();
        }

        [Test] // T1 — confirm bình thường: onConfirmed fire, IsTargeting về false
        public void T1_Confirm_FiresConfirmOnce_AndEnds()
        {
            var arrow = NewArrow();
            int confirmed = 0, cancelled = 0;
            arrow.StartTargeting(null, Vector2.zero, NoTargets(),
                _ => confirmed++, () => cancelled++);
            Assert.IsTrue(arrow.IsTargeting, "Bắt đầu targeting → IsTargeting = true.");

            arrow.ConfirmTarget(null);
            Assert.AreEqual(1, confirmed, "onConfirmed phải fire đúng 1 lần.");
            Assert.AreEqual(0, cancelled, "Không được fire onCancel khi đã confirm.");
            Assert.IsFalse(arrow.IsTargeting, "Confirm xong → IsTargeting = false.");
        }

        [Test] // T2 — cancel bình thường: onCancel fire, IsTargeting về false
        public void T2_Cancel_FiresCancelOnce_AndEnds()
        {
            var arrow = NewArrow();
            int confirmed = 0, cancelled = 0;
            arrow.StartTargeting(null, Vector2.zero, NoTargets(),
                _ => confirmed++, () => cancelled++);

            arrow.Cancel();
            Assert.AreEqual(1, cancelled, "onCancel phải fire đúng 1 lần.");
            Assert.AreEqual(0, confirmed, "Không được fire onConfirmed khi đã cancel.");
            Assert.IsFalse(arrow.IsTargeting, "Cancel xong → IsTargeting = false.");
        }

        [Test] // T3 — CORE FIX: 2 phiên targeting chồng nhau → phiên cũ bị hủy (onCancel fire),
               //          KHÔNG bỏ rơi callback → không treo coroutine đang chờ.
        public void T3_ReentrantStart_CancelsPreviousSession_NoOrphan()
        {
            var arrow = NewArrow();
            int cancelledA = 0, confirmedA = 0;
            int cancelledB = 0, confirmedB = 0;

            // Phiên A bắt đầu (giả lập: coroutine đang chờ callback A)
            arrow.StartTargeting(null, Vector2.zero, NoTargets(),
                _ => confirmedA++, () => cancelledA++);

            // Phiên B chồng lên KHI A chưa kết thúc
            arrow.StartTargeting(null, Vector2.zero, NoTargets(),
                _ => confirmedB++, () => cancelledB++);

            Assert.AreEqual(1, cancelledA, "Phiên A phải bị HỦY (onCancel fire) khi B bắt đầu — không bỏ rơi.");
            Assert.AreEqual(0, confirmedA, "Phiên A không được confirm.");
            Assert.IsTrue(arrow.IsTargeting, "Sau khi B bắt đầu → vẫn đang targeting (phiên B).");

            // Callback của A không được kích hoạt lại bởi thao tác của B
            arrow.ConfirmTarget(null);
            Assert.AreEqual(1, confirmedB, "Confirm giờ thuộc phiên B.");
            Assert.AreEqual(1, cancelledA, "Callback A KHÔNG được gọi thêm lần nữa.");
            Assert.AreEqual(0, confirmedA, "Callback A KHÔNG bị confirm.");
            Assert.IsFalse(arrow.IsTargeting, "Confirm B xong → hết targeting.");
        }

        [Test] // T4 — ClearLocked khi đang chọn target → onCancel fire (không treo coroutine)
        public void T4_ClearLocked_WhileSelecting_FiresCancel()
        {
            var arrow = NewArrow();
            int cancelled = 0;
            arrow.StartTargeting(null, Vector2.zero, NoTargets(), _ => { }, () => cancelled++);

            arrow.ClearLocked();
            Assert.AreEqual(1, cancelled, "ClearLocked khi đang selecting phải fire onCancel.");
            Assert.IsFalse(arrow.IsTargeting, "ClearLocked xong → hết targeting.");
        }

        [Test] // T5 — guard: gọi Confirm/Cancel khi KHÔNG selecting → no-op, không throw, không callback
        public void T5_ConfirmOrCancel_WhenIdle_IsNoOp()
        {
            var arrow = NewArrow();
            int confirmed = 0, cancelled = 0;

            Assert.DoesNotThrow(() => arrow.ConfirmTarget(null));
            Assert.DoesNotThrow(() => arrow.Cancel());
            Assert.IsFalse(arrow.IsTargeting);
            Assert.AreEqual(0, confirmed + cancelled, "Idle → không callback nào chạy.");
        }
    }

    // ══════════════════════════════════════════════════════════════════
    // S. SpellStack — luôn drain hết, kể cả target chết giữa chừng
    // ══════════════════════════════════════════════════════════════════
    public class SpellStackSoftlockTests
    {
        static StackEntry Entry(PlayerModel owner, string desc, Action<GameContext> resolve)
            => new StackEntry(owner, null, desc, resolve);

        [Test] // S1 — LIFO: resolve theo thứ tự ngược (top-down)
        public void S1_ResolvesLifoOrder()
        {
            var game = new GameModel();
            var stack = new SpellStack();
            var order = new List<string>();

            stack.Push(Entry(game.player, "A", _ => order.Add("A")));
            stack.Push(Entry(game.player, "B", _ => order.Add("B")));
            stack.Push(Entry(game.player, "C", _ => order.Add("C")));

            while (!stack.IsEmpty) stack.ResolveTop(game);

            Assert.AreEqual(new List<string> { "C", "B", "A" }, order, "Phải resolve LIFO: C, B, A.");
            Assert.IsTrue(stack.IsEmpty, "Stack phải rỗng sau khi resolve hết.");
        }

        [Test] // S2 — ResolveTop trên stack RỖNG là an toàn (không throw)
        public void S2_ResolveTop_OnEmpty_IsSafe()
        {
            var game = new GameModel();
            var stack = new SpellStack();
            Assert.DoesNotThrow(() => stack.ResolveTop(game), "ResolveTop khi rỗng phải no-op, không throw.");
            Assert.IsTrue(stack.IsEmpty);
        }

        [Test] // S3 — ResolveAll drain toàn bộ, mỗi entry chạy đúng 1 lần
        public void S3_ResolveAll_DrainsEverything()
        {
            var game = new GameModel();
            var stack = new SpellStack();
            int resolved = 0;
            for (int i = 0; i < 5; i++) stack.Push(Entry(game.player, "e" + i, _ => resolved++));

            stack.ResolveAll(game);

            Assert.AreEqual(5, resolved, "Cả 5 entry phải resolve đúng 1 lần.");
            Assert.IsTrue(stack.IsEmpty, "ResolveAll phải drain sạch stack.");
        }

        [Test] // S4 — CORE: target "chết" giữa chừng → resolve no-op nhưng stack VẪN drain hết (không treo)
        public void S4_DeadTargetsMidResolve_StillDrains_NoHang()
        {
            var game = new GameModel();
            var stack = new SpellStack();

            bool targetAlive = true;   // giả lập unit mục tiêu
            int applied = 0, resolvedCount = 0;

            // Mỗi entry mô phỏng skill: chỉ áp hiệu ứng nếu target còn sống (giống guard IsAlive trong skill thật).
            for (int i = 0; i < 3; i++)
                stack.Push(Entry(game.player, "spell" + i, _ =>
                {
                    resolvedCount++;
                    if (targetAlive) applied++;   // target chết → bỏ qua, KHÔNG kẹt
                }));

            // Target chết TRƯỚC khi stack resolve (vd bị spell khác giết, unit rời sân...)
            targetAlive = false;

            Assert.DoesNotThrow(() =>
            {
                while (!stack.IsEmpty) stack.ResolveTop(game);
            }, "Stack phải resolve trơn tru dù target đã chết — không throw, không treo.");

            Assert.AreEqual(3, resolvedCount, "Cả 3 entry vẫn được pop & resolve (drain hết).");
            Assert.AreEqual(0, applied, "Target đã chết → không hiệu ứng nào áp, nhưng không gây kẹt.");
            Assert.IsTrue(stack.IsEmpty, "Stack rỗng sau resolve — không softlock.");
        }

        [Test] // S5 — OnStackChanged fire khi push và khi resolve (view không bị 'đứng hình')
        public void S5_OnStackChanged_FiresOnPushAndResolve()
        {
            var game = new GameModel();
            var stack = new SpellStack();
            int changes = 0;
            stack.OnStackChanged += () => changes++;

            stack.Push(Entry(game.player, "A", _ => { }));   // +1
            stack.Push(Entry(game.player, "B", _ => { }));   // +1
            stack.ResolveTop(game);                          // +1
            stack.ResolveTop(game);                          // +1

            Assert.AreEqual(4, changes, "OnStackChanged phải fire mỗi lần push/resolve.");
        }

        [Test] // S6 — TryRemoveTopIfMatches chỉ xóa khi khớp source; stack rỗng → false an toàn
        public void S6_TryRemoveTop_EmptyStack_ReturnsFalse()
        {
            var stack = new SpellStack();
            Assert.IsFalse(stack.TryRemoveTopIfMatches(null), "Stack rỗng → không có gì để hủy, trả false.");
            Assert.IsTrue(stack.IsEmpty);
        }
    }
}
