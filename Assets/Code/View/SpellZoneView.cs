using System.Collections.Generic;
using LoRClone.Model;

namespace LoRClone.View
{
    /// <summary>
    /// Khu vực hiển thị spell — staging + committed stack.
    ///
    /// Thứ tự hiển thị (trái → phải):
    ///   • Committed: top of stack trước (spell MỚI NHẤT ở trái = slot 0)
    ///   • Staged: newest trước (spell staged gần nhất ở sát phải committed)
    ///   → Resolve từ TRÁI sang PHẢI (LIFO), khớp convention LoR gốc.
    ///
    /// Slot được gán động mỗi lần Refresh — không fixed.
    /// Spell có thể dịch chuyển vị trí khi stack thay đổi (giống LoR gốc).
    /// </summary>
    public class SpellZoneView : ZoneView
    {
        SpellStack _spellStack;
        PlayerModel _player;
        PlayerModel _enemy;

        // _stageOrder: index 0 = spell staged ĐẦU TIÊN, cuối list = spell staged MỚI NHẤT
        readonly List<CardModel> _stageOrder = new List<CardModel>();

        // ── Init ──────────────────────────────────────────────────────
        public void Init(PlayerModel player, PlayerModel enemy, SpellStack spellStack)
        {
            _player = player;
            _enemy = enemy;
            _spellStack = spellStack;

            _spellStack.OnStackChanged += Refresh;

            _player.OnSpellStaged += (_, c) => { _stageOrder.Add(c); Refresh(); };
            _player.OnSpellUnstaged += (_, c) => { _stageOrder.Remove(c); Refresh(); };
            // FIX: PHẢI Refresh() khi commit (staged → stack). Trước đây chỉ xóa khỏi _stageOrder mà
            // không vẽ lại → lá ĐỊCH đang úp (staged) không lật ngửa lúc commit, phải đợi refresh kế
            // (khi resolve sau confirm) mới hiện → đúng bug "skill úp tới khi confirm mới hiện".
            _player.OnCardCast += (_, c) => { _stageOrder.Remove(c); Refresh(); };

            _enemy.OnSpellStaged += (_, c) => { _stageOrder.Add(c); Refresh(); };
            _enemy.OnSpellUnstaged += (_, c) => { _stageOrder.Remove(c); Refresh(); };
            _enemy.OnCardCast += (_, c) => { _stageOrder.Remove(c); Refresh(); };

            Refresh();
        }

        // ── Render ────────────────────────────────────────────────────
        void Refresh()
        {
            ClearAll();
            if (_spellStack == null) return;

            int slot = 0;

            // Committed: TOP → BOTTOM (spell mới nhất = slot 0 = trái = resolve trước)
            foreach (var entry in _spellStack.PeekAll())
            {
                var cv = SpawnCardInSlot(entry.source, slot++);
                if (cv != null) cv.SetSpellCircleMode(true);
            }

            // Staged: mới nhất trước (cuối _stageOrder = mới nhất → iterate ngược)
            for (int i = _stageOrder.Count - 1; i >= 0; i--)
            {
                var spell = _stageOrder[i];
                var cv = SpawnCardInSlot(spell, slot++);
                if (cv != null)
                {
                    cv.SetSpellCircleMode(true);
                    // Ẩn spell của ĐỐI THỦ khi còn staged (chưa confirm). Canonical: đối thủ =
                    // belongsToPlayer != NetLocalIsPlayer. PvE (NetLocalIsPlayer=true) → ẩn spell enemy như cũ.
                    if (spell.belongsToPlayer != LoRClone.Controller.GameController.NetLocalIsPlayer)
                        cv.SetFaceDown(true);
                }
            }
        }

        void OnDestroy()
        {
            if (_spellStack != null)
                _spellStack.OnStackChanged -= Refresh;
        }
    }
}