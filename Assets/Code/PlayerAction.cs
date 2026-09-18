using LoRClone.Model;

namespace LoRClone.Controller
{
    public abstract class PlayerAction
    {
        public readonly bool byPlayer;
        protected PlayerAction(bool byPlayer) { this.byPlayer = byPlayer; }
    }

    // Hand → Bench
    public sealed class PlayUnitAction : PlayerAction
    {
        public readonly CardModel card;
        public PlayUnitAction(bool byPlayer, CardModel card) : base(byPlayer)
            => this.card = card;
    }

    // Bench → Battlefield (declare attacker)
    public sealed class DeclareAttackerAction : PlayerAction
    {
        public readonly CardModel card;
        public DeclareAttackerAction(bool byPlayer, CardModel card) : base(byPlayer)
            => this.card = card;
    }

    // Bench → Battlefield (declare blocker)
    public sealed class DeclareBlockerAction : PlayerAction
    {
        public readonly CardModel blocker;
        public readonly CardModel target; // attacker bị block
        public DeclareBlockerAction(bool byPlayer, CardModel blocker, CardModel target)
            : base(byPlayer)
        {
            this.blocker = blocker;
            this.target  = target;
        }
    }

    // Spell
    public sealed class CastSpellAction : PlayerAction
    {
        public readonly CardModel spell;
        public readonly CardModel targetCard;
        public CastSpellAction(bool byPlayer, CardModel spell, CardModel targetCard = null)
            : base(byPlayer)
        {
            this.spell      = spell;
            this.targetCard = targetCard;
        }
    }

    // Pass priority / End turn
    public sealed class PassPriorityAction : PlayerAction
    {
        public PassPriorityAction(bool byPlayer) : base(byPlayer) { }
    }

    // Battlefield → Bench (hủy declare attacker/blocker, chưa confirm)
    public sealed class UndeclareAction : PlayerAction
    {
        public readonly CardModel card;
        public UndeclareAction(bool byPlayer, CardModel card) : base(byPlayer)
            => this.card = card;
    }

    // Spell Stack → Hand (hủy cast spell chưa resolve, hoàn mana)
    public sealed class UndoCastSpellAction : PlayerAction
    {
        public readonly CardModel spell;
        public UndoCastSpellAction(bool byPlayer, CardModel spell) : base(byPlayer)
            => this.spell = spell;
    }

    // Hand → SpellZone (staging — chưa commit, chưa trừ mana, chưa chuyển lượt)
    // Dùng cho spell Fast/Slow. Burst dùng CastSpellAction (resolve ngay).
    public sealed class StageSpellAction : PlayerAction
    {
        public readonly CardModel spell;
        public StageSpellAction(bool byPlayer, CardModel spell) : base(byPlayer)
            => this.spell = spell;
    }

    // SpellZone (staging) → Hand (bỏ staging, chưa trừ mana nên không hoàn)
    public sealed class UnstageSpellAction : PlayerAction
    {
        public readonly CardModel spell;
        public UnstageSpellAction(bool byPlayer, CardModel spell) : base(byPlayer)
            => this.spell = spell;
    }
}
