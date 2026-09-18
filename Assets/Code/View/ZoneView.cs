using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using LoRClone.Model;

namespace LoRClone.View
{
    public abstract class ZoneView : MonoBehaviour, IDropHandler
    {
        public bool isPlayer = true;
        public List<Transform> slots = new List<Transform>();
        public GameObject cardPrefab;

        [Header("Card Size Override (0 = giữ nguyên size prefab)")]
        [Tooltip("Đặt size này TRƯỚC khi Bind() chạy → RefreshArtworkUV đọc đúng chiều ngang/dọc ngay lần đầu.")]
        [SerializeField] float overrideCardWidth = 0f;
        [SerializeField] float overrideCardHeight = 0f;

        protected readonly Dictionary<int, CardView> _slotMap = new Dictionary<int, CardView>();

        public event Action<ZoneView, CardView> OnCardDroppedHere;
        public event Action<ZoneView, CardView, DragIntent> OnCardDragIntent;

        public void OnDrop(PointerEventData eventData) { }

        public int FindEmptySlot()
        {
            for (int i = 0; i < slots.Count; i++)
                if (!_slotMap.ContainsKey(i)) return i;
            return -1;
        }

        public void PlaceCard(CardView cv, int slotIndex)
        {
            if (slotIndex < 0 || slotIndex >= slots.Count) return;
            _slotMap[slotIndex] = cv;
            cv.SnapToSlot(slots[slotIndex]);
        }

        public void RemoveCard(CardModel model)
        {
            foreach (var kv in new Dictionary<int, CardView>(_slotMap))
            {
                if (kv.Value != null && kv.Value.model == model)
                {
                    _slotMap.Remove(kv.Key);
                    Destroy(kv.Value.gameObject);
                    return;
                }
            }
        }

        public CardView GetView(CardModel model)
        {
            foreach (var kv in _slotMap)
                if (kv.Value.model == model) return kv.Value;
            return null;
        }

        /// <summary>Trả về slot index chứa model, -1 nếu không tìm thấy.</summary>
        public int GetSlotOf(CardModel model)
        {
            foreach (var kv in _slotMap)
                if (kv.Value != null && kv.Value.model == model) return kv.Key;
            return -1;
        }

        public IEnumerable<CardView> AllViews() => _slotMap.Values;

        public void ClearAll()
        {
            foreach (var kv in _slotMap)
                if (kv.Value != null) Destroy(kv.Value.gameObject);
            _slotMap.Clear();
        }

        protected CardView SpawnCardInSlot(CardModel cardModel, int preferredSlot = -1)
        {
            if (cardPrefab == null)
            {
                Debug.LogError($"[{GetType().Name}] cardPrefab chưa gán!");
                return null;
            }
            // Dùng preferredSlot nếu hợp lệ và còn trống, không thì lấy slot trống đầu tiên
            int slot = (preferredSlot >= 0 && preferredSlot < slots.Count && !_slotMap.ContainsKey(preferredSlot))
                       ? preferredSlot
                       : FindEmptySlot();
            if (slot < 0)
            {
                Debug.LogWarning($"[{GetType().Name}] Không còn slot trống!");
                return null;
            }

            var go = Instantiate(cardPrefab, slots[slot]);
            var cv = go.GetComponent<CardView>();
            if (cv == null)
            {
                Debug.LogError($"[{GetType().Name}] cardPrefab thiếu CardView!");
                Destroy(go);
                return null;
            }

            if (overrideCardWidth > 0f || overrideCardHeight > 0f)
                cv.OverrideSize(overrideCardWidth, overrideCardHeight);

            cv.Bind(cardModel);

            if (overrideCardWidth > 0f || overrideCardHeight > 0f)
                cv.ForceArtworkUV(overrideCardWidth, overrideCardHeight);

            cv.SaveCurrentPosition();
            cv.OnDragIntent += (view, intent) => FireDragIntent(view, intent);
            _slotMap[slot] = cv;
            return cv;
        }

        protected void FireDrop(CardView cv) => OnCardDroppedHere?.Invoke(this, cv);
        protected void FireDragIntent(CardView cv, DragIntent intent)
                                                     => OnCardDragIntent?.Invoke(this, cv, intent);
    }
}