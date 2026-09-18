using System.Collections.Generic;
using UnityEngine;

namespace LoRClone.Data
{
    /// <summary>
    /// BỘ ÂM THANH RIÊNG CỦA LÁ BÀI (voice + SFX) — mang bản sắc từng card.
    /// Gán vào CardData.audioData. Phát qua KÊNH CARD riêng của AudioManager
    /// (AudioManager.PlayCard) → KHÔNG lẫn/không bị SFX giao diện–gameplay lấn.
    ///
    /// Mỗi sự kiện là 1 DANH SÁCH clip (vài câu thoại) → tự bốc ngẫu nhiên, không lặp câu vừa nói.
    /// Để trống sự kiện nào = card không nói gì ở sự kiện đó.
    ///
    /// PHÁT: cardModel.data.audioData?.Play(CardAudioData.Ev.Attack);
    ///
    /// Khác với AudioManager: AudioManager lo âm thanh CHUNG (UI, gameplay, keyword);
    /// CardAudioData lo THOẠI/tiếng riêng của từng lá bài.
    /// </summary>
    [CreateAssetMenu(menuName = "LoRClone/Card Audio Data", fileName = "New CardAudio")]
    public class CardAudioData : ScriptableObject
    {
        /// <summary>Mọi sự kiện âm thanh của card. Thêm mới ở đây + SetOf() là xong.</summary>
        public enum Ev
        {
            Summon,          // ra sân (triệu hồi)
            Attack,          // tấn công
            TakeDamage,      // bị đánh (không tính Barrier hấp thụ)
            Die,             // bị tiêu diệt
            KillEnemy,       // giết được kẻ địch
            Buff,            // được buff / tăng sức mạnh
            LevelUp,         // thăng cấp (champion)
            SpellCast,       // bắt đầu dùng skill/spell
            SpellResolve,    // skill/spell phát huy tác dụng
            Interact,        // người chơi tương tác (click/giữ card)
            Idle,            // than vãn khi chờ lâu / bị bỏ quên
            EncounterEnemy,  // gặp đối thủ (đầu trận)
            Nemesis,         // gặp KHẮC TINH cốt truyện
            Victory,         // thắng trận
            Defeat           // thua trận
        }

        /// <summary>1 sự kiện = nhiều câu thoại. Bốc ngẫu nhiên, tránh lặp câu vừa phát.</summary>
        [System.Serializable]
        public class VoiceSet
        {
            [Tooltip("Danh sách clip cho sự kiện này. Nhiều clip = bốc ngẫu nhiên mỗi lần.")]
            public List<AudioClip> clips = new List<AudioClip>();

            [Tooltip("Âm lượng riêng sự kiện (nhân với Master Volume của card).")]
            [Range(0f, 1f)] public float volume = 1f;

            [Tooltip("Không phát lại đúng câu vừa nói (khi có ≥2 clip).")]
            public bool noRepeat = true;

            [System.NonSerialized] int _last = -1;

            public bool Has => clips != null && clips.Count > 0;

            public AudioClip Pick()
            {
                if (!Has) return null;
                if (clips.Count == 1) return clips[0];
                int i;
                do { i = Random.Range(0, clips.Count); }
                while (noRepeat && i == _last);
                _last = i;
                return clips[i];
            }
        }

        [Header("Unit — vòng đời")]
        public VoiceSet summon = new VoiceSet();
        public VoiceSet attack = new VoiceSet();
        public VoiceSet takeDamage = new VoiceSet();
        public VoiceSet die = new VoiceSet();
        public VoiceSet killEnemy = new VoiceSet();

        [Header("Buff / Level")]
        public VoiceSet buff = new VoiceSet();
        public VoiceSet levelUp = new VoiceSet();

        [Header("Spell / Skill")]
        public VoiceSet spellCast = new VoiceSet();
        public VoiceSet spellResolve = new VoiceSet();

        [Header("Tương tác / Thoại chờ")]
        public VoiceSet interact = new VoiceSet();
        public VoiceSet idle = new VoiceSet();

        [Header("Cốt truyện / Đối đầu")]
        public VoiceSet encounterEnemy = new VoiceSet();
        public VoiceSet nemesis = new VoiceSet();

        [Header("Kết trận")]
        public VoiceSet victory = new VoiceSet();
        public VoiceSet defeat = new VoiceSet();

        [Header("Chung")]
        [Tooltip("Âm lượng tổng của card này (nhân lên mọi sự kiện).")]
        [Range(0f, 1f)] public float masterVolume = 1f;

        [Tooltip("Khoảng cách tối thiểu giữa 2 lần card này lên tiếng (giây) — chống chồng thoại.")]
        [Min(0f)] public float minGap = 0.12f;

        [Tooltip("Trễ (giây) của VOICE so với âm thanh CHUNG (AudioManager). Voice phủ lên sau clip chung.\n" +
                 "0 = phát cùng lúc. Gợi ý 0.1–0.3 để nghe rõ 'ra đòn xong mới thoại'.")]
        [Min(0f)] public float voiceDelay = 0.15f;

        [System.NonSerialized] float _lastTime = -999f;

        VoiceSet SetOf(Ev e)
        {
            switch (e)
            {
                case Ev.Summon: return summon;
                case Ev.Attack: return attack;
                case Ev.TakeDamage: return takeDamage;
                case Ev.Die: return die;
                case Ev.KillEnemy: return killEnemy;
                case Ev.Buff: return buff;
                case Ev.LevelUp: return levelUp;
                case Ev.SpellCast: return spellCast;
                case Ev.SpellResolve: return spellResolve;
                case Ev.Interact: return interact;
                case Ev.Idle: return idle;
                case Ev.EncounterEnemy: return encounterEnemy;
                case Ev.Nemesis: return nemesis;
                case Ev.Victory: return victory;
                case Ev.Defeat: return defeat;
                default: return null;
            }
        }

        public bool Has(Ev e) { var s = SetOf(e); return s != null && s.Has; }

        /// <summary>Phát 1 câu thoại/SFX cho sự kiện (kênh Card riêng). volMul: nhân thêm tại call-site.</summary>
        public void Play(Ev e, float volMul = 1f)
        {
            var set = SetOf(e);
            if (set == null || !set.Has) return;

            // Die/Victory/Defeat là thoại "cuối" → cho phép bỏ qua minGap để chắc chắn phát.
            bool important = e == Ev.Die || e == Ev.Victory || e == Ev.Defeat;
            if (!important && minGap > 0f && Time.unscaledTime - _lastTime < minGap) return;

            var clip = set.Pick();
            if (clip == null) return;
            _lastTime = Time.unscaledTime;

            float vol = Mathf.Clamp01(masterVolume) * Mathf.Clamp01(set.volume) * Mathf.Clamp01(volMul);
            AudioManager.PlayCard(clip, vol);
        }
    }
}