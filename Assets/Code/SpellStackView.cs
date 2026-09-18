using System.Collections.Generic;
using UnityEngine;
using TMPro;
using LoRClone.Model;

namespace LoRClone.View
{
    /// <summary>
    /// Hiển thị danh sách spell/effect đang chờ trong SpellStack.
    /// Tự cập nhật khi stack thay đổi.
    /// </summary>
    public class SpellStackView : MonoBehaviour
    {
        [Header("References")]
        public Transform entriesContainer;   // parent của các entry item
        public GameObject entryPrefab;       // prefab có TextMeshProUGUI
        public GameObject panel;             // ẩn panel khi stack rỗng

        private readonly List<GameObject> _entries = new();
        private SpellStack _spellStack;

        // ── Init ──────────────────────────────────────────────────

        public void Init(SpellStack spellStack)
        {
            _spellStack = spellStack;
            _spellStack.OnStackChanged += Refresh;
            Refresh();
        }

        // ── Render ────────────────────────────────────────────────

        private void Refresh()
        {
            // Xóa entries cũ
            foreach (var go in _entries) Destroy(go);
            _entries.Clear();

            bool hasEntries = !_spellStack.IsEmpty;
            if (panel) panel.SetActive(hasEntries);

            if (!hasEntries) return;

            // Rebuild từ stack (top → bottom)
            foreach (var entry in _spellStack.PeekAll())
            {
                var go = Instantiate(entryPrefab, entriesContainer);
                var text = go.GetComponentInChildren<TextMeshProUGUI>();
                if (text) text.text = $"▶ {entry.description}";
                _entries.Add(go);
            }
        }

        private void OnDestroy()
        {
            if (_spellStack != null)
                _spellStack.OnStackChanged -= Refresh;
        }
    }
}
