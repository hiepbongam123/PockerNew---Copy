using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using LoRClone.Model;

namespace LoRClone.View
{
    public class BenchZoneView : ZoneView
    {
        [Header("Bench Card Size phải portrait (cao > rộng); 0 = giữ nguyên prefab")]
        [SerializeField] float benchPortraitWidth = 0f;
        [SerializeField] float benchPortraitHeight = 0f;

        [Header("Center Layout — căn giữa như LoR gốc")]
        [Tooltip("Bật = bài trên bench xếp CĂN GIỮA: 1 con ở giữa, thêm con thì dãn đều 2 bên.\n"
                 + "Tắt = giữ hành vi cũ (lấp vào slot cố định).")]
        public bool centerLayout = true;
        [Tooltip("Thời gian con cũ TRƯỢT nhường chỗ khi có con mới (giây). 0 = snap tức thì.")]
        public float slideDuration = 0.15f;

        public void Init(PlayerModel playerModel)
        {
            playerModel.OnCardPlayedToBench += (_, c) => OnArrival(c);
            playerModel.OnCardReturnedToBench += (_, c) => OnArrival(c);
            playerModel.OnCardMovedToBattlefield += (_, c) => RemoveAndRecenter(c);
            playerModel.OnCardDied += (_, c) => RemoveAndRecenter(c);
            playerModel.OnCardRecalled += (_, c) => RemoveAndRecenter(c); // Recall: xóa khỏi bench
        }

        void RemoveAndRecenter(CardModel c)
        {
            RemoveCard(c);
            RecenterBench(null);
        }

        void OnArrival(CardModel cardModel)
        {
            // Spawn theo ĐÚNG slotIndex của model (nguồn chân lý) — giống BattlefieldZoneView.
            // Trước đây dùng FindEmptySlot của view → lệch với model → "lúc trái lúc phải".
            var cv = SpawnCardInSlot(cardModel, cardModel.slotIndex);
            if (cv == null) return;

            // Bind model để CardView subscribe OnStatsChanged (HP/ATK cập nhật ngay)
            cv.Bind(cardModel);

            if (benchPortraitWidth > 0f && benchPortraitHeight > 0f)
            {
                cv.OverrideSize(benchPortraitWidth, benchPortraitHeight);
                cv.ForceArtworkUV(benchPortraitWidth, benchPortraitHeight);
            }
            else
            {
                cv.ResetToPortraitSize();
            }

            cv.OnDropped += (view, _) => FireDrop(view);

            // Căn giữa: con MỚI snap tới vị trí giữa (SummonSequence sẽ animate đáp vào đó),
            // con CŨ trượt nhường chỗ.
            RecenterBench(cardModel);
        }

        /// <summary>
        /// Xếp lại toàn bộ bài trên bench vào GIỮA hàng (vị trí liên tục quanh tâm).
        /// justArrived = con vừa tới → snap ngay tới đích (để summon-anim đáp trúng);
        /// các con còn lại trượt tới đích.
        /// </summary>
        void RecenterBench(CardModel justArrived)
        {
            if (!centerLayout || slots == null || slots.Count == 0) return;

            // Thứ tự hiển thị = ĐÚNG thứ tự model (slotIndex tăng dần) → khớp Support/adjacency,
            // trái→phải nhất quán, không nhảy chỗ.
            var ordered = new List<CardView>();
            foreach (var kv in _slotMap)
                if (kv.Value != null && kv.Value.model != null) ordered.Add(kv.Value);
            ordered.Sort((a, b) => a.model.slotIndex.CompareTo(b.model.slotIndex));

            int n = ordered.Count;
            if (n == 0) return;

            // Hàng ngang đều: tâm + spacing lấy từ slots cố định
            float rowCenterX = (slots[0].position.x + slots[slots.Count - 1].position.x) * 0.5f;
            float y = slots[0].position.y;
            float z = slots[0].position.z;
            float spacing = slots.Count > 1
                ? (slots[slots.Count - 1].position.x - slots[0].position.x) / (slots.Count - 1)
                : 0f;

            for (int i = 0; i < n; i++)
            {
                var cv = ordered[i];
                if (cv == null) continue;
                float x = rowCenterX + (i - (n - 1) * 0.5f) * spacing;
                Vector3 target = new Vector3(x, y, z);

                if (cv.model == justArrived || slideDuration <= 0f)
                    cv.transform.position = target;       // snap
                else
                    StartCoroutine(SlideTo(cv, target));  // trượt nhường chỗ
            }
        }

        IEnumerator SlideTo(CardView cv, Vector3 target)
        {
            if (cv == null) yield break;
            Vector3 start = cv.transform.position;
            float e = 0f;
            while (e < slideDuration && cv != null)
            {
                e += Time.deltaTime;
                float t = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(e / slideDuration));
                cv.transform.position = Vector3.Lerp(start, target, t);
                yield return null;
            }
            if (cv != null) cv.transform.position = target;
        }
    }
}