using System.Collections.Generic;
using LoRClone.Controller;
using LoRClone.Model;

namespace LoRClone.Net
{
    /// <summary>
    /// FILE 3/5 — Chuyển đổi 2 chiều giữa PlayerAction ↔ ActionDTO (gói tin mạng).
    /// Đặt ở: Assets/Code/Net/ActionCodec.cs
    ///
    /// ĐÃ HOÀN THIỆN theo đúng PlayerAction.cs của bạn (v1 core actions).
    /// v2 (chưa làm): action có targeting tương tác (equipment mode, unit-skill nhiều target).
    /// </summary>
    public static class ActionCodec
    {
        // ── PlayerAction → ActionDTO (bên GỬI). Mang theo byPlayer canonical (từ belongsToPlayer). ──
        public static ActionDTO ToDTO(PlayerAction a)
        {
            ActionDTO d;
            switch (a)
            {
                case PlayUnitAction x: d = new ActionDTO { type = "PlayUnit", cardId = x.card.netId }; break;
                case DeclareAttackerAction x: d = new ActionDTO { type = "Attack", cardId = x.card.netId }; break;
                case UndeclareAction x: d = new ActionDTO { type = "Undeclare", cardId = x.card.netId }; break;
                case DeclareBlockerAction x: d = new ActionDTO { type = "Block", cardId = x.blocker.netId, targetIds = new[] { x.target.netId } }; break;
                case CastSpellAction x: d = new ActionDTO { type = "Cast", cardId = x.spell.netId, targetIds = x.targetCard != null ? new[] { x.targetCard.netId } : null }; break;
                case StageSpellAction x: d = new ActionDTO { type = "Stage", cardId = x.spell.netId }; break;
                case UnstageSpellAction x: d = new ActionDTO { type = "Unstage", cardId = x.spell.netId }; break;
                case UndoCastSpellAction x: d = new ActionDTO { type = "UndoCast", cardId = x.spell.netId }; break;
                case PassPriorityAction _: d = new ActionDTO { type = "Pass" }; break;
                default:
                    UnityEngine.Debug.LogWarning($"[Net] Chưa hỗ trợ gửi action: {a.GetType().Name} (v2)");
                    return null;
            }
            d.byPlayer = a.byPlayer; // canonical: giống nhau trên 2 máy
            return d;
        }

        // ── ActionDTO → PlayerAction (bên NHẬN). Dùng byPlayer canonical trong DTO, KHÔNG lật. ──
        public static PlayerAction ToAction(ActionDTO d, GameModel m)
        {
            bool byPlayer = d.byPlayer;
            var card = FindCard(m, d.cardId);
            CardModel firstTarget = (d.targetIds != null && d.targetIds.Length > 0) ? FindCard(m, d.targetIds[0]) : null;

            switch (d.type)
            {
                case "PlayUnit": return card != null ? new PlayUnitAction(byPlayer, card) : null;
                case "Attack": return card != null ? new DeclareAttackerAction(byPlayer, card) : null;
                case "Undeclare": return card != null ? new UndeclareAction(byPlayer, card) : null;
                case "Block": return (card != null && firstTarget != null) ? new DeclareBlockerAction(byPlayer, card, firstTarget) : null;
                case "Cast": return card != null ? new CastSpellAction(byPlayer, card, firstTarget) : null;
                case "Stage": return card != null ? new StageSpellAction(byPlayer, card) : null;
                case "Unstage": return card != null ? new UnstageSpellAction(byPlayer, card) : null;
                case "UndoCast": return card != null ? new UndoCastSpellAction(byPlayer, card) : null;
                case "Pass": return new PassPriorityAction(byPlayer);
                default: return null;
            }
        }

        // Tìm lá theo netId trong toàn bộ state (tay/bench/battlefield/stack) của cả 2 phe.
        static CardModel FindCard(GameModel m, int netId)
        {
            if (netId < 0) return null;
            foreach (var side in new[] { m.player, m.enemy })
            {
                foreach (var c in side.hand) if (c.netId == netId) return c;
                foreach (var c in side.BenchCards()) if (c.netId == netId) return c;
                foreach (var c in side.BattlefieldCards()) if (c.netId == netId) return c;
            }
            // Spell đang staging / trên stack
            foreach (var c in m.player.stagedSpells) if (c != null && c.netId == netId) return c;
            foreach (var c in m.enemy.stagedSpells) if (c != null && c.netId == netId) return c;
            return null;
        }
    }
}