using System;
using System.Collections.Generic;

namespace LoRClone.Model
{
    /// <summary>
    /// Một entry trong spell stack — có thể là spell hoặc skill effect.
    /// </summary>
    public class StackEntry
    {
        public readonly PlayerModel owner;
        public readonly CardModel source;          // card tạo ra effect này
        public readonly string description;
        public readonly Action<GameContext> resolve; // logic thực thi

        public StackEntry(PlayerModel owner, CardModel source, string description, Action<GameContext> resolve)
        {
            this.owner = owner;
            this.source = source;
            this.description = description;
            this.resolve = resolve;
        }
    }

    /// <summary>
    /// Stack xử lý spell/effect theo thứ tự LIFO (last in, first out) giống LoR.
    /// Burst spell resolve ngay, Fast/Slow cho phép response trước khi resolve.
    ///
    /// Quy trình đúng LoR:
    ///   1. Player commit spell → push lên stack → đối thủ nhận response window.
    ///   2. Khi cả 2 pass liên tiếp → chỉ resolve TOP entry (ResolveTop).
    ///   3. Sau mỗi resolve, nếu stack còn entry → mở response window mới.
    ///   4. Lặp lại cho đến khi stack rỗng.
    /// </summary>
    public class SpellStack
    {
        private readonly Stack<StackEntry> _stack = new();

        public int Count => _stack.Count;
        public bool IsEmpty => _stack.Count == 0;

        // View lắng nghe để hiển thị stack
        public event Action OnStackChanged;

        public void Push(StackEntry entry)
        {
            _stack.Push(entry);
            OnStackChanged?.Invoke();
        }

        /// <summary>
        /// Resolve chỉ entry trên cùng (đúng LoR: mỗi spell resolve xong → mở response window mới).
        /// Gọi từ GameModel.OnBothPassed khi phase == SpellStackResolve.
        /// </summary>
        public void ResolveTop(GameModel game)
        {
            if (_stack.Count == 0) return;
            var entry = _stack.Pop();
            entry.resolve(new GameContext(game, entry.owner));
            OnStackChanged?.Invoke();
        }

        /// <summary>
        /// Resolve toàn bộ stack từ trên xuống, KHÔNG mở response window giữa các entry.
        /// Chỉ dùng trong trường hợp đặc biệt (ví dụ: flush khi game over).
        /// Gameplay bình thường phải dùng ResolveTop + response window loop.
        /// </summary>
        public void ResolveAll(GameModel game)
        {
            while (_stack.Count > 0)
            {
                var entry = _stack.Pop();
                entry.resolve(new GameContext(game, entry.owner));
                OnStackChanged?.Invoke();
            }
        }

        /// <summary>
        /// Trả về entry trên cùng mà không pop — null nếu stack rỗng.
        /// </summary>
        public StackEntry PeekTop() => _stack.Count > 0 ? _stack.Peek() : null;

        public IEnumerable<StackEntry> PeekAll() => _stack;

        /// <summary>
        /// Tìm entry theo source card và xóa khỏi stack (hủy cast spell chưa resolve).
        /// Chỉ cho phép hủy entry TRÊN CÙNG (top of stack) — đúng luật LIFO:
        /// không thể rút spell nằm dưới khi đã có spell khác chồng lên trên.
        /// Trả về true nếu xóa thành công.
        /// </summary>
        public bool TryRemoveTopIfMatches(CardModel sourceCard)
        {
            if (_stack.Count == 0) return false;
            if (_stack.Peek().source != sourceCard) return false;

            _stack.Pop();
            OnStackChanged?.Invoke();
            return true;
        }

        public void Clear()
        {
            _stack.Clear();
            OnStackChanged?.Invoke();
        }
    }
}
