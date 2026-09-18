using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using LoRClone.Data;
using LoRClone.Model;

namespace LoRClone.Skills
{
    /// <summary>
    /// Skill: TIÊN TRI (Predict) — hiện N lá trên đỉnh deck, chọn 1 đưa lên đỉnh.
    /// Tùy chọn: nếu lá được chọn thuộc loại 'interactCardType' (mặc định Equipment)
    /// thì buff cho CHÍNH unit mang skill này (mặc định +1|+1).
    ///
    /// ══ LUỒNG AN TOÀN (bám khuôn OnUnitSkillNeedsTarget đã chạy ổn) ══
    ///   • Player: gọi ctx.controller.RequestPredictChoice(...) → GameView mở CardPickerView
    ///     (modal dim chặn input) → player bấm 1 lá → callback ApplyChoice.
    ///   • Enemy: KHÔNG mở UI — tự chọn (EnemyPick) đồng bộ ngay → KHÔNG treo game.
    ///
    /// ══ SETUP INSPECTOR ══
    ///   1. Create → LoRClone/Skills/Predict
    ///   2. count = số lá hiện ra (3), interactCardType = Equipment, buffAttack/buffHealth = 1
    ///   3. Gán vào CardData.abilities: trigger = WhenPlayed (hoặc khác), effect = asset này.
    ///      Không cần targetType/requiresTarget — skill tự lo UI.
    ///
    /// ══ LƯU Ý / RỦI RO ══
    ///   • Chỉ đọc/đảo thứ tự deck (đưa 1 lá lên đỉnh) — không xóa lá, không xáo phần còn lại.
    ///   • deck rỗng → bỏ qua; deck &lt; count → hiện số lá có.
    ///   • Buff bám theo source runtime (OnStatsChanged tự cập nhật CardView).
    ///   • Reorder xảy ra trong callback (async cho player) — dùng cho hiệu ứng đứng một mình
    ///     (WhenPlayed). Không nên chain ngay 1 skill khác đọc top-deck sau Predict trong cùng lá.
    /// </summary>
    [CreateAssetMenu(menuName = "LoRClone/Skills/Predict", fileName = "Skill_Predict")]
    public class SkillPredict : SkillData
    {
        [Header("Số lá hiện ra từ đỉnh deck")]
        [Min(1)] public int count = 3;

        [Tooltip("true = đưa lá được chọn lên ĐỈNH deck (rút kế tiếp) — đúng Predict LoR.")]
        public bool putChosenOnTop = true;

        [Header("Tương tác với lá được chọn (tùy chọn)")]
        [Tooltip("Bật = nếu lá chọn đúng loại bên dưới thì buff cho unit mang skill này.")]
        public bool applyInteractionBuff = true;

        [Tooltip("Loại lá kích hoạt buff (mặc định Equipment: 'chọn trang bị → +1|+1').")]
        public CardType interactCardType = CardType.Equipment;

        [Tooltip("ATK buff cho source khi lá chọn đúng loại. Override p1 (-1 = dùng giá trị này).")]
        public int buffAttack = 1;
        [Tooltip("HP buff cho source khi lá chọn đúng loại. Override p2 (-1 = dùng giá trị này).")]
        public int buffHealth = 1;

        [Tooltip("true = buff chỉ trong round này; false = vĩnh viễn (mặc định).")]
        public bool temporaryBuff = false;

        public override void Execute(CardModel source, GameContext ctx,
                                     int p1 = -1, int p2 = -1, int p3 = -1)
        {
            if (source == null || ctx?.owner == null) return;

            var deck = ctx.owner.deck;
            if (deck == null || deck.Count == 0)
            {
                Debug.Log($"[SkillPredict] '{source?.data?.cardName}': deck rỗng — bỏ qua.");
                return;
            }

            int n = Mathf.Min((p3 >= 0) ? p3 : count, deck.Count);
            int atk = (p1 >= 0) ? p1 : buffAttack;
            int hp = (p2 >= 0) ? p2 : buffHealth;

            // Snapshot N lá trên đỉnh (deck[0] = đỉnh)
            var top = new List<CardModel>(n);
            for (int i = 0; i < n; i++) top.Add(deck[i]);
            var display = top.Select(c => c.data).ToList();

            var owner = ctx.owner;
            System.Action<int> resolver = idx => ApplyChoice(source, owner, top, idx, atk, hp);

            if (source.belongsToPlayer && ctx.controller != null)
            {
                // Player → mở UI qua GameController (modal, khoá input, callback resolver)
                ctx.controller.RequestPredictChoice(display, true, resolver);
            }
            else
            {
                // Enemy (hoặc không có controller) → tự chọn đồng bộ, KHÔNG mở UI, KHÔNG treo
                resolver(EnemyPick(top));
            }
        }

        void ApplyChoice(CardModel source, PlayerModel owner, List<CardModel> top, int idx,
                         int atk, int hp)
        {
            if (owner == null || top == null) return;
            if (idx < 0 || idx >= top.Count) return; // hủy / index xấu → không đổi gì

            var chosen = top[idx];
            if (chosen == null) return;

            if (putChosenOnTop)
            {
                owner.deck.Remove(chosen);
                owner.deck.Insert(0, chosen);
            }

            if (applyInteractionBuff
                && chosen.data != null
                && chosen.data.cardType == interactCardType
                && (atk != 0 || hp != 0)
                && source != null)
            {
                if (atk != 0) source.BuffAttack(atk, temporaryBuff);
                if (hp != 0) source.BuffHealth(hp, temporaryBuff);
                source.RecordSkillBuff(source.data.cardName, source.data.artwork, atk, hp, temporaryBuff);
                Debug.Log($"[SkillPredict] '{source.data.cardName}': chọn {chosen.data.cardName} " +
                          $"({interactCardType}) → source +{atk}|+{hp}.");
            }
            else
            {
                Debug.Log($"[SkillPredict] chọn {chosen.data?.cardName} → đưa lên đỉnh deck.");
            }
        }

        /// <summary>AI chọn: ưu tiên lá kích hoạt buff (đúng interactCardType) → unit → lá đầu.</summary>
        int EnemyPick(List<CardModel> top)
        {
            if (top == null || top.Count == 0) return -1;
            if (applyInteractionBuff)
                for (int i = 0; i < top.Count; i++)
                    if (top[i]?.data != null && top[i].data.cardType == interactCardType) return i;
            for (int i = 0; i < top.Count; i++)
                if (top[i]?.data != null && top[i].data.cardType == CardType.Unit) return i;
            return 0;
        }
    }
}
