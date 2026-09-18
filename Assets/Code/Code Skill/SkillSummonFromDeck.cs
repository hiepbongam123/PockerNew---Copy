using System.Collections.Generic;
using UnityEngine;
using LoRClone.Data;
using LoRClone.Model;

namespace LoRClone.Skills
{
    /// <summary>
    /// Skill: triệu hồi unit TỪ DECK của chủ sở hữu (không tạo mới — di chuyển lá thật).
    ///
    /// ══ 2 MODE ══
    ///   TopUnit           → unit gần đỉnh deck nhất (mặc định: unit ĐẦU TIÊN tính từ đỉnh,
    ///                       bỏ qua spell/equipment nằm trên. Tick strictTopCard nếu chỉ
    ///                       muốn summon khi lá TRÊN CÙNG đúng là unit).
    ///   RandomUnitMaxCost → unit NGẪU NHIÊN trong deck có mana cost ≤ maxManaCost.
    ///
    /// ══ SETUP TRONG INSPECTOR ══
    ///   1. Create → LoRClone/Skills/Summon From Deck
    ///   2. Chọn mode + maxManaCost (nếu dùng RandomUnitMaxCost)
    ///   3. Gán vào CardData.abilities của card gốc:
    ///        trigger = WhenPlayed (hoặc OnAttack, RoundStart, OnDeath...)
    ///        effect  = asset này
    ///        p1 = quantity override (-1 = dùng default)
    ///        p2 = maxManaCost override (-1 = dùng default; chỉ mode RandomUnitMaxCost)
    ///   4. Không cần target → AI enemy dùng được ngay, không cần setup thêm.
    ///
    /// ══ LƯU Ý / RỦI RO ══
    ///   • Lá bị XÓA khỏi deck (đúng luật LoR) — deck mỏng đi, ảnh hưởng Deep + draw.
    ///   • Giữ nguyên buff runtime của lá trong deck (Lurk, Kayle passive...).
    ///   • Deck rỗng / không có unit thỏa điều kiện → skill bỏ qua, không lỗi.
    ///   • Bench/battlefield đầy → dừng sớm, lá vẫn nằm nguyên trong deck.
    ///   • Recursion guard: WhenPlayed của unit vừa summon KHÔNG thể summon tiếp
    ///     (giống SkillSummonUnit — _isSummoning guard trong GameController).
    /// </summary>
    [CreateAssetMenu(menuName = "LoRClone/Skills/Summon From Deck", fileName = "Skill_SummonFromDeck")]
    public class SkillSummonFromDeck : SkillData
    {
        public enum DeckSummonMode
        {
            /// <summary>Unit gần đỉnh deck nhất.</summary>
            TopUnit,
            /// <summary>Unit ngẫu nhiên trong deck có cost ≤ maxManaCost.</summary>
            RandomUnitMaxCost,
        }

        [Header("Mode")]
        public DeckSummonMode mode = DeckSummonMode.TopUnit;

        [Header("TopUnit — chỉ dùng khi mode = TopUnit")]
        [Tooltip("true  = CHỈ summon nếu lá trên cùng deck đúng là Unit (spell trên cùng → fail).\n" +
                 "false = tìm unit đầu tiên tính từ đỉnh deck xuống (khuyến dùng).")]
        public bool strictTopCard = false;

        [Header("RandomUnitMaxCost — chỉ dùng khi mode = RandomUnitMaxCost")]
        [Tooltip("Chỉ chọn unit có mana cost ≤ giá trị này. Override bằng p2 (-1 = dùng giá trị này).")]
        [Min(0)]
        public int maxManaCost = 3;

        [Header("Số lượng")]
        [Tooltip("Số unit triệu hồi mỗi lần kích hoạt. Override bằng p1 (-1 = dùng giá trị này).")]
        [Min(1)]
        public int quantity = 1;

        [Header("Vị trí triệu hồi")]
        [Tooltip("false = bench (mặc định).\ntrue  = battlefield (tham chiến ngay nếu có attack token).")]
        public bool summonToBattlefield = false;

        public override void Execute(CardModel source, GameContext ctx,
                                     int p1 = -1, int p2 = -1, int p3 = -1)
        {
            if (ctx?.controller == null)
            {
                Debug.LogWarning("[SkillSummonFromDeck] ctx.controller null — skill không thể chạy ngoài GameController.");
                return;
            }

            var deck = ctx.owner.deck;
            if (deck.Count == 0)
            {
                Debug.Log($"[SkillSummonFromDeck] '{source.data.cardName}': deck rỗng — bỏ qua.");
                return;
            }

            int count = (p1 >= 0) ? p1 : quantity;
            int costCap = (p2 >= 0) ? p2 : maxManaCost;

            for (int i = 0; i < count; i++)
            {
                var pick = FindCandidate(deck, costCap);
                if (pick == null)
                {
                    Debug.Log($"[SkillSummonFromDeck] '{source.data.cardName}': không còn unit thỏa điều kiện trong deck.");
                    break;
                }

                var summoned = ctx.controller.SummonUnitFromDeck(
                    pick, source.belongsToPlayer, summonToBattlefield);
                if (summoned == null) break; // bench/battlefield đầy hoặc recursion guard → dừng sớm
            }
        }

        CardModel FindCandidate(List<CardModel> deck, int costCap)
        {
            switch (mode)
            {
                case DeckSummonMode.TopUnit:
                    if (strictTopCard)
                        return deck[0].data.cardType == CardType.Unit ? deck[0] : null;
                    // Unit đầu tiên tính từ đỉnh deck (deck[0] = lá trên cùng, xem DrawCard)
                    foreach (var c in deck)
                        if (c.data.cardType == CardType.Unit) return c;
                    return null;

                case DeckSummonMode.RandomUnitMaxCost:
                    var candidates = new List<CardModel>();
                    foreach (var c in deck)
                        if (c.data.cardType == CardType.Unit && c.currentManaCost <= costCap)
                            candidates.Add(c);
                    if (candidates.Count == 0) return null;
                    return candidates[LoRClone.Controller.GameRNG.Range(0, candidates.Count)];
            }
            return null;
        }
    }
}
