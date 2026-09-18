using System;
using System.Collections.Generic;
using LoRClone.Data;
using LoRClone.Model;

namespace LoRClone.Controller
{
    /// <summary>
    /// GameController — phần TIÊN TRI (Predict).
    /// Partial mới: chỉ thêm event + 1 method plumbing cho việc chọn 1 trong N lá.
    /// KHÔNG sửa file GameController khác.
    ///
    /// Luồng:
    ///   SkillPredict (player) gọi RequestPredictChoice(display, true, resolver)
    ///     → fire OnPredictNeedsChoice → GameView mở CardPickerView (modal dim, tự khoá input)
    ///     → player bấm 1 lá → wrapped(idx) → resolver(idx) (đảo deck + buff) → Notify + CheckGameOver.
    ///
    /// An toàn:
    ///   • Enemy KHÔNG đi qua đây (SkillPredict tự resolve inline) → không mở UI, không treo.
    ///   • Nếu không ai subscribe (thiếu GameView/CardPickerView) → tự chọn lá đầu (idx 0),
    ///     KHÔNG treo game.
    /// </summary>
    public partial class GameController
    {
        /// <summary>
        /// Fired khi cần player chọn 1 trong 'choices' lá (Tiên tri).
        /// GameView subscribe: nếu isPlayer → mở CardPickerView, trả index qua onPick.
        /// Args: (choices, isPlayer, onPick).
        /// </summary>
        public event Action<List<CardData>, bool, Action<int>> OnPredictNeedsChoice;

        /// <summary>
        /// Yêu cầu player chọn 1 trong 'choices'. onPick(index) do skill cung cấp (đảo deck + buff).
        /// index = -1 nghĩa là hủy/không chọn.
        /// </summary>
        public void RequestPredictChoice(List<CardData> choices, bool isPlayer, Action<int> onPick)
        {
            if (onPick == null) return;
            if (choices == null || choices.Count == 0) { onPick(-1); return; }

            Action<int> wrapped = idx =>
            {
                try { onPick(idx); }
                finally
                {
                    model?.CheckGameOver();
                    Notify();
                }
            };

            if (isPlayer && playerAutoPlay)
            {
                int pick = 0;
                for (int i = 0; i < choices.Count; i++)
                    if (choices[i] != null && choices[i].cardType == CardType.Unit) { pick = i; break; }
                wrapped(pick);
                return;
            }
            if (OnPredictNeedsChoice != null)
                OnPredictNeedsChoice.Invoke(choices, isPlayer, wrapped);
            else
                wrapped(0); // không có UI subscriber → tự chọn lá đầu, tránh treo
        }
    }
}