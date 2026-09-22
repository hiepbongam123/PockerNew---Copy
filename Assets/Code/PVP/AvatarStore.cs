using System;
using UnityEngine;

namespace LoRClone.Data
{
    /// <summary>
    /// KHO AVATAR — danh sách ảnh đại diện MẶC ĐỊNH (gán ở LobbyMenuView.avatars) + lựa chọn của người chơi.
    /// Chọn lưu bằng PlayerPrefs (chỉ số). Mọi nơi (sảnh, hồ sơ, bảng xếp hạng, PvP) đọc chung từ đây.
    /// Avatar người KHÁC (bảng xếp hạng) suy tất định từ PlayerId → mỗi người 1 ảnh khác nhau.
    /// </summary>
    public static class AvatarStore
    {
        /// <summary>Bộ ảnh mặc định — set 1 lần từ LobbyMenuView (kéo Sprite vào Inspector).</summary>
        public static Sprite[] Choices;

        const string Key = "avatar_index";
        public static event Action OnChanged;

        public static int SelectedIndex
        {
            get => PlayerPrefs.GetInt(Key, 0);
            set { PlayerPrefs.SetInt(Key, Mathf.Max(0, value)); PlayerPrefs.Save(); OnChanged?.Invoke(); }
        }

        public static int Count => Choices != null ? Choices.Length : 0;
        public static Sprite Selected => ForIndex(SelectedIndex);

        public static Sprite ForIndex(int i)
        {
            if (Choices == null || Choices.Length == 0) return null;
            i = ((i % Choices.Length) + Choices.Length) % Choices.Length;
            return Choices[i];
        }

        /// <summary>Avatar tất định theo PlayerId (cho người khác trên bảng — mỗi id 1 ảnh).</summary>
        public static Sprite ForPlayerId(string id)
        {
            if (Choices == null || Choices.Length == 0) return null;
            if (string.IsNullOrEmpty(id)) return Choices[0];
            int h = 0; foreach (var c in id) h = (h * 31 + c) & 0x7fffffff;
            return Choices[h % Choices.Length];
        }
    }
}
