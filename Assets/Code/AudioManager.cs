using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;   // đổi nhạc theo scene
using LoRClone.Data;   // CardAudioData.Ev

namespace LoRClone
{
    /// <summary>
    /// Singleton quản lý âm thanh — 3 KÊNH RIÊNG:
    ///   • Music  (_musicSource)  : nhạc nền, loop/playlist, fade, volume riêng.
    ///   • SFX    (sfxSource)     : global + keyword SFX (giữ nguyên như cũ).
    ///   • Card   (_cardSource)   : âm thanh của lá bài (summon/attack/die/voice) — KÊNH RIÊNG,
    ///                              KHÔNG anti-stacking, volume riêng → không bị SFX combat lấn.
    ///
    /// FIX "card audio bị đè": trước đây mọi thứ dùng CHUNG 1 source + anti-stacking nên tiếng lá
    /// bị SFX combat/keyword lấn và bị hạ volume khi lặp. Nay card đi kênh riêng.
    ///
    /// SETUP:
    ///   1. GameObject "AudioManager" + component này (AudioSource cho SFX tự thêm).
    ///   2. Gán các sfx như cũ. Muốn có nhạc nền: kéo track vào "Gameplay Music", để playMusicOnStart = ✔.
    ///   3. Music/Card source được TẠO TỰ ĐỘNG trong Awake — không cần thêm tay.
    /// </summary>
    [RequireComponent(typeof(AudioSource))]
    public class AudioManager : MonoBehaviour
    {
        public static AudioManager Instance { get; private set; }

        /// <summary>TRUE khi có nhạc thật đang phát (playlist/track). BgmPlayer nên đọc cờ này
        /// MỖI vòng lặp và tự dừng nhạc tự sinh khi = true (tránh phát chồng).</summary>
        public static bool MusicActive { get; private set; }

        // ── SFX (giữ nguyên) ──────────────────────────────────────────────
        [Header("SFX Source")]
        [SerializeField] AudioSource sfxSource;

        [Header("Volume")]
        [Range(0f, 1f)][SerializeField] float masterSfxVolume = 1f;

        [Header("Anti-Stacking (chỉ áp cho SFX, KHÔNG áp cho Card)")]
        [Range(0f, 0.5f)][SerializeField] float sameClipCooldown = 0.1f;

        [Header("Debug")]
        [Tooltip("Bật = in log mỗi lần phát (dễ spam Console / có thể trigger assertion Unity 6). Mặc định tắt.")]
        [SerializeField] bool verboseLog = false;
        [SerializeField] AudioClip testClip;

        [Header("Global Combat SFX")]
        public AudioClip sfxConfirmAttack;
        public AudioClip sfxCombatClash;

        [Header("Keyword SFX — Bảo Hộ (Barrier)")] public AudioClip sfxKwBarrier;
        [Header("Keyword SFX — Săn Bắn (QuickAttack)")] public AudioClip sfxKwQuickAttack;
        [Header("Keyword SFX — Hủy Diệt (Overwhelm)")] public AudioClip sfxKwOverwhelmAttack;
        public AudioClip sfxKwOverwhelmHit;
        [Header("Keyword SFX — Trù Phú (Lifesteal)")] public AudioClip sfxKwLifestealHeal;
        public AudioClip sfxKwLifestealPass;
        [Header("Keyword SFX — Tri Thức (Tough)")] public AudioClip sfxKwTough;
        [Header("Keyword SFX — Thần Bí (Elusive)")] public AudioClip sfxKwElusive;
        [Header("Keyword SFX — Hòa Hợp (SpellShield)")] public AudioClip sfxKwSpellShield;
        [Header("Keyword SFX — Vui Vẻ (CantBlock)")] public AudioClip sfxKwCantBlockAttack;
        public AudioClip sfxKwCantBlockHit;
        [Header("Keyword SFX — Cảm Tử (Ephemeral)")] public AudioClip sfxKwEphemeral;
        [Header("Keyword SFX — Hư Vô (Fearsome)")] public AudioClip sfxKwFearsome;

        [Header("Global Game SFX")]
        public AudioClip sfxRoundStart;
        public AudioClip sfxEndTurn;
        public AudioClip sfxDrawCard;
        public AudioClip sfxGlobalLevelUp;
        public AudioClip sfxSpellCast;

        // ── MUSIC / BGM (mới) ─────────────────────────────────────────────
        [Header("Music / Nhạc nền")]
        [Tooltip("Danh sách track nhạc nền gameplay. Nhiều track = tự phát nối tiếp (playlist).")]
        public List<AudioClip> gameplayMusic = new List<AudioClip>();
        [Tooltip("Tự phát nhạc nền khi vào scene.")]
        public bool playMusicOnStart = true;
        [Tooltip("Xáo trộn thứ tự playlist.")]
        public bool shuffleMusic = true;
        [Range(0f, 1f)] public float musicVolume = 0.5f;
        [Tooltip("Thời gian fade in/out nhạc (giây).")]
        [Min(0f)] public float musicFadeSeconds = 1.0f;
        [Tooltip("CHỜ bao nhiêu giây rồi mới bắt đầu phát nhạc nền khi vào scene (0 = phát ngay).")]
        [Min(0f)] public float musicStartDelaySeconds = 10f;

        [Tooltip("PLAYLIST THEO NGỮ CẢNH — mỗi set 1 tên (Menu/Map/Battle/Boss...) + nhiều bài.\n" +
                 "Đổi nhạc bằng code: AudioManager.Instance.PlaySet(\"Battle\").")]
        public List<MusicSet> musicSets = new List<MusicSet>();
        [Tooltip("Tên set tự phát khi vào scene. Trống = phát Gameplay Music mặc định.")]
        public string startSetName = "";

        // ── Persistent + đổi set theo scene (liên tục hoá nhạc) ──────────
        [Header("Nhạc liên tục qua scene")]
        [Tooltip("⚠ MẶC ĐỊNH TẮT. Bật = AudioManager sống xuyên scene (DontDestroyOnLoad).\n" +
                 "CẢNH BÁO: AudioManager giữ CẢ SFX gameplay. Nếu bật mà bản lobby sống trước, nó ĐÈ bản\n" +
                 "gameplay → mất hết SFX thật (thành procedural) + nhạc gameplay bị lobby thay. CHỈ bật khi\n" +
                 "bạn cố ý dùng 1 AudioManager DUY NHẤT mang đủ mọi SFX + cả 2 set nhạc cho mọi scene.\n" +
                 "Muốn nhạc chung liền mạch mà KHÔNG đụng SFX gameplay → để TẮT và dùng lớp nhạc riêng (hỏi tôi).")]
        public bool persistAcrossScenes = false;
        [Tooltip("Tự chọn Music Set theo TÊN scene: chứa 'lobby/menu...' → Lobby Set; 'game/battle...' → Battle Set.\n" +
                 "Tắt nếu bạn muốn tự gọi PlaySet bằng code.")]
        public bool autoSwitchSetByScene = true;
        [Tooltip("Tên set cho scene LOBBY/MENU.")]
        public string lobbySetName = "Lobby";
        [Tooltip("Tên set cho scene GAMEPLAY/BATTLE.")]
        public string battleSetName = "Battle";
        [Tooltip("Nhớ vị trí phát của từng set → quay lại set đó phát TIẾP chỗ cũ (nhạc lobby liền mạch qua nhiều lần về).")]
        public bool resumeMusicPosition = true;

        static readonly string[] _menuHints = { "lobby", "menu", "title", "home", "start" };
        static readonly string[] _gameHints = { "game", "battle", "play", "match", "combat", "board" };
        readonly Dictionary<string, (AudioClip clip, float time)> _resumeBySet =
            new Dictionary<string, (AudioClip, float)>();
        AudioClip _resumeStartClip;   // clip cần phát-tiếp ở đầu playlist (1 lần)
        float _resumeStartTime;

        /// <summary>True nếu đã gán track nhạc thật → BgmPlayer sẽ nhường, không phát nhạc tự sinh.</summary>
        public bool HasGameplayMusic =>
            (gameplayMusic != null && gameplayMusic.Count > 0)
            || (musicSets != null && musicSets.Exists(s => s != null && s.tracks != null && s.tracks.Count > 0));

        // ── CARD / VOICE (mới) ────────────────────────────────────────────
        [Header("Card / Voice channel")]
        [Range(0f, 1f)] public float cardVolume = 1f;

        // ── CARD COMMON SFX — MẶC ĐỊNH cho MỌI lá (kênh Card) ─────────────
        [Header("Card Common SFX — dùng chung cho MỌI lá bài")]
        [Tooltip("Âm thanh mặc định phát cho MỌI lá ở mỗi sự kiện (không cần CardAudioData riêng).\n" +
                 "Lá có CardAudioData sẽ phát THÊM voice (có delay) chồng lên các clip này.\n" +
                 "Field trống + autoGenerateMissingCardSfx = tự sinh bằng ProceduralAudio → không bao giờ im.")]
        public AudioClip cardSummon;
        public AudioClip cardAttack;
        public AudioClip cardTakeDamage;
        public AudioClip cardDie;
        public AudioClip cardSpellCast;
        public AudioClip cardSpellResolve;
        public AudioClip cardLevelUp;
        [Tooltip("KillEnemy KHÔNG tự sinh (để trống = không có common khi giết địch, chỉ voice nếu lá có).")]
        public AudioClip cardKillEnemy;
        [Range(0f, 1f)] public float commonCardVolume = 1f;
        [Tooltip("Tự sinh Card Common SFX còn trống bằng ProceduralAudio (trừ KillEnemy). Gán clip thật thì clip thật ưu tiên.")]
        public bool autoGenerateMissingCardSfx = true;

        // ── AUTO SFX (mới) ────────────────────────────────────────────────
        [Header("Auto SFX")]
        [Tooltip("Field SFX nào để TRỐNG sẽ được tự sinh bằng ProceduralAudio (không cần gán file). Gán clip thật thì clip thật ưu tiên.")]
        public bool autoGenerateMissingSfx = true;

        // ── Private ───────────────────────────────────────────────────────
        AudioSource _musicSource;
        AudioSource _cardSource;
        readonly Dictionary<AudioClip, float> _lastPlayedTime = new Dictionary<AudioClip, float>();
        const float AntiStackMinVolume = 0.3f;

        bool _playlistActive;
        int _playlistIndex = -1;
        Coroutine _musicFadeCo;
        Coroutine _playlistCo;
        List<AudioClip> _currentTracks;   // playlist đang phát (gameplayMusic hoặc 1 set)
        string _currentSetName = "";

        // ─────────────────────────────────────────────────────────────────
        void Awake()
        {
            if (Instance != null && Instance != this)
            {
                // Ưu tiên giữ bản CÓ nhạc thật. Nếu bản cũ rỗng (thường là bootstrap kèm BgmPlayer
                // nhạc tự sinh) mà bản mới có nhạc → bản mới soán ngôi, hủy bản cũ (tắt luôn nhạc code).
                if (this.HasGameplayMusic && !Instance.HasGameplayMusic)
                {
                    Debug.LogWarning("[AudioManager] Bản cũ không có nhạc — thay bằng bản CÓ nhạc.");
                    Destroy(Instance.gameObject);
                    Instance = this;
                }
                else
                {
                    Debug.LogWarning("[AudioManager] Instance trùng — hủy cái mới.");
                    Destroy(gameObject);
                    return;
                }
            }
            else Instance = this;

            // CHỈ khi bật persistent mới sống xuyên scene + nghe sự kiện đổi scene.
            // TẮT (mặc định) → AudioManager per-scene, độc lập hoàn toàn, KHÔNG đụng scene khác.
            if (persistAcrossScenes)
            {
                DontDestroyOnLoad(gameObject);
                SceneManager.sceneLoaded -= OnSceneLoadedAudio;
                SceneManager.sceneLoaded += OnSceneLoadedAudio;
            }

            if (sfxSource == null) sfxSource = GetComponent<AudioSource>();
            if (sfxSource == null) sfxSource = gameObject.AddComponent<AudioSource>();
            Config2D(sfxSource, loop: false);

            // Tạo 2 source phụ tự động (Unity cho phép nhiều AudioSource trên 1 GameObject).
            _musicSource = gameObject.AddComponent<AudioSource>();
            Config2D(_musicSource, loop: true);
            _musicSource.volume = musicVolume;

            _cardSource = gameObject.AddComponent<AudioSource>();
            Config2D(_cardSource, loop: false);

            if (autoGenerateMissingSfx) AutoFillSfx();
            if (autoGenerateMissingCardSfx) AutoFillCardSfx();
        }

        // Lấp Card Common SFX còn trống bằng ProceduralAudio (KillEnemy để trống). Clip gán tay được giữ.
        void AutoFillCardSfx()
        {
            cardSummon = Fb(cardSummon, "soft");
            cardAttack = Fb(cardAttack, "attack");
            cardTakeDamage = Fb(cardTakeDamage, "hit");
            cardDie = Fb(cardDie, "death");
            cardSpellCast = Fb(cardSpellCast, "spellCast");
            cardSpellResolve = Fb(cardSpellResolve, "shimmer");
            cardLevelUp = Fb(cardLevelUp, "levelUp");
            // cardKillEnemy: KHÔNG autofill.
        }

        // Lấp mọi field SFX còn trống bằng clip tự sinh (ProceduralAudio). Clip đã gán tay được giữ nguyên.
        void AutoFillSfx()
        {
            sfxConfirmAttack = Fb(sfxConfirmAttack, "confirm");
            sfxCombatClash = Fb(sfxCombatClash, "clash");
            sfxKwBarrier = Fb(sfxKwBarrier, "shield");
            sfxKwQuickAttack = Fb(sfxKwQuickAttack, "swift");
            sfxKwOverwhelmAttack = Fb(sfxKwOverwhelmAttack, "attack");
            sfxKwOverwhelmHit = Fb(sfxKwOverwhelmHit, "hit");
            sfxKwLifestealHeal = Fb(sfxKwLifestealHeal, "heal");
            sfxKwLifestealPass = Fb(sfxKwLifestealPass, "soft");
            sfxKwTough = Fb(sfxKwTough, "thud");
            sfxKwElusive = Fb(sfxKwElusive, "shimmer");
            sfxKwSpellShield = Fb(sfxKwSpellShield, "shield");
            sfxKwCantBlockAttack = Fb(sfxKwCantBlockAttack, "swift");
            sfxKwCantBlockHit = Fb(sfxKwCantBlockHit, "hit");
            sfxKwEphemeral = Fb(sfxKwEphemeral, "death");
            sfxKwFearsome = Fb(sfxKwFearsome, "lowbrass");
            sfxRoundStart = Fb(sfxRoundStart, "roundStart");
            sfxEndTurn = Fb(sfxEndTurn, "endTurn");
            sfxDrawCard = Fb(sfxDrawCard, "draw");
            sfxGlobalLevelUp = Fb(sfxGlobalLevelUp, "levelUp");
            sfxSpellCast = Fb(sfxSpellCast, "spellCast");
        }

        static AudioClip Fb(AudioClip assigned, string key)
            => assigned != null ? assigned : ProceduralAudio.Get(key);

        void Start()
        {
            if (!playMusicOnStart) return;
            if (musicStartDelaySeconds > 0f)
                StartCoroutine(StartMusicAfterDelay());
            else
                ApplySceneMusic(SceneManager.GetActiveScene().name);
        }

        IEnumerator StartMusicAfterDelay()
        {
            yield return new WaitForSecondsRealtime(musicStartDelaySeconds);
            ApplySceneMusic(SceneManager.GetActiveScene().name);
        }

        void OnDestroy()
        {
            SceneManager.sceneLoaded -= OnSceneLoadedAudio;
        }

        // Mỗi lần đổi scene: chọn set theo tên scene (dedup trong PlaySet → không restart nếu cùng set).
        void OnSceneLoadedAudio(Scene s, LoadSceneMode m)
        {
            if (Instance != this) return;   // chỉ bản đang sống mới xử lý
            if (!playMusicOnStart) return;
            ApplySceneMusic(s.name);
        }

        void ApplySceneMusic(string sceneName)
        {
            // Tự chọn set theo scene (nếu có music sets). PlaySet tự bỏ qua khi đang phát đúng set đó.
            if (autoSwitchSetByScene && musicSets != null && musicSets.Count > 0)
            {
                string setName = SetForScene(sceneName);
                if (FindSet(setName) != null) { PlaySet(setName); return; }
            }
            // Fallback: set khởi đầu gán tay, hoặc gameplayMusic mặc định.
            if (!string.IsNullOrEmpty(startSetName) && FindSet(startSetName) != null)
                PlaySet(startSetName);
            else if (!_playlistActive && gameplayMusic != null && gameplayMusic.Count > 0)
                PlayGameplayPlaylist();
        }

        string SetForScene(string sceneName)
        {
            string n = (sceneName ?? "").ToLowerInvariant();
            // LOBBY/MENU ưu tiên TRƯỚC: tên như "gamelobby" chứa cả "game" lẫn "lobby" → phải ra Lobby.
            foreach (var mn in _menuHints) if (n.Contains(mn)) return lobbySetName;
            foreach (var g in _gameHints) if (n.Contains(g)) return battleSetName;
            return lobbySetName;   // mặc định êm
        }

        MusicSet FindSet(string name)
        {
            if (musicSets == null || string.IsNullOrEmpty(name)) return null;
            foreach (var s in musicSets)
                if (s != null && s.tracks != null && s.tracks.Count > 0
                    && string.Equals(s.name, name, System.StringComparison.OrdinalIgnoreCase)) return s;
            return null;
        }

        static void Config2D(AudioSource s, bool loop)
        {
            s.playOnAwake = false;
            s.loop = loop;
            s.spatialBlend = 0f;
            s.volume = 1f;
            s.mute = false;
        }

        // ══════════════ SFX (giữ nguyên API cũ) ══════════════
        public static void Play(AudioClip clip, float volumeMultiplier = 1f)
        {
            if (Instance == null) { Debug.LogError("[AudioManager] Instance NULL — thêm GameObject AudioManager vào scene."); return; }
            Instance.PlaySFX(clip, volumeMultiplier);
        }

        public static void PlayKwBarrierStatic() => Play(Instance != null ? Instance.sfxKwBarrier : null);
        public static void PlayKwQuickAttackStatic() => Play(Instance != null ? Instance.sfxKwQuickAttack : null);
        public static void PlayKwOverwhelmAtkStatic() => Play(Instance != null ? Instance.sfxKwOverwhelmAttack : null);
        public static void PlayKwOverwhelmHitStatic() => Play(Instance != null ? Instance.sfxKwOverwhelmHit : null);
        public static void PlayKwLifestealHealStatic() => Play(Instance != null ? Instance.sfxKwLifestealHeal : null);
        public static void PlayKwLifestealPassStatic() => Play(Instance != null ? Instance.sfxKwLifestealPass : null);
        public static void PlayKwToughStatic() => Play(Instance != null ? Instance.sfxKwTough : null);
        public static void PlayKwElusiveStatic() => Play(Instance != null ? Instance.sfxKwElusive : null);
        public static void PlayKwSpellShieldStatic() => Play(Instance != null ? Instance.sfxKwSpellShield : null);
        public static void PlayKwCantBlockAtkStatic() => Play(Instance != null ? Instance.sfxKwCantBlockAttack : null);
        public static void PlayKwCantBlockHitStatic() => Play(Instance != null ? Instance.sfxKwCantBlockHit : null);
        public static void PlayKwEphemeralStatic() => Play(Instance != null ? Instance.sfxKwEphemeral : null);
        public static void PlayKwFearsomeStatic() => Play(Instance != null ? Instance.sfxKwFearsome : null);
        public static void PlayConfirmAttackStatic() => Play(Instance != null ? Instance.sfxConfirmAttack : null);
        public static void PlayCombatClashStatic() => Play(Instance != null ? Instance.sfxCombatClash : null);

        public void PlaySFX(AudioClip clip, float volumeMultiplier = 1f)
        {
            if (clip == null || sfxSource == null) return;
            float vol = masterSfxVolume * Mathf.Clamp01(volumeMultiplier);

            if (sameClipCooldown > 0f && _lastPlayedTime.TryGetValue(clip, out float lastTime))
            {
                float elapsed = Time.time - lastTime;
                if (elapsed < sameClipCooldown)
                    vol *= Mathf.Lerp(AntiStackMinVolume, 1f, Mathf.Clamp01(elapsed / sameClipCooldown));
            }
            _lastPlayedTime[clip] = Time.time;

            if (verboseLog) Debug.Log($"[AudioManager] SFX '{clip.name}' vol={vol:F2}");
            sfxSource.PlayOneShot(clip, vol);
        }

        // ══════════════ CARD / VOICE (mới) ══════════════
        /// <summary>Phát âm thanh của LÁ BÀI trên kênh riêng — không anti-stacking, volume riêng.</summary>
        public static void PlayCard(AudioClip clip, float volumeMultiplier = 1f)
        {
            if (Instance == null) return;
            Instance.PlayCardSfx(clip, volumeMultiplier);
        }

        public void PlayCardSfx(AudioClip clip, float volumeMultiplier = 1f)
        {
            if (clip == null || _cardSource == null) return;
            float vol = Mathf.Clamp01(cardVolume) * Mathf.Clamp01(volumeMultiplier);
            if (verboseLog) Debug.Log($"[AudioManager] CARD '{clip.name}' vol={vol:F2}");
            _cardSource.PlayOneShot(clip, vol);
        }

        // ══════════════ CARD EVENT — COMMON (mọi lá) + VOICE (tùy chọn, trễ) ══════════════
        /// <summary>
        /// ĐIỂM VÀO DUY NHẤT cho âm thanh sự kiện của lá bài:
        ///   1) Phát clip CHUNG (Card Common SFX) cho MỌI lá — luôn có tiếng.
        ///   2) Nếu lá có CardAudioData + clip cho sự kiện này → phát THÊM voice (sau 'voiceDelay' giây),
        ///      KHÔNG thay thế clip chung. Đây chỉ là lời thoại phủ lên, không phải cơ chế.
        /// voice = null (lá thường) → chỉ có common.
        /// </summary>
        public static void PlayCardEvent(CardAudioData.Ev ev, CardAudioData voice = null)
        {
            var inst = Instance;
            if (inst == null) return;
            // AN TOÀN TUYỆT ĐỐI: audio KHÔNG bao giờ được ném exception ra ngoài — nếu ném, nó sẽ
            // làm đứt coroutine resolve spell / luồng commit → kẹt game. Nuốt mọi lỗi, chỉ log.
            try { inst.PlayCardEventImpl(ev, voice); }
            catch (System.Exception e)
            {
                Debug.LogWarning($"[AudioManager] PlayCardEvent lỗi (đã bỏ qua, KHÔNG ảnh hưởng gameplay): {e.Message}");
            }
        }

        void PlayCardEventImpl(CardAudioData.Ev ev, CardAudioData voice)
        {
            var common = CommonCardClip(ev);
            if (common != null) PlayCardSfx(common, commonCardVolume);

            if (voice != null && voice.Has(ev))
            {
                float d = Mathf.Max(0f, voice.voiceDelay);
                // StartCoroutine ném nếu GameObject inactive → chỉ dùng khi chắc chắn active.
                if (d > 0f && isActiveAndEnabled && gameObject.activeInHierarchy)
                    StartCoroutine(PlayVoiceDelayed(ev, voice, d));
                else
                    voice.Play(ev);   // không delay được thì phát ngay (vẫn không phá flow)
            }
        }

        IEnumerator PlayVoiceDelayed(CardAudioData.Ev ev, CardAudioData voice, float delay)
        {
            yield return new WaitForSecondsRealtime(delay);
            if (voice != null) voice.Play(ev);
        }

        AudioClip CommonCardClip(CardAudioData.Ev ev)
        {
            switch (ev)
            {
                case CardAudioData.Ev.Summon: return cardSummon;
                case CardAudioData.Ev.Attack: return cardAttack;
                case CardAudioData.Ev.TakeDamage: return cardTakeDamage;
                case CardAudioData.Ev.Die: return cardDie;
                case CardAudioData.Ev.SpellCast: return cardSpellCast;
                case CardAudioData.Ev.SpellResolve: return cardSpellResolve;
                case CardAudioData.Ev.LevelUp: return cardLevelUp;
                case CardAudioData.Ev.KillEnemy: return cardKillEnemy;
                default: return null;   // Buff/Interact/Idle/Encounter... không có common
            }
        }

        // ══════════════ MUSIC / BGM (mới) ══════════════
        /// <summary>Phát 1 track nhạc nền (dừng playlist). loop = lặp 1 bài. fade &lt; 0 = dùng musicFadeSeconds.</summary>
        public void PlayMusic(AudioClip clip, bool loop = true, float fade = -1f)
        {
            MusicActive = true;
            _playlistActive = false;
            if (_playlistCo != null) { StopCoroutine(_playlistCo); _playlistCo = null; }
            if (fade < 0f) fade = musicFadeSeconds;
            if (_musicFadeCo != null) StopCoroutine(_musicFadeCo);
            _musicFadeCo = StartCoroutine(MusicSwapCo(clip, loop, fade));
        }

        /// <summary>Phát toàn bộ gameplayMusic nối tiếp (playlist). Chuyển bài theo ĐÚNG thời lượng clip.</summary>
        public void PlayGameplayPlaylist()
        {
            SaveResumeOfCurrent();
            _currentSetName = "";
            StartPlaylist(gameplayMusic, "");
        }

        /// <summary>Đổi sang playlist theo NGỮ CẢNH (Menu/Map/Battle/Boss...). Không có set → không làm gì.
        /// Đang phát đúng set đó rồi → bỏ qua (không cắt nhạc đang chạy).</summary>
        public void PlaySet(string setName)
        {
            var set = FindSet(setName);
            if (set == null) { Debug.LogWarning($"[AudioManager] Không có music set '{setName}' (hoặc set rỗng)."); return; }
            if (_playlistActive && string.Equals(_currentSetName, set.name, System.StringComparison.OrdinalIgnoreCase)) return;
            SaveResumeOfCurrent();          // nhớ chỗ đang phát của set CŨ trước khi đổi
            _currentSetName = set.name;
            StartPlaylist(set.tracks, set.name);
        }
        public static void PlaySetStatic(string setName) { if (Instance != null) Instance.PlaySet(setName); }

        // Lưu clip + thời điểm đang phát của set hiện tại → để lần sau quay lại phát tiếp.
        void SaveResumeOfCurrent()
        {
            if (!resumeMusicPosition) return;
            if (string.IsNullOrEmpty(_currentSetName)) return;
            if (_musicSource == null || _musicSource.clip == null) return;
            _resumeBySet[_currentSetName] = (_musicSource.clip, _musicSource.time);
        }

        void StartPlaylist(List<AudioClip> tracks, string setName = "")
        {
            if (tracks == null || tracks.Count == 0) return;
            MusicActive = true;
            _currentTracks = tracks;
            _playlistActive = true;
            _playlistIndex = -1;

            // Resume: set này có vị trí đã lưu + clip còn trong danh sách → phát tiếp từ chỗ cũ (1 lần).
            _resumeStartClip = null; _resumeStartTime = 0f;
            if (resumeMusicPosition && !string.IsNullOrEmpty(setName)
                && _resumeBySet.TryGetValue(setName, out var r) && r.clip != null && tracks.Contains(r.clip))
            {
                _resumeStartClip = r.clip;
                _resumeStartTime = r.clip.length > 0.1f ? Mathf.Clamp(r.time, 0f, r.clip.length - 0.05f) : 0f;
            }

            if (_playlistCo != null) StopCoroutine(_playlistCo);
            _playlistCo = StartCoroutine(PlaylistDriverCo());
        }

        // Driver playlist: KHÔNG poll isPlaying (dễ cắt sớm với clip nén/mất focus) —
        // đợi đúng clip.length bằng thời gian thực (WaitForSecondsRealtime, không bị pause làm lệch).
        IEnumerator PlaylistDriverCo()
        {
            var tracks = _currentTracks;
            if (tracks == null || tracks.Count == 0) yield break;

            // Tiêu thụ yêu cầu resume (chỉ áp cho lần phát ĐẦU TIÊN của lượt này).
            var resumeClip = _resumeStartClip; float resumeTime = _resumeStartTime;
            _resumeStartClip = null; _resumeStartTime = 0f;

            // 1 track → loop native, phát mãi, không cần đợi.
            if (tracks.Count == 1)
            {
                _playlistIndex = 0;
                float st0 = (resumeClip == tracks[0]) ? resumeTime : 0f;
                yield return StartCoroutine(MusicSwapCo(tracks[0], loop: true, musicFadeSeconds, st0));
                yield break;
            }

            bool first = true;
            while (_playlistActive && _currentTracks == tracks)
            {
                AudioClip clip;
                float startTime = 0f;
                if (first && resumeClip != null && tracks.Contains(resumeClip))
                {
                    clip = resumeClip;
                    startTime = resumeTime;
                    _playlistIndex = tracks.IndexOf(resumeClip);
                }
                else
                {
                    if (shuffleMusic)
                    {
                        int next;
                        do { next = Random.Range(0, tracks.Count); }
                        while (tracks.Count > 1 && next == _playlistIndex);
                        _playlistIndex = next;
                    }
                    else _playlistIndex = (_playlistIndex + 1) % tracks.Count;
                    clip = tracks[_playlistIndex];
                }
                first = false;

                if (clip == null) { yield return null; continue; }

                float fade = Mathf.Min(musicFadeSeconds, clip.length * 0.25f);
                yield return StartCoroutine(MusicSwapCo(clip, loop: false, fade, startTime)); // swap + fade-in

                // Đợi phần CÒN LẠI của track (sau điểm bắt đầu); chừa 'fade' giây cuối để crossfade.
                float remaining = clip.length - startTime;
                float wait = remaining - fade - fade;
                if (wait < 0f) wait = Mathf.Max(0f, remaining - fade);
                if (wait > 0f) yield return new WaitForSecondsRealtime(wait);
            }
            _playlistCo = null;
        }

        IEnumerator MusicSwapCo(AudioClip clip, bool loop, float fade, float startTime = 0f)
        {
            if (clip == null || _musicSource == null) yield break;
            // Fade out track hiện tại
            if (_musicSource.isPlaying && fade > 0f)
            {
                float v0 = _musicSource.volume;
                for (float t = 0; t < fade; t += Time.unscaledDeltaTime)
                { _musicSource.volume = Mathf.Lerp(v0, 0f, t / fade); yield return null; }
            }
            _musicSource.Stop();
            _musicSource.clip = clip;
            _musicSource.loop = loop;
            _musicSource.volume = fade > 0f ? 0f : musicVolume;
            _musicSource.Play();
            // Resume: tua tới vị trí đã lưu (sau Play để chắc chắn set được với clip nén).
            if (startTime > 0f && startTime < clip.length) _musicSource.time = startTime;
            // Fade in
            if (fade > 0f)
                for (float t = 0; t < fade; t += Time.unscaledDeltaTime)
                { _musicSource.volume = Mathf.Lerp(0f, musicVolume, t / fade); yield return null; }
            _musicSource.volume = musicVolume;
            _musicFadeCo = null;
        }

        public void StopMusic(float fade = -1f)
        {
            MusicActive = false;
            _playlistActive = false;
            _currentSetName = "";
            if (_playlistCo != null) { StopCoroutine(_playlistCo); _playlistCo = null; }
            if (_musicSource == null) return;
            if (fade < 0f) fade = musicFadeSeconds;
            if (_musicFadeCo != null) StopCoroutine(_musicFadeCo);
            _musicFadeCo = StartCoroutine(FadeOutStopCo(fade));
        }

        IEnumerator FadeOutStopCo(float fade)
        {
            if (fade > 0f && _musicSource.isPlaying)
            {
                float v0 = _musicSource.volume;
                for (float t = 0; t < fade; t += Time.unscaledDeltaTime)
                { _musicSource.volume = Mathf.Lerp(v0, 0f, t / fade); yield return null; }
            }
            _musicSource.Stop();
            _musicFadeCo = null;
        }

        // ══════════════ VOLUME (cho UI slider) ══════════════
        public float MasterSfxVolume { get => masterSfxVolume; set => masterSfxVolume = Mathf.Clamp01(value); }

        public void SetSfxVolume(float v) => masterSfxVolume = Mathf.Clamp01(v);
        public void SetCardVolume(float v) => cardVolume = Mathf.Clamp01(v);
        public void SetMusicVolume(float v)
        {
            musicVolume = Mathf.Clamp01(v);
            if (_musicSource != null && _musicFadeCo == null) _musicSource.volume = musicVolume;
        }
        public static void SetSfxVolumeStatic(float v) { if (Instance != null) Instance.SetSfxVolume(v); }
        public static void SetCardVolumeStatic(float v) { if (Instance != null) Instance.SetCardVolume(v); }
        public static void SetMusicVolumeStatic(float v) { if (Instance != null) Instance.SetMusicVolume(v); }

        // ══════════════ INSTANCE HELPERS (giữ nguyên) ══════════════
        public void PlayConfirmAttack() => PlaySFX(sfxConfirmAttack);
        public void PlayCombatClash() => PlaySFX(sfxCombatClash);
        public void PlayKwBarrier() => PlaySFX(sfxKwBarrier);
        public void PlayKwQuickAttack() => PlaySFX(sfxKwQuickAttack);
        public void PlayKwOverwhelmAttack() => PlaySFX(sfxKwOverwhelmAttack);
        public void PlayKwOverwhelmHit() => PlaySFX(sfxKwOverwhelmHit);
        public void PlayKwLifestealHeal() => PlaySFX(sfxKwLifestealHeal);
        public void PlayKwLifestealPass() => PlaySFX(sfxKwLifestealPass);
        public void PlayKwTough() => PlaySFX(sfxKwTough);
        public void PlayKwElusive() => PlaySFX(sfxKwElusive);
        public void PlayKwSpellShield() => PlaySFX(sfxKwSpellShield);
        public void PlayKwCantBlockAttack() => PlaySFX(sfxKwCantBlockAttack);
        public void PlayKwCantBlockHit() => PlaySFX(sfxKwCantBlockHit);
        public void PlayKwEphemeral() => PlaySFX(sfxKwEphemeral);
        public void PlayKwFearsome() => PlaySFX(sfxKwFearsome);
        public void PlayRoundStart() => PlaySFX(sfxRoundStart);
        public void PlayEndTurn() => PlaySFX(sfxEndTurn);
        public void PlayDrawCard() => PlaySFX(sfxDrawCard);
        public void PlayGlobalLevelUp() => PlaySFX(sfxGlobalLevelUp);
        public void PlaySpellCast() => PlaySFX(sfxSpellCast);

        // ══════════════ LAYERED (giữ nguyên) ══════════════
        public IEnumerator PlayLayered(LayeredClip[] clips)
        {
            if (clips == null) yield break;
            System.Array.Sort(clips, (a, b) => a.delay.CompareTo(b.delay));
            float timer = 0f; int idx = 0;
            while (idx < clips.Length)
            {
                if (timer >= clips[idx].delay) { PlaySFX(clips[idx].clip, clips[idx].volumeMultiplier); idx++; }
                else { yield return null; timer += Time.deltaTime; }
            }
        }

        // ══════════════ DEBUG ══════════════
        [ContextMenu("Test Play SFX (testClip)")]
        void TestPlaySFX()
        {
            if (!Application.isPlaying) { Debug.LogWarning("[AudioManager] Test chỉ chạy khi Play."); return; }
            if (testClip == null) { Debug.LogWarning("[AudioManager] Chưa gán testClip."); return; }
            PlaySFX(testClip, 1f);
        }

#if UNITY_EDITOR
        void OnValidate()
        {
            if (masterSfxVolume <= 0f) Debug.LogWarning("[AudioManager] masterSfxVolume = 0 — SFX bị tắt hết!");
            if (Application.isPlaying && _musicSource != null && _musicFadeCo == null)
                _musicSource.volume = musicVolume;
        }
#endif
    }

    [System.Serializable]
    public struct LayeredClip
    {
        public AudioClip clip;
        public float delay;
        [Range(0f, 1f)] public float volumeMultiplier;
        public LayeredClip(AudioClip c, float d, float v = 1f) { clip = c; delay = d; volumeMultiplier = v; }
    }

    /// <summary>1 PLAYLIST theo ngữ cảnh: tên (Menu/Map/Battle/Boss...) + danh sách bài. Gọi PlaySet(name) để đổi.</summary>
    [System.Serializable]
    public class MusicSet
    {
        public string name = "Battle";
        public List<AudioClip> tracks = new List<AudioClip>();
    }
}