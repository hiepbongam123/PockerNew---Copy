using System.Collections.Generic;
using UnityEngine;
using LoRClone.Model;

namespace LoRClone.View
{
    public class BattlefieldZoneView : ZoneView
    {
        int _preferredSlot = -1;

        public void PrepareBlockerSlot(int slot) => _preferredSlot = slot;

        public void Init(PlayerModel playerModel)
        {
            playerModel.OnCardMovedToBattlefield += (_, c) => OnArrival(c);
            playerModel.OnCardReturnedToBench += (_, c) => RemoveCard(c);
            playerModel.OnCardDied += (_, c) => RemoveCard(c);
            playerModel.OnCardRecalled += (_, c) => RemoveCard(c); // Recall: xóa khỏi battlefield
            // Compact: khi attacker bị undeclare, các attacker còn lại dồn về slot mới.
            playerModel.OnBattlefieldCompacted += (_, moved) => OnCompacted(moved);
        }

        void OnArrival(CardModel cardModel)
        {
            _preferredSlot = -1; // reset (không còn dùng — model slot là source of truth)
            // Dùng cardModel.slotIndex trực tiếp thay vì _preferredSlot.
            // MoveBlockerToBattlefield() đã gán slotIndex = attacker's slot,
            // nên blocker spawn đúng cột đối diện attacker → không đánh chéo.
            var cv = SpawnCardInSlot(cardModel, cardModel.slotIndex);
            if (cv == null) return;

            cv.Bind(cardModel);
            cv.OnDropped += (view, _) => FireDrop(view);
        }

        /// <summary>
        /// Xử lý compact: mỗi card trong <paramref name="movedCards"/> đã có slotIndex mới.
        /// Xóa CardView cũ (tìm theo CardModel ref) rồi spawn lại tại slot mới.
        /// RemoveCard scan theo CardModel ref nên tìm đúng dù slotIndex đã đổi.
        /// </summary>
        void OnCompacted(List<CardModel> movedCards)
        {
            foreach (var card in movedCards)
            {
                // Xóa CardView tại slot cũ (RemoveCard tìm theo CardModel reference)
                RemoveCard(card);

                // Spawn lại tại slot mới (card.slotIndex đã được PlayerModel cập nhật)
                var cv = SpawnCardInSlot(card, card.slotIndex);
                if (cv == null) continue;

                cv.Bind(card);
                cv.OnDropped += (view, _) => FireDrop(view);
            }
        }
    }
}