using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using TMPro;
using LoRClone.Model;
using LoRClone.Controller;

namespace LoRClone.View
{
    public class MulliganView : MonoBehaviour
    {
        [Header("References")]
        public GameObject mulliganPanel;
        public Transform cardContainer;
        public GameObject cardPrefab;
        public Button confirmButton;
        public TextMeshProUGUI instructionText;
        public TextMeshProUGUI sideLabel;

        [Header("Selection Visual")]
        public Color selectedOverlayColor = new Color(1f, 0.3f, 0.3f, 0.45f);
        public Color btnSelectedColor = new Color(0.9f, 0.3f, 0.3f, 1f);
        public Color btnDefaultColor = new Color(0.25f, 0.25f, 0.25f, 1f);

        [Header("Replace Button Prefab (để trống sẽ tự tạo)")]
        public GameObject replaceButtonPrefab;

        // ── State ─────────────────────────────────────────────────
        bool _isPlayerTurn = true;
        readonly HashSet<CardModel> _selected = new HashSet<CardModel>();
        readonly List<(CardModel card, GameObject wrapper, Image overlay, Button replaceBtn)> _slots
            = new List<(CardModel, GameObject, Image, Button)>();

        // ── Lifecycle ─────────────────────────────────────────────

        void Start()
        {
            confirmButton?.onClick.AddListener(OnConfirm);
            if (mulliganPanel) mulliganPanel.SetActive(false);

            var gc = GameController.Instance;
            if (gc == null) return;
            gc.OnStateChanged += OnStateChanged;
            // GameController.Start() có thể đã fire Notify() trước → check phase ngay
            if (gc.model != null) OnStateChanged(gc.model);
        }

        void OnDestroy()
        {
            if (GameController.Instance)
                GameController.Instance.OnStateChanged -= OnStateChanged;
        }

        // ── State sync ────────────────────────────────────────────

        bool _netConfirmed;   // PvP: đã confirm mulligan → khóa, đợi đối thủ

        void OnStateChanged(GameModel m)
        {
            bool isMulligan = m.phase == GamePhase.Mulligan;
            if (mulliganPanel) mulliganPanel.SetActive(isMulligan);

            if (!isMulligan) { _netConfirmed = false; return; } // vào trận → reset cho ván sau

            if (GameController.Instance.networkMode)
            {
                // PvP: đã confirm rồi thì KHÓA — không dựng lại, giữ nguyên lựa chọn, đợi đối thủ.
                if (_netConfirmed) return;
                bool mePlayer = GameController.NetLocalIsPlayer;
                _isPlayerTurn = mePlayer;
                ShowSide(mePlayer ? m.player.hand : m.enemy.hand, isPlayer: mePlayer);
            }
            else
            {
                _isPlayerTurn = true;
                ShowSide(m.player.hand, isPlayer: true);
            }
        }

        // ── Build card slots ──────────────────────────────────────

        void ShowSide(List<CardModel> hand, bool isPlayer)
        {
            foreach (var (_, wrapper, _, _) in _slots)
                if (wrapper) Destroy(wrapper);
            _slots.Clear();
            _selected.Clear();

            if (sideLabel)
                sideLabel.text = isPlayer ? "Lượt Player — Chọn lá muốn đổi" : "Lượt Enemy — Chọn lá muốn đổi";

            if (cardContainer == null || cardPrefab == null)
            {
                UpdateInstruction();
                return;
            }

            foreach (var card in hand)
            {
                // Wrapper dọc: card trên, nút Đổi dưới
                var wrapper = new GameObject("CardSlot", typeof(RectTransform));
                wrapper.transform.SetParent(cardContainer, false);

                var vl = wrapper.AddComponent<VerticalLayoutGroup>();
                vl.childAlignment = TextAnchor.UpperCenter;
                vl.childForceExpandWidth = false;
                vl.childForceExpandHeight = false;
                vl.childControlWidth = false;
                vl.childControlHeight = false;
                vl.spacing = 8f;

                // ContentSizeFitter: wrapper tự giãn để chứa card + nút
                var csf = wrapper.AddComponent<ContentSizeFitter>();
                csf.horizontalFit = ContentSizeFitter.FitMode.PreferredSize;
                csf.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

                // Card
                var go = Instantiate(cardPrefab, wrapper.transform);
                var cv = go.GetComponent<CardView>();
                if (cv != null) cv.Bind(card);

                // Overlay đỏ phủ lên card
                var overlayGo = new GameObject("Overlay", typeof(Image));
                overlayGo.transform.SetParent(go.transform, false);
                var ort = overlayGo.GetComponent<RectTransform>();
                ort.anchorMin = Vector2.zero;
                ort.anchorMax = Vector2.one;
                ort.sizeDelta = Vector2.zero;
                var overlay = overlayGo.GetComponent<Image>();
                overlay.color = Color.clear;

                // Nút Đổi
                GameObject btnGo = replaceButtonPrefab != null
                    ? Instantiate(replaceButtonPrefab, wrapper.transform)
                    : CreateDefaultReplaceButton(wrapper.transform);

                var replaceBtn = btnGo.GetComponent<Button>();
                var btnImg = btnGo.GetComponent<Image>();
                var btnText = btnGo.GetComponentInChildren<TextMeshProUGUI>();
                if (btnImg) btnImg.color = btnDefaultColor;
                if (btnText) btnText.text = "Đổi";

                var capturedCard = card;
                var capturedOverlay = overlay;
                var capturedBtnImg = btnImg;
                var capturedBtnText = btnText;
                replaceBtn.onClick.AddListener(() =>
                    ToggleSelect(capturedCard, capturedOverlay, capturedBtnImg, capturedBtnText));

                _slots.Add((card, wrapper, overlay, replaceBtn));
            }

            if (confirmButton) confirmButton.interactable = true;
            UpdateInstruction();

            // Force rebuild để layout tính đúng ngay frame này
            UnityEngine.UI.LayoutRebuilder.ForceRebuildLayoutImmediate(
                cardContainer as RectTransform);
        }

        // ── Toggle ────────────────────────────────────────────────

        void ToggleSelect(CardModel card, Image overlay, Image btnImg, TextMeshProUGUI btnText)
        {
            if (_selected.Contains(card))
            {
                _selected.Remove(card);
                overlay.color = Color.clear;
                if (btnImg) btnImg.color = btnDefaultColor;
                if (btnText) btnText.text = "Đổi";
            }
            else
            {
                _selected.Add(card);
                overlay.color = selectedOverlayColor;
                if (btnImg) btnImg.color = btnSelectedColor;
                if (btnText) btnText.text = "Giữ";
            }
            UpdateInstruction();
        }

        void UpdateInstruction()
        {
            if (instructionText == null) return;
            instructionText.text = _selected.Count == 0
                ? "Nhấn Confirm để giữ hết."
                : $"Đổi {_selected.Count} lá. Nhấn Confirm để xác nhận.";
        }

        // ── Default button (khi không có prefab) ─────────────────

        GameObject CreateDefaultReplaceButton(Transform parent)
        {
            var go = new GameObject("ReplaceBtn", typeof(RectTransform), typeof(Image), typeof(Button));
            go.transform.SetParent(parent, false);

            var rt = go.GetComponent<RectTransform>();
            rt.sizeDelta = new Vector2(90f, 34f);

            // LayoutElement để VerticalLayoutGroup nhận ra kích thước
            var le = go.AddComponent<LayoutElement>();
            le.preferredWidth = 90f;
            le.preferredHeight = 34f;

            var textGo = new GameObject("Text", typeof(RectTransform), typeof(TextMeshProUGUI));
            textGo.transform.SetParent(go.transform, false);
            var trt = textGo.GetComponent<RectTransform>();
            trt.anchorMin = Vector2.zero;
            trt.anchorMax = Vector2.one;
            trt.sizeDelta = Vector2.zero;
            var tmp = textGo.GetComponent<TextMeshProUGUI>();
            tmp.text = "Đổi";
            tmp.alignment = TextAlignmentOptions.Center;
            tmp.fontSize = 14f;

            return go;
        }

        // ── Confirm ───────────────────────────────────────────────

        void OnConfirm()
        {
            if (confirmButton) confirmButton.interactable = false;

            var toSwap = new List<CardModel>(_selected);

            // PvP: chỉ mulligan phe MÌNH + gửi qua mạng. Phe địch do máy kia gửi tới (RpcMulligan).
            if (GameController.Instance.networkMode)
            {
                _netConfirmed = true; // khóa: OnStateChanged sẽ không dựng lại mulligan nữa
                GameController.Instance.SubmitLocalMulligan(toSwap, GameController.NetLocalIsPlayer);
                return; // KHÔNG tự mulligan hộ enemy; giữ panel khóa, đợi đối thủ confirm
            }

            // PvE: giữ nguyên — player rồi enemy mulligan tuần tự trên cùng máy.
            GameController.Instance.HandleMulliganConfirm(_isPlayerTurn, toSwap);
            if (_isPlayerTurn)
            {
                _isPlayerTurn = false;
                var m = GameController.Instance.model;
                if (m.phase == GamePhase.Mulligan)
                    ShowSide(m.enemy.hand, isPlayer: false);
            }
        }
    }
}